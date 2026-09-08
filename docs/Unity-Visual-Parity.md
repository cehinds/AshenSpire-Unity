# Original visual presentation

Build 14 (`0.0.14.0`) restores the visual language of the pinned original
`b17a7f4543e1710f49fae8b58880121690a314de`. Visual acceptance is separate from
the existing domain/parity tests. Build 13 is preserved in Git with its exact
players and evidence; the foundation milestone remains incomplete.

## Reference contract

- Warm near-black/brown surfaces, parchment type and thin gold rules.
- Centered ASHEN SPIRE serif wordmark, diamond ornaments and understated menu rows.
- Compact resource HUD, simultaneous combatants, anchored intent/health feedback,
  a large battlefield and horizontally navigable framed cards.
- Cards retain the original authored glyph, type tint, tags and cost medallions.
- Character creation uses class choices beside a framed character/resource preview
  on desktop and a phone layout with reachable controls.
- Keep the approved painted enemy sprites. Do not restore the old low-poly enemies.

The original reference itself has defects, including a clipped phone wordmark in
this pinned version. Match its composition while fixing those defects. Native
Continue, New, Collection, cooperative play and Settings keep their implemented
callbacks. Extras retains earlier campaign and developer tools. Load-slot and
Quit menu semantics are not invented where no equivalent native feature exists.

## Editing

`OriginalTheme.uss` owns the shared palette and title styling;
`OriginalTitlePanel.cs` owns title anatomy. `OriginalCardView.cs` binds resolved
card data and `OriginalCards.uss` owns the card frame. Gameplay rules, resource
payment, save formats and network authority stay with their existing components.

Fonts live in `Resources/Fonts` with their redistribution licenses. The original
CSS names Cinzel and falls back to locally installed serif fonts; the reference
has no bundled font files. This build bundles Cinzel explicitly for consistent
cross-platform display. A licensed static Noto font set and `GlyphFonts.json`
select fonts for the 110 authored card icons and 25 tag glyphs. The source font
coverage audit finds no uncovered strings. Cmap coverage does not prove every
glyph has been rendered in Unity; compiled screenshots are separate evidence.

## Validation status

Runtime compilation, rendered comparisons and gameplay regression checks for
build 14 are pending. Do not reuse build 13's green receipts as build 14 proof.
Full screenshot equality, human balance acceptance and physical-device testing
remain outside the current evidence.
