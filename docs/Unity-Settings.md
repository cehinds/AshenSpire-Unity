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
| Reduce flashes | `reduceFlashes` | on/off | off | HTML `reduceFlashes` default. Added in schema 2. |
| High contrast | `highContrast` | on/off | off | Keeps today's look. The HTML game defaults this **on**; Unity keeps it off until the high-contrast styles are tuned. Added in schema 2. |
| Colorblind palette | `colorblindPalette` | `none`, `protanopia`, `deuteranopia`, `tritanopia` | `none` | |
| Master / music / SFX / UI volume | `audio.master`, `audio.music`, `audio.sfx`, `audio.ui` | 0 – 1 | 1.0 each | Buses do not turn the sound down. Campaign sound tuning (`Audio.Volume`) still applies on top. |
| Mute | `audio.muted` | on/off | off | The existing toggle. `Gain(bus)` returns 0 while muted. |
| Music | `audio.musicEnabled` | on/off | on | Music off silences only the music bus. Added in schema 2. |
| Key bindings | `keyBindings` | action → key name | see below | |
| Load content mods | `loadContentMods` | on/off | **off** | Mods are opt-in and solo only; co-op always uses the shipped content. See [MODDING.md](MODDING.md). |
| Combat pacing | *(none: `animationSpeed` / `instantAnimations`)* | `slow`, `normal`, `fast`, `instant` | `normal` | HTML `animSpeed`. `CombatPacing` reads and sets the same speed fields (slow 0.5, normal 1, fast 2, instant), so it can never disagree with the slider. |
| Reward collection | `rewardCollect` | `auto`, `manual` | **`manual`** | HTML `balance.ui.rewardCollect` modes. Added in schema 3. **Owner decision (2026-10-02):** the Unity default is `manual` (only cinders are collected automatically), not the content def `auto`. See *Gameplay options* and *Migration*. |
| Merchant buys back | `shopSell` | on/off | on | HTML `shopSell`. Added in schema 3. |
| Weapon swap cost | `swapCostRule` | `flat`, `gear`, `category` | `flat` | HTML `balance.equipment.swapCostRules` / `swapCostRule`. Added in schema 3. |
| Fullscreen | `fullscreen` | on/off | off | HTML `fullscreen`. Added in schema 3. |
| UI size (HTML chips) | `uiSize` | `Auto`, `S`, `M`, `L`, `XL` | `Auto` | HTML `uiScale` chips. Unity's numeric `uiScale` slider above is separate and still drives `PanelSettings.scale`. Added in schema 3. |
| Accent color | `accent` | `gold`, `crimson`, `frost`, `verdant`, `violet` | `gold` | Added in schema 3. |
| Card motif / strength | `cardMotif`, `cardMotifStrength` | `off`, `wash`, `accent`, `band` / `subtle`, `normal`, `strong` | `wash` / `normal` | HTML `balance.ui.cardMotif(Modes)`. Added in schema 3. |
| Map header | `mapHeaderDensity`, `mapHeaderRelics`, `mapHeaderSeed` | `comfortable`, `compact` / on/off / on/off | `comfortable`, on, on | Added in schema 3. |
| Control hints | `controlHints` | on/off | on | Added in schema 3. |
| Gamepad bindings | `gamepadBindings` | action → button id | see below | Added in schema 3. |

Choices are matched without case and stored in the spelling above. An unknown choice keeps
the default and adds a note such as `swapCostRule 'both' is unknown; flat used`.

Default key bindings are exactly the keys the map board handles today, as Unity
`KeyCode` names: `mapScrollUp` = `PageUp`, `mapScrollDown` = `PageDown`, `mapTop` =
`Home`, `mapBottom` = `End`.

Default gamepad buttons (`OriginalGamepad.DefaultBindings`) follow the HTML `input.js`
`defBtn` wherever Unity has the same action: `combatPlay` = `south` (0), `cancel` = `east`
(1), `endTurn` = `west` (2), `combatDeck` = `north` (3), `menu` = `start` (9), `flask1` =
`leftTrigger` (6), `flask2` = `rightTrigger` (7), `flask3` = `leftStick` (10). The HTML
shoulders cycle tabs; Unity has no tab ring, so `targetPrevious` / `targetNext` use
`leftShoulder` / `rightShoulder`. `mapScrollUp` / `mapScrollDown` use `dpadUp` / `dpadDown`.
Other actions start unbound. Button ids are the 16 W3C standard-gamepad buttons in index
order (`south` … `dpadRight`).

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

`schemaVersion` is `3`. The storage key keeps its `v1` name so existing saves are found.
Loading checks these cases in order:

