# Compiled map browser acceptance

Run against a completed Unity Web export with native diagnostics enabled. Keep
the served export unchanged during the run; the harness compares each viewport's
`build-source.json` with the initial receipt and checks it again before completion.

```powershell
$env:PLAYWRIGHT_MODULE = 'C:/Users/const/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
node tools/native-map-playtest.cjs http://127.0.0.1:8792/ TestResults/NativeMap
```

For a focused desktop rerun, append `--desktop-only` after a **new** output
directory. Its summary identifies the narrowed selection. The resize case waits
for each measured zoom decrease and explicitly requires 100%; a fixed number of
button presses does not prove minimum zoom.

On other systems, use an installed Playwright module and its Chromium browser.
The script uses Edge on Windows and Chromium elsewhere, with software WebGL.

Each independent browser context creates a normal seeded climb through the UI.
It checks fog, paths, legal route controls, measured 44 CSS-pixel targets,
non-overlap, viewport clipping, map overlays (including disabled underlying
nodes), wheel/drag/outside-release cancellation, zoom, Fit/Recenter, deck remount
and local preference persistence. Three emulated phone sizes also send
browser-level touch cancellation followed by a fresh touch that enters a room.
Desktop coverage resizes 1440×900 to 1280×800 at the same aspect ratio and measures
the minimum-zoom target sizes, then restores the original size before save checks.
No gameplay state, saves, Unity messages or network intents are injected.

After the direct map-node tap, the test replays the first fight and rewards from
the committed `UnityTests/Parity/native-browser-replay.json` seed-1 Reaver trace.
Each real command must match its recorded HP, MP, stamina, turn, act and phase.
The test stops at the first return to the map and requires at least two legal
routes before checking branching-decision geometry. A passing single-choice
starting floor alone is insufficient. The fixture is a historical domain oracle;
its source digest does not identify the newly tested Web player.

The map observation contract is `ASHENSPIRE_MAP_VIEW_CHUNK` with
`{sequence,index,count,text}` envelopes. These carry read-only projected geometry
and visibility, not hidden room resolutions. The shared native driver observes
controls and run state; the test owns its additional map observer.

Outputs include `summary.json`, per-viewport source and check receipts, map
snapshots, first-fight command/fixture-hash receipts, and nine PNG screenshots per
successful viewport (ten for desktop). Inspect screenshots
before asserting visual acceptance. A failed run preserves its last diagnostic
state and a failure screenshot; an exception exits nonzero.

These checks cover the solo board at 320×640, 390×844, 412×915 and 1440×900.
A fifth independent 390×844 climb uses the verified `BA` (395) Standard Reaver
route to earn the Sealstone Key through a real fight and an Unknown treasure.
Its four screenshots record Unknown, the reward offer, revealed fog and revealed
paths. Checks require the accepted reward and its resulting projection; direct
ownership is inferred from these observations because the diagnostics omit the
owned relic array. The Key must not expose future nodes in fog, while Paths
reveals their verified outcomes. The script records the domain oracle's hash.

These checks do not establish physical-device behavior, co-op voting,
later-act traversal or subjective visual acceptance. Those need separate
source-matched evidence. Authoring or syntax-checking this harness is not a pass
against a compiled player.
