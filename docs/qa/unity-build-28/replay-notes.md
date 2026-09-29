# Build 28 campaign replay

- [x] The first browser replay exercised all 306 recorded commands and reached the expected Defeat in Act 3, with 745 assertions recorded before failure.
- [x] Diagnosed the first failure: its final Chronicle assertion expected an obsolete single-line row.
- [x] The observed Chronicle displays `Reaver · Defeat` followed by `Act 3, floor 5 · 17 fights · 1068 damage dealt · Seed 1`. The matcher now requires the matching class/result and adjacent act line; negative cases cover wrong class, outcome, act and nonadjacent rows.
- [x] A fresh complete replay passed all 749 checks and 306 commands against the same packaged build, including exact reloads, the recorded Act-3 defeat, Chronicle totals and unchanged compiled source. See [passing receipt](campaign-replay.json).

The [original failed receipt](first-replay-failure.json) is preserved and has not been relabeled as a passed suite. The rerun uses a 390×844 CSS viewport at device scale 1; the normal CI default remains scale 2. All command/state assertions are unchanged, and the receipt records the chosen scale. This is browser evidence, not a physical-phone or balance acceptance claim.
