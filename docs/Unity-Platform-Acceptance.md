# Platform acceptance

## Current build-25 candidate

Build 25 has matching Web, Windows, Android and companion packages. Its Web
player passes the three-slot save/reload and two-player fight/rejoin checks;
see [build-25 evidence](qa/unity-build-25/README.md). These are desktop Edge
checks with emulated phone dimensions, not physical-device passes. The
2026-09-28 Android device check still found no connected device. No current
graphical Windows or iOS device acceptance is claimed. The device procedures
and iOS delivery plan below remain the outstanding platform gates.

## Build-22 validation scope

The local Windows machine has Unity 6000.6.0f1 and Web/Windows/Android build
support. On 2026-09-27, `adb devices` found no connected Android device. There
is no verified macOS/Xcode/signing environment. Builds and browser emulation
must not be recorded as physical-device passes.

The locally compiled Windows player reached `ASHENSPIRE_UI_READY` on
2026-09-27. Two attempts to capture its actual window failed with
`FrameArrived timed out` / `window capture timed out`. This is startup evidence
only, not graphical play-through acceptance. The capture failure did not report
a game exception, but the inaccessible screen prevents visual claims.

`tools/native-performance-playtest.cjs` records startup, resource bytes,
browser animation-frame intervals, JavaScript heap usage and reserved WASM
memory in title, map and combat. Each receipt names the compiled source digest.
The default Edge SwiftShader run is a reproducible software-rendering baseline;
its RAF intervals are not Unity GPU timings and localhost is not a mobile
network. Target-phone budgets remain unset until reference hardware is selected.

Build 22 imports the ten original music files with streaming/background loading
and native encoding quality 0.5. The source files under `music/` are preserved.
Quality 1 made the Windows ZIP 114,469,872 bytes; the adjusted export is
103,762,976 bytes. This is distribution-size work, not listening acceptance.
`unity-package.mjs` now refuses any tracked export above GitHub's
[100 MiB ordinary-Git file limit](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github).
The Windows archive remains close to that limit; future growth
may require a verified external download path rather than raising the gate.

## Android and Windows acceptance

Use a separate test profile. Record model, OS, graphics API, build number and
source digest. On Android, install the matching APK without clearing an existing
player's data. Exercise creation, targeting, card inspection, inventory, shop,
shrine, settings, background/return, rotation, save/relaunch and LAN rejoin.
Capture first-start and warm-start times, peak process memory, sustained combat
frame times and any thermal slowdown. Test touch on the actual screen.

On Windows, run the matching graphical player through the same flow with mouse
and keyboard. Verify resize/fullscreen, focus loss/return and save/relaunch.
Retain player logs, screenshots and any reproducible defects. A batch build
or software-WebGL result does not replace this run.

## iOS delivery plan

1. Establish the intended iPhone/iPad models and minimum supported OS with the
   owner. Use the Web build in Safari for an initial device usability baseline.
2. Provide a Mac with the pinned Unity editor's iOS support, a compatible Xcode
   installation, an Apple developer team and the owner's chosen bundle ID.
   Keep signing credentials outside the repository.
3. Add a separate iOS export method and receipt alongside BuildTools' existing
   targets. Build an IL2CPP Xcode project into `Builds/iOS`, then archive and sign
   in Xcode. No native iOS artifact is currently claimed.
4. Validate safe areas, keyboard/focus, touch targets, audio activation and
   interruptions, persistent saves, memory pressure and LAN transport on a
   physical iPhone. Review local-network permission behavior for the companion.
5. Distribute an accepted candidate through TestFlight, then record device
   evidence and owner acceptance before App Store submission.

## Acceptance gates

All platform rows remain open until their actual device receipts exist. Set
measurable budgets from the selected devices, not from desktop browser timings.
Real-player pacing/fun testing and owner acceptance remain separate gates.
