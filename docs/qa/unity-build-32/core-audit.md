# Core card QoL audit — 2026-10-02

Played deployed test/latest build 0.7.1.765, src f60aca04ad, through two player turns in a normal Classic Climb. Seed QOL2026, Reaver, Standard attributes, no keepsake, Wayfarer Plate, Straight Sword, Round Shield, empty slot 3. Existing slots 1 and 2 were neither loaded nor changed. Original repository was read-only. Fight completion was not verified: CUA repeatedly stalled with 7 HP left on the enemy.

Version distinction: local dirty checkout HEAD 0adaae515fbb9246c74732a27ee25870004de5ac has metadata ordinal 411; its packaged HTML visibly reports 410 src 6a86ee221b. The remote test branch buildordinal.json reports 793 src 0fe4b540ff built 2026-10-02, but the actual deployed latest title observed during this audit reports 765. Local-source pointers below describe inspected source contracts; do not imply all are deployed 765 behavior.

## Observed playing-card behavior

- Shield Defend first click selects/enlarges its face, reveals Information, and paints a blue player silhouette. Actions remain 3/3; reading does not spend the card.
- Information opens a full face with type, tags, cost, effect text, keyword buttons, lore disclosure and a contextual Play card button. Clicking Play card commits Shield Defend once: Actions 2/3, Discard 1, Block 9.
- Guard Counter first click paints a red enemy silhouette and selects its face without spending. Clicking Gilded Knight commits it: Actions 1/3, Stamina 1/2, enemy 31 to 21 HP. Its conditional effect used existing Block (10 damage rather than base 4).
- Gorefire Slash reading exposes 1 Energy + 1 Mana + 1 Stamina, Blade/Blood/Gorefire/Weapon/Melee tags, Bleed explanation control and lore. Opening/closing inspection did not spend. Closing the inspection cleared the attack arm: the next enemy click inspected the enemy; reselecting the card then clicking the enemy committed 5 damage and Bleed 3, leaving Actions 0/3 and enemy 16/31.
- End Turn at depleted Actions opened an explicit confirmation modal. Confirmation advanced through Enemy Turn, returned to Player Turn, and drew replacement cards.
- Slashing Strike on turn 2 used the same select then enemy-confirm flow, spending one Action and reducing enemy 16 to 7 HP. Runtime cards retained separate energy, mana and stamina costs.
- Right-clicks on Gorefire Slash selected the face but did not open the full reading door in the observed deployed build. A Unity right-click reading shortcut is a useful addition, but should not be reported as browser-observed core parity.
- Keyword buttons were present in inspection; clicking Explain Block focused that control, but no glossary popup was confirmed. Do not claim glossary interaction passed.

## High-priority contracts to bring into Unity

1. Keep card selection, inspection and committing distinct. Selection must never deduct resources. Armed legal enemies should visibly highlight and permit direct target confirmation, with invalid/dead targets rejected. Provide an explicit Play action for keyboard and users who prefer a button.
2. Use the same face and readable detail view for hand, deck, piles, offers and services. The detail footer must reflect context (Play, Choose, Buy, Upgrade or no action), price and current refusal. Revalidate when pressed; inspection must not accidentally commit.
3. Print each resource separately and show why a card cannot be played; preserve explicit zero Action cost. Dynamic values must come from the active runtime preview, including generated equipment cards.
4. Remember the last living hostile target for keyboard/controller focus, then fall back to the first legal living enemy. Memory moves the aiming cursor, not a silent commit.
5. Preserve save-safe, read-only pile browsing. Draw display ordering must not reveal actual future draw order or mutate RNG. Modal input owns focus and underlying combat shortcuts.
6. Drag/flick with a legal-drop ghost and red/blue silhouettes remains a source-supported follow-up, not verified gesture evidence in this audit.

## Source pointers (D:/repos/AshenSpire)

- src/ui/screens/combat.js:452 — focusTargeting prefers last living target, otherwise first living enemy.
- src/ui/screens/combat.js:570 — syncCardSelection changes presentation only; armSelf also selects without dispatch.
- src/ui/screens/combat.js:1536 — inspectionPlayAction validates active combat, phase, hand membership, playability and all resource pools; rechecks before execution. A single-target attack enters aiming, a no-target card plays.
- src/ui/screens/combat.js:1689 — endTurnHasPlayable evaluates runtime costs across Actions/Mana/Stamina rather than relying on Actions alone.
- src/ui/screens/combat.js:1890 — select/confirm/tap flow. First tap selects; confirmation can use hovered/focused target or a lone enemy. Unaffordable taps explain shortages. Hold for a targeted attack selects/focuses aiming rather than silently spending before target choice.
- src/ui/screens/combat.js:2056 — positional keys choose cards in visible order, then choose living enemy positions when aiming; unavailable cards are refused.
- src/ui/screens/combat.js:2294 — a played hostile target updates lastTargetId.
- src/ui/components/cardInspection.js:251 — contextual action rows can be live functions; disabled action reason appears in detail; click re-reads eligibility and disables the button before closing/committing.
- src/ui/components/cardInspection.js:319 — Information is separated from action/hold/drag handlers. Read-only cards can open on click; touch select then Information and Enter-to-inspect are explicit contracts.
- src/ui/components/card.js:144 — unavailable styling; 175 cost rows preserve zero Action cost while suppressing zero secondary pools; 210 shared glance/select/inspect levels.
- src/ui/components/piles.js:31 — draw display shuffle copies the list, never alters domain ordering. Piles reuse card faces and read-only details.

Evidence: core765-card-inspection.png and core765-target-selection.png beside this report.
