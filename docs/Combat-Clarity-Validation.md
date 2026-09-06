# Combat clarity validation — 0.3.0

Compiled source: 9bf7c44e10dde5c40ccd20e42fb55e14ad06a122.
Source digest: 540d80164172c820799e8ed17a6881addbe0971f5788b1379125a8886a2bd2e3.

The slice adds intent/status explanations, grouped draw/discard inspection, twelve recent action outcomes, inspectable unaffordable cards, actual healing/damage feedback and minimum touch-target scaling. It preserves campaign rules and save fields. Returning from inspection deliberately clears card selection. History is session-local; Continue reconstructs a session, so prior history is not restored.

## Verification

- 47 original domain checks plus 129 campaign checks: 176 passed. All 48 seeded policy campaigns completed in victory. These are regression/reachability results, not a human balance assessment.
- Targeted checks cover block absorption versus actual card damage, capped flask healing, modified attack intent, all three non-attack intents, poison timing, read-only explanations, rejected commands and bounded/transient history.
- The browser harness checks 320x740 inspection controls, repeated navigation without state/command/RNG changes, grouped draw counts, empty discard, filled discard, recent action feedback, history reset on Continue, unaffordable inspection/cancellation and matching visible build version. It also plays the complete nine-encounter campaign, purchases equipment, and compares every saved field after reload.
- Unity 6000.6.0f1 built Web, Windows and Android. Windows reached ASHENSPIRE_UI_READY in native headless startup. No C# errors were observed. The unchanged GameAudio FindFirstObjectByType call produces an obsolete-API warning in current compiler logs; it is not a new combat-clarity failure.
- Twelve package checks bind the runtime source, nine artifact hashes, Web cache tokens and displayed version. Web export now stamps its version rather than retaining a hardcoded old label.
- Previous 0.2.1 screenshots and cache evidence remain under Published/History/0.2.1. The final packaged browser completed all acceptance checks and reached victory; its 33 screenshots and report are under Published/Screenshots.

## Limits

Physical Android/touch hardware, native Windows graphical gameplay and iOS are not verified. The minimum-target assertion covers named inspection controls at the tested narrow viewport; it is not a universal accessibility certification. Transform measurements are rounded to hundredths of a screen pixel to avoid treating 43.99998 as an actual sub-44-pixel control.

No combat rules or save schema were changed. No browser-save migration, new content, persistent history, class rebalance or unattended Unity compilation is included. Channel publication and selected release promotion remain separate.
