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
