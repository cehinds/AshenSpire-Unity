# Unity music (F08)

**Integration status: wired; compile-verified against Unity reference assemblies; needs editor listening test**

Music in the Unity build is made in code, as it is in the HTML game. A pure-C#
**music director** decides what should play, when, and how loud. A pure-C#
**synthesizer** renders the HTML's procedural beds to PCM. A small
`MonoBehaviour` plays both on two `AudioSource`s. `RunController` drives it.
Nobody has listened to it in the editor or a player yet.

| Piece | Where | State |
|---|---|---|
| Track catalog (data) | `Unity/Assets/AshenSpire/Resources/Audio/music-catalog.json` | ready, generated |
| Catalog model + validation | `Unity/Assets/AshenSpire/Runtime/Domain/MusicCatalog.cs` | ready |
| Director (rules, crossfade, volume) | `Unity/Assets/AshenSpire/Runtime/Domain/MusicDirector.cs` | ready |
| Bed synthesizer (port of `audio.js` `playProcedural` + `drone`) | `Unity/Assets/AshenSpire/Runtime/Domain/MusicSynth.cs` | ready, tested |
| Deck plan (A/B crossfade, stop) + run phase → scene | `Unity/Assets/AshenSpire/Runtime/Domain/MusicPlayback.cs` | ready, tested |
| `AudioSource` adapter | `Unity/Assets/AshenSpire/Runtime/Application/MusicPlayer.cs` | wired, compile-verified only |
| `RunController` hooks | `Runtime/Application/RunController.Music.cs` + 8 one-line calls in `RunController.cs` | wired |
| Tests | `UnityTests/Music` (`dotnet run --project UnityTests/Music`) | 483 checks |
| Imported audio files | none exist | optional, see below |

## Synthesizer (`MusicSynth.cs`)

`MusicSynth` renders each catalog bed as mono float PCM at 22050 Hz, the same
rate `GameAudio` uses for sound effects. It uses no random numbers, so a track
always renders to the same samples. The output is at raw level. The context
gain, music bus, master and mute are applied as `AudioSource.volume`, at the
same points the HTML applies them.

What it reproduces from `src/ui/audio.js`:

- **Melody.** One note per cadence. The degree is
  `scale[(step*lift + (odd ? 2 : 0)) % len]`, and every fourth note is an
  octave up. The oscillator uses the variant's wave. Square and sawtooth are
  band-limited with PolyBLEP, which stands in for WebAudio's periodic waves.
  The envelope ramps exponentially 0.0001 → 0.16 by 0.08 s, then → 0.0001 by
  1.8 s. The note stops at 1.9 s.
- **Harmony.** On every note where `step % 3 == 1`, a sine at ×1.4983. It ramps
  to 0.07 by 0.12 s, decays by 1.6 s and stops at 1.7 s.
- **Pulse** (combat, elite and boss beds). A sine sweeping root/2 → root/3 over
  0.18 s, every `max(420, cadence/2)` ms. Its envelope peaks at 0.22 at 0.02 s
  and falls to 0.0001 at 0.32 s.
- **Drone.** Two sawtooths at root/2 and ×1.005, through a lowpass filter. The
  filter uses the WebAudio `BiquadFilterNode` formula with Q = 1 dB. A 0.07 Hz
  LFO sweeps its cutoff over 700 ± 260 Hz. The level is 0.12. The drone's own
  1.5 s ramp-in is carried by the director's bed fade-in.

**Loop length.** The HTML never loops. It plays notes forever, and the note
pattern repeats every 84 notes. That is the least common multiple of the
melodic period (14), the octave period (4) and the harmony period (3). One
Unity loop is therefore 84 × cadence, from 72.2 s (`bed.boss.4`, 860 ms) to
285.6 s (`bed.rest.3`, 3400 ms). To keep the seam click-free:

- The drone and LFO frequencies are nudged to a whole number of cycles per
  loop. The change is under 0.02 Hz.
