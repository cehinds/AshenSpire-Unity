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

Build 14 has matching Web, Windows and Android exports, plus the portable companion.
Runtime source: `ddc9656ef01f9adf47e80f5787f6ebeec4a174cd`.
Build digest: `2ac07c417b88e2adb899c532940736059d9c2769ee5870879fba91a608c5d2d5`.
Web compiled 2026-09-08 00:08:20 UTC (September 7 locally).

- Actual visual flow: 148 checks across 320x640, 390x844 and 1440x900, 21 screenshots.
- Card readability: 146 checks across those three viewports, 12 screenshots.
- Long-card reading: 30 checks at 320x640 and 1440x900. Real vertical scrolling
  exposes the last description line above Play, without changing gameplay state.
- Full climb: 657 checks, 280 commands, three-act victory and exact reloads.
- Co-op: eight host and six guest checks for combat, rewards, selection and rejoin.
- Portable companion: 22 actual restart/recovery checks.
- Package verification: 442 companion, 160 target-file and 18 package checks.
- Unicode diagnostics: 4,434 C# boundary checks; all final browser error gates passed.
- Source digest: 10 regression tests for canonical path order, text and binary bytes.

Pure domain, original parity, card-text (1,499), card-cost (2,133), map knowledge
(13,948), map camera (74,628) and original Node (136) checks also passed.
See [retained evidence](qa/unity-visual-parity-0.0.14.0/README.md), including
earlier failures and their explanations. These counts belong to this build.

Inventory/rewards, the exact HUD arrangement, fanned-card motion and other screens
still need reference matching. Long descriptions require scrolling inside the
hand. Human balance acceptance, physical Android and graphical Windows play
remain open; iOS is not built. Foundation acceptance is not complete.

Dev and Test remain on build 12 until owner review/promotion. Retaining both
builds 13 and 14 exceeds the current Pages archive capacity even after PR #41's
channel/evidence deduplication. Preserve historical players when expanding hosting;
do not remove builds or increase the existing size gate to claim publication.
