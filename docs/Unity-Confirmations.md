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
| `action.removeCard` | DESTRUCTIVE | merchant `native-remove-<instanceId>` | yes |
| `action.overwriteSave` | DESTRUCTIVE | slot review `native-slot-confirm` (after `native-slot-<n>-new`) | yes |
| `action.deleteSave` | DESTRUCTIVE | slot review `native-slot-confirm` (after `native-slot-<n>-delete`) | yes |
| `action.loadSlot`, `action.quitWithoutSaving`, `action.abandonRun` | DESTRUCTIVE | not routed yet | — |

The co-op merchant (`OriginalCoopPanel`, `coop-remove-*`) is not routed yet either.

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
and then cancels, so it needs no edit.

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
- The panels are wired to the button.

CI runs it in the `features` shard of `.github/workflows/unity-ci.yml`.
