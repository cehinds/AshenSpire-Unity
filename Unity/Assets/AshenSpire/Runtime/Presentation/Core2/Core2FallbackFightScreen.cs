// Core2FallbackFightScreen.cs — the minimal fight the run host mounts while Core2Screens.FightScreenFactory is null.
// Its only job is to keep a Core 2 run playable end to end: the enemies' HP, the hand (play a card at the first living
// enemy), End turn and Auto (Core2FallbackFight), and the two card choices as small doors (a card's maneuver, JS
// cardChoiceModal; a pending chosen-card selection, JS discardChoiceModal), which Auto answers itself. It is
// deliberately not the combat screen: that is the combat phase's (Presentation/Core2/Combat*), registered through the factory, and replaces this one with no change to the host.
using System;
using System.Linq;
using AshenSpire.Domain.Core2;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2FallbackFightScreen : ICore2FightScreen
    {
        private VisualElement _host;
        private Core2FallbackFight _fight;
        private Action _onFightOver;
        private IVisualElementScheduledItem _auto;
        private bool _over;

        public void Mount(VisualElement host, Core2RunSession session, Action onFightOver)
        {
            _host = host;
            _fight = new Core2FallbackFight(session);
            _onFightOver = onFightOver;
            Core2Ui.Style(host);
            // Escape cancels a maneuver choice (JS cardChoiceModal: Escape plays nothing).
            host.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            Draw();
        }

        private void OnKey(KeyDownEvent e)
        {
            if (e.keyCode != UnityEngine.KeyCode.Escape || _fight?.ManeuverPick == null) return;
            _fight.CancelManeuver();
            Draw();
            e.StopPropagation();
        }

        public void Unmount()
        {
            _auto?.Pause();
            _auto = null;
            _host?.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            _host?.Clear();
            _host = null;
        }

        /// <summary>One fight command; a combat opcode the Core 2 fight has not ported stops Auto and is named on screen.</summary>
        private void Act(Action command)
        {
            try { command(); }
            catch (NotSupportedException gap) { _auto?.Pause(); _auto = null; _status = "This fight reached a rule the Core 2 fight cannot run yet: " + gap.Message; }
            After();
        }

        private string _status = "";

        private void After()
        {
            if (_fight.Over && !_over)
            {
                _over = true;
                _auto?.Pause();
                _onFightOver?.Invoke();
                return;
            }
            Draw();
        }

        private void Draw()
        {
            if (_host == null) return;
            _host.Clear();
            var screen = Core2Ui.Div("c2-screen c2-fill");
            var head = Core2Ui.Div("c2-door-head");
            head.Add(Core2Ui.Text(_fight.Title, "c2-title-m"));
            head.Add(Core2Ui.Text(_fight.PlayerLine, "c2-strong"));
            screen.Add(head);
            var enemies = Core2Ui.Div("c2-fight-enemies");
            foreach (var (_, name, line, alive) in _fight.Enemies())
            {
                var card = Core2Ui.Div("c2-fight-enemy" + (alive ? "" : " is-locked"));
                card.Add(Core2Ui.Text(name, "c2-tile-title"));
                card.Add(Core2Ui.Text(line, alive ? "" : "c2-muted"));
                enemies.Add(card);
            }
            screen.Add(enemies);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            Core2Ui.Classes(scroll, "c2-fill");
            var hand = Core2Ui.Div("c2-fight-hand");
            foreach (var (instanceId, name, cost, affordable) in _fight.Hand())
            {
                var id = instanceId;
                hand.Add(Core2Ui.Btn("fallback-card-" + id, name + "\n" + cost, () => Act(() => _fight.Play(id)), "c2-fight-card", affordable && _auto == null));
            }
            scroll.Add(hand);
            screen.Add(scroll);
            screen.Add(Core2Ui.Text(_status.Length > 0 ? _status : _fight.Status, "c2-status"));
            var foot = Core2Ui.Div("c2-event-foot");
            foot.Add(Core2Ui.Btn("fallback-auto", _auto == null ? "Auto" : "Stop", ToggleAuto));
            foot.Add(Core2Ui.Btn("fallback-end-turn", "End Turn", () => Act(() => _fight.EndTurn()), "c2-btn-primary", _auto == null));
            screen.Add(foot);
            _host.Add(screen);
            if (_fight.ManeuverPick != null) { ChoiceDoor(screen); return; }
            if (_fight.CardChoice != null && _auto == null) { CardDoor(screen); return; }
            Core2Ui.FocusFirst(screen, _auto == null ? null : "fallback-auto");
        }

        /// <summary>The maneuver a card asks before it plays (JS cardChoiceModal): a button per maneuver, Cancel plays nothing.</summary>
        private void ChoiceDoor(VisualElement screen)
        {
            var (scrim, _, _, body, foot) = Core2Ui.Door(_fight.ManeuverCardName, Core2Text.T("cardChoice.stanceTitle"));
            foreach (var (id, name) in _fight.ManeuverOptions())
            {
                var choice = id;
                body.Add(Core2Ui.Btn("fallback-maneuver-" + id, name, () => Act(() => _fight.ChooseManeuver(choice))));
            }
            foot.Add(Core2Ui.Btn("fallback-maneuver-cancel", Core2Text.T("common.cancel"), () => { _fight.CancelManeuver(); Draw(); }));
            screen.Add(scrim);
            Core2Ui.InertBehindTopDoor(screen);
            Core2Ui.FocusFirst(scrim);
        }

        /// <summary>The pending chosen-card selection (JS discardChoiceModal): toggle candidates, Confirm selection at exactly N.</summary>
        private void CardDoor(VisualElement screen)
        {
            var pending = _fight.CardChoice;
            var (scrim, _, _, body, foot) = Core2Ui.Door(_fight.CardChoiceCardName, _fight.CardChoiceTitle, _fight.Picked.Count + " / " + pending.Count);
            foreach (var (instanceId, name, picked) in _fight.CardChoiceOptions())
            {
                var id = instanceId;
                var order = _fight.Picked.ToList().IndexOf(id);
                body.Add(Core2Ui.Btn("fallback-choice-" + id, (picked ? (order + 1) + ". " : "") + name, () => { _fight.ToggleCardChoice(id); Draw(); }, picked ? "is-selected" : ""));
            }
            foot.Add(Core2Ui.Btn("fallback-choice-confirm", Core2Text.T("combat.cards.choose.confirm"), () => Act(() => _fight.ConfirmCardChoice()), "c2-btn-primary", _fight.CardChoiceReady));
            screen.Add(scrim);
            Core2Ui.InertBehindTopDoor(screen);
            Core2Ui.FocusFirst(scrim);
        }

        /// <summary>Auto plays one turn every half second until the fight ends or the player stops it.</summary>
        private void ToggleAuto()
        {
            if (_auto != null) { _auto.Pause(); _auto = null; Draw(); return; }
            _auto = _host.schedule.Execute(() => { if (!Core2Screens.IsInterrupted) Act(_fight.AutoTurn); }).Every(500);
            Draw();
        }
    }
}
