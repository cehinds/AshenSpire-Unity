using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    // Only public outcomes leave the simulation. Never expose future cards,
    // hidden pile order, RNG state or other players' hands in a preview.
    public static class OriginalCardPreview
    {
        public static bool Random(JObject card) => card["effects"]?.ToString().IndexOf("random", StringComparison.OrdinalIgnoreCase) >= 0 || card["effects"]?.ToString().IndexOf("chance", StringComparison.OrdinalIgnoreCase) >= 0;
        public static JObject Refused(string reason) => new JObject { ["available"] = false, ["reason"] = reason };
        public static JObject Variable() => Refused("Random effects resolve when played.");
        public static JObject Compare(IEnumerable<JObject> before, IEnumerable<JObject> after)
        {
            var final = after.ToDictionary(e => (string)e["id"]);
            var changes = new JArray();
            foreach (var entity in before)
            {
                if (!final.TryGetValue((string)entity["id"], out var next)) continue;
                var values = new JArray();
                foreach (var key in new[] { "hp", "block", "energy", "mana", "stamina" })
                    if ((int?)entity[key] != (int?)next[key]) values.Add(new JObject { ["key"] = key, ["before"] = entity[key]?.DeepClone(), ["after"] = next[key]?.DeepClone() });
                var statuses = new JArray();
                var ids = (entity["statuses"] as JObject ?? new JObject()).Properties().Select(p => p.Name).Concat((next["statuses"] as JObject ?? new JObject()).Properties().Select(p => p.Name)).Distinct();
                foreach (var id in ids)
                    if (StatusSystem.Stacks(entity, id) != StatusSystem.Stacks(next, id)) statuses.Add(new JObject { ["id"] = id, ["before"] = StatusSystem.Stacks(entity, id), ["after"] = StatusSystem.Stacks(next, id) });
                if (values.Count + statuses.Count > 0) changes.Add(new JObject { ["id"] = entity["id"].DeepClone(), ["values"] = values, ["statuses"] = statuses });
            }
            return new JObject { ["available"] = true, ["changes"] = changes };
        }
    }
}
