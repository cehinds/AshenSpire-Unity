// Program.cs — F08 music director checks. Run from anywhere:
//   dotnet run --project UnityTests/Music                   (checks; exit 1 on failure)
//   dotnet run --project UnityTests/Music -- --write-catalog (regenerate the catalog JSON)
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AshenSpire.Domain;
using AshenSpire.Domain.Original;

var root = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "CREDITS.md"))
    ? Directory.GetCurrentDirectory()
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string Read(string rel) => File.ReadAllText(Path.Combine(root, rel));
bool Exists(string rel) => File.Exists(Path.Combine(root, rel));

if (args.Contains("--write-catalog"))
{
    File.WriteAllText(Path.Combine(root, CatalogBuilder.CatalogPath), CatalogBuilder.Serialize(CatalogBuilder.Build(root)));
    Console.WriteLine("Wrote " + CatalogBuilder.CatalogPath);
    return 0;
}

var passed = 0; var failures = new List<string>();
void Check(bool ok, string name) { if (ok) { Console.WriteLine("PASS: " + name); passed++; } else { Console.WriteLine("FAIL: " + name); failures.Add(name); } }
bool Near(double a, double b, double eps = 1e-5) => Math.Abs(a - b) <= eps;

// ---- catalog: data, files, credits -------------------------------------------------
var catalogJson = Read(CatalogBuilder.CatalogPath);
MusicCatalog Load() => JsonSerializer.Deserialize<MusicCatalog>(catalogJson, CatalogBuilder.Json);
var catalog = Load();
var credits = Read("CREDITS.md");
var errors = catalog.Validate(Exists, credits);
foreach (var e in errors) Console.WriteLine("  " + e);
Check(errors.Count == 0, "catalog validates against real files and CREDITS.md");
Check(CatalogBuilder.Serialize(CatalogBuilder.Build(root)) == catalogJson.Replace("\r\n", "\n"), "catalog matches src/content/music.js + music/manifest.json (regenerate with --write-catalog)");
Check(Exists(CatalogBuilder.CatalogPath + ".meta"), "catalog has a Unity .meta file");
foreach (var ctx in MusicCatalog.RequiredContexts)
    Check(catalog.Context(ctx) != null && catalog.Tracks.Any(t => t.Context == ctx), "context '" + ctx + "' has tracks");
foreach (var t in catalog.Tracks)
    Check(Exists(t.Path) && !string.IsNullOrEmpty(t.License) && MusicCatalog.HasCreditRow(credits, t.Credit), "track " + t.Id + " file exists (" + t.Path + ") and is credited (" + t.Credit + ", " + t.License + ")");
using (var manifest = JsonDocument.Parse(Read("music/manifest.json")))
{
    var listed = manifest.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array).Sum(p => p.Value.GetArrayLength());
    Check(listed == catalog.Tracks.Count(t => t.Kind == MusicCatalog.KindFile), "every music/manifest.json entry is a catalog file track (" + listed + ")");
}
var bedVariants = Regex.Matches(Read("src/content/music.js"), @"\{ root: [\d.]+, scale:").Count;
Check(bedVariants == catalog.Tracks.Count(t => t.Kind == MusicCatalog.KindBed) && bedVariants > 0, "every music.js bed variant is a catalog bed track (" + bedVariants + ")");

// Validation refuses what it must.
MusicCatalog Mutant(Action<MusicCatalog> change) { var c = Load(); change(c); return c; }
bool Refused(Action<MusicCatalog> change, string fragment) => Mutant(change).Validate(Exists, credits).Any(e => e.Contains(fragment));
Check(Refused(c => c.Tracks[0].Credit = "", "lacks a credit"), "validation fails a track without a credit");
Check(Refused(c => c.Tracks[0].License = null, "lacks a credit"), "validation fails a track without a license");
Check(Refused(c => c.Tracks[0].Credit = "Nobody's Uncredited Song", "no CREDITS.md row"), "validation fails a credit with no CREDITS.md row");
Check(Refused(c => c.Tracks[0].License = "All rights reserved", "license not allowed"), "validation fails a disallowed license");
Check(Refused(c => c.Tracks = c.Tracks.Append(new MusicTrackDefinition { Id = "file.combat.x", Context = "combat", Kind = "file", Path = "music/combat/missing.ogg", ResourcePath = "Audio/Music/combat/missing", Credit = CatalogBuilder.BedCredit, Author = "x", License = "CC0" }).ToArray(), "file does not exist"), "validation fails a missing audio file");
Check(Refused(c => c.Contexts = c.Contexts.Where(x => x.Id != "boss").ToArray(), "required context missing: boss"), "validation fails a missing required context");
Check(Refused(c => c.Tracks = c.Tracks.Where(t => t.Context != "shop").ToArray(), "shop has no tracks"), "validation fails a context with no tracks");
Check(Refused(c => c.Tracks[1].Id = c.Tracks[0].Id, "duplicate"), "validation fails duplicate track ids");

