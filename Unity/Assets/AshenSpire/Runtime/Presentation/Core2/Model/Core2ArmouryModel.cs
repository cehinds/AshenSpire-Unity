// Core2ArmouryModel.cs — the Armoury's Equipment view in a Core 2 run (JS src/ui/screens/equipment.js armoury tab,
// src/model/armouryRules.js): the equipped positions (BODY, MH1…) with Inspect, Replace and Unequip, the new-item glow
// (looking clears it), and the combat lock ("Equipment changes are disabled during combat."). Engine-free.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2ArmouryRow
    {
        public string SlotId { get; set; }
        public int SetIndex { get; set; }
        /// <summary>JS position code: BODY, MH1, OH2…</summary>
        public string Code { get; set; }
        public string SlotLabel { get; set; }
        public string Name { get; set; }
        public bool Empty { get; set; }
        public bool Active { get; set; }
        public bool IsNew { get; set; }
        public string Chip { get; set; }
        public Core2ArmouryPosition Position { get; set; }
    }

    public sealed class Core2ArmouryModel
    {
        public string Title => Core2Text.T("armoury.title");
        public string HubArmory => Core2Text.T("armoury.hub.armory");
        public string HubDeck => Core2Text.T("armoury.hub.deck");
        public string PositionsTitle => Core2Text.T("armoury.positions.title");
        public string InspectLabel => Core2Text.T("armoury.action.inspect");
        public string ReplaceLabel => Core2Text.T("armoury.action.replace");
        public string UnequipLabel => Core2Text.T("armoury.action.unequip");
        public string BackLabel => Core2Text.T("common.back");
        public string CloseLabel => Core2Text.T("armoury.close");
        public string EquipLabel => Core2Text.T("armoury.picker.act.equip");
        public List<Core2ArmouryRow> Rows { get; } = new List<Core2ArmouryRow>();
        /// <summary>Set while a fight is unresolved: every change is refused with this sentence.</summary>
        public string Locked { get; private set; }
        public bool CanEditDeck { get; private set; }
        public string Status { get; private set; } = "";
        public int? Inspecting { get; private set; }
        public int? Replacing { get; private set; }
        private readonly Core2RunFlow _flow;

        public Core2ArmouryModel(Core2RunFlow flow)
        {
            _flow = flow;
            Refresh();
            // JS showArmoury: looking clears the glow (persisted). Refused while a fight is unresolved; the rows still show.
            if (Locked == null && _flow.Session.ArmouryHasNew) _flow.MarkArmourySeen();
        }

        public void Refresh()
        {
            var data = _flow.Data;
            var session = _flow.Session;
            Locked = session.EditingLocked ? Core2RunArmoury.CombatChangeDisabled : null;
            CanEditDeck = Locked == null && Core2RunArmoury.OpensDeckEditor(data, session.DeckSettings);
            Rows.Clear();
            foreach (var position in session.ArmouryPositions())
            {
                var slot = Core2RunArmoury.Slot(data, position.SlotId);
                var code = Core2Text.Fill((string)slot["positionCode"] ?? position.SlotId, new Dictionary<string, string> { ["n"] = (position.SetIndex + 1).ToString(CultureInfo.InvariantCulture) });
                Rows.Add(new Core2ArmouryRow
                {
                    SlotId = position.SlotId, SetIndex = position.SetIndex, Code = code, SlotLabel = (string)slot["label"], Name = position.Name ?? "",
                    Empty = position.ItemId == null, Active = position.Active, IsNew = position.IsNew, Chip = position.ItemId != null && position.Active ? "EQUIPPED" : null,
                    Position = position,
                });
            }
        }

        /// <summary>The Inspect panel's title and lines for a row (JS "Inspect · BODY" and the equipment card).</summary>
        public (string Title, string Name, List<string> Lines) Inspect(int row)
        {
            Inspecting = row; Replacing = null;
            var r = Rows[row];
            var lines = new List<string>();
            if (!r.Empty)
            {
                var p = r.Position;
                if (p.Kind != null) lines.Add(p.Kind.ToUpperInvariant() + " · " + r.SlotLabel + (p.Tier > 0 ? " · +" + p.Tier : ""));
                var values = p.Values.Where(v => v.Value != 0).Select(v => v.Key.ToUpperInvariant() + " " + v.Value).ToList();
                if (values.Count > 0) lines.Add(string.Join("  ", values));
                if (p.GrantedCards.Count > 0)
                    lines.Add("Cards: " + string.Join(", ", p.GrantedCards.GroupBy(c => c).Select(g => (_flow.Content.HasCard(g.Key) ? _flow.Content.Card(g.Key).Name : g.Key) + (g.Count() > 1 ? " ×" + g.Count() : ""))));
                foreach (var req in p.Requirements.OfType<JObject>())
                    lines.Add(Core2Text.T("armoury.eligibility.requirement", new { attribute = Core2Text.AttributeShort((string)req["attributeId"]), required = (int)req["required"], actual = Core2Json2.Int(req["actual"]) }));
            }
            return (Core2Text.T("armoury.inspect.title", new { slot = r.Code }), r.Empty ? "" : r.Name, lines);
        }

        /// <summary>Replace: the owned pieces that fit the row's slot (JS the picker's Equip rows).</summary>
        public List<(string ItemId, string Name, bool Here)> Candidates(int row)
        {
            Replacing = row; Inspecting = null;
            var r = Rows[row];
            var data = _flow.Data;
            var slot = Core2RunArmoury.Slot(data, r.SlotId);
            return _flow.Session.ReplaceCandidates(r.SlotId).Select(id => (id, (string)Core2RunArmoury.PieceFor(data, _flow.Session.Run, slot, id)?["name"] ?? id, id == r.Position.ItemId)).ToList();
        }

        public string ChooseFor(int row) => Core2Text.T("armoury.action.chooseFor", new { slot = Rows[row].Code });

        public string Equip(int row, string itemId)
        {
            var r = Rows[row];
            var name = _flow.Data.Armament(itemId)?["name"] ?? _flow.Data.Armour(_flow.Session.Run.Class, itemId)?["name"];
            var error = Locked ?? _flow.Equip(r.SlotId, r.SetIndex, itemId);
            Status = error ?? Core2Text.T("armoury.change.equipped", new { name = (string)name ?? itemId, slot = r.Code });
            Replacing = null;
            Refresh();
            return error;
        }

        public string Unequip(int row)
        {
            var r = Rows[row];
            if (r.Empty) return null;
            var error = Locked ?? _flow.Equip(r.SlotId, r.SetIndex, null);
            Status = error ?? Core2Text.T("armoury.change.unequipped", new { name = r.Name, slot = r.Code });
            Refresh();
            return error;
        }
    }
}
