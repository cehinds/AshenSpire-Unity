# Original AshenSpire in Unity — build 10

Version **0.0.10.0**, Unity **6000.6.0f1**. The native original climb is playable
from character creation through three acts and progression. This is a foundation
review build, not a completed-game release.

## Try this build

- Create a wanderer. Assign starts all five attributes at 5 with 35 points left;
  Standard uses the original class preset. Every point benefits the character,
  with mechanical thresholds every five points.
- Choose your starting kit, hands, armour, relic, portrait, tint and sigil.
  Equipment supplies cards; inspect action, MP and stamina costs.
- Enter a seed and climb through combat, events, rewards, merchants and shrines.
  Try flask allocation, equipment changes, smithing, card mounts and level points.
- Open Custom climb for Ascension/chaos rules, Sealed/Draft starts, Endless,
  and map floor/column caps with relative room weights.
- Reload during a run and continue. Finish and inspect Chronicle progression.
- For local co-op, follow the included Windows companion's README and generated
  local address. Each player gets private rewards/cards while sharing the climb.

## Downloads and editing

The build page links Web, Windows, Android and the portable Windows companion.
Serve the unzipped Web folder over HTTP; opening index.html directly as a file
is not supported. The companion includes its runtime and content. Keep its
private state and credentials outside the served Web folder.

See docs/Unity-Owner-Guide.md for component ownership and modification points.
Author original JSON/CSV under GameContent/Unity/Original, preserve stable IDs,
validate references, and rebuild with tools/build-unity.ps1. Script headers
describe what to edit. The Pages workflow validates committed players and retains
previous browser builds by channel.

## Verification and remaining work

The current Web player completed 22 fights across three acts through real input:
657 checks and 280 commands, including reloads and Chronicle progression.
Separate current-player checks cover co-op/rejoin, custom maps and actual browser
hiding/freezing/return. See player-checks.json for each completed suite.
All four packages have matching source receipts and checked file hashes.

The map currently shows reachable route buttons; the original full branching
graph remains a visual gap. Co-op animation, broader visual/audio polish, phone
performance and human balance/fun testing remain. Physical Android and graphical
Windows interaction are unverified; iOS is not built. Original JavaScript saves
are preserved separately and cannot yet be imported into native runs. This
checkpoint does not certify full original parity.

Version format is game release · roadmap milestone · incremental upgrade · patch.
Foundation acceptance is 0.1.0.0; the completed game is 1.0.0.0.
