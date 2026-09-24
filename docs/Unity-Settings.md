# Unity customization settings

`OriginalPlayerSettings`
(`Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalPlayerSettings.cs`) holds every
player-facing customization option in one versioned record. It builds on the three
settings the Unity build already saves in PlayerPrefs: **Reduced motion**, **Quick
animations** and **Mute sound**. It is not a second, parallel settings system.

> **Status: domain ready, wiring pending (needs Unity editor).** The model, migration and
> tests are done (`dotnet run --project UnityTests/Mods`). `CampaignView`,
> `CombatFeedback`, `GameAudio` and `RunController` still read the old PlayerPrefs flags
> until they are switched over in the editor.

## Settings, ranges and defaults

The defaults match what the game does today, so switching to this model changes nothing
on screen or in sound.

| Setting | JSON field | Range | Default | Why this default |
|---|---|---|---|---|
| Text size | `textScale` | 0.8 – 1.6 | 1.0 | Current text size. |
| UI size | `uiScale` | 0.75 – 1.5 | 1.0 | Current layout. |
| Animation speed | `animationSpeed` | 0.5 – 2 | 1.0 | Full-length feedback. Duration is multiplied by `1 / speed`. |
| Instant animations | `instantAnimations` | on/off | off | Skips feedback timelines (`AnimationDurationScale` = 0). `"animationSpeed": "instant"` is accepted as shorthand. |
| Reduced motion | `reducedMotion` | on/off | off | The existing toggle. |
| Screen shake | `screenShake` | on/off | **off** | The Unity presentation has no shake today. |
| Shake intensity | `screenShakeIntensity` | 0 – 1 | 1.0 | Used only when shake is on. |
| Hit-stop | `hitStop` | on/off | **off** | The Unity presentation has no hit-stop today. |
| Colorblind palette | `colorblindPalette` | `none`, `protanopia`, `deuteranopia`, `tritanopia` | `none` | |
| Master / music / SFX / UI volume | `audio.master`, `audio.music`, `audio.sfx`, `audio.ui` | 0 – 1 | 1.0 each | Buses do not turn the sound down. Campaign sound tuning (`Audio.Volume`) still applies on top. |
| Mute | `audio.muted` | on/off | off | The existing toggle. `Gain(bus)` returns 0 while muted. |
| Key bindings | `keyBindings` | action → key name | see below | |

Default key bindings are exactly the keys the map board handles today, as Unity
`KeyCode` names: `mapScrollUp` = `PageUp`, `mapScrollDown` = `PageDown`, `mapTop` =
`Home`, `mapBottom` = `End`.

## Saving and loading

- Save `settings.ToJson().ToString()` under the PlayerPrefs key
  `OriginalPlayerSettings.StorageKey` (`AshenSpire.Settings.v1`).
- Load with the single entry point:

  ```csharp
  var settings = OriginalPlayerSettings.LoadOrMigrate(
      PlayerPrefs.GetString(OriginalPlayerSettings.StorageKey, ""),
      (key, fallback) => PlayerPrefs.GetInt(key, fallback),
      out var adjustments);
  ```

Loading never throws. Values that are out of range are pulled back to the nearest limit.
Values of the wrong type fall back to the default. Every change is added to
`adjustments` (for example `textScale 5 clamped to 1.6`), so it can be logged.

## Migration

`schemaVersion` is `1`. Loading checks these cases in order:

1. **A v1 record is stored.** It is read with clamping. A newer `schemaVersion` is still
   read field by field (known fields only), and a note is added.
2. **Nothing is stored.** The old PlayerPrefs ints are migrated:
   `AshenSpire.ReducedMotion` = 1 turns on reduced motion.
   `AshenSpire.FastMotion` = 1 sets animation speed 2, which halves durations exactly like
   the old *Quick animations* (`CombatFeedback`: duration × 0.5).
   `AshenSpire.Muted` = 1 turns on mute. Missing keys keep the defaults.
3. **The stored text is unreadable.** The same legacy migration runs, and a note says so.

A schema-0 JSON object is also accepted. It can use either the PlayerPrefs key names
(`{"AshenSpire.ReducedMotion":1, ...}`) or plain flags
(`{"reducedMotion":true,"fastMotion":true,"muted":true}`).

The old PlayerPrefs keys are not deleted. That way an older build on the same device
keeps working.

## Key-binding conflicts

- `TryBind(action, key, out conflictingAction)` refuses a key that another action
  already uses. It compares keys without case and ignores surrounding spaces, and it
  names the action that holds the key. It never takes the key away silently.
- `BindSwapping(action, key)` swaps: the action that held the key gets the old key of
  `action`.
- `KeyConflicts()` lists every key that is bound to more than one action. A hand-edited
  or old save can contain conflicts. They are kept as they are and reported in
  `adjustments`, so the settings screen can show them.
- Actions the build does not know are kept, so a newer build's bindings survive an
  older build.
- `ResetKeyBindings()` restores the defaults.

## Wiring it in (pending, needs Unity editor)

1. `RunController`: load with `LoadOrMigrate` and save with `ToJson` in place of the
   three `PlayerPrefs.SetInt` calls. Pass the settings object to `CampaignView` in place
   of `(reducedMotion, fast, muted)`.
2. `CombatFeedback.Play`: use `settings.AnimationDurationScale` in place of
   `(fast ? .5 : 1)`. When it is 0, finish at once.
3. `GameAudio`: `source.volume = tuning.Volume * settings.Gain("sfx")`.
4. UI Toolkit: apply `textScale` and `uiScale` to the root panel. Swap palette stylesheets
   by `colorblindPalette`.
5. Screen shake and hit-stop are new presentation features. Build them to read these
   settings when they are added.
