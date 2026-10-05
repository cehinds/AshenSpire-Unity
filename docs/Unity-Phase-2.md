# Phase 2 — gameplay and presentation

Build 36 (`0.0.30.5`) continues the existing native game. Build 31 introduced the
gameplay/settings pass; builds 32–36 add the current-core card QoL pass. Implementation,
exported-player verification and owner acceptance are tracked separately.
Owner review on 2026-10-02 identified missing quality-of-life features, particularly
card play and presentation compared with the current core AshenSpire. The listed
build-33 interactions are implemented and verified in the Web player. Builds 34–35
add status/readability polish alongside [Phase 3](Unity-Phase-3.md); fresh
exported verification is tracked below. Owner acceptance and physical-device/
controller checks remain open.
The 6,280 passing checks below cover specific implemented flows;
they do not establish complete feature or interaction parity.

- [ ] Current-core quality-of-life follow-up
  - [x] Agent played two turns in deployed core build 765 and compared card inspection, target confirmation and presentation. Local package 410 and committed test metadata 793 differ from the deployed player; drag/flick is source-supported but was not played.
  - [x] Implement the confirmed conveniences in the Unity player.
    - [x] Visual card offers in solo Draft, rewards and shops, plus co-op rewards and shops.
    - [x] Readable card inspection with cost, effects, tag explanations and contextual Play/Take/Buy/Select actions.
    - [x] Hand inspection by hold, right-click or an explicit Inspect control.
    - [x] Search, type filters and sorting in solo/co-op decks and solo combat piles; filters survive returning from inspection.
    - [x] Keep the last legal target across solo panel refreshes and co-op card changes; controller Back closes reading pages before leaving the run.
    - [x] Selecting spends nothing; tapping an armed, affordable enemy/self/ally target plays the card. Hand-inspection Back cancels selection.
    - [x] Build 33 source implements upward drag/flick-to-target play and live HP/guard/resource/status previews through copied combat rules; co-op previews are host-generated and refreshed after commands.
    - [x] Co-op Escape closes card reading and cancels selection without disconnecting; controller Back continues to use the same reading-page action.
    - [x] Build 34 shares meter-first status text across player/enemy HUDs,
      enemy inspection and co-op; buildup includes its threshold. Previews and
      contextual Play labels use readable words supported by the current font.
    - [x] Build 35 resolves authored status description numbers and current
      meter thresholds; unknown bindings stay visible, and formatting changes
      neither authored definitions nor combat state.
  - [x] Complete available exported-player verification of the new interactions.
    - [x] Build 32 frozen-source Web export and eight payload hashes verified; 133-file compile check and 461 co-op domain checks passed.
    - [x] Fresh solo player: selection, enemy/self tap, explicit Play, right-click inspection, Escape, deck/pile filters and reward inspection/claim; phone-sized inspection wraps and scrolls. See [bounded build-32 evidence](qa/unity-build-32/README.md).
    - [x] First build-33 export: fresh two-browser co-op UI, three-enemy target memory and dropdown keyboard navigation. The pass identified a co-op Escape gap; final corrected export verification is recorded below.
    - [x] Final corrected build-33 export, eight payload hashes and frozen source verified; reader/affordable-selection Escape, solo/co-op upward drops, peer damage, next-turn recovery, victory/reward offers and retained keyboard pile filter passed. See [final evidence](qa/unity-build-33/README.md).
    - [x] Build 34 runtime compilation: 136 source files; 169 focused domain
      checks include meter precedence, resets, thresholds and preserved state.
    - [x] Fresh build-34 Web player: played Gorefire Slash matched its preview,
      live Bleed meter showed 3/7, and phone-sized preview/action text wrapped.
      Inspection exposed raw description tokens, corrected in build 35.
    - [x] Build 35 runtime compilation: 136 files; 174 focused checks including
      authored description binding and current thresholds.
    - [x] Final build-35 Web player: save reload, resolved status descriptions,
      phone-sized enemy tap matching preview and readable contextual Play action.
      See [build-35 evidence](qa/unity-build-35/README.md).
    - [x] Final build-35 matching Windows/Android/Web/companion candidate;
      all payload hashes and six candidate-metadata checks passed.
    - [x] Final build-35 two-player browser UI against the actual portable
      companion: nine checks cover reader/selection Escape, readable contextual
      actions, host-generated previews, actual card costs and shared 3/7 meter.
  - [ ] Friendly co-op lobby readiness feedback (build 36)
    - [x] Explain the required second wanderer, choosing/disconnected seats and
      stale-view recovery; disable Start until the public roster is ready.
      The companion's authoritative refusal remains intact.
    - [x] Runtime reference compile: 137 files.
    - [x] Focused card QoL: 192 checks, including 18 readiness/notice cases.
    - [x] Fresh frozen Web player preserves the existing combat save; eleven
      two-player lobby checks against a separately packaged matching-version
      preview companion include readiness, disconnection, saved-seat rejoin,
      shared-map start and a 390x844 guest caption. See [build 36](qa/unity-build-36/README.md).
    - [x] Matching Web/Windows/Android/companion candidate archived; six metadata
      checks pass and the packaged companion matches the playtested ZIP exactly.
      This is build-36 evidence, not certification of the larger test-898 migration.
  - [ ] Physical touch hold/flick and physical-controller checks; the CUA drag API does not expose a timed stationary hold.
  - [x] Build 33 source polishes filter contrast, offer alignment and armed enemy colouring; exported verification is tracked in [build 33](qa/unity-build-33/README.md).
  - [ ] Owner review of the resulting player experience.

