# Player component art integration

Source: the owner's `player-components-2026-10-03.7z`, extracted under
`D:/repos/.codex/asset-review/player-components-2026-10-04`. The full source kit
was previously verified against all 466 checksum entries. The import selects
44 reusable assets; it does not execute the archive's scripts or import example
game values.

- [x] Import 21 component frames, seven desktop/portrait scene pairs and nine item illustrations.
  - [x] Verify every selected source checksum before conversion.
  - [x] Record output hashes and dimensions in `art/player-components/import-receipt.json`.
  - [x] Preserve painting proportions and source credits.
  - [x] Verify all 23 raster outputs against every decoded RGBA source pixel, including alpha.
- [x] Bind artwork to native Unity UI Toolkit screens.
  - [x] Title and guided creation use the Spire vista.
  - [x] Merchant, shrine, rewards and terminal screens select their scene artwork.
  - [x] Equipment and relic previews show the supplied canonical item illustrations.
  - [x] Engraved controls retain ready, disabled, focus and selection states.
  - [x] Map node shells use the supplied art while existing graph geometry and legal-route controls retain authority.
  - [x] Desktop creation separates the character stage from its current decision; portrait keeps one step and a bottom action tray.
- [x] Keep card faces at 2:3 with their own available illustrated artwork.
  - [x] Use published equipment/card artwork when illustrated; keep painted fallbacks for outline-only records.
  - [x] Derive the containing card height from the face, including border/padding.
  - [x] Keep reward/browser rows from stretching cards.
- [x] Local checks.
  - [x] C# syntax compilation: zero warnings/errors.
  - [x] Runtime reference checker: 167 sources; only three known Unity 6 reference gaps accepted.
  - [x] Card QoL: 192 checks.
  - [x] Map viewport: 74,628 checks across 3,024 camera fixtures and 27 maps.
  - [x] 44 output hashes/dimensions and 15 stylesheet resource links verified.
- [ ] Fresh Unity Web export and desktop/portrait playtest.
- [ ] Full current-game visual/mechanical parity and owner acceptance.
- [ ] All-platform packaging and green full CI.

The first isolated export uncovered an inherited package-cache location on a
missing H: drive. The preview launcher now supplies a process-local D: cache;
it does not change machine-wide Unity settings. Preview output directories now
follow the authoritative build number instead of overwriting historical build43.

## Reimport

`tools/import-player-components.py` requires Pillow and the `@resvg/resvg-js`
module directory passed with `--resvg`. These are authoring tools, not new game
packages. Raster conversion preserves aspect ratio; only scalable frame shells
use nine-slicing in `PlayerComponents.uss`. Character SpriteRenderer prefabs and
the repaired soldier hilt remain the existing game assets.

This work is isolated from the main checkout's concurrent gameplay/art changes.
Those changes and player saves are preserved. An asset import or local domain
test pass does not establish visual parity or exported-player verification.
