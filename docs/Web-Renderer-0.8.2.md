# Web rotation rendering — 0.8.2

Issue #29 reproduces missing label geometry and `GfxDevice::CopyBufferRanges` errors when a player starts an expedition, returns to choose another, edits its seed, switches tabs and rotates the screen. Both typed campaign seeds remain correct. The renderer's staging allocation is too small for the expanded copy ranges.

## What changes

This is a version-pinned Web build workaround for Unity `6000.6.0f1`, not an official Unity engine update. Gameplay, colors, layout, save data and native rendering behavior are unchanged. Windows and Android are rebuilt with the matching game version.

`WebStagingBuildProcessor` uses Unity's [documented player assembly callback](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Build.IPostBuildPlayerScriptDLLs.OnPostBuildPlayerScriptDLLs.html). Unity copies managed player assemblies to the project's temporary staging directory before calling it. The processor refuses any path outside that directory and checks the exact Unity version and original Web UIElements assembly SHA-256. The installed Editor is never edited.

The unmodified engine allocates from `totalDirtyCount`, then consolidates ranges and aligns their boundaries. `WebStagingPatch` replaces that allocation's count with a helper that first consolidates, then requests the resulting count plus two elements per remaining range. Each index range can grow by at most two elements when aligned to even boundaries. The subsequent consolidation is idempotent. Checked arithmetic rejects overflow; pooling still reserves enough space for each dataset. A [first-hand engine investigation](https://discussions.unity.com/t/webgl-6000-5-2f1-ui-toolkit-staged-updater-under-sizes-its-staging-buffer-copybufferranges-reads-out-of-bounds-on-large-single-frame-restyles/1731377) reports the same allocation ordering in Unity 6.5; this project independently inspected the installed 6.6 module and reproduced the failure.

Only the temporary player DLL and its symbols are rewritten. A receipt records original and patched assembly hashes, Unity version and algorithm under `Builds/WebStagingPatch.json`; the built Web folder includes `renderer-workaround.json`. Package validation requires this receipt and includes its hash. The callback resets its receipt for each build, preventing reuse of stale evidence.

## Modify it yourself

- Source: `Unity/Assets/AshenSpire/Editor/Rendering/`. No MonoBehaviour or Inspector wiring is needed. The top comments explain the hook, scope and removal conditions.
- Unity dependency: pinned `com.unity.nuget.mono-cecil` `1.11.6`, referenced only by the Editor rendering assembly. The headless tests use Mono.Cecil `0.11.6` on .NET 8.
- On an Editor upgrade, the build deliberately stops. Reproduce the original sequence against an unmodified candidate engine first. If Unity fixes it, remove the workaround and its receipt requirement together, then rebuild and repeat browser checks. Do not simply replace the allowed checksum.
- Prefer a supported engine correction when available. This workaround may increase transient staging capacity; it does not establish a mobile memory or performance budget.

## Regression checks

```powershell
dotnet run --project UnityTests/RendererPatch
node tools/interruption-playtest.cjs http://127.0.0.1:8787 Builds/SeedRenderer --seed-only
node tools/interruption-playtest.cjs http://127.0.0.1:8787 Builds/SeedRendererSlow --seed-only --slow-input
```

The .NET test first executes a failing allocation model, patches its assembly with the production rewriter, then executes the resulting IL against dense/sparse ranges, alignment boundaries, large counts, empty input and 1,000 deterministic random cases. It also checks arithmetic overflow, duplicate patching, missing renderer and changed consolidation contracts. The browser regression incorporates the first-draft coverage from #27: actual campaign starts must both use `240987`, with seven contrast checks, 44 CSS-pixel targets, real tab switches, rotations and the no-console-error gate intact.

The initial candidate passed all 25 browser checks with 18 screenshots, and the formerly missing validation-label text is visible again. Before/after logs and screenshots are retained separately. Final package and Linux CI results accompany the delivered checkpoint; this guide describes the implementation and reproduction, not physical-device certification.
