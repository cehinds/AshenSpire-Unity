// Core2ActMap.cs — the Core 2 act map: a branched route with fog, generated per act on the run's seeded `map` stream.
//
// JS at the pin:
//   src/engine/actmap.js       buildActMap → buildGameProfileActMap → buildBranchedActMap (routes.json maxBranches),
//                              drawSeatOrder, bossEncounterForNode
//   src/engine/encounters.js   resolveUnknownNode (an event node pre-rolls its event on the `events` stream; the routes
//                              path forces unknownWeights { event: 1 }), eligibleEventIds (quest gates on run history)
//   src/model/seats.js         defaultSeatOrder, seatAtTier, encounterFitsSeat
//   src/model/bossDestinationLabels.js bossDestinationLabel
//
// The act splits into 1..min(maxBranches, bosses) branches after a shared opening depth; each branch has its own rests
// (one per rest floor, the last directly before its boss), at least one elite at or past the threat floor, and its own
// boss. Unknown types (event, fight, treasure, shop) show as "event" (fog); rests, elites and bosses are visible.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2MapNode
    {
        public string Id { get; set; }
        public int Floor { get; set; }
        public double Col { get; set; }
        /// <summary>The branch a node belongs to; null on the shared opening depth.</summary>
        public int? Branch { get; set; }
        /// <summary>What the map shows (monster, event, elite, treasure, merchant, shrine, boss): fogged types show as event.</summary>
        public string Type { get; set; }
        /// <summary>What the node is (fight, event, elite, treasure, shop, rest, boss).</summary>
        public string ActualType { get; set; }
        public List<string> Next { get; set; } = new List<string>();
        /// <summary>A fogged node's pre-rolled outcome: { kind: event, eventId } or { kind: fight|merchant|treasure|shrine }.</summary>
        public string ResolvedKind { get; set; }
        public string ResolvedEventId { get; set; }
        public string EncounterId { get; set; }
        public string Label { get; set; }

        public JObject ToJson()
        {
            var row = new JObject { ["id"] = Id, ["floor"] = Floor, ["col"] = Core2RunJson.Number(Col), ["branch"] = Branch.HasValue ? (JToken)Branch.Value : JValue.CreateNull(),
                ["type"] = Type, ["actualType"] = ActualType, ["next"] = new JArray(Next) };
            if (ResolvedKind != null) row["resolved"] = ResolvedEventId != null ? new JObject { ["kind"] = ResolvedKind, ["eventId"] = ResolvedEventId } : new JObject { ["kind"] = ResolvedKind };
            if (EncounterId != null) { row["encounterId"] = EncounterId; row["label"] = Label; row["destinationLabel"] = Label; }
            return row;
        }
        public static Core2MapNode FromJson(JObject row) => new Core2MapNode
        {
            Id = (string)row["id"], Floor = (int)row["floor"], Col = (double)row["col"], Branch = row["branch"] == null || row["branch"].Type == JTokenType.Null ? (int?)null : (int)row["branch"],
            Type = (string)row["type"], ActualType = (string)row["actualType"], Next = Core2Json.Strings(row["next"]).ToList(),
            ResolvedKind = (string)row["resolved"]?["kind"], ResolvedEventId = (string)row["resolved"]?["eventId"],
            EncounterId = (string)row["encounterId"], Label = (string)row["label"] ?? (string)row["destinationLabel"],
        };
    }

    public sealed class Core2MapBranch
    {
        public int Index { get; set; }
        public string BossId { get; set; }
        public string EncounterId { get; set; }
        public int FirstCol { get; set; }
        public int Lanes { get; set; }
        public List<string> RestIds { get; set; } = new List<string>();
        public JObject ToJson() => new JObject { ["index"] = Index, ["bossId"] = BossId, ["encounterId"] = EncounterId, ["firstCol"] = FirstCol, ["lanes"] = Lanes, ["restIds"] = new JArray(RestIds) };
    }

    /// <summary>One act's graph (JS run.mapGraph for a branched Core 2 act).</summary>
    public sealed class Core2MapGraph
    {
        /// <summary>Nodes in creation order (depth by depth; bosses last).</summary>
        public List<Core2MapNode> NodeList { get; } = new List<Core2MapNode>();
        public Dictionary<string, Core2MapNode> Nodes { get; } = new Dictionary<string, Core2MapNode>(StringComparer.Ordinal);
        public List<string> StartIds { get; set; } = new List<string>();
        public string ShrineId { get; set; }
        public List<string> ShrineIds { get; set; } = new List<string>();
        public string BossId { get; set; }
        public List<string> BossIds { get; set; } = new List<string>();
        public List<Core2MapBranch> Branches { get; set; } = new List<Core2MapBranch>();
        public int Floors { get; set; }
        public int Columns { get; set; }
        public string GameProfileHash { get; set; }

        public void Add(Core2MapNode node) { NodeList.Add(node); Nodes[node.Id] = node; }
        public Core2MapNode Node(string id) => id != null && Nodes.TryGetValue(id, out var node) ? node : throw new KeyNotFoundException("Map node '" + id + "' is not on this act's map");

        public JObject ToJson()
        {
            var nodes = new JObject();
            foreach (var node in NodeList) nodes[node.Id] = node.ToJson();
            return new JObject
            {
                ["nodes"] = nodes, ["startIds"] = new JArray(StartIds), ["shrineId"] = ShrineId, ["shrineIds"] = new JArray(ShrineIds), ["bossId"] = BossId,
                ["bossIds"] = new JArray(BossIds), ["branches"] = new JArray(Branches.Select(b => b.ToJson())), ["floors"] = Floors, ["columns"] = Columns,
                ["gameProfileHash"] = GameProfileHash,
            };
        }
        public static Core2MapGraph FromJson(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            var row = (JObject)token;
            var graph = new Core2MapGraph
            {
                StartIds = Core2Json.Strings(row["startIds"]).ToList(), ShrineId = (string)row["shrineId"], ShrineIds = Core2Json.Strings(row["shrineIds"]).ToList(),
                BossId = (string)row["bossId"], BossIds = Core2Json.Strings(row["bossIds"]).ToList(), Floors = (int)row["floors"], Columns = (int)row["columns"],
                GameProfileHash = (string)row["gameProfileHash"],
                Branches = ((JArray)row["branches"] ?? new JArray()).OfType<JObject>().Select(b => new Core2MapBranch
                {
                    Index = (int)b["index"], BossId = (string)b["bossId"], EncounterId = (string)b["encounterId"], FirstCol = (int)b["firstCol"], Lanes = (int)b["lanes"],
                    RestIds = Core2Json.Strings(b["restIds"]).ToList(),
                }).ToList(),
            };
            foreach (var node in ((JObject)row["nodes"]).Properties()) graph.Add(Core2MapNode.FromJson((JObject)node.Value));
            return graph;
        }
    }

    public static class Core2ActMap
    {
        private static readonly Dictionary<string, string> DisplayTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["fight"] = "monster", ["event"] = "event", ["elite"] = "elite", ["treasure"] = "treasure", ["shop"] = "merchant", ["rest"] = "shrine", ["boss"] = "boss" };

        /// <summary>JS defaultSeatOrder: seats by ascending base tier, then id.</summary>
        public static List<string> DefaultSeatOrder(Core2Content content) => content.Seats
            .OrderBy(s => (int)s.Raw["baseTier"]).ThenBy(s => s.Id, StringComparer.Ordinal).Select(s => s.Id).ToList();

        /// <summary>JS drawSeatOrder: one shuffle on the `seats` stream, at run creation.</summary>
        public static List<string> DrawSeatOrder(Core2Content content, Core2Rng rng) => rng.Shuffle("seats", DefaultSeatOrder(content));

        /// <summary>The tier an act is (JS tierOf with the cycle length; Core 2 has no Endless loop).</summary>
        public static int Tier(Core2RunData data, int act) => ((Math.Max(1, act) - 1) % Math.Max(1, data.ActsPerCycle)) + 1;
        /// <summary>JS seatAtTier(run.seatOrder, tier).</summary>
        public static string SeatAt(Core2Run run) => run.SeatOrder[((Math.Max(1, run.ActNumber) - 1) % run.SeatOrder.Count)];

        /// <summary>JS encounterFitsSeat.</summary>
        public static bool FitsSeat(JObject encounter, string seat, int tier, int finalTier)
        {
            var own = (string)encounter["seat"];
            if (own == seat) return true;
            return encounter["seat"]?.Type == JTokenType.Null && tier == finalTier;
        }

        /// <summary>JS eligibleEventIds: ungated events, and gated steps whose history requirement is met and not yet answered.</summary>
        public static List<string> EligibleEventIds(Core2Content content, IReadOnlyList<JObject> history)
        {
            var gates = (JObject)content.Table("adapter/events.json")["eventHistoryRequirements"] ?? new JObject();
            var completed = new HashSet<string>((history ?? new List<JObject>()).Where(r => (string)r["kind"] == "eventChoice").Select(r => (string)r["eventId"]), StringComparer.Ordinal);
            return content.Events.Select(e => e.Id).Where(id => gates[id] == null || (!completed.Contains(id) && Core2RunEvents.RequirementMet(gates[id] as JObject, history))).ToList();
        }

        /// <summary>JS resolveUnknownNode with the routes override (unknownWeights { event: 1 }): one `events` float, then the event.</summary>
        private static (string Kind, string EventId) ResolveEvent(Core2Content content, Core2Rng rng, List<string> seen, IReadOnlyList<JObject> history)
        {
            var r = rng.Float("events") * 1;
            if (!(r - 1 < 0)) { /* the single weight always lands: kind stays event */ }
            var earned = EligibleEventIds(content, history);
            var pool = earned.Where(id => !seen.Contains(id)).ToList();
            if (pool.Count == 0) pool = earned;
            if (pool.Count == 0) return ("fight", null);
            return ("event", rng.Pick("events", pool));
        }

        /// <summary>JS buildActMap for a Core 2 run (routes configured, maxBranches set): the branched act at this seat and tier.</summary>
        public static Core2MapGraph Build(Core2RunData data, Core2Rng rng, string seat, int tier, IReadOnlyList<JObject> history)
        {
            var content = data.Content;
            var rules = content.Routes;
            if (tier < 1 || tier > rules.Acts) throw new InvalidDataException("Configured route tier is out of range");
            var count = rules.PrebossNodes;
            var columns = rules.BranchColumns;
            var minimumThreatFloor = rules.MinimumThreatFloor;
            var restFloors = rules.RestFloors;
            if (rules.RequirePrebossRest && !restFloors.Contains(count)) throw new InvalidDataException("Configured route requires a rest immediately before the boss");
            var firstFloor = rules.FirstFloor;
            double? shopShare = rules.Raw["shopShare"] == null || rules.Raw["shopShare"].Type == JTokenType.Null ? (double?)null : rules.ShopShare;
            var hidden = new HashSet<string>(rules.HiddenTypes, StringComparer.Ordinal);
            var alternatives = rules.HiddenTypes.Concat(rules.VisibleTypes).Distinct().Where(t => t != "rest" && t != "boss").ToList();
            var hideUnknown = (bool?)data.Module("routes")?["hideUnknownNodes"] ?? false;
            if (!rules.MaxBranches.HasValue) throw new InvalidDataException("Core 2 routes are branched (maxBranches); the single-trunk map is not ported");
            var maxBranches = rules.MaxBranches.Value;
            var pool = content.Encounters.Select(e => e.Raw).Where(e => (string)e["pool"] == "boss" && FitsSeat(e, seat, tier, rules.Acts)).ToList();
            if (pool.Count == 0) throw new InvalidDataException("Configured route has no boss for " + seat + "/" + tier);
            var branchCount = rng.Int("map", 1, Math.Min(maxBranches, pool.Count));
            var bosses = rng.Shuffle("map", pool).Take(branchCount).ToList();
            var lanes = Math.Max(1, Math.Min(columns, (int)Math.Floor(2.0 * columns / branchCount)));
            var total = branchCount * lanes;
            var restSet = new HashSet<int>(restFloors);
            var eliteFloors = new List<int>();
            for (var floor = Math.Max(2, minimumThreatFloor); floor <= count; floor++) if (!restSet.Contains(floor) && alternatives.Contains("elite")) eliteFloors.Add(floor);
            var guaranteed = new HashSet<string>(StringComparer.Ordinal);
            if (eliteFloors.Count > 0)
                for (var k = 0; k < branchCount; k++)
                {
                    var f = rng.Pick("map", eliteFloors);
                    guaranteed.Add(f + ":" + (k * lanes + rng.Int("map", 0, lanes - 1)));
                }
            var graph = new Core2MapGraph { GameProfileHash = (string)content.Profile["hash"] };
            var layers = new List<List<string>>();
            var seenEvents = new List<string>();
            string MakeNode(string id, int floor, double col, int? branch, bool rest, int lane)
            {
                var choices = alternatives.Where(type => type != "elite" || floor >= minimumThreatFloor).ToList();
                if (!rest && choices.Count == 0) throw new InvalidDataException("Configured route has no permitted node types at depth " + floor);
                var forced = !rest && floor == 1 && firstFloor != null;
                string actual;
                if (rest) actual = "rest";
                else if (forced) actual = firstFloor;
                else if (guaranteed.Contains(floor + ":" + lane)) actual = "elite";
                else
                {
                    actual = rng.Pick("map", choices);
                    if (shopShare.HasValue && alternatives.Contains("shop") && actual != "elite")
                    {
                        var plain = alternatives.Where(type => type != "elite" && type != "shop").ToList();
                        actual = rng.Chance("map", shopShare.Value * 100) || plain.Count == 0 ? "shop" : rng.Pick("map", plain);
                    }
                }
                var type = hideUnknown && hidden.Contains(actual) && !forced ? "event" : DisplayTypes[actual];
                var node = new Core2MapNode { Id = id, Floor = floor, Col = col, Branch = branch, Type = type, ActualType = actual };
                if (type == "event")
                {
                    if (actual == "event")
                    {
                        var resolved = ResolveEvent(content, rng, seenEvents, history);
                        node.ResolvedKind = resolved.Kind; node.ResolvedEventId = resolved.EventId;
                    }
                    else node.ResolvedKind = actual == "shop" ? "merchant" : actual;
                    if (node.ResolvedKind == "event") seenEvents.Add(node.ResolvedEventId);
                }
                graph.Add(node);
                return id;
            }
            for (var floor = 1; floor <= count; floor++)
            {
                var layer = new List<string>();
                if (floor == 1)
                {
                    if (restSet.Contains(1)) layer.Add(MakeNode("n1_0", 1, (total - 1) / 2.0, null, true, 0));
                    else for (var lane = 0; lane < total; lane++) layer.Add(MakeNode("n1_" + lane, 1, lane, null, false, lane));
                }
                else for (var k = 0; k < branchCount; k++)
                {
                    if (restSet.Contains(floor)) layer.Add(MakeNode("n" + floor + "_" + (k * lanes), floor, k * lanes + (lanes - 1) / 2.0, k, true, k * lanes));
                    else for (var j = 0; j < lanes; j++) layer.Add(MakeNode("n" + floor + "_" + (k * lanes + j), floor, k * lanes + j, k, false, k * lanes + j));
                }
                layers.Add(layer);
            }
            var bossIds = new List<string>();
            for (var k = 0; k < bosses.Count; k++)
            {
                var id = "boss_" + k;
                var encounterId = (string)bosses[k]["id"];
                graph.Add(new Core2MapNode { Id = id, Floor = count + 1, Col = k * lanes + (lanes - 1) / 2.0, Branch = k, Type = "boss", ActualType = "boss", EncounterId = encounterId, Label = BossLabel(data, encounterId) });
                bossIds.Add(id);
            }
            layers.Add(bossIds);
            for (var depth = 0; depth < layers.Count - 1; depth++)
                foreach (var id in layers[depth])
                {
                    var from = graph.Nodes[id];
                    from.Next = layers[depth + 1].Where(target =>
                    {
                        var to = graph.Nodes[target];
                        if (depth == 0) return from.ActualType == "rest" || (to.ActualType == "rest" || to.Type == "boss" ? (int)Math.Floor(from.Col / lanes) == to.Branch : Math.Abs(from.Col - to.Col) <= 1);
                        if (from.Branch != to.Branch) return false;
                        var single = from.ActualType == "rest" || to.Type == "boss" || to.ActualType == "rest";
                        return single || Math.Abs(from.Col - to.Col) <= 1;
                    }).ToList();
                }
            graph.StartIds = layers[0];
            graph.ShrineIds = restSet.Contains(count) ? Enumerable.Range(0, branchCount).Select(k => "n" + count + "_" + (k * lanes)).ToList() : new List<string>();
            graph.ShrineId = graph.ShrineIds.FirstOrDefault();
            graph.BossId = bossIds[0];
            graph.BossIds = bossIds;
            var flat = layers.SelectMany(l => l).ToList();
            graph.Branches = bosses.Select((boss, k) => new Core2MapBranch
            {
                Index = k, BossId = bossIds[k], EncounterId = (string)boss["id"], FirstCol = k * lanes, Lanes = lanes,
                RestIds = flat.Where(id => graph.Nodes[id].Branch == k && graph.Nodes[id].ActualType == "rest").ToList(),
            }).ToList();
            graph.Floors = count + 1;
            graph.Columns = total;
            return graph;
        }

        public static string BossLabel(Core2RunData data, string encounterId) => data.BossLabels.TryGetValue(encounterId, out var label) ? label : encounterId;

        /// <summary>JS bossEncounterForNode: the boss node's encounter, checked against the seat and tier.</summary>
        public static string BossEncounter(Core2RunData data, Core2MapGraph graph, string nodeId, string seat, int tier)
        {
            var node = graph.Node(nodeId);
            if (node.Type != "boss") throw new InvalidOperationException("Boss destination '" + nodeId + "' is not a boss node");
            var encounter = data.Encounter(node.EncounterId);
            if ((string)encounter["pool"] != "boss" || !FitsSeat(encounter, seat, tier, data.ActsPerCycle))
                throw new InvalidOperationException("Boss destination '" + nodeId + "' has no valid encounter for seat '" + seat + "' at tier " + tier);
            return node.EncounterId;
        }
    }
}
