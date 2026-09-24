# Unity customization settings

`OriginalPlayerSettings`
(`Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalPlayerSettings.cs`) holds every
player-facing customization option in one versioned record. It builds on the three
settings the Unity build already saves in PlayerPrefs: **Reduced motion**, **Quick
animations** and **Mute sound**. It is not a second, parallel settings system.

> **Status: wired; compile-verified against Unity reference assemblies; needs editor play
> test.** The model, migration and tests are done (`dotnet run --project UnityTests/Mods`).
> `RunController.Settings.cs` loads and saves the record, and the settings screen
> (`CampaignView.PlayerSettings.cs`) shows every field. `node tools/unity-runtime-check.mjs`
> compiles it. Nobody has played it in the Unity editor or a player build yet.

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
| Load content mods | `loadContentMods` | on/off | **off** | Mods are opt-in and solo only; co-op always uses the shipped content. See [MODDING.md](MODDING.md). |

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

## Wiring (done; needs editor play test)

Hook lines in existing files are marked with a comment that names the new file.

| Where | What it does |
|---|---|
| `RunController.Settings.cs` (new) | `InstallPlayerSettings()` runs in `OnEnable` after the view exists. It calls `LoadOrMigrate`. The three legacy ints then win for reduced motion, quick animations and mute, because an older build on the same device may have changed them. Every change saves `AshenSpire.Settings.v1` **and** writes `AshenSpire.ReducedMotion`, `AshenSpire.FastMotion` (= `QuickAnimations`) and `AshenSpire.Muted`. So the older code paths (`RunController.Settings/Mute`, the `CampaignView` constructor) keep working. |
| `CampaignView.PlayerSettings.cs` (new) | The settings screen, grouped like the HTML game: **Game** (Quick animations, animation speed, instant, interface size, screen shake + intensity, hit-stop), **Audio** (Mute sound, master/SFX/music/interface volume), **Accessibility** (Reduced motion, text size, colorblind palette), **Controls** (map keys, conflict message, reset), **Content mods**. The original toggles keep their names (`reduced-motion`, `fast-motion`, `mute-sound`) and labels; they are only moved under the headings. New control names: `animation-speed`, `instant-animations`, `ui-scale`, `screen-shake`, `screen-shake-intensity`, `hit-stop`, `volume-master`, `volume-sfx`, `volume-music`, `volume-ui`, `text-scale`, `colorblind-palette`, `key-mapScrollUp` … `key-mapBottom`, `keys-reset`, `load-content-mods`. |
| `OriginalKeyBindings.cs` (new) | Turns bindings (Unity `KeyCode` names, case-insensitive) into map actions. `OriginalMapBoard.Key` asks `OriginalMapViewServices.KeyAction`. Without bindings it uses the old four keys. |
| `Resources/OriginalPalette.uss` (new) | `palette-protanopia`, `palette-deuteranopia` and `palette-tritanopia` on the root re-color the health/stamina/mana pools, the card cost badges and warning text. |

What each setting does today:

- **Reduced motion, Mute:** the same flags as before.
- **Quick animations / animation speed / instant:** `CombatFeedback` still only knows normal
  and quick (half duration). The legacy flag is `QuickAnimations` = instant, or speed ≥ 2.
  Other speeds are saved but do not change timing yet. Turning Quick animations on sets
  speed 2, and turning it off sets speed 1.
- **Interface size:** `PanelSettings.scale` = the asset's own scale × `uiScale`. It is
  restored in `OnDisable`, so the shared asset is never left changed.
- **Text size:** each text element is scaled from its resolved USS size. Elements that size
  their own text in code (the title wordmark, the appearance name) are left alone. At 100%
  nothing is touched.
- **Volumes:** `GameAudio.SetVolumeScale(master × sfx)` multiplies the campaign tuning volume.
  The Unity build has no music or interface sounds yet, so those two sliders are only saved.
- **Screen shake, hit-stop:** only saved. They will be used when those effects are built.
- **Key bindings:** a key that another action already uses is refused. The message names
  the action that holds it (`TryBind`). Conflicts in a saved record are shown when the
  screen opens. Esc cancels a capture.

Play test in the editor: open Settings from the title screen, change every control, then
restart. Check that the values persist and that the three legacy toggles still match. Rebind
a map key, then scroll the map with it. Pick each palette and look at a combat HUD.
