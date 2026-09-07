// EquipmentRunModifiers.cs — data-owned equipment bonuses for character resources
// and combat startup. Edit equipment.modFields and piece mods; no class branches.
// Resolve walks slot order, so authored set modifiers replace preceding additions.
// MovePool preserves missing resources during equipment changes; it never heals
// the player merely because a larger resource vessel was equipped.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class EquipmentRunModifiers
    {
        private readonly OriginalContentCatalog _catalog;
        public EquipmentRunModifiers(OriginalContentCatalog catalog) { _catalog = catalog; }
        public JObject Resolve(JObject loadout, string classId)
        {
            var fields = _catalog.Data()["equipment"]["modFields"];
            var result = new JObject { ["maxHp"] = 0, ["maxMana"] = 0, ["maxStamina"] = 0, ["swapCostDelta"] = 0 };
            var statuses = new Dictionary<string, double>();
            foreach (var piece in new WeaponLoadout(_catalog).Pieces(loadout, classId)) foreach (var raw in piece["mods"] ?? new JArray())
            {
                var mod = WeaponCardProjection.Parse((string)raw) ?? throw new ArgumentException("Invalid equipment modifier: " + raw);
                var spec = fields[(string)mod["field"]]; if ((string)spec?["scope"] != "run") continue;
                var apply = (string)spec["apply"]; var amount = (double)mod["value"]; var add = (string)mod["mode"] == "add";
                if (new[] { "maxHp", "maxMana", "maxStamina", "swapCost" }.Contains(apply))
                { var field = apply == "swapCost" ? "swapCostDelta" : apply; result[field] = add ? (double)result[field] + amount : amount; }
                else if (apply == "startStatus")
                { var id = (string)spec["status"]; _catalog.Record("statuses", id); statuses.TryGetValue(id, out var previous); statuses[id] = add ? previous + amount : amount; }
                else throw new ArgumentException("Unknown run modifier application: " + apply);
            }
            result["startStatuses"] = new JArray(statuses.Where(x => x.Value > 0).Select(x => new JObject { ["status"] = x.Key, ["stacks"] = x.Value }));
            return result;
        }
        public static JObject MovePool(int oldMaximum, int current, int newMaximum, int? carriedDeficit = null)
        {
            if (oldMaximum < 0 || newMaximum < 0 || current < 0 || current > oldMaximum || carriedDeficit < 0) throw new ArgumentException("Invalid resource pool.");
            var observed = Math.Max(0, oldMaximum - current); var prior = carriedDeficit ?? observed;
            var deficit = checked(Math.Max(0, prior + observed - Math.Min(prior, oldMaximum)));
            return new JObject { ["maximum"] = newMaximum, ["current"] = Math.Max(0, newMaximum - deficit), ["deficit"] = deficit };
        }
    }
}
