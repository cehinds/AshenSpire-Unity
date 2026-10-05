// The published ruleset-7 table. Kept separate from frozen legacy calculators:
// current hand/resource/rating consumers use one receipt with identical arithmetic.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalStatRows
    {
        private const double Epsilon = 1e-9;
        private static readonly HashSet<string> NonWeights = new HashSet<string>(new[] {
            "base", "sourceStat", "pointsPerTier", "gainPerTier", "rounding", "cap", "perLevel",
            "min", "max", "attributeBaseline", "byClass", "pointsPerIncrease", "gain", "perLevelEvery", "pointsBaseline", "multiplier"
        }, StringComparer.Ordinal);
        private static readonly string[] StatIds = { "energy", "draw", "hp", "stamina", "mana", "poise", "openingHand", "handSize", "ar", "dr", "pr", "ward" };
        private static readonly string[] AttributeIds = { "strength", "dexterity", "constitution", "wisdom", "intelligence" };

        public static JObject Resolve(JObject source, params JObject[] layers)
        {
            if ((int?)source["rulesetVersion"] != 7) throw new ArgumentException("Expected published stat-row ruleset 7.");
            var defaults = source["defaults"] as JObject ?? throw new ArgumentException("Missing stat-row defaults.");
            var authored = source["rules"] as JObject ?? throw new ArgumentException("Missing stat rows.");
            var resolved = new JObject { ["rulesetVersion"] = 7, ["defaults"] = new JObject { ["perLevel"] = 0 }, ["rules"] = new JObject() };
            Apply((JObject)resolved["defaults"], defaults);
            foreach (var id in StatIds)
            {
                var row = authored[id] as JObject ?? throw new ArgumentException("Missing derived stat: " + id);
                var merged = (JObject)defaults.DeepClone(); Apply(merged, row);
                ((JObject)resolved["rules"])[id] = merged;
            }
            foreach (var entry in authored.Properties()) if (!StatIds.Contains(entry.Name)) throw new ArgumentException("Unknown derived stat: " + entry.Name);
            foreach (var layer in layers.Where(x => x != null))
            {
                if (layer["defaults"] is JObject patch)
                {
                    var normalized = Normalize(patch); Apply((JObject)resolved["defaults"], normalized);
                    foreach (var entry in ((JObject)resolved["rules"]).Properties()) Apply((JObject)entry.Value, normalized);
                }
                if (layer["rules"] is JObject patches) foreach (var entry in patches.Properties())
                {
                    var row = resolved["rules"][entry.Name] as JObject ?? throw new ArgumentException("Unknown derived stat: " + entry.Name);
                    var rowPatch = (JObject)entry.Value;
                    if (rowPatch["sourceStat"] != null || rowPatch["gainPerTier"] != null) throw new ArgumentException("Current stat overrides must name attribute weights.");
                    Apply(row, Normalize(rowPatch));
                }
            }
            foreach (var entry in ((JObject)resolved["rules"]).Properties()) Validate((JObject)entry.Value, entry.Name);
            return resolved;
        }

        public static JObject Receipt(JObject table, string id, JObject attributes, JObject classDefinition, int? level = null)
        {
            var authored = table["rules"]?[id] as JObject ?? throw new ArgumentException("Unknown derived stat: " + id);
            var row = (JObject)(table["defaults"] as JObject ?? new JObject()).DeepClone(); Apply(row, authored);
            row = ForClass(row, (string)classDefinition?["id"]);
            var weights = new JObject(); var terms = new JObject(); var points = 0d;
            var baseline = Optional(row, "attributeBaseline", 0);
            foreach (var property in row.Properties().Where(p => !NonWeights.Contains(p.Name)))
            {
                var weight = FormulaEvaluator.Number(row, property.Name); if (weight == 0) continue;
                var amount = FormulaEvaluator.Number(attributes, property.Name);
                if (baseline > 0) amount = Math.Max(0, amount - baseline);
                var term = Math.Floor(amount * weight + Epsilon);
                weights[property.Name] = weight; terms[property.Name] = term; points += term;
            }
            var perIncrease = Optional(row, "pointsPerIncrease", 1);
            if (perIncrease <= 0) throw new ArgumentException("pointsPerIncrease must be positive.");
            var counted = Math.Max(0, points - Math.Max(0, Optional(row, "pointsBaseline", 0)));
            // Without a positive baseline, negative legacy totals are retained.
            if (Optional(row, "pointsBaseline", 0) <= 0) counted = points;
            var scaled = Numeric(row["multiplier"]) ? Math.Floor(counted * (double)row["multiplier"] + Epsilon) : counted;
            var rounding = (string)row["rounding"] ?? "floor";
            var tier = perIncrease == 1 ? scaled : Round(scaled / perIncrease, rounding);
            var basis = Scalar(row["base"], classDefinition);
            var gain = row["gain"] == null ? 1 : Scalar(row["gain"], classDefinition);
            var perLevel = Optional(row, "perLevel", 0);
            var bonus = 0d;
            if (level > 1)
            {
                if (row["perLevel"] is JObject cadence)
                {
                    var every = Optional(cadence, "every", 0);
                    if (every > 0 && every == Math.Floor(every)) bonus = Math.Floor((level.Value - 1) / every) * Optional(cadence, "gain", 0);
                }
                else if (perLevel != 0)
                {
                    var every = Optional(row, "perLevelEvery", 0);
                    if (every > 0 && every == Math.Floor(every)) bonus = Math.Floor((level.Value - 1) / every) * perLevel;
                    else { var rawBonus = (level.Value - 1) * perLevel; bonus = Round(rawBonus + (rounding == "floor" ? Epsilon : 0), rounding); }
                }
            }
            var raw = basis + tier * gain + bonus; var value = raw;
            var cap = row["cap"] ?? JValue.CreateNull(); var min = Bound(row, "min"); var max = Bound(row, "max");
            if (Numeric(cap)) value = Math.Min(value, (double)cap);
            if (Numeric(min)) value = Math.Max(value, (double)min);
            if (Numeric(max)) value = Math.Min(value, (double)max);
            return new JObject { ["id"] = id, ["weights"] = weights, ["terms"] = terms, ["points"] = points,
                ["pointsPerIncrease"] = perIncrease, ["tier"] = tier, ["base"] = basis, ["gain"] = gain,
                ["perLevel"] = perLevel, ["level"] = level.HasValue ? new JValue(level.Value) : JValue.CreateNull(),
                ["levelBonus"] = bonus, ["raw"] = raw, ["cap"] = cap.DeepClone(), ["min"] = min, ["max"] = max, ["value"] = value };
        }

        private static JObject ForClass(JObject row, string id)
        {
            var own = id == null ? null : row["byClass"]?[id] as JObject;
            row.Remove("byClass");
            if (own == null) return row;
            var bounds = new JObject(row.Properties().Where(p => NonWeights.Contains(p.Name)).Select(p => new JProperty(p.Name, p.Value.DeepClone())));
            Apply(bounds, own); return bounds;
        }
        private static JObject Normalize(JObject patch)
        {
            var row = (JObject)patch.DeepClone();
            if (row["pointsPerTier"] != null) { row["pointsPerIncrease"] = row["pointsPerTier"].DeepClone(); row.Remove("pointsPerTier"); }
            return row;
        }
        private static void Validate(JObject row, string id)
        {
            foreach (var property in row.Properties())
                if (!NonWeights.Contains(property.Name) && !AttributeIds.Contains(property.Name)) throw new ArgumentException("Unknown stat-row attribute: " + id + "/" + property.Name);
            foreach (var property in row.Properties().Where(p => !NonWeights.Contains(p.Name)))
                if (FormulaEvaluator.Number(row, property.Name) < 0) throw new ArgumentException("Negative stat-row weight.");
            if (row["byClass"] is JObject forms) foreach (var form in forms.Properties()) Validate((JObject)form.Value, id);
            if (row["rounding"] != null) Round(0, (string)row["rounding"]);
            if (Optional(row, "pointsPerIncrease", 1) <= 0) throw new ArgumentException("Invalid stat-row tier size.");
        }
        private static bool Numeric(JToken token) => token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) && !double.IsNaN((double)token) && !double.IsInfinity((double)token);
        private static double Optional(JObject row, string field, double fallback) => Numeric(row[field]) ? (double)row[field] : fallback;
        private static JToken Bound(JObject row, string field) => Numeric(row[field]) ? row[field].DeepClone() : JValue.CreateNull();
        private static double Scalar(JToken token, JObject hero) => token is JObject field ? FormulaEvaluator.Number(hero, (string)field["field"]) : Numeric(token) ? (double)token : throw new ArgumentException("Invalid stat-row scalar.");
        private static double Round(double value, string rounding) => rounding == "floor" ? Math.Floor(value) : rounding == "ceil" ? Math.Ceiling(value) : rounding == "round" ? Math.Floor(value + .5) : throw new ArgumentException("Unknown stat-row rounding.");
        private static void Apply(JObject target, JObject patch) { foreach (var property in patch.Properties()) target[property.Name] = property.Value.DeepClone(); }
    }
}
