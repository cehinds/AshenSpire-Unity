# Branching map — Unity 0.0.12.0, build 12

The original route board now appears in solo and co-op. Fog, Paths, earned Sealstone room readings and shrine guidance preserve the original knowledge rules. Zoom, recenter, touch navigation and local camera preferences work on phone and desktop layouts. The painted enemy set remains in use.

## Try this build

- Start a native climb and inspect the fogged map. Toggle Paths and shrine guidance.
- Open the map key and Routes list. Pan, zoom and recenter; reload to check local preferences.
- Finish the opening fight and choose between the next legal routes directly on the map.
- Try a shared climb using the matching companion. Each player keeps their own camera.
- Earn a Sealstone to reveal eligible unknown-room types. The item does not reveal every hidden route.

Web.zip needs an HTTP server. Unzip the portable companion beside the Web folder and run Start-Companion.cmd for local hosting and co-op. Windows.zip and Android.apk were compiled from the same source; physical-device play remains to be tested.

## Verification and editing

The map suite passed 558 checks with 41 screenshots. A separate minimum-zoom rerun passed 133 overlapping checks with 10 screenshots. The current player completed all three acts with 657 checks and 280 real commands. Co-op passed eight host and six guest checks. Browser suites reported zero runtime errors.

Edit GameContent/Unity/Original/map-presentation.json for room labels, colors, descriptions and icon bindings. See docs/Unity-Map-Foundation.md for the components and authoring rules. The original card/equipment tables and painted art remain editable through their existing guides.

Full screenshots and input/state evidence: https://github.com/cehinds/AshenSpire-Unity/tree/3dafb2514dd22c7ed545ed872fed0ac6110ddd2f/docs/qa/unity-map-0.0.12.0

The earlier build-11 gallery and guide remain archived beside this file. Current validation.json describes build 12. Foundation acceptance remains open, including co-op animation, physical-device acceptance, broader polish and human balance testing. Version 0.1.0.0 marks accepted foundation; 1.0.0.0 marks the finished game.
