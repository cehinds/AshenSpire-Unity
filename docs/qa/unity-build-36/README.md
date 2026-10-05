# Build 36 — readable co-op readiness

Version `0.0.30.5` closes the lobby issue found in the final
[build-35 co-op pass](../unity-build-35/README.md). The host's Start action is
unavailable until at least two seats are connected and ready. The lobby explains
whether another wanderer must join, a member must reconnect or someone needs to
choose `I'm ready`. A stale-view server refusal receives a readable recovery
instruction. Unknown errors remain visible; the companion still enforces its
own authoritative readiness gate.

- [x] Readiness explanation, conservative Start availability and known-refusal text.
- [x] Runtime reference compile: 137 files; three existing reference-package
  Unity-6 API gaps accepted.
- [x] Focused card/QoL domain checks: 192 passed, including 18 readiness/notice
  cases for one through four players, disconnection, missing flags, stale views
  and preserved snapshots.
- [x] Fresh Web export and lobby/party browser verification.
  - [x] Web export and all eight frozen preview payload hashes verified;
    source digest `0baacd88861ab9e1a5a842b0cf6f1bd816e2c52fb39bb6e5fa29c78ac8cd4fcb`.
  - [x] Existing build-35 solo combat save reloads in build 36 with the exact
    turn, HP, resource pools, action count, pile counts and Bleed meter intact;
    zero console errors. See [observations](solo-browser-observations.json) and
    [screenshot](save-reload.png).
  - [x] Eleven two-player checks against the separately packaged build-36
    preview companion: one choosing/ready host cannot start; closing the host
    browser preserves its saved seat and resets readiness; a choosing guest
    disables Start; both ready enable it; Wait for me disables it again;
    disconnection explains reconnect/removal; guest rejoin resets readiness;
    readying again enables Start and both players enter the same seeded map.
    The 390x844 guest caption wraps above reachable readiness controls.
    [Observations](lobby-browser-observations.json) retain an early roster sample
    taken before the asynchronous reconnect response. It is excluded from the
    eleven completed checks. Captured final host/guest consoles contain zero
    errors and one/two existing warnings respectively.
- [x] Matching Windows/Android/Web/portable-companion candidate and all hashes.
  - [x] Android recovery completed; matching receipt and APK archived.
  - [x] Six metadata-integrity checks passed.
  - [x] Frozen Web receipt and eight payloads match final Delivery.
  - [x] Final companion ZIP/stamp are identical to the playtested package.
- [ ] Physical-device/controller, graphical-Windows and owner acceptance.

Build 35's verified players and delivery remain preserved. Its raw lobby
message observation remains historical evidence of the trigger for this change.

The frozen `WebPreview` matches all eight exporter hashes. The actual self-contained
Windows package under `PreviewCompanion` passed its payload and current-source
hash checks before launch; its [stamp](preview-companion-stamp.json) and the
[Web receipt](Web-source.json) preserve that provenance. Final `Delivery` packaging
completed; [package equivalence](package-equivalence.json) records byte-for-byte
reuse of that playtested ZIP. No additional final-companion launch is claimed.
The partial recovery-wrapper companion output was preserved and excluded.
Migration source edits began afterward; build 36 certifies its frozen source.
Initial host-key
entry timed out and produced a connection refusal; completing normal key entry
joined successfully. No authentication or origin restriction was changed.

Screenshots: [one seat](lobby-one-seat.png), [phone readiness](lobby-phone.png),
[disconnected guest](lobby-reconnect.png), [both ready](lobby-ready.png),
[shared phone map](shared-map-phone.png).
