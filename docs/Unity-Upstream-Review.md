# Original-game checks for AshenedSpire

AshenedSpire is the Unity adaptation. The original game remains AshenSpire at
`https://github.com/cehinds/AshenSpire`. Display branding may differ; existing
Unity namespaces, save keys, company/product storage identity, Android package
ID and archive filenames remain stable to preserve installed saves and links.

## Latest inspected reference

- Checked: 2026-09-28 (America/Anchorage).
- Original dev HEAD: `a8e2def5b52e3381ea0f9e56f721881b78130b04` (2026-09-29 06:15 UTC, still September 28 in Anchorage).
- Imported baseline: `b17a7f4543e1710f49fae8b58880121690a314de`.
- Scope: prior review plus 42 intervening commit summaries and the explicit state/skill changes in `2d48b91480`; not a complete audit of every change since the imported baseline.
- Daily check: `check-ashenspire-changes-for-ashenedspire`, attached to the
  development chat. Notify only for meaningful relevant changes or problems.

## Findings

- [x] Compared the original again while preparing the build-28 importer.
- [ ] Review XP rates and deferred/manual level-up UX (`368af4d0b6`, `1fe83cce6a`, `b1c34968b0`).
- [ ] Review configurable feat rewards, banked skill XP and claim state (`2d48b91480`, merged by `a8e2def5b5`). The source adds `feats`, `pendingReward.skillClaims` and `levelChoices`; the existing original schema is already beyond the baseline schema 5. These need explicit content/rule/save migration work, not a blind copy.
- [ ] Compare tag-routed physical/bow/spell animation (`0e3482e376`, `7e5fbc73ed`) and tiny-enemy/intent tap clearance (`4cc1dfc6be`, `379c5a2f3d`, `234bfe3b95`) against Unity's existing feedback and targeting.

Build 28's initial original-save importer covers baseline schema-5 map checkpoints,
not the latest original's expanded skill/zone/progression schema. Newer saves are
refused with their original bytes preserved. This is an explicit remaining
Foundation import gap; no import baseline or balance rule was changed by this review.

The original's short-phone fix (`37ed6bf7e`) gives a truncated-card reader an
exposed tap target above a fanned hand and improves reachability checks. Unity
uses a different hand layout with vertical description scrolling. Carry over
the acceptance principle: the complete description must be readable and Play /
End turn must remain reachable. Build 26 passed both narrow-phone and desktop
reading checks. Build 27 repeats both layouts successfully (32 checks).

Recent original changes also include more skill-draft cards, ten class abilities
per class, developer-tool visibility, and Settings/fullscreen/save-load test
repairs. Inspect the authored skill/ability data and mechanics before proposing
a Unity import. These are not included merely because their commit titles were
reviewed. Broad reimport would overwrite intentional Unity rules and must not
be treated as a routine update.

The original's older AI disclosure says no recorded audio ships. That statement
does not describe this Unity adaptation, which imports ten music files. The new
in-game About screen acknowledges AI-assisted work and describes recorded music
and synthesized cues; final release wording still needs owner review.

## Next comparison

Compare new dev commits after the inspected HEAD. Record player-facing changes,
their exact source, existing Unity equivalents and proposed port work. Preserve
the pinned import receipt until a deliberate, tested content migration occurs.
