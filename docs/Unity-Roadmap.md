# AshenSpire Unity roadmap

Build the original AshenSpire in Unity, preserving its painterly identity and
content while improving phone play. Current source: **0.0.14.0 · build 14 ·
Foundation in progress** (`GameContent/Unity/version.json`). Published channels
may still carry earlier checkpoints.

The HTML game (`index.html`, `src/`, `content/`, `assets/`, `styles/`,
[SPEC.md](../SPEC.md)) is the reference. The Unity port lives in `Unity/`
(domain C# in `Unity/Assets/AshenSpire/Runtime/Domain{,/Original}`, presentation
in `Runtime/Presentation`, UI Toolkit). Version numbers follow
[Unity-Versioning.md](Unity-Versioning.md): **B** counts completed features in
this tracker, **C** counts user stories inside the feature in progress.
Handoff for a fresh agent: [CONTINUE-HERE.md](CONTINUE-HERE.md). How to play:
[PLAYER-GUIDE.md](PLAYER-GUIDE.md).

## Feature tracker

Status is `todo`, `in-progress` or `done (<version>)`. A feature is `done` only
after its acceptance criteria pass on a compiled build **and** the owner accepts
it; code that exists but is unaccepted is `in-progress`. Order is by the biggest
improvement in playable feel first, respecting dependencies (F00 gates all).

