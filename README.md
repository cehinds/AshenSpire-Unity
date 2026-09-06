# AshenSpire Unity

A mobile-first Unity adaptation of [AshenSpire](https://github.com/cehinds/AshenSpire), seeded from `dev` at `d5c982e777df06221e181c437652b705d2f6abbc`.

**Early playable checkpoint, not the complete port.** One Reaver, three encounters, card rewards, a boss, save/resume and reused sprite art are implemented. Full classes, equipment, the original status/tag system, branching maps and richer animation remain on the migration backlog.

[Play and compare builds](https://cehinds.github.io/AshenSpire-Unity/) · [Dev](https://cehinds.github.io/AshenSpire-Unity/dev/) · [Test](https://cehinds.github.io/AshenSpire-Unity/test/) · [Release](https://cehinds.github.io/AshenSpire-Unity/release/) · [Main](https://cehinds.github.io/AshenSpire-Unity/main/)

Open `Unity/` in Unity Hub. The pinned editor version is recorded in `Unity/ProjectSettings/ProjectVersion.txt`. Use the **AshenSpire** editor menu to import content, prepare the scene, and build Web or Windows players.

- [Owner editing guide](docs/Unity-Owner-Guide.md)
- [Full build brief](docs/Unity-Build-Brief.md)
- [Current Unity slice specification](docs/UNITY-SPEC.md)
- [Changelog](Published/changelog.json)
- [Original browser project README](docs/Upstream-README.md)

## Data and source

`GameContent/Unity/expedition.json` is the first Unity slice's authoring source. `Unity/Assets/AshenSpire/Runtime` separates Domain, Application and Presentation. Editor tools live outside runtime. Script headers explain setup, ownership, edit points and verification.

The original browser files remain as migration reference. Existing sprites are reused with their provenance in `CREDITS.md`. Imported PNGs preserve the decoded pixels of the original WebP files.

## Build and verify

```powershell
dotnet run --project UnityTests/Domain
.\tools\build-unity.ps1 -Target Web
python -m http.server 8787 --directory Published/Web
```

Web players require a local server or hosted URL. This is the standard Unity web folder export that Constantine selected; it is not a single physical HTML file.

Pages publishing is automatic after channel branch changes. GitHub Actions runs domain tests and verifies the exported build's source and file hashes before publishing the combined channel site. Local Unity compilation and automatic Pages publishing are separate; unattended Unity compilation still needs a runner or CI license. Empty channels remain explicitly unselected.

The upstream workflow files are preserved with `.reference` suffixes so they cannot run against the new repository. The new Pages workflow is scoped to this adaptation.
