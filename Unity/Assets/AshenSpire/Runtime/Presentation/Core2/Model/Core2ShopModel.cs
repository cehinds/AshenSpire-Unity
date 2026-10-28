// Core2ShopModel.cs — the configured merchant (JS src/ui/screens/shop.js mountConfiguredShop, merchantOffer.js
// arrangeMerchantOffer): one shelf per offer kind in stock order (Armaments, Relics, Combat cards, Weapon arts, Flasks,
// Armour), one flat price, Buy behind a review, Leave. Engine-free.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2ShopOffer
    {
        public int Index { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        /// <summary>JS desc: the definition's textTemplate as authored (the configured shop does not fill it).</summary>
        public string Desc { get; set; }
        public string Price { get; set; }
        public bool Available { get; set; }
        /// <summary>The unavailable line (JS shop-offer-avail), null when available.</summary>
        public string Reason { get; set; }
        public string BuyLabel { get; set; }
        public string ActionLabel { get; set; }
        public string InspectLabel { get; set; }
        public string InspectText { get; set; }
    }

    public sealed class Core2ShopModel
    {
        public string Title { get; private set; }
        public string LeaveLabel { get; private set; }
        public List<(string Kind, string Label)> Categories { get; } = new List<(string, string)>();
        public string Active { get; private set; }
        public string PaneHead => Categories.FirstOrDefault(c => c.Kind == Active).Label ?? Title;
        public List<Core2ShopOffer> Offers { get; } = new List<Core2ShopOffer>();
        public IEnumerable<Core2ShopOffer> Shelf => Offers.Where(o => o.Kind == Active);
        public string Status { get; private set; } = "";
        private readonly Core2RunFlow _flow;

        public Core2ShopModel(Core2RunFlow flow) { _flow = flow; Refresh(); }

        public void Refresh()
        {
            var data = _flow.Data;
            var run = _flow.Session.Run;
            var stock = _flow.Session.ShopStock;
            Title = Core2Text.T("shop.title");
            LeaveLabel = Core2Text.T("shop.leave");
            Categories.Clear(); Offers.Clear();
            foreach (var kind in stock.Options.Select(o => o.Kind).Distinct())
                Categories.Add((kind, Core2Text.T(Core2Offers.KindLabelId(kind))));
            if (Active == null || Categories.All(c => c.Kind != Active)) Active = Categories.FirstOrDefault().Kind;
            var price = stock.Price ?? 0;
            for (var i = 0; i < stock.Options.Count; i++)
            {
                var option = Core2Offers.Option(data, run, stock, i);
                var flaskOk = option.Kind != "potions" || FlaskAcquirable(data, run, option.Id);
                var taken = stock.Taken.Contains(i);
                var available = !taken && !stock.Claimed.HasValue && run.Cinders >= price && flaskOk;
                string desc = option.Kind == "combatCards" || option.Kind == "weaponArts" ? (data.Content.HasCard(option.Id) ? data.Content.Card(option.Id).TextTemplate : "")
                    : option.Kind == "relics" && data.HasRelic(option.Id) ? (string)data.Relic(option.Id)["textTemplate"]
                    : option.Kind == "potions" && data.HasFlask(option.Id) ? (string)data.Flask(option.Id)["textTemplate"] : "";
                Offers.Add(new Core2ShopOffer
                {
                    Index = i, Kind = option.Kind, Name = option.Name, Desc = (desc ?? "").Trim(), Price = Core2Text.T("shop.price", new { cost = price }),
                    Available = available, Reason = available ? null : taken ? Core2Text.T("shop.avail.locked") : Core2Text.T("shop.avail.cinders"),
                    BuyLabel = Core2Text.T("shop.book.buy"), ActionLabel = Core2Text.T("shop.action.buy", new { cost = price }),
                    InspectLabel = Core2Text.T("deckEditor.inspectAction"), InspectText = option.Text,
                });
            }
        }

        /// <summary>JS flaskAcquisitionPlan.ok: a free flask slot, or a charge flask the run can take.</summary>
        private static bool FlaskAcquirable(Core2RunData data, Core2Run run, string flaskId)
        {
            var draft = Core2RunSave.Clone(run);
            try { Core2RunRewards.AcquireFlask(data, draft, flaskId); return true; }
            catch (InvalidOperationException) { return false; }
        }

        public void Show(string kind) { if (Categories.Any(c => c.Kind == kind)) Active = kind; }

        /// <summary>The purchase review (JS shop.configured.buy): question, target, message, confirm label.</summary>
        public (string Question, string Target, string Message, string Confirm, string Back) Review(int index)
        {
            var offer = Offers[index];
            var run = _flow.Session.Run;
            var price = _flow.Session.ShopStock.Price ?? 0;
            return (Core2Text.T("shop.configured.buy"), offer.Name, Core2Text.T("shop.configured.buy.message", new { cost = price, cinders = run.Cinders, left = run.Cinders - price }),
                Core2Text.T("shop.buy.confirm"), Core2Text.T("shop.back"));
        }

        public string Buy(int index)
        {
            var error = _flow.Buy(index);
            Status = error ?? "";
            if (error == null) Refresh();
            return error;
        }

        public string Leave() => _flow.LeaveShop();
    }
}
