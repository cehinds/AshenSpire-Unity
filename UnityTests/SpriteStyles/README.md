# Original sprite style checks

Run from any working directory:

```sh
dotnet run --project UnityTests/SpriteStyles/SpriteChecks.csproj
```

An optional argument is the repository root if the normal build output path is customized. This project compiles the **integrated** `OriginalSpriteCatalog.cs` and copies the **authored** `GameContent/Unity/Original/{sprite-styles,appearance-options}.json` into its output. It does not carry a second implementation or duplicate authored table.

The checked-in source fixture records actual original `playerSprite` decisions, `createPoseStage` CSS registration and per-figure attack rotation. Regenerate it only with the exact reference checkout:

```sh
node UnityTests/SpriteStyles/oracle.mjs <original-reference-checkout>
```

The exporter requires HEAD `b17a7f4543e1710f49fae8b58880121690a314de` and never writes the reference. Its DOM-shaped recorder observes original functions; it does not claim browser or Unity rendered pixels.

`asset-receipts.json` includes original source hashes, converted PNG hashes and decoded-pixel hashes. Tests verify all 600 integrated PNG files against those receipts. The original import compared all 580 original WebPs' decoded RGBA bytes with converted PNG bytes. The 20 classic textures are renders of the exact original SVG builders, with a separate native vector sigil overlay.

To reproduce the imported assets into a separate staging directory, install `sharp` or set `SHARP_MODULE` to its module path, then run:

```sh
node UnityTests/SpriteStyles/import-sprite-styles.mjs <original-reference-checkout> <explicit-staging-output>
```

This deterministic format importer reads the integrated authored appearance table, checks the pinned SHA and refuses an output directory inside the original checkout. It does not write Unity assets. The output contains PNGs, SVG evidence, the rendering table and new receipts for review. Copy/import into production remains a separate action.

Validated result: **9,437 checks** (8,236 original route/registration/rotation checks, authored style identity consistency, and existence/hash checks for 600 imported textures). The project writes `checks.json` beside its executable. A separate compiled-game pointer test lives at `tools/native-appearance-playtest.cjs`; this headless suite does not replace it or prove physical mobile behavior.
