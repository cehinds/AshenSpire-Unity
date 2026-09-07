# Unity specification — faithful rebuild with preserved campaign checkpoints

## Owner-requested creation defaults (four-part versioning)

Assign points is the default creation mode. Tuned is removed from the fork's
available modes. All five attributes start at the minimum value of 5 for every
class. The total budget is 60, so the initial unspent pool is 60 - (5 × 5) = 35.
The per-attribute maximum remains 15. Standard remains available with its existing
presets. Plus/minus controls reflect the current bounds and remaining budget.
These are Constantine's explicit fork settings; the pinned original-engine
fixtures remain unchanged as historical parity evidence.

## Owner-requested attribute and resource progression

`Original/progression.json` records intentional Unity improvements separately from
the pinned original oracle. STR, DEX, INT and WIS each add one total damage per
point above five to their matching attacks: strength, finesse, magic and arcane.
Weapon profile identity selects the attribute; otherwise damage school and authored
card tags select it. A multi-hit card distributes that bonus across its hits,
retaining every invested point without multiplying it by the hit count. Only the
first damage operation receives the bonus. WIS also adds one card healing per point
above five. CON retains two health and two carry capacity per point; STR retains
one carry capacity per point. These benefits are explained during allocation.

DEX adds one action and INT one draw at each five-point threshold; at five they
still begin with two actions and four draw. WIS and CON retain one maximum mana
and stamina respectively per five points. Other original dodge and equipment
rules remain in their own authored mechanics. MP persists between battles; mana
flasks and shrines restore it. An authored Catch Breath command converts one
action into one stamina once per turn, only while stamina is below capacity.
Normal idle stamina recovery retains the original spend ledger.

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
