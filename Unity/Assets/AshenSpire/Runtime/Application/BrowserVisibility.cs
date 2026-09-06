// BrowserVisibility.cs — Web-only document lifecycle adapter; no polling or rules.
// RunController installs after its view exists and removes before disposing it.
// The matching Plugins/WebGL/BrowserVisibility.jslib sends only visibility state.
using System.Runtime.InteropServices;

namespace AshenSpire.Application
{
    internal static class BrowserVisibility
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void AshenSpire_WatchVisibility(string owner);
        [DllImport("__Internal")] private static extern void AshenSpire_UnwatchVisibility();
#endif
        public static void Install(string owner)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            AshenSpire_WatchVisibility(owner);
#endif
        }
        public static void Remove()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            AshenSpire_UnwatchVisibility();
#endif
        }
    }
}
