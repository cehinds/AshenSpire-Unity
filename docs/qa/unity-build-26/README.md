# Build 26: interface polish

Version **0.0.26.0**, build **26**. Compiled source commit: `41756f9`.
Source digest: `3e9d219b9a1b375663d13425427b369b0e6cc829f1d6a7bd036dc582785bd44e`.
Web, Windows, Android and the companion are compiled and packaged from matching
source. This is a local review candidate; owner acceptance is still open.

## Changes

The title gives new/resumed climbs a prominent main action and groups secondary
choices. Settings have framed sections and navigation shortcuts. Collection
discoveries and saved climbs use clearer cards and action groups. Combat has a
framed resource display, brighter card text, larger card faces and a compact
utility rail. Creation shares the parchment, forest and brass palette.

Above 125% text size, the title uses full-width rows with natural height and
vertical scrolling. Settings shortcuts align section headings at the top.
Read-only control reports now measure after the scheduled font-size pass.
Game rules, save schemas and co-op commands are unchanged.

## Compiled verification

| Scope | Result |
| --- | --- |
| Menus and large text | 32 passed |
| Opening combat and exact reload | 148 passed |
| Settings persistence and inventory navigation | 17 passed |
| Profile and save-slot recovery | 16 passed |
| Long-card reading | 32 passed |
| Keyboard controls and pile inspection | 74 passed |
| Two-player combat, rewards and rejoin | 14 passed |
| Additional large-text smoke | 4 passed |

**337 browser checks passed** in the final compiled player. Menus were exercised
at 390×844 and 1440×900, including 160% text; combat at 320×640, 390×844 and
1440×900; long-card reading and keyboard controls at 320×640 and 1440×900.
Co-op used two browser contexts and the newly packaged companion, covering a
shared fight, rewards, route voting and exact-hand rejoin. No browser/Unity
errors were recorded in these cases. This does not certify an entire campaign.

Long-card setup uses real defensive play to draw Gorefire Slash from the actual
starting deck, replacing an obsolete opening-hand assumption. The test preserves
its complete-description, stationary-action-row and touch-target assertions.
The settings test uses the actual How to play shortcut before Back, so enlarged
text does not exceed the driver's bounded scroll search. Neither test injects
game state. Final narrow/desktop card screenshots were visually inspected.

All four exports pass **452 companion**, **163 native exported-file** and **20
package checks**. Download copies were verified byte-for-byte by SHA-256.
The version gate passes six checks against build 25 (`01b354c`). The source diff
passes whitespace validation with CRLF allowed for byte-preserved generated
Windows receipts. Changed browser scripts pass JavaScript syntax checks.

Earlier source verification passed Domain, all 13 Parity sections, CardText
(1,500), CardCosts (2,133), HandRules (172), MapKnowledge (13,948), MapViewport
(74,628), 12 review/browser-driver tests and the C# language/reference build
with zero warnings/errors. Runtime reference checks passed 125 checks with the
three documented Unity 6 API gaps. These ran before the final presentation-only
refinements; game-rule sources are unchanged and the final Unity exports compile.

## Evidence and limits

[Validation receipt](validation.json) records scope, case counts, source digest
and download hashes. [Title](screens/title-desktop.png),
[settings](screens/settings-desktop.png), [combat](screens/combat-desktop.png),
[large text](screens/large-text-phone.png) and
[long card on a narrow phone viewport](screens/long-card-phone.png) show the
compiled result. Raw local evidence remains in the task workspace under
`work/evidence/build26-*`; superseded attempts are separate. Co-op credentials
and raw session states are intentionally excluded from this review receipt.

Physical Android testing, graphical Windows play, real-player playtesting,
owner visual acceptance and channel promotion remain open. The branch
`feature/unity-interface-polish` is stacked after local build 25; the existing
successive-version review path remains required. No roadmap feature was marked
Done and no release or owner merge is implied.
