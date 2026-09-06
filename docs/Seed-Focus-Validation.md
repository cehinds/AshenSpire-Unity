# Initial seed focus regression — issue #27

Status: failed validation; draft candidate, not a new published game build.

## Change

`tools/interruption-playtest.cjs` separates raw mouse hover, press and release/focus using two animation frames per phase. Existing keyboard and modifier waits remain. The high-density seed scenario starts the initial draft as a real campaign before returning through Menu and Choose your wanderer for selection/replacement, Backspace, interruption and rotation. Read-only campaign snapshots record expected and observed seeds for both starts.

The no-console-error gate, exact seed `240987`, seven rendered-contrast checks, visibility checks and 44 CSS-pixel minimum targets remain enforced. Console errors now include the preceding screenshot, input, revision and layout so failures can be located without game instrumentation.

## Baseline and local evidence

Base dev: `729cc816d02f66926729ee7b09f0233aa5aa0de5`. Compiled player: Unity `6000.6.0f1`, game `0.8.1`, source `60bb79abf6557a836119244ed47129a481256512`, source digest `1e82b1b871db4b1a6bb0f913d86bf108084efdca8342d8908c4bfb63d431b264`.

The earlier Linux run [34055746683](https://github.com/cehinds/AshenSpire-Unity/actions/runs/34055746683) displayed initial draft `40987` despite typing `240987`; later replacement concealed that gap. This turn's local baseline with the stronger assertion preserved the initial seed, so local evidence alone does not establish the old focus timing as fixed on Linux.

Four expanded Chrome runs against the unchanged Web player reached both campaigns with `240987`, but all failed the retained no-error gate: original pointer timing with CPU throttle 6, updated pointer timing with throttle 6, normal speed, and normal speed with error context. Each reported:

```text
GfxDevice::CopyBufferRanges: range reads out of bounds (srcEnd=16848 srcSize=16384)
GfxDevice::CopyBufferRanges: range reads out of bounds (srcEnd=16836 srcSize=16384)
```

Error context places these after the portrait draft return and landscape draft return, during the following viewport rotation. The first campaign, menu and hero-screen re-entry alter the renderer's allocation history. This is a newly exposed failure in the existing player, not an error introduced by changed Unity source.

The existing standard interruption scenario passes 44 checks with 21 screenshots using the new pointer driver. Package verification passes 12 checks and the browser visibility bridge passes 9. Node syntax and diff whitespace checks pass. No Unity source, scene, asset, package, Editor installation or compiled player was modified.

## Renderer investigation

Read-only inspection of the installed WebGL `UnityEngine.UIElementsModule.dll` shows `GpuUpdaterStaged.CompleteUpdate` calling `FindOrAllocateBuffer(totalDirtyCount)` before `PrepareCopyRanges`. The latter calls `ConsolidateRanges(0.9)` and then aligns index ranges. The allocation can therefore precede growth of the copied range. This is consistent with the observed 16,384-byte staging buffer being too small, but a targeted runtime fix has not yet confirmed causality.

A [first-hand Unity forum report](https://discussions.unity.com/t/webgl-6000-5-2f1-ui-toolkit-staged-updater-under-sizes-its-staging-buffer-copybufferranges-reads-out-of-bounds-on-large-single-frame-restyles/1731377) describes the same ordering in Unity 6.5. Its proposed binary patch has not been applied. Changing colors, adding arbitrary pauses or increasing a panel's vertex budget would not establish that the underlying staging bounds are correct.

## Reproduce

```powershell
node tools/interruption-playtest.cjs http://127.0.0.1:8787 Builds/SeedFocus27 --seed-only
node tools/interruption-playtest.cjs http://127.0.0.1:8787 Builds/SeedFocus27Slow --seed-only --slow-input
```

Serve the unchanged packaged Web player. Use separate output folders to preserve failures. The driver launches a disposable browser profile and performs real pointer, keyboard and tab-switch input. It never injects a seed, campaign or save.

Manual sequence: choose a wanderer with seed `240987`; return to Menu; choose another expedition; enter an out-of-range seed, correct it using selection and Backspace; switch tabs and return; rotate to landscape, switch tabs and return, then rotate to portrait. Watch the browser Console for the buffer errors. Browser emulation does not prove physical-phone behavior.

## Acceptance

| Criterion | State |
| --- | --- |
| Rendered-frame pointer/focus phases | Implemented |
| Initial and edited campaign seed assertions | Pass locally, both exactly `240987` |
| Existing contrast, visibility and target-size assertions | Pass locally |
| No browser or Unity errors in expanded scenario | Fail: renderer buffer ranges |
| Linux CI | Pending draft validation |
| Merge and dev publication | Withheld while validation fails |

Next: preserve the failing Linux result and pursue a separate renderer correction or supported Unity fix. Keep this regression intact and rerun it against any candidate player before merging.
