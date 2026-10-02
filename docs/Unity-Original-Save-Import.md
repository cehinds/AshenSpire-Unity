# Original-game save import

Owner decision, 2026-09-28: **implement original-game save import before Phase 1 completion**.
This is required acceptance under US-0.6 and foundation issue #33, not a waiver.

- [ ] US-0.6 — original-game save import acceptance
  - [x] Preview and empty-slot-only commit implemented.
  - [x] Raw schema-5 map checkpoints and original exported run archives convert for all four baseline classes.
  - [x] Preserve map, path, seed/random counters, card identities, resources and frozen equipment/stat rules.
  - [x] Original-engine card costs/effects match; imported checkpoints reload and play an enemy turn (394 domain checks including refusal/storage cases).
  - [x] Browser file selection and explicit reads of the three original slot keys implemented; 12 browser-adapter checks pass.
  - [x] Build 28 compiled import flow: 36 phone/desktop checks, plus 16 native save/profile checks. See [build-28 QA](qa/unity-build-28/README.md).
  - [x] Build 29 CI repeats the 36 import and 16 native profile/slot checks against exactly matching packaged runtime hashes; [current receipt](qa/unity-build-29/ci.json).
  - [ ] Profile history/unlock/settings import and compatible progressed-save matrix.
    - [ ] Domain merge, fixtures and checks implemented on `feature/unity-us0-6-profile-import` (see [Profile import](#profile-import)); compiled browser/native verification, the progressed run-save matrix and owner acceptance remain open.
  - [ ] Active room conversion (combat, reward, merchant), newer content/rule schemas and mode compatibility.
    - [ ] Domain conversion, fixtures and checks implemented on `feature/unity-us0-6-active-rooms` (see [Active rooms](#active-rooms)): reward and merchant rooms, fight-entry receipts, entered events, Custom Climb rules and Endless import; Sealed and Draft import since the original's reload fix (`feature/unity-us0-6-sealed-draft-import`); exact mid-fight snapshots, custom map shapes and other run schemas are refused by name. Compiled browser/native verification and owner acceptance remain open.
  - [ ] Owner acceptance of the finished import experience.

Open **Saved climbs → Import original-game save**. Web supports choosing a JSON
file, pasting its contents, or explicitly reading original slot 1, 2 or 3.
Browser-slot reads only work on the same website origin, browser and profile
where the original game stored the save. No automatic storage scan runs.
Desktop/mobile native players currently offer the paste path.

Preview shows class, act, floor, HP, deck size and destination. Confirm writes
through the existing verified journal into the first empty slot. Occupied,
corrupt and backup-only slots cannot be replaced. Original keys and files are
never changed. Identical checkpoints, including archive wrappers or reformatted
JSON, cannot be imported twice into occupied slots.

The run importer converts map checkpoints and the active rooms listed under
[Active rooms](#active-rooms). It refuses exact mid-fight snapshots, unsupported
schemas, profiles, missing/unknown references, incompatible frozen rules,
duplicate JSON properties and files over 1 MiB. It does not reset a battle, skip
a reward, grant unlocks, or convert an unknown skill tree. Unsupported cases
remain acceptance work.

Validation uses `tools/unity-web-save-fixtures.mjs` to produce saves and card
oracles through the repository's original JavaScript engine. Run
`dotnet run --project UnityTests/WebSaveImport -- <fixture-directory>` and
`node tools/unity-web-save-bridge.test.mjs`. Compiled verification is
`tools/native-web-import-playtest.cjs`; generated fixtures and browser profiles
must remain separate from the player's actual saves.

## Active rooms

The original game saves inside a room in four ways. Each is converted into the
native room the restored climb continues from, or refused with a message that
says why; the original save is never changed. The preview adds where the import
resumes (map, fight rewards, merchant or the start of a fight).

- **Rewards** (`pendingReward`, schema 1). The offer (cinders, card choices,
  flask, relic, armament) and every taken or skipped row carry over. Pending
  cinders are granted on import, as the original reward screen grants them when
  a saved reward is resumed; the Smithing Stone row was already granted at the
  fight and stays granted. Taking a reward, continuing with or without
  auto-collect, and a boss reward's move to the next act (including its map)
  match the original. The offer must match the fight on its map node, its
  Smithing Stone claim must be in the run, and a final-boss reward is refused.
- **Merchant** (`shopStock`). Remaining stock and prices (Custom Climb price
  rules included), the card-removal price and the merchant's smith carry over.
  The original removes sold items from the stock; the native merchant marks them
  sold, so the remaining items are the same. Purchases, removal and the rising
  removal price match the original. `removesPurchased` and the flask-drop chance
  (`flaskChancePct`) are imported.
- **Fights.** The original autosaves a fight-entry receipt (node and encounter)
  and rebuilds that fight from the saved random streams on resume. The import
  does the same: the opening hand, draw order, enemies, intents and stream
  positions match the original for all four classes and under Custom rules. An
  exact mid-fight snapshot (the original's Save Game during a fight) is refused:
  "This save is mid-fight; finish the fight in the original game, then import."
  Its card piles, enemy state and stream positions use a different snapshot
  model and are not converted approximately.
- **Events.** The original saves on entering an event and resumes such a save on
  the map at that node, with the event spent. The import is that map checkpoint.
  Shrines and treasure rooms are not saved inside the room by the original.
- **Modes.** Custom Climb ascension and chaos rules and Endless (including acts
  past 3) import. **Sealed and Draft** import too. Their deck is dealt from a
  pool after the original composed one from the equipment. Until
  [cehinds/AshenSpire#1479](https://github.com/cehinds/AshenSpire/pull/1479)
  the original therefore kept the composed deck's birth attack quota, refused
  to reload its own save ("attack instance count 0 does not match authored
  N"), and the import refused it the same way. The fix, ported into this
  repository's `src/`, holds a dealt deck to its own rule. Its quota is the
  attack slots it was dealt (`attack:0..k-1`, none on a fresh deal), which is
  also the native rule (`OriginalCustomRunRules.Initialize`).
  - **Pre-fix saves.** The original now marks a dealt-deck run
    `poolDeckRule: 1`. A pool save without the marker was written before
    the fix and may carry the larger composed quota. The original's load
    door heals that quota once, to the dealt count, and so does the import
    (`OriginalWebSaveImport.BirthAttackQuota`). A marked save is held to its
    quota: one that lost an attack card is refused, as the original refuses
    it. The import receipt keeps the original bytes.
  - **Refused.** A gap or a duplicate in the dealt slots, more slots than the
    quota, or a malformed quota is refused. A Standard deck that lacks its
    composed slots is still refused, as the original refuses it.
  - **The dealt deck is kept exactly.** The original never deals a pool
    deck the equipment's lent cards (kit basics, weapon arts, Dodge Roll) at
    any restamp: load, end of fight, Armoury change, mid-fight swap or
    resumed fight. The import's one reconcile therefore keeps exactly the
    dealt instances, and so does every later native reconcile:
    `WeaponCardComposer.Recompose` and `ReconcileCombat` take the rule
    (`OriginalCustomRunRules.IsPoolDeckRun`) as a required argument, so a
    relic, a service, an Armoury change, a mid-fight swap or the end of a
    fight leaves an imported or native Sealed/Draft deck as dealt (Parity
    section `pool-deck`). A card the player installs into an item mount at
    the smith is the player's, not lent: it materializes in its mount and
    stays through those same doors, while the equipment's own grants and
    mount fallbacks are swept and never dealt.
  - **Checked against the original.** The imported Sealed and Draft runs open
    the same next encounter and opening hand as the original.

  Custom map-shape climbs are refused for now. Unknown modifiers and deck
  modes are refused.
- **Schemas.** `src/engine/save.js` writes run schema 5 and has no newer schema,
  so there is no newer migration to port; other run schemas are refused by
  name as before.

Fixtures come from the original modules: `node
UnityTests/WebSaveImport/export-room-reference.mjs` rewrites
`room-reference.json` and its receipt (source hashes and output SHA-256). It
reaches each room with the same roll functions, order and streams as
`src/main.js`, persists through the real save manager, and records what the
original does next and whether its own load door accepts the save. Two steps are
not played out and are named in the script: the route to a deeper room is
walked without playing the rooms before it, and a reward room follows a fight
won without HP loss. The few lines of reward/shop screen code that apply a take
or purchase are mirrored with their source line, and those screens are hashed in
the receipt. The checks run with the other WebSaveImport checks.

Open follow-ups: compiled Web/native verification of each room type, owner
acceptance, exact mid-fight snapshot conversion, and custom map shapes.
Sealed/Draft saves that retired an attack slot (`removedAttackSlotIds`, which
this `src/` copy predates) are still refused as unknown state.

## Profile import

The same **Check save** box also accepts an original profile: the raw
`sote_meta_v1` value, the file written by the original's *save a copy of your
profile* (`exportProfile`), or a `meta` archive entry. The importer
(`OriginalWebProfileImport.Merge`) never writes storage and never changes the
existing profile object; the preview shows what would be added, and confirming
writes the merged profile through the existing verified profile journal.

- **Schemas.** Original profile schema 2 imports directly; unversioned (0) and
  schema 1 profiles migrate as `src/engine/save.js` does (the kit ledger starts
  from the wardrobe). Newer or unreadable versions are refused by name, and
  nothing changes on either side.
- **History.** Original results have no run ID or timestamp, so each is keyed
  by the SHA-256 of its exact stored row plus its occurrence number. The keys
  join `completedRunIds`, which outlive the 20-result history, so importing the
  same profile again, in any wrapper or property order, or a later export of the
  same player adds only the new runs. Imported results are treated as older than
  native ones: they are listed after earlier imports and before native results,
  and leave the 20-result history first. Native results and their keys are never
  removed. Unreadable result rows are skipped and counted.
- **Progress.** The original tally (runs, wins, furthest act, bosses, winning
  classes) outlives its own history. It is added as a delta over the tally
  already imported, recorded in the profile's `originalWebImport` receipt. A
  profile without a tally rebuilds it from its history.
- **Unlocks and discoveries.** Original unlock IDs, found armaments, the
  starting-kit ledger and discovery receipts map one-to-one onto the native
  profile. The native unlock rules then run on the merged tally. Unknown IDs are
  reported, never granted, and kept in the receipt.
- **Settings.** Settings are applied only when the player chooses *Import with
  settings*. Only explicitly stored values map: `reducedMotion`, `screenShake`,
  `muteAudio`, `musicEnabled`, `highContrast`, `reduceFlashes`,
  `musicVolume`/`sfxVolume` (percent to 0–1), `animSpeed`
  (`slow`/`normal`/`fast`/`instant`), `uiScale` S–XL and `textSize` S–XL (or
  legacy `largeText`). Everything else is listed as not carried over and kept
  verbatim in the receipt, including `colorblindSafe` (the original shifts one
  palette; AshenedSpire palettes are per colour-vision type), keyboard
  bindings (different actions), `Auto` sizes, map, card-motif, HUD, hold-to-confirm, reward-collection,
  tuning and debug settings. Unity-only settings such as master and interface
  volume and content mods are kept.
- **Not imported.** The original `seen` record and other original-only fields
  have no native home; they are kept in the receipt only.

Fixtures come from the real JavaScript save manager, unlock rules and discovery
ledger: `node UnityTests/WebSaveImport/export-profile-reference.mjs` rewrites
`profile-reference.json` and its receipt (source hashes and output SHA-256).
They cover a fresh profile, five finished runs with victories, a defeat and a
custom run, a later re-export, a 25-run veteran, schemas 0 and 1, and a newer
schema. `dotnet run --project UnityTests/WebSaveImport -- <fixture-directory>`
runs the profile checks after the run checks.

Open follow-ups: compiled Web/native verification of the profile preview, a
browser button that reads `sote_meta_v1` directly (the run-slot bridge reads
only run keys today), and two known limits: identical original results with no
distinguishing field are keyed by occurrence, and importing profiles from two
different original browsers adds only the larger progress tally.
