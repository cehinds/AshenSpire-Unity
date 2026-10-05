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

## Integration validation and release boundary

Integration source is `0.0.33.1` (build 35), rebased onto dev `88b86ee`.
The original preview branch's build numbers 31–43 are local preview history;
this integration follows dev's version sequence.

- [x] C# language build: no warnings or errors.
- [x] Runtime reference compilation: 166 sources; three known Unity-6 API gaps.
- [x] Published-reference mechanics: 40,497 checks; card QoL: 192 checks.
- [x] Settings/mods: 197; gamepad: 195; confirmation policies: 144 checks.
- [x] Feedback: 10,296 checks through 1,263 native commands.
- [x] Browser driver: 14 unit checks, including guided screen navigation.
- [x] Source digest: 17 binary/text hashing checks; companion mutations: 24.
- [ ] Complete full local parity suite and fresh integrated platform exports.
  The all-platform invocation was stopped during its long parity precheck;
  no integrated export or published package is claimed from that invocation.
- [ ] Full CI acceptance and source-matched release packaging.

At integration time dev source was `0.0.33.0`/34, while its Published manifest
was `0.0.28.1`/29. Its checkpoint workflow already failed the packaging/assembly
gate (run 37072759752; preceding PR 67 likewise failed that gate). Published
archives are retained unchanged by this source checkpoint. Prior browser evidence
validates the named historical builds only, not the merged source.

Reconciliation retains dev's reward collection policy, save confirmations and
single gamepad input path; it adds preview setting aliases and removes duplicate
map-header information. The later in-progress edits appearing in the original
checkout after checkpoint b44715f are preserved there and are not swept into this
integration.

CI exposed a missing content binding in the policy harness and an unmatched brace
in the browser replay harness after reconciliation. Both were repaired; the
policy harness compiles and the changed JavaScript tools pass syntax checks.
Full browser CI still targets the retained older Published player.
