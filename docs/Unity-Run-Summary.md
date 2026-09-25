# Unity end-of-run summary (F11 / US-11.2)

**Integration status: wired; compile-verified against Unity reference assemblies; needs editor play test.**

The Unity Victory/Defeat screen now renders the full HTML end screen
(`src/ui/screens/gameover.js`) from `RunSummary`, in solo and co-op. The UI
code compiles against the Unity reference assemblies
(`node tools/unity-runtime-check.mjs`), but nobody has yet looked at it in the
Unity editor or a rebuilt player.

## What was added

| File | Purpose |
| --- | --- |
| `Unity/Assets/AshenSpire/Runtime/Domain/Original/Summary/RunSummary.cs` | Immutable view-model with the raw numbers and labels ready to display |
| `Unity/Assets/AshenSpire/Runtime/Domain/Original/Summary/RunSummaryTracker.cs` | Read-only observer on `OriginalGameSession.Changed`. It saves the summary the first time the run reaches Victory or Defeat |
| `UnityTests/RunSummary/` | Console checks: `dotnet run --project UnityTests/RunSummary` (run from the repo root) |

Both files are in the `AshenSpire.Original` assembly, which has no engine
references. They sit in a `Summary/` subfolder so that the companion's native
source list (`Published/Companion.build.json`, checked by
`tools/validate-companion.py`) stays unchanged.

## Entry point

```csharp
// When the run starts (or after OriginalGameSession.Restore):
var tracker = new RunSummaryTracker(game);            // game: OriginalGameSession
tracker.Completed += summary => { /* show the end screen */ };

// When the run ends, after recording it in the profile:
var finish = profile.Finish(runId, game.RunPlayer, tracker.Summary.Victory);
var summary = tracker.AttachEarned(((JArray)finish["newUnlocks"]).Values<string>());

// Without a tracker (for example, a finished save loaded later):
var summary = RunSummary.FromSession(game, newUnlockIds);
var summary = RunSummary.FromRun(snapshot["run"] as JObject, catalog);
```

A restored session that is already finished fills `tracker.Summary`
immediately. `summary.Lines()` returns a plain-text version of the screen, from
top to bottom.

## What it shows (parity with `src/ui/screens/gameover.js`)

| HTML element | `RunSummary` member | Wording |
| --- | --- | --- |
| page door eyebrow / title | `DoorEyebrow`, `DoorTitle` | "The climb" / "Victory"; "The climb ends" / "Defeat" |
| Title·L (+ `data-tone="loss"`) | `Title`, `AriaLabel`, `TitleTone` | "Ember restored"; "You perished" (CSS shows it as YOU PERISHED, SPEC §7.4) |
| DetailCard | `CardEyebrow`, `CardName`, `CardLine`, `CardMeta` | "Forsaken"; `name glyph` (name defaults to Forsaken); "Floor N / M · K fight(s) won"; "Seed S" |
| StatStrip chips | `Stats[]` (`Label`, `Value`) | Damage dealt, Damage taken, Cinders, Final HP (`hp / maxHp`, 0 on defeat) |
| Earned card | `EarnedEyebrow`, `Earned[].Line` | "Earned"; `unlock name` + space + `kind` |
| Final deck | `DeckEyebrow`, `DeckTitle`, `Deck[]` (`Glyph`, `Name`) | "Final deck"; "N card(s)"; ✦ upgraded / ◆ not |
| buttons | `HistoryLabel`, `ReturnLabel` | "Run history", "Return to title" |

The summary also carries `ClassId`, `ClassName`, `Act` and `BossesBeaten`,
because the HTML run-history record (`src/main.js` `runResult`) stores them.
`ToRecord()` returns exactly the fields of the `OriginalProfile.Finish` result.

**Not included, because the HTML does not have them:** score (the HTML does
not calculate one), playtime, highest hit, cards played, cinders earned or spent
(the chip shows the current balance, `game.cinders`), elites/bosses slain
counts, relics and cause of death. Adding any of these would first need a SPEC
change (see AGENTS.md, "The spec wins").