- Notes that ring past the end of the loop wrap into its start.
- The filter state carries across the seam.

**Tests** (`UnityTests/Music`):

- Every bed renders its full loop.
- Output is finite, audible and below ±1 (no clipping). Peaks are 0.20 to
  0.52 (raw).
- The seam is continuous.
- Streaming and `SetPosition` match the pre-render.
- Output does not depend on the read chunk size.
- Every track's output hash matches `UnityTests/Music/synth-hashes.json`. The
  hash is FNV-1a over 16-bit samples. After an intended change, regenerate it
  with `dotnet run --project UnityTests/Music -- --write-synth-hashes`.
- Note frequencies match the `audio.js` formula.
- The `audio.js` literals the port depends on are still present.
- Run `RandomStreams` counters are unchanged by rendering.
- A muted mix is exactly silent.

## Playback (`MusicPlayer.cs`)

- **Two decks.** `MusicPlayer` owns two `AudioSource`s. `MusicDecks` turns each
  director command into start and stop actions on deck A or B. Every frame,
  `MusicPlayer` copies `VolumeAt(deck, Time.unscaledTimeAsDouble)` into
  `AudioSource.volume`. A fade that interrupts another fade starts from the
  deck's actual level. A deck whose fade-out has finished is stopped and its
  clip is released.
- **Desktop and mobile.** Each bed plays as an `AudioClip.Create(..., stream:
  true, PCMReaderCallback, PCMSetPositionCallback)`. Each clip has its own
  `MusicBedRenderer`, which holds about 3 KB of state. Nothing is
  pre-rendered, so memory use does not depend on loop length.
- **WebGL.** PCM callbacks are not supported on WebGL, so beds are
  pre-rendered into ordinary looping clips:
  - Rendering is time-sliced, about 4 ms per frame.
  - Loops are capped at 120 s (`MusicSynth.DefaultMaxPrerenderSeconds`). A bed
    whose 84-note loop is longer uses its 28-note melodic loop instead. Only
    the harmony pattern changes at that seam.
  - At 4 bytes per sample, the largest clip is 9.9 MiB (`bed.combat.3` and
    `bed.victory.1`, 84 × 1.4 s = 117.6 s). The smallest is 3.5 MiB (`bed.combat.1`, 28 × 1.5 s = 42 s).
  - The cache holds at most 240 s of audio (`PrerenderSampleBudget`, about
    20 MiB). It evicts the least recently used clip that no deck is playing.
  - An uncached bed starts once it is rendered, typically 2 to 4 s later. Its
    fade-in starts at that point. The outgoing bed still fades out on time.
- **Settings.**
  - Mute uses `AshenSpire.Muted`, the same key the sound-effects toggle uses.
  - Optional `PlayerPrefs` keys set the bus levels: `AshenSpire.MasterVolume`
    and `AshenSpire.MusicVolume` (0 to 100), and `AshenSpire.MusicEnabled`
    (0 or 1). No settings UI writes them yet.
  - When a key is absent, the catalog default applies: master 100, music 50,
    music on.
- **Hooks.**
  - `Menu()` plays Title.
  - `Refresh()` follows the foundation campaign phase.
  - `RefreshOriginal()` follows the native run phase and passes the combat
    pool and act (see `MusicSceneMap`).
  - `Mute()` forwards the mute setting.
  - Interruption and return pause and unpause both decks.
  - `OnDisable` stops the music with the 0.6 s quit fade.
  - Co-op screens keep the title bed.
  - Sound effects (`GameAudio`) are unchanged.
- **Still to do in the editor.** Listen to every context, check the crossfades
  and the mute fade, and check the WebGL render delay on a real build.

## What the catalog contains today

`music/` in this repository holds only `README.md` and an empty
`manifest.json`, so **there are no audio files to import yet**. The HTML game
makes all of its music in code: `src/content/music.js` defines 32 procedural
"bed" variants across 8 contexts, and `src/ui/audio.js` synthesizes them. The
catalog therefore lists:

