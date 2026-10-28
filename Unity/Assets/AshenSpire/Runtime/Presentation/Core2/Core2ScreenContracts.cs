// Core2ScreenContracts.cs — the seam between the Core 2 run screens (map, spoils, shop, rest, event, Armoury, deck
// editor) and the Core 2 combat screen, so the two can be built separately. The run host mounts whatever fight screen is
// registered while Core2RunSession.Screen is Fight, and reads the session again when the screen reports the fight over.
// Presentation only: every rule lives in AshenSpire.Domain.Core2.
using System;
using AshenSpire.Domain.Core2;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    /// <summary>A Core 2 combat screen: drives session.Fight and calls onFightOver once session.Fight.Result is set.</summary>
    public interface ICore2FightScreen
    {
        /// <summary>Build the screen under host (a fixed ScreenRouter body) for the fight in progress.</summary>
        void Mount(VisualElement host, Core2RunSession session, Action onFightOver);
        /// <summary>Tear down: stop timers and input hooks; the host clears its children.</summary>
        void Unmount();
    }

    /// <summary>Registry the run host reads; the combat screen registers its factory at startup.</summary>
    public static class Core2Screens
    {
        public static Func<ICore2FightScreen> FightScreenFactory { get; set; }
        /// <summary>True while the interruption cover is up (backgrounded, paused, hidden tab) until the player returns:
        /// timed play (Auto, paced beats) must hold still, so nothing resolves behind the cover.</summary>
        public static Func<bool> Interrupted { get; set; }
        public static bool IsInterrupted => Interrupted?.Invoke() == true;
    }
}
