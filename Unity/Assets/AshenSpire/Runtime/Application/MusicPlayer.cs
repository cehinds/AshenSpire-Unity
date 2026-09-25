// MusicPlayer.cs — Unity adapter for F08 music: executes MusicDirector commands on two
// AudioSources (A/B decks for crossfades) and makes the procedural beds audible with
// MusicSynth (no audio files needed). Owner file tracks load from Resources when present.
// ATTACH: RunController.Music.cs calls MusicPlayer.Attach(gameObject, muted) in OnEnable.
// DRIVE: RunController calls Enter(scene) on title/map/combat/elite/boss/shop/shrine/
//   victory/death (RunController.Music.cs maps run phases via MusicSceneMap), SetMuted
//   from the mute toggle and SetSuspended on backgrounding. Volumes come from the
//   director (0.9 × master × music × context gain) and are copied to AudioSource.volume
//   every frame from MusicDecks, so fades never jump when a command interrupts a fade.
// SETTINGS: mute = "AshenSpire.Muted" (shared with GameAudio). Optional bus levels are read
//   once from PlayerPrefs "AshenSpire.MasterVolume", "AshenSpire.MusicVolume" (0..100) and
//   "AshenSpire.MusicEnabled" (0/1); absent keys use the catalog defaults (100, 50, on).
// CLIPS: desktop/mobile stream each bed through an AudioClip PCM reader callback (one
//   MusicBedRenderer per clip, ~3 KB of state; nothing pre-rendered). WebGL does not support
//   PCM callbacks, so beds are pre-rendered time-sliced (~4 ms per frame) into ordinary clips:
//   22050 Hz mono float32, loops capped at 120 s (MusicSynth.LoopSteps) = at most 9.9 MiB
//   per clip, cache capped at PrerenderSampleBudget (240 s ≈ 20 MiB) with LRU eviction of
//   clips no deck is using. A crossfade whose clip is still rendering starts when ready.
// RNG: the shuffle seed comes from the clock, never from the run's RandomStreams.
// SFX: GameAudio is untouched; music uses its own two AudioSources on the same object.
// VERIFY (editor): title plays a bed, entering the map crossfades, mute fades out in 0.3 s.
using System;
using System.Collections.Generic;
using AshenSpire.Domain;
using UnityEngine;
namespace AshenSpire.Application
{
    public sealed class MusicPlayer : MonoBehaviour
    {
        public const string MasterVolumeKey = "AshenSpire.MasterVolume";
        public const string MusicVolumeKey = "AshenSpire.MusicVolume";
        public const string MusicEnabledKey = "AshenSpire.MusicEnabled";
        /// <summary>WebGL pre-render cache: 240 s of 22050 Hz mono (≈ 20 MiB float32).</summary>
        public const int PrerenderSampleBudget = MusicSynth.SampleRate * 240;
        // Time-sliced pre-render: ~4 ms of main thread per frame in 4096-sample chunks. Measured in .NET at
        // roughly 300× real time (one core), i.e. ~70 s of audio per second of frames; the longest (120 s) loop is
        // ready in about 2–4 s (slower in WebAssembly). The outgoing bed still fades on schedule, so an
        // uncached bed starts that much late (its fade-in is re-anchored); cached beds start at once.
        private const double RenderMillisecondsPerFrame = 4;
        private const int RenderChunkSamples = MusicBedRenderer.BlockSamples * 16;

        private sealed class RenderJob
        {
            public string TrackId;
            public MusicBedRenderer Renderer;
            public float[] Buffer;
            public int Written;
        }

        private MusicCatalog _catalog;
        private MusicDirector _director;
        private readonly MusicDecks _decks = new MusicDecks();
        private readonly AudioSource[] _sources = new AudioSource[2];
        private readonly AudioClip[] _ownedClips = new AudioClip[2];
        private readonly string[] _pending = new string[2];
        private readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
        private readonly List<string> _recent = new List<string>();
        private RenderJob _job;
        private bool _suspended;

        private static bool Prerender => UnityEngine.Application.platform == RuntimePlatform.WebGLPlayer;
        private static double Now => Time.unscaledTimeAsDouble;
        public string CurrentTrackId => _director?.CurrentTrack?.Id;

