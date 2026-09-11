# Combat readability: editing and verification

**0.0.13.0 · build 13 · foundation in progress.** This change makes native card
numbers and resource requirements clearer in solo and co-op. It changes
presentation and Play-button availability; it does not change card balance,
resource prices, payment rules or combat effects.

## What changed

- Whole-valued JSON floating numbers from equipment, such as `6.0`, now display
  the same single-hit total as the equivalent authored integer `6`. A safe
  single-hit, single-repeat literal can include its existing attribute bonus
  in the displayed amount. The accompanying “Includes” sentence identifies that
  contribution rather than suggesting a second bonus to add.
- Solo and co-op share `OriginalCardCostText`. They display the resolved card
  cost: `1 action`, `2 actions`, `Free` or `X actions`, with positive MP and
  stamina costs appended. Zero MP and stamina are omitted.
- Cards remain selectable when resources are insufficient, so their effects
  and requirements can still be inspected. A shortage such as `Need 1 more
  action` or `Need 2 MP, 1 stamina` explains the deficit and disables Play.
  Recovery updates availability from the current resource pools.
- Cards tagged `internal.unplayable` disable Play with `Cannot play this card`.
  Resource affordability does not override target, turn, ownership or other
  authoritative command restrictions.

For the tested Standard Reaver at **Strength 13**, the starting Slashing Strike
has a six-point equipment amount and an existing eight-point Strength
contribution. It reads **“Deal 14 damage. Includes +8 total damage from
Strength.”** The tested smithing tier 1 version reads 17. These are specific
loadout examples against an unmodified test target, not universal card values
or guaranteed enemy HP loss.

## Where to edit

Paths are relative to the repository root. Keep wording and layout changes
separate from changes to authored mechanics.

| Change | Source |
|---|---|
| Card templates, effects, authored costs and equipment profiles | `GameContent/Unity/Original/content.json` |
| Attribute progression rules | `GameContent/Unity/Original/progression.json` |
| Template binding and safe inclusion of an existing attribute bonus | `Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalCardText.cs` |
| Shared cost wording and resource-deficit wording | `Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalCardCostText.cs` |
| Solo card selection, shortage labels and Play availability | `Unity/Assets/AshenSpire/Runtime/Presentation/OriginalRunPanel.cs` |
| Co-op equivalents, using host-provided costs and local player pools | `Unity/Assets/AshenSpire/Runtime/Presentation/OriginalCoopPanel.cs` |
| Unaffordable-card and shortage appearance | `Unity/Assets/AshenSpire/Resources/Expedition.uss` |

Author templates with effect tokens such as `{damage}`, `{block}` and
`{block.2}`. Numbered tokens refer to repeated bindings in effect order.
Unresolved tokens remain visibly braced so content errors can be found; do not
replace them with plausible-looking words or invented values.

The resource helper takes a resolved `CardMechanics.CostProfile` and player
`energy`, `mana` and `stamina`. It does not calculate a new price or spend
resources. Keep reductions and special prices in the existing domain rules.
`X actions` does not impose a new minimum action balance. A null shortage means
only that the resource check passes.

## Tests and acceptance

Run these from the repository root with .NET 8:

```powershell
dotnet run --project UnityTests/CardText
dotnet run --project UnityTests/CardCosts
```

The current pure-source results are **1,499 CardText checks**, including the
366 preserved oracle fixtures and ten real native combat commands, and
**2,133 CardCosts checks** covering all 182 authored cards. The card-text suite
checks integer/floating equivalence, actual weapon composition and smithing,
input immutability, token grammar and unsafe numeric cases. The cost suite
checks actual cost/payment boundaries, singular/plural wording, Free/X costs,
combined deficits, reductions and recovery. See the adjacent test READMEs for
receipt locations and deliberate original-oracle regeneration instructions.

After exporting the changed Unity player, run the focused browser script
against that export, supplying its URL:

```powershell
node tools/native-card-cost-playtest.cjs <compiled-Web-URL> TestResults/NativeCardCosts
```

The script requires Playwright (or `PLAYWRIGHT_MODULE` pointing to its installed
module). It uses real inputs in separate 320×640, 390×844 and 1440×900 browser
contexts. It checks the starting weapon description, actual resource spending,
selectable unaffordable cards, disabled Play, next-turn recovery and exact
reload state. Inspect the PNGs as well as the label/control receipts, including
the Play-button wording. Match the recorded `build-source.json` to the export;
a URL alone does not identify the tested build.

**Build 13 acceptance recorded:** source commit
`b353058064d6c765ecc64144d7b7d7eba09d883c`, compiled source digest
`e162744f76dbfc50212711190eaa9fa00bfb62c492f71bd495f75282436b3105`.
The final focused browser run passed **168 checks**, 56 each at 320×640,
390×844 and 1440×900, with 12 screenshots and zero runtime errors. It exercised
real card payments, selectable unaffordable cards, disabled Play, next-turn
recovery and exact reload state. Phone weapon/shortage and desktop shortage
screenshots were inspected for the reported cost and damage wording.

Separate source-matched checks passed the full three-act native climb
(657 checks, 280 commands), two-player combat/rewards/rejoin (8 host and
6 guest checks), and actual companion process restart (22 checks). Web,
Windows and Android exports are built. The portable companion and package
checks passed 442 companion checks, 160 exported-file checks and 18 package
checks. The original Node suite passed 136/136. These are different evidence
types; no physical-phone or graphical Windows playtest is claimed.

Earlier focused harness failures remain in the QA folder. The first reserved
a fixed 120-pixel footer margin and could not click a menu that was actually
visible. The second compared a measured 43.999916-pixel target with exactly 44;
the harness now measures the real footer and allows 0.1 pixel of floating-point
tolerance. A third run passed both phone sizes but was interrupted during
desktop testing, so it has no aggregate pass. The subsequent complete final
run above passed all three sizes. These were harness changes, not combat fixes.

Receipts, logs and screenshots are under
`docs/qa/unity-combat-readability-0.0.13.0`; the smaller current-build gallery
and play guide are under `Published/CombatReadability`. The optional
`Published/presentation.json` selects that gallery without changing the older
shared `Published/NativeEvidence` folder.

## Remaining scope

Formula-, target- and status-aware damage previews remain a separate task.
Multi-hit and repeated effects keep the attribute contribution as a separate
total; it must not be multiplied once per hit. Fractional, nonfinite and
out-of-range amounts do not claim a folded bonus. These presentation checks
also do not establish physical-device usability, balance or completed
foundation parity. Version `0.1.0.0` remains the foundation acceptance milestone.
The current stacked layout and narrow combat stage have not established visual
parity with the original HTML game. Faithful original layout and aesthetics are
the next visual priority, separate from this readability checkpoint.
