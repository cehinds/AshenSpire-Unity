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

- [x] Latest downloadable Web preview: [build 50](https://github.com/cehinds/AshenSpire-Unity/releases/tag/preview-build50), version `0.0.33.3`.
  - [x] Unity export, source/eight payload hashes and archive verification.
  - [x] 42 targeted desktop/portrait browser assertions and screenshot inspection.
  - [x] Public ZIP downloaded back; SHA-256 matches the local package.
  - Evidence: [build 50](Unity-Build50-Preview.md). The broader art/reset coverage remains [build 49](Unity-Build49-Preview.md).
- [ ] Build 51 (`0.0.33.4`): [mounted property integration](Unity-Property-Integration.md).
  - [x] Solo and party carrier derivation, shared FIFO with owning-seat execution,
    heal recipient routing, saved gates, class/tag predicates and equipment remounting.
  - [x] 109 focused property checks and 41,156 full migration checks; 169-file runtime reference check.
  - [ ] Unity export and exported-player validation of this source.
  - Active source: `D:/repos/.codex/worktrees/unity-current-rules`.
    Warm export workspace: `D:/repos/.codex/worktrees/unity-upstream-reconciled`.
- [ ] Full migration: content activation, foundation scheduling/status integration,
  progression/rewards, classes/books/sideboard, world/services and schema-20 import.
- [ ] Final platform/device and owner acceptance. No phase is accepted by these checks.

The owner authorized the earlier intermediate merge and rejected visual parity.
The current increment is reconciled onto dev `3c7ebff`, including its
controller, settings, import and content-validation fixes. See
[the prior integration checkpoint](Unity-Integration-Checkpoint.md).

The active owner request is the full native migration, including **all recorded
upstream changes through original build 913** (explicit owner answer on October 4).
Published HTML test **898** (`0.7.1.898`, digest `1b60c22e01`) remains the frozen oracle baseline. Read
[the migration tracker](Unity-HTML-Parity-Migration.md) before continuing.
Build 36 below is the archived pre-migration rollback baseline; migration source
edits do not share its source digest.

Historical build 48 exported successfully from the prior checkpoint;
its source digest `612a0f6d9de3710a0cf1acc21ed783d6765db6ea4666a9c31da49df602666fc0`
and all eight payload hashes were verified. Its 14 starter-art and 22 settings
checks passed; actual paintings were visually inspected. Its corrected combat
playtest passed another 18 desktop/portrait assertions: inspector Play charges
once, removes the card and survives immediate browser reload exactly.
The monitor assessment through original build 913 has been carried forward
byte-for-byte from the existing local review; its historical unchecked findings
are reconciled by the implementation checklist in the migration tracker.
The reconciled increment passes 40,544 migration checks, 211 settings/mod checks,
195 gamepad checks and the 166-file runtime reference check (three documented
Unity-6 API gaps in the older NuGet reference remain accepted).
Build 49 also offers existing custom preferences a keep/defaults choice after
profile loading, once per defaults revision; acknowledgement and values persist
together with rollback on failure. New profiles keep an uninterrupted startup.
Builds 46 and 47 were deliberately cancelled before export to repair the stale
branch version line against refreshed dev `3c7ebff` (`0.0.33.1`, build 35).
Build 48 passed all six version checks against that exact remote commit without
declaring another feature complete.
This increment adds native settings-reset confirmation/failure recovery and
saved, opt-in combat rules for hand refresh and a shared Stamina turn budget.
The combat rules are **not activated** in the older shipped content bundle.
Focused evidence: 40,544 migration checks, 6,587 legacy co-op checks after the
deck-order change, 2,063 legacy
equipment-swap checks, 172 hand-rule checks and 101 settings/mod checks passed.
The user expanded scope rather than accepting the current milestone; broader
progression, world/services, schema-20 import and content activation remain open.
The three v11 starter paintings are imported with verified pixels into
`Resources/Art/upstream913`, preserving equipment-profile artwork precedence.
Receipts: `TestResults/HtmlParity/upstream913/starter-art-import.json`.
Build 44 exported successfully and verified all eight payload hashes with digest
`3a98ced68694ca1e326ba1bb1dbbb6a22466371df104b9557f99bd469ba776fb`.
Its desktop test found off-screen return focus after reset cancellation; build 45
adds a layout-aware scroll/focus fix. Build 45 passed 22 desktop/portrait reset,
focus, persistence and unchanged-climb checks; screenshots were inspected.
Its source/eight-payload digest was verified before subsequent source edits:
`37f93e78fa9d86893f1aa3afb4602f0565a82f3850277e4b97ce2aede4512d38`.
However, visual inspection of its three starter-card previews found generic
OwnerCardFace artwork despite the imported paintings. Build-48 source shares the
card/profile artwork resolver across both native layouts to correct this.
It also adds saved deck-order behavior to solo/co-op combat (including ordered
hand refresh and generated-card returns). Deck/settings UI activation is still open.

The preceding verified player is build 43 (`0.0.30.12`), an intermediate migration preview.
Read [guided creation and card proportions](Unity-Guided-Creation.md) for the
latest owner-requested fixes: one-screen creation steps, ready Reaver defaults,
fixed 2:3 card faces and visible map paths with compact tools. Export/receipt,
desktop/portrait setup and a real Reaver card play passed. Preview: port 8907.
The following build-42 notes describe the preceding combat reframe.
Read [the combat reframe](Unity-Combat-Reframe.md) for the latest work: generated
hilt repair/card back, seven SpriteRenderer prefabs, compact combat controls,
reference select/play, width-aware card fan and a scrollable menu. Build 42
export/receipt and desktop/390x844 player checks passed. Preview: port 8906.
The following build-40 notes are historical; broader migration remains incomplete.
The owner selected `http://127.0.0.1:4175/` and its Starseer screenshot as the
appearance reference; test 898 remains the gameplay reference. See
[the appearance tracker](Unity-Owner-Appearance.md). Eleven verified image
imports, Cormorant typography, painted combatants and the card fan are in source.
The combined published-JS comparisons and compact-card checks pass 40,399 checks, including native
stance-choice command refusal, selection and replay cases. The full current
content bundle is not yet active. Build 37 was exported and playtested in solo
combat with reload/continue and portrait checks. Build 38's separate export in
`Builds/OwnerAppearance/build38` compiled, passed its receipt/hash check and
ran a Starseer fight with inspection and payment. Its desktop and portrait
checks exposed layout issues corrected and player-checked in build 39. Its
portrait view revealed that the copied background import settings had resized
non-square cutouts to square textures. Build-40 source disables that resampling
and compression, and checks all eleven actual imported dimensions before export.
Its export in `Builds/OwnerAppearance/build40/Web` succeeded and passed its
source/eight-payload receipt, digest
`59f51ce9999d1472f043cabc2b39c490d0be7e8b154c3f3b934e42b5b3cc936f`.
Fresh desktop/portrait player checks confirmed correct proportions. Inspection,
play and reload/Continue preserved actions 2, hand 5, discard 1 and soldier HP
18/25 with no console errors. The new playable preview is
`http://127.0.0.1:8904/`; screenshots and controls are in
`docs/qa/unity-owner-appearance`. Complete gesture checks, remaining screen
styling, current gameplay integration and owner acceptance remain open.

Archived build-36 checkpoint: the owner requested Phase 2 polish and
Phase 3 platform work together. Builds 34–35 improve status meters and resolve
authored description numbers; build 36 explains co-op readiness and disables
Start until the public roster is connected and ready. Runtime compilation
checks 137 files; 192 focused checks include 18 readiness/notice cases. The
fresh Web player preserves the prior combat save, and eleven two-player checks
against the separately packaged matching preview companion cover readiness,
disconnection, saved-seat rejoin, shared-map start and a 390x844 guest caption.
See [build-36 evidence](qa/unity-build-36/README.md), [Phase 2](Unity-Phase-2.md)
and [Phase 3](Unity-Phase-3.md) for current nested checkboxes.

Build 35's matching Web/Windows/Android/companion delivery is preserved.
Build 36 Web and Windows exports match digest
`0baacd88861ab9e1a5a842b0cf6f1bd816e2c52fb39bb6e5fa29c78ac8cd4fcb`.
Android recovery finished successfully. The final matching Web, Windows,
Android and companion candidate is archived with receipts; six candidate
metadata checks passed. Its companion ZIP is byte-identical to the one used
for the eleven browser checks. Physical-device/controller, graphical-Windows, iOS,
owner acceptance and remote publication remain separate open gates. No current
remote CI or new published release is claimed.

Historical build-33 checkpoint:

Build 33 (`0.0.30.2`) implements solo/co-op upward drag
and flick play, live sanitized combat previews, hostile target memory, readable
filters/keyboard choices and aligned card offers. The first Web export exercised
solo and two-browser co-op input and found missing co-op Escape behavior. That
gap and hand-rebuild focus loss are fixed. The final frozen-source Web export
and all eight payload hashes match digest
`0c9916e6e4bbb00ff3f75803f6696876885b43fb0a311762b15c2adb197cbb90`.
Final normal-input solo/co-op checks passed: Escape, upward drops, contextual
play, peer damage, next-turn recovery, victory/reward inspection and retained
keyboard pile filters. Phase 2 implementation and available Web verification
are complete; owner acceptance and physical-device/controller testing remain open.
Read [build-33 QA](qa/unity-build-33/README.md) for the final receipt
and checkbox state. Focused domain checks: 158; co-op: 461 / 146 command batches
through three-act victory; transient panel state: 21. Owner/physical-device
acceptance remains separate. The Unity export process has finished.

Historical build-32 checkpoint:

Build 32 (`0.0.30.1`) card QoL source now includes shared faces/inspection,
contextual actions, hold/right-click reading, deck/pile filters and target-tap
confirmation. Hand-inspection Back cancels arming; solo target memory survives
accepted-command panel refreshes and co-op keeps a legal target on card changes.
Runtime compilation passed 133 source files; co-op domain validation passed
461 checks / 146 batches. The final frozen-source Web export passed at
2026-10-02T20:29:27Z and all eight payload hashes verified. Read the
[build-32 evidence and receipt](qa/unity-build-32/README.md) before marking checks.
Do not use the earlier intermediate build-32 payload: source changed during it.
Source digest is da4968c5caac3fd65406e95874670dc08dde6bc8defe43a7b6f231cd7bd93b4c.
CUA played the final export on a fresh isolated origin 8802, seed QOL2026,
Reaver/Iron Vanguard: target/self taps, explicit Play, right-click reading,
Escape cancellation, deck/pile filters and one normal fight/reward claim passed.
Phone-sized inspection wrapped and scrolled at 390 x 844. The port-8802 save is
at Act 1 Rewards with Shield Bash claimed; cinders remain unclaimed. Port 8801
has the earlier opening-fight checkpoint. The owner's port-8791 run was not played.
The historical next steps were: improve filter-field contrast, card/offer alignment and armed enemy
colouring; verify dropdown keyboard navigation, hold inspection, multiple-enemy
target memory and fresh co-op UI. Build 33 supersedes that source-work list.
The original-core agent's two-turn build-765 audit is
saved beside the build-32 checklist; original/editor repositories were unchanged.

2026-10-02 owner correction: the preview still lacks quality-of-life conveniences.
Audit the current core AshenSpire through actual play, then port its confirmed
card-play and card-presentation conveniences. Build 31's 6,280 checks remain
valid bounded evidence, not complete feature parity or Phase 2 sign-off.

Earlier 2026-10-02 verification report:
6,280 assertions in 48 source/payload-matched cases include all 22 events
and 62 choices, the history-gated Nameless chain, four target viewports,
Custom/Sealed/Draft/Endless exact reload, Ascensions 0–6 and all 11 modifiers.
[Evidence](qa/unity-build-31/README.md). Build 31 is the local Web preview;
build 30 remains the packaged multi-platform checkpoint. Owner acceptance and
physical controller/device testing stay open. No player saves or original/editor
repository files were changed by these isolated playtests.

Historical build-31 work: the owner requested completion of Phase 2 and use of
AshenedSpire-Editor where useful. Build 31 (`0.0.30.0`) adds controller bindings,
accessibility/display preferences, automatic rewards, enemy inspection,
room/death transitions, co-op receipt feedback and bundled Web/Android packs.
See [Phase 2](Unity-Phase-2.md) for separate implementation, compiled evidence
and acceptance checkboxes. The editor's real pose validator/sampler was used
read-only; it has no Unity asset adapter. Build 30 remains the packaged
multi-platform checkpoint. Existing Foundation acceptance and physical-device
work are still open. Older build notes below are historical context.

Historical 2026-10-01 continuation: build 31's focused exported Web suites now pass **268
checks across 15 cases**, including audio, merchant/shrine and two-player
host-restart recovery. [QA](qa/unity-build-31/README.md) verifies the exact source
and served payloads. The later victory replay, all three boss encounters,
Endless Act 4 reload/next-room entry and UI-focus hold interruption now bring the
completed count to **1,375 checks across 18 cases**. Current work expands all
22 events and 62 authored choices through normal controls and copied save slots;
incomplete event runs are excluded from the count. New test profiles/dependencies
use D: in accordance with the owner's storage decision.

Owner authorized merging PR #57 on 2026-09-29. It is merged into `dev` at
`e107f87e97240b267f712df42609c9187a83d8f9`. Continue Phase 1 on
`feature/ashenedspire-phase1`. Build 29 is now live at https://cehinds.github.io/AshenSpire-Unity/dev/ after successful deployment and public browser verification; see [hosting](Unity-Archive-Hosting.md). Build-30 profile-import source is under compiled
validation; build 29 remains the last packaged candidate until those exports
and checks finish. Merge approval does not constitute gameplay acceptance.

Build 30 update: source `e3cecca` fixes the compiled profile preview's false
stale-record refusal. The updated importer passes 469 domain checks; the first
compiled failure and interrupted exports remain recorded in [build-30 QA](qa/unity-build-30/README.md).
The Windows restart workflow is the task workspace's `work/finish-build30.ps1`.
Do not run a second Unity Editor against this project while it is active.
Codex Process Jobs is unavailable on Windows (controller reports unsupported
platform), so do not rely on a completion notification from that plugin.

Owner scope correction (2026-09-29): the original AshenSpire supplies assets and
current gameplay mechanics, not agent instructions or management requirements.
The initial repository seed copied its Markdown too broadly. Root AGENTS.md now
records Unity-specific guidance; old workflow prose is historical context, not
an additional owner approval requirement. Existing remote protections and CI
behavior still need to be handled as actual technical constraints.


## Current dev integration context

The Unity game is named **AshenedSpire** (owner, 2026-09-28). The original game
remains AshenSpire. See [core phases](Unity-Milestones.md) and the
[original-game review](Unity-Upstream-Review.md). Build 29 fixes fixed-action clearance and passes its scoped Web checks. Build 28's initial original-save importer preserves compatible map checkpoints, with 879 scoped browser checks recorded for that build. Foundation acceptance remains open; see [build-29 QA](qa/unity-build-29/README.md) and [build-28 QA](qa/unity-build-28/README.md).

| | |
|---|---|
| Version / build / stage | **0.0.30.2 · build 33 · Phase 2 gameplay and card QoL validation** (`GameContent/Unity/version.json`). `Published/build.json` still describes packaged build 30. |
| Feature in progress | **Phase 2 F01–F16**, per the current owner request; **F00 Foundation** acceptance remains open ([detail](Unity-Roadmap.md#foundation-f00-detailed-acceptance)). |
| Reference game | HTML: `index.html`, `src/`, `content/`, `assets/`, `styles/`, [SPEC.md](../SPEC.md) |
| Unity project | `Unity/` (Unity **6000.6.0f1**, `Unity/ProjectSettings/ProjectVersion.txt`) |
| Domain C# | `Unity/Assets/AshenSpire/Runtime/Domain` and `Domain/Original` (engine-independent) |
| Presentation | `Unity/Assets/AshenSpire/Runtime/Presentation` (UI Toolkit, `Resources/*.uss`) |
| Unity content source | `GameContent/Unity/Original/*.json` (imported into `Resources/Original`; never edit the copy) |
| Specs | [UNITY-SPEC.md](UNITY-SPEC.md), [Unity-Build-Brief.md](Unity-Build-Brief.md), [Unity-Parity.md](Unity-Parity.md), [Unity-Visual-Parity.md](Unity-Visual-Parity.md) |

### Build 29 layout patch

- [ ] **Phase 1 — Foundation / US-0.3 interaction acceptance**
  - [x] Restore clearance above fixed actions so the legacy flask and inspection content remain reachable.
  - [x] Nine compiled portrait/landscape checks: whole-button visibility, touch size, charge consumption and exact healing.
  - [x] Complete the nine-encounter reference campaign with rewards, equipment and exact reload.
  - [x] Pass high-density touch across 12 layout scenarios and six landscape inspections.
  - [x] A second touch run confirms six Settings scroll stops preserve displayed values; [receipt](qa/unity-build-29/mobile-settings-guard.json).
  - [x] Web, Windows, Android and companion packages match; 453 companion, 163 native-file and 20 package checks pass. Tested Web payloads are byte-identical to the package.
  - [x] Current CI receipts verify 36 import, 16 native profile/slot and 14 co-op checks against exactly matching packaged Web hashes; [receipt](qa/unity-build-29/ci.json).
  - [x] Build-29 native campaign replay passes 749 checks and 306 real-input commands, exact reloads and its recorded Act-3 defeat. The browser/checkpoint pipeline passes; fast-gate jobs pass except the known version-sequence gate.
  - [ ] Wider original/native interaction coverage and owner acceptance remain open.

The build-28 CI failures and corrected harness behavior are preserved in [regression notes](qa/unity-build-28/ci-regressions.md). The legal patch step from build 28 passes six version checks; the accumulated branch still cannot jump over the owner's preceding integrations from `dev` build 21. Draft [PR #57](https://github.com/cehinds/AshenSpire-Unity/pull/57) remains a development candidate.

### Build 29 downloads and hosting

- [ ] **US-0.8 — public hosting acceptance**
  - [x] All four build-29 public downloads verified against their SHA-256 receipts, pinned to `1b92c1d56a78c104dd9bdd996ac64892d4a2910e`.
  - [x] Revised archive library retains 31 players at 383.1 MiB; 3,458 navigation and 564 runtime/hash/CORS checks pass. Historical 30-player validation includes three sampled browser startups.
  - [ ] Owner merge into `dev`, Pages deployment and public build-29 player verification.

The attempted public deployment failed its 950 MiB size guard and preserved the previous site. The tested capacity fix is on the feature branch. Pages permits deployment from `dev` only; repository rules reserve that merge for the owner. See [downloads, hosting evidence and limitations](Unity-Archive-Hosting.md).

### Local build 28 original-save candidate

- [ ] **Phase 1 — Foundation / US-0.6 original-save acceptance**
  - [x] Schema-5 map-checkpoint import for all four baseline classes, preview and empty-slot commit.
  - [x] Real original save-manager fixtures: 394 domain checks; browser adapter: 12 checks.
  - [x] Compiled file/browser-slot import: 36 checks; native profile/slot persistence: 16 checks.
  - [x] Full compiled standard campaign replay: 749 checks, ending at its recorded terminal result.
  - [x] Four matching packages, source digest `f3de4f1487e5653fbc87932d05623326255e252d11275ebb5f654835c17ab05f`.
  - [x] Profile import and active-room import (reward, merchant, fight-start, event, Custom, Endless) in build-31 source.
  - [ ] Mid-fight and Sealed/Draft saves, newer original schemas and compiled import acceptance.
  - [ ] Full custom modes, multiplayer recovery, authoring/appearance matrices and owner acceptance.

Original-game regression: 136 checks; original shipped/version checks: 6 and 8.
Twelve current domain policy runs completed 160 fights with no errors, recording 12 defeats and zero victories. This is not balance or fun acceptance.

The current importer refuses unsupported saves explicitly and preserves their original bytes. See [import checklist](Unity-Original-Save-Import.md) and [QA](qa/unity-build-28/README.md).

Pending owner scope decision (2026-09-29): whether the newer original-game skills/progression system must also be ported before Phase 1, or follows in Phase 2 after baseline import compatibility. No answer has been received and neither option is accepted by default. Original-game save import remains required; baseline profile and active-room compatibility are still open regardless of this decision.

### Local build 27 foundation candidate

- [x] AshenedSpire branding, welcome, four-page guide and About implemented.
- [x] **186 compiled browser checks** passed; see [QA](qa/unity-build-27/README.md).
  - [x] Welcome/guide/About: 36; actual build-26 save upgrade: 8.
  - [x] Custom/Sealed/Draft/Endless setup, opening combat and exact resume: 44.
  - [x] Menus/160% text: 32; narrow/desktop card reading: 32.
  - [x] Draft, shrine and merchant journey: 20; two-player packaged co-op: 14.
- [x] All four packages match source digest `81a2af8ce111515dc38343e374c7fc5d47cde4ae2dbccc1181016043077dac76`.
  - [x] Windows startup/window caption and Android launcher metadata verified.
  - [x] 452 companion, 163 native-file and 20 package checks passed.
- [x] Original-game change review recorded; owner-requested daily comparison active.
- [ ] Complete full campaigns, encounter coverage, recovery/authoring matrices and owner acceptance.
- [x] Owner decision (2026-09-28): implement original JavaScript save import before Phase 1 completion.
- [ ] Complete [original-save import](Unity-Original-Save-Import.md): initial map-checkpoint converter and UI pass compiled build-28 verification; broader profile/active-room compatibility remains open.
- [ ] Physical-device play, performance budgets and hosted channel promotion.

Work is on `feature/ashenedspire-foundation`, stacked after local build 26.
Source commit `10f4811` was frozen for the exports. No roadmap feature or phase
is accepted merely because these scoped checks passed. The next major milestone
remains **0.1.0.0 Foundation acceptance**. Follow the unchecked US-0.x items in
the roadmap; opening-mode checks do not replace full mode playthroughs.
The guide is not an interactive tutorial. The newer original skill-draft/class
abilities still need a deliberate spec/content/schema review before porting.

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
   Original JavaScript save import is required by the owner's 2026-09-28 decision; complete the import checklist before Foundation acceptance.

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
