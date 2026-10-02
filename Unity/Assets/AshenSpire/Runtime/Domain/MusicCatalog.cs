// MusicCatalog.cs — data-driven music track catalog (F08) read from
// Resources/Audio/music-catalog.json. Pure C#: no UnityEngine, no run RNG.
// SOURCE: HTML src/content/music.js (BEDS, SCALES) and music/manifest.json.
// EDIT: regenerate with `dotnet run --project UnityTests/Music -- --write-catalog`
// after changing music.js or music/manifest.json; never hand-tune track rows.
// CREDIT: every track names a row of CREDITS.md; Validate() refuses a track
// without Credit/Author/License, with a license outside AllowedLicenses, or
// whose Credit key has no CREDITS.md table row.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain
{
    [Serializable]
    public sealed class MusicCatalog
    {
        public const string KindBed = "bed";
        public const string KindFile = "file";
        public static readonly string[] RequiredContexts = { "title", "map", "combat", "elite", "boss", "shop", "rest", "victory" };
        public static readonly string[] Waves = { "sine", "triangle", "square", "sawtooth" };

        public int SchemaVersion = 1;
        public string ManifestPath;      // music/manifest.json (owner-supplied file tracks)
        public string BedsPath;          // src/content/music.js (procedural beds)
        public string FileResourceRoot;  // Resources-relative folder for imported file tracks
        public string[] AllowedLicenses;
        public MusicBusDefinition Bus = new MusicBusDefinition();
        public MusicCrossfadeDefinition Crossfade = new MusicCrossfadeDefinition();
        public MusicScaleDefinition[] Scales;
        public MusicContextDefinition[] Contexts;
        public MusicTrackDefinition[] Tracks;

        public MusicContextDefinition Context(string id) => Contexts?.FirstOrDefault(c => c.Id == id);
        public MusicTrackDefinition Track(string id) => Tracks?.FirstOrDefault(t => t.Id == id);
        public MusicTrackDefinition[] TracksFor(string context, string kind) =>
            (Tracks ?? new MusicTrackDefinition[0]).Where(t => t.Context == context && t.Kind == kind).ToArray();

        /// <summary>Every problem found, in catalog order. Empty means valid.</summary>
        /// <param name="fileExists">Repository-relative path check (the catalog never copies audio).</param>
        /// <param name="creditsMarkdown">Full text of CREDITS.md.</param>
        public List<string> Validate(Func<string, bool> fileExists, string creditsMarkdown)
        {
            var errors = new List<string>();
            void Require(bool ok, string message) { if (!ok) errors.Add("music-catalog.json: " + message); }
            Require(SchemaVersion == 1, "SchemaVersion must be 1.");
            Require(!string.IsNullOrEmpty(ManifestPath) && fileExists(ManifestPath), "ManifestPath missing: " + ManifestPath);
            Require(!string.IsNullOrEmpty(BedsPath) && fileExists(BedsPath), "BedsPath missing: " + BedsPath);
            Require(!string.IsNullOrEmpty(FileResourceRoot), "FileResourceRoot required.");
            Require(AllowedLicenses != null && AllowedLicenses.Length > 0, "AllowedLicenses required.");
            Require(Bus != null && Bus.MasterHeadroom > 0 && Bus.MasterHeadroom <= 1 && InVolume(Bus.DefaultMasterVolume) && InVolume(Bus.DefaultMusicVolume), "Bus: headroom (0,1], volumes 0..100.");
            Require(Crossfade != null && Crossfade.FadeOutSeconds > 0 && Crossfade.SettingsFadeOutSeconds > 0 && Crossfade.BedFadeInSeconds >= 0 && Crossfade.FileFadeInSeconds >= 0 && Crossfade.VolumeRampSeconds >= 0, "Crossfade: fade-outs > 0, fade-ins/ramp >= 0.");
            Require(Crossfade != null && Crossfade.FloorGain > 0 && Crossfade.FloorGain <= 0.01f, "Crossfade.FloorGain must be in (0, 0.01].");
            Require(Crossfade != null && Crossfade.Curve == MusicCurve.Exponential, "Crossfade.Curve must be exponential (HTML stopMusic/drone ramps).");
            var scales = new HashSet<string>();
            foreach (var s in Scales ?? new MusicScaleDefinition[0])
                Require(!string.IsNullOrEmpty(s.Id) && scales.Add(s.Id) && s.Steps != null && s.Steps.Length > 0, "Scale empty or duplicate: " + s.Id);
            var contexts = new HashSet<string>();
            foreach (var c in Contexts ?? new MusicContextDefinition[0])
            {
                Require(!string.IsNullOrEmpty(c.Id) && contexts.Add(c.Id), "Context empty or duplicate: " + c.Id);
                Require(c.Silence || (c.Gain > 0 && c.Gain <= 1), "Context " + c.Id + ": gain must be in (0,1] (quiet is spelled Silence=true).");
            }
            foreach (var required in RequiredContexts) Require(contexts.Contains(required), "required context missing: " + required);
            var ids = new HashSet<string>();
            foreach (var t in Tracks ?? new MusicTrackDefinition[0])
            {
                var label = "Track " + t.Id + ": ";
                Require(!string.IsNullOrEmpty(t.Id) && ids.Add(t.Id), label + "empty or duplicate id.");
                Require(contexts.Contains(t.Context), label + "unknown context " + t.Context);
                Require(t.Kind == KindBed || t.Kind == KindFile, label + "Kind must be bed or file.");
                Require(!string.IsNullOrEmpty(t.Path) && fileExists(t.Path), label + "file does not exist: " + t.Path);
                Require(!string.IsNullOrWhiteSpace(t.Credit) && !string.IsNullOrWhiteSpace(t.Author) && !string.IsNullOrWhiteSpace(t.License), label + "lacks a credit (Credit, Author and License are required).");
                Require(AllowedLicenses == null || AllowedLicenses.Contains(t.License), label + "license not allowed: " + t.License);
                Require(string.IsNullOrWhiteSpace(t.Credit) || HasCreditRow(creditsMarkdown, t.Credit), label + "no CREDITS.md row for '" + t.Credit + "'.");
                if (t.Kind == KindBed)
                    Require(t.Path == BedsPath && t.Root > 0 && t.CadenceMs > 0 && t.Lift > 0 && scales.Contains(t.Scale) && Waves.Contains(t.Wave) && t.Loop, label + "bed needs BedsPath, root, cadence, lift, known scale/wave and Loop.");
                if (t.Kind == KindFile)
                    Require(t.Path.StartsWith("music/", StringComparison.Ordinal) && !string.IsNullOrEmpty(t.ResourcePath) && t.ResourcePath.StartsWith(FileResourceRoot + "/", StringComparison.Ordinal) && !t.Loop, label + "file track must live under music/, map to FileResourceRoot, and not loop (HTML re-picks on end).");
            }
            foreach (var c in Contexts ?? new MusicContextDefinition[0])
                Require(c.Silence || (Tracks ?? new MusicTrackDefinition[0]).Any(t => t.Context == c.Id), "Context " + c.Id + " has no tracks and is not Silence.");
            return errors;
        }

        public void EnsureValid(Func<string, bool> fileExists, string creditsMarkdown)
        {
            var errors = Validate(fileExists, creditsMarkdown);
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
        }

        /// <summary>True when CREDITS.md has a table row whose first cell contains the key.</summary>
        public static bool HasCreditRow(string creditsMarkdown, string key)
        {
            if (string.IsNullOrEmpty(creditsMarkdown)) return false;
            foreach (var raw in creditsMarkdown.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("|", StringComparison.Ordinal)) continue;
                var cells = line.Split('|');
                if (cells.Length > 2 && cells[1].Contains(key)) return true;
            }
            return false;
        }

        private static bool InVolume(int v) => v >= 0 && v <= 100;
    }

    [Serializable]
    public sealed class MusicBusDefinition
    {
        public float MasterHeadroom = 0.9f;  // audio.js applyGains(): master.gain = 0.9 * (muted ? 0 : 1)
        public int DefaultMasterVolume = 100; // Unity-only master slider; HTML has none (100 = HTML level)
        public int DefaultMusicVolume = 50;   // content/balance.js ui.audio.musicVolume
        public bool DefaultMusicEnabled = true;
    }

    [Serializable]
    public sealed class MusicCrossfadeDefinition
    {
        public double FadeOutSeconds = 0.6;         // audio.js stopMusic(fade = 0.6)
        public double SettingsFadeOutSeconds = 0.3; // audio.js setVolumes(): stopMusic(0.3) on mute/disable
        public double BedFadeInSeconds = 1.5;       // audio.js drone(): exponential ramp over 1.5 s
        public double FileFadeInSeconds = 0;        // audio.js playExternal(): <audio>.play() at full level
        public double VolumeRampSeconds = 0.1;      // Unity: slider moves ramp in place (HTML restarts the bed)
        public float FloorGain = 0.0001f;           // exponentialRampToValueAtTime(0.0001, ...)
        public string Curve = MusicCurve.Exponential;
    }

    [Serializable]
    public sealed class MusicScaleDefinition
    {
        public string Id;
        public int[] Steps;
    }

    [Serializable]
    public sealed class MusicContextDefinition
    {
        public string Id;
        public float Gain;    // music.js BEDS[context].gain — applied to beds AND file tracks (audio.js bedGain)
        public bool Silence;  // music.js MUSIC_SILENCE_WORD: deliberate quiet
    }

    [Serializable]
    public sealed class MusicTrackDefinition
    {
        public string Id;
        public string Context;
        public string Kind;          // bed | file
        public string Path;          // repository-relative source (never copied by the catalog)
        public string ResourcePath;  // file tracks: Resources.Load<AudioClip> path once imported
        public bool Loop;            // beds loop forever; files end and the director re-picks
        // Procedural bed parameters (music.js BEDS[context].variants[Variant]).
        public int Variant;
        public float Root;
        public string Scale;
        public int CadenceMs;
        public string Wave;
        public int Lift;
        public bool Drone;
        public bool Pulse;
        // License carried per track; Credit is the first-cell key of a CREDITS.md row.
        public string Credit;
        public string Author;
        public string License;
    }
}
