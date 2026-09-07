# Faithful Unity rebuild: foundation and parity

The target is the original AshenSpire, with the same mechanics, content, progression
and painterly identity, rebuilt in Unity and polished for phones. The previous
25-card, nine-encounter adaptation is a preserved playable checkpoint, not the
definition of the finished game. Original parity is required scope.

Reference: `cehinds/AshenSpire` dev at
`b17a7f4543e1710f49fae8b58880121690a314de` (content 0.5.5).
Unity starting point: dev `3838b896b4d160ba51c6c6e06d339ea946a3f35a`.
Task: [#33](https://github.com/cehinds/AshenSpire-Unity/issues/33).

## Current checkpoint: 0.0.10.0

The current source version is **0.0.10.0**, build number **10**, with stage
**Foundation in progress**. This is a local implementation status, not a statement
that a newly compiled player is published. Check the selected channel's build
record for its actual version, source digest, date and verification evidence.

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
  custom progression classification and Endless cycles. Debug map-shape overrides
  and a separate practice mode are explicitly unsupported.
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

The imported catalog contains 182 cards, 55 relics, 50 statuses, 19 enemies,
21 encounters, 22 events, 7 flasks, 4 classes, 25 armaments, 16 armour records,
8 starting kits, 137 upgrade rows and 18 unlocks. Imported data is not equivalent
to working campaign behavior.

## Parity checklist

| Area | Implemented / available evidence | Still required |
|---|---|---|
| Content | Full original JSON, IDs, tag joins, source receipts and native table consumers | Complete reachable interaction coverage and full original schema parity |
| Randomness | Original streams/counters, map/offers, custom starts and resume comparisons | Broader adversarial and multiplayer command traces |
| Maps and rooms | Seeded graph, encounters, unknown/history gates, all 62 event choices, services and act cycles | Player-driven coverage of every room/service branch; debug map-shape overrides remain unsupported |
| Character creation | Four classes, Assign/Standard, discovered kits/alternatives, wardrobe/relic choices, keepsakes and saved kit identity | Compiled discovery-to-unlock-to-new-character loop; full appearance render parity |
| Formulas, tags and statuses | Native consumers and original differential formula/damage/status/command fixtures | All-content interactions, malformed-content and authoring coverage |
| Flasks | Charges/utility commands, shrine split/refill, growth and cooperative friendly targets | Complete browser interaction, audible feedback and physical-device regression |
| Equipment | Owned/unlocked sets, stable cards, tiers, mounts, smithing and paid solo combat swaps | Full touch flow; co-op in-combat set changes are explicitly refused |
| Combat | Hand/resources, targets, AI/triggers/statuses, dodge/poise, deaths and real multi-enemy fights | Balance/pacing, all interactions, owner feedback and full target-dependent damage previews |
| Standard run | Real three-act native combat policies plus deterministic run/room transactions | Owner acceptance through the compiled player, including services and restoration |
| Profiles | History, unlock evaluation, discoveries, saved progression and controller consumers | End-to-end finish/reload/unlock acceptance and browser profile corruption coverage |
| Custom modes | Ascension, authored modifiers, standard/sealed/draft starts, keepsakes and Endless cycles | Full compiled mode interaction/playthrough evidence; debug map-shape and practice remain unsupported |
| Co-op | Native C# host/transport, 2–4-seat combat oracle, votes, private offers, catch-up, actual three-act two-player policy and host resume | Current multi-browser/network/host-restart acceptance; phone/LAN hardware testing; co-op supports Endless rather than all solo custom modifiers |
| Presentation | Original painted sprites, tint/sigil identity, mobile panels and receipt-driven feedback | Four original style/armour render paths under active integration; side-by-side visual/audio and touch QA |
| Save compatibility | Frozen native contracts and checksummed backup journals; isolated browser primary/backup corruption and real hidden/return checks | Current-source rerun, profile corruption/quota/upgrade matrix and explicit original-JavaScript save-import decision |
| Delivery | Four channel pages, immutable build library, dated build metadata and changelogs | Latest source-matched Web/Windows/Android and companion package checks; hosted links and owner promotion |

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

- Original card text: 1,481 domain checks cover all 364 original base/upgraded
  definitions plus grammar edges, with eight real commands verifying that the
  attribute bonus is not multiplied by hit count. The committed portable oracle
  and its pinned-source hash receipt live in `UnityTests/CardText`.
- Co-op: the focused handoff has 233 checks, four tier-flask checks and 6,587
  original combat comparisons. A separate two-Rogue, seed-1 policy completed three
  acts using normal authored enemy health in 359 commands with nine exact resumes.
  These are C# engine receipts, not browser, network-latency or fun evidence.
- Native browser storage/interruption was exercised on source digest
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
