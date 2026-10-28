// Core2MapModel.cs — what the run HUD band, the act route strip and the act map show (JS src/ui/components/runHud.js,
// actRouteStrip.js, mapboard.js, src/model/map/mapknowledge.js, src/ui/models/MapSelectionModel.js, src/ui/screens/map.js).
// Engine-free: the Unity views only place what this says.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Core2;

namespace AshenSpire.Presentation.Core2
{
    /// <summary>The HUD band every run screen wears (JS runHudHtml + wireRunHud).</summary>
    public sealed class Core2HudModel
    {
        public string HpText { get; set; }
        public double HpFraction { get; set; }
        public List<(string Id, string Name, string Text)> Relics { get; } = new List<(string, string, string)>();
        public string CindersLabel { get; set; } = "CINDERS";
        public string Cinders { get; set; }
        public bool ArmouryNew { get; set; }
        public string ArmouryLabel { get; set; }
        /// <summary>The flask charges (JS potionContents category charge): kind, name, count.</summary>
        public List<(string Kind, string Name, int Count)> Flasks { get; } = new List<(string, string, int)>();
        public string ActTitle { get; set; }

        /// <summary>JS seatNameOf: the seat's display name for the run's act.</summary>
        public static string SeatName(Core2RunData data, Core2Run run)
        {
            if (run.SeatOrder == null || run.SeatOrder.Count == 0) return null;
            var id = Core2ActMap.SeatAt(run);
            return (string)data.Content.Seats.FirstOrDefault(s => s.Id == id)?.Raw["name"];
        }

        public static Core2HudModel From(Core2RunData data, Core2RunSession session)
        {
            var run = session.Run;
            var hp = session.Fight?.Player != null ? session.Fight.Player.Hp : run.Hp;
            var max = session.Fight?.Player != null ? session.Fight.Player.MaxHp : run.MaxHp;
            var model = new Core2HudModel
            {
                HpText = hp.ToString(CultureInfo.InvariantCulture) + "/" + max.ToString(CultureInfo.InvariantCulture),
                HpFraction = max > 0 ? Math.Max(0, Math.Min(1, hp / (double)max)) : 0,
                Cinders = run.Cinders.ToString(CultureInfo.InvariantCulture),
                ArmouryNew = session.ArmouryHasNew,
                ArmouryLabel = Core2Text.T("armoury.title") + (session.ArmouryHasNew ? " (" + Core2Text.T("armoury.hud.newItem") + ")" : ""),
                ActTitle = Core2Text.ActTitle(run.ActNumber, SeatName(data, run)),
            };
            foreach (var id in run.Relics.Where(data.HasRelic)) model.Relics.Add((id, (string)data.Relic(id)["name"], Core2CardText.Relic(data, id)));
            if (run.FlaskCharges != null)
            {
                model.Flasks.Add(("hp", data.HasFlask("crimsonFlask") ? (string)data.Flask("crimsonFlask")["name"] : "Crimson Flask", run.FlaskCharges.HpCurrent));
                if (run.FlaskCharges.Mana > 0 || run.FlaskCharges.ManaCurrent > 0)
                    model.Flasks.Add(("mana", data.HasFlask("azureFlask") ? (string)data.Flask("azureFlask")["name"] : "Azure Flask", run.FlaskCharges.ManaCurrent));
            }
            return model;
        }
    }

    public sealed class Core2MapNodeView
    {
        public string Id { get; set; }
        public int Floor { get; set; }
        public double Col { get; set; }
        /// <summary>What the node shows: its kind when known, else "event" (Unknown).</summary>
        public string ShownType { get; set; }
        /// <summary>known or placed (a hidden node is not in the list at all).</summary>
        public string Rung { get; set; }
        public bool Reachable { get; set; }
        public bool Visited { get; set; }
        public bool Current { get; set; }
        public string Icon { get; set; }
        public string Name { get; set; }
        public string Tint { get; set; }
        public List<string> Next { get; } = new List<string>();
        /// <summary>A boss node's destination label (JS node.destinationLabel).</summary>
        public string Destination { get; set; }
    }

    /// <summary>The map screen: the drawn nodes and edges under fog, the selection tray, the route strip.</summary>
    public sealed class Core2MapModel
    {
        public string ActTitle { get; set; }
        public List<Core2MapNodeView> Nodes { get; } = new List<Core2MapNodeView>();
        public List<(string From, string To, bool Travelled)> Edges { get; } = new List<(string, string, bool)>();
        public int Floors { get; set; }
        public int Columns { get; set; }
        public bool AtEntrance { get; set; }
        /// <summary>The route strip: per floor before the boss, the visited node's shown kind (null = not yet).</summary>
        public List<(int Floor, string Type, bool Current)> Route { get; } = new List<(int, string, bool)>();
        public bool BossVisited { get; set; }
        public string EntranceLabel => "ENTRANCE";
        public string BossLabel => "BOSS";