// ---- HTML selection logic still matches the call sites this director cites ------------
var main = Read("src/main.js");
var called = Regex.Matches(main, @"audio\.music\('(\w+)'\)").Select(m => m.Groups[1].Value).Distinct().OrderBy(s => s).ToArray();
Check(called.SequenceEqual(new[] { "map", "rest", "shop", "title", "victory" }), "main.js literal music contexts are title/map/rest/shop/victory");
Check(main.Contains("audio.music(enc.pool === 'boss' ? 'boss' : enc.pool === 'elite' ? 'elite' : 'combat')"), "main.js combat music chooses boss/elite/combat by pool");
var showEvent = Regex.Match(main, @"function showEvent\(.*?\n}\n", RegexOptions.Singleline).Value;
Check(showEvent.Length > 0 && !showEvent.Contains("audio."), "main.js showEvent makes no music call (event holds the map bed)");
Check(Regex.IsMatch(main, @"if \(result !== 'victory'\) \{\s*audio\.stopMusic\(\);"), "main.js death stops music");
Check(Regex.IsMatch(main, @"if \(run\.actNumber >= 3 && !endlessOn\(\)\) \{[^}]*audio\.music\('victory'\)", RegexOptions.Singleline), "main.js victory music only for act-3 boss outside Endless");
var audioJs = Read("src/ui/audio.js");
Check(audioJs.Contains("function stopMusic(fade = 0.6)") && audioJs.Contains("stopMusic(0.3)") && audioJs.Contains("exponentialRampToValueAtTime(gain, now() + 1.5)") && audioJs.Contains("master.gain.value = 0.9 * m"), "audio.js fade/headroom constants match the catalog crossfade plan");

// ---- owner file tracks listed in music/manifest.json are tried before beds ---------------
// Until the owner imports them into Resources (docs/Unity-Music.md step 4) each one fails to
// load once and its context falls back to a bed; that fallback is checked with synthetic
// files below. Everything else is checked against the beds alone.
MusicCatalog BedsOnly() { var c = Load(); c.Tracks = c.Tracks.Where(t => t.Kind == MusicCatalog.KindBed).ToArray(); return c; }
var sceneFor = new Dictionary<string, MusicScene> { ["title"] = MusicScene.Title, ["map"] = MusicScene.Map, ["combat"] = MusicScene.Combat, ["elite"] = MusicScene.Elite, ["boss"] = MusicScene.Boss, ["shop"] = MusicScene.Shop, ["rest"] = MusicScene.Shrine, ["victory"] = MusicScene.Victory };
foreach (var pair in sceneFor.Where(p => catalog.TracksFor(p.Key, MusicCatalog.KindFile).Any()))
    Check(new MusicDirector(catalog, 1).Enter(pair.Value, 0)[0].TrackId.StartsWith("file." + pair.Key + "."), "listed " + pair.Key + " file track is tried first");
catalog = BedsOnly();

