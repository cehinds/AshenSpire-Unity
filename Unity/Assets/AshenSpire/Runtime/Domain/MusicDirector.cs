// MusicDirector.cs — pure-C# music director (F08): scene → context → track,
// crossfade plan and bus volume, emitted as MusicCommand rows that a Unity
// AudioSource adapter executes. It never touches audio itself.
//
// SELECTION REPLICATES THE HTML (src/main.js audio.music call sites):
//   Title   → 'title'   main.js showLobby (729), showStartupGate (1008), showTitle (1035)
//   Map     → 'map'     main.js showMap (1420); one bed for every act — BEDS has no per-act key
//   Combat  → 'combat' / Elite → 'elite' / Boss → 'boss'   main.js enterCombat (1553), by encounter pool
//   Shop    → 'shop'    main.js showShop (1870)
//   Shrine  → 'rest'    main.js showRest (1824)
//   Event   → hold      main.js showEvent (1884) makes no music call: the map bed plays on
//   Rewards → hold      the post-combat reward screen makes no call: the battle bed plays on
//   Victory → 'victory' main.js 1735, only for the act-3 boss outside Endless (SceneAfterBoss)
//   Death   → stop 0.6s main.js 1707 audio.stopMusic() then sfx 'youDied'
//   Quit    → stop 0.6s main.js 969 (quit without saving), 1188 (quitGame)
// Within a context (src/ui/audio.js music/playExternal/playProcedural):
//   same context while enabled → unchanged (no restart); owner file tracks
//   (music/manifest.json) win over beds; one uniform pick per start; a file
//   that ends re-picks; a file that fails falls back to a bed; Silence → quiet.
//
// DETERMINISM: picks use a private mulberry32 stream seeded by the caller
// (ShuffleSeed, ShuffleDraws). It never reads or advances the run's
// RandomStreams, UnityEngine.Random or System.Random, so music can never
// change a run. The HTML used Math.random here, which was likewise outside
// the run RNG.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain
{
    public enum MusicScene { Title, Map, Combat, Elite, Boss, Shop, Shrine, Event, Rewards, Victory, Death, Quit }

    public static class MusicCommandKind
    {
        public const string Play = "Play";           // start TrackId, fading in from FloorGain
        public const string Crossfade = "Crossfade"; // fade FromTrackId out while TrackId fades in, both at AtSeconds
        public const string Stop = "Stop";           // fade FromTrackId out to silence
        public const string SetVolume = "SetVolume"; // ramp TrackId to TargetVolume in place
    }

    public static class MusicCurve
    {
        public const string Exponential = "exponential";
        public const string Linear = "linear";

        /// <summary>Gain at progress t/duration moving from→to. Exponential mirrors
        /// WebAudio exponentialRampToValueAtTime: endpoints are clamped to the floor,
        /// and the final value is exactly `to` (so a fade-out really reaches 0).</summary>
        public static float Gain(string curve, float from, float to, double t, double duration, float floor)
        {
            if (duration <= 0 || t >= duration - 1e-9) return to; // tolerance: absolute-time subtraction rounds
            if (t <= 0) return Math.Max(from, curve == Exponential ? floor : 0);
            var p = t / duration;
            if (curve == Linear) return (float)(from + (to - from) * p);
            var a = Math.Max(from, floor);
            var b = Math.Max(to, floor);
            return (float)(a * Math.Pow(b / a, p));
        }
    }

    [Serializable]
    public sealed class MusicCommand
    {
        public string Kind;
        public string Context;
        public string TrackId;
        public string FromTrackId;
        public string ResourcePath;
        public bool Loop;
        public double AtSeconds;
        public double FadeInSeconds;
        public double FadeOutSeconds;
        public float FromVolume;    // level of the outgoing track (Crossfade/Stop) or start level (SetVolume)
        public float TargetVolume;  // final level of TrackId: headroom × master × music × context gain
        public float FloorGain;
        public string Curve;
        public string Reason;

        /// <summary>Incoming (or re-levelled) track gain at absolute time.</summary>
        public float IncomingGainAt(double time)
        {
            if (Kind == MusicCommandKind.Stop) return 0;
            if (Kind == MusicCommandKind.SetVolume) return MusicCurve.Gain(Curve, FromVolume, TargetVolume, time - AtSeconds, FadeInSeconds, FloorGain);
            return MusicCurve.Gain(Curve, 0, TargetVolume, time - AtSeconds, FadeInSeconds, FloorGain);
        }

        /// <summary>Outgoing track gain at absolute time (Crossfade/Stop).</summary>
        public float OutgoingGainAt(double time) => MusicCurve.Gain(Curve, FromVolume, 0, time - AtSeconds, FadeOutSeconds, FloorGain);

        public override string ToString() =>
            Kind + " " + (FromTrackId ?? "-") + "→" + (TrackId ?? "-") + " @" + AtSeconds.ToString("0.###") +
            " in " + FadeInSeconds.ToString("0.###") + " out " + FadeOutSeconds.ToString("0.###") +
            " vol " + TargetVolume.ToString("0.#####") + " (" + Reason + ")";
    }

    [Serializable]
    public sealed class MusicSettings
    {
        public int MasterVolume = 100; // 0..100 (Unity-only; 100 = HTML)
        public int MusicVolume = 50;   // 0..100, audio.js musicBus.gain = musicVol / 100
        public bool MusicEnabled = true;
        public bool Muted;             // settings.muteAudio

        public MusicSettings Clone() => new MusicSettings { MasterVolume = MasterVolume, MusicVolume = MusicVolume, MusicEnabled = MusicEnabled, Muted = Muted };
        public static MusicSettings Defaults(MusicCatalog catalog) => new MusicSettings
        {
            MasterVolume = catalog.Bus.DefaultMasterVolume,
            MusicVolume = catalog.Bus.DefaultMusicVolume,
            MusicEnabled = catalog.Bus.DefaultMusicEnabled,
        };
    }

    public sealed class MusicDirector
    {
        private const uint Increment = 0x6d2b79f5;
        private readonly MusicCatalog _catalog;
        private readonly HashSet<string> _failed = new HashSet<string>(StringComparer.Ordinal);
        private MusicSettings _settings;
        private uint _state;

        public uint ShuffleSeed { get; }
        public uint ShuffleDraws { get; private set; }
        public string CurrentContext { get; private set; }
        public MusicTrackDefinition CurrentTrack { get; private set; }
        public float CurrentVolume { get; private set; }
        public MusicSettings Settings => _settings.Clone();

        /// <param name="shuffleSeed">Music's own seed. Derive it however you like (profile,
        /// clock, constant) but never by drawing from the run's RandomStreams.</param>
        /// <param name="shuffleDraws">Restores a saved position in the music stream.</param>
        public MusicDirector(MusicCatalog catalog, uint shuffleSeed, MusicSettings settings = null, uint shuffleDraws = 0)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _settings = (settings ?? MusicSettings.Defaults(catalog)).Clone();
            ShuffleSeed = shuffleSeed;
            ShuffleDraws = shuffleDraws;
            unchecked { _state = shuffleSeed + shuffleDraws * Increment; }
        }

        /// <summary>HTML enterCombat: pool 'boss' → Boss, 'elite' → Elite, anything else → Combat.</summary>
        public static MusicScene SceneForEncounter(string pool) =>
            pool == "boss" ? MusicScene.Boss : pool == "elite" ? MusicScene.Elite : MusicScene.Combat;

        /// <summary>HTML main.js 1733: the act-3 boss outside Endless plays 'victory'; otherwise the reward screen holds the boss bed.</summary>
        public static MusicScene SceneAfterBoss(int actNumber, bool endless) =>
            actNumber >= 3 && !endless ? MusicScene.Victory : MusicScene.Rewards;

        /// <summary>The context a scene requests, or null for hold (Event/Rewards) and stop (Death/Quit).</summary>
        public static string ContextFor(MusicScene scene)
        {
            switch (scene)
            {
                case MusicScene.Title: return "title";
                case MusicScene.Map: return "map";
                case MusicScene.Combat: return "combat";
                case MusicScene.Elite: return "elite";
                case MusicScene.Boss: return "boss";
                case MusicScene.Shop: return "shop";
                case MusicScene.Shrine: return "rest";
                case MusicScene.Victory: return "victory";
                default: return null;
            }
        }

        /// <summary>A screen mounted. `act` is accepted for Map (every act uses 'map', as in the HTML).</summary>
        public IReadOnlyList<MusicCommand> Enter(MusicScene scene, double now, int act = 1)
        {
            if (act < 1) throw new ArgumentOutOfRangeException(nameof(act));
            if (scene == MusicScene.Event || scene == MusicScene.Rewards) return new MusicCommand[0];
            if (scene == MusicScene.Death || scene == MusicScene.Quit)
            {
                // The HTML leaves state.context set after stopMusic(); clearing it here means
                // re-entering the same context after a stop always plays. No HTML path differs.
                CurrentContext = null;
                return StopCurrent(now, _catalog.Crossfade.FadeOutSeconds, scene == MusicScene.Death ? "death" : "quit");
            }
            return EnterContext(ContextFor(scene), now);
        }

        /// <summary>audio.js music(context).</summary>
        public IReadOnlyList<MusicCommand> EnterContext(string context, double now)
        {
            var ctx = _catalog.Context(context) ?? throw new ArgumentException("Unknown music context: " + context);
            if (CurrentContext == context && _settings.MusicEnabled) return new MusicCommand[0]; // 'unchanged'
            CurrentContext = context;
            if (!Audible) return StopCurrent(now, _catalog.Crossfade.FadeOutSeconds, _settings.Muted ? "muted" : "disabled");
            return StartContext(ctx, now, "enter " + context, preferFiles: true);
        }

        /// <summary>audio.js setVolumes(): mute/disable stop in 0.3 s; re-enable/unmute restart
        /// the current context; a level change ramps in place.</summary>
        public IReadOnlyList<MusicCommand> ApplySettings(MusicSettings next, double now)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            var was = _settings;
            _settings = next.Clone();
            _settings.MasterVolume = Clamp(_settings.MasterVolume);
            _settings.MusicVolume = Clamp(_settings.MusicVolume);
            if (!Audible) return StopCurrent(now, _catalog.Crossfade.SettingsFadeOutSeconds, _settings.Muted ? "muted" : "disabled");
            var ctx = CurrentContext == null ? null : _catalog.Context(CurrentContext);
            if (ctx == null) return new MusicCommand[0];
            if (CurrentTrack == null) return StartContext(ctx, now, "resume " + ctx.Id, preferFiles: true);
            var target = VolumeFor(ctx);
            if (Math.Abs(target - CurrentVolume) < 1e-6f && was.MusicEnabled && !was.Muted) return new MusicCommand[0];
            var cmd = Command(MusicCommandKind.SetVolume, ctx, CurrentTrack, null, now, "volume");
            cmd.FromVolume = CurrentVolume;
            cmd.FadeInSeconds = _catalog.Crossfade.VolumeRampSeconds;
            cmd.Curve = MusicCurve.Linear;
            CurrentVolume = target;
            return new[] { cmd };
        }

        /// <summary>An adapter reports a non-looping (file) track finished: re-pick in the same context.</summary>
        public IReadOnlyList<MusicCommand> TrackEnded(string trackId, double now)
        {
            if (CurrentTrack == null || CurrentTrack.Id != trackId || CurrentTrack.Loop || !Audible) return new MusicCommand[0];
            var ctx = _catalog.Context(CurrentContext);
            CurrentTrack = null;
            return StartContext(ctx, now, "ended " + trackId, preferFiles: true);
        }

        /// <summary>An adapter could not load/play a track: fall back to the context's bed (audio.js proceduralFallback).</summary>
        public IReadOnlyList<MusicCommand> TrackFailed(string trackId, double now)
        {
            _failed.Add(trackId);
            if (CurrentTrack == null || CurrentTrack.Id != trackId || !Audible) return new MusicCommand[0];
            var ctx = _catalog.Context(CurrentContext);
            CurrentTrack = null;
            return StartContext(ctx, now, "failed " + trackId, preferFiles: false);
        }

        private bool Audible => _settings.MusicEnabled && !_settings.Muted;

        private IReadOnlyList<MusicCommand> StartContext(MusicContextDefinition ctx, double now, string reason, bool preferFiles)
        {
            var files = preferFiles ? _catalog.TracksFor(ctx.Id, MusicCatalog.KindFile).Where(t => !_failed.Contains(t.Id)).ToArray() : new MusicTrackDefinition[0];
            var pool = files.Length > 0 ? files : ctx.Silence ? new MusicTrackDefinition[0] : _catalog.TracksFor(ctx.Id, MusicCatalog.KindBed);
            if (pool.Length == 0) return StopCurrent(now, _catalog.Crossfade.FadeOutSeconds, "silence " + ctx.Id);
            var next = pool[Pick(pool.Length)];
            var from = CurrentTrack;
            var cmd = Command(from == null ? MusicCommandKind.Play : MusicCommandKind.Crossfade, ctx, next, from, now, reason);
            cmd.FadeInSeconds = next.Kind == MusicCatalog.KindBed ? _catalog.Crossfade.BedFadeInSeconds : _catalog.Crossfade.FileFadeInSeconds;
            if (from != null) { cmd.FadeOutSeconds = _catalog.Crossfade.FadeOutSeconds; cmd.FromVolume = CurrentVolume; }
            CurrentTrack = next;
            CurrentVolume = cmd.TargetVolume;
            return new[] { cmd };
        }

        private IReadOnlyList<MusicCommand> StopCurrent(double now, double fade, string reason)
        {
            if (CurrentTrack == null) return new MusicCommand[0];
            var cmd = Command(MusicCommandKind.Stop, _catalog.Context(CurrentTrack.Context), null, CurrentTrack, now, reason);
            cmd.FadeOutSeconds = fade;
            cmd.FromVolume = CurrentVolume;
            cmd.TargetVolume = 0;
            CurrentTrack = null;
            CurrentVolume = 0;
            return new[] { cmd };
        }

        private MusicCommand Command(string kind, MusicContextDefinition ctx, MusicTrackDefinition to, MusicTrackDefinition from, double now, string reason) => new MusicCommand
        {
            Kind = kind,
            Context = ctx?.Id,
            TrackId = to?.Id,
            FromTrackId = from?.Id,
            ResourcePath = to?.ResourcePath,
            Loop = to != null && to.Loop,
            AtSeconds = now,
            TargetVolume = to == null ? 0 : VolumeFor(ctx),
            FloorGain = _catalog.Crossfade.FloorGain,
            Curve = _catalog.Crossfade.Curve,
            Reason = reason,
        };

        /// <summary>audio.js: master (0.9 × unmuted) → musicBus (vol/100) → bed stage (context gain), times the Unity master slider.</summary>
        public float VolumeFor(MusicContextDefinition ctx)
        {
            if (!Audible || ctx == null || ctx.Silence) return 0;
            return _catalog.Bus.MasterHeadroom * (_settings.MasterVolume / 100f) * (_settings.MusicVolume / 100f) * ctx.Gain;
        }

        private int Pick(int count)
        {
            // mulberry32, the same generator family as the run RNG but a private stream.
            unchecked
            {
                _state += Increment;
                ShuffleDraws++;
                uint t = _state;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                var r = (t ^ (t >> 14)) / 4294967296.0;
                return (int)Math.Floor(r * count);
            }
        }

        private static int Clamp(int v) => Math.Max(0, Math.Min(100, v));
    }
}
