# AshenSpire Unity roadmap

Build the original AshenSpire in Unity, preserving its painterly identity and
content while improving phone play. Current source: **0.0.10.0 · build 10 ·
Foundation in progress**. Published channels may still carry earlier checkpoints.

Checked items describe implemented functionality with focused evidence. Unchecked
items are acceptance work, limitations or remaining scope; checkmarks do not certify
every interaction or platform. See [Unity-Parity.md](Unity-Parity.md) for receipts.

## Implemented foundation

- [x] Pin original dev `b17a7f4543e1710f49fae8b58880121690a314de` and retain original import/oracle hashes.
- [x] Import original cards, classes, equipment, enemies, relics, events and rules.
- [x] Keep simulation in engine-independent C# components with tag queries,
  .NET names and maintenance comments; keep Unity input/rendering separate.
- [x] Support original JSON/CSV tables with reference validation, stale-edit
  refusal, atomic replacement and exact source backups.
- [x] Implement original RNG, formulas, derived stats, statuses and seeded maps.
- [x] Default to Assign points: five attributes at 5, 60 total, 35 unspent;
  retain Standard and remove Tuned from the fork's available modes.
- [x] Make every attribute point useful and author five-point mechanical thresholds.
- [x] Apply discovered starting kits, alternate hands, wardrobe/relic choices and
  saved kit identity; retain the uncustomized baseline kit's creation-only waiver.
- [x] Compose weapon-owned cards and pay final action/MP/stamina costs.
- [x] Resolve combat turns, piles, targets, enemy AI, triggers, dodge/poise,
  statuses, flasks and the authored Catch Breath action.
- [x] Traverse three acts, bosses, unknown rooms, events, rewards, shops and shrines.
- [x] Track owned items and stable cards; equip unlocked sets, smith, extract and
  install supported mounts while preserving resource deficits.
- [x] Pay for solo combat set changes with configured resource/turn costs.
- [x] Purchase shrine level points and resell eligible relics/utility flasks.
- [x] Save content, room/combat state, RNG and creation choices; validate recovery.
- [x] Persist profile history, unlocks and discovery receipts with native consumers.
- [x] Bind original numbered card-text tokens; present safe single-hit bonuses
  correctly and drive combat feedback from actual result receipts.
- [x] Keep per-channel latest links and immutable previous-build pages with
  build/version/date/source/PR metadata and concise changes.

## Implemented original modes and multiplayer

- [x] Custom Climb with six Ascension difficulty rules and authored chaos modifiers.
- [x] Standard, Sealed and three-round Draft starts; save offers before selection.
- [x] Keepsakes, name/tint/sigil identity and original custom progression classification.
- [x] Endless cycles beyond Act 3, with authored enemy growth and exact restoration.
- [x] Native authoritative co-op combat with 2–4-seat original differential fixtures.
- [x] Shared route votes, private rewards, shops/shrines/events, catch-up and
  actual cooperative combat; connected, disconnected and downed seat handling.
- [x] Authenticated companion commands, duplicate protection, host persistence,
  late join/rejoin and member-specific snapshots.
- [x] Co-op deck/equipment/sets and supported mount service controls.

## Finish foundation acceptance

- [ ] Complete current-source compiled solo playthrough and profile finish/reload/
  unlock/creation loop, including choices, shopping, equipment and services.
- [ ] Complete current-source compiled Custom/Sealed/Draft/Endless interaction and
  save/resume checks. Headless victories are recorded separately.
- [ ] Complete current-source multi-browser co-op play, friendly targets, catch-up,
  host restart, duplicate retry and rejoin checks using the packaged companion.
- [ ] Resolve explicit scope gaps: debug custom map-shape overrides; co-op
  in-combat set changes; any original settings/inventory flows not covered by the
  current commands. Co-op's original Endless option does not imply all solo custom rules.
- [ ] Verify full original appearance render paths, armour/tint poses and selected
  style behavior. Tint/sigil identity is integrated; broader rendering is active work.
- [ ] Check actual card numbers, target availability, affordability, rejection
  recovery and result feedback throughout phone and desktop flows.
- [ ] Finish field/schema/runtime authoring coverage. The native CSV demonstration
  already adds cards, equipment, enemies and encounters and executes real combat.
- [ ] Rerun native storage recovery and real background/return on the final source;
  add profile corruption, quota exhaustion and upgrade coverage.
- [ ] Decide original JavaScript save import explicitly. Existing legacy saves are
  preserved; importing them into a native run is not implemented.
- [ ] Validate source-matched Web, Windows, Android and companion artifacts, hosted
  build/history links, downloadable folders and current screenshots.
- [ ] Record remaining defects and owner acceptance before selecting `0.1.0.0`.
  Native co-op and broader original parity have not been waived.

## Polish and completed-game readiness

- [ ] Compare original and Unity screens side by side and retain the original aesthetic.
- [ ] Finish sprite animation, transitions, status feedback, sound/music and settings.
- [ ] Make every required original flow comfortable on touch with consistent UI.
- [ ] Test physical Android hardware and graphical Windows play; establish the iOS
  build/device path separately from browser phone emulation.
- [ ] Measure startup, download size, memory and frame-time budgets on target phones.
- [ ] Playtest class pacing, stamina/MP decisions, fairness and fun with players.
  Automated victories alone do not establish balance or resource usefulness.
- [ ] Complete regression and owner-selected test/release/main promotions.
  `1.0.0.0` remains reserved for the completed game.

## Evidence boundary

Original differential fixtures, native headless policies, browser input/screenshots,
host transport tests and physical devices are separate evidence categories. Every
compiled receipt must identify its build source. Earlier adaptation playthroughs
and screenshots remain valid only for their historical checkpoints.

The normal solo policy receipt records 12 victories, 264 fights, 3,450 accepted
commands and 858 save/resume comparisons at source digest
`3363adc7b3f7e9971513dc7cde6df24b5207aa8572f27ae7ffd91a43d374521c`.
Separate custom policies completed Sealed and Draft and reached the first scaled
Act 4 fight in Endless. A normal-health two-player co-op policy completed three
acts in 359 commands with nine exact resumes. These are simulation evidence,
not owner acceptance, current browser proof or device certification.

Issue: [#33](https://github.com/cehinds/AshenSpire-Unity/issues/33).
