# Platform acceptance

## Build-22 validation scope

The local Windows machine has Unity 6000.6.0f1 and Web/Windows/Android build
support. On 2026-09-27, `adb devices` found no connected Android device. There
is no verified macOS/Xcode/signing environment. Builds and browser emulation
must not be recorded as physical-device passes.

`tools/native-performance-playtest.cjs` records startup, resource bytes,
browser animation-frame intervals, JavaScript heap usage and reserved WASM
memory in title, map and combat. Each receipt names the compiled source digest.
The default Edge SwiftShader run is a reproducible software-rendering baseline;
its RAF intervals are not Unity GPU timings and localhost is not a mobile
network. Target-phone budgets remain unset until reference hardware is selected.

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
