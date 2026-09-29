# AshenedSpire: core phases

These four groups summarize the detailed F00–F17 roadmap. They are a reading aid,
not a second feature tracker. Completed implementation does not imply owner
acceptance or physical-device certification.

- [ ] **Phase 1 — Foundation (F00), target 0.1.0.0**
  - [x] Implement the native content/rules, save-slot and companion foundations.
  - [x] Package build 29 for Web, Windows, Android and companion; focused flask reachability, complete reference-campaign and high-density touch scenarios pass against its exact Web payloads. See [QA](qa/unity-build-29/README.md).
    - [x] Current native campaign replay: 749 checks and 306 commands, exact reloads and the recorded Act-3 defeat; [CI receipt](qa/unity-build-29/ci.json).
  - [x] Package build 28 and pass its 879 scoped compiled browser checks (build 27 has 186 historical checks).
    - [x] Standard campaign replay: 306 real-input commands, exact reloads, expected Act-3 defeat and recorded Chronicle result (749 checks).
  - [ ] US-0.1: complete encounter-art coverage and owner visual acceptance.
    - [x] Build 28: all 19 painted portraits load in the compiled phone/desktop gallery (78 checks, 38 captures).
  - [ ] US-0.2: complete current-build custom-mode play and save/resume coverage.
    - [x] Build 27: Custom, Sealed, Draft and Endless setup, opening combat and exact reload (44 checks).
    - [ ] Full playthroughs and Endless later-act transition.
  - [ ] US-0.3–0.4: finish combat, settings, inventory and service interaction coverage.
    - [x] Build 27: menus/large text (32), card reading (32), draft/shrine/merchant journey (20).
  - [ ] US-0.5: complete multiplayer recovery and interaction acceptance.
    - [x] Build 27: two players, combat, rewards and rejoin with packaged companion (14 checks).
    - [x] Build 29 CI: 14 two-player checks, including exact-hand rejoin, next-turn card play and retained peer selection; [receipt](qa/unity-build-29/ci.json).
    - [ ] Wider recovery matrix, host restart and final multiplayer acceptance.
  - [ ] US-0.6: complete profile/quota/upgrade checks and original-save import.
    - [x] Actual build-26 browser save upgrades through the rename with exact state (8 checks).
    - [x] Owner requires original JavaScript save import before Phase 1 completion (2026-09-28).
    - [x] Initial map-checkpoint converter, preview and empty-slot commit implemented; 394 domain and 12 browser-adapter checks passed.
    - [x] Build 28 compiled file/browser-slot import, preview, cancellation, duplicate refusal, exact reload and combat continuation: 36 checks; native profile/slot regression: 16 checks.
    - [x] Build 29 CI: the same 36 import and 16 native profile/slot checks pass against the current packaged runtime.
    - [ ] Broader save/profile compatibility and quota/corruption matrix; see [import checklist](Unity-Original-Save-Import.md).
  - [ ] US-0.7: complete content-authoring field/schema/runtime coverage.
    - [x] Real CSV transactions preserve 474 fields across all 77 shipped record tables (1,214 records); 245 checks plus 11 native checks exercising authored additions. [Field receipt](qa/unity-build-30/original-field-coverage.json).
    - [ ] Complete field-specific rendered behavior and editing acceptance.
  - [ ] US-0.8: hosted artifacts and archive capacity.
    - [x] Four public build-29 downloads fetched and verified against packaged hashes.
    - [x] All 31 archived players retained at 383.1 MiB; 3,458 navigation checks pass. Earlier hosting validation includes three sampled player startups.
    - [x] Owner-authorized merge, Pages deployment and public build-29 verification: five compiled checks and nine page/image responses; see [hosting checklist](Unity-Archive-Hosting.md).
  - [ ] US-0.9–0.10: appearance, final defects and owner acceptance.
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
Build 29 is the latest locally verified Web patch. Build 28's 879 scoped compiled
browser checks remain historical evidence; see [build 29](qa/unity-build-29/README.md)
and [build 28](qa/unity-build-28/README.md). No whole phase is accepted by these checks.

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
