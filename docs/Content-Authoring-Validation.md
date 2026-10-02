# Content authoring validation

## Original schema validation (US-16.3)

`OriginalContentValidation` (`Unity/Assets/AshenSpire/Runtime/Domain/Original`) checks
authored original content — a CSV-imported `content.json` candidate and every merged mod pack —
before the game accepts it. It collects **every** problem instead of stopping at the first one.

It has two scopes. **Full** (everything below) runs where content is authored: the CSV importer
and `OriginalModPacks`. **References** (ids and cross-table references only — the same coverage
as the earlier first-error check) is what `OriginalContentCatalog` runs on every load, including
content frozen inside saves; it now also reports every problem at once. The catalog deliberately
does not apply the field schema, so saves made with older content keep loading and loading stays
cheap (about 1 ms for references versus about 50 ms for the full check on the shipped content).

**What is checked**

- **Required fields, types and closed sets** for cards, relics, statuses, stances, keywords,
  resources, enemies (including moves, phases and Arcane Exposure), encounters, events, flasks,
  classes, attributes, creation modes, `equipment.basicCardProfiles`, `attributeRules` and
  `mapConfigs`. Unknown fields are refused, as in the HTML game.
- **Effects, triggers, predicates and formulas**: opcode, target, trigger-event, predicate and
  formula-op closed sets; per-opcode required/allowed fields; pile and position names.
- **Ranges**: non-negative costs, weights and move values; enemy HP `[min, max]` with min ≥ 1;
  level, floor and target bands with 1 ≤ min ≤ max; resistance percentages 0–100; positive
  Arcane Exposure thresholds; class max HP ≥ 1; non-empty names and encounter enemy lists.
- **References**: every id that names another row (status, card, relic, enemy, stance, encounter,
  flask, class, attribute, keyword, starting kit, armament, unlock, basic card profile, script),
  plus unique ids per table and unique armour `classId/id` pairs. `script` references accept
  `content.scripts` keys and the scripts implemented natively in C# (`wondrousDraught`).

**Where the schema comes from.** The field schema is the HTML game's own
`src/model/schemas.js` (the same `SCHEMAS`, `EFFECT_SPECS` and closed sets that
`src/model/validate.js` walks). `node tools/unity-content-schema.mjs` generates
`OriginalContentSchema.cs` from it; `--check` fails when the generated file is stale. The schema
is embedded as C# because the engine-independent Domain assembly cannot read Unity `Resources`.
`content/framework/properties.json` is not a field schema — it is the framework's
PropertyDefinition taxonomy (classification, damage, scaling, cost, lifecycle, targeting …) and
Unity's `content.json` does not reference it, so it is not shipped to Unity or used here.
Semantic rules that the Unity port deliberately changed (for example derived-stat rules and
attribute preset totals) remain validated by their own Unity code, not by this schema.

**Error format.** Each error is `file: path: message`. A row with a unique string id is named by
id; any other row (no id, duplicate id) by its index. Nested arrays use indexes, objects and maps
use dots:

```
content.json: cards[strike].cost: Expected integer or one of [X], got string "one".
content.json: enemies[wanderingSoldier].moves.slash.damage: Expected integer, got number 7.5.
content.json: encounters[loneSoldier].enemies[0]: Unknown enemy 'missingEnemy' (no enemies row has this id).
content.json: cards[182].id: Duplicate id 'strike' in cards.
```

**APIs.**

- `OriginalContentValidation.Validate(data, file, scope = Full)` returns the full list (empty
  when valid) and never throws for bad data.
- `OriginalContentValidation.ThrowIfInvalid(data, file, scope = Full)` throws one
  `OriginalContentValidationException` (still an `ArgumentException`, so existing callers are
  unchanged) whose message lists the first 20 errors and counts the rest; `Errors` holds all.
  The CSV importer (`tools/original-table.py`, via `UnityTests/Parity --validate`) runs it in
  Full scope and prints the message.
- `new OriginalContentCatalog(json[, sourceName])` calls
  `OriginalContentValidation.ValidateReferences(data, file)` (References scope, throwing).
- `OriginalModPacks.Load` turns each problem in a refused pack into its own `ValidationFailed`
  error. Its `Path` points at the pack file and record that supplied the row
  (`Mods/x/cards.json#cards[0].effects[0].status`); its message starts with the merged path
  (`cards[strike].rarity: Expected one of …`).

