# Owner-selected appearance

Latest: [build 42 combat reframe and evidence](Unity-Combat-Reframe.md),
`0.0.30.11`, local preview `http://127.0.0.1:8906/`. The checks below retain
earlier build history; owner acceptance and noncombat styling remain open.

On 2026-10-04 the owner selected `http://127.0.0.1:4175/` and supplied its
Starseer battlefield screenshot as the new visual target. Published test 898
remains the gameplay reference. This replaces the earlier requirement to copy
test 898's card typography and combat presentation exactly.

- [x] Inspect the local reference and its source at `D:/repos/TheAshenedSpire`.
- [x] Import the courtyard, four hero cutouts, three enemy cutouts and three
  card illustrations into a separate Unity resource namespace.
- [x] Verify all eleven imported images' decoded pixels against source crops.
- [x] Import the reference's Cormorant Garamond font with its OFL license.
- [x] Implement native full-screen battlefield composition and actor floor.
- [x] Implement compact enemy overlays and angled illustrated cards.
- [x] Preserve native play, drag, inspect, keyboard, slot and save commands.
- [ ] Validate build 38 in the exported player.
  - [x] Actual Unity Editor script compilation and reference asset import.
  - [x] Current-rule models and stance commands: 40,394 checks passed.
  - [x] Legacy co-op regression: 6,587 checks passed.
  - [x] Finish Web export and verify source/payload receipt (eight payload files).
  - [x] Inspect and play a Starseer attack: actions 3 to 2, hand 6 to 5,
    discard 0 to 1 and soldier HP 25 to 18.
  - [x] No observed browser errors; existing persistent-data synchronization warning.
  - [x] Initial export reload/continue retained the exact post-play combat state.
  - [ ] Complete save/continue and gesture checks in the corrected player.
- [x] Compare desktop and portrait screenshots; correct imported actor proportions,
  HUD/status overlap and compact card rule overflow in build 40.
- [ ] Match remaining title, map, creation, reward, services and co-op surfaces.
- [ ] Owner acceptance.

Build 38 is a separate `Builds/OwnerAppearance/build38/Web` preview, source digest
`08acc39066b5fe1df859bf80029925aff4c01d109061e6d89bb1ff687fcc77cc`.
Desktop and portrait screenshots are in `docs/qa/unity-owner-appearance`.
The first exported appearance revealed squeezed portrait actors, HUD/tool
overlap and verbose card text overflow. Build 39 (`0.0.30.8`) corrects these
in source and places intent/Poise overlays above/below enemy figures. Its
export finished in `Builds/OwnerAppearance/build39/Web` and passed its
source/eight-payload receipt check. Desktop and portrait checks confirmed HUD
separation, readable compact rules and positioned intent/Poise overlays.
Its source digest is
`e2f1c24f6162c183edb4662796a12f7a7e4f0aab5effd9c5d79f3395c78a88e7`.
Those checks also revealed square imported cutouts: the soldier source is
626x548, while the player texture was 512x512. Build 40 (`0.0.30.9`) disables
NPOT resampling/compression and checks all eleven imported dimensions against
PNG headers before its separate export in `Builds/OwnerAppearance/build40/Web`.
Build 40 compiled and exported successfully. Its source and eight payload files
passed the receipt check with digest
`59f51ce9999d1472f043cabc2b39c490d0be7e8b154c3f3b934e42b5b3cc936f`.
The Editor dimension gate passed all eleven textures; the actual player reported
the soldier at 626x548. Desktop (1280x720) and portrait (390x844) screenshots
confirmed corrected cutout proportions and readable cards. Inspection/play spent
one action, discarded the card and reduced soldier HP 25 to 18. Reload/Continue
retained two actions, five cards, one discard and 18/25 HP, with no console errors.
The utility tray responds to horizontal scrolling. Portrait still uses horizontal
hand/tool navigation; the complete gesture and physical-device gates remain open.
The playable appearance preview is `http://127.0.0.1:8904/`.
The compact card retains resolved values;
the inspector retains the full rating explanation. The attempted automated
drag opened long-press inspection, so it does not certify a successful drop.
Build 37
remains the archived intermediate test-898 presentation. Neither preview
certifies that the full current gameplay migration is complete.

Asset provenance and pixel checks: `TestResults/OwnerAppearance/import.json`.
Import tool: `tools/import-owner-appearance.py`. The reference checkout is read-only.