        public static MusicPlayer Attach(GameObject host, bool muted)
        {
            var player = host.GetComponent<MusicPlayer>();
            if (player == null) player = host.AddComponent<MusicPlayer>();
            player.Initialize(muted);
            return player;
        }

        private void Initialize(bool muted)
        {
            if (_director != null) { SetMuted(muted); return; }
            var asset = Resources.Load<TextAsset>("Audio/music-catalog");
            if (asset == null) { Debug.LogWarning("Music catalog Resources/Audio/music-catalog.json is missing; music is off."); return; }
            _catalog = JsonUtility.FromJson<MusicCatalog>(asset.text);
            for (var i = 0; i < 2; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0;
                source.volume = 0;
                source.priority = 0;
                _sources[i] = source;
            }
            var settings = MusicSettings.Defaults(_catalog);
            settings.MasterVolume = PlayerPrefs.GetInt(MasterVolumeKey, settings.MasterVolume);
            settings.MusicVolume = PlayerPrefs.GetInt(MusicVolumeKey, settings.MusicVolume);
            settings.MusicEnabled = PlayerPrefs.GetInt(MusicEnabledKey, settings.MusicEnabled ? 1 : 0) == 1;
            settings.Muted = muted;
            _director = new MusicDirector(_catalog, unchecked((uint)DateTime.UtcNow.Ticks | 1u), settings);
        }

        /// <summary>A screen mounted (the director ignores re-entering the playing context).</summary>
        public void Enter(MusicScene scene, int act = 1)
        {
            if (_director == null) return;
            Execute(_director.Enter(scene, Now, Math.Max(1, act)));
        }

        public void SetMuted(bool muted)
        {
            if (_director == null) return;
            var settings = _director.Settings;
            if (settings.Muted == muted) return;
            settings.Muted = muted;
            Execute(_director.ApplySettings(settings, Now));
        }

        /// <summary>Master/music bus levels and music on/off (0..100); persisted by the caller.</summary>
        public void ApplySettings(int masterVolume, int musicVolume, bool musicEnabled)
        {
            if (_director == null) return;
            var settings = _director.Settings;
            settings.MasterVolume = masterVolume; settings.MusicVolume = musicVolume; settings.MusicEnabled = musicEnabled;
            Execute(_director.ApplySettings(settings, Now));
        }

        /// <summary>Backgrounding pauses both decks without changing director state (F08 acceptance).</summary>
        public void SetSuspended(bool suspended)
        {
            _suspended = suspended;
            foreach (var source in _sources)
            {
                if (source == null || source.clip == null) continue;
                if (suspended) source.Pause(); else source.UnPause();
            }
        }

        private void Execute(IReadOnlyList<MusicCommand> commands)
        {
            foreach (var command in commands)
                foreach (var action in _decks.Apply(command))
                    Perform(action);
        }

        private void Perform(MusicDeckAction action)
        {
            StopDeck(action.Deck);
            if (action.Kind != MusicDeckActionKind.Start) return;
            var track = _catalog.Track(action.TrackId);
            if (track == null) return;
            if (track.Kind == MusicCatalog.KindFile)
            {
                var file = Resources.Load<AudioClip>(action.ResourcePath);
                if (file == null) { Execute(_director.TrackFailed(action.TrackId, Now)); return; }
                StartDeck(action.Deck, file, false, null);
                return;
            }
            var spec = MusicBedSpec.From(_catalog, action.TrackId);
            if (!Prerender)
            {
                var renderer = MusicSynth.Open(spec);
                var stream = AudioClip.Create("AshenSpire_Music_" + action.TrackId, renderer.LoopSamples, 1, renderer.SampleRate, true,
                    new AudioClip.PCMReaderCallback(renderer.Read), new AudioClip.PCMSetPositionCallback(renderer.SetPosition));
                StartDeck(action.Deck, stream, true, stream);
                return;
            }
            if (_cache.TryGetValue(action.TrackId, out var cached)) { Touch(action.TrackId); StartDeck(action.Deck, cached, true, null); return; }
            _pending[action.Deck] = action.TrackId;
        }

