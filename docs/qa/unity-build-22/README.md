# Build 22 compiled review

This is evidence for review, **not owner acceptance**. All roadmap features remain open.

The initial source-matched build-22 checkpoint (`93540d6`, digest
`c16679867a29b7a304ee252ace53ef99753b0d300f2650920cce75a840c681cc`) passed:

- Enemy catalog: **19 enemies, 38 screenshots, 78 checks**, phone and desktop.
- Visual flow: **148 checks**, at 320×640, 390×844 and 1440×900; creation,
  first combat, actual card payments, enemy turn and exact save restoration.
- Card readability/payment: **152 checks** across the same three sizes;
  unaffordable selection, disabled Play, payment and reload.
- Saved settings and inventory navigation. See `settings-scope.json` for limits.
- Draft and paid services: **20 checks** using a seven-floor Custom Climb with
  merchant weights selected in the real setup screen. Draft/reload, shrine
  cancellation/purchase, flask split, merchant purchase/resale and reload passed.
- CI native climb: **748 checks**, 305 commands through terminal Act 3 defeat,
  with 17 fights won and the chronicle correctly updated. Not a victory claim.

The included screenshots were visually inspected. The full 38-image gallery and
four original/Unity screen comparisons are delivered in the task's `outputs`
directory. Portrait registration is proven; every actual encounter is not.

Initial CI found a real co-op lobby music bug: JSON `game:null` was accessed as an
object. Commit `af13ed9` fixes it. Corrected exports in `b41e32f` match digest
`54b429cfb6788b65be1ed34a185c8422841c44439a50f922ecfc60374a11e3ea`.
The original images above retain their original digest. The corrected player
passes a fresh 19-enemy gallery (78 checks), enemy combat/feedback/reload (66),
packaged-companion two-client fight/reward/exact-hand rejoin (eight host/six
guest checks), foundation campaign victory/reload, and performance sampling.
The foundation campaign is distinct from the original native climb.

[All 42 applicable runtime CI checks passed](https://github.com/cehinds/AshenSpire-Unity/actions/runs/36346337322)
at `b41e32f`, with the [fast gate](https://github.com/cehinds/AshenSpire-Unity/actions/runs/36346337124)
also green. `Published/BuildReview` holds 24 current screenshots and the generated
review guide; `Published/validation.json` records six source-matched suites.

Pages allows deployment only from `dev`. Later review metadata needs the new
`attachBuildReviews` assembler support in this PR before it can replace the
archive's first-export screenshot selection. Original player/download commits
remain immutable. Nine regression checks cover exact-byte identity, digest
matching, failed captures, missing files and path safety; existing history and
channel-storage checks also pass. Owner merge is required to publish this tooling.

CI also exposed stale probes: re-selecting an inspected card toggled it off,
and all-enemy redraws invalidated an assumption that the last asset report was
the selected target. The corrected probes preserve real pointer input and
state assertions. The campaign movement probe now selects an attack explicitly.

Physical Android, graphical Windows play, audible listening, real-player fun,
all full modes and owner visual/profile sign-off remain outstanding. No feature
has been marked done. LAN tests passed with an explicit local 60-second deadline;
the normal CI deadline remains 10 seconds and is not claimed as a local pass.
