# AshenSpire Unity

A mobile-first Unity adaptation of [AshenSpire](https://github.com/cehinds/AshenSpire), seeded from `dev` at `d5c982e777df06221e181c437652b705d2f6abbc`.

**The Ember Endures — a complete three-act campaign.** Choose Reaver, Rogue, Herald or Starseer; follow nine encounter stops, read enemy intents, shape a deck, buy equipment and defeat the Wyrm Lord. Eighteen cards compose damage, block, poison, weakness, strength, healing, energy and draw effects. Saves, sprite pose animation, sound feedback and motion settings are included. This adaptation does not claim full mechanical parity with the original browser game.

[Play and compare builds](https://cehinds.github.io/AshenSpire-Unity/) · [Dev](https://cehinds.github.io/AshenSpire-Unity/dev/) · [Test](https://cehinds.github.io/AshenSpire-Unity/test/) · [Release](https://cehinds.github.io/AshenSpire-Unity/release/) · [Main](https://cehinds.github.io/AshenSpire-Unity/main/)

Open `Unity/` in Unity Hub with Unity 6.6. Use the **AshenSpire** editor menu to edit/import campaign content, prepare the scene, and build Web, Windows or Android players.

- [Campaign editing and testing guide](docs/Campaign-Owner-Guide.md)
- [Full build brief](docs/Unity-Build-Brief.md)
- [Current Unity slice specification](docs/UNITY-SPEC.md)
- [Changelog](Published/changelog.json)
- [Original browser project README](docs/Upstream-README.md)

## Data and source

`GameContent/Unity/campaign.json` is authoritative. Edit records in Unity or round-trip any table through CSV with `tools/campaign-table.py`. The older expedition.json and its domain implementation remain as a reference for the first checkpoint. `Unity/Assets/AshenSpire/Runtime` separates Domain, Application and Presentation. Editor tools live outside runtime. Script headers explain setup, ownership, edit points and verification.

The original browser files remain as migration reference. Hero poses and backgrounds are reused. A new painterly enemy atlas and its extraction manifest live under `GameContent/Unity/Art`; original sprites remain intact. Provenance is in `CREDITS.md`.

## Build and verify

```powershell
dotnet run --project UnityTests/Domain
.\tools\build-unity.ps1 -Target Web
python -m http.server 8787 --directory Published/Web
```

Web players require a local server or hosted URL. This is the standard Unity web folder export that Constantine selected; it is not a single physical HTML file.

Pages publishing is automatic after channel branch changes. GitHub Actions runs domain tests and verifies the exported build's source and file hashes before publishing the combined channel site. Local Unity compilation and automatic Pages publishing are separate; unattended Unity compilation still needs a runner or CI license. Empty channels remain explicitly unselected.

The upstream workflow files are preserved with `.reference` suffixes so they cannot run against the new repository. The new Pages workflow is scoped to this adaptation.
