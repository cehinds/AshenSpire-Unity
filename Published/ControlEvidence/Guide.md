# Control diagnostics after tab return

Version 0.8.3 follows the 0.8.2 Web renderer workaround. The 0.8.2 PR validation passed, but its dev deployment failed twice waiting for the return control. In both failures the screenshot showed the cover and the game reported `CanReturn=true`; the browser driver's control snapshot still described an earlier screen. The first attempt failed on the title and the second after editing a seed. Neither recorded the earlier rendering-buffer error.

`CampaignView.ReportControls` rejects non-finite geometry, which can occur while newly attached cover children are being measured. Previously that rejection ended the scheduled report. A child finishing layout does not necessarily change the root's geometry and schedule another report. This is a concrete reporting gap; the preserved failures do not directly identify which control had an invalid bound.

The view now owns one pending report. New view changes replace it. It measures at the existing 180 ms diagnostic cadence until all bounds are finite, then pauses. Twenty unsuccessful measurements produce an explicit error and pause; disposing the view also pauses the report. This does not add a gameplay delay or continuous idle polling. Release players with diagnostics disabled do not schedule these measurements.

`LayoutAttempts` is diagnostic metadata. The browser interruption driver stores retries separately from its exact control geometry/content comparisons. Input, campaign-state, contrast and touch-target assertions remain active. A passing run without any recorded retry proves the scenario passed, but does not independently exercise recovery from invalid geometry.

To maintain this behavior, edit `Unity/Assets/AshenSpire/Runtime/Presentation/CampaignView.cs`, especially `Report`, `TryReportControls`, `ReportControls` and `Dispose`. Keep the finite-bound rejection and the terminal error. Do not replace a missing control with guessed coordinates or relax the return-cover assertion. Rebuild all checkpoint players with `tools/build-unity.ps1`, run normal and throttled seed tests plus interruption/mobile/campaign tests, and inspect `layoutRetries`, errors and screenshots.

Unity's [scheduled-item API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.IVisualElementScheduledItem.html) provides the recurring callback and explicit pause used here. The separate [Web renderer maintenance guide](Web-Renderer-0.8.2.md) still applies to Unity 6000.6.0f1.

Physical-phone behavior, audible sound quality and performance remain separate validation work.
