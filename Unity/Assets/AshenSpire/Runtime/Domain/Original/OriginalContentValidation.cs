// OriginalContentValidation.cs — cross-table authoring checks before content is accepted.
// Add new reference contracts here when introducing a new authored relationship.
// Errors identify the table/property. This validates data; it never grants an item
// or implements an effect. Runtime command tests remain required for new mechanics.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public static class OriginalContentValidation
    {
        public static void ValidateReferences(JObject data)
        {
            var ids = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var table in new[] { "cards", "classes", "enemies", "relics", "unlocks", "equipment.armaments", "equipment.startingKits", "equipment.slots" })
            {
                var rows = data.SelectToken(table) as JArray ?? throw new ArgumentException("Missing table: " + table);
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in rows)
                    if (!(row is JObject) || row["id"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)row["id"]) || !set.Add((string)row["id"]))
                        throw new ArgumentException("Duplicate or invalid ID at " + row.Path);
                ids[table] = set;
            }
            void Ref(JToken value, string table, bool optional = false)
            {
                if (optional && (value == null || value.Type == JTokenType.Null || (value.Type == JTokenType.String && (string)value == ""))) return;
                if (value?.Type != JTokenType.String || !ids[table].Contains((string)value)) throw new ArgumentException("Missing " + table + " reference at " + (value?.Path ?? "required field") + ": " + value);
            }
            foreach (var row in data["encounters"])
            {
                if (!(row["enemies"] is JArray enemies) || enemies.Count == 0) throw new ArgumentException("Encounter needs enemies: " + row["id"]);
                foreach (var id in enemies) Ref(id, "enemies");
            }
            foreach (var hero in data["classes"])
            {
                Ref(hero["startingRelic"], "relics"); Ref(hero["startingSignatureCard"], "cards");
                foreach (var id in hero["eligibleStartingKitIds"] ?? new JArray()) Ref(id, "equipment.startingKits");
            }
            foreach (var kit in data["equipment"]["startingKits"])
            {
                Ref(kit["classId"], "classes"); Ref(kit["leftHand"], "equipment.armaments", true); Ref(kit["rightHand"], "equipment.armaments", true);
            }
            var armourKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var armour in data["equipment"]["armour"])
            {
                Ref(armour["classId"], "classes");
                if (armour["id"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)armour["id"]) || !armourKeys.Add((string)armour["classId"] + "/" + (string)armour["id"])) throw new ArgumentException("Duplicate or invalid armour identity at " + armour.Path);
                Ref(armour["unlock"], "unlocks", true);
            }
            foreach (var armament in data["equipment"]["armaments"])
            {
                Ref(armament["unlock"], "unlocks", true);
                foreach (var profile in new[] { "attackProfile", "guardProfile", "techniqueProfile" })
                    if (armament[profile]?.Type == JTokenType.String && !data["equipment"]["basicCardProfiles"].Any(row => (string)row["id"] == (string)armament[profile])) throw new ArgumentException("Unknown basic card profile at " + armament[profile].Path);
            }
        }
    }
}
