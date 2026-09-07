# Editing and testing your Unity game

This guide describes the native original-game rebuild at **0.0.11.0 · build 11**.
Foundation acceptance is still in progress. The earlier
[campaign editor guide](Content-Authoring-0.6.0.md) and
[campaign validation report](Content-Authoring-Validation.md) describe preserved
adaptation checkpoints; their `campaign.json` and `expedition.json` examples do
not edit the native original-game content.

Build 11 integrates painted artwork for all 19 enemies. Matching Web, Windows,
Android and companion exports and package checks passed, together with bounded
solo art and co-op checks. The full campaign, storage and other older results
later in this guide remain explicitly build 10 evidence.

## Open and play

Open `Unity/` in Unity Hub using the version pinned in
`Unity/ProjectSettings/ProjectVersion.txt` (Unity 6.6, `6000.6.0f1`). Install the
Web, Windows and Android build modules for the targets you use. Select
**AshenSpire → 1. Validate and Import Content**, then
**AshenSpire → 2. Prepare Playable Scene**. Open
`Assets/AshenSpire/Scenes/Expedition.unity` and press Play.

The scene's root uses `RunController` and `UIDocument`; preserve its PanelSettings
reference. Begin a native climb through character creation. The separate original
foundation preview is a catalog/map inspection tool, not an executable campaign.

## Find the right file

Paths below are relative to the repository root.

| What to change | Authoritative file or component |
|---|---|
| Cards, enemies, encounters, classes, relics, events, equipment and tag joins | `GameContent/Unity/Original/content.json` |
| Per-point bonuses, five-point thresholds and Catch Breath | `GameContent/Unity/Original/progression.json` |
| Weight, resource and original supplementary mechanics | `GameContent/Unity/Original/mechanics.json` |
| Authored event choice/history rules | `GameContent/Unity/Original/event-choices.json` |
| Custom menu labels/options | `GameContent/Unity/Original/custom-run-options.json`; magnitudes live in `content.json` under `balance.customMods` and `balance.endless` |
| Run-shape control labels, limits and probe count | `GameContent/Unity/Original/custom-run-options.json` under `mapShape`; generation uses `OriginalMapShape` and `ActMapGenerator` |
| Four renderer paths and imported pose registration | `GameContent/Unity/Original/sprite-styles.json`, `Runtime/Presentation/OriginalSpriteCatalog.cs` and `OriginalPlayerFigure.cs` |
| Tint/sigil labels, palette and badge geometry | `GameContent/Unity/Original/appearance-options.json` |
| Painted enemy IDs, resource paths and visible bounds | `GameContent/Unity/Original/enemy-art.json` and `Runtime/Presentation/OriginalEnemyFigure.cs` |
| Seven new painted enemy masters and generation prompts | `GameContent/Unity/Art/EnemyExpansion/`; import with `tools/import-painted-enemies.py` |
| Version, build number and foundation stage | `GameContent/Unity/version.json`; follow `docs/Unity-Versioning.md` |
| Original sprite assets | `Unity/Assets/AshenSpire/Resources/Art` and imported source receipts; preserve sprite identities and pose alignment |
| Combat rules and interpretation | `Runtime/Domain/Original/CombatSession*.cs`, `StatusSystem`, `FormulaEvaluator` |
| Room transactions, events and services | `Runtime/Domain/Original/OriginalRunSession`, `OriginalRunContent`, `OriginalRunServices` |
| Equipment/cards/requirements | `Runtime/Domain/Original/WeaponLoadout`, `WeaponCardProjection`, `ItemUpgradeService`, `CardMountService` |
| Creation, derived resources and discovery gates | `Runtime/Domain/Original/CreationModel`, `OriginalStartingOptions`, `OriginalCharacterBuilder`, `OriginalPlayerProjection` |
| Card descriptions | `Runtime/Domain/Original/OriginalCardText.cs`; prose binds authored effects, not a second set of numbers |
| Screen layout and touch controls | `Runtime/Presentation/OriginalFoundationPanel.cs`, `OriginalRunPanel.cs`, `OriginalCoopPanel.cs` and `CampaignView.cs` |
| Animation/result cues and interruption | `Runtime/Presentation/NativeFeedbackProjection.cs`, `CombatFeedback.cs`, and `Runtime/Application/RunController.cs` |
| Host rules versus connections | `Runtime/Domain/Original/OriginalCoopRun*.cs` and `tools/NativeLan/Transport` |
| Import, scenes and player export | `Unity/Assets/AshenSpire/Editor/BuildTools.cs` |

