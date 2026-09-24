# Enemy telegraphs: intent badges and Poise meter (F04)

**Integration status: view-model ready, UI Toolkit wiring pending (needs Unity editor).**

The Unity domain already tracks enemy intents and Poise, but
`Presentation/OriginalCombatLayout.cs` (`Enemy`) draws the intent as text and
draws no Poise meter. This change adds a pure C# view-model that computes what
the HTML shows. Presentation is unchanged because it cannot be compiled or
checked without the Unity editor.

## Entry point

```csharp
// Solo. In co-op, pass "each hero" (the HTML's victim wording).
IReadOnlyList<EnemyTelegraph> rows = combatSession.Telegraphs();
```

- `CombatSession.Telegraphs(victim = "you")`
  (`Unity/Assets/AshenSpire/Runtime/Domain/Original/CombatSession.Telegraphs.cs`)
  returns one `EnemyTelegraph` per enemy, in row order. Intent numbers go
  through `AttackDamageCalculator`, the same calculator enemy attacks use, so
  they include the enemy's Strength/Weak and the player's Vulnerable. This
  matches `previewIntent` in `src/engine/combat.js`. Call it again after every
  command to get fresh numbers.
- `CombatSession.PreviewEnemyAttack(enemyId, basis)` returns the per-hit
  number on its own.
- `EnemyTelegraphViewModel.Build(enemyJObject, player?, previewDamage?, victim, growthMult, staggeredTooltip)`
  (`.../Original/EnemyTelegraph.cs`) is the underlying pure function. It works
  on any enemy snapshot `JObject`, including the enemy rows in a co-op snapshot.
  If `previewDamage` is null, it shows the authored damage.

Calling these methods does not change combat state. The tests compare
`Snapshot()` before and after, and compare each enemy `JObject` before and
after `Build`.

### `IntentDisplay`

| Member | Meaning / HTML source (`src/ui/uiContent.js`) |
|---|---|
| `Kind`, `IconId`, `Icon` | Intent kind and its icon-table row (below) |
| `ValueText` | Pill label: `7`, `6×3`, `14 ⌛`, `12`, `Staggered`, `?`, or empty for buff/debuff |
| `BadgeText` | Glyph + label, e.g. `⚔ 6×3` |
| `CssClasses` | `intent lg attack delayed`, the same classes the HTML uses |
| `Dashed` | Delayed attack (`.as-pill.dashed`) |
| `TooltipTitle` / `TooltipBody` / `TooltipText` | `intentTooltip` wording as plain text (`<br>` → `\n`, no `<b>`) |
| `Severity` | `Unknown, Defend, Buff, Debuff, Stagger, Attack, Lethal` (Lethal: attack total ≥ player HP + Block; this is a Unity addition) |
| `Damage`, `Hits`, `TotalDamage`, `Block`, `Delayed`, `Pending` | Raw numbers and flags |

Precedence follows `intentBadge`: damage first, then block, then buff/debuff,
then unknown. A move with both damage and block is shown as an attack.

**Deliberate difference from the HTML:** `intentBadge` checks
`moveId === null` before it checks `kind === 'staggered'`. The engine always
writes staggered intents with `moveId: null`, so the HTML shows `?` for a
Staggered enemy and its Staggered branch never runs. SPEC §4.6 (and US-4.1)
says a Staggered enemy shows "Staggered", and the spec wins, so the view-model
shows `✦ Staggered`. The HTML bug should be fixed separately.

### `PoiseDisplay`

| Member | Meaning |
|---|---|
| `Visible` | False when max ≤ 0 or the meter is missing (`resources.js` poise `read → null`) |
| `Current`, `Max`, `Fraction` | Value clamped at ≥ 0; fraction clamped to [0, 1] |
| `ValueText`, `Tooltip` | `4/10`; `Poise\n4 / 10 — fill it to Stagger. <staggered status tooltip>` (co-op `.poisebar` wording) |
| `NearBreak`, `Ticks` | At ≥ 75% of max (`.poisebar.full` in `coop.js`); one tick at 0.75 |
| `Broken` | The next enemy action is skipped (`skipNextTurn` or a `staggered` intent) |
| `Staggered`, `StaggerStacks` | The `staggered` status is active, so the enemy takes +50% attack damage. This can last longer than the skipped turn |
| `NextMax` | Threshold after the next fill: `ceil(max × balance.poise.growthMult)` |
| `Tone`, `ColorHex` | `poise`, `#c9a227` (`.as-meter[data-tone="poise"]` = `--gold`) |

## Icon mapping

The table is the static `IntentIconTable.Entries` in `EnemyTelegraph.cs`.
There is no JSON copy, so no Resources asset or `.meta` is needed.

The HTML does not draw intents from image files. Each intent is a glyph
StatePill (`INTENT_ICONS` + `.as-pill[data-tone]`), and `assets/` contains no
intent art. SPEC §1 plans game-icons.net art for this, but none has been
imported. So every row's sprite fallback is the framework's typed ICON
fallback, `asset.fallback.icon` → `assets/framework/missing.svg`. It is a real
file, and the tests check that it exists. Every row also has
`DedicatedArt = false`. When intent art is imported, change `AssetPath` and
`DedicatedArt` in the table; the tests then check the new files.

