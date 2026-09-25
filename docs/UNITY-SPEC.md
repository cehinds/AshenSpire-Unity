# Unity specification — faithful rebuild with preserved campaign checkpoints

## Owner-requested creation defaults (four-part versioning)

Owner, 2026-09-24: "why are the numbers so high, rebase because the numbers
should be 1's with 3 points to spend (total of 8, not 35) ... use my past prompts
for the defaults." The fork now follows the web build's lean scale.

Owner, 2026-09-24: "please for the love of god improve the settings for this one. in
short, I'd like everyone to have low stats 1's in most stats, and starseer to have a 3
in int, and start with 4-6 cards depending on the base (3-5)". Follow-ups the same day:
creation "should have the option of standard (pre assigned class presets) and assign
points (x points to assign but configurable in advanced settings)"; opening hand
"Class base 3–5, +1 from stats"; balance "Only hand + stats" — nothing else was retuned.

Two creation modes are offered, both on the lean scale (baseline **1**, each
attribute **1–4**, total = 5 + pool, **8** as shipped):

- **Standard** (mode id `leanStandard`, the default) opens each class on its preset
  with nothing unspent, so a climb can begin at once. The presets are the web
  build's class rows (str/dex/con/wis/int): Reaver 3/1/2/1/1, Starseer 1/1/1/2/3
  (INT 3, WIS 2), Herald 1/1/2/3/1, Rogue 1/3/2/1/1. Points may still be moved
  within the fixed total.
- **Assign points** (mode id `lean`) opens every class at all **1s** with the pool
  (**3**) unspent; a climb cannot begin until it is spent. Plus/minus controls
  reflect the bounds and the remaining pool.

**Configuring the pool.** Unity has no Advanced-settings screen; the pool is
content. Edit `bonusPool` on the `lean` (Assign points) and `leanStandard`
(Standard) rows of `creationModes` in `GameContent/Unity/Original/content.json`
and mirror the file byte for byte to
`Unity/Assets/AshenSpire/Resources/Original/content.json` (baseline, minimum and
maximum live on the same rows; presets under `attributeRules.presets.leanStandard`).
A Standard preset that no longer matches its pool is fitted, not refused
(`CreationModel.Select`): values are clamped to the mode's limits; if the preset
spends more than the new total, one point at a time comes off the highest
attribute (ties: the one authored lower, then the later attribute), so the class's
primary stat shrinks last; if it spends less, the difference is left unspent for
the player to assign. A pool of 1 gives Reaver 2/1/1/1/1 and Starseer 1/1/1/1/2;
a pool of 5 gives the shipped presets plus 2 to assign. Edit the presets too if a
different spread is wanted.

`characterCreation.visibleModeIds` is `["leanStandard", "lean"]` and
`attributeRules.defaultMode` is `leanStandard`. The older `pointbuy` (relabelled
"Assign points (legacy)"; five at 5, 60 total, 35 unspent, maximum 15) and
`standard` (relabelled "Standard (legacy)"; 10–15, total 55) rows stay in
`creationModes` with their presets, so existing saves, LAN setups and exported
configurations still resolve; they are not offered. Tuned remains removed. These
are Constantine's explicit fork settings; the pinned original-engine fixtures
remain unchanged as historical parity evidence.

## Owner-requested attribute and resource progression