1. **A v1, v2 or v3 record is stored.** It is read with clamping. A schema-1 record has no
   `reduceFlashes`, `highContrast` or `audio.musicEnabled`; they get their defaults (off,
   off, on). A schema-1 or schema-2 record has none of the schema-3 fields (gameplay,
   display, `gamepadBindings`); they get the defaults in the table above. Either way one
   note is added (`migrated schema 1 to schema 3` or `migrated schema 2 to schema 3`) and
   the next save writes schema 3. A newer `schemaVersion` is still read field by field
   (known fields only), and a note is added. A non-boolean value or unknown choice in a new
   field falls back to its default with a note.
2. **Nothing is stored.** The old PlayerPrefs ints are migrated:
   `AshenSpire.ReducedMotion` = 1 turns on reduced motion.
   `AshenSpire.FastMotion` = 1 sets animation speed 2, which halves durations exactly like
   the old *Quick animations* (`CombatFeedback`: duration × 0.5).
   `AshenSpire.Muted` = 1 turns on mute. The optional music keys MusicPlayer used to read
   are migrated here too: `AshenSpire.MasterVolume` and `AshenSpire.MusicVolume` (0–100,
   values above 100 clamp) and `AshenSpire.MusicEnabled` (0/1). Nothing in the Unity build
   wrote them, and nothing reads them after this. Missing keys keep the defaults.
3. **The stored text is unreadable.** The same legacy migration runs, and a note says so.

**Reward collection default change (owner decision, 2026-10-02).** The default moved from
`auto` to `manual` without a schema bump. A record with no `rewardCollect` field (schema 1
or 2, or a schema-3 record missing it) means the player never chose, so it loads as
`manual`, silently. A stored value is kept: every schema-3 record a Unity build has written
carries `rewardCollect` explicitly (`ToJson` always writes it), so a stored `auto` cannot be
told apart from a deliberate choice and is never overridden. A player who had the old silent
`auto` default keeps it until they change the setting. Covered by `UnityTests/Mods`.

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

Gamepad bindings work the same way: `TryBindGamepad`, `BindGamepadSwapping`,
`ResetGamepadBindings()` and `GamepadConflicts()`. The difference is that the button set is
closed: an unknown button id is refused by `TryBindGamepad` and, in a saved record, keeps
the action's default with a note. Unknown actions are kept.

## Gameplay options (US-15.2)

The HTML game reads these from `meta.settings` while it plays. Unity runs read
`run.profileMeta.settings`, so `RunController.BindOriginal` calls
`OriginalGameSession.ApplyProfileSettings(OriginalGameplayOptions.ProfileSettings(settings))`
whenever a run is bound (new climb or Continue). That merges `shopSell` and `swapCostRule`
into the run, changes nothing else, and is a no-op when the values already match.
Settings is reached from the title screen only, so a change applies from the next new
climb or Continue.

- **Reward collection — applied.** `OriginalRunPanel.Rewards` resolves the mode with
  `OriginalGameplayOptions.RewardCollectMode` (the content dial's modes, like `reward.js`
  `collectMode`) and calls `OriginalGameSession.ContinueRewards(mode)`. *Auto* takes every
  pending, unskipped, unblocked reward in `REWARD_KIND_ORDER` and picks a card on the
  `cardRewards` stream, exactly like `rewardplan.resolveContinue` with `reward.js` `pickFn`.
  *Manual* takes only the pending, unskipped cinders and leaves every other unchosen reward
  behind. **Owner decision (2026-10-02): the default is manual** — after a fight only cinders
  are collected automatically; the card, relic, flask and armament are the player's to take
  or skip. The HTML game grants cinders on arrival (`reward.js` `grantCinders`); Unity grants
  them at Continue (`OriginalRunSession.ContinueRewards(autoCollect, collectCinders)`), and
  *Collect N cinders* still takes them early. An unset or unknown mode resolves to the Unity
  default `manual` when the content lists it (`reward.js` falls back to the content def
  `auto`; this is the one deliberate difference). In both modes each pending reward has a
  Skip control (`native-skip-reward-<kind>`; not `native-reward-*`, which the playtests
  treat as "take"), and an explicit skip is respected, including a skip of the cinders. The
  argument-free `OriginalGameSession.ContinueRewards()` still leaves everything, cinders
  included, for the domain replays; the compiled playtests that replay one use
  `NativeUiDriver.continueRewards()`, which skips every pending kind whose Skip control is
  shown first, so they keep their exact replay under either mode. Co-op rewards are unchanged.
