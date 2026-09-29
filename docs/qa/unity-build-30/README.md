# Build 30 — original profile import and Foundation verification

- [x] Implement one-time additive original profile import, with preview, cancellation, retained native progress and run slots, source receipt, duplicate protection and journal persistence.
- [x] Importer domain suite: 469 checks, including 75 focused profile checks.
- [x] C# language compilation: zero errors and warnings.
- [x] Real CSV authoring: 245 checks across 77 tables, 474 fields and 1,214 records; authored additions pass 11 native-combat checks. [Field receipt](original-field-coverage.json).
- [x] Custom, Sealed, Draft and Endless policy simulations reach terminal results with no errors and exact save/resume comparisons. These are defeats, not balance acceptance or an Endless later-act transition.
- [ ] Complete corrected compiled profile-import verification and full browser mode replays.
- [ ] Export and verify matching Web, Windows, Android and companion packages.
- [ ] Publish the verified candidate and verify its public player/downloads.
- [ ] Wider Foundation compatibility/recovery/authoring coverage and owner acceptance.

Source candidate: `e3cecca`, version 0.0.29.0, build 30. Build 29 remains the
verified public release candidate at https://cehinds.github.io/AshenSpire-Unity/dev/.

The initial Web preview compiled with source digest
`176301d1df5acccd024c1a1a6e06320e237ade59c359ad026efb6ccbe162451f`.
Its profile-import browser check failed at confirmation with a false stale-profile
refusal. The domain regression reproduced serialized float preferences differing
from their in-memory representation. The correction compares the serialized
representation and guards durable and in-memory preview baselines separately,
preserving pending local preferences while still refusing real concurrent changes.
The corrected built player remains to be verified.

The first test started before the preview finalized and was deliberately canceled.
The second preserves the actual confirmation failure. A later Windows export and
Draft browser replay were interrupted when their local processes disappeared;
neither is a passing receipt. Logs and partial captures remain in the task workspace.

The restart workflow is `work/finish-build30.ps1` in the task workspace. It exports
all platforms, snapshots a separate Web preview, runs profile import and four full
mode browser replays, then compares every tested Web payload with the package
manifest. It stops on failure and neither publishes nor marks Phase 1 accepted.
Codex Process Jobs rejected this Windows host as unsupported; the ordinary
Windows runner executes the workflow instead. No detached-job notification is promised.
