# AshenedSpire build 49 preview

- [x] Unity Web export: `0.0.33.2`, build 49, source commit `120182f`.
  - [x] Source digest: `167a55309efd6cc813c106d8fe56241b1ee66ac2523d27adbad53243d3f504a0`.
  - [x] Verify all eight exported payload hashes before testing and packaging.
  - [x] Web ZIP: 120,852,145 bytes; CRC and embedded payload hashes verified.
  - [x] ZIP SHA-256: `13abab2e1fa654dd9889f45f6a67ad7ffc94fb28ee4cec0d262eb512401309d6`.
- [x] Compiled-player checks at 1280x720 and 390x844: 80 assertions.
  - [x] Three starter paintings and creation navigation: 14.
  - [x] Settings reset, cancellation, focus, persistence and saved-climb preservation: 24.
  - [x] Startup keep/defaults choices, reload acknowledgement and saved-climb preservation: 24.
  - [x] Painted combat inspection, pay once, card removal and exact reload: 18.
  - [x] Inspect paintings, portrait audio settings, startup choice, card inspector and resumed combat screenshots.
- [ ] Physical Android/Windows/controller testing and owner acceptance.
- [ ] Complete recorded upstream migration and activate the compatible new content bundle.
  - [ ] Current stats/foundations, XP/rewards, books/classes/sideboard, world/services and schema-20 import.
  - [ ] Remaining visual/animation/audio/haptic parity and final solo/co-op checks.

This is an intermediate Web preview, not Phase 1 or migration acceptance.
The older runtime content remains active. Optional saved hand and Stamina rules
are implemented but are not silently installed into old runs. Build-50 source
work is separate and is not contained in this archive. `Published/` and the
Dev/Test/Release/Main channels are not promoted by this preview.

Local export and archive: `Builds/OwnerAppearance/build49/`.
Evidence: `TestResults/{UpstreamStarterArt,SettingsReset,SettingsDefaults,PaintedCombat}/build49/`.
The archive contains instructions for serving the extracted Web folder over HTTP.
Its sibling `build49-verification.json` records the bounded checks and remaining gates.