        private void StartDeck(int deck, AudioClip clip, bool loop, AudioClip owned)
        {
            var source = _sources[deck];
            source.clip = clip;
            source.loop = loop;
            source.volume = _decks.VolumeAt(deck, Now);
            source.Play();
            if (_suspended) source.Pause();
            _ownedClips[deck] = owned;
        }

        private void StopDeck(int deck)
        {
            _pending[deck] = null;
            var source = _sources[deck];
            if (source != null) { source.Stop(); source.clip = null; }
            if (_ownedClips[deck] != null) { Destroy(_ownedClips[deck]); _ownedClips[deck] = null; }
        }

        private void Update()
        {
            if (_director == null) return;
            var now = Now;
            foreach (var action in _decks.Tick(now)) Perform(action);
            for (var i = 0; i < 2; i++)
            {
                var source = _sources[i];
                if (source.clip == null) continue;
                source.volume = _decks.VolumeAt(i, now);
                // A file track (never looped) that played to its end: the director re-picks.
                var trackId = _decks.TrackOn(i);
                if (!_suspended && !source.loop && !source.isPlaying && source.timeSamples == 0 && trackId != null && source.clip.loadState == AudioDataLoadState.Loaded)
                {
                    _decks.Ended(i);
                    StopDeck(i);
                    Execute(_director.TrackEnded(trackId, now));
                }
            }
            RenderStep(now);
        }

        /// <summary>WebGL: render one chunk of the pending bed per frame, then cache and start it.</summary>
        private void RenderStep(double now)
        {
            if (_job != null && _pending[0] != _job.TrackId && _pending[1] != _job.TrackId) _job = null; // nobody waits for it any more
            if (_job == null)
            {
                var id = _pending[0] ?? _pending[1];
                if (id == null) return;
                var spec = MusicBedSpec.From(_catalog, id);
                var renderer = MusicSynth.Open(spec, MusicSynth.LoopSteps(spec, MusicSynth.DefaultMaxPrerenderSeconds));
                _job = new RenderJob { TrackId = id, Renderer = renderer, Buffer = new float[renderer.LoopSamples] };
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (_job.Written < _job.Buffer.Length && watch.Elapsed.TotalMilliseconds < RenderMillisecondsPerFrame)
            {
                var count = Math.Min(RenderChunkSamples, _job.Buffer.Length - _job.Written);
                _job.Renderer.Read(_job.Buffer, _job.Written, count);
                _job.Written += count;
            }
            if (_job.Written < _job.Buffer.Length) return;
            var clip = AudioClip.Create("AshenSpire_Music_" + _job.TrackId, _job.Buffer.Length, 1, _job.Renderer.SampleRate, false);
            clip.SetData(_job.Buffer, 0);
            _cache[_job.TrackId] = clip;
            Touch(_job.TrackId);
            for (var deck = 0; deck < 2; deck++)
            {
                if (_pending[deck] != _job.TrackId) continue;
                _pending[deck] = null;
                _decks.Reanchor(deck, now);
                StartDeck(deck, clip, true, null);
            }
            _job = null;
            Evict();
        }

        private void Touch(string id) { _recent.Remove(id); _recent.Add(id); }

        private void Evict()
        {
            var total = 0L;
            foreach (var clip in _cache.Values) total += clip.samples;
            for (var i = 0; i < _recent.Count && total > PrerenderSampleBudget;)
            {
                var id = _recent[i];
                var clip = _cache[id];
                if (_sources[0].clip == clip || _sources[1].clip == clip) { i++; continue; }
                total -= clip.samples;
                _cache.Remove(id);
                _recent.RemoveAt(i);
                Destroy(clip);
            }
        }

        private void OnDisable()
        {
            for (var i = 0; i < 2; i++) if (_sources[i] != null && _sources[i].clip != null) _sources[i].Pause();
        }

        private void OnEnable()
        {
            if (_suspended) return;
            for (var i = 0; i < 2; i++) if (_sources[i] != null && _sources[i].clip != null) _sources[i].UnPause();
        }

        private void OnDestroy()
        {
            for (var i = 0; i < 2; i++) { StopDeck(i); if (_sources[i] != null) Destroy(_sources[i]); }
            foreach (var clip in _cache.Values) Destroy(clip);
            _cache.Clear();
            _recent.Clear();
            _job = null;
        }
    }
}