`Runtime/...` entries are under `Unity/Assets/AshenSpire`. Read each component's
header before editing; it identifies dependencies and the intended modification
point. Keep C# PascalCase public APIs/types, `_camelCase` private fields and
namespaces consistent with their folders. Preserve original JSON property names
and stable IDs, which are saved-data contracts.

## Shape a Custom Climb

In character creation, open **Custom climb**, then **Run shape**. Floors and
columns are caps: they can shorten/narrow an act but do not enlarge its authored
map. Relative weights affect rolled room types. A zero weight does not remove
forced elite/merchant minima or Monster fallback nodes; the readout explains
these exceptions and reports small-map shortfalls. All-zero weights are refused,
and invalid settings disable Begin with an explanation.

Opening the group samples 24 isolated probe seeds and reports mean map-node
counts for each act. This is density, not an estimated play duration, and it does
not consume the live run RNG. Reset removes the run-shape override. Selected
shape and validation limits freeze with the saved run, including Draft
continuation. There is no separate practice mode in the pinned original. These
controls are implemented and passed 60 build 10 phone/desktop control, combat and
exact-reload checks. The current route-button view still lacks the original full
branching-map presentation, including solo fog and Sealstone Key reveal; working
generation and shape controls do not close those parity gaps.

The creator also offers Animated, Rendered, Classic and Sigil as distinct sprite
styles. Animated uses the original registered outfit/tint poses; Rendered keeps
its painting, Classic uses the original silhouette and Sigil shows the selected
symbol. The saved choice reaches solo and co-op views. Co-op pose feedback and
owner visual acceptance remain separate work.

Paid combat set changes and Catch Breath are intentionally solo-only. The pinned
original co-op offers neither command; adding them to cooperative play would be
a future design extension, not a missing original behavior.

## Edit painted enemy art

Solo and co-op use `OriginalEnemyFigure` and the same 19 entries in
`GameContent/Unity/Original/enemy-art.json`. The earlier 12 painted resources are
retained unchanged. Seven added sprites cover Husk Brute, Stitched Hound, Court
Marionette, Ash Revenant, Ember-Starved Pilgrim, Charred Colossus and Blighted
Valkyrie. Their transparent, high-resolution masters and exact built-in generation
prompts/provenance are in `GameContent/Unity/Art/EnemyExpansion/`.

Preserve each enemy's stable ID and existing master before replacing artwork.
Run `python tools/import-painted-enemies.py` with Pillow available to copy the
masters into `Resources/Art/Enemies/Painted` and refresh normalized visible-alpha
bounds. Then run **Validate and Import Content**. Import refuses a missing enemy
mapping, resource PNG or invalid registration. The source PNGs remain unchanged;
Unity's import policy caps these seven textures at 512 pixels, preserves alpha
and disables mipmaps and CPU readback.

The shared renderer fits the visible silhouette within 90% of frame width and
84% of frame height, placing the registered bottom on the 94% baseline. This
handles uneven generated margins without editing the masters. After replacing
an image, check full wings/weapons/feet, target changes and feedback in both solo
and co-op at phone and desktop sizes. Static art coverage is not animation or
visual acceptance; co-op pose feedback remains open. Build 11 passed 217 source/art
checks across all 19 mappings and 31 compiled solo checks on hounds/wisp, plus
eight host/six guest co-op checks. Other enemy silhouettes still need compiled
visual coverage; mapping every resource is not the same as seeing every enemy.

