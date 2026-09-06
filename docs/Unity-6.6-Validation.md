# Unity 6.6 checkpoint

Issue: https://github.com/cehinds/AshenSpire-Unity/issues/1

The playable slice now builds with Unity **6000.6.0f1**, revision **f7f8ed4d1e24**, from the previous `dev` checkpoint `3e8777b4ab76b1df091b05c8a60812e452474956`. This is an editor upgrade; gameplay rules, content and save schema remain the same.

## Changes

- Unity migrated ProjectSettings to its current serialization format and added its PhysicsCore2D and Project Auditor settings.
- The editor migrated built-in package dependencies, including Multiplayer Center 1.0.1 to 2.0.1, the PhysicsCore2D/TetGen/Timeline Foundation modules, and removal of the legacy VR module. No third-party package was added.
- Web and Windows players were rebuilt with 6.6. Web output is 31,443,571 bytes; Windows output is 96,939,042 bytes according to Unity's build summaries.
- A reusable upgrade test serves old and new players at the same local origin and exercises actual pointer commands. It records both engine versions and exact saved/resumed states.
- Draft PRs targeting dev now run package and browser validation. PR events cannot execute the Pages deployment job.

## Completed validation

| Check | Result |
|---|---|
| C# domain harness | 47 checks passed, including 30 complete seeded runs |
| Unity Web build | Succeeded; no C# compiler errors or warnings found in the build log |
| Unity Windows build | Succeeded; no C# compiler errors or warnings found in the build log |
| Windows startup | Headless player reached ASHENSPIRE_UI_READY |
| Exported Web interaction | Card selection/play and end-turn changed game state |
| Web save/reload | Exact state restored; before/after screenshots matched |
| 6.4 to 6.6 save compatibility | Exact combat state restored, both engine versions observed, another turn accepted |
| Browser errors | None in either browser pass |
| Visual evidence | Ten current-player screenshots plus three upgrade screenshots |
| Original browser artifact integrity | verify-shipped: 6 checks passed; buildversion --check passed |
| Original browser test suite | 134 passed, 2 failed in inherited CI inventory tests 67 and 68 |

The two inherited failures read `.github/workflows/ci.yml`, which was already absent from baseline commit `3e8777b` after the original workflows were archived as `.reference`. The failing harness and tests are unchanged by this upgrade. The original suite therefore remains red; this is not a claim that every repository check passes.

## Evidence and limits

`Published/build.json` records the compiled source digest, source commit, editor version, timestamp and player/archive hashes. `Published/Screenshots/playtest.json` records runtime commands and save state. `Published/UpgradeEvidence/upgrade-playtest.json` records cross-version compatibility.

Browser checks used desktop Edge with phone, desktop and landscape viewports. They do not establish physical touch input, Android/iOS behavior, or device performance. Portrait is the primary layout; landscape requires scrolling. Windows was built and initialized headlessly, not visually playtested. Art and animation remain the initial slice. Automatic Unity compilation still needs a runner/licensing decision; current CI validates already exported players.

The upgrade is delivered for review on its task branch. The previously published dev checkpoint remains available until owner merge. Test, release and main are not promoted by this work.
