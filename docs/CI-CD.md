# CI/CD for the Unity game

This page explains what runs on GitHub Actions, when, and what a green, red or
skipped result actually means. The working rules (who merges what) are in
[AGENTS.md](../AGENTS.md); the verdict wrapper contract is in
[DEVELOPER.md](../DEVELOPER.md).

## Branch flow

```
feature branch ──► draft PR ──► dev ──► promotion PR ──► test ──► main / release
                     (agent)    [owner merges]  (automatic)  [owner merges]  [owner]
```

1. Work happens on a feature branch off `dev` and goes up as a **draft pull
   request into `dev`**.
2. The **owner** reviews and merges into `dev`. Agents never push to or merge
   into `dev`, `test`, `main` or `release`.
3. Every push to `dev` (and a nightly run) creates or updates **one** pull
   request from `dev` into `test`, titled
   `Promote dev → test: <version>`, whose body is a generated test plan.
   The workflow never merges it. The **owner** merges it after testing.
4. `test` → `main` / `release` is done by the **owner** only.

## The workflows

| File | Runs on | What it does |
|---|---|---|
| `unity-ci.yml` — *Unity fast gate* | PRs to `dev`/`test`/`main`, manual | Quick answer: version-bump gate, .NET console tests in parallel, optional GameCI build. |
| `unity-pages.yml` — *Unity checkpoint and Pages* | PRs to `dev`, pushes to `dev`/`test`/`release`/`main`/`codex/**` | The full pipeline: every console test, NativeLan checks, `unity-package.mjs --check`, and all Playwright playtests against the committed `Published/Web`. Unchanged in what it checks. |
| `unity-build-library.yml` | after the pipeline above completes, pushes to `dev` touching `tools/unity-*` | Assembles and publishes the playable Pages channels (dev/test/release/main). |
| `unity-dev.yml` — *Unity dev builds and promotion PR* | pushes to `dev`, nightly 03:17 UTC, manual | Optional GameCI player builds as artifacts; creates/updates the `dev → test` promotion PR. |

### Unity fast gate (`unity-ci.yml`)

- **changes** — diffs the PR against its base and sets three flags:
  `unity` (`Unity/`, `GameContent/Unity/`, `UnityTests/`, `tools/NativeLan/`),
  `tooling` (`tools/unity-*`, `.github/workflows/`), `docs` (`docs/`, `*.md`).
  A manual run sets all three.
- **version-gate** — always runs:
  `node tools/verdict.mjs -- node tools/unity-version.mjs check --base origin/<base>`.
  Fails if Unity source changed without a legal `A.B.C.D` version bump.
- **domain-tests** — only if `unity` or `tooling` changed. One job per console
  test project (Domain, Parity, NativeFeedback, CardText, CardCosts,
  SpriteStyles, MapShape, MapKnowledge, MapViewport, CoopRun, CoopRun
  `--policy`, Interruption, Viewport, RendererPatch, Authoring, Balance, plus the
  optional Mods, SaveSlots, RunSummary, Telegraphs and Music suites), run in
  parallel with `fail-fast: false` so one failure does not hide others. An
  optional suite is skipped with a notice until its project exists on the branch;
  once every feature branch carrying one has merged, drop `optional: true`.
