# Unity integration checkpoint — October 4, 2026

The owner authorized merging the work as an intermediate checkpoint and explicitly
said it still does not look like the current game. Merge permission is not visual
acceptance. Phase 2, Phase 3 and current-game parity remain incomplete.

- [ ] Current-game visual and gameplay parity
  - [x] Checkpoint card inspection/targeting, guided setup, fixed card proportions,
    map navigation and illustrated Unity 2D presentation source.
  - [x] Prior build 43 exported and tested through setup, map and a real card play.
    Its receipt applies only to that build, not the merged integration source.
  - [ ] Verify the integrated source in newly exported players.
  - [ ] Match current-game composition, card layout, typography, character scale,
    combat feedback and responsive layouts screen by screen.
  - [ ] Activate and verify the full current content and remaining mechanics.
  - [ ] Owner visual acceptance, physical-device and controller acceptance.

## Supplied component reference

The owner attached `player-components-2026-10-03.7z` from
`D:/repos/.codex/worktrees/04f4/AshenSpire/docs/design/` during the merge.
It was extracted separately under
`D:/repos/.codex/asset-review/player-components-2026-10-04/`.
All 466 entries in its SHA256SUMS inventory matched. Its manifest describes 316
artwork components, including separate card/frame artwork, enemy cutouts,
backgrounds and 24 screen recipes. The component review sheet was inspected.
The package is a visual reference; this checkpoint does not claim its assets are
registered in Unity or that its example values are game rules.

Use the supplied artwork as separate Unity layers with live model-backed text.
Frame borders can use authored slice insets; cards and figures retain their
aspect ratio. Compare an exported desktop and portrait screen with the actual
current game before calling any surface matched. Enemy illustration imports
still need floor/action anchors and pose routing.
