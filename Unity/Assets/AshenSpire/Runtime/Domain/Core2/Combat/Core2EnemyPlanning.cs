// Core2EnemyPlanning.cs — how a Core 2 enemy spends its SP pool (SPEC §4.6a; owner 2026-10-12): each turn it plans
// moves with the weighted pick until the pool is spent, the head is shown as its intent, and the saved plan's
// follow-ups play in order after it during the enemy phase.
//
// JS at the pin:
//   src/engine/combat.js              weightedMovePick (moves whose quote fits the SP left; reaction and locked moves are
//                                     never rolled; maxConsecutive over movesHistory; an unaffordable approach is replaced
//                                     by the owned closing move), rollIntents, presentIntent, buildIntent
//   src/engine/combatRoundsAdapter.js extendEnemyQueue (the pool is the turn's SP; the opener is firstMove; the plan stops at
//                                     a charging move; each pick is priced from the stance the plan has projected so far),
//                                     takePlannedMove (top the saved plan up, then take its head), enemyTurnSp
//   src/engine/normalPreview.js       enemyPlanActions (the head and its follow-ups with their SP, as the timeline shows them)
// Every pick draws on the enemyAI stream; a plan is stored on the enemy (plannedMoves), so a saved fight resumes it.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>One entry of an enemy's plan as the timeline shows it (JS enemyTimelineCard: id, kind, sp, stance).</summary>
    public sealed class Core2PlanAction
    {
        public string MoveId { get; set; }
        public int Sp { get; set; }
        public string Kind { get; set; }
        public string Stance { get; set; }
        public string Target { get; set; }
    }

    public static class Core2EnemyPlanning
    {
        /// <summary>JS QUEUE_MAX: a plan never holds more than 16 moves (zero-cost guard).</summary>
        public const int QueueMax = 16;

        /// <summary>JS enemyTurnSp under the action policy: the SP pool less a Staggered break's loss.</summary>
        public static int TurnSp(Core2Combatant enemy) => Math.Max(0, enemy.MaxStamina - enemy.PendingStaminaLoss);

        /// <summary>The pricing state a plan is priced in (JS { ...combat, rangeState: clone } and { ...enemy }).</summary>
        public sealed class Pricing
        {
            public Core2Combatant Enemy;
            public Core2RangeState Range;
        }

        /// <summary>
        /// JS weightedMovePick(pricing, enemy, def, maxSp). Null when no move fits. Draws one enemyAI value when the eligible
        /// pool has positive total weight.
        /// </summary>
        public static string WeightedPick(Core2Fight fight, Pricing pricing, double maxSp)
        {
            var enemy = pricing.Enemy;
            var def = fight.Content.Enemy(enemy.EnemyId);
            var policy = fight.Ctx.Stamp.ActionPolicyEnabled;
            var closingId = (string)def.Raw["rangeClosingMove"];
            var closing = closingId == null ? null : def.Move(closingId)?.Raw;
            var canClose = policy && closing != null && !Flag(closing, "reaction") && (!Flag(closing, "locked") || enemy.UnlockedMoves.Contains(closingId))
                && (closing["effects"] as JArray ?? new JArray()).OfType<JObject>().Any(e => (string)e["op"] == "moveEngagement")
                && Core2EnemyMoves.Quote(fight.Ctx, pricing.Range, enemy, fight.Player, closing, closingId).TotalSp <= maxSp;
            var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
            var entries = new List<Core2EnemyMove>();
            foreach (var move in def.Moves)
            {
                if (Flag(move.Raw, "reaction") || Flag(move.Raw, "locked") && !enemy.UnlockedMoves.Contains(move.Id)) continue;
                if (!policy) { if (Core2EnemyMoves.MoveSp(fight.Ctx, enemy, move.Raw, move.Id) <= maxSp) entries.Add(move); continue; }
                var quote = Core2EnemyMoves.Quote(fight.Ctx, pricing.Range, enemy, fight.Player, move.Raw, move.Id);
                if (quote.TotalSp <= maxSp) { entries.Add(move); continue; }
                // An affordable attack whose approach is too expensive keeps its weight; its closing move plays instead.
                if (canClose && quote.Sp <= maxSp && quote.AdvanceSp > 0) { replacements[move.Id] = closingId; entries.Add(move); }
            }
            if (entries.Count == 0) return null;
            var eligible = entries.Where(move =>
            {
                var max = move.Raw["maxConsecutive"];
                if (max == null || max.Type == JTokenType.Null) return true;
                var run = 0;
                for (var i = enemy.MovesHistory.Count - 1; i >= 0 && enemy.MovesHistory[i] == move.Id; i--) run++;
                return run < (int)max;
            }).ToList();
            var pool = eligible.Count > 0 ? eligible : entries;
            string Replace(string id) => replacements.TryGetValue(id, out var r) ? r : id;
            var total = pool.Sum(m => (double)(m.Raw["weight"] ?? 0));
            if (total <= 0) return Replace(pool[0].Id);
            var roll = fight.Rng.Float("enemyAI") * total;
            foreach (var move in pool)
            {
                roll -= (double)(move.Raw["weight"] ?? 0);
                if (roll < 0) return Replace(move.Id);
            }
            return Replace(pool[pool.Count - 1].Id);
        }

        private static bool Flag(JObject row, string key) => row?[key] != null && row[key].Type != JTokenType.Null
            && !(row[key].Type == JTokenType.Boolean && !(bool)row[key]);

        /// <summary>
        /// JS extendEnemyQueue (normal combat, Core 2 policy): append picks to the plan until the pool is spent, nothing fits,
        /// or a charging move is planned; every pick is recorded in movesHistory as it is made.
        /// </summary>
        public static List<Core2PlannedMove> ExtendQueue(Core2Fight fight, Core2Combatant enemy, List<Core2PlannedMove> queue, bool isFirstTurn)
        {
            var def = fight.Content.Enemy(enemy.EnemyId);
            var policy = fight.Ctx.Stamp.ActionPolicyEnabled;
            var pool = policy ? TurnSp(enemy) : Math.Max(0, enemy.MaxStamina);
            var pricing = new Pricing { Enemy = enemy.Clone(), Range = fight.Range };
            foreach (var entry in queue)
            {
                if (policy) ProjectMovement(fight, pricing, entry.MoveId);
                var planned = Core2EnemyMoves.PlannedStance(def.Move(entry.MoveId).Raw, entry.MoveId);
                if (planned != null) pricing.Enemy.Stance = planned;
            }
            var spent = queue.Sum(e => e.Sp);
            while (queue.Count < QueueMax)
            {
                if (policy && queue.Any(e => def.Move(e.MoveId)?.Raw["delay"] != null && def.Move(e.MoveId).Raw["delay"].Type != JTokenType.Null)) break;
                if (queue.Count > 0 && spent >= pool) break;
                var opener = isFirstTurn && queue.Count == 0 ? (string)def.Raw["firstMove"] : null;
                var moveId = opener ?? WeightedPick(fight, pricing, pool - spent);
                if (policy && moveId != null && Core2EnemyMoves.Quote(fight.Ctx, pricing.Range, pricing.Enemy, fight.Player, def.Move(moveId).Raw, moveId).TotalSp > pool - spent)
                    moveId = WeightedPick(fight, pricing, pool - spent);
                if (!policy && moveId == null && queue.Count == 0 && opener == null) moveId = WeightedPick(fight, pricing, double.PositiveInfinity);
                if (moveId == null) break;
                var sp = policy ? Core2EnemyMoves.Quote(fight.Ctx, pricing.Range, pricing.Enemy, fight.Player, def.Move(moveId).Raw, moveId).TotalSp
                    : Core2EnemyMoves.MoveSp(fight.Ctx, pricing.Enemy, def.Move(moveId).Raw, moveId);
                if (spent + sp > pool && (policy || queue.Count > 0)) break;
                spent += sp;
                enemy.MovesHistory.Add(moveId);
                pricing.Enemy.MovesHistory.Add(moveId);
                queue.Add(new Core2PlannedMove { MoveId = moveId, Sp = sp });
                if (policy) ProjectMovement(fight, pricing, moveId);
                var stance = Core2EnemyMoves.PlannedStance(def.Move(moveId).Raw, moveId);
                if (stance != null) pricing.Enemy.Stance = stance;
            }
            return queue;
        }

        /// <summary>
        /// JS projectEnemyMovement: only an authored engagement move (an Advance) carries into the next pick's price; an
        /// attack's own Advance is temporary to that attack.
        /// </summary>
        private static void ProjectMovement(Core2Fight fight, Pricing pricing, string moveId)
        {
            if (pricing.Range == null || !pricing.Range.Policy.Enabled) return;
            var raw = fight.Content.Enemy(pricing.Enemy.EnemyId).Move(moveId).Raw;
            var move = Core2EnemyMoves.Expanded(fight.Rules, raw, moveId);
            if (move["delay"] != null && move["delay"].Type != JTokenType.Null) return;
            var effects = (move["effects"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            if (!effects.Any(e => (string)e["op"] == "moveEngagement")) return;
            foreach (var effect in Core2EnemyMoves.ActionEffects(fight.Rules, pricing.Enemy, raw, moveId))
            {
                if ((string)effect["op"] != "moveEngagement" || effect["oncePerTurn"] != null) continue;
                if (effect["if"] != null) throw new NotSupportedException("Enemy move " + moveId + ": a conditional engagement move is not ported");
                var repeats = Math.Max(0, Core2Formula.Int(effect["repeat"], 1));
                for (var r = 0; r < repeats; r++)
                    pricing.Range = pricing.Range.Moved(Core2Range.ActorKey(pricing.Enemy), Core2Range.ActorKey(fight.Player), (int)effect["bands"]);
            }
        }

        /// <summary>JS takePlannedMove: top the saved plan up to the pool, then take its head (the move this roll plays).</summary>
        public static string TakePlannedMove(Core2Fight fight, Core2Combatant enemy, bool isFirstTurn)
        {
            enemy.PlannedMoves ??= new List<Core2PlannedMove>();
            ExtendQueue(fight, enemy, enemy.PlannedMoves, isFirstTurn);
            if (enemy.PlannedMoves.Count == 0) return null;
            var head = enemy.PlannedMoves[0];
            enemy.PlannedMoves.RemoveAt(0);
            return head.MoveId;
        }

        /// <summary>
        /// JS enemyPlanActions: the current action first (its SP quoted now), then the saved follow-ups, as timeline cards.
        /// Empty for a staggered enemy, a dead one, or one with no intent.
        /// </summary>
        public static List<Core2PlanAction> PlanActions(Core2Fight fight, Core2Combatant enemy)
        {
            var output = new List<Core2PlanAction>();
            if (enemy == null || !enemy.Alive || enemy.Intent?.MoveId == null || enemy.Intent.Kind == "staggered") return output;
            var def = fight.Content.Enemy(enemy.EnemyId);
            var headSp = Core2EnemyMoves.Quote(fight.Ctx, fight.Range, enemy, fight.Player, def.Move(enemy.Intent.MoveId).Raw, enemy.Intent.MoveId).TotalSp;
            var entries = new[] { new Core2PlannedMove { MoveId = enemy.Intent.MoveId, Sp = headSp } }.Concat(enemy.PlannedMoves ?? new List<Core2PlannedMove>());
            foreach (var entry in entries)
            {
                var raw = def.Move(entry.MoveId).Raw;
                var kind = Core2EnemyMoves.MoveKind(raw);
                var tags = Core2Json.Strings(raw["tags"]);
                output.Add(new Core2PlanAction
                {
                    MoveId = entry.MoveId, Sp = entry.Sp, Kind = kind, Target = kind == "attack" ? "player" : "self",
                    Stance = kind == "attack" && (tags.Contains("smash") || tags.Contains("maneuver:smash")) ? "smash"
                        : kind == "attack" && (tags.Contains("sweep") || tags.Contains("maneuver:sweep")) ? "sweep"
                        : kind == "attack" ? "attack" : kind == "guard" ? "block" : kind == "counter" ? "counter" : null,
                });
            }
            return output;
        }

        /// <summary>JS buildIntent: the telegraph of a move (its expanded damage packet, Block, Barrier and combat profile).</summary>
        public static Core2Intent BuildIntent(Core2Rules rules, Core2Combatant enemy, JObject raw, string moveId)
        {
            var move = Core2EnemyMoves.Expanded(rules, raw, moveId);
            var hasDamage = move["damage"] != null && move["damage"].Type != JTokenType.Null;
            return new Core2Intent
            {
                Profile = Core2EnemyMoves.Carrier(rules, raw, moveId).Profile, Kind = (string)move["intent"], MoveId = moveId,
                Damage = Core2EnemyMoves.EnemyMoveDamage(enemy, move), Hits = hasDamage ? (int?)move["hits"] ?? 1 : (int?)null,
                Block = move["block"] != null && move["block"].Type != JTokenType.Null ? (int)move["block"] : (int?)null,
                Barrier = move["barrier"] != null && move["barrier"].Type != JTokenType.Null ? (int)move["barrier"] : (int?)null,
                Delayed = move["delay"] != null && move["delay"].Type != JTokenType.Null, Pending = false,
            };
        }
    }
}
