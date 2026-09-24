# Enemy telegraphs: intent badges and Poise meter (F04)

**Integration status: wired; compile-verified against Unity reference assemblies; needs editor play test.**

The Unity domain tracks enemy intents and Poise. A pure C# view-model
(`EnemyTelegraphViewModel`) computes what the HTML shows, and
`Presentation/EnemyTelegraphView.cs` draws it in the UI Toolkit combat screen,
solo and co-op. `node tools/unity-runtime-check.mjs` compiles it against the
Unity reference assemblies; nobody has run it in the editor or a player yet.

## UI wiring (what shipped)

- `Presentation/EnemyTelegraphView.cs` (new) and `Resources/EnemyTelegraphs.uss`
  (new, loaded by the view onto each enemy slot). `Attach(slot, telegraph)`
  replaces the old `original-enemy-intent` text label, at the same position, with
  an intent pill named `enemy-intent-<id>` (glyph + value, classes
  `telegraph-intent telegraph-<kind> telegraph-severity-<severity>` plus
  `telegraph-delayed`/`telegraph-pending`). It inserts a Poise meter named
  `enemy-poise-<id>` directly under the HP pool: a 6px gold track, a tick at 75%,
  `near-break`, `broken` and `staggered` classes, and a small ✦ when the meter is
  broken or the enemy is Staggered.
- Hook lines: `OriginalRunPanel.Combat` passes `OriginalGameSession.Telegraphs()`
  (a new one-line pass-through to `CombatSession.Telegraphs()`, so solo numbers
  include Strength/Weak/Vulnerable). `OriginalCoopPanel.Combat` builds from the
  host snapshot with `EnemyTelegraphView.FromSnapshot` (victim "each hero").
  Co-op shows **authored** damage because the client has no combat session; the
  local hero is used only for the Lethal tier.
- Enemy target buttons keep their names (`native-target-<id>`, `coop-target-<id>`),
  click behaviour and slot sizes. The new elements are not Buttons, so the
  control reports the browser playtests read are unchanged. The HP pool,
  name and caption labels are untouched.
- Tooltips: runtime UI Toolkit panels do not draw `tooltip`, so the view shows
  its own floating panel on the panel root. With a mouse it appears after a
  350 ms hover and hides when the pointer leaves. With touch or a pen it appears
  after a 450 ms long press, releases the slot's pointer capture so the press
  does not also select the target, and hides after 4 s or on the next re-render.
  The intent and Poise meter both have one.
- Glyph fonts: ⚔ 🛡 ✦ ⌛ come from `Fonts/GlyphFonts.json`. ↑ and ☾ are not in
  that map, so they fall back to `Fonts/NotoSansSymbols-Regular`, which contains
  both (checked against its cmap). The sprite fallback (`missing.svg`) is not
  wired.

## What the owner should look at in play

1. **Solo first fight:** each enemy shows a coloured pill above its art (red ⚔
   for attacks, blue 🛡 for block, gold ↑ buff, violet ☾ debuff, grey `?`). Check
   that the number matches the damage taken, including with Weak or Vulnerable.
2. **Glyph rendering:** every glyph draws rather than showing a box, on Web,
   Windows and Android.
3. **Poise meter:** a thin gold bar under each enemy HP bar. It fills as you
   hit, brightens at 75% (at the tick), and shows a gold border and ✦ when the
   enemy is Staggered. The next intent should read `✦ Staggered`.
4. **Delayed/committed attacks** (e.g. charged moves): the pill is hollow and
   dimmer with ⌛. When committed it gets a thicker border.
5. **Lethal:** when an attack would kill you through your Block, the pill turns
   bright red with a thicker border.
6. **Tooltips:** hover on desktop, long-press on a phone. The panel should
   stay on screen near the screen edges, and a long press must not change
   the selected target.
7. **Layout:** the phone portrait/landscape enemy slots still fit. The pill
   keeps the old 24px row, and the Poise row adds about 12px taken from the art.
8. **Co-op:** the same pill and meter show on the shared fight. The tooltip
   says "each hero", and the numbers are authored (unmodified) damage.

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

## UI Toolkit sketch (original design note; the shipped version is above)

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
