# AshenedSpire — build 28

Foundation development candidate, version **0.0.28.0**. Phase 1 is not complete.

- [ ] **Phase 1 — Foundation**
  - [x] Initial original-game map-save importer, file/slot selection, preview and empty-slot commit.
  - [x] 394 import/parity/storage checks, 12 browser-adapter checks and 126 Unity-reference checks (three documented reference API gaps).
  - [x] Original-save file and browser-slot import: 36 compiled browser checks.
  - [x] Native profile and three-slot persistence: 16 compiled browser checks.
  - [x] Compiled 19-enemy portrait gallery: 78 compiled browser checks.
  - [x] Four matching download packages, verified against SHA-256 receipts.
  - [ ] Original profile and active-room import; newer original-game schemas and broader progressed-save compatibility.
  - [ ] Remaining campaign/mode, encounter, interaction, authoring, multiplayer recovery and appearance acceptance.
  - [ ] Owner visual and final acceptance before 0.1.0.0.

Use **Saved climbs → Import original-game save**. This candidate accepts compatible original schema-5 map checkpoints and exported run archives. Preview before saving into an empty slot. Original files and browser slots remain unchanged. Current original-game saves with newer skill/progression schemas are refused explicitly; they are not supported by this first importer.

The browser-slot option requires the same browser and website origin as the original game. File selection supports exported JSON. Desktop/native players currently support pasting JSON.

Source digest: `f3de4f1487e5653fbc87932d05623326255e252d11275ebb5f654835c17ab05f`. See [validation receipt](validation.json) for scoped assertions and download hashes. Simulated browser viewports are not physical-device testing.

[Review all 19 painted enemy portraits](portraits/README.md). Owner visual acceptance remains open.
