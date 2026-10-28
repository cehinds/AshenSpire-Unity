// Core2RestModel.cs — the Core rest site (JS src/ui/components/gameProfileRest.js, src/ui/models/GameProfileRestModel.js):
// healed on arrival ("HP and MP fully restored."), each service once (Smith with its upgrade preview, Level Up with one
// earned point), sorted available-first with Level Up leading, and only Continue leaves. Engine-free.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2RestService
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Line { get; set; }
        public bool Available { get; set; }
    }

    public sealed class Core2SmithRow
    {
        public string ItemRef { get; set; }
        public string Name { get; set; }
        public string Tier { get; set; }
        public List<string> Changes { get; } = new List<string>();
    }

    public sealed class Core2RestModel
    {
        public string Title { get; private set; }
        public string Status { get; private set; }
        public string Restored { get; private set; }
        public string ContinueLabel { get; private set; }
        public List<Core2RestService> Services { get; } = new List<Core2RestService>();
        public List<Core2SmithRow> Smith { get; } = new List<Core2SmithRow>();
        public List<(string Id, string Short, string Label, int Value)> Attributes { get; } = new List<(string, string, string, int)>();
        public string Error { get; private set; } = "";
        private readonly Core2RunFlow _flow;

        public Core2RestModel(Core2RunFlow flow) { _flow = flow; Refresh(); }

        public static string ItemName(Core2RunData data, string itemRef)
        {
            var (kind, id, classId) = Core2RunRest.Identity(itemRef);
            if (kind == "relic") return data.HasRelic(id) ? (string)data.Relic(id)["name"] : id;
            var piece = kind == "armor" ? data.Armour(classId, id) : data.Armament(id);
            return (string)piece?["name"] ?? id;
        }

        public void Refresh()
        {
            var data = _flow.Data;
            var session = _flow.Session;
            var run = session.Run;
            var visit = session.RestVisit;
            var all = Core2RunRest.AllServices(data);
            Title = Core2Text.T("rest.title");
            ContinueLabel = Core2Text.T("rest.continue");
            Restored = all ? Core2Text.T("gameProfile.rest.restored") : visit?.Service != null
                ? Core2Text.T("gameProfile.rest.complete", new { service = Core2Text.T("gameProfile.rest.service." + visit.Service) }) : Core2Text.T("gameProfile.rest.chooseOne");
            Services.Clear(); Smith.Clear(); Attributes.Clear();
            var open = session.RestServices();
            var levels = session.Rules.CharacterLevels;
            var smithFree = (bool?)data.Content.Rests["smith"]?["free"] == true; // JS restSmithFree
            foreach (var id in Core2Json2.Strings(data.Content.Rests["services"]))
            {
                if (id == "levelUp" && !levels && !(run.Level.UnspentPoints > 0)) continue;
                var used = visit != null && Core2RunRest.ServiceUsed(visit, id);
                var available = open.Contains(id);
                string reason = used ? Core2Text.T("gameProfile.rest.usedService")
                    : id == "smith" && !available ? Core2Text.T("gameProfile.rest.noSmith")
                    : id == "levelUp" && !available ? Core2Text.T("gameProfile.rest.noLevel") : "";
                var description = Core2Text.T(all ? "gameProfile.rest.description." + (id == "smith" && !smithFree ? "smithAllPaid" : id + "All") : "gameProfile.rest.description." + id);
                Services.Add(new Core2RestService { Id = id, Title = Core2Text.T("gameProfile.rest.service." + id), Available = available, Line = available ? description : reason });
            }
            // Available first, Level Up leading; spent or unavailable sink (a stable sort, as Array#sort is).
            var ranked = Services.Select((s, i) => (s, i)).OrderBy(p => (p.s.Available ? 0 : 2) + (p.s.Id == "levelUp" ? 0 : 1)).ThenBy(p => p.i).Select(p => p.s).ToList();
            Services.Clear(); Services.AddRange(ranked);
            var total = Services.Count;
            Status = Core2Text.T("rest.status.available", new { available = Services.Count(s => s.Available), total });
            foreach (var candidate in session.SmithCandidates())
            {
                var row = new Core2SmithRow { ItemRef = candidate.ItemRef, Name = ItemName(data, candidate.ItemRef), Tier = "+" + candidate.CurrentLevel + " → +" + candidate.NextLevel };
                foreach (var pair in candidate.Stats) row.Changes.Add(pair.Key.ToUpperInvariant() + " " + pair.Value.Before + " → " + pair.Value.After);
                Smith.Add(row);
            }
            foreach (var id in Core2Run.AttributeIds) Attributes.Add((id, Core2Text.AttributeShort(id), Core2Text.AttributeLabel(id), run.AttributeValue(id)));
        }

        public string SmithConsequence => Core2Text.T(Core2RunRest.AllServices(_flow.Data)
            ? (Services.Any(s => s.Id == "levelUp") ? "gameProfile.rest.smithConsequenceAll" : "gameProfile.rest.smithConsequenceAny") : "gameProfile.rest.smithConsequence");
        public string SmithBadge => Core2Text.T(Core2RunRest.AllServices(_flow.Data) ? "gameProfile.rest.staysVisit" : "gameProfile.rest.completesVisit");
        public string LevelNote => Core2Text.T(Core2RunRest.AllServices(_flow.Data) ? "gameProfile.rest.levelNoteAll" : "gameProfile.rest.levelNote");
        public string LevelConfirm => Core2Text.T(Core2RunRest.AllServices(_flow.Data) ? "gameProfile.rest.levelConfirmAll" : "gameProfile.rest.levelConfirm");
        public string SmithTitle => Core2Text.T("gameProfile.rest.service.smith");
        public string LevelTitle => Core2Text.T("gameProfile.rest.service.levelUp");
        public string SmithHeading => Core2Text.T("gameProfile.rest.copy6");
        public string LevelHeading => Core2Text.T("gameProfile.rest.copy7");

        public string DoSmith(string itemRef) { var e = _flow.Smith(itemRef); Error = e ?? ""; if (e == null) Refresh(); return e; }
        public string DoLevelUp(string attributeId) { var e = _flow.LevelUp(attributeId); Error = e ?? ""; if (e == null) Refresh(); return e; }
        public string Leave() => _flow.LeaveRest();
    }
}
