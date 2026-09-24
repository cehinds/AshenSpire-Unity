# Continue here: Unity port handoff

Read this first if you are an agent (or person) picking up the Unity port with
no other context. Then read [AGENTS.md](../AGENTS.md) (one page, the rules) and
the [roadmap feature tracker](Unity-Roadmap.md#feature-tracker).

## Where things stand

| | |
|---|---|
| Version / build / stage | **0.0.14.0 · build 14 · Foundation in progress** (`GameContent/Unity/version.json`) |
| Feature in progress | **F00 Foundation**: finish its acceptance ([detail](Unity-Roadmap.md#foundation-f00-detailed-acceptance)) |
| Reference game | HTML: `index.html`, `src/`, `content/`, `assets/`, `styles/`, [SPEC.md](../SPEC.md) |
| Unity project | `Unity/` (Unity **6000.6.0f1**, `Unity/ProjectSettings/ProjectVersion.txt`) |
| Domain C# | `Unity/Assets/AshenSpire/Runtime/Domain` and `Domain/Original` (engine-independent) |
| Presentation | `Unity/Assets/AshenSpire/Runtime/Presentation` (UI Toolkit, `Resources/*.uss`) |
| Unity content source | `GameContent/Unity/Original/*.json` (imported into `Resources/Original`; never edit the copy) |
| Specs | [UNITY-SPEC.md](UNITY-SPEC.md), [Unity-Build-Brief.md](Unity-Build-Brief.md), [Unity-Parity.md](Unity-Parity.md), [Unity-Visual-Parity.md](Unity-Visual-Parity.md) |

### Last known-good build

- **Packaged locally:** `Published/build.json` records **0.0.14.0, build 14**,
  Unity 6000.6.0f1, runtime source commit `ddc9656ef01f9adf47e80f5787f6ebeec4a174cd`,
  source digest `2ac07c417b88e2adb899c532940736059d9c2769ee5870879fba91a608c5d2d5`,
  built 2026-09-08T00:08:20Z. Packaged in commit `1697365` ("Package build 14
  visual parity checkpoint and verified player evidence"). Screenshots:
  `Published/OriginalVisuals/`; evidence: `docs/qa/unity-visual-parity-0.0.14.0/`.
- **Live Pages:** hub <https://cehinds.github.io/AshenSpire-Unity/> with `/dev/`,
  `/test/`, `/release/`, `/main/` (README.md). Per
  [Unity-Visual-Parity.md](Unity-Visual-Parity.md) and the roadmap, **Dev and
  Test still host build 12** (selected at `4b4a28dfe9aecc1a3292b498cd6a9310a6aef2fc`)
  pending owner review; builds 13 and 14 are not published because Pages
  archive capacity is exhausted. Which exact build each channel serves right now
  cannot be confirmed from the repository alone; open the channel page and read
  its build record.

## Next three user stories (F00)

1. **US-0.1** Extend compiled painted-enemy coverage from Blight Hound/Grave
   Wisp to all 19 enemies (every encounter, solo and co-op), then ask the owner
   for visual acceptance. Start from `tools/native-enemy-art-playtest.cjs` and
   `GameContent/Unity/Original/enemy-art.json`.
2. **US-0.2** Current-source compiled Custom/Sealed/Draft/Endless interaction and
   save/resume checks. Start from `tools/native-features-playtest.cjs` and
   `tools/native-map-shape-playtest.cjs`; domain in `OriginalCustomRunRules.cs`.
3. **US-0.3** Card numbers, target availability, affordability, rejection
   recovery and result feedback across phone and desktop flows. Start from
   `tools/native-card-cost-playtest.cjs`, `native-long-card-playtest.cjs` and
   `UnityTests/CardCosts`.

The full F00 story list (US-0.1 to US-0.10) is in the roadmap. Stories that
need a compiled player (all three above) need the owner to rebuild first; see blockers.

## Commands

### Tests you can run anywhere (.NET 8, Node)

These are the dotnet projects in `.github/workflows/unity-pages.yml`:

```sh
dotnet run --project UnityTests/Domain
dotnet run --project UnityTests/Parity
dotnet run --project UnityTests/NativeFeedback
dotnet run --project UnityTests/CardText
dotnet run --project UnityTests/CardCosts
dotnet run --project UnityTests/SpriteStyles
dotnet run --project UnityTests/MapShape
dotnet run --project UnityTests/MapKnowledge
dotnet run --project UnityTests/MapViewport
dotnet run --project UnityTests/CoopRun
dotnet run --project UnityTests/CoopRun -- --policy
dotnet run --project UnityTests/Playthrough -- 3 TestResults/NativePolicy
dotnet build tools/NativeLan/Companion/AshenSpire.Companion.csproj -c Release
dotnet run --project tools/NativeLan/Tests/Checks/TransportChecks.csproj -c Release
dotnet run --project tools/NativeLan/Tests/NativeChecks/NativeChecks.csproj -c Release
dotnet run --project tools/NativeLan/Tests/PersistenceChecks/PersistenceChecks.csproj -c Release
dotnet run --project tools/NativeLan/Tests/StartingChoicesChecks/StartingChoicesChecks.csproj -c Release
dotnet run --project tools/NativeLan/Tests/CommandGateChecks/CommandGateChecks.csproj -c Release
dotnet run --project tools/NativeLan/Tests/PanelStateChecks/PanelStateChecks.csproj -c Release
dotnet run --project UnityTests/Authoring -- TestResults/Authoring/checks.json
dotnet run --project UnityTests/Viewport
dotnet run --project UnityTests/RendererPatch
dotnet run --project UnityTests/Interruption
dotnet run --project UnityTests/Balance -- GameContent/Unity/campaign.json TestResults/Balance/campaign.json
```

Node checks from the same workflow:

```sh
node tools/browser-visibility.test.cjs
node tools/control-report.test.cjs
node --test tools/native-ui-driver.test.cjs
node --test tools/unity-source-digest.test.mjs
node tools/unity-package.mjs --check          # fails until the owner rebuilds after Unity source changes
node tools/unity-build-history.test.mjs
node tools/unity-channel-storage.test.mjs
node tools/unity-archive-hosting.test.mjs
node tools/unity-git-blobs.test.mjs
```

Browser playtests run against the packaged player
(`python3 -m http.server 8787 --bind 127.0.0.1 --directory Published/Web`,
after `npm install --no-save playwright && npx playwright install chromium`):
`node tools/native-playtest.cjs http://127.0.0.1:8787 TestResults/NativeBrowser`,
and likewise `native-card-cost-`, `native-features-`, `native-appearance-`,
`native-enemy-art-`, `native-map-shape-`, `native-map-playtest.cjs`, plus
`node tools/native-coop-ci.cjs TestResults/NativeCoopBrowser`. The packaged
player reflects the last owner build, not your uncommitted source.

The HTML reference game has its own tests: `node tests/run-node.mjs` (see
[DEVELOPER.md](../DEVELOPER.md)).

### Owner only: build on Windows

```powershell
.\tools\build-unity.ps1 -Target All            # tests, then Web + Windows + Android + companion
.\tools\build-unity.ps1 -Target Web -PreviewOnly   # quick browser iteration
python -m http.server 8787 --bind 127.0.0.1 --directory Builds/Web
```

Needs Unity `6000.6.0f1` with Web, Windows and Android modules (default path
`C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe`, or `-EditorPath`).

### Owner only: Unity editor steps

1. Unity Hub → open `Unity/` with **6000.6.0f1**.
2. Menu **AshenSpire → 1. Validate and Import Content**.
3. Menu **AshenSpire → 2. Prepare Playable Scene**.
4. Open `Assets/AshenSpire/Scenes/Expedition.unity` and press **Play**.

More in [Unity-Owner-Guide.md](Unity-Owner-Guide.md).

### Versioning

Rules in [Unity-Versioning.md](Unity-Versioning.md): fix → D+1; user story →
C+1, D=0; feature complete → B+1, C=0, D=0; BuildNumber always +1; A is owner-only.

```sh
node tools/unity-version.mjs show
node tools/unity-version.mjs bump story --feature F00 --note "US-0.2 custom-mode save/resume"
node tools/unity-version.mjs check --base origin/dev
```

The changelog goes to `docs/Unity-Changelog.md`. If `tools/unity-version.mjs`
is not present on your branch yet (it is being added separately), edit
`GameContent/Unity/version.json` by hand following the same rules and add the
changelog entry yourself. Any bump changes `GameContent/Unity`, so it needs an
owner rebuild before the package check passes.

## Branch and PR rules (from AGENTS.md)

- Only the owner (Constantine, `cehinds`) merges into `main`.
- One task → one branch off `dev` → one **draft** PR targeting `dev`. Never push
  to `dev`, `main`, `test` or `release`; never merge your own PR.
- Never assume approval. Publishing, deploying, tagging, deleting or promoting
  channels waits for the owner's explicit yes.
- Generated files are rebuilt, never hand-edited (`AshenSpire.html`, `build/`,
  `dist/`, `buildordinal.json`, Unity `Resources/Original` copies, `Published/` exports).
- SPEC.md wins for mechanics: change it first in its own PR.
- Tasks live in GitHub Issues (Unity work: issue #33).
- If two agents would touch the same files, stop and ask.

## Known blockers

1. **No Unity editor in cloud agent sessions.** Agents can edit C#, JSON and
   docs and run the .NET/Node tests, but cannot compile the Unity player, run
   Play mode or take new compiled screenshots.
2. **Unity CI builds need `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD`
   secrets, which are not configured.** `unity-pages.yml` therefore validates
   and publishes the committed `Published/` exports; it does not compile Unity.
3. **The package check needs an owner rebuild after Unity source changes.**
   Any change under `Unity/Assets`, `Unity/Packages`, `Unity/ProjectSettings` or
   `GameContent/Unity` (including `version.json`) changes the source digest, so
   `node tools/unity-package.mjs --check` fails until the owner runs
   `.\tools\build-unity.ps1` and commits the new `Published/` exports.
4. **Owner acceptance is required before `0.1.0.0`.** F00 cannot be marked done
   (and B cannot become 1) on test counts alone.
5. Pages archive capacity: builds 13–14 do not fit under the current budget;
   expand hosting without deleting archived players (US-0.8).

## Resume checklist

1. `git fetch origin && git checkout -b <task-branch> origin/dev`; read AGENTS.md.
2. Read `GameContent/Unity/version.json` and the roadmap feature tracker; confirm
   F00 is still the feature in progress and pick the next open US-0.x.
3. Run the .NET tests above (at least Domain, Parity, CardText, CardCosts,
   MapKnowledge, MapViewport, CoopRun) to confirm a green baseline.
4. Make the change; keep domain rules in `Runtime/Domain/Original`, presentation
   in `Runtime/Presentation`; update the component's header comment.
5. Re-run the relevant tests; if you touched Unity sources, note in the PR that
   an owner rebuild (`build-unity.ps1`) is needed before `unity-package --check` passes.
6. Bump the version (`node tools/unity-version.mjs bump story --feature F00 --note "US-0.x ..."`)
   and update the roadmap tracker/checklist with evidence.
7. Open a **draft** PR to `dev` saying what changed, why, and how it was verified.
8. Ask the owner for the rebuild and, when a feature's criteria are met, for acceptance.
