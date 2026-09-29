# AshenedSpire — build 27

Version **0.0.27.0** · source commit `10f4811005ead91aefd1fd06d26f26882d2a2a54` · local review candidate.
Source digest: `81a2af8ce111515dc38343e374c7fc5d47cde4ae2dbccc1181016043077dac76`.

- [x] **Branding and first-visit guidance**
  - [x] AshenedSpire title, Web page, Windows window caption and Android launcher label.
  - [x] Welcome, four-page field guide and About screen compiled and exercised.
  - [x] Existing save identity retained; actual build-26 browser save survives two reloads into build 27 with exact run, hand, resources and RNG state.
- [x] **Compiled browser verification — 186 checks**
  - [x] Welcome, guide and About: 36.
  - [x] Actual build-26 save upgrade: 8.
  - [x] Custom, Sealed, Draft and Endless openings/resume: 44.
  - [x] Menus and 160% text: 32.
  - [x] Full card descriptions and reachable actions: 32.
  - [x] Draft, shrine and merchant journey: 20.
  - [x] Two-player fight, reward and rejoin: 14.
  - [x] No browser/Unity errors recorded in these cases.
  - [x] Welcome, About, guide, title and large-text/card captures visually inspected.
- [x] **Local packages and focused validation**
  - [x] Web, Windows, Android and companion source-matched exports.
  - [x] Packaging: 452 companion, 163 native-file and 20 package checks.
  - [x] Windows startup and window caption verified; Android APK label/package/version inspected.
  - [x] Copied downloads verified byte-for-byte using SHA-256.
  - [x] Browser driver unit tests: 11. Runtime reference checks: 125, with three documented Unity-6 API gaps.
- [ ] **Foundation acceptance and release**
  - [ ] Full current-build campaigns, custom modes and Endless later-act transition.
  - [ ] Complete encounter-art, profile/quota, authoring, multiplayer recovery and appearance acceptance.
  - [ ] Owner decision about importing saves from the original JavaScript game.
  - [ ] Physical Android play, graphical Windows playthrough, iOS and device performance budgets.
  - [ ] Real-player feedback, owner visual/final acceptance and hosted channel promotion.

[Validation receipt](validation.json) contains scoped assertions, source identity and download hashes.
Raw evidence is retained locally under `work/evidence/build27-*`; co-op credentials and raw state are excluded from this review receipt.
The upgrade check uses the real previous player and browser input on the same origin/path. It does not inject saves.
The first co-op startup attempt encountered an occupied local test port; its failure log is preserved, and the final test uses a separate unused port. No game code was changed for that retry.
The mode and service journeys are bounded checks, not whole-game certification. The guide is not an interactive tutorial.
Windows caption verification launches an owned player and reads its window title; it is not a graphical playthrough.
Android package inspection does not prove behavior on a physical phone. Original save import remains undecided.
Prior build-26 domain/parity results are historical evidence; those suites were not re-run for this presentation change.

Work is on `feature/ashenedspire-foundation`, stacked after local build 26. No feature or core phase has been marked accepted and no remote publication/merge occurred.
The original game's latest inspected dev commit and deliberate porting gaps are recorded in [the upstream review](../../Unity-Upstream-Review.md).
The owner-requested daily comparison is active and reports meaningful relevant changes.
