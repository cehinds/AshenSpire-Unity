# Campaign 0.4.0 — class identity and rewards

Four-to-five affinity cards now appear in each new eight-card starter deck, up from two signature cards. Reaver develops strength/heavy attacks; Rogue applies poison and weakness; Herald mixes faith healing/protection with attacks; Starseer draws into magic. Seven new composed cards bring the catalog to 25.

New rewards contain two cards matching the hero's RewardTags and one CommonRewardTag card, with deterministic, non-overlapping pools. The screen labels each category and shows how many copies are already in the deck. Existing saved decks and pending reward IDs remain untouched.

Rage Band, Venom Vial and Sunward Seal expand equipment to nine items. Poison/heal modifiers use the existing tag query pattern, and card descriptions/results reflect them. Shared equipment costs more; the forge reports current deck relevance. Quickstep now grants four block without drawing, so free copies consume hand space.

The browser test enters a fixed seed through the real input, claims an affinity reward, checks every offer and can switch from a previous exported Web player to the current one on the same origin to verify a real save upgrade. CSV imports now use explicit LF line endings to avoid formatting drift on Windows.

This is a balance iteration, not a declaration that all classes are equally strong or fun. Fixed-policy diagnostics and human playtesting serve different purposes. Physical mobile testing, richer animation and the original game's unported mechanics remain separate work.
