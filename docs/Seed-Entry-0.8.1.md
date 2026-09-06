# Readable seed entry — 0.8.1

The optional expedition seed uses an ivory foreground on a dark input surface. A gold border and caret identify the focused field; selected digits use a dark amber highlight. The label, keyboard editing, minimum touch height and interruption draft retention use the existing UI Toolkit control.

## Modify it yourself

Edit the `.seed-field .unity-base-text-field__input` rules in `Unity/Assets/AshenSpire/Resources/Expedition.uss`. Keep foreground, background, border, caret and selection colors readable together. The adjacent comment identifies the scope. Rebuild the player after a stylesheet change.

`CampaignView.Heroes` creates the field and validates the seed. `RefreshTouchTargets` maintains the displayed target size; do not replace it with a fixed device-pixel height. The field is presentation state, and the interruption cover preserves its instance. This patch introduces no MonoBehaviour, Inspector wiring, content field or save-schema change.

Unity documents the child selector and custom caret/selection properties in its [TextField reference](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-uxml-element-TextField.html).

## Test it yourself

1. Choose New expedition. Focus the optional seed field and enter digits. Check the text, caret and focus border.
2. Select all, replace the number, then use Backspace. Check highlighted digits remain readable.
3. Enter `42949672950`, then choose a wanderer. The hero screen should remain and explain the allowed range.
4. Replace it with `240987`, switch tabs/apps and explicitly return. Rotate between portrait and landscape; the draft should remain readable.
5. Start the run. The expedition should use seed `240987`.

```powershell
node tools/interruption-playtest.cjs http://127.0.0.1:8787 Builds/SeedCheck --seed-only
```

The browser check uses real pointer and keyboard events, real tab visibility changes, a 390 × 844 portrait viewport and 740 × 320 landscape viewport at device pixel ratio 3. It reads the resulting campaign seed rather than injecting campaign state. Screenshots cover blank, focused, selected, invalid, edited and returned fields. Browser emulation does not establish physical-phone keyboard behavior, native rendering or accessibility conformance.

The keyboard drivers separate presses, releases and modifier changes across rendered frames. For the browser's mobile keyboard bar and Enter/Tab dismissal, run `node tools/campaign-playtest.cjs http://127.0.0.1:8787 Builds/MobileCheck --dpr 3 --touch --mobile-layout --landscape-repeats 3`. Its seed captures show the entered value before and after keyboard dismissal, and the campaign must start with seed `3`.

The issue #27 follow-up also separates raw mouse hover, press and release/focus across rendered frames. Before editing, it starts a campaign with the initial `240987` draft and records the observed state in `seedCampaigns`. It returns through Menu and Choose your wanderer for the existing replacement, Backspace and interruption scenario. Both campaigns must use exactly `240987`; a later correction cannot conceal missing initial digits. Console errors remain failures. See [the follow-up validation report](Seed-Focus-Validation.md) for the renderer failure exposed by this expanded sequence.