**Verification.** `dotnet run --project UnityTests/OriginalAuthoring` (from the repository root)
runs the validation checks: the shipped content reports zero errors; missing required fields,
wrong types, bad enums, dangling references, rows without ids, duplicate ids and several errors at
once are all reported with their paths; the aggregate message is checked; the catalog reports
several dangling references at once and still accepts schema-only problems.
`UnityTests/Mods` checks a pack with three independent problems reports all three at their pack
paths. `UnityTests/Parity/authoring-checks.py` still runs the CSV round-trip and native-combat
acceptance.

**Limits.** The C# validator does not port the HTML text-template token binding (SPEC §3.13),
effect-tag vocabulary checks, the script budget report, or the HTML-only semantic sections
(sfx, music, equipment upgrade tables, character creation). Those either remain enforced by
existing Unity code (`HandRules`, `DerivedStatCalculator`, `ActMapGenerator`, `TagCatalog`) or
are open follow-ups. The four restated validator tables in `tools/unity-content-schema.mjs`
(predicate/formula/effect/trigger field lists) are private in `validate.js` and must be kept in
step by hand.

## 0.6.0 campaign-editor validation (historical)

This is a historical adaptation document. For the native original-game rebuild
at 0.0.10.0, use [Unity-Owner-Guide.md](Unity-Owner-Guide.md). Editing its
`Original/content.json` tables is separate from the campaign editor described here.

Compiled source: `551285af6c20b41ac11c97d2d2978d8561558c88`.
Source digest: `a8d7174753a64ffdab7e8d5efa7e8bd6fdcaa6087b3033f65f989c157b5fabd3`.

### Observed

- 224 domain checks passed: 47 original, 129 campaign, 25 class-identity and 23 feedback checks.
- 21 checks passed inside Unity 6000.6.0f1 using real SerializedObject, SerializedProperty and Undo APIs. They cover seven table bindings, nested field/settings changes, Undo/Redo, readable unique IDs, deep-copy isolation, row removal, reward enrollment and draft serialization. Authoritative content stayed byte-identical.
- 19 .NET authoring checks passed for invalid definitions, missing equipment sprites, empty display names, stale saves, exact backups including BOM/line endings, import failure after save, checkpoint recovery and temporary-file cleanup.
- Equipment CSV round-trip preserved every authoritative source byte. Import of a missing equipment sprite failed before replacing source.
- Current-content diagnostics completed 192 campaigns: 189 victories and three defeats, unchanged from 0.5.0. Authoring changes do not alter gameplay or balance.
- The packaged Reaver browser campaign completed all nine encounters, purchased equipment, checked nine affinity/shared reward offers and restored saved state exactly.
- Rogue, Herald and Starseer each passed initial combat/reward, inspection, phone-width touch sizing and exact save/resume. These three browser checks were not full campaigns.
- Real-pointer feedback checks passed normal and quick timeline reset, static reduced motion, mute suppression, persisted preferences and cancellation when navigating away.
- A real 0.5.0 player created a combat save. The same browser origin switched to 0.6.0, restored all saved fields exactly and accepted a later reward. No save/state injection was used.
- The six local browser evidence sets contain 135 PNG screenshots and report no browser errors. Prior 0.5.0 screenshots, reports, metadata and changelog remain under Published/History/0.5.0. Existing audio previews are retained because their source data and waveform implementation did not change.
- Unity built Web, Windows and Android. Windows headless startup reached ASHENSPIRE_UI_READY. No C# errors or warnings were found in the three final target logs. Twelve package checks and 82 workflow checks passed.

### Limits

Editor tests exercise native serialization and Undo APIs, not graphical interaction with the authoring window. Browser checks use real desktop pointer commands at phone-sized viewports; physical Android/iOS play, graphical Windows play and audible listening remain unverified. ADB reported no connected devices. The Android inventory tool does not install or run the game.

Draft checkpoints happen explicitly and on window close/recompile. They are not continuous or crash-proof autosave. Hash conflict detection is optimistic, not a distributed write lock. Source backup/replacement and subsequent Unity import have distinct outcomes.

GitHub validates and publishes prepared players; unattended Unity compilation remains unconfigured. Deployment and hosted checks are recorded separately in the delivery report. Test, release and main have no selected Unity build.
