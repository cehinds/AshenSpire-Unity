// OriginalGamepad.cs — gamepad button ids, default bindings and a pure press resolver (US-15.3).
// Bindings live in OriginalPlayerSettings.GamepadBindings (action → button id), next to the
// keyboard KeyBindings and with the same conflict rule (TryBindGamepad refuses a held button).
// The resolver never executes anything: Action(...) names the bound action for a press in a
// context, and KeyName(...) gives the keyboard key that action is bound to, so a presentation
// hook can feed a pad press into the existing key-action path (the HTML input.js approach:
// "a pad press dispatches that same key"). `cancel` resolves to Escape; `menu` has no key.
// READING A PAD: the project uses the legacy Input Manager (activeInputHandler 0, no Input System
// package). Presentation/GamepadDriver.cs polls joystick buttons and axes, OriginalGamepadInput.cs
// turns a frame into presses/directions per platform layout, OriginalGamepadNavigation.cs plans the
// steps, and Presentation/GamepadNavigator.cs runs them. docs/Unity-Settings.md has the mapping.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalGamepad
    {
        /// <summary>Button ids, in W3C standard-gamepad index order (south = 0 … dpadRight = 15).</summary>
        public static readonly string[] Buttons =
        {
            "south", "east", "west", "north", "leftShoulder", "rightShoulder", "leftTrigger", "rightTrigger",
            "select", "start", "leftStick", "rightStick", "dpadUp", "dpadDown", "dpadLeft", "dpadRight",
        };
        /// <summary>Pad-only actions: they have no rebindable keyboard key (cancel = Escape, menu = the run menu).</summary>
        public static readonly string[] PadOnlyActions = { "cancel", "menu" };

        /// <summary>Defaults follow the HTML input.js ACTIONS defBtn where Unity has the same action:
        /// confirm/play = south (0), cancel = east (1), end turn = west (2), deck = north (3), menu = start (9),
        /// flask 1/2/3 = triggers (6, 7) and left stick (10). The HTML shoulders cycle tabs; Unity uses them
        /// for the enemy target, and the d-pad up/down scroll the map. Other actions start unbound.</summary>
        public static readonly IReadOnlyDictionary<string, string> DefaultBindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combatPlay"] = "south", ["cancel"] = "east", ["endTurn"] = "west", ["combatDeck"] = "north", ["menu"] = "start",
            ["flask1"] = "leftTrigger", ["flask2"] = "rightTrigger", ["flask3"] = "leftStick",
            ["targetPrevious"] = "leftShoulder", ["targetNext"] = "rightShoulder",
            ["mapScrollUp"] = "dpadUp", ["mapScrollDown"] = "dpadDown",
        };

        /// <summary>The canonical button id (case-insensitive, trimmed), or null when it is not a gamepad button.</summary>
        public static string Normalize(string button)
        {
            if (string.IsNullOrWhiteSpace(button)) return null;
            var text = button.Trim();
            return Buttons.FirstOrDefault(b => string.Equals(b, text, StringComparison.OrdinalIgnoreCase));
        }
        /// <summary>Standard-gamepad button index (0..15), or -1.</summary>
        public static int StandardIndex(string button) { var id = Normalize(button); return id == null ? -1 : Array.IndexOf(Buttons, id); }
        /// <summary>The button id for a standard-gamepad index, or null.</summary>
        public static string FromStandardIndex(int index) => index >= 0 && index < Buttons.Length ? Buttons[index] : null;

        /// <summary>
        /// The action bound to <paramref name="button"/> among <paramref name="contextActions"/> (e.g. the map or
        /// combat action list; include "cancel"/"menu" where they apply), or null. Null bindings mean defaults.
        /// When a hand-edited save binds one button twice, the first action in context order wins.
        /// </summary>
        public static string Action(IReadOnlyDictionary<string, string> bindings, string button, IEnumerable<string> contextActions)
        {
            var id = Normalize(button);
            if (id == null || contextActions == null) return null;
            var source = bindings ?? DefaultBindings;
            foreach (var action in contextActions)
                if (action != null && source.TryGetValue(action, out var bound) && Normalize(bound) == id) return action;
            return null;
        }
        /// <summary>The keyboard key name (Unity KeyCode name) a resolved action dispatches as: "Escape" for
        /// cancel, null for menu (no key) or an action with no keyboard binding.</summary>
        public static string KeyName(string action, IReadOnlyDictionary<string, string> keyBindings)
        {
            if (action == "cancel") return "Escape";
            if (action == null || action == "menu") return null;
            var source = keyBindings ?? OriginalPlayerSettings.DefaultKeyBindings;
            return source.TryGetValue(action, out var key) ? key : null;
        }
        public static string Label(string button)
        {
            switch (Normalize(button))
            {
                case "south": return "A / Cross";
                case "east": return "B / Circle";
                case "west": return "X / Square";
                case "north": return "Y / Triangle";
                case "leftShoulder": return "LB / L1";
                case "rightShoulder": return "RB / R1";
                case "leftTrigger": return "LT / L2";
                case "rightTrigger": return "RT / R2";
                case "select": return "Select / Share";
                case "start": return "Start / Options";
                case "leftStick": return "Left stick press";
                case "rightStick": return "Right stick press";
                case "dpadUp": return "D-pad up";
                case "dpadDown": return "D-pad down";
                case "dpadLeft": return "D-pad left";
                case "dpadRight": return "D-pad right";
                default: return "unbound";
            }
        }
    }
}
