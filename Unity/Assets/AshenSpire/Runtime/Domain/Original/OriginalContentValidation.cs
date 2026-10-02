// OriginalContentValidation.cs — schema and cross-table authoring checks before content is accepted.
// ENTRY POINTS: Validate(data, file) returns every problem; ThrowIfInvalid(data, file) throws one
// OriginalContentValidationException listing the first MessageLimit problems plus the total.
// Scope.Full (schema + ranges + references) is for authored content: the CSV importer and mod packs.
// Scope.References is what OriginalContentCatalog runs on every load, unchanged in coverage from the
// earlier first-error check, so content frozen inside older saves keeps loading. Nothing here grants
// an item or implements an effect; runtime command tests remain required for new mechanics.
// Field schema: OriginalContentSchema (generated from the HTML game's src/model/schemas.js by
// tools/unity-content-schema.mjs). Add new cross-table reference contracts in References().
// Paths: a row with a unique string id is named by id (cards[strike].cost); any other row by its
// index (cards[12].cost). Nested arrays use indexes, objects and maps use dots
// (enemies[wanderingSoldier].moves.slash.damage, mapConfigs.1.floors).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    /// <summary>One content problem: the source file, the path inside it and what is wrong.</summary>
    public sealed class OriginalContentError
    {
        public OriginalContentError(string file, string path, string message) { File = file; Path = path; Message = message; }
        public string File { get; }
        public string Path { get; }
        public string Message { get; }
        public override string ToString() => File + ": " + Path + ": " + Message;
    }

    /// <summary>Thrown by ThrowIfInvalid. Still an ArgumentException so existing callers keep working.</summary>
    public sealed class OriginalContentValidationException : ArgumentException
    {
        public OriginalContentValidationException(IReadOnlyList<OriginalContentError> errors, int limit = OriginalContentValidation.MessageLimit)
            : base(OriginalContentValidation.Describe(errors, limit)) { Errors = errors; }
        public IReadOnlyList<OriginalContentError> Errors { get; }
    }

    /// <summary>References: ids and cross-table references only (catalog load). Full: adds the field schema and ranges.</summary>
    public enum OriginalValidationScope { References, Full }

    public static class OriginalContentValidation
    {
        public const string DefaultFile = "content.json";
        /// <summary>How many problems the thrown message lists before "… and N more".</summary>
        public const int MessageLimit = 20;
        /// <summary>Scripts implemented natively in C# (CombatSession.Effects); valid targets of a `script` reference.</summary>
        public static readonly IReadOnlyList<string> NativeScripts = new[] { "wondrousDraught" };

        // Tables every row of which must be an object with a unique string id. The first group is
        // required by OriginalContentCatalog and the reference checks; the rest are checked when present.
        private static readonly string[] RequiredTables = { "cards", "classes", "statuses", "stances", "enemies", "encounters", "events", "flasks", "relics", "attributes", "creationModes", "unlocks", "equipment.armaments", "equipment.startingKits", "equipment.slots" };
        private static readonly string[] OptionalTables = { "resources", "keywords", "equipment.basicCardProfiles" };
        private static readonly Dictionary<string, string> Singular = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attributes"] = "attribute", ["creationModes"] = "creation mode", ["cards"] = "card", ["resources"] = "resource", ["relics"] = "relic",
            ["statuses"] = "status", ["stances"] = "stance", ["keywords"] = "keyword", ["enemies"] = "enemy", ["encounters"] = "encounter",
            ["events"] = "event", ["flasks"] = "flask", ["classes"] = "class", ["scripts"] = "script", ["unlocks"] = "unlock",
            ["equipment.armaments"] = "armament", ["equipment.startingKits"] = "starting kit", ["equipment.slots"] = "equipment slot",
            ["equipment.basicCardProfiles"] = "basic card profile",
        };
        private static readonly Lazy<JObject> Schema = new Lazy<JObject>(() => JObject.Parse(OriginalContentSchema.Json));

        /// <summary>Every schema, range and reference problem in <paramref name="data"/>; empty when valid. Never throws for bad data.</summary>
        public static IReadOnlyList<OriginalContentError> Validate(JObject data, string file = DefaultFile, OriginalValidationScope scope = OriginalValidationScope.Full)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return new Run(data, string.IsNullOrEmpty(file) ? DefaultFile : file, scope == OriginalValidationScope.Full).Execute();
        }

        /// <summary>Throwing convenience: one exception describing every problem (first <see cref="MessageLimit"/> listed).</summary>
        public static void ThrowIfInvalid(JObject data, string file = DefaultFile, OriginalValidationScope scope = OriginalValidationScope.Full)
        {
            var errors = Validate(data, file, scope);
            if (errors.Count > 0) throw new OriginalContentValidationException(errors);
        }

        /// <summary>The catalog's load-time check: references only, every problem reported in one exception.</summary>
        public static void ValidateReferences(JObject data, string file = DefaultFile) => ThrowIfInvalid(data, file, OriginalValidationScope.References);

        public static string Describe(IReadOnlyList<OriginalContentError> errors, int limit = MessageLimit)
        {
            if (errors == null || errors.Count == 0) return "Original content is valid.";
            var text = new StringBuilder();
            text.Append("Original content is invalid: ").Append(errors.Count).Append(errors.Count == 1 ? " error." : " errors.");
            foreach (var error in errors.Take(Math.Max(1, limit))) text.Append("\n  ").Append(error);
            if (errors.Count > limit) text.Append("\n  … and ").Append(errors.Count - limit).Append(" more.");
            return text.ToString();
        }

        private sealed class Run
        {
            private readonly JObject _data;
            private readonly string _file;
            private static JObject _s => Schema.Value; // parsed once, on first Full validation
            private readonly Dictionary<string, HashSet<string>> _sets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            private readonly List<OriginalContentError> _errors = new List<OriginalContentError>();
            private readonly Dictionary<string, HashSet<string>> _ids = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            // Row label (table[id] or table[index]) for every object row of an id table.
            private readonly Dictionary<string, List<(JObject Row, string Label)>> _rows = new Dictionary<string, List<(JObject, string)>>(StringComparer.Ordinal);

            private readonly bool _full;
            public Run(JObject data, string file, bool full) { _data = data; _file = file; _full = full; }

            public IReadOnlyList<OriginalContentError> Execute()
            {
                try
                {
                    CollectIds();
                    if (_full) { Schemas(); Ranges(); }
                    References();
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    Err(_errors, "$", "Validation stopped early on malformed content (" + error.GetType().Name + ": " + error.Message + ").");
                }
                return _errors;
            }

            private void Err(List<OriginalContentError> sink, string path, string message) => sink.Add(new OriginalContentError(_file, path, message));
            private void Err(string path, string message) => Err(_errors, path, message);

            private JToken Get(string dotted)
            {
                JToken node = _data;
                foreach (var part in dotted.Split('.'))
                {
                    if (!(node is JObject parent)) return null;
                    node = parent[part];
                }
                return node;
            }
            private IEnumerable<(JObject Row, string Label)> Rows(string table) => _rows.TryGetValue(table, out var rows) ? rows : Enumerable.Empty<(JObject, string)>();
            private HashSet<string> Ids(string table) => _ids.TryGetValue(table, out var ids) ? ids : (_ids[table] = new HashSet<string>(StringComparer.Ordinal));

            // ---- ids --------------------------------------------------------------------------------
            private void CollectIds()
            {
                foreach (var table in RequiredTables.Concat(OptionalTables))
                {
                    var value = Get(table); var ids = Ids(table); var rows = _rows[table] = new List<(JObject, string)>();
                    // Reference scope reports only the tables the catalog always required; optional tables just supply ids.
                    var report = _full || RequiredTables.Contains(table);
                    if (value == null) { if (RequiredTables.Contains(table)) Err(table, "Missing required table '" + table + "'."); continue; }
                    if (!(value is JArray array)) { if (report) Err(table, "Expected an array of " + table + " rows, got " + Describe(value) + "."); continue; }
                    var counts = array.OfType<JObject>().Select(r => r["id"]).Where(IsId).GroupBy(id => (string)id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                    for (var i = 0; i < array.Count; i++)
                    {
                        var at = table + "[" + i + "]";
                        if (!(array[i] is JObject row)) { if (report) Err(at, "Each row must be an object, got " + Describe(array[i]) + "."); continue; }
                        var id = row["id"];
                        if (!IsId(id)) { if (report) Err(at + ".id", id == null ? "Missing required field 'id'." : "'id' must be a non-empty string, got " + Describe(id) + "."); rows.Add((row, at)); continue; }
                        var text = (string)id;
                        if (counts[text] > 1) { if (report) Err(at + ".id", "Duplicate id '" + text + "' in " + table + "."); rows.Add((row, at)); ids.Add(text); continue; }
                        ids.Add(text); rows.Add((row, table + "[" + text + "]"));
                    }
                }
                var scripts = Ids("scripts");
                if (_data["scripts"] is JObject authored) foreach (var property in authored.Properties()) scripts.Add(property.Name);
                foreach (var name in NativeScripts) scripts.Add(name);
            }

            // ---- schema -----------------------------------------------------------------------------
            private void Schemas()
            {
                foreach (var table in ((JObject)_s["tables"]).Properties())
                    foreach (var (row, label) in Rows(table.Name)) Walk(row, (JObject)table.Value, label, _errors);
                foreach (var entry in ((JObject)_s["objects"]).Properties())
                {
                    var value = Get(entry.Name);
                    if (value == null) Err(entry.Name, "Missing required section '" + entry.Name + "'.");
                    else Walk(value, (JObject)entry.Value, entry.Name, _errors);
                }
                foreach (var entry in ((JObject)_s["maps"]).Properties())
                {
                    var value = Get(entry.Name);
                    if (value == null) { Err(entry.Name, "Missing required section '" + entry.Name + "'."); continue; }
                    if (!(value is JObject map)) { Err(entry.Name, "Expected an object keyed by id, got " + Describe(value) + "."); continue; }
                    foreach (var property in map.Properties()) Walk(property.Value, (JObject)entry.Value, entry.Name + "." + property.Name, _errors);
                }
                if (Get("characterCreation.keepsakes") is JArray keepsakes)
                    for (var i = 0; i < keepsakes.Count; i++)
                        if (keepsakes[i] is JObject keepsake && keepsake["effects"] != null)
                            Effects(keepsake["effects"], "characterCreation.keepsakes[" + (IsId(keepsake["id"]) ? (string)keepsake["id"] : i.ToString(CultureInfo.InvariantCulture)) + "].effects", _errors);
            }

            private void Walk(JToken value, JObject node, string path, List<OriginalContentError> sink)
            {
                switch ((string)node["k"])
                {
                    case "any": return;
                    case "str": if (value.Type != JTokenType.String) Err(sink, path, "Expected string, got " + Describe(value) + "."); return;
                    case "bool": if (value.Type != JTokenType.Boolean) Err(sink, path, "Expected boolean, got " + Describe(value) + "."); return;
                    case "num":
                        if (!IsNumber(value)) Err(sink, path, "Expected " + (Flag(node, "int") ? "integer" : "number") + ", got " + Describe(value) + ".");
                        else if (Flag(node, "int") && !IsInteger(value)) Err(sink, path, "Expected integer, got " + Describe(value) + ".");
                        return;
                    case "enum":
                    {
                        var values = (JArray)node["values"];
                        if (!values.Any(x => JToken.DeepEquals(x, value))) Err(sink, path, "Expected one of [" + string.Join(", ", values.Select(x => x.ToString())) + "], got " + Describe(value) + ".");
                        return;
                    }
                    case "arr":
                    {
                        if (!(value is JArray array)) { Err(sink, path, "Expected array, got " + Describe(value) + "."); return; }
                        if (node["len"] != null && array.Count != (int)node["len"]) Err(sink, path, "Expected array of length " + (int)node["len"] + ", got " + array.Count + ".");
                        for (var i = 0; i < array.Count; i++) Walk(array[i], (JObject)node["of"], path + "[" + i + "]", sink);
                        return;
                    }
                    case "map":
                    {
                        if (!(value is JObject map)) { Err(sink, path, "Expected object map, got " + Describe(value) + "."); return; }
                        foreach (var property in map.Properties()) Walk(property.Value, (JObject)node["of"], path + "." + property.Name, sink);
                        return;
                    }
                    case "obj":
                    {
                        if (!(value is JObject record)) { Err(sink, path, "Expected object, got " + Describe(value) + "."); return; }
                        var fields = (JObject)node["fields"];
                        foreach (var property in record.Properties())
                            if (fields[property.Name] == null) Err(sink, path + "." + property.Name, "Unknown field '" + property.Name + "'.");
                        foreach (var field in fields.Properties())
                        {
                            var child = record[field.Name];
                            if (child == null) { if (!Flag((JObject)field.Value, "opt")) Err(sink, path + "." + field.Name, "Missing required field '" + field.Name + "'."); continue; }
                            Walk(child, (JObject)field.Value, path + "." + field.Name, sink);
                        }
                        return;
                    }
                    case "union":
                    {
                        var branches = (JArray)node["anyOf"];
                        foreach (JObject branch in branches)
                        {
                            var probe = new List<OriginalContentError>();
                            Walk(value, branch, path, probe);
                            if (probe.Count == 0) return;
                        }
                        Err(sink, path, "Expected " + string.Join(" or ", branches.Select(b => Kind((JObject)b))) + ", got " + Describe(value) + ".");
                        return;
                    }
                    case "ref": Ref(value, (string)node["reg"], path, sink); return;
                    case "effects": Effects(value, path, sink); return;
                    case "triggers": Triggers(value, path, sink); return;
                    case "predicate": Predicate(value, path, sink); return;
                    case "formulaOrNum": Formula(value, path, sink); return;
                    default: Err(sink, path, "Internal: unknown schema kind '" + node["k"] + "'."); return;
                }
            }

            private void Ref(JToken value, string registry, string path, List<OriginalContentError> sink)
            {
                var noun = Singular.TryGetValue(registry, out var word) ? word : registry;
                if (value.Type != JTokenType.String) Err(sink, path, "Expected " + noun + " id string, got " + Describe(value) + ".");
                else if (!Ids(registry).Contains((string)value)) Err(sink, path, "Unknown " + noun + " '" + (string)value + "' (no " + registry + " row has this id).");
            }

            private static string Kind(JObject node)
            {
                switch ((string)node["k"])
                {
                    case "num": return Flag(node, "int") ? "integer" : "number";
                    case "str": return "string";
                    case "bool": return "boolean";
                    case "enum": return "one of [" + string.Join(", ", ((JArray)node["values"]).Select(x => x.ToString())) + "]";
                    case "obj": var mode = node["fields"]?["mode"]; return mode != null && (string)mode["k"] == "enum" ? "object with mode " + string.Join("/", ((JArray)mode["values"]).Select(x => x.ToString())) : "object";
                    case "arr": return "array";
                    case "map": return "object map";
                    default: return (string)node["k"];
                }
            }

            private void Effects(JToken effects, string path, List<OriginalContentError> sink)
            {
                if (!(effects is JArray list)) { Err(sink, path, "Expected effects array, got " + Describe(effects) + "."); return; }
                var opcodes = Strings("opcodes"); var targets = Strings("targets"); var common = Strings("commonEffectFields");
                for (var i = 0; i < list.Count; i++)
                {
                    var p = path + "[" + i + "]";
                    if (!(list[i] is JObject effect)) { Err(sink, p, "Effect must be an object, got " + Describe(list[i]) + "."); continue; }
                    if (effect["script"]?.Type == JTokenType.String) { Ref(effect["script"], "scripts", p + ".script", sink); continue; }
                    if (effect["op"]?.Type != JTokenType.String) { Err(sink, p + ".op", "Effect is missing its 'op' string."); continue; }
                    var op = (string)effect["op"];
                    if (!opcodes.Contains(op)) { Err(sink, p + ".op", "Unknown opcode '" + op + "' (closed set: " + string.Join(", ", opcodes) + ")."); continue; }
                    var spec = (JObject)_s["effectSpecs"][op];
                    var allowed = new HashSet<string>(common.Concat(spec["allowed"].Values<string>()), StringComparer.Ordinal);
                    foreach (var property in effect.Properties())
                        if (!allowed.Contains(property.Name)) Err(sink, p + "." + property.Name, "Unknown field '" + property.Name + "' on opcode '" + op + "'.");
                    foreach (var required in spec["required"].Values<string>())
                        if (effect[required] == null) Err(sink, p + "." + required, "Opcode '" + op + "' is missing required field '" + required + "'.");
                    if (effect["target"] != null && !(effect["target"].Type == JTokenType.String && targets.Contains((string)effect["target"])))
                        Err(sink, p + ".target", "Unknown target " + Describe(effect["target"]) + " (closed set: " + string.Join(", ", targets) + ").");
                    foreach (var numeric in new[] { "amount", "stacks", "hits", "pct", "count", "repeat" })
                        if (effect[numeric] != null) Formula(effect[numeric], p + "." + numeric, sink);
                    if (effect["if"] != null) Predicate(effect["if"], p + ".if", sink);
                    foreach (var reference in ((JObject)spec["refs"]).Properties())
                        if (effect[reference.Name]?.Type == JTokenType.String) Ref(effect[reference.Name], (string)reference.Value, p + "." + reference.Name, sink);
                    if (op == "damage" && effect["tags"] != null && !(effect["tags"] is JArray tags && tags.Count > 0 && tags.All(t => t.Type == JTokenType.String)))
                        Err(sink, p + ".tags", "damage tags must be a non-empty array of effect-tag ids.");
                    if (op == "stagger" && effect["target"]?.Type == JTokenType.String && new[] { "self", "player", "owner", "ally" }.Contains((string)effect["target"]))
                        Err(sink, p + ".target", "stagger targets enemies only, got '" + (string)effect["target"] + "'.");
                    if (op == "addFlaskCapacity")
                    {
                        if (!(effect["kind"]?.Type == JTokenType.String && ((string)effect["kind"] == "hp" || (string)effect["kind"] == "mana"))) Err(sink, p + ".kind", "Must be 'hp' or 'mana'.");
                        if (effect["amount"] != null && !(IsInteger(effect["amount"]) && effect["amount"].Value<double>() > 0)) Err(sink, p + ".amount", "Must be a positive integer.");
                    }
                    if (op == "addCard")
                    {
                        if (effect["pile"] != null && !Strings("piles").Contains(Text(effect["pile"]) ?? "")) Err(sink, p + ".pile", "Unknown pile " + Describe(effect["pile"]) + " (legal: " + string.Join(", ", Strings("piles")) + ").");
                        if (effect["position"] != null && !Strings("pilePositions").Contains(Text(effect["position"]) ?? "")) Err(sink, p + ".position", "Unknown position " + Describe(effect["position"]) + " (legal: " + string.Join(", ", Strings("pilePositions")) + ").");
                    }
                }
            }

            private void Triggers(JToken triggers, string path, List<OriginalContentError> sink)
            {
                if (!(triggers is JArray list)) { Err(sink, path, "Expected triggers array, got " + Describe(triggers) + "."); return; }
                var fields = Strings("triggerFields"); var events = Strings("triggerEvents");
                for (var i = 0; i < list.Count; i++)
                {
                    var p = path + "[" + i + "]";
                    if (!(list[i] is JObject trigger)) { Err(sink, p, "Trigger must be an object, got " + Describe(list[i]) + "."); continue; }
                    foreach (var property in trigger.Properties()) if (!fields.Contains(property.Name)) Err(sink, p + "." + property.Name, "Unknown trigger field '" + property.Name + "'.");
                    if (!(trigger["on"]?.Type == JTokenType.String && events.Contains((string)trigger["on"]))) Err(sink, p + ".on", "Unknown trigger event " + Describe(trigger["on"]) + ".");
                    if (trigger["if"] != null) Predicate(trigger["if"], p + ".if", sink);
                    if (trigger["once"] != null && trigger["once"].Type != JTokenType.Boolean) Err(sink, p + ".once", "Expected boolean, got " + Describe(trigger["once"]) + ".");
                    if (trigger["limitPerTurn"] != null && !IsInteger(trigger["limitPerTurn"])) Err(sink, p + ".limitPerTurn", "Expected integer, got " + Describe(trigger["limitPerTurn"]) + ".");
                    if (trigger["do"] == null) Err(sink, p + ".do", "Missing required field 'do'.");
                    else Effects(trigger["do"], p + ".do", sink);
                }
            }

            private void Predicate(JToken value, string path, List<OriginalContentError> sink)
            {
                if (!(value is JObject predicate) || predicate["p"]?.Type != JTokenType.String) { Err(sink, path, "Predicate must be an object with a 'p' string, got " + Describe(value) + "."); return; }
                var name = (string)predicate["p"];
                if (!Strings("predicates").Contains(name)) { Err(sink, path + ".p", "Unknown predicate '" + name + "'."); return; }
                var allowed = new HashSet<string>(new[] { "p" }.Concat(_s["predicateFields"][name].Values<string>()), StringComparer.Ordinal);
                foreach (var property in predicate.Properties()) if (!allowed.Contains(property.Name)) Err(sink, path + "." + property.Name, "Unknown field '" + property.Name + "' on predicate '" + name + "'.");
                if (predicate["of"] != null && !Strings("predicateOf").Contains(Text(predicate["of"]) ?? "")) Err(sink, path + ".of", "Unknown entity ref " + Describe(predicate["of"]) + " (allowed: " + string.Join(", ", Strings("predicateOf")) + ").");
                switch (name)
                {
                    case "inStance": Ref(predicate["stance"] ?? JValue.CreateNull(), "stances", path + ".stance", sink); break;
                    case "hasStatus": case "eventStatusIs": Ref(predicate["status"] ?? JValue.CreateNull(), "statuses", path + ".status", sink); break;
                    case "cardTypeIs": if (!Strings("cardTypes").Contains(Text(predicate["type"]) ?? "")) Err(sink, path + ".type", "Unknown card type " + Describe(predicate["type"]) + "."); break;
                    case "everyNthCardThisCombat": if (!(IsInteger(predicate["n"]) && predicate["n"].Value<double>() >= 1)) Err(sink, path + ".n", "n must be a positive integer."); break;
                    case "random": if (!IsNumber(predicate["pct"])) Err(sink, path + ".pct", "pct must be a number."); break;
                    case "all": case "any":
                        if (!(predicate["preds"] is JArray preds)) Err(sink, path + ".preds", "'" + name + "' requires a preds array.");
                        else for (var i = 0; i < preds.Count; i++) Predicate(preds[i], path + ".preds[" + i + "]", sink);
                        break;
                    case "not": Predicate(predicate["pred"] ?? JValue.CreateNull(), path + ".pred", sink); break;
                }
            }

            private void Formula(JToken value, string path, List<OriginalContentError> sink)
            {
                if (IsNumber(value)) return;
                if (!(value is JObject formula) || formula["f"]?.Type != JTokenType.String) { Err(sink, path, "Expected number or formula object, got " + Describe(value) + "."); return; }
                var op = (string)formula["f"];
                if (!Strings("formulaOps").Contains(op)) { Err(sink, path + ".f", "Unknown formula op '" + op + "'."); return; }
                var allowed = new HashSet<string>(new[] { "f" }.Concat(_s["formulaFields"][op].Values<string>()), StringComparer.Ordinal);
                foreach (var property in formula.Properties()) if (!allowed.Contains(property.Name)) Err(sink, path + "." + property.Name, "Unknown field '" + property.Name + "' on formula '" + op + "'.");
                if (formula["of"] != null && !Strings("formulaOf").Contains(Text(formula["of"]) ?? "")) Err(sink, path + ".of", "Unknown entity ref " + Describe(formula["of"]) + " (allowed: " + string.Join(", ", Strings("formulaOf")) + ").");
                if (op == "add" || op == "mul")
                {
                    if (!(formula["args"] is JArray args)) Err(sink, path + ".args", "'" + op + "' requires an args array.");
                    else for (var i = 0; i < args.Count; i++) Formula(args[i], path + ".args[" + i + "]", sink);
                }
                if (op == "stacks") { Ref(formula["status"] ?? JValue.CreateNull(), "statuses", path + ".status", sink); if (formula["of"] == null) Err(sink, path + ".of", "'stacks' requires 'of'."); }
                if ((op == "percentMaxHp" || op == "missingHp" || op == "blockOf" || op == "hpOf") && formula["of"] == null) Err(sink, path + ".of", "'" + op + "' requires 'of'.");
                if (op == "percentMaxHp" && !IsNumber(formula["pct"])) Err(sink, path + ".pct", "'percentMaxHp' requires a numeric pct.");
            }

            private HashSet<string> Strings(string key) => _sets.TryGetValue(key, out var set) ? set : (_sets[key] = new HashSet<string>(_s[key].Values<string>(), StringComparer.Ordinal));

            // ---- ranges -----------------------------------------------------------------------------
            private void Ranges()
            {
                void Band(JToken band, string path)
                {
                    if (!(band is JObject b) || !IsInteger(b["min"]) || !IsInteger(b["max"])) return; // shape already reported
                    if ((long)b["min"] < 1) Err(path + ".min", "Must be a positive integer, got " + b["min"] + ".");
                    if ((long)b["min"] > (long)b["max"]) Err(path, "min (" + b["min"] + ") must not exceed max (" + b["max"] + ").");
                }
                void AtLeast(JToken value, double floor, string path)
                {
                    if (IsNumber(value) && value.Value<double>() < floor) Err(path, "Must be " + (floor == 0 ? "zero or more" : "at least " + floor.ToString(CultureInfo.InvariantCulture)) + ", got " + value + ".");
                }
                var classIds = Ids("classes");
                foreach (var (card, label) in Rows("cards"))
                {
                    if (card["class"]?.Type == JTokenType.String && (string)card["class"] != "colorless" && !classIds.Contains((string)card["class"]))
                        Err(label + ".class", "Unknown class '" + (string)card["class"] + "' (use a classes id or 'colorless').");
                    foreach (var field in new[] { "cost", "manaCost", "staminaCost" }) { AtLeast(card[field], 0, label + "." + field); AtLeast((card["upgrade"] as JObject)?[field], 0, label + ".upgrade." + field); }
                }
                foreach (var (enemy, label) in Rows("enemies"))
                {
                    if (enemy["hp"] is JArray hp && hp.Count == 2 && IsInteger(hp[0]) && IsInteger(hp[1]))
                    {
                        if ((long)hp[0] < 1) Err(label + ".hp[0]", "Minimum HP must be at least 1, got " + hp[0] + ".");
                        if ((long)hp[0] > (long)hp[1]) Err(label + ".hp", "Minimum HP (" + hp[0] + ") must not exceed maximum HP (" + hp[1] + ").");
                    }
                    AtLeast(enemy["poiseMax"], 0, label + ".poiseMax");
                    Band(enemy["levelProfile"], label + ".levelProfile");
                    if (enemy["moves"] is JObject moves)
                    {
                        if (!moves.HasValues) Err(label + ".moves", "An enemy needs at least one move.");
                        foreach (var move in moves.Properties())
                        {
                            if (!(move.Value is JObject)) continue;
                            AtLeast(move.Value["weight"], 0, label + ".moves." + move.Name + ".weight");
                            foreach (var field in new[] { "damage", "hits", "block" }) AtLeast(move.Value[field], 0, label + ".moves." + move.Name + "." + field);
                        }
                        if (enemy["firstMove"]?.Type == JTokenType.String && moves[(string)enemy["firstMove"]] == null) Err(label + ".firstMove", "Unknown move '" + (string)enemy["firstMove"] + "' (not in this enemy's moves).");
                        if (enemy["phases"] is JArray phases)
                            for (var i = 0; i < phases.Count; i++)
                                if ((phases[i] as JObject)?["unlockMoves"] is JArray unlock)
                                    for (var j = 0; j < unlock.Count; j++)
                                        if (unlock[j].Type == JTokenType.String && moves[(string)unlock[j]] == null) Err(label + ".phases[" + i + "].unlockMoves[" + j + "]", "Unknown move '" + (string)unlock[j] + "' (not in this enemy's moves).");
                    }
                    if (enemy["arcaneExposure"] is JObject exposure && Text(exposure["mode"]) == "configured")
                    {
                        if (IsNumber(exposure["threshold"]) && exposure["threshold"].Value<double>() <= 0) Err(label + ".arcaneExposure.threshold", "Must be a positive integer.");
                        if (IsNumber(exposure["buildupMultiplier"]) && exposure["buildupMultiplier"].Value<double>() <= 0) Err(label + ".arcaneExposure.buildupMultiplier", "Must be greater than 0.");
                    }
                    if (enemy["damageResistanceBySchool"] is JObject resist)
                        foreach (var school in resist.Properties())
                            if (IsNumber(school.Value) && (school.Value.Value<double>() < 0 || school.Value.Value<double>() > 100)) Err(label + ".damageResistanceBySchool." + school.Name, "Must be a percent from 0 to 100, got " + school.Value + ".");
                }
                foreach (var (encounter, label) in Rows("encounters"))
                {
                    if (encounter["enemies"] is JArray enemies && enemies.Count == 0) Err(label + ".enemies", "An encounter needs at least one enemy.");
                    AtLeast(encounter["weight"], 0, label + ".weight");
                    AtLeast(encounter["act"], 1, label + ".act");
                    Band(encounter["floorBand"], label + ".floorBand");
                    Band(encounter["targetBand"], label + ".targetBand");
                }
                foreach (var (hero, label) in Rows("classes"))
                {
                    AtLeast(hero["maxHp"], 1, label + ".maxHp");
                    AtLeast((hero["startingFlaskAllocation"] as JObject)?["hp"], 0, label + ".startingFlaskAllocation.hp");
                    AtLeast((hero["startingFlaskAllocation"] as JObject)?["mana"], 0, label + ".startingFlaskAllocation.mana");
                }
                foreach (var table in new[] { "relics", "flasks", "events" })
                    foreach (var (row, label) in Rows(table)) if (row["name"]?.Type == JTokenType.String && string.IsNullOrWhiteSpace((string)row["name"])) Err(label + ".name", "Name must not be empty.");
                foreach (var (card, label) in Rows("cards")) if (card["name"]?.Type == JTokenType.String && string.IsNullOrWhiteSpace((string)card["name"])) Err(label + ".name", "Name must not be empty.");
            }

            // ---- cross-table references not expressed in the HTML schema -----------------------------
            private void References()
            {
                void RefAt(JToken value, string table, string path, bool optional = false)
                {
                    if (value == null || value.Type == JTokenType.Null || (value.Type == JTokenType.String && (string)value == ""))
                    {
                        if (!optional) Err(path, "Missing required " + (Singular.TryGetValue(table, out var noun) ? noun : table) + " reference.");
                        return;
                    }
                    Ref(value, table, path, _errors);
                }
                if (!_full)
                {
                    // In Full scope the field schema already checks these references (and Ranges the empty list).
                    foreach (var (encounter, label) in Rows("encounters"))
                    {
                        if (!(encounter["enemies"] is JArray enemies) || enemies.Count == 0) { Err(label + ".enemies", "An encounter needs at least one enemy."); continue; }
                        for (var i = 0; i < enemies.Count; i++) RefAt(enemies[i], "enemies", label + ".enemies[" + i + "]");
                    }
                    foreach (var (hero, label) in Rows("classes"))
                    {
                        RefAt(hero["startingRelic"], "relics", label + ".startingRelic");
                        RefAt(hero["startingSignatureCard"], "cards", label + ".startingSignatureCard");
                    }
                }
                foreach (var (hero, label) in Rows("classes"))
                    if (hero["eligibleStartingKitIds"] is JArray kits)
                        for (var i = 0; i < kits.Count; i++) RefAt(kits[i], "equipment.startingKits", label + ".eligibleStartingKitIds[" + i + "]");
                foreach (var (kit, label) in Rows("equipment.startingKits"))
                {
                    RefAt(kit["classId"], "classes", label + ".classId");
                    RefAt(kit["leftHand"], "equipment.armaments", label + ".leftHand", true);
                    RefAt(kit["rightHand"], "equipment.armaments", label + ".rightHand", true);
                }
                var armour = Get("equipment.armour");
                if (armour == null) Err("equipment.armour", "Missing required table 'equipment.armour'.");
                else if (!(armour is JArray armourRows)) Err("equipment.armour", "Expected an array of armour rows, got " + Describe(armour) + ".");
                else
                {
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < armourRows.Count; i++)
                    {
                        var at = "equipment.armour[" + i + "]";
                        if (!(armourRows[i] is JObject row)) { Err(at, "Each row must be an object, got " + Describe(armourRows[i]) + "."); continue; }
                        if (!IsId(row["id"])) { Err(at + ".id", "'id' must be a non-empty string."); continue; }
                        var key = Text(row["classId"]) + "/" + (string)row["id"];
                        if (!keys.Add(key)) { Err(at, "Duplicate armour identity '" + key + "' (classId/id must be unique)."); continue; }
                        at = "equipment.armour[" + key + "]";
                        RefAt(row["classId"], "classes", at + ".classId");
                        RefAt(row["unlock"], "unlocks", at + ".unlock", true);
                    }
                }
                var profiles = Ids("equipment.basicCardProfiles");
                foreach (var (armament, label) in Rows("equipment.armaments"))
                {
                    RefAt(armament["unlock"], "unlocks", label + ".unlock", true);
                    foreach (var profile in new[] { "attackProfile", "guardProfile", "techniqueProfile" })
                        if (armament[profile]?.Type == JTokenType.String && !profiles.Contains((string)armament[profile]))
                            Err(label + "." + profile, "Unknown basic card profile '" + (string)armament[profile] + "' (no equipment.basicCardProfiles row has this id).");
                }
            }
        }

        // ---- token helpers ---------------------------------------------------------------------------
        private static bool Flag(JObject node, string name) => node[name]?.Type == JTokenType.Boolean && (bool)node[name];
        private static string Text(JToken token) => token?.Type == JTokenType.String ? (string)token : null;
        private static bool IsId(JToken token) => token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)token);
        private static bool IsNumber(JToken token)
        {
            if (token == null) return false;
            if (token.Type == JTokenType.Integer) return true;
            if (token.Type != JTokenType.Float) return false;
            var value = token.Value<double>();
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
        private static bool IsInteger(JToken token) => IsNumber(token) && (token.Type == JTokenType.Integer || Math.Floor(token.Value<double>()) == token.Value<double>());

        /// <summary>The value as an author reads it: string "x", number 5, boolean true, array, object, null, missing.</summary>
        public static string Describe(JToken value)
        {
            if (value == null) return "missing";
            switch (value.Type)
            {
                case JTokenType.Null: return "null";
                case JTokenType.Array: return "array";
                case JTokenType.Object: return "object";
                case JTokenType.String: return "string \"" + (string)value + "\"";
                case JTokenType.Integer: case JTokenType.Float: return "number " + value.ToString(Newtonsoft.Json.Formatting.None);
                case JTokenType.Boolean: return "boolean " + ((bool)value ? "true" : "false");
                default: return value.Type.ToString().ToLowerInvariant();
            }
        }
    }
}
