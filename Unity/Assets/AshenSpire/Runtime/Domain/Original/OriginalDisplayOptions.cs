// OriginalDisplayOptions.cs — what the schema-3 display options (US-15.1) mean on screen,
// as pure data the presentation applies. No Unity types; checked by UnityTests/Mods.
// ROOT CLASSES: CampaignView.PlayerSettings.cs puts RootClasses(settings) on the app root;
// Resources/OriginalTheme.uss (accent, map header, control hints) and
// Resources/OriginalCards.uss (card motif and strength) style from them.
// UI SIZE: RunController.Settings.cs sets PanelSettings.scale = base × PanelScale(...).
// Values are the HTML game's (src/content/balance.js ui.accents / ui.uiScale.named /
// ui.cardMotifStrength); the USS sheets restate the colours and opacities, and the
// Mods suite fails if the two drift apart.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalDisplayOptions
    {
        /// <summary>Every root class prefix this file owns; the presentation removes these before re-adding.</summary>
        public static readonly string[] ClassPrefixes = { "ui-size-", "accent-", "card-motif-", "motif-strength-", "map-header-", "no-control-hints" };

        /// <summary>HTML balance.ui.uiScale.named (s .85, m 1, l 1.2, xl 1.45). Auto is 1: the Unity panel already
        /// scales with the screen height (ExpeditionPanel: scale with screen size, match height).</summary>
        public static readonly IReadOnlyDictionary<string, double> UiSizeFactors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        { ["Auto"] = 1, ["S"] = .85, ["M"] = 1, ["L"] = 1.2, ["XL"] = 1.45 };

        /// <summary>The smallest logical panel a larger UI size may leave: the 430-wide portrait layout, 600 tall
        /// (ViewportLayout's short-screen threshold). A named size is a ceiling, as in the HTML game:
        /// S–XL "asks for a fixed size and gets as much of it as fits".</summary>
        public const double MinLayoutWidth = 430, MinLayoutHeight = 600;

        /// <summary>Accent → the --ash-gold token. Gold keeps the Unity theme's own gold (#d6b475) so the
        /// default look is unchanged; the other four are the HTML balance.ui.accents hex values.</summary>
        public static readonly IReadOnlyDictionary<string, string> AccentColors = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["gold"] = "#d6b475", ["crimson"] = "#c1453a", ["frost"] = "#7fa8c9", ["verdant"] = "#8bae54", ["violet"] = "#a06cc8" };

        /// <summary>HTML balance.ui.cardMotifStrength: the wash opacity. Accent mode draws its sigil at × 1.3.</summary>
        public static readonly IReadOnlyDictionary<string, double> MotifStrengths = new Dictionary<string, double>(StringComparer.Ordinal)
        { ["subtle"] = .06, ["normal"] = .10, ["strong"] = .17 };
        public const double AccentMotifFactor = 1.3;

        public static double UiSizeFactor(string uiSize) => uiSize != null && UiSizeFactors.TryGetValue(uiSize, out var factor) ? factor : 1;

        /// <summary>
        /// The multiplier for PanelSettings.scale. <paramref name="uiScale"/> is the numeric "Interface size"
        /// slider; <paramref name="logicalWidth"/>/<paramref name="logicalHeight"/> are the panel's size at
        /// multiplier 1. S and M apply as asked. L and XL grow only as far as the panel stays at least
        /// MinLayoutWidth × MinLayoutHeight, and never below the slider alone. Unknown sizes act as Auto.
        /// </summary>
        public static double PanelScale(double uiScale, string uiSize, double logicalWidth, double logicalHeight)
        {
            var slider = double.IsNaN(uiScale) || double.IsInfinity(uiScale) ? 1 : Math.Min(OriginalPlayerSettings.UiScaleMax, Math.Max(OriginalPlayerSettings.UiScaleMin, uiScale));
            var named = UiSizeFactor(uiSize);
            if (named <= 1) return slider * named;
            if (!(logicalWidth > 0) || !(logicalHeight > 0) || double.IsInfinity(logicalWidth) || double.IsInfinity(logicalHeight)) return slider;
            var fit = Math.Min(logicalWidth / MinLayoutWidth, logicalHeight / MinLayoutHeight);
            return Math.Max(slider, Math.Min(slider * named, fit));
        }

        /// <summary>The root classes for <paramref name="settings"/>, in a stable order. Unknown stored values
        /// cannot reach here (FromJson keeps the closed sets), but defaults are used if they do.</summary>
        public static IReadOnlyList<string> RootClasses(OriginalPlayerSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string Pick(string value, string[] allowed, string fallback) => allowed.Contains(value, StringComparer.Ordinal) ? value : fallback;
            var classes = new List<string>
            {
                "ui-size-" + Pick(settings.UiSize, OriginalPlayerSettings.UiSizes, "Auto").ToLowerInvariant(),
                "accent-" + Pick(settings.Accent, OriginalPlayerSettings.Accents, "gold"),
                "card-motif-" + Pick(settings.CardMotif, OriginalPlayerSettings.CardMotifs, OriginalPlayerSettings.DefaultCardMotif),
                "motif-strength-" + Pick(settings.CardMotifStrength, OriginalPlayerSettings.CardMotifStrengths, "normal"),
                "map-header-" + Pick(settings.MapHeaderDensity, OriginalPlayerSettings.MapHeaderDensities, "comfortable"),
            };
            if (!settings.MapHeaderRelics) classes.Add("map-header-no-relics");
            if (!settings.MapHeaderSeed) classes.Add("map-header-no-seed");
            if (!settings.ControlHints) classes.Add("no-control-hints");
            return classes;
        }

        /// <summary>True for a root class RootClasses may produce (used to clear stale ones).</summary>
        public static bool IsDisplayClass(string name) => name != null && ClassPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }
}
