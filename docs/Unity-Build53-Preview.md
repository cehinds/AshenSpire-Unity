# AshenedSpire build 53 preview

- [x] Unity Web export: `0.0.33.6`, build 53, runtime source `c393630`.
  - [x] Source digest `646ee9eaa0422189c78ae79c3b038a84afe0a04dcca8560aa4685608a7605d4d`.
  - [x] All eight payload hashes, archive CRC and embedded payload hashes verified.
  - [x] ZIP: 120,873,641 bytes; SHA-256
    `f429b22d65246c92df52b33d1c64efccaf69c93238554edc515833d3eed5b9ea`.
- [x] Source changes and bounded validation.
  - [x] Build-52 party HUD, phone formations, lower controls and subpage transitions.
  - [x] Clear the inherited horizontal-tray height when mounting the combat popup.
  - [x] Bound deck search/type/sort fields so they cannot grow over the card grid.
  - [x] Read-only diagnostics identify the selected card; input tests check exact selection.
  - [x] 169-file runtime reference compilation, 1,068 valid GUIDs and six version checks.
  - [x] 17 driver/report tests, including 42 control-report assembly assertions.
  - [x] The unchanged party state model passed 23 checks in build 52.
- [x] Two-browser co-op: 32 assertions passed on the preceding build-53 candidate
  (`a6cf2c4f…`) at 390x844, pixel density 1.
  - [x] Three-enemy target bounds/non-overlap, End turn clear of the hand,
    usable tools/Flasks, compact deck filters and subpage return.
  - [x] Exact-hand rejoin, next-turn card play, selection retained across a peer
    action, a real shared victory and both players completing rewards.
  - [x] Compiled feedback reached impact; no observed browser/Unity errors.
  - [x] Inspected phone menu, deck, selected-card combat and rewards screenshots.
  - Evidence: `TestResults/NativeCoopBrowser/build53/{Host,Guest}`. This run
    tests client rejoin, not a companion process restart or a physical phone.
  - The final change only hides the decorative encounter heading on phones;
    co-op already hid that heading. WebAssembly/framework hashes match the
    tested candidate. The affected solo screens are rechecked separately below.
- [ ] Desktop/portrait solo checks and screenshot review.
- [ ] Download delivery and download-back hash verification.
- [ ] Current content activation, upstream migration, physical-device and owner acceptance.

Build-51 mounted property carriers and owned reactions are included in this
export, but compatible current content is not active. Property checks and their
Unity-compiled assembly evidence are in [the integration record](Unity-Property-Integration.md).
Original builds 914–963 are assessed in the upstream monitor; their new XP caps,
card ranks and skill rewards are not implemented here.

This is an intermediate Web preview, not Phase 1 completion or promotion of
Dev/Test/Release/Main. No current Windows/Android delivery or physical-device,
controller or owner acceptance is claimed. Earlier failed/superseded browser
runs remain documented in [build 52](Unity-Build52-Preview.md).
