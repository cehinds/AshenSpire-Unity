# Shared card cost labels

Run from the repository root:

```text
dotnet run --project UnityTests/CardCosts
```

This focused executable compares `OriginalCardCostText` with actual
`CardMechanics.CostProfile` and `ResourceWallet` boundaries for every authored
card. It checks singular/plural labels, omitted zero MP/stamina, Free/X costs,
combined deficits, power reductions, dodge prices, payment and recovery.

The helper reads resolved costs and player `energy`, `mana`, `stamina`. It never
spends resources. A null shortage means the resource check passes; targets,
turn ownership and unplayable tags remain separate command checks. These tests
do not establish browser layout or gameplay balance.