- **Merchant buys back — applied.** `OriginalRunServices.Sellables` already returns no rows
  when `profileMeta.settings.shopSell` is false; the setting now reaches it.
- **Weapon swap cost — applied.** `OriginalCombatEquipment.Rule` already resolved
  `profileMeta.settings.swapCostRule` like `loadout.resolveSwapCostRule` (unknown id → the
  content default). The equipment screen in combat now shows one line per set from
  `OriginalGameplayOptions.DescribeSwapPrice` (rule label, base or category, gear delta,
  price). Difference from the HTML: the HTML freezes the rule when a fight starts; Unity
  freezes it at the fight's first swap (`equipmentSwapRule`). Because Settings is only
  reachable from the title, this shows only when a saved fight is continued after the rule
  was changed and before any swap.
- **Combat pacing — applied** (it is the existing animation speed; see below).

## Display options (US-15.1, data side)

All are saved and validated. **Fullscreen** is applied when the toggle changes
(`Screen.fullScreen`; on Web this needs the click that changes it). The others are applied
only as root USS classes, with no styles yet: `ui-size-auto|s|m|l|xl`, `accent-<name>`,
`card-motif-<mode>`, `motif-strength-<strength>`, `map-header-comfortable|compact`,
`map-header-no-relics`, `map-header-no-seed`, `no-control-hints`.
**Editor follow-up:** write the USS for those classes (accent tokens, card motif frames,
compact map header), decide how the `uiSize` chips relate to the numeric `uiScale` slider,
and hide the map-header relics/seed and any control-hint bar when their classes are set.

## Gamepad (US-15.3, domain side)

`OriginalGamepad` (Domain/Original) holds the button ids, the defaults and a pure resolver:
`Action(bindings, button, contextActions)` names the bound action for a press in a context
(map or combat action list, plus `cancel` / `menu` where they apply), and
`KeyName(action, keyBindings)` gives the keyboard key that action dispatches as (`cancel`
→ `Escape`, `menu` → none), so a pad press can enter the existing key-action path the way
HTML `input.js` does. The Settings screen lists a button choice per action
(`pad-<action>`, `pad-reset`) with the same conflict refusal.
**Not wired to input:** `Unity/Packages/manifest.json` does not reference
`com.unity.inputsystem`, and no package was added. Follow-up: add the Input System (or
read the legacy joystick axes), map each device control to an `OriginalGamepad` button id
(`FromStandardIndex` covers the standard layout), call `Action`, then raise the bound key
through `OriginalRunPanel.CombatTools` / `OriginalMapBoard.Key`, and the menu callback for
`menu`.

## Wiring (done; needs editor play test)

Hook lines in existing files are marked with a comment that names the new file.

| Where | What it does |
|---|---|
| `RunController.Settings.cs` (new) | `InstallPlayerSettings()` runs in `OnEnable` after the view exists. It calls `LoadOrMigrate`. The three legacy ints then win for reduced motion, quick animations and mute, because an older build on the same device may have changed them. Every change saves `AshenSpire.Settings.v1` **and** writes `AshenSpire.ReducedMotion`, `AshenSpire.FastMotion` (= `QuickAnimations`) and `AshenSpire.Muted`. So the older code paths (`RunController.Settings/Mute`, the `CampaignView` constructor) keep working. |
| `CampaignView.PlayerSettings.cs` (new) | The settings screen, grouped like the HTML game: **Game** (Quick animations, animation speed, instant, interface size, screen shake + intensity, hit-stop), **Audio** (Mute sound, master/SFX/music/interface volume), **Accessibility** (Reduced motion, Reduce flashes, High contrast, text size, colorblind palette), **Controls** (map keys, conflict message, reset), **Content mods**. The original toggles keep their names (`reduced-motion`, `fast-motion`, `mute-sound`) and labels; they are only moved under the headings. New control names: `animation-speed`, `instant-animations`, `ui-scale`, `screen-shake`, `screen-shake-intensity`, `hit-stop`, `volume-master`, `volume-sfx`, `volume-music`, `music-enabled`, `volume-ui`, `reduce-flashes`, `high-contrast`, `text-scale`, `colorblind-palette`, `key-mapScrollUp` … `key-mapBottom`, `keys-reset`, `load-content-mods`. Schema 3 adds, without new headings (the section jump ids `settings-section-0…5` are unchanged): in **Game** `combat-pacing`, `reward-collect`, `shop-sell`, `swap-cost-rule`, then a *Display* group `fullscreen`, `ui-size`, `accent-color`, `card-motif`, `card-motif-strength`, `map-header-density`, `map-header-relics`, `map-header-seed`, `control-hints`; in **Controls** `pad-<action>` and `pad-reset`. |
| `OriginalKeyBindings.cs` (new) | Turns bindings (Unity `KeyCode` names, case-insensitive) into map actions. `OriginalMapBoard.Key` asks `OriginalMapViewServices.KeyAction`. Without bindings it uses the old four keys. |
| `Resources/OriginalTheme.uss` | `high-contrast` on the root swaps the ink, secondary-text and edge colour tokens (the same idea as the HTML `body.hi-contrast`). Visual tuning is not done. |
| `FeelSettings.From` / `SpeedFor` (Domain, `FeelProfile.cs`) | Maps animation speed, Instant, Reduced motion, Reduce flashes, shake, intensity and hit-stop to the feel settings. `FeelDriver.Configure(OriginalPlayerSettings)` only forwards the fields. Tested in `UnityTests/Feel`. |
| `AudioBusLevels.From` (Domain/Original) | Music, SFX and interface levels: master × bus, 0 while muted, music 0 while music is off. `ApplyPlayerSettings` uses it for `GameAudio` and `MusicPlayer`. Tested in `UnityTests/Music`. |
| `Resources/OriginalPalette.uss` (new) | `palette-protanopia`, `palette-deuteranopia` and `palette-tritanopia` on the root re-color the health/stamina/mana pools, the card cost badges and warning text. |

