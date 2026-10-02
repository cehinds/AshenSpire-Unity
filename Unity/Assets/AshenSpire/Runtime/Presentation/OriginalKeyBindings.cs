// OriginalKeyBindings.cs — maps OriginalPlayerSettings key bindings (Unity KeyCode names)
// to context-specific map and solo combat actions. Null bindings mean defaults.
// This resolver never executes commands. Settings UI: CampaignView.PlayerSettings.cs.
using System;
using System.Collections.Generic;
using AshenSpire.Domain.Original;
using UnityEngine;

namespace AshenSpire.Presentation
{
    public static class OriginalKeyBindings
    {
        public static readonly string[] MapActions = { "mapScrollUp", "mapScrollDown", "mapTop", "mapBottom" };
        public static readonly string[] CombatActions = { "combatPlay", "endTurn", "combatDeck", "drawPile", "discardPile", "exhaustPile", "flask1", "flask2", "flask3", "targetPrevious", "targetNext", "card1", "card2", "card3", "card4", "card5", "card6", "card7", "card8", "card9" };
        public static string DisplayKey(string key)
        {
            if (key == null) return "unbound";
            if (key.StartsWith("Alpha", StringComparison.Ordinal) && key.Length == 6) return key.Substring(5);
            switch (key)
            {
                case "Return": return "Enter";
                case "LeftArrow": return "Left arrow";
                case "RightArrow": return "Right arrow";
                case "UpArrow": return "Up arrow";
                case "DownArrow": return "Down arrow";
                case "PageUp": return "Page Up";
                case "PageDown": return "Page Down";
                default: return key;
            }
        }
        public static string Label(string action)
        {
            switch (action)
            {
                case "mapScrollUp": return "Map: scroll up";
                case "mapScrollDown": return "Map: scroll down";
                case "mapTop": return "Map: jump to top";
                case "mapBottom": return "Map: jump to bottom";
                case "combatPlay": return "Combat: play selected card";
                case "endTurn": return "Combat: end turn";
                case "combatDeck": return "Combat: deck and equipment";
                case "drawPile": return "Combat: draw pile";
                case "discardPile": return "Combat: discard pile";
                case "exhaustPile": return "Combat: exhausted pile";
                case "flask1": return "Combat: use Crimson flask";
                case "flask2": return "Combat: use Azure flask";
                case "flask3": return "Combat: use first utility flask";
                case "targetPrevious": return "Combat: previous enemy";
                case "targetNext": return "Combat: next enemy";
                case "card1": case "card2": case "card3": case "card4": case "card5": case "card6": case "card7": case "card8": case "card9": return "Combat: select card " + action.Substring(4);
                default: return action;
            }
        }
        /// <summary>Gamepad list label: the keyboard label, with the pad-only actions and Play's double duty named.</summary>
        public static string PadLabel(string action)
        {
            switch (action)
            {
                case "cancel": return "Cancel / back";
                case "menu": return "Go to menu button";
                case "combatPlay": return "Confirm · Combat: play selected card";
                default: return Label(action);
            }
        }
        /// <summary>The bound action for <paramref name="code"/>, or null. Unknown key names bind nothing.</summary>
        public static string Action(IReadOnlyDictionary<string, string> bindings, KeyCode code)
            => Find(bindings, code, MapActions);
        public static string CombatAction(IReadOnlyDictionary<string, string> bindings, KeyCode code)
            => Find(bindings, code, CombatActions);
        private static string Find(IReadOnlyDictionary<string, string> bindings, KeyCode code, string[] actions)
        {
            if (code == KeyCode.None) return null;
            var source = bindings ?? OriginalPlayerSettings.DefaultKeyBindings;
            foreach (var action in actions)
                if (source.TryGetValue(action, out var key) && Enum.TryParse(key, true, out KeyCode bound) && bound == code) return action;
            return null;
        }
    }
}
