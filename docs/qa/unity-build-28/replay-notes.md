# Build 28 campaign replay

- [x] The first browser replay exercised all 306 recorded commands and reached the expected Defeat in Act 3, with 745 assertions recorded before failure.
- [ ] The complete replay suite is not yet recorded as passed: its final Chronicle assertion expected an obsolete single-line row.
- [x] The observed Chronicle displays `Reaver · Defeat` followed by `Act 3, floor 5 · 17 fights · 1068 damage dealt · Seed 1`. The matcher now requires the matching class/result and adjacent act line; negative cases cover wrong class, outcome, act and nonadjacent rows.
- [ ] A fresh complete replay is running against the same packaged build. Its receipt will supersede this pending status only after success.

The original failed evidence is preserved locally in `work/evidence/build28-climb-chronicle-label-failure`. It has not been relabeled as a passed suite. The rerun uses a 390×844 CSS viewport at device scale 1; the normal CI default remains scale 2. All command/state assertions are unchanged, and the receipt records the chosen scale. This is browser evidence, not a physical-phone or balance acceptance claim.