| Kind | IconId (USS class) | Glyph | Tone | Colour | Asset |
|---|---|---|---|---|---|
| attack | `attack` | ⚔ | danger | `#d4622e` | `assets/framework/missing.svg` |
| block | `block` | 🛡 | frost | `#7fa8c9` | `assets/framework/missing.svg` |
| buff | `buff` | ↑ | gold | `#c9a227` | `assets/framework/missing.svg` |
| debuff | `debuff` | ☾ | violet | `#a07ad6` | `assets/framework/missing.svg` |
| staggered | `staggered` | ✦ | gold | `#c9a227` | `assets/framework/missing.svg` |
| unknown | `unknown` | ? | (none) | `#7a6f5a` | `assets/framework/missing.svg` |

The default UI Toolkit font may not contain ⚔ 🛡 ☾ ✦. Check this in the editor,
and use the sprite when a glyph does not render.

## UI Toolkit sketch (to wire in the editor)

This matches the HTML composition: the intent pill sits above the head, and a
thin gold Poise track sits directly under the HP meter.

```xml
<!-- per enemy, inside the combatant column -->
<ui:VisualElement name="intent" class="intent lg attack" data-tone="danger">
  <ui:VisualElement class="intent-icon" />          <!-- sprite fallback -->
  <ui:Label class="intent-glyph" text="⚔" />
  <ui:Label class="intent-value" text="6×3" />
</ui:VisualElement>
<ui:VisualElement class="as-meter hp">…</ui:VisualElement>
<ui:VisualElement name="poise" class="as-meter skinny poise">
  <ui:VisualElement class="m-track">
    <ui:VisualElement class="m-fill" />              <!-- style.width = Fraction * 100% -->
    <ui:VisualElement class="m-tick" />              <!-- style.left = Ticks[0] * 100% -->
  </ui:VisualElement>
</ui:VisualElement>
```

```css
.intent { flex-direction: row; align-items: center; align-self: center;
  padding: 3px 11px; border-width: 1px; border-radius: 999px;
  background-color: #1b1610; -unity-font-style: bold; font-size: 19px; }
.intent.attack   { border-color: #d4622e; color: #d4622e; }
.intent.block    { border-color: #7fa8c9; color: #7fa8c9; }
.intent.buff, .intent.staggered { border-color: #c9a227; color: #c9a227; }
.intent.debuff   { border-color: #a07ad6; color: #a07ad6; }
.intent.unknown  { border-color: #3a3024; color: #7a6f5a; }
.intent.delayed  { border-style: dashed; }  /* USS has no dashed borders: use a dashed 9-slice sprite or opacity: 0.75 */
.intent-icon { width: 18px; height: 18px; margin-right: 6px; display: none; } /* show when the glyph cannot render */
.as-meter.skinny .m-track { height: 6px; border-radius: 999px; background-color: #0d0b08;
  border-width: 1px; border-color: #3a3024; overflow: hidden; }
.as-meter.poise .m-fill { position: absolute; left: 0; top: 0; bottom: 0; background-color: #c9a227; }
.as-meter.poise.near-break .m-fill { background-color: #e0b84a; }  /* NearBreak */
.as-meter.poise.broken .m-track { border-color: #c9a227; }         /* Broken / Staggered */
.as-meter.poise .m-tick { position: absolute; top: 0; bottom: 0; width: 1px; background-color: rgba(233,220,190,0.5); }
```

Wiring steps: in `OriginalCombatLayout.Enemy`, replace the `original-enemy-intent`
text label with the pill, set its classes from `IntentDisplay.CssClasses`, set
`tooltip = IntentDisplay.TooltipText`, add the Poise meter under HP when
`Poise.Visible` is true, and toggle `near-break`/`broken` from the flags.
Rebuild these elements from `Telegraphs()` after every command result.

## Tests

`dotnet run --project UnityTests/Telegraphs` (run from the repository root)
prints `PASS: …` lines and ends with `Telegraphs: N checks passed`, or exits 1.
It covers the following:

- **Icon table vs HTML:** glyphs are parsed from `INTENT_ICONS`, kinds from
  `schemas.js` `INTENT_KINDS`, and tone colours from `base.css`/`kit.css`.
- **Asset files:** every mapped asset file exists, and it is a framework ICON row.
- **Every enemy move:** every move in `GameContent/Unity/Original/content.json`
  renders a mapped badge and a Poise meter, and rendering it does not change
  the move data.
- **Formatting and wording:** multi-hit/delayed/pending formatting, all tooltip
  wording, unknown and staggered intents, Lethal severity.
- **Poise edge cases:** zero or missing max, overfull and negative values, the
  75% cut, growth, and broken vs staggered.
- **Live `CombatSession`:** the preview equals the engine's number, Strength and
  Vulnerable are included, telegraphs are recomputed live, a Poise fill
  Staggers the enemy, and the snapshot is unchanged.

The project is not in `.github/workflows/unity-pages.yml` yet, because workflows
are owner-edited.