- **32 bed tracks** (`bed.<context>.<n>`, `Kind: "bed"`). Each one records the
  music.js parameters (root, scale, cadence, wave, lift, drone, pulse), loops
  forever, and points at `src/content/music.js`. They are credited to the new
  CREDITS.md row *Procedural music beds* (AshenSpire, CC0).
- **0 file tracks** (`file.<context>.<n>`, `Kind: "file"`). One is generated
  for every entry the owner adds to `music/manifest.json`.

The catalog is generated, not hand-edited. The test fails if it drifts from
`music.js` or `manifest.json`:

```
dotnet run --project UnityTests/Music -- --write-catalog
```

### Credits are required per track

Validation fails any track that has no `Credit`, `Author` or `License`, whose
license is not CC0, CC BY 3.0 or CC BY 4.0, or whose `Credit` does not match
the first cell of a CREDITS.md table row. For owner tracks, add a `_credits`
object to `music/manifest.json`. The HTML game only reads the context keys, so
it ignores this object:

```json
{
  "combat": ["combat/ashen_duel.ogg"],
  "_credits": {
    "combat/ashen_duel.ogg": { "credit": "Ashen Duel", "author": "Jane Doe", "license": "CC BY 4.0" }
  }
}
```

Then add a CREDITS.md row whose first cell contains `Ashen Duel`, run
`--write-catalog`, and run the tests.

## Selection rules (copied from the HTML)

| Scene | Context | HTML source |
|---|---|---|
| Title, lobby, startup gate | `title` | `src/main.js` showLobby, showStartupGate, showTitle |
| Map, acts 1, 2, 3 and Endless | `map` (every act uses the same bed; `BEDS` has no per-act key) | `src/main.js` showMap |
| Combat / Elite / Boss | `combat` / `elite` / `boss`, picked from the encounter pool (`SceneForEncounter`) | `src/main.js` enterCombat |
| Shop | `shop` | `src/main.js` showShop |
| Shrine | `rest` | `src/main.js` showRest |
| Event | hold: the current (map) track keeps playing | `src/main.js` showEvent makes no music call |
| Rewards | hold: the battle track keeps playing | no music call after a combat win |
| Victory | `victory`, only after the act-3 boss outside Endless (`SceneAfterBoss`) | `src/main.js` `actNumber >= 3 && !endlessOn()` |
| Death | stop, 0.6 s fade | `src/main.js` `audio.stopMusic()` before `youDied` |
| Quit | stop, 0.6 s fade | `src/main.js` quit-without-saving, quitGame |

Within a context the director follows `src/ui/audio.js` `music()`:

- Re-entering the context that is already playing does nothing.
- Owner file tracks take priority over beds.
- It picks one track uniformly at random.
- A file track that ends is replaced by a new pick (`TrackEnded`).
- A file track that fails to load falls back to a bed (`TrackFailed`).
- A context marked `Silence` fades out and stays quiet.

## Crossfade and volume

The HTML never overlaps two tracks on purpose. It fades the old sound out over
0.6 s and ramps the new drone in over 1.5 s, both starting at the same moment.
The director turns this into one `Crossfade` command:

- `FadeOutSeconds` 0.6. The outgoing gain falls exponentially from its level to
  the floor `0.0001`, and it is exactly 0 at the end.
- `FadeInSeconds` 1.5 for beds and 0 for file tracks, which start at full
  level just as `<audio>.play()` does. The incoming gain rises exponentially
  from the floor to the target.
- `MusicCommand.IncomingGainAt(t)` and `OutgoingGainAt(t)` give the gain at any
  time, so the adapter can set `AudioSource.volume` every frame.

The volume formula is
`0.9 (HTML master headroom) × master/100 × music/100 × context gain`. The
context gain is `BEDS[ctx].gain`, and the HTML applies it to file tracks too.
The master slider is Unity-only and defaults to 100, which matches the HTML
level.

