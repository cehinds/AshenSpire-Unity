// ItemUpgradeService.cs — authored item/tier changes and Smithing Stone transactions.
// Edit equipment.itemUpgradeChanges and balance.smithing in Original/content.json.
// Levels belong to namespaced items, never card copies. ResolveCard applies all
// authored tiers after equipment profile/mod projection. Commit returns a new run
// and a receipt; callers replace their run only after this method succeeds.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class ItemUpgradeService
    {
        public const string StoneCostTag = "upgrade:cost:smithing-stone";
        private readonly OriginalContentCatalog _catalog;
        public ItemUpgradeService(OriginalContentCatalog catalog) { _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); }
        public JObject Definition(string itemRef)
        {
            var parts = itemRef?.Split('/') ?? Array.Empty<string>();
            if (parts.Length == 2 && parts[0] == "armament") return _catalog.Record("equipment.armaments", parts[1]);
            if (parts.Length == 3 && parts[0] == "armor") return _catalog.Record("equipment.armour", parts[2], parts[1]);
            if (parts.Length == 2 && parts[0] == "relic") return _catalog.Record("relics", parts[1]);
            throw new ArgumentException("Invalid namespaced upgrade item: " + itemRef);
        }
        public JArray Rows(string itemRef, int nextTier) => new JArray(_catalog.Table("equipment.itemUpgradeChanges").Where(x => (string)x["itemRef"] == itemRef && (int)x["nextTier"] == nextTier));
        public int MaximumTier(string itemRef) { Definition(itemRef); return _catalog.Table("equipment.itemUpgradeChanges").Where(x => (string)x["itemRef"] == itemRef).Select(x => (int)x["nextTier"]).DefaultIfEmpty(0).Max(); }
        private void ValidateLevel(string itemRef, int level) { if (level < 0 || level > MaximumTier(itemRef)) throw new ArgumentException("Item level exceeds authored tiers: " + itemRef); }
        public double RequirementDelta(string itemRef, string attributeId, int level)
        { ValidateLevel(itemRef, level); return Enumerable.Range(1, level).Sum(t => Rows(itemRef, t).Where(x => (string)x["tag"] == "requirement:" + attributeId).Sum(x => (double)x["value"])); }
        public JObject ResolveItem(string itemRef, int level)
        {
            ValidateLevel(itemRef, level); var item = Definition(itemRef); var family = itemRef.Split('/')[0];
            for (var tier = 1; tier <= level; tier++) foreach (var row in Rows(itemRef, tier))
            {
                var tag = (string)row["tag"]; if (tag == StoneCostTag) continue;
                if (family == "armor" && tag == "equipment:poise-threshold") item["poiseThreshold"] = Nonnegative(item["poiseThreshold"], row["value"], tag);
                else if (family == "relic" && new[] { "relic:passive:poise-threshold-add", "relic:passive:power-cost-reduction" }.Contains(tag))
                { var key = tag.EndsWith("poise-threshold-add", StringComparison.Ordinal) ? "poiseThresholdAdd" : "powerCostReduction"; item["passives"][key] = Nonnegative(item["passives"]?[key], row["value"], tag); }
                else if (family != "armament" || !tag.StartsWith("card:", StringComparison.Ordinal) && !tag.StartsWith("requirement:", StringComparison.Ordinal)) throw new ArgumentException("Upgrade tag is not valid for item: " + tag);
            }
            return item;
        }
        private static double Nonnegative(JToken before, JToken delta, string field)
        {
            if (before == null || before.Type != JTokenType.Integer && before.Type != JTokenType.Float || delta == null || delta.Type != JTokenType.Integer && delta.Type != JTokenType.Float) throw new ArgumentException("Upgrade targets nonnumeric field: " + field);
            var result = (double)before + (double)delta;
            if (double.IsNaN(result) || double.IsInfinity(result) || result < 0) throw new ArgumentException("Upgrade would produce invalid value: " + field);
            return result;
        }
        public JObject ResolveCard(JObject definition, string role, string itemRef, int level)
        {
            ValidateLevel(itemRef, level);
            if (!itemRef.StartsWith("armament/", StringComparison.Ordinal)) throw new ArgumentException("Only armaments upgrade role cards.");
            var card = (JObject)definition.DeepClone();
            for (var tier = 1; tier <= level; tier++)
            {
                var changed = false;
                foreach (var row in Rows(itemRef, tier))
                {
                    var parts = ((string)row["tag"]).Split(':');
                    if (parts.Length != 4 || parts[0] != "card" || parts[1] != role) continue;
                    if (parts[2] == "effect" && new[] { "damage", "block", "draw", "discard" }.Contains(parts[3]))
                    {
                        var effects = card["effects"].Where(x => (string)x["op"] == parts[3]).ToArray();
                        if (effects.Length != 1) throw new ArgumentException("Upgrade requires exactly one effect: " + row["tag"]);
                        effects[0]["amount"] = Nonnegative(effects[0]["amount"], row["value"], (string)row["tag"]); changed = true;
                    }
                    else if (parts[2] == "cost" && new[] { "action", "mana", "stamina" }.Contains(parts[3]))
                    { var field = parts[3] == "action" ? "cost" : parts[3] + "Cost"; card[field] = Nonnegative(card[field] ?? new JValue(0), row["value"], field); changed = true; }
                    else throw new ArgumentException("Unknown item card upgrade tag: " + row["tag"]);
                }
                if (changed) card["name"] = ((string)card["name"] ?? "").TrimEnd('+') + "+";
            }
            return card;
        }
        public static string[] CarriedArmaments(JObject loadout)
        {
            var ids = new List<string>();
            foreach (var field in new[] { "rightHand", "leftHand" }) foreach (var id in loadout["sets"]?[field] ?? new JArray()) if (!string.IsNullOrEmpty((string)id)) ids.Add((string)id);
            ids.AddRange((loadout["storage"] ?? new JArray()).Select(x => (string)x)); return ids.Distinct(StringComparer.Ordinal).ToArray();
        }
        public string[] OwnedRefs(JObject run)
        {
            var refs = new List<string>(); var locations = new WeaponLoadout(_catalog);
            refs.AddRange(CarriedArmaments((JObject)run["loadout"]).Select(x => "armament/" + x));
            foreach (var item in locations.Pieces((JObject)run["loadout"], ((string)run["class"] ?? (string)run["classId"])).OfType<JObject>()) if ((string)item["kind"] == "armor") refs.Add(WeaponLoadout.ItemRef(item));
            refs.AddRange((run["relics"] ?? run["relicIds"] ?? new JArray()).Select(x => "relic/" + (string)x)); return refs.Distinct(StringComparer.Ordinal).ToArray();
        }
        public static int Stones(JObject run)
        { var value = run["smithingStones"]; if (value == null) return 0; if (value.Type != JTokenType.Integer || (int)value < 0) throw new ArgumentException("Smithing Stones must be a nonnegative integer."); return (int)value; }
        public JObject Plan(JObject run, string itemRef)
        {
            var item = Definition(itemRef);
            if (!OwnedRefs(run).Contains(itemRef)) throw new ArgumentException("Item is not carried or worn: " + itemRef);
            var level = (int?)run["itemUpgradeLevels"]?[itemRef] ?? 0; ValidateLevel(itemRef, level);
            var rows = Rows(itemRef, level + 1);
            var costs = rows.Where(x => (string)x["tag"] == StoneCostTag).ToArray();
            if (costs.Length != 1 || costs[0]["value"].Type != JTokenType.Integer || (int)costs[0]["value"] < 0) throw new ArgumentException("No valid next authored item tier.");
            var changes = new JArray(rows.Where(x => (string)x["tag"] != StoneCostTag));
            if (changes.Count == 0 || changes.All(x => (double)x["value"] == 0)) throw new ArgumentException("Authored tier has no effect.");
            // Validate the exact next tier before offering or charging for it.
            ResolveItem(itemRef, level + 1);
            if (itemRef.StartsWith("armament/", StringComparison.Ordinal))
            {
                var previewLoadout = (JObject)run["loadout"].DeepClone();
                previewLoadout["sets"]["rightHand"][0] = JValue.CreateNull(); previewLoadout["sets"]["leftHand"][0] = JValue.CreateNull();
                previewLoadout["active"]["rightHand"] = 0; previewLoadout["active"]["leftHand"] = 0;
                previewLoadout["sets"][(string)item["hand"] == "left" ? "leftHand" : "rightHand"][0] = item["id"].DeepClone();
                var projector = new WeaponCardProjection(_catalog);
                foreach (var role in new[] { "attack", "guard", "technique" })
                {
                    var profileId = (string)item[role + "Profile"]; if (string.IsNullOrEmpty(profileId)) continue;
                    var profile = _catalog.Record("equipment.basicCardProfiles", profileId);
                    var instance = new JObject { ["cardId"] = profile["baseCardId"].DeepClone(), ["equipmentRole"] = role, ["profileId"] = profileId, ["weaponId"] = item["id"].DeepClone(), ["sourceArmamentId"] = item["id"].DeepClone(), ["smithingLevel"] = level + 1 };
                    projector.Resolve(instance, previewLoadout, ((string)run["class"] ?? (string)run["classId"]), run["attributes"] as JObject ?? throw new ArgumentException("Upgrade preview needs attributes."));
                }
            }
            var stones = Stones(run); var cost = (int)costs[0]["value"];
            return new JObject { ["itemRef"] = itemRef, ["itemName"] = item["name"].DeepClone(), ["currentLevel"] = level, ["nextLevel"] = level + 1, ["cost"] = cost, ["stones"] = stones, ["shortfall"] = Math.Max(0, cost - stones), ["affordable"] = stones >= cost, ["authoredChanges"] = changes };
        }
        public JObject Commit(JObject run, string itemRef, bool free = false)
        {
            var plan = Plan(run, itemRef); if (!free && !(bool)plan["affordable"]) throw new ArgumentException("Insufficient Smithing Stones.");
            var next = (JObject)run.DeepClone(); if (!(next["itemUpgradeLevels"] is JObject)) next["itemUpgradeLevels"] = new JObject();
            next["itemUpgradeLevels"][itemRef] = plan["nextLevel"].DeepClone(); next["smithingStones"] = (int)plan["stones"] - (free ? 0 : (int)plan["cost"]);
            RestampCards(next);
            var receipt = new JObject { ["schemaVersion"] = 3, ["itemRef"] = itemRef, ["itemName"] = plan["itemName"].DeepClone(), ["beforeLevel"] = plan["currentLevel"].DeepClone(), ["afterLevel"] = plan["nextLevel"].DeepClone(), ["authoredCost"] = plan["cost"].DeepClone(), ["spent"] = free ? 0 : (int)plan["cost"], ["stoneBalanceBefore"] = plan["stones"].DeepClone(), ["stoneBalanceAfter"] = next["smithingStones"].DeepClone(), ["free"] = free, ["authoredChanges"] = plan["authoredChanges"].DeepClone() };
            next["lastSmithingReceipt"] = receipt.DeepClone(); return new JObject { ["run"] = next, ["receipt"] = receipt };
        }
        public void RestampCards(JObject run)
        {
            var composer = new WeaponCardComposer(_catalog);
            foreach (var card in (run["deck"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var role = (string)card["equipmentRole"];
                if (!new[] { "attack", "guard", "technique" }.Contains(role)) continue;
                var id = role == "attack" ? (string)card["weaponId"] : (string)composer.RoleSource((JObject)run["loadout"], ((string)run["class"] ?? (string)run["classId"]), role)["piece"]?["id"];
                if (string.IsNullOrEmpty(id)) { card.Remove("sourceArmamentId"); card.Remove("smithingLevel"); continue; }
                card["sourceArmamentId"] = id; card["smithingLevel"] = (int?)run["itemUpgradeLevels"]?["armament/" + id] ?? 0; card["upgraded"] = false;
            }
        }
        public JObject GrantReward(JObject run, string pool, string rewardId)
        {
            if (string.IsNullOrEmpty(rewardId)) throw new ArgumentException("Reward claim needs an ID.");
            var amount = _catalog.Data()["balance"]["smithing"]["rewardByPool"]?[pool];
            if (amount?.Type != JTokenType.Integer || (int)amount < 0) throw new ArgumentException("Unknown Smithing reward pool.");
            var next = (JObject)run.DeepClone(); var duplicate = (run["smithingRewardClaims"] ?? new JArray()).Any(x => (string)x == rewardId);
            if (!duplicate) { next["smithingStones"] = checked(Stones(run) + (int)amount); if (!(next["smithingRewardClaims"] is JArray)) next["smithingRewardClaims"] = new JArray(); ((JArray)next["smithingRewardClaims"]).Add(rewardId); }
            return new JObject { ["run"] = next, ["receipt"] = new JObject { ["pool"] = pool, ["rewardId"] = rewardId, ["amount"] = duplicate ? 0 : (int)amount, ["duplicate"] = duplicate, ["stoneBalanceAfter"] = Stones(next) } };
        }
    }
}