**Quirk kept from the HTML:** on a victory, the floor line reads
`Floor 13 / 12`. The boss node sits at `floors + 1` in both `mapgen.js` and
`ActMapGenerator.cs`, and the HTML prints `game.floor / mapGraph.floors`
unchanged.

## Where the numbers come from

`damageDealt` and `damageTaken` are totals of the same combat events the HTML
`trackStats` sums (`damageDealt` with `sourceId == "player"`, `hpLost` with
`targetId == "player"`). `OriginalGameSession.CommitCombat` already folds them
into `run.stats` when a fight ends. `fightsWon` is counted by
`OriginalRunSession.CompleteCombat`. So the summary reads the committed run
state, and its numbers always match the saved run (the F11 acceptance
criterion). The tracker never issues commands, never reads or advances an RNG
stream, and never writes the save.

## Verification

`UnityTests/RunSummary` checks:

- the wording, pluralisation, defaults and immutability of every label;
- a seeded Reaver run to Victory and a seeded Starseer run to Defeat. Each is
  driven in lockstep on two sessions, one with a tracker and one without. After
  every command, the snapshots (including RNG counters) and `LastEvents` are
  byte-identical;
- the summary against the saved run, the `OriginalProfile.Finish` record, the
  earned unlocks and a restored finished save.

## UI wiring

| File | Change |
| --- | --- |
| `Runtime/Presentation/RunSummaryView.cs` (new) | Builds the end screen: page door head, title (upper-cased, danger tone on defeat), ornament, detail card, stat chips, "Earned" card, "Final deck" head and strip, button row. `FromCoopView` builds a member's summary from a completed co-op view |
| `Resources/RunSummary.uss` (new) | Styles, with values taken from `styles/kit.css` and `styles/base.css`. The view adds it to the panel root |
| `Runtime/Presentation/CampaignView.RunSummary.cs` (new) | `NativeSummary` property that carries the summary from the controller to the panel |
| `Runtime/Application/RunController.Summary.cs` (new) | Creates the `RunSummaryTracker` and attaches the earned unlocks |
| `RunController.cs` (3 hook lines) | `BindOriginal` creates the tracker before it subscribes `RefreshOriginal`, so the summary is saved first. `RefreshOriginal` passes the `Finish` result to `AttachSummaryUnlocks`, then sets `_view.NativeSummary` |
| `CampaignView.cs` (1 line) | Passes `NativeSummary` and a "Run history" handler (`ProfileRequested`, the Chronicle) to `OriginalRunPanel` |
| `OriginalRunPanel.cs` | On Victory/Defeat it skips the status header and mounts `RunSummaryView`. If no summary was passed, it falls back to `RunSummary.FromSession`. The existing `native-deck` and `native-menu` buttons keep their names and move into the button row. On this screen `native-menu` reads "Return to title" (the HTML wording) and is primary |
| `OriginalCoopPanel.cs` (1 line) | The `complete` scene mounts the same view (without "Run history" or earned unlocks, which the co-op panel does not receive). If the summary cannot be built, it keeps the old text |

Named elements for playtests: `native-run-summary`, `native-run-summary-title`,
`native-run-summary-card`, `native-run-summary-seed`,
`native-run-summary-stats`, `native-run-summary-stat-<id>`,
`native-run-summary-earned`, `native-run-summary-deck`,
`native-run-summary-buttons`, `native-run-history`.

## Remaining work (needs the Unity editor)

1. Play a solo run to Victory and to Defeat, and a co-op run to completion, in
   the editor. Check the layout at phone and desktop sizes, and check that
   "Run history" opens the Chronicle.
2. Rebuild the player. Any new file under `Unity/Assets` changes the source
   digest, so `node tools/unity-package.mjs --check` reports "built from
   different source" until the packaged build is regenerated.
3. Add the RunSummary project to the CI workflow (the workflows were
   deliberately left unchanged).
