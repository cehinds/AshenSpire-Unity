// OptionsChecks.cs — OriginalPlayerSettings schema 3 (US-15.1 display, US-15.2 gameplay, US-15.3 gamepad):
// HTML reference defaults/allowed values, 2 → 3 migration, round trip, validation, gamepad conflicts and resolver.
using System.Text.RegularExpressions;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

static class OptionsChecks
{
    public static void Run(string root, Action<bool, string> Check)
    {
        Func<string, int, int> NoPrefs = (key, fallback) => fallback;
        var d = new OriginalPlayerSettings();

        // ---- Defaults and allowed values match the HTML reference ---------------------------------
        var settingsJs = File.ReadAllText(Path.Combine(root, "src/ui/screens/settings.js"));
        string Row(string key)
        {
            var at = settingsJs.IndexOf("key: '" + key + "'", StringComparison.Ordinal);
            if (at < 0) throw new Exception("FAIL: settings.js has no row " + key);
            var end = settingsJs.IndexOf("label:", at, StringComparison.Ordinal);
            return settingsJs.Substring(at, end - at);
        }
        string Def(string key) => Regex.Match(Row(key), @"def: '?(?<v>[\w]+)'?").Groups["v"].Value;
        string[] Choices(string key) => Regex.Matches(Regex.Match(Row(key), @"choices: \[(?<v>[^\]]*)\]").Groups["v"].Value, @"'(?<c>[^']*)'").Select(m => m.Groups["c"].Value).ToArray();
        Check(Def("accent") == d.Accent && Choices("accent").SequenceEqual(OriginalPlayerSettings.Accents), "html: accent color default gold and its five choices");
        Check(Def("uiScale") == d.UiSize && Choices("uiScale").SequenceEqual(OriginalPlayerSettings.UiSizes), "html: UI size default Auto and S–XL choices");
        Check(Def("cardMotifStrength") == d.CardMotifStrength && Choices("cardMotifStrength").SequenceEqual(OriginalPlayerSettings.CardMotifStrengths), "html: motif strength default normal and its choices");
        Check(Def("mapHeaderDensity") == d.MapHeaderDensity && Choices("mapHeaderDensity").SequenceEqual(OriginalPlayerSettings.MapHeaderDensities), "html: map header default comfortable and its choices");
        Check(Def("mapHeaderRelics") == "true" && d.MapHeaderRelics && Def("mapHeaderSeed") == "true" && d.MapHeaderSeed && Def("controlHints") == "true" && d.ControlHints, "html: relics/seed in map header and control hints default on");
        Check(Def("fullscreen") == "false" && !d.Fullscreen, "html: fullscreen defaults off");
        Check(Def("shopSell") == "true" && d.ShopSell, "html: merchant buys back defaults on");
        Check(Def("animSpeed") == d.CombatPacing && Choices("animSpeed").SequenceEqual(OriginalPlayerSettings.CombatPacings), "html: combat pacing default normal and slow/normal/fast/instant");
        var content = JObject.Parse(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/content.json")));
        var ui = (JObject)content["balance"]["ui"]; var equipment = (JObject)content["balance"]["equipment"];
        Check((string)ui["rewardCollect"]["def"] == d.RewardCollect && ui["rewardCollect"]["modes"].Values<string>().SequenceEqual(OriginalPlayerSettings.RewardCollectModes), "content: reward collection default auto and auto/manual modes");
        Check((string)equipment["swapCostRule"] == d.SwapCostRule && equipment["swapCostRules"].Select(r => (string)r["id"]).SequenceEqual(OriginalPlayerSettings.SwapCostRuleIds), "content: weapon swap cost default flat and flat/gear/category rules");
        Check((string)ui["cardMotif"] == d.CardMotif && ui["cardMotifModes"].Values<string>().SequenceEqual(OriginalPlayerSettings.CardMotifs), "content: card motif default wash and off/wash/accent/band");
        Check(settingsJs.Contains("def: UI_DEFAULTS.rewardCollect.def") && settingsJs.Contains("def: EQ_DEFAULTS.swapCostRule") && settingsJs.Contains("def: UI_DEFAULTS.cardMotif"), "html: those three rows derive from the same balance tables");

        // ---- Migration 2 → 3 ---------------------------------------------------------------------
        const string v2 = "{\"schemaVersion\":2,\"textScale\":1.2,\"uiScale\":1.1,\"animationSpeed\":0.75,\"reducedMotion\":true,\"reduceFlashes\":true,\"highContrast\":true,\"colorblindPalette\":\"deuteranopia\",\"audio\":{\"master\":0.5,\"music\":0.4,\"sfx\":0.3,\"ui\":0.2,\"muted\":false,\"musicEnabled\":false},\"keyBindings\":{\"mapTop\":\"F1\"},\"loadContentMods\":true}";
        var fromV2 = OriginalPlayerSettings.LoadOrMigrate(v2, NoPrefs, out var v2Notes);
        Check(v2Notes.SequenceEqual(new[] { "migrated schema 2 to schema 3" }), "migration v2 → v3: one note, nothing clamped");
        Check(fromV2.TextScale == 1.2 && fromV2.UiScale == 1.1 && fromV2.AnimationSpeed == .75 && fromV2.ReducedMotion && fromV2.ReduceFlashes && fromV2.HighContrast && fromV2.ColorblindPalette == ColorblindPalette.Deuteranopia
            && fromV2.MasterVolume == .5 && !fromV2.MusicEnabled && fromV2.KeyBindings["mapTop"] == "F1" && fromV2.LoadContentMods, "migration v2 → v3: every schema-2 field is kept");
        var freshJson = d.ToJson();
        var v3Keys = new[] { "rewardCollect", "shopSell", "swapCostRule", "fullscreen", "uiSize", "accent", "cardMotif", "cardMotifStrength", "mapHeaderDensity", "mapHeaderRelics", "mapHeaderSeed", "controlHints", "gamepadBindings" };
        Check(v3Keys.All(k => JToken.DeepEquals(fromV2.ToJson()[k], freshJson[k])), "migration v2 → v3: every new field takes its default");
        var resaved = OriginalPlayerSettings.LoadOrMigrate(fromV2.ToJson().ToString(), NoPrefs, out var resavedNotes);
        Check((int)fromV2.ToJson()["schemaVersion"] == 3 && JToken.DeepEquals(resaved.ToJson(), fromV2.ToJson()) && resavedNotes.Count == 0, "migration v2 → v3: saving writes schema 3, which loads without notes");
        Check(v3Keys.All(k => freshJson[k] != null), "defaults: every schema-3 field is saved explicitly");

        // ---- Round trip and validation ------------------------------------------------------------
        var custom = new OriginalPlayerSettings { RewardCollect = "manual", ShopSell = false, SwapCostRule = "category", Fullscreen = true, UiSize = "XL", Accent = "violet", CardMotif = "band", CardMotifStrength = "strong", MapHeaderDensity = "compact", MapHeaderRelics = false, MapHeaderSeed = false, ControlHints = false };
        custom.BindGamepadSwapping("endTurn", "south");
        var back = OriginalPlayerSettings.LoadOrMigrate(custom.ToJson().ToString(), NoPrefs, out var backNotes);
        Check(JToken.DeepEquals(back.ToJson(), custom.ToJson()) && backNotes.Count == 0 && back.RewardCollect == "manual" && back.SwapCostRule == "category" && !back.ShopSell && back.GamepadBindings["endTurn"] == "south", "roundtrip: gameplay, display and gamepad fields survive save and load");
        var bad = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":3,\"rewardCollect\":\"sometimes\",\"swapCostRule\":\"both\",\"accent\":7,\"uiSize\":\"xxl\",\"shopSell\":\"no\",\"controlHints\":0,\"cardMotif\":null}"), out var badNotes);
        Check(bad.RewardCollect == "auto" && bad.SwapCostRule == "flat" && bad.Accent == "gold" && bad.UiSize == "Auto" && bad.ShopSell && bad.ControlHints && bad.CardMotif == "wash", "validation: unknown choices and non-booleans fall back to defaults");
        Check(badNotes.Count == 7 && badNotes.Contains("swapCostRule 'both' is unknown; flat used") && badNotes.Contains("shopSell was not true/false; default used"), "validation: every fallback is noted");
        var cased = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":3,\"rewardCollect\":\" MANUAL \",\"uiSize\":\"xl\",\"swapCostRule\":\"Gear\"}"), out var casedNotes);
        Check(cased.RewardCollect == "manual" && cased.UiSize == "XL" && cased.SwapCostRule == "gear" && casedNotes.Count == 0 && (string)cased.ToJson()["uiSize"] == "XL", "validation: choices match case-insensitively and store canonically");

        // ---- Combat pacing is the speed slider's bucket -------------------------------------------
        var pace = new OriginalPlayerSettings();
        Check(pace.CombatPacing == "normal" && pace.CombatPacing == AshenSpire.Domain.FeelSettings.SpeedFor(pace.AnimationSpeed, pace.InstantAnimations), "pacing: default normal agrees with FeelSettings.SpeedFor");
        foreach (var name in OriginalPlayerSettings.CombatPacings)
        { pace.CombatPacing = name; Check(pace.CombatPacing == name && AshenSpire.Domain.FeelSettings.SpeedFor(pace.AnimationSpeed, pace.InstantAnimations) == name, "pacing: " + name + " sets a speed FeelSettings reads as " + name); }
        pace.CombatPacing = "fast"; Check(pace.QuickAnimations && pace.AnimationSpeed == 2 && !pace.InstantAnimations, "pacing: fast is the legacy Quick animations speed");
        foreach (var speed in new[] { .5, .99, 1, 1.5, 1.99, 2 }) { pace.InstantAnimations = false; pace.AnimationSpeed = speed; Check(pace.CombatPacing == AshenSpire.Domain.FeelSettings.SpeedFor(speed, false), "pacing: slider " + speed + " reads as " + pace.CombatPacing); }
        var threw = false; try { pace.CombatPacing = "ludicrous"; } catch (ArgumentException) { threw = true; }
        Check(threw, "pacing: unknown pacing is refused");

        // ---- Gamepad bindings ---------------------------------------------------------------------
        Check(d.GamepadBindings.Count == OriginalGamepad.DefaultBindings.Count && d.GamepadConflicts().Count == 0 && OriginalGamepad.DefaultBindings.Values.All(b => OriginalGamepad.Normalize(b) == b), "gamepad: defaults use known buttons without conflicts");
        var inputJs = File.ReadAllText(Path.Combine(root, "src/ui/input.js"));
        int DefBtn(string id) => int.Parse(Regex.Match(inputJs, @"\{ id: '" + id + @"'[^\n]*defBtn: (?<n>\d+)").Groups["n"].Value);
        Check(new[] { ("combatPlay", "confirm"), ("cancel", "cancel"), ("endTurn", "endTurn"), ("combatDeck", "deck"), ("menu", "menu"), ("flask1", "flask1"), ("flask2", "flask2"), ("flask3", "flask3") }
            .All(p => OriginalGamepad.StandardIndex(d.GamepadBindings[p.Item1]) == DefBtn(p.Item2)), "gamepad: defaults match the HTML input.js defBtn for the shared actions");
        var pad = new OriginalPlayerSettings();
        Check(!pad.TryBindGamepad("flask1", "SOUTH", out var holder) && holder == "combatPlay" && pad.GamepadBindings["flask1"] == "leftTrigger", "gamepad: a held button is refused (case-insensitive) and names the holder");
        Check(pad.TryBindGamepad("flask1", "select", out _) && pad.GamepadBindings["flask1"] == "select", "gamepad: a free button binds");
        pad.BindGamepadSwapping("endTurn", "south");
        Check(pad.GamepadBindings["endTurn"] == "south" && pad.GamepadBindings["combatPlay"] == "west" && pad.GamepadConflicts().Count == 0, "gamepad: swap hands the old button to the displaced action");
        var unknownThrew = false; try { pad.TryBindGamepad("endTurn", "turbo", out _); } catch (ArgumentException) { unknownThrew = true; }
        Check(unknownThrew, "gamepad: an unknown button is refused");
        var saved = OriginalPlayerSettings.FromJson(JObject.Parse("{\"schemaVersion\":3,\"gamepadBindings\":{\"endTurn\":\"south\",\"flask1\":\"turbo\",\"futureAction\":\"rightStick\",\"drawPile\":5}}"), out var padNotes);
        var clash = saved.GamepadConflicts();
        Check(clash.Count == 1 && clash[0].Key == "south" && clash[0].Actions.SequenceEqual(new[] { "combatPlay", "endTurn" }), "gamepad: conflicts in a saved record are kept and detected");
        Check(saved.GamepadBindings["flask1"] == "leftTrigger" && saved.GamepadBindings["futureAction"] == "rightStick" && !saved.GamepadBindings.ContainsKey("drawPile") && padNotes.Count == 3 && padNotes.Any(n => n.StartsWith("gamepad button south is bound to")), "gamepad: bad buttons keep defaults, unknown actions survive, all noted");
        pad.ResetGamepadBindings();
        Check(pad.GamepadBindings.OrderBy(p => p.Key).SequenceEqual(OriginalGamepad.DefaultBindings.OrderBy(p => p.Key)), "gamepad: reset restores defaults");

        // ---- Resolver -----------------------------------------------------------------------------
        var combat = new[] { "cancel", "menu", "combatPlay", "endTurn", "combatDeck", "flask1", "flask2", "flask3", "targetPrevious", "targetNext" };
        var map = new[] { "cancel", "menu", "mapScrollUp", "mapScrollDown", "mapTop", "mapBottom" };
        Check(OriginalGamepad.Action(null, "south", combat) == "combatPlay" && OriginalGamepad.Action(null, "east", combat) == "cancel" && OriginalGamepad.Action(null, "start", map) == "menu", "resolver: south/east/start resolve to play, cancel and menu");
        Check(OriginalGamepad.Action(null, "dpadUp", map) == "mapScrollUp" && OriginalGamepad.Action(null, "dpadUp", combat) == null && OriginalGamepad.Action(null, "leftShoulder", combat) == "targetPrevious" && OriginalGamepad.Action(null, "RightShoulder", combat) == "targetNext", "resolver: d-pad and shoulders resolve per context");
        Check(OriginalGamepad.Action(null, "dpadLeft", combat) == null && OriginalGamepad.Action(null, "turbo", combat) == null && OriginalGamepad.Action(null, null, combat) == null, "resolver: unbound and unknown buttons resolve to nothing");
        var rebound = new OriginalPlayerSettings(); rebound.BindGamepadSwapping("endTurn", "south");
        Check(OriginalGamepad.Action(rebound.GamepadBindings, "south", combat) == "endTurn" && OriginalGamepad.Action(rebound.GamepadBindings, "west", combat) == "combatPlay", "resolver: follows the player's bindings");
        var keys = new OriginalPlayerSettings(); keys.TryBind("endTurn", "Z", out _);
        Check(OriginalGamepad.KeyName("endTurn", keys.KeyBindings) == "Z" && OriginalGamepad.KeyName("combatPlay", null) == "Return" && OriginalGamepad.KeyName("cancel", null) == "Escape" && OriginalGamepad.KeyName("menu", null) == null, "resolver: a pad action dispatches as its bound keyboard key");
        Check(OriginalGamepad.Buttons.Select((b, i) => OriginalGamepad.StandardIndex(b) == i && OriginalGamepad.FromStandardIndex(i) == b).All(x => x) && OriginalGamepad.Buttons.Length == 16 && OriginalGamepad.FromStandardIndex(16) == null, "resolver: button ids follow the standard-gamepad index order");
        Check(OriginalGamepad.Buttons.All(b => OriginalGamepad.Label(b) != "unbound"), "gamepad: every button has a label");

        // ---- Gameplay options threading ------------------------------------------------------------
        var profile = OriginalGameplayOptions.ProfileSettings(custom);
        Check(JToken.DeepEquals(profile, JObject.Parse("{\"shopSell\":false,\"swapCostRule\":\"category\"}")) && JToken.DeepEquals(OriginalGameplayOptions.ProfileSettings(null), JObject.Parse("{\"shopSell\":true,\"swapCostRule\":\"flat\"}")), "gameplay: run-facing keys use the HTML meta.settings names");
        Check(OriginalGameplayOptions.RewardCollectMode(content, "manual") == "manual" && OriginalGameplayOptions.RewardCollectMode(content, "bogus") == "auto" && OriginalGameplayOptions.RewardCollectMode(content, null) == "auto" && OriginalGameplayOptions.RewardCollectMode(new JObject(), "manual") == "manual", "gameplay: reward mode resolves against the content dial, as reward.js collectMode");
    }
}
