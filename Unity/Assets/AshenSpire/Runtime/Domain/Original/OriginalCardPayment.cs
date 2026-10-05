using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    // Published test 898: action is a compatibility name for the same Stamina
    // charge. Keep this separate from wallets belonging to frozen older runs.
    public static class OriginalCardPayment
    {
        public static JObject Profile(JObject definition, int powerCostReduction = 0, JObject weightClass = null)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (powerCostReduction < 0) throw new ArgumentOutOfRangeException(nameof(powerCostReduction));
            var effects = definition["effects"] as JArray ?? new JArray();
            if (weightClass != null && effects.Count > 0 && effects.All(e => (string)e["op"] == "dodgeRoll"))
            {
                var dodge = CardMechanics.Nonnegative(weightClass["dodgeStaminaCost"], "dodge Stamina cost");
                return new JObject { ["action"] = dodge, ["mana"] = 0, ["stamina"] = dodge, ["variable"] = false, ["classPriced"] = true };
            }
            var view = CardMechanics.FromDefinition(definition, true);
            var modifiers = new JArray();
            if (powerCostReduction > 0 && CardMechanics.HasProperty(view, "classification.power"))
                modifiers.Add(new JObject { ["resource"] = "action", ["delta"] = -powerCostReduction });
            var entries = (JArray)CardMechanics.CompileCosts(view, modifiers)["entries"];
            int Amount(string resource) => (int?)entries.FirstOrDefault(e => (string)e["resource"] == resource)?["amount"] ?? 0;
            var action = Amount("action");
            var variable = (bool?)((JArray)view["properties"]).FirstOrDefault(p => (string)p["propertyId"] == "cost.action")?["parameters"]?["variable"] ?? false;
            return new JObject { ["action"] = action, ["mana"] = Amount("mana"), ["stamina"] = action, ["variable"] = variable };
        }
        // Validate the target and choice before calling this commit. A refused
        // play cannot change either resource or the spend counter.
        public static JObject TryPay(JObject player, JObject profile, bool confirmed = true)
        {
            var stamina = CardMechanics.Nonnegative(player?["stamina"], "Stamina");
            var mana = CardMechanics.Nonnegative(player?["mana"], "Mana");
            var action = CardMechanics.Nonnegative(profile?["action"], "action cost");
            var authoredStamina = CardMechanics.Nonnegative(profile?["stamina"], "Stamina cost");
            if (action != authoredStamina) throw new ArgumentException("Current action and Stamina costs must alias.");
            var spend = (bool?)profile["variable"] == true ? stamina : action;
            var manaSpend = CardMechanics.Nonnegative(profile["mana"], "Mana cost");
            var prior = CardMechanics.Nonnegative(player["staminaSpentThisTurn"] ?? 0, "Stamina spend counter");
            if (!confirmed || spend > stamina || manaSpend > mana) return null;
            var total = checked(prior + spend);
            player["stamina"] = stamina - spend; player["energy"] = stamina - spend;
            player["mana"] = mana - manaSpend; player["staminaSpentThisTurn"] = total;
            return new JObject { ["energySpent"] = spend, ["staminaSpent"] = spend, ["manaSpent"] = manaSpend };
        }
    }
}
