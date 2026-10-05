# Published HTML to native Unity migration

Owner scope: migrate the remaining HTML/JS game to Unity in one continuous
implementation, matching how the latest published test build plays and looks.
The owner selected **published test 898**, rather than an editable local build.
On 2026-10-04 the owner replaced the visual target with the supplied Starseer
combat screenshot and `http://127.0.0.1:4175/`. Test 898 remains the gameplay
reference; native appearance work is tracked in `Unity-Owner-Appearance.md`.

- [x] Freeze the reference identity.
  - [x] Published version `0.7.1.898`, source digest `1b60c22e01`.
  - [x] Commit `0b85909adc103a915aad5d37af53b4c2c66aa894`.
  - [x] Reference URL: https://cehinds.github.io/AshenSpire/test/898/.
  - [x] Read-only source snapshot: `D:/repos/.codex/outputs/unity-reference-test898`.
  - [x] Source archive SHA256 `fd1ff56fdf72442eae9189a54243650f5c1b3c1c556dc031c4429cf559e7d812`.
  - [x] Enumerate 589 JS modules and their source hashes.
  - [x] Validate the current JS content with its own validator.
  - [x] Fetch and verify the reference's `hd-assets-v9` high, light and common packs.
  - [x] Archive the matching build-36 Unity rollback candidate and playtest evidence.
- [ ] Match current rules and content in native C#.
  - [ ] Replace the old 0.5.5 content baseline only after its new behaviors work.
    - Reference/native: cards 219/182, enemies 33/19, encounters 35/21,
      relics 63/55, events 25/22, stances 3/2, tags 272/33.
    - [x] Stage the new content separately; preserve runtime content and saved rules.
    - [ ] Implement and validate new executable opcodes and their callers.
    - Authored node metadata containing an empty `op` is not evidence of a runtime opcode.
  - [ ] Character XP, skill XP, mastery, level claims and point allocation.
    - [x] Port the shared XP step arithmetic to C#.
    - [x] Port banked character/skill ledgers and claim arithmetic to C#.
    - [x] Verify generated JS oracles for levels, caps, claims and standing card upgrades.
      - Current-rule model, stance-choice command and compact-card checks: 40,399 pass,
        including 12,960 stat receipts and all generated card-cost/choice cases.
      - [x] Verify transactional claim reconciliation and refusal without run mutation.
    - [ ] Connect fight/quest payouts, skill award hooks, claims and point allocation.
    - [ ] Verify the level/progression UI in an exported player.
  - [ ] Class library, class swapping, feats, core tree and equipment/card zones.
  - [ ] World journey, town, atlas quests, dialogue, seat order and persistent services.
  - [ ] Market, blacksmith and master stocks; books, companions and sigils.
  - [ ] Current combat costs, choices, foundations, enemy levels and status rules.
    - [x] Port ruleset-7 stat rows, class-specific weights, baselines, limits and layered overrides.
    - [x] Compare 1,080 stat cases and 533 object tag/kind cases against published JS.
    - [x] Add an explicit frozen-atlas location-tag adapter; retain unknown-object refusals.
    - [ ] Activate current stat rows in new-run, combat and save consumers.
    - [x] Port combat-power and cumulative-floor XP receipts; compare 665 enemy-power
      and 384 payout cases, including defeat, missing definitions and XP multipliers.
    - [x] Generate 5,196 current cost, 1,764 stance-choice and four Stamina-alias oracle cases.
    - [x] Port pure current card costs, stance offers/assertions and turn-budget aliases;
      compare all generated JS cases and atomic single-pool payment checks.
    - [ ] Finish current turn-budget integration across combat, effects, equipment and saves.
    - [ ] Verify native stance-choice commands, solo dialog and co-op callers.
  - [ ] Co-op current-rule parity.
    - [x] Port enemy pile-operation fanout for normal and charging moves.
    - [x] Preserve the old routing for saves that freeze the old mechanics.
    - [x] Focused routing and exact mid-turn restore checks pass.
    - [ ] Enable the new routing when the current rules are installed for new runs.
    - [ ] Verify full current solo/co-op JS command/state corpora.
  - [ ] Current HTML schema-20 import with explicit compatible mappings.
  - [ ] Preserve native saved content, rules, slot identity and refused-import bytes.
- [ ] Match current presentation in Unity.
  - [ ] Title, quick start, creation sequence, starting equipment and prologue.
    - [x] Import reference title background/traveler; wire native Quick Start and Run History.
  - [ ] Card art, frames, numbers, targeting, information, equipment packages and animation.
    - [x] Import 219 Card Studio layer documents and cost-layout variants.
    - [x] Import 71 textures with decoded-pixel verification, including 54 rasterized SVGs.
    - [x] Implement native scaled layers, text fitting and beveled artwork clipping.
    - [x] Verify upright imported art, selection, inspection and inspector Play in build 37.
    - [ ] Finish reference text sizing, masking and input parity in the exported player.
    - [ ] Match the owner's newer card typography/layout reference and image sampling.
  - [ ] Combat figures, formations, resource labels, meters and battle transitions.
  - [ ] XP breakdown, banked level claims, spoils and collection behavior.
  - [ ] Character/inventory/deck/class trees, services, world/atlas and settings.
  - [ ] Audio and desktop/portrait/controller interactions.
  - [ ] Compare reference and Unity screenshots at identical viewports.
- [ ] Validate the complete migrated player.
  - [ ] Compile the final Unity source.
    - [x] Intermediate runtime reference compilation: 142 files; three known Unity-6 API gaps accepted.
    - [x] Actual Editor compile identified and repaired native font-override and UV API warnings.
    - [x] Actual Unity Editor compilation and build-37 preview export.
    - [x] Verify build-37 source digest and all eight payload hashes before further edits.
    - [x] Bounded desktop/portrait card play and save/reload checks; no observed console errors.
  - [ ] Export and verify a player whose source matches the implementation receipt.
  - [ ] Play normal creation, multiple fights, rewards, progression and services.
  - [ ] Verify save/reload and two-peer co-op on the final export.
  - [ ] Stage matching platform packages; record payload sizes and hashes.
  - [ ] Physical device/controller acceptance.
  - [ ] Owner acceptance.

The existing Phase 2/3 build-36 results are a verified rollback baseline. Build
37 (`0.0.30.6`) is the first migration preview, with its export in the separate
`Builds/HtmlParity/test898/Web` directory. It still uses the older runtime content;
the complete new bundle is staged, not silently activated. The baseline results
do not certify this larger migration. Source implementation, focused checks,
Unity compilation, exported-player behavior and owner acceptance remain separate.

Reference inventories and generated JS oracles are in
`TestResults/HtmlParity/test898`; regenerate them with the dedicated tools.
Reference browser evidence is in `qa/html-parity-test898`. Original game and
Editor checkouts are read-only during this migration.
