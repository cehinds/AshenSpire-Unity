// Core2Counters.cs — Core 2 Counters (owner ruling 2026-10-19, SPEC §17): preparing one, what it covers, the Block an
// advantaged covered hit adds before it is absorbed, and the return — a fraction of the damage that hit (its
// `incoming`, after the attacker's whole chain and its single floor, before Block, Barrier, Ward or Poise), not the
// card's printed numbers. Spell Counters (a spell-camp counter maneuver that authors counterCoverage) arm while their
// owner stands in the prepared stance they enter and until any later card is accepted; they return HP and Poise only.
//
// JS at the pin:
//   src/engine/counterRule.js      counterBeats, counterReturn, counterReturnForReceipt, counterReturnSpent,
//                                  paidCounterContact, counterBlockGrant, counterBlockBonus
//   src/model/profileStance.js     isSpellCounter, armsAsCoreCounter
//   src/engine/combatMatchups.js   armExpansionCounter (mode, serial, coverage, expiry, armedAtCardCount),
//                                  normalizedProfile, counterCoverageMatches, preparedCounter, startTacticalTurn,
//                                  completeTacticalAction (the Core 2 branch: who answers, and the HP/Poise payload)
//   src/engine/combatCardTactics.js prepareTacticalCard (a counter maneuver arms; a reactive Sweep's melee riders)
//   content/source/fixCoreMechanics.json counter: returnFraction 0.5, poiseFraction 0.2, advantageIncomingFraction
//                                  0.25, advantagePoiseFraction 0.5, advantageBlockFraction 0.25
//   combatExpansionMatchups.counter.defaultCoverage (mechanics.json counterRules)
// The legacy payload numbers (authored hp, Counterstrike, Impact, Flux, school edges) do not reach a Core 2 return
// (completeTacticalAction's coreFixed branch) and are not ported. A Counter paid from the hand as a reaction to one contact
// (Core2FightReactions.cs) carries its serial into these predicates: it answers that contact even after this cycle's
// automatic return.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>What a Counter answers (JS counterCoverage): every declared filter must match.</summary>
    public sealed class Core2CounterCoverage
    {
        public IReadOnlyList<string> Camps { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Schools { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Ranges { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Targeting { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Maneuvers { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Effects { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> RequiredTraits { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> ExcludedTraits { get; set; } = Array.Empty<string>();

        public static Core2CounterCoverage FromJson(JToken token)
        {
            var row = token as JObject ?? new JObject();
            return new Core2CounterCoverage
            {
                Camps = Core2Json.Strings(row["camps"]), Schools = Core2Json.Strings(row["schools"]), Ranges = Core2Json.Strings(row["ranges"]),
                Targeting = Core2Json.Strings(row["targeting"]), Maneuvers = Core2Json.Strings(row["maneuvers"]), Effects = Core2Json.Strings(row["effects"]),
                RequiredTraits = Core2Json.Strings(row["requiredTraits"]), ExcludedTraits = Core2Json.Strings(row["excludedTraits"]),
            };
        }
    }

    /// <summary>A prepared Counter (JS combatCounter, version 2).</summary>
    public sealed class Core2CounterState
    {
        /// <summary>1 while it can still intercept; 0 once a covered hit consumed it (the return may still be owed).</summary>
        public int Charges { get; set; } = 1;
        public int Serial { get; set; }
        /// <summary>melee, ranged or spell.</summary>
        public string Mode { get; set; }
        public Core2CounterCoverage Coverage { get; set; }
        /// <summary>The owner cycle at which it lapses (the owner's next turn).</summary>
        public int ExpiresOnOwnerCycle { get; set; }
        /// <summary>The owner's card count at arming: a spell Counter is disarmed by any later card.</summary>
        public int ArmedAtCardCount { get; set; }
        public Core2Carrier Carrier { get; set; }
        public Core2CounterState Clone()
        {
            var copy = (Core2CounterState)MemberwiseClone();
            copy.Carrier = Carrier?.Clone();
            return copy;
        }
    }

    /// <summary>JS counterReturn result: the HP and Poise a Counter returns and the Block it adds, for one hit.</summary>
    public struct Core2CounterReturn
    {
        public int Hp;
        public int Poise;
        public int Block;
        public Core2CounterReturn(int hp, int poise, int block) { Hp = hp; Poise = poise; Block = block; }
        public override string ToString() => Hp + " HP, " + Poise + " Poise, " + Block + " Block";
    }

    public static class Core2Counters
    {
        /// <summary>JS counterBeats: the stances module's counter matchups list the incoming maneuver.</summary>
        public static bool Beats(Core2Rules rules, string incomingManeuver) =>
            !string.IsNullOrEmpty(incomingManeuver) && rules.StanceMatchups.TryGetValue("counter", out var beats) && beats.Contains(incomingManeuver);

        /// <summary>
        /// JS counterReturn: HP = floor(returnFraction × incoming) (+ floor(advantageIncomingFraction × incoming) when
        /// advantaged); Poise = floor(poiseFraction × HP), or floor(advantagePoiseFraction × HP) when advantaged; Block =
        /// floor(advantageBlockFraction × incoming) when advantaged, else 0. Incoming is floored first.
        /// </summary>
        public static Core2CounterReturn Return(Core2Rules rules, double incoming, bool advantaged)
        {
            var whole = Math.Max(0, (int)Math.Floor(double.IsNaN(incoming) ? 0 : incoming));
            var hp = (int)Math.Floor(rules.CounterReturnFraction * whole) + (advantaged ? (int)Math.Floor(rules.CounterAdvantageIncomingFraction * whole) : 0);
            return new Core2CounterReturn(hp,
                (int)Math.Floor((advantaged ? rules.CounterAdvantagePoiseFraction : rules.CounterPoiseFraction) * hp),
                advantaged ? (int)Math.Floor(rules.CounterAdvantageBlockFraction * whole) : 0);
        }

        /// <summary>JS counterReturnForReceipt: the sum over the contacts the Counter covered, each priced at its own incoming and maneuver.</summary>
        public static Core2CounterReturn ReturnFor(Core2Rules rules, IEnumerable<(bool Covered, int Incoming, string Maneuver)> contacts)
        {
            var total = new Core2CounterReturn();
            foreach (var row in contacts ?? Enumerable.Empty<(bool, int, string)>())
            {
                if (!row.Covered) continue;
                var r = Return(rules, row.Incoming, Beats(rules, row.Maneuver));
                total.Hp += r.Hp; total.Poise += r.Poise; total.Block += r.Block;
            }
            return total;
        }

        /// <summary>JS counterBlockGrant: the Block an entity can take (no Core 2 status sets a blockCap at the pin).</summary>
        public static int BlockGrant(Core2Combatant entity, int amount) => Math.Max(0, amount);

        /// <summary>JS counterReturnSpent: an automatic return was already made this owner cycle.</summary>
        public static bool ReturnSpent(Core2Combatant entity) => entity?.CounterReturnedCycle == entity?.OwnerCycle;

        /// <summary>JS isSpellCounter: a spell-camp carrier whose effective maneuver is counter and that authors coverage.</summary>
        public static bool IsSpellCounter(Core2Carrier card)
        {
            if (card == null) return false;
            var profile = card.Profile ?? new Core2CombatProfile();
            var maneuver = profile.StanceTrigger ?? profile.Maneuver ?? card.Tags.FirstOrDefault(t => t.StartsWith("maneuver:", StringComparison.Ordinal))?.Substring(9);
            var camp = profile.Camp ?? (card.Tags.Contains("camp:spell") ? "spell" : null);
            return maneuver == "counter" && camp == "spell" && profile.CounterCoverage != null;
        }

        /// <summary>JS armsAsCoreCounter: the stances module includes Counter, and the card is a Counter by stance or a spell Counter whose prepared stance is available.</summary>
        public static bool ArmsAsCoreCounter(Core2Rules rules, Core2Carrier card)
        {
            if (!rules.StanceInclude.Contains("counter")) return false;
            var stance = Core2Stances.ConfiguredStance(card);
            if (stance == "counter") return true;
            return IsSpellCounter(card) && rules.StanceInclude.Contains(stance);
        }

        private static bool IsSpellCamp(string camp) => camp == "spell" || camp == "spells" || camp == "magic" || camp == "magical";

        /// <summary>
        /// JS normalizedProfile: the contact's profile with the spell camp folded, a missing range read from its delivery
        /// (ranged → reach, else melee), a missing targeting read from its maneuver (Sweep → area), and the return flag.
        /// </summary>
        public static Core2CombatProfile ContactProfile(Core2Carrier carrier)
        {
            var profile = (carrier?.Profile ?? new Core2CombatProfile()).Clone();
            if (IsSpellCamp(profile.Camp)) profile.Camp = "spell";
            profile.Range ??= profile.Maneuver == "ranged" ? "reach" : "melee";
            profile.Targeting ??= profile.Maneuver == "sweep" ? "area" : "single";
            return profile;
        }

        /// <summary>JS counterCoverageMatches: every declared filter matches; an area hit needs area coverage; a return reads as an Attack.</summary>
        public static bool CoverageMatches(Core2CounterCoverage coverage, Core2CombatProfile profile, string effect = "damage")
        {
            if (coverage == null || profile == null) return false;
            bool Pass(IReadOnlyList<string> values, string value) => values == null || values.Count == 0 || values.Contains(value);
            var maneuver = profile.CounterReturnAttack && profile.Maneuver == "counter" ? "attack" : profile.Maneuver;
            if (!Pass(coverage.Camps, profile.Camp) || !Pass(coverage.Schools, profile.School) || !Pass(coverage.Ranges, profile.Range)
                || !Pass(coverage.Targeting, profile.Targeting) || !Pass(coverage.Maneuvers, maneuver)) return false;
            if (coverage.Effects.Count > 0 && !coverage.Effects.Contains(effect)) return false;
            if (profile.Targeting == "area" && !coverage.Targeting.Contains("area")) return false;
            if (coverage.RequiredTraits.Any(t => !profile.Traits.Contains(t))) return false;
            if (coverage.ExcludedTraits.Any(t => profile.Traits.Contains(t))) return false;
            return true;
        }

        /// <summary>
        /// JS armExpansionCounter (Core 2): a new serial, the mode (authored counter: tag, else spell or melee by camp), the
        /// authored coverage or the mode's default, expiry at the owner's next turn, and the owner's card count.
        /// The caller has counted the card that arms it (JS doPlayCard increments cardsPlayedThisCombat first).
        /// </summary>
        public static Core2CounterState Arm(Core2CombatContext ctx, Core2Combatant entity, Core2Carrier carrier)
        {
            if (entity == null || carrier?.Profile == null) return null;
            var profile = ContactProfile(carrier);
            var mode = profile.CounterMode ?? (profile.Camp == "spell" ? "spell" : "melee");
            entity.PreparationSerial += 1;
            entity.Counter = new Core2CounterState
            {
                Charges = 1, Serial = entity.PreparationSerial, Mode = mode,
                Coverage = profile.CounterCoverage ?? (ctx.Rules.CounterDefaultCoverage.TryGetValue(mode, out var coverage) ? coverage : null),
                ExpiresOnOwnerCycle = entity.OwnerCycle + 1, ArmedAtCardCount = entity.CardsPlayed, Carrier = carrier.Clone(),
            };
            return entity.Counter;
        }

        /// <summary>
        /// JS preparedCounter (configured, with a stances module): the target's Counter while it holds a charge, the target
        /// stands in Counter (or Evade with an authored Counter, or a spell Counter's prepared stance with no card accepted
        /// since), and it has not lapsed. A reaction-made hit is answered only by the Counter paid for it.
        /// </summary>
        public static Core2CounterState Prepared(Core2CombatContext ctx, Core2Combatant target, Core2Carrier carrier = null, int? paidSerial = null, bool counterDisabled = false)
        {
            var counter = target?.Counter;
            if (counter == null || counter.Charges <= 0 || carrier?.CombatReaction == true && paidSerial != counter.Serial || counterDisabled) return null;
            var stance = target.Stance?.Maneuver;
            var authoredCounter = counter.Carrier?.Profile?.Maneuver == "counter";
            var coreSpell = IsSpellCounter(counter.Carrier) && ArmsAsCoreCounter(ctx.Rules, counter.Carrier);
            if (coreSpell && counter.ArmedAtCardCount != target.CardsPlayed) return null;
            var spellCounter = coreSpell && stance == Core2Stances.ConfiguredStance(counter.Carrier);
            if (stance != "counter" && !(stance == "evade" && authoredCounter) && !spellCounter) return null;
            if (target.OwnerCycle >= counter.ExpiresOnOwnerCycle) return null;
            return counter;
        }

        /// <summary>JS paidCounterContact (perHit): a paid hand Counter carries the serial it prepared; it answers its own contact.</summary>
        public static bool PaidContact(int? paidSerial, Core2CounterState counter) => paidSerial.HasValue && counter != null && paidSerial.Value == counter.Serial;

        /// <summary>
        /// JS startTacticalTurn (with startPlayerTurn / the enemy turn start's barrier reset): the owner cycle advances, the
        /// Counter lapses, Barrier is gone, and a stance that expires this cycle (Staggered) ends.
        /// </summary>
        public static void StartTacticalTurn(Core2Combatant entity)
        {
            if (entity == null) return;
            entity.Barrier = 0;
            entity.OwnerCycle += 1;
            entity.Counter = null;
            if (entity.Stance?.ExpiresOnOwnerCycle is int expiry && entity.OwnerCycle >= expiry) entity.Stance = null;
        }

        /// <summary>
        /// JS prepareTacticalCard: a Sweep played as a reaction against a physical melee attacker adds sweepReactionPoise
        /// Poise damage and sweepReactionWard Ward damage to the attacker. Null when it does not apply.
        /// </summary>
        public static (int Poise, int Ward)? ReactiveSweepRiders(Core2Rules rules, Core2Carrier sweep, Core2CombatProfile incoming, bool playedAsReaction)
        {
            if (!playedAsReaction || sweep?.Profile?.Maneuver != "sweep" || incoming == null) return null;
            var range = incoming.Range ?? (incoming.Maneuver == "ranged" || incoming.Projectile ? "ranged" : "melee");
            if (incoming.Camp != "physical" || range != "melee") return null;
            return (rules.SweepReactionPoise, rules.SweepReactionWard);
        }
    }
}