- [ ] Controls and accessibility (US-3.6, US-13.2–13.4, US-15.1–15.3)
  - [x] Keyboard combat shortcuts and rebinding; controller navigation, action
    bindings, conflict refusal and persisted rebinding.
  - [x] High contrast, colourblind palettes, text/interface size, reduced motion,
    reduced flashes, larger tap targets and optional hold-to-confirm save deletion/overwrite.
  - [x] Fullscreen action, accents, card motifs, control hints and map header options.
  - [x] Build 31 exported Web player: 24 control/accessibility checks across
    phone and desktop, plus 64 menu checks at four viewports. Gamepad input was virtual.
  - [x] Six additional exported-player checks verify that moving UI focus away
    cancels a held deletion and a new short press cannot inherit elapsed hold time.
  - [ ] Owner acceptance and physical controller/device checks.
- [ ] Combat and presentation (US-4.4, US-7.1–7.4, F14)
  - [x] Enemy inspection includes authored status descriptions.
  - [x] Screen transitions, enemy death reactions and receipt-targeted hit feedback.
  - [x] Co-op feedback consumes authoritative receipts once per revision and
    distinguishes local actions from a peer's actions.
  - [x] Domain projection checks: 10,296 checks over 1,263 accepted native commands.
  - [x] Build 31 exported Web player: 156 combat-tool checks, 44 appearance checks
    across four renderers, reward/death/room transition checks and 26 two-player
    fight/rejoin/host-restart checks. The appearance sweep used selected tint/sigil
    values; it did not repeat every cosmetic dropdown combination.
  - [ ] Owner visual and animation acceptance.
- [ ] Rewards and content packs (US-6.1, US-15.2, US-16.4)
  - [x] Optional automatic collection uses the existing seeded reward resolver.
    Manual collection remains the default for existing preferences.
  - [x] Merchant buy-back visibility can be changed without changing prices or
    the inventory. Existing preferences retain visible buy-back offers.
  - [x] Web/Android load bundled packs through the same validated loader as
    desktop. Co-op uses shared base content; saved solo runs retain frozen content.
  - [x] Settings/mod checks: 96 passed, including migration, invalid values,
    conflicting bindings, pack validation and unchanged base content.
  - [x] Build 31 exported Web player: 8 reward/transition checks, 96 draft/shrine/
    merchant checks, plus bundled-pack coverage in the controls suite.
  - [ ] Owner acceptance.
- [ ] Existing Phase 2 game flows (F01–F12)
  - [x] Native title/collection, welcome/guide/About, four classes and appearance
    styles, combat, branching maps, inventory/flasks, services, save slots,
    Chronicle, three acts, Custom/Sealed/Draft/Endless and audio are implemented.
  - [x] Full current rules comparison passed all 13 sections; additional card
    text, card costs, hand rules, map knowledge and map viewport checks passed.
  - [x] Build 31 audio: 32 checks observe actual Web Audio PCM and gain, mute,
    independent buses and exact settings persistence. Listening acceptance remains open.
  - [x] Current-build three-act Custom Climb victory: 542 checks and 220 actual-input
    commands, normal combat rules, exact reloads and Chronicle recording.
  - [x] All three authored bosses rendered and fought; Endless Act 4 map reload
    and next-room entry: 559 checks and 225 actual-input commands. Shorter maps use
    public Custom Climb controls. This does not measure default-map balance.
  - [x] All 22 events and 62 authored choices in the exported player: 4,528
    checks in eight cases. Branches use normal copied save slots; history-gated
    choices follow real Grave/Keeper decisions into the public Endless cycle.
  - [x] Planned four-viewport Web matrix: menus/large text, welcome/guide/About,
    combat inspection, inventory, shrine/merchant and campaign/event screens;
    real defeat/Chronicle actions preserve the completed run across all four sizes.
  - [x] Custom/Sealed/Draft/Endless setup, opening combat and exact reload: 44 checks.
    Ascensions 0–6 and all 11 public modifiers: 65 checks. Full Sealed/Draft
    campaigns, device performance and owner usability acceptance remain separate.
  - [ ] Owner acceptance.

The completed focused, campaign and interruption suites contain **6,280 checks across 48 cases** and
match source/payload digest `a269b8d1b67b99a951a84cec4f35646c4382688eaea9841a4eea5772b2c27f8d`.
See [build 31 evidence](qa/unity-build-31/browser-summary.json). Failed or interrupted attempts
are excluded from that count. Owner acceptance and physical-device checks
remain separate.

## AshenedSpire-Editor use

The [editor](https://github.com/cehinds/AshenedSpire-Editor) is currently an
authoring tool for the original JavaScript game. Its draft/source adapters do
not write Unity assets. `tools/unity-editor-audit.mjs` uses its actual pose
validator and sampler as a timing/accessibility reference: 21 samples passed,
including flash suppression and consistent pose sampling. The receipt records
the source hashes. Neither the original game nor the editor checkout was changed.

Run from this repository:

```powershell
node tools/unity-editor-audit.mjs ../AshenedSpire-Editor TestResults/Phase2/Editor
```

The newer original skill/progression system was reviewed against implementation.
Porting its changed run/save schema is a separate compatibility change; build 31
preserves existing Unity run saves. Editor sampler results are reference evidence,
not proof that Unity or a physical device was playtested.
