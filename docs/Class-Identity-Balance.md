# Class identity balance diagnostics

Baseline: the 0.3.0 authored content captured from dev 0f6051ab54067a72848a5d8ca568de10ece1ec61. Candidate: the 0.4.0 content. The same fixed policies use seeds 1–24 and route index equal to seed modulo available routes. Report files record input-byte hashes. Baseline and candidate each cover 192 campaigns. These are diagnostic policies, not optimal strategies or human playtests.

| Hero | Reward policy | Baseline wins / 24 | Candidate wins / 24 | Baseline mean ended turns | Candidate mean ended turns |
|---|---|---:|---:|---:|---:|
| Reaver | Rest | 24 | 24 | 16.75 | 20.29 |
| Reaver | Take cards | 24 | 21 | 16.50 | 21.75 |
| Rogue | Rest | 24 | 24 | 18.62 | 16.79 |
| Rogue | Take cards | 24 | 24 | 16.38 | 18.17 |
| Herald | Rest | 24 | 24 | 18.83 | 22.92 |
| Herald | Take cards | 24 | 24 | 16.75 | 26.62 |
| Starseer | Rest | 24 | 24 | 16.08 | 12.50 |
| Starseer | Take cards | 24 | 24 | 14.83 | 10.71 |

An ended turn is an EndTurn command, not an animation duration or wall-clock minute. Failed runs contribute their completed turns and zero final-health ratio. The card-taking policy selects a card except at low health; the rest policy always takes healing. Purchases follow a simple effect-match/price score and may make poor choices. Rules are in UnityTests/Balance/Program.cs so the result can be reproduced and challenged.

Observed: the candidate separates class pacing more than the baseline. Herald sustains over longer fights; Starseer clears substantially faster. Reaver's aggressive reward policy lost three runs while its conservative policy remained viable. These are signals for the next human playtest, not a reason to tune every bot to 100% wins or to claim equal class strength.

Player questions: does Reaver have enough useful defensive options; does Herald feel resilient or merely slow; does Starseer's early damage overshadow other classes; do Rogue's poison turns feel deliberate; and do item prices create a useful choice rather than delayed automatic purchases?

Run the current comparison lane with `dotnet run --project UnityTests/Balance -- GameContent/Unity/campaign.json Builds/Balance.json`. CI also runs 192 current-content campaigns and retains the report. Its gate checks termination; the domain regression suite separately requires each class to have a reachable victory.