- **unity-language** — builds every file under `Unity/Assets/AshenSpire/Runtime/Domain`
  with `UnityTests/LangCheck` (C# 9, .NET Standard 2.1, no implicit usings), the
  rules the Unity 6 editor compiles by. The console test projects use net8.0/C# 12,
  so a newer feature (file-scoped namespaces, collection expressions, `required`)
  would otherwise pass here and only break in the editor. Run locally:
  `dotnet build UnityTests/LangCheck`. The same job then runs
  `node tools/unity-runtime-check.mjs`, which compiles **all** Runtime code
  (Application and Presentation included) against Unity's reference assemblies
  (`UnityEngine.Modules` 2021.3 from NuGet plus `UnityTests/RuntimeCheck/Unity6Shims.cs`
  for newer UI Toolkit types). Any compiler error fails it except the listed
  Unity 6 gaps (`MeshGenerationContext.painter2D`). It proves the code compiles,
  not that it behaves in the editor.
- **unity-license** + **unity-build** — only if `unity` changed. See
  [GameCI](#gameci-unity-builds-in-ci).
- A new commit on the same PR cancels the older run of this workflow.

Docs-only PRs run only `changes` and `version-gate`, which take seconds.

### Promotion PR (`unity-dev.yml`)

`promotion-pr` checks out `dev`, then:

1. If `origin/test` does not exist it prints a warning and stops.
2. Runs `node tools/unity-test-plan.mjs --base origin/test --head origin/dev --out test-plan.md`.
3. Reads the version from `GameContent/Unity/version.json`.
4. With `gh` and the workflow's `GITHUB_TOKEN` (`contents: read`,
   `pull-requests: write`), edits the open `dev → test` PR or opens one.
   It never merges.

Two repository settings matter here:

- **Settings → Actions → General → "Allow GitHub Actions to create and
  approve pull requests"** must be on, or `gh pr create` is refused.
- GitHub does not start workflows for events caused by `GITHUB_TOKEN`, so
  opening or editing the promotion PR does **not** itself trigger PR checks.
  The `dev` commits it carries were already checked on push. Close and
  reopen the PR, or push to `dev`, to get PR-event checks on it.

**Publishing is not duplicated.** `unity-dev.yml` uploads build *artifacts*
only. The playable dev/test/release/main channels on Pages are still assembled
from the committed `Published/` builds by `unity-pages.yml` and
`unity-build-library.yml`.

## GameCI: Unity builds in CI

Today, Unity players are built locally by the owner with
`tools/build-unity.ps1` and committed to `Published/`. CI can also run the
Unity editor through [GameCI](https://game.ci), but only once a license is
configured.

### Required secrets

Add these under **Settings → Secrets and variables → Actions → Repository
secrets**:

| Secret | Value |
|---|---|
| `UNITY_LICENSE` | The contents of the Unity license file (`.ulf`) |
| `UNITY_EMAIL` | Email of the Unity account that owns the license |
| `UNITY_PASSWORD` | Password of that account |

How to get the license file: follow the GameCI activation guide at
<https://game.ci/docs/github/activation>. For a Personal license you activate
Unity Hub locally and copy the `.ulf` file it writes; Pro licenses use a serial
instead (see the same guide).

### What happens once they are set

- `unity-ci.yml` → **unity-build**: `game-ci/unity-test-runner@v4` runs
  EditMode and PlayMode tests (project path `Unity`), then
  `game-ci/unity-builder@v4` builds WebGL through
  `AshenSpire.Editor.BuildTools.BuildWeb` (the same entry point the local
  script uses). The WebGL build and test results are uploaded as artifacts.
- `unity-dev.yml` → **build-web** on every push to `dev`, and **build-native**
  (Windows via `BuildWindows`, Android via `BuildAndroid`) only on the nightly
  schedule and manual runs.

These CI builds are evidence and downloadable artifacts. They do not replace
the committed `Published/` build and are not published to Pages.

### When the secrets are absent

The **Unity license probe** job prints a warning titled *Unity build NOT run*
and the build job is **skipped** — shown grey, not green. No job claims a Unity
build succeeded when none ran. The gate remains the committed `Published/`
build plus `node tools/unity-package.mjs --check` in `unity-pages.yml`.
Secrets are also unavailable to pull requests from forks, so those behave
the same way.

## Green, red, skipped

| Result | Meaning |
|---|---|
| Green job | Its checks ran and passed. |
| Red job | A check found a problem, or a harness could not run (see the exit codes in DEVELOPER.md). |
| Skipped (grey) domain-tests | No Unity or tooling paths changed. |
| Skipped (grey) unity-build / build-web / build-native | No license secrets, or no Unity paths changed. **No Unity build happened.** |
| Green *Unity license probe* with a warning | Only means the probe ran. Read the warning: no build was made. |
| Promotion PR job green with a *No promotion PR* warning | `test` does not exist yet; nothing was created. |

## Caching

- **NuGet**: `~/.nuget/packages`, keyed on the hash of every
  `UnityTests/**/*.csproj`, with a prefix fallback.
- **Unity Library**: `Unity/Library`, keyed per target platform on the hash of
  `Unity/Packages/packages-lock.json` and
  `Unity/ProjectSettings/ProjectVersion.txt`, with a prefix fallback. A package
  or editor upgrade starts a fresh cache.

## Cost controls

- `unity-pages.yml` skips pushes and PRs that change only `docs/**` or
  `*.md` files. Any other file in the change runs the full pipeline.
  Caveat: if the owner ever marks a check from this workflow as **required** in
  branch protection, a docs-only PR would wait on a check that never starts.
  In that case require the fast gate's `version-gate` instead, or drop the
  `paths-ignore` for `pull_request`.
- `unity-pages.yml` cancels an older run when a PR gets a new commit. Push runs
  are never cancelled, because the Pages library is assembled after them.
- `unity-ci.yml` cancels superseded runs per PR; `unity-dev.yml` cancels a
  superseded run of the same trigger type.

## Running the same checks locally

```
# version gate and test plan
node tools/verdict.mjs -- node tools/unity-version.mjs check --base origin/dev
node tools/unity-test-plan.mjs --base origin/test --head origin/dev --out test-plan.md

# console tests (any one project)
dotnet run --project UnityTests/Domain
dotnet run --project UnityTests/CoopRun -- --policy
dotnet run --project UnityTests/Authoring -- TestResults/Authoring/checks.json
mkdir -p TestResults/Balance
dotnet run --project UnityTests/Balance -- GameContent/Unity/campaign.json TestResults/Balance/campaign.json

# packaged player and workflow shape
node tools/unity-package.mjs --check
node tools/workflow-lint.mjs

# Unity player (Windows, owner machine)
.\tools\build-unity.ps1 -Target Web
```

The full browser playtest list is the `validate-and-assemble` job in
`.github/workflows/unity-pages.yml`; each step there is a command you can run
from the repository root after serving `Published/Web` on port 8787.
