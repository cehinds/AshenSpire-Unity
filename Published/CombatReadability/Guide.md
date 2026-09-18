# AshenSpire 0.0.13.0 — combat readability

Build 13 is a foundation checkpoint, not completed visual parity with the original HTML game.

- Start a Standard Reaver with seed 1 and inspect Slashing Strike: Deal 14 damage includes the existing +8 Strength contribution for this loadout.
- Costs now read 1 action, Free or X actions without zero MP/stamina clutter.
- Spend your actions and select another card. Its shortage explains why Play is disabled; End turn restores actions.
- Save/reload restores the same combat state. Use the matching companion for co-op.

Verified: 168 focused browser checks across 320×640, 390×844 and 1440×900; 657 full-climb checks / 280 commands; 14 two-player checks; 22 companion-restart checks. All compiled exports use source digest:

`e162744f76dbfc50212711190eaa9fa00bfb62c492f71bd495f75282436b3105`

Runtime source commit: `b353058064d6c765ecc64144d7b7d7eba09d883c`. Pure tests: 2,133 cost checks and 1,499 card-text checks. Package checks: 442 companion, 160 native-file, 18 root-package.

Screenshots and real input/state receipts support these checks. Physical Android and graphical Windows play are unverified; iOS is not built. Formula/status-aware damage previews, faithful original layout/aesthetic parity and broader polish remain open. Initial harness failures and retries are preserved in the repository QA folder, separate from the successful final run.
