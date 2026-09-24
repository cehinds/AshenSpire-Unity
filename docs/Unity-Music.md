# Unity music (F08)

**Integration status: director ready, AudioSource adapter + asset import pending (needs Unity editor)**

The Unity build has no music yet. This change adds the part that can be built
and tested without the editor: a pure-C# **music director** that decides what
should play, when, and how loud. It does not play sound. It returns a list of
commands that a Unity `AudioSource` adapter will carry out once someone writes
that adapter in the editor.

| Piece | Where | State |
|---|---|---|
| Track catalog (data) | `Unity/Assets/AshenSpire/Resources/Audio/music-catalog.json` | ready, generated |
| Catalog model + validation | `Unity/Assets/AshenSpire/Runtime/Domain/MusicCatalog.cs` | ready |
| Director (rules, crossfade, volume) | `Unity/Assets/AshenSpire/Runtime/Domain/MusicDirector.cs` | ready |
| Tests | `UnityTests/Music` (`dotnet run --project UnityTests/Music`) | 125 checks |
| `AudioSource` adapter (MonoBehaviour) | not written | pending, needs the editor |
| Bed synthesizer or rendered bed clips | not written | pending, see below |
| Imported audio files | none exist | pending, see below |

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

1. **Parse the catalog.** Load it with
   `JsonUtility.FromJson<MusicCatalog>(Resources.Load<TextAsset>("Audio/music-catalog").text)`.
   The field names are PascalCase public fields and are JsonUtility-compatible.
   Call `EnsureValid` in editor or dev builds only, because the repository
   paths are not present in a player.
2. **Write the adapter.** Create a MonoBehaviour, for example
   `Runtime/Application/GameMusic.cs`, next to `GameAudio.cs`. It should hold
   two `AudioSource`s with `playOnAwake = false`, route both to a Music mixer
   group or read settings directly, call `MusicDirector.Enter(...)` from the
   same places the HTML calls `audio.music`, and apply each command.
3. **Make the beds audible.** No bed audio files exist. Choose one option:
   - (a) Port `playProcedural` and `drone` from `src/ui/audio.js` into a
     `OnAudioFilterRead` or `AudioClip.Create` synthesizer, using the bed
     parameters in the catalog.
   - (b) Render each bed to a loopable `.ogg` offline and import it as a file
     track. Each rendered clip needs its own credit.

   Until one of these is done, beds produce commands but no sound.
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
5. **Handle backgrounding.** On pause or focus loss, pause both sources and
   leave the director state unchanged. On resume, unpause them. This meets
   F08's "pauses on backgrounding" acceptance criterion.
6. **Persist the settings.** Store master and music volume, music on/off and
   mute with the profile, and pass them in through `ApplySettings`.
