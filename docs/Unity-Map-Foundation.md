# Original map foundation — 0.0.12.0 candidate

This checkpoint restores the original branching map as a shared Unity UI component.
It is foundation work; it does not promote the game to `0.1.0.0`.

## Player behavior

- Solo starts with fog. Entrances, the boss, your trail and branches already seen
  remain visible. Reaching a shrine reveals the nearest forward shrine.
- Paths shows the full graph. Switching this local display preference does not
  change room rolls, route legality, the run, or RNG.
- Sealstone reveals the kind of already-visible unknown rooms when their stored
  outcome is a fight, shrine or treasure. An actual event remains unknown.
- Teal guidance follows the nearest shrine only along already-visible edges.
  Players can turn this guidance off.
- Co-op uses the original full-path view, shared legal choices and party votes.
  Camera position and zoom stay local to each player.
- Fit, zoom steps, recenter and vertical navigation use the original camera rules.
  Routes provides a named, legal-choice list when a wide decision cannot fit.
- The map has its own bounded viewport. Its controls stay outside the scrolling
  graph. Map key and route list are dismissible overlays.

## Editing the map

Edit `GameContent/Unity/Original/map-presentation.json` to change room names,
descriptions, colors and icon bindings. Its `tapPixels` is authored at reference
zoom 1.15. The minimum 51 preserves at least 44 display pixels at zoom 1, independent
of device pixel ratio. The build validates the table and imports it into Resources.
Do not hand-edit generated `Unity/Assets/AshenSpire/Resources/Original` JSON.

The icon vocabulary is `swords`, `skull`, `eye`, `flame`, `scales`, `chest`, and
`question`. These small map symbols use deterministic UI paths so they display
consistently without relying on an operating system emoji font. Enemy artwork
continues to use the painted sprite bindings from build 11.

Graph and gameplay changes belong to the original content tables and domain
components. The presentation table cannot grant routes or change encounters.

## Code ownership

- `OriginalMapKnowledge` derives the visible graph and room readings from the
  existing path. It introduces no second saved seen-set and returns no hidden
  room-resolution payloads.
- `OriginalMapViewport` is pure geometry, framing and camera restoration.
- `OriginalMapBoard` renders the projections and handles local input. Its one
  gameplay callback delegates to the existing solo command or co-op vote.
- `OriginalMapViewServices` supplies explicit local preference and display callbacks.
- `RunController.Map` stores two bounded camera slots in profile settings, flushes
  on normal save/lifecycle events and keeps camera failures separate from run saves.

## Validation

The new pure components compare against the pinned original at
`b17a7f4543e1710f49fae8b58880121690a314de`. Their committed fixtures execute the
original JavaScript knowledge and camera functions; CI consumes those fixtures.
Regeneration is a deliberate command requiring the exact original checkout.

- Knowledge: 13,948 assertions, including 1,824 projected graphs and 505 shrine searches.
- Viewport: 74,628 assertions, including 3,024 original camera comparisons and
  120 tap-geometry cases.
- Browser acceptance: `tools/native-map-playtest.cjs` uses actual pointer/keyboard
  input at four viewports. It checks displayed bounds, gestures, preference
  restoration and authoritative travel. Read-only diagnostics are chunked to
  avoid Web console truncation.

The corrected Web export compiled with source digest
`d58bf5ccee7e45bd2afc94de693aad5ca872d41cdbfb754de8cf3c9f6637c772`.
The expanded browser suite passed 558 checks and captured 41 screenshots at
320×640, 390×844, 412×915 and 1440×900, plus an earned Sealstone case at 390×844.
It completed an actual opening fight before checking three available routes.
Its original desktop resize screenshot used 115% zoom; a focused rerun explicitly
checks the minimum 100% zoom: 133 checks, 10 screenshots and a measured 45×44 CSS-pixel target. These overlapping suites are not a unique combined count.

Web, Windows and Android exports share the digest above. The current Web player completed all three acts with 657 checks and 280 real gameplay commands, including 22 fights, two reloads and Chronicle. Co-op passed eight host and six guest checks for fight, rewards, rejoin and continued play. All browser suites reported zero runtime errors. The original Node suite passed 136 tests on a clean rerun; an earlier busy-machine gatelist timeout and its successful focused retry remain in the evidence.
Physical Android/iOS and graphical Windows acceptance remain separate.
