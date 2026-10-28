// Core2DeckEditorModel.cs — the Reading Desk (JS src/ui/screens/deckEditor.js over src/model/deckEdit.js): the
// collection tiles with their steppers, the deck counter "{count} / {min}–{max}", the refusals in the JS words,
// Undo, Accept (refused outside the bounds) and Cancel (every change undone), and — under Play in deck order — the
// deck list reordered with ▲ ▼. Engine-free; every rule is Core2DeckEdit's.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Core2;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2DeckTileView
    {
        public string Key { get; set; }
        public string CardId { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string Type { get; set; }
        public string Text { get; set; }
        public string Cost { get; set; }
        public string Count { get; set; }
        public bool CanAdd { get; set; }
        public bool CanRemove { get; set; }
        public string RemoveInstanceId { get; set; }
        /// <summary>Why the + is closed, in the JS words (null when it is open or there is nothing to say).</summary>
        public string Refusal { get; set; }
    }

    public sealed class Core2DeckEditorModel
    {
        public string Title => Core2Text.T("deckEditor.readingDesk");
        public string Heading { get; private set; }
        public string Counter { get; private set; }
        public string CounterRefusal { get; private set; }
        public List<Core2DeckTileView> Tiles { get; } = new List<Core2DeckTileView>();
        /// <summary>The deck in draw order (shown with ▲ ▼ only under Play in deck order).</summary>
        public List<(string InstanceId, string Name)> Deck { get; } = new List<(string, string)>();
        public bool Reorder { get; private set; }
        public bool CanUndo => _edit != null && _edit.CanUndo;
        public string UndoLabel => Core2Text.T("deckEditor.undo");
        public string AcceptLabel => Core2Text.T("deckEditor.accept");
        public string CancelLabel => Core2Text.T("deckEditor.cancel");
        public string AddLabel => Core2Text.T("deckEditor.addSelected");
        public string RemoveLabel => Core2Text.T("deckEditor.removeSelected");
        public string DrawOrderLabel => Core2Text.T("deckEditor.drawOrder");
        public string Keys => Core2Text.T("deckEditor.keys", new { panes = "[ ]", filter = "D", move = "E", done = "M", cancel = "Esc" });
        public string Status { get; private set; } = "";
        private readonly Core2RunFlow _flow;
        private Core2DeckEdit _edit;

        public Core2DeckEditorModel(Core2RunFlow flow)
        {
            _flow = flow;
            _edit = flow.DeckEdit;
            Refresh();
        }

        public static string Unlimited => Core2Text.T("deckEditor.unlimited");

        public void Refresh()
        {
            var data = _flow.Data;
            var run = _flow.Session.Run;
            _edit = _flow.DeckEdit;
            var training = Core2RunDeck.AdeptTraining(data, run);
            Heading = _flow.Content.Class(run.Class).Name + (training != null ? " · " + (string)training["label"] : "");
            Reorder = _flow.Session.DeckSettings.PlayInDeckOrder;
            Tiles.Clear(); Deck.Clear();
            if (_edit == null) return;
            var (count, min, max, refusal) = _edit.Counter();
            Counter = Core2Text.T("deckEditor.counter", new { count, min, max = max.HasValue ? max.Value.ToString(CultureInfo.InvariantCulture) : Unlimited });
            CounterRefusal = refusal;
            foreach (var tile in _edit.Tiles())
            {
                var name = data.Content.HasCard(tile.CardId) ? data.Content.Card(tile.CardId).Name : tile.CardId;
                var card = data.Content.HasCard(tile.CardId) ? data.Content.Card(tile.CardId) : null;
                var limit = Core2RunDeck.CopyLimit(data, run, tile.CardId);
                string why = tile.Refusal switch
                {
                    "copyLimit" => Core2Text.T("deckEditor.refuse.copyLimit", new { name, limit = limit?.ToString(CultureInfo.InvariantCulture) ?? Unlimited }),
                    "allInDeck" => Core2Text.T("deckEditor.refuse.allInDeck", new { name }),
                    "equipment" => Core2Text.T("deckEditor.refuse.equipment"),
                    _ => null,
                };
                Tiles.Add(new Core2DeckTileView
                {
                    Key = tile.Key, CardId = tile.CardId, Name = name,
                    Source = tile.Unlimited ? Core2Text.T("deckEditor.source.basic") : tile.Key.StartsWith("kept:", StringComparison.Ordinal) ? Core2Text.T("deckEditor.tile.kept", new { count = tile.Owned ?? 0 }) : Core2Text.T("deckEditor.source.reward"),
                    Type = card?.Type ?? "", Text = Core2CardText.Card(data.Content, tile.CardId),
                    Cost = card == null ? "" : (card.XCost ? "X" : card.StaminaCost.ToString(CultureInfo.InvariantCulture)) + (card.ManaCost > 0 ? " · " + card.ManaCost + " MP" : ""),
                    Count = tile.Unlimited ? Core2Text.T("deckEditor.stepperCount.unlimited", new { inDeck = tile.InDeck })
                        : Core2Text.T("deckEditor.stepperCount", new { inDeck = tile.InDeck, owned = tile.Owned ?? 0 }),
                    CanAdd = tile.Addable, CanRemove = tile.RemoveInstanceId != null, RemoveInstanceId = tile.RemoveInstanceId, Refusal = why,
                });
            }
            foreach (var c in run.Deck) Deck.Add((c.InstanceId, data.Content.HasCard(c.CardId) ? data.Content.Card(c.CardId).Name : c.CardId));
        }

        public string Add(string key)
        {
            var name = Tiles.FirstOrDefault(t => t.Key == key)?.Name ?? key;
            var (ok, refusal) = _edit.Add(key);
            Status = ok ? Core2Text.T("deckEditor.added", new { name }) : Tiles.FirstOrDefault(t => t.Key == key)?.Refusal ?? refusal ?? "";
            Refresh();
            return ok ? null : Status;
        }

        public string Remove(string instanceId)
        {
            var card = _flow.Session.Run.Deck.FirstOrDefault(c => c.InstanceId == instanceId);
            var name = card != null && _flow.Content.HasCard(card.CardId) ? _flow.Content.Card(card.CardId).Name : card?.CardId ?? "";
            var (ok, _) = _edit.Remove(instanceId);
            Status = ok ? Core2Text.T("deckEditor.removed", new { name }) : "";
            Refresh();
            return ok ? null : Status;
        }

        public bool Move(string instanceId, int delta) { var ok = _edit.Move(instanceId, delta); Refresh(); return ok; }
        public bool Undo() { var ok = _edit.Undo(); Status = ""; Refresh(); return ok; }

        /// <summary>Accept: closes the editor when the deck is within the bounds; otherwise the sentence naming them.</summary>
        public string Accept()
        {
            var error = _flow.ConfirmDeckEdit();
            Status = error ?? "";
            if (error != null) Refresh();
            return error;
        }

        public void Cancel() => _flow.CancelDeckEdit();
    }
}