## Edit a table through CSV

From the repository root:

```powershell
python tools/original-table.py export cards Builds/Cards.csv
# Edit the file, preserving the adjacent .receipt.json.
python tools/original-table.py import cards Builds/Cards.csv
```

Every nonempty cell contains JSON: strings need quotes (`"Strike"`), numbers do
not, and nested effects remain JSON arrays/objects. Blank means absent; `null`
means explicit JSON null. Any record array can be exported, including
`equipment.armaments`, `enemies` and `encounters`.

Import validates the complete candidate through the native catalog, checks the
source hash, retains exact previous bytes under `Backups/`, and replaces the source
atomically. A stale receipt or invalid reference refuses the edit. Export again
after a successful import before making another edit. This is optimistic conflict
detection, not simultaneous spreadsheet collaboration.

For example, change a card's `effects[].amount` and leave its matching `{damage}`
token in `textTemplate`. Two damage operations bind `{damage}` and `{damage.2}`;
their optional hit counts bind `{hits}` and `{hits.2}`. Status tokens use the status
ID. Bracing Stance's second block value is `{block.2}`. Never replace these tokens
with hand-maintained numeric prose. An unresolved token remains visibly braced so
the defect can be fixed.

Weapon-sourced cards can be projected from profiles, equipment tiers and attributes.
Changing a generic card amount may therefore not change that weapon's final card.
Inspect its resolved card and compare the actual combat result. The native
description includes safe single-hit bonuses; multi-hit totals remain a separate
note. Enemy status/resistance may still change actual damage.

New content must be reachable: reference a card in the correct class/reward pool,
an armament in a kit or reward/shop source, and an enemy in an encounter/map pool.
Use existing supported opcodes/tags where possible. New behavior needs a domain
implementation, validation and meaningful tests before exposing it in a table.

After editing, use **Validate and Import Content**. Do not edit
`Resources/Original/*.json`: those copies are generated. Existing runs freeze
their own content/rules; start a new run to test newly authored content. Keep the
original import/oracle receipts as provenance rather than rewriting them to match
owner tuning. The validator does not yet implement every original schema rule.

## Test the change

Run from the repository root:

```powershell
dotnet run --project UnityTests/Parity
dotnet run --project UnityTests/CardText
python UnityTests/Parity/authoring-checks.py
dotnet run --project UnityTests/NativeFeedback
dotnet run --project UnityTests/MapShape
dotnet run --project UnityTests/SpriteStyles
dotnet run --project UnityTests/CoopRun -- --focused
```

Choose checks relevant to the edit, then run required build/CI gates. The authoring
script works on isolated copies: it round-trips CSV, refuses stale/invalid edits,
adds a card, weapon, enemy and encounter, and invokes `UnityTests/OriginalAuthoring`
to execute the new content in actual native combat. `CardText` compares all 364
original base/upgraded definitions against a pinned portable oracle.

Play the changed flow in Unity and the exported player. Record the version,
source digest, class/seed, input sequence, expected and actual resource/state
changes, and a screenshot. A screenshot proves pixels; pair it with command and
state evidence. Browser phone emulation is not a physical Android/iOS test.

## Build and preview

```powershell
.\tools\build-unity.ps1 -Target All
python -m http.server 8787 --bind 127.0.0.1 --directory Builds/Web
```

Open `http://127.0.0.1:8787`. Unity Web output is an HTML/runtime/assets folder;
serve the folder rather than double-clicking an HTML file. Use `-Target Windows`
or `-Target Android` for those exports. For a quick Web-only edit, use
`-Target Web -PreviewOnly`; this updates `Builds/Web` without presenting mixed
platform packages as a new checkpoint. `-Target All` builds all three targets and
the companion from matching source. The script tests, builds and packages
locally; it does not commit, push or publish. iOS export/device validation remains
separate work. Inspect the actual target log and `build-source.json` instead of
assuming every platform was rebuilt because Web succeeded.

