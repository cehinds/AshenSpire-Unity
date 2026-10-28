// Core2FightEnemyReactions.cs — enemies react on the same stack (docs/combat-card-traits-contract.md "Enemy reactions"):
// a move authored with `reaction: { on, minIncoming, chance }` is never planned; it answers a window instead. The
// playerAction window opens before a player card's root begins against the enemy; the playerReaction window opens when
// the player pushes a reaction on a root the enemy owns or that hits it. Each enemy decides in roster order, at most once
// per combat turn, and pays the move's SP when it pushes (Core 2 action policy); its plan is committed then and runs
// exactly as committed when the stack reaches it — paying nothing more, or fizzling when the enemy is halted.
//
// JS at the pin:
//   src/engine/enemyReactions.js     enemyReactionsActive, reactionMovesOf, incomingAgainst, decideEnemyReaction (the
//                                    enemyReaction stream, memoised per window; a detached copy never draws),
//                                    fightReacts, pushEnemyReaction (commitEnemySp, the plan into a sink as a
//                                    combatReaction carrier), relinkEnemyPlan, seatGone, enemyEntryHalt, runEnemyPlan,
//                                    playerRootSeat, openEnemyWindow
//   src/engine/combat.js             planEnemyReactionMove (expandedEnemyMove, enemyMoveCarrier, enemyMoveActions)
//   src/engine/combatRoundsAdapter.js enemyAvailableSp, commitEnemySp, enemyMoveQuote
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed partial class Core2Fight
    {
        /// <summary>JS reactionMovesOf: the enemy's reaction moves listing <paramref name="window"/>, in authored order (locked ones once unlocked).</summary>
        private List<(string MoveId, JObject Move)> ReactionMovesOf(Core2Combatant enemy, string window)
        {
            var moves = EnemyDef(enemy).Raw["moves"] as JObject ?? new JObject();
            return moves.Properties().Where(p => p.Value is JObject move && move["reaction"]?["on"] is JArray on && on.Any(w => (string)w == window)
                && (!((bool?)move["locked"] ?? false) || enemy.UnlockedMoves.Contains(p.Name))).Select(p => (p.Name, (JObject)p.Value)).ToList();
        }

        /// <summary>JS fightReacts: some enemy of the fight holds a reaction move for the window (or for either window).</summary>
        private bool FightReacts(string window) =>
            Enemies.Any(enemy => (window == null ? new[] { "playerAction", "playerReaction" } : new[] { window }).Any(name => ReactionMovesOf(enemy, name).Count > 0));

        /// <summary>JS incomingAgainst: the largest positive contact amount a group (or committed plan) aims at <paramref name="id"/>.</summary>
        private static int IncomingAgainst(Core2ActionGroup group, string id)
        {
            var best = 0.0;
            foreach (var row in group?.Targets.Values ?? Enumerable.Empty<Core2GroupTarget>())
            {
                if (row.Entity?.Id != id) continue;
                foreach (var contact in row.Contacts) if (contact.Amount > best) best = contact.Amount;
            }
            return (int)Math.Floor(best);
        }

        private static int EnemyAvailableSp(Core2Combatant enemy) => Math.Max(0, enemy.Stamina);

        /// <summary>JS decideEnemyReaction: the move this enemy takes for one window, or null.</summary>
        private string DecideEnemyReaction(Core2Combatant enemy, string window, string groupKey, int windowIndex, double incoming)
        {
            if (enemy == null || !enemy.Alive || enemy.SkipNextTurn) return null;
            if (enemy.CombatReactionTurn == Turn) return null;
            foreach (var (moveId, move) in ReactionMovesOf(enemy, window))
            {
                if (Core2EnemyMoves.Quote(Ctx, Range, enemy, Player, move, moveId).TotalSp > EnemyAvailableSp(enemy)) continue;
                var reaction = (JObject)move["reaction"];
                var minIncoming = (double?)reaction["minIncoming"] ?? 1;
                var chance = (double?)reaction["chance"] ?? 1;
                if (minIncoming != 0 && !(incoming > 0 && incoming >= minIncoming)) continue;
                if (chance >= 1) return moveId;
                if (chance <= 0) continue;
                var key = groupKey + ":" + windowIndex + ":" + enemy.Id + ":" + moveId;
                if (!EnemyReactionRolls.ContainsKey(key))
                {
                    if (_reactionPlaying) continue;
                    EnemyReactionRolls[key] = Rng.Float("enemyReaction") < chance;
                }
                if (EnemyReactionRolls[key]) return moveId;
            }
            return null;
        }

        /// <summary>
        /// JS openEnemyWindow: playerAction asks the enemies the root card hits; playerReaction the enemies that own the
        /// root or that it hits, each qualifying on the pushed card's contact against it. Roster order, enemy entries
        /// capped at maxDepth in total.
        /// </summary>
        private void OpenEnemyWindow(Core2ActionGroup group, string window, Core2ReactionEntry pushed)
        {
            if (!FightReacts(window) || group.CounterReturn != null || group.Carrier?.CombatReaction == true || group.Carrier?.ReactionDepth > 0) return;
            var index = group.EnemyWindows ?? 0;
            group.EnemyWindows = index + 1;
            var targetPlayerId = window == "playerAction" ? PlayerRootSeat(group) : pushed.OwnerId;
            if (targetPlayerId == null) return;
            var cap = Rules.ReactionStackMaxDepth;
            foreach (var enemy in Enemies.ToList())
            {
                var stacked = ReactionStack != null && ReactionStack.GroupKey == group.Key ? ReactionStack.Entries : new List<Core2ReactionEntry>();
                if (stacked.Count(entry => entry.Side == "enemy") >= cap) break;
                double incoming = window == "playerAction" ? IncomingAgainst(group, enemy.Id)
                    : pushed.Incoming != null && pushed.Incoming.TryGetValue(enemy.Id, out var amount) ? amount : 0;
                if (window == "playerAction" ? !(incoming > 0) : group.Source?.Id != enemy.Id && !(IncomingAgainst(group, enemy.Id) > 0)) continue;
                var moveId = DecideEnemyReaction(enemy, window, group.Key, index, incoming);
                if (moveId != null) PushEnemyReaction(group, enemy, moveId, targetPlayerId);
            }
        }

        /// <summary>
        /// JS pushEnemyReaction: pay the move's SP now, plan it as a combatReaction carrier into a sink (nothing is queued),
        /// and stack it as an enemy entry (combatEnemyReactionStacked). One reaction per enemy per combat turn.
        /// </summary>
        private Core2ReactionEntry PushEnemyReaction(Core2ActionGroup group, Core2Combatant enemy, string moveId, string targetPlayerId)
        {
            var raw = EnemyDef(enemy).Move(moveId).Raw;
            var quote = Core2EnemyMoves.Quote(Ctx, Range, enemy, Player, raw, moveId);
            if (quote.TotalSp > EnemyAvailableSp(enemy)) return null;
            // JS commitEnemySp(combat, enemy, moveId, quote.sp).
            var available = EnemyAvailableSp(enemy);
            if (quote.Sp > available)
            {
                Emit("enemyMoveUnavailable", new JObject { ["sourceId"] = enemy.Id, ["moveId"] = moveId, ["required"] = quote.Sp, ["available"] = available, ["resource"] = "sp" });
                return null;
            }
            enemy.Stamina = available - quote.Sp;
            var stack = ReactionStack ??= new Core2ReactionStack { GroupKey = group.Key };
            var move = Core2EnemyMoves.Expanded(Rules, raw, moveId);
            var carrier = Core2EnemyMoves.Carrier(Rules, raw, moveId);
            carrier.CombatReaction = true;
            carrier.ReactionDepth = stack.Entries.Count + 1;
            var actions = EnemyMoveActions(enemy, move, moveId, carrier);
            foreach (var action in actions) action.Card = carrier;
            var sink = new List<Core2FightAction>();
            var previousSink = _sink;
            var pendingCount = _pendingExpansionActions;
            Core2ActionGroup planned;
            try
            {
                _sink = sink;
                planned = EnqueueGroup(actions, enemy, Player, carrier);
            }
            finally { _sink = previousSink; _pendingExpansionActions = pendingCount; }
            planned.Kind = (string)move["intent"];
            var entry = new Core2ReactionEntry
            {
                Side = "enemy", OwnerId = enemy.Id, EnemyId = enemy.EnemyId, MoveId = moveId, TargetPlayerId = targetPlayerId,
                Plan = new Core2EnemyReactionPlan { Group = planned, Actions = sink }, SpPayment = quote,
            };
            stack.Entries.Add(entry);
            enemy.CombatReactionTurn = Turn;
            Emit("combatEnemyReactionStacked", new JObject
            {
                ["sourceId"] = enemy.Id, ["enemyId"] = enemy.EnemyId, ["moveId"] = moveId, ["targetPlayerId"] = targetPlayerId, ["groupKey"] = group.Key, ["depth"] = stack.Entries.Count,
            });
            return entry;
        }

        /// <summary>JS enemyEntryHalt: an entry fizzles when its enemy is dead or staggered, or its target seat is gone.</summary>
        private string EnemyEntryHalt(Core2ReactionEntry entry)
        {
            var enemy = Enemies.FirstOrDefault(row => row.Id == entry.OwnerId);
            if (enemy == null || !enemy.Alive || enemy.Hp <= 0 || enemy.SkipNextTurn) return "enemyHalted";
            return SeatGone(entry.TargetPlayerId) ? "targetGone" : null;
        }

        /// <summary>JS relinkEnemyPlan: the committed plan's group and actions bound to this fight's combatants.</summary>
        private (Core2ActionGroup Group, List<Core2FightAction> Actions) RelinkPlan(Core2EnemyReactionPlan plan)
        {
            Core2Combatant Link(Core2Combatant entity) => entity == null ? null : Find(entity.Id);
            var source = plan.Group;
            var group = new Core2ActionGroup
            {
                Key = source.Key, RootKey = source.RootKey, Source = Link(source.Source), Carrier = source.Carrier, RangePlan = source.RangePlan,
                EnemyMoveStart = source.EnemyMoveStart, CounterReturn = source.CounterReturn, Kind = source.Kind, ManualCounterSerial = source.ManualCounterSerial,
            };
            foreach (var pair in source.Targets)
                group.Targets[pair.Key] = new Core2GroupTarget { Entity = Link(pair.Value.Entity), Contacts = pair.Value.Contacts.ToList() };
            var actions = plan.Actions.Select(action =>
            {
                var copy = Clone(action);
                copy.Source = Link(action.Source); copy.Owner = Link(action.Owner); copy.Target = Link(action.Target); copy.Group = group;
                return copy;
            }).ToList();
            return (group, actions);
        }

        /// <summary>
        /// JS runEnemyPlan: one enemy entry's committed plan, on its own queue, as a reaction (no window, rider roll or
        /// automatic Counter return chains from it). Returns the halt reason when it fizzles instead.
        /// </summary>
        private string RunEnemyPlan(Core2ReactionEntry entry)
        {
            var halted = EnemyEntryHalt(entry);
            if (halted != null) return halted;
            var saved = new List<Core2FightAction>(_queue);
            _queue.Clear();
            var wasPlaying = _reactionPlaying;
            _reactionPlaying = true;
            try
            {
                var enemy = Enemies.FirstOrDefault(row => row.Id == entry.OwnerId);
                enemy?.PerformedMoves.Add(entry.MoveId);
                Emit("enemyMoveStarted", new JObject { ["sourceId"] = entry.OwnerId, ["enemyId"] = entry.EnemyId, ["moveId"] = entry.MoveId, ["kind"] = entry.Plan.Group.Kind, ["reaction"] = true });
                _pendingExpansionActions++;
                foreach (var action in RelinkPlan(entry.Plan).Actions) _queue.Add(action);
                DrainQueue();
            }
            finally
            {
                _reactionPlaying = wasPlaying;
                var left = new List<Core2FightAction>(_queue);
                _queue.Clear();
                if (Result == null) { _queue.AddRange(left); _queue.AddRange(saved); }
            }
            return null;
        }
    }
}
