# Original card text checks

Run from the repository root with .NET 8:

```powershell
dotnet run --project UnityTests/CardText
```

The default receipt is `TestResults/CardText/checks.json`; an optional first argument selects another output path. Tests compile the integrated native domain source, including `OriginalCardText`, rather than a copied implementation.

The suite compares binding receipts, literal tokens and rendered text against all 182 original cards in both base and upgraded forms. Two additional grammar fixtures cover repeated effects, numbered hit counts, unknown tokens, formulas, case sensitivity and malformed records. It also verifies input immutability and culture independence. Eight real native combat commands verify that an eight-point attribute bonus remains eight total damage for one, two, three and five hits; only unambiguous single-hit literal amounts include that bonus in the displayed number.

The committed oracle is portable; ordinary tests require neither the original checkout nor Node. To deliberately regenerate it, use Node and a clean original source checkout at `b17a7f4543e1710f49fae8b58880121690a314de`:

```powershell
node UnityTests/CardText/export-reference.mjs <pinned-original-checkout>
```

The exporter calls original `computeTokenBindings`, `staticTokens` and `resolveCard` directly. The adjacent receipt records source hashes, pinned SHA, fixture count and output hash. Do not regenerate this oracle from edited Unity content to make a failed test pass.

These are domain/text checks. Unity layout, screenshots and target-dependent combat damage previews require separate evidence.
