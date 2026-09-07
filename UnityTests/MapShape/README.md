# Original Custom Climb map shape checks

Run from the repository root with .NET 8:

```powershell
dotnet run --project UnityTests/MapShape
```

The default output is `TestResults/MapShape/checks.json`; an optional first argument
selects another receipt path. The runner compiles integrated domain source, with no
duplicate runtime implementation. **2,696 checks passed** at integration.

The portable fixture covers 339 original shape validation/configuration cases,
720 complete original act maps with exact named RNG counters and twelve canonical
24-seed sampler receipts. It tests caps, all authored weight keys, fractional and
boundary weights, malformed inputs, all-zero refusal, defaults and minimum viable
floors. The pinned executable rules derive seven minimum floors per act, even
though old original comments mention four.

Native integration checks verify frozen limits, exact restoration, corrupted
shape/graph/limit refusal, deterministic Draft continuation and three shaped act
transitions. That room traversal supplies combat results explicitly; it is not
claimed as playable-combat evidence. Real combat/browser evidence is separate.

To deliberately regenerate the original oracle with Node:

```powershell
node UnityTests/MapShape/export-reference.mjs <clean-pinned-original-checkout>
```

The exporter requires original SHA `b17a7f4543e1710f49fae8b58880121690a314de` and a
clean source tree. It calls original `applyRunShape`, `minViableFloors`, `buildActMap`
and `sampleActShape`; the adjacent receipt includes source and fixture SHA-256s.
Ordinary tests require neither Node nor an original checkout. Do not replace these
expected values with owner-tuned Unity output to make a failed test pass.
