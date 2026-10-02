// GamepadNavigator.cs — runs gamepad signals against the UI Toolkit tree (US-15.3).
// INPUT: GamepadDriver (MonoBehaviour) reads the legacy Input Manager each frame and calls Tick.
// RULES (Domain/Original): GamepadReader turns frames into presses/directions/pans;
// OriginalGamepadNavigation.Plan/PlanNavigate turns those into ordered steps and picks focus
// targets. This file only executes steps and reports whether one was handled.
// ONE DISPATCHER: a bound action is sent as the KEY its keyboard binding names (KeyDown + KeyUp to
// the focused element), so combat (OriginalRunPanel.BindCombatKeys) and the map
// (OriginalMapBoard.Key) see exactly what a keyboard press would give them, including the rule
// that inspections block combat actions and that Enter on a focused button activates the button.
// ASSUMES synthesized events dispatch synchronously when sent outside event processing (they do in
// the UI Toolkit versions read for this); "handled" means a handler stopped propagation.
// DEVICE: the root gets class input-gamepad while the pad was the last input (keyboard, mouse or
// touch clears it); LastInputWasGamepad is read by the combat controls screen.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class GamepadNavigator
    {
        public const string GamepadClass = "input-gamepad";
        private readonly VisualElement _root;
        private readonly Func<OriginalPlayerSettings> _settings;
        private readonly Action _report;
        private readonly GamepadReader _reader = new GamepadReader(GamepadLayout.XInput);
        private string _lastFocusName;
        private bool _navigateKeyHandled;
        private readonly GamepadCaptureFilter _captured = new GamepadCaptureFilter();

        /// <summary>True when the most recent input came from a gamepad (shared by every view in the process).</summary>
        public static bool LastInputWasGamepad { get; private set; }
        /// <summary>When set, button presses go here first (Settings rebinding). Return true to consume the press.</summary>
        public Func<string, bool> Capture { get; set; }
        public GamepadLayout Layout { get => _reader.Layout; set => _reader.Layout = value ?? GamepadLayout.XInput; }
        public GamepadTuning Tuning => _reader.Tuning;

        public GamepadNavigator(VisualElement root, Func<OriginalPlayerSettings> settings, Action report = null)
        { _root = root; _settings = settings; _report = report; }

        /// <summary>Forget held buttons (focus loss, view disposed): a held button fires again only after release.</summary>
        public void Reset() { _reader.Reset(); _captured.Reset(); }

        public void Tick(GamepadFrame frame, double time)
        {
            if (_root?.panel == null) { Reset(); return; }
            var signals = _reader.Step(frame, time);
            if (_reader.Active || signals.Count > 0) MarkDevice(true);
            foreach (var signal in signals)
            {
                try { Handle(signal); }
                catch (Exception error) { Debug.LogException(error); }
            }
            _captured.EndStep();
        }

        /// <summary>Keyboard, mouse or touch input happened: control hints follow the keyboard again.</summary>
        public void NoteKeyboardOrPointer() => MarkDevice(false);

        private void MarkDevice(bool pad)
        {
            if (LastInputWasGamepad == pad && _root.ClassListContains(GamepadClass) == pad) return;
            LastInputWasGamepad = pad;
            _root.EnableInClassList(GamepadClass, pad);
        }

        private IReadOnlyDictionary<string, string> PadBindings => _settings?.Invoke()?.GamepadBindings;
        private IReadOnlyDictionary<string, string> KeyBindings => _settings?.Invoke()?.KeyBindings;

        private void Handle(PadSignal signal)
        {
            switch (signal.Kind)
            {
                case PadSignalKind.Press:
                    var capture = Capture;
                    if (capture != null && capture(signal.Button)) { _captured.Captured(signal.Button); return; }
                    Run(OriginalGamepadNavigation.Plan(PadBindings, KeyBindings, signal.Button));
                    break;
                case PadSignalKind.Navigate:
                    if (Capture != null) return; // Rebinding: directions neither move focus nor act.
                    if (_captured.Suppress(signal)) return; // The d-pad press a capture just consumed, and its repeats.
                    if (!signal.Repeat) _navigateKeyHandled = false;
                    var steps = OriginalGamepadNavigation.PlanNavigate(PadBindings, KeyBindings, signal.Direction, signal.Button, signal.Repeat, _navigateKeyHandled);
                    var handledBy = Run(steps);
                    if (!signal.Repeat && handledBy.HasValue && handledBy.Value.Kind == PadStepKind.Key) _navigateKeyHandled = true;
                    break;
                case PadSignalKind.Pan:
                    Pan(signal.PanX, signal.PanY);
                    break;
            }
        }

        private PadStep? Run(IReadOnlyList<PadStep> steps)
        {
            foreach (var step in steps)
                if (Execute(step)) { _report?.Invoke(); return step; }
            return null;
        }

        private bool Execute(PadStep step)
        {
            switch (step.Kind)
            {
                case PadStepKind.Key: return SendKey(step.Key, step.Action);
                case PadStepKind.Submit: return Submit();
                case PadStepKind.Cancel: return CancelPopup();
                case PadStepKind.Back: return Back();
                case PadStepKind.Menu: return FocusMenu();
                case PadStepKind.Move: return Move(step.Direction);
                default: return false;
            }
        }

        private VisualElement Focused => _root.panel?.focusController?.focusedElement as VisualElement;
        private static bool IsControl(VisualElement element) => element is Button || element is Toggle || element is DropdownField || element is SliderInt || element is TextField || IsInPopup(element);
        private static bool IsInPopup(VisualElement element)
        {
            for (var e = element; e != null; e = e.hierarchy.parent) if (e.ClassListContains("unity-base-dropdown")) return true;
            return false;
        }

        private bool SendKey(string key, string action)
        {
            if (string.IsNullOrEmpty(key) || !Enum.TryParse(key, true, out KeyCode code) || code == KeyCode.None) return false;
            var focused = Focused;
            if (OriginalGamepadNavigation.IsActivationKey(key) && IsControl(focused)) return false;
            var target = focused != null && focused.panel == _root.panel ? focused : _root;
            // Map actions go to the shown map's viewport (OriginalMapBoard.Key only handles them there); the
            // viewport is a container, never a focus target, so a focused node would otherwise swallow them.
            if (OriginalGamepadNavigation.IsMapAction(action)) { var viewport = MapKeyTarget(); if (viewport != null) target = viewport; }
            bool handled;
            using (var down = KeyDownEvent.GetPooled('\0', code, EventModifiers.None))
            {
                target.SendEvent(down);
                handled = down.isPropagationStopped || down.isImmediatePropagationStopped;
            }
            using (var up = KeyUpEvent.GetPooled('\0', code, EventModifiers.None)) target.SendEvent(up);
            return handled;
        }

        private bool Submit()
        {
            var focused = Focused;
            if (focused == null || !IsControl(focused) || !focused.enabledInHierarchy) return FocusEntry();
            Activate(focused);
            return true;
        }
        private static void Activate(VisualElement element)
        {
            if (element is Toggle toggle) { toggle.value = !toggle.value; return; }
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = element; element.SendEvent(submit); }
        }

        private bool CancelPopup()
        {
            var focused = Focused;
            if (focused == null || !IsInPopup(focused)) return false;
            using (var cancel = NavigationCancelEvent.GetPooled()) { cancel.target = focused; focused.SendEvent(cancel); }
            return true;
        }

        private bool Back()
        {
            var back = Candidates().FirstOrDefault(e => e is Button && OriginalGamepadNavigation.IsBackControl(e.name));
            if (back == null) return false;
            Activate(back);
            return true;
        }

        private bool FocusMenu()
        {
            var candidates = Candidates();
            foreach (var name in OriginalGamepadNavigation.MenuControls)
            {
                var menu = candidates.FirstOrDefault(e => e.name == name);
                if (menu != null) { FocusOn(menu); return true; }
            }
            return false;
        }

        private bool Move(PadDirection direction)
        {
            var focused = Focused;
            if (focused != null && IsInPopup(focused))
            {
                var vector = direction == PadDirection.Up ? Vector2.up : direction == PadDirection.Down ? Vector2.down : direction == PadDirection.Left ? Vector2.left : Vector2.right;
                using (var move = NavigationMoveEvent.GetPooled(vector)) { move.target = focused; focused.SendEvent(move); }
                return true;
            }
            if (direction == PadDirection.Left || direction == PadDirection.Right)
            {
                var delta = direction == PadDirection.Right ? 1 : -1;
                if (focused is SliderInt slider && slider.enabledInHierarchy)
                {
                    var step = Math.Max(1, (int)Math.Round((slider.highValue - slider.lowValue) / 20.0));
                    slider.value = Mathf.Clamp(slider.value + delta * step, Math.Min(slider.lowValue, slider.highValue), Math.Max(slider.lowValue, slider.highValue));
                    return true;
                }
                if (focused is DropdownField choice && choice.enabledInHierarchy && choice.choices != null && choice.choices.Count > 0)
                {
                    choice.index = Mathf.Clamp(choice.index + delta, 0, choice.choices.Count - 1);
                    return true;
                }
            }
            var candidates = Candidates();
            if (candidates.Count == 0) return false;
            var rects = candidates.Select(Rect).ToList();
            var origin = focused == null ? -1 : candidates.IndexOf(focused);
            if (origin < 0 && !string.IsNullOrEmpty(_lastFocusName)) origin = candidates.FindIndex(e => e.name == _lastFocusName);
            if (origin < 0) return FocusEntry(candidates, rects);
            var next = OriginalGamepadNavigation.Next(rects[origin], rects, direction, origin);
            FocusOn(candidates[next >= 0 ? next : origin]);
            return true;
        }

        private bool FocusEntry() { var candidates = Candidates(); return FocusEntry(candidates, candidates.Select(Rect).ToList()); }
        private bool FocusEntry(List<VisualElement> candidates, List<PadRect> rects)
        {
            var view = _root.worldBound;
            var index = OriginalGamepadNavigation.Entry(rects, new PadRect(view.x, view.y, view.width, view.height));
            if (index < 0) return false;
            FocusOn(candidates[index]);
            return true;
        }

        private void FocusOn(VisualElement element)
        {
            element.Focus();
            element.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(element);
            if (!string.IsNullOrEmpty(element.name)) _lastFocusName = element.name;
        }

        private static PadRect Rect(VisualElement element) { var b = element.worldBound; return new PadRect(b.x, b.y, b.width, b.height); }

        /// <summary>Focusable, enabled, displayed leaf controls under the root, in tree order. A focusable
        /// element that contains other focusable elements (the combat surface, the map viewport, settings
        /// sections) is a container, not a target.</summary>
        private List<VisualElement> Candidates()
        {
            var all = new List<VisualElement>();
            _root.Query<VisualElement>().ForEach(e => { if (Usable(e)) all.Add(e); });
            var set = new HashSet<VisualElement>(all);
            var containers = new HashSet<VisualElement>();
            foreach (var element in all)
                for (var p = element.hierarchy.parent; p != null && p != _root.hierarchy.parent; p = p.hierarchy.parent)
                    if (set.Contains(p)) containers.Add(p);
            return all.Where(e => !containers.Contains(e)).ToList();
        }
        private static bool Usable(VisualElement element)
        {
            if (!element.focusable || !element.canGrabFocus || element.tabIndex < 0 || !element.enabledInHierarchy) return false;
            if (!Rect(element).Valid) return false;
            for (var e = element; e != null; e = e.hierarchy.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden || !e.visible) return false;
            return true;
        }

        private VisualElement MapKeyTarget()
        {
            VisualElement viewport = null;
            _root.Query<OriginalMapBoard>().ForEach(m => { if (viewport == null && Rect(m).Valid && m.enabledInHierarchy) viewport = m.KeyTarget; });
            return viewport;
        }

        private void Pan(double x, double y)
        {
            // Map first: the right stick drags the visible route map (stick up shows higher floors).
            OriginalMapBoard map = null;
            _root.Query<OriginalMapBoard>().ForEach(m => { if (map == null && Rect(m).Valid && m.enabledInHierarchy) map = m; });
            if (map != null && map.PanBy(-y)) return;
            var focused = Focused;
            var scroll = focused?.GetFirstAncestorOfType<ScrollView>();
            if (scroll == null) _root.Query<ScrollView>().ForEach(s => { if (scroll == null && Rect(s).Valid) scroll = s; });
            if (scroll == null) return;
            scroll.scrollOffset = new Vector2(scroll.scrollOffset.x + (float)x, scroll.scrollOffset.y - (float)y);
        }
    }
}
