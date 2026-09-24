// OriginalKeyBindings.cs — maps OriginalPlayerSettings key bindings (Unity KeyCode names)
// to the map board's keyboard actions. Null bindings mean the defaults, which are exactly
// the keys OriginalMapBoard handled before bindings existed (PageUp/PageDown/Home/End).
// Display-only: no gameplay commands. Settings UI: CampaignView.PlayerSettings.cs.
using System;
using System.Collections.Generic;
using AshenSpire.Domain.Original;
using UnityEngine;

namespace AshenSpire.Presentation
{
    public static class OriginalKeyBindings
    {
        public static readonly string[] MapActions = { "mapScrollUp", "mapScrollDown", "mapTop", "mapBottom" };
        public static string Label(string action)
        {
            switch (action)
            {
                case "mapScrollUp": return "Map: scroll up";
                case "mapScrollDown": return "Map: scroll down";
                case "mapTop": return "Map: jump to top";
                case "mapBottom": return "Map: jump to bottom";
                default: return action;
            }
        }
        /// <summary>The bound action for <paramref name="code"/>, or null. Unknown key names bind nothing.</summary>
        public static string Action(IReadOnlyDictionary<string, string> bindings, KeyCode code)
        {
            if (code == KeyCode.None) return null;
            var source = bindings ?? OriginalPlayerSettings.DefaultKeyBindings;
            foreach (var action in MapActions)
                if (source.TryGetValue(action, out var key) && Enum.TryParse(key, true, out KeyCode bound) && bound == code) return action;
            return null;
        }
    }
}
