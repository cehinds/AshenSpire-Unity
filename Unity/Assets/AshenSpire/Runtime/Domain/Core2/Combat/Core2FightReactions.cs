// Core2FightReactions.cs — the reaction window and the reaction stack (reaction rules version 2) of a Core 2 fight:
// an incoming enemy contact (or Counter return) pauses before its defense begins and offers the player the reaction
// cards in hand that answer it; a chosen card is pushed on the root's stack (nothing is paid yet), the enemies may
// answer the push, and the stack resolves last pushed first — each entry paying and playing as it is reached, or
// fizzling — before the paused contact goes on. Back answers nothing and resumes the contact.
//
// JS at the pin:
//   src/engine/combatReactions.js  setReactionsEnabled, counterCoversAttack, eligible, candidateByManeuver, attackKind,
//                                  threatsFor, optionsFor (the candidate check: the card and every stacked entry must
//                                  play, LIFO, on a detached copy), reportUnavailable, rootEnded, cancelEndedPlayerRoot,
//                                  reportAlreadyAnswered, beforeReactionAction, beforeStackedReaction, beforePlayerRoot,
//                                  offerSeats, resolveReactionStack, playStackEntry, finishReactions, answerReaction,
//                                  reportRestOfRoot, deferRootPlay, restoreRootPlay, restoreReactionQueue
//   src/engine/reactionState.js    reactionPaused
//   src/engine/combat.js           dispatch (setReactions, chooseReaction → resumeEnemyTurn), completeEndTurn,
//                                  finishReactionPhase
//   src/engine/combatEngagement.js incomingRangeScope (a reaction plays in the incoming contact's frozen engagement),
//                                  paidCounterRange (a paid Counter reserves its return's Advance when it is played)
//   src/engine/combatRules.js      candidateState (detached copy: own RNG counters, empty event log)
// Core 2 runs version 2 with the perHit contact policy: each contact of a multi-hit move is its own root, the player
// pushes at most one card per contact (the cap is 1), and a Counter-return root offers at most one. Co-op seats are not
// ported (SPEC §0.7); the one seat is "player".
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>How an offered card would be played (JS option.play).</summary>
    public sealed class Core2ReactionPlay
    {
        public string CardInstanceId { get; set; }
        /// <summary>The enemy a card that aims at an enemy is played at; null for a card that does not aim.</summary>
        public string TargetId { get; set; }
        /// <summary>A Counter's return route: advance (pay the Advance its return needs) or throw; null for other cards.</summary>
        public string RangeRoute { get; set; }
        public Core2ReactionPlay Clone() => (Core2ReactionPlay)MemberwiseClone();
        public bool SameAs(Core2ReactionPlay other) => other != null && other.CardInstanceId == CardInstanceId && other.TargetId == TargetId && other.RangeRoute == RangeRoute;
    }

    /// <summary>One card the player may react with (JS pendingReaction.options[]): its price is what playing it pays.</summary>
    public sealed class Core2ReactionOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CardId { get; set; }
        public Core2ReactionPlay Play { get; set; }
        public int StaminaCost { get; set; }
        public int ManaCost { get; set; }
        /// <summary>The enemy entry this option answers when the stack holds enemy reactions; null otherwise.</summary>
        public string SourceId { get; set; }
        /// <summary>The card's largest planned contact against each enemy (it decides the enemies' playerReaction window).</summary>
        public Dictionary<string, int> Incoming { get; set; }
    }

    /// <summary>The pending reaction choice (JS combat.pendingReaction): answer it with ChooseReaction (an option id, or null for Back).</summary>
    public sealed class Core2ReactionOffer
    {
        public string Id { get; set; }
        public string GroupKey { get; set; }
        public string OwnerId { get; set; }
        /// <summary>The attacker: the incoming root's enemy, or (a player-card root) the top enemy reaction on the stack.</summary>
        public string SourceId { get; set; }
        public List<Core2ReactionOption> Options { get; set; } = new List<Core2ReactionOption>();
    }

    /// <summary>A committed enemy reaction's plan (JS entry.plan): its contact group and its actions, by entity id.</summary>
    public sealed class Core2EnemyReactionPlan
    {
        public Core2ActionGroup Group { get; set; }
        public List<Core2FightAction> Actions { get; set; } = new List<Core2FightAction>();
    }

    /// <summary>One entry of the reaction stack (JS reactionStack.entries[]): a player card not yet paid, or a committed enemy move.</summary>
    public sealed class Core2ReactionEntry
    {
        /// <summary>player or enemy.</summary>
        public string Side { get; set; }
        public string OwnerId { get; set; }
        // player entries
        public string OptionId { get; set; }
        public Core2ReactionPlay Play { get; set; }
        public string CardId { get; set; }
        public string CardInstanceId { get; set; }
        /// <summary>counter for a Counter card (one per seat per stack), else null.</summary>
        public string Maneuver { get; set; }
        public string SourceId { get; set; }
        public Dictionary<string, int> Incoming { get; set; }
        // enemy entries
        public string EnemyId { get; set; }
        public string MoveId { get; set; }
        public string TargetPlayerId { get; set; }
        public Core2EnemyReactionPlan Plan { get; set; }
        /// <summary>The SP the enemy paid when it pushed (JS spPayment): sp, advanceSp, totalSp.</summary>
        public Core2MoveQuote SpPayment { get; set; }
    }

    /// <summary>The reaction stack of one incoming root (JS combat.reactionStack).</summary>
    public sealed class Core2ReactionStack
    {
        public string GroupKey { get; set; }
        public List<Core2ReactionEntry> Entries { get; set; } = new List<Core2ReactionEntry>();
        public bool Resolving { get; set; }
        public List<string> CounterSeats { get; set; } = new List<string>();
    }

    /// <summary>A reserved Counter return (JS carrier.rangeCounterReservation): the return's range plan, paid when the Counter was played.</summary>
    public sealed class Core2CounterReservation
    {
        public string SourceKey { get; set; }
        public string TargetKey { get; set; }
        public string IncomingRootId { get; set; }
        public string ContactId { get; set; }
        public Core2RangePlan RangePlan { get; set; }
        public bool Consumed { get; set; }
    }

    public sealed partial class Core2Fight
    {
        public const string Seat = "player";

        /// <summary>The choice the fight waits on, or null. While it is set only ChooseReaction is accepted.</summary>
        public Core2ReactionOffer PendingReaction { get; private set; }
        /// <summary>The reaction stack of the incoming root being answered (JS reactionStack), or null.</summary>
        public Core2ReactionStack ReactionStack { get; private set; }
        /// <summary>JS reactionPaused: a choice is pending or a reaction's play is still holding the paused queue.</summary>
        public bool ReactionPaused => PendingReaction != null || _reactionResume != null;
        /// <summary>JS enemyReactionRolls: the enemyReaction draws per window, cleared at each player turn start.</summary>
        public Dictionary<string, bool> EnemyReactionRolls { get; private set; } = new Dictionary<string, bool>(StringComparer.Ordinal);

        private List<Core2FightAction> _reactionResume;
        private bool _reactionPlaying;
        private string _reactionSourceId;
        private bool _reactionHandCleanup;
        private PendingPlay _reactionRootPlay;
        /// <summary>JS ctx._rangeContext / _rangeIncoming: the incoming contact's frozen engagement while a reaction plays.</summary>
        private Core2RangePlan _rangeContext;
        private (string RootId, string ContactId, string SourceKey, string TargetKey)? _rangeIncoming;
        /// <summary>JS ctx._groupSink: an offer check reads the planned contacts of the card it tries.</summary>
        private List<Core2ActionGroup> _groupSink;

        private static readonly Dictionary<string, int> ReasonRank = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["notCovered"] = 0, ["other"] = 1, ["controlled"] = 2, ["unplayable"] = 3, ["cost"] = 4,
        };

        // ================================================================================================================
        // Commands (JS dispatch setReactions / chooseReaction)
        // ================================================================================================================

        /// <summary>JS setReactionsEnabled: the Reaction switch, only between actions on the player turn. An armed Counter still answers.</summary>
        public void SetReactions(bool enabled)
        {
            if (Result != null) throw new InvalidOperationException("Combat is over");
            if (Phase != "player" || ReactionPaused) throw new InvalidOperationException("Change reactions between actions on the player turn");
            ReactionsEnabled = enabled;
        }

        /// <summary>
        /// JS dispatch chooseReaction: answer the pending offer with one of its options, or null (Back) to let the incoming
        /// contact go on unanswered; then the paused turn resumes (JS resumeEnemyTurn). A stale offer id or an option
        /// that is no longer playable is refused and changes nothing.
        /// </summary>
        public void ChooseReaction(string offerId, string optionId)
        {
            if (Result != null) throw new InvalidOperationException("Combat is over");
            AnswerReaction(Seat, offerId, optionId);
            ResumeEnemyTurn();
        }

        /// <summary>
        /// JS createStanceLedger(kindForCard) over the event log: the maneuver of the player's last confirmed card (a card
        /// played as a reaction included), or null before any. Auto reads it while the incoming action is unknown.
        /// </summary>
        public string LastConfirmedManeuver()
        {
            var played = Events.LastOrDefault(e => e.Type == "cardPlayed" && ((string)e.Data["sourceId"] ?? Seat) == Seat);
            if (played == null) return null;
            return Core2AutoCombat.KindForCard(CardCarrier(new Core2CardInstance { InstanceId = (string)played.Data["cardInstanceId"], CardId = (string)played.Data["cardId"] }));
        }

        private void RequireNoPendingReaction()
        {
            if (PendingReaction != null) throw new InvalidOperationException("Answer the pending reaction first");
        }

        /// <summary>JS resumeEnemyTurn: continue the drain the offer paused (and the card it belonged to), or the enemy phase.</summary>
        private void ResumeEnemyTurn()
        {
            if (_reactionResume != null) return;
            if (!_cursorActive) { DrainQueue(); FinishPlay(); return; }
            EnemyPhase();
            if (Result != null || ReactionPaused) return;
            FinishReactionPhase();
        }

        /// <summary>JS finishReactionPhase: the deferred hand cleanup, then new intents and the next player turn.</summary>
        private void FinishReactionPhase()
        {
            if (_reactionHandCleanup)
            {
                FinishPlayerHand();
                _reactionHandCleanup = false;
                if (Result != null) return;
            }
            RollIntents(false);
            StartPlayerTurn();
        }

        /// <summary>JS finishReactions: the fight ended, so every reaction state goes.</summary>
        private void FinishReactions()
        {
            _cursorActive = false; PendingReaction = null; _reactionResume = null; _reactionHandCleanup = false; ReactionStack = null; _reactionRootPlay = null;
            _pendingExpansionActions = 0;
            _queue.Clear();
        }

        // ================================================================================================================
        // Seats, rows and eligibility
        // ================================================================================================================

        private bool SeatPresent(string seat) => Player != null && Player.Alive;
        private bool SeatGone(string seat) => Player == null || !Player.Alive || Player.Hp <= 0;
        /// <summary>JS haltReason: a staggered attacker (attackerStaggered) or one a control status locks (attackerControlled) offers no reaction.</summary>
        private string HaltReason(Core2Combatant source) => source.SkipNextTurn ? "attackerStaggered" : ControlRestrictions(source).Locked ? "attackerControlled" : null;

        /// <summary>JS playerRootSeat: the acting seat of a player-card root ("player"), else null.</summary>
        private string PlayerRootSeat(Core2ActionGroup group) =>
            group?.Carrier?.InstanceId == null || group.Source?.Side == Core2Side.Enemy ? null : Seat;

        private Core2GroupTarget RowFor(Core2ActionGroup group) => group?.Targets.Values.FirstOrDefault(row => row.Entity?.Id == Player.Id);

        private Core2ReactionStack StackOf(Core2ActionGroup group) => ReactionStack != null && ReactionStack.GroupKey == group.Key ? ReactionStack : null;

        private Core2ActionGroup QueuedGroup(string key) => _queue.FirstOrDefault(action => action.Group?.Key == key)?.Group;

        /// <summary>JS counterCoversAttack: a Counter answers a contact its coverage reaches.</summary>
        private bool CounterCovers(Core2Carrier card, Core2ContactPlan contact)
        {
            if (!card.Tags.Contains("maneuver:counter")) return false;
            var mode = card.Tags.Contains("counter:spell") ? "spell" : card.Tags.Contains("counter:ranged") ? "ranged" : "melee";
            var authored = Content.Card(card.Id).Raw["counterCoverage"];
            var coverage = authored is JObject ? Core2CounterCoverage.FromJson(authored) : Rules.CounterDefaultCoverage.TryGetValue(mode, out var row) ? row : null;
            // The contact's own profile as planned (JS reads the raw contact, not the normalized one).
            return Core2Counters.CoverageMatches(coverage, contact.Profile, contact.Effect ?? "damage");
        }

        /// <summary>JS eligible (version 2): a trait:reaction card; a Counter needs coverage, any other card a positive contact.</summary>
        private bool Eligible(Core2Carrier card, Core2GroupTarget row)
        {
            if (!card.Tags.Contains("trait:reaction")) return false;
            return card.Tags.Contains("maneuver:counter") ? row.Contacts.Any(contact => CounterCovers(card, contact)) : row.Contacts.Any(Positive);
        }

        private static bool CandidateByManeuver(Core2Carrier card) => card.Tags.Contains("trait:reaction");

        /// <summary>JS attackKind: what the refusal log line calls the incoming attack.</summary>
        private static string AttackKind(Core2GroupTarget row)
        {
            var hit = row.Contacts.FirstOrDefault(contact => Positive(contact) && contact.Effect == null);
            if (hit == null) return "statusOnly";
            var profile = hit.Profile ?? new Core2CombatProfile();
            if (profile.Maneuver == "sweep") return "sweep";
            if (profile.Targeting == "area") return "area";
            if (profile.Camp == "spell") return "spell";
            if (profile.Range == "reach" || profile.Range == "ranged") return "ranged";
            return "attack";
        }

        private List<string> CandidateCardIds() =>
            Hand.Select(inst => (inst, card: CardCarrier(inst))).Where(x => CandidateByManeuver(x.card)).Select(x => x.inst.CardId).Distinct().ToList();

        /// <summary>
        /// JS reportUnavailable: once per root and seat, and only when the hand holds a reaction card, say why no reaction
        /// is offered (combatReactionUnavailable: switchOff, notCovered, cost with the resource, attackerStaggered…).
        /// </summary>
        private void ReportUnavailable(Core2ActionGroup group, Core2GroupTarget row, string reason, JObject extra = null)
        {
            if (group.ReactionNoticed.Contains(Seat)) return;
            var cardIds = CandidateCardIds();
            if (cardIds.Count == 0) return;
            group.ReactionNoticed.Add(Seat);
            var payload = new JObject { ["ownerId"] = Seat, ["sourceId"] = group.Source?.Id, ["reason"] = reason, ["cardId"] = (string)extra?["cardId"] ?? cardIds[0], ["cardIds"] = new JArray(cardIds), ["attackKind"] = AttackKind(row) };
            if (extra != null) foreach (var p in extra.Properties()) if (p.Name != "cardId") payload[p.Name] = p.Value;
            Emit("combatReactionUnavailable", payload);
        }

        /// <summary>JS rootEnded: the attacker is dead, or a player-card root's acting seat is gone.</summary>
        private bool RootEnded(Core2ActionGroup group)
        {
            var source = group.Source == null ? null : Enemies.FirstOrDefault(enemy => enemy.Id == group.Source.Id);
            if (source != null) return !source.Alive;
            var acting = PlayerRootSeat(group);
            return acting != null && SeatGone(acting);
        }

        private void CancelEndedPlayerRoot(Core2ActionGroup group)
        {
            var acting = PlayerRootSeat(group);
            if (acting != null && SeatGone(acting)) group.Cancelled = true;
            RestoreRootPlay();
        }

        /// <summary>JS reportAlreadyAnswered: a later action of an answered (non per-hit) root says the rest cannot be answered.</summary>
        private void ReportAlreadyAnswered(Core2FightAction action, Core2ActionGroup group)
        {
            if (group.Contacts != null) return;
            foreach (var id in group.ReactionAnswered.ToList())
            {
                var row = RowFor(group);
                if (row == null || group.ReactionNoticed.Contains(id) || !SeatPresent(id)) continue;
                if (!action.ContactRefs.Any(contact => group.Targets.TryGetValue(contact.Key, out var target) && target == row && contact.Index < row.Contacts.Count && Positive(row.Contacts[contact.Index]))) continue;
                ReportUnavailable(group, row, "alreadyAnswered");
            }
        }

        /// <summary>JS reportRestOfRoot: the one offer covered the whole (non per-hit) root; its further hits cannot be answered.</summary>
        private void ReportRestOfRoot(Core2ActionGroup group, string ownerId)
        {
            if (group.Contacts != null || PlayerRootSeat(group) != null) return;
            var row = RowFor(group);
            if (row == null || row.Contacts.Count(Positive) < 2 || !SeatPresent(ownerId)) return;
            var source = group.Source == null ? null : Enemies.FirstOrDefault(enemy => enemy.Id == group.Source.Id);
            if (source != null && (!source.Alive || source.SkipNextTurn)) return;
            ReportUnavailable(group, row, "alreadyAnswered");
        }

        // ================================================================================================================
        // The window (JS beforeReactionAction and its branches)
        // ================================================================================================================

        /// <summary>
        /// JS beforeReactionAction: before the queue's head runs, an enemy root about to begin against the player (or a
        /// player-card root an enemy may answer) opens its window. True pauses the drain: an offer is pending (or a
        /// stacked entry's own play is holding the queue).
        /// </summary>
        private bool BeforeReactionAction(Core2FightAction action)
        {
            ActivateContact(action);
            if (_reactionPlaying) return false;
            if (PendingReaction != null) return true;
            var group = action.Group;
            if (group == null || group.Carrier?.ReactionDepth > 0) return false;
            if (action.Source?.Side != Core2Side.Enemy) return !group.Begun && action.Source?.Side == Core2Side.Player && BeforePlayerRoot(group);
            if (group.Begun) { ReportAlreadyAnswered(action, group); return false; }
            var halted = HaltReason(action.Source);
            var present = group.Targets.Values.Any(r => r.Contacts.Any(Positive) && r.Entity.Alive);
            if (!group.ContactTelegraphed && action.Source.Alive && halted == null && present)
            {
                group.ContactTelegraphed = true;
                var data = (JObject)(group.EnemyMoveStart ?? group.CounterReturn ?? new JObject()).DeepClone();
                data["sourceId"] = action.Source.Id; data["contactKey"] = group.Key; data["kind"] = "attack";
                data["contactIndex"] = Math.Max(0, group.ContactIndex); data["contactCount"] = group.Contacts?.Count ?? 1;
                Emit("combatContactPrepared", data);
            }
            return BeforeStackedReaction(action, group);
        }

        /// <summary>JS beforeStackedReaction: a halted attacker reports and opens nothing; otherwise the seat is offered.</summary>
        private bool BeforeStackedReaction(Core2FightAction action, Core2ActionGroup group)
        {
            if (StackOf(group) == null)
            {
                if (!action.Source.Alive) return false;
                var halted = HaltReason(action.Source);
                if (halted != null)
                {
                    var row = RowFor(group);
                    if (row != null && row.Contacts.Any(Positive) && !group.ReactionAnswered.Contains(Seat) && SeatPresent(Seat)) ReportUnavailable(group, row, halted);
                    return false;
                }
            }
            var open = action.Source.Alive && HaltReason(action.Source) == null;
            return OfferSeats(group, open);
        }

        /// <summary>JS beforePlayerRoot: before a player card's root begins, the enemies it hits may react (playerAction).</summary>
        private bool BeforePlayerRoot(Core2ActionGroup group)
        {
            if (!FightReacts(null)) return false;
            var acting = PlayerRootSeat(group);
            if (acting == null || group.Carrier.CombatReaction || group.CounterReturn != null) return false;
            if (StackOf(group) == null)
            {
                // A stack whose last entry paused for its own choice has emptied: the root resumes.
                if (group.EnemyWindows != null) { if (ReactionStack == null) RestoreRootPlay(); return false; }
                if (ReactionStack != null || SeatGone(acting)) return false;
                OpenEnemyWindow(group, "playerAction", null);
                if (StackOf(group) == null) return false;
            }
            return OfferSeats(group, !SeatGone(acting));
        }

        /// <summary>
        /// JS offerSeats: the seat is offered (again after each push) until it answers Back, has no option left or holds
        /// the cap (1 under perHit); then the stack resolves. Reactions switched off report switchOff and offer nothing.
        /// </summary>
        private bool OfferSeats(Core2ActionGroup group, bool open)
        {
            var stack = StackOf(group);
            var playerRoot = PlayerRootSeat(group) != null;
            const int cap = 1; // perHit: one entry per seat per contact (counterReturnMaxDepth is 1 as well).
            var row = playerRoot ? null : RowFor(group);
            var enemyRows = (stack?.Entries ?? new List<Core2ReactionEntry>()).Where(entry => entry.Side == "enemy" && entry.TargetPlayerId == Seat
                && PlanRowFor(entry)?.Contacts.Any(Positive) == true).ToList();
            if ((row == null || !row.Contacts.Any(Positive)) && enemyRows.Count == 0) return ResolveIfStacked(stack);
            if (group.ReactionAnswered.Contains(Seat)) return ResolveIfStacked(stack);
            var depth = (stack?.Entries ?? new List<Core2ReactionEntry>()).Count(entry => entry.Side == "player" && entry.OwnerId == Seat);
            if (!open || depth >= cap || !Player.Alive || !ReactionsEnabled)
            {
                if (row != null && open && depth < cap && !ReactionsEnabled && SeatPresent(Seat)) ReportUnavailable(group, row, "switchOff");
                group.ReactionAnswered.Add(Seat);
                return ResolveIfStacked(stack);
            }
            var refusal = new JObject();
            var options = OptionsFor(row, group, refusal);
            if (options.Count == 0)
            {
                if (depth == 0 && row != null)
                {
                    var reason = (string)refusal["reason"] ?? "other";
                    var extra = (JObject)refusal.DeepClone(); extra.Remove("reason");
                    ReportUnavailable(group, row, reason, extra);
                }
                group.ReactionAnswered.Add(Seat);
                return ResolveIfStacked(stack);
            }
            var sourceId = playerRoot ? enemyRows.Last().OwnerId : group.Source?.Id;
            PendingReaction = new Core2ReactionOffer { Id = group.Key + ":" + Seat + ":" + depth, GroupKey = group.Key, OwnerId = Seat, SourceId = sourceId, Options = options };
            Emit("combatReactionOffered", new JObject { ["offerId"] = PendingReaction.Id, ["ownerId"] = Seat, ["sourceId"] = sourceId });
            return true;
        }

        private bool ResolveIfStacked(Core2ReactionStack stack) => stack?.Entries.Count > 0 && ResolveReactionStack();

        private Core2GroupTarget PlanRowFor(Core2ReactionEntry entry) => entry.Plan?.Group?.Targets.Values.FirstOrDefault(target => target.Entity?.Id == Player.Id);

        /// <summary>JS threatsFor: the enemy entries' plans against the seat (top first), then an enemy root's own row.</summary>
        private List<(string EnemyId, Core2GroupTarget Row)> ThreatsFor(Core2GroupTarget row, Core2ActionGroup group, List<Core2ReactionEntry> entries)
        {
            var threats = new List<(string, Core2GroupTarget)>();
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry.Side != "enemy" || entry.TargetPlayerId != Seat) continue;
                var planRow = PlanRowFor(entry);
                if (planRow != null && planRow.Contacts.Any(Positive)) threats.Add((entry.OwnerId, planRow));
            }
            if (row != null && PlayerRootSeat(group) == null && (threats.Count == 0 || row.Contacts.Any(Positive))) threats.Add((group.Source?.Id, row));
            return threats;
        }

        /// <summary>
        /// JS optionsFor: every hand card eligible against the threats, played on a detached copy of the fight (as a reaction,
        /// in the incoming contact's engagement) together with every stacked entry after it; an option is offered only when all
        /// of them play. A card that cannot is noted in <paramref name="refusal"/> with the furthest reason (cost, unplayable…).
        /// </summary>
        private List<Core2ReactionOption> OptionsFor(Core2GroupTarget row, Core2ActionGroup group, JObject refusal)
        {
            var options = new List<Core2ReactionOption>();
            var stack = StackOf(group);
            var entries = stack?.Entries ?? new List<Core2ReactionEntry>();
            var counterStacked = entries.Any(entry => entry.Side == "player" && entry.OwnerId == Seat && entry.Maneuver == "counter");
            var enemyEntries = entries.Any(entry => entry.Side == "enemy");
            var threats = ThreatsFor(row, group, entries);
            var captureIncoming = FightReacts("playerReaction");
            foreach (var inst in Hand.ToList())
            {
                if (entries.Any(entry => entry.Side == "player" && entry.OwnerId == Seat && entry.Play?.CardInstanceId == inst.InstanceId)) continue;
                var card = CardCarrier(inst);
                void Note(string reason, JObject extra = null)
                {
                    if (refusal == null) return;
                    var current = (string)refusal["reason"];
                    if (current != null && ReasonRank[reason] <= ReasonRank[current]) return;
                    refusal.RemoveAll();
                    refusal["reason"] = reason;
                    if (extra != null) foreach (var p in extra.Properties()) refusal[p.Name] = p.Value;
                    refusal["cardId"] = inst.CardId;
                }
                if (counterStacked && card.Tags.Contains("maneuver:counter")) continue;
                var answered = threats.Where(threat => Eligible(card, threat.Row)).ToList();
                if (answered.Count == 0) { if (CandidateByManeuver(card)) Note("notCovered"); continue; }
                var aimed = CardAimsAtEnemy(Content.Card(inst.CardId), card);
                var aims = aimed ? answered.Select(threat => threat.EnemyId).Distinct().ToList() : new List<string> { answered[0].EnemyId };
                var routes = Range != null && card.Tags.Contains("maneuver:counter") ? new[] { "advance", "throw" } : new string[] { null };
                foreach (var route in routes) foreach (var aim in aims)
                {
                    var candidate = Candidate();
                    candidate._reactionPlaying = true;
                    candidate._reactionSourceId = aim;
                    var play = new Core2ReactionPlay { CardInstanceId = inst.InstanceId, TargetId = aimed ? aim : null, RangeRoute = route };
                    try
                    {
                        var paymentStart = candidate.Events.Count;
                        if (captureIncoming) candidate._groupSink = new List<Core2ActionGroup>();
                        candidate.WithIncomingRange(group, () => candidate.DoPlayCard(play));
                        var planned = candidate._groupSink?.FirstOrDefault(g => g.Carrier?.InstanceId == inst.InstanceId);
                        candidate._groupSink = null;
                        var paid = candidate.Events.Skip(paymentStart).FirstOrDefault(e => e.Type == "cardPlayed");
                        if (paid == null || !candidate.Player.Alive) { Note("other"); continue; }
                        var reservation = candidate.Player.Counter?.Carrier?.RangeCounterReservation;
                        if (route == "throw" && reservation?.RangePlan?.Targets.Any(t => t.MissingBands > 0) != true) continue;
                        // The stacked entries resolve after this option, last pushed first; all must play.
                        for (var i = entries.Count - 1; i >= 0; i--)
                        {
                            if (candidate.RootEnded(group)) break;
                            var entry = entries[i];
                            if (entry.Side == "enemy") { candidate.RunEnemyPlan(entry); continue; }
                            var start = candidate.Events.Count;
                            candidate.DoPlayCard(entry.Play.Clone());
                            if (!candidate.Events.Skip(start).Any(e => e.Type == "cardPlayed")) throw new InvalidOperationException("Stacked reaction did not play");
                        }
                        Dictionary<string, int> incoming = null;
                        if (captureIncoming)
                        {
                            incoming = new Dictionary<string, int>(StringComparer.Ordinal);
                            foreach (var enemy in Enemies)
                            {
                                var amount = IncomingAgainst(planned, enemy.Id);
                                if (amount > 0) incoming[enemy.Id] = amount;
                            }
                        }
                        options.Add(new Core2ReactionOption
                        {
                            Id = inst.InstanceId + ":0:default" + (route == "throw" ? ":throw" : "") + (aimed && enemyEntries ? ":" + aim : ""),
                            Name = Content.Card(inst.CardId).Name, CardId = inst.CardId, Play = play,
                            StaminaCost = (int?)paid.Data["staminaSpent"] ?? (int?)paid.Data["energySpent"] ?? 0, ManaCost = (int?)paid.Data["manaSpent"] ?? 0,
                            Incoming = incoming, SourceId = enemyEntries ? aim : null,
                        });
                    }
                    catch (Core2PlayRefusedException refused) { Note(ReasonRank.ContainsKey(refused.Reason ?? "") ? refused.Reason : "other", refused.Extra()); }
                    catch (InvalidOperationException) { Note("other"); }
                }
            }
            return options;
        }

        /// <summary>JS cardTargetPlan(...).mode === 'enemy': the card's immediate effects aim at an enemy (a Counter's damage is its deferred return).</summary>
        private static bool CardAimsAtEnemy(Core2Card card, Core2Carrier carrier)
        {
            var counter = carrier.Profile?.Maneuver == "counter";
            var effects = card.Effects.OfType<JObject>().Where(e => !counter || (string)e["op"] != "damage" && (string)e["op"] != "poiseDamage");
            return effects.Any(e => HostileTargets.Contains((string)e["target"] ?? ""));
        }

        // ================================================================================================================
        // Answer, stack resolution (JS answerReaction, resolveReactionStack, playStackEntry)
        // ================================================================================================================

        private void AnswerReaction(string ownerId, string offerId, string optionId)
        {
            var pending = PendingReaction;
            if (pending == null || pending.Id != offerId || pending.OwnerId != ownerId) throw new InvalidOperationException("This reaction choice is unavailable");
            var group = QueuedGroup(pending.GroupKey);
            if (group == null || group.Begun) throw new InvalidOperationException("The incoming action has already resolved");
            var option = optionId == null ? null : pending.Options.FirstOrDefault(row => row.Id == optionId);
            if (optionId != null && option == null) throw new InvalidOperationException("Choose an available reaction card");
            if (option != null)
            {
                var row = PlayerRootSeat(group) != null ? null : RowFor(group);
                var canonical = OptionsFor(row, group, null).FirstOrDefault(current => current.Id == optionId);
                if (canonical == null || !canonical.Play.SameAs(option.Play) || canonical.CardId != option.CardId || canonical.StaminaCost != option.StaminaCost || canonical.ManaCost != option.ManaCost)
                    throw new InvalidOperationException("That reaction card is no longer playable");
                option = canonical;
                var instance = Hand.First(inst => inst.InstanceId == option.Play.CardInstanceId);
                var card = CardCarrier(instance);
                var stack = ReactionStack ??= new Core2ReactionStack { GroupKey = group.Key };
                var entry = new Core2ReactionEntry
                {
                    Side = "player", OwnerId = ownerId, OptionId = option.Id, Play = option.Play, CardId = option.CardId, CardInstanceId = option.Play.CardInstanceId,
                    Maneuver = card.Tags.Contains("maneuver:counter") ? "counter" : null, SourceId = option.SourceId, Incoming = option.Incoming,
                };
                stack.Entries.Add(entry);
                group.ReactionAnswered.Add(ownerId); // perHit
                PendingReaction = null;
                var depth = stack.Entries.Count(e => e.Side == "player" && e.OwnerId == ownerId);
                Emit("combatReactionChosen", new JObject { ["offerId"] = offerId, ["ownerId"] = ownerId, ["cardInstanceId"] = option.Play.CardInstanceId });
                Emit("combatReactionStacked", new JObject { ["offerId"] = offerId, ["ownerId"] = ownerId, ["cardInstanceId"] = option.Play.CardInstanceId, ["depth"] = depth });
                // Enemy reactions: the push opens a playerReaction window, then the seat is re-offered.
                OpenEnemyWindow(group, "playerReaction", entry);
                return;
            }
            group.ReactionAnswered.Add(ownerId);
            PendingReaction = null;
            Emit("combatReactionChosen", new JObject { ["offerId"] = offerId, ["ownerId"] = ownerId, ["cardInstanceId"] = null });
            ReportRestOfRoot(group, ownerId);
        }

        /// <summary>
        /// JS resolveReactionStack: the committed entries, top first. An enemy entry runs its committed plan (or fizzles when
        /// its enemy is halted); a player entry pays and plays its card (or fizzles). A root that ends (its attacker dead)
        /// fizzles what is left. True while a played entry still holds the paused queue.
        /// </summary>
        private bool ResolveReactionStack()
        {
            var stack = ReactionStack;
            if (stack == null || stack.Entries.Count == 0) return false;
            var group = QueuedGroup(stack.GroupKey) ?? throw new InvalidOperationException("The reaction stack has no incoming action");
            stack.Resolving = true;
            while (stack.Entries.Count > 0)
            {
                if (RootEnded(group))
                {
                    while (stack.Entries.Count > 0)
                    {
                        var left = Pop(stack);
                        if (left.Side == "enemy") { Emit("combatReactionFizzled", EnemyFizzle(left, "rootEnded")); continue; }
                        Emit("combatReactionFizzled", new JObject { ["ownerId"] = left.OwnerId, ["cardInstanceId"] = left.Play.CardInstanceId, ["reason"] = "rootEnded" });
                    }
                    ReactionStack = null;
                    CancelEndedPlayerRoot(group);
                    return false;
                }
                var entry = Pop(stack);
                if (entry.Side == "enemy")
                {
                    var halted = RunEnemyPlan(entry);
                    if (halted != null) Emit("combatReactionFizzled", EnemyFizzle(entry, halted));
                    if (Result != null) { ReactionStack = null; return false; }
                    continue;
                }
                if (PlayStackEntry(group, entry))
                {
                    if (stack.Entries.Count == 0) ReactionStack = null;
                    return true;
                }
            }
            ReactionStack = null;
            CancelEndedPlayerRoot(group);
            return false;
        }

        private static Core2ReactionEntry Pop(Core2ReactionStack stack)
        {
            var entry = stack.Entries[stack.Entries.Count - 1];
            stack.Entries.RemoveAt(stack.Entries.Count - 1);
            return entry;
        }

        private static JObject EnemyFizzle(Core2ReactionEntry entry, string reason) => new JObject
        {
            ["side"] = "enemy", ["ownerId"] = entry.OwnerId, ["enemyId"] = entry.EnemyId, ["moveId"] = entry.MoveId, ["targetPlayerId"] = entry.TargetPlayerId, ["reason"] = reason,
        };

        /// <summary>JS playStackEntry: one player entry pays and plays now (its own drain runs on a fresh queue), or fizzles.</summary>
        private bool PlayStackEntry(Core2ActionGroup group, Core2ReactionEntry entry)
        {
            var ownerId = entry.OwnerId;
            var play = entry.Play;
            if (!SeatPresent(ownerId))
            {
                Emit("combatReactionFizzled", new JObject { ["ownerId"] = ownerId, ["cardInstanceId"] = play.CardInstanceId, ["reason"] = "ownerGone" });
                return false;
            }
            var instance = Hand.FirstOrDefault(inst => inst.InstanceId == play.CardInstanceId);
            if (instance == null)
            {
                Emit("combatReactionFizzled", new JObject { ["ownerId"] = ownerId, ["cardInstanceId"] = play.CardInstanceId, ["reason"] = "notInHand" });
                ReportRestOfRoot(group, ownerId);
                return false;
            }
            // One Counter per seat per stack, read from the card itself.
            if (CardCarrier(instance).Tags.Contains("maneuver:counter") && ReactionStack != null)
            {
                if (ReactionStack.CounterSeats.Contains(ownerId))
                {
                    Emit("combatReactionFizzled", new JObject { ["ownerId"] = ownerId, ["cardInstanceId"] = play.CardInstanceId, ["reason"] = "secondCounter" });
                    ReportRestOfRoot(group, ownerId);
                    return false;
                }
                ReactionStack.CounterSeats.Add(ownerId);
            }
            _reactionResume = new List<Core2FightAction>(_queue);
            _queue.Clear();
            _reactionPlaying = true;
            _reactionSourceId = entry.SourceId ?? group.Source?.Id;
            Core2PlayRefusedException refusal = null;
            try { WithIncomingRange(group, () => DoPlayCard(play.Clone())); }
            catch (Core2PlayRefusedException refused) { refusal = refused; }
            finally { _reactionPlaying = false; _reactionSourceId = null; }
            if (refusal != null)
            {
                RestoreReactionQueue();
                Emit("combatReactionFizzled", new JObject { ["ownerId"] = ownerId, ["cardInstanceId"] = play.CardInstanceId, ["reason"] = refusal.Reason ?? "other" });
                ReportRestOfRoot(group, ownerId);
                return false;
            }
            if (Player.Counter != null && Player.Counter.Carrier?.InstanceId == play.CardInstanceId)
            {
                group.ManualCounterSerial = Player.Counter.Serial;
                // A stacked Counter also answers the enemy entries still beneath it.
                foreach (var below in ReactionStack?.Entries ?? new List<Core2ReactionEntry>())
                    if (below.Side == "enemy" && below.TargetPlayerId == ownerId) below.Plan.Group.ManualCounterSerial = Player.Counter.Serial;
            }
            ReportRestOfRoot(group, ownerId);
            return !RestoreReactionQueue();
        }

        /// <summary>JS restoreReactionQueue: the paused queue comes back after a reaction's play has finished.</summary>
        private bool RestoreReactionQueue()
        {
            if (_reactionResume == null || _pendingPlay != null) return false;
            _queue.AddRange(_reactionResume);
            _reactionResume = null;
            return true;
        }

        /// <summary>JS deferRootPlay: a player card whose own root waits on an enemy reaction window stays unresolved until it resumes.</summary>
        private bool DeferRootPlay()
        {
            var pending = PendingReaction;
            if (pending == null || _pendingPlay == null) return false;
            if (PlayerRootSeat(QueuedGroup(pending.GroupKey)) == null) return false;
            _reactionRootPlay = _pendingPlay;
            _pendingPlay = null;
            return true;
        }

        /// <summary>JS restoreRootPlay: once the stack has resolved, the deferred card is the resolving play again.</summary>
        private void RestoreRootPlay()
        {
            if (_reactionRootPlay == null || _pendingPlay != null) return;
            _pendingPlay = _reactionRootPlay;
            _reactionRootPlay = null;
        }

        // ================================================================================================================
        // Engagement scope and the detached copy (JS incomingRangeScope, candidateState)
        // ================================================================================================================

        /// <summary>JS incomingRangeScope: run <paramref name="body"/> in the incoming contact's frozen engagement.</summary>
        private void WithIncomingRange(Core2ActionGroup group, Action body)
        {
            var priorContext = _rangeContext; var priorIncoming = _rangeIncoming;
            if (Range != null && group?.RangePlan != null)
            {
                var key = Core2Range.ActorKey(Player);
                var id = RowFor(group)?.Contacts.FirstOrDefault()?.RangeContactId;
                var root = group.RangePlan;
                var contact = root.Targets.FirstOrDefault(row => row.ContactId == id) ?? root.Targets.FirstOrDefault(row => row.TargetKey == key);
                if (contact != null)
                {
                    _rangeContext = new Core2RangePlan { SourceKey = root.SourceKey, Targets = new List<Core2RangeTarget> { contact } };
                    _rangeIncoming = (root.RootId, contact.ContactId, root.SourceKey, key);
                }
            }
            try { body(); }
            finally { _rangeContext = priorContext; _rangeIncoming = priorIncoming; }
        }

        /// <summary>
        /// JS candidateState(ctx, { detached: true }) as optionsFor uses it: a copy with its own combatants, piles, RNG
        /// counters and an empty event log and queue; nothing it does reaches this fight.
        /// </summary>
        private Core2Fight Candidate()
        {
            var copy = new Core2Fight(Content, Ctx.Clone(), Seed, Rng.Detached())
            {
                Setup = Setup, CombatKey = CombatKey, Range = Range, HandMax = HandMax, KnowledgeSerial = KnowledgeSerial, ReactionsEnabled = ReactionsEnabled, Result = Result,
                _expansionSerial = _expansionSerial, _commands = _commands, _generatedCards = _generatedCards, _drawOrder = _drawOrder,
            };
            var map = new Dictionary<Core2Combatant, Core2Combatant>();
            copy.Player = Player.Clone(); map[Player] = copy.Player;
            foreach (var enemy in Enemies) { var clone = enemy.Clone(); map[enemy] = clone; copy.Enemies.Add(clone); }
            copy.Draw = new List<Core2CardInstance>(Draw); copy.Hand = new List<Core2CardInstance>(Hand);
            copy.Discard = new List<Core2CardInstance>(Discard); copy.Exhaust = new List<Core2CardInstance>(Exhaust);
            foreach (var pair in _triggerState) copy._triggerState[pair.Key] = pair.Value;
            foreach (var id in _phasesFired) copy._phasesFired.Add(id);
            copy._mounts.AddRange(_mounts);
            foreach (var pair in EnemyReactionRolls) copy.EnemyReactionRolls[pair.Key] = pair.Value;
            foreach (var pair in FollowTrace) copy.FollowTrace[pair.Key] = pair.Value.ToList();
            copy.ReactionStack = ReactionStack;
            copy._rangeContext = _rangeContext; copy._rangeIncoming = _rangeIncoming;
            return copy;
        }
    }
}
