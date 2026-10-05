# Player component art integration

Preview: http://127.0.0.1:8936/ — Unity 0.0.33.2, build 36.
Source digest: `9be946ff1411ad19b8cc8e7b09738a4d169454cd3924265e7ef76f628bc67488`.
PR: https://github.com/cehinds/AshenSpire-Unity/pull/71

## Visual and card-layout pass

- [x] Implementation.
  - [x] Import 44 owner-supplied assets: 21 frames, seven desktop/portrait scene pairs, nine item illustrations.
  - [x] Verify selected source checksums, output hashes/dimensions and all 23 raster conversions pixel-for-pixel including alpha.
  - [x] Bind painted scenes and engraved controls to native Unity UI Toolkit screens; retain existing SpriteRenderer character prefabs and repaired soldier hilt.
  - [x] Keep guided setup on one screen per step with Reaver, Standard, Straight Sword, Round Shield and Forsaken Medallion ready by default.
  - [x] Frame inventory images using nine importer-generated alpha bounds, without changing the original PNG pixels.
  - [x] Preserve 2:3 card faces, center/enlarge inspection, and fit the combat hand against available height and fan rotation.
- [x] Compiled and local verification.
  - [x] Runtime reference checker: 167 sources; only three known Unity 6 gaps in the 2021 reference package accepted.
  - [x] Card QoL: 192 checks; map viewport: 74,628 checks across 3,024 camera fixtures and 27 maps.
  - [x] Native Unity Web export succeeded; exporter confirmed unchanged source during the build.
  - [x] Source/version verification and hashes for all eight exported payload files passed.
  - [x] Desktop 1280x720 and portrait 390x844: full card faces, reachable setup controls, readable equipment/relic illustrations.
  - [x] Final player resumed the saved combat. End Turn advanced to turn two and refilled actions.
  - [x] Desktop drag: Slashing Strike reduced enemy HP from 16 to 7 and actions from 3 to 2.
  - [x] Portrait drag: Shield Defend increased block from 0 to 11, spent one action and moved to discard.
  - [x] Initial exported player also verified tap-card/tap-target attacks and connected-map entry.
  - [x] No browser runtime errors observed in the final pass; viewport override restored.
- [ ] Owner acceptance and physical-device/controller testing.

## Remaining migration and release work

- [ ] Full current HTML-game visual and mechanical parity. The runtime still uses the older rules/content baseline; this art pass does not complete that migration.
- [ ] Fresh all-platform published bundles and green full CI. Published bundles were not replaced by this isolated preview.
- [ ] Resolve inspected release checks: companion/player version mismatch and published-player `merchant-buy-back` timeout.

PR #71 remains a draft with these release limits recorded. Unrelated main-checkout
work and existing player saves were preserved. The original game was not changed.

## Evidence and reproduction

Final receipt: `qa/unity-player-components/build36-source.json`.
Final screenshots: `final-desktop-combat.png`, `final-portrait-combat.png`,
`final-portrait-equipment.png`, `final-portrait-relic.png`, and
`final-portrait-ready.png` under `qa/unity-player-components`.
The `initial-*` and `verified-art-*` captures document earlier visual passes.

Source archive: the owner's `player-components-2026-10-03.7z`, extracted under
`D:/repos/.codex/asset-review/player-components-2026-10-04`. All 466 kit checksums
were verified before import. Source/output provenance is recorded in
`art/player-components/import-receipt.json`; credits are preserved beside it.

Reimport with `tools/import-player-components.py KIT --resvg MODULE_DIRECTORY`.
Pillow and @resvg/resvg-js are authoring dependencies outside the game project.
Export with `tools/build-owner-appearance-preview.ps1`; verify with
`node tools/unity-verify-parity-preview.mjs Builds/OwnerAppearance/build36/Web`.
The launcher uses a process-local D: package cache and versioned output directory.
