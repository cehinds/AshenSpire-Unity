// Core2Accuracy.cs — d20 + Precision against a fixed Evasion (JS SPEC §17 2026-10-21 correction: the defender does
// not roll), the exact enumerated hit %, and natural 1 / natural 20.
//
// JS at the pin: src/engine/stanceHit.js
//   precisionStatMix   floor(0.5 DEX + 0.1 STR + 0.2 WIS + 0.2 INT + 1 × level)  (weights: fixCoreMechanics accuracy)
//   effectiveEvasion   evasionBase + DEX + baseEvasionBonus + Evade stacks × (baseAC + DEX)
//   attackHitPreview   Accuracy = floor(precisionBase + mix × (Precision stacks + 1) + basePrecisionBonus
//                      + maneuverPrecision[maneuver] + accuracyDelta / 5 + range precisionDelta);
//                      need = Evasion − Accuracy; every die (20, or 400 pairs with advantage/disadvantage) is
//                      enumerated and the shown % is floor(100 × hits / outcomes)
//   diceHit            FixCore ("strict natural"): the kept die hits on 20, misses on 1, else hits when die ≥ need
//   resolveAttackHit   rolls the 'combatAvoidance' stream; a miss against held Evade spends one Evade stack
// Range engagement (src/engine/combatEngagement.js rangeHitContact) is Core2Range.HitContact: a range-aware fight passes
// the contact it computed; without one the carrier's range is compared with the range the target holds (JS: a fight
// with no rangeState).
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>The seeded dice a live hit rolls (JS ctx.rng.int(stream, 1, 20)). Bound to the run's seeded RNG by the fight.</summary>
    public interface ICore2Dice
    {
        int Roll(string stream, int min, int max);
    }

    /// <summary>The range contact a range-aware fight computed for this hit (JS rangeHitContact: inRange, precisionDelta).</summary>
    public sealed class Core2RangeContact
    {
        public bool InRange { get; }
        public int PrecisionDelta { get; }
        public Core2RangeContact(bool inRange, int precisionDelta = 0) { InRange = inRange; PrecisionDelta = precisionDelta; }
    }

    /// <summary>Inputs a hit reads besides the two combatants (JS options + proposalTargetPlan).</summary>
    public sealed class Core2HitOptions
    {
        public Core2RangeContact Contact { get; set; }
        /// <summary>Status-rule Accuracy change in percent points (JS proposal.accuracyDelta; counts one fifth per point).</summary>
        public double AccuracyDelta { get; set; }
        /// <summary>JS proposal.unavoidable: the hit bypasses Evasion.</summary>
        public bool Unavoidable { get; set; }
        public bool EvadeDisabled { get; set; }
        /// <summary>JS carrier.counterRequiredRange (else the carrier's own CounterRequiredRange).</summary>
        public string RequiredRange { get; set; }
        /// <summary>The hit is one contact of an action whose Accuracy was rolled when it began (JS carrier.expansionContact): no second roll.</summary>
        public bool AccuracyResolved { get; set; }
        /// <summary>JS profileDamageReceipt's 'status-rules' stage: the status rules' hit plan amount (proposalHitPlan), unrounded; null for none.</summary>
        public Func<double, double> StatusRules { get; set; }
        /// <summary>JS defensiveCombatStance: the stance a target with two or more active stances defends with; null reads its stance.</summary>
        public Core2Stance DefendingStance { get; set; }
    }

    public sealed class Core2HitPreview
    {
        public string Range { get; set; }
        public string RequiredRange { get; set; }
        public bool InRange { get; set; }
        public int Accuracy { get; set; }
        public int Evasion { get; set; }
        public int Need { get; set; }
        public bool Bypass { get; set; }
        /// <summary>normal, advantage or disadvantage.</summary>
        public string Mode { get; set; }
        public int EvadeStacks { get; set; }
        public bool EvadeAttempted { get; set; }
        /// <summary>Whole percent, rounded down (JS hitChance under FixCore).</summary>
        public int HitChance { get; set; }
    }

    public sealed class Core2HitResult
    {
        public Core2HitPreview Preview { get; set; }
        public IReadOnlyList<int> Dice { get; set; } = Array.Empty<int>();
        /// <summary>The die kept (higher with advantage, lower with disadvantage); null when no die was rolled.</summary>
        public int? AttackDie { get; set; }
        public bool Hit { get; set; }
        /// <summary>accuracy or range on a miss, else null.</summary>
        public string MissReason { get; set; }
        /// <summary>"Hit", "Hit — Evade Failed" or "Missed" (JS attackHit/attackMissed label).</summary>
        public string Label { get; set; }
    }

    public static class Core2Accuracy
    {
        public const string Stream = "combatAvoidance";

        /// <summary>JS mixLevel: an enemy without a level profile counts level 0 in the Precision mix.</summary>
        public static int MixLevel(Core2Combatant entity) => entity.Side == Core2Side.Enemy && !entity.HasLevelProfile ? 0 : entity.Level;

        /// <summary>JS precisionStatMix.</summary>
        public static int PrecisionStatMix(Core2Rules rules, Core2Combatant entity) =>
            (int)Math.Floor(rules.PrecisionWeightDex * entity.Attribute("dexterity") + rules.PrecisionWeightStr * entity.Attribute("strength")
                + rules.PrecisionWeightWis * entity.Attribute("wisdom") + rules.PrecisionWeightInt * entity.Attribute("intelligence")
                + rules.PrecisionWeightLevel * MixLevel(entity));

        /// <summary>JS precisionEntryGrant: an offensive stance entry's Precision is its base grant plus the stat mix.</summary>
        public static int PrecisionEntryGrant(Core2Rules rules, Core2Combatant entity, int baseGrant) => baseGrant > 0 ? baseGrant + PrecisionStatMix(rules, entity) : 0;

        /// <summary>JS evadeStackGain: Evade stacks one Evade play adds.</summary>
        public static int EvadeStackGain(Core2Rules rules, Core2Combatant entity) =>
            rules.FixCoreEnabled ? (int)Math.Floor(rules.EvadeStackBase + rules.EvadeStackDex * entity.Attribute("dexterity")) : 0;

        /// <summary>JS effectiveEvasion (FixCore): fixed defense, whole number.</summary>
        public static int EffectiveEvasion(Core2Rules rules, Core2Combatant entity, bool evadeDisabled = false)
        {
            var dex = entity.Attribute("dexterity");
            var perStack = rules.BaseAC + dex;
            return (int)Math.Floor(rules.EvasionBase + dex + entity.BaseEvasionBonus + (evadeDisabled ? 0 : perStack * entity.StatusStacks("evade")));
        }

        /// <summary>JS hitMode: advantage and disadvantage cancel.</summary>
        public static string HitMode(Core2Combatant source, Core2Combatant target, bool evadeDisabled = false)
        {
            var advantage = source.AccuracyAdvantage || (!evadeDisabled && target.EvadeDisadvantage);
            var disadvantage = source.AccuracyDisadvantage || (!evadeDisabled && target.EvadeAdvantage);
            return advantage == disadvantage ? "normal" : advantage ? "advantage" : "disadvantage";
        }

        /// <summary>JS diceHit with FixCore's strict naturals: only the kept die's 20/1 decide.</summary>
        public static bool DiceHit(IReadOnlyList<int> dice, int need, string mode)
        {
            if (dice.Count == 0) return false;
            var die = mode == "disadvantage" ? dice.Min() : dice.Max();
            return die == 20 || die != 1 && die >= need;
        }

        /// <summary>JS attackHitPreview under FixCore; null when the hit is not between opposing sides.</summary>
        public static Core2HitPreview Preview(Core2Rules rules, Core2Combatant source, Core2Combatant target, Core2Carrier carrier, Core2HitOptions options = null)
        {
            if (source == null || target == null || ReferenceEquals(source, target) || source.Side == target.Side) return null;
            if (!rules.FixCoreEnabled) throw new InvalidOperationException("Core 2 accuracy runs only under FixCore");
            options ??= new Core2HitOptions();
            var profile = carrier?.Profile ?? new Core2CombatProfile();
            var spell = profile.Camp == "spell" || profile.Camp == "spells" || profile.Camp == "magic" || profile.Camp == "magical";
            var bypass = options.Unavoidable || spell && (carrier?.Tags.Contains("target") ?? false);
            var range = profile.Range ?? "melee";
            // A spell stance at Reach is delivery, not a held distance: melee still connects (owner bug, 2026-10-18).
            var held = target.Stance;
            var delivery = held != null && held.Camp == "spell" && held.Range == "reach";
            var requiredRange = options.RequiredRange ?? carrier?.CounterRequiredRange ?? (delivery ? "melee" : held?.Range) ?? "melee";
            var rangeOverride = profile.RangeOverride || profile.Traits.Contains("adequateReach");
            var inRange = options.Contact != null ? options.Contact.InRange
                : rangeOverride || Core2Stances.RangeIndex(range) >= 0 && Core2Stances.RangeIndex(requiredRange) >= 0 && Core2Stances.RangeIndex(range) >= Core2Stances.RangeIndex(requiredRange);
            var maneuver = Core2Stances.EffectiveManeuver(carrier) ?? "";
            var precision = source.StatusStacks("precision");
            var mix = PrecisionStatMix(rules, source) * (precision + 1);
            var accuracy = (int)Math.Floor(rules.PrecisionBase + mix + source.BasePrecisionBonus + (rules.ManeuverPrecision.TryGetValue(maneuver, out var bonus) ? bonus : 0)
                + options.AccuracyDelta / 5 + (options.Contact?.PrecisionDelta ?? 0));
            var evadeStacks = options.EvadeDisabled ? 0 : target.StatusStacks("evade");
            var evasion = EffectiveEvasion(rules, target, options.EvadeDisabled);
            var need = evasion - accuracy;
            var mode = HitMode(source, target, options.EvadeDisabled);
            int hits = 0, outcomes = mode == "normal" ? 20 : 400;
            if (inRange)
                for (var first = 1; first <= 20; first++)
                {
                    if (mode == "normal") { if (bypass || DiceHit(new[] { first }, need, mode)) hits++; }
                    else for (var second = 1; second <= 20; second++) if (bypass || DiceHit(new[] { first, second }, need, mode)) hits++;
                }
            return new Core2HitPreview
            {
                Range = range, RequiredRange = requiredRange, InRange = inRange, Accuracy = accuracy, Evasion = evasion, Need = need, Bypass = bypass,
                Mode = mode, EvadeStacks = evadeStacks, EvadeAttempted = inRange && !bypass && !options.EvadeDisabled && evadeStacks > 0,
                HitChance = 100 * hits / outcomes,
            };
        }

        /// <summary>JS resolveAttackHit: roll the kept die (two with advantage/disadvantage), decide, spend an Evade stack on a miss.</summary>
        public static Core2HitResult Resolve(Core2Rules rules, Core2Combatant source, Core2Combatant target, Core2Carrier carrier, ICore2Dice dice, Core2HitOptions options = null)
        {
            var preview = Preview(rules, source, target, carrier, options);
            if (preview == null) return null;
            var rolled = new List<int>();
            if (preview.InRange && !preview.Bypass)
            {
                if (dice == null) throw new ArgumentNullException(nameof(dice), "a live hit needs the fight's seeded dice");
                rolled.Add(dice.Roll(Stream, 1, 20));
                if (preview.Mode != "normal") rolled.Add(dice.Roll(Stream, 1, 20));
            }
            var hit = preview.InRange && (preview.Bypass || DiceHit(rolled, preview.Need, preview.Mode));
            if (!hit && preview.InRange && !preview.Bypass && preview.EvadeStacks > 0) Core2Statuses.Remove(target, "evade", 1);
            return new Core2HitResult
            {
                Preview = preview, Dice = rolled, Hit = hit,
                AttackDie = rolled.Count == 0 ? (int?)null : preview.Mode == "disadvantage" ? rolled.Min() : rolled.Max(),
                MissReason = hit ? null : preview.InRange ? "accuracy" : "range",
                Label = hit ? (preview.EvadeAttempted && !preview.Bypass ? "Hit — Evade Failed" : "Hit") : "Missed",
            };
        }
    }
}
