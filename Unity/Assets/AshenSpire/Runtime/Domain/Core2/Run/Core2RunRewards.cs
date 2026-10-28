// Core2RunRewards.cs — Core 2 victory rewards, level rewards and shops: persisted mixed offers, take one.
//
// JS at the pin:
//   src/model/gameProfileRewards.js  pools (eligibility: class cards, owned basics skipped, reward groups of weapons and
//                                    armour, unowned relics, potions, the authored weapon art), makeOffer,
//                                    gameProfileRewardOffers, claimGameProfileReward, gameProfileShopStock,
//                                    purchaseGameProfileShopOffer
//   engine/kernel/rewardPolicy.js    pickRewardCategory (weighted groups: cards 60 / equipment and relics 10 / potions 30)
//   src/model/stanceCoverage.js      reserveStanceCards (a slot for a basic or cantrip card of a stance the character lacks)
//   src/model/gracerefill.js         flaskAcquisitionPlan / acquireFlask (a charge flask raises capacity; others take a slot)
//   src/model/collectedCards.js      recordCollectedCards (Core 2's collected-card ledger)
//
// An offer is rolled once per offer id and persisted; claiming never edits the deck (a won card lands in the sideboard).
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public static class Core2RunRewards
    {
        private static readonly Dictionary<string, string> StanceAlias = new Dictionary<string, string>(StringComparer.Ordinal) { ["prepared"] = "casting" };

        private static int CardRank(JObject card, JObject rule) =>
            card["rank"]?.Type == JTokenType.Integer ? (int)card["rank"] : (int?)rule?["rankOfRarity"]?[(string)card["rarity"] ?? ""] ?? 1;

        /// <summary>JS gameProfileRewards pools(registries, run): what each category may offer right now.</summary>
        public static Dictionary<string, List<string>> Pools(Core2RunData data, Core2Run run)
        {
            var content = data.Content;
            var rules = content.Rewards;
            var cardRule = rules["eligibility"]?["combatCards"] as JObject;
            var owned = new HashSet<string>(run.Owned.Select(c => c.CardId), StringComparer.Ordinal);
            var minRank = (int?)cardRule?["minRank"] ?? 0;
            var cards = content.Cards.Select(c => c.Raw).Where(card =>
                ((string)card["class"] == "colorless" || (string)card["class"] == run.Class) && (string)card["rarity"] != "special" && (string)card["rarity"] != "status"
                && (string)card["type"] != "status" && !((bool?)cardRule?["skipOwnedBasics"] == true && CardRank(card, cardRule) < minRank && owned.Contains((string)card["id"]))).ToList();
            var carried = new HashSet<string>(run.Loadout.Storage.Concat(run.Loadout.Sets.Values.SelectMany(v => v)).Where(id => id != null), StringComparer.Ordinal);
            var weapons = GroupMembers(rules, "weapons");
            var armor = GroupMembers(rules, "armor");
            var arts = Core2Json.Strings(rules["eligibility"]?["weaponArts"]?["cardIds"]);
            var includesMp = Core2Json.Strings(data.Module("resources")?["include"]).Contains("mp");
            var worn = new HashSet<string>(run.Loadout.Sets.Values.SelectMany(v => v).Where(id => id != null), StringComparer.Ordinal);
            return new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["weapons"] = content.Armaments.Where(a => weapons.Contains(a.Id) && !carried.Contains(a.Id)).Select(a => a.Id).ToList(),
                ["relics"] = content.Relics.Where(r => !run.Relics.Contains(r.Id)).Select(r => r.Id).ToList(),
                ["combatCards"] = cards.Select(c => (string)c["id"]).ToList(),
                ["combatCardsLow"] = cards.Where(c => CardRank(c, cardRule) < minRank).Select(c => (string)c["id"]).ToList(),
                ["weaponArts"] = arts.Distinct().ToList(),
                ["potions"] = content.Flasks.Where(f => includesMp || !((JArray)f.Raw["effects"] ?? new JArray()).OfType<JObject>().Any(e => (string)e["op"] == "restoreMana")).Select(f => f.Id).ToList(),
                ["armor"] = content.Armour.Where(a => armor.Contains((string)a.Raw["classId"] + ":" + a.Id) && (string)a.Raw["classId"] == run.Class
                    && !run.Loadout.BoughtArmour.Any(b => b.Value == a.Id) && !worn.Contains(a.Id)).Select(a => a.Id).ToList(),
            };
        }

        /// <summary>JS equipmentGroupMembers: the authored reward group members of a category.</summary>
        private static HashSet<string> GroupMembers(JObject rules, string category)
        {
            var output = new HashSet<string>(StringComparer.Ordinal);
            foreach (var groupId in Core2Json.Strings(rules["eligibility"]?[category]?["groups"]))
                foreach (var member in Core2Json.Strings(rules["equipmentGroups"]?[groupId]?["members"])) output.Add(member);
            return output;
        }

        /// <summary>JS pickRewardCategory: a weighted group among those with a category left, then a category in it.</summary>
        public static string PickCategory(Core2Rng rng, string stream, IReadOnlyList<string> categories, JArray groups)
        {
            if (categories.Count == 0) return null;
            if (groups == null || groups.Count == 0) return rng.Pick(stream, categories);
            var eligible = groups.OfType<JObject>().Select(g => (Weight: (double)g["weight"], Categories: Core2Json.Strings(g["categories"]).Where(categories.Contains).ToList()))
                .Where(g => g.Weight > 0 && g.Categories.Count > 0).ToList();
            if (eligible.Count == 0) return null;
            var roll = rng.Float(stream) * eligible.Sum(g => g.Weight);
            foreach (var group in eligible)
            {
                roll -= group.Weight;
                if (roll < 0) return rng.Pick(stream, group.Categories);
            }
            return rng.Pick(stream, eligible[eligible.Count - 1].Categories);
        }

        private static string StanceOfCard(Core2Content content, string id)
        {
            try { return Core2Stances.StanceOf(Core2Carrier.FromCard(content.Card(id)))?.Maneuver; }
            catch (Exception) { return null; }
        }

        /// <summary>JS missingBasicStances: the stances module's stances no owned card enters, in authored order.</summary>
        public static List<string> MissingBasicStances(Core2RunData data, Core2Run run)
        {
            var owned = new HashSet<string>(run.Owned.Select(c => StanceOfCard(data.Content, c.CardId)).Where(s => s != null), StringComparer.Ordinal);
            return Core2Json.Strings(data.Module("stances")?["include"]).Select(id => StanceAlias.TryGetValue(id, out var a) ? a : id).Distinct().Where(s => !owned.Contains(s)).ToList();
        }

        /// <summary>JS reserveStanceCards: up to reservedSlots basic or cantrip cards of distinct missing stances.</summary>
        public static List<string> ReserveStanceCards(Core2RunData data, Core2Run run, Core2Rng rng, string stream, IReadOnlyList<string> eligible, int limit)
        {
            var rule = data.Content.Rewards["eligibility"]?["combatCards"]?["stanceCoverage"] as JObject;
            if (rule == null || (bool?)rule["enabled"] != true || data.Module("stances") == null || data.Module("cards") == null || ((int?)rule["reservedSlots"] ?? 0) <= 0) return new List<string>();
            var slots = Math.Min((int)rule["reservedSlots"], limit);
            if (slots <= 0) return new List<string>();
            var exclude = Core2Json.Strings(rule["excludePools"]);
            var prefer = Core2Json.Strings(rule["preferTiers"]);
            var basic = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pool in ((JObject)data.Content.Table("pack/cardPools.json")).Properties())
            {
                if (exclude.Contains(pool.Name)) continue;
                foreach (var id in ((pool.Value["byTag"] as JObject) ?? new JObject()).Properties().Select(p => (string)p.Value)) basic.Add(id);
                foreach (var byTag in ((pool.Value["byClass"] as JObject) ?? new JObject()).Properties())
                    foreach (var id in ((byTag.Value as JObject) ?? new JObject()).Properties().Select(p => (string)p.Value)) basic.Add(id);
            }
            bool Cantrip(string id) => data.Content.Card(id).Tags.Contains("rank.cantrip");
            bool InTier(string id) => (prefer.Contains("basic") && basic.Contains(id)) || (prefer.Contains("cantrip") && Cantrip(id));
            var byStance = new List<KeyValuePair<string, List<string>>>();
            foreach (var stance in MissingBasicStances(data, run))
            {
                var ids = eligible.Where(id => InTier(id) && StanceOfCard(data.Content, id) == stance).ToList();
                if (ids.Count > 0) byStance.Add(new KeyValuePair<string, List<string>>(stance, ids));
            }
            var picked = new List<string>();
            while (picked.Count < slots && byStance.Count > 0)
            {
                var stance = rng.Pick(stream, byStance.Select(p => p.Key).ToList());
                var entry = byStance.First(p => p.Key == stance);
                picked.Add(rng.Pick(stream, entry.Value));
                byStance.Remove(entry);
            }
            return picked;
        }

        /// <summary>JS makeOffer: roll (once per offer id) and persist a mixed offer.</summary>
        private static Core2RewardOffer MakeOffer(Core2RunData data, Core2Run run, Core2Rng rng, string offerId, string source, int count, int takeCount,
            IReadOnlyList<string> categories, JArray groups, int? itemsPerCategory)
        {
            if (string.IsNullOrEmpty(offerId)) throw new ArgumentException("A stable reward offerId is required");
            if (run.RewardOffers.TryGetValue(offerId, out var existing)) return existing;
            var available = Pools(data, run);
            var options = new List<Core2RewardOption>();
            var stream = source == "shop" && itemsPerCategory.HasValue ? "shop" : "cardRewards";
            var shelf = new Dictionary<string, int>(StringComparer.Ordinal);
            var groupList = groups ?? new JArray();
            if (source != "shop" && categories.Contains("combatCards") && (groupList.Count == 0 || groupList.OfType<JObject>().Any(g => (double)g["weight"] > 0 && Core2Json.Strings(g["categories"]).Contains("combatCards"))))
                foreach (var id in ReserveStanceCards(data, run, rng, stream, available["combatCards"], count - options.Count))
                {
                    options.Add(new Core2RewardOption { Kind = "combatCards", Id = id });
                    available["combatCards"] = available["combatCards"].Where(v => v != id).ToList();
                }
            var rule = data.Content.Rewards["eligibility"]?["combatCards"] as JObject;
            var hundred = Enumerable.Range(0, 100).ToList();
            while (options.Count < count)
            {
                var kinds = categories.Where(kind => available.TryGetValue(kind, out var ids) && ids.Count > 0 && (!itemsPerCategory.HasValue || (shelf.TryGetValue(kind, out var n) ? n : 0) < itemsPerCategory.Value)).ToList();
                if (kinds.Count == 0) break;
                var kind = !itemsPerCategory.HasValue ? PickCategory(rng, stream, kinds, groupList) : kinds[0];
                if (kind == null) break;
                var pool = available[kind];
                if (kind == "combatCards" && rule != null)
                {
                    var low = new HashSet<string>(available["combatCardsLow"], StringComparer.Ordinal);
                    var high = pool.Where(id => !low.Contains(id)).ToList();
                    var lowIds = pool.Where(low.Contains).ToList();
                    var allowLow = lowIds.Count > 0 && rng.Pick(stream, hundred) < (int)Math.Floor(((double?)rule["lowerRankChance"] ?? 0) * 100 + 0.5);
                    if (!allowLow && high.Count == 0) { available[kind] = new List<string>(); continue; }
                    pool = allowLow ? lowIds : high;
                }
                var picked = rng.Pick(stream, pool);
                available[kind] = available[kind].Where(v => v != picked).ToList();
                options.Add(new Core2RewardOption { Kind = kind, Id = picked });
                shelf[kind] = (shelf.TryGetValue(kind, out var c) ? c : 0) + 1;
            }
            var offer = new Core2RewardOffer { Id = offerId, Source = source, Options = options, TakeCount = takeCount };
            run.RewardOffers[offerId] = offer;
            return offer;
        }

        /// <summary>JS gameProfileRewardOffers: a combat (or treasure) offer with the weighted groups, or a level offer of cards only.</summary>
        public static Core2RewardOffer Offer(Core2RunData data, Core2Run run, Core2Rng rng, string offerId, string source = "combat")
        {
            var module = data.Module("rewards");
            if ((string)module?["mode"] != "configured") throw new InvalidOperationException("Configured rewards unavailable");
            var rules = data.Content.Rewards;
            var count = (int?)module["offerCount"] ?? (int)rules["offerCount"];
            var take = (int?)module["takeCount"] ?? (int)rules["takeCount"];
            var categories = source == "level" ? new List<string> { "combatCards" } : Core2Json.Strings(rules["categories"]).ToList();
            var groups = source == "combat" ? (module["rewardGroups"] as JArray ?? rules["rewardGroups"] as JArray ?? new JArray()) : new JArray();
            return MakeOffer(data, run, rng, offerId, source, count, take, categories, groups, null);
        }

        /// <summary>JS gameProfileShopStock: every category's shelf (itemsPerCategory each) at the flat price.</summary>
        public static Core2RewardOffer ShopStock(Core2RunData data, Core2Run run, Core2Rng rng, string shopId)
        {
            var module = data.Module("shops");
            if ((string)module?["mode"] != "configured") throw new InvalidOperationException("Configured shop unavailable");
            var rules = data.Content.Shops;
            var categories = Core2Json.Strings(data.Content.Rewards["categories"]).ToList();
            int? perCategory = (int?)module["itemsPerCategory"] ?? (int?)rules["itemsPerCategory"];
            var count = perCategory.HasValue ? perCategory.Value * categories.Count : (int?)module["options"] ?? (int)rules["offerCount"];
            var stock = MakeOffer(data, run, rng, "shop:" + shopId, "shop", count, count, categories, new JArray(), perCategory);
            if (!stock.Price.HasValue) stock.Price = (int?)module["price"] ?? (int)rules["price"];
            return stock;
        }

        /// <summary>JS chargeKindForFlask: the first heal flask is the HP charge, the first restoreMana flask the MP charge.</summary>
        public static string ChargeKind(Core2RunData data, string flaskId)
        {
            string Kind(JObject def)
            {
                if (def["kind"]?.Type == JTokenType.String) return (string)def["kind"];
                var effects = ((JArray)def["effects"] ?? new JArray()).OfType<JObject>().ToList();
                if (effects.Any(e => (string)e["op"] == "restoreMana")) return "mana";
                if (effects.Any(e => (string)e["op"] == "heal")) return "hp";
                return "utility";
            }
            foreach (var kind in new[] { "hp", "mana" })
            {
                var first = data.Content.Flasks.FirstOrDefault(f => Kind(f.Raw) == kind);
                if (first != null && first.Id == flaskId) return kind;
            }
            return null;
        }

        /// <summary>JS acquireFlask: a charge flask adds one charge of capacity; any other flask takes a potion slot. Null when refused.</summary>
        public static string AcquireFlask(Core2RunData data, Core2Run run, string flaskId)
        {
            data.Flask(flaskId);
            var amount = (int?)data.Module("resources")?["chargeFlaskCapacityPerPickup"] ?? 0;
            var chargeKind = amount > 0 ? ChargeKind(data, flaskId) : null;
            if (chargeKind != null)
            {
                var c = run.FlaskCharges;
                var valid = c != null && c.Base > 0 && c.Capacity > 0 && c.Hp + c.Mana == c.Capacity && c.Capacity == c.Base + c.GrownHp + c.GrownMana + c.Granted
                    && c.HpCurrent <= c.Hp && c.ManaCurrent <= c.Mana;
                if (!valid) return "Invalid flask capacity ledger";
                c.Capacity += amount; c.Granted += amount;
                if (chargeKind == "hp") { c.Hp += amount; c.HpCurrent += amount; } else { c.Mana += amount; c.ManaCurrent += amount; }
                return null;
            }
            if (run.Flasks.Count >= data.FlaskSlots) return "Potion slots are full";
            run.Flasks.Add(flaskId);
            return null;
        }

        /// <summary>JS recordCollectedCards (knownCollectedCardIds): the ledger plus every loose owned card the profile defines.</summary>
        public static void RecordCollectedCards(Core2RunData data, Core2Run run)
        {
            var definitions = new HashSet<string>(data.Content.Cards.Select(c => c.Id), StringComparer.Ordinal);
            var next = new List<string>();
            foreach (var id in run.CollectedCardIds.Concat(run.Owned.Where(c => !c.ItemOwned).Select(c => c.CardId)))
                if (definitions.Contains(id) && !next.Contains(id)) next.Add(id);
            run.CollectedCardIds = next;
        }

        /// <summary>JS claimGameProfileReward: take one option (validated against current eligibility); the run changes only on success.</summary>
        public static Core2RewardOption Claim(Core2RunData data, Core2Run run, string offerId, int index)
        {
            if (!run.RewardOffers.TryGetValue(offerId, out var offer) || offer.Claimed.HasValue || offer.Taken.Contains(index) || offer.TakeCount < 1)
                throw new InvalidOperationException("Reward offer is missing or already claimed");
            if (index < 0 || index >= offer.Options.Count) throw new InvalidOperationException("Invalid reward option");
            var option = offer.Options[index];
            if (!Pools(data, run).TryGetValue(option.Kind, out var pool) || !pool.Contains(option.Id))
                throw new InvalidOperationException("Reward option is unavailable under configured eligibility");
            switch (option.Kind)
            {
                case "combatCards":
                case "weaponArts":
                    data.Content.Card(option.Id);
                    run.Sideboard.Add(new Core2RunCard { InstanceId = "reward:" + offerId + ":" + index, CardId = option.Id });
                    break;
                case "weapons":
                    if (data.Armament(option.Id) == null) throw new InvalidOperationException("Unknown reward weapon");
                    if (run.Loadout.Storage.Contains(option.Id)) throw new InvalidOperationException("Reward weapon already owned");
                    run.Loadout.Storage.Add(option.Id);
                    break;
                case "relics":
                    data.Relic(option.Id);
                    if (run.Relics.Contains(option.Id)) throw new InvalidOperationException("Reward relic already owned");
                    run.Relics.Add(option.Id);
                    break;
                case "potions":
                {
                    var probe = Clone(run);
                    var refusal = AcquireFlask(data, probe, option.Id);
                    if (refusal != null) throw new InvalidOperationException(refusal);
                    AcquireFlask(data, run, option.Id);
                    break;
                }
                case "armor":
                    if (data.Armour(run.Class, option.Id) == null) throw new InvalidOperationException("Unknown reward armor");
                    run.Loadout.BoughtArmour.Add(new KeyValuePair<string, string>(run.Class, option.Id));
                    break;
                default: throw new InvalidOperationException("Unknown reward category");
            }
            offer.Taken.Add(index);
            if (offer.Taken.Count >= Math.Min(offer.TakeCount, offer.Options.Count)) offer.Claimed = index;
            RecordCollectedCards(data, run);
            return option;
        }

        /// <summary>JS purchaseGameProfileShopOffer: pay the price, then the ordinary claim; a refusal changes nothing.</summary>
        public static Core2RewardOption Purchase(Core2RunData data, Core2Run run, string shopId, int index)
        {
            var offerId = "shop:" + shopId;
            if (!run.RewardOffers.TryGetValue(offerId, out var stock) || stock.Source != "shop") throw new InvalidOperationException("Missing configured shop");
            if (!stock.Price.HasValue || stock.Price.Value < 0) throw new InvalidOperationException("Invalid configured shop price");
            if (run.Cinders < stock.Price.Value) throw new InvalidOperationException("Insufficient cinders");
            var draft = Clone(run);
            draft.Cinders -= stock.Price.Value;
            var option = Claim(data, draft, offerId, index);
            Core2RunSave.CopyInto(draft, run);
            return option;
        }

        private static Core2Run Clone(Core2Run run) => Core2RunSave.Clone(run);
    }
}
