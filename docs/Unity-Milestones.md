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
  - [ ] US-0.8: hosted artifacts and archive capacity.
    - [x] Four public build-29 downloads fetched and verified against packaged hashes.
    - [x] All 31 archived players retained at 383.1 MiB; 3,458 navigation checks pass. Earlier hosting validation includes three sampled player startups.
    - [ ] Owner merge, Pages deployment and public player verification; see [hosting checklist](Unity-Archive-Hosting.md).
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
  - [x] Build 31 implementation: controller navigation/rebinding, accessibility
    and display settings, merchant buy-back visibility and automatic rewards.
  - [x] Build 31 implementation: enemy status inspection, room/death transitions,
    authoritative co-op feedback and bundled Web/Android content packs.
  - [x] Use AshenedSpire-Editor's pose validator/sampler: 21 reference samples.
  - [x] Build 31 focused exported Web verification: 268 checks across 15 cases,
    including virtual-controller input, accessibility, combat tools, rewards,
    four appearance styles, audio, merchant/shrine and co-op host restart.
  - [x] Current Web victory replay (542 checks), all three boss encounters and
    Endless Act 4 reload/next-room entry (559 checks), plus held-confirmation
    focus interruption (6 checks). Those original suites total 1,375 checks.
  - [x] All 22 events/62 choices, four-viewport Web coverage, current mode reloads,
    Ascensions 0–6 and all 11 modifiers. Current build: 6,280 checks in 48
    cases; see the [detailed Phase 2 checklist](Unity-Phase-2.md).
  - [x] Listed build-31 features and bounded exported Web verification complete.
  - [ ] Current-core quality-of-life follow-up: card play, inspection, targeting and presentation (owner review, 2026-10-02).
    - [x] Build 33 implementation: visual offers/readers, target-tap play, drag/flick, live previews, target memory, filters and co-op Escape.
    - [x] Compile 135 source files; focused preview/flick 158 checks, co-op 461 checks / 146 batches, transient target state 21 checks.
    - [x] Final frozen-source Web export, eight payload hashes and normal-input solo/two-peer regression; see [build 33](qa/unity-build-33/README.md).
    - [x] Builds 34–35: readable status meters/thresholds, resolved authored
      description numbers and fresh solo/co-op player checks.
    - [x] Build 36: readable lobby readiness, conservative Start availability,
      137-file compile, 192 focused checks and eleven two-player preview checks.
      See [build 36](qa/unity-build-36/README.md) for matching delivery status.
    - [ ] Owner acceptance and physical hold/flick/controller input.
  - [ ] Owner acceptance and physical-controller/device checks.
- [ ] **Phase 3 — Platform and performance (F17)**
  - [x] Matching build-35 Web/Windows/Android/companion candidate, exact hashes,
    download size gates and six metadata refusal/preservation checks.
  - [x] Candidate tooling preserves earlier exports and Published releases;
    portable Windows companion runs the two-browser playtests.
  - [ ] US-17.1: establish and meet reference-device performance budgets.
  - [ ] US-17.2: physical Android and graphical Windows playtesting.
  - [ ] US-17.3: native iOS delivery and device validation.
  - [ ] US-17.4: final hosted archive capacity and network delivery.
  - [ ] Owner acceptance. See [detailed Phase 3](Unity-Phase-3.md).
- [ ] **Phase 4 — Final playtesting and release**
  - [ ] Real-player pacing, balance and fun checks.
  - [ ] Final regression and owner approval.
  - [ ] Owner-selected channel promotion; 1.0.0.0 is the owner's release decision.

No whole phase is currently signed off. The content/rules port, native runtime,
save slots and companion pipeline are implemented components of Foundation.
Build 36 is the archived verified multi-platform rollback checkpoint. Build 37
is the current intermediate migration source; complete published-test-898
behavior, visual parity and final delivery remain open in the
[migration tracker](Unity-HTML-Parity-Migration.md).
Build 28's 879 scoped compiled browser checks remain
historical evidence; see [build 29](qa/unity-build-29/README.md)
and [build 28](qa/unity-build-28/README.md). No whole phase is accepted by these checks.

Foundation acceptance remains open while the owner-requested Phase 2 work proceeds.
Build 27 completes the rename,
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
