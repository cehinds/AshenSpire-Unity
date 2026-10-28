// Core2FinishModel.cs — the finished-run door (JS src/ui/screens/gameover.js mountGameOver, src/main.js showFinishedRun):
// Victory / Defeat, the Forsaken's line (floor, fights won, seed), the stat chips, what the run unlocked (the class
// ledger, recorded once by Core2RunFlow), the final deck, and Run again / Return to title. Engine-free.
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2FinishModel
    {
        public bool Victory { get; }
        public string Eyebrow { get; }
        public string Title { get; }
        public string Heading { get; }
        public string IdentityEyebrow => "Forsaken";
        public string Name => "Forsaken";
        public string Line { get; }
        public string Meta { get; }
        /// <summary>Key/value chips. JS also shows Damage dealt and Damage taken, which a Core 2 run does not record yet.</summary>
        public List<(string Key, string Value)> Chips { get; } = new List<(string, string)>();
        public List<string> Earned { get; } = new List<string>();
        public string EarnedLabel => "Earned";
        public string DeckEyebrow => "Final deck";
        public string DeckTitle { get; }
        public List<string> Deck { get; } = new List<string>();
        public string RunAgainLabel => Core2Text.T("gameover.runAgain");
        public string TitleLabel => "Return to title";
        public string ClassId { get; }

        public Core2FinishModel(Core2RunFlow flow)
        {
            var run = flow.Session.Run;
            Victory = run.Finish?.Victory == true;
            ClassId = run.Class;
            Eyebrow = Victory ? "The climb" : "The climb ends";
            Title = Victory ? "Victory" : "Defeat";
            Heading = Victory ? "Ember restored" : "You perished";
            var floors = run.MapGraph != null ? " / " + run.MapGraph.Floors : "";
            Line = "Floor " + run.Floor + floors + " · " + run.FightsWon + " fight" + (run.FightsWon == 1 ? "" : "s") + " won";
            Meta = "Seed " + Core2Seed.ToText(run.Seed);
            Chips.Add(("Cinders", run.Cinders.ToString()));
            Chips.Add(("Final HP", (Victory ? run.Hp : 0) + " / " + run.MaxHp));
            Earned.AddRange(flow.Earned.Select(name => name + " · Class"));
            DeckTitle = run.Deck.Count + " card" + (run.Deck.Count == 1 ? "" : "s");
            Deck.AddRange(run.Deck.Select(c => flow.Content.HasCard(c.CardId) ? flow.Content.Card(c.CardId).Name : c.CardId));
        }
    }
}