// ---- every context ---------------------------------------------------------------------
MusicDirector Director(uint seed = 7, MusicSettings s = null) => new MusicDirector(catalog, seed, s);
var d = Director();
IReadOnlyList<MusicCommand> cmds;
cmds = d.Enter(MusicScene.Title, 0);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Play && cmds[0].Context == "title" && catalog.Track(cmds[0].TrackId).Context == "title", "title plays a title track");
var titleTrack = cmds[0].TrackId;
Check(d.Enter(MusicScene.Title, 1).Count == 0 && d.CurrentTrack.Id == titleTrack, "re-entering the same context is unchanged (no restart)");
cmds = d.Enter(MusicScene.Map, 2, act: 1);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Crossfade && cmds[0].FromTrackId == titleTrack && cmds[0].Context == "map", "map act 1 crossfades from title to a map track");
var mapTrack = cmds[0].TrackId;
Check(d.Enter(MusicScene.Map, 3, act: 2).Count == 0 && d.Enter(MusicScene.Map, 3, act: 3).Count == 0, "map acts 2 and 3 use the same 'map' context (HTML has no per-act bed)");
foreach (var act in new[] { 1, 2, 3, 4 }) Check(MusicDirector.ContextFor(MusicScene.Map) == "map" && new MusicDirector(catalog, 1).Enter(MusicScene.Map, 0, act)[0].Context == "map", "map act " + act + " → map");
Check(d.Enter(MusicScene.Event, 4).Count == 0 && d.CurrentTrack.Id == mapTrack, "event holds the map bed");
foreach (var (pool, scene, ctx) in new[] { ("normal", MusicScene.Combat, "combat"), ("elite", MusicScene.Elite, "elite"), ("boss", MusicScene.Boss, "boss") })
{
    Check(MusicDirector.SceneForEncounter(pool) == scene, "encounter pool '" + pool + "' → " + scene);
    var before = d.CurrentTrack.Id;
    cmds = d.Enter(scene, 5);
    Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Crossfade && cmds[0].Context == ctx && cmds[0].FromTrackId == before && catalog.Track(cmds[0].TrackId).Context == ctx, scene + " crossfades into a " + ctx + " track");
    var battle = d.CurrentTrack.Id;
    Check(d.Enter(MusicScene.Rewards, 6).Count == 0 && d.CurrentTrack.Id == battle, scene + " rewards screen holds the battle bed");
    d.Enter(MusicScene.Map, 7);
}
cmds = d.Enter(MusicScene.Shop, 8);
Check(cmds.Count == 1 && cmds[0].Context == "shop" && catalog.Track(cmds[0].TrackId).Context == "shop", "shop plays a shop track");
cmds = d.Enter(MusicScene.Shrine, 9);
Check(cmds.Count == 1 && cmds[0].Context == "rest" && catalog.Track(cmds[0].TrackId).Context == "rest", "shrine plays the 'rest' track");
Check(MusicDirector.SceneAfterBoss(3, false) == MusicScene.Victory && MusicDirector.SceneAfterBoss(3, true) == MusicScene.Rewards && MusicDirector.SceneAfterBoss(2, false) == MusicScene.Rewards, "victory only after the act-3 boss outside Endless");
cmds = d.Enter(MusicScene.Victory, 10);
Check(cmds.Count == 1 && cmds[0].Context == "victory" && catalog.Track(cmds[0].TrackId).Context == "victory", "victory plays a victory track");
var victoryTrack = d.CurrentTrack.Id;
cmds = d.Enter(MusicScene.Death, 11);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Stop && cmds[0].FromTrackId == victoryTrack && Near(cmds[0].FadeOutSeconds, 0.6) && d.CurrentTrack == null, "death stops music with a 0.6 s fade");
Check(d.Enter(MusicScene.Death, 12).Count == 0, "death while silent emits nothing");
cmds = d.Enter(MusicScene.Title, 13);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Play && cmds[0].Context == "title", "title after death starts fresh with Play");
d.Enter(MusicScene.Map, 14);
cmds = d.Enter(MusicScene.Quit, 15);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Stop && Near(cmds[0].FadeOutSeconds, 0.6), "quit stops music with a 0.6 s fade");
cmds = d.Enter(MusicScene.Map, 16);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Play, "re-entering the context that was stopped plays again");
Check(catalog.Tracks.Where(t => t.Kind == MusicCatalog.KindBed).All(t => t.Loop), "beds loop");
Check(d.TrackEnded(d.CurrentTrack.Id, 17).Count == 0, "a looping bed never re-picks on 'ended'");

// ---- crossfade math ----------------------------------------------------------------------
var cf = new MusicDirector(catalog, 3);
cf.Enter(MusicScene.Title, 0);
var x = cf.Enter(MusicScene.Boss, 10)[0];
var floor = catalog.Crossfade.FloorGain;
var bossVol = 0.9f * 1f * 0.5f * catalog.Context("boss").Gain;
var titleVol = 0.9f * 1f * 0.5f * catalog.Context("title").Gain;
Check(Near(x.TargetVolume, bossVol) && Near(x.FromVolume, titleVol), "crossfade from/to volumes = headroom × master × music × context gain");
Check(Near(x.AtSeconds, 10) && Near(x.FadeOutSeconds, 0.6) && Near(x.FadeInSeconds, 1.5) && x.Curve == MusicCurve.Exponential, "crossfade timing: out 0.6 s, bed in 1.5 s, exponential, starting together");
Check(Near(x.IncomingGainAt(10), floor) && Near(x.IncomingGainAt(11.5), bossVol) && Near(x.IncomingGainAt(20), bossVol), "incoming curve starts at floor and lands on target");
Check(Near(x.IncomingGainAt(10.75), Math.Sqrt(floor * bossVol)), "incoming exponential midpoint is the geometric mean");
Check(Near(x.OutgoingGainAt(10), titleVol) && Near(x.OutgoingGainAt(10.3), Math.Sqrt(titleVol * floor)) && x.OutgoingGainAt(10.6) == 0, "outgoing curve: from level → geometric mean → exactly 0");
var mono = true; for (var t = 10.0; t < 11.6; t += 0.05) mono &= x.IncomingGainAt(t + 0.05) >= x.IncomingGainAt(t) && x.OutgoingGainAt(t + 0.05) <= x.OutgoingGainAt(t);
Check(mono, "crossfade curves are monotonic");
Check(Near(MusicCurve.Gain(MusicCurve.Linear, 0.2f, 0.4f, 0.5, 1, floor), 0.3) && MusicCurve.Gain(MusicCurve.Exponential, 0.2f, 0.4f, 0, 0, floor) == 0.4f, "linear ramp midpoint and zero-duration jump");

