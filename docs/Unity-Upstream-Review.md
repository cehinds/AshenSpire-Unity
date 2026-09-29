# Original-game checks for AshenedSpire

AshenedSpire is the Unity adaptation. The original game remains AshenSpire at
`https://github.com/cehinds/AshenSpire`. Display branding may differ; existing
Unity namespaces, save keys, company/product storage identity, Android package
ID and archive filenames remain stable to preserve installed saves and links.

## Latest inspected reference

- Checked: 2026-09-28 (America/Anchorage).
- Original dev HEAD: `439d18275348f2216d291a196b4fad48b595a1e9`.
- Imported baseline: `b17a7f4543e1710f49fae8b58880121690a314de`.
- Scope: latest 25 commit summaries and the short-phone reachability change;
  this is not a complete audit of every change since the imported baseline.
- Daily check: `check-ashenspire-changes-for-ashenedspire`, attached to the
  development chat. Notify only for meaningful relevant changes or problems.

## Findings

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
