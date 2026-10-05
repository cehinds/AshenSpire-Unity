# Published test 898: bounded core playtest

Observed on October 4, 2026 using normal gameplay controls in an agent-created Chrome tab. Reference URL: https://cehinds.github.io/AshenSpire/test/898/. The downloaded published HTML was served unchanged at a new localhost origin, `http://127.0.0.1:8899/`; a small server supplied its pinned public asset packs and objects. Its asset-base sidecar changed only the relative location from `../../` to `./`. No game state injection, screenshot scenario, global viewport override, original-repository write, or existing-save reset was used. The tab and both temporary servers were closed afterwards. Parent's published reference tab was untouched.

The UI visibly reported **BUILD 0.7.1.898 · src 1b60c22e01**. That `src` value is the build identity digest, not the Git commit. The source snapshot selected by the owner is commit `0b85909adc103a915aad5d37af53b4c2c66aa894`, frozen at `D:/repos/.codex/outputs/unity-reference-test898`. Its `buildordinal.json` independently reports ordinal 898, release 0.7.1 and digest 1b60c22e01. Published HTML SHA256: `48264b3eff4ffae7f16bfa79c0f4fa9be2d860f80ea1bd32fa2733b0b0debdde`.

The published edition pins **light** plus **common** content-addressed art indexes (light digest 26a3119a4bd6, common digest 18cb7f56cba7). Screenshots consequently document the published light appearance, not the separately verified high-resolution pack. New local slot 1 was empty before this audit. Seed **QA898CARDS**, character **QA898**, Reaver, Standard attributes, Old Cinder keepsake, Wayfarer Plate, Straight Sword main hand, Round Shield off hand, Forsaken Medallion, Classic Climb.

## Actually observed checklist

- [x] Empty-slot creation, four creation categories and required explicit choices.
- [x] Class, Standard attributes, keepsake, armour, hand equipment, relic and seeded review.
- [x] Five opening scenes through Continue/Set forth; no skip used.
- [x] Initial map, node preview and separate Enter action; subsequent paths revealed as travel progressed.
- [x] Armoury Cards and Character panels, character XP, attributes and collapsed skill progression.
- [x] First normal multi-enemy fight completed in three player turns at 70/70 HP.
- [x] Keyboard selection, selected-card Information, defensive inspector Play, self-target commit, enemy-target click, mixed Stamina/Mana cost, drag-to-enemy, and positional keyboard targeting.
- [x] End Turn and reward-leaving confirmation dialogs.
- [x] Victory XP breakdown and full spoils/progression tray.
- [x] Normal Oracle event: confirmation, two random card upgrades, HP 70→65.
- [x] Reachable Shrine: recovery preview, once-per-visit Rest, owned-item upgrade preview/shortage, disabled extraction/mount services, charge reallocation, unavailable level-up.
- [x] Reachable Merchant: armaments, relics, service shelves, readable contextual inspector and disabled Buy/removal shortage feedback.
- [ ] Card reward offer/claim: this fight explicitly said **No card this time**.
- [ ] Actual character level-up point spending, skill level unlock/draft and mastery reward claiming.
- [ ] Actual paid merchant purchase/sale/removal; stock exceeded this run's 141 cinders.
- [ ] Actual smith upgrade/extraction/card mounting; no Smithing Stone or compatible item available.
- [ ] Long press/flick/right-click inspection, hand reorder, touch input, gamepad, responsive portrait, physical device or full high-art edition.
- [ ] Bosses, later acts, victory/death endings, co-op, resume/import/export and every settings surface.
- [ ] Unity runtime parity or owner acceptance. This is reference evidence only.

## Behavior contracts for migration

