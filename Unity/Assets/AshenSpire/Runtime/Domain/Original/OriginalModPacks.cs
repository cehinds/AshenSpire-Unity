// OriginalModPacks.cs — layers data-only mod packs over the original content.json.
// ENTRY POINT: OriginalModPacks.Load(baseContentJson, files, "Mods"). The Application
// layer calls it once, before constructing sessions, and uses result.Catalog wherever
// it would have used `new OriginalContentCatalog(content)`. Runtime wiring is pending
// (needs the Unity editor); see docs/MODDING.md.
// A pack is <root>/<modId>/mod.json plus *.json files shaped like a partial content.json:
// { "cards": [ full records ], "remove": { "cards": ["id"] } }. A record whose id exists
// replaces it in place; a new id is appended. Order: dependencies first, then
// (loadOrder, id). Each pack is validated by OriginalContentCatalog after it is applied;
// a refused pack leaves the catalog exactly as it was and is reported, never thrown.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalModPacks
    {
        public const string ManifestFile = "mod.json";
        public const string DefaultRoot = "Mods";
        private static readonly Regex IdPattern = new Regex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> ManifestFields = new HashSet<string>(StringComparer.Ordinal) { "id", "name", "version", "gameVersionMin", "loadOrder", "dependsOn", "description", "author" };
        private static readonly JsonLoadSettings Strict = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };

        private sealed class TableSchema { public string Name; public Dictionary<string, HashSet<string>> Required = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal); }
        private sealed class Pack
        {
            public OriginalModManifest Manifest;
            public List<(string Table, JObject Record, string Path)> Records = new List<(string, JObject, string)>();
            public List<(string Table, string Id, string Path)> Removes = new List<(string, string, string)>();
        }

        /// <summary>
        /// Application entry point. Loads every pack under <paramref name="root"/> and returns the merged,
        /// validated catalog plus a structured error list. Never throws for bad mod data.
        /// </summary>
        /// <param name="baseContentJson">The shipped content.json text (Resources/Original/content).</param>
        /// <param name="files">Mods folder access; null or an empty folder means "no mods".</param>
        /// <param name="root">Folder inside <paramref name="files"/> holding one sub-folder per pack.</param>
        /// <param name="gameVersion">Version compared with gameVersionMin; defaults to the base content "version".</param>
        public static OriginalModLoadResult Load(string baseContentJson, IOriginalModFileSource files, string root = DefaultRoot, string gameVersion = null)
        {
            var errors = new List<OriginalModError>(); var rejected = new List<string>();
            OriginalContentCatalog baseCatalog; JObject working;
            try { baseCatalog = new OriginalContentCatalog(baseContentJson); working = JObject.Parse(baseContentJson, Strict); }
            catch (Exception error) when (error is ArgumentException || error is JsonException || error is InvalidCastException || error is NullReferenceException)
            {
                errors.Add(new OriginalModError(OriginalModErrorCodes.BaseContentInvalid, null, "content.json", error.Message));
                return new OriginalModLoadResult { Errors = errors };
            }
            gameVersion = gameVersion ?? (string)working["version"];
            var schemas = Schemas(working);

            // 1. Discover and read each pack; a pack with any error here is refused whole.
            var packs = new Dictionary<string, Pack>(StringComparer.Ordinal);
            foreach (var folder in files == null ? Array.Empty<string>() : files.Directories(root).ToArray())
            {
                var path = Join(root, folder);
                var packErrors = new List<OriginalModError>();
                var manifest = ReadManifest(files, path, folder, gameVersion, packErrors);
                var id = manifest?.Id ?? folder;
                if (manifest != null && packs.ContainsKey(id)) packErrors.Add(new OriginalModError(OriginalModErrorCodes.DuplicateMod, id, path, "Another pack already uses this id."));
                var pack = new Pack { Manifest = manifest };
                if (manifest != null) ReadTables(files, path, id, schemas, pack, packErrors);
                if (packErrors.Count > 0) { errors.AddRange(packErrors); rejected.Add(id); continue; }
                packs.Add(id, pack);
            }

            // 2. Dependencies: prune packs whose dependencies are absent, then order the rest.
            var order = Order(packs, errors, rejected);

            // 3. Apply in order; each pack must leave a catalog the game accepts.
            var loaded = new List<OriginalModManifest>(); var changes = new List<string>(); var catalog = baseCatalog;
            foreach (var pack in order)
            {
                var manifest = pack.Manifest;
                var failedDependency = manifest.DependsOn.FirstOrDefault(d => !loaded.Any(x => x.Id == d));
                if (failedDependency != null) { Reject(errors, rejected, OriginalModErrorCodes.DependencyRejected, manifest.Id, manifest.Folder + "/" + ManifestFile, "Depends on '" + failedDependency + "', which was not loaded."); continue; }
                var draft = (JObject)working.DeepClone(); var packErrors = new List<OriginalModError>(); var packChanges = new List<string>();
                foreach (var remove in pack.Removes)
                {
                    var rows = (JArray)draft.SelectToken(remove.Table);
                    var row = rows.OfType<JObject>().FirstOrDefault(x => (string)x["id"] == remove.Id);
                    if (row == null) { packErrors.Add(new OriginalModError(OriginalModErrorCodes.RemoveUnknown, manifest.Id, remove.Path, "No " + remove.Table + " record '" + remove.Id + "' to remove.")); continue; }
                    row.Remove(); packChanges.Add(manifest.Id + " remove " + remove.Table + "/" + remove.Id);
                }
                foreach (var entry in pack.Records)
                {
                    var rows = (JArray)draft.SelectToken(entry.Table); var id = (string)entry.Record["id"];
                    var existing = rows.OfType<JObject>().FirstOrDefault(x => (string)x["id"] == id);
                    if (existing != null) { existing.Replace(entry.Record.DeepClone()); packChanges.Add(manifest.Id + " override " + entry.Table + "/" + id); }
                    else { rows.Add(entry.Record.DeepClone()); packChanges.Add(manifest.Id + " add " + entry.Table + "/" + id); }
                }
                OriginalContentCatalog candidate = null;
                if (packErrors.Count == 0)
                {
                    try { candidate = new OriginalContentCatalog(draft.ToString(Formatting.None)); }
                    catch (Exception error) when (!(error is OutOfMemoryException))
                    { packErrors.Add(new OriginalModError(OriginalModErrorCodes.ValidationFailed, manifest.Id, manifest.Folder, "Content check refused the merged tables: " + error.Message)); }
                }
                if (packErrors.Count > 0) { errors.AddRange(packErrors); rejected.Add(manifest.Id); continue; }
                working = draft; catalog = candidate; loaded.Add(manifest); changes.AddRange(packChanges);
            }
            return new OriginalModLoadResult
            {
                Catalog = catalog, ContentJson = loaded.Count == 0 ? baseContentJson : working.ToString(Formatting.Indented),
                Loaded = loaded, Rejected = rejected, Errors = errors, Changes = changes
            };
        }

        /// <summary>Compares dotted numeric versions ("0.5.5" &lt; "0.10"). Returns false when either is malformed.</summary>
        public static bool TryCompareVersions(string left, string right, out int comparison)
        {
            comparison = 0;
            if (!TryParseVersion(left, out var a) || !TryParseVersion(right, out var b)) return false;
            for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                var x = i < a.Length ? a[i] : 0; var y = i < b.Length ? b[i] : 0;
                if (x != y) { comparison = x < y ? -1 : 1; return true; }
            }
            return true;
        }
        private static bool TryParseVersion(string text, out int[] parts)
        {
            parts = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var pieces = text.Split('.');
            if (pieces.Length < 1 || pieces.Length > 4) return false;
            parts = new int[pieces.Length];
            for (var i = 0; i < pieces.Length; i++)
                if (pieces[i].Length == 0 || !pieces[i].All(c => c >= '0' && c <= '9') || !int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out parts[i])) return false;
            return true;
        }

        /// <summary>Tables a pack may touch: every array (top level or one level down, e.g. equipment.armaments)
        /// whose rows are objects with unique string ids. Required fields = fields every base row has.</summary>
        private static Dictionary<string, TableSchema> Schemas(JObject content)
        {
            var result = new Dictionary<string, TableSchema>(StringComparer.Ordinal);
            void Consider(string name, JToken value)
            {
                if (!(value is JArray rows)) return;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                if (!rows.All(row => row is JObject o && o["id"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)o["id"]) && ids.Add((string)o["id"]))) return;
                var schema = new TableSchema { Name = name };
                if (rows.Count > 0)
                {
                    var keys = ((JObject)rows[0]).Properties().Select(p => p.Name).ToList();
                    foreach (JObject row in rows) keys = keys.Where(k => row[k] != null).ToList();
                    foreach (var key in keys) schema.Required[key] = new HashSet<string>(rows.Select(r => Kind(r[key])), StringComparer.Ordinal);
                }
                result[name] = schema;
            }
            foreach (var property in content.Properties())
            {
                Consider(property.Name, property.Value);
                if (property.Value is JObject nested) foreach (var inner in nested.Properties()) Consider(property.Name + "." + inner.Name, inner.Value);
            }
            return result;
        }
        private static string Kind(JToken token) => token.Type == JTokenType.Integer || token.Type == JTokenType.Float ? "number" : token.Type.ToString().ToLowerInvariant();

        private static OriginalModManifest ReadManifest(IOriginalModFileSource files, string path, string folder, string gameVersion, List<OriginalModError> errors)
        {
            var file = path + "/" + ManifestFile; var text = files.ReadText(file);
            if (text == null) { errors.Add(new OriginalModError(OriginalModErrorCodes.ManifestMissing, folder, file, "Every pack folder needs a mod.json manifest.")); return null; }
            JObject json;
            try { json = JObject.Parse(text, Strict); }
            catch (JsonException error) { errors.Add(new OriginalModError(OriginalModErrorCodes.JsonInvalid, folder, file, error.Message)); return null; }
            void Bad(string message) => errors.Add(new OriginalModError(OriginalModErrorCodes.ManifestInvalid, folder, file, message));
            var before = errors.Count;
            foreach (var property in json.Properties()) if (!ManifestFields.Contains(property.Name)) Bad("Unknown manifest field '" + property.Name + "'.");
            string Text(string key, bool required)
            {
                var token = json[key];
                if (token == null || token.Type == JTokenType.Null) { if (required) Bad("'" + key + "' is required."); return null; }
                if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token)) { Bad("'" + key + "' must be a non-empty string."); return null; }
                return (string)token;
            }
            var manifest = new OriginalModManifest
            {
                Id = Text("id", true), Name = Text("name", true), Version = Text("version", true), GameVersionMin = Text("gameVersionMin", false),
                Description = Text("description", false), Author = Text("author", false), Folder = path
            };
            if (manifest.Id != null && !IdPattern.IsMatch(manifest.Id)) Bad("'id' must be lowercase letters, digits, '.', '_' or '-' (max 64).");
            else if (manifest.Id != null && manifest.Id != folder) Bad("'id' (" + manifest.Id + ") must match its folder name (" + folder + ").");
            if (manifest.Version != null && !TryParseVersion(manifest.Version, out _)) Bad("'version' must be dotted numbers such as 1.0.0.");
            if (manifest.GameVersionMin != null && !TryParseVersion(manifest.GameVersionMin, out _)) Bad("'gameVersionMin' must be dotted numbers such as 0.5.5.");
            var order = json["loadOrder"];
            if (order != null && order.Type != JTokenType.Integer) Bad("'loadOrder' must be a whole number.");
            else if (order != null) { var value = (long)order; if (value < int.MinValue || value > int.MaxValue) Bad("'loadOrder' is out of range."); else manifest.LoadOrder = (int)value; }
            var depends = json["dependsOn"];
            if (depends != null && !(depends is JArray)) Bad("'dependsOn' must be an array of pack ids.");
            else if (depends is JArray list)
            {
                if (list.Any(x => x.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)x))) Bad("'dependsOn' entries must be pack id strings.");
                else manifest.DependsOn = list.Values<string>().Distinct(StringComparer.Ordinal).ToArray();
            }
            if (errors.Count > before) return null;
            if (manifest.GameVersionMin != null && TryCompareVersions(gameVersion, manifest.GameVersionMin, out var comparison) && comparison < 0)
            {
                errors.Add(new OriginalModError(OriginalModErrorCodes.GameVersionTooOld, manifest.Id, file, "Needs game content " + manifest.GameVersionMin + " or newer; this build has " + gameVersion + "."));
                return null;
            }
            return manifest;
        }

        private static void ReadTables(IOriginalModFileSource files, string path, string modId, Dictionary<string, TableSchema> schemas, Pack pack, List<OriginalModError> errors)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal); var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in files.Files(path).Where(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && x != ManifestFile).OrderBy(x => x, StringComparer.Ordinal))
            {
                var file = path + "/" + name; JObject json;
                try { json = JObject.Parse(files.ReadText(file) ?? "", Strict); }
                catch (JsonException error) { errors.Add(new OriginalModError(OriginalModErrorCodes.JsonInvalid, modId, file, error.Message)); continue; }
                foreach (var property in json.Properties())
                {
                    if (property.Name == "remove")
                    {
                        if (!(property.Value is JObject removeTables)) { errors.Add(new OriginalModError(OriginalModErrorCodes.RecordInvalid, modId, file, "'remove' must be an object of table → [ids].")); continue; }
                        foreach (var table in removeTables.Properties())
                        {
                            if (!schemas.ContainsKey(table.Name)) { errors.Add(UnknownTable(modId, file, table.Name, schemas)); continue; }
                            if (!(table.Value is JArray ids) || ids.Any(x => x.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)x))) { errors.Add(new OriginalModError(OriginalModErrorCodes.RecordInvalid, modId, file + "#remove." + table.Name, "Expected an array of id strings.")); continue; }
                            foreach (var id in ids.Values<string>())
                            {
                                if (!removed.Add(table.Name + "/" + id)) { errors.Add(new OriginalModError(OriginalModErrorCodes.DuplicateRecord, modId, file + "#remove." + table.Name, "'" + id + "' is removed twice.")); continue; }
                                pack.Removes.Add((table.Name, id, file + "#remove." + table.Name));
                            }
                        }
                        continue;
                    }
                    if (!schemas.TryGetValue(property.Name, out var schema)) { errors.Add(UnknownTable(modId, file, property.Name, schemas)); continue; }
                    if (!(property.Value is JArray rows)) { errors.Add(new OriginalModError(OriginalModErrorCodes.RecordInvalid, modId, file + "#" + property.Name, "A table must be an array of records.")); continue; }
                    for (var i = 0; i < rows.Count; i++)
                    {
                        var where = file + "#" + property.Name + "[" + i + "]";
                        if (!(rows[i] is JObject record) || record["id"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)record["id"]))
                        { errors.Add(new OriginalModError(OriginalModErrorCodes.RecordInvalid, modId, where, "Each record must be an object with a string 'id'.")); continue; }
                        var id = (string)record["id"];
                        if (!seen.Add(property.Name + "/" + id)) { errors.Add(new OriginalModError(OriginalModErrorCodes.DuplicateRecord, modId, where, "'" + id + "' appears twice in this pack.")); continue; }
                        var problems = schema.Required.Where(field => record[field.Key] == null || !field.Value.Contains(Kind(record[field.Key])))
                            .Select(field => record[field.Key] == null ? "missing '" + field.Key + "'" : "'" + field.Key + "' should be " + string.Join(" or ", field.Value.OrderBy(x => x, StringComparer.Ordinal))).ToArray();
                        if (problems.Length > 0) { errors.Add(new OriginalModError(OriginalModErrorCodes.SchemaMismatch, modId, where, property.Name + "/" + id + ": " + string.Join("; ", problems) + ".")); continue; }
                        pack.Records.Add((property.Name, (JObject)record.DeepClone(), where));
                    }
                }
            }
            foreach (var record in pack.Records) if (removed.Contains(record.Table + "/" + (string)record.Record["id"]))
                errors.Add(new OriginalModError(OriginalModErrorCodes.DuplicateRecord, modId, record.Path, "'" + (string)record.Record["id"] + "' is both removed and supplied; supply it alone to replace it."));
        }
        private static OriginalModError UnknownTable(string modId, string file, string table, Dictionary<string, TableSchema> schemas) =>
            new OriginalModError(OriginalModErrorCodes.UnknownTable, modId, file, "'" + table + "' is not a moddable table. Use one of: " + string.Join(", ", schemas.Keys.OrderBy(x => x, StringComparer.Ordinal)) + ".");

        /// <summary>Dependencies first; otherwise lowest (loadOrder, id). Packs in or behind a cycle are refused.</summary>
        private static List<Pack> Order(Dictionary<string, Pack> packs, List<OriginalModError> errors, List<string> rejected)
        {
            var live = new Dictionary<string, Pack>(packs, StringComparer.Ordinal);
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var pack in live.Values.OrderBy(x => x.Manifest.Id, StringComparer.Ordinal).ToArray())
                {
                    var missing = pack.Manifest.DependsOn.FirstOrDefault(d => !live.ContainsKey(d));
                    if (missing == null) continue;
                    var code = packs.ContainsKey(missing) || rejected.Contains(missing) ? OriginalModErrorCodes.DependencyRejected : OriginalModErrorCodes.MissingDependency;
                    Reject(errors, rejected, code, pack.Manifest.Id, pack.Manifest.Folder + "/" + ManifestFile, code == OriginalModErrorCodes.MissingDependency ? "Needs pack '" + missing + "', which is not installed." : "Depends on '" + missing + "', which was refused.");
                    live.Remove(pack.Manifest.Id); changed = true;
                }
            }
            var result = new List<Pack>(); var done = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                var next = live.Values.Where(p => !done.Contains(p.Manifest.Id) && p.Manifest.DependsOn.All(done.Contains))
                    .OrderBy(p => p.Manifest.LoadOrder).ThenBy(p => p.Manifest.Id, StringComparer.Ordinal).FirstOrDefault();
                if (next == null) break;
                done.Add(next.Manifest.Id); result.Add(next);
            }
            var stuck = live.Values.Where(p => !done.Contains(p.Manifest.Id)).OrderBy(p => p.Manifest.Id, StringComparer.Ordinal).ToArray();
            foreach (var pack in stuck)
            {
                var cycle = Cycle(pack.Manifest.Id, live, done);
                if (cycle != null) Reject(errors, rejected, OriginalModErrorCodes.DependencyCycle, pack.Manifest.Id, pack.Manifest.Folder + "/" + ManifestFile, "Dependency cycle: " + string.Join(" -> ", cycle) + ".");
                else Reject(errors, rejected, OriginalModErrorCodes.DependencyRejected, pack.Manifest.Id, pack.Manifest.Folder + "/" + ManifestFile, "Depends on a pack caught in a dependency cycle.");
            }
            return result;
        }
        /// <summary>Returns the cycle through <paramref name="start"/> (start … start), or null if start only leads into one.</summary>
        private static List<string> Cycle(string start, Dictionary<string, Pack> live, HashSet<string> done)
        {
            var path = new List<string> { start }; var visited = new HashSet<string>(StringComparer.Ordinal);
            bool Walk(string id)
            {
                foreach (var next in live[id].Manifest.DependsOn.Where(d => !done.Contains(d)).OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (next == start) { path.Add(next); return true; }
                    if (!visited.Add(next)) continue;
                    path.Add(next); if (Walk(next)) return true; path.RemoveAt(path.Count - 1);
                }
                return false;
            }
            return Walk(start) ? path : null;
        }
        private static void Reject(List<OriginalModError> errors, List<string> rejected, string code, string modId, string path, string message)
        { errors.Add(new OriginalModError(code, modId, path, message)); if (!rejected.Contains(modId)) rejected.Add(modId); }
        private static string Join(string root, string child) { var r = OriginalModMemorySource.Normalize(root); return r.Length == 0 ? child : r + "/" + child; }
    }
}
