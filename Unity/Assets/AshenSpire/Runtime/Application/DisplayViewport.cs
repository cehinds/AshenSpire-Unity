// DisplayViewport.cs — reads the presented canvas size, not its WebGL pixel buffer.
// WEB: Plugins/WebGL/DisplayViewport.jslib reads this instance's canvas client height.
// NATIVE: keeps Screen.height behavior for Height. DevicePixelRatio converts screen pixels to the
// CSS px the JS layout decider works in (RunController.Settings.cs ApplyPanelScale).
// No scene component or Inspector wiring needed.
// VERIFY: dense phone browsers and portrait/landscape keep controls at least 44 CSS px.
using System.Runtime.InteropServices;
using UnityEngine;
namespace AshenSpire.Application
{
    public static class DisplayViewport
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int AshenSpire_CanvasHeight();
#endif
        public static int Height
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                var height = AshenSpire_CanvasHeight();
                if (height > 0) return height;
#endif
                return Mathf.Max(1, Screen.height);
            }
        }

        /// <summary>
        /// Screen pixels per CSS px. Web: the canvas buffer over its CSS height (the browser's
        /// devicePixelRatio). Phones: Screen.dpi / 160, the Android density-independent pixel a mobile
        /// browser's CSS px matches, so a portrait phone gets the JS narrow layout. Desktop and the
        /// editor: 1 (one screen pixel is one CSS px, as a desktop browser at 100%).
        /// </summary>
        public static double DevicePixelRatio
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return System.Math.Max(1e-3, (double)Mathf.Max(1, Screen.height) / Height);
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                var dpi = Screen.dpi;
                return dpi > 0 ? System.Math.Max(1, dpi / 160d) : 1;
#else
                return 1;
#endif
            }
        }
    }
}
