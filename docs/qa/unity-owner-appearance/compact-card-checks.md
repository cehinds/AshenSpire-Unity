# Compact card checks

On 2026-10-04, after the build-39 source edits, this command exited successfully:

```powershell
dotnet run --project UnityTests/HtmlParity/HtmlParity.csproj -- .
```

Output: `Published test-898 migration: 40399 checks passed.`

Five additional checks verify that compact card rules include the folded damage
bonus, the inspector retains its explanation, rendering does not mutate the
card, and multi-hit/repeated attacks retain their separate total bonus.
These are domain/rendering-input checks; they do not verify player geometry.
