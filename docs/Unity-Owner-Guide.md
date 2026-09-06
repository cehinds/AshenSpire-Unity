# Editing your Unity game

This is an early playable slice. The original browser source is preserved beside it as a migration reference; it is not a completed Unity port.

## Open and play

Open `Unity/` in Unity Hub with the version in `Unity/ProjectSettings/ProjectVersion.txt`. Use **AshenSpire → Prepare Playable Scene**, open `Assets/AshenSpire/Scenes/Expedition.unity`, and press Play. The root prefab holds a `RunController` and `UIDocument`; the PanelSettings reference is required.

The project now targets **Unity 6.6 (6000.6.0f1)**. Install its Web Build Support and Windows Build Support modules. The upgrade keeps the expedition save schema and channel-specific save keys unchanged.

## Find the right file

| Goal | File or folder |
|---|---|
| Change cards, enemies, costs, health, rewards | `GameContent/Unity/expedition.json` |
| Add an interpreted behavior | `Unity/Assets/AshenSpire/Runtime/Domain/RunSession.cs` |
| Add/validate definition fields | `Unity/Assets/AshenSpire/Runtime/Domain/ContentDefinition.cs` |
| Change touch flow and screen composition | `Unity/Assets/AshenSpire/Runtime/Presentation/ExpeditionView.cs` |
| Change colors, spacing, card sizes | `Unity/Assets/AshenSpire/Resources/Expedition.uss` |
| Change save/lifecycle/input coordination | `Unity/Assets/AshenSpire/Runtime/Application/RunController.cs` |
| Change build settings/import commands | `Unity/Assets/AshenSpire/Editor/BuildTools.cs` |
| Change web loading/canvas behavior | `Unity/Assets/WebGLTemplates/Mobile/index.html` |

## Example: change Strike damage

1. Open `GameContent/Unity/expedition.json`, find `Cards` → `strike`, and change the damage effect's `Amount` from 6 to 7.
2. Update its Description to match. Automated generated descriptions are not implemented in this slice.
3. In Unity select **AshenSpire → Validate and Import Content**. Read the Console's counted result.
4. Start a new expedition, play Strike, and verify enemy health falls by 7.
5. Run `dotnet run --project UnityTests/Domain`, build, and test the exported player.

Do not edit `Resources/expedition.json`: it is generated from the authoring file. Unknown tags or operations should fail import with the affected record named. New cards can compose `damage`, `block`, and `draw`; new primitives need an implementation and tests.

## Build and preview

From the repository root in PowerShell:

```powershell
.\tools\build-unity.ps1 -Target Web
python -m http.server 8787 --directory Published/Web
```

Open `http://localhost:8787`. A Web build is a folder, not a double-click HTML file. The script validates the rules, invokes the pinned editor, and stamps hashes. It does not commit or push.

For Windows, use `-Target Windows`. Android/iOS export commands are not implemented yet. Those editor modules are installed on the current development PC; native builds and physical devices still need validation. Browser phone testing is distinct from a native mobile player.

## Publishing and channels

GitHub Actions automatically validates and publishes existing exported checkpoints when `dev`, `test`, `release`, or `main` changes. It assembles all channel directories together. Changing Unity source without rebuilding makes the package check fail, preserving the previously deployed site.

Unity compilation currently happens locally with the build tool. A dedicated runner or cloud CI license is still needed for unattended compilation. Channel promotion remains separate from deployment; empty channels are shown as awaiting a build.

Draft pull requests targeting `dev` run the domain, packaged-source and browser checks without deploying Pages. After owner review and merge, the existing channel publication workflow runs automatically.

For editor upgrades, `tools/unity-upgrade-playtest.cjs` accepts the previous Web build folder, the new Web build folder and an evidence directory. It creates a combat save through pointer input in the previous player, switches players at the same local web origin, and verifies that Continue restores every saved field and accepts another turn. It records the engine versions, three screenshots and a JSON report.

## Debug one change at a time

State your expected result, reproduce the symptom, inspect the nearest boundary, change one thing, and rerun. Record build version, channel, device/browser, steps, expected result, and actual result. The current debug facility is the Unity Console plus build logs and deterministic tests; a user-facing debug panel and bug-report exporter remain planned.

## Conventions

The Unity C# code uses PascalCase types/public members, camelCase parameters/locals, `_camelCase` private instance fields, descriptive namespaces matching folders, and explicit dependencies. Editor code stays outside runtime. Every first-party MonoBehaviour starts with setup and maintenance guidance. These are proposed .NET-style project conventions; the detailed Forge/Dimitar naming standard was not available in the local library.

Forge's relevant approach is used here: plain language, a concrete path before abstraction, bounded changes, visible failure evidence, and verification from packaged output. Personal training records are not copied into the game repository.