Mute and music-off send a `Stop` with a 0.3 s fade, as `setVolumes` does.
Turning either back on restarts the current context with a new pick.

**One deliberate difference from the HTML:** a volume slider change produces a
`SetVolume` command, a 0.1 s linear ramp on the same track. The HTML restarts
the bed instead. The director also clears the current context on a stop, so
re-entering the same context afterwards always plays. No HTML path depends on
this difference.

## Determinism

Picks come from a private mulberry32 stream. The caller passes its seed as
`shuffleSeed` and can restore its position with `shuffleDraws`. The director
never touches the run's `RandomStreams`, `System.Random` or
`UnityEngine.Random`, so music cannot change a run. The tests check this with
reflection, with a scan of the source, and by confirming that the counters of a
live `RandomStreams` are unchanged after a full script. The same seed and the
same inputs always produce the same command sequence.

## Command contract for the adapter

Each `MusicCommand` has these fields: `Kind` (`Play`, `Crossfade`, `Stop` or
`SetVolume`), `Context`, `TrackId`, `FromTrackId`, `ResourcePath` (file tracks
only), `Loop`, `AtSeconds`, `FadeInSeconds`, `FadeOutSeconds`, `FromVolume`,
`TargetVolume`, `FloorGain`, `Curve` and `Reason`.

The adapter should:

- Keep two `AudioSource`s (A/B) so a crossfade can play both tracks at once.
- Pass `Time.unscaledTimeAsDouble` as `now`.
- Report `TrackEnded` or `TrackFailed` back to the director.
- Fade from the source's actual current volume if a new command interrupts a
  fade that is still running.

## Owner steps in the Unity editor

1. **Catalog parsing: done.** `MusicPlayer` loads the catalog with
   `JsonUtility.FromJson<MusicCatalog>(Resources.Load<TextAsset>("Audio/music-catalog").text)`.
   It does not call `EnsureValid` in a player, because the repository paths
   are not present there. The tests run that validation.
2. **Adapter: done.** See `MusicPlayer.cs`, described above. There is no Music
   mixer group yet. The volume is set directly on the sources.
3. **Audible beds: done.** `MusicSynth` renders them. **Open step:** play
   through title, map, combat, elite, boss, shop, shrine, victory and death in
   the editor and in a WebGL build, and listen to each context and each
   crossfade.
4. **Import owner file tracks** once `music/manifest.json` lists any:
   - Copy each file `music/<context>/<name>.ogg` (or `.mp3`) to
     `Unity/Assets/AshenSpire/Resources/Audio/Music/<context>/<name>.ogg`. This
     is the catalog's `ResourcePath` with `Resources/` in front and the
     extension added. Keep the originals in `music/`, because the HTML reads
     them there.
   - Let the editor create the `.meta` files and commit them.
   - Use these import settings for every music clip, on all platforms:
     - Load Type **Streaming**
     - Compression Format **Vorbis**, Quality **70**
     - Preload Audio Data **off**
     - Load In Background **on**
     - Force To Mono **off**
     - Sample Rate Setting **Preserve Sample Rate**
     - Ambisonic **off**
   - For WebGL, Unity decodes through the browser. Keep the clips streaming
     and expect the first play to wait for a user gesture, as the HTML does.
   - Run `dotnet run --project UnityTests/Music -- --write-catalog`, then
     `dotnet run --project UnityTests/Music`.
5. **Backgrounding: wired.** `RunController.Interrupt` and
   `ReturnFromInterruption` call `MusicPlayer.SetSuspended`. It pauses and
   unpauses both sources and leaves the director state unchanged. This needs
   a device check against F08's "pauses on backgrounding" criterion.
6. **Settings: partly done.** Mute is shared with sound effects. Master and
   music volume and music on/off are read from `PlayerPrefs` at start. No
   settings UI writes them yet. When one is added, call
   `MusicPlayer.ApplySettings(master, music, enabled)`.
