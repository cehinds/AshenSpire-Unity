// DisplayChecks.cs — OriginalDisplayOptions (US-15.1): what each display setting does on screen.
// Root classes, the UI size → panel scale mapping, and that the USS sheets the classes drive
// restate the shipped content's numbers (balance.ui accents / uiScale.named / cardMotifStrength).
using System.Globalization;
using System.Text.RegularExpressions;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

static class DisplayChecks
{
    public static void Run(string root, Action<bool, string> Check)
    {
        var resources = Path.Combine(root, "Unity/Assets/AshenSpire/Resources");
        var ui = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(resources, "Original/content.json")))["balance"]["ui"];
        var theme = File.ReadAllText(Path.Combine(resources, "OriginalTheme.uss"));
        var cards = File.ReadAllText(Path.Combine(resources, "OriginalCards.uss"));
        var uss = theme + "\n" + cards;

        // ---- Root classes --------------------------------------------------------------------
        var d = new OriginalPlayerSettings();
        Check(OriginalDisplayOptions.RootClasses(d).SequenceEqual(new[] { "ui-size-auto", "accent-gold", "card-motif-wash", "motif-strength-normal", "map-header-comfortable" }),
            "classes: defaults are Auto, gold, wash, normal, comfortable with relics, seed and hints shown");
        var custom = new OriginalPlayerSettings { UiSize = "XL", Accent = "violet", CardMotif = "band", CardMotifStrength = "strong", MapHeaderDensity = "compact", MapHeaderRelics = false, MapHeaderSeed = false, ControlHints = false };
        Check(OriginalDisplayOptions.RootClasses(custom).SequenceEqual(new[] { "ui-size-xl", "accent-violet", "card-motif-band", "motif-strength-strong", "map-header-compact", "map-header-no-relics", "map-header-no-seed", "no-control-hints" }),
            "classes: every option is one root class, hidden header parts and hints included");
        var stray = new OriginalPlayerSettings { UiSize = "huge", Accent = "teal", CardMotif = null };
        Check(OriginalDisplayOptions.RootClasses(stray).Take(3).SequenceEqual(new[] { "ui-size-auto", "accent-gold", "card-motif-wash" }), "classes: a value outside the closed set falls back to its default");
        var every = OriginalPlayerSettings.UiSizes.Select(v => "ui-size-" + v.ToLowerInvariant()).Concat(OriginalPlayerSettings.Accents.Select(v => "accent-" + v))
            .Concat(OriginalPlayerSettings.CardMotifs.Select(v => "card-motif-" + v)).Concat(OriginalPlayerSettings.CardMotifStrengths.Select(v => "motif-strength-" + v))
            .Concat(OriginalPlayerSettings.MapHeaderDensities.Select(v => "map-header-" + v)).Concat(new[] { "map-header-no-relics", "map-header-no-seed", "no-control-hints" }).ToArray();
        Check(every.All(OriginalDisplayOptions.IsDisplayClass) && !new[] { "app", "high-contrast", "palette-protanopia", "ui-wide", "large-text", "map-screen" }.Any(OriginalDisplayOptions.IsDisplayClass),
            "classes: the clearing prefix covers every display class and no other root class");

        // ---- Each class that changes the look has a USS rule ------------------------------------
        // Defaults are the base look; UI size is panel scale (RunController), not a style.
        var noRule = new HashSet<string> { "accent-gold", "card-motif-off", "motif-strength-normal", "map-header-comfortable" };
        var missing = every.Where(c => !c.StartsWith("ui-size-", StringComparison.Ordinal) && !noRule.Contains(c) && !Regex.IsMatch(uss, @"\." + Regex.Escape(c) + @"[\s.{:]")).ToArray();
        Check(missing.Length == 0, "uss: every non-default display class has a rule" + (missing.Length == 0 ? "" : " (missing " + string.Join(", ", missing) + ")"));

        // ---- Accent ----------------------------------------------------------------------------
        var baseGold = Regex.Match(theme, @"^\.app \{[^}]*--ash-gold: (?<v>#[0-9a-fA-F]{6})", RegexOptions.Multiline).Groups["v"].Value;
        Check(baseGold == OriginalDisplayOptions.AccentColors["gold"], "accent: gold keeps the theme's own --ash-gold " + baseGold);
        Check(OriginalPlayerSettings.Accents.SequenceEqual(OriginalDisplayOptions.AccentColors.Keys) && OriginalPlayerSettings.Accents.All(a => ui["accents"][a] != null), "accent: one colour per setting choice, each in balance.ui.accents");
        foreach (var accent in OriginalPlayerSettings.Accents.Where(a => a != "gold"))
        {
            var hex = OriginalDisplayOptions.AccentColors[accent];
            Check(string.Equals(hex, (string)ui["accents"][accent]["hex"], StringComparison.OrdinalIgnoreCase), "accent: " + accent + " is the HTML " + hex);
            Check(theme.Contains(".app.accent-" + accent + " { --ash-gold: " + hex + "; }"), "uss: .app.accent-" + accent + " sets --ash-gold to " + hex);
        }

        // ---- UI size ---------------------------------------------------------------------------
        var named = (JObject)ui["uiScale"]["named"];
        Check(OriginalPlayerSettings.UiSizes.Where(s => s != "Auto").All(s => Math.Abs(OriginalDisplayOptions.UiSizeFactor(s) - (double)named[s.ToLowerInvariant()]) < 1e-9) && OriginalDisplayOptions.UiSizeFactor("Auto") == 1,
            "ui size: S–XL are the HTML balance.ui.uiScale.named factors; Auto is 1 (the panel already fits the screen)");
        const double desktopW = 1600, desktopH = 900, phoneW = 416, phoneH = 900; // 1920×1080 and 390×844 at reference height 900
        bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
        Check(Near(OriginalDisplayOptions.PanelScale(1, "Auto", phoneW, phoneH), 1) && Near(OriginalDisplayOptions.PanelScale(1.25, "Auto", phoneW, phoneH), 1.25) && Near(OriginalDisplayOptions.PanelScale(1, "M", desktopW, desktopH), 1),
            "ui size: Auto and M leave the Interface size slider as it is");
        Check(Near(OriginalDisplayOptions.PanelScale(1, "S", phoneW, phoneH), .85) && Near(OriginalDisplayOptions.PanelScale(1.2, "S", phoneW, phoneH), 1.2 * .85), "ui size: S shrinks by .85 on any screen, times the slider");
        Check(Near(OriginalDisplayOptions.PanelScale(1, "L", desktopW, desktopH), 1.2) && Near(OriginalDisplayOptions.PanelScale(1, "XL", desktopW, desktopH), 1.45), "ui size: L and XL apply in full where they fit (1920×1080)");
        Check(Near(OriginalDisplayOptions.PanelScale(1, "XL", phoneW, phoneH), 1) && Near(OriginalDisplayOptions.PanelScale(1, "L", 516, 900), 1.2), "ui size: on a phone L/XL get only what fits (none at 390 wide; L in full at 516 logical)");
        Check(Near(OriginalDisplayOptions.PanelScale(1.5, "XL", desktopW, desktopH), 1.5) && Near(OriginalDisplayOptions.PanelScale(1.2, "XL", 1000, desktopH), 1.5), "ui size: XL is capped at 900 / 600 tall but never below the slider");
        Check(Near(OriginalDisplayOptions.PanelScale(1.1, "XL", double.NaN, 0), 1.1) && Near(OriginalDisplayOptions.PanelScale(1, "giant", desktopW, desktopH), 1) && Near(OriginalDisplayOptions.PanelScale(9, "Auto", desktopW, desktopH), OriginalPlayerSettings.UiScaleMax),
            "ui size: unknown screen size, unknown size names and an out-of-range slider are safe");
        Check(Near(OriginalDisplayOptions.PanelScale(1, "xl", desktopW, desktopH), 1.45), "ui size: names match case-insensitively");

        // ---- Card motif ------------------------------------------------------------------------
        var strengths = (JObject)ui["cardMotifStrength"];
        Check(OriginalPlayerSettings.CardMotifStrengths.All(s => Near(OriginalDisplayOptions.MotifStrengths[s], (double)strengths[s])), "motif: strengths are balance.ui.cardMotifStrength (.06/.10/.17)");
        string Opacity(string selector) => Regex.Match(cards, Regex.Escape(selector) + @"\s*\{[^}]*opacity: (?<v>[0-9.]+)").Groups["v"].Value;
        double Read(string selector) => double.TryParse(Opacity(selector), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        Check(Near(Read(".original-card .original-card-motif"), OriginalDisplayOptions.MotifStrengths["normal"]) && Near(Read(".card-motif-wash.motif-strength-subtle .original-card .original-card-motif"), OriginalDisplayOptions.MotifStrengths["subtle"])
            && Near(Read(".card-motif-wash.motif-strength-strong .original-card .original-card-motif"), OriginalDisplayOptions.MotifStrengths["strong"]), "uss: wash opacities are the three strengths");
        Check(OriginalPlayerSettings.CardMotifStrengths.All(s => Near(Math.Round(Read(s == "normal" ? ".card-motif-accent .original-card .original-card-motif" : ".card-motif-accent.motif-strength-" + s + " .original-card .original-card-motif"), 4), Math.Round(OriginalDisplayOptions.MotifStrengths[s] * OriginalDisplayOptions.AccentMotifFactor, 4))),
            "uss: accent sigil opacities are the strengths × 1.3");
        Check(Regex.IsMatch(cards, @"\.card-motif-wash \.original-card \.original-card-motif \{ display: flex; \}") && Regex.IsMatch(cards, @"\.original-card \.original-card-motif \{\s*display: none;"),
            "uss: the motif layer is hidden unless a motif mode shows it (Off shows none)");
        var classes = ((JArray)JObject.Parse(File.ReadAllText(Path.Combine(resources, "Original/content.json")))["classes"]).OfType<JObject>().ToArray();
        Check(classes.Length > 0 && classes.All(c => Regex.IsMatch((string)c["cardTint"] ?? "", "^#[0-9a-fA-F]{6}$")), "motif: every shipped class has a cardTint for the motif layer");
    }
}
