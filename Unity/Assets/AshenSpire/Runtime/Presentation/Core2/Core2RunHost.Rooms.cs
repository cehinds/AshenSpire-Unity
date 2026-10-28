// Core2RunHost.Rooms.cs — the rooms a node opens: the merchant (JS mountConfiguredShop: portrait, Merchant frame,
// shelf of offers with Inspect and Buy, a category dock, Leave), the rest site (JS mountGameProfileRest: scene, Shrine
// of Ember, "HP and MP fully restored.", Smith with its upgrade preview, Level Up, Continue) and the event (JS mountEvent
// / mountDialogue: stage with the speaker, the narrative, the responses, the result, Continue or Steel yourself).
using System;
using System.Linq;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    public sealed partial class Core2RunHost
    {
        private int? _review;
        private int? _shopInspect;
        private string _restPanel;
        private string _restPick;

        private bool Narrow => _root.ClassListContains("layout-narrow");

        // ---- the merchant ----------------------------------------------------------------------------------------

        private void BuildShop(VisualElement screen)
        {
            if (_shop == null) _shop = new Core2ShopModel(_flow); else _shop.Refresh();
            var m = _shop;
            var room = Core2Ui.Div("c2-room");
            var portrait = Core2Ui.Div("c2-room-scene");
            Core2Ui.Surface(portrait, Narrow ? "Art/js1815/ui/scene-merchant-mobile" : "Art/js1815/ui/scene-merchant-desktop");
            room.Add(portrait);
            var frame = Core2Ui.Div("c2-room-panel");
            var head = Core2Ui.Div("c2-door-head");
            head.Add(Core2Ui.Text(m.Title, "c2-title-m"));
            var close = Core2Ui.Btn("shop-close", "", () => Run(m.Leave), "c2-icon-btn", tooltip: m.LeaveLabel);
            Core2Ui.Surface(close, "Art/js1815/ui/icon-close");
            head.Add(close);
            frame.Add(head);
            frame.Add(Core2Ui.Text(m.PaneHead, "c2-pane-head"));
            var shelf = new ScrollView(ScrollViewMode.Vertical);
            Core2Ui.Classes(shelf, "c2-fill");
            foreach (var offer in m.Shelf)
            {
                var index = offer.Index;
                var tile = Core2Ui.Div("c2-tile" + (offer.Available ? "" : " is-locked"));
                var thumb = Core2Ui.Btn("shop-inspect-" + index, "", () => { _shopInspect = _shopInspect == index ? (int?)null : index; Render(); }, "c2-thumb", tooltip: Core2Text.T("deckEditor.inspectNamed", new { name = offer.Name }));
                Core2Ui.Surface(thumb, OfferArt(offer.Kind));
                thumb.Add(Core2Ui.Text(offer.InspectLabel, "c2-thumb-label"));
                tile.Add(thumb);
                var body = Core2Ui.Div("c2-tile-body");
                body.Add(Core2Ui.Text(offer.Name, "c2-tile-title"));
                if (offer.Desc.Length > 0) body.Add(Core2Ui.Text(offer.Desc, "c2-tile-line"));
                if (_shopInspect == index && offer.InspectText.Length > 0) body.Add(Core2Ui.Text(offer.InspectText, "c2-caption"));
                if (offer.Reason != null) body.Add(Core2Ui.Text(offer.Reason, "c2-status"));
                tile.Add(body);
                var actions = Core2Ui.Div("c2-tile-actions");
                actions.Add(Core2Ui.Btn("shop-buy-" + index, offer.BuyLabel, () => { _review = index; Render(); }, "c2-btn-primary", offer.Available, offer.ActionLabel));
                actions.Add(Core2Ui.Text(offer.Price, "c2-offer-price"));
                tile.Add(actions);
                shelf.Add(tile);
            }
            frame.Add(shelf);
            frame.Add(StatusLine(m.Status));
            var dock = Core2Ui.Div("c2-shop-dock");
            foreach (var (kind, label) in m.Categories)
                dock.Add(Core2Ui.Btn("shop-category-" + kind, label, () => { m.Show(kind); _shopInspect = null; Render(); }, kind == m.Active ? "is-on" : ""));
            frame.Add(dock);
            var foot = Core2Ui.Div("c2-door-foot");
            foot.Add(Core2Ui.Btn("leave-shop", m.LeaveLabel, () => Run(m.Leave), "c2-btn-fill"));
            frame.Add(foot);
            room.Add(frame);
            screen.Add(room);
            if (_review.HasValue) ShopReview(screen, m, _review.Value);
        }

        /// <summary>JS purchase review (beatArmer shopBuy): question, the offer, what it costs, BUY IT / Back.</summary>
        private void ShopReview(VisualElement screen, Core2ShopModel m, int index)
        {
            var r = m.Review(index);
            var door = Core2Ui.Door("", r.Question);
            door.Body.Add(Core2Ui.Text(r.Target, "c2-title-s"));
            door.Body.Add(Core2Ui.Text(r.Message, "c2-text"));
            door.Foot.Add(Core2Ui.Btn("shop-review-back", r.Back, () => { _review = null; Render(); }));
            door.Foot.Add(Core2Ui.Btn("shop-review-buy", r.Confirm, () => { _review = null; Run(() => m.Buy(index)); Render(); }, "c2-btn-primary"));
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door, "shop-review-buy");
        }

        // ---- the rest site ---------------------------------------------------------------------------------------

        private void BuildRest(VisualElement screen)
        {
            if (_rest == null) _rest = new Core2RestModel(_flow); else _rest.Refresh();
            var m = _rest;
            var room = Core2Ui.Div("c2-room");
            var scene = Core2Ui.Div("c2-room-scene");
            Core2Ui.Surface(scene, "Art/js1815/ui/scene-chapel-rest");
            room.Add(scene);
            var panel = Core2Ui.Div("c2-room-panel");
            var head = Core2Ui.Div("c2-door-head");
            head.Add(Core2Ui.Text(m.Title, "c2-title-m"));
            head.Add(Core2Ui.Text(m.Status, "c2-caption"));
            panel.Add(head);
            var body = Core2Ui.Div("c2-door-body");
            body.Add(Core2Ui.Text(m.Restored, "c2-text c2-muted"));
            foreach (var service in m.Services)
            {
                var id = service.Id;
                var tile = Core2Ui.Tile(id == "smith" ? "Art/js1815/ui/icon-smith" : "Art/js1815/journey/icon-progression", service.Title, new[] { service.Line }, !service.Available);
                tile.Add(Core2Ui.Btn(id == "smith" ? "smith-opt" : "level-opt", service.Title, () => { _restPanel = id; _restPick = null; Render(); }, "c2-btn-small", service.Available));
                body.Add(tile);
            }
            body.Add(StatusLine(m.Error));
            panel.Add(body);
            var foot = Core2Ui.Div("c2-door-foot");
            foot.Add(Core2Ui.Btn("shrine-leave", m.ContinueLabel, () => Run(m.Leave), "c2-btn-primary c2-btn-fill"));
            panel.Add(foot);
            room.Add(panel);
            screen.Add(room);
            if (_restPanel == "smith") SmithModal(screen, m);
            else if (_restPanel == "levelUp") LevelModal(screen, m);
        }

        /// <summary>JS mountSmithUpgradeModal: the candidates, the selected upgrade's changes, the consequence, Confirm.</summary>
        private void SmithModal(VisualElement screen, Core2RestModel m)
        {
            var door = Core2Ui.Door(m.SmithHeading.ToUpperInvariant(), m.SmithTitle, m.SmithBadge);
            foreach (var row in m.Smith)
            {
                var itemRef = row.ItemRef;
                var tile = Core2Ui.Tile("Art/js1815/ui/icon-smith", row.Name + "  " + row.Tier, row.Changes, false, _restPick == itemRef);
                tile.Add(Core2Ui.Btn("smith-pick-" + itemRef.Replace('/', '-'), row.Name, () => { _restPick = itemRef; Render(); }, "c2-btn-small"));
                door.Body.Add(tile);
            }
            door.Body.Add(Core2Ui.Text(m.SmithConsequence, "c2-caption"));
            door.Foot.Add(Core2Ui.Btn("smith-back", Core2Text.T("common.back"), () => { _restPanel = null; Render(); }));
            door.Foot.Add(Core2Ui.Btn("smith-confirm", Core2Text.T("common.confirm"), () => { var pick = _restPick; _restPanel = null; Run(() => m.DoSmith(pick)); Render(); }, "c2-btn-primary", _restPick != null));
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door);
        }

        /// <summary>JS renderStatAllocationCard (Level Up): one point onto one attribute, then Assign point.</summary>
        private void LevelModal(VisualElement screen, Core2RestModel m)
        {
            var door = Core2Ui.Door(m.LevelHeading.ToUpperInvariant(), m.LevelTitle);
            door.Body.Add(Core2Ui.Text(m.LevelNote, "c2-text c2-muted"));
            foreach (var (id, shortLabel, label, value) in m.Attributes)
            {
                var row = Core2Ui.Div("c2-deck-row");
                row.Add(Core2Ui.Text(shortLabel, "c2-stat-key"));
                row.Add(Core2Ui.Text(label, "c2-fill"));
                var stepper = Core2Ui.Div("c2-stepper");
                var chosen = _restPick == id;
                stepper.Add(Core2Ui.Btn("level-dec-" + id, "−", () => { _restPick = null; Render(); }, "", chosen));
                stepper.Add(Core2Ui.Text((value + (chosen ? 1 : 0)).ToString(), "c2-stepper-count"));
                stepper.Add(Core2Ui.Btn("level-inc-" + id, "+", () => { _restPick = id; Render(); }, "", _restPick == null));
                row.Add(stepper);
                door.Body.Add(row);
            }
            door.Foot.Add(Core2Ui.Btn("level-cancel", Core2Text.T("common.cancel"), () => { _restPanel = null; Render(); }));
            door.Foot.Add(Core2Ui.Btn("level-confirm", m.LevelConfirm, () => { var pick = _restPick; _restPanel = null; Run(() => m.DoLevelUp(pick)); Render(); }, "c2-btn-primary", _restPick != null));
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door);
        }

        // ---- the event -------------------------------------------------------------------------------------------

        private void BuildEvent(VisualElement screen)
        {
            if (_event == null) _event = new Core2EventModel(_flow); else _event.Refresh();
            var m = _event;
            var stage = Core2Ui.Div("c2-event-stage");
            Core2Ui.Surface(stage, "Art/js1815/ui/scene-grave-dialogue");
            var player = Core2Ui.Div("c2-event-portrait");
            player.Add(Core2Ui.Text(m.PlayerLabel, "c2-event-speaker"));
            stage.Add(player);
            if (m.Spoken)
            {
                var speaker = Core2Ui.Div("c2-event-portrait");
                Core2Ui.Surface(speaker, "Art/Original/enemy_" + m.PortraitKey);
                speaker.Add(Core2Ui.Text(m.SpeakerName, "c2-event-speaker"));
                stage.Add(speaker);
            }
            screen.Add(stage);
            var door = Core2Ui.Div("c2-event-door");
            var head = Core2Ui.Div("c2-row");
            var titles = Core2Ui.Div("c2-col c2-fill");
            titles.Add(Core2Ui.Text(m.Eyebrow.ToUpperInvariant(), "c2-micro"));
            titles.Add(Core2Ui.Text(m.Title, "c2-title-s"));
            head.Add(titles);
            head.Add(Core2Ui.Text(m.Status, "c2-caption"));
            door.Add(head);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            Core2Ui.Classes(scroll, "c2-fill");
            scroll.Add(Core2Ui.Text(m.Spoken ? m.Caption : m.Resolved ? m.Text : m.Text, "c2-lead"));
            if (m.Resolved && !m.Spoken) scroll.Add(Core2Ui.Text(m.Result, "c2-lead c2-muted"));
            if (m.ResponsesShown)
                foreach (var choice in m.Choices)
                {
                    var id = choice.Id;
                    var text = choice.Label + (choice.Tag != null ? "   · " + choice.Tag : "");
                    scroll.Add(Core2Ui.Btn("event-choice-" + id, text, () => Run(() => m.Choose(id)), "c2-event-choice", choice.Enabled, choice.Question));
                }
            scroll.Add(StatusLine(m.Error));
            door.Add(scroll);
            screen.Add(door);
            var foot = Core2Ui.Div("c2-event-foot");
            if (m.Spoken)
            {
                foot.Add(Core2Ui.Btn("dialogue-back", m.BackLabel, () => { m.Back(); Render(); }, "", m.BackEnabled));
                foot.Add(Core2Ui.Btn("dialogue-skip", m.SkipLabel, () => { m.SkipSpeech(); Render(); }, "", m.SkipEnabled));
            }
            foot.Add(Core2Ui.Btn("event-continue", m.ContinueLabel, () => Run(() => { var e = m.Continue(); if (e == null && _flow.Screen == AshenSpire.Domain.Core2.Core2Screen.Event) Render(); return e; }), "c2-btn-primary", m.ContinueEnabled, m.ContinueReason));
            screen.Add(foot);
            if (!m.ContinueEnabled && m.ContinueReason != null) screen.Add(Core2Ui.Text(m.ContinueReason, "c2-caption"));
        }
    }
}
