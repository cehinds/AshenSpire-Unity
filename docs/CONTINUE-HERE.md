# Continue here: AshenedSpire Unity handoff

Owner reporting preference (2026-09-28): use nested checkbox lists for progress
and status, grouped by core phase with user-story subitems. Check only the
specific completed item; keep a parent phase unchecked until all its acceptance
is complete. Distinguish implemented source, compiled validation and owner
acceptance. Carry this format into future updates and handoffs.

Read this first if you are an agent (or person) picking up the Unity port with
no other context. Then read [AGENTS.md](../AGENTS.md) (one page, the rules) and
the [roadmap feature tracker](Unity-Roadmap.md#feature-tracker).

## Where things stand

The Unity game is named **AshenedSpire** (owner, 2026-09-28). The original game
remains AshenSpire. See [core phases](Unity-Milestones.md) and the
[original-game review](Unity-Upstream-Review.md). Build 27 is being compiled and
validated for the rename and first-visit guidance; build 26 remains the last
fully packaged checkpoint until the new receipts pass.

| | |
|---|---|
| Version / build / stage | **0.0.26.0 · build 26 · Foundation in progress** (`GameContent/Unity/version.json`) |
| Feature in progress | **F00 Foundation**: finish its acceptance ([detail](Unity-Roadmap.md#foundation-f00-detailed-acceptance)) |
| Reference game | HTML: `index.html`, `src/`, `content/`, `assets/`, `styles/`, [SPEC.md](../SPEC.md) |
| Unity project | `Unity/` (Unity **6000.6.0f1**, `Unity/ProjectSettings/ProjectVersion.txt`) |
| Domain C# | `Unity/Assets/AshenSpire/Runtime/Domain` and `Domain/Original` (engine-independent) |
| Presentation | `Unity/Assets/AshenSpire/Runtime/Presentation` (UI Toolkit, `Resources/*.uss`) |
| Unity content source | `GameContent/Unity/Original/*.json` (imported into `Resources/Original`; never edit the copy) |
| Specs | [UNITY-SPEC.md](UNITY-SPEC.md), [Unity-Build-Brief.md](Unity-Build-Brief.md), [Unity-Parity.md](Unity-Parity.md), [Unity-Visual-Parity.md](Unity-Visual-Parity.md) |

### Local build 26 interface candidate

Build 26 refreshes the title, settings, collection, save slots and combat
presentation. Enlarged text uses a vertical title menu; control reports now
wait for text scaling so browser input targets the displayed positions.
The final compiled Web player passes **337 browser checks** across menus,
large text, settings, slots, combat, card reading, keyboard controls and co-op.
Web, Windows, Android and companion packages match the final source and pass
packaging checks; see [build-26 QA](qa/unity-build-26/README.md).

Work is on `feature/unity-interface-polish`, stacked after local build 25.
Owner visual acceptance and physical-device play remain open. No channel
promotion or owner merge is implied by the local builds.

### Local build 25 candidate

Build 25 is compiled and packaged for Web, Windows, Android and the companion.
Profile recovery and failed-save retries pass **148 storage checks**, **36
Unity-compiled controller/view checks**, **16 compiled profile/slot browser
checks**, and **8 host / 6 guest checks** with the packaged companion.
See [build-25 QA](qa/unity-build-25/README.md) for exact scope and receipts.

Work is on `feature/unity-save-recovery`, stacked after the local build-24
checkpoint. Build 22's PR #56 still needs owner merge before successive
versions can pass the one-step dev gate. No own-PR merge or channel promotion.
Keep profile/browser quota and physical-device acceptance open: injected C#
I/O failures do not certify asynchronous browser storage durability. Web uses
compact storage so three slots/backups fit; compact records require this build
or a newer compatible player. Existing plain records remain readable.

### Local build 24 candidate

Build 24 is compiled and packaged for Web, Windows, Android and the companion.
Solo piles, keyboard controls and rebinding pass **37 checks at each of 320×640
and 1440×900** using normal input in the compiled player. Retained-card choice
fixtures pass **45 Unity-compiled callback checks**; they use modified hand
rules, not a normal-run acceptance claim. See [build-24 QA](qa/unity-build-24/README.md).

The build-25 change above addresses profile corruption and failed save retries.
Owner acceptance and physical-device checks remain open. This candidate has
not been promoted; build 22's owner merge still gates the successive versions.

### Previous local build 23 candidate

Build 23 adds separate interface sounds and volume, plus interface/combat previews
in Settings. Web, Windows, Android and companion exports match the recorded build-23 source.
The compiled phone-sized audio test passes 32 checks, including real Web Audio
PCM/gain, keyboard submit, cancelled drags, zero levels, mute and reload.
See [build-23 QA](qa/unity-build-23/README.md).

Work is on `feature/unity-interface-audio`. Build 22's [draft PR #56](https://github.com/cehinds/AshenSpire-Unity/pull/56)
is fully green and remains unchanged for owner review. `dev` is still build 21:
the one-step version gate passes for 22 → 23, but needs the owner to merge 22
before 23 can pass against dev. Do not weaken the gate or merge your own PR.
Build 23 has not been promoted; the public build-22 link below remains current.
Owner feature/portrait acceptance and physical-device checks remain open.

### Last known-good build

**2026-09-27 update:** build 20's matching Web, Windows, Android and companion
exports were verified, committed in `4707586` and pushed on
`feature/unity-roadmap-completion`. The branch then merged `dev` build 21 and
implements the build-22 integration fixes. Read `Published/build.json` and the
build-22 QA receipt for the final packaged source digest; do not infer it from
this older build-14 history.

The archive capacity blocker is resolved by the existing raw-data hosting path.
Pages workflow [36337567712](https://github.com/cehinds/AshenSpire-Unity/actions/runs/36337567712)
successfully deployed 22 archived players (816.5 MiB) without deleting history.
Builds 13, 14 and 20 are available; versions 15–19 were source-only merges with
no compiled exports to publish. Build 20's live player and four download URLs
were checked. This archive publication does not promote Dev/Test/Release/Main
or provide owner acceptance.

Corrected build 22 is published: [play the candidate](https://cehinds.github.io/AshenSpire-Unity/builds/build-515749744afd909728e6/Web/).
Archive deployment [36347303524](https://github.com/cehinds/AshenSpire-Unity/actions/runs/36347303524)
succeeded with export commit `b41e32f`. The live hub now identifies Dev and Test
as build 14, with Release/Main awaiting a selected build. Neither channel has
been promoted to build 22. The source digest is
`54b429cfb6788b65be1ed34a185c8422841c44439a50f922ecfc60374a11e3ea`.
All 42 applicable runtime CI checks passed at that checkpoint. See
[build-22 QA](qa/unity-build-22/README.md) and [draft PR #56](https://github.com/cehinds/AshenSpire-Unity/pull/56).
The public screenshot-selection fix is prepared in the PR; Pages permits only
`dev` deployment, so the owner must merge it before the new review tooling is live.

The following build-14 paragraphs are retained as historical evidence only:

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
  pending owner review. Builds 13 and 14 are now archived online after the
  2026-09-27 deployment. Which exact build each channel serves right now
  cannot be confirmed from the repository alone; open the channel page and read
  its build record.

## Merged story work (2026-09-25)

The six story PRs are merged into `dev` in this order, each re-merged with
`dev` and re-bumped first: [#52](https://github.com/cehinds/AshenSpire-Unity/pull/52) F10 save slots (0.0.15.0) →
[#48](https://github.com/cehinds/AshenSpire-Unity/pull/48) F15/F16 settings + mods (0.0.16.0) → [#46](https://github.com/cehinds/AshenSpire-Unity/pull/46) F08 music (0.0.17.0) →
[#49](https://github.com/cehinds/AshenSpire-Unity/pull/49) F04 telegraphs (0.0.18.0) → [#50](https://github.com/cehinds/AshenSpire-Unity/pull/50) F11 run summary (0.0.19.0) →
[#51](https://github.com/cehinds/AshenSpire-Unity/pull/51) F07 feel (0.0.20.0).
Subsequently [#54](https://github.com/cehinds/AshenSpire-Unity/pull/54) merged lean
creation and solo hand rules as build 21. The current task branch is build 22.

**A green task branch does not repair `dev` until its PR is merged.** The build-20
export checkpoint is pushed; build 22 supplies the latest source-matched exports.
Follow the branch/draft-PR rule instead of pushing directly to `dev`.

Every console suite and both Unity compile checks pass on the merged `dev`.
None of this has been played in the editor yet.

Merge conflicts resolved while stacking (review in the editor):

- `RunController.Menu()`: #52's slot-aware title plus #46's `MusicTitle()`.
- `RunController.RefreshOriginal()`: #52 records the finished climb through
  `RecordOriginalResult` (verified slot-store save); it now returns the
  `profile.Finish` receipt, which #50's `AttachSummaryUnlocks` reads.
- `CampaignView` settings: #48's `ExtendSettings()` plus #51's
  `FeelDriver.Configure`. `ApplyPlayerSettings()` also reconfigures the feel
  driver.

Known gaps carried over:

| Feature | Gap |
|---|---|
| F08 music | build 22 imports all 10 credited tracks, applies music/master volume live and selects co-op scene music; audible owner acceptance remains |
| F15/F16 | build 22 consumes shake/intensity and optional hit-stop; mods remain opt-in and desktop only, outside co-op |
| F04 | build 22 sends per-seat damage previews including modifiers; legacy snapshots explicitly label base damage; dedicated intent art remains open |
| F11 | `SaveCoopProgress` already records completed co-op runs and discoveries; compiled end-to-end history/unlock acceptance remains |
| F07 | corrected build 22 passes campaign feedback and mobile CI; owner feel acceptance and remaining animation hooks are open |
| F10 | the legacy save key is no longer written (no rollback) |

## Next three user stories (F00)

1. **US-0.1** Review the 19-enemy compiled compendium gallery and obtain owner
   visual acceptance; then extend actual encounter/solo/co-op coverage.
   Build 25 renders all 19 portraits at phone and desktop sizes (78 checks).
   The catalog gallery is not every encounter. See [build-25 QA](qa/unity-build-25/README.md),
   `tools/native-enemy-catalog-playtest.cjs` and `native-enemy-art-playtest.cjs`.
2. **US-0.2** Current-source compiled Custom/Sealed/Draft/Endless interaction and
   save/resume checks. Start from `tools/native-features-playtest.cjs` and
   `tools/native-map-shape-playtest.cjs`; domain in `OriginalCustomRunRules.cs`.
3. **US-0.3** Card numbers, target availability, affordability, rejection
   recovery and result feedback across phone and desktop flows. Start from
   `tools/native-card-cost-playtest.cjs`, `native-long-card-playtest.cjs` and
   `UnityTests/CardCosts`.

The full F00 story list (US-0.1 to US-0.10) is in the roadmap. Use the exact
compiled source digest in each receipt. The new catalog gallery proves texture
loading for each enemy, not every solo/co-op encounter or owner approval.

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

### Local build on Windows

```powershell
.\tools\build-unity.ps1 -Target All            # tests, then Web + Windows + Android + companion
.\tools\build-unity.ps1 -Target Web -PreviewOnly   # quick browser iteration
python -m http.server 8787 --bind 127.0.0.1 --directory Builds/Web
```

Needs Unity `6000.6.0f1` with Web, Windows and Android modules (default path
`C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe`, or `-EditorPath`).

### Unity editor steps

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
node tools/unity-version.mjs bump story --note "US-0.2 custom-mode save/resume"
node tools/unity-version.mjs check --base origin/dev
```

The changelog goes to `docs/Unity-Changelog.md`. Any bump changes `GameContent/Unity`, so it needs an
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

## Parallel story branches and version numbers

Several stories can be in flight at once, each on its own branch and draft PR
(`feature/unity-f08-music`, …). Each one bumps from `dev`'s version, so two open
story PRs will both claim the same next number (e.g. `0.0.15.0`). That is
expected. After the owner merges one, update the next branch:

1. `git merge origin/dev` into the story branch; on the `version.json` and
   `docs/Unity-Changelog.md` conflict take `dev`'s side.
2. Re-run `node tools/unity-version.mjs bump story --note "<same note>"`.
3. `node tools/unity-version.mjs check --base origin/dev`, then rebuild with
   `tools/build-unity.ps1` (owner, Windows) and push.

Also check: **all code under `Runtime/Domain` must compile as C# 9 on .NET Standard
2.1** (Unity 6's rules): `dotnet build UnityTests/LangCheck`.
Application/Presentation code (UI wiring) can be compile-checked without the
editor: `node tools/unity-runtime-check.mjs`. It does not replace a play test.

## Known blockers

1. **Environment-dependent builds.** This local Windows session has a licensed
   Unity 6000.6.0f1 at `D:/Unity/6000.6.0f1/Editor/Unity.exe` and all three build
   modules. Cloud sessions may still lack an editor. No Android device was
   connected; no macOS/Xcode/signing environment was verified.
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
5. **Device and owner acceptance.** Windows player startup was observed, but
   desktop window capture timed out twice, preventing graphical play evidence.
   See [platform acceptance](Unity-Platform-Acceptance.md) for Android/iOS work.
   Original JavaScript save import remains an unanswered product decision.

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
6. Bump the version (`node tools/unity-version.mjs bump story --note "US-0.x ..."`)
   and update the roadmap tracker/checklist with evidence.
7. Open a **draft** PR to `dev` saying what changed, why, and how it was verified.
8. Ask the owner for the rebuild and, when a feature's criteria are met, for acceptance.
