# AshenedSpire: core phases

These four groups summarize the detailed F00–F17 roadmap. They are a reading aid,
not a second feature tracker. Completed implementation does not imply owner
acceptance or physical-device certification.

- [ ] **Phase 1 — Foundation (F00), target 0.1.0.0**
  - [x] Implement the native content/rules, save-slot and companion foundations.
  - [x] Package build 27 and pass its 186 scoped compiled browser checks (build 26 has 337 historical checks).
  - [ ] US-0.1: complete encounter-art coverage and owner visual acceptance.
  - [ ] US-0.2: complete current-build custom-mode play and save/resume coverage.
    - [x] Build 27: Custom, Sealed, Draft and Endless setup, opening combat and exact reload (44 checks).
    - [ ] Full playthroughs and Endless later-act transition.
  - [ ] US-0.3–0.4: finish combat, settings, inventory and service interaction coverage.
    - [x] Build 27: menus/large text (32), card reading (32), draft/shrine/merchant journey (20).
  - [ ] US-0.5: complete multiplayer recovery and interaction acceptance.
    - [x] Build 27: two players, combat, rewards and rejoin with packaged companion (14 checks).
    - [ ] Wider recovery matrix, host restart and final multiplayer acceptance.
  - [ ] US-0.6: complete profile/quota/upgrade checks and original-save import.
    - [x] Actual build-26 browser save upgrades through the rename with exact state (8 checks).
    - [x] Owner requires original JavaScript save import before Phase 1 completion (2026-09-28).
    - [x] Initial map-checkpoint converter, preview and empty-slot commit implemented; 376 domain and 12 browser-adapter checks passed.
    - [ ] Compiled import verification, broader save/profile compatibility and quota/corruption matrix; see [import checklist](Unity-Original-Save-Import.md).
  - [ ] US-0.7: complete content-authoring field/schema/runtime coverage.
  - [ ] US-0.8–0.10: hosted artifacts, appearance, final defects and owner acceptance.
- [ ] **Phase 2 — Gameplay and presentation (F01–F16)**
  - [x] Build-26 UI redesign and its bounded compiled verification.
  - [ ] US-1.4: first-visit welcome acceptance.
    - [x] Implemented and exercised in compiled build 27.
    - [ ] Owner acceptance.
  - [ ] US-1.5: About/AI acknowledgement acceptance.
    - [x] Implemented and exercised in compiled build 27.
    - [ ] Owner review of disclosure wording.
  - [x] Field guide and AshenedSpire branding implemented; 36 combined welcome/guide/About browser checks passed.
    - [x] Compiled Windows startup/window caption and Android launcher metadata verified.
  - [ ] Remaining controls, animation, visual/input parity and feature acceptance.
- [ ] **Phase 3 — Platform and performance (F17)**
  - [ ] US-17.1: establish and meet reference-device performance budgets.
  - [ ] US-17.2: physical Android and graphical Windows playtesting.
  - [ ] US-17.3: native iOS delivery and device validation.
- [ ] **Phase 4 — Final playtesting and release**
  - [ ] Real-player pacing, balance and fun checks.
  - [ ] Final regression and owner approval.
  - [ ] Owner-selected channel promotion; 1.0.0.0 is the owner's release decision.

No whole phase is currently signed off. The content/rules port, native runtime,
save slots and companion pipeline are implemented components of Foundation.
Build 27 is now the latest locally verified checkpoint, with 186 scoped compiled
browser checks and all four matching packages; see [QA](qa/unity-build-27/README.md).

The next milestone is Foundation acceptance. Build 27 completes the rename,
first-visit guidance and focused opening-mode/upgrade checks. Continue the
unaccepted foundation stories and review relevant changes in the original game. A guide is not an interactive
tutorial, opening-mode checks are not full playthroughs, and a compiled portrait
gallery does not cover every solo/co-op encounter.

Still required for Foundation: the broader current-build campaign/custom-mode,
inventory/services, multiplayer recovery, profile/quota/upgrade and authoring
matrix; artwork and appearance acceptance; the required original-save importer;
hosted artifact checks and the owner's final defect review.

See [roadmap](Unity-Roadmap.md), [handoff](CONTINUE-HERE.md),
[platform acceptance](Unity-Platform-Acceptance.md) and
[original-game comparison](Unity-Upstream-Review.md).