// ---- mute and bus volumes ------------------------------------------------------------------
var v = new MusicDirector(catalog, 5);
v.Enter(MusicScene.Map, 0);
var mapGain = catalog.Context("map").Gain;
Check(Near(v.CurrentVolume, 0.9 * 0.5 * mapGain), "default music bus 50 → 0.9 × 0.5 × map gain");
cmds = v.ApplySettings(new MusicSettings { MasterVolume = 50, MusicVolume = 50, MusicEnabled = true }, 1);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.SetVolume && Near(cmds[0].TargetVolume, 0.9 * 0.5 * 0.5 * mapGain) && cmds[0].TrackId == v.CurrentTrack.Id, "master 50 halves the level in place");
cmds = v.ApplySettings(new MusicSettings { MasterVolume = 50, MusicVolume = 100, MusicEnabled = true }, 2);
Check(cmds.Count == 1 && Near(cmds[0].TargetVolume, 0.9 * 0.5 * 1 * mapGain) && Near(cmds[0].IncomingGainAt(2 + catalog.Crossfade.VolumeRampSeconds), cmds[0].TargetVolume), "music 100 raises the level with a short ramp");
cmds = v.ApplySettings(new MusicSettings { MasterVolume = 150, MusicVolume = -3, MusicEnabled = true }, 2.5);
Check(v.Settings.MasterVolume == 100 && v.Settings.MusicVolume == 0 && cmds.Count == 1 && cmds[0].TargetVolume == 0, "volumes clamp to 0..100");
v.ApplySettings(new MusicSettings { MasterVolume = 100, MusicVolume = 50, MusicEnabled = true }, 2.6);
Check(v.ApplySettings(new MusicSettings { MasterVolume = 100, MusicVolume = 50, MusicEnabled = true }, 2.7).Count == 0, "unchanged settings emit nothing");
var playing = v.CurrentTrack.Id;
cmds = v.ApplySettings(new MusicSettings { MasterVolume = 100, MusicVolume = 50, MusicEnabled = true, Muted = true }, 3);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Stop && cmds[0].FromTrackId == playing && Near(cmds[0].FadeOutSeconds, 0.3), "mute stops music with the 0.3 s settings fade");
Check(v.Enter(MusicScene.Map, 4).Count == 0, "muted: same context stays quiet");
Check(v.Enter(MusicScene.Combat, 5).Count == 0 && v.CurrentContext == "combat" && v.CurrentTrack == null, "muted: new context is tracked but silent");
Check(v.VolumeFor(catalog.Context("combat")) == 0, "muted volume is 0");
cmds = v.ApplySettings(new MusicSettings { MasterVolume = 100, MusicVolume = 50, MusicEnabled = true }, 6);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Play && cmds[0].Context == "combat", "unmute resumes the current context");
cmds = v.ApplySettings(new MusicSettings { MasterVolume = 100, MusicVolume = 50, MusicEnabled = false }, 7);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Stop, "music disabled stops music");
Check(v.Enter(MusicScene.Shop, 8).Count == 0 && v.Enter(MusicScene.Shop, 9).Count == 0, "disabled: entering contexts emits nothing");
cmds = v.ApplySettings(new MusicSettings { MasterVolume = 100, MusicVolume = 50, MusicEnabled = true }, 10);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Play && cmds[0].Context == "shop", "re-enable resumes the current context");
var mutedStart = new MusicDirector(catalog, 5, new MusicSettings { MusicVolume = 50, MasterVolume = 100, MusicEnabled = true, Muted = true });
Check(mutedStart.Enter(MusicScene.Title, 0).Count == 0, "starting muted plays nothing");

// ---- file tracks: preference, re-pick on end, fallback on failure, silence ------------------
var withFiles = BedsOnly();
withFiles.Tracks = withFiles.Tracks.Concat(new[] { "a", "b" }.Select(n => new MusicTrackDefinition { Id = "file.combat." + n, Context = "combat", Kind = "file", Path = "music/combat/" + n + ".ogg", ResourcePath = "Audio/Music/combat/" + n, Credit = CatalogBuilder.BedCredit, Author = "Owner", License = "CC0" })).ToArray();
Check(withFiles.Validate(p => p.StartsWith("music/combat/") || Exists(p), credits).Count == 0, "synthetic credited file tracks validate");
var f = new MusicDirector(withFiles, 11);
cmds = f.Enter(MusicScene.Combat, 0);
Check(cmds[0].TrackId.StartsWith("file.combat.") && !cmds[0].Loop && Near(cmds[0].FadeInSeconds, 0) && cmds[0].ResourcePath.StartsWith("Audio/Music/combat/"), "owner file tracks win over beds, start at full level, don't loop");
cmds = f.TrackEnded(f.CurrentTrack.Id, 30);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Play && cmds[0].TrackId.StartsWith("file.combat."), "a finished file re-picks within the context");
Check(f.TrackEnded("file.combat.zzz", 31).Count == 0, "'ended' for a track that is not current is ignored");
cmds = f.TrackFailed(f.CurrentTrack.Id, 32);
Check(cmds.Count == 1 && cmds[0].TrackId.StartsWith("bed.combat.") && cmds[0].Loop, "a failed file falls back to the context's bed");
f.Enter(MusicScene.Map, 33);
var failedAgain = f.Enter(MusicScene.Combat, 34)[0].TrackId;
Check(failedAgain.StartsWith("file.combat."), "other files remain eligible after one fails");
var quiet = BedsOnly();
quiet.Contexts.First(c => c.Id == "shop").Silence = true;
var q = new MusicDirector(quiet, 1);
q.Enter(MusicScene.Map, 0);
cmds = q.Enter(MusicScene.Shop, 1);
Check(cmds.Count == 1 && cmds[0].Kind == MusicCommandKind.Stop && q.CurrentTrack == null, "a Silence context fades out and plays nothing");