| ID | Feature | Status | Version | Evidence |
|---|---|---|---|---|
| F00 | Foundation (native engine, content import, build/package pipeline) | in-progress | 0.0.14.0 | [detail](#foundation-f00-detailed-acceptance) · [parity](Unity-Parity.md) · `Published/build.json` |
| F01 | Title, profile & settings | in-progress | — | `OriginalTitlePanel.cs` · `CampaignView.cs` (Settings, Chronicle) · [visual](Unity-Visual-Parity.md) |
| F02 | Class select & previews | in-progress | — | `OriginalFoundationPanel.cs` · `CreationModel.cs` · `OriginalStartingOptions.cs` |
| F03 | Combat core (hand, actions, targets, piles) | in-progress | — | `OriginalRunPanel.cs` · `OriginalCardView.cs` · `CombatSession*.cs` · [readability](Unity-Combat-Readability.md) |
| F04 | Enemy AI & telegraphs (intents, Poise/Stagger) | in-progress | — | `CombatSession.EnemyAi.cs` · `OriginalCombatLayout.Enemy` · merged: [#49](https://github.com/cehinds/AshenSpire-Unity/pull/49) telegraph view-model |
| F05 | Branching map & fog | in-progress | — | `OriginalMapBoard.cs` · `OriginalMapKnowledge.cs` · [map](Unity-Map-Foundation.md) |
| F06 | Rewards, cinders, relics, equipment & flasks | in-progress | — | `OriginalRunPanel.cs` (Reward/Deck) · `WeaponLoadout.cs` · `FlaskChargePool.cs` |
| F07 | Juice pass (animation, hit feedback, transitions) | in-progress | — | `CombatFeedback.cs` · `NativeFeedbackProjection.cs` · `OriginalPlayerFigure.cs` · merged: [#51](https://github.com/cehinds/AshenSpire-Unity/pull/51) feel profile |
| F08 | Audio & music | in-progress | — | `Runtime/Application/GameAudio.cs` (procedural cues only; no music) · merged: [#46](https://github.com/cehinds/AshenSpire-Unity/pull/46) music director |
| F09 | Merchant, shrine & events | in-progress | — | `OriginalRunServices.cs` · `OriginalRunPanel.cs` (Shop/Shrine/Event) |
| F10 | Run, seed & save slots | in-progress | — | `OriginalSaveJournal.cs` · `RandomStreams.cs` · `RunController.cs` · merged: [#52](https://github.com/cehinds/AshenSpire-Unity/pull/52) save slots |
| F11 | Death, victory & stats | in-progress | — | `OriginalRunPanel.cs` (Victory/Defeat) · `OriginalProfile.cs` · merged: [#50](https://github.com/cehinds/AshenSpire-Unity/pull/50) run summary |
| F12 | Acts 1–3 & bosses (plus Custom Climb/Endless) | in-progress | — | `OriginalRunRules.cs` · `OriginalCustomRunRules.cs` · full-climb 657 checks |
| F13 | Accessibility & phone layout | in-progress | — | `ViewportLayout.cs` · `DisplayViewport.cs` · reduced-motion toggle |
| F14 | Co-op (LAN companion) | in-progress | — | `OriginalCoopRun*.cs` · `OriginalCoopPanel.cs` · `tools/NativeLan/` |
| F15 | Customization settings & controls | in-progress | — | HTML `src/ui/screens/settings.js`, `controls.js`; Unity has 3 toggles only · merged: [#48](https://github.com/cehinds/AshenSpire-Unity/pull/48) settings model |
| F16 | Content modding & data packs | in-progress | — | `tools/original-table.py` · `UnityTests/Authoring` · `UnityTests/OriginalAuthoring` · merged: [#48](https://github.com/cehinds/AshenSpire-Unity/pull/48) mod packs |
| F17 | Performance & platform polish | todo | — | no budgets measured; no physical device/iOS evidence |

### Parity diff summary (HTML reference → Unity build 14)

Evidence-based survey of `src/ui/screens/*.js`, `src/ui/components/*.js`,
`src/engine/*.js`, `src/content/*` and `SPEC.md` against
`Unity/Assets/AshenSpire/Runtime/**`. "Exists" means implemented in source; it
is not owner acceptance.

- **Exists, broadly complete in domain:** content import (182 cards, 55 relics,
  50 statuses, 19 enemies, 21 encounters, 22 events, 7 flasks, 4 classes);
  RNG, formulas, statuses, combat turns, enemy AI, equipment/smithing/mounts,
  three acts, rewards, shops, shrines, events, Custom Climb/Ascension, Sealed,
  Draft, Endless, profile unlocks, checksummed saves and native LAN co-op
  (`Runtime/Domain/Original/*.cs`, see [Unity-Parity.md](Unity-Parity.md)).
- **Exists, presentation partial:** title (`OriginalTitlePanel.cs`), creation
  (`OriginalFoundationPanel.cs`), map with fog/Sealstone (`OriginalMapBoard.cs`),
  combat HUD/hand (`OriginalCombatLayout.cs`, `OriginalRunPanel.cs`), Chronicle
  (`CampaignView.cs` ~l.185). Rewards/inventory and exact HUD arrangement are
  not yet matched to the reference ([visual parity](Unity-Visual-Parity.md)).
- **Missing or thin in Unity versus the HTML reference:**
  - Settings: HTML `settings.js` offers ~40 options (music/SFX volume, custom
    music folder, text size, high contrast, colorblind-friendly, reduce
    flashes, minimum tap size, hold-to-confirm, screen shake, accent colour,
    card motif, map header, merchant buy-back, reward collection…). Unity
    `CampaignView.Settings` has three: Reduced motion, Quick animations, Mute.
  - Controls: HTML `src/ui/input.js` binds keyboard and gamepad (Enter, Esc,
    E end turn, M menu, D/R/T armoury, F/G/H flasks, I inspect, `[`/`]` tabs)
    with rebinding in `controls.js`. Unity has pointer/touch buttons plus map
    PageUp/PageDown/Home/End and Ctrl+wheel zoom (`OriginalMapBoard.Key/Wheel`);
    no combat hotkeys, gamepad or rebinding.
  - Music: HTML ships a procedural score plus `music/manifest.json` overrides
    (`src/ui/audio.js`, `src/content/music.js`). Unity `GameAudio.cs` generates
    short procedural cues only; there is no music and no volume slider.
  - Save slots: HTML `title.js`/`saveSlotSelector.js` provide load/new slots and
    `profileArchive.js` a profile archive. Unity keeps one native run per
    channel (Continue) with a checksummed backup; no slot picker.
  - Poise meter: SPEC §4.4 shows a Poise meter under enemy HP. Unity domain
    tracks poise (`CombatSession.cs`, `StatusSystem.cs`) but
    `OriginalCombatLayout.Enemy` shows only intent text, HP, Guard and statuses.
    Intents are text, not icons.
  - Death/victory: HTML `gameover.js` shows a run summary (seed, floor, cinders,
    kills, deck). Unity shows a title line and one sentence
    (`OriginalRunPanel.cs` Victory/Defeat); history lives in Chronicle.
  - Startup gate, tutorial, Compendium, About/AI disclosure: HTML has
    `startupGate.js`, `tutorial.js`, `compendium.js`, `about.js`. Unity has a
    developer compendium inside the foundation preview only, no tutorial and
    no in-game About screen (a Settings "How to play" paragraph exists).
  - Card drag-to-target (`tools/card-drag-targeting.mjs` in HTML): Unity uses
    tap card → tap target → Play, which is the phone-first design; drag is not
    required by the build brief.
  - Performance budgets, physical Android, graphical Windows and iOS: not
    measured or built ([visual parity](Unity-Visual-Parity.md)).

## Feature detail

Each section lists the player-facing goal, user stories (US-x.y), acceptance
criteria, reference files and tests. "Reference" paths are the HTML game;
"Unity" paths are relative to `Unity/Assets/AshenSpire/Runtime`. Test lists name
existing `UnityTests/*` projects or `tools/*playtest*.cjs` scripts, or say
"to write".

### F00 — Foundation

**Goal:** a player can start, play and finish a faithful three-act native climb
in the compiled Web/Windows/Android player, and the owner can edit content,
build and verify it. Completion selects `0.1.0.0`.

User stories (open work, from the detailed checklist below):

- US-0.1 Extend compiled painted-enemy coverage from hounds/wisp to all 19
  enemies and obtain owner visual acceptance.
- US-0.2 Run current-source compiled Custom/Sealed/Draft/Endless interaction
  and save/resume checks.
- US-0.3 Check card numbers, target availability, affordability, rejection
  recovery and result feedback in phone and desktop flows.
- US-0.4 Cover original settings/inventory flows not exercised by current commands.
- US-0.5 Current-source multi-browser co-op: friendly targets, catch-up, host
  restart, duplicate retry and rejoin via the packaged companion.
- US-0.6 Profile corruption, quota exhaustion and upgrade coverage; explicit
  decision on original JavaScript save import.
- US-0.7 Finish field/schema/runtime authoring coverage.
- US-0.8 Verify hosted build/history links, downloads and screenshots; expand
  archive hosting capacity without deleting archived players.
- US-0.9 Armour/tint visual acceptance and co-op pose feedback.
- US-0.10 Record remaining defects and owner acceptance, then select `0.1.0.0`.

Acceptance: every unchecked item in
[Foundation (F00) detailed acceptance](#foundation-f00-detailed-acceptance) is
checked with source-matched evidence or explicitly waived by the owner.
Reference: [SPEC.md](../SPEC.md), `src/engine/*.js`, `src/content/*`,
[Unity-Build-Brief.md](Unity-Build-Brief.md), [UNITY-SPEC.md](UNITY-SPEC.md).
Tests: all `UnityTests/*` projects run in `.github/workflows/unity-pages.yml`;
`tools/native-playtest.cjs`, `native-features-playtest.cjs`,
`native-enemy-art-playtest.cjs`, `native-coop-ci.cjs`,
`native-visual-parity-playtest.cjs`; `node tools/unity-package.mjs --check`.

### F01 — Title, profile & settings

**Goal:** opening the game feels like the original: a painted, calm title, a
clear Continue, and a settings screen that saves.

- US-1.1 As a returning player I see Continue first and resume my run in one tap.
- US-1.2 As a player I open Collection/Chronicle and see my wanderers, runs and unlocks.
- US-1.3 As a player I change sound, motion and animation speed and it persists.
- US-1.4 As a first-time player a startup gate/press-to-begin avoids an accidental menu tap (SPEC §7.1).
- US-1.5 As a player I can read an About screen with the AI-use acknowledgement (SPEC §2.1).

Acceptance: title matches the reference composition at 320×640, 390×844 and
1440×900 with no clipped wordmark; Continue disabled when no run exists;
settings persist across reload; Chronicle lists history and unlock hints.
Reference: `src/ui/screens/title.js`, `settings.js`, `history.js`, `about.js`,
`profileArchive.js`, `profileNotice.js`, `src/ui/components/startupGate.js`,
`intro.js`, `styles/ui.css`, `styles/base.css`, `assets/bg/`.
Unity: `Presentation/OriginalTitlePanel.cs`, `CampaignView.cs` (Title, Settings,
Chronicle), `Resources/OriginalTheme.uss`, `Domain/Original/OriginalProfile.cs`.
Tests: `tools/native-visual-parity-playtest.cjs`, `tools/campaign-playtest.cjs
--feedback-only`, `tools/native-playtest.cjs` (Chronicle); startup gate/About: to write.

### F02 — Class select & previews

**Goal:** choosing a class is inviting and informative: portrait, resources,
starting kit and attributes are visible before committing.

- US-2.1 Pick one of Reaver, Starseer, Rogue or Herald beside a framed preview.
- US-2.2 Assign 35 points (all attributes start at 5, max 15) or use Standard presets, and see each point's benefit.
- US-2.3 Choose discovered starting kits, alternate hands, wardrobe/relic and appearance (Animated/Rendered/Classic/Sigil, tint, sigil).
- US-2.4 Enter a seed and start; phone layout keeps Begin reachable.

Acceptance: all four classes render at phone/desktop sizes; allocation bounds
and remaining budget are enforced; the chosen kit, relic and appearance are
saved and restored.
Reference: `src/ui/screens/customize.js`, `src/ui/components/creationCards.js`,
`statAllocationCard.js`, `seedfield.js`, `src/content/classes.js`,
`src/model/characterCreation.js`, `assets/poses/`, `assets/sprites/`.
Unity: `Presentation/OriginalFoundationPanel.cs`, `OriginalAppearance.cs`,
`OriginalPlayerFigure.cs`, `Resources/OriginalCreation.uss`,
`Domain/Original/CreationModel.cs`, `OriginalStartingOptions.cs`, `OriginalCharacterBuilder.cs`.
Tests: `UnityTests/Domain`, `UnityTests/SpriteStyles`, `tools/NativeLan/Tests/StartingChoicesChecks`,
`tools/native-appearance-playtest.cjs`, `tools/native-visual-parity-playtest.cjs`.

### F03 — Combat core

**Goal:** a turn feels fast and legible: pick a card, pick a target, play; costs
and shortages are obvious; the hand is readable on a phone.

- US-3.1 Select a card and target, see the exact cost (actions/MP/stamina) and play it.
- US-3.2 An unaffordable card stays inspectable and Play explains the shortage.
- US-3.3 Browse a long hand and read long descriptions without losing Play/End turn.
- US-3.4 Inspect draw, discard and exhaust piles.
- US-3.5 Use Catch Breath (solo) and paid set switches with visible costs.
- US-3.6 Keyboard shortcuts for Play, End turn, flasks and cancel (see F15 for rebinding).

Acceptance: card numbers match `OriginalCardText` for all 364 definitions; the
same `Resolve/Cost` source drives cards, Play label and HUD; no action is lost
on reload; long-card reading passes at 320×640.
Reference: `src/ui/screens/combat.js`, `src/ui/components/hand.js`, `card.js`,
`piles.js`, `resbars.js`, `hudmeta.js`, `src/engine/combat.js`, `actions.js`,
`styles/combat.css`, SPEC §4.1–4.3, §7.2–7.3.
Unity: `Presentation/OriginalRunPanel.cs`, `OriginalCardView.cs`,
`OriginalCombatLayout.cs`, `Resources/OriginalCards.uss`, `OriginalCombat.uss`,
`Domain/Original/CombatSession*.cs`, `CardZoneLedger.cs`, `ResourceWallet.cs`.
Tests: `UnityTests/Parity`, `CardText`, `CardCosts`, `tools/native-card-cost-playtest.cjs`,
`native-long-card-playtest.cjs`, `native-playtest.cjs`; pile inspection & hotkeys: to write.

### F04 — Enemy AI & telegraphs

**Goal:** you always know what every enemy will do next and how close it is to
breaking.

- US-4.1 Each enemy shows its intent (attack total incl. multi-hit, block, buff, debuff, unknown, staggered).
- US-4.2 Each enemy shows a Poise meter under HP; filling it Staggers the enemy (skip turn, +50% damage).
- US-4.3 Intents use icons plus numbers, recomputed live when statuses change.
- US-4.4 Tapping an enemy shows its statuses and what they do.

Acceptance: intent numbers equal the engine's damage preview; Poise meter and
Stagger state are visible and correct in solo and co-op; icons readable at 320×640.
Reference: `src/ui/components/combatantFrame.js`, `combatantInspector.js`,
`battlefieldStage.js`, `stature.js`, `src/engine/statuses.js`,
`src/content/statuses.js`, SPEC §4.4, §4.6.
Unity: `Domain/Original/CombatSession.EnemyAi.cs`, `StatusSystem.cs`,
`Presentation/OriginalCombatLayout.cs` (`Enemy`), `OriginalEnemyFigure.cs`.
Tests: `UnityTests/Parity` (combat fixtures), `tools/native-enemy-art-playtest.cjs`;
Poise meter/intent icons: to write.

### F05 — Branching map & fog

**Goal:** the map is a place you want to read: branching routes, fog you push
back, shrines that glow.

- US-5.1 See the act's branching graph, current node, travelled path and lit choices.
- US-5.2 Solo fog hides unseen rooms; a Sealstone Key reveals rooms; the nearest shrine can glow.
- US-5.3 Pan, zoom, Fit and Recenter by touch, mouse wheel or keys; preferences persist.
- US-5.4 Open a Routes list and a Key legend.

Acceptance: 558-check map suite and minimum-zoom rerun pass on the current
build; parchment styling matches the reference.
Reference: `src/ui/screens/map.js`, `src/ui/components/mapboard.js`,
`actRouteStrip.js`, `src/engine/mapgen.js`, `actmap.js`, `styles/map.css`,
`assets/map/parchment_act{1,2,3}.svg`.
Unity: `Presentation/OriginalMapBoard.cs`, `OriginalMapViewServices.cs`,
`Domain/Original/ActMapGenerator.cs`, `OriginalMapKnowledge.cs`, `OriginalMapViewport.cs`.
Tests: `UnityTests/MapKnowledge`, `MapViewport`, `MapShape`, `tools/native-map-playtest.cjs`,
`native-map-shape-playtest.cjs`; parchment styling comparison: to write.

### F06 — Rewards, cinders, relics, equipment & flasks

**Goal:** winning feels rewarding: clear spoils, meaningful equipment choices,
flasks you plan around.

- US-6.1 After a fight collect cinders, a relic, a flask or an armament individually.
- US-6.2 Equip prepared sets, improve (smith) and extract/install weapon cards between fights.
- US-6.3 Drink Crimson/Azure charges and utility flasks in combat, with friendly targets in co-op.
- US-6.4 Reward and inventory screens match the reference layout.

Acceptance: every reward kind claims exactly once and survives reload; equipment
changes preserve resource deficits; flask charges match the shared pool.
Reference: `src/ui/screens/reward.js`, `equipment.js`, `smithServices.js`,
`src/ui/components/flask.js`, `armouryComponents.js`, `smithUpgradeModal.js`,
`mountServiceModal.js`, `src/content/relics.js`, `equipment.js`, `flasks.js`, `assets/equipment/`, `assets/ui/flasks/`.
Unity: `Presentation/OriginalRunPanel.cs` (Reward, Deck, Equipment, Mounts),
`Domain/Original/WeaponLoadout.cs`, `ItemUpgradeService.cs`, `CardMountService.cs`,
`FlaskChargePool.cs`, `OriginalRewardAvailability.cs`.
Tests: `UnityTests/Parity`, `tools/native-features-playtest.cjs`; reward-layout parity: to write.

### F07 — Juice pass

**Goal:** hits land: anticipation, impact, recovery, damage numbers and
transitions make each action satisfying, without slowing the game.

- US-7.1 Attacks play anticipation/impact/recovery poses for all four renderer styles.
- US-7.2 Damage, guard, healing and status numbers float from actual result receipts.
- US-7.3 Screen and room transitions; enemy hit/death reactions.
- US-7.4 Reduced motion and Quick animations respected everywhere.

Acceptance: feedback never changes simulation results; reduced motion removes
meaningful motion; no dropped frames on target phones (see F17).
Reference: `src/ui/fx.js`, `motion.js`, `reaverAttack.js`, `src/ui/services/PoseAnimator.js`,
`src/ui/components/holdbeat.js`, `veil.js`, `assets/animations/`, SPEC §7.4.
Unity: `Presentation/CombatFeedback.cs`, `NativeFeedbackProjection.cs`,
`OriginalPlayerFigure.cs`, `OriginalEnemyFigure.cs`.
Tests: `UnityTests/NativeFeedback`, `tools/native-appearance-playtest.cjs`; transitions/death reactions: to write.

### F08 — Audio & music

**Goal:** the Spire sounds alive: per-screen music and distinct combat sounds,
with volume control.

- US-8.1 Distinct SFX for attack, guard, hit, reward, UI (procedural cues exist).
- US-8.2 Per-context music (title, map, combat, boss) with smooth transitions.
- US-8.3 Separate music and SFX volume sliders; mute persists.
- US-8.4 Owner can drop replacement tracks into a folder (reference `music/`).

Acceptance: audible on Web after first interaction, Windows and Android; pauses
on backgrounding; volume persists.
Reference: `src/ui/audio.js`, `sfx.js`, `src/content/music.js`, `sfx.js`,
`music/README.md`, `music/manifest.json`, `tools/music-*.mjs`, `tools/sfx-*.mjs`.
Unity: `Application/GameAudio.cs`, `Domain/CampaignDefinition.cs` (`SoundDefinition`).
Tests: `UnityTests/AudioPreview`, `tools/campaign-playtest.cjs --feedback-only`; music and volume: to write.

### F09 — Merchant, shrine & events

**Goal:** non-combat rooms offer real decisions.

- US-9.1 Merchant: buy cards, relics and flasks; sell eligible relics/utility flasks; remove a card.
- US-9.2 Shrine: refill flasks on arrival, rest, reallocate Crimson/Azure, buy level points, or leave without resting.
- US-9.3 Events: all 22 events and 62 choices reachable with results shown.

Acceptance: prices/refusals match the domain; every event choice reachable in a
compiled run; purchases survive reload.
Reference: `src/ui/screens/shop.js`, `rest.js`, `event.js`, `src/content/events.js`,
`src/model/gracerefill.js`, `styles/ui.css`.
Unity: `Presentation/OriginalRunPanel.cs` (Shop, Shrine, Event),
`Domain/Original/OriginalRunServices.cs`, `OriginalRunContent.cs`.
Tests: `UnityTests/Parity` (run fixtures), `tools/native-features-playtest.cjs`; all-event compiled sweep: to write.

### F10 — Run, seed & save slots

**Goal:** a run is never lost and can be shared by seed.

- US-10.1 Enter or copy a seed; the same seed gives the same map and offers.
- US-10.2 Autosave after each accepted command; Continue restores exact state.
- US-10.3 Multiple save slots with a load/new picker.
- US-10.4 Recover from corrupted or full storage without losing the backup.

Acceptance: exact resume comparisons pass; slots isolated per channel; quota
and corruption cases handled.
Reference: `src/engine/save.js`, `rng.js`, `src/ui/components/saveSlotSelector.js`,
`seedfield.js`, `src/ui/screens/title.js`, SPEC §3.11–3.12.
Unity: `Domain/Original/OriginalSaveJournal.cs`, `RandomStreams.cs`,
`Application/RunController.cs`, `CampaignSaveStore.cs`, `InterruptionState.cs`.
Tests: `UnityTests/Interruption`, `UnityTests/Playthrough`, `tools/interruption-playtest.cjs`,
`tools/unity-upgrade-playtest.cjs`; save slots: to write.

### F11 — Death, victory & stats

**Goal:** a run ends with a moment and a story: what you reached and why.

- US-11.1 Death screen ("ash returns to ash") and victory screen with art and motion.
- US-11.2 Run summary: class, seed, floor, cinders, kills, deck, cause of death.
- US-11.3 Chronicle records the run and shows new unlocks immediately.

Acceptance: summary numbers match the saved run; unlocks earned appear on the
next creation screen.
Reference: `src/ui/screens/gameover.js`, `history.js`, `src/engine/save.js` (`recordResult`).
Unity: `Presentation/OriginalRunPanel.cs` (Victory/Defeat), `CampaignView.cs` (Chronicle),
`Domain/Original/OriginalProfile.cs`.
Tests: `tools/native-playtest.cjs` (victory + Chronicle); run summary: to write.

### F12 — Acts 1–3 & bosses

**Goal:** three distinct acts with escalating enemies and memorable bosses, plus
Custom Climb, Sealed/Draft and Endless.

- US-12.1 Act backgrounds and encounters for all three acts; bosses The Fell Warden (Act 1), The Stitched King (Act 2) and The Blighted Valkyrie (Act 3), and elites Wyrm Aspirant, Duelist of the Court and Wyrm Lord, on screen.
- US-12.2 Custom Climb with Ascension 0–6 and modifiers; Sealed and Draft starts.
- US-12.3 Endless cycles past Act 3.

Acceptance: compiled three-act victory on the current build; each boss rendered
and fought in a compiled check; custom modes save/resume.
Reference: `src/content/enemies/act{1,2,3}.js`, `src/content/encounters/act{1,2,3}.js`,
`src/content/mapconfig.js`, `customMods.js`, `src/ui/screens/customRun.js`, `draft.js`, `assets/bg/`.
Unity: `Domain/Original/OriginalRunRules.cs`, `OriginalCustomRunRules.cs`,
`OriginalMapShape.cs`, `Presentation/OriginalCustomSetupPanel.cs`, `OriginalMapShapePanel.cs`.
Tests: `UnityTests/Playthrough`, `MapShape`, `tools/native-playtest.cjs`,
`native-map-shape-playtest.cjs`; all-boss compiled check: to write.

### F13 — Accessibility & phone layout

**Goal:** comfortable one-handed phone play and readable text for everyone.

- US-13.1 Every flow fits 320×640 to 1440×900 with safe areas and reachable controls.
- US-13.2 Text size, high contrast, colorblind-friendly palette, reduce flashes.
- US-13.3 Minimum tap size and hold-to-confirm for destructive actions.
- US-13.4 No hover-only information; tooltips have tap equivalents.

Acceptance: all screens pass layout checks at four viewports; accessibility
options persist and apply globally.
Reference: `src/ui/screens/settings.js` (accessibility section), `tools/tapsize.mjs`,
`textfit.mjs`, `mobilefit.mjs`, `contrast-audit.mjs`, `src/ui/components/holdconfirm.js`.
Unity: `Presentation/ViewportLayout.cs`, `Application/DisplayViewport.cs`, `CampaignView.cs` (reduced motion).
Tests: `UnityTests/Viewport`, `tools/campaign-playtest.cjs --mobile-layout`; accessibility options: to write.

### F14 — Co-op

**Goal:** two to four friends climb together on a LAN, each with their own hand.

- US-14.1 Host via the companion; guests join with an invitation code; lobby, ready and seed.
- US-14.2 Vote on routes; private rewards, shops and events; catch-up.
- US-14.3 Shared combat with friendly targets; downed/disconnected seats.
- US-14.4 Rejoin a saved seat after disconnect or host restart.

Acceptance: multi-browser fight/reward/rejoin, host restart and duplicate retry
pass on the packaged companion; phone/LAN hardware check.
Reference: `src/ui/screens/lobby.js`, `coop.js`, `src/engine/coopCombat.js`,
`src/net/lan.js`, `src/ui/components/friendlyTargets.js`, [MULTIPLAYER.md](MULTIPLAYER.md).
Unity: `Domain/Original/OriginalCoopRun*.cs`, `OriginalCoopCombat.cs`,
`Presentation/OriginalCoopPanel.cs`, `CampaignView.Coop.cs`, `Presentation/Networking/`,
`Application/RunController.Coop.cs`, `tools/NativeLan/`.
Tests: `UnityTests/CoopRun`, `tools/NativeLan/Tests/*`, `tools/native-coop-ci.cjs`,
`native-coop-playtest.cjs`.

### F15 — Customization settings & controls

**Goal:** players tune the game to taste and input device.

- US-15.1 Display options: fullscreen, UI size, accent colour, card motif, map header, control hints.
- US-15.2 Gameplay options: combat pacing, reward collection, merchant buy-back, weapon swap cost display.
- US-15.3 Keyboard and gamepad bindings with rebinding.

Acceptance: each option persists per channel and changes the named behaviour
only; defaults match the reference.
Reference: `src/ui/screens/settings.js`, `controls.js`, `src/ui/input.js`,
`src/ui/components/hudQuickSettings.js`, `tools/rebind-capture.mjs`.
Unity: `Presentation/CampaignView.cs` (`Settings`), `Application/RunController.cs`.
Tests: to write.

### F16 — Content modding & data packs

**Goal:** the owner (and later modders) change content without C#.

- US-16.1 Export/import any table as CSV with validation, stale-edit refusal and backups.
- US-16.2 Add a card, weapon, enemy and encounter that work in real combat.
- US-16.3 Full original schema validation with file/row/field error messages.
- US-16.4 Optional data packs layered over base content.

Acceptance: authoring checks pass; invalid imports never replace valid content.
Reference: `content/framework/*.json`, `src/framework/importer.js`, `validate.js`,
`tools/content-build.mjs`, [Content-Authoring-0.6.0.md](Content-Authoring-0.6.0.md).
Unity: `Domain/Original/OriginalContentCatalog.cs`, `OriginalContentValidation.cs`,
`Unity/Assets/AshenSpire/Editor/BuildTools.cs`, `tools/original-table.py`.
Tests: `UnityTests/Authoring`, `UnityTests/OriginalAuthoring`,
`python UnityTests/Parity/authoring-checks.py`; data packs: to write.

### F17 — Performance & platform polish

**Goal:** fast start, smooth frames and a real install on target devices.

- US-17.1 Measure startup, download size, memory and frame time on reference phones.
- US-17.2 Physical Android and graphical Windows play-through.
- US-17.3 iOS build/device path.
- US-17.4 Hosted Pages archive capacity for continued builds.

Acceptance: budgets recorded and met; device receipts per build.
Reference: [Unity-Build-Brief.md](Unity-Build-Brief.md) §2–3, `tools/unity-archive-hosting.mjs`.
Unity: `Editor/BuildTools.cs`, `tools/build-unity.ps1`, `tools/android-preflight.ps1`.
Tests: `tools/unity-archive-hosting.test.mjs`, `unity-channel-storage.test.mjs`; device budgets: to write.

## Foundation (F00) detailed acceptance

The checklist and evidence history below are preserved from earlier roadmap
revisions. Checked items describe implemented functionality with focused
evidence. Unchecked items are acceptance work, limitations or remaining scope;
checkmarks do not certify every interaction or platform. See
[Unity-Parity.md](Unity-Parity.md) for receipts.

### Build history

Build 14 prioritizes matching the HTML reference visually: title composition, serif display type, warm palette, class preview, compact combat HUD, simultaneous enemies and framed horizontal cards. See [visual parity](Unity-Visual-Parity.md) for evidence and remaining gaps.

Build 13 improves whole-valued weapon damage text and shares concise card costs and resource-shortage hints between solo and co-op. Cards remain selectable when resources are insufficient; Play explains the shortage. See [Unity-Combat-Readability.md](Unity-Combat-Readability.md) for editing and verification.

Build 12 restores the shared branching map, solo fog/Sealstone knowledge, shrine
guidance, local camera preferences and touch navigation. Its Web map suite passed
558 checks across four sizes and an earned Sealstone case, plus a focused minimum-zoom rerun (133 overlapping checks). Matching Web, Windows and Android exports are complete. The current player passed a three-act climb (657 checks, 280 commands) and co-op fight/reward/rejoin checks (eight host, six guest).
See [Unity-Map-Foundation.md](Unity-Map-Foundation.md) for editing and test boundaries.

Build 11 integrated the painted enemy expansion and has matching Web, Windows,
Android and companion exports, package verification, 31 solo art checks and
eight host/six guest co-op checks. Full-campaign, storage and other older counts
below belong to build 10 at commit `890af027…` / digest `62aa53dc…`.
Build 11's bounded results do not repeat that full baseline.


Build 12 was merged into Dev through PR #39 and selected as the initial Test build at
`4b4a28dfe9aecc1a3292b498cd6a9310a6aef2fc`. Live Pages is separate. Issue #40 prepares
shared channel storage: 847.1 MiB for Dev/Test instead of 1,206.3 MiB, preserving
all archived players and independently tested channel saves. See
[Unity-Channel-Storage.md](Unity-Channel-Storage.md) for the candidate and evidence.

### Implemented foundation

- [x] Pin original dev `b17a7f4543e1710f49fae8b58880121690a314de` and retain original import/oracle hashes.
- [x] Import original cards, classes, equipment, enemies, relics, events and rules.
- [x] Keep simulation in engine-independent C# components with tag queries,
  .NET names and maintenance comments; keep Unity input/rendering separate.
- [x] Support original JSON/CSV tables with reference validation, stale-edit
  refusal, atomic replacement and exact source backups.
- [x] Implement original RNG, formulas, derived stats, statuses and seeded maps.
- [x] Render the original branching graph in the same solo/co-op component, with
  authoritative routes, visited/current markers, party votes and a named route list.
- [x] Restore original solo fog, Sealstone room readings and nearest-shrine guidance;
  preserve co-op's original full-path visibility. Keep knowledge derived from the trail.
- [x] Restore local Fit/manual camera state, vertical pan, zoom steps, recenter,
  touch cancellation and profile persistence; test real multi-choice layouts.
- [x] Default to Assign points: five attributes at 5, 60 total, 35 unspent;
  retain Standard and remove Tuned from the fork's available modes.
- [x] Make every attribute point useful and author five-point mechanical thresholds.
- [x] Apply discovered starting kits, alternate hands, wardrobe/relic choices and
  saved kit identity; retain the uncustomized baseline kit's creation-only waiver.
- [x] Compose weapon-owned cards and pay final action/MP/stamina costs.
- [x] Resolve combat turns, piles, targets, enemy AI, triggers, dodge/poise,
  statuses, flasks and the authored solo Catch Breath action.
- [x] Traverse three acts, bosses, unknown rooms, events, rewards, shops and shrines.
- [x] Track owned items and stable cards; equip unlocked sets, smith, extract and
  install supported mounts while preserving resource deficits.
- [x] Pay for solo combat set changes with configured resource/turn costs.
- [x] Purchase shrine level points and resell eligible relics/utility flasks.
- [x] Save content, room/combat state, RNG and creation choices; validate recovery.
- [x] Persist profile history, unlocks and discovery receipts with native consumers.
- [x] Bind original numbered card-text tokens; present safe single-hit bonuses
  correctly and drive combat feedback from actual result receipts.
- [x] Keep per-channel latest links and immutable previous-build pages with
  build/version/date/source/PR metadata and concise changes.

### Implemented original modes and multiplayer

- [x] Custom Climb with six Ascension difficulty rules and authored chaos modifiers.
- [x] Standard, Sealed and three-round Draft starts; save offers before selection.
- [x] Keepsakes, name/tint/sigil identity and original custom progression classification.
- [x] Endless cycles beyond Act 3, with authored enemy growth and exact restoration.
- [x] Original Custom Climb run shape: floor/column caps, relative node weights,
  per-act validation, isolated 24-seed density estimates and frozen save limits.
  The pinned original has no separate practice mode to port.
- [x] Distinct Animated, Rendered, Classic and Sigil presentation paths, including
  original armour/tint poses and saved appearance in solo/co-op views.
- [x] Map all 19 original enemies to the same painted resources in solo/co-op:
  retain the earlier 12 paintings and add seven transparent sprites. Register
  visible alpha bounds for consistent framing; cap the new texture imports at
  512 pixels while preserving high-resolution masters and exact prompts.
- [x] Native authoritative co-op combat with 2–4-seat original differential fixtures.
- [x] Shared route votes, private rewards, shops/shrines/events, catch-up and
  actual cooperative combat; connected, disconnected and downed seat handling.
- [x] Authenticated companion commands, duplicate protection, host persistence,
  late join/rejoin and member-specific snapshots.
- [x] Co-op deck/equipment/sets and supported mount service controls.

### Finish foundation acceptance

- [x] Build matching-source build 11 Web, Windows, Android and companion exports;
  pass 436 companion, 160 target-file and 18 root-package checks.
- [x] Pass 217 source/art checks across all 19 painted mappings, 31 compiled solo
  checks on hounds/wisp with nine screenshots, and eight host/six guest co-op
  fight/reward/rejoin checks with nine non-lobby screenshots; zero browser errors.
- [ ] Extend build 11 compiled art coverage beyond hounds/wisp and obtain owner
  visual acceptance. Source mapping coverage does not prove every enemy on screen.
- [x] Complete the build 10 scripted native browser run: 657 checks, 280
  commands, 22 fights, three acts, two reloads and Chronicle checks.
- [ ] Complete owner acceptance and the broader profile unlock/new-creation,
  shopping, equipment and service interaction matrix.
- [ ] Complete current-source compiled Custom/Sealed/Draft/Endless interaction and
  save/resume checks. Headless victories are recorded separately.
- [x] Pass 16 compiled Draft/shrine/flask/merchant/reload checks, including CON
  raising HP from 64 to 66 and actual purchase/resale transactions.
- [ ] Complete current-source multi-browser co-op play, friendly targets, catch-up,
  host restart, duplicate retry and rejoin coverage using the packaged companion.
  A real browser fight/reward/exact-hand rejoin and 22 packaged restart checks
  already passed; these do not cover every multiplayer interaction.
- [x] Pass 60 phone/desktop map-shape control, actual-combat and exact-reload checks.
- [ ] Cover original settings/inventory flows not exercised by the current commands.
  Co-op's original Endless option does not imply all solo custom rules. Paid combat
  set changes and Catch Breath are intentionally solo-only: the pinned original
  co-op offers neither. Cooperative extensions are future design, not missing parity.
- [x] Pass 44 actual appearance choice/attack/feedback/reload checks, 11 each for
  Animated, Rendered, Classic and Sigil.
- [ ] Finish broader armour/tint visual acceptance and co-op pose feedback.
- [ ] Check actual card numbers, target availability, affordability, rejection
  recovery and result feedback throughout phone and desktop flows.
- [ ] Finish field/schema/runtime authoring coverage. The native CSV demonstration
  already adds cards, equipment, enemies and encounters and executes real combat.
- [x] Pass eight real raw-CDP background/freeze/return checks on build 10.
- [x] Pass 50 build 10 storage checks: 12 served-file hashes and 38 storage checks,
  exact backup recovery and preservation of 729,414 damaged bytes.
- [ ] Complete profile corruption, quota exhaustion and upgrade coverage.
- [ ] Decide original JavaScript save import explicitly. Existing legacy saves are
  preserved; importing them into a native run is not implemented.
- [x] Build matching-source build 10 Web, Windows, Android and companion packages; pass
  433 companion, 160 Windows/APK and 18 root-package checks, plus 22 self-contained restart checks.
- [ ] Verify hosted build/history links, downloadable folders and current screenshots.
  Packaging does not establish graphical Windows or physical phone acceptance.
- [ ] Record remaining defects and owner acceptance before selecting `0.1.0.0`.
  Native co-op and broader original parity have not been waived.

### Polish and completed-game readiness

- [ ] Compare original and Unity screens side by side and retain the original aesthetic.
  The native map now restores branching presentation, solo fog and Sealstone reveal.
  Broader screen-by-screen visual acceptance and map styling refinement remain.
- [ ] Finish sprite animation, transitions, status feedback, sound/music and settings.
- [ ] Make every required original flow comfortable on touch with consistent UI.
- [ ] Test physical Android hardware and graphical Windows play; establish the iOS
  build/device path separately from browser phone emulation.
- [ ] Measure startup, download size, memory and frame-time budgets on target phones.
- [ ] Playtest class pacing, stamina/MP decisions, fairness and fun with players.
  Automated victories alone do not establish balance or resource usefulness.
- [ ] Complete regression and owner-selected test/release/main promotions.
  `1.0.0.0` remains reserved for the completed game.

### Evidence boundary

Original differential fixtures, native headless policies, browser input/screenshots,
host transport tests and physical devices are separate evidence categories. Every
compiled receipt must identify its build source. Earlier adaptation playthroughs
and screenshots remain valid only for their historical checkpoints.

The prior **0.0.10.0 / build 10** matched local checkpoint is source commit
`890af027be07a5648119521165aaeadf2dc5e938`, build digest
`62aa53dcfe5f399be01efd6ed697b43a6aaf09c544950a90337641ba5101032b`. Its Web, Windows, Android
and companion packages passed matching-source checks. Publication is separate.
Build 11 has its own full build digest
`eb5ff8e45b16eef61930a9d94ab94cc681e6dd6c4d6a6dc3bea19ea5d2cffe2e`,
Web built 2026-09-07 06:25:03.732683 UTC. Its all-target builder exited zero and
package checks passed 436 companion, 160 target-file and 18 root-package checks.
Solo art passed 31 checks with nine screenshots at 390×844 and 1440×900, including
two hound instances, one wisp, an action-spending attack (15→1 HP) and exact reload.
Co-op passed eight host and six guest fight/reward/rejoin checks, with nine
non-lobby screenshots and a visibly painted hound. Both suites recorded zero
errors; asset-path console receipts were unavailable in the solo art test.
The 217 source/art checks cover all 19 mappings, not all 19 compiled encounters.
The prepared 21-image gallery combines nine solo, nine co-op and three art images.
The local Pages preview passed 242 navigation checks across 38 pages and 16
archives. Batched copying preserved all 1,826 site files exactly. Publication
remains separate; no new full campaign or storage result is claimed for build 11.
- [ ] Expand archive hosting capacity before another large native build: this
  preview occupies 943.373 MiB of the unchanged 950 MiB limit. Preserve every
  archived player and its evidence when changing storage.

The normal solo policy receipt records 12 victories, 264 fights, 3,450 accepted
commands and 858 save/resume comparisons in
`TestResults/NativePolicyFinal/results.json`, runtime-source digest
`39286ae6213e53c5a6d657a0cd571bed04617ca73692f58e760cc5874cc883ea`.
This runtime subset digest differs from the full Unity build source digest.
Separate custom policies completed Sealed and Draft and reached the first scaled
Act 4 fight in Endless. A normal-health two-player co-op policy completed three
acts in 359 commands with nine exact resumes. These are simulation evidence,
not owner acceptance or device certification. Separately, the build 10 native solo
browser, map-shape and raw-CDP interruption checks passed as recorded above.
Build 10 appearance passed 44 checks, custom-feature interactions passed 16 and
served-file/storage recovery passed 50 on the unchanged full build source digest
recorded above. These bounded receipts do not close the remaining branching-map,
solo fog/Sealstone reveal, co-op
animation, balance, profile/quota, JavaScript-save import or owner/device gaps.
No physical phone, graphical Windows, iOS or audible sound acceptance is recorded.

Issue: [#33](https://github.com/cehinds/AshenSpire-Unity/issues/33).
