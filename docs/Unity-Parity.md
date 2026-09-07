# Faithful Unity rebuild: foundation and parity

The target is the original AshenSpire, with the same mechanics, content, progression
and painterly identity, rebuilt in Unity and polished for phones. The previous
25-card, nine-encounter adaptation is a preserved playable checkpoint, not the
definition of the finished game. Original parity is required scope.

Reference: `cehinds/AshenSpire` dev at
`b17a7f4543e1710f49fae8b58880121690a314de` (content 0.5.5).
Unity starting point: dev `3838b896b4d160ba51c6c6e06d339ea946a3f35a`.
Task: [#33](https://github.com/cehinds/AshenSpire-Unity/issues/33).

## Current source: 0.0.13.0

The current source version is **0.0.13.0**, build number **13**, with stage
**Foundation in progress**. This is a local implementation status, not a statement
that a newly compiled player is published. Check the selected channel's build
record for its actual version, source digest, date and verification evidence.

Build 13 fixes whole-valued weapon damage labels and adds shared cost and shortage wording to solo/co-op. See [combat readability](Unity-Combat-Readability.md) for current checks and limitations; the [roadmap](Unity-Roadmap.md) lists remaining foundation acceptance.

Build 12 restored the shared original branching map and camera controls; see [map foundation](Unity-Map-Foundation.md) for its historical evidence.

## Earlier build evidence

Build 11 integrates painted artwork for all 19 original enemies in solo and co-op.
It preserves the earlier 12 paintings and adds seven individual transparent sprites.
The all-target builder completed successfully. Web, Windows, Android and companion
exports share source digest
`eb5ff8e45b16eef61930a9d94ab94cc681e6dd6c4d6a6dc3bea19ea5d2cffe2e`;
Web was built at **2026-09-07 06:25:03.732683 UTC**. Explicit package verification
passed **436 companion, 160 target-file and 18 root-package checks**. These results
do not establish publication or physical-device acceptance.

The build 11 source/art review passed 217 checks across all 19 mappings. Compiled
solo art passed 31 checks with nine screenshots and no errors; it exercised two
Blight Hounds and one Grave Wisp at phone and desktop sizes, a real attack and exact
reload. Co-op passed eight host and six guest checks through a fight, rewards and
rejoin, with nine non-lobby screenshots and no errors. This is bounded build 11
evidence, not a new full-campaign or storage run. Full details and limits are below.

The prior **0.0.10.0 / build 10** Web, Windows, Android and companion checkpoint identifies source
commit `890af027be07a5648119521165aaeadf2dc5e938` and build source digest
`62aa53dcfe5f399be01efd6ed697b43a6aaf09c544950a90337641ba5101032b`. The Web manifest records
2026-09-07 05:02:18 UTC. Package verification passed 433 companion, 160 Windows/APK
and 18 root-package checks; the actual self-contained companion restart check passed 22 checks.
These are local build/package results, not publication, graphical Windows play or
physical Android acceptance.

The native game now connects character creation, equipment-derived cards, combat,
three-act room traversal, rewards, profiles and persistence. Custom/Ascension,
sealed/draft starts, Endless, original starting choices and native co-op now have
implemented domain and presentation paths. Their implementation is distinct from
complete browser, device and original-interaction acceptance. The older adaptation remains
available as a preserved checkpoint. Neither its nine-encounter playthrough nor
the original catalog preview proves the native game's full parity.

## Implemented native components

The `AshenSpire.Original` assembly contains engine-independent C# components under
`Runtime/Domain/Original`. Its folders and namespaces match. JSON retains the
original property names as an interchange contract; C# public APIs use PascalCase
and private fields use `_camelCase`. Each script begins with its role, ownership
and modification points. Unity lifecycle stays in `RunController`; the view emits
input requests and delegates calculations to domain components.

- `OriginalContentCatalog`: the complete imported content bundle, stable IDs,
  scoped record lookup, validation and copy boundaries.
- `RandomStreams`: uint32 mulberry32, original named streams, counters and seed text.
- `FormulaEvaluator`: all nine original structured formula operations, intermediate
  fractions, clamps and final flooring. No evaluated strings.
- `TagCatalog`: family/domain validation and family/scope/object/tag joins.
- `DerivedStatCalculator` and `CreationModel`: rule layers, attribute tiers,
  editable class presets, allocation budgets and base resource previews.
- `FlaskChargePool`: original health/mana allocation, capacity ledger, spending,
  refill and validated snapshot restore.
- `ActMapGenerator`: original seeded graph geometry, typing, fixed floors,
  graph-level minima and pre-elite rest placement.
- `StatusSystem`: original stack modes, duration/decay, meters, threshold procs,
  resistance, modifiers and ordered on-fill effects.
- `AttackDamageCalculator`: original bonus/multiplier/vulnerability/resistance order.
- `CombatContext`: explicit effect registration, FIFO queue, event copies and bounds.
  Unimplemented handlers throw; they never silently discard an effect.
- `ResourceWallet`, `CardZoneLedger` and `CombatSession`: final projected card
  costs, draw/discard/exhaust/retain, target selection, enemy turns, triggers,
  statuses, dodge/poise, health/mana charges and utility flasks.
- `WeaponLoadout`, `WeaponCardComposer`, `WeaponCardProjection`, `ItemUpgradeService`
  and `CardMountService`: ownership, slot requirements, weapon-sourced cards,
  upgrades, extraction, installation and stable card instances.
- `OriginalCharacterBuilder` and `OriginalPlayerProjection`: selected kit,
  starting relic, useful attributes, equipment bonuses, weight and resource
  reconciliation. Out-of-combat equipment changes preserve missing resources.
- `OriginalRunRules`, `OriginalRunContent` and `OriginalRunSession`: original
  encounter selection, pre-resolved unknown rooms, all authored event choices,
  rewards, shops, shrines, act transitions and saved room transactions.
- `OriginalGameSession`: native combat/run composition with command rollback and
  frozen content, mechanics, progression and RNG inside the saved run.
- `OriginalSaveJournal`: checksummed current/previous records with validated
  recovery. Native run and profile storage use separate per-channel keys.
- `OriginalProfile`: run history, cumulative progress, authored unlock evaluation,
  equipment discovery receipts and slot unlock counts. Controller persistence and
  the profile view are integrated; complete player-facing progression verification
  remains a separate gate.

Additional integrated components:

- `OriginalStartingOptions`: discovered kits and hand alternatives, starting
  relics/wardrobe, saved kit identity and the creation-only baseline waiver.
- `OriginalCustomRunRules`: Ascension/modifiers, sealed and draft starts, keepsakes,
  custom progression classification and Endless cycles. `OriginalMapShape` and
  `OriginalMapShapePanel` add the original Custom Climb floor/column caps and
  relative node weights, per-act validation, isolated density sampling and frozen
  restoration. There is no separate practice mode in the pinned original.
- `OriginalRunServices`: shrine level-point purchases and merchant resale of
  eligible relics/utility flasks. The original resale contract does not sell weapons,
  armour or cards; extraction is a separate smithing operation.
- `OriginalCombatEquipment`: paid solo active-set changes, resource/turn costs,
  remaining swap budget, stable card recomposition and atomic refusal.
- `OriginalCoopCombat` and `OriginalCoopRun`: authoritative shared battles/routes,
  private member rewards, shops/events/services, catch-up, join/rejoin, sequence
  validation and saved host state. The native LAN companion runs this C# engine;
  clients submit intents and receive member-specific projections.
- `OriginalCardText`: original numbered effect/hit tokens and explicit unresolved
  values. Single-hit literal attribute bonuses can be included in the shown amount;
  multi-hit bonuses remain separate totals. `NativeFeedbackProjection` observes
  actual damage/healing/guard/resource/status receipts without applying game rules.
- `OriginalEnemyFigure`: shared solo/co-op painted enemy rendering, using all 19
  stable enemy IDs from `GameContent/Unity/Original/enemy-art.json`. Registered
  alpha bounds fit visible silhouettes inside the frame and align their feet at
  94% height. The seven new textures import with a 512-pixel cap; high-resolution
  transparent masters and exact prompts remain in `GameContent/Unity/Art/EnemyExpansion`.
  The earlier 12 painted resources are unchanged. Build 11 has source/art and
  bounded compiled checks; all-enemy browser and owner visual acceptance remain open.

The imported catalog contains 182 cards, 55 relics, 50 statuses, 19 enemies,
21 encounters, 22 events, 7 flasks, 4 classes, 25 armaments, 16 armour records,
8 starting kits, 137 upgrade rows and 18 unlocks. Imported data is not equivalent
to working campaign behavior.

## Parity checklist

Passed compiled-player counts in this table belong to **build 10**, source commit
`890af027be07a5648119521165aaeadf2dc5e938`, digest `62aa53dc…`, unless explicitly
identified otherwise. They are regression baselines, not build 11 test results.

| Area | Implemented / available evidence | Still required |
|---|---|---|
| Content | Full original JSON, IDs, tag joins, source receipts and native table consumers | Complete reachable interaction coverage and full original schema parity |
| Randomness | Original streams/counters, map/offers, custom starts and resume comparisons | Broader adversarial and multiplayer command traces |
| Maps and rooms | Seeded graph, original run-shape caps/weights, encounters, unknown/history gates, all 62 event choices, services and act cycles; 60 phone/desktop map-shape controls/combat/reload checks passed | Full original branching-map presentation, including solo fog and Sealstone Key reveal, plus player-driven coverage of every room/service branch |
| Character creation | Four classes, Assign/Standard, discovered kits/alternatives, wardrobe/relic choices, keepsakes and saved kit identity | Broader discovery-to-unlock-to-new-character coverage and owner visual acceptance |
| Formulas, tags and statuses | Native consumers and original differential formula/damage/status/command fixtures | All-content interactions, malformed-content and authoring coverage |
| Flasks | Charges/utility commands, shrine split/refill, growth and cooperative friendly targets | Complete browser interaction, audible feedback and physical-device regression |
| Equipment | Owned/unlocked sets, stable cards, tiers, mounts, smithing and paid solo combat swaps; pinned original co-op has no paid combat swap command | Broader touch interaction coverage; adding co-op combat swaps would be future design, not original parity |
| Combat | Hand/resources, targets, AI/triggers/statuses, dodge/poise, deaths and real multi-enemy fights | Balance/pacing, all interactions, owner feedback and full target-dependent damage previews |
| Standard run | Real three-act native combat policies plus deterministic run/room transactions; scripted compiled browser run passed 657 checks, 280 commands, 22 fights, three acts and two reloads | Owner acceptance and broader interaction coverage |
| Profiles | History, unlock evaluation, discoveries, saved progression and controller consumers | End-to-end finish/reload/unlock acceptance and browser profile corruption coverage |
| Custom modes | Ascension, authored modifiers, standard/sealed/draft starts, keepsakes, original run-shape controls and Endless cycles | Broader compiled mode/playthrough matrix beyond the 16 passed Draft/service/reload checks; no separate practice mode exists in the pinned original |
| Co-op | Native C# host/transport, 2–4-seat combat oracle, votes, private offers, catch-up, actual three-act two-player policy and host resume; browser fight/reward/exact-hand rejoin and packaged restart checks passed | Broader multiplayer/network and phone/LAN hardware acceptance; co-op supports Endless rather than all solo custom modifiers |
| Presentation | Distinct Animated/Rendered/Classic/Sigil paths, original armour/tint pose assets, mobile panels and receipt-driven feedback; 44 actual style-choice/attack/feedback/reload checks passed | Co-op pose timeline, side-by-side owner visual/audio and physical touch QA |
| Painted enemies | Build 11 maps all 19 enemies to shared solo/co-op paintings; 217 source/art checks, 31 compiled hound/wisp checks, nine solo screenshots and visible painted co-op combat; registered bounds and 512-pixel imports for additions | Browser coverage of remaining enemy silhouettes, broader feedback/target interactions and owner visual acceptance |
| Save compatibility | Frozen native contracts and checksummed backup journals; build 10's 12 served-file hashes and 38 storage checks passed, including exact backup and damaged-byte preservation; build 10 background/freeze/return passed eight checks | Profile corruption, quota/upgrade matrix and original-JavaScript save-import decision |
| Delivery | Four channel pages, immutable build library, dated metadata and changelogs; build 11 matching Web/Windows/Android/companion exports passed 436 companion, 160 target-file and 18 root-package checks | Current Pages assembly/link verification and owner promotion; no live build 11 publication claimed |

No row is complete merely because its JSON exists or its happy path runs.
The detailed remaining work is in [Unity-Roadmap.md](Unity-Roadmap.md).
**0.1.0.0** requires an accepted foundation checkpoint; **1.0.0.0** requires the
completed game. Native co-op remains required original-game parity. Its implementation does not
by itself complete multiplayer acceptance or waive any original feature.

## Owner preview and editing

In the current native player, start a climb through its creation screen. The
separate **Original game foundation preview** remains an inspection tool.
Inspect all four classes, change allocation modes and points, generate seeded maps,
walk routes to the boss, and search the content tables. These are development tools;
the route explorer does not execute battles or alter a campaign save.

Owner-requested defaults in the current four-part-version branch: **Assign points** starts every attribute at 5.
Five attributes consume 25 of the 60 total points, leaving **35 unspent**. The
maximum remains 15. **Standard** keeps its presets; **Tuned** is removed from the
fork. The original oracle still contains all original modes, so these intentional
fork settings are checked separately from upstream parity. Removing Tuned does
not delete its historical fixtures or rewrite the original import receipt.

The owner-requested per-point/threshold bonuses and Catch Breath are recorded in
`progression.json` and [UNITY-SPEC.md](UNITY-SPEC.md). They are deliberate fork
rules. Leaving a shrine without resting is another explicit usability addition;
it grants no healing or charges and prevents a no-rest relic from trapping a run.

Paid combat set changes and the fork's Catch Breath action are intentionally
solo-only. The pinned original co-op offers neither command. Any cooperative
extension needs a separate design decision; their absence is not a parity gap.

Authoritative fork data: `GameContent/Unity/Original/content.json`.
Unity imports it to `Resources/Original/content.json` using
**AshenSpire → 1. Validate and Import Content**. Never edit the generated copy.

For a spreadsheet table:

```powershell
python tools/original-table.py export cards Builds/Cards.csv
# Edit the CSV. Every non-empty cell is JSON: "Strike", 1, [ ... ], { ... }.
# Blank = property absent; null = explicit JSON null. Keep the receipt beside it.
python tools/original-table.py import cards Builds/Cards.csv
```

Any array of records can be exported, including `equipment.armaments`. Nested
effects stay as JSON cells. Import checks the full candidate catalog, refuses stale
source hashes, retains exact previous bytes and replaces the source atomically.
The content validator covers foundations, IDs, cross-references, tag joins and
effect vocabulary; it is not yet the full original schema validator. The native
authoring test also exercises added cards, weapons, enemies and encounters through
combat using an isolated content copy. Review new mechanics before use. See
[Unity-Owner-Guide.md](Unity-Owner-Guide.md) for the current editing/test path.

`manifest.json` records the original import, not later owner edits. It is provenance,
not a promise that customized content still hashes to the upstream bundle.

## Reproduce and verify

```powershell
dotnet run --project UnityTests/Parity
dotnet run --project UnityTests/Domain
dotnet run --project UnityTests/CardText
dotnet run --project UnityTests/NativeFeedback
dotnet run --project UnityTests/MapShape
dotnet run --project UnityTests/SpriteStyles
dotnet run --project UnityTests/CoopRun -- --focused
.\tools\build-unity.ps1 -Target Windows
.\tools\build-unity.ps1 -Target Android
.\tools\build-unity.ps1 -Target Web
```

The upstream importer (`tools/unity-parity-import.mjs`) executes the pinned
original JavaScript and records reference oracles. Treat reimport as a deliberate
upstream migration: it can replace authoritative imported content, so it is not
an ordinary owner-editing or test command. It checks its SHA and refuses dirty reference source. Ordinary
tests consume the committed oracle and need neither GitHub nor Node. The oracle
includes original content so owner tuning does not rewrite the expected values.
Map arrays and RNG counters are compared, not just graph sizes. Combat fixtures
compare state/events and restore at command boundaries. Run tests exercise eight
three-act routes using supplied combat victories; a ninth focused route checks
boss IDs. The run/equipment lane passed 4,538 baseline and 139 focused checks on
the integrated checkpoint. These 4,677 checks prove that lane's contracts, not
playable combat. Actual native combat policy runs, compiled browser interactions
and physical-device checks must retain their own source-matched receipts.

`UnityTests/Parity` now contains separate resource, equipment, combat, run and
profile fixtures; the weapon group invokes the profile checks. A fixture file's
presence is not a passing test result. Newly integrated groups need a successful
invoked-runner/build receipt before their verification is claimed. Build and CI run parity checks
before packaging; do not substitute earlier adaptation evidence for native tests.

Unity uses the pinned Unity Newtonsoft JSON package to retain original nested
records and dictionary keys across Editor and IL2CPP. The domain has no engine
reference. The existing campaign assembly remains separately available, and its
legacy saves are preserved rather than silently converted into native runs.

### Source-matched evidence, not blanket completion

**Build 11:** the full build digest is
`eb5ff8e45b16eef61930a9d94ab94cc681e6dd6c4d6a6dc3bea19ea5d2cffe2e`,
with Web export time 2026-09-07 06:25:03.732683 UTC. All-target build exit was zero;
explicit package verification passed 436 companion, 160 target-file and 18
root-package checks. Use this full digest to identify exported bytes; the final
artifact commit also includes derived Unity version fields.

- Source/art review: 217 checks cover 19 mappings, twelve reused paintings and
  seven new transparent sprites. This does not mean every enemy was rendered in
  a compiled battle.
- Solo art: 31 checks, nine screenshots and zero errors at 390×844 and 1440×900.
  Two Blight Hound instances and one Grave Wisp were exercised through target
  selection and framing. One actual attack spent an action and reduced a hound
  from 15 HP to 1; reload restored exact state. Painted silhouettes were visually
  inspected. Asset-path console receipts were unavailable and are not claimed.
- Co-op: eight host and six guest checks passed a real fight, rewards and rejoin,
  with zero errors and nine non-lobby screenshots. The host combat screenshot
  visibly shows a painted hound. This does not complete all multiplayer or enemy
  visual coverage.
- The prepared gallery contains 21 images: nine solo, nine co-op and three art
  galleries. A prepared gallery is not a live Pages deployment. The local Pages
  preview passed 242 navigation checks across 38 pages and 16 archived players.
  The bounded Git copier preserved all 1,826 site files byte for byte; it uses
  seven Git processes for 1,635 channel files. The preview selects candidate
  `0546f0702ca9d9e1c8f3edbc4ce5cc22db65fe66` as Dev without moving a branch.
  Its 943.373 MiB size leaves 6.627 MiB under the unchanged 950 MiB budget.
  Additional archive hosting capacity is needed before another large native
  build; owner promotion and live publication remain separate.

**Historical build 10 baseline:**

The compiled browser, co-op, package, appearance and storage receipts below are
for **0.0.10.0 / build 10**, commit `890af027be07a5648119521165aaeadf2dc5e938`
and full build digest `62aa53dcfe5f399be01efd6ed697b43a6aaf09c544950a90337641ba5101032b`.
Named engine/subset and earlier-build receipts retain their separate hashes.
They do not certify build 11's new paintings. No new full 657-check campaign or
storage run was performed for build 11; its bounded results are recorded above.

- Original card text: 1,481 domain checks cover all 364 original base/upgraded
  definitions plus grammar edges, with eight real commands verifying that the
  attribute bonus is not multiplied by hit count. The committed portable oracle
  and its pinned-source hash receipt live in `UnityTests/CardText`.
- Co-op: the focused handoff has 233 checks, four tier-flask checks and 6,587
  original combat comparisons. A separate two-Rogue, seed-1 policy completed three
  acts using normal authored enemy health in 359 commands with nine exact resumes.
  These are C# engine receipts, not network-latency or fun evidence. Separately,
  the build 10 co-op browser check passed a real fight, rewards and exact-hand
  rejoin. The packaged companion passed 22 actual self-contained restart checks;
  this does not certify physical phone/LAN hardware or every multiplayer flow.
- Normal-content solo policies record 12 victories, 264 fights, 3,450 accepted
  commands and 858 save/resume comparisons. The final policy receipt is
  `TestResults/NativePolicyFinal/results.json`, with runtime-source digest
  `39286ae6213e53c5a6d657a0cd571bed04617ca73692f58e760cc5874cc883ea`.
  That digest covers a runtime subset, not the full Unity build digest above.
- Native Browser-Final passed 657 checks, 280 commands, 22 fights, three acts,
  two reloads and Chronicle checks through actual browser controls. This is
  scripted compiled-player evidence, not owner or physical-device acceptance.
- Map shape has 2,696 native/source checks, including 339 original shape cases,
  720 exact maps/RNG counters and twelve 24-seed sampler receipts. The shaped
  room traversal supplies combat results, so it is not combat-play evidence.
  Separately, MapShape-Final passed 60 actual phone/desktop control, combat and
  exact-reload checks. The map view remains reachable route buttons; it does not
  reproduce the original full branching-map presentation, including solo fog and
  the Sealstone Key's reveal behavior.
- Appearance-Final-R2 passed 44 checks, 11 each for Animated, Rendered, Classic
  and Sigil, through actual choice, attack, feedback and reload interactions.
- Feature-Final passed 16 checks covering Draft, shrine CON/HP growth from 64 to
  66, flask allocation, merchant purchase/resale and reload.
- Storage-Final passed 50 checks: 12 served-file hashes plus 38 storage checks.
  Backup recovery restored exact state, and 729,414 damaged bytes were preserved.
  This does not establish profile corruption, quota or upgrade coverage.
- Interruption-Final passed eight actual raw-CDP
  background/freeze/return checks on build 10; this is not phone lifecycle
  or audible audio acceptance.
- Earlier native browser storage/interruption was exercised on source digest
  `496d262a1639afac75ca8d8d01e5361cf9cf31b8c66c4d51d1eb99184846f96f`,
  built 2026-09-07 03:46:40 UTC. An isolated fresh browser passed 38 journal checks
  and eight raw-CDP real hide/freeze/return checks. Primary corruption restored
  the exact backup; both-corrupt bytes remained intact. This does not cover profile
  corruption, quota exhaustion, physical phone lifecycle or later source changes.

Acceptance stays open until the current compiled checkpoint demonstrates the
required original modes and flows, restoration/reconnect, content editing, readable
phone/desktop screens and target-specific build evidence. Record remaining defects
and explicit scope decisions before selecting `0.1.0.0`; keep `1.0.0.0` for the
completed game. A test count alone cannot close this gate.
