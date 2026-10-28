// Core2FightActions.cs — how a committed card or move runs in a Core 2 fight: its action group, one contact at a time
// (the perHit policy), each contact's begin (Accuracy roll, Counter interception), land (the damage receipt) and
// complete (the Counter's return), and the effect opcodes Core 2 content uses.
//
// JS at the pin:
//   src/engine/combatExpansionActions.js enqueueExpandedAction (contact planning per target and hit), contactActions
//                                    (perHit: one unit and one completion per contact), activateCombatContact,
//                                    beginExpandedAction, completeExpandedAction, completeContact
//   src/engine/combatMatchups.js     previewTacticalAction (configured: coverage, amounts), beginTacticalAction (FixCore:
//                                    resolveAttackHit, consume the covered Counter), completeTacticalAction (Core 2 return)
//   src/engine/actions.js            executeAction / runOpcode (damage, block, applyStatus, poiseDamage, draw, heal,
//                                    restoreMana, restoreStamina, loseHp, moveEngagement, removeStatus), applyAttackDamage /
//                                    applyProfileDamage, drainFixCorePoise, fixCorePoiseBreak, fixCoreStagger, staggerEnemy,
//                                    gainBlock / gainBlockAmount, applyLoseHp, applyHeal, afterHpChange
//   src/engine/stanceHit.js          resolveAttackHit (attackHit / attackMissed; a miss spends an Evade stack)
//   src/engine/counterRule.js        counterBlockBonus, counterReturnForReceipt, counterReturnSpent
//   src/engine/combatReactions.js    beforeReactionAction (the per-contact telegraph and the switched-off report),
//                                    startQueuedEnemy (enemyMoveStarted / combatHitStarted / combatCounterTriggered)
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>One planned contact of an action on one target (JS group.targets[key].contacts[i]).</summary>
    public sealed class Core2ContactPlan
    {
        public Core2CombatProfile Profile { get; set; }
        /// <summary>The contact's damage before defenses (JS profileDamageReceipt raw); 0 for a status or impact contact.</summary>
        public double Amount { get; set; }
        /// <summary>null for damage, "status" or "impact" for a pressure contact.</summary>
        public string Effect { get; set; }
        public double Pressure { get; set; }
        public string RangeContactId { get; set; }
    }

    /// <summary>JS previewTacticalAction / beginTacticalAction receipt (configured profile) for one target of one contact.</summary>
    public sealed class Core2TacticalReceipt
    {
        public bool Begun, Avoided, Connected = true, Completed, CounterEligible, CounterDisabledAtStart;
        public Core2CounterState Counter;
        public List<bool> Covered = new List<bool>();
        public List<Core2CombatProfile> Profiles = new List<Core2CombatProfile>();
        public Core2CombatProfile Profile;
        public List<int> Amounts = new List<int>();
        public List<int> Incomings = new List<int>();
        public int Amount, HpLoss, Blocked;
        /// <summary>JS recordTacticalStatus: buildup accepted (pending plus active stacks) and resisted (or spent on Ward) by this target.</summary>
        public double StatusAccepted, StatusResisted;
        public Core2HitResult CounterAttack;
    }

    public sealed class Core2GroupTarget
    {
        public Core2Combatant Entity;
        public List<Core2ContactPlan> Contacts = new List<Core2ContactPlan>();
        public Core2TacticalReceipt Receipt;
        /// <summary>JS row.interactions: what the status hooks (thaw, shatter, reveal) read off this target's contacts.</summary>
        public Core2StatusInteractions Interactions;
    }

    /// <summary>JS row.interactions: the first contact's profile, and whether a landed contact carried Fire, Blunt or a reveal (Sweep, Sacred, Holy).</summary>
    public sealed class Core2StatusInteractions
    {
        public Core2CombatProfile Profile;
        public bool ContainsFire, ContainsBlunt, ContainsReveal;
    }

    /// <summary>A committed card or move (JS expansion group): its carrier, its targets and, under perHit, its contact sequence.</summary>
    public sealed class Core2ActionGroup
    {
        public string Key;
        public string RootKey;
        public Core2Combatant Source;
        public Core2Carrier Carrier;
        public Dictionary<string, Core2GroupTarget> Targets = new Dictionary<string, Core2GroupTarget>(StringComparer.Ordinal);
        public bool Begun, Cancelled, PayloadStarted, ContactTelegraphed;
        public JObject EnemyMoveStart;
        public JObject CounterReturn;
        public List<string> ReactionNoticed = new List<string>();
        public List<string> ReactionAnswered = new List<string>();
        // perHit contact sequence
        public List<Dictionary<string, Core2GroupTarget>> Contacts;
        public int ContactIndex = -1;
        public Dictionary<string, Core2GroupTarget> RootTargets;
        public Dictionary<int, bool> Gates = new Dictionary<int, bool>();
        public Core2RangePlan RangePlan;
        /// <summary>JS group.kind: an enemy reaction plan's move intent (enemyMoveStarted kind).</summary>
        public string Kind;
        /// <summary>JS group.enemyWindows: how many enemy reaction windows this root (contact) has opened.</summary>
        public int? EnemyWindows;
        /// <summary>JS group.manualCounterSerials[seat]: the serial of the Counter the player paid as a reaction to this contact.</summary>
        public int? ManualCounterSerial;
    }

    public sealed partial class Core2Fight
    {
        private static bool IsHostileTarget(string target) => target == "enemy" || target == "allEnemies" || target == "otherEnemies" || target == "randomEnemy" || target == "player";

        /// <summary>JS resolveRawTargets for one action.</summary>
        private List<Core2Combatant> ResolveTargets(Core2FightAction action, string spec)
        {
            switch (spec)
            {
                case null: return new[] { action.Target ?? action.Source }.Where(e => e != null).ToList();
                case "self": return new[] { action.Source }.Where(e => e != null).ToList();
                case "owner": return new[] { action.Owner ?? action.Source }.Where(e => e != null).ToList();
                case "player": return new List<Core2Combatant> { Player };
                case "enemy":
                    if (action.Target != null && action.Target.Side == Core2Side.Enemy && action.Target.Alive) return new List<Core2Combatant> { action.Target };
                    if (action.Source != null && action.Source.Side == Core2Side.Enemy) return new List<Core2Combatant> { Player };
                    return Enemies.Where(e => e.Alive).Take(1).ToList();
                case "allEnemies": return Enemies.Where(e => e.Alive).ToList();
                case "otherEnemies":
                {
                    var named = (string)action.Event?.Data["targetId"];
                    return Enemies.Where(e => e.Alive && e != action.Target && e.Id != named).ToList();
                }
            }
            throw new NotSupportedException("Effect target '" + spec + "' is not ported");
        }

        /// <summary>A formula value for an action (JS evalNum): numbers, add/mul and the evadeStackGain metric.</summary>
        private double Eval(Core2FightAction action, JToken value, double fallback)
        {
            if (value == null || value.Type == JTokenType.Null) return fallback;
            return Core2Formula.Evaluate(value, (metric, of) =>
            {
                var entity = of == "self" || of == "owner" || of == null ? action.Source : action.Target;
                if (metric == "evadeStackGain") return Core2Accuracy.EvadeStackGain(Rules, entity);
                throw new NotSupportedException("Formula metric '" + metric + "' is not ported");
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Groups (JS enqueueExpandedAction, contactActions)
        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// JS enqueueExpandedAction: plan every targeted effect's contacts on the committed state, accept the action's range
        /// plan (paying its Advance), then queue the actions — one unit and one completion per contact under perHit — and the
        /// group's final completion.
        /// </summary>
        private Core2ActionGroup EnqueueGroup(List<Core2FightAction> actions, Core2Combatant source, Core2Combatant target, Core2Carrier carrier, JObject enemyMoveStart = null, JObject counterReturn = null,
            bool prepaidRange = false, Core2RangePlan acceptedPlan = null, bool? charge = null)
        {
            // JS: a reaction-made carrier (an enemy reaction's plan) delivers as an Attack; a Counter return keeps its stance.
            if (carrier?.CombatReaction == true && carrier.Profile?.CounterReturnAttack != true)
            {
                carrier = carrier.Clone();
                carrier.Profile ??= new Core2CombatProfile();
                carrier.Profile.Maneuver = "attack";
                foreach (var action in actions) action.Card = carrier;
            }
            var group = new Core2ActionGroup { Source = source, Carrier = carrier, EnemyMoveStart = enemyMoveStart, CounterReturn = counterReturn };
            group.Key = group.RootKey = CombatKey + ":" + (++_expansionSerial);
            group.RangePlan = AcceptRange(source, actions, carrier, charge ?? (!prepaidRange && counterReturn == null), acceptedPlan);
            if (group.RangePlan != null) group.RangePlan.RootId = group.RootKey;
            var plans = new Dictionary<Core2FightAction, List<List<(string Key, int Index)>>>();
            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                action.Group = group;
                var eff = action.Effect;
                var op = (string)eff["op"];
                var targeted = IsHostileTarget((string)eff["target"]) || op == "removeStatus" || op == "loseHp" || op == "stagger";
                if (!targeted && op != "damage" && op != "buildup" && op != "applyStatus" && op != "poiseDamage" && op != "wardDamage") continue;
                if (eff["oncePerTurn"] != null || eff["if"] != null && op == "damage") throw new NotSupportedException("A conditional or once-per-turn contact is not ported");
                var repeat = Math.Max(0, (int)Math.Floor(Eval(action, eff["repeat"], 1)));
                var hits = op == "damage" ? Math.Max(0, (int)Math.Floor(Eval(action, eff["hits"], 1))) : 1;
                var batches = new List<List<(string, int)>>();
                for (var r = 0; r < repeat; r++) for (var h = 0; h < hits; h++)
                {
                    var contacts = new List<(string, int)>();
                    foreach (var entity in ResolveTargets(action, (string)eff["target"]))
                    {
                        if (!entity.Alive) continue;
                        if (eff["if"] is JObject cond && !Predicate(cond, action.Owner ?? action.Source, entity, null, action)) continue;
                        if (entity == source || entity.Side == source?.Side && op != "damage") continue;
                        var key = entity.Id;
                        if (!group.Targets.TryGetValue(key, out var row)) group.Targets[key] = row = new Core2GroupTarget { Entity = entity };
                        var profile = (action.Card ?? carrier)?.Profile?.Clone() ?? new Core2CombatProfile();
                        var baseAmount = Eval(action, eff["amount"] ?? eff["stacks"], 1);
                        var amount = op == "damage" ? Core2Damage.Receipt(Rules, source, entity, (int)Math.Floor(baseAmount), action.Card ?? carrier, HitOptions(source, entity, action.Card ?? carrier, group.RangePlan, null)).Raw : 0;
                        var plan = new Core2ContactPlan { Profile = profile, Amount = amount };
                        if (group.RangePlan != null) plan.RangeContactId = index + ":" + r + ":" + h + ":" + (source?.Side == Core2Side.Enemy ? "player:player" : "enemy:" + entity.Id);
                        if (op != "damage") { plan.Effect = op == "poiseDamage" || op == "wardDamage" ? "impact" : "status"; plan.Pressure = baseAmount; }
                        var index2 = row.Contacts.Count;
                        row.Contacts.Add(plan);
                        contacts.Add((key, index2));
                    }
                    batches.Add(contacts);
                }
                plans[action] = batches;
                action.ContactRefs = batches.SelectMany(batch => batch).ToList();
            }
            _pendingExpansionActions++;
            foreach (var queued in ContactActions(group, actions, plans)) Enqueue(queued);
            Enqueue(new Core2FightAction { Effect = new JObject { ["op"] = "completeCombatAction" }, Source = source, Owner = source, Target = target, Card = carrier, Group = group, GroupCompletion = true, MoveId = actions.FirstOrDefault()?.MoveId });
            // A reaction offer check reads the planned contacts of the card it tries (enemy reactions).
            _groupSink?.Add(group);
            return group;
        }

        /// <summary>JS contactActions (perHit): each damage hit becomes its own unit and completion; status effects ride the adjacent contact.</summary>
        private List<Core2FightAction> ContactActions(Core2ActionGroup group, List<Core2FightAction> actions, Dictionary<Core2FightAction, List<List<(string Key, int Index)>>> plans)
        {
            if (group.Carrier?.CombatReaction == true) return actions;
            var sequence = new List<Dictionary<string, Core2GroupTarget>>();
            var queued = new List<Core2FightAction>();
            var ancillary = new List<(Core2FightAction Action, int Index)>();
            for (var effectIndex = 0; effectIndex < actions.Count; effectIndex++)
            {
                var action = actions[effectIndex];
                plans.TryGetValue(action, out var batches);
                if ((string)action.Effect["op"] != "damage" || batches == null || batches.Count == 0)
                {
                    if (batches != null && batches.Count > 0) ancillary.Add((action, Math.Max(0, sequence.Count - 1)));
                    queued.Add(action);
                    continue;
                }
                for (var index = 0; index < batches.Count; index++)
                {
                    var targets = new Dictionary<string, Core2GroupTarget>(StringComparer.Ordinal);
                    var refs = new List<(string, int)>();
                    foreach (var (key, contactIndex) in batches[index])
                    {
                        var original = group.Targets[key];
                        if (!targets.TryGetValue(key, out var row)) targets[key] = row = new Core2GroupTarget { Entity = original.Entity };
                        refs.Add((key, row.Contacts.Count));
                        row.Contacts.Add(original.Contacts[contactIndex]);
                    }
                    sequence.Add(targets);
                    var effect = (JObject)action.Effect.DeepClone(); effect["hits"] = 1; effect["repeat"] = 1;
                    var unit = Clone(action); unit.Effect = effect; unit.ContactIndex = sequence.Count - 1; unit.ContactEffectIndex = effectIndex; unit.ContactEffectFirst = index == 0;
                    unit.ContactRefs = refs;
                    var completion = Clone(unit); completion.Effect = new JObject { ["op"] = "completeCombatAction" }; completion.ContactCompletion = true;
                    queued.Add(unit); queued.Add(completion);
                }
            }
            if (sequence.Count == 0) return actions;
            foreach (var (action, index) in ancillary)
            {
                var refs = new List<(string, int)>();
                foreach (var batch in plans[action]) foreach (var (key, contactIndex) in batch)
                {
                    var targets = sequence[index];
                    var original = group.Targets[key];
                    if (!targets.TryGetValue(key, out var row)) targets[key] = row = new Core2GroupTarget { Entity = original.Entity };
                    refs.Add((key, row.Contacts.Count));
                    row.Contacts.Add(original.Contacts[contactIndex]);
                }
                action.ContactRefs = refs;
            }
            group.Contacts = sequence;
            group.RootTargets = group.Targets;
            ActivateContact(queued.FirstOrDefault(a => a.ContactIndex == 0));
            return queued;
        }

        private static Core2FightAction Clone(Core2FightAction a) => new Core2FightAction
        {
            Effect = a.Effect, Source = a.Source, Owner = a.Owner, Target = a.Target, Card = a.Card, MoveId = a.MoveId, Preparing = a.Preparing,
            TriggeredBy = a.TriggeredBy, Event = a.Event, Group = a.Group, ContactIndex = a.ContactIndex, ContactCompletion = a.ContactCompletion,
            GroupCompletion = a.GroupCompletion, ContactEffectFirst = a.ContactEffectFirst, ContactEffectIndex = a.ContactEffectIndex,
            CounterReaction = a.CounterReaction, EnergySpent = a.EnergySpent, ManaSpent = a.ManaSpent, ContactRefs = a.ContactRefs.ToList(),
        };

        /// <summary>JS activateCombatContact: entering a new contact index makes its targets the group's and resets its begin state.</summary>
        private void ActivateContact(Core2FightAction action)
        {
            var group = action?.Group;
            if (group?.Contacts == null || action.ContactIndex < 0 || action.ContactCompletion || group.ContactIndex == action.ContactIndex) return;
            group.ContactIndex = action.ContactIndex;
            group.Key = group.RootKey + ":hit:" + action.ContactIndex;
            group.Targets = group.Contacts[action.ContactIndex];
            group.Begun = false;
            group.PayloadStarted = false;
            group.ContactTelegraphed = false;
            group.ReactionAnswered.Clear();
            group.ReactionNoticed.Clear();
            group.ManualCounterSerial = null;
            group.EnemyWindows = null;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The enemy move's start (JS startQueuedEnemy); the reaction window itself is Core2FightReactions.cs
        // ---------------------------------------------------------------------------------------------------------------

        private static bool Positive(Core2ContactPlan c) => c != null && (c.Amount > 0 || c.Pressure > 0);

        /// <summary>JS startQueuedEnemy: the first action of an enemy move (or a Counter return) announces it once per contact.</summary>
        private void StartQueuedEnemy(Core2FightAction action)
        {
            var group = action.Group;
            if (group == null) return;
            if (group.Contacts != null && (!action.Source.Alive || action.Source.SkipNextTurn)) group.Cancelled = true;
            if (group.EnemyMoveStart == null && group.CounterReturn == null) return;
            if (group.Cancelled || group.PayloadStarted) return;
            group.PayloadStarted = true;
            var source = action.Source;
            group.Cancelled = !source.Alive || source.SkipNextTurn;
            if (group.Cancelled) return;
            if (group.Contacts != null && !group.Targets.Values.Any(r => r.Contacts.Any(Positive) && r.Entity.Alive)) return;
            JObject Prepared(JObject start)
            {
                var data = (JObject)start.DeepClone();
                if (group.ContactTelegraphed) { data["contactPrepared"] = true; data["contactKey"] = group.Key; }
                return data;
            }
            if (group.CounterReturn != null) { Emit("combatCounterTriggered", Prepared(group.CounterReturn)); return; }
            if (group.Contacts != null && group.ContactIndex > 0)
            {
                var hit = Prepared(group.EnemyMoveStart); hit["contactIndex"] = group.ContactIndex; hit["contactCount"] = group.Contacts.Count;
                Emit("combatHitStarted", hit);
                return;
            }
            source.PerformedMoves.Add((string)group.EnemyMoveStart["moveId"]);
            Emit("enemyMoveStarted", Prepared(group.EnemyMoveStart));
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Execution (JS executeAction / runOpcode)
        // ---------------------------------------------------------------------------------------------------------------

        private void ExecuteAction(Core2FightAction action)
        {
            var op = (string)action.Effect["op"];
            if (op == "completeCombatAction") { WithAction(action, () => CompleteGroup(action)); return; }
            if (Result != null) return;
            var group = action.Group;
            if (group?.Contacts != null && !action.ContactEffectFirst && !(group.Gates.TryGetValue(action.ContactEffectIndex, out var gate) && gate)) return;
            if (group?.Cancelled == true) return;
            if (group != null) WithAction(action, () => BeginGroup(group, action.Source));
            if (action.CounterReaction && (!action.Source.Alive || action.CounterInterrupted)) return;
            var eff = action.Effect;
            if (eff["if"] is JObject cond && action.ContactEffectFirst && !Predicate(cond, action.Owner ?? action.Source, action.Target, action.Event, action)) return;
            if (eff["oncePerTurn"] != null) throw new NotSupportedException("oncePerTurn effects are not ported");
            if (group?.Contacts != null && action.ContactEffectFirst) group.Gates[action.ContactEffectIndex] = true;
            var repeat = (int)Math.Floor(Eval(action, eff["repeat"], 1));
            for (var r = 0; r < repeat; r++)
            {
                WithAction(action, () => RunOpcode(action, eff));
                if (Result != null) return;
            }
        }

        private void WithAction(Core2FightAction action, Action body)
        {
            var previous = _currentAction;
            _currentAction = action;
            try { body(); } finally { _currentAction = previous; }
        }

        private void RunOpcode(Core2FightAction action, JObject eff)
        {
            var op = (string)eff["op"];
            switch (op)
            {
                case "damage":
                {
                    var hits = Math.Max(0, (int)Math.Floor(Eval(action, eff["hits"], 1)));
                    for (var h = 0; h < hits; h++)
                    {
                        var targets = action.Group?.Targets.Values.Where(r => r.Contacts.Count > 0).Select(r => r.Entity).ToList() ?? ResolveTargets(action, (string)eff["target"]);
                        if (action.Group == null) targets = ResolveTargets(action, (string)eff["target"]);
                        foreach (var t in targets)
                        {
                            if (!t.Alive) continue;
                            var amount = (int)Math.Floor(Eval(action, eff["amount"], 0));
                            ApplyAttackDamage(action, action.Source, t, amount);
                        }
                    }
                    break;
                }
                case "block":
                    foreach (var t in ResolveTargets(action, (string)eff["target"])) GainBlock(t, (int)Math.Floor(Eval(action, eff["amount"], 0)), action.Card);
                    break;
                case "gainBarrier":
                    foreach (var t in ResolveTargets(action, (string)eff["target"])) GainBlock(t, (int)Math.Floor(Eval(action, eff["amount"], 0)), action.Card);
                    break;
                case "applyStatus":
                    foreach (var t in StatusTargets(action, eff))
                    {
                        var stacks = (int)Math.Floor(Eval(action, eff["stacks"], 1));
                        ApplyStatus(t, (string)eff["status"], stacks, action.Source, action.Card?.Tags);
                        var row = action.Group?.Targets.TryGetValue(t.Id, out var r) == true ? r : null;
                        if (row?.Receipt != null) row.Receipt.StatusAccepted += stacks;
                    }
                    break;
                case "buildup":
                    // JS actions.js runOpcode 'buildup': each planned target the action did not miss takes the authored
                    // pressure (plus the Spell-stance bonus) through applyStatusPressure, after an optional chance roll on
                    // statusPressure; the receipt records what the target accepted and resisted.
                    foreach (var t in StatusTargets(action, eff))
                    {
                        if (eff["chance"] != null && eff["chance"].Type != JTokenType.Null
                            && !(bool)RollStatusChance((double)eff["chance"], (bool?)eff["advantage"] ?? false, (bool?)eff["disadvantage"] ?? false, "statusPressure")["success"]) continue;
                        var authored = Eval(action, eff["amount"], 0, t);
                        // JS ashenBlightSpellBuildup (Cinder Sight) adds 1 only for an Ashen Blight owner's Fire/Frost/Lightning
                        // Burn/Frost/Paralysis buildup; enemies carry no Blight, and no Core 2 card builds those statuses.
                        if (action.Source?.Side == Core2Side.Player && (action.Card?.Profile?.School == "fire" || action.Card?.Profile?.School == "frost" || action.Card?.Profile?.School == "lightning")
                            && ((string)eff["status"] == "burn" || (string)eff["status"] == "frost" || (string)eff["status"] == "paralysis"))
                            throw new NotSupportedException("Ashen Blight Cinder Sight buildup (ashenBlightSpellBuildup) is not ported");
                        var pressure = ApplyStatusPressure(t, (string)eff["status"], authored + SpellAuthoredStatusBonus(action, authored), action.Source,
                            (string)eff["camp"] ?? action.Card?.Profile?.Camp, action.Group?.Key ?? Turn + ":" + (action.Card?.InstanceId ?? "effect"), (string)eff["recoveryProfile"]);
                        var row = action.Group?.Targets.TryGetValue(t.Id, out var r) == true ? r : null;
                        if (row?.Receipt != null)
                        {
                            row.Receipt.StatusAccepted += pressure.AcceptedBuildup + pressure.ActiveAdded;
                            row.Receipt.StatusResisted += pressure.Resisted + pressure.WardSpent;
                        }
                    }
                    break;
                case "removeStatus":
                    foreach (var t in ResolveTargets(action, (string)eff["target"]))
                        RemoveStatus(t, (string)eff["status"], eff["amount"] != null ? Math.Max(0, (int)Math.Floor(Eval(action, eff["amount"], 0))) : (int?)null, "consumed");
                    break;
                case "poiseDamage":
                    foreach (var t in ResolveTargets(action, (string)eff["target"]))
                    {
                        var row = action.Group?.Targets.TryGetValue(t.Id, out var r) == true ? r : null;
                        if (row?.Receipt?.Avoided == true) continue;
                        var authored = Eval(action, eff["amount"], 0);
                        var amount = action.Card?.Profile?.CounterReturnAttack == true ? Math.Max(0, (int)Math.Floor(authored))
                            : (int)Math.Ceiling(authored * MatchupPoiseMultiplier(action.Card, t) * StancePressureMultiplier(action.Source, t, action.Card, row?.Receipt?.CounterAttack));
                        // JS dealPoiseDamage(ctx, t, amount): the drain names no source (its break event carries none).
                        DrainPoise(null, t, amount);
                    }
                    break;
                case "draw":
                    DrawCards(Math.Max(0, (int)Math.Floor(Eval(action, eff["amount"], 1))));
                    break;
                case "addCard":
                {
                    // JS actions.js 'addCard': a generated card ('gen' ids) into a pile; a full hand discards it instead;
                    // position top, bottom or (default) a random slot drawn from the shuffle stream.
                    var cardId = (string)eff["card"];
                    Content.Card(cardId);
                    var count = Math.Max(1, (int)Eval(action, eff["count"], 1));
                    var pileName = (string)eff["pile"] ?? "discard";
                    for (var i = 0; i < count; i++)
                    {
                        var instance = new Core2CardInstance { InstanceId = "gen" + (++_generatedCards), CardId = cardId };
                        var pile = pileName switch { "draw" => Draw, "discard" => Discard, "hand" => Hand, "exhaust" => Exhaust, _ => throw new InvalidOperationException("Unknown pile '" + pileName + "'") };
                        if (pileName == "hand" && pile.Count >= HandMax)
                        {
                            Discard.Add(instance);
                            Emit("cardDiscarded", new JObject { ["cardInstanceId"] = instance.InstanceId, ["cardId"] = cardId, ["reason"] = "handFull" });
                            continue;
                        }
                        var position = (string)eff["position"] ?? "random";
                        if (position == "top") pile.Insert(0, instance);
                        else if (position == "bottom") pile.Add(instance);
                        else pile.Insert((int)Math.Floor(Rng.Float("shuffle") * (pile.Count + 1)), instance);
                    }
                    break;
                }
                case "restoreStamina":
                    foreach (var t in ResolveTargets(action, (string)eff["target"]))
                    {
                        var amount = Math.Max(0, Math.Min(t.MaxStamina - t.Stamina, Math.Max(0, (int)Math.Floor(Eval(action, eff["amount"], 1)))));
                        t.Stamina += amount;
                        Emit("staminaRecovered", new JObject { ["targetId"] = t.Id, ["amount"] = amount, ["reason"] = "effect" });
                    }
                    break;
                case "restoreMana":
                    foreach (var t in ResolveTargets(action, (string)eff["target"]))
                    {
                        var before = t.Mana;
                        t.Mana = Math.Min(t.MaxMana, t.Mana + Math.Max(0, (int)Math.Floor(Eval(action, eff["amount"], 1)) - ((int?)eff["funded"] ?? 0)));
                        Emit("manaRestored", new JObject { ["targetId"] = t.Id, ["amount"] = t.Mana - before });
                    }
                    break;
                case "loseHp":
                    foreach (var t in ResolveTargets(action, (string)eff["target"]))
                        LoseHp(t, (int)Math.Floor(Eval(action, eff["amount"], 0, t)), (string)eff["cause"] ?? "effect", action.Source);
                    break;
                case "heal":
                    foreach (var t in ResolveTargets(action, (string)eff["target"]))
                    {
                        var nominal = Eval(action, eff["amount"], 0);
                        var amount = Math.Max(0, (int)Math.Floor(nominal));
                        Heal(t, (int)Math.Floor(amount * Core2Stances.StanceEffectiveness(Rules, action.Source, action.Card)));
                    }
                    break;
                case "moveEngagement":
                {
                    if (Range == null || !Range.Policy.Enabled) break;
                    var target = action.Target;
                    if (target == null || target.Side == action.Source.Side) target = action.Source.Side == Core2Side.Enemy ? Player : action.Target;
                    if (target == null || target.Side == action.Source.Side) throw new InvalidOperationException("Engagement movement requires an opposing target");
                    Range = Range.Moved(Core2Range.ActorKey(action.Source), Core2Range.ActorKey(target), (int)eff["bands"]);
                    Emit("combatEngagementMoved", new JObject { ["sourceId"] = action.Source.Id, ["targetId"] = target.Id, ["bands"] = eff["bands"] });
                    break;
                }
                case "stagger":
                    foreach (var t in ResolveTargets(action, (string)eff["target"])) if (t.Side == Core2Side.Enemy) StaggerEnemy(t, null);
                    break;
                default:
                    throw new NotSupportedException("Opcode '" + op + "' is not ported to the Core 2 fight");
            }
        }

        private double Eval(Core2FightAction action, JToken value, double fallback, Core2Combatant target)
        {
            if (value is JObject row && (string)row["f"] == "stacks")
            {
                var of = (string)row["of"];
                var entity = of == "owner" || of == "self" ? action.Owner ?? action.Source : target;
                return entity?.StatusStacks((string)row["status"]) ?? 0;
            }
            return Eval(action, value, fallback);
        }

        /// <summary>JS plannedStatusTargets: a status effect lands on its planned targets that the action did not miss.</summary>
        private List<Core2Combatant> StatusTargets(Core2FightAction action, JObject eff)
        {
            var targets = ResolveTargets(action, (string)eff["target"]);
            var group = action.Group;
            if (group == null) return targets;
            return targets.Where(t => t.Alive && !(group.Targets.TryGetValue(t.Id, out var row) && row.Receipt?.Avoided == true)).ToList();
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Contacts: begin, land, complete
        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>The hit options a contact reads: its range contact from the accepted plan.</summary>
        private Core2HitOptions HitOptions(Core2Combatant source, Core2Combatant target, Core2Carrier carrier, Core2RangePlan plan, string rangeContactId)
        {
            var options = new Core2HitOptions();
            if (Range == null || source == null || target == null) return options;
            var rangeSource = RangeSourceFor(source, carrier, Core2Range.ActorKey(target));
            options.Contact = Core2Range.HitContact(Range, Core2Range.ActorKey(source), rangeSource, plan, rangeContactId, _rangeContext);
            return options;
        }

        /// <summary>JS rangeSourceContact: a player card commits its resolved face's source; an enemy move covers its own range.</summary>
        private Core2RangeSource RangeSourceFor(Core2Combatant source, Core2Carrier carrier, string targetKey)
        {
            if (source.Side == Core2Side.Player && carrier?.Id != null && Setup.Cards.TryGetValue(carrier.Id, out var face) && face.RangeSource != null && carrier.Profile?.CounterReturnAttack != true)
                return new Core2RangeSource { ContactId = "0", TargetKey = targetKey, Coverage = face.RangeSource.Coverage, Weapon = face.RangeSource.Weapon, SourceWeaponId = face.RangeSource.SourceWeaponId, ThrowCapable = face.RangeSource.ThrowCapable, Bypass = face.RangeSource.Bypass };
            if (source.Side == Core2Side.Player && carrier?.Profile?.CounterReturnAttack == true && carrier.Id != null && Setup.Cards.TryGetValue(carrier.Id, out var counterFace) && counterFace.RangeSource != null)
                return new Core2RangeSource { ContactId = "0", TargetKey = targetKey, Coverage = counterFace.RangeSource.Coverage, Weapon = counterFace.RangeSource.Weapon, SourceWeaponId = counterFace.RangeSource.SourceWeaponId, ThrowCapable = counterFace.RangeSource.ThrowCapable, Bypass = counterFace.RangeSource.Bypass };
            return Core2RangeSource.For(carrier, targetKey);
        }

        /// <summary>JS acceptRangeActions: quote the action's hostile contacts, refuse an unreachable route, pay the Advance once.</summary>
        private Core2RangePlan AcceptRange(Core2Combatant source, List<Core2FightAction> actions, Core2Carrier carrier, bool charge = true, Core2RangePlan acceptedPlan = null)
        {
            if (Range == null || source == null) return null;
            if (acceptedPlan != null) return ChargeRange(source, acceptedPlan, charge);
            return ChargeRange(source, QuoteRangeActions(source, actions, carrier, _rangeContext), charge);
        }

        /// <summary>JS quoteRangeActions (route advance): one contact per hostile effect hit on each living opponent, priced in <paramref name="context"/>.</summary>
        private Core2RangePlan QuoteRangeActions(Core2Combatant source, List<Core2FightAction> actions, Core2Carrier carrier, Core2RangePlan context)
        {
            var contacts = new List<Core2RangeSource>();
            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                var eff = action.Effect;
                var op = (string)eff["op"];
                if (op != "damage" && op != "applyStatus" && op != "buildup" && op != "poiseDamage" && op != "wardDamage" && op != "loseHp" && op != "stagger" && op != "arcaneBuildup") continue;
                var repeats = Math.Max(0, (int)Math.Floor(Eval(action, eff["repeat"], 1)));
                var hits = op == "damage" ? Math.Max(0, (int)Math.Floor(Eval(action, eff["hits"], 1))) : 1;
                for (var r = 0; r < repeats; r++) for (var h = 0; h < hits; h++)
                    foreach (var target in ResolveTargets(action, (string)eff["target"]))
                        if (target.Alive && source.Side != target.Side)
                        {
                            var key = Core2Range.ActorKey(target);
                            var contact = RangeSourceFor(source, action.Card ?? carrier, key);
                            contact.ContactId = index + ":" + r + ":" + h + ":" + key;
                            contacts.Add(contact);
                        }
            }
            return Core2Range.ActionPlan(Range, Core2Range.ActorKey(source), contacts, "advance", context);
        }

        /// <summary>
        /// JS acceptIndependentTrigger (combatRangeLineage.js): a trigger with a hostile effect (a relic's playerTurnStart
        /// Crimson Blight on ALL enemies) reaches its targets from its own spell source — the range its effect tags name
        /// (range:*), else melee — quoted outside any reaction context. When the route cannot reach or the owner cannot pay
        /// its Advance, combatRangeActionUnavailable is reported and only the trigger's non-hostile effects run; otherwise the
        /// Advance is paid now (acceptRangeActions). Range-lineage inheritance (a trigger riding the range plan of the card
        /// whose event fired it, inheritedRangeAction) is not ported: Core 2 starter content has no such trigger.
        /// </summary>
        private List<Core2FightAction> AcceptIndependentTrigger(Core2Combatant source, List<Core2FightAction> actions, string key)
        {
            if (Range == null || !Range.Policy.Enabled || source == null || !actions.Any(a => RangeHostileOps.Contains((string)a.Effect["op"] ?? ""))) return actions;
            var tags = actions.SelectMany(a => Core2Json.Strings(a.Effect["tags"])).ToList();
            var range = tags.FirstOrDefault(t => t.StartsWith("range:", StringComparison.Ordinal))?.Substring(6) ?? "melee";
            var carrier = new Core2Carrier { Profile = new Core2CombatProfile { Range = range } };
            var quoted = QuoteRangeActions(source, actions, carrier, null);
            if (quoted.Targets.Count == 0) return actions;
            var available = source.Stamina;
            if (!quoted.Available || available < quoted.AdvanceSp)
            {
                Emit("combatRangeActionUnavailable", new JObject
                {
                    ["sourceId"] = source.Id, ["ownerId"] = "player", ["required"] = quoted.AdvanceSp, ["available"] = available,
                    ["advanceSp"] = quoted.AdvanceSp, ["reason"] = "stamina", ["triggerKey"] = key,
                });
                return actions.Where(a => !RangeHostileOps.Contains((string)a.Effect["op"] ?? "")).ToList();
            }
            ChargeRange(source, quoted, true);
            return actions;
        }

        private static readonly HashSet<string> RangeHostileOps = new HashSet<string>(StringComparer.Ordinal)
            { "damage", "applyStatus", "buildup", "poiseDamage", "wardDamage", "loseHp", "stagger", "arcaneBuildup" };

        /// <summary>JS acceptRangeActions' acceptance: refuse an unreachable route, pay the Advance once (a prepaid plan pays nothing).</summary>
        private Core2RangePlan ChargeRange(Core2Combatant source, Core2RangePlan plan, bool charge)
        {
            if (!plan.Available) throw new Core2PlayRefusedException("The selected range route cannot reach every target", "range", 0, 0);
            var extra = charge ? plan.AdvanceSp : 0;
            if (source.Stamina < extra) throw new Core2PlayRefusedException("Not enough Stamina to Advance", "stamina", extra, source.Stamina);
            if (extra > 0)
            {
                source.Stamina -= extra;
                if (source.Side == Core2Side.Player)
                {
                    source.StaminaSpentThisTurn += extra;
                    Emit("energySpent", new JObject { ["amount"] = extra }); Emit("staminaSpent", new JObject { ["amount"] = extra });
                }
            }
            return new Core2RangePlan { SourceKey = plan.SourceKey, Route = plan.Route, Available = plan.Available, AdvanceSp = plan.AdvanceSp, Targets = plan.Targets, RootId = plan.RootId };
        }

        /// <summary>JS beginExpandedAction → beginTacticalAction (configured, FixCore) for every present target of the current contact.</summary>
        private void BeginGroup(Core2ActionGroup group, Core2Combatant source)
        {
            if (group.Begun) return;
            group.Begun = true;
            foreach (var row in group.Targets.Values)
            {
                var target = row.Entity;
                if (!target.Alive) continue;
                var carrier = group.Carrier;
                // JS restrictions(ctx, target).counterDisabled: a locked defender's Counter neither intercepts nor returns.
                var counterDisabled = ControlRestrictions(target).CounterDisabled;
                var receipt = new Core2TacticalReceipt { Profile = Core2Counters.ContactProfile(carrier), CounterDisabledAtStart = counterDisabled };
                var counter = Core2Counters.Prepared(Ctx, target, carrier, target == Player ? group.ManualCounterSerial : null, counterDisabled);
                foreach (var contact in row.Contacts)
                {
                    var profile = ContactProfile(carrier, contact);
                    receipt.Profiles.Add(profile);
                    receipt.Covered.Add(counter != null && Core2Counters.CoverageMatches(counter.Coverage, profile, contact.Effect ?? "damage")
                        && (contact.Effect == "status" ? contact.Pressure > 0 : contact.Amount > 0));
                    receipt.Amounts.Add(contact.Effect != null ? 0 : (int)Math.Floor(Math.Max(0, contact.Amount)));
                }
                receipt.Amount = receipt.Amounts.Sum();
                receipt.CounterEligible = counter != null && receipt.Covered.Any(c => c);
                receipt.Counter = receipt.CounterEligible ? counter.Clone() : null;
                receipt.Begun = true;
                var rangeContactId = row.Contacts.FirstOrDefault()?.RangeContactId;
                var hit = ResolveHit(source, target, carrier, HitOptions(source, target, carrier, group.RangePlan, null));
                receipt.CounterAttack = hit;
                receipt.Avoided = hit != null && !hit.Hit;
                receipt.Connected = !receipt.Avoided;
                if (receipt.CounterEligible && !receipt.Avoided)
                {
                    target.Counter.Charges = 0;
                    var consumed = CounterCarrierFields(target, receipt.Counter.Carrier);
                    consumed["sourceId"] = target.Id; consumed["targetId"] = source?.Id; consumed["serial"] = receipt.Counter.Serial;
                    Emit("combatCounterConsumed", consumed);
                }
                row.Receipt = receipt;
                // JS beginExpandedAction: the row's status interactions, then beforeStatusAction (Fire thaws) once the roll is known.
                row.Interactions = StatusInteractions(carrier, row.Contacts);
                BeforeStatusAction(target, row.Interactions, receipt.Connected);
            }
        }

        /// <summary>JS normalizedProfile(carrier, contact): the contact's profile with the camp folded and range/targeting defaulted.</summary>
        private static Core2CombatProfile ContactProfile(Core2Carrier carrier, Core2ContactPlan contact)
        {
            var profile = (contact?.Profile ?? carrier?.Profile ?? new Core2CombatProfile()).Clone();
            if (profile.Camp == "spells" || profile.Camp == "magic" || profile.Camp == "magical") profile.Camp = "spell";
            profile.Range ??= profile.Maneuver == "ranged" ? "reach" : "melee";
            profile.Targeting ??= profile.Maneuver == "sweep" ? "area" : "single";
            if (carrier?.Profile?.CounterReturnAttack == true) profile.CounterReturnAttack = true;
            return profile;
        }

        /// <summary>JS resolveAttackHit: one Accuracy roll on combatAvoidance, announced as attackHit or attackMissed; a miss spends an Evade stack.</summary>
        private Core2HitResult ResolveHit(Core2Combatant source, Core2Combatant target, Core2Carrier carrier, Core2HitOptions options)
        {
            var preview = Core2Accuracy.Preview(Rules, source, target, carrier, options);
            if (preview == null) return null;
            var dice = new List<int>();
            if (preview.InRange && !preview.Bypass)
            {
                dice.Add(Rng.Int(Core2Accuracy.Stream, 1, 20));
                if (preview.Mode != "normal") dice.Add(Rng.Int(Core2Accuracy.Stream, 1, 20));
            }
            var hit = preview.InRange && (preview.Bypass || Core2Accuracy.DiceHit(dice, preview.Need, preview.Mode));
            var result = new Core2HitResult
            {
                Preview = preview, Dice = dice, Hit = hit,
                AttackDie = dice.Count == 0 ? (int?)null : preview.Mode == "disadvantage" ? dice.Min() : dice.Max(),
                MissReason = hit ? null : preview.InRange ? "accuracy" : "range",
                Label = hit ? (preview.EvadeAttempted && !preview.Bypass ? "Hit — Evade Failed" : "Hit") : "Missed",
            };
            if (!hit && preview.InRange && !preview.Bypass && preview.EvadeStacks > 0) RemoveStatus(target, "evade", 1, "consumed");
            // JS resolveAttackHit's receipt: the preview, the dice, the totals a fixed-Evasion roll reads, and the carrier.
            var outcome = new JObject
            {
                ["sourceId"] = source.Id, ["targetId"] = target.Id,
                ["cardId"] = source.Side == Core2Side.Enemy ? Undefined : (JToken)carrier?.Id, ["moveId"] = source.Side == Core2Side.Enemy && carrier?.Id != null ? (JToken)carrier.Id : Undefined,
                ["range"] = preview.Range, ["requiredRange"] = preview.RequiredRange, ["inRange"] = preview.InRange, ["accuracy"] = preview.Accuracy, ["evasion"] = preview.Evasion,
                ["need"] = preview.Need, ["bypass"] = preview.Bypass, ["opposed"] = false, ["fixedDefense"] = true, ["mode"] = preview.Mode,
                ["advantage"] = preview.Mode == "advantage", ["disadvantage"] = preview.Mode == "disadvantage", ["evadeStacks"] = preview.EvadeStacks,
                ["evadeAttempted"] = preview.EvadeAttempted, ["hitChance"] = preview.HitChance, ["chance"] = preview.HitChance,
                ["d20"] = dice.Count > 0 ? (JToken)dice[0] : JValue.CreateNull(), ["dice"] = new JArray(dice), ["hit"] = hit, ["label"] = result.Label,
            };
            if (dice.Count > 0) { outcome["attackTotal"] = result.AttackDie + preview.Accuracy; outcome["evasionTotal"] = preview.Evasion; }
            if (!hit) outcome["missReason"] = result.MissReason;
            Emit(hit ? "attackHit" : "attackMissed", outcome);
            return result;
        }

        /// <summary>
        /// JS applyAttackDamage → applyProfileDamage (configured, FixCore): a planned contact uses its group's roll (no
        /// second roll); a covered advantaged contact adds the Counter's Block first; Block, Poise and HP take the receipt;
        /// Poise emptied from above zero breaks it (Staggered); an advantaged Smash interrupts once per round.
        /// </summary>
        private int ApplyAttackDamage(Core2FightAction action, Core2Combatant source, Core2Combatant target, int baseDamage)
        {
            if (target == null || !target.Alive) return 0;
            var group = action.Group;
            var row = group != null && group.Targets.TryGetValue(target.Id, out var r) ? r : null;
            var tactical = row?.Receipt;
            if (tactical?.Avoided == true) return 0;
            var carrier = action.Card ?? group?.Carrier;
            var options = HitOptions(source, target, carrier, group?.RangePlan, null);
            if (tactical == null)
            {
                var hit = ResolveHit(source, target, carrier, options);
                if (hit != null && !hit.Hit) return 0;
            }
            var chance = Core2Damage.Receipt(Rules, source, target, baseDamage, carrier, options);
            var paidContact = target == Player && Core2Counters.PaidContact(group?.ManualCounterSerial, tactical?.Counter);
            if (tactical != null && (!Core2Counters.ReturnSpent(target) || paidContact) && tactical.CounterEligible && !tactical.Avoided && tactical.Covered.Count > 0 && tactical.Covered[0]
                && Core2Counters.Beats(Rules, tactical.Profiles[0].Maneuver))
            {
                var counterBlock = Core2Counters.Return(Rules, chance.Incoming, true).Block;
                if (counterBlock > 0)
                {
                    target.Block += counterBlock;
                    Emit("blockGained", new JObject { ["targetId"] = target.Id, ["amount"] = counterBlock, ["total"] = target.Block, ["source"] = "counter" });
                }
            }
            var receipt = Core2Damage.Receipt(Rules, source, target, baseDamage, carrier, options);
            if (tactical != null)
            {
                tactical.Amount += receipt.Amount - (tactical.Amounts.Count > 0 ? tactical.Amounts[0] : 0);
                if (tactical.Amounts.Count > 0) tactical.Amounts[0] = receipt.Amount;
                if (tactical.Incomings.Count == 0) tactical.Incomings.Add(receipt.Incoming); else tactical.Incomings[0] = receipt.Incoming;
                tactical.HpLoss += receipt.HpLoss; tactical.Blocked += receipt.Blocked;
            }
            var blockBefore = target.Block;
            var poiseBefore = target.Poise?.Value ?? 0;
            target.Block = receipt.BlockRemaining;
            target.Barrier = Math.Max(0, target.Barrier - receipt.BarrierMitigated);
            if (target.Poise != null) target.Poise.Value = Math.Max(0, target.Poise.Value - receipt.PoiseAbsorbed - receipt.PoiseDamage);
            target.Hp -= receipt.HpLoss;
            Emit("damageDealt", new JObject
            {
                ["sourceId"] = source?.Id, ["targetId"] = target.Id, ["amount"] = receipt.Amount, ["blocked"] = receipt.Blocked, ["hpLoss"] = receipt.HpLoss,
                ["poiseAbsorbed"] = receipt.PoiseAbsorbed, ["poiseDamage"] = receipt.PoiseDamage, ["advantage"] = receipt.Advantage,
                ["damageReceipt"] = receipt.ToJson(target.Id),
                ["blockRemaining"] = target.Block, ["poiseRemaining"] = target.Poise?.Value, ["isAttack"] = true,
            });
            if (blockBefore > 0 && target.Block == 0) Emit("blockBroken", new JObject { ["sourceId"] = source?.Id, ["targetId"] = target.Id });
            if (receipt.HpLoss > 0) Emit("hpLost", new JObject { ["targetId"] = target.Id, ["amount"] = receipt.HpLoss, ["cause"] = "attack" });
            var poiseBroke = poiseBefore > 0 && target.Poise?.Value == 0 && target.Alive && target.Hp > 0;
            if (poiseBroke) PoiseBreak(source, target);
            if (receipt.Interrupted && !poiseBroke && target.Side == Core2Side.Enemy && target.Alive && Ctx.ClaimStagger(target, Rules.StaggerLimitPerTargetPerRound))
                StaggerEnemy(target, null);
            AfterHpChange(target, source);
            return receipt.Amount;
        }

        /// <summary>JS fixCorePoiseBreak: report the break, then stagger (an enemy's break uses the round's stagger claim).</summary>
        private void PoiseBreak(Core2Combatant source, Core2Combatant target)
        {
            Emit("poiseBroken", new JObject { ["targetId"] = target.Id, ["targetKind"] = target.Side == Core2Side.Enemy ? "enemy" : "player", ["sourceId"] = source?.Id ?? Undefined, ["poiseBreak"] = true });
            if (target.Side == Core2Side.Enemy) { Ctx.ClaimStagger(target, Rules.StaggerLimitPerTargetPerRound); StaggerEnemy(target, "poiseBreak"); }
            else FixCoreStagger(target, "poiseBreak");
        }

        /// <summary>JS staggerEnemy (FixCore): the Staggered stance, then enemyStaggered.</summary>
        private void StaggerEnemy(Core2Combatant enemy, string cause)
        {
            if (enemy == null || enemy.Side != Core2Side.Enemy || !enemy.Alive) return;
            FixCoreStagger(enemy, cause);
            var data = new JObject { ["targetId"] = enemy.Id, ["enemyId"] = enemy.EnemyId, ["cancelledMove"] = null };
            if (cause != null) data["cause"] = cause;
            Emit("enemyStaggered", data);
        }

        /// <summary>JS fixCoreStagger: the Counter is cleared (a spent one still answers), the Staggered stance, −SP next turn, +Vulnerable.</summary>
        private void FixCoreStagger(Core2Combatant entity, string cause)
        {
            if (entity.Counter != null && entity.Counter.Charges == 0) entity.CounterStaggeredSerial = entity.Counter.Serial;
            ClearCounter(entity);
            if (Rules.StaggeredStance)
            {
                entity.Stance = Core2Stance.Staggered(entity.OwnerCycle);
                Emit("combatStanceChanged", new JObject { ["targetId"] = entity.Id, ["maneuver"] = "staggered" });
            }
            if (Rules.StaggeredStaminaLoss > 0) entity.PendingStaminaLoss += Rules.StaggeredStaminaLoss;
            if (Rules.StaggeredVulnerableStacks > 0) ApplyStatus(entity, "vulnerable", Rules.StaggeredVulnerableStacks, entity);
            if (entity.Side == Core2Side.Player)
            {
                var data = new JObject { ["targetId"] = entity.Id, ["actionLoss"] = Rules.StaggeredStaminaLoss };
                if (cause != null) data["cause"] = cause;
                Emit("playerStaggered", data);
            }
        }

        /// <summary>JS clearCombatCounter: the prepared Counter is gone; a queued return of its owner is interrupted.</summary>
        private void ClearCounter(Core2Combatant entity)
        {
            if (entity == null) return;
            entity.Counter = null;
            foreach (var queued in _queue) if (queued.Source == entity && queued.CounterReaction) queued.CounterInterrupted = true;
        }

        /// <summary>JS drainFixCorePoise: whole Poise damage, never more than the pool; emptying it breaks Poise.</summary>
        private int DrainPoise(Core2Combatant source, Core2Combatant target, int amount)
        {
            var n = Math.Max(0, amount);
            if (target == null || !target.Alive || n <= 0 || target.Poise == null || target.Poise.Value <= 0) return 0;
            var spent = Math.Min(target.Poise.Value, n);
            target.Poise.Value -= spent;
            Emit("persistentPoiseChanged", new JObject { ["targetId"] = target.Id, ["spent"] = spent, ["value"] = target.Poise.Value, ["max"] = target.Poise.Max, ["source"] = "poiseDamage" });
            if (target.Poise.Value == 0 && target.Hp > 0) PoiseBreak(source, target);
            return spent;
        }

        /// <summary>JS matchupPoiseMultiplier (expansion): a physical Attack into a Smash or Casting stance gains preparedPoiseMultiplier.</summary>
        private double MatchupPoiseMultiplier(Core2Carrier carrier, Core2Combatant target)
        {
            var profile = carrier?.Profile;
            if (profile == null || carrier.CombatReaction || profile.Camp != "physical" || profile.Maneuver != "attack") return 1;
            var stance = target?.Stance?.Maneuver;
            if (stance != "smash" && stance != "casting") return 1;
            throw new NotSupportedException("preparedPoiseMultiplier (combatExpansionMatchups.attack) is not exported");
        }

        /// <summary>JS stancePressureMultiplier: Smash into Defend/Casting ×smashMultiplier, Counter into Attack/Smash ×counterMultiplier, Sweep into Counter, then stance mitigation.</summary>
        private double StancePressureMultiplier(Core2Combatant source, Core2Combatant target, Core2Carrier carrier, Core2HitResult hit)
        {
            if (carrier == null) return 1;
            var profile = ContactProfile(carrier, null);
            var held = Core2Stances.StanceOf(target?.Stance);
            Core2Stance incoming;
            if (profile.Maneuver == "counter") incoming = new Core2Stance("counter");
            else incoming = Core2Stances.StanceOf(new Core2Carrier { Profile = profile });
            if (incoming == null) return 1;
            var cfg = (JObject)Content.Mechanics["stanceRules"];
            var evadeAttempted = hit?.Preview?.EvadeAttempted == true;
            double offense = 1;
            if (incoming.Maneuver == "smash" && (held?.Maneuver == "defend" || held?.Maneuver == "casting" || evadeAttempted)) offense = (double)cfg["smashMultiplier"];
            else if (incoming.Maneuver == "counter" && (held?.Maneuver == "attack" || held?.Maneuver == "smash" || evadeAttempted)) offense = (double)cfg["counterMultiplier"];
            else if (incoming.Maneuver == "sweep" && held?.Maneuver == "counter") offense = (double)cfg["sweepCounterMultiplier"];
            return offense * Core2Stances.StanceMitigationMultiplier(Rules, incoming.Maneuver, held?.Maneuver, out _);
        }

        /// <summary>JS gainBlock (FixCore, a card or move): DR-rated, matching-stance Block; a card's Block refills Poise.</summary>
        private int GainBlock(Core2Combatant entity, int baseBlock, Core2Carrier card)
        {
            if (entity == null || !entity.Alive) return 0;
            var amount = Rules.BlockDefense
                ? Math.Max(0, (int)Math.Floor((baseBlock + Core2Statuses.RatingValue(Rules, entity, "dr") * Rules.DefenseRatingWeight) * Core2Stances.StanceEffectiveness(Rules, entity, card)))
                : 0;
            entity.Block += amount;
            if (amount > 0 && card != null && entity.Poise != null && entity.Poise.Value < entity.Poise.Max)
            {
                var before = entity.Poise.Value;
                entity.Poise.Value = Math.Min(entity.Poise.Max, entity.Poise.Value + amount);
                Emit("persistentPoiseChanged", new JObject { ["targetId"] = entity.Id, ["restored"] = entity.Poise.Value - before, ["value"] = entity.Poise.Value, ["max"] = entity.Poise.Max, ["source"] = "block-card" });
            }
            Emit("blockGained", new JObject { ["targetId"] = entity.Id, ["amount"] = amount, ["total"] = entity.Block });
            return amount;
        }

        /// <summary>JS applyLoseHp: direct HP loss, no defenses.</summary>
        private int LoseHp(Core2Combatant target, int amount, string cause, Core2Combatant source)
        {
            if (target == null || !target.Alive) return 0;
            var n = Math.Max(0, amount);
            if (n == 0) return 0;
            target.Hp -= n;
            Emit("hpLost", new JObject { ["targetId"] = target.Id, ["amount"] = n, ["cause"] = cause });
            AfterHpChange(target, source);
            return n;
        }

        /// <summary>JS applyHeal: up to max HP; the healed event fires even at full HP.</summary>
        private int Heal(Core2Combatant target, int amount)
        {
            if (target == null || !target.Alive) return 0;
            var n = Math.Max(0, amount);
            var gained = Math.Min(n, target.MaxHp - target.Hp);
            target.Hp += gained;
            Emit("healed", new JObject { ["targetId"] = target.Id, ["amount"] = gained, ["requested"] = n });
            AfterHpChange(target, null);
            return gained;
        }

        /// <summary>JS afterHpChange: a combatant at 0 HP dies (enemyDied for an enemy); phases check HP thresholds.</summary>
        private void AfterHpChange(Core2Combatant target, Core2Combatant source)
        {
            if (target.Hp <= 0 && target.Alive)
            {
                target.Hp = 0;
                target.Alive = false;
                if (target.Side == Core2Side.Enemy) Emit("enemyDied", new JObject { ["targetId"] = target.Id, ["enemyId"] = target.EnemyId, ["sourceId"] = source?.Id });
            }
            CheckPhases();
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Completion (JS completeExpandedAction, completeContact, completeTacticalAction)
        // ---------------------------------------------------------------------------------------------------------------

        private void CompleteGroup(Core2FightAction action)
        {
            var group = action.Group;
            if (group == null) return;
            if (action.ContactCompletion)
            {
                if (!group.Cancelled) CompleteContact(action, group);
                return;
            }
            if (group.Cancelled) { _pendingExpansionActions = Math.Max(0, _pendingExpansionActions - 1); FinishGroup(group); return; }
            BeginGroup(group, action.Source);
            var sequence = group.Contacts;
            if (sequence != null) group.Targets = group.RootTargets;
            foreach (var pair in group.Targets)
            {
                var row = pair.Value;
                // JS completedRootTargets: a contact sequence's root row sums its contacts' receipts (connected if any
                // connected; the total amount) and reads its interactions off all its contacts.
                var connected = row.Receipt?.Connected ?? false;
                var amount = row.Receipt?.Amount ?? 0;
                var interactions = row.Interactions;
                var hasReceipt = row.Receipt != null;
                if (sequence != null)
                {
                    var receipts = sequence.Select(targets => targets.TryGetValue(pair.Key, out var contactRow) ? contactRow.Receipt : null).Where(r => r != null).ToList();
                    hasReceipt = true;
                    connected = receipts.Any(r => r.Connected);
                    amount = receipts.Sum(r => r.Amount);
                    interactions = StatusInteractions(group.Carrier, row.Contacts);
                }
                if (!hasReceipt || !row.Entity.Alive) continue;
                if (sequence == null) CompleteTactical(action.Source, row.Entity, group, row.Receipt);
                CompleteStatusAction(row.Entity, interactions ?? StatusInteractions(group.Carrier, row.Contacts), connected, group.Key, amount > 0);
            }
            foreach (var row in group.Targets.Values) DiscardReservation(group, row);
            _pendingExpansionActions = Math.Max(0, _pendingExpansionActions - 1);
            FinishGroup(group);
        }

        /// <summary>JS completeContact: each target's return finishes (its actions run next) before the following contact begins.</summary>
        private void CompleteContact(Core2FightAction action, Core2ActionGroup group)
        {
            var start = _queue.Count;
            foreach (var row in group.Targets.Values)
            {
                if (row.Receipt == null || !row.Entity.Alive) { DiscardReservation(group, row); continue; }
                CompleteTactical(action.Source, row.Entity, group, row.Receipt, row);
                DiscardReservation(group, row);
            }
            var generated = _queue.Skip(start).ToList();
            _queue.RemoveRange(start, generated.Count);
            _queue.InsertRange(0, generated);
        }

        /// <summary>JS discardCounterRangeReservation: a reservation made for this contact is spent once the contact completes.</summary>
        private void DiscardReservation(Core2ActionGroup group, Core2GroupTarget row)
        {
            var rootId = group.RangePlan?.RootId;
            var ids = new HashSet<string>(row.Contacts.Select(contact => contact.RangeContactId).Where(id => id != null), StringComparer.Ordinal);
            foreach (var carrier in new[] { row.Entity?.Counter?.Carrier, row.Receipt?.Counter?.Carrier })
            {
                var reservation = carrier?.RangeCounterReservation;
                if (reservation != null && reservation.IncomingRootId == rootId && ids.Contains(reservation.ContactId)) carrier.RangeCounterReservation = null;
            }
        }

        /// <summary>
        /// JS completeTacticalAction under the Core 2 Counter rule: a covered contact that connected is answered by a still
        /// prepared Counter (a Poise break on that hit keeps it), once per owner cycle; the return is a fraction of the
        /// incoming damage, priced for its Advance by the defender, and runs as its own one-roll action.
        /// </summary>
        private void CompleteTactical(Core2Combatant source, Core2Combatant target, Core2ActionGroup group, Core2TacticalReceipt receipt, Core2GroupTarget row = null)
        {
            if (receipt == null || receipt.Completed) return;
            receipt.Completed = true;
            var counter = receipt.Counter;
            var stillPrepared = counter != null && (target.Counter?.Serial == counter.Serial || target.CounterStaggeredSerial == counter.Serial);
            target.CounterStaggeredSerial = null;
            // A Counter the player paid as a reaction to this contact answers it even after this cycle's automatic return.
            var paid = target == Player && Core2Counters.PaidContact(group.ManualCounterSerial, counter);
            // JS: `blocked` — the defender is locked by a control status now, or was when the contact began.
            var blocked = ControlRestrictions(target).CounterDisabled || receipt.CounterDisabledAtStart;
            if (!stillPrepared || blocked || receipt.Avoided || receipt.Profiles.Any(p => p.Traits.Contains("unreflectable")) || !target.Alive || source == null || !source.Alive
                || Core2Counters.ReturnSpent(target) && !paid) return;
            var payload = Core2Counters.ReturnFor(Rules, receipt.Covered.Select((covered, i) =>
                (covered, i < receipt.Incomings.Count ? receipt.Incomings[i] : (i < receipt.Amounts.Count ? receipt.Amounts[i] : 0), receipt.Profiles[i].Maneuver)));
            var ev = CounterCarrierFields(target, counter.Carrier);
            ev["sourceId"] = target.Id; ev["targetId"] = source.Id; ev["amount"] = payload.Hp; ev["poiseDamage"] = payload.Poise; ev["wardDamage"] = 0;
            if (!(payload.Hp > 0 || payload.Poise > 0)) { Emit("combatCounterTriggered", ev); return; }
            // The return's own Advance, quoted in the incoming contact's frozen engagement; unaffordable → no return.
            var returnCarrier = counter.Carrier.Clone();
            returnCarrier.CombatReaction = true; returnCarrier.SkipRatingBonus = true; returnCarrier.Profile.CounterReturnAttack = true;
            returnCarrier.ReactionDepth = Math.Max(counter.Carrier.ReactionDepth, group.Carrier?.ReactionDepth ?? 0);
            returnCarrier.CounterRequiredRange = receipt.Profiles.Where((p, i) => receipt.Covered[i]).Select(p => p.Range ?? "melee")
                .Aggregate(receipt.Profile?.Range ?? "melee", (range, next) => Core2Stances.RangeIndex(next) > Core2Stances.RangeIndex(range) ? next : range);
            Core2RangePlan returnPlan = null;
            var prepaid = false;
            if (Range != null)
            {
                // JS authorizeCounterRange: a Counter paid as a reaction to this very contact reserved (and paid) its return's
                // plan; any other return is quoted in the incoming contact's frozen engagement, and refused when its owner
                // cannot pay the Advance (the defense stands).
                var defenderKey = Core2Range.ActorKey(target); var attackerKey = Core2Range.ActorKey(source);
                var incomingPlan = group.RangePlan;
                var contactId = row?.Contacts.FirstOrDefault()?.RangeContactId;
                var contact = incomingPlan?.Targets.FirstOrDefault(t => t.ContactId == contactId) ?? incomingPlan?.Targets.FirstOrDefault(t => t.TargetKey == defenderKey);
                var reservation = counter.Carrier.RangeCounterReservation;
                if (reservation != null && !reservation.Consumed && reservation.IncomingRootId == incomingPlan?.RootId && reservation.ContactId == contact?.ContactId
                    && reservation.SourceKey == defenderKey && reservation.TargetKey == attackerKey)
                {
                    returnPlan = reservation.RangePlan;
                    prepaid = true;
                    counter.Carrier.RangeCounterReservation = null;
                    returnCarrier.RangeCounterReservation = null;
                }
                else
                {
                    var context = contact == null ? null : new Core2RangePlan { SourceKey = incomingPlan.SourceKey, Targets = new List<Core2RangeTarget> { contact } };
                    var returnSource = RangeSourceFor(target, returnCarrier, attackerKey);
                    returnSource.TargetKey ??= attackerKey;
                    returnPlan = Core2Range.ActionPlan(Range, defenderKey, new[] { returnSource }, "advance", context);
                    var available = Math.Max(0, target.Stamina);
                    if (available < returnPlan.AdvanceSp)
                    {
                        Emit("combatRangeReturnUnavailable", new JObject
                        {
                            ["sourceId"] = target.Id, ["targetId"] = source.Id, ["ownerId"] = Seat, ["required"] = returnPlan.AdvanceSp, ["available"] = available,
                            ["advanceSp"] = returnPlan.AdvanceSp, ["reason"] = "stamina", ["defenseKept"] = true,
                        });
                        return;
                    }
                }
            }
            target.CounterReturnedCycle = target.OwnerCycle;
            var actions = new List<Core2FightAction>();
            if (payload.Hp > 0) actions.Add(new Core2FightAction { Effect = new JObject { ["op"] = "damage", ["amount"] = payload.Hp }, Source = target, Owner = target, Target = source, Card = returnCarrier, CounterReaction = true });
            if (payload.Poise > 0) actions.Add(new Core2FightAction { Effect = new JObject { ["op"] = "poiseDamage", ["amount"] = payload.Poise }, Source = target, Owner = target, Target = source, Card = returnCarrier, CounterReaction = true });
            EnqueueGroup(actions, target, source, returnCarrier, counterReturn: ev, acceptedPlan: returnPlan, charge: !prepaid);
        }

        /// <summary>JS eventCarrier: a Counter event names the player's card (cardId) or the enemy's move (moveId) it came from.</summary>
        private static JObject CounterCarrierFields(Core2Combatant owner, Core2Carrier carrier)
        {
            var fields = new JObject { ["sourceKind"] = owner.Side == Core2Side.Enemy ? "enemy" : "player" };
            // An enemy's move carrier has no card: JS spreads cardId: undefined over the acting card's label.
            if (owner.Side == Core2Side.Enemy) { fields["enemyId"] = owner.EnemyId; if (carrier?.Id != null) fields["moveId"] = carrier.Id; fields["cardId"] = Undefined; fields["cardInstanceId"] = Undefined; }
            else { if (carrier?.Id != null) fields["cardId"] = carrier.Id; if (carrier?.InstanceId != null) fields["cardInstanceId"] = carrier.InstanceId; }
            return fields;
        }

        /// <summary>A finished card group resolves its play (JS finishAbilityPlay after the root drains).</summary>
        partial void FinishGroup(Core2ActionGroup group);
    }
}
