// FeelProfile.cs — every "feel" timing of the HTML reference as data (F07 juice pass).
// Read from Resources/Feel/feel-profile.json (JsonUtility in Unity, System.Text.Json with
// IncludeFields in UnityTests/Feel). Pure C#: no UnityEngine, no RNG, never touches combat.
// SOURCE: src/ui/fx.js, styles/combat.css, styles/base.css, styles/kit.css, styles/ui.css,
// src/ui/reaverAttack.js, src/content/balance.js — every value is cited in docs/Unity-Feel.md
// and re-read from those files by `dotnet run --project UnityTests/Feel`, so drift fails a check.
// EDIT: change the HTML first, then the JSON; never tune a number here only.
//
// Settings semantics (SPEC §7.4, src/ui/fx.js):
//   * Animation speed (slow/normal/fast/instant) scales PACING only — beat, step and lunge.
//     Fixed effect durations (hit flash, glyph, wobble, number pop…) never scale.
//   * Instant, and Reduced motion, collapse pacing to zero; queued effects still play
//     QueueStepMs apart (fx.js animateEvents).
//   * Reduced motion removes every movement track (translate, scale, rotate, letter-spacing,
//     travel) and keeps opacity/colour tracks; a motion left with no track does not play.
//   * Screen shake off (or Reduced motion) suppresses Shake motions; Reduce flashes suppresses
//     Flash motions (everything fx.js routes through flash()).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AshenSpire.Domain
{
    public static class FeelProperty
    {
        public const string X = "x", Y = "y", Scale = "scale", ScaleX = "scaleX", Rotate = "rotate", LetterSpacing = "letterSpacing", Travel = "travel";
        public const string Opacity = "opacity", Brightness = "brightness", Saturate = "saturate", Grayscale = "grayscale", Blur = "blur", Tint = "tint";
        public static readonly string[] Movement = { X, Y, Scale, ScaleX, Rotate, LetterSpacing, Travel };
        public static readonly string[] Appearance = { Opacity, Brightness, Saturate, Grayscale, Blur, Tint };
        public static bool IsMovement(string property) => Array.IndexOf(Movement, property) >= 0;
        public static bool IsKnown(string property) => IsMovement(property) || Array.IndexOf(Appearance, property) >= 0;
        /// <summary>The value a property has when no animation touches it (CSS `none`).</summary>
        public static double Rest(string property) =>
            property == Scale || property == ScaleX || property == Opacity || property == Brightness || property == Saturate ? 1 : 0;
    }

    public static class FeelTiming
    {
        public const string Fixed = "fixed"; // never scaled by animation speed (SPEC §7.4)
        public const string Lunge = "lunge"; // scaled by speed.LungeMs / normal LungeMs (reaverAttack.js)
    }

    /// <summary>The player's motion settings. Unity's two toggles map through FromToggles.</summary>
    [Serializable]
    public sealed class FeelSettings
    {
        public string Speed = "normal";
        public bool ReducedMotion;
        public bool ScreenShake = true;
        public bool ReduceFlashes;
        /// <summary>CampaignView's "Reduced motion" and "Quick animations" toggles: Quick = the HTML `fast` speed.</summary>
        public static FeelSettings FromToggles(bool reducedMotion, bool quickAnimations) =>
            new FeelSettings { Speed = quickAnimations ? "fast" : "normal", ReducedMotion = reducedMotion };
    }

    [Serializable]
    public sealed class FeelProfile
    {
        public const string ResourcePath = "Feel/feel-profile";
        public static readonly string[] RequiredSpeeds = { "slow", "normal", "fast", "instant" };
        public static readonly string[] RequiredMotions =
        {
            "card.hover", "card.rewardHover", "card.play", "button.hover", "toggle.knob",
            "actor.lunge", "actor.step", "enemy.coopLunge",
            "hit.flash", "hit.recoil", "hit.recoilHeavy", "screen.shake",
            "damageNumber.pop", "damageNumber.reduced",
            "banner.turn", "banner.coop", "banner.coopSmall",
            "screen.enter", "toast.enter", "saveSlots.enter", "eventChoice.enter",
            "boss.veil", "boss.rise", "boss.rule", "boss.out", "reward.taken",
            "enemy.death", "fx.slash", "fx.glyph", "fx.spark", "stagger.wobble", "relic.proc", "stance.flare",
            "hud.barFill", "proc.barDrain", "tooltip.show", "tooltip.fade", "idle.bob", "idle.bobEnemy",
        };

        public int SchemaVersion = 1;
        public string Reference;
        public string DefaultSpeed = "normal";
        public FeelSpeed[] Speeds;
        public FeelPacing Pacing = new FeelPacing();
        public FeelThresholds Thresholds = new FeelThresholds();
        public FeelDamageNumbers DamageNumbers = new FeelDamageNumbers();
        public FeelInput Input = new FeelInput();
        public FeelSequences Sequences = new FeelSequences();
        public FeelMotion[] Motions;
        /// <summary>Feel moments the task list names that the HTML reference does not animate (checked against the reference).</summary>
        public string[] NotInReference;

        public FeelMotion Motion(string id) => Motions?.FirstOrDefault(m => m.Id == id);

        /// <summary>The speed row for an id; unknown ids fall back to DefaultSpeed like fx.js setAnimSpeed.</summary>
        public FeelSpeed Speed(string id) =>
            Speeds?.FirstOrDefault(s => s.Id == id) ?? Speeds?.FirstOrDefault(s => s.Id == DefaultSpeed);

        /// <summary>Pacing for these settings (fx.js playTimeline). Reduced motion paces like instant.</summary>
        public FeelPace Pace(FeelSettings settings)
        {
            settings = settings ?? new FeelSettings();
            var speed = Speed(settings.Speed);
            var instant = speed == null || speed.Instant || settings.ReducedMotion;
            var pace = new FeelPace { SpeedId = speed?.Id ?? DefaultSpeed, Instant = instant, QueueStepMs = Pacing.QueueStepMs, HitStopMs = Pacing.HitStopMs };
            if (instant) return pace;
            pace.BeatMs = speed.BeatMs;
            pace.StepMs = speed.StepMs;
            pace.LungeMs = speed.LungeMs;
            pace.WindupMs = (int)Math.Round(speed.LungeMs * Pacing.WindupLungeFraction, MidpointRounding.AwayFromZero);
            pace.BannerBeatMs = Math.Max(Pacing.BannerMinBeatMs, speed.BeatMs);
            return pace;
        }

        /// <summary>fx.js watchdog: the most wall-clock a paced timeline of `beats` beats may take.</summary>
        public int WatchdogMs(int beats, FeelSettings settings, int actorAnimationMs = 0)
        {
            var pace = Pace(settings);
            if (pace.Instant) return 0;
            return Pacing.WatchdogBaseMs + beats * (pace.BeatMs + Math.Max(pace.LungeMs, actorAnimationMs) + Pacing.WatchdogStepsPerBeat * pace.StepMs);
        }

        /// <summary>Reaver painted attack timing at this speed (reaverAttack.js reaverAttackTiming).</summary>
        public FeelFrameTiming ReaverAttack(FeelSettings settings)
        {
            var pace = Pace(settings);
            if (pace.Instant) return new FeelFrameTiming();
            var scale = Math.Max(0.1, pace.LungeMs / (double)Sequences.ReaverNormalLungeMs);
            var frameMs = Math.Max(1, (int)Math.Round(Sequences.ReaverFrameMs * scale, MidpointRounding.AwayFromZero));
            return new FeelFrameTiming { FrameMs = frameMs, ImpactMs = Sequences.ReaverImpactFrame * frameMs, TotalMs = Sequences.ReaverFrameCount * frameMs };
        }

        /// <summary>Speed factor applied to Lunge-timed motions: speed.LungeMs / normal LungeMs; 0 when instant.</summary>
        public double LungeScale(FeelSettings settings)
        {
            var pace = Pace(settings);
            var normal = Speed("normal");
            return pace.Instant || normal == null || normal.LungeMs <= 0 ? 0 : pace.LungeMs / (double)normal.LungeMs;
        }

        /// <summary>What Presentation should play for one motion under these settings.</summary>
        public FeelPlayback Resolve(string id, FeelSettings settings)
        {
            settings = settings ?? new FeelSettings();
            var motion = Motion(id) ?? throw new ArgumentException("feel-profile.json: unknown motion " + id);
            if (settings.ReducedMotion && !string.IsNullOrEmpty(motion.ReducedMotionId)) motion = Motion(motion.ReducedMotionId);
            var tracks = (motion.Tracks ?? new FeelTrack[0]).Where(t => !settings.ReducedMotion || !FeelProperty.IsMovement(t.Property)).ToArray();
            var suppressed = (motion.Flash && settings.ReduceFlashes)
                || (motion.Shake && (!settings.ScreenShake || settings.ReducedMotion))
                || tracks.Length == 0;
            double duration = motion.DurationMs, lifetime = motion.LifetimeMs > 0 ? motion.LifetimeMs : motion.DurationMs;
            if (motion.Timing == FeelTiming.Lunge)
            {
                var scale = LungeScale(settings);
                duration = Math.Round(motion.DurationMs * scale, MidpointRounding.AwayFromZero);
                lifetime = Pace(settings).LungeMs;
                if (scale <= 0) suppressed = true; // instant pacing plays no actor wind-up
            }
            if (settings.ReducedMotion && motion.ReducedDurationMs > 0) duration = lifetime = motion.ReducedDurationMs;
            if (suppressed) return new FeelPlayback { Id = motion.Id, Play = false, Easing = motion.Easing, Curve = FeelCurves.Parse(motion.Easing), Tracks = new FeelTrack[0] };
            return new FeelPlayback
            {
                Id = motion.Id, Play = true, DurationMs = (int)duration, LifetimeMs = (int)Math.Max(duration, lifetime),
                Easing = motion.Easing, Curve = FeelCurves.Parse(motion.Easing), Loop = motion.Loop, Tracks = tracks,
            };
        }

        /// <summary>Every problem found, in profile order. Empty means valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            void Require(bool ok, string message) { if (!ok) errors.Add("feel-profile.json: " + message); }
            Require(SchemaVersion == 1, "SchemaVersion must be 1.");
            var speeds = Speeds ?? new FeelSpeed[0];
            foreach (var id in RequiredSpeeds) Require(speeds.Count(s => s.Id == id) == 1, "speed row required once: " + id);
            foreach (var s in speeds)
                Require(s.Instant ? s.BeatMs == 0 && s.StepMs == 0 && s.LungeMs == 0 : s.BeatMs > 0 && s.StepMs > 0 && s.LungeMs > 0, "speed " + s.Id + ": paced rows need beat/step/lunge > 0; instant needs zeros.");
            Require(speeds.Any(s => s.Id == DefaultSpeed && !s.Instant), "DefaultSpeed must name a paced speed.");
            Require(Pacing != null && Pacing.QueueStepMs > 0 && Pacing.BannerMinBeatMs > 0 && Pacing.WindupLungeFraction > 0 && Pacing.WindupLungeFraction < 1 && Pacing.HitStopMs >= 0, "Pacing ranges.");
            Require(Thresholds != null && Thresholds.SmallDamageBelow < Thresholds.HeavyHitDamage && Thresholds.HeavyHitDamage < Thresholds.CritDamage && Thresholds.ShakeMaxPx > 0, "Thresholds must ascend small < heavy < crit.");
            Require(DamageNumbers != null && DamageNumbers.BaseFontRem > 0 && DamageNumbers.SmallFontRem < DamageNumbers.BaseFontRem && DamageNumbers.BaseFontRem < DamageNumbers.HeavyFontRem && DamageNumbers.HeavyFontRem < DamageNumbers.CritFontRem, "DamageNumbers font tiers must ascend small < base < heavy < crit.");
            Require(Input != null && Input.HoldBeatAt != null && Input.HoldBeatAt.Length > 0 && Input.HoldBeatAt.All(a => a >= 0 && a < 1), "Input.HoldBeatAt fractions in [0, 1).");
            Require(Sequences != null && Sequences.ReaverImpactFrame > 0 && Sequences.ReaverImpactFrame < Sequences.ReaverFrameCount, "Sequences reaver impact inside the sequence.");
            Require(Sequences != null && Speed("normal") != null && Speed("normal").LungeMs == Sequences.ReaverNormalLungeMs, "normal LungeMs must equal ReaverNormalLungeMs.");
            var ids = new HashSet<string>();
            foreach (var m in Motions ?? new FeelMotion[0])
            {
                Require(m != null && !string.IsNullOrWhiteSpace(m.Id) && ids.Add(m.Id), "empty or duplicate motion id: " + m?.Id);
                if (m == null) continue;
                Require(m.DurationMs > 0, m.Id + ": DurationMs > 0.");
                Require(m.LifetimeMs == 0 || m.LifetimeMs >= m.DurationMs * 0.5, m.Id + ": LifetimeMs 0 or near the duration.");
                Require(FeelCurves.TryParse(m.Easing, out _), m.Id + ": unsupported Easing " + m.Easing);
                Require(m.Timing == FeelTiming.Fixed || m.Timing == FeelTiming.Lunge, m.Id + ": Timing must be fixed or lunge.");
                Require(string.IsNullOrEmpty(m.ReducedMotionId) || (Motions.Any(o => o.Id == m.ReducedMotionId) && m.ReducedMotionId != m.Id), m.Id + ": ReducedMotionId must name another motion.");
                Require(m.Tracks != null && m.Tracks.Length > 0, m.Id + ": at least one track.");
                var props = new HashSet<string>();
                foreach (var t in m.Tracks ?? new FeelTrack[0])
                {
                    Require(t != null && FeelProperty.IsKnown(t.Property) && props.Add(t.Property), m.Id + ": unknown or duplicate track " + t?.Property);
                    var keys = t?.Keys ?? new FeelKey[0];
                    Require(keys.Length >= 2 && keys[0].T == 0 && keys[keys.Length - 1].T == 1, m.Id + "." + t?.Property + ": keys must start at T=0 and end at T=1.");
                    for (var i = 1; i < keys.Length; i++) Require(keys[i].T > keys[i - 1].T, m.Id + "." + t?.Property + ": key times must ascend.");
                }
            }
            foreach (var id in RequiredMotions) Require(ids.Contains(id), "required motion missing: " + id);
            return errors;
        }
    }

    [Serializable]
    public sealed class FeelSpeed
    {
        public string Id;
        public int BeatMs, StepMs, LungeMs;
        public bool Instant;
    }

    [Serializable]
    public sealed class FeelPacing
    {
        public int QueueStepMs = 80;          // fx.js STEP_MS: queued events ≤80 ms apart
        public int BannerMinBeatMs = 260;     // turn banner beat = max(260, beatMs)
        public float WindupLungeFraction = .55f; // impact after lungeMs × 0.55
        public int WatchdogBaseMs = 2000;
        public int WatchdogStepsPerBeat = 4;
        public int HitStopMs;                 // 0: the reference has no hit-stop
    }

    [Serializable]
    public sealed class FeelThresholds
    {
        public int HeavyHitDamage = 15;   // heavy recoil + screen shake + heavy number
        public int CritDamage = 25;       // crit number tier
        public int SmallDamageBelow = 6;  // chip number tier
        public float ShakeMaxPx = 4;
    }

    [Serializable]
    public sealed class FeelDamageNumbers
    {
        public float BaseFontRem = 2.5f, SmallFontRem = 2f, HeavyFontRem = 3.4f, CritFontRem = 4.4f, BurstFontRem = 3.6f;
        public float JitterPx = 13, PairedOffsetPx = 26, AnchorHeightFraction = .25f, EdgePadPx = 6;

        /// <summary>fx.js dmgClass: "crit" | "heavy" | "small" | "normal" for an HP-residual amount.</summary>
        public static string Tier(int amount, FeelThresholds thresholds) =>
            amount >= thresholds.CritDamage ? "crit" : amount >= thresholds.HeavyHitDamage ? "heavy" : amount < thresholds.SmallDamageBelow ? "small" : "normal";

        /// <summary>Font size relative to the base number for a tier ("burst" for proc numbers).</summary>
        public float FontScale(string tier)
        {
            switch (tier)
            {
                case "crit": return CritFontRem / BaseFontRem;
                case "heavy": return HeavyFontRem / BaseFontRem;
                case "small": return SmallFontRem / BaseFontRem;
                case "burst": return BurstFontRem / BaseFontRem;
                default: return 1f;
            }
        }
    }

    [Serializable]
    public sealed class FeelInput
    {
        public float DragSlopPx = 12;
        public int InspectHoldMs = 400;
        public string HoldConfirmDefault = "normal";
        public int HoldConfirmOffMs, HoldConfirmShortMs = 350, HoldConfirmNormalMs = 600, HoldConfirmLongMs = 1000;
        public float[] HoldBeatAt;
        public int TooltipOpenMs = 500, TooltipFocusMs = 160, TooltipHandoverMs = 120;
    }

    [Serializable]
    public sealed class FeelSequences
    {
        public int BossIntroHoldMs = 2300, BossIntroRemoveMs = 480;
        public int PoseHoldMs = 260, PoseMinMs = 60;
        public int ReaverFrameMs = 56, ReaverFrameCount = 60, ReaverImpactFrame = 31, ReaverNormalLungeMs = 260;
        public float SlashRotateJitterDeg = 25;
    }

    [Serializable]
    public sealed class FeelMotion
    {
        public string Id;
        public string Source;          // HTML origin, e.g. "styles/combat.css @keyframes num-pop"
        public int DurationMs;         // CSS animation/transition duration
        public string Easing;          // CSS timing function, applied per keyframe interval
        public string Timing = FeelTiming.Fixed;
        public int LifetimeMs;         // JS removal / class time (0 = the duration)
        public bool Loop;
        public bool Flash;             // routed through fx.js flash(): Reduce flashes suppresses it
        public bool Shake;             // Screen shake setting (and Reduced motion) suppresses it
        public string ReducedMotionId; // variant played under Reduced motion
        public int ReducedDurationMs;  // duration override under Reduced motion (0 = none)
        public FeelTrack[] Tracks;
    }

    [Serializable]
    public sealed class FeelTrack
    {
        public string Property;
        public FeelKey[] Keys;
    }

    [Serializable]
    public struct FeelKey
    {
        public float T, V;
        public FeelKey(float t, float v) { T = t; V = v; }
    }

    public sealed class FeelPace
    {
        public string SpeedId;
        public bool Instant;
        public int BeatMs, StepMs, LungeMs, WindupMs, BannerBeatMs, QueueStepMs, HitStopMs;
    }

    public sealed class FeelFrameTiming
    {
        public int FrameMs, ImpactMs, TotalMs;
    }

    /// <summary>A resolved motion: whether to play, for how long, and how to sample it.</summary>
    public sealed class FeelPlayback
    {
        public string Id, Easing;
        public bool Play, Loop;
        public int DurationMs, LifetimeMs;
        public CubicBezier Curve;
        public FeelTrack[] Tracks;

        public bool Has(string property) => Tracks != null && Tracks.Any(t => t.Property == property);

        /// <summary>Property value at progress p in [0, 1]; the rest value when the track is absent or the motion does not play.</summary>
        public double Sample(string property, double p)
        {
            var track = Tracks?.FirstOrDefault(t => t.Property == property);
            return track == null ? FeelProperty.Rest(property) : FeelCurves.SampleTrack(track.Keys, p, Curve, FeelProperty.Rest(property));
        }

        /// <summary>Property value at elapsed milliseconds (loops wrap).</summary>
        public double SampleAt(string property, double elapsedMs)
        {
            if (!Play || DurationMs <= 0) return FeelProperty.Rest(property);
            var p = elapsedMs / DurationMs;
            p = Loop ? p - Math.Floor(p) : Math.Min(1, Math.Max(0, p));
            return Sample(property, p);
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0} play={1} {2}ms {3}", Id, Play, DurationMs, Easing);
    }
}
