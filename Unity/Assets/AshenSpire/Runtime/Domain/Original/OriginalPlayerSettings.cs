// OriginalPlayerSettings.cs — device-local customization, extending today's three
// PlayerPrefs flags (AshenSpire.ReducedMotion / FastMotion / Muted) into one versioned record.
// ENTRY POINT: OriginalPlayerSettings.LoadOrMigrate(stored, legacyInt). Store ToJson() under
// StorageKey. Defaults reproduce the current build: full-speed feedback, no shake or
// hit-stop (the Unity presentation has neither yet), unattenuated buses, unmuted.
// Reading never throws: bad or out-of-range values are clamped or defaulted and listed
// in Adjustments. RunController loads/saves it and keeps the legacy flags written;
// CampaignView.PlayerSettings.cs is the settings screen (needs an editor play test).
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
        public const int SchemaVersion = 1;
        public const string StorageKey = "AshenSpire.Settings.v1";
        public const string LegacyReducedMotionKey = "AshenSpire.ReducedMotion", LegacyFastMotionKey = "AshenSpire.FastMotion", LegacyMutedKey = "AshenSpire.Muted";
        public const double TextScaleMin = .8, TextScaleMax = 1.6, UiScaleMin = .75, UiScaleMax = 1.5, AnimationSpeedMin = .5, AnimationSpeedMax = 2, IntensityMin = 0, IntensityMax = 1, VolumeMin = 0, VolumeMax = 1;
        /// <summary>Legacy "Quick animations" halved feedback duration (CombatFeedback: × .5), i.e. speed 2.</summary>
        public const double LegacyFastAnimationSpeed = 2;

        /// <summary>Default key per action: exactly the keys OriginalMapBoard handles today (KeyCode names).</summary>
        public static readonly IReadOnlyDictionary<string, string> DefaultKeyBindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mapScrollUp"] = "PageUp", ["mapScrollDown"] = "PageDown", ["mapTop"] = "Home", ["mapBottom"] = "End",
        };

        public double TextScale = 1, UiScale = 1, AnimationSpeed = 1;
        /// <summary>Skip feedback timelines entirely; AnimationSpeed is kept for when it is turned off.</summary>
        public bool InstantAnimations;
        public bool ReducedMotion;
        public bool ScreenShake; public double ScreenShakeIntensity = 1;
        public bool HitStop;
        public ColorblindPalette ColorblindPalette = ColorblindPalette.None;
        public double MasterVolume = 1, MusicVolume = 1, SfxVolume = 1, UiVolume = 1;
        public bool Muted;
        /// <summary>Read StreamingAssets/Mods when content loads. Off by default: the shipped content only.</summary>
        public bool LoadContentMods;
        private readonly Dictionary<string, string> _keys = new Dictionary<string, string>(DefaultKeyBindings, StringComparer.Ordinal);
        public IReadOnlyDictionary<string, string> KeyBindings => _keys;

        /// <summary>Multiplier for feedback durations: 0 when instant, 1 / speed otherwise (legacy fast = .5).</summary>
        /// <summary>The legacy "Quick animations" flag (AshenSpire.FastMotion): instant or at least the legacy fast speed.</summary>
        public bool QuickAnimations => InstantAnimations || AnimationSpeed >= LegacyFastAnimationSpeed;
        public double AnimationDurationScale => InstantAnimations ? 0 : 1 / AnimationSpeed;
        /// <summary>Effective gain for a bus after master volume and mute.</summary>
        public double Gain(string bus)
        {
            if (Muted) return 0;
            switch (bus) { case "music": return MasterVolume * MusicVolume; case "sfx": return MasterVolume * SfxVolume; case "ui": return MasterVolume * UiVolume; case "master": return MasterVolume; default: throw new ArgumentException("Unknown audio bus: " + bus); }
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

        public JObject ToJson()
        {
            var keys = new JObject(); foreach (var pair in _keys.OrderBy(p => p.Key, StringComparer.Ordinal)) keys[pair.Key] = pair.Value;
            return new JObject
            {
                ["schemaVersion"] = SchemaVersion, ["textScale"] = TextScale, ["uiScale"] = UiScale,
                ["animationSpeed"] = AnimationSpeed, ["instantAnimations"] = InstantAnimations, ["reducedMotion"] = ReducedMotion,
                ["screenShake"] = ScreenShake, ["screenShakeIntensity"] = ScreenShakeIntensity, ["hitStop"] = HitStop,
                ["colorblindPalette"] = PaletteName(ColorblindPalette),
                ["audio"] = new JObject { ["master"] = MasterVolume, ["music"] = MusicVolume, ["sfx"] = SfxVolume, ["ui"] = UiVolume, ["muted"] = Muted },
                ["keyBindings"] = keys, ["loadContentMods"] = LoadContentMods,
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
            s.LoadContentMods = Bool(json["loadContentMods"], "loadContentMods", s.LoadContentMods);
            var palette = json["colorblindPalette"];
            if (palette != null) { if (palette.Type == JTokenType.String && TryParsePalette((string)palette, out var parsed)) s.ColorblindPalette = parsed; else notes.Add("colorblindPalette '" + palette + "' is unknown; none used"); }
            if (json["audio"] is JObject audio)
            {
                s.MasterVolume = Number(audio["master"], "audio.master", 1, VolumeMin, VolumeMax); s.MusicVolume = Number(audio["music"], "audio.music", 1, VolumeMin, VolumeMax);
                s.SfxVolume = Number(audio["sfx"], "audio.sfx", 1, VolumeMin, VolumeMax); s.UiVolume = Number(audio["ui"], "audio.ui", 1, VolumeMin, VolumeMax);
                s.Muted = Bool(audio["muted"], "audio.muted", false);
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
