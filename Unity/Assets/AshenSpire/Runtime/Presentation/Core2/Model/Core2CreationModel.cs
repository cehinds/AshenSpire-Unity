// Core2CreationModel.cs — Create character for a Core 2 run (JS src/ui/screens/customize.js with the game profile's
// class-selection module): step 1 Class (the profile's ledger locks classes: "Play 1 game to unlock."), step 2 Review
// (seed, destination slot, the class's fixed six-point attributes and pools, its armour, main hand and relic, the
// starting deck), then Begin. A Core 2 class's attributes are fixed by the class (six points, its primary at 2):
// creation shows them; it does not spend them. Engine-free.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Core2;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2ClassCard
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Training { get; set; }
        public bool Locked { get; set; }
        public string LockText { get; set; }
        public List<(string Key, string Value)> Stats { get; } = new List<(string, string)>();
        public string Relic { get; set; }
    }

    public sealed class Core2CreationModel
    {
        public string Title => Core2Text.T("creation.title");
        public string ClassStep => Core2Text.T("creation.category.class");
        public string ReviewStep => Core2Text.T("creation.category.review");
        public string NextLabel => Core2Text.T("creation.next");
        public string BackLabel => Core2Text.T("common.back");
        public string BeginLabel => "Begin";
        public string SeedLabel => "Seed";
        public string SeedHint => "The same seed produces the same climb.";
        public string DestinationLabel => Core2Text.T("creation.review.destination");
        public string DestinationHint => Core2Text.T("creation.review.destination.hint");
        public List<Core2ClassCard> Classes { get; } = new List<Core2ClassCard>();
        public string Step { get; private set; } = "class";
        public string Selected { get; private set; }
        public string Seed { get; set; }
        public int Slot { get; }
        public string Status { get; private set; } = "";
        private readonly Core2RunFlow _flow;

        public Core2CreationModel(Core2RunFlow flow, int slot, uint randomSeed)
        {
            _flow = flow;
            Slot = slot;
            Seed = Core2Seed.ToText(randomSeed);
            var data = flow.Data;
            foreach (var cls in data.Content.Classes)
            {
                var (run, _) = Core2RunCreation.Create(data, cls.Id, 0, ignoreUnlocks: true);
                var card = new Core2ClassCard
                {
                    Id = cls.Id, Name = cls.Name, Description = cls.Description ?? "", Training = (string)data.AdeptTraining(cls.Id)?["label"],
                    Locked = !flow.ClassAvailable(cls.Id), Relic = run.Relics.Where(data.HasRelic).Select(id => (string)data.Relic(id)["name"]).FirstOrDefault(),
                };
                if (card.Locked) card.LockText = Core2Text.T("creation.class.unlockNextRun");
                card.Stats.Add(("HP", run.MaxHp.ToString(CultureInfo.InvariantCulture)));
                card.Stats.Add(("SP", run.MaxStamina.ToString(CultureInfo.InvariantCulture)));
                card.Stats.Add(("MP", run.MaxMana.ToString(CultureInfo.InvariantCulture)));
                card.Stats.Add(("HAND", Hand(data, run).ToString(CultureInfo.InvariantCulture)));
                card.Stats.Add(("DRAW", run.DrawPerTurn.ToString(CultureInfo.InvariantCulture)));
                Classes.Add(card);
            }
            Selected = Classes.FirstOrDefault(c => !c.Locked)?.Id;
        }

        /// <summary>The hand size a class starts with (profile cards.handBase + its hand attribute).</summary>
        public static int Hand(Core2RunData data, Core2Run run)
        {
            var cards = data.Module("cards");
            var b = Core2Json2.Int(cards?["handBase"], 4);
            var attr = Core2Json2.Str(cards?["handAttribute"]) ?? "intelligence";
            return b + run.AttributeValue(attr);
        }

        public void Choose(string classId)
        {
            var card = Classes.FirstOrDefault(c => c.Id == classId);
            if (card == null || card.Locked) { Status = card?.LockText ?? ""; return; }
            Selected = classId; Status = "";
        }

        public void Next() { if (Selected != null) Step = "review"; }
        public void Back() { Step = "class"; Status = ""; }

        /// <summary>The Review step's character sheet and gear for the selected class.</summary>
        public (List<(string Key, int Value)> Attributes, List<(string Key, string Value)> Pools, List<(string Label, string Name)> Gear, List<string> Deck, string Destination, string BeginTip) Review()
        {
            var data = _flow.Data;
            var (run, _) = Core2RunCreation.Create(data, Selected, 0, ignoreUnlocks: true);
            var attributes = Core2Run.AttributeIds.Select(id => (Core2Text.AttributeShort(id), run.AttributeValue(id))).ToList();
            var card = Classes.First(c => c.Id == Selected);
            var pools = card.Stats.ToList();
            var gear = new List<(string, string)>();
            var armour = run.Loadout.Equipped("armor");
            if (armour != null) gear.Add(("ARMOUR", (string)data.Armour(run.Class, armour)?["name"] ?? armour));
            var main = run.Loadout.Equipped("rightHand");
            if (main != null) gear.Add(("MAIN HAND", (string)data.Armament(main)?["name"] ?? main));
            var off = run.Loadout.Equipped("leftHand");
            if (off != null) gear.Add(("OFF HAND", (string)data.Armament(off)?["name"] ?? off));
            // JS Review shows the class's starting relic (the first); the universal starter rides along unshown.
            var relic = run.Relics.FirstOrDefault(data.HasRelic);
            if (relic != null) gear.Add(("RELIC", (string)data.Relic(relic)["name"]));
            var deck = run.Deck.Select(c => data.Content.HasCard(c.CardId) ? data.Content.Card(c.CardId).Name : c.CardId).ToList();
            return (attributes, pools, gear, deck, Core2Text.T("creation.review.slot", new { slot = Slot + 1 }), "Begin as " + card.Name + ".");
        }

        /// <summary>Begin: the seed is checked (JS seedProblem) and the run starts in the slot, saved.</summary>
        public string Begin()
        {
            var error = _flow.Begin(Selected, Seed, Slot);
            Status = error ?? "";
            return error;
        }
    }
}
