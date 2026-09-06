# Unity adaptation specification — campaign 0.2

Upstream baseline: `cehinds/AshenSpire` `dev` at `d5c982e777df06221e181c437652b705d2f6abbc`.

The original `SPEC.md` remains a migration reference. This document defines the Unity campaign as a deliberate adaptation; it does not claim mechanical parity.

Campaign: four authored hero loadouts, three acts with nine encounter stops, twelve foe definitions and three bosses. Each stop offers one or two authored foe choices. Victory requires clearing the final encounter and selecting its reward; defeat ends command acceptance. Starting resources, decks, enemy intents and reward pools come from campaign.json.

Combat refills three energy and draws five cards each turn, up to a ten-card hand. Cards compose damage, block, draw, poison, weak, strength, heal and energy effects. Played cards reach discard after their effects. Enemy poison resolves before enemy intent and bypasses block; lethal poison prevents retaliation. Player poison resolves after enemy intent. Poison decays by one. Weak subtracts three from enemy attack damage and decays each enemy turn. Strength lasts one battle. Enemy guard lasts through the next player turn; player block resets at the next player turn.

Enemy intent patterns cycle through attack, guard, charge and poison. Equipment is unique per run, purchased on the map, and contributes through required card/hero tags. Cinders also buy a 15-cinder rest once per stop or 25-cinder card removal down to the minimum hand size. Three flasks heal 20 each by default. Rewards offer three deterministic random unique cards or configured healing. The tag registry records Id, Domain and Family; it does not implement every query in the original status DSL.

Campaign draw/discard/hand/RNG, hero, route, equipment, statuses and reward choices are saved in a checksummed per-channel record with a previous-record backup. Original JavaScript and Expedition.v1 saves are preserved; campaign saves use a separate key. Development browser output is a conventional static-hostable Unity export. Read-only diagnostics and the component gallery are limited to editor/local/dev; motion and sound preferences are available everywhere.

Acceptance: all four classes can complete seeded campaigns; model tests cover illegal commands, rewards, equipment queries, poison order and save determinism; the packaged browser completes all nine encounters using pointer commands, purchases gear and resumes exactly; Web/Windows/Android artifacts report their actual validation; CSV import rejects invalid data before source replacement; screenshots show current output. Physical-device and iOS coverage must be reported separately. Broader architecture and delivery intentions remain in Unity-Build-Brief.md.
