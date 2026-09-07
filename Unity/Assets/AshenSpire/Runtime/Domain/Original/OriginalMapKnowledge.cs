// OriginalMapKnowledge.cs — original map visibility and shrine guidance, without Unity.
// SOURCE: src/model/mapknowledge.js and the edge/node projection in mapboard.js,
// pinned at b17a7f4543e1710f49fae8b58880121690a314de. No RNG or saved-state writes.
// WIRING: Project once when map/path/display preferences change, not every frame.
// MODIFY: visibility rules here; geometry/art in the board; legal travel remains
// the run session's responsibility. Results contain no hidden node or resolved payload.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalMapKnowledge
    {
        public sealed class NodeReading
        {
            public string Id { get; internal set; }
            public string ShownType { get; internal set; }
            public string Knowledge { get; internal set; }
            public bool Revealed { get; internal set; }
            public bool Visited { get; internal set; }
            public bool Current { get; internal set; }
            public bool ShrineLane { get; internal set; }
        }
        public sealed class EdgeReading
        {
            public string From { get; internal set; }
            public string To { get; internal set; }
            public bool Traveled { get; internal set; }
            public bool ShrineLane { get; internal set; }
        }
        public sealed class Projection
        {
            public IReadOnlyList<NodeReading> Nodes { get; internal set; }
            public IReadOnlyCollection<string> VisibleIds { get; internal set; }
            public IReadOnlyList<EdgeReading> Edges { get; internal set; }
        }
        public sealed class ShrinePath
        {
            public string Id { get; internal set; }
            public IReadOnlyList<string> Path { get; internal set; }
            public int Distance { get; internal set; }
        }
        private static IEnumerable<string> Strings(JToken token) => (token as JArray ?? new JArray()).Values<string>();
        private static bool Exists(JObject nodes, string id) => !string.IsNullOrEmpty(id) && nodes?[id] is JObject;

        // Sorted frontiers and ordinal ties match JavaScript's default string sort.
        // The source is excluded even if it is itself a shrine; cycles terminate.
        public static ShrinePath NearestShrine(JObject map, IEnumerable<string> from)
        {
            var nodes = map?["nodes"] as JObject;
            if (nodes == null) return null;
            var seen = new HashSet<string>((from ?? Enumerable.Empty<string>()).Where(id => Exists(nodes,id)), StringComparer.Ordinal);
            var frontier = seen.OrderBy(id => id,StringComparer.Ordinal).ToList();
            var previous = new Dictionary<string,string>(StringComparer.Ordinal);
            var distance = 0;
            while (frontier.Count > 0)
            {
                distance++;
                var step = new List<string>();
                foreach (var id in frontier)
                    foreach (var next in Strings(nodes[id]["next"]))
                        if (Exists(nodes,next) && seen.Add(next)) { previous[next] = id; step.Add(next); }
                step.Sort(StringComparer.Ordinal);
                var hit = step.FirstOrDefault(id => (string)nodes[id]["type"] == "shrine");
                if (hit != null)
                {
                    var path = new List<string> { hit }; var at = hit;
                    while (previous.TryGetValue(at,out var parent)) { at = parent; path.Add(at); }
                    path.Reverse();
                    return new ShrinePath { Id = hit, Path = path.AsReadOnly(), Distance = distance };
                }
                frontier = step;
            }
            return null;
        }

        public static Projection Project(JObject map, IEnumerable<string> path, string currentId, bool fog = true, bool revealUnknown = false, bool shrineGlow = true)
        {
            var nodes = map?["nodes"] as JObject ?? new JObject();
            var trail = (path ?? Enumerable.Empty<string>()).ToList();
            var traveled = new HashSet<string>(trail,StringComparer.Ordinal);
            var lit = new HashSet<string>(StringComparer.Ordinal);
            void Add(string id) { if (Exists(nodes,id)) lit.Add(id); }
            void Shine(string id)
            {
                if (!Exists(nodes,id)) return;
                lit.Add(id); foreach (var next in Strings(nodes[id]["next"])) Add(next);
                if ((string)nodes[id]["type"] == "shrine") Add(NearestShrine(map,new[] { id })?.Id);
            }
            foreach (var id in Strings(map?["startIds"])) Add(id);
            Add((string)map?["bossId"]);
            foreach (var id in trail) Shine(id);
            Shine(currentId);

            var lane = shrineGlow ? NearestShrine(map,!string.IsNullOrEmpty(currentId) ? new[] { currentId } : Strings(map?["startIds"]))?.Path : null;
            var laneNodes = new HashSet<string>(lane ?? Array.Empty<string>(),StringComparer.Ordinal);
            var laneEdges = new HashSet<(string,string)>();
            if (lane != null) for (var index = 0; index + 1 < lane.Count; index++) laneEdges.Add((lane[index],lane[index+1]));
            var readings = new List<NodeReading>();
            var visible = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in nodes.Properties())
            {
                var node = (JObject)property.Value; var id = (string)node["id"];
                if (fog && !lit.Contains(id)) continue;
                var type = (string)node["type"];
                var revealed = type == "event" && revealUnknown && node["resolved"] is JObject resolved && (string)resolved["kind"] != "event";
                var known = type != "event" || revealed;
                readings.Add(new NodeReading { Id = id, ShownType = known ? (revealed ? (string)node["resolved"]["kind"] : type) : "event",
                    Knowledge = known ? "known" : "placed", Revealed = revealed, Current = id == currentId,
                    Visited = traveled.Contains(id) || id == currentId, ShrineLane = laneNodes.Contains(id) });
                visible.Add(id);
            }
            var edges = new List<EdgeReading>();
            foreach (var property in nodes.Properties())
            {
                var node = property.Value; var id = (string)node["id"];
                if (!visible.Contains(id)) continue;
                foreach (var next in Strings(node["next"]))
                {
                    if (!visible.Contains(next) || !Exists(nodes,next)) continue;
                    // indexOf, not every occurrence: preserve original behavior on repeated paths.
                    var index = trail.IndexOf(id);
                    edges.Add(new EdgeReading { From = id, To = next, Traveled = index >= 0 && index + 1 < trail.Count && trail[index+1] == next,
                        ShrineLane = laneEdges.Contains((id,next)) });
                }
            }
            return new Projection { Nodes = readings.AsReadOnly(), VisibleIds = readings.Select(node => node.Id).ToList().AsReadOnly(), Edges = edges.AsReadOnly() };
        }
    }
}
