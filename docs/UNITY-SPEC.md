# Unity adaptation specification — campaign 0.2

Upstream baseline: `cehinds/AshenSpire` `dev` at `d5c982e777df06221e181c437652b705d2f6abbc`.

The original `SPEC.md` remains a migration reference. This document defines the Unity campaign as a deliberate adaptation; it does not claim mechanical parity.

Campaign: four authored hero loadouts, three acts with nine encounter stops, twelve foe definitions and three bosses. Each stop offers one or two authored foe choices. Victory requires clearing the final encounter and selecting its reward; defeat ends command acceptance. Starting resources, decks, enemy intents and reward pools come from campaign.json.

Combat refills three energy and draws five cards each turn, up to a ten-card hand. Cards compose damage, block, draw, poison, weak, strength, heal and energy effects. Played cards reach discard after their effects. Enemy poison resolves before enemy intent and bypasses block; lethal poison prevents retaliation. Player poison resolves after enemy intent. Poison decays by one. Weak subtracts three from enemy attack damage and decays each enemy turn. Strength lasts one battle. Enemy guard lasts through the next player turn; player block resets at the next player turn.

Enemy intent patterns cycle through attack, guard, charge and poison. Equipment is unique per run, purchased on the map, and contributes through required card/hero tags. Cinders also buy a 15-cinder rest once per stop or 25-cinder card removal down to the minimum hand size. Three flasks heal 20 each by default. Rewards offer three deterministic random unique cards or configured healing. The tag registry records Id, Domain and Family; it does not implement every query in the original status DSL.

Campaign draw/discard/hand/RNG, hero, route, equipment, statuses and reward choices are saved in a checksummed per-channel record with a previous-record backup. Original JavaScript and Expedition.v1 saves are preserved; campaign saves use a separate key. Development browser output is a conventional static-hostable Unity export. Read-only diagnostics and the component gallery are limited to editor/local/dev; motion and sound preferences are available everywhere.

Acceptance: all four classes can complete seeded campaigns; model tests cover illegal commands, rewards, equipment queries, poison order and save determinism; the packaged browser completes all nine encounters using pointer commands, purchases gear and resumes exactly; Web/Windows/Android artifacts report their actual validation; CSV import rejects invalid data before source replacement; screenshots show current output. Physical-device and iOS coverage must be reported separately. Broader architecture and delivery intentions remain in Unity-Build-Brief.md.

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
