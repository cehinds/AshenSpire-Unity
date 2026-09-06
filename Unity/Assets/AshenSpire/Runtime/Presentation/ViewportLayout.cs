// ViewportLayout.cs — pure display-space sizing shared by controller and view.
// Web passes CSS canvas height; native players retain their screen-pixel sizing.
// Render-buffer density must never determine the physical size of a browser control.
using System;
namespace AshenSpire.Presentation
{
    public static class ViewportLayout
    {
        public static int ReferenceHeight(int displayHeight) => displayHeight < 600 ? Math.Max(1, displayHeight) : 900;
        public static float MinimumTouchHeight(int displayHeight) => (float)Math.Ceiling(44d * ReferenceHeight(displayHeight) / Math.Max(1, displayHeight));
    }
}
