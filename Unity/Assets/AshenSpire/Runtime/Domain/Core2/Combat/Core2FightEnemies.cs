// Core2FightEnemies.cs — the enemy side of a Core 2 fight: planning and presenting intents, the enemy phase with its
// saved plan, paying moves from the SP pool, charging moves, and the stance a telegraph or a card enters.
//
// JS at the pin:
//   src/engine/combat.js              rollIntents, presentIntent, enemyTurnStart, enemyOwnerTurnStart, enemyPhase,
//                                     executeMovePayload (SP paid from the pool; enemyMoveUnavailable when it cannot pay),
//                                     startDelayedMove, enemyMoveActions
//   src/engine/combatCardTactics.js   primeEnemyCounter (the telegraph sets the stance; a Counter move arms)
//   src/engine/gameProfileCombat.js   setProfileStance (matching-stance decision, entry grants, combatStanceChanged)
//   src/engine/combatRoundsAdapter.js commitEnemySp
//   src/engine/combatReactions.js     revealActor (enemyActorTurnStarted)
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed partial class Core2Fight
    {
        /// <summary>
        /// JS rollIntents: each living enemy without a charging move tops up its saved plan from its SP pool (the first turn
        /// opens with its firstMove), takes the head as its intent, and presents it; its follow-ups are due this turn.
        /// </summary>
        private void RollIntents(bool isFirstTurn)
        {
            foreach (var enemy in Enemies)
            {
                enemy.ActorIntentRevealed = false;
                if (!enemy.Alive) continue;
                if (enemy.PendingMove != null) { if (enemy.Intent != null) enemy.Intent.Pending = true; continue; }
                if (enemy.SkipNextTurn) { enemy.Intent = Core2Intent.Staggered(); continue; }
                var moveId = Core2EnemyPlanning.TakePlannedMove(this, enemy, isFirstTurn);
                if (moveId == null) { enemy.Intent = new Core2Intent { Kind = "unknown" }; continue; }
                PresentIntent(enemy, moveId);
                enemy.PlanDue = true;
                DrainQueue();
            }
        }

        /// <summary>
        /// JS presentIntent: show the move (buildIntent), roll the observer's Perception read of it, then prime it — the
        /// telegraph enters the move's stance (and arms a Counter move). The head and every follow-up go through here.
        /// </summary>
        public void PresentIntent(Core2Combatant enemy, string moveId)
        {
            ClearCounter(enemy);
            var raw = EnemyDef(enemy).Move(moveId).Raw;
            enemy.Intent = Core2EnemyPlanning.BuildIntent(Rules, enemy, raw, moveId);
            Core2Knowledge.Roll(this, enemy, raw["delay"] != null && raw["delay"].Type != JTokenType.Null);
            PrimeEnemyCounter(enemy, raw, moveId);
        }

        /// <summary>JS primeEnemyCounter: the telegraph sets the stance; a Counter move arms and lays down its Block now.</summary>
        private void PrimeEnemyCounter(Core2Combatant enemy, JObject raw, string moveId)
        {
            var carrier = Core2EnemyMoves.Carrier(Rules, raw, moveId);
            SetProfileStance(enemy, carrier, null);
            if (carrier.Profile.Maneuver != "counter") return;
            var move = Core2EnemyMoves.Expanded(Rules, raw, moveId);
            if (move["counterCoverage"] is JObject coverage) carrier.Profile.CounterCoverage = Core2CounterCoverage.FromJson(coverage);
            Core2Counters.Arm(Ctx, enemy, carrier);
            Emit("combatCounterArmed", new JObject { ["sourceKind"] = "enemy", ["sourceId"] = enemy.Id, ["moveId"] = moveId, ["mode"] = enemy.Counter.Mode });
            if (((int?)move["block"] ?? 0) > 0)
                Enqueue(new Core2FightAction { Effect = new JObject { ["op"] = "block", ["target"] = "self", ["amount"] = move["block"] }, Source = enemy, Owner = enemy, Target = enemy, Card = carrier, MoveId = moveId, Preparing = true });
            if (enemy.Intent?.MoveId == moveId) enemy.Intent.CounterDefensePrimed = true;
        }

        /// <summary>
        /// JS setProfileStance (configured, FixCore): the matching-stance decision is taken first; the entity stands in the
        /// carrier's configured stance; only a real change grants Precision (offensivePrecision + stat mix) and the Evade
        /// stack (unless the card applies Evade itself); combatStanceChanged reports it either way.
        /// </summary>
        private void SetProfileStance(Core2Combatant entity, Core2Carrier carrier, IReadOnlyList<string> tags)
        {
            if (carrier != null && carrier.FixCoreSameStance == null) carrier.FixCoreSameStance = Core2Stances.SameStance(entity, carrier);
            entity.MatchingStanceCardMultiplier = carrier?.FixCoreSameStance == true ? Rules.StanceEffectivenessMultiplier - 1 : 0;
            var grants = !Core2Stances.SameStance(entity, carrier) ? Core2Stances.EntryBonuses(Rules, carrier) : null;
            var name = Core2Stances.ConfiguredStance(carrier);
            var mirrored = Core2Stances.MirroredManeuver(carrier) != null;
            var previous = entity.Stance;
            entity.Stance = Rules.StanceInclude.Contains(name)
                ? new Core2Stance(name, mirrored ? "spell" : carrier?.Profile?.Camp ?? "physical", carrier?.Profile?.Range ?? "melee", mirrored)
                : new Core2Stance("neutral", "physical", "melee");
            if (grants != null && grants.Precision > 0) ApplyStatus(entity, "precision", Core2Accuracy.PrecisionEntryGrant(Rules, entity, grants.Precision), entity, tags ?? carrier?.Tags);
            if (grants != null && grants.Evade > 0 && !(carrier?.AppliedStatuses.Contains("evade") ?? false)) ApplyStatus(entity, "evade", grants.Evade, entity);
            RememberCombatStance(entity, previous);
            Emit("combatStanceChanged", new JObject { ["targetId"] = entity.Id, ["maneuver"] = entity.Stance.Maneuver });
        }

        // ================================================================================================================
        // The enemy phase (JS enemyPhase)
        // ================================================================================================================

        /// <summary>JS combat.reactionCursor: where a paused enemy phase resumes (the acting enemy and its stage).</summary>
        private bool _cursorActive;
        private int _cursorIndex;
        /// <summary>start (its owner turn start), payload (its head move) or drain (its queue, then its follow-ups).</summary>
        private string _cursorStage = "start";

        /// <summary>
        /// JS enemyPhase with its reaction cursor: the turn start, then each living enemy in roster order — its owner turn
        /// start, its head move (or a charge resolving), the queue, its saved plan's follow-ups, its owner turn end — and
        /// the turn end. A reaction offer pauses it after a drain; called again it resumes at the cursor.
        /// </summary>
        private void EnemyPhase()
        {
            if (!_cursorActive)
            {
                Phase = "enemy";
                FollowTrace.Clear();
                Emit("enemyTurnStart", new JObject { ["turn"] = Turn });
                foreach (var e in Enemies.Where(e => e.Alive))
                {
                    e.Block = 0;
                    ClearCounter(e);
                    e.Barrier = 0;
                    StartTacticalTurn(e);
                }
                DrainQueue();
                if (Result != null) return;
                _cursorActive = true; _cursorIndex = 0; _cursorStage = "start";
            }
            for (; _cursorIndex < Enemies.Count; _cursorIndex++, _cursorStage = "start")
            {
                var enemy = Enemies[_cursorIndex];
                if (Result != null) return;
                if (!enemy.Alive) continue;
                if (_cursorStage == "start")
                {
                    EnemyOwnerTurnStart(enemy);
                    if (Result != null) return;
                    if (!enemy.Alive) continue;
                    _cursorStage = "payload";
                }
                if (_cursorStage == "payload")
                {
                    _cursorStage = "drain";
                    // JS: staggered, locked by a control status, or denied by a Frozen stack — the move does not happen.
                    if (enemy.SkipNextTurn || ControlRestrictions(enemy).Locked || ConsumeControlActionLoss(enemy) > 0)
                    {
                        if (enemy.PendingMove == null) Core2Knowledge.Cancel(enemy);
                        enemy.SkipNextTurn = false;
                        enemy.PlanDue = false;
                    }
                    else if (enemy.PendingMove != null)
                    {
                        enemy.PlanDue = false;
                        if (Turn >= enemy.PendingMove.ResolveOnTurn)
                        {
                            var pending = enemy.PendingMove;
                            enemy.PendingMove = null;
                            ExecuteMovePayload(enemy, pending.MoveId, pending);
                        }
                    }
                    else if (enemy.Intent?.MoveId != null)
                    {
                        var raw = EnemyDef(enemy).Move(enemy.Intent.MoveId).Raw;
                        if (raw["delay"] != null && raw["delay"].Type != JTokenType.Null)
                        {
                            enemy.PlanDue = false;
                            enemy.PlannedMoves = new List<Core2PlannedMove>();
                            StartDelayedMove(enemy, enemy.Intent.MoveId);
                        }
                        else ExecuteMovePayload(enemy, enemy.Intent.MoveId, null, expanded: true);
                    }
                }
                DrainQueue();
                if (ReactionPaused) return;
                if (Result != null) return;
                // The saved plan's follow-ups play in order after the head; each leaves the plan before it runs, so a
                // reaction pause resumes at the next one. A stagger during the turn ends it.
                while (enemy.Alive && enemy.PlanDue && enemy.PendingMove == null && enemy.PlannedMoves?.Count > 0)
                {
                    if (enemy.SkipNextTurn || ControlRestrictions(enemy).Locked) { enemy.PlanDue = false; break; }
                    var next = enemy.PlannedMoves[0];
                    enemy.PlannedMoves.RemoveAt(0);
                    PresentIntent(enemy, next.MoveId);
                    var traced = new Core2FollowTrace { Read = Core2Knowledge.PerceptionRead(enemy), Outcome = "played" };
                    if (!FollowTrace.TryGetValue(enemy.Id, out var trace)) FollowTrace[enemy.Id] = trace = new List<Core2FollowTrace>();
                    trace.Add(traced);
                    var raw = EnemyDef(enemy).Move(next.MoveId).Raw;
                    if (raw["delay"] != null && raw["delay"].Type != JTokenType.Null)
                    {
                        enemy.PlannedMoves = new List<Core2PlannedMove>();
                        traced.Outcome = "charging";
                        StartDelayedMove(enemy, next.MoveId);
                    }
                    else traced.Outcome = ExecuteMovePayload(enemy, next.MoveId, null, expanded: true) ?? "played";
                    DrainQueue();
                    if (ReactionPaused) return;
                    if (Result != null) return;
                }
                if (enemy.PlannedMoves?.Count == 0) enemy.PlanDue = false;
                if (enemy.Alive)
                {
                    EndEnemyBurdens(enemy);
                    ProposalTurn(enemy, "end");
                    FireOwnerHooks(enemy, "ownerTurnEnd");
                    DrainQueue();
                    if (Result != null) return;
                    if (enemy.Alive) DecayAtTurnEnd(enemy);
                    EndControlTurn(enemy);
                }
            }
            Emit("enemyTurnEnd", new JObject { ["turn"] = Turn });
            DrainQueue();
            _cursorActive = false;
        }

        /// <summary>
        /// JS enemyOwnerTurnStart (FixCore): decay, the SP pool refilled less a Staggered break's loss, the acting enemy's
        /// intent made public (enemyActorTurnStarted), its owner hooks (Venom), then status recovery.
        /// </summary>
        private void EnemyOwnerTurnStart(Core2Combatant enemy)
        {
            DecayAtTurnStart(enemy);
            enemy.Stamina = Math.Max(0, enemy.MaxStamina - enemy.PendingStaminaLoss);
            enemy.PendingStaminaLoss = 0;
            BeginEnemyBurdens(enemy);
            enemy.ActorIntentRevealed = true;
            Emit("enemyActorTurnStarted", new JObject { ["sourceId"] = enemy.Id, ["enemyId"] = enemy.EnemyId, ["moveId"] = enemy.PendingMove?.MoveId ?? enemy.Intent?.MoveId });
            ProposalTurn(enemy, "start");
            FireOwnerHooks(enemy, "ownerTurnStart");
            DrainQueue();
            if (Result != null || !enemy.Alive) return;
            RecoverStatusesAtOwnerStart(enemy);
            SleepRestoration(enemy);
        }

        /// <summary>JS enemyMoveActions: the damage packet (none for a Counter), its Block and Barrier, then its effects.</summary>
        private List<Core2FightAction> EnemyMoveActions(Core2Combatant enemy, JObject move, string moveId, Core2Carrier carrier)
        {
            var actions = new List<Core2FightAction>();
            var counter = carrier.Profile.Maneuver == "counter";
            var primed = counter && enemy.Intent?.MoveId == moveId && enemy.Intent.CounterDefensePrimed;
            Core2FightAction Make(JObject effect, Core2Combatant target) => new Core2FightAction { Effect = effect, Source = enemy, Owner = enemy, Target = target, Card = carrier, MoveId = moveId };
            if (move["damage"] != null && move["damage"].Type != JTokenType.Null && !counter)
            {
                var damage = new JObject { ["op"] = "damage", ["target"] = "player", ["amount"] = Core2EnemyMoves.EnemyMoveDamage(enemy, move), ["hits"] = move["hits"] ?? 1 };
                if (move["damageSchool"] != null) damage["damageSchool"] = move["damageSchool"];
                actions.Add(Make(damage, Player));
            }
            if (move["block"] != null && move["block"].Type != JTokenType.Null && !primed) actions.Add(Make(new JObject { ["op"] = "block", ["target"] = "self", ["amount"] = move["block"] }, Player));
            if (move["barrier"] != null && move["barrier"].Type != JTokenType.Null && !primed) actions.Add(Make(new JObject { ["op"] = "gainBarrier", ["target"] = "self", ["amount"] = move["barrier"] }, enemy));
            foreach (var effect in (move["effects"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var op = (string)effect["op"];
                if (!counter || op != "damage" && op != "poiseDamage" && op != "wardDamage") actions.Add(Make(effect, Player));
            }
            return actions;
        }

        /// <summary>
        /// JS executeMovePayload (Core 2 action policy): price the move (its own SP plus the Advance its contacts need), refuse
        /// it when the pool cannot pay (enemyMoveUnavailable), else pay its SP now (the Advance when the range plan is
        /// accepted) and queue its actions as one group whose first action announces enemyMoveStarted. A charge that already
        /// paid resolves on its prepaid SP, paying only a newly required Advance.
        /// <paramref name="expanded"/>: JS enemyPhase hands the head move and each follow-up over already expanded
        /// (combat.js enemyPhase: executeMovePayload(combat, enemy, expandedEnemyMove(...))), and executeMovePayload prices
        /// the move it is handed (Rounds.enemyMoveSp(move) before it expands it again). An expanded maneuver-less move
        /// carries stanceTrigger:false and so earns no same-stance discount: a Spell move without a maneuver (graveWisp
        /// drain, eclipseCantor lunarRay) pays its full table price here although the plan budgeted the discounted one.
        /// The range closer is handed over raw (def.moves[closingId]), as is a resolving charge (it is prepaid).
        /// </summary>
        private string ExecuteMovePayload(Core2Combatant enemy, string moveId, Core2PendingMove prepaid, bool expanded = false)
        {
            var raw = EnemyDef(enemy).Move(moveId).Raw;
            var spCost = prepaid?.PaidSp ?? Core2EnemyMoves.MoveSp(Ctx, enemy, expanded ? Core2EnemyMoves.Expanded(Rules, raw, moveId) : raw, moveId);
            var move = Core2EnemyMoves.Expanded(Rules, raw, moveId);
            var carrier = Core2EnemyMoves.Carrier(Rules, raw, moveId);
            var actions = EnemyMoveActions(enemy, move, moveId, carrier);
            var quote = Core2EnemyMoves.QuoteRange(Rules, Range, enemy, Player, raw, moveId);
            var advance = quote?.AdvanceSp ?? 0;
            var available = Math.Max(0, enemy.Stamina);
            var required = prepaid != null ? Math.Max(0, advance - (prepaid.PaidAdvanceSp ?? 0)) : spCost + advance;
            if (prepaid == null && required > available && advance > 0)
            {
                var def = EnemyDef(enemy);
                var closingId = (string)def.Raw["rangeClosingMove"];
                var closing = closingId == null ? null : def.Move(closingId)?.Raw;
                var eligible = closing != null && !((bool?)closing["reaction"] ?? false) && (!((bool?)closing["locked"] ?? false) || enemy.UnlockedMoves.Contains(closingId));
                if (eligible && closingId != moveId && (closing["effects"] as JArray ?? new JArray()).OfType<JObject>().Any(e => (string)e["op"] == "moveEngagement")
                    && Core2EnemyMoves.MoveSp(Ctx, enemy, closing, closingId) <= available) return ExecuteMovePayload(enemy, closingId, null);
            }
            if (required > available)
            {
                Emit("enemyMoveUnavailable", new JObject { ["sourceId"] = enemy.Id, ["moveId"] = moveId, ["required"] = required, ["available"] = available, ["resource"] = "sp" });
                return "refused";
            }
            enemy.Stamina = available - (prepaid != null ? required : spCost);
            if (!AllowBurdenMove(enemy, spCost, move)) return "blocked";
            var start = new JObject { ["sourceId"] = enemy.Id, ["enemyId"] = enemy.EnemyId, ["moveId"] = moveId, ["kind"] = move["intent"],
                ["actionPreview"] = new JObject { ["spCost"] = spCost, ["totalSp"] = spCost + advance } };
            Core2Knowledge.Resolve(enemy);
            if (prepaid != null)
            {
                // The charge already paid its accepted price: its plan is accepted without a second Advance payment.
                var group = EnqueueGroup(actions, enemy, Player, carrier, enemyMoveStart: start, prepaidRange: true);
                return null;
            }
            EnqueueGroup(actions, enemy, Player, carrier, enemyMoveStart: start);
            return null;
        }

        /// <summary>
        /// JS startDelayedMove: a charging move pays its SP when it starts (commitEnemySp), does its whileCharging part now
        /// (its Block), and resolves on a later enemy turn; the telegraph stays, marked pending. JS quotes the expanded move
        /// enemyPhase hands it (combat.js startDelayedMove: Rounds.enemyMoveQuote(move) on expandedEnemyMove), so a
        /// maneuver-less charge (furnaceSaint openFurnace) pays its full price, with no same-stance discount.
        /// </summary>
        private string StartDelayedMove(Core2Combatant enemy, string moveId)
        {
            var raw = EnemyDef(enemy).Move(moveId).Raw;
            var quote = Core2EnemyMoves.Quote(Ctx, Range, enemy, Player, Core2EnemyMoves.Expanded(Rules, raw, moveId), moveId);
            var available = Math.Max(0, enemy.Stamina);
            if (quote.TotalSp > available)
            {
                Emit("enemyMoveUnavailable", new JObject { ["sourceId"] = enemy.Id, ["moveId"] = moveId, ["required"] = quote.TotalSp, ["available"] = available, ["resource"] = "sp" });
                return "refused";
            }
            enemy.Stamina = available - quote.TotalSp;
            var move = Core2EnemyMoves.Expanded(Rules, raw, moveId);
            var charging = move["delay"]?["whileCharging"] as JObject ?? new JObject();
            var carrier = Core2EnemyMoves.Carrier(Rules, raw, moveId);
            if (charging["block"] != null && charging["block"].Type != JTokenType.Null)
                Enqueue(new Core2FightAction { Effect = new JObject { ["op"] = "block", ["target"] = "self", ["amount"] = charging["block"] }, Source = enemy, Owner = enemy, Target = Player, Card = carrier, MoveId = moveId, Preparing = true });
            if (charging["barrier"] != null && charging["barrier"].Type != JTokenType.Null)
                Enqueue(new Core2FightAction { Effect = new JObject { ["op"] = "gainBarrier", ["target"] = "self", ["amount"] = charging["barrier"] }, Source = enemy, Owner = enemy, Target = enemy, Card = carrier, MoveId = moveId, Preparing = true });
            if ((charging["effects"] as JArray)?.Count > 0) throw new NotSupportedException("Charging effects of " + moveId + " are not ported");
            enemy.PendingMove = new Core2PendingMove
            {
                MoveId = moveId, ResolveOnTurn = Turn + ((int?)move["delay"]?["turns"] ?? 1), PaidSp = quote.Sp, PaidAdvanceSp = quote.AdvanceSp, PaidTotalSp = quote.TotalSp,
            };
            if (enemy.Intent?.MoveId == moveId) enemy.Intent.Pending = true;
            return null;
        }
    }
}
