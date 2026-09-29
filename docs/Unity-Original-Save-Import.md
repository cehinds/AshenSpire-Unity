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
  - [x] Build-30 source: one-time additive original profile import, preserving lifetime totals, earned unlocks, equipment discovery, existing Unity progress and the original record. 77 focused profile checks pass; combined importer suite: 471. The stored baseline protects progress changed before or after preview.
  - [x] Build 30: 60 phone/desktop checks verify run/profile preview, cancellation, import, duplicate refusal, unchanged active combat and Chronicle reload. [Receipt](qa/unity-build-30/import/summary.json).
  - [ ] Profile history/unlock/settings import and compatible progressed-save matrix.
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

The initial importer refuses active rooms, unsupported schemas, profiles,
missing/unknown references, incompatible frozen rules, duplicate JSON properties
and files over 1 MiB. It does not reset a battle, skip a reward, grant unlocks,
or convert an unknown skill tree. Finish active rooms in the original game and
save on its map to use this initial route. Unsupported cases remain acceptance work.

Build-30 source adds a separate **Import original profile** action. It reads raw
original profile schemas 0–2 (including unversioned profiles), exported profiles
and recovery archives. It adds original lifetime counters once, combines earned
IDs, and places imported history before existing Unity history within the normal
20-entry Chronicle. The complete original record remains inside a validated import
receipt. Original browser settings are preserved there, while current Unity
preferences remain active; applying compatible browser preferences is still open.
Another profile import is refused to prevent double-counting profiles without
stable original run IDs. Active run slots are not used or replaced. Confirmation
also checks that Unity progress has not changed since the preview.

Validation uses `tools/unity-web-save-fixtures.mjs` to produce saves and card
oracles through the repository's original JavaScript engine. Run
`dotnet run --project UnityTests/WebSaveImport -- <fixture-directory>` and
`node tools/unity-web-save-bridge.test.mjs`. Compiled verification is
`tools/native-web-import-playtest.cjs`; generated fixtures and browser profiles
must remain separate from the player's actual saves.
