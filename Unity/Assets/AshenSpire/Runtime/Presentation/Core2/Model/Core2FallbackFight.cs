// Core2FallbackFight.cs — the minimal fight the run host mounts when no combat screen is registered
// (Core2Screens.FightScreenFactory is null). Its only job is to keep a run playable end to end on the public
// Core2Fight commands: play a card, End turn, Auto. It is NOT the combat screen (Presentation/Core2/Combat* is another
// phase's): no intents, no reactions UI (a pending reaction is answered the way Auto answers it), no pacing.
// The two card choices are the player's, as on the combat screen: a card that offers a maneuver choice (JS cardChoicePlan)
// asks it before it plays (Cancel plays nothing), and a pending chosen-card selection (JS pendingCardChoice) waits for
// exactly its count of picks before anything else is accepted. Auto picks deterministically: the first maneuver that
// plays, and the first N candidates (the content-coverage oracle driver's chosen cards).
// Engine-free, so UnityTests/Core2RunView fights through it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Core2;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2FallbackFight
    {
        private readonly Core2RunSession _session;
        public Core2Fight Fight => _session.Fight;
        public string Status { get; private set; } = "";

        public Core2FallbackFight(Core2RunSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); }

        public bool Over => Fight == null || Fight.Result != null;
        /// <summary>The node kind the fight was entered from (JS NODE_TYPES name: Monster, Elite, Boss).</summary>
        public string Title => Core2Text.NodeType((string)_session.FightEncounter?["pool"] == "elite" ? "elite" : (string)_session.FightEncounter?["pool"] == "boss" ? "boss" : "monster").Name;

        public string PlayerLine => Fight?.Player == null ? "" : "HP " + Fight.Player.Hp + "/" + Fight.Player.MaxHp + " · SP " + Fight.Player.Stamina + "/" + Fight.Player.MaxStamina
            + (Fight.Player.MaxMana > 0 ? " · MP " + Fight.Player.Mana + "/" + Fight.Player.MaxMana : "") + (Fight.Player.Block > 0 ? " · Block " + Fight.Player.Block : "") + " · Turn " + Fight.Turn;

        public List<(string Id, string Name, string Line, bool Alive)> Enemies() => Fight == null ? new List<(string, string, string, bool)>()
            : Fight.Enemies.Select(e => (e.Id, Name(e), "HP " + Math.Max(0, e.Hp) + "/" + e.MaxHp + (e.Block > 0 ? " · Block " + e.Block : ""), e.Alive && e.Hp > 0)).ToList();

        private string Name(Core2Combatant e) { try { return _session.Content.Enemy(e.EnemyId).Name; } catch (KeyNotFoundException) { return e.EnemyId; } }

        private string CardName(string cardId) => _session.Content.HasCard(cardId) ? _session.Content.Card(cardId).Name : cardId;

        /// <summary>The hand: each card's name, its price and whether the purse covers it now.</summary>
        public List<(string InstanceId, string Name, string Cost, bool Affordable)> Hand()
        {
            var output = new List<(string, string, string, bool)>();
            if (Fight == null) return output;
            foreach (var card in Fight.Hand)
            {
                var name = CardName(card.CardId);
                Core2Costs costs;
                try { costs = Fight.CardCosts(card.InstanceId); }
                catch (InvalidOperationException) { output.Add((card.InstanceId, name, "—", false)); continue; }
                var cost = costs.Sp.ToString(CultureInfo.InvariantCulture) + " SP" + (costs.Mp > 0 ? " · " + costs.Mp + " MP" : "");
                output.Add((card.InstanceId, name, cost, costs.Sp <= Fight.Player.Stamina && costs.Mp <= Fight.Player.Mana));
            }
            return output;
        }

        // ---- the card choices ---------------------------------------------------------------------------------------------

        /// <summary>The hand card whose maneuver choice is asked (JS openCardChoiceModal), or null.</summary>
        public string ManeuverPick { get; private set; }
        private string _maneuverTarget;

        /// <summary>The maneuvers the asked card offers ("Attack" … "Evade" with their ids), empty when none is asked.</summary>
        public List<(string Id, string Name)> ManeuverOptions()
        {
            if (ManeuverPick == null || Fight == null || !Fight.Hand.Any(c => c.InstanceId == ManeuverPick)) return new List<(string, string)>();
            return (Fight.CardChoiceOptions(ManeuverPick) ?? Array.Empty<string>()).Select(id => (id, char.ToUpperInvariant(id[0]) + id.Substring(1))).ToList();
        }

        /// <summary>The asked card's name (the picker's eyebrow).</summary>
        public string ManeuverCardName => ManeuverPick == null ? "" : CardName(Fight.Hand.FirstOrDefault(c => c.InstanceId == ManeuverPick)?.CardId ?? "");

        /// <summary>A maneuver tile: the asked card plays with it at the target it was aimed at.</summary>
        public string ChooseManeuver(string choice)
        {
            var instanceId = ManeuverPick;
            ManeuverPick = null;
            if (instanceId == null) return null;
            return Play(instanceId, choice, _maneuverTarget);
        }

        /// <summary>Cancel: nothing plays and the card stays in hand.</summary>
        public void CancelManeuver() { ManeuverPick = null; _maneuverTarget = null; Status = ""; }

        private readonly List<string> _picked = new List<string>();

        /// <summary>How many chosen-card selections Auto answered (its first N candidates) in this fight.</summary>
        public int AutoCardChoices { get; private set; }
        /// <summary>How many cards Auto played with a maneuver choice (the first that plays) in this fight.</summary>
        public int AutoManeuvers { get; private set; }

        /// <summary>The pending chosen-card selection, or null.</summary>
        public Core2CardChoice CardChoice => Fight?.PendingCardChoice;
        /// <summary>The picks so far, in pick order.</summary>
        public IReadOnlyList<string> Picked => _picked;
        /// <summary>"Choose N cards" (JS combat.cards.choose.title, as the combat screen reads it).</summary>
        public string CardChoiceTitle => CardChoice == null ? "" : Core2Text.T("combat.cards.choose.title", new { count = CardChoice.Count });
        /// <summary>The resolving card's name (the door's eyebrow).</summary>
        public string CardChoiceCardName => CardChoice == null ? "" : CardName(CardChoice.CardId);
        /// <summary>The candidates: id, name, picked.</summary>
        public List<(string InstanceId, string Name, bool Picked)> CardChoiceOptions() => CardChoice == null ? new List<(string, string, bool)>()
            : CardChoice.Candidates.Select(c => (c.InstanceId, CardName(c.CardId), _picked.Contains(c.InstanceId))).ToList();
        public bool CardChoiceReady => CardChoice != null && _picked.Count == CardChoice.Count;

        /// <summary>A candidate: picked again it is dropped; past the count nothing more is taken.</summary>
        public void ToggleCardChoice(string instanceId)
        {
            if (CardChoice == null || !CardChoice.CandidateIds.Contains(instanceId)) return;
            if (!_picked.Remove(instanceId) && _picked.Count < CardChoice.Count) _picked.Add(instanceId);
        }

        /// <summary>Confirm selection at exactly the count; the card finishes and any reaction it raises is answered.</summary>
        public string ConfirmCardChoice()
        {
            if (CardChoice == null) return null;
            if (!CardChoiceReady) return Status = "Choose exactly " + CardChoice.Count + ".";
            try { Fight.ChooseCards(_picked.ToList()); }
            catch (InvalidOperationException refused) { return Status = refused.Message; }
            _picked.Clear();
            Settle(chooseCards: false);
            Status = "";
            return null;
        }

        /// <summary>
        /// Answer what the fight waits on: a pending reaction as Auto answers it (JS reactionAnswer(chooseReaction); Back when
        /// Auto has none), and — only for Auto — a pending card choice with its first N candidates. A choice left for the
        /// player stops here.
        /// </summary>
        private void Settle(bool chooseCards)
        {
            for (var guard = 0; guard < 50 && Fight != null && Fight.Result == null; guard++)
            {
                if (Fight.HasCardChoice)
                {
                    if (!chooseCards) return;
                    var pending = Fight.PendingCardChoice;
                    _picked.Clear();
                    Fight.ChooseCards(pending.CandidateIds.Take(pending.Count).ToList());
                    AutoCardChoices++;
                }
                else if (Fight.PendingReaction != null) Fight.ChooseReaction(Fight.PendingReaction.Id, Core2AutoCombat.Answer(Fight));
                else return;
            }
        }

        /// <summary>
        /// Play a card at the first living enemy (or with no target when the card takes none). A card that offers a maneuver
        /// choice and is played without one asks it first (ManeuverPick) and plays nothing yet. While a card choice is
        /// pending nothing plays.
        /// </summary>
        public string Play(string instanceId, string choice = null) => Play(instanceId, choice, null);

        private string Play(string instanceId, string choice, string aimedAt)
        {
            if (Over) return null;
            if (Fight.HasCardChoice) return Status = "Choose the cards first.";
            Settle(chooseCards: false);
            if (Fight.HasCardChoice) return Status = "Choose the cards first.";
            var target = aimedAt ?? Fight.Enemies.FirstOrDefault(e => e.Alive && e.Hp > 0)?.Id;
            if (choice == null && Fight.Hand.Any(c => c.InstanceId == instanceId))
            {
                IReadOnlyList<string> options;
                try { options = Fight.CardChoiceOptions(instanceId); }
                catch (InvalidOperationException refused) { return Status = refused.Message; }
                if (options != null) { ManeuverPick = instanceId; _maneuverTarget = target; Status = ""; return null; }
            }
            string error = null;
            foreach (var targetId in new[] { target, null })
            {
                try { Fight.PlayCard(instanceId, targetId, choice); error = null; break; }
                catch (InvalidOperationException refused) { error = refused.Message; }
            }
            Settle(chooseCards: false);
            Status = error ?? "";
            return error;
        }

        public string EndTurn()
        {
            if (Over) return null;
            if (Fight.HasCardChoice) return Status = "Choose the cards first.";
            Settle(chooseCards: false);
            try { Fight.EndTurn(); }
            catch (InvalidOperationException refused) { return Status = refused.Message; }
            Settle(chooseCards: false);
            Status = "";
            return null;
        }

        /// <summary>Auto's play of one card: a maneuver card takes the first maneuver that plays; a card choice its first N candidates.</summary>
        private bool AutoPlay(string instanceId)
        {
            var options = Fight.CardChoiceOptions(instanceId);
            foreach (var choice in options ?? new string[] { null })
            {
                var target = Fight.Enemies.FirstOrDefault(e => e.Alive && e.Hp > 0)?.Id;
                foreach (var targetId in new[] { target, null })
                {
                    try { Fight.PlayCard(instanceId, targetId, choice); }
                    catch (InvalidOperationException) { continue; /* not playable this way */ }
                    if (choice != null) AutoManeuvers++;
                    Settle(chooseCards: true);
                    return true;
                }
            }
            return false;
        }

        /// <summary>One automatic turn: every affordable card in hand order, then End turn (the oracle drivers' play).</summary>
        public void AutoTurn()
        {
            if (Over) return;
            ManeuverPick = null; _maneuverTarget = null;
            Settle(chooseCards: true);
            for (var guard = 0; guard < 20 && !Over; guard++)
            {
                var played = false;
                foreach (var card in Hand().Where(c => c.Affordable))
                    if (AutoPlay(card.InstanceId)) { played = true; break; }
                if (!played) break;
            }
            if (!Over)
            {
                try { Fight.EndTurn(); Status = ""; }
                catch (InvalidOperationException refused) { Status = refused.Message; }
                Settle(chooseCards: true);
            }
        }

        /// <summary>Auto: turns until the fight ends (bounded, as the oracle runs are).</summary>
        public void Auto(int maxTurns = 60)
        {
            for (var turn = 0; turn < maxTurns && !Over; turn++) AutoTurn();
        }
    }
}
