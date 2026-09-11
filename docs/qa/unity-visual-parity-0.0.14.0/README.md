# Build 14 evidence

All final player suites use digest `2ac07c417b88e2adb899c532940736059d9c2769ee5870879fba91a608c5d2d5`.

- VisualFlow: 148 checks, three viewports, 21 screenshots.
- CardReadability: 146 checks, three viewports, 12 screenshots.
- LongCardReading: 30 checks, two viewports, four screenshots; real wheel input exposes the final line above Play without changing game state.
- FullClimb: 657 checks, 280 commands, three acts and exact reloads.
- Coop: eight host and six guest checks. Lobby pictures, credentials and complete transport/control snapshots remain local and are excluded here.
- Checks: matching exports, pure suites, package logs and font/reporting receipts.
- Attempts: prior failing visual/diagnostic candidates remain separate. The invalid stylesheet GUID and split Unicode logging were fixed before the final export. The first package refusal was a Windows path-order verifier bug, fixed without changing players or receipts.

The first two co-op runs were interrupted locally: one to reduce machine pressure, the second because the legacy test still tried to reset a paged hand 100 times. The final narrow harness patch uses actual horizontal scrolling and retains all gameplay assertions. Partial runs remain in TestResults and claim no success.

Screenshots prove visible pixels; input/state receipts prove only their tested behavior. Physical Android, graphical Windows play, iOS, complete visual parity and human balance acceptance remain open.
