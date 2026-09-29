# AshenedSpire — build 29

Version **0.0.28.1**, a Foundation development patch. Phase 1 remains incomplete.

- [ ] **Phase 1 — Foundation**
  - [x] US-0.3: the legacy combat flask and inspection content retain space above fixed actions.
    - [x] Nine compiled checks: portrait and landscape reachability, touch size, charge consumption and exact healing.
    - [x] Full reference campaign: nine defeated enemies, rewards, equipment purchases, victory and exact reload.
  - [x] High-density touch scenarios: repeated rotations, six landscape inspections, synthetic insets and expanded Settings navigation.
  - [x] Web, Windows, Android and companion packages share the verified source digest.
  - [ ] Finish broader original-save/profile compatibility, custom modes, multiplayer recovery, authoring/appearance matrices and owner acceptance.
  - [ ] Owner integration, Pages deployment and public-player verification.

Original save import remains available at **Saved climbs → Import original-game save** for compatible schema-5 map checkpoints. Newer original schemas, profiles and active rooms remain unsupported. This patch does not change save conversion.

Source digest: `7717600e2683ae8c3eb39a37b9452ced96bab6d56a8ebbaf2daacc5dc8dde92f`. See [validation](validation.json). The nine counted checks are the focused footer suite; the complete campaign and touch scenarios are additional results without invented check counts. Browser simulations are not physical-device testing.
