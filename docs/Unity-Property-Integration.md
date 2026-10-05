# Mounted properties in AshenedSpire

This build-51 increment implements the held-carrier and pre-foundation party
dispatch rules inspected in original commit `40c8a45fe951de4f757b2add0811aeeeb7f41fe1`.
Published test 898 supplies the frozen property definitions for bounded tests.
It does not complete current combat foundations or activate the new content.

- [x] Derive properties from worn armaments/armour, owned relics, the equipped
  class and its own chosen tree nodes, travelling companions, worn slotted
  sigils and attuned legendary sigils.
  - [x] Sorted carrier keys, authored requires/excludes and explicit malformed-table refusals.
  - [x] Freeze carrier input in native saves; rederive mounts from the run's frozen content.
  - [x] Equipment swaps update carrier input transactionally; once/per-turn gates survive remounts.
- [x] Execute owned reactions in solo and two-to-four-player combat.
  - [x] Shared bounded FIFO preserves the owning interpreter's piles, metadata and resources.
  - [x] Heal events identify the recipient before scanning living, connected party seats.
  - [x] Per-seat skills/gates and a shared generated-card counter survive exact restore/rejoin.
  - [x] Ordinary-hit Overcharge, Siphon/Resonance, positive-event predicates,
    integer skill/class-level gates and authored/derived card-tag predicates.
  - [x] Absent `mechanics.properties.mounted` retains legacy queues and receipts.
- [x] Local source validation.
  - [x] 109 focused checks, including actual property definitions, controlled queue
    scenarios, equipment transactions, corrupt content and invalid-command rollback.
  - [x] 41,156 migration checks.
  - [x] All 109 property checks repeated against Unity's compiled domain assembly.
    Receipt: `TestResults/HtmlParity/build51/compiled-property-checks.json`.
  - [x] Zero healing leaves positive-heal gates available; new-seat draw reactions
    drain before native command/save completion.
  - [x] 6,587 frozen legacy co-op checks after the shared-queue change.
  - [x] Runtime reference compilation: 169 files; three documented Unity-6 API gaps
    in the older NuGet reference remain accepted, not new compile failures.
- [x] Compile/export build 51 from source `b7cf094` with Unity.
  - [x] Source digest `1e3c089ddc4146c2ee0c1370b474b3b6f5d436ad41d7eae350e3cf7dab4e375f`
    and all eight Web payload hashes verified; 18 desktop/portrait solo checks passed.
  - [x] Web archive verified: 120,871,520 bytes, SHA-256
    `9dd59e97ff2f618d1a270efc91efc6001466fc422f918e869c563a4286b7a29a`.
  - [ ] Two-browser co-op acceptance: both build-51 attempts reached the real
    encounter and exact-hand rejoin, then failed card selection. The second
    attempt exposed inspector Back clearing selection. Phone screenshots also
    showed overlapping party resources, enemy targets and lower controls.
    Build 52 addresses these findings; no co-op pass is claimed for build 51.
  - The first build-51 attempt failed because the new partial class had a
    malformed 33-character GUID. The metadata was corrected before retry;
    the failed attempt is not an exported-player validation result.
- [ ] Activate the compatible content and validate visible property behavior in the player.
- [ ] Finish foundation-wide trigger priority/ancestry, status clocks, location
  properties and scoped skill-XP readers before declaring current-rule parity.
- [ ] Full solo/co-op command corpus, physical-device and owner acceptance.

Native frozen-content saves remain an intentional difference from the original's
live definition lookup. Definitions are not duplicated inside combat snapshots.
The new shared queue is selected by the saved properties flag; it does not change
existing legacy fights. Foundation-wide party scheduling is a separate remaining
integration, not implied by the heal dispatch checks above.