The current **0.0.11.0 / build 11** exports share full source digest
`eb5ff8e45b16eef61930a9d94ab94cc681e6dd6c4d6a6dc3bea19ea5d2cffe2e`.
Web was built 2026-09-07 06:25:03.732683 UTC; the all-target builder exited zero.
Explicit package verification passed 436 companion, 160 target-file and 18
root-package checks. The artifact commit also includes derived Unity version
fields; use the full digest to match exports.

The build 11 solo art test passed 31 checks with nine screenshots and no errors
at 390×844 and 1440×900. It selected two hound instances and a wisp, spent an
action on an attack that reduced hound HP from 15 to 1, and restored exact state
after reload. Painted silhouettes were visually inspected; asset-path console
receipts were unavailable. Co-op passed eight host and six guest checks through
a fight, rewards and rejoin, with nine non-lobby screenshots and no errors; the
host combat image shows a painted hound. The prepared gallery has 21 images
(nine solo, nine co-op, three art galleries). These are local, bounded results;
current Pages assembly/link verification and publication remain separate.

The prior **0.0.10.0 / build 10** local checkpoint uses source commit
`890af027be07a5648119521165aaeadf2dc5e938` and source digest
`62aa53dcfe5f399be01efd6ed697b43a6aaf09c544950a90337641ba5101032b`. Matching Web, Windows,
Android and companion packages passed 433 companion, 160 Windows/APK and 18 root-package checks;
22 actual self-contained companion restart checks also passed. A separate co-op
browser run passed a fight, rewards and exact-hand rejoin. These results do not
mean the build is published or accepted on physical phones/graphical Windows.
The native browser run passed 657 checks, 280 commands, 22 fights, three acts,
two reloads and Chronicle checks. Map-shape controls passed 60 phone/desktop
checks; actual background/freeze/return passed eight raw-CDP checks. Appearance
passed 44 actual choice/attack/feedback/reload checks, 11 for each original style.
The feature test passed 16 checks for Draft, shrine CON/HP growth from 64 to 66,
flask allocation, merchant purchase/resale and reload. Storage passed 50 checks
(12 served-file hashes and 38 storage checks), restoring the exact backup and
preserving 729,414 damaged bytes. These results use the unchanged source digest
above, not build 11's changed source. Build 11 did not repeat the full campaign
or storage run. Broader profile corruption/quota, JavaScript-save import,
full branching-map presentation with solo fog/Sealstone reveal, co-op animation,
balance and owner/device/audio acceptance remain
open; iOS and audible sound acceptance are not established.

GitHub Pages serves static game files. Native co-op requires the matching C# host;
it cannot run a server inside Pages. The portable Windows companion instructions
are in `tools/NativeLan/Packaging/README.txt`. Extract it beside the matching Web
folder, run its launcher and open its own game URL. Guests use the join key;
the host key stays private. Preserve the `State` folder for restart/rejoin. Host
corruption recovery is explicit and preserves damaged bytes. Test local/LAN play
through the companion origin, not an HTTPS Pages page connecting to insecure local
WebSockets. No firewall/router changes are made automatically.

## Builds, history and promotion

The four channel pages are `dev`, `test`, `release` and `main`. Each can retain
immutable per-build folders with version, build number, date, source and associated
PR when known, plus changes from its predecessor. Check the selected build record
for what is actually playable. Unknown PR metadata must stay unknown.

Actions validates and publishes prepared exports after authorized channel changes;
PR validation does not deploy. Local Unity compilation and channel promotion are
separate steps. Unattended Unity compilation needs its own licensed runner setup.
Do not call a local test a deployed fix. Preserve prior versions and their original
metadata; do not relabel archived bytes to match the newer four-part version scheme.

Foundation completion is `0.1.0.0`; the finished game is `1.0.0.0`. Follow the
[roadmap](Unity-Roadmap.md) and its outstanding acceptance gates before promotion.
