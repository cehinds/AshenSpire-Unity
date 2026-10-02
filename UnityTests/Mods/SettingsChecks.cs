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
        Check(d.KeyBindings.Count == 24 && d.KeyBindings["mapScrollUp"] == "PageUp" && d.KeyBindings["mapBottom"] == "End" && d.KeyBindings["endTurn"] == "E" && d.KeyBindings["card9"] == "Alpha9" && d.KeyConflicts().Count == 0, "defaults: map and combat keys have no conflicts");
        var oldKeys = OriginalPlayerSettings.FromJson(JObject.Parse("{schemaVersion:1,keyBindings:{mapTop:'F1'}}"), out _);
        Check(oldKeys.KeyBindings["mapTop"] == "F1" && oldKeys.KeyBindings["combatPlay"] == "Return", "keys: old saves retain map changes and gain combat defaults");
        Check(oldKeys.TryBind("endTurn", "Z", out _) && OriginalPlayerSettings.LoadOrMigrate(oldKeys.ToJson().ToString(), Prefs(0,0,0), out _).KeyBindings["endTurn"] == "Z", "keys: combat rebinding persists without changing settings schema");
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
        Check(wildNotes.Count == 8 && wildNotes.Contains("migrated schema 1 to schema 2") && wildNotes.Any(n => n == "textScale 5 clamped to 1.6"), "clamp: every adjustment is reported");
        var low = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":1,\"textScale\":0.2,\"animationSpeed\":0.1}"), out _);
        Check(low.TextScale == .8 && low.AnimationSpeed == .5, "clamp: lower bounds 0.8 text and 0.5 speed");
        var odd = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":1,\"colorblindPalette\":\"sepia\",\"hitStop\":\"yes\",\"keyBindings\":[]}"), out var oddNotes);
        Check(odd.ColorblindPalette == ColorblindPalette.None && !odd.HitStop && odd.KeyBindings.Count == 24 && oddNotes.Count == 4, "bad values: unknown palette, non-bool and bad bindings fall back with notes");
        Check(OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":1,\"animationSpeed\":\"instant\"}"), out _).AnimationDurationScale == 0, "instant animations: shorthand gives zero duration");
        var future = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":9,\"textScale\":1.4,\"newThing\":1}"), out var futureNotes);
        Check(future.TextScale == 1.4 && futureNotes.Any(n => n.Contains("newer")), "newer schema: known fields read, version noted");
        Check(OriginalPlayerSettings.FromJson(new JArray(), out var arrayNotes).TextScale == 1 && arrayNotes.Count == 1, "non-object settings give defaults");

        // Round trip.
        var custom = new OriginalPlayerSettings { TextScale = 1.35, UiScale = 1.25, AnimationSpeed = .75, InstantAnimations = true, ReducedMotion = true, ScreenShake = true, ScreenShakeIntensity = .4, HitStop = true, ReduceFlashes = true, HighContrast = true, ColorblindPalette = ColorblindPalette.Tritanopia, MasterVolume = .8, MusicVolume = .3, SfxVolume = .6, UiVolume = .9, Muted = true, MusicEnabled = false };
        custom.BindSwapping("mapTop", "T"); custom.TryBind("openDeck", "D", out _);
        var text = custom.ToJson().ToString();
        var back = OriginalPlayerSettings.LoadOrMigrate(text, Prefs(0, 0, 0), out var backNotes);
        Check(JToken.DeepEquals(back.ToJson(), custom.ToJson()) && backNotes.Count == 0, "roundtrip: every field survives save and load without adjustments");
        Check((string)custom.ToJson()["colorblindPalette"] == "tritanopia" && (int)custom.ToJson()["schemaVersion"] == 2, "roundtrip: palette stored by name with schemaVersion 2");
        foreach (ColorblindPalette palette in Enum.GetValues(typeof(ColorblindPalette)))
            Check(OriginalPlayerSettings.TryParsePalette(OriginalPlayerSettings.PaletteName(palette), out var parsed) && parsed == palette, "palette " + palette + " round-trips by name");
        Check(JToken.DeepEquals(custom.Clone().ToJson(), custom.ToJson()), "clone is a deep equal copy");

        // Schema 2 (US-13.2 / US-8.3): reduce flashes, high contrast, music on/off.
        Check(OriginalPlayerSettings.SchemaVersion == 2 && OriginalPlayerSettings.StorageKey == "AshenSpire.Settings.v1", "schema 2 keeps the v1 storage key so existing saves are found");
        Check(!d.ReduceFlashes && !d.HighContrast && d.MusicEnabled && (bool)d.ToJson()["reduceFlashes"] == false && (bool)d.ToJson()["highContrast"] == false && (bool)d.ToJson()["audio"]["musicEnabled"], "defaults: reduce flashes and high contrast off, music on, all saved explicitly");
        const string v1 = "{\"schemaVersion\":1,\"textScale\":1.2,\"animationSpeed\":0.75,\"reducedMotion\":true,\"screenShake\":true,\"hitStop\":true,\"colorblindPalette\":\"protanopia\",\"audio\":{\"master\":0.5,\"music\":0.4,\"sfx\":0.3,\"ui\":0.2,\"muted\":true},\"keyBindings\":{\"mapTop\":\"F1\"},\"loadContentMods\":true}";
        var fromV1 = OriginalPlayerSettings.LoadOrMigrate(v1, Prefs(0, 0, 0), out var v1Notes);
        Check(!fromV1.ReduceFlashes && !fromV1.HighContrast && fromV1.MusicEnabled, "migration v1 → v2: new fields default to off, off and music on");
        Check(fromV1.TextScale == 1.2 && fromV1.AnimationSpeed == .75 && fromV1.ReducedMotion && fromV1.ScreenShake && fromV1.HitStop && fromV1.ColorblindPalette == ColorblindPalette.Protanopia
            && fromV1.MasterVolume == .5 && fromV1.MusicVolume == .4 && fromV1.SfxVolume == .3 && fromV1.UiVolume == .2 && fromV1.Muted && fromV1.KeyBindings["mapTop"] == "F1" && fromV1.LoadContentMods, "migration v1 → v2: every schema-1 field is kept");
        Check(v1Notes.SequenceEqual(new[] { "migrated schema 1 to schema 2" }), "migration v1 → v2: one note, nothing clamped");
        var resaved = OriginalPlayerSettings.LoadOrMigrate(fromV1.ToJson().ToString(), Prefs(0, 0, 0), out var resavedNotes);
        Check((int)fromV1.ToJson()["schemaVersion"] == 2 && JToken.DeepEquals(resaved.ToJson(), fromV1.ToJson()) && resavedNotes.Count == 0, "migration v1 → v2: saving writes schema 2, which then loads without notes");
        var a11y = new OriginalPlayerSettings { ReduceFlashes = true, HighContrast = true, MusicEnabled = false };
        var a11yBack = OriginalPlayerSettings.LoadOrMigrate(a11y.ToJson().ToString(), Prefs(0, 0, 0), out _);
        Check(a11yBack.ReduceFlashes && a11yBack.HighContrast && !a11yBack.MusicEnabled, "roundtrip: reduce flashes, high contrast and music off survive save and load");
        var badA11y = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":2,\"reduceFlashes\":1,\"highContrast\":\"on\",\"audio\":{\"musicEnabled\":null}}"), out var badA11yNotes);
        Check(!badA11y.ReduceFlashes && !badA11y.HighContrast && badA11y.MusicEnabled && badA11yNotes.Count == 3 && badA11yNotes.Contains("reduceFlashes was not true/false; default used") && badA11yNotes.Contains("highContrast was not true/false; default used"), "validation: non-boolean new fields fall back to their defaults with notes");
        Check(OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":3,\"reduceFlashes\":true}"), out var newerNotes).ReduceFlashes && newerNotes.Single().Contains("newer"), "newer schema: reduce flashes still read");
        Check(OriginalPlayerSettings.FromJson(JObject.Parse("{\"reducedMotion\":true,\"muted\":true}"), out _).HighContrast == false, "schema 0 JSON: high contrast stays off");
        // MusicPlayer's old optional PlayerPrefs keys migrate once (only when nothing is stored).
        Func<string, int, int> MusicPrefs(int master, int music, int enabled) => (key, fallback) =>
            key == OriginalPlayerSettings.LegacyMasterVolumeKey ? master : key == OriginalPlayerSettings.LegacyMusicVolumeKey ? music : key == OriginalPlayerSettings.LegacyMusicEnabledKey ? enabled : fallback;
        var musicPrefs = OriginalPlayerSettings.LoadOrMigrate(null, MusicPrefs(80, 40, 0), out _);
        Check(musicPrefs.MasterVolume == .8 && musicPrefs.MusicVolume == .4 && !musicPrefs.MusicEnabled, "migration: legacy music PlayerPrefs (0–100, 0/1) move into the record");
        Check(OriginalPlayerSettings.LoadOrMigrate(null, MusicPrefs(250, 100, 1), out _).MasterVolume == 1 && OriginalPlayerSettings.LoadOrMigrate(null, MusicPrefs(250, 100, 1), out _).MusicEnabled, "migration: legacy music levels above 100 clamp to 1");
        Check(OriginalPlayerSettings.LoadOrMigrate(a11y.ToJson().ToString(), MusicPrefs(10, 10, 1), out _).MasterVolume == 1 && !OriginalPlayerSettings.LoadOrMigrate(a11y.ToJson().ToString(), MusicPrefs(10, 10, 1), out _).MusicEnabled, "migration: a stored record ignores the legacy music keys afterwards");
        Check(new OriginalPlayerSettings { MusicEnabled = false }.Gain("music") == 0 && new OriginalPlayerSettings { MusicEnabled = false }.Gain("sfx") == 1, "music off silences only the music bus");

        // Content mods toggle and the legacy "Quick animations" flag.
        Check(!d.LoadContentMods && d.ToJson()["loadContentMods"]?.Type == JTokenType.Boolean && !(bool)d.ToJson()["loadContentMods"], "mods: content mods are off by default and saved explicitly");
        Check(OriginalPlayerSettings.LoadOrMigrate(new OriginalPlayerSettings { LoadContentMods = true }.ToJson().ToString(), Prefs(0, 0, 0), out _).LoadContentMods, "mods: the toggle survives save and load");
        Check(!OriginalPlayerSettings.LoadOrMigrate(null, Prefs(1, 1, 1), out _).LoadContentMods && !OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":1,\"loadContentMods\":\"yes\"}"), out var modNotes).LoadContentMods && modNotes.Count == 2, "mods: migration and bad values keep mods off");
        var modsOn = new OriginalPlayerSettings { LoadContentMods = true };
        Check(modsOn.ContentModsActive(coop: false) && !modsOn.ContentModsActive(coop: true) && !d.ContentModsActive(false) && !d.ContentModsActive(true), "mods: co-op resolution ignores the toggle; solo follows it");
        Check(!OriginalPlayerSettings.LoadOrMigrate(modsOn.ToJson().ToString(), Prefs(0, 0, 0), out _).ContentModsActive(coop: true), "mods: a saved 'on' toggle still gives base content in co-op");
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
        Check(clash.KeyBindings["futureAction"] == "Q" && clash.KeyBindings["mapBottom"] == "End" && clashNotes.Count == 3, "keys: unknown actions kept, empty keys keep defaults, both noted");
        keys.ResetKeyBindings();
        Check(keys.KeyBindings.OrderBy(p => p.Key).SequenceEqual(OriginalPlayerSettings.DefaultKeyBindings.OrderBy(p => p.Key)), "keys: reset restores defaults");
        Check(OriginalPlayerSettings.FindConflicts(new Dictionary<string, string> { ["a"] = "X", ["b"] = "x", ["c"] = "Y" }).Single().Actions.Length == 2, "keys: static conflict finder works on any map");
    }
}
