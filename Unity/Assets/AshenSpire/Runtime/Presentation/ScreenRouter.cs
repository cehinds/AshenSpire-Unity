// ScreenRouter.cs — full-viewport layered screens, as the JS game composes them (JS tokens.json `layer`).
// LAYERS (child order = stacking, bottom to top, under the app root):
//   screen-layer-base     one full-viewport screen at a time (JS #app > .screen; layer.scene/hud)
//   screen-layer-overlay  modals and covers above the screen (JS layer.sheet/popover; the interruption cover)
//   screen-layer-toast    passive notices, never pickable (JS layer.toast)
// SCREEN: Show() replaces the base screen with a new `.screen` holding its own vertical ScrollView
// (`.screen-scroll`, also `.scroll` for the legacy sheet) and a `.body` content element inside it.
// A screen that does not scroll in JS (map, combat, guided creation) calls SetFixed(body, true): the
// body leaves the scroll region and fills the screen, so absolute JS geometry never fights a scroller.
// SAFE AREA: SetSafeArea pads the app root (the base layer follows the padding) and insets the
// absolute overlay/toast layers by the same amounts. Presentation only: no game state or saves.
using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public sealed class ScreenRouter
    {
        public const string LayerClass = "screen-layer", BaseClass = "screen-layer-base", OverlayClass = "screen-layer-overlay", ToastClass = "screen-layer-toast";
        public const string ScreenClass = "screen", ScrollClass = "screen-scroll", BodyClass = "body", FixedClass = "screen-fixed";
        private readonly VisualElement _app;
        public VisualElement BaseLayer { get; }
        public VisualElement OverlayLayer { get; }
        public VisualElement ToastLayer { get; }
        /// <summary>The current full-viewport screen (null before the first Show).</summary>
        public VisualElement Screen { get; private set; }
        /// <summary>The current screen's own scroll region.</summary>
        public ScrollView Scroll { get; private set; }
        /// <summary>The current screen's content element (inside Scroll, or directly in Screen when fixed).</summary>
        public VisualElement Body { get; private set; }
        public string Identity { get; private set; }

        public ScreenRouter(VisualElement app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            app.styleSheets.Add(Resources.Load<StyleSheet>("Generated/JsTokens"));
            app.styleSheets.Add(Resources.Load<StyleSheet>("ScreenRouter"));
            BaseLayer = Layer(BaseClass, PickingMode.Position);
            OverlayLayer = Layer(OverlayClass, PickingMode.Ignore);
            ToastLayer = Layer(ToastClass, PickingMode.Ignore);
        }
        private VisualElement Layer(string className, PickingMode picking)
        {
            var layer = new VisualElement { name = className, pickingMode = picking };
            layer.AddToClassList(LayerClass); layer.AddToClassList(className);
            _app.Add(layer);
            return layer;
        }
        /// <summary>Re-attach the layers in order (after an app-root Clear) and keep them below later root children.</summary>
        public void EnsureLayers()
        {
            foreach (var layer in new[] { BaseLayer, OverlayLayer, ToastLayer })
                if (layer.parent != _app) _app.Add(layer);
        }

        /// <summary>Replace the base screen. Returns the new screen's body (scrolling until SetFixed).</summary>
        public VisualElement Show(string identity)
        {
            EnsureLayers();
            BaseLayer.Clear();
            Identity = identity;
            Screen = new VisualElement { name = "screen" };
            Screen.AddToClassList(ScreenClass);
            Scroll = new ScrollView(ScrollViewMode.Vertical) { name = "screen-scroll", verticalScrollerVisibility = ScrollerVisibility.Hidden };
            Scroll.AddToClassList(ScrollClass); Scroll.AddToClassList("scroll");
            Screen.Add(Scroll);
            Body = new VisualElement();
            Body.AddToClassList(BodyClass);
            Scroll.Add(Body);
            BaseLayer.Add(Screen);
            return Body;
        }

        /// <summary>The screen element that hosts <paramref name="element"/> (itself, or its nearest `.screen` ancestor).</summary>
        public static VisualElement ScreenOf(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent) if (e.ClassListContains(ScreenClass)) return e;
            return null;
        }
        public static bool IsFixed(VisualElement element) => ScreenOf(element)?.ClassListContains(FixedClass) == true;

        /// <summary>
        /// Fixed: <paramref name="body"/> fills its screen with no scroller (JS screens that do not scroll).
        /// Scrolling: it returns to the top of the screen's own scroll region. Idempotent; a body outside a
        /// router screen is left alone.
        /// </summary>
        public static void SetFixed(VisualElement body, bool fixedScreen)
        {
            var screen = ScreenOf(body);
            if (screen == null || body == screen) return;
            var scroll = screen.Q<ScrollView>(className: ScrollClass);
            if (scroll == null) return;
            screen.EnableInClassList(FixedClass, fixedScreen);
            if (fixedScreen)
            {
                if (body.parent != screen) screen.Insert(screen.IndexOf(scroll) + 1, body);
            }
            else if (body.parent != scroll.contentContainer)
            {
                scroll.Add(body);
                scroll.scrollOffset = Vector2.zero;
            }
        }

        /// <summary>Show a modal or cover above the screen. The caller owns removing it.</summary>
        public void OpenOverlay(VisualElement overlay)
        {
            EnsureLayers();
            overlay.AddToClassList("screen-overlay");
            OverlayLayer.Add(overlay);
        }

        /// <summary>A passive notice in the toast layer, removed after <paramref name="durationMs"/>.</summary>
        public VisualElement Toast(string text, long durationMs)
        {
            EnsureLayers();
            var toast = new Label(text) { pickingMode = PickingMode.Ignore };
            toast.AddToClassList("screen-toast");
            ToastLayer.Add(toast);
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(Math.Max(0, durationMs));
            return toast;
        }

        /// <summary>Safe-area insets in panel px: the root's padding for the base layer, offsets for the absolute layers.</summary>
        public void SetSafeArea(float top, float right, float bottom, float left)
        {
            _app.style.paddingTop = top; _app.style.paddingRight = right; _app.style.paddingBottom = bottom; _app.style.paddingLeft = left;
            foreach (var layer in new[] { OverlayLayer, ToastLayer })
            {
                layer.style.top = top; layer.style.right = right; layer.style.bottom = bottom; layer.style.left = left;
            }
        }
    }
}
