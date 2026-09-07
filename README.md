# AshenSpire Unity

A mobile-first Unity rebuild of [AshenSpire](https://github.com/cehinds/AshenSpire), with native parity measured against original `dev` at `b17a7f4543e1710f49fae8b58880121690a314de`.

Choose Reaver, Rogue, Herald or Starseer and climb the original three acts. Native C# owns combat, weapon-sourced cards, equipment, services, rewards, custom modes, progression, saves and cooperative runs. Original portraits, armour, tints and four sprite styles preserve the game's identity. The earlier nine-encounter adaptation remains available from the title screen and build history.

Current source: **0.0.10.0 · build 10 · Foundation in progress**. A source implementation, a verified export and a published channel are separate states. The [parity checklist](docs/Unity-Parity.md) records evidence and remaining acceptance work; the selected channel's manifest identifies its actual build.

[Play and compare builds](https://cehinds.github.io/AshenSpire-Unity/) · [Dev](https://cehinds.github.io/AshenSpire-Unity/dev/) · [Test](https://cehinds.github.io/AshenSpire-Unity/test/) · [Release](https://cehinds.github.io/AshenSpire-Unity/release/) · [Main](https://cehinds.github.io/AshenSpire-Unity/main/)

Open `Unity/` in Unity Hub with Unity 6.6. Use the **AshenSpire** editor menu to edit/import campaign content, prepare the scene, and build Web, Windows or Android players.

- [Native game editing and testing guide](docs/Unity-Owner-Guide.md)
- [Foundation and game roadmap](docs/Unity-Roadmap.md)
- [Playable build history](docs/Build-History.md)
- [Earlier campaign editing guide](docs/Campaign-Owner-Guide.md)
- [Combat clarity slice (0.3.0)](docs/Campaign-0.3.0-Changes.md)
- [Class identity slice (0.4.0)](docs/Class-Identity-0.4.0.md)
- [Balance diagnostics](docs/Class-Identity-Balance.md)
- [Full build brief](docs/Unity-Build-Brief.md)
- [Current Unity slice specification](docs/UNITY-SPEC.md)
- [Changelog](Published/changelog.json)
- [Original browser project README](docs/Upstream-README.md)

## Data and source

The native game is authored in `GameContent/Unity/Original/`: original content, mechanics, attribute progression, event choices, custom-run controls and appearance tables. Round-trip original content through CSV with `tools/original-table.py`. Validation checks record references and executes a CSV-authored card, weapon, enemy and encounter in native combat. `campaign.json` and `tools/campaign-table.py` belong to the preserved earlier adaptation.

`Unity/Assets/AshenSpire/Runtime` separates Domain, Application and Presentation. Rules are pure components consuming explicit tags and JSON. Editor tools live outside runtime; script headers explain ownership, setup and modification points. Active saves freeze their rule tables so authoring changes apply to new runs predictably.

The original browser files remain as migration reference. Hero poses and backgrounds are reused. A new painterly enemy atlas and its extraction manifest live under `GameContent/Unity/Art`; original sprites remain intact. Provenance is in `CREDITS.md`.

## Build and verify

```powershell
dotnet run --project UnityTests/Domain
.\tools\build-unity.ps1 -Target All
python -m http.server 8787 --directory Builds/Web
```

Web players require a local server or hosted URL. This is the standard Unity web folder export that Constantine selected; it is not a single physical HTML file.

Use `-Target Web -PreviewOnly` for local browser iteration. `-Target All` builds matching Web, Windows, Android and portable co-op companion artifacts. The companion serves the same Web player and owns shared runs on your local machine; its launcher needs neither Unity nor an installed .NET runtime. See [companion instructions](tools/NativeLan/Packaging/README.txt).

Pages publishing is automatic after channel branch changes. GitHub Actions runs domain tests and verifies the exported build's source and file hashes before publishing the combined channel site. Local Unity compilation and automatic Pages publishing are separate; unattended Unity compilation still needs a runner or CI license. Empty channels remain explicitly unselected.

The upstream workflow files are preserved with `.reference` suffixes so they cannot run against the new repository. The new Pages workflow is scoped to this adaptation.
