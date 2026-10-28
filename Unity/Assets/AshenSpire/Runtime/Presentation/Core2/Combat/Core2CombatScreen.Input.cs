// Core2CombatScreen.Input.cs — keyboard, gamepad and touch for the Core 2 combat screen, and the playback's visible
// effects. Keys are read on the panel root, so they arrive whatever holds focus; GamepadNavigator sends a pad's bound
// action as the key its keyboard binding names (KeyDown then KeyUp to the focused element), so the pad goes through
// the same handler, and its d-pad moves focus between the focusable cards, enemies and buttons (NavigationSubmit on a
// card or enemy taps it).
//   1–9, Q    JS cardHotkeyAction: select (or, while a card is armed, target the Nth enemy)
//   E         End Turn: a tap while nothing is playable; while a card is playable, hold E (a quick press arms it and a
//             second press confirms — the HoldConfirm pattern's tap path, which a pad's instant press also takes)
//   Return    play the armed card (JS combatPlay)
//   Escape    Back on the reaction window; else close the log door / inspector; else drop the selection
//   Log button ArrowUp / ArrowDown / Home / End resize the drawer; a drag snaps to the nearest size
// End Turn on pointer: hold to commit while a card is playable (the fill shows progress, release early aborts), a tap
// arms it ("Tap again to confirm") — Domain/Original HoldConfirmState, as HoldConfirmButton uses it.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    public sealed partial class Core2CombatScreen
    {
        private VisualElement _keyTarget;
        private HoldConfirmState _hold;
        private double _suppressClickUntil;
        private bool _keyHolding;

        private static double HoldMs
        {
            get
            {
                try { return FeelDriver.Profile.Input.HoldConfirmNormalMs; }
                catch (Exception) { return HoldConfirmState.DefaultHoldMs; }
            }
        }

        private void BindInput()
        {
            _hold = new HoldConfirmState(HoldMs);
            _root.RegisterCallback<AttachToPanelEvent>(_ => AttachKeys());
            if (_root.panel != null) AttachKeys();
            // A tap anywhere during playback skips it (JS: pointerdown skips, the release lands the final state).
            _root.RegisterCallback<PointerDownEvent>(_ => { if (_controller.Busy) _controller.Skip(); }, TrickleDown.TrickleDown);
        }

        private void AttachKeys()
        {
            var target = _root.panel?.visualTree;
            if (target == null || target == _keyTarget) return;
            DetachKeys();
            _keyTarget = target;
            target.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            target.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
        }

        private void DetachKeys()
        {
            if (_keyTarget == null) return;
            _keyTarget.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _keyTarget.UnregisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            _keyTarget = null;
        }

        private void UnbindInput() => DetachKeys();

        private static string KeyName(KeyCode code)
        {
            if (code >= KeyCode.Alpha1 && code <= KeyCode.Alpha9) return ((char)('1' + (code - KeyCode.Alpha1))).ToString();
            if (code >= KeyCode.Keypad1 && code <= KeyCode.Keypad9) return ((char)('1' + (code - KeyCode.Keypad1))).ToString();
            switch (code)
            {
                case KeyCode.Q: return "q";
                case KeyCode.E: return "e";
                case KeyCode.Escape: return "Escape";
                case KeyCode.Return: case KeyCode.KeypadEnter: return "Enter";
                default: return null;
            }
        }

        /// <summary>
        /// A focusable tile that is not a Button (hand card, enemy, reaction option) activates on NavigationSubmit and on
        /// Return/Enter/Space: the GamepadNavigator sends a focused element Return, and its Submit fallback only reaches Buttons.
        /// </summary>
        private static void Activatable(VisualElement node, Action activate)
        {
            node.RegisterCallback<NavigationSubmitEvent>(e => { activate(); e.StopPropagation(); });
            node.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter && e.keyCode != KeyCode.Space) return;
                activate(); e.StopPropagation();
            });
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            // The handler sits on the panel root, outside the subtree the interruption cover disables: no command behind it.
            if (_root?.panel == null || _controller == null || Core2Screens.IsInterrupted) return;
            var key = KeyName(e.keyCode);
            if (key == null) return;
            var fight = _controller.Fight;
            if (key == "Escape")
            {
                if (fight.PendingReaction != null && !_controller.Busy) _controller.AnswerReaction(null);
                else if (_controller.Log.FullOpen) { _controller.Log.CloseFull(); PaintOverlay(); }
                else if (_trayOpen) { _trayOpen = false; PaintOverlay(); }
                else _controller.Cancel();
                e.StopPropagation();
                return;
            }
            // While a door is open (an offer, the full log, the inspector) only its own controls take input; Escape above closes it.
            if (fight.PendingReaction != null || _controller.Log.FullOpen || _controller.Inspecting != null) return;
            if (key == "e")
            {
                e.StopPropagation();
                if (_keyHolding) return; // key repeat
                if (!_controller.CanAct) return;
                if (!_controller.EndTurnNeedsHold) { CommitEndTurn(); return; }
                var now = NowMs;
                if (_hold.IsArmed(now)) { if (_hold.Tap(now)) CommitEndTurn(); return; }
                _keyHolding = true;
                _hold.Begin(now);
                return;
            }
            if (key == "Enter")
            {
                var selected = _controller.Selected;
                if (selected == null) return; // a focused button or card takes Enter itself
                // A focused control (an enemy to target, a card, a button) takes Confirm itself: the default target below is
                // only for Enter with nothing focused, so the pad's chosen enemy is the one attacked.
                if (e.target is VisualElement focused && focused != _root && focused.focusable && focused.panel == _root.panel
                    && _root.Contains(focused)) return;
                var target = _controller.UnambiguousTarget(selected, out var ambiguous);
                if (!ambiguous) _controller.PlayCard(selected, target);
                else _controller.TapEnemy(fight.Enemies.First(x => x.Alive).Id);
                e.StopPropagation();
                return;
            }
            if (_controller.Key(key)) e.StopPropagation();
        }

        private void OnKeyUp(KeyUpEvent e)
        {
            if (KeyName(e.keyCode) != "e" || !_keyHolding) return;
            // A hold released behind the interruption cover ends without committing: nothing resolves behind it.
            if (Core2Screens.IsInterrupted) { _keyHolding = false; _hold.Reset(); PaintEndTurnState(); return; }
            _keyHolding = false;
            var now = NowMs;
            if (_hold.Release(now)) { CommitEndTurn(); return; }
            // Released early: the tap path arms it, and the next press confirms.
            _hold.Tap(now);
            PaintEndTurnState();
            e.StopPropagation();
        }

        private void BindEndTurn(Button button)
        {
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !_controller.CanAct || !_controller.EndTurnNeedsHold) return;
                _hold.Begin(NowMs);
                PaintEndTurnState();
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(_ =>
            {
                if (_hold.Release(NowMs)) CommitEndTurn();
                else PaintEndTurnState();
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerLeaveEvent>(_ => { if (_hold.Holding && !_keyHolding) { _hold.Cancel(); PaintEndTurnState(); } });
            button.RegisterCallback<PointerCancelEvent>(_ => { _hold.Cancel(); PaintEndTurnState(); });
            button.clicked += () =>
            {
                var now = NowMs;
                if (now < _suppressClickUntil || !_controller.CanAct) return;
                if (!_controller.EndTurnNeedsHold) { CommitEndTurn(); return; }
                if (_hold.Tap(now)) CommitEndTurn();
                else PaintEndTurnState();
            };
        }

        private void CommitEndTurn()
        {
            _hold.Reset();
            _keyHolding = false;
            _suppressClickUntil = NowMs + 400;
            _controller.EndTurn();
        }

        private void TickEndTurnHold()
        {
            if (_hold == null) return;
            var now = NowMs;
            if (_hold.Tick(now)) { CommitEndTurn(); return; }
            if (_hold.Holding || _hold.IsArmed(now)) PaintEndTurnState();
            else if (_holdFill != null && _holdFill.style.width.value.value > 0) PaintEndTurnState();
        }

        private void PaintEndTurnState()
        {
            if (_endTurn == null || _holdFill == null) return;
            var now = NowMs;
            _holdFill.style.width = Length.Percent((float)(_hold.Progress(now) * 88));
            var hint = _endTurn.Q<Label>(className: "c2c-end-hint");
            if (hint != null && _model != null)
                hint.text = _hold.IsArmed(now) ? Core2CombatText.T("js.holdConfirm.armed") : _model.Footer.EndTurnNeedsHold ? _model.Footer.EndTurnHint : "";
        }

        // ---- the log button: tap toggles, drag resizes, arrow keys step (JS combatTools) ---------------------------------

        private Dictionary<string, double> LogHeights()
        {
            var card = _hand.Children().FirstOrDefault()?.resolvedStyle.height ?? 0;
            if (!(card > 0)) card = 234;
            var height = _root.resolvedStyle.height;
            var dockTop = _tools.layout.yMin;
            var menuBottom = _top.layout.yMax;
            return _controller.Log.Heights(card, height, dockTop, menuBottom);
        }

        private void BindLogButton(Button button)
        {
            var dragging = false; var startY = 0f; var startHeight = 0.0; var moved = false;
            button.RegisterCallback<KeyDownEvent>(e =>
            {
                var key = e.keyCode == KeyCode.UpArrow ? "ArrowUp" : e.keyCode == KeyCode.DownArrow ? "ArrowDown" : e.keyCode == KeyCode.Home ? "Home" : e.keyCode == KeyCode.End ? "End" : null;
                if (key != null && _controller.Log.Key(key)) { PaintOverlay(); e.StopPropagation(); }
            });
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragging = true; moved = false; startY = e.position.y;
                var heights = LogHeights();
                startHeight = _controller.Log.Open ? heights[_controller.Log.Size] : heights["Small"];
                button.CapturePointer(e.pointerId);
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                var delta = startY - e.position.y;
                if (Math.Abs(delta) < 6 && !moved) return;
                moved = true;
                var heights = LogHeights();
                if (!_controller.Log.Open) { _controller.Log.Toggle(); PaintOverlay(); }
                var height = _controller.Log.DragHeight(heights, startHeight, delta, _root.resolvedStyle.height, _tools.layout.yMax);
                if (_logDrawer != null) _logDrawer.style.height = (float)height;
            });
            button.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                if (button.HasPointerCapture(e.pointerId)) button.ReleasePointer(e.pointerId);
                if (!moved) return;
                var heights = LogHeights();
                _controller.Log.EndDrag(heights, _logDrawer != null ? _logDrawer.resolvedStyle.height : startHeight);
                _suppressLogClick = true;
                PaintOverlay();
            });
            button.clicked += () =>
            {
                if (_suppressLogClick) { _suppressLogClick = false; return; }
                _controller.Log.Toggle();
                PaintOverlay();
            };
        }

        private bool _suppressLogClick;

        // ---- playback effects: action words, lunges, Hit / Missed, floaters, the turn banner ------------------------------

        private readonly Dictionary<string, int> _floatSlots = new Dictionary<string, int>(StringComparer.Ordinal);

        private void OnStage(Core2TimelineStep step)
        {
            if (_root == null || _controller.Timeline == null) return;
            var beat = _controller.Timeline.Beats[step.Beat];
            switch (step.Stage)
            {
                case "intent":
                    if (beat.Banner != null) Banner(beat.Banner);
                    _floatSlots.Clear();
                    break;
                case "action":
                    if (beat.ActionWord != null && beat.ActorId != null) Callout(beat.ActorId, beat.ActionWord, "word");
                    break;
                case "sprite":
                    if (beat.ActorId != null && _fighters.TryGetValue(beat.ActorId, out var actor)) actor.AddToClassList("c2c-lunge");
                    break;
                case "reaction":
                    foreach (var target in step.Events.Where(e => e.Type == "damageDealt").Select(e => (string)e.Data["targetId"]).Distinct())
                        if (target != null && _fighters.TryGetValue(target, out var hit)) { hit.AddToClassList("c2c-flinch"); hit.schedule.Execute(() => hit.RemoveFromClassList("c2c-flinch")).StartingIn(180); }
                    break;
                case "resolution":
                    if (beat.ActorId != null && _fighters.TryGetValue(beat.ActorId, out var done)) done.RemoveFromClassList("c2c-lunge");
                    break;
            }
            foreach (var callout in step.Callouts) Callout(callout.TargetId, callout.Text, callout.Tone);
            foreach (var floater in step.Floaters) Floater(floater);
        }

        private Vector2 Anchor(string fighterId)
        {
            if (fighterId == null || !_fighters.TryGetValue(fighterId, out var node) || node.panel == null) return new Vector2(_fx.resolvedStyle.width / 2, _fx.resolvedStyle.height / 3);
            var figure = node.Q(className: "c2c-figure") ?? node;
            var bound = figure.worldBound;
            return _fx.WorldToLocal(new Vector2(bound.center.x, bound.yMin + bound.height * 0.3f));
        }

        private int NextSlot(string id)
        {
            var key = id ?? "";
            _floatSlots.TryGetValue(key, out var slot);
            _floatSlots[key] = slot + 1;
            return slot;
        }

        private void Floater(Core2Floater floater)
        {
            var label = Text(floater.Text, "c2c-float");
            foreach (var part in floater.Class.Split(' ')) label.AddToClassList("c2c-float-" + part);
            Place(label, floater.TargetId);
        }

        private void Callout(string targetId, string text, string tone)
        {
            var label = Text(text, "c2c-callout", "c2c-tone-" + tone);
            Place(label, targetId);
        }

        private void Place(Label label, string targetId)
        {
            var at = Anchor(targetId);
            var slot = NextSlot(targetId);
            label.style.left = at.x;
            label.style.top = at.y - slot * 18;
            label.style.translate = new Translate(Length.Percent(-50), 0);
            _fx.Add(label);
            var reduced = _controller.Pace().Scale == 0;
            label.schedule.Execute(() => label.AddToClassList("c2c-rise")).StartingIn(reduced ? 0 : 30);
            label.schedule.Execute(() => label.RemoveFromHierarchy()).StartingIn(reduced ? 600 : 1000);
        }

        private void Banner(string text)
        {
            var banner = Text(text, "c2c-banner");
            _fx.Add(banner);
            var ms = Math.Max(300, _controller.Pace().BannerMs);
            banner.schedule.Execute(() => banner.RemoveFromHierarchy()).StartingIn((long)ms);
        }
    }
}
