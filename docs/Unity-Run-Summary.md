# Unity end-of-run summary (F11 / US-11.2)

**Integration status: domain ready, UI wiring pending (needs Unity editor).**

The Unity Victory/Defeat screen (`Presentation/OriginalRunPanel.cs`) shows one
line of text. The domain now produces the complete run summary that the HTML
end screen shows. The screen itself has not been wired up yet.

## What was added

| File | Purpose |
| --- | --- |
| `Unity/Assets/AshenSpire/Runtime/Domain/Original/Summary/RunSummary.cs` | Immutable view-model with the raw numbers and labels ready to display |
| `Unity/Assets/AshenSpire/Runtime/Domain/Original/Summary/RunSummaryTracker.cs` | Read-only observer on `OriginalGameSession.Changed`. It saves the summary the first time the run reaches Victory or Defeat |
| `UnityTests/RunSummary/` | Console checks: `dotnet run --project UnityTests/RunSummary` (run from the repo root) |

Both files are in the `AshenSpire.Original` assembly, which has no engine
references. They sit in a `Summary/` subfolder so that the companion's native
source list (`Published/Companion.build.json`, checked by
`tools/validate-companion.py`) stays unchanged. No existing file was edited.

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

## Remaining work (needs the Unity editor)

1. In `OriginalRunPanel`, replace the single `Victory`/`Defeat` line with a
   layout that renders `RunSummary`. Create the tracker when the panel receives
   its session.
2. Call `AttachEarned` wherever the panel records the run in `OriginalProfile`.
3. Rebuild the player. Any new file under `Unity/Assets` changes the source
   digest, so `node tools/unity-package.mjs --check` reports "built from
   different source" until the packaged build is regenerated.
4. Add the RunSummary project to the CI workflow (the workflows were
   deliberately left unchanged).
