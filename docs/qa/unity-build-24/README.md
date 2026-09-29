# Build 24: combat inspection and keyboard controls

Version **0.0.24.0**, build **24**. Source digest:
`2314894d6014c76a489ef495002d10a051db5fd180b75465b745e5de3c3568fa`.
Runtime source commit: `766a470e9cfc27c7aa6753c8717cbc12c452a95e`.

Solo combat now provides draw, discard and exhausted-card inspection, optional
and forced retained-card discard choices, and saved keyboard bindings with
contextual help. Inspection leaves state and RNG untouched and hides draw order.
Keys act on release; held/repeated keys do not submit additional enemy turns.
Discard choices preserve control focus and commit in original hand order.

## Validation

- Compiled Web player: **37 + 37 checks** at 320×640 and 1440×900.
  Normal pointer/keyboard input covers rebinding cancellation/conflicts, saved
  bindings, piles, empty states, blocked combat input while inspecting, exact
  card/flask payment, one turn per release and exact save/reload restoration.
  No browser or Unity errors. See [combat-controls.json](combat-controls.json).
- Unity-compiled retained-card callback fixtures: **45 checks**, optional and
  forced min/max choices, cancellation, stable controls, hand-order submission,
  single turn and restored snapshot. Modified saved hand rules are fixtures,
  not a claim that shipped-default play opens these prompts.
- Domain/parity/card/map build prechecks passed. Hand rules **172**, settings/mods
  **87**, save slots **64**, runtime compile **122** checks; C# 9 compilation passed.
- Unity 6000.6.0f1 exported Web, Windows and Android plus the companion package.
  **451 companion, 163 native-file and 20 package checks** passed.
- Phone pile/help captures were visually inspected for readable text and the
  return control. This is agent review, not owner visual acceptance.

## Limits

This is a local candidate. Build 22's [draft PR #56](https://github.com/cehinds/AshenSpire-Unity/pull/56)
still needs owner merge; current dev is build 21. The one-step version gate
passes against build 23. No gate was weakened, no agent merge occurred, and no
channel was promoted. F00–F17 remain unaccepted. Full mode/content coverage,
physical Android, graphical Windows, iOS, device performance and player pacing
remain open. Build 25 save-recovery work is separate from this checkpoint.