        /// <summary>JS nearestShrine: the closest shrine ahead of a node (breadth-first along next), or null.</summary>
        public static string NearestShrine(Core2MapGraph graph, string from)
        {
            var seen = new HashSet<string> { from };
            var queue = new Queue<string>(graph.Node(from).Next);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!seen.Add(id) || !graph.Nodes.TryGetValue(id, out var node)) continue;
                if (node.Type == "shrine") return id;
                foreach (var next in node.Next) queue.Enqueue(next);
            }
            return null;
        }

        /// <summary>JS litNodes: the start row and every boss, each node stood on and one step past it, and the next shrine ahead of a shrine stood on.</summary>
        public static HashSet<string> Lit(Core2MapGraph graph, IReadOnlyList<string> path, string current)
        {
            var lit = new HashSet<string>(StringComparer.Ordinal);
            void Add(string id) { if (id != null && graph.Nodes.ContainsKey(id)) lit.Add(id); }
            void Shine(string id)
            {
                if (!graph.Nodes.TryGetValue(id, out var n)) return;
                lit.Add(id);
                foreach (var to in n.Next) Add(to);
            }
            void ShrineAhead(string id)
            {
                if (!graph.Nodes.TryGetValue(id, out var n) || n.Type != "shrine") return;
                Add(NearestShrine(graph, id));
            }
            foreach (var id in graph.StartIds) Add(id);
            foreach (var id in graph.BossIds.Count > 0 ? graph.BossIds : graph.NodeList.Where(n => n.Type == "boss").Select(n => n.Id).ToList()) Add(id);
            Add(graph.BossId);
            foreach (var id in path ?? Array.Empty<string>()) { Shine(id); ShrineAhead(id); }
            if (current != null) { Shine(current); ShrineAhead(current); }
            return lit;
        }

        public static Core2MapModel From(Core2RunData data, Core2RunSession session, bool fog = true)
        {
            var run = session.Run;
            var graph = run.MapGraph;
            var reachable = new HashSet<string>(session.TravelOptions(), StringComparer.Ordinal);
            var lit = fog ? Lit(graph, run.Path, run.MapNodeId) : null;
            var travelled = new HashSet<string>(run.Path, StringComparer.Ordinal);
            var model = new Core2MapModel
            {
                ActTitle = Core2Text.ActTitle(run.ActNumber, Core2HudModel.SeatName(data, run)), Floors = graph.Floors, Columns = graph.Columns,
                AtEntrance = run.MapNodeId == null,
            };
            foreach (var node in graph.NodeList)
            {
                if (fog && !lit.Contains(node.Id)) continue; // HIDDEN is never drawn (JS: the element is never created)
                var known = node.Type != "event";
                var shown = known ? node.Type : "event";
                var kind = Core2Text.NodeType(shown);
                var view = new Core2MapNodeView
                {
                    Id = node.Id, Floor = node.Floor, Col = node.Col, ShownType = shown, Rung = known ? "known" : "placed",
                    Reachable = reachable.Contains(node.Id), Visited = travelled.Contains(node.Id) || node.Id == run.MapNodeId, Current = node.Id == run.MapNodeId,
                    Icon = kind.Icon, Name = kind.Name, Tint = Core2Text.NodeTint(shown),
                    Destination = node.Type == "boss" && node.EncounterId != null ? Core2ActMap.BossLabel(data, node.EncounterId) : null,
                };
                view.Next.AddRange(node.Next);
                model.Nodes.Add(view);
            }
            var drawn = new HashSet<string>(model.Nodes.Select(n => n.Id), StringComparer.Ordinal);
            foreach (var node in model.Nodes)
                foreach (var to in node.Next)
                    if (drawn.Contains(to))
                    {
                        var i = run.Path.IndexOf(node.Id);
                        model.Edges.Add((node.Id, to, i >= 0 && i + 1 < run.Path.Count && run.Path[i + 1] == to));
                    }
            // JS actRouteModel: one slot per floor before the boss floor; a travelled floor shows its node's kind.
            var bossFloor = graph.NodeList.Where(n => n.Type == "boss").Select(n => n.Floor).DefaultIfEmpty(graph.Floors).Max();
            for (var floor = 1; floor < bossFloor; floor++)
            {
                var at = run.Path.Select(graph.Node).FirstOrDefault(n => n.Floor == floor);
                model.Route.Add((floor, at?.Type, at != null && at.Id == run.MapNodeId));
            }
            model.BossVisited = run.Path.Any(id => graph.Node(id).Type == "boss");
            return model;
        }

        /// <summary>JS projectMapContext + renderSelection: the tray's lines and the Enter label for a selected node.</summary>
        public static (bool Empty, string Floor, string Title, List<string> Lines, string EnterLabel, bool CanEnter) Context(Core2MapNodeView node)
        {
            if (node == null) return (true, null, null, new List<string> { Core2Text.T("map.context.empty") }, Core2Text.T("map.enter"), false);
            var kind = Core2Text.NodeType(node.ShownType);
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(kind.Blurb)) lines.Add(kind.Blurb);
            if (node.ShownType == "boss" && node.Destination != null) lines.Add(node.Destination);
            return (false, Core2Text.T("map.context.floor", new { floor = node.Floor }).ToUpperInvariant(), kind.Name, lines,
                node.Reachable ? Core2Text.T("map.enterNamed", new { name = kind.Name }) : Core2Text.T("map.enter"), node.Reachable);
        }
    }
}
