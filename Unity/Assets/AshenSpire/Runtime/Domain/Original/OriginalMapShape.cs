// OriginalMapShape.cs — per-run floor/column caps and relative node weights.
// Mirrors pinned floorplan.js applyRunShape; ActMapGenerator remains the only
// graph generator. Author limits in Original/custom-run-options.json/mapShape.
// Save selected shape plus limits with the run; restore never reads live limits.
// Caps cannot enlarge an act. Zero roll weight does not remove forced/fallback nodes.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalMapShape
    {
        public static JObject Normalize(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return null;
            if (!(value is JObject shape)) throw new ArgumentException("Map shape must be an object.");
            if (shape.Properties().Any(p => !new[] { "floors", "columns", "typeWeights" }.Contains(p.Name))) throw new ArgumentException("Unknown map-shape setting.");
            foreach (var key in new[] { "floors", "columns" })
                if (shape[key] != null && shape[key].Type != JTokenType.Null && (!Number(shape[key]) || (double)shape[key] != Math.Floor((double)shape[key]))) throw new ArgumentException(key + " cap must be a whole number.");
            if (shape["typeWeights"] != null && shape["typeWeights"].Type != JTokenType.Null && !(shape["typeWeights"] is JObject)) throw new ArgumentException("Node weights must be an object.");
            return shape.HasValues ? (JObject)shape.DeepClone() : null;
        }
        public static int MinimumFloors(JObject config)
        {
            for (var floors = 2; floors <= (int)config["floors"]; floors++)
            {
                var candidate = (JObject)config.DeepClone(); candidate["floors"] = floors;
                try { ActMapGenerator.Validate(candidate); return floors; } catch (ArgumentException) { }
            }
            throw new ArgumentException("No viable act length satisfies its authored floor rules.");
        }
        private static bool Number(JToken value) => value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) && !double.IsNaN((double)value) && !double.IsInfinity((double)value);
        public static JObject Apply(JObject config, JToken shapeValue, JObject limits)
        {
            var shape = Normalize(shapeValue); var next = (JObject)config.DeepClone(); var notes = new JArray();
            if (shape == null) return new JObject { ["config"] = next, ["notes"] = notes, ["changed"] = false };
            if (limits == null || !Number(limits["minColumns"]) || (int)limits["minColumns"] < 1 || !Number(limits["maxWeight"]) || (double)limits["maxWeight"] <= 0) throw new ArgumentException("Missing or invalid frozen map-shape limits.");
            var changed = false;
            foreach (var key in new[] { "floors", "columns" })
            {
                var cap = shape[key]; if (cap == null || cap.Type == JTokenType.Null) continue;
                var minimum = key == "floors" ? MinimumFloors(config) : (int)limits["minColumns"];
                if ((double)cap < minimum) throw new ArgumentException(key + " cap is below " + minimum + ".");
                var authored = (int)config[key]; var resolved = Math.Min(authored,(double)cap);
                if ((double)cap > authored) notes.Add(key + ": cap " + cap + " is above this act's " + authored + "; the act keeps " + authored + ".");
                next[key] = (int)resolved; changed |= resolved != authored;
            }
            if (shape["typeWeights"] is JObject weights)
            {
                var merged = next["typeWeights"] as JObject ?? throw new ArgumentException("Act has no node weights.");
                foreach (var row in weights.Properties())
                {
                    if (merged[row.Name] == null) throw new ArgumentException("Unknown node weight: " + row.Name);
                    if (!Number(row.Value) || (double)row.Value < 0 || (double)row.Value > (double)limits["maxWeight"]) throw new ArgumentException("Node weight must be between zero and " + limits["maxWeight"] + ": " + row.Name);
                    merged[row.Name] = row.Value.DeepClone();
                }
                if (merged.Properties().Sum(p => (double)p.Value) <= 0) throw new ArgumentException("At least one node weight must be positive.");
                changed = true; // Original creates a new weight object even for {}.
                if ((double?)merged["monster"] == 0) notes.Add("Monster 0 — a node whose every other type is barred by a floor rule or the no-repeat-neighbour ban still falls back to Monster, so Monsters do not reach zero.");
            }
            ActMapGenerator.Validate(next);
            if (changed)
                foreach (var type in new[] { "elite", "merchant" })
                {
                    var minimum = (int?)next["floorRules"][type == "elite" ? "minElites" : "minMerchants"] ?? 0;
                    if (minimum > 0 && (double?)next["typeWeights"][type] == 0) notes.Add(char.ToUpperInvariant(type[0]) + type.Substring(1) + " 0 — but this act promises at least " + minimum + " a map, so " + minimum + " are force-placed. Zero weight means never ROLLED, not never present.");
                }
            return new JObject { ["config"] = next, ["notes"] = notes, ["changed"] = changed };
        }
        public static JArray ResolveAll(JObject configs, JToken shape, JObject limits)
        {
            var result = new JArray();
            foreach (var act in configs.Properties().OrderBy(p => int.Parse(p.Name)))
            {
                try { var row = Apply((JObject)act.Value,shape,limits); row["act"] = act.Name; result.Add(row); }
                catch (ArgumentException error) { throw new ArgumentException("Act " + act.Name + ": " + error.Message,error); }
            }
            return result;
        }
        // Uses isolated probe seeds, never a player's RNG. Reports measured graph
        // density and missed minima honestly; this is not elapsed play time.
        public static JObject Sample(JObject config, int seeds)
        {
            if (seeds < 1 || seeds > 100) throw new ArgumentException("Map sample count outside 1–100.");
            var counts = new JArray(); var types = new JObject();
            for (var seed = 1; seed <= seeds; seed++)
            {
                var graph = ActMapGenerator.Generate(config,new RandomStreams(unchecked((uint)seed * 2654435761u))); var nodes = ((JObject)graph["nodes"]).Properties().Select(p => p.Value).ToArray(); counts.Add(nodes.Length);
                foreach (var group in nodes.GroupBy(n => (string)n["type"])) types[group.Key] = ((double?)types[group.Key] ?? 0) + group.Count();
            }
            foreach (var type in types.Properties().ToArray()) types[type.Name] = Math.Floor((double)type.Value / seeds * 100 + .5) / 100;
            return new JObject { ["seeds"] = seeds, ["nodes"] = new JObject { ["min"] = counts.Values<int>().Min(), ["max"] = counts.Values<int>().Max(), ["mean"] = Math.Floor(counts.Values<int>().Average() * 100 + .5) / 100 }, ["byType"] = types };
        }
    }
}
