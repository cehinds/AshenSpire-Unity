// DisplayViewport.cs — reads the presented canvas size, not its WebGL pixel buffer.
// WEB: Plugins/WebGL/DisplayViewport.jslib reads this instance's canvas client height.
// NATIVE: keeps Screen.height behavior. No scene component or Inspector wiring needed.
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
    }
}
