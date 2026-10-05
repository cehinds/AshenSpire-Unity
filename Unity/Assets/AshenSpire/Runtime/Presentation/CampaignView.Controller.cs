using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed partial class CampaignView
    {
        private IVisualElementScheduledItem _controllerPoll;
        private readonly bool[] _controllerDown = new bool[16];
        private readonly Dictionary<int, VisualElement> _controllerPressTargets = new Dictionary<int, VisualElement>();
        private string _capturingController;
        private Action _refreshControllerBindings;
        private Label _controllerMessage;
        private bool _controllerSubmitting;
        private string _controllerFocus;

        private void InstallController()
        {
            // The legacy Input Manager already supplies stick/d-pad navigation.
            // Consume its fixed A/B submit events; our persisted bindings dispatch once,
            // on release, to the same controls used by pointer and keyboard input.
            _root.RegisterCallback<NavigationSubmitEvent>(GuardControllerSubmit, TrickleDown.TrickleDown);
            _root.RegisterCallback<NavigationCancelEvent>(GuardControllerCancel, TrickleDown.TrickleDown);
            _root.RegisterCallback<FocusInEvent>(RememberControllerFocus);
            _controllerPoll = _root.schedule.Execute(PollController).Every(16);
        }
        private void UninstallController()
        {
            _root.UnregisterCallback<NavigationSubmitEvent>(GuardControllerSubmit, TrickleDown.TrickleDown);
            _root.UnregisterCallback<NavigationCancelEvent>(GuardControllerCancel, TrickleDown.TrickleDown);
            _root.UnregisterCallback<FocusInEvent>(RememberControllerFocus);
        }
        private static bool ControllerHeld()
        {
            for (var i = 0; i < 16; i++) if (Input.GetKey((KeyCode)((int)KeyCode.JoystickButton0 + i))) return true;
            return false;
        }
        private void GuardControllerSubmit(NavigationSubmitEvent e)
        {
            if (!_controllerSubmitting && (ControllerHeld() || _controllerDown.Any(v => v))) e.StopImmediatePropagation();
        }
        private void GuardControllerCancel(NavigationCancelEvent e)
        {
            if (ControllerHeld() || _controllerDown.Any(v => v)) e.StopImmediatePropagation();
        }
        private void RememberControllerFocus(FocusInEvent e)
        {
            if (e.target is VisualElement control && !string.IsNullOrEmpty(control.name)) _controllerFocus = control.name;
        }
        private VisualElement ControllerFocus()
        {
            var current = _root.focusController?.focusedElement as VisualElement;
            while (current != null && !(current is Button || current is Toggle || current is DropdownField || current is SliderInt || current is TextField)) current = current.parent;
            if ((current is Button || current is Toggle || current is DropdownField || current is SliderInt || current is TextField) && Usable(current)) return current;
            var prior = string.IsNullOrEmpty(_controllerFocus) ? null : _root.Q(_controllerFocus);
            var next = Usable(prior) ? prior : _root.Query<Button>().ToList().FirstOrDefault(Usable);
            next?.Focus(); return next;
        }
        private static bool Usable(VisualElement element)
        {
            if (element == null || !element.enabledInHierarchy || element.panel == null || element.resolvedStyle.visibility == Visibility.Hidden) return false;
            for (var parent = element; parent != null; parent = parent.parent) if (parent.resolvedStyle.display == DisplayStyle.None) return false;
            return element.worldBound.width > 0 && element.worldBound.height > 0;
        }
        private void PollController()
        {
            if (_disposed || _root.panel == null) return;
            for (var i = 0; i < 16; i++)
            {
                var down = Input.GetKey((KeyCode)((int)KeyCode.JoystickButton0 + i));
                if (down == _controllerDown[i]) continue;
                _controllerDown[i] = down;
                if (!UnityEngine.Application.isFocused) { _controllerPressTargets.Remove(i); continue; }
                if (_capturingController != null)
                {
                    if (down) continue;
                    var action = _capturingController; _capturingController = null;
                    var ok = _playerSettings.TryBindController(action, i, out var conflict);
                    _controllerMessage.text = ok ? "Saved " + action + " on button " + i + "." : "Button " + i + " is used by " + conflict + ". Choose another button.";
                    _refreshControllerBindings?.Invoke(); if (ok) Changed(); continue;
                }
                var bindings = _playerSettings?.ControllerBindings ?? OriginalPlayerSettings.DefaultControllerBindings;
                var command = bindings.FirstOrDefault(pair => pair.Value == i).Key;
                if (command == null) continue;
                if (down)
                {
                    var target = ControllerFocus(); _controllerPressTargets[i] = target;
                    if (command == "submit" && target?.name == "native-slot-confirm" && _playerSettings?.HoldToConfirm == true) HoldKey(target, true);
                    continue;
                }
                if (!_controllerPressTargets.TryGetValue(i, out var pressed)) continue;
                _controllerPressTargets.Remove(i);
                // A server render, interruption or navigation while held invalidates input.
                if (!Usable(pressed) || pressed != ControllerFocus()) continue;
                if (command == "submit" && pressed.name == "native-slot-confirm" && _playerSettings?.HoldToConfirm == true) { HoldKey(pressed, false); continue; }
                ControllerAction(command, pressed);
            }
        }
        private static void HoldKey(VisualElement target, bool down)
        {
            var key = new Event { type = down ? EventType.KeyDown : EventType.KeyUp, keyCode = KeyCode.Return };
            if (down) { using (var e = KeyDownEvent.GetPooled(key)) { e.target = target; target.SendEvent(e); } }
            else { using (var e = KeyUpEvent.GetPooled(key)) { e.target = target; target.SendEvent(e); } }
        }
        private void ControllerAction(string command, VisualElement focused)
        {
            if (command == "submit") { SubmitController(focused); return; }
            if (focused is TextField && command != "cancel") return;
            if (command == "previous" || command == "next")
            {
                var controls = _root.Query<VisualElement>().ToList().Where(e => (e is Button || e is Toggle || e is DropdownField || e is SliderInt || e is TextField) && Usable(e)).ToList();
                if (controls.Count == 0) return;
                var index = controls.IndexOf(focused); var delta = command == "next" ? 1 : -1;
                var target = controls[(Math.Max(0, index) + delta + controls.Count) % controls.Count]; target.Focus();
                for (var parent = target.parent; parent != null; parent = parent.parent) if (parent is ScrollView scroll) scroll.ScrollTo(target);
                Report(); return;
            }
            var ids = command == "endTurn" ? new[] { "native-end-turn", "coop-end-turn" }
                : command == "deck" ? new[] { "native-deck", "coop-deck" }
                : new[] { "native-slot-cancel", "native-card-inspection-back", "coop-card-inspection-back", "native-pile-back", "native-inspection-back", "native-deck-back", "native-equipment-back", "native-service-back", "native-level-cancel", "foundation-back", "native-profile-back", "coop-deck-back", "coop-equipment-back", "coop-flasks-back", "coop-mounts-back", "coop-back", "back", "native-slots-back", "extras-back", "native-guide-back", "native-about-back", "native-menu", "coop-menu", "coop-leave" };
            var button = ids.Select(id => _root.Q<Button>(id)).FirstOrDefault(Usable);
            if (button == null && command == "cancel") button = _root.Query<Button>().ToList().FirstOrDefault(b => Usable(b) && b.name?.EndsWith("-back") == true);
            if (button != null) SubmitController(button);
        }
        private void SubmitController(VisualElement target)
        {
            if (!Usable(target)) return;
            _controllerSubmitting = true;
            try { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = target; target.SendEvent(e); } }
            finally { _controllerSubmitting = false; }
        }
        private void ControllerSettings()
        {
            _body.Add(Text("CONTROLLER", "heading"));
            var hint = Text("Stick / D-pad navigates. A confirms, B returns, X opens equipment, Y ends the turn. Bumpers visit every control. Button numbers vary by controller; choose an action to rebind it.", "caption");
            hint.AddToClassList("control-hint"); hint.AddToClassList("controller-hint"); _body.Add(hint);
            var buttons = new Dictionary<string, Button>();
            _controllerMessage = Text("", "caption");
            _refreshControllerBindings = () => { foreach (var pair in buttons) pair.Value.text = pair.Key + " · " + (_capturingController == pair.Key ? "press and release a controller button…" : "button " + _playerSettings.ControllerBindings[pair.Key]); Report(); };
            foreach (var key in OriginalPlayerSettings.DefaultControllerBindings.Keys)
            {
                var action = key;
                buttons[action] = AddButton("controller-" + action, "", () => { _capturingController = action; _controllerMessage.text = "Press a controller button, or cancel below."; _refreshControllerBindings(); });
            }
            AddButton("controller-cancel-binding", "Cancel controller rebinding", () => { _capturingController = null; _controllerMessage.text = "Unchanged."; _refreshControllerBindings(); });
            AddButton("controller-reset", "Reset controller bindings", () => { _capturingController = null; _playerSettings.ResetControllerBindings(); _refreshControllerBindings(); Changed(); });
            _body.Add(_controllerMessage); _refreshControllerBindings();
        }
    }
}
