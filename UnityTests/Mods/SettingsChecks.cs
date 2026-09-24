// SettingsChecks.cs — OriginalPlayerSettings defaults, migration, clamping and key bindings.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

static class SettingsChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Func<string, int, int> Prefs(int reduced, int fast, int muted) => (key, fallback) =>
            key == OriginalPlayerSettings.LegacyReducedMotionKey ? reduced : key == OriginalPlayerSettings.LegacyFastMotionKey ? fast : key == OriginalPlayerSettings.LegacyMutedKey ? muted : fallback;

        // Defaults reproduce the current build.
        var d = new OriginalPlayerSettings();
        Check(d.TextScale == 1 && d.UiScale == 1 && d.AnimationSpeed == 1 && !d.InstantAnimations && d.AnimationDurationScale == 1, "defaults: text/UI scale 1 and full-length feedback");
        Check(!d.ReducedMotion && !d.ScreenShake && d.ScreenShakeIntensity == 1 && !d.HitStop && d.ColorblindPalette == ColorblindPalette.None, "defaults: motion on, no shake or hit-stop, no palette");
        Check(d.MasterVolume == 1 && d.MusicVolume == 1 && d.SfxVolume == 1 && d.UiVolume == 1 && !d.Muted && d.Gain("sfx") == 1, "defaults: unattenuated, unmuted audio buses");
        Check(d.KeyBindings.Count == 4 && d.KeyBindings["mapScrollUp"] == "PageUp" && d.KeyBindings["mapBottom"] == "End" && d.KeyConflicts().Count == 0, "defaults: map keys match OriginalMapBoard, no conflicts");
        var fresh = OriginalPlayerSettings.LoadOrMigrate(null, Prefs(0, 0, 0), out _);
        Check(JToken.DeepEquals(fresh.ToJson(), d.ToJson()), "defaults: a device with no saved preferences equals defaults");
        Check(JToken.DeepEquals(OriginalPlayerSettings.LoadOrMigrate("", (k, f) => f, out _).ToJson(), d.ToJson()), "defaults: absent legacy keys fall back to current defaults");

        // Migration from PlayerPrefs flags (schema 0).
        var legacy = OriginalPlayerSettings.LoadOrMigrate(null, Prefs(1, 1, 1), out var legacyNotes);
        Check(legacy.ReducedMotion && legacy.AnimationSpeed == 2 && legacy.AnimationDurationScale == .5 && legacy.Muted, "migration: reduced motion, quick animations (x.5 duration) and mute carried over");
        Check(legacy.Gain("music") == 0 && legacyNotes.Any(n => n.Contains("migrated")), "migration: mute silences every bus and is reported");
        Check(OriginalPlayerSettings.LoadOrMigrate(null, Prefs(0, 1, 0), out _).AnimationDurationScale == .5 && !OriginalPlayerSettings.LoadOrMigrate(null, Prefs(0, 1, 0), out _).ReducedMotion, "migration: single legacy flag maps alone");
        var prefsObject = OriginalPlayerSettings.FromJson(JObject.Parse("{\"AshenSpire.ReducedMotion\":1,\"AshenSpire.FastMotion\":0,\"AshenSpire.Muted\":1}"), out _);
        var flagObject = OriginalPlayerSettings.FromJson(JObject.Parse("{\"reducedMotion\":true,\"fastMotion\":true,\"muted\":false}"), out _);
        Check(prefsObject.ReducedMotion && prefsObject.Muted && prefsObject.AnimationSpeed == 1 && flagObject.ReducedMotion && flagObject.AnimationSpeed == 2 && !flagObject.Muted, "migration: schema-0 JSON (PlayerPrefs keys or flag names) is read");
        var stored = OriginalPlayerSettings.LoadOrMigrate(new OriginalPlayerSettings { TextScale = 1.2 }.ToJson().ToString(), Prefs(1, 1, 1), out _);
        Check(stored.TextScale == 1.2 && !stored.ReducedMotion, "migration: a stored v1 record wins over legacy flags");
        var unreadable = OriginalPlayerSettings.LoadOrMigrate("{{{", Prefs(1, 0, 0), out var unreadableNotes);
        Check(unreadable.ReducedMotion && unreadableNotes[0].Contains("unreadable"), "migration: unreadable stored text falls back to legacy flags");

        // Clamping and bad values.
        var wild = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":1,\"textScale\":5,\"uiScale\":0.1,\"animationSpeed\":10,\"screenShakeIntensity\":-2,\"audio\":{\"master\":3,\"music\":-1,\"sfx\":0.25,\"ui\":\"loud\"}}"), out var wildNotes);
        Check(wild.TextScale == 1.6 && wild.UiScale == .75 && wild.AnimationSpeed == 2 && wild.ScreenShakeIntensity == 0, "clamp: scales, speed and intensity pinned to their ranges");
        Check(wild.MasterVolume == 1 && wild.MusicVolume == 0 && wild.SfxVolume == .25 && wild.UiVolume == 1, "clamp: bus volumes pinned to 0–1, non-numbers use defaults");
        Check(wildNotes.Count == 7 && wildNotes.Any(n => n == "textScale 5 clamped to 1.6"), "clamp: every adjustment is reported");
        var low = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":1,\"textScale\":0.2,\"animationSpeed\":0.1}"), out _);
        Check(low.TextScale == .8 && low.AnimationSpeed == .5, "clamp: lower bounds 0.8 text and 0.5 speed");
        var odd = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":1,\"colorblindPalette\":\"sepia\",\"hitStop\":\"yes\",\"keyBindings\":[]}"), out var oddNotes);
        Check(odd.ColorblindPalette == ColorblindPalette.None && !odd.HitStop && odd.KeyBindings.Count == 4 && oddNotes.Count == 3, "bad values: unknown palette, non-bool and bad bindings fall back with notes");
        Check(OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":1,\"animationSpeed\":\"instant\"}"), out _).AnimationDurationScale == 0, "instant animations: shorthand gives zero duration");
        var future = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":9,\"textScale\":1.4,\"newThing\":1}"), out var futureNotes);
        Check(future.TextScale == 1.4 && futureNotes.Any(n => n.Contains("newer")), "newer schema: known fields read, version noted");
        Check(OriginalPlayerSettings.FromJson(new JArray(), out var arrayNotes).TextScale == 1 && arrayNotes.Count == 1, "non-object settings give defaults");

        // Round trip.
        var custom = new OriginalPlayerSettings { TextScale = 1.35, UiScale = 1.25, AnimationSpeed = .75, InstantAnimations = true, ReducedMotion = true, ScreenShake = true, ScreenShakeIntensity = .4, HitStop = true, ColorblindPalette = ColorblindPalette.Tritanopia, MasterVolume = .8, MusicVolume = .3, SfxVolume = .6, UiVolume = .9, Muted = true };
        custom.BindSwapping("mapTop", "T"); custom.TryBind("openDeck", "D", out _);
        var text = custom.ToJson().ToString();
        var back = OriginalPlayerSettings.LoadOrMigrate(text, Prefs(0, 0, 0), out var backNotes);
        Check(JToken.DeepEquals(back.ToJson(), custom.ToJson()) && backNotes.Count == 0, "roundtrip: every field survives save and load without adjustments");
        Check((string)custom.ToJson()["colorblindPalette"] == "tritanopia" && (int)custom.ToJson()["schemaVersion"] == 1, "roundtrip: palette stored by name with schemaVersion 1");
        foreach (ColorblindPalette palette in Enum.GetValues(typeof(ColorblindPalette)))
            Check(OriginalPlayerSettings.TryParsePalette(OriginalPlayerSettings.PaletteName(palette), out var parsed) && parsed == palette, "palette " + palette + " round-trips by name");
        Check(JToken.DeepEquals(custom.Clone().ToJson(), custom.ToJson()), "clone is a deep equal copy");

        // Content mods toggle and the legacy "Quick animations" flag.
        Check(!d.LoadContentMods && d.ToJson()["loadContentMods"]?.Type == JTokenType.Boolean && !(bool)d.ToJson()["loadContentMods"], "mods: content mods are off by default and saved explicitly");
        Check(OriginalPlayerSettings.LoadOrMigrate(new OriginalPlayerSettings { LoadContentMods = true }.ToJson().ToString(), Prefs(0, 0, 0), out _).LoadContentMods, "mods: the toggle survives save and load");
        Check(!OriginalPlayerSettings.LoadOrMigrate(null, Prefs(1, 1, 1), out _).LoadContentMods && !OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":1,\"loadContentMods\":\"yes\"}"), out var modNotes).LoadContentMods && modNotes.Count == 1, "mods: migration and bad values keep mods off");
        Check(!d.QuickAnimations && legacy.QuickAnimations && new OriginalPlayerSettings { InstantAnimations = true }.QuickAnimations && !new OriginalPlayerSettings { AnimationSpeed = 1.5 }.QuickAnimations, "quick animations: legacy flag is instant or speed 2");

        // Key bindings and conflicts.
        var keys = new OriginalPlayerSettings();
        Check(!keys.TryBind("mapTop", "pageup", out var conflict) && conflict == "mapScrollUp" && keys.KeyBindings["mapTop"] == "Home", "keys: binding a used key (case-insensitive) is refused and names the holder");
        Check(keys.TryBind("mapTop", "Home", out _) && keys.TryBind("mapTop", " F1 ", out _) && keys.KeyBindings["mapTop"] == "F1", "keys: rebinding to own or free key works, whitespace trimmed");
        keys.BindSwapping("mapBottom", "PageUp");
        Check(keys.KeyBindings["mapBottom"] == "PageUp" && keys.KeyBindings["mapScrollUp"] == "End" && keys.KeyConflicts().Count == 0, "keys: swap binding hands the old key to the displaced action");
        var clash = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":1,\"keyBindings\":{\"mapTop\":\"PageDown\",\"futureAction\":\"Q\",\"mapBottom\":\"\"}}"), out var clashNotes);
        var found = clash.KeyConflicts();
        Check(found.Count == 1 && found[0].Key == "PageDown" && found[0].Actions.SequenceEqual(new[] { "mapScrollDown", "mapTop" }), "keys: conflicts in saved bindings are detected");
        Check(clash.KeyBindings["futureAction"] == "Q" && clash.KeyBindings["mapBottom"] == "End" && clashNotes.Count == 2, "keys: unknown actions kept, empty keys keep defaults, both noted");
        keys.ResetKeyBindings();
        Check(keys.KeyBindings.OrderBy(p => p.Key).SequenceEqual(OriginalPlayerSettings.DefaultKeyBindings.OrderBy(p => p.Key)), "keys: reset restores defaults");
        Check(OriginalPlayerSettings.FindConflicts(new Dictionary<string, string> { ["a"] = "X", ["b"] = "x", ["c"] = "Y" }).Single().Actions.Length == 2, "keys: static conflict finder works on any map");
    }
}
