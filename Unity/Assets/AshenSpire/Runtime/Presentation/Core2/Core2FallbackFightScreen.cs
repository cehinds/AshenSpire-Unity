// Core2FallbackFightScreen.cs — the minimal fight the run host mounts while Core2Screens.FightScreenFactory is null.
// Its only job is to keep a Core 2 run playable end to end: the enemies' HP, the hand (play a card at the first living
// enemy), End turn and Auto (Core2FallbackFight). It is deliberately not the combat screen: that is the combat phase's
// (Presentation/Core2/Combat*), registered through the factory, and replaces this one with no change to the host.
using System;
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
            Draw();
        }

        public void Unmount()
        {
            _auto?.Pause();
            _auto = null;
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
            Core2Ui.FocusFirst(screen, _auto == null ? null : "fallback-auto");
        }

        /// <summary>Auto plays one turn every half second until the fight ends or the player stops it.</summary>
        private void ToggleAuto()
        {
            if (_auto != null) { _auto.Pause(); _auto = null; Draw(); return; }
            _auto = _host.schedule.Execute(() => Act(_fight.AutoTurn)).Every(500);
            Draw();
        }
    }
}
