# Guided creation, map and card proportions

Owner request: replace the long creation form with one screen per decision;
preselect Reaver, Standard, Straight Sword, shield and the main class relic.
Also fix the nearly empty map and cards stretched across their preview row.

- [ ] Delivered and accepted.
  - [x] Five primary steps: Class, Attributes, Equipment, Relic, Ready.
  - [x] Begin the climb remains available on any step with a valid setup.
  - [x] Default Reaver uses leanStandard and the authored reaverBaseline kit:
    Straight Sword, Round Shield and Forsaken Medallion.
  - [x] Class changes reset incompatible equipment/relic choices; Back retains
    the draft. Existing builder validation still controls starting a run.
  - [x] Optional card previews show one portrait card at a time with Previous/
    Next controls; count is outside the card face.
  - [x] Appearance/custom-climb controls remain in a separate optional screen.
    Its advanced controls use a bounded scroll area with persistent navigation.
  - [x] Card faces retain width:height 2:3 at every width; preview rows no longer
    grow cards to fill spare space. Artwork crops within the illustration area.
  - [x] New map viewers default to Paths; explicitly saved Fog preferences remain.
    The toolbar shows Map options, Choose route and Key; secondary controls hide
    behind Map options. Available nodes get an ENTER label.
  - [x] Export build 43 and verify source/payload receipt.
  - [x] Check desktop and portrait setup steps, defaults, navigation, card ratio,
    route visibility and entering combat in the actual player.
  - [ ] Owner acceptance and physical-device checks.

No existing save or original-game files are modified by the new setup flow.

## Exported-player verification

Build 43, version `0.0.30.12`, local preview `http://127.0.0.1:8907/`.
Unity exited 0; independent source and all eight payload hashes verified:
`a71287d6ca4e0f3e3829cf9c6883ca66a1d03d0e97a13cf9a57d4308baedf4a3`.

- Walked Class → Attributes → Equipment → Relic → Ready at 1280x720 and
  390x844, without page scrolling. Navigation and Begin stayed visible.
- Confirmed default kit and main relic; Back from card preview returned to
  equipment with the draft preserved. Previous/Next card paging retained ratio.
- Assign points displayed three unspent points and disabled Next/Begin;
  selecting Standard restored the ready preset.
- Ready started a Reaver run with HP49, mana1, stamina2. Map showed connected
  future choices and the available entrance. Save/Continue retained the run.
- Entered the soldier fight from the map. Selecting Shield Defend spent nothing;
  second click changed actions3→2, Block0→11, discard0→1.
- No browser error entries observed. Screenshots live under
  `docs/qa/unity-guided-creation/`. Physical-device input and owner acceptance
  remain separate; the advanced custom-climb form still has its bounded scroll.
