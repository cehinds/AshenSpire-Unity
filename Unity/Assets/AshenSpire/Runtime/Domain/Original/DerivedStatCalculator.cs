// DerivedStatCalculator.cs — attribute-tier rules, shared by creation and previews.
// Defaults -> authored row -> ordered override layers. Save resolved rules with a
// run so later balance edits cannot change the meaning of its allocation.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class DerivedStatCalculator
    {
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
            foreach (var row in resolved.Properties().Select(x => (JObject)x.Value))
            {
                if (FormulaEvaluator.Number(row, "pointsPerTier") <= 0) throw new ArgumentException("pointsPerTier must be positive.");
                if (!new[] { "floor", "ceil", "round" }.Contains((string)row["rounding"])) throw new ArgumentException("Unknown tier rounding.");
            }
            return resolved;
        }
        public static JObject Receipt(JObject rules, string id, JObject attributes, JObject classDefinition)
        {
            var row = rules[id] as JObject ?? throw new ArgumentException("Unknown derived stat: " + id);
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