// ---- determinism and RNG isolation ----------------------------------------------------------
string Script(MusicDirector dir)
{
    var log = new List<string>(); double t = 0;
    var scenes = new[] { MusicScene.Title, MusicScene.Map, MusicScene.Combat, MusicScene.Rewards, MusicScene.Map, MusicScene.Event, MusicScene.Shop, MusicScene.Map, MusicScene.Shrine, MusicScene.Map, MusicScene.Elite, MusicScene.Map, MusicScene.Boss, MusicScene.Map, MusicScene.Combat, MusicScene.Map, MusicScene.Boss, MusicScene.Victory, MusicScene.Death, MusicScene.Title };
    for (var round = 0; round < 3; round++) foreach (var s in scenes) foreach (var c in dir.Enter(s, t += 2.5)) log.Add(c.ToString());
    return string.Join("\n", log);
}
var runRng = new RandomStreams(424242);
runRng.Float("map"); runRng.Float("shuffle");
var rngBefore = JsonSerializer.Serialize(runRng.Snapshot());
var scriptA = Script(new MusicDirector(catalog, 99));
var scriptB = Script(new MusicDirector(catalog, 99));
Check(scriptA == scriptB && scriptA.Length > 0, "same shuffle seed → identical command sequence");
Check(Script(new MusicDirector(catalog, 100)) != scriptA, "a different shuffle seed changes the picks");
Check(JsonSerializer.Serialize(runRng.Snapshot()) == rngBefore, "directing a whole script leaves run RandomStreams counters untouched");
var half = new MusicDirector(catalog, 99);
half.Enter(MusicScene.Title, 0); half.Enter(MusicScene.Map, 1);
var resumed = new MusicDirector(catalog, 99, null, half.ShuffleDraws);
Check(half.Enter(MusicScene.Boss, 2)[0].TrackId == resumed.Enter(MusicScene.Boss, 2)[0].TrackId, "saved (seed, draws) resumes the same pick sequence");
var forbidden = new[] { typeof(RandomStreams), typeof(Random) };
var members = typeof(MusicDirector).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Select(fi => fi.FieldType)
    .Concat(typeof(MusicDirector).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
    .Concat(typeof(MusicDirector).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SelectMany(m => m.GetParameters()).Select(p => p.ParameterType));
Check(!members.Any(forbidden.Contains), "MusicDirector has no field or parameter of a run-RNG type");
foreach (var file in new[] { "MusicDirector.cs", "MusicCatalog.cs" })
{
    var src = Regex.Replace(Read("Unity/Assets/AshenSpire/Runtime/Domain/" + file), @"//.*", "");
    Check(!Regex.IsMatch(src, @"RandomStreams\s*[\.\(\w]|new Random\(|System\.Random|UnityEngine"), file + " references no run RNG, System.Random or UnityEngine");
    Check(Exists("Unity/Assets/AshenSpire/Runtime/Domain/" + file + ".meta"), file + " has a Unity .meta file");
}

// ---- procedural bed synthesis (MusicSynth.cs ports audio.js playProcedural + drone) ----------------
foreach (var literal in new[] {
    "scale[(step * lift + (step % 2 ? 2 : 0)) % scale.length]", "step % 4 === 0 ? 2 : 1", "step % 3 === 1",
    "exponentialRampToValueAtTime(0.16, t + 0.08)", "exponentialRampToValueAtTime(0.0001, t + 1.8)", "o.stop(t + 1.9)",
    "freq * 1.4983", "exponentialRampToValueAtTime(0.07, t + 0.12)", "exponentialRampToValueAtTime(0.0001, t + 1.6)", "h.stop(t + 1.7)",
    "Math.max(420, variant.cadence / 2)", "exponentialRampToValueAtTime(variant.root / 3, t + 0.18)", "o.frequency.setValueAtTime(variant.root / 2, t)",
    "exponentialRampToValueAtTime(0.22, t + 0.02)", "exponentialRampToValueAtTime(0.0001, t + 0.32)", "o.stop(t + 0.36)",
    "drone(variant.root / 2, 0.12, out)", "freq * 1.005", "lp.frequency.value = 700", "lfoG.gain.value = 260", "lfo.frequency.value = 0.07", "o.type = 'sawtooth'" })
    Check(audioJs.Contains(literal), "audio.js still has the synthesized literal: " + literal);
var synthRng = new RandomStreams(777); synthRng.Float("map");
var synthRngBefore = JsonSerializer.Serialize(synthRng.Snapshot());
var bedTracks = catalog.Tracks.Where(t => t.Kind == MusicCatalog.KindBed).ToArray();
var synthHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
var synthWatch = System.Diagnostics.Stopwatch.StartNew();
long largestPrerenderBytes = 0; long totalSamples = 0;
foreach (var t in bedTracks)
{
    var spec = MusicBedSpec.From(catalog, t.Id);
    var steps = spec.PatternSteps;
    Check(steps == 84, t.Id + ": note pattern repeats every 84 steps (melody 14 x octave 4 x harmony 3)");
    var js = new double[steps];
    var scale = catalog.Scales.First(x => x.Id == t.Scale).Steps;
    for (var step = 0; step < steps; step++) js[step] = t.Root * (step % 4 == 0 ? 2 : 1) * Math.Pow(2, scale[(step * t.Lift + (step % 2 == 1 ? 2 : 0)) % scale.Length] / 12.0);
    Check(Enumerable.Range(0, steps).All(i => Near(spec.NoteFrequency(i), js[i], 1e-9) && Near(spec.NoteFrequency(i + steps), js[i], 1e-9)), t.Id + ": note frequencies match the audio.js formula and repeat after the pattern");
    var loopSamples = MusicSynth.LoopSamples(spec, steps);
    Check(Near(MusicSynth.LoopSeconds(spec, steps), steps * t.CadenceMs / 1000.0, 1e-9) && Math.Abs(loopSamples - steps * t.CadenceMs * 22.05) <= 0.5, t.Id + ": loop = 84 x " + t.CadenceMs + " ms = " + (steps * t.CadenceMs / 1000.0).ToString("0.##") + " s (" + loopSamples + " samples @ 22050 Hz)");
    if (t.Pulse) Check(Near(steps * t.CadenceMs / (Math.Max(420, t.CadenceMs / 2.0)) % 1, 0, 1e-9), t.Id + ": pulse interval divides the loop");
    var full = MusicSynth.Render(spec);
    totalSamples += full.Length;
    var finite = full.All(float.IsFinite);
    var peak = full.Max(Math.Abs);
    var rms = Math.Sqrt(full.Average(x => (double)x * x));
    Check(full.Length == loopSamples && finite && peak < 1f && peak > 0.05f && rms > 0.005, t.Id + ": finite, audible, never clips (peak " + peak.ToString("0.###") + ", rms " + rms.ToString("0.####") + ")");
    Check(Math.Abs(full[^1] - full[0]) < 0.05, t.Id + ": loop seam is continuous");
    var stream = MusicSynth.Open(spec);
    stream.SetPosition(loopSamples - 22050);
    var across = new float[44100];
    stream.Read(across, 0, across.Length);
    var drift = 0.0; for (var i = 0; i < 44100; i++) drift = Math.Max(drift, Math.Abs(across[i] - full[(loopSamples - 22050 + i) % loopSamples]));
    Check(drift < 1e-3, t.Id + ": streaming reader wraps the loop seamlessly and matches the pre-render (max drift " + drift.ToString("0.#####") + ")");
    if (synthHashes.Count < 3)
    {
        var chunked = new float[loopSamples]; var reader = MusicSynth.Open(spec);
        for (var at = 0; at < loopSamples; at += 1000) reader.Read(chunked, at, Math.Min(1000, loopSamples - at));
        Check(MusicSynth.Hash(chunked) == MusicSynth.Hash(full), t.Id + ": output does not depend on read chunk size (PCM callback vs pre-render)");
    }
    var jumped = MusicSynth.Open(spec); jumped.SetPosition(loopSamples / 2); var part = new float[4096]; jumped.Read(part);
    var jumpDrift = 0.0; for (var i = 0; i < part.Length; i++) jumpDrift = Math.Max(jumpDrift, Math.Abs(part[i] - full[loopSamples / 2 + i]));
    Check(jumpDrift < 1e-3, t.Id + ": SetPosition resumes mid-loop (max drift " + jumpDrift.ToString("0.#####") + ")");
    synthHashes[t.Id] = MusicSynth.Hash(full);
    var capped = MusicSynth.LoopSteps(spec, MusicSynth.DefaultMaxPrerenderSeconds);
    var cappedSeconds = MusicSynth.LoopSeconds(spec, capped);
    Check((capped == 84 || capped == 28) && cappedSeconds <= MusicSynth.DefaultMaxPrerenderSeconds && (capped == 84) == (84 * t.CadenceMs / 1000.0 <= MusicSynth.DefaultMaxPrerenderSeconds), t.Id + ": WebGL pre-render loop " + capped + " steps = " + cappedSeconds.ToString("0.#") + " s");
    largestPrerenderBytes = Math.Max(largestPrerenderBytes, MusicSynth.LoopSamples(spec, capped) * 4L);
}
synthWatch.Stop();
Console.WriteLine("  rendered " + bedTracks.Length + " full loops (" + (totalSamples / 22050.0).ToString("0") + " s of audio) in " + synthWatch.Elapsed.TotalSeconds.ToString("0.0") + " s");
var again = MusicSynth.Render(MusicBedSpec.From(catalog, bedTracks[0].Id));
Check(MusicSynth.Hash(again) == synthHashes[bedTracks[0].Id], "rendering the same track twice gives the same hash");
Check(synthHashes.Values.Distinct().Count() == bedTracks.Length, "every bed renders to distinct audio");
Check(largestPrerenderBytes <= 11 * 1024 * 1024, "largest WebGL pre-rendered loop is " + (largestPrerenderBytes / 1048576.0).ToString("0.0") + " MB (float32 mono)");
var hashPath = Path.Combine(root, "UnityTests/Music/synth-hashes.json");
var hashJson = JsonSerializer.Serialize(synthHashes, new JsonSerializerOptions { WriteIndented = true }) + "\n";
if (args.Contains("--write-synth-hashes")) { File.WriteAllText(hashPath, hashJson); Console.WriteLine("Wrote UnityTests/Music/synth-hashes.json"); }
var pinned = File.Exists(hashPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(hashPath)) : new Dictionary<string, string>();
foreach (var (id, hash) in synthHashes)
    Check(pinned.TryGetValue(id, out var want) && want == hash, id + ": output hash " + hash + " matches the pinned synth-hashes.json (regenerate with --write-synth-hashes after an intended change)");
Check(JsonSerializer.Serialize(synthRng.Snapshot()) == synthRngBefore, "rendering every bed leaves run RandomStreams counters untouched");
Check(Throws(() => MusicBedSpec.From(catalog, "bed.nope.1")) && Throws(() => MusicBedSpec.From(withFiles, "file.combat.a")), "the synth refuses unknown tracks and file tracks");

// ---- decks: the adapter's crossfade/stop plan -------------------------------------------------
var dd = new MusicDirector(catalog, 21);
var decks = new MusicDecks();
List<MusicDeckAction> Run(IReadOnlyList<MusicCommand> commands) => commands.SelectMany(decks.Apply).ToList();
var acts = Run(dd.Enter(MusicScene.Title, 0));
Check(acts.Count == 1 && acts[0].Kind == MusicDeckActionKind.Start && acts[0].Deck == 0 && acts[0].Loop && decks.ActiveDeck == 0, "title starts on deck A");
var titleLevel = dd.CurrentVolume;
Check(Near(decks.VolumeAt(0, 0), floor) && Near(decks.VolumeAt(0, 1.5), titleLevel) && Near(decks.VolumeAt(0, 60), titleLevel), "deck A fades in over 1.5 s to the director volume");
acts = Run(dd.Enter(MusicScene.Boss, 10));
Check(acts.Count == 1 && acts[0].Deck == 1 && decks.IsFadingOut(0) && decks.ActiveDeck == 1, "boss crossfades onto deck B while A fades out");
Check(Near(decks.VolumeAt(0, 10), titleLevel) && decks.VolumeAt(0, 10.6) == 0 && Near(decks.VolumeAt(1, 11.5), dd.CurrentVolume), "crossfade: A 0.6 s out, B 1.5 s in");
var midB = decks.VolumeAt(1, 10.3);
acts = Run(dd.Enter(MusicScene.Map, 10.3));
Check(acts.Count == 2 && acts[0].Kind == MusicDeckActionKind.Stop && acts[0].Deck == 0 && acts[1].Kind == MusicDeckActionKind.Start && acts[1].Deck == 0, "an interrupting crossfade reclaims the still-fading deck");
Check(Near(decks.VolumeAt(1, 10.3), midB) && decks.VolumeAt(1, 10.5) < midB, "the interrupted deck fades out from its actual level (no jump)");
Check(decks.Tick(10.8).Count == 0 && decks.Tick(10.9).Count == 1 && decks.TrackOn(1) == null, "a finished fade-out stops its deck");
var mapLevel = dd.CurrentVolume;
acts = Run(dd.ApplySettings(new MusicSettings { MasterVolume = 100, MusicVolume = 80, MusicEnabled = true }, 20));
Check(acts.Count == 0 && Near(decks.VolumeAt(0, 20.1), dd.CurrentVolume) && Near(decks.VolumeAt(0, 20.05), (mapLevel + dd.CurrentVolume) / 2, 1e-4), "a volume change ramps the playing deck in place");
var mapTrackOnDeck = decks.TrackOn(0);
acts = Run(dd.ApplySettings(new MusicSettings { MasterVolume = 100, MusicVolume = 80, MusicEnabled = true, Muted = true }, 30));
Check(acts.Count == 0 && decks.ActiveDeck == -1 && decks.IsFadingOut(0), "mute fades the deck out");
Check(decks.Tick(30.31).Count == 1 && decks.VolumeAt(0, 30.31) == 0 && decks.VolumeAt(1, 30.31) == 0, "mute: both decks silent after 0.3 s");
var mixed = new float[22050]; var mapBed = MusicSynth.Open(MusicBedSpec.From(catalog, mapTrackOnDeck)); mapBed.Read(mixed);
for (var i = 0; i < mixed.Length; i++) mixed[i] *= decks.VolumeAt(0, 31 + i / 22050.0) + decks.VolumeAt(1, 31 + i / 22050.0);
Check(mixed.All(x => x == 0), "silence when muted: rendered bed x deck volume is exactly 0");
Check(Run(dd.Enter(MusicScene.Combat, 32)).Count == 0 && decks.TrackOn(0) == null && decks.TrackOn(1) == null, "muted: entering a context starts no deck");
var mutedDecks = new MusicDecks();
Check(new MusicDirector(catalog, 1, new MusicSettings { MusicVolume = 50, MasterVolume = 100, MusicEnabled = true, Muted = true }).Enter(MusicScene.Title, 0).SelectMany(mutedDecks.Apply).Count() == 0, "muted at start: no deck ever starts");
acts = Run(dd.ApplySettings(new MusicSettings { MasterVolume = 100, MusicVolume = 80, MusicEnabled = true }, 40));
Check(acts.Count == 1 && acts[0].Kind == MusicDeckActionKind.Start && acts[0].TrackId.StartsWith("bed.combat."), "unmute starts the current context on a deck");
decks.Reanchor(acts[0].Deck, 41);
Check(Near(decks.VolumeAt(acts[0].Deck, 41), floor), "a late clip (WebGL render) restarts its fade-in when it becomes ready");
acts = Run(dd.Enter(MusicScene.Death, 50));
Check(acts.Count == 0 && decks.Tick(50.61).Count == 1 && decks.ActiveDeck == -1, "death fades the deck out and stops it");

// ---- run phase to scene ------------------------------------------------------------------------
Check(MusicSceneMap.ForNativePhase("Map", null) == MusicScene.Map && MusicSceneMap.ForNativePhase("Combat", "normal") == MusicScene.Combat
    && MusicSceneMap.ForNativePhase("Combat", "elite") == MusicScene.Elite && MusicSceneMap.ForNativePhase("Combat", "boss") == MusicScene.Boss
    && MusicSceneMap.ForNativePhase("Rewards", "boss") == MusicScene.Rewards && MusicSceneMap.ForNativePhase("Shop", null) == MusicScene.Shop
    && MusicSceneMap.ForNativePhase("Shrine", null) == MusicScene.Shrine && MusicSceneMap.ForNativePhase("Event", null) == MusicScene.Event
    && MusicSceneMap.ForNativePhase("EventResult", null) == MusicScene.Event && MusicSceneMap.ForNativePhase("Draft", null) == MusicScene.Event
    && MusicSceneMap.ForNativePhase("Victory", "boss") == MusicScene.Victory && MusicSceneMap.ForNativePhase("Defeat", null) == MusicScene.Death, "native phases map to HTML music scenes");
Check(MusicSceneMap.ForCampaignPhase("Map") == MusicScene.Map && MusicSceneMap.ForCampaignPhase("Combat") == MusicScene.Combat && MusicSceneMap.ForCampaignPhase("Reward") == MusicScene.Rewards
    && MusicSceneMap.ForCampaignPhase("Victory") == MusicScene.Victory && MusicSceneMap.ForCampaignPhase("Defeat") == MusicScene.Death, "campaign phases map to music scenes");
var phaseNames = Regex.Match(Read("Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalRunSession.cs"), @"enum OriginalRunPhase \{([^}]*)\}").Groups[1].Value.Split(',').Select(p => p.Trim()).ToArray();
Check(phaseNames.Length == 10 && phaseNames.All(p => p is "Map" or "Combat" or "Rewards" or "Shop" or "Shrine" or "Event" or "EventResult" or "Victory" or "Defeat" or "Draft"), "every OriginalRunPhase has a music mapping");
foreach (var file in new[] { "MusicSynth.cs", "MusicPlayback.cs" })
{
    var src = Regex.Replace(Read("Unity/Assets/AshenSpire/Runtime/Domain/" + file), @"//.*", "");
    Check(!Regex.IsMatch(src, @"RandomStreams|new Random\(|System\.Random|UnityEngine"), file + " references no run RNG, System.Random or UnityEngine");
    Check(Exists("Unity/Assets/AshenSpire/Runtime/Domain/" + file + ".meta"), file + " has a Unity .meta file");
}
Check(Exists("Unity/Assets/AshenSpire/Runtime/Application/MusicPlayer.cs.meta"), "MusicPlayer.cs has a Unity .meta file");
var playerSource = Regex.Replace(Read("Unity/Assets/AshenSpire/Runtime/Application/MusicPlayer.cs"), @"//.*", "");
Check(!Regex.IsMatch(playerSource, @"RandomStreams|UnityEngine\.Random|System\.Random|new Random\("), "MusicPlayer.cs draws no random numbers");

if (failures.Count > 0) { Console.WriteLine("Music: " + failures.Count + " of " + (passed + failures.Count) + " checks FAILED"); return 1; }
Console.WriteLine("Music: " + passed + " checks passed");
return 0;

static bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }
