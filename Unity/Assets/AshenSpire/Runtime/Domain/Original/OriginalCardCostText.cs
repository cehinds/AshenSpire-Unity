// OriginalCardCostText.cs — shared, read-only card cost labels for solo and co-op.
// INPUT: a resolved CardMechanics.CostProfile and player energy/mana/stamina.
// MODIFY: presentation wording here; authored costs and payment remain in their
// existing domain components. X cards require no minimum action balance.
// Shortage returns null when resource-affordable; it does not validate targets,
// turn ownership, unplayable tags, or other command rules. No state is mutated.
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalCardCostText
    {
        public static string Describe(JObject cost)
        {
            if (cost == null) throw new ArgumentNullException(nameof(cost));
            var action = Read(cost, "action");
            var mana = Read(cost, "mana");
            var stamina = Read(cost, "stamina");
            var variable = Variable(cost);
            if (!variable && action == 0 && mana == 0 && stamina == 0) return "Free";
            var parts = new List<string> { variable ? "X actions" : Actions(action) };
            if (mana > 0) parts.Add(Number(mana) + " MP");
            if (stamina > 0) parts.Add(Number(stamina) + " stamina");
            return string.Join(" · ", parts);
        }

        public static bool IsAffordable(JObject cost, JObject player) => Shortage(cost, player) == null;

        public static string Shortage(JObject cost, JObject player)
        {
            if (cost == null) throw new ArgumentNullException(nameof(cost));
            if (player == null) throw new ArgumentNullException(nameof(player));
            var action = Read(cost, "action");
            var mana = Read(cost, "mana");
            var stamina = Read(cost, "stamina");
            var energy = Read(player, "energy");
            var manaPool = Read(player, "mana");
            var staminaPool = Read(player, "stamina");
            var parts = new List<string>();
            if (!Variable(cost) && action > energy)
            {
                var deficit = action - energy;
                parts.Add(Number(deficit) + " more " + (deficit == 1 ? "action" : "actions"));
            }
            if (mana > manaPool) parts.Add(Number(mana - manaPool) + " MP");
            if (stamina > staminaPool) parts.Add(Number(stamina - staminaPool) + " stamina");
            return parts.Count == 0 ? null : "Need " + string.Join(", ", parts);
        }

        private static int Read(JObject value, string name) => CardMechanics.Nonnegative(value[name], name);
        private static bool Variable(JObject cost)
        {
            var value = cost["variable"];
            if (value == null || value.Type == JTokenType.Null) return false;
            if (value.Type != JTokenType.Boolean) throw new ArgumentException("Invalid variable card cost.");
            return (bool)value;
        }
        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Actions(int value) => Number(value) + (value == 1 ? " action" : " actions");
    }
}
