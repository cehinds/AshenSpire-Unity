// Core2FightRiders.cs — the per-entity ability ledger a Core 2 fight keeps for card riders, turn metrics and card charges:
// what the player played this turn and this combat, the HP lost since the turn started, the cards discarded on purpose,
// and the charges a property grants the next matching card.
//
// JS at the pin:
//   src/engine/abilityRiders.js  stateFor / beginAbilityTurn (the turn reset keeps the combat-long ledgers), abilitySnapshot,
//                                abilityMetric (tagPlays, distinctTagPlays, sameCardPlays, else the named counter),
//                                recordAbilityCard (typePlays, instanceUsesCombat, cardUsesCombat, cardsPlayed, cardPlaysCombat,
//                                attacksPlayed, sameCardPlays, manaSpent, previousSpell, tagPlays, distinctTagPlays),
//                                recordAbilityEvent (hpLost: hpLostSinceTurnStart / hpLostThisRound; an explicit discard),
//                                chargeMatches / matchingAbilityCharges, abilityChargedEffects, consumeAbilityCharges,
//                                grantAbilityCharge, attachAbilityCharges, allowAbilityOnce
//   src/engine/triggers.js       evalPredicate 'turnMetric' (the event's or action's abilityBefore snapshot unless snapshot
//                                'current'; cardId 'event' reads the event's card)
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>One card charge (JS abilityRiders.charges[key]): the card it matches and the riders it adds.</summary>
    public sealed class Core2AbilityCharge
    {
        public string CardType, CardTag, AbilityKind, BuildupStatus, DamageScope;
        public int Damage, ManaDiscount, Block, Heal, Break, Buildup;
        public Core2AbilityCharge Clone() => (Core2AbilityCharge)MemberwiseClone();
    }

    /// <summary>JS entity.abilityRiders: the turn's and the combat's card ledger, the charges and the once markers.</summary>
    public sealed class Core2AbilityState
    {
        public int CardsPlayed, ManaSpent, Discarded, HpLostSinceTurnStart, HpLostThisRound, OfferingsPaid, PreviousSpell, AttacksPlayed, CardPlaysCombat;
        public double TurnStartHpPct = 100;
        public Dictionary<string, int> TagPlays = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> DistinctTagPlays = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        public Dictionary<string, Core2AbilityCharge> Charges = new Dictionary<string, Core2AbilityCharge>(StringComparer.Ordinal);
        public HashSet<string> Once = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, int> InstanceUsesCombat = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> CardUsesCombat = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> TypePlays = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> SameCardPlays = new Dictionary<string, int>(StringComparer.Ordinal);

        public Core2AbilityState Clone()
        {
            var copy = (Core2AbilityState)MemberwiseClone();
            copy.TagPlays = new Dictionary<string, int>(TagPlays, StringComparer.Ordinal);
            copy.DistinctTagPlays = DistinctTagPlays.ToDictionary(p => p.Key, p => new HashSet<string>(p.Value, StringComparer.Ordinal), StringComparer.Ordinal);
            copy.Charges = Charges.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal);
            copy.Once = new HashSet<string>(Once, StringComparer.Ordinal);
            copy.InstanceUsesCombat = new Dictionary<string, int>(InstanceUsesCombat, StringComparer.Ordinal);
            copy.CardUsesCombat = new Dictionary<string, int>(CardUsesCombat, StringComparer.Ordinal);
            copy.TypePlays = new Dictionary<string, int>(TypePlays, StringComparer.Ordinal);
            copy.SameCardPlays = new Dictionary<string, int>(SameCardPlays, StringComparer.Ordinal);
            return copy;
        }

        public static int Count(Dictionary<string, int> map, string key) => key != null && map.TryGetValue(key, out var n) ? n : 0;
    }

    /// <summary>The card charges a play matched (JS matchingAbilityCharges): their keys and summed riders.</summary>
    public sealed class Core2MatchedCharges
    {
        public List<string> Keys = new List<string>();
        public int Damage, ManaDiscount, Block, Heal, Break, DamageEffect;
        public List<(string Status, int Amount)> BuildupBonuses = new List<(string, int)>();
    }

    public sealed partial class Core2Combatant
    {
        /// <summary>JS entity.abilityRiders (null until first touched, as JS stateFor creates it lazily).</summary>
        public Core2AbilityState AbilityRiders { get; set; }
    }

    public sealed partial class Core2Fight
    {
        /// <summary>JS stateFor: the entity's ledger, created on first use.</summary>
        private static Core2AbilityState Riders(Core2Combatant entity) => entity.AbilityRiders ??= new Core2AbilityState();

        /// <summary>JS beginAbilityTurn: a new turn's ledger; the HP lost last round becomes this turn's, the combat-long counts stay.</summary>
        private static void BeginAbilityTurn(Core2Combatant entity)
        {
            var old = Riders(entity);
            entity.AbilityRiders = new Core2AbilityState
            {
                HpLostSinceTurnStart = old.HpLostThisRound,
                TurnStartHpPct = entity.MaxHp > 0 ? 100.0 * entity.Hp / entity.MaxHp : 0,
                InstanceUsesCombat = new Dictionary<string, int>(old.InstanceUsesCombat, StringComparer.Ordinal),
                CardUsesCombat = new Dictionary<string, int>(old.CardUsesCombat, StringComparer.Ordinal),
                CardPlaysCombat = old.CardPlaysCombat,
            };
        }

        /// <summary>JS abilitySnapshot: a detached copy of the ledger (the abilityBefore a play and its events carry).</summary>
        private static Core2AbilityState AbilitySnapshot(Core2Combatant entity) => Riders(entity).Clone();

        /// <summary>JS abilityMetric(entity, predicate, snapshot): a turn metric read off the snapshot, else the live ledger.</summary>
        private static int AbilityMetric(Core2Combatant entity, JObject predicate, string cardId, Core2AbilityState snapshot)
        {
            var state = snapshot ?? entity?.AbilityRiders ?? new Core2AbilityState();
            var metric = (string)predicate["metric"];
            switch (metric)
            {
                case "tagPlays": return Core2AbilityState.Count(state.TagPlays, (string)predicate["tag"]);
                case "distinctTagPlays": return (string)predicate["tag"] != null && state.DistinctTagPlays.TryGetValue((string)predicate["tag"], out var names) ? names.Count : 0;
                case "sameCardPlays": return Core2AbilityState.Count(state.SameCardPlays, cardId);
                case "cardsPlayed": return state.CardsPlayed;
                case "manaSpent": return state.ManaSpent;
                case "discarded": return state.Discarded;
                case "hpLostSinceTurnStart": return state.HpLostSinceTurnStart;
                case "hpLostThisRound": return state.HpLostThisRound;
                case "offeringsPaid": return state.OfferingsPaid;
                case "previousSpell": return state.PreviousSpell;
                case "attacksPlayed": return state.AttacksPlayed;
                case "cardPlaysCombat": return state.CardPlaysCombat;
            }
            // JS `state[predicate.metric] || 0`: an unnamed counter reads 0.
            return 0;
        }

        /// <summary>JS recordAbilityCard(entity, cardRef, manaSpent): the played card enters the ledger.</summary>
        private static void RecordAbilityCard(Core2Combatant entity, string kind, string cardId, string instanceId, IReadOnlyList<string> authoredTags, string abilityKind, int manaSpent)
        {
            var s = Riders(entity);
            s.TypePlays[kind ?? "undefined"] = Core2AbilityState.Count(s.TypePlays, kind ?? "undefined") + 1;
            if (instanceId != null) s.InstanceUsesCombat[instanceId] = Core2AbilityState.Count(s.InstanceUsesCombat, instanceId) + 1;
            s.CardUsesCombat[cardId] = Core2AbilityState.Count(s.CardUsesCombat, cardId) + 1;
            s.CardsPlayed += 1;
            s.CardPlaysCombat += 1;
            s.AttacksPlayed += kind == "attack" ? 1 : 0;
            s.SameCardPlays[cardId] = Core2AbilityState.Count(s.SameCardPlays, cardId) + 1;
            s.ManaSpent += manaSpent;
            s.PreviousSpell = abilityKind == "spell" || authoredTags.Contains("source:spell") ? 1 : 0;
            foreach (var tag in authoredTags.Distinct(StringComparer.Ordinal))
            {
                s.TagPlays[tag] = Core2AbilityState.Count(s.TagPlays, tag) + 1;
                if (!s.DistinctTagPlays.TryGetValue(tag, out var names)) s.DistinctTagPlays[tag] = names = new HashSet<string>(StringComparer.Ordinal);
                names.Add(cardId);
            }
        }

        /// <summary>JS recordAbilityEvent: HP lost counts toward the target's turn and round; an explicit discard toward the player's.</summary>
        private void RecordAbilityEvent(Core2FightEvent ev)
        {
            if (ev.Type == "hpLost")
            {
                var entity = Find((string)ev.Data["targetId"]);
                if (entity != null)
                {
                    var s = Riders(entity);
                    var amount = Math.Max(0, (int?)ev.Data["amount"] ?? 0);
                    s.HpLostSinceTurnStart += amount;
                    s.HpLostThisRound += amount;
                }
            }
            var reason = (string)ev.Data["reason"];
            if (ev.Type == "cardDiscarded" && (reason == "effect" || reason == "chosen" || reason == "random") && Player != null) Riders(Player).Discarded += 1;
        }

        /// <summary>JS chargeMatches + matchingAbilityCharges: the charges this card takes and their summed riders.</summary>
        private static Core2MatchedCharges MatchingAbilityCharges(Core2Combatant entity, string kind, IReadOnlyList<string> authoredTags, string abilityKind, bool enemyAvailable = true)
        {
            var result = new Core2MatchedCharges();
            foreach (var pair in entity?.AbilityRiders?.Charges ?? new Dictionary<string, Core2AbilityCharge>())
            {
                var c = pair.Value;
                var matches = (c.CardType == null || c.CardType == kind) && (c.CardTag == null || authoredTags.Contains(c.CardTag)) && (c.AbilityKind == null || c.AbilityKind == abilityKind);
                if (!matches) continue;
                if (!enemyAvailable && c.Buildup > 0 && !(c.Damage > 0 || c.ManaDiscount > 0 || c.Block > 0 || c.Heal > 0 || c.Break > 0)) continue;
                result.Keys.Add(pair.Key);
                result.Damage += c.DamageScope == "effect" ? 0 : c.Damage;
                result.ManaDiscount += c.ManaDiscount; result.Block += c.Block; result.Heal += c.Heal; result.Break += c.Break;
                result.DamageEffect += c.DamageScope == "effect" ? c.Damage : 0;
                if (c.Buildup > 0) result.BuildupBonuses.Add((c.BuildupStatus, c.Buildup));
            }
            return result;
        }

        /// <summary>
        /// JS abilityChargedEffects (expanded rules): a buildup charge with no authored application of its status adds a zero
        /// buildup at the enemy after the card's primary damage, Block, heal or Barrier effect.
        /// </summary>
        private static List<JObject> AbilityChargedEffects(List<JObject> effects, Core2MatchedCharges charges, bool enemyAvailable)
        {
            if (!enemyAvailable) return effects;
            string Id(string id) => id == "insanity" ? "dazed" : id;
            var missing = charges.BuildupBonuses.Select(b => Id(b.Status)).Distinct(StringComparer.Ordinal)
                .Where(status => !effects.Any(e => ((string)e["op"] == "applyStatus" || (string)e["op"] == "buildup") && (string)e["status"] == status)).ToList();
            if (missing.Count == 0) return effects;
            var primary = effects.FindIndex(e => (string)e["op"] == "damage" || (string)e["op"] == "block" || (string)e["op"] == "heal" || (string)e["op"] == "gainBarrier");
            var index = primary < 0 ? effects.Count : primary + 1;
            var list = effects.Take(index).ToList();
            list.AddRange(missing.Select(status => new JObject { ["op"] = "buildup", ["target"] = "enemy", ["status"] = status, ["amount"] = 0 }));
            list.AddRange(effects.Skip(index));
            return list;
        }

        /// <summary>JS consumeAbilityCharges.</summary>
        private static void ConsumeAbilityCharges(Core2Combatant entity, IEnumerable<string> keys)
        {
            var charges = Riders(entity).Charges;
            foreach (var key in keys.ToList()) charges.Remove(key);
        }

        /// <summary>JS grantAbilityCharge: a repeated source replaces its own charge with the larger riders, never accumulates.</summary>
        private static void GrantAbilityCharge(Core2Combatant entity, JObject effect, Func<string, int> amount)
        {
            var charges = Riders(entity).Charges;
            var key = (string)effect["key"];
            charges.TryGetValue(key, out var old);
            var charge = new Core2AbilityCharge
            {
                CardType = (string)effect["cardType"], CardTag = (string)effect["cardTag"], AbilityKind = (string)effect["abilityKind"],
                Damage = Math.Max(old?.Damage ?? 0, amount("damage")), ManaDiscount = Math.Max(old?.ManaDiscount ?? 0, amount("manaDiscount")),
            };
            if (amount("block") != 0) charge.Block = Math.Max(old?.Block ?? 0, amount("block"));
            if (amount("heal") != 0) charge.Heal = Math.Max(old?.Heal ?? 0, amount("heal"));
            if (amount("break") != 0) charge.Break = Math.Max(old?.Break ?? 0, amount("break"));
            if (amount("buildup") != 0) charge.Buildup = Math.Max(old?.Buildup ?? 0, amount("buildup"));
            if (effect["buildupStatus"] != null) charge.BuildupStatus = (string)effect["buildupStatus"];
            if (effect["damageScope"] != null) charge.DamageScope = (string)effect["damageScope"];
            charges[key] = charge;
        }

        /// <summary>JS attachAbilityCharges: the first damage effect carries the damage and break riders, the first Block/Barrier the Block, the first heal the heal, each status its buildup.</summary>
        private static void AttachAbilityCharges(Core2FightAction action, Core2MatchedCharges charges, HashSet<string> applied)
        {
            var op = (string)action.Effect["op"];
            if (op == "damage" && charges.Break > 0 && applied.Add("break")) action.ChargeBreak = charges.Break;
            if (op == "damage" && charges.DamageEffect > 0 && applied.Add("damageEffect")) action.ChargeDamageEffect = charges.DamageEffect;
            if (op == "applyStatus" || op == "buildup")
            {
                var status = (string)action.Effect["status"];
                var amount = charges.BuildupBonuses.Where(b => (b.Status == "insanity" ? "dazed" : b.Status) == status).Sum(b => b.Amount);
                if (amount != 0 && !applied.Contains("buildup:" + status)) { applied.Add("buildup:" + status); action.ChargeBuildup = amount; }
            }
            var field = op == "damage" ? "damage" : op == "block" || op == "gainBarrier" ? "block" : op == "heal" ? "heal" : null;
            if (field == null || applied.Contains(field)) return;
            var value = field == "damage" ? charges.Damage : field == "block" ? charges.Block : charges.Heal;
            if (value == 0) return;
            applied.Add(field);
            if (field == "damage") action.ChargeDamage = value; else if (field == "block") action.ChargeBlock = value; else action.ChargeHeal = value;
        }
    }
}
