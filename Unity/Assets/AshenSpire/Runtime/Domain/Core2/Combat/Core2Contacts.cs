// Core2Contacts.cs — one committed attack against one defender, contact by contact, with the defender's Counter.
// "5 × 3" is three independent contacts (2026-10-22 amendment): under Core 2's perHit contact policy each contact begins
// on its own (its own Accuracy roll and Counter interception), lands through the defender's Block and Poise, and
// completes (the Counter's return) before the next contact begins. A Sweep or other multi-target action is resolved per
// defender by the caller (its range plan is one per action: Core2Range.ActionPlan).
//
// JS at the pin:
//   src/engine/combatExpansionActions.js  enqueueExpandedAction / contactActions (perHit: one contact per unit,
//                                         completeContact after each)
//   src/engine/combatMatchups.js          previewTacticalAction (configured: coverage per contact), beginTacticalAction
//                                         (configured + FixCore: one resolveAttackHit, consume the covered Counter),
//                                         completeTacticalAction (Core 2: the return), stillPrepared/combatCounterStaggered
//   src/engine/actions.js                 applyAttackDamage → applyProfileDamage (counterBlock before absorption; no
//                                         second roll for a contact), poiseDamage op → drainFixCorePoise
//   src/engine/counterRule.js             counterBlockBonus, counterReturnForReceipt, counterReturnSpent
//   src/engine/combatEngagement.js        authorizeCounterRange (a return's own Advance, paid by the defender, quoted in
//                                         the incoming contact's frozen engagement; unaffordable → no return, defense kept)
// The live fight offers reactions between contacts (Core2FightReactions.cs); a Counter paid as one passes its serial in.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>Optional inputs of one attack action.</summary>
    public sealed class Core2ActionOptions
    {
        /// <summary>The fight's engagement state; null resolves range from the held stance (a fight with no range state).</summary>
        public Core2RangeState Range { get; set; }
        /// <summary>The attacker's accepted plan for this action (JS carrier.rangePlan); null quotes each contact unplanned.</summary>
        public Core2RangePlan Plan { get; set; }
        /// <summary>The attacker's committed source per contact (its coverage); null reads the carrier's own range.</summary>
        public Func<int, Core2RangeSource> AttackSource { get; set; }
        /// <summary>The defender's Counter return source (its weapon); null reads the Counter card's own range.</summary>
        public Core2RangeSource ReturnSource { get; set; }
        /// <summary>A paid per-contact hand Counter's serial (JS carrier.manualCounterSerial).</summary>
        public int? PaidCounterSerial { get; set; }
        /// <summary>The defender's Counter is disabled by a control status (JS restrictions.counterDisabled).</summary>
        public bool CounterDisabled { get; set; }
    }

    /// <summary>A Counter's answer to one contact.</summary>
    public sealed class Core2CounterExchange
    {
        public Core2CounterReturn Payload { get; set; }
        /// <summary>The return's own range plan (null without range); its Advance SP was paid by the defender.</summary>
        public Core2RangePlan Plan { get; set; }
        /// <summary>The defender could not pay the return's Advance: no return, the interception stands.</summary>
        public bool RangeRefused { get; set; }
        public Core2HitResult Accuracy { get; set; }
        public Core2HitOutcome Hit { get; set; }
        public int PoiseSpent { get; set; }
    }

    public sealed class Core2ContactResult
    {
        public int Index { get; set; }
        public Core2HitResult Accuracy { get; set; }
        /// <summary>Null when the contact missed (or the defender had already fallen).</summary>
        public Core2HitOutcome Hit { get; set; }
        public bool Covered { get; set; }
        public bool CounterConsumed { get; set; }
        /// <summary>The Block an advantaged covered hit added before it was absorbed.</summary>
        public int CounterBlock { get; set; }
        /// <summary>The hit as it met the defenses (the Counter return reads it).</summary>
        public int Incoming { get; set; }
        public Core2CounterExchange Return { get; set; }
    }

    public static class Core2Contacts
    {
        /// <summary>
        /// Resolve <paramref name="contacts"/> (each contact's base damage, in order) from <paramref name="source"/> into
        /// <paramref name="target"/>. Each contact: begin (the defender's prepared Counter and whether it covers this
        /// contact; one Accuracy roll; a covered contact that connects consumes the Counter's charge), land (an advantaged
        /// covered contact first adds the Counter's Block, then the receipt applies with no second roll), complete (a still
        /// prepared Counter answers a landed covered contact, once per owner cycle unless paid).
        /// </summary>
        public static List<Core2ContactResult> ResolveAttack(Core2CombatContext ctx, Core2Combatant source, Core2Combatant target, Core2Carrier carrier,
            IReadOnlyList<int> contacts, ICore2Dice dice, Core2ActionOptions options = null)
        {
            options ??= new Core2ActionOptions();
            var rules = ctx.Rules;
            var results = new List<Core2ContactResult>();
            var profile = Core2Counters.ContactProfile(carrier);
            for (var index = 0; index < (contacts?.Count ?? 0); index++)
            {
                if (target == null || !target.Alive || source == null || !source.Alive) break;
                var result = new Core2ContactResult { Index = index };
                results.Add(result);
                var hitOptions = new Core2HitOptions();
                if (options.Range != null)
                {
                    var attack = options.AttackSource?.Invoke(index) ?? Core2RangeSource.For(carrier, Core2Range.ActorKey(target));
                    attack.TargetKey ??= Core2Range.ActorKey(target);
                    hitOptions.Contact = Core2Range.HitContact(options.Range, Core2Range.ActorKey(source), attack, options.Plan, options.Plan == null ? null : attack.ContactId);
                }
                // ---- begin (previewTacticalAction + beginTacticalAction, configured) ----
                var counter = Core2Counters.Prepared(ctx, target, carrier, options.PaidCounterSerial, options.CounterDisabled);
                var planned = Core2Damage.Receipt(rules, source, target, contacts[index], carrier, hitOptions);
                result.Covered = counter != null && Core2Counters.CoverageMatches(counter.Coverage, profile, "damage") && planned.Raw > 0;
                var snapshot = result.Covered ? counter.Clone() : null;
                var accuracy = Core2Accuracy.Resolve(rules, source, target, carrier, dice, hitOptions);
                result.Accuracy = accuracy;
                var avoided = accuracy != null && !accuracy.Hit;
                if (result.Covered && !avoided) { target.Counter.Charges = 0; result.CounterConsumed = true; }
                // ---- land (applyAttackDamage → applyProfileDamage) ----
                if (!avoided)
                {
                    var paid = Core2Counters.PaidContact(options.PaidCounterSerial, snapshot);
                    if (result.Covered && (!Core2Counters.ReturnSpent(target) || paid) && Core2Counters.Beats(rules, profile.Maneuver))
                    {
                        var chance = Core2Damage.Receipt(rules, source, target, contacts[index], carrier, hitOptions);
                        result.CounterBlock = Core2Counters.BlockGrant(target, Core2Counters.Return(rules, chance.Incoming, true).Block);
                        target.Block += result.CounterBlock;
                    }
                    hitOptions.AccuracyResolved = true;
                    result.Hit = Core2Damage.ApplyAttack(ctx, source, target, contacts[index], carrier, dice, hitOptions);
                    result.Incoming = result.Hit.Receipt?.Incoming ?? 0;
                }
                // ---- complete (completeTacticalAction, Core 2) ----
                result.Return = Complete(ctx, source, target, profile, snapshot, result, avoided, dice, options);
            }
            return results;
        }

        private static Core2CounterExchange Complete(Core2CombatContext ctx, Core2Combatant attacker, Core2Combatant defender, Core2CombatProfile profile,
            Core2CounterState counter, Core2ContactResult contact, bool avoided, ICore2Dice dice, Core2ActionOptions options)
        {
            var rules = ctx.Rules;
            // A Poise break on the covered hit clears the Counter, but that hit still answers.
            var stillPrepared = counter != null && (defender.Counter?.Serial == counter.Serial || defender.CounterStaggeredSerial == counter.Serial);
            defender.CounterStaggeredSerial = null;
            var paid = Core2Counters.PaidContact(options.PaidCounterSerial, counter);
            if (!stillPrepared || options.CounterDisabled || avoided || profile.Traits.Contains("unreflectable") || !defender.Alive || !attacker.Alive
                || Core2Counters.ReturnSpent(defender) && !paid) return null;
            // Core 2 (SPEC §17): the return is a fraction of the damage that hit; a spell Counter returns HP and Poise only.
            var payload = Core2Counters.ReturnFor(rules, new[] { (contact.Covered, contact.Incoming, profile.Maneuver) });
            var exchange = new Core2CounterExchange { Payload = payload };
            var returnCarrier = counter.Carrier.Clone();
            returnCarrier.CombatReaction = true; returnCarrier.SkipRatingBonus = true;
            returnCarrier.Profile.CounterReturnAttack = true;
            // The covered contacts' farthest range, from the incoming action's range.
            var required = profile.Range ?? "melee";
            returnCarrier.CounterRequiredRange = required;
            var returnOptions = new Core2HitOptions();
            if (options.Range != null)
            {
                // With the policy on the return is priced only once it has something to send (JS deferredRangeReturn):
                // an empty return spends no Advance and no cycle.
                if (options.Range.Policy.Enabled && !(payload.Hp > 0 || payload.Poise > 0)) return exchange;
                var plan = AuthorizeCounterRange(options.Range, defender, attacker, returnCarrier, options);
                if (plan == null) { exchange.RangeRefused = true; return exchange; }
                exchange.Plan = plan;
                var source = options.ReturnSource ?? Core2RangeSource.For(returnCarrier, Core2Range.ActorKey(attacker));
                source.TargetKey ??= Core2Range.ActorKey(attacker);
                returnOptions.Contact = Core2Range.HitContact(options.Range, Core2Range.ActorKey(defender), source, plan);
            }
            defender.CounterReturnedCycle = defender.OwnerCycle;
            if (!(payload.Hp > 0 || payload.Poise > 0)) return exchange;
            // The return resolves as one action: one Accuracy roll, then its HP (a flat chain), then its Poise drain.
            exchange.Accuracy = Core2Accuracy.Resolve(rules, defender, attacker, returnCarrier, dice, returnOptions);
            if (exchange.Accuracy != null && !exchange.Accuracy.Hit) return exchange;
            returnOptions.AccuracyResolved = true;
            if (payload.Hp > 0) exchange.Hit = Core2Damage.ApplyAttack(ctx, defender, attacker, payload.Hp, returnCarrier, dice, returnOptions);
            if (payload.Poise > 0) exchange.PoiseSpent = Core2Damage.DrainPoise(ctx, attacker, payload.Poise);
            return exchange;
        }

        /// <summary>
        /// JS authorizeCounterRange (automatic return, no reservation): quote the return in the incoming contact's frozen
        /// engagement; the defender pays its Advance SP when it can, else the return is refused (null). With the policy off
        /// the plan costs nothing.
        /// </summary>
        public static Core2RangePlan AuthorizeCounterRange(Core2RangeState state, Core2Combatant defender, Core2Combatant attacker, Core2Carrier returnCarrier, Core2ActionOptions options)
        {
            var attackerKey = Core2Range.ActorKey(attacker);
            var source = options.ReturnSource ?? Core2RangeSource.For(returnCarrier, attackerKey);
            source.TargetKey ??= attackerKey;
            var incoming = options.Plan?.Targets.FirstOrDefault(row => row.TargetKey == Core2Range.ActorKey(defender));
            var context = incoming == null ? null : new Core2RangePlan { SourceKey = options.Plan.SourceKey, Targets = new List<Core2RangeTarget> { incoming } };
            var plan = Core2Range.ActionPlan(state, Core2Range.ActorKey(defender), new[] { source }, "advance", context);
            if (defender.Stamina < plan.AdvanceSp) return null;
            defender.Stamina -= plan.AdvanceSp;
            return plan;
        }
    }
}
