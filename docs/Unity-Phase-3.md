# Phase 3 — platform and performance

The owner requested Phase 3 alongside continuing Phase 2 polish on 2026-10-03.
Local exports, installed-player play-through, physical-device measurements and
owner acceptance are separate gates. See [Phase 2](Unity-Phase-2.md) for gameplay.

Build 36 is the matching archived rollback candidate. Build 37 is a separate,
intermediate Web preview for the [published-test-898 migration](Unity-HTML-Parity-Migration.md).
The new migration has not received final platform or device validation.

- [ ] US-17.1 — performance on reference phones
  - [x] Local candidate tooling records source digest, exact payload hashes and
    download bytes, and enforces the existing 100 MiB download gate.
  - [ ] Select reference Android/iPhone/iPad models and measurable budgets.
  - [ ] Measure cold/warm start, peak process memory, sustained combat frame
    time and thermal slowdown on those devices.
  - [ ] Owner acceptance of the measured experience.
- [ ] US-17.2 — Android and Windows
  - [x] Windows build 33 exported; all 162 payload hashes match the exporter.
  - [x] Build 35 matching Windows/Android/Web/companion candidate verified:
    163 native payload checks, 457 companion checks and six metadata checks.
    See [build-35 evidence](qa/unity-build-35/README.md).
  - [x] Build 36 matching Web/Windows/Android/companion candidate archived;
    six metadata checks pass, and its companion ZIP matches the playtested bytes.
    See [build-36 evidence](qa/unity-build-36/README.md).
  - [x] Actual portable Windows companion starts and serves the staged Web
    player; two browser clients complete nine focused shared-combat UI checks.
    This verifies the companion, not the graphical Windows game executable.
  - [ ] Graphical Windows creation/combat/save/relaunch and LAN play-through.
  - [ ] Physical Android install, touch/hold/flick, interruption, rotation,
    save/relaunch and LAN play-through. No device was attached on 2026-10-03.
  - [ ] Physical controller testing and owner acceptance.
- [ ] US-17.3 — iOS
  - [x] Installed iOS Unity module inventoried and the delivery prerequisites
    documented in [platform acceptance](Unity-Platform-Acceptance.md).
  - [ ] Mac/Xcode/signing environment, chosen bundle ID and target devices.
  - [ ] Native iOS export/archive, device validation and accepted distribution.
- [ ] US-17.4 — archive capacity
  - [x] Existing immutable archive-hosting checks pass: 27 hosting tests and
    35 channel-storage checks preserve exact committed payloads and save identity.
  - [x] A fresh local candidate can be staged without overwriting Published.
    Candidate verification rejects mixed source builds and unreceipted bytes.
  - [ ] Verify capacity and network delivery against the final hosted artifact.

## Local delivery workflow

After exporting matching Web, Windows and Android players and building the
matching companion, stage into a new directory beneath `Builds`:

```powershell
node tools/unity-stage-candidate.mjs <new-candidate-directory> <companion-folder>
node tools/unity-stage-candidate.mjs <candidate-directory> --check
```

To perform the complete export and staging sequence with the pinned Editor:

```powershell
./tools/unity-build-candidate.ps1
```

This uses the pinned `D:/Unity` installation by default; pass `-EditorPath` for
another installation. It refuses an active Editor or existing candidate root,
backs up and verifies prior exports, uses a short temporary directory beneath
`D:/repos/.codex/tmp`, and exports
the three players sequentially before building the companion and checking the
fresh delivery directory. Build logs stay beside that candidate.

The candidate manifest records whether the source worktree is dirty and retains
each exporter's receipt. It records device/graphical-Windows/publishing gates as
false; packaging success does not satisfy those gates. Existing Published files
and player saves are preserved. Candidate directories are immutable inputs to
review: use a new directory for another export rather than replacing one.

The companion validator accepts an explicit candidate's Published directory;
its 24 mutation checks include isolation from an invalid default release and
refusal of an invalid candidate receipt. No remote publication is performed.

## Verified build-35 local candidate

`Builds/PlatformCandidates/build35-retry-20261003/Delivery` contains version
`0.0.30.4`, source digest
`a17b17e07cf104f75499b8ff7981bec944d44272721cce7e1cdac30df5e8d465`.
The complete export/staging workflow and its final `--check` passed. Six
additional checks rejected altered download sizes/hashes, raw Web size and
nested platform provenance while preserving the original delivery manifest.

| Download | Bytes |
| --- | ---: |
| Web.zip | 86,549,128 |
| Windows.zip | 101,803,914 |
| Android.apk | 93,195,392 |
| Companion.zip | 46,034,642 |

Every download is below the existing 104,857,600-byte gate. The uncompressed
Web runtime payload is 130,542,149 bytes. These are measured local package
sizes; no phone startup, frame-time, thermal or memory result is implied.
The [candidate receipt](qa/unity-build-35/candidate.json) records the dirty
source worktree and leaves physical-device, graphical-Windows and publication
flags false. Prior exports and existing Published releases were preserved.
