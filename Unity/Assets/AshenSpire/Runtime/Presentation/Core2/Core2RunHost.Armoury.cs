// Core2RunHost.Armoury.cs — the Armoury hub over the run (JS showArmoury / showDeckEditor; desktop-07b, 07e): tabs
// Armory | Edit Deck; the equipped positions with Inspect, Replace and Unequip (a new piece glows until looked at; a
// fight in progress locks every change); the Reading Desk with its tiles, steppers, counter, Undo, Accept and Cancel,
// and ▲ ▼ reordering under Play in deck order. Rules and words: Core2ArmouryModel, Core2DeckEditorModel.
using System.Linq;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    public sealed partial class Core2RunHost
    {
        private int? _armouryRow;
        private bool _armouryReplace;

        private VisualElement Tabs(bool deck)
        {
            var tabs = Core2Ui.Div("c2-tabs");
            tabs.Add(Core2Ui.Btn("hub-armory", Core2Text.T("armoury.hub.armory"), () => { if (_overlay == Overlay.Deck) { _flow.CancelDeckEdit(); } _overlay = Overlay.Armoury; Render(); }, "c2-tab" + (!deck ? " is-on" : "")));
            tabs.Add(Core2Ui.Btn("hub-deck", Core2Text.T("armoury.hub.deck"), () => Open(Overlay.Deck), "c2-tab" + (deck ? " is-on" : ""), !_flow.Session.EditingLocked));
            return tabs;
        }

        private void BuildArmoury(VisualElement screen)
        {
            var m = new Core2ArmouryModel(_flow);
            var door = Core2Ui.Door("", m.Title, m.Locked, wide: true);
            door.Door.Insert(1, Tabs(false));
            var split = Core2Ui.Div("c2-split");
            var list = Core2Ui.Div("c2-pane c2-col");
            list.Add(Core2Ui.Text(m.PositionsTitle, "c2-title-s"));
            for (var i = 0; i < m.Rows.Count; i++)
            {
                var row = m.Rows[i];
                var index = i;
                var tile = Core2Ui.Div("c2-tile" + (row.IsNew ? " c2-glow" : "") + (_armouryRow == i ? " is-selected" : ""));
                var body = Core2Ui.Div("c2-tile-body");
                var title = Core2Ui.Div("c2-row");
                title.Add(Core2Ui.Text(row.Code + " — ", "c2-gold"));
                title.Add(Core2Ui.Text(row.Empty ? "—" : row.Name, "c2-strong"));
                if (row.Chip != null) { var chip = Core2Ui.Text(row.Chip, "c2-chip"); chip.style.marginLeft = 8; title.Add(chip); }
                body.Add(title);
                var actions = Core2Ui.Div("c2-row c2-gap");
                actions.Add(Core2Ui.Btn("armoury-inspect-" + i, m.InspectLabel, () => { _armouryRow = index; _armouryReplace = false; Render(); }, "c2-btn-small", !row.Empty));
                actions.Add(Core2Ui.Btn("armoury-replace-" + i, m.ReplaceLabel, () => { _armouryRow = index; _armouryReplace = true; Render(); }, "c2-btn-small", m.Locked == null));
                actions.Add(Core2Ui.Btn("armoury-unequip-" + i, m.UnequipLabel, () => { m.Unequip(index); _status = m.Status; Render(); }, "c2-btn-small", m.Locked == null && !row.Empty));
                body.Add(actions);
                tile.Add(body);
                list.Add(tile);
            }
            split.Add(list);
            var side = Core2Ui.Div("c2-pane c2-col");
            if (_armouryRow.HasValue && _armouryRow.Value < m.Rows.Count)
            {
                var row = _armouryRow.Value;
                if (_armouryReplace)
                {
                    side.Add(Core2Ui.Text(m.ChooseFor(row), "c2-title-s"));
                    foreach (var (itemId, name, here) in m.Candidates(row))
                    {
                        var id = itemId;
                        var tile = Core2Ui.Tile(null, name, new[] { here ? Core2Text.T("armoury.picker.status.here") : null }, false, here);
                        tile.Add(Core2Ui.Btn("armoury-equip-" + id, m.EquipLabel, () => { m.Equip(row, id); _status = m.Status; _armouryReplace = false; Render(); }, "c2-btn-small", !here && m.Locked == null));
                        side.Add(tile);
                    }
                }
                else
                {
                    var (title, name, lines) = m.Inspect(row);
                    side.Add(Core2Ui.Text(title, "c2-title-s"));
                    var sheet = Core2Ui.Div("c2-sheet");
                    sheet.Add(Core2Ui.Text(name, "c2-title-s"));
                    foreach (var line in lines) sheet.Add(Core2Ui.Text(line, "c2-text"));
                    side.Add(sheet);
                }
            }
            split.Add(side);
            door.Body.Add(split);
            door.Body.Add(StatusLine(m.Status));
            door.Foot.Add(Core2Ui.Btn("armoury-back", m.BackLabel, Close, "c2-btn-fill"));
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door);
        }

        private void BuildDeckEditor(VisualElement screen)
        {
            if (_flow.DeckEdit == null) { _overlay = Overlay.None; return; }
            var m = new Core2DeckEditorModel(_flow);
            var door = Core2Ui.Door(m.Heading, m.Title, m.Counter, wide: true);
            door.Door.Insert(1, Tabs(true));
            var split = Core2Ui.Div("c2-split");
            var library = Core2Ui.Div("c2-pane c2-col");
            foreach (var tile in m.Tiles)
            {
                var key = tile.Key;
                var row = Core2Ui.Div("c2-deck-row");
                var body = Core2Ui.Div("c2-tile-body");
                var top = Core2Ui.Div("c2-row c2-gap");
                top.Add(Core2Ui.Text(tile.Source, "c2-chip"));
                top.Add(Core2Ui.Text(tile.Name, "c2-strong"));
                top.Add(Core2Ui.Text(tile.Cost, "c2-caption"));
                body.Add(top);
                body.Add(Core2Ui.Text(tile.Text, "c2-caption"));
                if (tile.Refusal != null) body.Add(Core2Ui.Text(tile.Refusal, "c2-status"));
                row.Add(body);
                var stepper = Core2Ui.Div("c2-stepper");
                var remove = tile.RemoveInstanceId;
                stepper.Add(Core2Ui.Btn("deck-remove-" + key, "−", () => { m.Remove(remove); _status = m.Status; Render(); }, "", tile.CanRemove, Core2Text.Full("deckEditor.remove", new { name = tile.Name })));
                stepper.Add(Core2Ui.Text(tile.Count, "c2-stepper-count"));
                stepper.Add(Core2Ui.Btn("deck-add-" + key, "+", () => { m.Add(key); _status = m.Status; Render(); }, "", tile.CanAdd, Core2Text.Full("deckEditor.add", new { name = tile.Name })));
                row.Add(stepper);
                library.Add(row);
            }
            split.Add(library);
            if (m.Reorder)
            {
                var order = Core2Ui.Div("c2-pane c2-col");
                order.Add(Core2Ui.Text(m.DrawOrderLabel, "c2-title-s"));
                foreach (var (instanceId, name) in m.Deck)
                {
                    var id = instanceId;
                    var row = Core2Ui.Div("c2-deck-row");
                    row.Add(Core2Ui.Text(name, "c2-fill"));
                    row.Add(Core2Ui.Btn("deck-up-" + id, "▲", () => { m.Move(id, -1); Render(); }, "c2-btn-small", true, Core2Text.Full("deckEditor.up", new { name })));
                    row.Add(Core2Ui.Btn("deck-down-" + id, "▼", () => { m.Move(id, 1); Render(); }, "c2-btn-small", true, Core2Text.Full("deckEditor.down", new { name })));
                    order.Add(row);
                }
                split.Add(order);
            }
            door.Body.Add(split);
            door.Body.Add(StatusLine(m.CounterRefusal ?? ""));
            door.Foot.Add(Core2Ui.Text(m.Keys, "c2-door-note"));
            door.Foot.Add(Core2Ui.Btn("deck-undo", m.UndoLabel, () => { m.Undo(); Render(); }, "", m.CanUndo));
            door.Foot.Add(Core2Ui.Btn("deck-cancel", m.CancelLabel, () => { m.Cancel(); _overlay = Overlay.None; Render(); }));
            door.Foot.Add(Core2Ui.Btn("deck-accept", m.AcceptLabel, () =>
            {
                var error = m.Accept();
                if (error == null) _overlay = Overlay.None; else _status = error;
                Render();
            }, "c2-btn-primary"));
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door);
        }
    }
}
