# Interruption and return — 0.8.0

When the app is backgrounded or its browser tab is hidden, the game saves the active campaign, stops transient sound and cancels its animation. A return screen covers the existing view. When the app is visible again, choose **Return to the game**. The current card selection, inspection, scroll position and unfinished seed stay where you left them. Returning does not take a turn or replay a sound.

Mute remains your setting. Suspending sound temporarily does not overwrite it. Browser canvas blur and Android keyboard focus loss do not pause the game. Native pause does; Windows focus loss also does. A second interruption cannot dismiss the cover while another source is still active.

## Modification guide

| Change | File under Unity/Assets |
| --- | --- |
| Lifecycle callbacks, saving and return wiring | `AshenSpire/Runtime/Application/RunController.cs` |
| Which overlapping signals allow return | `AshenSpire/Runtime/Application/InterruptionState.cs` |
| Web registration and removal | `AshenSpire/Runtime/Application/BrowserVisibility.cs` and `Plugins/WebGL/BrowserVisibility.jslib` |
| Return wording, cover and preserved controls | `AshenSpire/Runtime/Presentation/CampaignView.cs` |
| Cover spacing and color | `AshenSpire/Resources/Expedition.uss` |
| Temporary sound suspension | `AshenSpire/Runtime/Application/GameAudio.cs` |

No Inspector assignments, scene changes or new packages are needed. The existing RunController owns subscriptions; its disable path removes listeners and disposes the view. The plain C# state class has no campaign or Unity dependency. Keep gameplay commands in CampaignSession and keep return as a presentation operation. Saves retain the existing channel-specific checksum/backup storage and schema.

## Test it yourself

1. Enter a seed on the wanderer screen, switch tabs/apps and return. The draft should remain.
2. Enter combat, select a card or open intent details, then switch away. The same screen should return behind the cover.
3. Switch away just after playing a card or ending a turn. There should be no delayed animation or sound on return, and the completed command should happen only once.
4. Mute, switch away and return, then reload and continue. Mute and the saved campaign should remain.
5. Repeat on a real phone with Home, screen lock, the on-screen keyboard and rotation. Record browser/device, steps and result. A browser test is not a physical-phone test.

```powershell
dotnet run --project UnityTests/Interruption
node tools/browser-visibility.test.cjs
node tools/interruption-playtest.cjs http://127.0.0.1:8787 Builds/InterruptionCheck
```

The browser check launches a disposable browser profile and switches real browser targets through CDP. It verifies `document.visibilityState`, uses pointer/keyboard events and reads development diagnostics. It does not inject gameplay commands or saved state. The separate bridge check simulates lifecycle events only to test subscription cleanup and page-cache ordering.

Commands already save immediately. An OS kill or crash can prevent a final lifecycle callback; this feature does not promise to intercept termination. Web, Windows and Android exports are development checkpoints. iOS and physical-phone interruption/audio behavior require device validation. Unity compilation runs locally; GitHub validates the prepared player and publishes the dev channel after its checks pass.
