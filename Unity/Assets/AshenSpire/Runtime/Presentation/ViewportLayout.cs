// ViewportLayout.cs — the JS layout decider (src/main.js layoutForCap + FormFactorModel.js), pure.
// One decider picks the mode, the zoom and the composition together, from CSS px, as JS does (#24):
// narrow is width-owned (w <= NarrowMaxWidth); the zoom is the fit to the mode's reference size,
// clamped to [MinZoom, MaxZoom]; wide rounds to 0.01, narrow floors. Numbers: JsUiTokens.UiScale
// (generated from JS balance.ui.uiScale by tools/unity-ui-tokens.mjs).
// UNITS: 1 panel px = 1 JS CSS px at zoom 1. RunController turns Zoom into PanelSettings.scale for
// the wide or narrow PanelSettings (ExpeditionPanel / ExpeditionPanelNarrow). No Unity types here;
// UnityTests/Viewport checks this file against the JS arithmetic.
using System;
namespace AshenSpire.Presentation
{
    /// <summary>What the decider answers for one viewport (JS: --ui-zoom, data-layout, data-composition, data-short, data-form-factor).</summary>
    public sealed class UiLayout
    {
        public double Zoom { get; internal set; } = 1;
        public bool Narrow { get; internal set; }
        /// <summary>Wide but short (JS data-composition="short-wide").</summary>
        public bool Compact { get; internal set; }
        /// <summary>Below the height gate with no complete compact/narrow answer (JS data-short).</summary>
        public bool Short { get; internal set; }
        public string FormFactor { get; internal set; } = "desktop";
        /// <summary>The PanelSettings reference size for this mode (CSS px).</summary>
        public int ReferenceWidth => (int)(Narrow ? JsUiTokens.UiScale.NarrowWidth : JsUiTokens.UiScale.DesignWidth);
        public int ReferenceHeight => (int)(Narrow ? JsUiTokens.UiScale.NarrowHeight : JsUiTokens.UiScale.DesignHeight);
        public bool SameAs(UiLayout other) => other != null && other.Zoom == Zoom && other.Narrow == Narrow && other.Compact == Compact && other.Short == Short && other.FormFactor == FormFactor;
    }

    public static class ViewportLayout
    {
        /// <summary>JS layoutForCap(cap, w, h) in CSS px. <paramref name="cap"/> is a named UI size's zoom ceiling
        /// (balance.ui.uiScale.named); +Infinity is Auto. Invalid sizes answer zoom 1, wide.</summary>
        public static UiLayout Resolve(double cssWidth, double cssHeight, double cap = double.PositiveInfinity)
        {
            var w = cssWidth; var h = cssHeight;
            if (!(w > 0) || !(h > 0) || double.IsInfinity(w) || double.IsInfinity(h)) return new UiLayout();
            if (double.IsNaN(cap) || cap <= 0) cap = double.PositiveInfinity;
            double Clamp(double v) => Math.Max(JsUiTokens.UiScale.MinZoom, Math.Min(JsUiTokens.UiScale.MaxZoom, v));
            double Fit(double dw, double dh) => Math.Min(w / dw, h / dh);
            double Capped(double v) => Math.Min(v, cap);
            bool ShortWide(double zoom) => h < JsUiTokens.UiScale.GateBelowHeight && w / zoom >= JsUiTokens.UiScale.DesignWidth && h >= JsUiTokens.UiScale.ShortWideMinHeight;
            UiLayout Answer(double zoom, bool narrow)
            {
                var compact = !narrow && ShortWide(zoom);
                return new UiLayout { Zoom = zoom, Narrow = narrow, Compact = compact, Short = h < JsUiTokens.UiScale.GateBelowHeight && !compact, FormFactor = FormFactor(w, h) };
            }
            // Wide keeps Math.round (JS: byte-for-byte what players see); narrow floors so it never overflows.
            var wideZoom = Clamp(Capped(Math.Floor(Fit(JsUiTokens.UiScale.DesignWidth, JsUiTokens.UiScale.DesignHeight) * 100 + .5) / 100));
            var narrowFit = Clamp(Math.Floor(Fit(JsUiTokens.UiScale.NarrowWidth, JsUiTokens.UiScale.NarrowHeight) * 100) / 100);
            if (w <= JsUiTokens.UiScale.NarrowMaxWidth) return Answer(Clamp(Capped(narrowFit)), true);
            return Answer(wideZoom, false);
        }

        /// <summary>JS FormFactorModel.formFactorFor: widescreen, mobile, tablet or desktop (first match wins).</summary>
        public static string FormFactor(double w, double h)
        {
            if (!(w > 0) || !(h > 0)) return "desktop";
            if ((w / h >= JsUiTokens.FormFactor.WidescreenMinAspect && w >= JsUiTokens.FormFactor.WidescreenAspectMinWidth)
                || w >= JsUiTokens.FormFactor.WidescreenMinWidth) return "widescreen";
            if (w <= JsUiTokens.FormFactor.MobileMaxWidth) return "mobile";
            if (w <= JsUiTokens.FormFactor.TabletMaxWidth) return "tablet";
            return "desktop";
        }

        /// <summary>Unity's Scale With Screen Size factor for a PanelSettings (matchMode 0 = match width or height
        /// with <paramref name="match"/>, 1 = shrink, 2 = expand), computed on the same CSS-px size the zoom uses.</summary>
        public static double PanelFit(double cssWidth, double cssHeight, double referenceWidth, double referenceHeight, int matchMode, double match)
        {
            if (!(cssWidth > 0) || !(cssHeight > 0) || !(referenceWidth > 0) || !(referenceHeight > 0)) return 1;
            var sw = cssWidth / referenceWidth; var sh = cssHeight / referenceHeight;
            if (matchMode == 1) return Math.Max(sw, sh);
            if (matchMode == 2) return Math.Min(sw, sh);
            var t = Math.Max(0, Math.Min(1, match));
            return sw + (sh - sw) * t;
        }

        /// <summary>The panel-px height that displays as at least <paramref name="tapCssPx"/> CSS px when one panel px
        /// shows as <paramref name="displayScale"/> CSS px (JS --tap-floor = tap / zoom).</summary>
        public static float MinimumTouchHeight(double displayScale, double tapCssPx = 44)
        {
            if (!(displayScale > 0) || double.IsInfinity(displayScale)) displayScale = 1;
            if (!(tapCssPx > 0)) tapCssPx = 44;
            return (float)Math.Ceiling(tapCssPx / displayScale - 1e-6);
        }
    }
}
