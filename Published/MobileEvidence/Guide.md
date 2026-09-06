# Mobile browser viewport — 0.7.0

The browser now sizes touch controls using the displayed canvas height. Device pixel density still improves rendering, up to the existing 1.5 cap, but no longer shrinks the controls. Buttons, settings toggles and seed entry retain a minimum 44 CSS-pixel height. Landscape layout uses the displayed height and existing controls resize after rotation.

## Reproduced problem

The 0.6.0 player measured the intent control at 41.33 CSS pixels high in a 320 × 740 viewport with device scale factor 3. Previous browser tests used density 1. High-density landscape also selected its reference size using render pixels, making the layout unnecessarily small.

## Editing and testing

- `Presentation/ViewportLayout.cs` owns the 44-pixel minimum and 600-pixel compact-layout threshold.
- `Application/DisplayViewport.cs` reads the browser canvas height through `Plugins/WebGL/DisplayViewport.jslib`; native players retain Screen.height behavior.
- `Application/RunController.cs` updates the panel reference height only when dimensions or native safe area change.
- `Presentation/CampaignView.cs` refreshes controls when screens are built or their geometry changes. No hierarchy scan was added to Update.
- The Web template retains its render-density cap and CSS safe-area padding. No new scene component, Inspector assignment or package is needed.

```powershell
dotnet run --project UnityTests/Viewport
node tools/campaign-playtest.cjs http://127.0.0.1:8787 Builds/MobileCheck --dpr 3 --touch --mobile-layout
```

The browser check covers repeated portrait/landscape changes, controls, settings, seed entry, synthetic canvas insets, state-preserving inspection, combat, rewards and save/resume. Touch mode uses browser touchscreen taps and swipe scrolling; seed text is entered with the test keyboard. Densities 1, 2 and 3 are distinct test contexts. Synthetic CSS insets are not evidence from a physical notch or mobile browser toolbar.

The bridge follows Unity's [JavaScript plug-in interop](https://docs.unity3d.com/6000.0/Documentation/Manual/web-interacting-browser-js-to-unity.html). It reads the current instance's canvas rather than looking up a globally named page element.

## Limits

No physical Android device was connected. Native safe-area behavior is retained and native exports are compile/startup checks, not physical-phone playtests. Real Android/iOS hardware, native graphical Windows play and audible listening still need testing. No frame-rate, battery-life or physical-mobile performance improvement is claimed. Gameplay, campaign content and save schema are unchanged.
