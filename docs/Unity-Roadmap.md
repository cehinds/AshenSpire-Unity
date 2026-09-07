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
- [x] Original Custom Climb run shape: floor/column caps, relative node weights,
  per-act validation, isolated 24-seed density estimates and frozen save limits.
  The pinned original has no separate practice mode to port.
- [x] Distinct Animated, Rendered, Classic and Sigil presentation paths, including
  original armour/tint poses and saved appearance in solo/co-op views.
- [x] Native authoritative co-op combat with 2–4-seat original differential fixtures.
- [x] Shared route votes, private rewards, shops/shrines/events, catch-up and
  actual cooperative combat; connected, disconnected and downed seat handling.
- [x] Authenticated companion commands, duplicate protection, host persistence,
  late join/rejoin and member-specific snapshots.
- [x] Co-op deck/equipment/sets and supported mount service controls.

## Finish foundation acceptance

- [x] Complete the current-source scripted native browser run: 657 checks, 280
  commands, 22 fights, three acts, two reloads and Chronicle checks.
- [ ] Complete owner acceptance and the broader profile unlock/new-creation,
  shopping, equipment and service interaction matrix.
- [ ] Complete current-source compiled Custom/Sealed/Draft/Endless interaction and
  save/resume checks. Headless victories are recorded separately.
- [x] Pass 16 compiled Draft/shrine/flask/merchant/reload checks, including CON
  raising HP from 64 to 66 and actual purchase/resale transactions.
- [ ] Complete current-source multi-browser co-op play, friendly targets, catch-up,
  host restart, duplicate retry and rejoin coverage using the packaged companion.
  A real browser fight/reward/exact-hand rejoin and 22 packaged restart checks
  already passed; these do not cover every multiplayer interaction.
- [x] Pass 60 phone/desktop map-shape control, actual-combat and exact-reload checks.
- [ ] Resolve co-op in-combat set changes plus original settings/inventory flows not covered by the
  current commands. Co-op's original Endless option does not imply all solo custom rules.
- [x] Pass 44 actual appearance choice/attack/feedback/reload checks, 11 each for
  Animated, Rendered, Classic and Sigil.
- [ ] Finish broader armour/tint visual acceptance and co-op pose feedback.
- [ ] Check actual card numbers, target availability, affordability, rejection
  recovery and result feedback throughout phone and desktop flows.
- [ ] Finish field/schema/runtime authoring coverage. The native CSV demonstration
  already adds cards, equipment, enemies and encounters and executes real combat.
- [x] Pass eight real raw-CDP background/freeze/return checks on the final source.
- [x] Pass 50 final storage checks: 12 served-file hashes and 38 storage checks,
  exact backup recovery and preservation of 729,414 damaged bytes.
- [ ] Complete profile corruption, quota exhaustion and upgrade coverage.
- [ ] Decide original JavaScript save import explicitly. Existing legacy saves are
  preserved; importing them into a native run is not implemented.
- [x] Build matching-source Web, Windows, Android and companion packages; pass
  433 companion, 160 Windows/APK and 18 root-package checks, plus 22 self-contained restart checks.
- [ ] Verify hosted build/history links, downloadable folders and current screenshots.
  Packaging does not establish graphical Windows or physical phone acceptance.
- [ ] Record remaining defects and owner acceptance before selecting `0.1.0.0`.
  Native co-op and broader original parity have not been waived.

## Polish and completed-game readiness

- [ ] Compare original and Unity screens side by side and retain the original aesthetic.
  The native map exposes reachable route buttons; full original graph presentation
  remains visual parity work despite working shape/generation controls.
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

The matched local build checkpoint is source commit
`890af027be07a5648119521165aaeadf2dc5e938`, build digest
`62aa53dcfe5f399be01efd6ed697b43a6aaf09c544950a90337641ba5101032b`. Its Web, Windows, Android
and companion packages passed matching-source checks. Publication is separate.

The normal solo policy receipt records 12 victories, 264 fights, 3,450 accepted
commands and 858 save/resume comparisons in
`TestResults/NativePolicyFinal/results.json`, runtime-source digest
`39286ae6213e53c5a6d657a0cd571bed04617ca73692f58e760cc5874cc883ea`.
This runtime subset digest differs from the full Unity build source digest.
Separate custom policies completed Sealed and Draft and reached the first scaled
Act 4 fight in Endless. A normal-health two-player co-op policy completed three
acts in 359 commands with nine exact resumes. These are simulation evidence,
not owner acceptance or device certification. Separately, the final native solo
browser, map-shape and raw-CDP interruption checks passed as recorded above.
Final appearance passed 44 checks, custom-feature interactions passed 16 and
served-file/storage recovery passed 50 on the unchanged full build source digest
recorded above. These bounded receipts do not close the remaining graph, co-op
animation, balance, profile/quota, JavaScript-save import or owner/device gaps.
No physical phone, graphical Windows, iOS or audible sound acceptance is recorded.

Issue: [#33](https://github.com/cehinds/AshenSpire-Unity/issues/33).
