# AshenedSpire — build 29

Version **0.0.28.1**, a Foundation development patch. Phase 1 remains incomplete.

- [ ] **Phase 1 — Foundation**
  - [ ] US-0.3: interaction acceptance.
    - [x] The legacy combat flask and inspection content retain space above fixed actions.
    - [x] Nine compiled checks: portrait and landscape reachability, touch size, charge consumption and exact healing.
    - [x] Full reference campaign: nine defeated enemies, rewards, equipment purchases, victory and exact reload.
  - [x] High-density touch scenarios: repeated rotations, six landscape inspections, synthetic insets and expanded Settings navigation.
    - [x] A second high-density touch run verifies scrolling Settings leaves its displayed values unchanged; six Settings scroll stops are explicitly checked. See [guarded run](mobile-settings-guard.json).
  - [x] Web, Windows, Android and companion packages share the verified source digest.
  - [x] All four public downloads were fetched and SHA-256 verified against the package manifest; see [download receipts](public-downloads.json).
  - [x] Current-build CI receipts: 36 original-save import checks, 16 native profile/slot checks and 14 two-player co-op checks. All tested runtime hashes match the package; see [CI evidence](ci.json).
  - [x] Current-build native campaign: 749 checks and 306 actual-input commands, exact reloads and the expected Act-3 defeat with Chronicle recording.
  - [ ] Finish broader original-save/profile compatibility, custom modes, multiplayer recovery, authoring/appearance matrices and owner acceptance.
  - [ ] Owner integration, Pages deployment and public-player verification.

Original save import remains available at **Saved climbs → Import original-game save** for compatible schema-5 map checkpoints. Newer original schemas, profiles and active rooms remain unsupported. This patch does not change save conversion.

Source digest: `7717600e2683ae8c3eb39a37b9452ced96bab6d56a8ebbaf2daacc5dc8dde92f`. See [validation](validation.json). The nine counted checks are the focused footer suite; the complete campaign and touch scenarios are additional results without invented check counts. Browser simulations are not physical-device testing.

The later [CI receipt](ci.json) supplements the original packaging-time validation above. The [browser/checkpoint pipeline](https://github.com/cehinds/AshenSpire-Unity/actions/runs/36548177241) passed against package commit `1b92c1d56a78c104dd9bdd996ac64892d4a2910e`; its publication job is skipped for PR runs. The [fast gate](https://github.com/cehinds/AshenSpire-Unity/actions/runs/36548176757) completed with only the existing version-sequence failure. Scope and results are preserved separately rather than relabeling build-28 evidence. No public Pages deployment or owner acceptance is implied.
