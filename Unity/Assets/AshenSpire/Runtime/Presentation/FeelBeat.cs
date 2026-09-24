// FeelBeat.cs — when each part of one combat feedback beat plays (fx.js playTimeline order).
// Pure C#: no UnityEngine, no RNG, no timers. CombatFeedback samples a plan per frame;
// UnityTests/Feel checks the plan against the profile. Every number comes from
// Resources/Feel/feel-profile.json through FeelProfile; nothing is tuned here.
// Order: [enemy turn banner] → actor lunge/step → impact at WindupMs → victim recoil,
// shake (heavy hits only) and the number pop together. Instant/Reduced collapse pacing
// and keep queued effects QueueStepMs apart. A click still skips everything (Cancel).
using System;
using AshenSpire.Domain;

namespace AshenSpire.Presentation
{
    public enum FeelVictim { None, Player, Enemy }

    public sealed class FeelBeat
    {
        public FeelPace Pace;
        public int BannerAtMs = -1;       // -1: no banner
        public FeelPlayback Banner;
        public int ActorAtMs;
        public FeelPlayback Actor;        // actor.lunge (attacks) or actor.step (other actions)
        public float ActorSide;           // +1 player acting (lunge-right), -1 enemy acting (lunge-left)
        public bool ActorIsPlayer;
        public int ImpactMs;
        public FeelVictim Victim;
        public int VictimAmount;
        public bool Heavy;
        public FeelPlayback Recoil;       // hit.recoil / hit.recoilHeavy on the damaged side
        public float VictimSide;          // +1 enemy knocked right (hit-enemy), -1 player (hit-player)
        public FeelPlayback Glow;         // hit.flash colour track for heal/guard/poison outcomes
        public FeelPlayback Shake;        // screen.shake on the combat stage, heavy hits only
        public FeelPlayback Number;       // damageNumber.pop (damageNumber.reduced under Reduced motion)
        public string NumberTier;
        public float NumberFontScale;
        public int TotalMs;

        /// <summary>
        /// Plans one beat. `playerAttack` is an attack cue or damage dealt to the foe;
        /// `damage` is HP the foe lost, `hurt` HP the player lost (post-guard residuals, as fx.js).
        /// </summary>
        public static FeelBeat Plan(FeelProfile profile, FeelSettings settings, bool enemyTurn, bool playerAttack, int damage, int hurt)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            settings = settings ?? new FeelSettings();
            var pace = profile.Pace(settings);
            var beat = new FeelBeat { Pace = pace };
            var at = 0;
            if (enemyTurn)
            {
                beat.BannerAtMs = 0;
                beat.Banner = profile.Resolve("banner.turn", settings);
                at = pace.Instant ? pace.QueueStepMs : pace.BannerBeatMs;
            }
            beat.ActorAtMs = at;
            beat.ActorIsPlayer = !enemyTurn;
            beat.ActorSide = enemyTurn ? -1 : 1;
            beat.Actor = profile.Resolve(enemyTurn || playerAttack ? "actor.lunge" : "actor.step", settings);
            beat.ImpactMs = at + (pace.Instant ? 0 : pace.WindupMs);

            var amount = enemyTurn ? hurt : damage;
            beat.Victim = amount > 0 ? (enemyTurn ? FeelVictim.Player : FeelVictim.Enemy) : FeelVictim.None;
            beat.VictimAmount = Math.Max(0, amount);
            beat.VictimSide = enemyTurn ? -1 : 1;
            beat.Heavy = amount >= profile.Thresholds.HeavyHitDamage;
            beat.Recoil = amount > 0 ? profile.Resolve(beat.Heavy ? "hit.recoilHeavy" : "hit.recoil", settings) : Off("hit.recoil");
            beat.Glow = amount > 0 ? Off("hit.flash") : profile.Resolve("hit.flash", settings);
            beat.Shake = beat.Heavy ? profile.Resolve("screen.shake", settings) : Off("screen.shake");
            beat.Number = profile.Resolve("damageNumber.pop", settings);
            beat.NumberTier = amount > 0 ? FeelDamageNumbers.Tier(amount, profile.Thresholds) : "normal";
            beat.NumberFontScale = profile.DamageNumbers.FontScale(beat.NumberTier);

            var end = beat.ImpactMs;
            void Extend(int start, FeelPlayback playback) { if (playback != null && playback.Play) end = Math.Max(end, start + playback.LifetimeMs); }
            if (beat.BannerAtMs >= 0) Extend(beat.BannerAtMs, beat.Banner);
            Extend(beat.ActorAtMs, beat.Actor);
            Extend(beat.ImpactMs, beat.Recoil);
            Extend(beat.ImpactMs, beat.Glow);
            Extend(beat.ImpactMs, beat.Shake);
            Extend(beat.ImpactMs, beat.Number);
            beat.TotalMs = end;
            return beat;
        }

        private static FeelPlayback Off(string id) => new FeelPlayback { Id = id, Play = false, Tracks = new FeelTrack[0], Curve = FeelCurves.Linear };

        /// <summary>Elapsed time inside a part that starts at `startMs`; negative before it starts.</summary>
        public static double Local(double elapsedMs, int startMs) => elapsedMs - startMs;
    }
}