`Original/progression.json` (schema 2) records intentional Unity improvements
separately from the pinned original oracle. Every calculation is
`Σ floor(weight × attribute)` plus any equipment addend (owner, 2026-09-24: "all
calculations should be sum(floor(statmult*stat)) + equipment bonus"); no creation
mode converts an attribute on its way in.

**Derived stats, ruleset 6.** `derivedStatRules.rulesetVersion` is 6. Each row is
`base + Σ floor(weight × attribute) + floor((level − 1) × perLevel)`, where level is
`balance.levels.playerStartingLevel` plus the run's level-ups:

| Pool | Base | STR | DEX | CON | WIS | INT | Per level |
|---|---|---|---|---|---|---|---|
| Actions (energy) | 3 | 0.1 | 0.2 | — | 0.01 | 0.01 | 0.1 |
| Draw | 3 | — | 0.25 | — | 0.25 | 0.5 | 0.1 |
| HP | 30 | 0.35 | — | 4 | 0.1 | — | 2 |
| Stamina | 1 | 0.25 | 0.25 | 0.5 | 0.1 | — | 0.2 |
| Mana | 1 | 0.1 | — | 0.25 | 0.5 | 0.3 | 0.2 |

At all 1s a character has 34 HP and 3 actions; each CON point adds 4 HP and each
level 2 HP. Rows authored with `sourceStat`/tiers (rulesets before 6) still resolve
through the legacy path, so older saves are never re-priced.

**Ratings.** Attack (AR), Defense (DR) and Power (PR) Rating replace the old
per-point-above-five bonuses:

- AR = 0.75 STR + 0.5 DEX + 0.25 CON + 0.25 WIS + 0.25 INT
- DR = 0.5 STR + 0.75 DEX + 0.25 CON + 0.35 WIS + 0.15 INT
- PR = 0.25 DEX + 0.5 CON + 0.5 WIS + 0.75 INT

Each weight is a floored term of its own, so a 0.25 weight contributes nothing
until the attribute reaches 4, and all-1s ratings are 0. The card's weapon profile
selects the rating (`profileRatings`); otherwise its damage school does (physical
and pierce → AR, magic and arcane → PR). A profile-projected weapon card also adds
its source armament's own `attackRating` (AR/PR) or `defenseRating` (DR) — the
equipment addend. The rating joins the first damage operation, and guard-profile
cards add DR/PR once to their first block. Card text names the rating, e.g. a lean
Reaver's Slashing Strike "Includes +4 total damage from Attack Rating." Healing
receives PR (attribute term only) only on a magic or arcane card. Armament
ratings follow the web weapon table (for example Straight Sword AR 2, Greatsword
AR 4). Allocation explains the next score at which each attribute steps a rating
or pool.

**Carry weight.** Capacity stays `2 × CON + STR`; `weight.itemWeightScale` 0.2
multiplies every piece weight (kept to a tenth) so lean starts are not all Heavy.
**Flasks** hold 3 charges: Reaver, Rogue and Herald start 2 HP / 1 MP, the
Starseer 1 HP / 2 MP. MP persists between battles; mana flasks and shrines restore
it. An authored Catch Breath command converts one action into one stamina once per
turn, only while stamina is below capacity. Normal idle stamina recovery retains
the original spend ledger.

**Hand rules (O-4, ported 2026-09-24).** Owner, 2026-09-24, after the lean bot
gate won 0/12: "Port web hand rules first." Solo combat follows the web build's
hand rules (web SPEC §4.1, `src/content/handRules.js`), authored once as
`handRules` in `Original/content.json`. Each count is
`base + floor(max(0, attribute − baseline) / pointsPerCard)`, clamped to its
minimum/maximum, reading the attribute as the sheet shows it:

| Count | Base | Stat | Baseline | Points per card | Min–max |
|---|---|---|---|---|---|
| Opening draw (shared fallback) | 4 | INT | 1 | 2 | 3–15 |
| Turn draw (fixed mode) | 2 | INT | 4 | 5 | 2–10 |
| Hand capacity | 7 | INT | 1 | 5 | 1–30 |

**Per-class opening hand** (owner, 2026-09-24: "start with 4-6 cards depending on
the base (3-5)"; "Class base 3–5, +1 from stats"; 2026-09-25: "start with 4-6
cards", so every class opens on at least 4). `handRules.classStarting` gives
each class its own opening-draw rule with the same formula, replacing the shared
row for that class:

| Class | Base | Stat | Baseline | Points per card | Min–max | Standard preset | All 1s |
|---|---|---|---|---|---|---|---|
| Reaver | 3 | STR | 1 | 2 | 4–6 | 4 (STR 3) | 4 |
| Rogue | 4 | DEX | 1 | 2 | 4–6 | 5 (DEX 3) | 4 |
| Herald | 4 | WIS | 1 | 2 | 4–6 | 5 (WIS 3) | 4 |
| Starseer | 5 | INT | 1 | 2 | 4–6 | 6 (INT 3) | 5 |

So the opening hand is the class base, +1 once the class's primary stat reaches 3
(lean maximum 4 keeps it at +1), never fewer than 4 and never more than 6. An
Assign-points character at all 1s opens on its base unless it invests in its
primary stat, except the Reaver, whose base 3 is lifted to the floor of 4 (so its
STR 3 bonus adds nothing over all 1s). Each entry is
validated like any hand rule, and the content catalog refuses entries for unknown
classes. When a solo fight is created its snapshot takes the class's entry as its
`starting` rule (`HandRules.ForClass`) and drops `classStarting`, so saved fights
hold only the rule they use; content, runs and fights without `classStarting`
keep the shared row. Turn draw and capacity are unchanged: a hand holds 7 and
draws a fixed 2 a turn at lean INT 1–4.
Unplayed cards are retained (`retain`); the fixed turn draw and every draw effect
stop at capacity without touching the draw pile (`drawMode` `fill` instead draws
up to capacity). `overflow` `discard` requires the retained cards past capacity to
be chosen for discard at turn end; `promptDiscard` (off by default),
`discardLimit` and `replaceDiscards` offer optional turn-end discards and
replacement draws; `reshuffle` can stop the discard pile refilling the draw pile.
Retain and Ethereal keep their lifecycle: an Ethereal card still exhausts and can
never be chosen for discard. `CombatSession.EndTurn(discardIds)` validates the
choice before anything moves; `DiscardChoicePlan()` / `OriginalGameSession.DiscardPlan`
state its bounds.

A new solo fight snapshots the rules from its run's frozen content into the
combat save (`handRules`, `pendingDiscardDraw`). A fight or run without that
snapshot — saves made before 2026-09-24, co-op and LAN combat (the web build
applies hand rules to solo only) and headless fixtures — keeps the legacy draw
exactly: derived Draw each turn, `balance.handMax` (10) capacity, hand discarded
at turn end. The web build's Advanced-settings editor for these rules is not
ported: the values are content. The native UI has no discard picker; with the
shipped rules no prompt arises unless a combat set swap leaves retained cards past
capacity, when ending the turn is refused until a picker exists.
Checks: `UnityTests/HandRules` (mirrors web `tests/hand-rules.test.mjs`, then the
per-class openings 4/5/5/6 for Standard presets and 4/4/4/5 at all 1s).

**Bot gate: record wins, gate errors** (owner, 2026-09-25: "Record wins, gate
errors"). The policy playthrough (`dotnet run --project UnityTests/Playthrough -- 3
<dir>`, 4 classes × 3 seeds through the real public commands) must bring every run
to a terminal Victory or Defeat cleanly: it fails on an exception, an accepted
command that changes no state (stuck turn), a rejected command, an invalid command
that is not rejected atomically, a save/resume divergence, or the 3,000-command
budget running out without a terminal state. A Defeat is recorded, not a failure:
the gate prints a per-class table (wins/runs, act reached, fights won) and totals
and writes them to `results.json` (`winTable`, `totals`). Win rate is a balance
measurement tuned separately, not a gate. Likewise the compiled-player replay
(`tools/native-playtest.cjs`) must reach the terminal state its recorded trace
ends in (currently the Reaver seed-1 Defeat in act 3), with every per-step check
unchanged, and the Chronicle must record that result.

**Not ported (open gaps, owner 2026-09-24).** O-5: the web Poise
rating row and Ward meters are not ported; poise stays on its authored mechanics.
O-6: dodge keeps its `(DEX − 10) / 2` formula unchanged even though lean DEX
(1–4) sits below its pivot; it is flagged for review rather than retuned.

Weapon requirements are on the lean scale: Straight Sword STR 2, Greatsword STR 3,
Dagger DEX 2, Ash Staff INT 3.

Weapon cards and resource costs must use their final projected definition for
both display and execution. Alternate kits must meet their equipment requirements.
The class-baseline kit remains a creation-only grant even if the assigned stats
would fail its later-equip requirements; it must not be rejected as an invalid
kit on that basis. This waiver does not apply to later equip/set commands or to
alternate kits. Original discovery gating is a separate rule: baseline kits are
always available, while alternate kits require their authored discoveries.
The native creator now applies discovery gating and preserves the selected kit
identity/snapshot. Explicitly changing the baseline kit's hands must pass normal
requirements; the uncustomized class baseline retains its birth waiver. Compiled
profile/discovery/creation acceptance remains a separate verification gate.
Original fixtures remain unchanged; owner progression has separate boundary,
multi-hit conservation and transaction checks. Four-part milestone policy is in
[Unity-Versioning.md](Unity-Versioning.md); incomplete integration remains below
`0.1.0.0`.

Current native parity reference: `cehinds/AshenSpire` dev at
`b17a7f4543e1710f49fae8b58880121690a314de`. Current source checkpoint: `0.0.10.0`.
The earlier adaptation planning baseline was
`d5c982e777df06221e181c437652b705d2f6abbc`; entries below retain that historical context.

The target is now a faithful Unity rebuild of the original AshenSpire, preserving
its mechanics, content and painterly identity with mobile-first polish. The original
implemented baseline and current parity checklist are in [Unity-Parity.md](Unity-Parity.md).
The campaign 0.2–0.8 entries below document the existing playable adaptation and
remain its compatibility contract while original systems are integrated. They do
not narrow the finished game's scope or establish original mechanical parity.

Campaign: four authored hero loadouts, three acts with nine encounter stops, twelve foe definitions and three bosses. Each stop offers one or two authored foe choices. Victory requires clearing the final encounter and selecting its reward; defeat ends command acceptance. Starting resources, decks, enemy intents and reward pools come from campaign.json.

Combat refills three energy and draws five cards each turn, up to a ten-card hand. Cards compose damage, block, draw, poison, weak, strength, heal and energy effects. Played cards reach discard after their effects. Enemy poison resolves before enemy intent and bypasses block; lethal poison prevents retaliation. Player poison resolves after enemy intent. Poison decays by one. Weak subtracts three from enemy attack damage and decays each enemy turn. Strength lasts one battle. Enemy guard lasts through the next player turn; player block resets at the next player turn.

Enemy intent patterns cycle through attack, guard, charge and poison. Equipment is unique per run, purchased on the map, and contributes through required card/hero tags. Cinders also buy a 15-cinder rest once per stop or 25-cinder card removal down to the minimum hand size. Three flasks heal 20 each by default. Rewards offer three deterministic random unique cards or configured healing. The tag registry records Id, Domain and Family; it does not implement every query in the original status DSL.

Campaign draw/discard/hand/RNG, hero, route, equipment, statuses and reward choices are saved in a checksummed per-channel record with a previous-record backup. Original JavaScript and Expedition.v1 saves are preserved; campaign saves use a separate key. Development browser output is a conventional static-hostable Unity export. Read-only diagnostics and the component gallery are limited to editor/local/dev; motion and sound preferences are available everywhere.

Acceptance: all four classes can complete seeded campaigns; model tests cover illegal commands, rewards, equipment queries, poison order and save determinism; the packaged browser completes all nine encounters using pointer commands, purchases gear and resumes exactly; Web/Windows/Android artifacts report their actual validation; CSV import rejects invalid data before source replacement; screenshots show current output. Physical-device and iOS coverage must be reported separately. Broader architecture and delivery intentions remain in Unity-Build-Brief.md.

## Native original modes and service contract — 0.0.10.0

Custom Climb freezes Ascension, authored modifiers, sealed/draft deck preparation,
keepsakes and Endless configuration with the run. Draft offers are saved before
selection; resume must not reroll them. Custom runs retain their original profile
classification. Native debug map-shape overrides and a separate practice mode are
unsupported and must fail explicitly rather than appear functional.

Shrines can sell level points using authored costs and one-point allocation.
Eligible nonstarter relics and utility flasks can be resold at merchants; this
does not grant a new weapon/armour/card resale system. Equipment, upgrades and
mounts retain ownership, card-instance identity and resource deficits. Paid solo
combat set changes use actual configured cost/turn rules and rollback on refusal.

Native co-op uses an authoritative C# companion with member-specific views,
sequence-checked intents, shared routes/enemies, per-member resources/loot and
deterministic host snapshots. Join, leave, downed seats, friendly targeting and
catch-up operate on actual combat/run state. Co-op supports its original Endless
option; other solo custom modifiers and co-op paid combat set swaps are explicitly
refused. Native merchant rooms and active-combat host persistence are deliberate
improvements over the older host. Browser clients must not manufacture game state.

Card prose binds numbered tokens using the original effect order. Unbound tokens
stay braced as a visible content error. A literal single-hit/single-repeat amount
may include the projected attribute bonus, labelled as included; multi-hit and
repeat bonuses remain separate totals. Presentation must not multiply a total
bonus by hit count or claim that static amounts include target status/resistance.

These are implemented contracts, not a declaration that foundation acceptance is
complete. Current verification and remaining gates are in Unity-Parity and the
roadmap. The following 0.4–0.8 slices describe historical adaptation behavior.

## Class identity slice — 0.4

New expeditions receive authored signature-heavy eight-card loadouts. Reaver builds strength into heavy attacks; Rogue combines poison and weakness with defensive tempo; Herald sustains through faith attacks and protection; Starseer uses draw and multi-effect magic. Existing saved decks are retained.

A configured CommonRewardTag and each hero's RewardTags partition the existing RewardCards catalog. New rewards select two distinct affinity cards and one shared card, deterministically using saved RNG; affinity and shared pools must be disjoint and validated. Saved pending reward IDs are retained without rerolling or being rejected for belonging to an older pool. Definitions without affinity configuration retain the legacy three-card random selection.

Poison and heal effects can receive equipment bonuses through the same RequiredTag query used for damage/block. Descriptions and feedback use those bonuses. Gear remains unique, purchased on the map, with no slot restriction. Existing items and resource values are preserved in saves; updated content tuning applies when future commands resolve. The forge reports how many current deck cards contain the relevant tagged effect.

Quickstep remains free but provides block without draw, avoiding free replacement chains from repeated copies. Other added cards use the existing eight effect primitives. Shared gear becomes more expensive relative to narrow affinity gear; no currency or resource mechanic is added.

Acceptance: no duplicate or off-affinity new rewards; deterministic offers survive save/resume; old pending offers remain claimable; poison/heal modifiers match tags and actual results; unrelated cards receive no bonus; starter identities and class-specific reward choices are visible in browser play; diagnostic policy runs report wins, turns and equipment choices without equating wins with fun. Authoring validation and available target builds pass before dev publication.

## Combat presentation slice — 0.5

Successful commands resolve and save immediately. Presentation observes their results; animation and audio never apply damage, consume resources or block input. Ordered card-tag rules choose authored feedback cues. Each cue defines a sprite-pose timeline, duration, motion distance, impact color and synthesized sound shape. Existing hero sprites are reused. Enemy reactions and temporary outcome labels accompany actual health, block, poison and healing changes.

One view-owned feedback component runs a bounded timeline and cancels it when the view changes or a newer command arrives. Cached textures avoid repeated resource loads during animation. Reduced motion retains static outcome text with no movement or flashing; fast motion shortens the timeline. Mute stops active sounds and remains persisted. Feedback preferences and cues do not change the campaign save schema or simulation.

Acceptance: invalid cue IDs, tags, poses and numeric ranges fail content validation; successful, rejected, repeated and interrupted commands preserve their existing model outcomes; normal and fast timelines return to idle; reduced motion and mute work through real controls and survive reload; actual damage labels reflect blocked/capped results; browser evidence captures an active pose and settled frame. Web, Windows and Android builds and the full campaign regression pass, with physical-device and audible listening limits stated separately.

## Content authoring slice — 0.6

The Unity Campaign Content Editor uses structured fields for records, nested effects and lists, with searchable tables for cards, heroes, foes, encounters, equipment, tags and feedback cues. Campaign settings expose resources, reward catalogs and sound volume. Adding or duplicating records produces readable unique IDs; adding a selected card to the reward catalog is explicit. Draft edits survive table navigation and support Unity Undo/Redo.

Authoritative JSON changes only after full definition and referenced-art validation. A save checks that the source still matches the loaded hash, retains the exact previous bytes in a backup, and atomically replaces the source. Import failure after a successful source save is reported distinctly. Unsaved drafts have a recoverable checkpoint outside Assets; restoring a draft retains its original source hash so newer disk changes cannot be silently overwritten.

Acceptance: structured nested edits and add/remove operations support Undo/Redo; duplicate records do not share mutable arrays; invalid or stale saves leave source bytes intact; successful saves preserve a byte-identical backup; failed import reports saved source accurately; draft recovery preserves unsaved values and conflict detection. No gameplay, balance or save-schema changes. Mobile device coverage must identify actual connected hardware; an empty adb inventory is not a device test.

## Browser mobile viewport slice — 0.7

Browser layout uses the displayed canvas height in CSS pixels, independently of its render-buffer resolution. Keep the existing 1.5 render-density cap. Named action buttons, settings toggles and seed input have a minimum 44 CSS-pixel touch height at supported phone sizes. Compact landscape layouts use the displayed height when choosing their reference resolution. Reflow existing controls after orientation or viewport changes without changing campaign state or saves.

Acceptance: reproduce the 0.6 high-density failure; verify densities 1, 2 and 3, narrow portrait, landscape and repeated rotation; use real touchscreen input for a dense-browser combat/reward check; verify canvas-offset input mapping with synthetic safe-area padding. Report browser emulation separately from physical devices. Native safe-area behavior, gameplay, content and save schema remain unchanged. Export Web, Windows and Android checkpoints and retain prior evidence.

## Interruption and return slice — 0.8

Native application pause and browser document hiding save the current campaign, cancel active visual feedback and stop transient audio. A blocking cover preserves the current screen, card selection, seed draft and scroll position. Returning to the foreground enables an explicit Return button; it does not advance the run or replay an interrupted cue. Temporary audio suspension is separate from the persisted mute setting. Overlapping interruption sources and repeated notifications are idempotent. Browser canvas blur and Android keyboard focus loss do not count as backgrounding.

The controller owns lifecycle binding and unbinding. Presentation owns the cover and emits the return request; a small plain C# state object tracks interruption sources and return eligibility. Campaign rules, content and save schema are unchanged. Existing saves after commands remain the protection when an operating system terminates a player without delivering a final callback.

Acceptance: cover input cannot reach underlying controls; return preserves serialized run state and the current view; interrupted feedback cancels without later completion; sound stops without overwriting mute; repeated and overlapping signals require all sources to clear before return; draft input survives; reload and upgrade from 0.7 preserve the campaign. Verify actual browser visibility transitions separately from simulated lifecycle unit tests and physical phone behavior. Export all three dev targets, retain screenshots and document owner modification points.