| Priority / surface | Actual behavior and migration contract | Frozen source pointer |
|---|---|---|
| P0 resources | Reaver creation showed HP70, MP1, SP3, opening HAND4, DRAW4, HAND SIZE15. Standard attributes STR3/DEX1/CON2/WIS1/INT1. Combat orb explicitly says **Stamina**, card costs use Stamina and optional Mana. Tutorial says Stamina refills every turn. Do not retain older Action/Energy wording in player UI. First fight start SP3/MP1; defensive play SP3→2; Gorefire SP3→2 and MP1→0. Shrine restores persistent Mana. | `src/ui/components/card.js:167` (cost rail); `src/ui/models/StaminaOrbModel.js`; `src/engine/combat.js:462`; `src/ui/screens/customize.js:609` |
| P0 selection/targeting | First shortcut or card click selects/enlarges without spending. Shield Defend makes self a legal target; Guard Counter makes living enemies legal. Clicking an enemy with an armed attack commits it once. Dragging Slashing Strike from hand onto Hound committed SP1→0 and killed its remaining3HP. Keyboard1 selected a card then keyboard1 targeted the sole remaining enemy. This is **positional enemy targeting while armed**, not a universal 'press same card twice' contract. | `src/ui/screens/combat.js:408`, `:1687`, `:1740`, `:1810`, `:1984`; `src/ui/cardDragEnd.js` |
| P0 live card details | Shield Defend face reads Gain9Block in combat although Armoury base face reads Gain3Block. Gorefire reads Deal8/Apply3Bleed in combat versus Deal5 base. Inspection preserves live values, full illustrated face, tag buttons, type explanation, explicit Stamina/Mana explanations, effect glossary buttons, lore and contextual Play. | `src/ui/components/card.js:126`; `src/ui/components/cardInspection.js:227`; `src/ui/screens/combat.js:1454` |
| P0 contextual action safety | Shield Defend inspector Play directly applies non-target defense (SP1, Block9). Gorefire inspector Play closes the panel and arms/focuses a legal enemy **without spending**; subsequent enemy click pays SP1+MP1. Source rechecks active combat, player turn, hand membership and affordability before execution. | `src/ui/screens/combat.js:1473`; `src/ui/components/cardInspection.js:274` |
| P0 progression | Compact VICTORY first showed25XP =21combat-power bonus+2Soldier+2Hound. Continue revealed cinder grant71,141total, TAKEN/1of1claimed; no card offer. Progress bars showed character25/100, Blade25/100, Shield22/100, Reaver mastery5/100. Character and equipment skill progress are separate tracks, not one generic XP bar. | `src/ui/screens/reward.js:683`, `:1086`, `:1115`; `src/ui/screens/equipment.js:1904`, `:1939` |
| P1 creation | Category navigation Class/Character/Equipment/Review; Next unavailable until required choices. Equipment selector advances Armour→Main→Off→Relic. Illustrated card selection glows green and reveals stats/requirements/tags/lore. Sword preview stated4cards/3kinds with counts depending on both hands. Review repeats actual equipment and resources before Begin. | `src/ui/screens/customize.js:204`, `:245`, `:714`, `:1196`, `:1390` |
| P1 turn/reward confirmation | End Turn(E) at SP0 still opened CANNOT BE UNDONE dialog; confirmed turn restored SP3. Full spoils Continue opened leave confirmation even when all loot was already claimed. These were default behavior in this fresh profile. Actual repeated-key behavior must respect state changes/animation locking. | `src/ui/screens/combat.js:2345`; `src/ui/screens/reward.js`; `src/ui/components/confirmationModal.js` |
| P1 Shrine | Rest preview65→70HP/0→1Mana; Rest confirmation says stay until choosing to leave. Afterwards Rest disabled 'already rested here', other services stay available. Round Shield smith preview tier0→1, attack/guard card changes, requirements and cost1Stone vs available0; Upgrade disabled with 'Need1more'. Smith previews distinct sourced basic-card improvements. | `src/ui/screens/rest.js:115`, `:239`, `:314`; `src/ui/components/smithUpgradeModal.js:125`, `:241`, `:303` |
| P1 flask charges | Fixed capacity3, Crimson2/Azure1 initially. Clicking One fewer Crimson immediately changed to Crimson1/Azure2; assigned total remained3. It transfers allocation to the other pool, not an unassigned budget requiring a second click. | `src/model/gracerefill.js:156`; `src/ui/screens/rest.js:483` |
| P1 Merchant | Category shelves ARMAMENTS/RELICS/FLASKS/SERVICES/SELL with availability counts, illustrated offers, Inspect controls, description, price and explicit disabled shortage. Lantern inspector showed equipment face/requirements/tags/stat explanations/weapon art/Smithing tier and disabled Buy262. Buy adds armament inventory, with equip in Armoury per source. No purchase was actually performed. | `src/ui/screens/shop.js:709`, `:800`, `:825`, `:836` |
| P1 overall art/UI | Painted backgrounds, weathered gold frames, illustrated equipment/cards/relics, green selected/action states, parchment effect panel, cost gems and exposed shortcut numbers. Combat has sprite actors, intent chips, HP/Block/status/Poise/Ward reads, central hand and compact bottom orb/piles/potions. Full-screen creator and maps differ markedly from a generic Unity list panel. | `styles/illustrated-cards.css`; `styles/player-polish.css`; `styles/combat.css`; `src/ui/components/equipmentCard.js` |

Source pointers above explain observed behavior; they do not independently prove unplayed paths. For example, holds and hand reorder exist in source but remain unchecked here.

## Evidence index

- `01-title.png`: verified title/build identity.
- `02-creator-stats.png` through `04-creator-review.png`: mandatory choices, illustrated equipment and review.
- `05-opening-1.png`, `06-opening-class.png`: opening art and class-specific prose.
- `07-map-start.png`: initial discovered region; map is initially revealed around one reachable node.
- `08-armoury-cards.png`, `09-armoury-progression.png`: base deck11 and progression.
- `10-combat-tutorial-stamina.png`: tutorial's Stamina rule.
- `11-combat-card-inspection.png`, `12-combat-targeting.png`, `13-combat-mana-inspection.png`: live card details/aim/resource presentation.
- `14-victory-xp.png`, `15-rewards-progression.png`: completed-fight XP and spoils.
- `16-event.png`: Oracle event choices.
- `17-shrine-services.png`, `18-smith-preview-shortage.png`, `19-flask-reallocation.png`: normal service doors.
- `20-merchant-relics.png` through `22-merchant-service-shortage.png`: merchant shelves/contextual inspection/shortage.

All screenshots use the browser's existing desktop viewport; no dimensions were imposed. A tooltip overlays part of screenshot15, but the reward amounts/progress bars are visible. Browser errors/warnings captured at completion are stored in `browser-warnings.json`: no console error returned; one warning reported a stale preview snapshot for Shield Bash after the finishing attack. This audit does not establish absence of all resource/network failures. The isolated server logged a favicon404; visible game art and interactions rendered.
