// OriginalRewardAvailability.cs — shared inventory-capacity receipt for rewards and shops.
// UI reads the refusal before offering a command; the mutation boundary checks it
// again. Capacity lives in authored balance, and ownership lives in the current run.
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public static class OriginalRewardAvailability
    {
        public static string Refusal(JObject content, JObject run, string kind, string id)
        {
            if (kind == "flask" && ((JArray)run["flasks"]).Count >= (int)content["balance"]["flaskSlots"]) return "Your utility flask slots are full.";
            if (kind == "relic" && ((JArray)run["relics"]).Values<string>().Contains(id)) return "You already carry this relic.";
            if (kind == "armament")
            {
                var loadout = (JObject)run["loadout"]; var storage = (JArray)loadout["storage"];
                if (((JObject)loadout["sets"]).Properties().SelectMany(p => ((JArray)p.Value).Values<string>()).Concat(storage.Values<string>()).Contains(id)) return "You already own this armament.";
                if (storage.Count >= (int)content["balance"]["equipment"]["storageSlots"]) return "Your armament storage is full.";
            }
            return null;
        }
    }
}
