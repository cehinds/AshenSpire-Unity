// OriginalGameplayOptions.cs — threads the US-15.2 gameplay settings into a run, matching the HTML.
// The HTML game reads these from meta.settings while it plays: shop.js reads shopSell,
// loadout.resolveSwapCostRule reads swapCostRule, reward.js collectMode reads rewardCollect.
// Unity runs read run.profileMeta.settings (OriginalRunServices.Sellables, OriginalCombatEquipment.Rule),
// so ProfileSettings is merged in there by OriginalGameSession.ApplyProfileSettings whenever a run is
// bound (new or continued). rewardCollect is resolved at Continue (OriginalGameSession.ContinueRewards(mode)).
// Swap price display: DescribeSwapPrice turns an OriginalCombatEquipment.Price receipt into one line.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalGameplayOptions
    {
        /// <summary>The run-facing gameplay keys, named exactly as the HTML meta.settings keys.</summary>
        public static JObject ProfileSettings(OriginalPlayerSettings settings)
        {
            var s = settings ?? new OriginalPlayerSettings();
            return new JObject { ["shopSell"] = s.ShopSell, ["swapCostRule"] = s.SwapCostRule };
        }

        /// <summary>reward.js collectMode: the wanted mode when the content's balance.ui.rewardCollect.modes
        /// lists it, otherwise its def. Content without the dial falls back to { def: auto, modes: [auto, manual] }.</summary>
        public static string RewardCollectMode(JObject content, string wanted)
        {
            var dial = content?["balance"]?["ui"]?["rewardCollect"] as JObject;
            var modes = (dial?["modes"] as JArray)?.Values<string>().ToArray() ?? OriginalPlayerSettings.RewardCollectModes;
            var fallback = (string)dial?["def"] ?? OriginalPlayerSettings.DefaultRewardCollect;
            return wanted != null && modes.Contains(wanted) ? wanted : fallback;
        }

        /// <summary>One line naming the live rule and how it reached the price, e.g.
        /// "Weapon swap cost · Weapon category: heavy weapon, 3 actions".</summary>
        public static string DescribeSwapPrice(JObject receipt, JObject equipmentBalance)
        {
            if (receipt == null) return "";
            var ruleId = (string)receipt["ruleId"];
            var row = (equipmentBalance?["swapCostRules"] as JArray)?.OfType<JObject>().FirstOrDefault(r => (string)r["id"] == ruleId);
            var label = (string)row?["label"] ?? ruleId ?? "Flat";
            var cost = (int)receipt["cost"]; var baseCost = (int)receipt["baseCost"];
            var parts = new System.Collections.Generic.List<string>();
            if ((string)receipt["base"] == "category")
                parts.Add(receipt["categoryTag"]?.Type == JTokenType.String ? (string)receipt["categoryTag"] + " weapon " + baseCost : "no category, base " + baseCost);
            else parts.Add("base " + baseCost);
            var gearDelta = (int?)receipt["gearDelta"] ?? 0; var gearIgnored = (int?)receipt["gearIgnored"] ?? 0;
            if ((bool?)receipt["gearOn"] == true && gearDelta != 0) parts.Add("gear " + Signed(gearDelta));
            if (gearIgnored != 0) parts.Add("gear " + Signed(gearIgnored) + " ignored by this rule");
            if ((bool?)receipt["floored"] == true) parts.Add("floored at 0");
            return "Weapon swap cost · " + label + ": " + string.Join(", ", parts) + " → " + cost + (cost == 1 ? " action" : " actions");
        }
        private static string Signed(int value) => value > 0 ? "+" + value : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
