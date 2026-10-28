// Core2RunRest.cs — a Core 2 rest site: full recovery on arrival, then the free Smith and Level Up, each once per visit,
// in any order; only Continue leaves (owner ruling D10; packs/core/rests.json servicesPerVisit "all").
//
// JS at the pin:
//   src/model/gameProfileRest.js  beginGameProfileRest (heal on arrival, once per visit: HP, MP, SP, flask charges),
//                                 commitGameProfileRest / applyService (smith, levelUp; each service then heals fully)
//   src/model/smithing.js         smithingPlan (owned item refs in order: deck sources, carried armaments, worn armour,
//                                 relics; a tier exists when the ladder authors one), commitItemUpgrade (free: one tier)
//   src/model/smithingCore.js     coreSmithingStats (the item's stat rows at the two tiers), coreSmithingCards
//   src/model/levelup.js          applyLevelUp (one earned point), rederivePools
//
// Port note: JS decides whether a Core Smith tier changes a deck card by running two real fights and comparing every
// card's previewed Damage and Block (coreSmithingCards). The run layer reads the same fact from the rating the tier
// moves: an equipped tier that raises AR changes every damage card, one that raises DR every block card (the
// upgradeCard event effect reads it; the Smith's own candidates do not need it).
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2SmithCandidate
    {
        public string ItemRef { get; set; }
        public string ItemKind { get; set; }
        public string ItemId { get; set; }
        public int CurrentLevel { get; set; }
        public int NextLevel { get; set; }
        /// <summary>The item's stat changes for this tier (JS coreSmithingStats rows: stat id → before/after).</summary>
        public Dictionary<string, (int Before, int After)> Stats { get; set; } = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
    }

    public static class Core2RunRest
    {
        private static readonly string[] StatOrder = { "ar", "dr", "poise", "ward", "hp", "mp", "sp", "evasion", "precision", "strength", "dexterity", "constitution", "wisdom", "intelligence" };

        /// <summary>JS itemRefIdentity.</summary>
        public static (string Kind, string Id, string ClassId) Identity(string itemRef)
        {
            var parts = itemRef?.Split('/') ?? new string[0];
            if (parts.Length == 2 && parts[0] == "armament") return ("armament", parts[1], null);
            if (parts.Length == 3 && parts[0] == "armor") return ("armor", parts[2], parts[1]);
            if (parts.Length == 2 && parts[0] == "relic") return ("relic", parts[1], null);
            return (null, null, null);
        }

        private static List<JObject> Rows(Core2RunData data, string itemRef, int tier) => data.ItemUpgradeChanges.Where(r => (string)r["itemRef"] == itemRef && (int)r["nextTier"] == tier).ToList();

        /// <summary>JS ownedItemRefs: deck sources, carried armaments, the worn armour, then relics.</summary>
        public static List<string> OwnedItemRefs(Core2RunData data, Core2Run run)
        {
            var refs = new List<string>();
            void Add(string r) { if (r != null && !refs.Contains(r)) refs.Add(r); }
            var carried = new List<string>(run.Loadout.Storage);
            foreach (var ids in run.Loadout.Sets.Values) foreach (var id in ids) if (id != null && !carried.Contains(id)) carried.Add(id);
            foreach (var id in carried) if (data.Armament(id) != null) Add("armament/" + id);
            var armour = run.Loadout.Equipped("armor");
            if (armour != null && data.Armour(run.Class, armour) != null) Add("armor/" + run.Class + "/" + armour);
            foreach (var id in run.Relics) if (data.HasRelic(id)) Add("relic/" + id);
            return refs;
        }

        /// <summary>JS itemAtTier: the item's stat values (and point total) at a Smith tier; a weapon adds AR per tier.</summary>
        private static (Dictionary<string, int> Values, int Points, int? Budget) AtTier(Core2RunData data, JObject item, int level)
        {
            var settings = data.Module("gear");
            var resolved = Core2EnemyStats.ResolveGearItem(item, settings, ((int?)item["rank"] ?? 1) + level);
            var values = new Dictionary<string, int>(resolved.Values, StringComparer.Ordinal);
            var kind = (string)item["kind"];
            if (kind != "armor" && kind != "shield") values["ar"] = (values.TryGetValue("ar", out var ar) ? ar : 0) + level * ((int?)data.Module("equipment")?["upgradeBonuses"]?["weaponARPerTier"] ?? 0);
            var tagCost = Core2Json.Strings(item["tags"]).Sum(t => (int?)settings["tags"]?[t]?["cost"] ?? 0);
            return (values, resolved.Points.Values.Sum() + tagCost, resolved.Budget);
        }

        /// <summary>JS smithingPlan (free): every owned item with a next authored tier whose Core (or relic) rows change something.</summary>
        public static List<Core2SmithCandidate> SmithPlan(Core2RunData data, Core2Run run)
        {
            var output = new List<Core2SmithCandidate>();
            foreach (var itemRef in OwnedItemRefs(data, run))
            {
                var identity = Identity(itemRef);
                var current = run.ItemUpgradeLevels.TryGetValue(itemRef, out var level) ? level : 0;
                var next = current + 1;
                var rows = Rows(data, itemRef, next);
                if (rows.Count == 0) continue;
                var authored = rows.Where(r => (string)r["tag"] != "upgrade:cost:smithing-stone").ToList();
                if (authored.Count == 0) continue;
                var candidate = new Core2SmithCandidate { ItemRef = itemRef, ItemKind = identity.Kind, ItemId = identity.Id, CurrentLevel = current, NextLevel = next };
                if (identity.Kind == "relic")
                {
                    // Legacy relic tiers: their passive rows (JS itemUpgradeValueReceipts) are the change.
                    if (!authored.Any(r => ((string)r["tag"]).StartsWith("relic:", StringComparison.Ordinal))) continue;
                    output.Add(candidate);
                    continue;
                }
                var item = Core2RunGear.GearItem(data.Content, identity.Id, identity.Kind == "armor", run.Class);
                if (item == null) continue;
                var before = AtTier(data, item, current);
                var after = AtTier(data, item, next);
                var changed = false;
                if ((string)item["kind"] == "armor" || (string)item["kind"] == "shield") changed |= (before.Budget ?? before.Points) != (after.Budget ?? after.Points);
                foreach (var id in StatOrder)
                {
                    before.Values.TryGetValue(id, out var b); after.Values.TryGetValue(id, out var a);
                    if (a != b) { candidate.Stats[id] = (b, a); changed = true; }
                }
                if (!changed) continue;
                output.Add(candidate);
            }
            return output;
        }

        private static bool Equipped(Core2Run run, Core2SmithCandidate candidate) => run.Loadout.Sets.Any(pair =>
            run.Loadout.Equipped(pair.Key) == candidate.ItemId && (pair.Key == "armor") == (candidate.ItemKind == "armor"));

        /// <summary>The deck cards whose Damage or Block this tier moves (see the port note): an equipped tier raising AR or DR.</summary>
        public static List<string> ChangedCardIds(Core2RunData data, Core2Run run, Core2SmithCandidate candidate)
        {
            if (!Equipped(run, candidate)) return new List<string>();
            var ar = candidate.Stats.TryGetValue("ar", out var a) && a.After != a.Before;
            var dr = candidate.Stats.TryGetValue("dr", out var d) && d.After != d.Before;
            // AR prices a physical card's Damage and DR its Block; a Spell's numbers read other ratings.
            return run.Deck.Select(c => c.CardId).Distinct().Where(id =>
            {
                var card = data.Content.Card(id);
                if (card.Tags.Contains("camp:spell")) return false;
                var effects = card.Effects.OfType<JObject>().Select(e => (string)e["op"]).ToList();
                return (ar && effects.Contains("damage")) || (dr && effects.Contains("block"));
            }).ToList();
        }

        public static bool ChangesDeckCards(Core2RunData data, Core2Run run, Core2SmithCandidate candidate) => ChangedCardIds(data, run, candidate).Count > 0;

        /// <summary>JS commitItemUpgrade (free): the item's Smith tier rises by one; the pools are re-derived.</summary>
        public static void CommitUpgrade(Core2RunData data, Core2Run run, string itemRef)
        {
            var candidate = SmithPlan(data, run).FirstOrDefault(c => c.ItemRef == itemRef)
                ?? throw new InvalidOperationException("Item '" + itemRef + "' is not an eligible Smithing candidate");
            run.ItemUpgradeLevels[itemRef] = candidate.NextLevel;
        }

        private static void HealFull(Core2Run run)
        {
            run.Hp = run.MaxHp; run.Mana = run.MaxMana; run.Stamina = run.MaxStamina;
            run.FlaskCharges?.Refill();
        }

        private static string RestContext(Core2Run run)
        {
            var node = run.MapGraph?.Nodes.TryGetValue(run.MapNodeId ?? "", out var n) == true ? n : null;
            if (node == null || (node.Type != "shrine" && node.ActualType != "rest")) throw new InvalidOperationException("Rest service requires the current rest node");
            return run.ActNumber + ":" + run.MapNodeId;
        }

        /// <summary>JS beginGameProfileRest: open (or resume) the visit; arriving heals once, reloads never heal again.</summary>
        public static Core2RestVisit Begin(Core2RunData data, Core2Run run, string visitId = null)
        {
            if ((string)data.Module("rest")?["mode"] != "configured") throw new InvalidOperationException("Configured rest is unavailable");
            var context = RestContext(run);
            visitId ??= context;
            if (visitId != context) throw new InvalidOperationException("Rest visitId must identify the current rest node");
            if (!run.RestVisits.TryGetValue(visitId, out var visit)) run.RestVisits[visitId] = visit = new Core2RestVisit { Id = visitId };
            run.RestVisitId = visitId;
            if (AllServices(data) && visit.Healed == null)
            {
                var before = (run.Hp, run.Mana, run.Stamina, run.FlaskCharges?.HpCurrent ?? 0, run.FlaskCharges?.ManaCurrent ?? 0);
                HealFull(run);
                visit.Healed = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["hp"] = run.Hp - before.Item1, ["mana"] = run.Mana - before.Item2, ["stamina"] = run.Stamina - before.Item3,
                    ["flaskHp"] = (run.FlaskCharges?.HpCurrent ?? 0) - before.Item4, ["flaskMana"] = (run.FlaskCharges?.ManaCurrent ?? 0) - before.Item5,
                };
            }
            return visit;
        }

        public static bool AllServices(Core2RunData data) => (string)data.Content.Rests["servicesPerVisit"] == "all";
        public static bool ServiceUsed(Core2RestVisit visit, string service) => visit.Used != null ? visit.Used.TryGetValue(service, out var used) && used : visit.Service == service;

        /// <summary>The rest services still open this visit (smith needs a candidate; levelUp needs an earned point).</summary>
        public static List<string> OpenServices(Core2RunData data, Core2Run run)
        {
            var visit = run.RestVisitId != null && run.RestVisits.TryGetValue(run.RestVisitId, out var v) ? v : null;
            if (visit == null) return new List<string>();
            return Core2Json.Strings(data.Content.Rests["services"]).Where(service => !(AllServices(data) ? ServiceUsed(visit, service) : visit.Service != null)
                && (service != "smith" || SmithPlan(data, run).Count > 0) && (service != "levelUp" || run.Level.UnspentPoints > 0)).ToList();
        }

        /// <summary>JS commitGameProfileRest: one service on a draft (smith: itemRef; levelUp: attributeId), then a full heal.</summary>
        public static void Commit(Core2RunData data, Core2Run run, string service, string itemRef = null, string attributeId = null)
        {
            var draft = Core2RunSave.Clone(run);
            var rules = data.Content.Rests;
            if (draft.RestVisitId != RestContext(draft)) throw new InvalidOperationException("Rest visit is stale");
            if (!draft.RestVisits.TryGetValue(draft.RestVisitId, out var visit) || (string)data.Module("rest")?["mode"] != "configured") throw new InvalidOperationException("Rest visit is unavailable or spent");
            if (!Core2Json.Strings(rules["services"]).Contains(service)) throw new InvalidOperationException("Unknown rest service '" + service + "'");
            if (AllServices(data) ? ServiceUsed(visit, service) : visit.Service != null) throw new InvalidOperationException("Rest visit is unavailable or spent");
            var characterLevels = (bool?)data.Module("progression")?["characterLevels"] == true;
            if (service == "levelUp" && !characterLevels && !(draft.Level.UnspentPoints > 0)) throw new InvalidOperationException("Character leveling is disabled for this profile");
            if (service == "levelUp")
            {
                if (!Core2Run.AttributeIds.Contains(attributeId)) throw new InvalidOperationException("levelUp: '" + attributeId + "' is not an attribute id");
                if (draft.Level.UnspentPoints <= 0)
                    throw new InvalidOperationException("levelUp: no attribute point waiting to be assigned (level " + draft.Level.Level + ", " + draft.Level.Xp + "/" + Core2RunProgression.XpToNext(data, draft.Level.Level) + " XP)");
                draft.Attributes[attributeId] = draft.AttributeValue(attributeId) + 1;
                draft.Level.UnspentPoints -= 1;
                draft.LevelUps += 1; draft.LevelPoints += 1;
                Core2RunGear.RederivePools(data, draft);
            }
            if (service == "smith")
            {
                if ((int?)rules["smith"]?["tiersPerService"] != 1) throw new InvalidOperationException("Smith requires an authored single-tier service policy");
                CommitUpgrade(data, draft, itemRef);
                Core2RunGear.RederivePools(data, draft);
            }
            HealFull(draft);
            if (AllServices(data))
            {
                visit.Used ??= visit.Service != null ? new Dictionary<string, bool>(StringComparer.Ordinal) { [visit.Service] = true } : new Dictionary<string, bool>(StringComparer.Ordinal);
                visit.Used[service] = true;
            }
            visit.Service = service;
            Core2RunSave.CopyInto(draft, run);
        }
    }
}
