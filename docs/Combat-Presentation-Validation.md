# Combat presentation validation — 0.5.0

Compiled source: `1a10b0618b8c45ea8b536b8540e890b0c954f57d`.
Source digest: `25819ed56fb110a539309c67b61d36f79d0520b134688aa402de106719ddf0d8`.

## Observed

- 224 domain checks passed: 47 original, 129 campaign, 25 class-identity and 23 feedback checks. New checks cover tag precedence, invalid cue records, actual blocked damage, capped healing, unchanged state/RNG and bounded deterministic sound samples.
- Current-content diagnostics completed 192 campaigns: 189 victories and three defeats, unchanged from 0.4.0. Presentation does not alter balance.
- The final packaged Reaver browser campaign completed all nine encounters, bought equipment, checked nine affinity/shared offers and restored the saved state exactly.
- Rogue, Herald and Starseer each passed initial combat/reward, inspection, narrow touch sizing and exact save/resume. These three browser runs were not full campaigns. Cue diagnostics confirm poison, faith and magic selection during play.
- Dedicated real-pointer checks passed normal and quick timeline completion/reset, static reduced motion, mute suppression of sound dispatch, preferences persisting through reload and navigation cancelling an active timeline. Active-frame and settled screenshots were inspected. The standard hyphen is visible in damage labels; enemy actions have explicit labels.
- A real 0.4.0 player created a combat save; the same browser origin switched to 0.5.0, which restored every saved field exactly and accepted a subsequent reward. No state/save injection was used.
- The six final local browser evidence sets contain 135 PNG screenshots with no reported browser errors. Previous 0.4.0 screenshots, metadata, changelog and balance reports remain under Published/History/0.4.0.
- Eight PCM reference WAVs were generated from the same waveform function used by the player and checked for sample rate, channel count, sample width and duration. Waveform tests verify finite bounded samples and fade to silence. This is not an audible quality or device-playback assessment.
- FeedbackCues CSV round-trip preserved authoritative bytes. An invalid color was rejected before replacement; source hash remained unchanged.
- Unity 6000.6.0f1 built Web, Windows and Android. Final Windows native headless startup reached ASHENSPIRE_UI_READY. No C# errors or warnings were found in the final target logs. Twelve package checks and 79 workflow checks passed; no scene, prefab or save-schema changes occurred.

## Corrected during validation

The first compile caught a resolved-translation type mismatch. A Windows text encoding conversion was caught in diff review and corrected. Active-frame inspection caught an unsupported minus glyph, replaced by an ASCII hyphen. These intermediate builds and failed logs remain in ignored Builds evidence; final packaged players include the corrections.

Start-Process -Wait also waited for a Hub helper surviving the failed Editor process. The build script now waits for the specific Editor's exit, preserving Hub and other processes.

## Limits

Physical phones, iOS, native Windows graphical play and audible listening remain unverified. Browser checks use desktop pointer input and phone-sized viewports. Existing sprite pixels are reused; no new painted art, soundtrack or full original-game parity is claimed. GitHub validates and publishes prepared players; unattended Unity compilation remains unconfigured. Publication and hosted verification are recorded separately in the delivery report.
