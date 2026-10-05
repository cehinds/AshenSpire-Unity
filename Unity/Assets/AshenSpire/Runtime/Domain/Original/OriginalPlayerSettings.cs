// OriginalPlayerSettings.cs — device-local customization, extending today's three
// PlayerPrefs flags (AshenSpire.ReducedMotion / FastMotion / Muted) into one versioned record.
// ENTRY POINT: OriginalPlayerSettings.LoadOrMigrate(stored, legacyInt). Store ToJson() under
// StorageKey. Defaults reproduce the current build: full-speed feedback, no shake or
// hit-stop (the Unity presentation has neither yet), unattenuated buses, unmuted.
// Reading never throws: bad or out-of-range values are clamped or defaulted and listed
// in Adjustments. RunController loads/saves it and keeps the legacy flags written;
// CampaignView.PlayerSettings.cs is the settings screen (needs an editor play test).
// Schema 3 adds the HTML gameplay options (rewardCollect, shopSell, swapCostRule; combat pacing
// is AnimationSpeed/InstantAnimations), display options (data side) and gamepad bindings.
// Allowed values and defaults are the HTML src/ui/screens/settings.js rows and balance tables.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public enum ColorblindPalette { None, Protanopia, Deuteranopia, Tritanopia }

    public sealed class OriginalPlayerSettings
    {
        /// <summary>2 added reduceFlashes, highContrast and audio.musicEnabled (absent in schema 1: off, off, on).
        /// 3 added the gameplay options (rewardCollect, shopSell, swapCostRule), the display options
        /// (fullscreen, uiSize, accent, cardMotif, cardMotifStrength, mapHeader*, controlHints) and
        /// gamepadBindings. A schema-1 or schema-2 record gets their defaults and a migration note.</summary>
        public const int SchemaVersion = 3;
        // A promotion identity, not the application build number: rebuilding the
        // player must not ask again when its defaults have not changed.
        public const string CurrentDefaultsRevision = "unity-schema3-manual-rewards";
        public string DefaultsChoiceRevision;
        /// <summary>The key keeps its v1 name so existing saves are found; schemaVersion inside the record is what changes.</summary>
        public const string StorageKey = "AshenSpire.Settings.v1";
        public const string LegacyReducedMotionKey = "AshenSpire.ReducedMotion", LegacyFastMotionKey = "AshenSpire.FastMotion", LegacyMutedKey = "AshenSpire.Muted";
        /// <summary>Music bus keys MusicPlayer used to read (0..100, 0/1). Nothing in the Unity build wrote them; they are
        /// migrated once by FromLegacy when no settings record exists and are not read again.</summary>
        public const string LegacyMasterVolumeKey = "AshenSpire.MasterVolume", LegacyMusicVolumeKey = "AshenSpire.MusicVolume", LegacyMusicEnabledKey = "AshenSpire.MusicEnabled";
        public const double TextScaleMin = .8, TextScaleMax = 1.6, UiScaleMin = .75, UiScaleMax = 1.5, AnimationSpeedMin = .5, AnimationSpeedMax = 2, IntensityMin = 0, IntensityMax = 1, VolumeMin = 0, VolumeMax = 1;
        /// <summary>Legacy "Quick animations" halved feedback duration (CombatFeedback: × .5), i.e. speed 2.</summary>
        public const double LegacyFastAnimationSpeed = 2;

        /// <summary>Default keys for map navigation and solo combat (Unity KeyCode names).</summary>
        public static readonly IReadOnlyDictionary<string, string> DefaultKeyBindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mapScrollUp"] = "PageUp", ["mapScrollDown"] = "PageDown", ["mapTop"] = "Home", ["mapBottom"] = "End",
            ["combatPlay"] = "Return", ["endTurn"] = "E", ["combatDeck"] = "D",
            ["drawPile"] = "U", ["discardPile"] = "J", ["exhaustPile"] = "K",
            ["flask1"] = "F", ["flask2"] = "G", ["flask3"] = "H",
            ["targetPrevious"] = "LeftArrow", ["targetNext"] = "RightArrow",
            ["card1"] = "Alpha1", ["card2"] = "Alpha2", ["card3"] = "Alpha3",
            ["card4"] = "Alpha4", ["card5"] = "Alpha5", ["card6"] = "Alpha6",
            ["card7"] = "Alpha7", ["card8"] = "Alpha8", ["card9"] = "Alpha9",
        };

        // ---- Schema 3: closed sets and defaults, copied from the HTML reference --------------
        /// <summary>HTML animSpeed ("Combat pacing", fx.js ANIM_SPEEDS). Stored as AnimationSpeed/InstantAnimations.</summary>
        public static readonly string[] CombatPacings = { "slow", "normal", "fast", "instant" };
        /// <summary>balance.ui.rewardCollect.modes. The default is NOT the content def ("auto"): owner decision
        /// 2026-10-02, after a fight only cinders are collected automatically; every other reward is taken or
        /// skipped by the player. A record without rewardCollect (schema 1/2, or a schema-3 record missing the
        /// field) gets "manual"; a stored "auto" is an explicit choice and is kept.</summary>
        public static readonly string[] RewardCollectModes = { "auto", "manual" };
        public const string DefaultRewardCollect = "manual";
        /// <summary>balance.equipment.swapCostRules ids / balance.equipment.swapCostRule.</summary>
        public static readonly string[] SwapCostRuleIds = { "flat", "gear", "category" };
        public const string DefaultSwapCostRule = "flat";
        /// <summary>HTML uiScale chips. Unity's numeric uiScale slider keeps its own field; this is the HTML choice.</summary>
        public static readonly string[] UiSizes = { "Auto", "S", "M", "L", "XL" };
        public static readonly string[] Accents = { "gold", "crimson", "frost", "verdant", "violet" };
        /// <summary>balance.ui.cardMotifModes / balance.ui.cardMotif.</summary>
        public static readonly string[] CardMotifs = { "off", "wash", "accent", "band" };
        public const string DefaultCardMotif = "wash";
        public static readonly string[] CardMotifStrengths = { "subtle", "normal", "strong" };
        public static readonly string[] MapHeaderDensities = { "comfortable", "compact" };

        public double TextScale = 1, UiScale = 1, AnimationSpeed = 1;
        /// <summary>Skip feedback timelines entirely; AnimationSpeed is kept for when it is turned off.</summary>
        public bool InstantAnimations;
        public bool ReducedMotion;
        public bool ScreenShake; public double ScreenShakeIntensity = 1;
        public bool HitStop;
        /// <summary>Suppress bright impact flashes (photosensitivity); damage numbers stay (HTML reduceFlashes).</summary>
        public bool ReduceFlashes;
        /// <summary>Brighter text and stronger borders: the root gets the high-contrast USS class (HTML highContrast).</summary>
        public bool HighContrast;
        public bool HoldToConfirm;
        public bool AutoCollectRewards { get => RewardCollect == "auto"; set => RewardCollect = value ? "auto" : "manual"; }
        public bool CompactMapHeader { get => MapHeaderDensity == "compact"; set => MapHeaderDensity = value ? "compact" : "comfortable"; }
        public bool MerchantBuyBack { get => ShopSell; set => ShopSell = value; }
        public int MinimumTapSize = 44;
        public static readonly IReadOnlyDictionary<string, int> DefaultControllerBindings = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["submit"] = 0, ["cancel"] = 1, ["deck"] = 2, ["endTurn"] = 3,
            ["previous"] = 4, ["next"] = 5
        };
        private readonly Dictionary<string, int> _controller = new Dictionary<string, int>(DefaultControllerBindings, StringComparer.Ordinal);
        public IReadOnlyDictionary<string, int> ControllerBindings => _controller;
        public bool TryBindController(string action, int button, out string conflict)
        {
            conflict = null;
            if (!DefaultControllerBindings.ContainsKey(action) || button < 0 || button > 15) return false;
            conflict = _controller.FirstOrDefault(p => p.Key != action && p.Value == button).Key;
            if (conflict != null) return false;
            _controller[action] = button; return true;
        }
        public void ResetControllerBindings() { _controller.Clear(); foreach (var pair in DefaultControllerBindings) _controller[pair.Key] = pair.Value; }
        public ColorblindPalette ColorblindPalette = ColorblindPalette.None;
        public double MasterVolume = 1, MusicVolume = 1, SfxVolume = 1, UiVolume = 1;
        public bool Muted;
        /// <summary>Music on/off, separate from Mute (which silences every bus).</summary>
        public bool MusicEnabled = true;
        /// <summary>Read StreamingAssets/Mods when content loads. Off by default: the shipped content only.</summary>
        public bool LoadContentMods;

        // Gameplay (US-15.2). Read live by the run: OriginalGameplayOptions.ProfileSettings copies
        // shopSell and swapCostRule into run.profileMeta.settings; rewardCollect is passed to Continue.
        /// <summary>auto: Continue takes every pending, un-skipped reward (a card is picked); manual (default): Continue takes
        /// the pending, un-skipped cinders and otherwise only what was chosen.</summary>
        public string RewardCollect = DefaultRewardCollect;
        /// <summary>HTML shopSell ("Merchant buys back"): the merchant's Sell rows. Off removes them.</summary>
        public bool ShopSell = true;
        /// <summary>HTML swapCostRule ("Weapon swap cost"): which balance.equipment.swapCostRules row prices a swap.</summary>
        public string SwapCostRule = DefaultSwapCostRule;
        // Display (US-15.1, data side). Applied as root USS classes; visual tuning is editor follow-up.
        public bool Fullscreen;
        public string UiSize = "Auto", Accent = "gold", CardMotif = DefaultCardMotif, CardMotifStrength = "normal", MapHeaderDensity = "comfortable";
        public bool MapHeaderRelics = true, MapHeaderSeed = true, ControlHints = true;
        private readonly Dictionary<string, string> _pad = new Dictionary<string, string>(OriginalGamepad.DefaultBindings, StringComparer.Ordinal);
        /// <summary>Action → gamepad button id (OriginalGamepad.Buttons). Resolve presses with OriginalGamepad.Action.</summary>
        public IReadOnlyDictionary<string, string> GamepadBindings => _pad;

        /// <summary>HTML "Combat pacing": the FeelSettings.SpeedFor bucket of AnimationSpeed, or instant.
        /// Setting it picks the bucket's representative speed (slow .5, normal 1, fast 2).</summary>
        public string CombatPacing
        {
            get => InstantAnimations ? "instant" : AnimationSpeed >= LegacyFastAnimationSpeed ? "fast" : AnimationSpeed >= 1 ? "normal" : "slow";
            set
            {
                switch (value)
                {
                    case "instant": InstantAnimations = true; break;
                    case "slow": InstantAnimations = false; AnimationSpeed = AnimationSpeedMin; break;
                    case "normal": InstantAnimations = false; AnimationSpeed = 1; break;
                    case "fast": InstantAnimations = false; AnimationSpeed = LegacyFastAnimationSpeed; break;
                    default: throw new ArgumentException("Unknown combat pacing: " + value);
                }
            }
        }
        /// <summary>Persist defaults before replacing live settings. A failed commit restores
        /// the previous preferences through the same settings-only persistence boundary.</summary>
        public OriginalPlayerSettings ResetSaved(Action<OriginalPlayerSettings> persist)
        {
            return ChooseDefaultsSaved(true, persist);
        }
        public bool NeedsDefaultsChoice
        {
            get
            {
                if (DefaultsChoiceRevision == CurrentDefaultsRevision) return false;
                var local = ToJson(); var defaults = new OriginalPlayerSettings().ToJson();
                local.Remove("defaultsChoiceRevision"); defaults.Remove("defaultsChoiceRevision");
                return !SamePreferences(local, defaults);
            }
        }
        private static bool SamePreferences(JToken left, JToken right)
        {
            bool Number(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;
            if (Number(left) && Number(right)) return Math.Abs((double)left - (double)right) < 1e-9;
            if (left is JObject a && right is JObject b)
                return a.Count == b.Count && a.Properties().All(p => b[p.Name] != null && SamePreferences(p.Value, b[p.Name]));
            return JToken.DeepEquals(left, right);
        }
        /// <summary>Preferences and acknowledgement share one saved record. Live
        /// settings change only after persistence succeeds; refusal restores the
        /// previous record and leaves the decision available for retry.</summary>
        public OriginalPlayerSettings ChooseDefaultsSaved(bool useDefaults, Action<OriginalPlayerSettings> persist)
        {
            if (persist == null) throw new ArgumentNullException(nameof(persist));
            var candidate = useDefaults ? new OriginalPlayerSettings() : Clone();
            candidate.DefaultsChoiceRevision = CurrentDefaultsRevision;
            try { persist(candidate); }
            catch (Exception failure)
            {
                try { persist(this); }
                catch (Exception rollback) { throw new AggregateException("Settings reset and rollback could not be saved.", failure, rollback); }
                throw;
            }
            return candidate;
        }
        /// <summary>Whether packs apply to this content load. Co-op (host, guest, companion) always uses the
        /// shipped content so every seat runs the same rules; the toggle only affects solo play.</summary>
        public bool ContentModsActive(bool coop) => LoadContentMods && !coop;
        private readonly Dictionary<string, string> _keys = new Dictionary<string, string>(DefaultKeyBindings, StringComparer.Ordinal);
        public IReadOnlyDictionary<string, string> KeyBindings => _keys;

        /// <summary>The legacy "Quick animations" flag (AshenSpire.FastMotion): instant or at least the legacy fast speed.</summary>
        public bool QuickAnimations => InstantAnimations || AnimationSpeed >= LegacyFastAnimationSpeed;
        /// <summary>Multiplier for feedback durations: 0 when instant, 1 / speed otherwise (legacy fast = .5).</summary>
        public double AnimationDurationScale => InstantAnimations ? 0 : 1 / AnimationSpeed;
        /// <summary>Effective gain for a bus after master volume and mute.</summary>
        public double Gain(string bus)
        {
            if (Muted) return 0;
            switch (bus) { case "music": return MusicEnabled ? MasterVolume * MusicVolume : 0; case "sfx": return MasterVolume * SfxVolume; case "ui": return MasterVolume * UiVolume; case "master": return MasterVolume; default: throw new ArgumentException("Unknown audio bus: " + bus); }
        }

        /// <summary>Binds <paramref name="key"/> to <paramref name="action"/> unless another action already uses it.
        /// Returns false and names the conflicting action instead of silently stealing the key.</summary>
        public bool TryBind(string action, string key, out string conflictingAction)
        {
            conflictingAction = null; key = NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("An action name is required.");
            if (key == null) throw new ArgumentException("A key name is required.");
            conflictingAction = _keys.Where(p => p.Key != action && string.Equals(p.Value, key, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
            if (conflictingAction != null) return false;
            _keys[action] = key; return true;
        }
        /// <summary>Binds and, if needed, gives the previous key of <paramref name="action"/> to the action that held <paramref name="key"/>.</summary>
        public void BindSwapping(string action, string key)
        {
            if (TryBind(action, key, out var other)) return;
            var previous = _keys.TryGetValue(action, out var old) ? old : null;
            if (previous == null) _keys.Remove(other); else _keys[other] = previous;
            _keys[action] = NormalizeKey(key);
        }
        public void ResetKeyBindings() { _keys.Clear(); foreach (var pair in DefaultKeyBindings) _keys[pair.Key] = pair.Value; }
        /// <summary>Every key bound to more than one action, with those actions (sorted, case-insensitive keys).</summary>
        public IReadOnlyList<(string Key, string[] Actions)> KeyConflicts() => FindConflicts(_keys);
        public static IReadOnlyList<(string Key, string[] Actions)> FindConflicts(IEnumerable<KeyValuePair<string, string>> bindings) =>
            bindings.Where(p => NormalizeKey(p.Value) != null).GroupBy(p => NormalizeKey(p.Value), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
                .Select(g => (g.Key, g.Select(p => p.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray())).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        private static string NormalizeKey(string key) => string.IsNullOrWhiteSpace(key) ? null : key.Trim();

        /// <summary>Binds a gamepad button unless another action holds it (same rule as TryBind). Unknown buttons throw.</summary>
        public bool TryBindGamepad(string action, string button, out string conflictingAction)
        {
            conflictingAction = null;
            if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("An action name is required.");
            var id = OriginalGamepad.Normalize(button) ?? throw new ArgumentException("Unknown gamepad button: " + button);
            conflictingAction = _pad.Where(p => p.Key != action && p.Value == id).Select(p => p.Key).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
            if (conflictingAction != null) return false;
            _pad[action] = id; return true;
        }
        /// <summary>Binds and gives the previous button of <paramref name="action"/> to the action that held <paramref name="button"/>.</summary>
        public void BindGamepadSwapping(string action, string button)
        {
            if (TryBindGamepad(action, button, out var other)) return;
            if (_pad.TryGetValue(action, out var old)) _pad[other] = old; else _pad.Remove(other);
            _pad[action] = OriginalGamepad.Normalize(button);
        }
        public void ResetGamepadBindings() { _pad.Clear(); foreach (var pair in OriginalGamepad.DefaultBindings) _pad[pair.Key] = pair.Value; }
        /// <summary>Every gamepad button bound to more than one action, with those actions.</summary>
        public IReadOnlyList<(string Key, string[] Actions)> GamepadConflicts() => FindConflicts(_pad);

        public JObject ToJson()
        {
            var keys = new JObject(); foreach (var pair in _keys.OrderBy(p => p.Key, StringComparer.Ordinal)) keys[pair.Key] = pair.Value;
            var pad = new JObject(); foreach (var pair in _pad.OrderBy(p => p.Key, StringComparer.Ordinal)) pad[pair.Key] = pair.Value;
            var controller = new JObject(); foreach (var pair in _controller) controller[pair.Key] = pair.Value;
            return new JObject
            {
                ["schemaVersion"] = SchemaVersion, ["textScale"] = TextScale, ["uiScale"] = UiScale,
                ["defaultsChoiceRevision"] = DefaultsChoiceRevision,
                ["animationSpeed"] = AnimationSpeed, ["instantAnimations"] = InstantAnimations, ["reducedMotion"] = ReducedMotion,
                ["screenShake"] = ScreenShake, ["screenShakeIntensity"] = ScreenShakeIntensity, ["hitStop"] = HitStop,
                ["reduceFlashes"] = ReduceFlashes, ["highContrast"] = HighContrast,
                ["colorblindPalette"] = PaletteName(ColorblindPalette),
                ["audio"] = new JObject { ["master"] = MasterVolume, ["music"] = MusicVolume, ["sfx"] = SfxVolume, ["ui"] = UiVolume, ["muted"] = Muted, ["musicEnabled"] = MusicEnabled },
                ["keyBindings"] = keys, ["loadContentMods"] = LoadContentMods,
                ["rewardCollect"] = RewardCollect, ["shopSell"] = ShopSell, ["swapCostRule"] = SwapCostRule,
                ["fullscreen"] = Fullscreen, ["uiSize"] = UiSize, ["accent"] = Accent, ["cardMotif"] = CardMotif, ["cardMotifStrength"] = CardMotifStrength,
                ["mapHeaderDensity"] = MapHeaderDensity, ["mapHeaderRelics"] = MapHeaderRelics, ["mapHeaderSeed"] = MapHeaderSeed, ["controlHints"] = ControlHints,
                ["gamepadBindings"] = pad,
                ["holdToConfirm"] = HoldToConfirm, ["minimumTapSize"] = MinimumTapSize, ["controllerBindings"] = controller,
            };
        }
        public OriginalPlayerSettings Clone() => FromJson(ToJson(), out _);

        /// <summary>
        /// Application entry point. <paramref name="stored"/> is the text under StorageKey (null/empty when never saved);
        /// <paramref name="legacyInt"/> reads the old PlayerPrefs ints (key, fallback) and is used only when nothing is stored.
        /// </summary>
        public static OriginalPlayerSettings LoadOrMigrate(string stored, Func<string, int, int> legacyInt, out List<string> adjustments)
        {
            if (!string.IsNullOrWhiteSpace(stored))
            {
                JToken json = null;
                try { json = JToken.Parse(stored); } catch (Newtonsoft.Json.JsonException) { }
                if (json is JObject) return FromJson(json, out adjustments);
                var fallback = FromLegacy(legacyInt, out adjustments); adjustments.Insert(0, "stored settings were unreadable; rebuilt from legacy preferences"); return fallback;
            }
            return FromLegacy(legacyInt, out adjustments);
        }
        /// <summary>Migrates the pre-v1 PlayerPrefs flags. Missing keys keep today's defaults.</summary>
        public static OriginalPlayerSettings FromLegacy(Func<string, int, int> legacyInt, out List<string> adjustments)
        {
            adjustments = new List<string>(); var settings = new OriginalPlayerSettings();
            if (legacyInt == null) return settings;
            settings.ReducedMotion = legacyInt(LegacyReducedMotionKey, 0) == 1;
            if (legacyInt(LegacyFastMotionKey, 0) == 1) settings.AnimationSpeed = LegacyFastAnimationSpeed;
            settings.Muted = legacyInt(LegacyMutedKey, 0) == 1;
            // MusicPlayer's old optional keys (0..100). Absent (-1) keeps the defaults; values are clamped.
            var master = legacyInt(LegacyMasterVolumeKey, -1); if (master >= 0) settings.MasterVolume = Math.Min(100, master) / 100.0;
            var music = legacyInt(LegacyMusicVolumeKey, -1); if (music >= 0) settings.MusicVolume = Math.Min(100, music) / 100.0;
            var enabled = legacyInt(LegacyMusicEnabledKey, -1); if (enabled >= 0) settings.MusicEnabled = enabled != 0;
            adjustments.Add("migrated legacy preferences to schema " + SchemaVersion);
            return settings;
        }
        /// <summary>Reads any known version. Schema 0 (no schemaVersion) is the legacy flag object
        /// {"AshenSpire.ReducedMotion":1,...} or {"reducedMotion":true,"fastMotion":true,"muted":true}.</summary>
        public static OriginalPlayerSettings FromJson(JToken token, out List<string> adjustments)
        {
            var notes = new List<string>(); adjustments = notes; var s = new OriginalPlayerSettings();
            if (!(token is JObject json)) { if (token != null && token.Type != JTokenType.Null) notes.Add("settings were not an object; defaults used"); return s; }
            var version = json["schemaVersion"];
            if (version == null)
            {
                bool Flag(string web, string prefs) => json[web]?.Type == JTokenType.Boolean ? (bool)json[web] : json[prefs]?.Type == JTokenType.Integer && (long)json[prefs] == 1;
                s.ReducedMotion = Flag("reducedMotion", LegacyReducedMotionKey);
                if (Flag("fastMotion", LegacyFastMotionKey)) s.AnimationSpeed = LegacyFastAnimationSpeed;
                s.Muted = Flag("muted", LegacyMutedKey);
                notes.Add("migrated schema 0 to schema " + SchemaVersion); return s;
            }
            if (version.Type != JTokenType.Integer || (long)version < 1) { notes.Add("unknown schemaVersion " + version + "; defaults used"); return s; }
            if ((long)version > SchemaVersion) notes.Add("schemaVersion " + version + " is newer than " + SchemaVersion + "; known fields read");
            else if ((long)version < SchemaVersion) notes.Add("migrated schema " + version + " to schema " + SchemaVersion); // 1 → 2 → 3: new fields keep their defaults
            if (json["defaultsChoiceRevision"]?.Type == JTokenType.String)
                s.DefaultsChoiceRevision = (string)json["defaultsChoiceRevision"];
            double Number(JToken value, string name, double fallback, double min, double max)
            {
                if (value == null) return fallback;
                if ((value.Type != JTokenType.Integer && value.Type != JTokenType.Float) || double.IsNaN((double)value) || double.IsInfinity((double)value)) { notes.Add(name + " was not a number; default used"); return fallback; }
                var raw = (double)value; var clamped = Math.Min(max, Math.Max(min, raw));
                if (clamped != raw) notes.Add(name + " " + raw.ToString(CultureInfo.InvariantCulture) + " clamped to " + clamped.ToString(CultureInfo.InvariantCulture));
                return clamped;
            }
            bool Bool(JToken value, string name, bool fallback)
            {
                if (value == null) return fallback;
                if (value.Type == JTokenType.Boolean) return (bool)value;
                notes.Add(name + " was not true/false; default used"); return fallback;
            }
            s.TextScale = Number(json["textScale"], "textScale", s.TextScale, TextScaleMin, TextScaleMax);
            s.UiScale = Number(json["uiScale"], "uiScale", s.UiScale, UiScaleMin, UiScaleMax);
            var speed = json["animationSpeed"];
            if (speed?.Type == JTokenType.String && (string)speed == "instant") s.InstantAnimations = true; // accepted shorthand
            else s.AnimationSpeed = Number(speed, "animationSpeed", s.AnimationSpeed, AnimationSpeedMin, AnimationSpeedMax);
            s.InstantAnimations = Bool(json["instantAnimations"], "instantAnimations", s.InstantAnimations);
            s.ReducedMotion = Bool(json["reducedMotion"], "reducedMotion", s.ReducedMotion);
            s.ScreenShake = Bool(json["screenShake"], "screenShake", s.ScreenShake);
            s.ScreenShakeIntensity = Number(json["screenShakeIntensity"], "screenShakeIntensity", s.ScreenShakeIntensity, IntensityMin, IntensityMax);
            s.HitStop = Bool(json["hitStop"], "hitStop", s.HitStop);
            s.ReduceFlashes = Bool(json["reduceFlashes"], "reduceFlashes", s.ReduceFlashes);
            s.HighContrast = Bool(json["highContrast"], "highContrast", s.HighContrast);
            s.HoldToConfirm = Bool(json["holdToConfirm"], "holdToConfirm", false);
            s.MinimumTapSize = (int)Number(json["minimumTapSize"], "minimumTapSize", 44, 44, 64);
            if (json["controllerBindings"] is JObject controller)
            {
                var proposed = new Dictionary<string, int>(DefaultControllerBindings, StringComparer.Ordinal);
                foreach (var pair in controller.Properties())
                {
                    if (!proposed.ContainsKey(pair.Name)) continue;
                    if (pair.Value.Type == JTokenType.Integer && (long)pair.Value >= 0 && (long)pair.Value <= 15) proposed[pair.Name] = (int)pair.Value;
                    else notes.Add("controllerBindings." + pair.Name + " was invalid; default kept");
                }
                if (proposed.Values.Distinct().Count() == proposed.Count) { s._controller.Clear(); foreach (var pair in proposed) s._controller[pair.Key] = pair.Value; }
                else notes.Add("controllerBindings had conflicts; defaults used");
            }
            s.LoadContentMods = Bool(json["loadContentMods"], "loadContentMods", s.LoadContentMods);
            var palette = json["colorblindPalette"];
            if (palette != null) { if (palette.Type == JTokenType.String && TryParsePalette((string)palette, out var parsed)) s.ColorblindPalette = parsed; else notes.Add("colorblindPalette '" + palette + "' is unknown; none used"); }
            if (json["audio"] is JObject audio)
            {
                s.MasterVolume = Number(audio["master"], "audio.master", 1, VolumeMin, VolumeMax); s.MusicVolume = Number(audio["music"], "audio.music", 1, VolumeMin, VolumeMax);
                s.SfxVolume = Number(audio["sfx"], "audio.sfx", 1, VolumeMin, VolumeMax); s.UiVolume = Number(audio["ui"], "audio.ui", 1, VolumeMin, VolumeMax);
                s.Muted = Bool(audio["muted"], "audio.muted", false);
                s.MusicEnabled = Bool(audio["musicEnabled"], "audio.musicEnabled", true);
            }
            else if (json["audio"] != null) notes.Add("audio was not an object; defaults used");
            if (json["keyBindings"] is JObject keys)
            {
                foreach (var property in keys.Properties())
                {
                    if (property.Value.Type != JTokenType.String || NormalizeKey((string)property.Value) == null) { notes.Add("keyBindings." + property.Name + " was empty; default kept"); continue; }
                    s._keys[property.Name] = NormalizeKey((string)property.Value); // unknown actions survive for newer builds
                }
                foreach (var conflict in s.KeyConflicts()) notes.Add("key " + conflict.Key + " is bound to " + string.Join(", ", conflict.Actions));
            }
            else if (json["keyBindings"] != null) notes.Add("keyBindings was not an object; defaults used");
            // Schema 3. Choices match case-insensitively and are stored in their canonical spelling;
            // anything else keeps the default and is noted.
            string Choice(string name, string fallback, string[] allowed)
            {
                var value = json[name];
                if (value == null) return fallback;
                var match = value.Type == JTokenType.String ? allowed.FirstOrDefault(a => string.Equals(a, ((string)value).Trim(), StringComparison.OrdinalIgnoreCase)) : null;
                if (match == null) notes.Add(name + " '" + value + "' is unknown; " + fallback + " used");
                return match ?? fallback;
            }
            // rewardCollect: the default moved from "auto" to "manual" (owner decision 2026-10-02) without a schema bump.
            // An absent field (schema 1/2, or a hand-written record) means the player never chose, so it gets the new
            // default. Every schema-3 record this build or an earlier one wrote carries the field, so a stored "auto"
            // cannot be told apart from a deliberate choice and is kept as one.
            // Preview builds used aliases before the schema-3 settings landed on dev.
            // A canonical saved setting always wins; preserve the older preference otherwise.
            if (json["rewardCollect"] == null) s.AutoCollectRewards = Bool(json["autoCollectRewards"], "autoCollectRewards", s.AutoCollectRewards);
            if (json["shopSell"] == null) s.MerchantBuyBack = Bool(json["merchantBuyBack"], "merchantBuyBack", s.MerchantBuyBack);
            if (json["mapHeaderDensity"] == null) s.CompactMapHeader = Bool(json["compactMapHeader"], "compactMapHeader", s.CompactMapHeader);
            s.RewardCollect = Choice("rewardCollect", s.RewardCollect, RewardCollectModes);
            s.ShopSell = Bool(json["shopSell"], "shopSell", s.ShopSell);
            s.SwapCostRule = Choice("swapCostRule", s.SwapCostRule, SwapCostRuleIds);
            s.Fullscreen = Bool(json["fullscreen"], "fullscreen", s.Fullscreen);
            s.UiSize = Choice("uiSize", s.UiSize, UiSizes);
            var legacyAccent = json["accent"]?.Type == JTokenType.String ? (string)json["accent"] : null;
            s.Accent = legacyAccent == "jade" ? "verdant" : legacyAccent == "ember" ? "crimson" : Choice("accent", s.Accent, Accents);
            s.CardMotif = Choice("cardMotif", s.CardMotif, CardMotifs);
            s.CardMotifStrength = Choice("cardMotifStrength", s.CardMotifStrength, CardMotifStrengths);
            s.MapHeaderDensity = Choice("mapHeaderDensity", s.MapHeaderDensity, MapHeaderDensities);
            s.MapHeaderRelics = Bool(json["mapHeaderRelics"], "mapHeaderRelics", s.MapHeaderRelics);
            s.MapHeaderSeed = Bool(json["mapHeaderSeed"], "mapHeaderSeed", s.MapHeaderSeed);
            s.ControlHints = Bool(json["controlHints"], "controlHints", s.ControlHints);
            if (json["gamepadBindings"] is JObject pad)
            {
                foreach (var property in pad.Properties())
                {
                    var button = property.Value.Type == JTokenType.String ? OriginalGamepad.Normalize((string)property.Value) : null;
                    if (button == null) { notes.Add("gamepadBindings." + property.Name + " '" + property.Value + "' is not a gamepad button; " + (s._pad.ContainsKey(property.Name) ? "default kept" : "ignored")); continue; }
                    s._pad[property.Name] = button; // unknown actions survive for newer builds
                }
                foreach (var conflict in s.GamepadConflicts()) notes.Add("gamepad button " + conflict.Key + " is bound to " + string.Join(", ", conflict.Actions));
            }
            else if (json["gamepadBindings"] != null) notes.Add("gamepadBindings was not an object; defaults used");
            return s;
        }
        public static string PaletteName(ColorblindPalette palette) => palette.ToString().ToLowerInvariant();
        public static bool TryParsePalette(string text, out ColorblindPalette palette)
        {
            foreach (ColorblindPalette value in Enum.GetValues(typeof(ColorblindPalette)))
                if (string.Equals(PaletteName(value), text, StringComparison.OrdinalIgnoreCase)) { palette = value; return true; }
            palette = ColorblindPalette.None; return false;
        }
    }
}
