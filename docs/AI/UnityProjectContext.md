# Unity project context

Inspected 2026-09-27. Baseline: `ec45df6` / 0.0.20.0; updated with `origin/dev`
`62c5970` (lean attributes and hand rules) before implementation. The separate
`cehinds/AshenedSpire` repository is not this port.

- **Confirmed:** Unity 6000.6.0f1, project `Unity/`, enabled startup scene
  `Assets/AshenSpire/Scenes/Expedition.unity`. Built-in renderer; UI Toolkit.
  `GraphicsSettings.asset` has no custom render pipeline.
- **Confirmed:** engine-independent Domain and Original assemblies own combat,
  catalogs, seeded maps and persistence models. Application/RunController adapts
  PlayerPrefs, audio and co-op. Presentation owns UI Toolkit panels and feedback.
  Extend the existing partial RunController and CampaignView files.
- **Confirmed:** Newtonsoft JSON 3.2.1; no connected Unity MCP. The installed
  editor is `D:/Unity/6000.6.0f1/Editor/Unity.exe`. Local batch builds are available.
- **Confirmed:** co-op uses `tools/NativeLan` companion, authoritative snapshots
  and authenticated seats. Solo mods do not modify co-op content.
- **Confirmed:** authored content is `GameContent/Unity/Original`; Resources copies
  and Published players are generated. Music files and credits are in `music/`
  and `CREDITS.md`. BuildTools imports content and stamps player hashes.
- **Confirmed:** `tools/build-unity.ps1 -Target All -EditorPath ...` validates
  domain/parity/card/map suites, then builds Windows, Android and Web and packages
  the companion. Set `PYTHON` to the installed Python executable if the inherited
  environment points at a missing installation.
- **Confirmed:** browser tests use real pointer/keyboard input through
  `native-ui-driver.cjs`; console diagnostics observe state and loaded assets.
  They do not establish physical device or owner visual acceptance.
- **Constraints:** preserve existing changes; generated players are never edited
  to hide source mismatches. Task branches and draft PRs target dev. Features stay
  in progress until compiled acceptance and explicit owner acceptance.
- **Unknown:** physical Android performance, iOS signing/build environment,
  owner visual acceptance, and the desired JavaScript save-import policy.

Sources: AGENTS.md, CONTINUE-HERE.md, Unity-Roadmap.md, Unity-Versioning.md,
ProjectVersion.txt, Packages/manifest.json, GraphicsSettings.asset,
EditorBuildSettings.asset, BuildTools.cs, RunController*.cs, CampaignView*.cs,
OriginalEnemyFigure.cs, MusicPlayer.cs, FeelDriver.cs, build-unity.ps1 and
native-ui-driver.cjs.
