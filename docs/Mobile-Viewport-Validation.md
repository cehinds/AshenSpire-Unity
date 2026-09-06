# Mobile viewport validation — 0.7.0

Compiled source: `2c558b71fc15a45fb1715d3b716a7a964bd36565`.
Source digest: `2eaeedaace2522361d29b99e7af812e84fc7fc77726a7fc2b9de59c7912075b6`.

## Observed

- The unmodified 0.6.0 Web player failed the new density-3 baseline: intent-details measured 41.33 CSS pixels at 320 x 740. Failure image and raw control geometry are retained in Published/MobileEvidence/Baseline.
- 14 display-space sizing checks passed, including compact/portrait thresholds and zero-size initialization. The existing 224 domain checks passed during target builds.
- Densities 1, 2 and 3 each passed eight layout snapshots: narrow portrait, both landscape sizes, repeated rotation, synthetic insets, settings and seed entry. Every measured touch height is at least 44 CSS pixels within 0.005 pixels of float rounding. Render density remains 1 for density-1 browsers and 1.5 for density-2/3 browsers.
- The density-3 Starseer run used touch taps and swipe scrolling. Two landscape intent inspections and the synthetic-inset inspection preserved campaign state. Combat, reward selection and exact save/resume passed. Seed entry uses a test keyboard and leaves field focus before gestures; this is not a physical mobile keyboard check.
- Rogue and Herald passed combat/reward and save/resume at densities 1 and 2. Reaver completed the full nine-encounter campaign, bought equipment and checked nine reward offers.
- Normal, quick and reduced motion, mute persistence and animation interruption passed their existing real-input regression.
- A real 0.6.0 Web player produced a combat save, then the same origin switched to 0.7.0. Every saved field restored exactly and a subsequent reward was accepted. No state/save injection was used.
- Six local gameplay evidence sets contain 162 PNG screenshots and report no browser errors. The previous 0.6.0 checkpoint evidence remains in Published/History/0.6.0. Unchanged authoring/audio/balance reference evidence is retained with its original provenance.
- Unity 6000.6.0f1 built Web, Windows and Android. Windows headless startup reached ASHENSPIRE_UI_READY. Final target logs contained no C# errors or warnings. Twelve package and 88 workflow checks passed.

## Test-driver corrections

Initial touch attempts remained in seed-entry focus or released a tap between player frames. The driver now submits/leaves the field and holds taps across frames. A landscape probe exposed swipe inertia moving a target during a tap; the driver waits for stable observed geometry and swipes inside the scroll area above fixed actions. Those failed attempts remain in ignored Builds evidence. No gameplay workaround was added for them. The first CI candidate failed a landscape inspection. The final driver records its chosen input bounds and waits for actual inspection content, instead of treating any layout update as proof of navigation.

## Boundaries

The new bridge uses the displayed Web canvas height. Native screen/safe-area behavior, all domain rules, campaign JSON, save schema, scenes and prefabs are unchanged. Scroll-only updates no longer rescan touch-control sizes. Only PlayerSettings bundleVersion changed; Unity generated new C# and plugin metadata. No new Inspector wiring or package is required.

Physical Android/iOS play, graphical native Windows play, native editor interaction and audible listening remain unverified. ADB found no connected devices. Android is an ARM64 test APK, version 0.7.0, minimum API 26. Insets are synthetic CSS padding, not a physical notch or browser-toolbar test. No physical-mobile performance, frame-rate or battery-life improvement is claimed. Deployment and hosted verification are recorded separately in the delivery report.
