// OriginalWebProfileImport.cs — explicit, read-only merge of an original-game PROFILE
// (localStorage sote_meta_v1, its exportProfile() file, or a 'meta' archive) into the
// AshenedSpire profile. Never writes storage and never mutates its inputs: the caller
// previews the result, then persists Profile (and, only if the player opts in, Settings).
// MERGE RULES (docs/Unity-Original-Save-Import.md):
// - Original schema 0/1/2 accepted (0/1 migrated as src/engine/save.js does); newer or
//   malformed versions are refused by name. Existing Unity data is never removed.
// - Results carry no run ID or timestamp in the original, so each is keyed by the SHA-256
//   of its exact stored row plus its occurrence number. Keys join completedRunIds, which
//   outlive the 20-result history, so re-importing the same or a later export never adds
//   a run twice. Imported results are treated as older than native ones and leave first.
// - The original progress tally (it outlives its own 20 results) is added as a delta over
//   the tally already imported, recorded in the profile's originalWebImport receipt.
// - Unknown unlock/armament IDs and original-only fields are preserved in that receipt.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalWebProfileImportResult
    {
        /// <summary>The merged profile. The existing profile passed in is not modified.</summary>
        public OriginalProfile Profile { get; internal set; }
        /// <summary>A copy of the existing settings with every mapped original setting applied.
        /// Persist it only when the player chooses to import settings.</summary>
        public OriginalPlayerSettings Settings { get; internal set; }
        public int SourceSchemaVersion { get; internal set; }
        public bool Migrated { get; internal set; }
        /// <summary>False when the profile already holds everything this export contains.</summary>
        public bool Changed { get; internal set; }
        public int SourceResults { get; internal set; }
        public int ImportedResults { get; internal set; }
        public int DuplicateResults { get; internal set; }
        public int SkippedResults { get; internal set; }
        /// <summary>Imported results beyond the 20-result history; their keys are still recorded.</summary>
        public int ArchivedOnlyResults { get; internal set; }
        public int AddedRuns { get; internal set; }
        public int AddedWins { get; internal set; }
        public IReadOnlyList<string> AddedUnlocks { get; internal set; } = Array.Empty<string>();
        /// <summary>Unlocks the AshenedSpire rules grant from the merged progress tally.</summary>
        public IReadOnlyList<string> EarnedUnlocks { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> UnknownUnlocks { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> AddedArmaments { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> UnknownArmaments { get; internal set; } = Array.Empty<string>();
        /// <summary>"key → Unity field" for every original setting with an equivalent.</summary>
        public IReadOnlyList<string> MappedSettings { get; internal set; } = Array.Empty<string>();
        /// <summary>"key: reason" for every original setting without an equivalent (kept in the receipt).</summary>
        public IReadOnlyList<string> UnmappedSettings { get; internal set; } = Array.Empty<string>();
        /// <summary>Top-level original profile fields with no AshenedSpire home (kept in the receipt).</summary>
        public IReadOnlyList<string> UnmappedFields { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> Notes { get; internal set; } = Array.Empty<string>();
        public string Summary()
        {
            var lines = new List<string>
            {
                "Original profile schema " + SourceSchemaVersion + (Migrated ? " (migrated)" : ""),
                ImportedResults + " of " + SourceResults + " run results new · " + DuplicateResults + " already imported" + (SkippedResults > 0 ? " · " + SkippedResults + " unreadable skipped" : ""),
                "Progress +" + AddedRuns + " runs, +" + AddedWins + " wins",
                "Unlocks: " + (AddedUnlocks.Count + EarnedUnlocks.Count) + " new" + (UnknownUnlocks.Count > 0 ? " · " + UnknownUnlocks.Count + " unknown kept aside" : ""),
                "Armaments found: " + AddedArmaments.Count + " new" + (UnknownArmaments.Count > 0 ? " · " + UnknownArmaments.Count + " unknown kept aside" : ""),
                "Settings with an AshenedSpire equivalent: " + MappedSettings.Count + " · without: " + UnmappedSettings.Count,
            };
            if (ArchivedOnlyResults > 0) lines.Add(ArchivedOnlyResults + " older results exceed the 20-result history; they count toward progress but are not listed.");
            if (!Changed) lines.Add("Everything in this profile has already been imported.");
            return string.Join("\n", lines);
        }
    }

    public static class OriginalWebProfileImport
    {
        /// <summary>src/engine/save.js META_SCHEMA_VERSION this importer understands.</summary>
        public const int SupportedSchemaVersion = 2;
        public const int ReceiptVersion = 1;
        public const string ReceiptKey = "originalWebImport";
        public const string ResultKeyPrefix = "web-profile-";
        private const int ImportLogLimit = 16;
        private static readonly string[] KnownFields = { "schemaVersion", "settings", "results", "progress", "unlocked", "found", "discoveredArmaments", "discoveryReceipts" };

        /// <summary>True when the text is an original profile in any accepted wrapper (for routing the import screen).</summary>
        public static bool IsProfile(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > OriginalWebSaveImport.MaximumBytes) return false;
                var source = OriginalWebSaveImport.ParseStrict(text);
                if (source["archive"] is JObject archive) return (string)archive["kind"] == "meta";
                if (source["profile"] != null) return true;
                return !LooksLikeRun(source) && new[] { "results", "progress", "unlocked", "found", "discoveredArmaments", "settings" }.Any(k => source[k] != null);
            }
            catch (Exception error) when (error is ArgumentException || error is Newtonsoft.Json.JsonException) { return false; }
        }
        private static bool LooksLikeRun(JObject source) => source["deck"] != null || source["mapGraph"] != null || source["class"] != null;

        private static JObject Unwrap(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > OriginalWebSaveImport.MaximumBytes)
                throw new ArgumentException("Choose an original profile JSON file no larger than 1 MB.");
            var source = OriginalWebSaveImport.ParseStrict(text);
            if (source["archive"] is JObject archive)
            {
                if ((string)archive["kind"] != "meta" || archive["save"]?.Type != JTokenType.String)
                    throw new ArgumentException("This is a run archive, not a profile. Import it as a run save.");
                return OriginalWebSaveImport.ParseStrict((string)archive["save"]);
            }
            if (source["profile"] != null)
            {
                if (source["profile"].Type != JTokenType.String) throw new ArgumentException("This profile export is damaged: its profile is not stored text.");
                return OriginalWebSaveImport.ParseStrict((string)source["profile"]);
            }
            if (LooksLikeRun(source)) throw new ArgumentException("This is a run save, not a profile. Import it as a run save.");
            return source;
        }

        private static int Counter(JToken token, string name, int fallback)
        {
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type == JTokenType.Integer && (long)token >= 0 && (long)token <= int.MaxValue) return (int)token;
            if (token.Type == JTokenType.Float && (double)token >= 0 && (double)token <= int.MaxValue && Math.Floor((double)token) == (double)token) return (int)(double)token;
            throw new ArgumentException("The original profile's " + name + " is not a whole number.");
        }
        private static string[] Ids(JObject meta, string name)
        {
            var token = meta[name];
            if (token == null || token.Type == JTokenType.Null) return Array.Empty<string>();
            if (!(token is JArray rows) || rows.Any(x => x.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)x)))
                throw new ArgumentException("The original profile's " + name + " list is damaged. Nothing was imported.");
            return rows.Values<string>().Distinct().ToArray();
        }
        private static bool AddOnce(JArray rows, string value)
        {
            if (string.IsNullOrEmpty(value) || rows.Values<string>().Contains(value)) return false;
            rows.Add(value); return true;
        }

        /// <summary>
        /// Converts an original profile and merges it into <paramref name="existing"/> (null: a fresh profile).
        /// Throws ArgumentException with a player-facing reason when the profile cannot be imported faithfully.
        /// </summary>
        public static OriginalWebProfileImportResult Merge(string text, OriginalContentCatalog catalog, OriginalProfile existing, OriginalPlayerSettings existingSettings = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var meta = Unwrap(text);
            var result = new OriginalWebProfileImportResult(); var notes = new List<string>();

            // ---- schema (src/engine/save.js readMetaFrom / migrateMeta) ----------------------
            var version = meta["schemaVersion"];
            int schema;
            if (version == null || version.Type == JTokenType.Null) schema = 0;
            else if (version.Type != JTokenType.Integer || (long)version < 0 || (long)version > int.MaxValue)
                throw new ArgumentException("This original profile has an unreadable schema version (" + version.ToString(Newtonsoft.Json.Formatting.None) + "). Nothing was imported; your original profile is unchanged.");
            else schema = (int)version;
            if (schema > SupportedSchemaVersion)
                throw new ArgumentException("This original profile uses schema version " + schema + ", newer than this importer supports (" + SupportedSchemaVersion + "). Nothing was imported; your original profile and AshenedSpire progress are unchanged.");
            result.SourceSchemaVersion = schema; result.Migrated = schema < SupportedSchemaVersion;
            foreach (var name in new[] { "settings", "progress" })
                if (meta[name] != null && meta[name].Type != JTokenType.Null && !(meta[name] is JObject)) throw new ArgumentException("The original profile's " + name + " is damaged. Nothing was imported.");
            if (meta["results"] != null && meta["results"].Type != JTokenType.Null && !(meta["results"] is JArray)) throw new ArgumentException("The original profile's run history is damaged. Nothing was imported.");
            var found = Ids(meta, "found"); var unlocked = Ids(meta, "unlocked");
            // Migration 0/1 → 2: the kit ledger starts from the wardrobe when it is absent.
            var discovered = meta["discoveredArmaments"] == null || meta["discoveredArmaments"].Type == JTokenType.Null ? (schema < 2 ? found : Array.Empty<string>()) : Ids(meta, "discoveredArmaments");
            var sourceReceipts = meta["discoveryReceipts"] as JArray ?? new JArray();
            if (meta["discoveryReceipts"] != null && meta["discoveryReceipts"].Type != JTokenType.Null && !(meta["discoveryReceipts"] is JArray)) throw new ArgumentException("The original profile's discovery receipts are damaged. Nothing was imported.");

            var baseProfile = existing ?? new OriginalProfile(catalog);
            var before = baseProfile.Snapshot(); var draft = (JObject)before.DeepClone();
            var receipt = draft[ReceiptKey] as JObject;
            if (draft[ReceiptKey] != null && (receipt == null || (int?)receipt["version"] != ReceiptVersion))
                throw new ArgumentException("This AshenedSpire profile holds an import record from a newer build. Nothing was imported.");
            var emptyReceipt = new JObject { ["version"] = ReceiptVersion, ["progress"] = new JObject { ["runs"] = 0, ["wins"] = 0 }, ["imports"] = new JArray(), ["preserved"] = new JObject() };
            if (receipt == null) { receipt = (JObject)emptyReceipt.DeepClone(); draft[ReceiptKey] = receipt; }
            var preserved = (JObject)receipt["preserved"];

            // ---- results ------------------------------------------------------------------
            var classes = catalog.Table("classes").OfType<JObject>().ToArray();
            var rows = meta["results"] as JArray ?? new JArray(); result.SourceResults = rows.Count;
            var completed = new HashSet<string>(((JArray)draft["completedRunIds"]).Values<string>(), StringComparer.Ordinal);
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var fresh = new List<(string Key, JObject Row)>(); var valid = new List<JObject>();
            foreach (var token in rows)
            {
                if (!(token is JObject row) || row["victory"]?.Type != JTokenType.Boolean) { result.SkippedResults++; continue; }
                JObject normalized;
                try { normalized = NormalizeResult(row, classes); }
                catch (ArgumentException) { result.SkippedResults++; continue; }
                valid.Add(normalized);
                var hash = OriginalWebSaveImport.Hash(row);
                occurrences[hash] = occurrences.TryGetValue(hash, out var n) ? n + 1 : 1;
                var key = ResultKeyPrefix + hash.Substring(0, 32) + "-" + occurrences[hash];
                if (completed.Contains(key)) { result.DuplicateResults++; continue; }
                completed.Add(key); fresh.Add((key, normalized));
            }
            if (result.SkippedResults > 0) notes.Add(result.SkippedResults + " unreadable original results were skipped.");
            var results = (JArray)draft["results"]; var ids = (JArray)draft["completedRunIds"];
            // ResultArchive aligns the newest IDs with the retained results (ID index = result
            // index + offset). Imported runs are older than native ones but newer than earlier
            // imports: insert the block after the last imported result, before native results.
            var offset = ids.Count - results.Count; var resultAt = Math.Max(0, -offset);
            for (var i = results.Count - 1; i >= resultAt; i--) if ((string)results[i]["origin"] == "original-web") { resultAt = i + 1; break; }
            var idAt = resultAt + offset;
            for (var i = 0; i < fresh.Count; i++) { results.Insert(resultAt + i, fresh[i].Row); ids.Insert(idAt + i, fresh[i].Key); }
            result.ImportedResults = fresh.Count;
            while (results.Count > OriginalProfile.ResultArchiveLimit) results[0].Remove();
            result.ArchivedOnlyResults = fresh.Count(f => !results.Any(r => ReferenceEquals(r, f.Row)));

            // ---- progress (delta over what was already imported) -----------------------------
            JObject source;
            if (meta["progress"] is JObject stored) source = stored;
            else
            {
                source = new JObject { ["runs"] = valid.Count, ["wins"] = valid.Count(r => (bool)r["victory"]), ["maxAct"] = valid.Select(r => (int)r["act"]).DefaultIfEmpty(1).Max(),
                    ["bosses"] = new JArray(valid.SelectMany(r => r["bosses"].Values<string>()).Distinct().ToArray()),
                    ["wonClasses"] = new JArray(valid.Where(r => (bool)r["victory"] && r["class"]?.Type == JTokenType.String).Select(r => (string)r["class"]).Distinct().ToArray()) };
                if (valid.Count > 0) notes.Add("This original profile predates its progress tally; progress was rebuilt from its run history.");
            }
            var runs = Counter(source["runs"], "progress runs", 0); var wins = Counter(source["wins"], "progress wins", 0); var maxAct = Counter(source["maxAct"], "progress act", 1);
            if (wins > runs) throw new ArgumentException("The original profile records more wins than runs. Nothing was imported.");
            var bosses = ProgressIds(source, "bosses"); var wonClasses = ProgressIds(source, "wonClasses");
            var imported = (JObject)receipt["progress"]; var p = (JObject)draft["progress"];
            var deltaRuns = Math.Max(0, runs - Counter(imported["runs"], "imported runs", 0)); var deltaWins = Math.Max(0, wins - Counter(imported["wins"], "imported wins", 0));
            var totalRuns = checked((int)p["runs"] + deltaRuns); var totalWins = Math.Min(totalRuns, checked((int)p["wins"] + deltaWins));
            result.AddedRuns = deltaRuns; result.AddedWins = totalWins - (int)p["wins"];
            p["runs"] = totalRuns; p["wins"] = totalWins; p["maxAct"] = Math.Max((int)p["maxAct"], maxAct);
            foreach (var id in bosses) AddOnce((JArray)p["bosses"], id);
            foreach (var id in wonClasses) AddOnce((JArray)p["wonClasses"], id);
            imported["runs"] = Math.Max(runs, Counter(imported["runs"], "imported runs", 0)); imported["wins"] = Math.Max(wins, Counter(imported["wins"], "imported wins", 0));

            // ---- unlocks --------------------------------------------------------------------
            var unlockIds = new HashSet<string>(catalog.Table("unlocks").Select(u => (string)u["id"]), StringComparer.Ordinal);
            var added = new List<string>(); var unknown = new List<string>();
            foreach (var id in unlocked) { if (!unlockIds.Contains(id)) unknown.Add(id); else if (AddOnce((JArray)draft["unlocked"], id)) added.Add(id); }
            result.AddedUnlocks = added; result.UnknownUnlocks = unknown;
            if (unknown.Count > 0) Preserve(preserved, "unknownUnlocks", unknown);
            var earned = baseProfile.EvaluateUnlocks(draft).Values<string>().ToArray();
            foreach (var id in earned) AddOnce((JArray)draft["unlocked"], id);
            result.EarnedUnlocks = earned;

            // ---- armaments: wardrobe, kit ledger and discovery receipts ----------------------
            var armamentIds = new HashSet<string>(catalog.Table("equipment.armaments").Select(a => (string)a["id"]), StringComparer.Ordinal);
            var addedArmaments = new List<string>(); var unknownArmaments = found.Concat(discovered).Where(id => !armamentIds.Contains(id)).Distinct().ToList();
            foreach (var id in found.Where(armamentIds.Contains)) if (AddOnce((JArray)draft["found"], id)) addedArmaments.Add(id);
            foreach (var id in discovered.Where(armamentIds.Contains)) AddOnce((JArray)draft["discoveredArmaments"], id);
            result.AddedArmaments = addedArmaments; result.UnknownArmaments = unknownArmaments;
            if (unknownArmaments.Count > 0) Preserve(preserved, "unknownArmaments", unknownArmaments);
            var receipts = (JArray)draft["discoveryReceipts"];
            var held = new HashSet<string>(receipts.Select(r => (string)r["pieceId"]).Where(x => x != null), StringComparer.Ordinal);
            var older = sourceReceipts.OfType<JObject>().Where(r => (string)r["kind"] == "armamentDiscovery" && armamentIds.Contains((string)r["pieceId"]) && held.Add((string)r["pieceId"])).ToArray();
            // Like results: imported receipts sit after earlier imports and before native ones.
            var receiptAt = 0; for (var i = receipts.Count - 1; i >= 0; i--) if ((string)receipts[i]["origin"] == "original-web") { receiptAt = i + 1; break; }
            for (var i = 0; i < older.Length; i++) { var copy = (JObject)older[i].DeepClone(); copy["origin"] = "original-web"; receipts.Insert(receiptAt + i, copy); }
            var limit = 64;
            try { var configured = catalog.Data()["balance"]?["equipment"]?["startingKitDiscovery"]?["receiptLimit"]; if (configured?.Type == JTokenType.Integer && (int)configured > 0) limit = (int)configured; } catch (ArgumentException) { }
            while (receipts.Count > limit) receipts[0].Remove();

            // ---- settings and original-only fields -------------------------------------------
            var settings = (existingSettings ?? new OriginalPlayerSettings()).Clone();
            var originalSettings = meta["settings"] as JObject ?? new JObject();
            MapSettings(originalSettings, settings, catalog, out var mapped, out var unmapped);
            result.Settings = settings; result.MappedSettings = mapped; result.UnmappedSettings = unmapped;
            if (originalSettings.Count > 0) preserved["settings"] = originalSettings.DeepClone();
            var extra = meta.Properties().Where(x => !KnownFields.Contains(x.Name)).ToArray();
            result.UnmappedFields = extra.Select(x => x.Name).ToArray();
            if (extra.Length > 0) { var fields = preserved["fields"] as JObject ?? new JObject(); foreach (var x in extra) fields[x.Name] = x.Value.DeepClone(); preserved["fields"] = fields; }

            // An empty profile export adds nothing, not even an empty import record.
            if (before[ReceiptKey] == null && JToken.DeepEquals(receipt, emptyReceipt)) draft.Remove(ReceiptKey);
            result.Changed = !JToken.DeepEquals(draft, before);
            if (result.Changed)
            {
                var log = (JArray)receipt["imports"];
                log.Add(new JObject { ["sha256"] = OriginalWebSaveImport.Hash(meta), ["schemaVersion"] = schema, ["results"] = fresh.Count, ["duplicates"] = result.DuplicateResults, ["runs"] = deltaRuns });
                while (log.Count > ImportLogLimit) log[0].Remove();
            }
            else draft = before;
            result.Profile = OriginalProfile.Restore(catalog, draft);
            result.Notes = notes;
            return result;
        }

        private static string[] ProgressIds(JObject progress, string name)
        {
            var token = progress[name];
            if (token == null || token.Type == JTokenType.Null) return Array.Empty<string>();
            if (!(token is JArray rows) || rows.Any(x => x.Type != JTokenType.String)) throw new ArgumentException("The original profile's progress " + name + " is damaged. Nothing was imported.");
            return rows.Values<string>().Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
        }
        private static void Preserve(JObject preserved, string name, IEnumerable<string> ids)
        {
            var rows = preserved[name] as JArray ?? new JArray(); foreach (var id in ids) AddOnce(rows, id); preserved[name] = rows;
        }

        // main.js runResult(victory) → the native result row shape OriginalProfile.Finish writes.
        private static JObject NormalizeResult(JObject row, JObject[] classes)
        {
            var classId = row["class"]?.Type == JTokenType.String ? (string)row["class"] : null;
            var className = row["className"]?.Type == JTokenType.String ? (string)row["className"] : null;
            var hero = classes.FirstOrDefault(c => classId != null ? (string)c["id"] == classId : className != null && (string)c["name"] == className);
            if (classId == null && hero != null) classId = (string)hero["id"];
            var seed = row["seed"]; if (seed != null && seed.Type != JTokenType.String && seed.Type != JTokenType.Integer) seed = null;
            var bosses = row["bosses"] as JArray ?? new JArray();
            var normalized = new JObject
            {
                ["victory"] = (bool)row["victory"], ["seed"] = seed?.DeepClone() ?? JValue.CreateNull(),
                ["class"] = classId == null ? JValue.CreateNull() : new JValue(classId), ["className"] = className ?? (string)hero?["name"] ?? classId,
                ["act"] = Math.Max(1, Counter(row["act"], "result act", 1)), ["floor"] = Counter(row["floor"], "result floor", 0), ["fightsWon"] = Counter(row["fightsWon"], "result fights", 0),
                ["damageDealt"] = Counter(row["damageDealt"], "result damage dealt", 0), ["damageTaken"] = Counter(row["damageTaken"], "result damage taken", 0),
                ["custom"] = row["custom"]?.Type == JTokenType.Boolean && (bool)row["custom"], ["ascension"] = Counter(row["ascension"], "result ascension", 0),
                ["bosses"] = new JArray(bosses.Where(b => b.Type == JTokenType.String).Select(b => (string)b).ToArray()),
                ["origin"] = "original-web",
            };
            if (row["name"]?.Type == JTokenType.String) normalized["name"] = (string)row["name"];
            return normalized;
        }

        // src/ui/screens/settings.js rows → OriginalPlayerSettings. Only explicitly stored values
        // map (the original keeps settings sparse); defaults are never imported as choices.
        private static void MapSettings(JObject original, OriginalPlayerSettings target, OriginalContentCatalog catalog, out List<string> mapped, out List<string> unmapped)
        {
            mapped = new List<string>(); unmapped = new List<string>();
            JToken ui = null; try { ui = catalog.Data()["balance"]?["ui"]; } catch (ArgumentException) { }
            double Named(string table, string key, double fallback)
            {
                var named = ui?[table]?["named"]?[key.ToLowerInvariant()];
                return named != null && (named.Type == JTokenType.Float || named.Type == JTokenType.Integer) ? (double)named : fallback;
            }
            double? TextRatio(string key)
            {
                // Root font-size percentages; M (62.5%) is the Unity 1.0 baseline.
                double Percent(JToken t) => t?.Type == JTokenType.String && double.TryParse(((string)t).TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
                var at = Percent(ui?["textSize"]?[key]); var baseline = Percent(ui?["textSize"]?["M"]);
                if (double.IsNaN(at) || double.IsNaN(baseline) || baseline <= 0) return null;
                return Math.Round(at / baseline, 4);
            }
            double Clamp(double v, double min, double max) => Math.Min(max, Math.Max(min, v));
            var textHandled = false;
            foreach (var property in original.Properties())
            {
                var key = property.Name; var value = property.Value;
                bool IsBool() => value.Type == JTokenType.Boolean;
                bool IsNumber() => value.Type == JTokenType.Integer || value.Type == JTokenType.Float;
                var text = value.Type == JTokenType.String ? ((string)value).Trim() : null;
                switch (key)
                {
                    case "reducedMotion": if (IsBool()) { target.ReducedMotion = (bool)value; mapped.Add("reducedMotion → ReducedMotion"); } else unmapped.Add(key + ": value not recognised"); break;
                    case "screenShake": if (IsBool()) { target.ScreenShake = (bool)value; mapped.Add("screenShake → ScreenShake"); } else unmapped.Add(key + ": value not recognised"); break;
                    case "muteAudio": if (IsBool()) { target.Muted = (bool)value; mapped.Add("muteAudio → Muted"); } else unmapped.Add(key + ": value not recognised"); break;
                    case "musicVolume": if (IsNumber()) { target.MusicVolume = Clamp((double)value / 100, OriginalPlayerSettings.VolumeMin, OriginalPlayerSettings.VolumeMax); mapped.Add("musicVolume → MusicVolume (÷100)"); } else unmapped.Add(key + ": value not recognised"); break;
                    case "sfxVolume": if (IsNumber()) { target.SfxVolume = Clamp((double)value / 100, OriginalPlayerSettings.VolumeMin, OriginalPlayerSettings.VolumeMax); mapped.Add("sfxVolume → SfxVolume (÷100)"); } else unmapped.Add(key + ": value not recognised"); break;
                    case "animSpeed":
                        // src/ui/fx.js ANIM_SPEEDS beat lengths: slow 700ms, normal 400ms, fast 180ms (legacy fast = 2×).
                        if (text == "instant") { target.InstantAnimations = true; mapped.Add("animSpeed → InstantAnimations"); }
                        else if (text == "slow" || text == "normal" || text == "fast")
                        {
                            target.InstantAnimations = false;
                            target.AnimationSpeed = text == "slow" ? Clamp(400d / 700d, OriginalPlayerSettings.AnimationSpeedMin, OriginalPlayerSettings.AnimationSpeedMax) : text == "fast" ? OriginalPlayerSettings.LegacyFastAnimationSpeed : 1;
                            mapped.Add("animSpeed → AnimationSpeed");
                        }
                        else unmapped.Add(key + ": value not recognised");
                        break;
                    case "uiScale":
                        if (text != null && new[] { "S", "M", "L", "XL" }.Contains(text.ToUpperInvariant()))
                        { target.UiScale = Clamp(Named("uiScale", text, 1), OriginalPlayerSettings.UiScaleMin, OriginalPlayerSettings.UiScaleMax); mapped.Add("uiScale → UiScale"); }
                        else unmapped.Add(key + ": '" + value + "' follows the browser window; AshenedSpire keeps its own size");
                        break;
                    case "textSize":
                        if (text != null && new[] { "S", "M", "L", "XL" }.Contains(text.ToUpperInvariant()) && TextRatio(text.ToUpperInvariant()) is double ratio)
                        { target.TextScale = Clamp(ratio, OriginalPlayerSettings.TextScaleMin, OriginalPlayerSettings.TextScaleMax); mapped.Add("textSize → TextScale"); textHandled = true; }
                        else unmapped.Add(key + ": '" + value + "' follows the browser text size; AshenedSpire keeps its own");
                        break;
                    case "largeText": break; // handled after the loop: legacy L when textSize is not explicit
                    case "colorblindSafe": unmapped.Add(key + ": the original shifts one shared palette; AshenedSpire palettes are chosen per colour-vision type"); break;
                    case "musicEnabled": if (IsBool()) { target.MusicEnabled = (bool)value; mapped.Add("musicEnabled → MusicEnabled"); } else unmapped.Add(key + ": value not recognised"); break;
                    case "highContrast": if (IsBool()) { target.HighContrast = (bool)value; mapped.Add("highContrast → HighContrast"); } else unmapped.Add(key + ": value not recognised"); break;
                    case "reduceFlashes": if (IsBool()) { target.ReduceFlashes = (bool)value; mapped.Add("reduceFlashes → ReduceFlashes"); } else unmapped.Add(key + ": value not recognised"); break;
                    case "keyBindings": case "bindings": unmapped.Add(key + ": original keyboard actions differ from AshenedSpire actions"); break;
                    default: unmapped.Add(key + ": no AshenedSpire equivalent"); break;
                }
            }
            if (original["largeText"] != null)
            {
                if (!textHandled && original["largeText"].Type == JTokenType.Boolean && (bool)original["largeText"] && TextRatio("L") is double large)
                { target.TextScale = Clamp(large, OriginalPlayerSettings.TextScaleMin, OriginalPlayerSettings.TextScaleMax); mapped.Add("largeText → TextScale"); }
                else unmapped.Add("largeText: legacy flag; only true maps (to text size L), and only when textSize is not set");
            }
        }
    }
}
