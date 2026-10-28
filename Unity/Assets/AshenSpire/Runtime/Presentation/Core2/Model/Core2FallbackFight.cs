// Core2FallbackFight.cs — the minimal fight the run host mounts when no combat screen is registered
// (Core2Screens.FightScreenFactory is null). Its only job is to keep a run playable end to end on the public
// Core2Fight commands: play a card, End turn, Auto. It is NOT the combat screen (Presentation/Core2/Combat* is another
// phase's): no intents, no reactions UI (a pending reaction is answered the way Auto answers it), no pacing.
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

        /// <summary>The hand: each card's name, its price and whether the purse covers it now.</summary>
        public List<(string InstanceId, string Name, string Cost, bool Affordable)> Hand()
        {
            var output = new List<(string, string, string, bool)>();
            if (Fight == null) return output;
            foreach (var card in Fight.Hand)
            {
                var name = _session.Content.HasCard(card.CardId) ? _session.Content.Card(card.CardId).Name : card.CardId;
                Core2Costs costs;
                try { costs = Fight.CardCosts(card.InstanceId); }
                catch (InvalidOperationException) { output.Add((card.InstanceId, name, "—", false)); continue; }
                var cost = costs.Sp.ToString(CultureInfo.InvariantCulture) + " SP" + (costs.Mp > 0 ? " · " + costs.Mp + " MP" : "");
                output.Add((card.InstanceId, name, cost, costs.Sp <= Fight.Player.Stamina && costs.Mp <= Fight.Player.Mana));
            }
            return output;
        }

        /// <summary>A pending reaction is answered as Auto answers it (JS reactionAnswer(chooseReaction)); Back when Auto has none.</summary>
        private void AnswerReactions()
        {
            for (var guard = 0; guard < 50 && Fight != null && Fight.Result == null && Fight.PendingReaction != null; guard++)
                Fight.ChooseReaction(Fight.PendingReaction.Id, Core2AutoCombat.Answer(Fight));
        }

        /// <summary>Play a card at the first living enemy (or with no target when the card takes none).</summary>
        public string Play(string instanceId)
        {
            if (Over) return null;
            AnswerReactions();
            var target = Fight.Enemies.FirstOrDefault(e => e.Alive && e.Hp > 0)?.Id;
            string error = null;
            foreach (var targetId in new[] { target, null })
            {
                try { Fight.PlayCard(instanceId, targetId); error = null; break; }
                catch (InvalidOperationException refused) { error = refused.Message; }
            }
            AnswerReactions();
            Status = error ?? "";
            return error;
        }

        public string EndTurn()
        {
            if (Over) return null;
            AnswerReactions();
            try { Fight.EndTurn(); }
            catch (InvalidOperationException refused) { return Status = refused.Message; }
            AnswerReactions();
            Status = "";
            return null;
        }

        /// <summary>One automatic turn: every affordable card in hand order, then End turn (the oracle drivers' play).</summary>
        public void AutoTurn()
        {
            if (Over) return;
            AnswerReactions();
            for (var guard = 0; guard < 20 && !Over; guard++)
            {
                var played = false;
                foreach (var card in Hand().Where(c => c.Affordable))
                    if (Play(card.InstanceId) == null) { played = true; break; }
                if (!played) break;
            }
            if (!Over) EndTurn();
        }

        /// <summary>Auto: turns until the fight ends (bounded, as the oracle runs are).</summary>
        public void Auto(int maxTurns = 60)
        {
            for (var turn = 0; turn < maxTurns && !Over; turn++) AutoTurn();
        }
    }
}
