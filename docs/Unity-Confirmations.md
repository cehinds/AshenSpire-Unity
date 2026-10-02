# Unity hold-to-confirm (US-13.3)

Destructive actions in AshenedSpire commit only after a deliberate second beat.

> **Status: implemented and compile-checked, not yet played.** `UnityTests/HoldConfirm`
> passes and `node tools/unity-runtime-check.mjs` compiles the UI. Nobody has tried the
> buttons in the Unity editor or a player build yet.

## Which actions

The table is data, not code:
`GameContent/Unity/Original/confirmation-policies.json` is a byte-identical copy of the
original game's `content/framework/confirmationPolicies.json`. *Validate and Import
Content* (`BuildTools.ImportContent`) validates it and copies it to
`Resources/Original/confirmation-policies.json`. Never edit the Resources copy.
`ConfirmationPolicy` (`Runtime/Domain/Original/ConfirmationPolicy.cs`) loads it. It
rejects an action whose `destructive` flag disagrees with its policy, and it throws on an
unknown action id, so a missing entry is an error instead of an action with no
confirmation.

| Action | Level | Unity control | Hold-to-confirm? |
|---|---|---|---|
| `action.removeCard` | DESTRUCTIVE | merchant `native-remove-<instanceId>`; co-op merchant `coop-remove-<instanceId>` | yes |
| `action.overwriteSave` | DESTRUCTIVE | slot review `native-slot-confirm` (after `native-slot-<n>-new`) | yes |
| `action.deleteSave` | DESTRUCTIVE | slot review `native-slot-confirm` (after `native-slot-<n>-delete`) | yes |
| `action.loadSlot` | DESTRUCTIVE | `native-slot-<n>-continue` and the title's `native-continue` | only while the climb in memory has unsaved progress |
| `action.quitWithoutSaving` | DESTRUCTIVE | none | no Unity control exists |
| `action.abandonRun` | DESTRUCTIVE | none | no Unity control exists |

`ConfirmationPolicy.UnityRoutes` records this table in code. `UnityTests/HoldConfirm` asserts
that it covers every DESTRUCTIVE action in the data. It also checks that each routed source
binds its constant through `HoldConfirmButton`, and that every policy constant used in
`Presentation/` has a registry entry.

**When loading a slot discards progress.** `native-menu` always saves before it returns to
the title. Normally the title and slot screens therefore have nothing unsaved to lose, and
Continue stays a one-tap button. If a save fails (`RunController.SaveOriginalSlot`), the
game keeps the climb in memory so it can retry later. In that case `RunController` sets
`CampaignView.NativeUnsavedProgress`. Continue on the title and on each slot then needs a
hold or a second tap, and the slot screen shows a notice explaining why. The next
successful save, load or new climb clears the flag.

**Actions with no Unity UI.** No Unity control quits without saving or abandons a run.
A climb ends in victory or defeat, or by deleting or overwriting its slot, and both of
those are routed. Nothing was invented for these two actions. The test checks that no
Presentation string offers either one.

## How it behaves

`HoldConfirmState` (`Runtime/Domain/Original/HoldConfirmState.cs`) holds the rules. It
takes time as an argument and never reads a clock. `Presentation/HoldConfirmButton.cs`
connects it to UI Toolkit input:

- **Hold:** press and hold the button. A bar fills along the bottom edge, and the action
  commits once when the bar is full. Releasing early, dragging past the 12 px slop
  (`FeelInput.DragSlopPx`) or leaving the button cancels the hold. A cancelled hold
  never commits.
- **Tap twice:** a quick click, **Enter**, **Space** or pad submit arms the button. The
  label changes to "Tap again to confirm · …". A second activation within 4 seconds
  (`DefaultArmWindowMs`) commits. A key that auto-repeats while held also confirms.
  Moving focus away disarms the button.
- **Duration:** the hold lasts 600 ms, which is `FeelInput.HoldConfirmNormalMs`, the
  original `balance.ui.holdConfirm` "normal" step (`src/content/balance.js`). A duration
  of 0 turns off the hold path, and tapping twice still works. Unity has no player
  setting for this dial yet.
- Each control commits exactly once and then resets. A click that arrives after a
  completed hold can arm the button again but cannot commit.
- The bar shows state, not decoration, so it also appears with reduced motion on.

### Why a quick click arms instead of opening a dialog

In the original game, a tap opens a review modal and a hold commits directly. The Unity
slot screen already has a review page (`native-slot-confirm` / `native-slot-cancel`). If
a tap also opened a modal, the player would need three steps. With arm-then-confirm, the
whole flow is a single button with no extra element ids. Browser playtests stay simple:
to commit, a playtest clicks the same id twice. Element ids did not change.
`tools/native-save-recovery-playtest.cjs` only checks that `native-slot-confirm` exists
and then cancels, so it needs no edit. No playtest clicks `coop-remove-*` or forces a
save failure, so every playtest that uses `native-continue` or `native-slot-<n>-continue`
still loads with one click.

## Tests

`dotnet run --project UnityTests/HoldConfirm` checks the following:

- The data copy, its mirror and its import are in sync.
- Every DESTRUCTIVE action is classified, and inconsistent data is rejected.
- The duration matches the original game and the feel profile.
- Early release and cancel never commit.
- A completed hold commits exactly once.
- Tapping twice commits, and the arm window expires.
- The control can be used again after a reset.
- The disabled-hold path works.
- The panels are wired to the button, and the route registry covers every DESTRUCTIVE action.

CI runs it in the `features` shard of `.github/workflows/unity-ci.yml`.
