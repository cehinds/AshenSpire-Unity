# Content authoring validation — 0.6.0

Compiled source: `551285af6c20b41ac11c97d2d2978d8561558c88`.
Source digest: `a8d7174753a64ffdab7e8d5efa7e8bd6fdcaa6087b3033f65f989c157b5fabd3`.

## Observed

- 224 domain checks passed: 47 original, 129 campaign, 25 class-identity and 23 feedback checks.
- 21 checks passed inside Unity 6000.6.0f1 using real SerializedObject, SerializedProperty and Undo APIs. They cover seven table bindings, nested field/settings changes, Undo/Redo, readable unique IDs, deep-copy isolation, row removal, reward enrollment and draft serialization. Authoritative content stayed byte-identical.
- 19 .NET authoring checks passed for invalid definitions, missing equipment sprites, empty display names, stale saves, exact backups including BOM/line endings, import failure after save, checkpoint recovery and temporary-file cleanup.
- Equipment CSV round-trip preserved every authoritative source byte. Import of a missing equipment sprite failed before replacing source.
- Current-content diagnostics completed 192 campaigns: 189 victories and three defeats, unchanged from 0.5.0. Authoring changes do not alter gameplay or balance.
- The packaged Reaver browser campaign completed all nine encounters, purchased equipment, checked nine affinity/shared reward offers and restored saved state exactly.
- Rogue, Herald and Starseer each passed initial combat/reward, inspection, phone-width touch sizing and exact save/resume. These three browser checks were not full campaigns.
- Real-pointer feedback checks passed normal and quick timeline reset, static reduced motion, mute suppression, persisted preferences and cancellation when navigating away.
- A real 0.5.0 player created a combat save. The same browser origin switched to 0.6.0, restored all saved fields exactly and accepted a later reward. No save/state injection was used.
- The six local browser evidence sets contain 135 PNG screenshots and report no browser errors. Prior 0.5.0 screenshots, reports, metadata and changelog remain under Published/History/0.5.0. Existing audio previews are retained because their source data and waveform implementation did not change.
- Unity built Web, Windows and Android. Windows headless startup reached ASHENSPIRE_UI_READY. No C# errors or warnings were found in the three final target logs. Twelve package checks and 82 workflow checks passed.

## Limits

Editor tests exercise native serialization and Undo APIs, not graphical interaction with the authoring window. Browser checks use real desktop pointer commands at phone-sized viewports; physical Android/iOS play, graphical Windows play and audible listening remain unverified. ADB reported no connected devices. The Android inventory tool does not install or run the game.

Draft checkpoints happen explicitly and on window close/recompile. They are not continuous or crash-proof autosave. Hash conflict detection is optimistic, not a distributed write lock. Source backup/replacement and subsequent Unity import have distinct outcomes.

GitHub validates and publishes prepared players; unattended Unity compilation remains unconfigured. Deployment and hosted checks are recorded separately in the delivery report. Test, release and main have no selected Unity build.
