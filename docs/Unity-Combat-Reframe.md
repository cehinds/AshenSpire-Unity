# Combat reframe — 2026-10-04

The owner rejected build 40's combat presentation and asked for the reference's
interaction, clearer UI, a hilt correction, generated artwork where needed, and
real Unity 2D sprites/prefabs. Appearance and interaction reference:
`http://127.0.0.1:4175/` (`D:/repos/TheAshenedSpire`, read-only). Published test
898 remains the broader rules-migration target. The reference's simpler energy
economy is not silently substituted for existing saves' action/mana/stamina rules.

- [ ] Combat reframe delivered and accepted.
  - [x] Inventory existing reference art: courtyard, four heroes, three enemies,
    three card illustrations and Cormorant typography remain available.
  - [x] Generate a soldier hilt repair and a matching ember card back.
  - [x] Preserve original art; save replacement assets in `Art/CombatRefresh`.
  - [x] Author seven SpriteRenderer prefabs with real imported Sprite assets.
  - [x] Bind the prefab artwork to combat portraits; cameras render on changes,
    with render targets and instances released when their UI leaves the panel.
  - [x] Move secondary tools into a menu; show action count, draw, discard and
    End Turn around the hand.
  - [x] Fit the hand to available width; selected cards lift out of the fan.
  - [x] Match reference select-then-click-again behavior through native commands.
  - [x] Lower the hero scale and move enemies back so health stays above the hand.
  - [x] Compile/export and inspect the new player at desktop and portrait sizes.
    - [x] Unity compiled the new runtime and Editor scripts.
    - [x] Editor authored all seven prefab/sprite references and validated eleven
      unchanged reference texture dimensions.
    - [x] Existing rules/model comparison suite: 40,399 checks passed.
    - [x] Web export, receipt and actual player verification.
  - [x] Verify select/play, target selection, menu, draw pile, turn and save/reload.
  - [ ] Verify drag/flick with reliable input; physical devices remain separate.
  - [ ] Owner acceptance.

The reference was exercised using a separate localhost origin: selecting Shield
Defend spent nothing, clicking it again changed Block 0 to 7, actions 4 to 3,
and discard 0 to 1. Native verification must demonstrate the same interaction
contract using the native card's actual costs and values.

Build 41 compiled and exported successfully with eight verified payload files.
Its browser playtest confirmed Arcane Ward selects without payment and a second
click plays once: actions 3 to 2, Block 0 to 5, discard 0 to 1. Reload/Continue
preserved that state. Both desktop and 390x844 portrait rendered the real painted
prefabs. That inspection found enemy labels behind the desktop hand, overlapping
card descriptions and clipped menu controls. Build 42 addresses these with a
higher enemy baseline, width-aware cards, selected-card front ordering, a vertical
scrolling menu and visible Block/cinders. Build 42 uses Unity's short build-time
Web optimization for this local visual preview; release performance is not
certified by it. The previous optimization setting is restored after export.

## Build 42 exported-player evidence

- Version `0.0.30.11`, source digest
  `9233bde2e1ac87232fa66e82a3a34b10571cbbe0f7dde606a8be6585bdd19c0e`.
- Export `Builds/OwnerAppearance/build42/Web`; local preview
  `http://127.0.0.1:8906/`. Unity batch process exited 0; independent verifier
  matched source, version and all eight payload hashes.
- Fresh Starseer, seed ASHEN: Arcane Ward selection retained actions 3, Block 0,
  discard 0. Second click changed those to 2, 5, 1 exactly once.
- Upward drag of Starstone Pebble onto the soldier changed actions 2 to 1,
  mana 3 to 2, discard 1 to 2 and enemy HP 25 to 9.
- End Turn advanced to turn 2, refreshed actions to 3 and displayed attack 9.
  Draw-pile inspection showed two cards and returned to combat.
- Menu scrolling reached Azure and Save and return to title.
- Reload/Continue at 390x844 retained HP 34, mana 2, enemy HP 9, actions 3,
  draw 2 and discard 2. Selected Staff Channel appeared fully in front of the fan.
- No observed browser error entries. Desktop, menu and selected portrait images
  are saved in `docs/qa/unity-combat-reframe/`.
- Upward mouse drag is verified. Flick velocity, touch hardware, controllers,
  multiplayer and multi-enemy layouts have not been revalidated in this pass.
- Existing full-page inspection screens still need the reference visual language;
  long Dodge Roll text overflows its compact draw-pile tile. These are not covered
  by the combat hand fix and remain open polish work.

## Art provenance

Mode: built-in `image_gen`, transparent background requested; both saved outputs
are RGBA with nontrivial alpha. No original source artwork was overwritten.

- `Unity/Assets/AshenSpire/Resources/Art/CombatRefresh/wanderingSoldier-hilt-v2.png`
  — 1341x1173; edited from the imported reference soldier.
- `Unity/Assets/AshenSpire/Resources/Art/CombatRefresh/ember-card-back.png`
  — 1024x1536; new painted card-back artwork used in pile controls.
- `Unity/Assets/AshenSpire/Resources/Combatants2D/*.prefab` — authored by
  `CombatSpriteAuthoring.Build`, with custom art on SpriteRenderer components.

Hilt prompt:

> Use case: precise-object-edit. Final Unity 2D character sprite. Repair only the sword hilt and the gauntleted hand holding it in this exact full-body armored soldier cutout. Make a physically coherent straight sword: blade points down toward the lower left as now; a single crossguard is perpendicular to the blade at its base, one leather grip passes naturally through the closed gauntlet, a small pommel behind the fist. Remove any bent, dangling or duplicate hilt shapes. Preserve the same soldier identity, weathered iron armor, helmet, shield, pose, silhouette, sword blade direction, colors and painted detail. Keep the entire character and sword visible with transparent background and no ground, scenery, labels or shadow rectangle. Match original proportions and framing. Do not redesign armor or add anything.

Card-back prompt:

> Use case: stylized-concept. Asset type: final Unity 2D card-back sprite for a dark fantasy deckbuilding game. A single tall rectangular playing-card back, front-on orthographic, isolated with true transparent background outside the card. Painted, weathered black leather and soot-dark iron, fine antique gold double border, restrained ember-orange light from one small sacred flame emblem centered inside an engraved circular sun wheel. Rich tangible texture, elegant readable silhouette at small size, subtle asymmetrical wear. Entire card visible with a narrow transparent margin. Aspect ratio of the card itself 2:3. No text, letters, numbers, UI, hands, scenery, extra objects, drop shadow outside the card, mockup or perspective. This will be actual game art, not a screenshot.

## Further recommendations

Keep the hand and targets central. Keep equipment, exhaustive status explanations
and keyboard help behind explicit controls. Expand card-specific illustrations
from the existing content catalog before commissioning unrelated decoration.
Use short attack/recoil animations on the 2D prefab layer after the input and
layout are accepted; rigged sprite meshes can be introduced where deformation
improves motion. Do not deform a whole painted body as though it were a weapon rig.

Remaining whole-game work includes the current content/rule migration, title/map/
reward/service styling, co-op-specific reframe and device/controller acceptance.