What each setting does today:

- **Reduced motion, Mute:** the same flags as before.
- **Quick animations / animation speed / instant:** the HTML game has four pacing buckets
  (`fx.js` `ANIM_SPEEDS`: slow, normal, fast, instant), and Unity uses exactly those.
  `FeelSettings.SpeedFor` maps the slider: 50–99% → slow, 100–199% → normal, 200% → fast;
  Instant overrides. So 125% and 150% pace like 100%; the caption under the slider says so.
  The legacy flag is `QuickAnimations` = instant, or speed ≥ 2. Turning Quick animations on
  sets speed 2, and turning it off sets speed 1.
- **Reduce flashes:** turns off every feel motion marked `Flash` (the `fx.js` `flash()`
  group): the hit flash and recoil, the heal/guard/poison glow and the actor lunge/step.
  Damage numbers still play.
- **High contrast:** puts `high-contrast` on the root. Only colour tokens change.
- **Interface size:** `PanelSettings.scale` = the asset's own scale × `uiScale`. It is
  restored in `OnDisable`, so the shared asset is never left changed.
- **Text size:** each text element is scaled from its resolved USS size. Elements that size
  their own text in code (the title wordmark, the appearance name) are left alone. At 100%
  nothing is touched.
- **Volumes:** `AudioBusLevels.From(settings)` gives each bus. `GameAudio.SetVolumeScale(sfx)`
  multiplies the campaign tuning volume, and `SetInterfaceVolumeScale(ui)` sets interface
  sounds. Music gets `SetMuted(muted)` first (so mute fades the track out), then, unless
  muted, `ApplySettings(100, music bus %, music on)`. Mute is saved in the record and in
  `AshenSpire.Muted`, so it is still on after a restart.
- **Screen shake, hit-stop:** only saved. They will be used when those effects are built.
- **Key bindings:** a key that another action already uses is refused. The message names
  the action that holds it (`TryBind`). Conflicts in a saved record are shown when the
  screen opens. Esc cancels a capture.

Play test in the editor: open Settings from the title screen, change every control, then
restart. Turn on Reduce flashes and check that a hit no longer flashes the figure but the
number still pops. Turn on High contrast and check that captions get brighter. Turn Music
off and on, and mute then unmute: music should stop and come back at the same level. Check that the values persist and that the three legacy toggles still match. Rebind
a map key, then scroll the map with it. Pick each palette and look at a combat HUD.

Schema-3 play test: on a fresh profile (Reward collection Manual, the default), win a fight,
press Continue and check that the cinders and only what you took came along; skip the cinders
once and check they are left; set Auto, skip the card, press Continue and check that
the cinders and other rows were taken but no card. Turn Merchant buys back off and check the
merchant has no Sell rows. Set Weapon swap cost to Category, Continue a climb, open the
Armoury in a fight and read the swap price line for a heavy and a quick weapon. Toggle
Fullscreen on desktop and Web. Change a gamepad button to one already used and check the
refusal names the holder.

## Hold-to-confirm

Destructive actions (card removal, save overwrite/delete, loading over unsaved progress) use hold-to-confirm with the
original 600 ms hold; there is no player setting for its duration yet. See
[Unity-Confirmations.md](Unity-Confirmations.md).
