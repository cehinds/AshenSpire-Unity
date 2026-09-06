# Class identity validation — 0.4.0

Compiled source: 939fe567e78daae63cb57c8f60e9104a4b2a33ed.
Source digest: 45b1887a0cd39a4de011f3ad179e95258ceca746e0bd2c84b74d14b0402d4411.

## Observed results

- 201 domain checks passed: 47 original checks, 129 campaign checks and 25 class-identity checks. Existing 48 seeded campaign regressions all reached victory.
- Reward property checks cover sixteen seeds per class, requiring exactly two distinct affinity cards and one shared card, plus identical outcomes after restoring saved RNG/state. Tests cover old decks and pending cross-class offers, matching/nonmatching gear, actual poison/healing results, flask isolation, multi-effect magic and invalid authoring pools.
- Separate fixed-policy diagnostics ran 192 baseline campaigns and 192 candidate campaigns. Candidate results were 189 victories and three defeats; all terminated. See Class-Identity-Balance.md and Published/BalanceEvidence for the exact comparison and its limits.
- The final packaged Reaver browser run used seed 3, took an affinity card, purchased equipment and completed all nine encounters. Every offer satisfied the two/one split, and save/resume matched every state field.
- Separate Rogue, Herald and Starseer browser runs used seed 3 and verified their authored starter decks, class/shared labels, reward selection, inspection and exact save/resume. They cover initial combat/reward, not full browser campaigns for those three classes.
- A real 0.3.0 Web player created a combat save, then the same local origin switched to the final 0.4.0 player. Continue restored that saved state exactly, preserved the old deck, and subsequently accepted a new affinity reward. No state or save was injected by the test.
- The five browser evidence sets contain 117 screenshots and no reported browser errors. Current sources: Published/Screenshots, Published/ClassEvidence and Published/SaveUpgradeEvidence. Previous 0.3.0 screenshots remain in Published/History/0.3.0.
- Hero CSV export/import preserved authoritative bytes after explicitly normalizing the import writer to LF line endings. Importing an unknown affinity tag was rejected before replacement; source bytes were unchanged.
- Unity 6000.6.0f1 built Web, Windows and Android. Windows reached ASHENSPIRE_UI_READY in native headless startup. Twelve source/artifact/cache/version checks and 79 workflow checks passed. The workflow now also retains current-content policy diagnostics.

## Limits

The unchanged GameAudio FindFirstObjectByType call still produces an obsolete-API compiler warning; no C# errors were observed. Physical touch devices, native Windows graphical play and iOS remain unverified. The browser harness uses desktop pointer/keyboard input and phone-sized viewports. Bot wins, ended-turn counts and startup timing do not establish fun, equal class strength or physical mobile performance.

Existing saves retain their stored decks/resources/rewards. Updated definitions affect future commands, including the changed Quickstep effect; this is intentional balance evolution, not a promise that old and new combat outcomes remain identical. Save fields and schema are unchanged.

No original-game mechanical parity, richer animation or unattended Unity compilation is claimed in this slice. Those remain separate roadmap work.
