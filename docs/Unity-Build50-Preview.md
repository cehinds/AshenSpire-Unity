# AshenedSpire build 50 preview

- [x] [Public preview and downloads](https://github.com/cehinds/AshenSpire-Unity/releases/tag/preview-build50).
  - [x] Downloaded the public ZIP back and verified its SHA-256.
- [x] Unity Web export: `0.0.33.3`, build 50, source commit `a0d1628`.
  - [x] Source digest: `68123de7ec5b44a271e2f3a7b2e24ef64a8202c3a0e6b9fd6e140afb6af55df2`.
  - [x] All eight payload hashes, archive CRC and embedded payload hashes verified.
  - [x] Web ZIP: 120,855,374 bytes.
  - [x] ZIP SHA-256: `3f5085d6807a4512038437325b32f30c5fe08163044fed60e9c0872d39460774`.
- [x] Source changes and bounded checks.
  - [x] Saved Arcane Ward provenance; shared-Stamina labels and card costs.
  - [x] Arcane Buildup fanout, saved-allocation flask refills and cross-zone card IDs.
  - [x] 41,047 migration, 6,587 legacy co-op and 4,538 native-run checks.
  - [x] 2,139 card-cost and 10,299 feedback checks; 167-file runtime reference check.
  - [x] Gamepad cancel routing for the startup settings choice; updated popup scroll diagnostics.
- [x] Exported-player checks at 1280x720 and 390x844: 42 assertions.
  - [x] Painted inspection, pay once, card removal and exact reload: 18.
  - [x] Startup keep/defaults, reload acknowledgement and saved-climb preservation: 24.
  - [x] Inspect desktop/portrait combat and portrait settings-choice screenshots.
  - The broader art/reset checks remain evidence from [build 49](Unity-Build49-Preview.md),
    which passed 80 assertions. They are not claimed as repeated build-50 tests.
- [ ] Activate the compatible current content bundle and verify its new-rule HUD/gameplay in the player.
- [ ] Finish recorded progression, property, class/book/sideboard, world/service and schema-20 import work.
- [ ] Final solo/co-op, platform, physical-device and owner acceptance.

This is an intermediate Web preview. The older runtime content remains active;
new saved-rule adapters do not silently change existing runs. It is not Phase 1
completion or a promotion of Dev/Test/Release/Main. Build-51 property integration
is separate source work and is not included here.

Local payload, ZIP and verification summary: `Builds/OwnerAppearance/build50/`.
Follow the ZIP's README to serve the extracted Web folder over HTTP.
