# Faithful Unity rebuild: foundation and parity

The target is the original AshenSpire, with the same mechanics, content, progression
and painterly identity, rebuilt in Unity and polished for phones. The previous
25-card, nine-encounter adaptation is a preserved playable checkpoint, not the
definition of the finished game. Original parity is required scope.

Reference: `cehinds/AshenSpire` dev at
`b17a7f4543e1710f49fae8b58880121690a314de` (content 0.5.5).
Unity starting point: dev `3838b896b4d160ba51c6c6e06d339ea946a3f35a`.
Task: [#33](https://github.com/cehinds/AshenSpire-Unity/issues/33).

## What this foundation implements

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

The imported catalog contains 182 cards, 55 relics, 50 statuses, 19 enemies,
21 encounters, 22 events, 7 flasks, 4 classes, 25 armaments, 16 armour records,
8 starting kits, 137 upgrade rows and 18 unlocks. Imported data is not equivalent
to working campaign behavior.

## Parity checklist

| Area | Current evidence | Required for full parity |
|---|---|---|
| Content | Full original JSON imported with source hash and counts | All records usable through native game flows |
| Randomness | Exact original draws, counters and resume comparisons | Full run stream consumption matches command traces |
| Maps | Exact node/edge/type/counter comparisons | Encounter assignment, unknown resolution and act transitions |
| Character creation | Presets and base resource arithmetic; editable preview | Equipment/relic bonuses, selected loadouts and run creation |
| Formulas and damage | Original formula and damage fixtures | Every card preview and actual hit uses these components |
| Statuses | All 50 definitions exercised on players and enemies | Trigger orchestration, turns, deaths and full combat outcomes |
| Tags | 401 original family/object lookups | Equipment/card/trigger consumers adopt the catalog |
| Flasks | Allocation, refill, spending, ledger and restore | Native combat and shrine commands, capacity growth and utility items |
| Equipment | All source records imported | Owned instances, sets, mounts, recomposition, smithing, extraction, installation |
| Combat | Foundation components and FIFO checks | Resources, hand lifecycle, targeting, AI, dodge, poise, triggers, multi-enemy fights |
| Encounters | Definitions imported | Rewards, shops, events, treasure, shrines, bosses and complete original run |
| Progression | Unlock definitions imported | Profiles, history, custom runs and progression persistence |
| Co-op | Original source includes LAN/co-op flows | Native host authority, connection, synchronization and multiplayer QA |
| Presentation | Existing painted sprites and mobile UI reused | Original-screen comparison, animation/FX polish, complete visual parity |
| Save compatibility | Existing adaptation saves untouched | Versioned original-run save contract; explicit legacy import decision |
| Delivery | Existing per-channel build library retained | Recurring compiled builds, physical-device checks and owner promotion |

No row is marked complete merely because its JSON exists. 0.9.0 is a foundation
checkpoint, not a claim that full Unity parity or the whole game is finished.

## Owner preview and editing

In the Editor, local browser or dev page, open **Original game foundation preview**.
Inspect all four classes, change allocation modes and points, generate seeded maps,
walk routes to the boss, and search the content tables. These are development tools;
the route explorer does not execute battles or alter a campaign save.

Owner-requested defaults in 0.9.1: **Assign points** starts every attribute at 5.
Five attributes consume 25 of the 60 total points, leaving **35 unspent**. The
maximum remains 15. **Standard** keeps its presets; **Tuned** is removed from the
fork. The original oracle still contains all original modes, so these intentional
fork settings are checked separately from upstream parity. Removing Tuned does
not delete its historical fixtures or rewrite the original import receipt.

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
The content validator covers foundations, IDs, tag joins and effect vocabulary;
it is not yet the full original schema validator. Review new mechanics before use.

`manifest.json` records the original import, not later owner edits. It is provenance,
not a promise that customized content still hashes to the upstream bundle.

## Reproduce and verify

```powershell
node tools/unity-parity-import.mjs C:/repos/AshenSpire-parity-reference
dotnet run --project UnityTests/Parity
dotnet run --project UnityTests/Domain
.\tools\build-unity.ps1 -Target Windows
.\tools\build-unity.ps1 -Target Android
.\tools\build-unity.ps1 -Target Web
```

The importer executes the pinned original JavaScript and records results as the
reference oracle. It checks its SHA and refuses dirty reference source. Ordinary
tests consume the committed oracle and need neither GitHub nor Node. The oracle
includes original content so owner tuning does not rewrite the expected values.
Map arrays and RNG counters are compared, not just graph sizes. Status checks
compare state, emitted events and queued effects; queued effects are not claimed
as full combat execution. Build and CI run the parity checks before packaging.

Unity uses the pinned Unity Newtonsoft JSON package to retain original nested
records and dictionary keys across Editor and IL2CPP. The domain has no engine
reference. The existing campaign assembly stays separate until replacement flows
have behavioral and save evidence.
