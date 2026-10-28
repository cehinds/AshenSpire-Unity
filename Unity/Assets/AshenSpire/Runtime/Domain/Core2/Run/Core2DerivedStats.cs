// Core2DerivedStats.cs — the derived-stat rows a Core 2 run prices its pools with (ruleset 7, "the one formula").
//
// JS at the pin: src/model/derivedStats.js resolvedRuleRow / rowForClass / ruleWeights / deriveStatIncrease /
// levelBonus / statRowValue / deriveStat. The rule table comes from the run's own snapshot (Core2Run.DerivedStatRules,
// exported per class as JS createRunState builds it), so a run keeps the rules it was born under.
//
//   value = clamp(base + Σ floor(attribute × weight) + floor(perLevel × (level − 1)), min, max)
//
// Each attribute term is floored on its own (a weight of 0.125 adds nothing until the attribute reaches 8).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public static class Core2DerivedStats
    {
        private const double Epsilon = 1e-9;
        /// <summary>JS NON_WEIGHT_FIELDS: every key of a row that is not an attribute weight.</summary>
        private static readonly HashSet<string> NonWeightFields = new HashSet<string>(new[]
        {
            "base", "sourceStat", "pointsPerTier", "gainPerTier", "rounding", "cap", "perLevel", "min", "max", "attributeBaseline", "byClass",
            "pointsPerIncrease", "gain", "perLevelEvery", "pointsBaseline", "multiplier",
        }, StringComparer.Ordinal);

        private static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);

        /// <summary>JS resolvedRuleRow(table, statId, classId): the table's defaults under the row, normalized, the class's form.</summary>
        public static JObject Row(JObject table, string statId, string classId = null)
        {
            if (!(table?["rules"]?[statId] is JObject row)) return null;
            var merged = new JObject();
            foreach (var p in ((table["defaults"] as JObject) ?? new JObject()).Properties()) merged[p.Name == "pointsPerTier" ? "pointsPerIncrease" : p.Name] = p.Value.DeepClone();
            foreach (var p in row.Properties()) merged[p.Name] = p.Value.DeepClone();
            var normal = Normalize(merged);
            if (!(normal["byClass"] is JToken byClass)) return normal;
            normal.Remove("byClass");
            if (classId == null || !(byClass[classId] is JObject own)) return normal;
            var bounds = new JObject();
            foreach (var p in normal.Properties()) if (NonWeightFields.Contains(p.Name)) bounds[p.Name] = p.Value;
            foreach (var p in own.Properties()) bounds[p.Name] = p.Value.DeepClone();
            return bounds;
        }

        /// <summary>JS normalizeRule: the retired spellings (sourceStat, pointsPerTier, gainPerTier, perLevel {every, gain}).</summary>
        private static JObject Normalize(JObject row)
        {
            var output = new JObject();
            foreach (var p in row.Properties())
                if (p.Name != "sourceStat" && p.Name != "pointsPerTier" && p.Name != "gainPerTier" && p.Name != "perLevel") output[p.Name] = p.Value;
            if (row["sourceStat"]?.Type == JTokenType.String) output[(string)row["sourceStat"]] = 1;
            if (row["pointsPerTier"] != null) output["pointsPerIncrease"] = row["pointsPerTier"];
            if (row["gainPerTier"] != null) output["gain"] = row["gainPerTier"];
            if (row["perLevel"] is JObject old) { output["perLevel"] = old["gain"]; output["perLevelEvery"] = old["every"]; }
            else if (row["perLevel"] != null) output["perLevel"] = row["perLevel"];
            return output;
        }

        /// <summary>JS ruleWeights: every non-zero numeric field that is not a carrier, in authored order.</summary>
        public static List<KeyValuePair<string, double>> Weights(JObject row) => row.Properties()
            .Where(p => !NonWeightFields.Contains(p.Name) && IsNumber(p.Value) && (double)p.Value != 0)
            .Select(p => new KeyValuePair<string, double>(p.Name, (double)p.Value)).ToList();

        private static double RoundBy(string rounding, double value)
        {
            switch (rounding ?? "floor")
            {
                case "floor": return Math.Floor(value);
                case "ceil": return Math.Ceiling(value);
                case "round": return Math.Floor(value + 0.5);
                case "trunc": return Math.Truncate(value);
                default: throw new InvalidDataException("rounding '" + rounding + "' is not executable");
            }
        }

        /// <summary>JS levelBonus(row, level).</summary>
        public static double LevelBonus(JObject row, int level)
        {
            if (level <= 1 || !IsNumber(row["perLevel"])) return 0;
            var term = (double)row["perLevel"];
            if (term == 0) return 0;
            if (row["perLevelEvery"]?.Type == JTokenType.Integer && (int)row["perLevelEvery"] > 0) return Math.Floor((level - 1) / (double)(int)row["perLevelEvery"]) * term;
            var raw = (level - 1) * term;
            var rounding = (string)row["rounding"];
            return rounding == null || rounding == "floor" ? Math.Floor(raw + Epsilon) : RoundBy(rounding, raw);
        }

        /// <summary>JS statRowValue(row, { attributes, level }).value.</summary>
        public static double Value(JObject row, IReadOnlyDictionary<string, int> attributes, int? level, string statId = "derivedStat")
        {
            if (row == null) throw new InvalidDataException("Derived stat '" + statId + "' has no row");
            var pointsPerIncrease = IsNumber(row["pointsPerIncrease"]) ? (double)row["pointsPerIncrease"] : 1;
            if (pointsPerIncrease <= 0) throw new InvalidDataException("pointsPerIncrease must be > 0");
            double points = 0;
            var baseline = IsNumber(row["attributeBaseline"]) ? (double)row["attributeBaseline"] : 0;
            foreach (var pair in Weights(row))
            {
                if (attributes == null || !attributes.TryGetValue(pair.Key, out var value)) throw new InvalidDataException("attribute '" + pair.Key + "' is not a finite number");
                var counted = baseline > 0 ? Math.Max(0, value - baseline) : value;
                points += Math.Floor(counted * pair.Value + Epsilon);
            }
            var gain = row["gain"] == null ? 1 : IsNumber(row["gain"]) ? (double)row["gain"] : throw new InvalidDataException(statId + ".gain must be a number in a Core 2 snapshot");
            var counted2 = IsNumber(row["pointsBaseline"]) && (double)row["pointsBaseline"] > 0 ? Math.Max(0, points - (double)row["pointsBaseline"]) : points;
            var scaled = IsNumber(row["multiplier"]) ? Math.Floor(counted2 * (double)row["multiplier"] + Epsilon) : counted2;
            var tier = pointsPerIncrease == 1 ? scaled : RoundBy((string)row["rounding"], scaled / pointsPerIncrease);
            var basis = IsNumber(row["base"]) ? (double)row["base"] : throw new InvalidDataException(statId + ".base must be a number in a Core 2 snapshot");
            var raw = basis + tier * gain + (level.HasValue ? LevelBonus(row, level.Value) : 0);
            var value2 = row["cap"] != null && IsNumber(row["cap"]) ? Math.Min(raw, (double)row["cap"]) : raw;
            if (IsNumber(row["min"])) value2 = Math.Max((double)row["min"], value2);
            if (IsNumber(row["max"])) value2 = Math.Min((double)row["max"], value2);
            return value2;
        }

        /// <summary>JS deriveStat(rules, statId, { attributes, classDef, level }).value as an integer pool.</summary>
        public static int Derive(JObject rules, string statId, IReadOnlyDictionary<string, int> attributes, int level, string classId = null)
        {
            var row = Row(rules, statId, classId) ?? throw new InvalidDataException("Unknown derived stat '" + statId + "'");
            return (int)Math.Floor(Value(row, attributes, level, statId) + 1e-12);
        }
    }
}
