// DerivedStatCalculator.cs — derived-stat rules, shared by creation and previews.
// Defaults -> authored row -> ordered override layers. Save resolved rules with a
// run so later balance edits cannot change the meaning of its allocation.
// A row naming sourceStat is a legacy (ruleset 1-5) attribute-tier row and reads
// exactly as before. Any other row is a ruleset-6 weighted row (web
// src/model/derivedStats.js deriveStat): base + sum(floor(attribute x weight))
// + floor((level - 1) x perLevel), capped. Weights are decimals per attribute.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class DerivedStatCalculator
    {
        // Binary floats lie at tier edges (0.2 x 15 = 3.0000000000000004); same epsilon as the web.
        private const double Epsilon = 1e-9;
        private static readonly string[] AttributeIds = { "strength", "dexterity", "constitution", "wisdom", "intelligence" };
        private static readonly string[] WeightedFields = { "base", "perLevel", "cap" };
        private static readonly string[] LegacyFields = { "sourceStat", "pointsPerTier", "gainPerTier", "rounding" };
        private static bool Legacy(JObject row) => row["sourceStat"]?.Type == JTokenType.String;
        public static JObject Resolve(JObject source, params JObject[] layers)
        {
            var defaults = source["defaults"] as JObject ?? throw new ArgumentException("Missing derived-stat defaults.");
            var rules = source["rules"] as JObject ?? throw new ArgumentException("Missing derived-stat rules.");
            var resolved = new JObject();
            foreach (var entry in rules.Properties()) { var row = (JObject)defaults.DeepClone(); Apply(row, (JObject)entry.Value); resolved[entry.Name] = row; }
            foreach (var layer in layers.Where(x => x != null))
            {
                if (layer["defaults"] is JObject patch) foreach (var entry in resolved.Properties()) Apply((JObject)entry.Value, patch);
                if (layer["rules"] is JObject patches) foreach (var entry in patches.Properties())
                {
                    if (!(resolved[entry.Name] is JObject row)) throw new ArgumentException("Unknown derived stat: " + entry.Name);
                    Apply(row, (JObject)entry.Value);
                }
            }
            foreach (var entry in resolved.Properties())
            {
                var row = (JObject)entry.Value;
                if (!Legacy(row)) { ValidateWeighted(entry.Name, row); continue; }
                if (FormulaEvaluator.Number(row, "pointsPerTier") <= 0) throw new ArgumentException("pointsPerTier must be positive.");
                if (!new[] { "floor", "ceil", "round" }.Contains((string)row["rounding"])) throw new ArgumentException("Unknown tier rounding.");
            }
            return resolved;
        }
        private static void ValidateWeighted(string id, JObject row)
        {
            foreach (var property in row.Properties())
            {
                if (LegacyFields.Contains(property.Name)) throw new ArgumentException("Derived stat " + id + " is a weighted row and cannot carry " + property.Name + ".");
                if (!WeightedFields.Contains(property.Name) && !AttributeIds.Contains(property.Name)) throw new ArgumentException("Unknown field on derived stat " + id + ": " + property.Name);
            }
            if (!(row["base"] is JObject)) FormulaEvaluator.Number(row, "base");
            if (row["perLevel"] != null && FormulaEvaluator.Number(row, "perLevel") < 0) throw new ArgumentException("Derived stat " + id + ": perLevel cannot be negative.");
            if (row["cap"] != null && row["cap"].Type != JTokenType.Null && FormulaEvaluator.Number(row, "cap") < 0) throw new ArgumentException("Derived stat " + id + ": cap cannot be negative.");
            foreach (var attribute in AttributeIds)
                if (row[attribute] != null && FormulaEvaluator.Number(row, attribute) < 0) throw new ArgumentException("Derived stat " + id + ": weight " + attribute + " cannot be negative.");
        }
        // level is the character level; null (creation previews, catalog checks) reads the attribute term alone.
        public static JObject Receipt(JObject rules, string id, JObject attributes, JObject classDefinition, int? level = null)
        {
            var row = rules[id] as JObject ?? throw new ArgumentException("Unknown derived stat: " + id);
            if (!Legacy(row)) return WeightedReceipt(row, id, attributes, classDefinition, level);
            var stat = (string)row["sourceStat"];
            var points = FormulaEvaluator.Number(attributes, stat);
            var pointsPerTier = FormulaEvaluator.Number(row, "pointsPerTier");
            var ratio = points / pointsPerTier;
            var tier = (string)row["rounding"] == "ceil" ? Math.Ceiling(ratio) : (string)row["rounding"] == "round" ? Math.Floor(ratio + 0.5) : Math.Floor(ratio);
            var basis = Scalar(row, "base", classDefinition);
            var gain = Scalar(row, "gainPerTier", classDefinition);
            var raw = basis + tier * gain;
            var cap = row["cap"];
            var value = cap == null || cap.Type == JTokenType.Null ? raw : Math.Min(raw, (double)cap);
            return new JObject { ["id"] = id, ["sourceStat"] = stat, ["points"] = points, ["pointsPerTier"] = pointsPerTier,
                ["tier"] = tier, ["base"] = basis, ["gainPerTier"] = gain, ["raw"] = raw, ["cap"] = cap?.DeepClone(), ["value"] = value };
        }
        private static JObject WeightedReceipt(JObject row, string id, JObject attributes, JObject classDefinition, int? level)
        {
            var weights = new JObject(); var terms = new JObject(); var points = 0d;
            foreach (var property in row.Properties().Where(x => AttributeIds.Contains(x.Name)))
            {
                var weight = FormulaEvaluator.Number(row, property.Name);
                if (weight == 0) continue;
                var term = Math.Floor(FormulaEvaluator.Number(attributes, property.Name) * weight + Epsilon);
                weights[property.Name] = weight; terms[property.Name] = term; points += term;
            }
            var basis = Scalar(row, "base", classDefinition);
            var perLevel = row["perLevel"] == null ? 0 : FormulaEvaluator.Number(row, "perLevel");
            var levelBonus = level.HasValue && level.Value > 1 ? Math.Floor((level.Value - 1) * perLevel + Epsilon) : 0;
            var raw = basis + points + levelBonus;
            var cap = row["cap"];
            var value = cap == null || cap.Type == JTokenType.Null ? raw : Math.Min(raw, (double)cap);
            return new JObject { ["id"] = id, ["weights"] = weights, ["terms"] = terms, ["points"] = points, ["base"] = basis, ["perLevel"] = perLevel,
                ["level"] = level.HasValue ? new JValue(level.Value) : JValue.CreateNull(), ["levelBonus"] = levelBonus, ["raw"] = raw, ["cap"] = cap == null ? JValue.CreateNull() : cap.DeepClone(), ["value"] = value };
        }
        private static double Scalar(JObject row, string field, JObject classDefinition)
        {
            if (row[field] is JObject reference)
            {
                if ((string)reference["strategy"] != "classField") throw new ArgumentException("Unknown derived value strategy.");
                return FormulaEvaluator.Number(classDefinition, (string)reference["field"]);
            }
            return FormulaEvaluator.Number(row, field);
        }
        private static void Apply(JObject target, JObject patch) { foreach (var property in patch.Properties()) target[property.Name] = property.Value.DeepClone(); }
    }
}
