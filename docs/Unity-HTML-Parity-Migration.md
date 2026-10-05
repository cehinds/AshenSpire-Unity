# Published HTML to native Unity migration

Owner scope: migrate the remaining HTML/JS game to Unity in one continuous
implementation, matching how the latest published test build plays and looks.
The owner selected **published test 898**, rather than an editable local build.
On 2026-10-04 the owner replaced the visual target with the supplied Starseer
combat screenshot and `http://127.0.0.1:4175/`. Test 898 remains the gameplay
reference; native appearance work is tracked in `Unity-Owner-Appearance.md`.

Owner scope expansion, 2026-10-04: also implement all relevant upstream changes
already recorded in `Unity-Upstream-Review.md`, through original build 913,
commit `40c8a45fe951de4f757b2add0811aeeeb7f41fe1`. Test 898 remains the frozen
oracle baseline; later changes need their own source comparisons and checks.
Original assets and gameplay are references, not its Markdown governance.

- [ ] Recorded upstream follow-through (build-49 work in progress).
  - [ ] US: refresh unplayed hands under saved rules.
    - [x] Optional `shuffleHand` rule; solo/co-op Retain, Ethereal and ordinary-card handling.
    - [x] Deterministic shuffle receipts, exact mid-turn restore and absent/false legacy behavior.
    - [x] Existing hand-rule regression suite: 172 checks.
    - [x] Saved deck-order mode: stable Innate opening, ordered return, generated-card order,
      no implicit shuffle draws, explicit shuffle effects unchanged, solo/co-op reload.
    - [ ] Activate with the compatible new content bundle and deck/settings UI.
    - [ ] Exported-player gameplay and owner acceptance.
  - [ ] US: one Stamina turn budget under saved rules.
    - [x] Explicit `mechanics.stamina.turnBudget` flag; absent/false retains legacy pools.
    - [x] Atomic card/X payment, resource aliases, bonus energy, recovery and turn refill.
    - [x] Solo/co-op save replay, pending loss, equipment payment/resizing and surplus preservation.
    - [x] Migration suite: 40,544 checks. Legacy co-op corpus after deck-order change: 6,587 checks;
      legacy equipment swaps: 2,063 checks.
    - [ ] Current stat rows/new-run activation, complete status/foundation integration and UI.
      - [x] Bind `restoreStamina` card text tokens, including repeated recovery amounts.
      - [ ] Remove the legacy Catch Breath action from the current-rule HUD before enabling the new bundle.
    - [ ] Exported-player gameplay and owner acceptance.
  - [ ] US: reset device settings safely.
    - [x] Native confirmation, cancel/Escape, return focus and settings-only persistence.
    - [x] Restore prior preferences on failed save; report rollback failures without false success.
    - [x] Settings/mod suite: 101 checks, including partial-write and rollback failures.
    - [x] Build-44 export/receipt; desktop cancellation preserved the customized preference.
    - [x] Fix off-screen focus restoration found by the build-44 browser test.
    - [x] Build-45 desktop/portrait reset, focus, persistence and save-preservation checks:
      22 assertions passed; confirmation/audio/map screenshots inspected at 1280x720 and 390x844.
    - [ ] Owner acceptance.
  - [ ] US: remaining monitored changes.
    - [x] Updated-defaults keep/reset choice, saved acknowledgement and persistence rollback in source.
    - [x] Build 49: both startup choices, acknowledgement and exact saved-climb preservation; 24 desktop/portrait assertions.
    - [ ] Guided rewards, banked level claims and deferred chooser behavior.
    - [ ] Class/book repeat rewards, feats, sigils and service/progression changes.
    - [ ] Deck/sideboard/equipment eligibility, extraction restrictions and lent-card reconciliation.
    - [ ] Current save import, audio/haptics and remaining art/UI parity.
      - [x] Verify v11 pack and three starter paintings by SHA-256 and decoded RGBA pixels;
        stage a repeatable native import without modifying the active export.
      - [x] Import the three paintings and record their upstream overrides separately from test-898 layouts.
      - [x] Inspect build 45: all three cards were reachable, but its active face still used generic art.
      - [x] Route both native face layouts through shared card/profile artwork bindings.
      - [x] Build 48: 14 desktop/portrait interaction checks and visual inspection of the actual paintings.
    - [ ] Review each recorded item against the final compiled player before closing it.

Recorded-change completion map (use the latest finding through build 913 when
an older monitor entry describes superseded defaults):

- [ ] UP-01: settings recovery and promoted-default choices.
  - [x] Reset-all confirmation, focus, cancel, rollback handling and saved-climb preservation.
  - [x] When new defaults are activated, offer keep/reset after profile loading;
    persist acknowledgement transactionally and keep local values on dismissal.
  - [x] Use a defaults revision so ordinary rebuilds do not repeat the prompt; 211 settings/mod checks pass.
  - [x] Build 49: startup keep/defaults, saved climb and reload acknowledgement; 24 browser assertions.
  - [ ] Owner acceptance.
- [ ] UP-02: current combat and hand rules.
  - [x] Optional shared Stamina payment/refill, hand refresh and saved deck order in C#.
  - [ ] Connect current stats, foundation/status effects, authored costs and draws,
    swap rules, current HUD and new-run settings as one compatible bundle.
  - [ ] Preserve Block totals while adding Arcane Ward provenance and solo/co-op displays.
  - [ ] Validate stance selection/cancellation and enemy pile-effect fanout in the exported player.
- [ ] UP-03: XP, claims and reward flow.
  - [x] Pure XP/stat/cost/claim models compared with frozen published-JS oracles.
  - [ ] Fight/quest/skill awards, banked claims, point allocation and saved rule versions.
  - [ ] Source-bonus ledgers and current 10% combat-card/three-choice defaults,
    combat feat bonuses and class-level rewards; preserve older run snapshots.
  - [ ] Deferred choices survive reload, later victories and class changes without rerolls.
  - [ ] Guided claim feedback leaves unlocked choosers available without forcing them open.
- [ ] UP-04: books, class library, feats and card ownership.
  - [ ] Repeatable book reads, quoted choices/rewards and successful-read revision checks.
  - [ ] Class learning/equipping, class cards/core tags/armour and class skill trees.
  - [ ] Deck/sideboard identity allocation, copy limits and equipment eligibility.
  - [ ] Restamping preserves set-aside lent cards; extraction/install restrictions
    and upgraded weapon-art provenance match current source.
- [ ] UP-05: world and persistent services.
  - [ ] Atlas/town journeys, quests, dialogue and reward checkpoints.
  - [ ] Market books and compact offers replace direct card/art purchases only once teaching works.
  - [ ] Blacksmith, master and legendary-sigil ownership/attunement, with persisted stock,
    stale-transaction refusal and zero-chance RNG behavior.
- [ ] UP-06: presentation and local feedback.
  - [x] Native Quick Start and guided creation already have build-43 player evidence.
  - [x] Verify the three new starter paintings in build 48 after fixing the active native renderer.
  - [ ] Remaining service/scene/item art, prologue traveller placement, class-routed
    animation, grounded formations and unobscured short-screen controls.
  - [ ] Haptics settings, supported-platform delivery, local-seat routing and replay deduplication.
- [ ] UP-07: saves and delivery.
  - [ ] Schema-20 import only after the new mechanics/state above are supported;
    preserve refused bytes and avoid silently enabling new non-XP rules.
  - [ ] Final solo/co-op player, download, offline, device and owner acceptance checks.
  - Native packaging remains an intentional platform difference: browser DOM,
    service-worker/build-storage machinery and original Markdown are not transplanted.

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
