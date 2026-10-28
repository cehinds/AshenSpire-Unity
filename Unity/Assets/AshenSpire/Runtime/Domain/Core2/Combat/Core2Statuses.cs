// Core2Statuses.cs — status stacking, FixCore decay, and the ratings statuses change (Weak).
//
// JS at the pin:
//   src/engine/statuses.js       applyStatus/applyStatusRaw (stackMode add/refresh/unique, duration refresh),
//                                removeStatus/removeStatusRaw, decayAtTurnStart (FixCore: once per round, decayAmount
//                                off every perTurnEnd status and Evade, Precision exempt; duration statuses tick),
//                                decayAtTurnEnd (FixCore: only temporary Precision expires at its owner's turn end)
//   src/model/fixCoreMechanics.js fixCoreStatusDefinition (weak, vulnerable, staggered stack by adding)
//   src/model/combatRatings.js   ratingValue (stance bonus; Weak: floor(AR × weakARMultiplier))
//   content/source/fixCoreMechanics.json statuses: decayAmount 1, weakARMultiplier 0.75, vulnerabilityMultiplier 1.25
// The legacy-adapter status riders (procs, resistances, control) are not part of phase 5a; a status here is stacks,
// duration and the FixCore numbers above.
using System;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    public static class Core2Statuses
    {
        /// <summary>JS applyStatus: whole stacks; add, refresh (keep the larger) or unique; a duration status refreshes its duration.</summary>
        public static void Apply(Core2CombatContext ctx, Core2Combatant target, string statusId, int stacks, bool persistent = false)
        {
            if (target == null || !target.Alive) return;
            var def = ctx.Rules.Status(statusId);
            var before = target.StatusStacks(statusId);
            var amount = stacks;
            if (amount <= 0 && def.StackMode != "unique") return;
            if (!target.Statuses.TryGetValue(statusId, out var inst)) target.Statuses[statusId] = inst = new Core2StatusInstance();
            switch (def.StackMode)
            {
                case "add": inst.Stacks += amount; break;
                case "refresh": inst.Stacks = Math.Max(inst.Stacks, amount); break;
                case "unique": inst.Stacks = 1; break;
            }
            if (def.Decay == Core2StatusDecay.Duration) inst.Duration = def.Duration;
            // Persistence belongs to the granting effect (a trait:persistent Precision grant), not every later application.
            if (persistent && statusId == "precision") inst.PersistentStacks = Math.Min(inst.Stacks, inst.PersistentStacks + Math.Max(0, inst.Stacks - before));
        }

        /// <summary>JS removeStatus: take <paramref name="amount"/> stacks off (all of them when null); at 0 the status is gone.</summary>
        public static void Remove(Core2Combatant target, string statusId, int? amount = null)
        {
            if (!target.Statuses.TryGetValue(statusId, out var inst)) return;
            if (amount.HasValue)
            {
                inst.Stacks = Math.Max(0, inst.Stacks - amount.Value);
                inst.PersistentStacks = Math.Min(inst.PersistentStacks, inst.Stacks);
                if (inst.Stacks > 0) return;
            }
            target.RemoveStatusEntry(statusId);
        }

        /// <summary>
        /// JS decayAtTurnStart under FixCore: once per round per owner, every perTurnEnd status and Evade lose
        /// decayAmount stacks (Precision never decays here); duration statuses tick down and expire at 0.
        /// </summary>
        public static void DecayAtTurnStart(Core2CombatContext ctx, Core2Combatant entity)
        {
            if (!ctx.Rules.FixCoreEnabled || entity.DecayRound == ctx.Turn) return;
            entity.DecayRound = ctx.Turn;
            foreach (var id in entity.Statuses.Keys.ToList())
            {
                var decay = ctx.Rules.HasStatus(id) ? ctx.Rules.Status(id).Decay : Core2StatusDecay.None;
                if (id != "precision" && (decay == Core2StatusDecay.PerTurnEnd || id == "evade")) Remove(entity, id, ctx.Rules.StatusDecayAmount);
                else if (decay == Core2StatusDecay.Duration)
                {
                    var inst = entity.Statuses[id];
                    inst.Duration -= 1;
                    if (inst.Duration <= 0) Remove(entity, id);
                }
            }
        }

        /// <summary>JS decayAtTurnEnd under FixCore: temporary Precision (stacks beyond the persistent ones) expires.</summary>
        public static void DecayAtTurnEnd(Core2CombatContext ctx, Core2Combatant entity)
        {
            if (!ctx.Rules.FixCoreEnabled) return;
            if (!entity.Statuses.TryGetValue("precision", out var precision)) return;
            var temporary = Math.Max(0, precision.Stacks - precision.PersistentStacks);
            if (temporary > 0) Remove(entity, "precision", temporary);
        }

        /// <summary>
        /// JS ratingValue: base rating + stance rating bonus, then Weak takes AR to floor(AR × weakARMultiplier).
        /// (JS also adds ratingsRules status bonuses; the Core 2 profile defines none.)
        /// </summary>
        public static int RatingValue(Core2Rules rules, Core2Combatant entity, string id)
        {
            var value = entity.Rating(id) + Core2Stances.StanceRatingBonus(rules, entity, id);
            if (rules.FixCoreEnabled && id == "ar" && entity.StatusStacks("weak") > 0) value = (int)Math.Floor(value * rules.WeakARMultiplier);
            return Math.Max(0, value);
        }
    }
}
