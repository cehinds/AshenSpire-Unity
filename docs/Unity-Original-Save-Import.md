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

The run importer refuses active rooms, unsupported schemas, profiles,
missing/unknown references, incompatible frozen rules, duplicate JSON properties
and files over 1 MiB. It does not reset a battle, skip a reward, grant unlocks,
or convert an unknown skill tree. Finish active rooms in the original game and
save on its map to use this initial route. Unsupported cases remain acceptance work.

Validation uses `tools/unity-web-save-fixtures.mjs` to produce saves and card
oracles through the repository's original JavaScript engine. Run
`dotnet run --project UnityTests/WebSaveImport -- <fixture-directory>` and
`node tools/unity-web-save-bridge.test.mjs`. Compiled verification is
`tools/native-web-import-playtest.cjs`; generated fixtures and browser profiles
must remain separate from the player's actual saves.

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
  `muteAudio`, `musicVolume`/`sfxVolume` (percent to 0–1), `animSpeed`
  (`slow`/`normal`/`fast`/`instant`), `uiScale` S–XL and `textSize` S–XL (or
  legacy `largeText`). Everything else is listed as not carried over and kept
  verbatim in the receipt, including `colorblindSafe` (the original shifts one
  palette; AshenedSpire palettes are per colour-vision type), `musicEnabled`
  (no separate music switch), keyboard bindings (different actions), `Auto`
  sizes, contrast, map, card-motif, HUD, hold-to-confirm, reward-collection,
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
