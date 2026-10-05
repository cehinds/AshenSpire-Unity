# AshenedSpire build 52 preview

- [x] Source: `0.0.33.5`, build 52.
  - [x] Party combat uses compact shared controls, a roster inside its resource HUD,
    separate phone enemy positions and a lower action row clear of the hand.
  - [x] Co-op inspector Back retains the selected card; Escape on the battlefield
    still clears it. This is an intentional native interaction change.
  - [x] Open combat tools survive peer updates and disappear when opening subpages.
    Pending commands disable their gameplay controls while Leave remains available.
  - [x] Includes build-51 saved property carriers and owned party reactions.
- [x] Local validation.
  - [x] 169-file runtime reference compilation and 1,068 valid asset GUIDs.
  - [x] 23 co-op presentation-state checks and six version checks against build 51.
  - [x] 17 driver/control-report tests, including 42 report-assembly assertions.
- [ ] Final Unity export, exact-source receipt and archive verification.
- [ ] Desktop/portrait solo and two-browser co-op playtests; screenshot inspection.
- [ ] Download delivery and download-back verification.
- [ ] Current content activation, complete upstream migration and owner acceptance.

Earlier build-52 attempts remain separate evidence. The first was superseded
before player testing to remove combat chrome on subpages. The second verified
its export/archive and phone target/action separation, but its menu-close test
mistook hidden zero-size controls for visible controls. Its screenshots also
revealed an encounter-heading/intent collision. Both findings are corrected in
the final source; the earlier archive is retained under `build52/attempt2`.

This remains an intermediate native Web preview. It does not claim the new
content bundle is active, parity with original build 963, current Windows or
Android exports, channel promotion, physical-device testing or phase acceptance.
