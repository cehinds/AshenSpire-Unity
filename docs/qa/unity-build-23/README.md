# Build 23: interface audio

Version **0.0.23.0**, build **23**. Source digest:
`2bc3124c0f7bb1e1fe875b6a479a2322a82dcb4205324a0375dada163db13600`.
Runtime source commit: `6de5196c5dc54cdec19144b4e8003944ba181174`.

## Changed behavior

Buttons, toggles and dropdown changes now produce a short interface tick. It
uses its own cached clip and AudioSource, so an interface action does not stop
the separate combat source. Master × Interface controls its volume; Master ×
Sound effects controls combat cues. Mute, interruption and component shutdown
stop both sources. Settings includes separate interface and combat previews.
Controls are bound once using weak keys, including recreated screens and co-op.
No save schema, game rules, scene references or Inspector setup changed.

## Validation

- `native-audio-playtest.cjs`: **32 checks** on the compiled player at 390×844.
  Observed real Web Audio PCM and gain, keyboard activation, cancelled drags,
  no duplicated callbacks, independent levels, zero/mute suppression and reload.
  The observed gain **0.113460004** matches 0.3 × 0.61 × 0.62. Zero/mute cases
  started no transient Web Audio source. See [audio.json](audio.json).
- `campaign-playtest.cjs --feedback-only`: 14 feedback events, three combat
  sound events, normal/fast/reduced modes, persisted preferences and navigation
  cancellation passed; no browser/Unity errors. This is feedback coverage,
  not a full campaign victory.
- `native-coop-ci.cjs` used the executable extracted from this candidate's
  `Published/Companion.zip`: **eight host and six guest checks** passed through
  a real shared fight, rewards, exact-hand rejoin, next-turn play and selection
  retained during peer actions. See [coop-summary.json](coop-summary.json).
  Full state/invitation records are excluded from published evidence.
- Standard build prechecks: Domain, Parity, CardText, CardCosts, HandRules,
  MapKnowledge and MapViewport passed. Feedback synthesis includes four new
  bounded/deterministic interface-sample assertions (27 feedback checks total).
- Music **502**, settings/mods **85**, runtime compile **121** checks passed.
  C# 9/.NET Standard compilation had no warnings/errors. Original JavaScript
  tests passed **136/136**. Browser-driver tests passed **11/11** and archive
  review regressions **9/9**. An older-source capture was rejected before writes.
- Unity 6000.6.0f1 exported Windows, Android and Web. Companion packaging and
  final verification passed **451 companion, 163 native-file and 20 package
  checks**. All target receipts match the digest above.

Windows ZIP: 103,764,081 bytes; Android APK: 93,017,248; Web ZIP: 86,546,476;
companion ZIP: 47,540,425. Current screenshots and review metadata were generated
with `tools/unity-review-evidence.mjs` into `Published/BuildReview23`.

## Limits and delivery dependency

This is a local candidate on `feature/unity-interface-audio`. Build 22's draft
[PR #56](https://github.com/cehinds/AshenSpire-Unity/pull/56) remains unchanged and
fully green for owner review. The version gate passes six checks against build
22; it fails against current dev (build 21) because only one version step is
allowed. The owner must merge build 22 before build 23 can pass against dev.
No version gate was weakened and no agent merge or channel promotion occurred.

Software WebGL checks do not certify listening quality or physical-device
behavior. Graphical Windows and physical Android audio/play were not tested for
this candidate. Three/four-player co-op and full-run acceptance remain open.
F00–F17 and owner visual/profile acceptance remain in progress.
