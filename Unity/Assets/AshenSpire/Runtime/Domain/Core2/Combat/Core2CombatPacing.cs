// Core2CombatPacing.cs — the combat pacing settings and the pace they give, as data the combat presentation reads
// (SPEC §0.5 "Pacing", §7.4): the pause between steps (default 500 ms, 0–2000) and the animation step duration (default
// 390 ms, 50–3000), the mobile-fast preset (250 ms / 195 ms; a manual edit returns to Custom), the animation speed choice,
// the manual speed and Auto's own speed (×1.5 by default), and Accessibility's one-tap play switch (off by default: a single
// tap selects a card and never plays it). Nothing here runs a timer; the presentation (UI phase) waits these amounts.
//
// JS at the pin:
//   content/config/ui/presentation/actionPacing.json (exported in ui/combat.json: actionPacing.motion / behavior)
//   src/model/autoCombatPolicy.js   resolveCombatAutomation (bounded values), combatPacingChanges (presets and Custom)
//   src/ui/animationPace.js         ANIM_SPEEDS, COMBAT_TIMING, turnBannerHoldMs, actionPacing (the step pace:
//                                   gap, step, banner and sprite speeds; Auto's multiplier while Auto is on)
//   src/ui/screens/settings.js      the Combat rows and the Accessibility oneTapPlay row (keys, defaults, bounds)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>JS resolveCombatAutomation result: Auto on/off, the manual and Auto speeds, and the two pacing controls.</summary>
    public sealed class Core2CombatAutomation
    {
        public bool Enabled { get; set; }
        public double Speed { get; set; }
        public string Preference { get; set; }
        public double AutoSpeed { get; set; }
        public double AnimationMs { get; set; }
        public double DelayMs { get; set; }
    }

    /// <summary>JS actionPacing result: how long each combat step waits (ms), and the sprite animation speeds.</summary>
    public sealed class Core2ActionPace
    {
        /// <summary>The pause at each step boundary (action word, result, sprite reaction, damage, reset).</summary>
        public double GapMs { get; set; }
        public double StepMs { get; set; }
        public double BannerMs { get; set; }
        /// <summary>0 on Instant or with Reduced motion.</summary>
        public double Scale { get; set; }
        /// <summary>The animation speed table row scaled for the sprite (beatMs, stepMs, lungeMs, impactCapMs, reactionMs, stanceLeadMs).</summary>
        public Dictionary<string, double> SpriteSpeed { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);
    }

    /// <summary>One settings row the combat presentation offers (JS settings.js row): key, type, default and bounds.</summary>
    public sealed class Core2SettingRow
    {
        public string Category { get; set; }
        public string Key { get; set; }
        public string Type { get; set; }
        public JToken Default { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Step { get; set; }
        public IReadOnlyList<string> Choices { get; set; }
        public string Label { get; set; }
        public string Note { get; set; }
    }

    public sealed class Core2CombatPacing
    {
        private readonly JObject _ui;
        private JObject Motion => (JObject)_ui["actionPacing"]["motion"];
        private JObject Behavior => (JObject)_ui["actionPacing"]["behavior"];

        public Core2CombatPacing(Core2Content content)
        {
            _ui = (JObject)(content ?? throw new ArgumentNullException(nameof(content))).Table("ui/combat.json");
        }

        /// <summary>The default pause between steps (JS ACTION_PACING.stageGapMs, 500).</summary>
        public double StepPauseMs => (double)Motion["stageGapMs"];
        /// <summary>The default animation step duration (JS ACTION_PACING.animationStepDurationMs, 390).</summary>
        public double AnimationStepMs => (double)Motion["animationStepDurationMs"];
        public double SpritePlaybackRate => (double)Motion["spritePlaybackRate"];
        /// <summary>The mobile-fast preset's pause and animation step (250 / 195).</summary>
        public (double StepPauseMs, double AnimationStepMs) MobileFast =>
            ((double)Behavior["mobileFastPreset"]["combatStepPause"], (double)Behavior["mobileFastPreset"]["animationStepDuration"]);
        /// <summary>Auto's default speed multiplier (×1.5).</summary>
        public double AutoCombatSpeed => (double)Behavior["autoCombatSpeed"];
        public bool AutoCombatDefault => (bool)Behavior["autoCombat"];

        /// <summary>The Combat settings rows (and Accessibility's one-tap play) with their JS defaults and bounds.</summary>
        public IReadOnlyList<Core2SettingRow> Settings => ((JArray)_ui["settings"]).OfType<JObject>().Select(row => new Core2SettingRow
        {
            Category = (string)row["cat"], Key = (string)row["key"], Type = (string)row["type"] ?? "toggle", Default = row["def"]?.DeepClone(),
            Min = (double?)row["min"], Max = (double?)row["max"], Step = (double?)row["step"], Choices = row["choices"] == null ? null : Core2Json.Strings(row["choices"]),
            Label = (string)row["label"], Note = (string)row["note"],
        }).ToList();

        public Core2SettingRow Setting(string key) => Settings.FirstOrDefault(row => row.Key == key) ?? throw new KeyNotFoundException("No combat setting " + key);
        /// <summary>One-tap play is opt-in: off by default (JS settings.js oneTapPlay def false).</summary>
        public bool OneTapPlayDefault => (bool)Setting("oneTapPlay").Default;

        /// <summary>JS bounded: a finite number (a numeric string counts; null and "" do not) clamped to the bounds, else the fallback.</summary>
        private static double Bounded(JToken value, double fallback, double minimum, double maximum)
        {
            if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined) return fallback;
            double number;
            switch (value.Type)
            {
                case JTokenType.Integer: case JTokenType.Float: number = (double)value; break;
                case JTokenType.Boolean: number = (bool)value ? 1 : 0; break;
                case JTokenType.String:
                {
                    var text = ((string)value).Trim();
                    if ((string)value == "") return fallback;
                    if (text.Length == 0) number = 0;
                    else if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return fallback;
                    break;
                }
                default: return fallback;
            }
            if (double.IsNaN(number) || double.IsInfinity(number)) return fallback;
            return Math.Min(maximum, Math.Max(minimum, number));
        }

        /// <summary>JS resolveCombatAutomation(settings).</summary>
        public Core2CombatAutomation Resolve(JObject settings)
        {
            settings ??= new JObject();
            var b = Behavior;
            return new Core2CombatAutomation
            {
                Enabled = settings["autoCombat"]?.Type == JTokenType.Boolean && (bool)settings["autoCombat"],
                Speed = Bounded(settings["combatSpeed"], (double)b["combatSpeed"], (double)b["speedMinimum"], (double)b["speedMaximum"]),
                Preference = (string)settings["combatPreference"] == "defense" ? "defense" : (string)b["combatPreference"],
                AutoSpeed = Bounded(settings["autoCombatSpeed"], (double)b["autoCombatSpeed"], (double)b["autoSpeedMinimum"], (double)b["autoSpeedMaximum"]),
                AnimationMs = Bounded(settings["animationStepDuration"], AnimationStepMs, (double)b["animationStepMinimumMs"], (double)b["animationStepMaximumMs"]),
                DelayMs = Bounded(settings["combatStepPause"], StepPauseMs, (double)b["stageGapMinimumMs"], (double)b["stageGapMaximumMs"]),
            };
        }

        /// <summary>JS combatPacingChanges: choosing mobile-fast writes its two values; editing either value returns to Custom.</summary>
        public JObject Changes(string key, JToken value)
        {
            if (key == "combatPacingPreset")
            {
                if ((string)value == "mobile-fast")
                {
                    var preset = (JObject)Behavior["mobileFastPreset"].DeepClone();
                    var changes = new JObject { ["combatPacingPreset"] = value?.DeepClone() };
                    foreach (var p in preset.Properties()) changes[p.Name] = p.Value;
                    return changes;
                }
                return new JObject { ["combatPacingPreset"] = "custom" };
            }
            return key == "combatStepPause" || key == "animationStepDuration"
                ? new JObject { [key] = value?.DeepClone(), ["combatPacingPreset"] = "custom" }
                : new JObject { [key] = value?.DeepClone() };
        }

        /// <summary>The animation speed table row (JS ANIM_SPEEDS[speed]); null for instant (or an unknown speed reads normal).</summary>
        public JObject AnimSpeed(string speed)
        {
            var speeds = (JObject)_ui["animSpeeds"];
            var key = speeds.ContainsKey(speed ?? "") ? speed : "normal";
            return speeds[key] as JObject;
        }

        /// <summary>
        /// JS actionPacing({ reduced }) under setAnimSpeed(<paramref name="animSpeed"/>) and setCombatPacing(<paramref name="settings"/>):
        /// one multiplier for the whole action — the speed in force (Auto's while Auto is on), 0 on Instant or Reduced motion —
        /// scales the pause between steps, the step and the turn banner; the sprite speeds follow the animation step duration.
        /// </summary>
        public Core2ActionPace Pace(string animSpeed, JObject settings, bool reduced)
        {
            var automation = Resolve(settings);
            var speed = automation.Enabled ? automation.AutoSpeed : automation.Speed;
            var pace = AnimSpeed(animSpeed);
            var normal = AnimSpeed("normal");
            var scale = pace == null || reduced ? 0 : ((double)pace["beatMs"] / (double)normal["beatMs"]) / speed;
            double Scaled(double value) => Math.Max(0, value) * scale;
            var spriteScale = scale / SpritePlaybackRate * automation.AnimationMs / AnimationStepMs;
            var floor = (double)_ui["combatTiming"]["turnBannerFloorMs"];
            var result = new Core2ActionPace
            {
                GapMs = Scaled(automation.DelayMs), StepMs = Scaled(pace == null ? 0 : (double)pace["stepMs"]),
                BannerMs = Scaled(pace == null ? 0 : Math.Max(floor, (double)pace["beatMs"])), Scale = scale,
            };
            foreach (var p in (pace ?? normal).Properties()) result.SpriteSpeed[p.Name] = (double)p.Value * spriteScale;
            return result;
        }
    }
}
