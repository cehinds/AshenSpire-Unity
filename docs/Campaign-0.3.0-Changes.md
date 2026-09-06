# Campaign 0.3.0 — combat clarity

Tap the enemy intent or status summary to read the current values and timing. Intent explanations include current enemy strength, weakness and your block. Poison timing and block bypass are explicit.

Draw and discard buttons open grouped card contents with counts and current effect descriptions. Draw order stays hidden. Empty piles have an explicit message. Return to combat stays available at the bottom. Inspection never issues a campaign command and clears the previous card selection when returning.

Unaffordable cards can be selected to read their full effect and cost. Play remains disabled and explains the energy requirement; tapping the selected card again cancels it. Recent actions show the latest twelve command outcomes, newest first. Card feedback distinguishes actual vitality damage and absorbed block, capped healing, cards actually drawn and energy gained. Enemy action feedback includes both poison ticks. History is session-local and restarts after player reload; the saved campaign state and schema are unchanged.

Owner edit locations: CampaignSession.cs owns effect results and rule explanations; CampaignView.cs owns read-only screens and semantic input; Expedition.uss owns their styles. Diagnostics report label text and control bounds only in development, allowing browser tests to check explanations and interactions against actual state.

No combat rules, card definitions or reward balance changed. Physical-device validation is still outstanding. Web, Windows and Android builds must be rebuilt for this version before publication.
