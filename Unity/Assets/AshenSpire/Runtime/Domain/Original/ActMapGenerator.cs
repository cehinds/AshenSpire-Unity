// ActMapGenerator.cs — original procedural graph and RNG draw order, ported from
// engine/mapgen.js and model/floorplan.js. Owns geometry only, not node encounters.
// Edit mapConfigs in Original/content.json. Edges merge without crossing; graph-level
// minimum counts and a rest below the first elite do not promise every route a rest.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class ActMapGenerator
    {
        private sealed class Node
        {
            public string Id, Type;
            public int Floor, Column;
            public readonly List<string> Next = new List<string>();
        }
        private sealed class Plan
        {
            public int Floors, ShrineFrom, EliteFrom, NoShrineOn, MinElites, MinMerchants;
            public bool RestBeforeElite;
            public readonly Dictionary<int, string> Fixed = new Dictionary<int, string>();
            public readonly List<int> RestFloors = new List<int>();
            public int Minimum(string type) => type == "elite" ? MinElites : type == "merchant" ? MinMerchants : 0;
        }
        public static int ResolveAnchor(JToken anchor, int floors)
        {
            if (!(anchor is JObject) || floors < 2) throw new ArgumentException("Invalid floor anchor.");
            var band = floors - 1;
            switch ((string)anchor["at"])
            {
                case "first": return 1;
                case "last": return band;
                case "floor": var index = Integer(anchor, "index"); if (index < 1 || index > band) throw new ArgumentException("Floor anchor outside map."); return index;
                case "fraction": var fraction = FormulaEvaluator.Number(anchor, "of"); if (fraction <= 0 || fraction > 1) throw new ArgumentException("Fraction anchor outside (0,1]."); return Math.Min(band, Math.Max(1, (int)Math.Floor(fraction * band + 0.5)));
                default: throw new ArgumentException("Unknown floor anchor: " + anchor["at"]);
            }
        }
        private static Plan Resolve(JObject config)
        {
            var floors = Integer(config, "floors"); var columns = Integer(config, "columns"); var paths = Integer(config, "pathCount");
            if (floors < 3 || floors > 100 || columns < 1 || columns > 100 || paths < 1 || paths > 100) throw new ArgumentException("Map dimensions outside supported bounds.");
            if (config["entries"] != null && (Integer(config, "entries") < 1 || Integer(config, "entries") > Math.Min(columns, paths))) throw new ArgumentException("Invalid map entries.");
            var rules = config["floorRules"] as JObject ?? throw new ArgumentException("Missing map floorRules.");
            foreach (var retired in new[] { "noEliteOrShrineBefore", "minReachableElites", "minReachableMerchants" }) if (rules[retired] != null) throw new ArgumentException("Retired map rule: " + retired);
            int At(string key, int fallback) => rules[key] == null ? fallback : ResolveAnchor(rules[key], floors);
            int Minimum(string key) { var value = rules[key] == null ? 0 : Integer(rules, key); if (value < 0) throw new ArgumentException("Negative map minimum: " + key); return value; }
            var plan = new Plan { Floors = floors, ShrineFrom = At("noShrineBefore", 1), EliteFrom = At("noEliteBefore", 1), NoShrineOn = At("noShrineOn", 0),
                RestBeforeElite = (bool?)rules["restBeforeElite"] ?? false, MinElites = Minimum("minElites"), MinMerchants = Minimum("minMerchants") };
            foreach (var entry in rules["fixed"] as JArray ?? new JArray())
            {
                var floor = ResolveAnchor(entry, floors); var type = (string)entry["type"];
                if (!new[] { "monster", "elite", "merchant", "shrine", "event", "treasure", "unknown", "boss" }.Contains(type)) throw new ArgumentException("Unknown node type: " + type);
                if (plan.Fixed.TryGetValue(floor, out var old) && old != type) throw new ArgumentException("Conflicting fixed floor: " + floor);
                plan.Fixed[floor] = type;
            }
            if (plan.RestBeforeElite)
            {
                for (var floor = plan.ShrineFrom; floor < plan.EliteFrom && floor < floors; floor++) if (floor != plan.NoShrineOn && !plan.Fixed.ContainsKey(floor)) plan.RestFloors.Add(floor);
                if (plan.RestFloors.Count == 0 && !plan.Fixed.Any(x => x.Value == "shrine" && x.Key < plan.EliteFrom)) throw new ArgumentException("No floor can hold the promised pre-elite rest.");
                foreach (var elite in plan.Fixed.Where(x => x.Value == "elite"))
                    if (!plan.RestFloors.Any(f => f < elite.Key) && !plan.Fixed.Any(x => x.Value == "shrine" && x.Key < elite.Key)) throw new ArgumentException("Fixed elite has no preceding rest floor.");
            }
            Weights(config, "typeWeights"); Weights(config, "unknownWeights");
            return plan;
        }
        public static void Validate(JObject config) => Resolve(config);
        private static JObject Weights(JObject config, string key)
        {
            var weights = config[key] as JObject ?? throw new ArgumentException("Missing " + key);
            var values = weights.Properties().Select(x => FormulaEvaluator.Number(weights, x.Name)).ToArray();
            if (values.Any(x => x < 0) || values.Sum() <= 0) throw new ArgumentException("Invalid " + key);
            return weights;
        }
        private static int Integer(JToken value, string key)
        {
            var number = FormulaEvaluator.Number(value, key);
            if (number != Math.Floor(number) || number > int.MaxValue || number < int.MinValue) throw new ArgumentException("Expected integer " + key);
            return (int)number;
        }
        public static JObject Generate(JObject config, RandomStreams random)
        {
            var plan = Resolve(config);
            var columns = Integer(config, "columns"); var pathCount = Integer(config, "pathCount"); var pathFloors = plan.Floors - 1;
            var starts = new List<int>(); var nodes = new List<Node>();
            // Lists deliberately preserve JavaScript Set/Map insertion order.
            var used = Enumerable.Range(0, pathFloors + 1).Select(_ => new List<int>()).ToArray();
            var edges = Enumerable.Range(0, pathFloors).Select(_ => new List<(int From, List<int> To)>()).ToArray();
            void Add(List<int> list, int value) { if (!list.Contains(value)) list.Add(value); }
            var entries = (int?)config["entries"];
            for (var path = 0; path < pathCount; path++)
            {
                var col = random.Int("map", 0, columns - 1);
                if (entries == null ? path == 1 : path < entries)
                {
                    var guard = 0;
                    while (starts.Contains(col) && ++guard < 50) col = random.Int("map", 0, columns - 1);
                }
                else if (entries != null) col = starts[random.Int("map", 0, entries.Value - 1)];
                starts.Add(col); Add(used[1], col);
                for (var floor = 1; floor < pathFloors; floor++)
                {
                    var next = Math.Max(0, Math.Min(columns - 1, col + random.Int("map", -1, 1)));
                    foreach (var edge in edges[floor]) foreach (var to in edge.To)
                        if ((edge.From < col && to > next) || (edge.From > col && to < next)) next = to;
                    var index = edges[floor].FindIndex(x => x.From == col);
                    if (index < 0) { edges[floor].Add((col, new List<int>())); index = edges[floor].Count - 1; }
                    Add(edges[floor][index].To, next); col = next; Add(used[floor + 1], col);
                }
            }
            string Id(int floor, int col) => "n" + floor + "_" + col;
            for (var floor = 1; floor <= pathFloors; floor++) foreach (var col in used[floor]) nodes.Add(new Node { Id = Id(floor, col), Floor = floor, Column = col });
            var byId = nodes.ToDictionary(x => x.Id);
            for (var floor = 1; floor < pathFloors; floor++) foreach (var edge in edges[floor]) foreach (var to in edge.To) byId[Id(floor, edge.From)].Next.Add(Id(floor + 1, to));
            var shrine = new Node { Id = Id(plan.Floors, columns / 2), Floor = plan.Floors, Column = columns / 2, Type = "shrine" };
            var boss = new Node { Id = Id(plan.Floors + 1, columns / 2), Floor = plan.Floors + 1, Column = columns / 2, Type = "boss" };
            foreach (var col in used[pathFloors]) byId[Id(pathFloors, col)].Next.Add(shrine.Id);
            shrine.Next.Add(boss.Id); nodes.Add(shrine); nodes.Add(boss);
            var rollable = nodes.Where(x => x.Type == null).ToArray();
            for (var attempt = 0; attempt < 40; attempt++)
            {
                foreach (var node in rollable) node.Type = null;
                var lowestShrine = int.MaxValue;
                foreach (var node in rollable.OrderBy(x => x.Floor))
                {
                    if (plan.Fixed.TryGetValue(node.Floor, out var type)) node.Type = type;
                    else
                    {
                        var banned = nodes.Where(x => x.Next.Contains(node.Id) && x.Type != null && x.Type != "monster").Select(x => x.Type).ToHashSet();
                        var weights = ((JObject)config["typeWeights"]).Properties().Where(x => (double)x.Value > 0 && !banned.Contains(x.Name)
                            && !(x.Name == "elite" && node.Floor < plan.EliteFrom)
                            && !(x.Name == "shrine" && (node.Floor < plan.ShrineFrom || node.Floor == plan.NoShrineOn))
                            && !(x.Name == "elite" && plan.RestBeforeElite && lowestShrine >= node.Floor)).ToArray();
                        node.Type = "monster";
                        if (weights.Length > 0)
                        {
                            var roll = random.Float("map") * weights.Sum(x => (double)x.Value);
                            foreach (var weight in weights) { roll -= (double)weight.Value; if (roll < 0) { node.Type = weight.Name; break; } }
                        }
                    }
                    if (node.Type == "shrine") lowestShrine = Math.Min(lowestShrine, node.Floor);
                }
                if (Count(nodes, "elite") >= plan.MinElites && Count(nodes, "merchant") >= plan.MinMerchants) { EnsureRest(nodes, plan, random); return Finish(); }
            }
            Relax(nodes, "elite", plan.MinElites, plan, random); Relax(nodes, "merchant", plan.MinMerchants, plan, random); EnsureRest(nodes, plan, random);
            return Finish();

            JObject Finish()
            {
                var records = new JObject();
                foreach (var node in nodes) records[node.Id] = new JObject { ["id"] = node.Id, ["floor"] = node.Floor, ["col"] = node.Column, ["type"] = node.Type, ["next"] = new JArray(node.Next) };
                return new JObject { ["nodes"] = records, ["startIds"] = new JArray(starts.Distinct().Select(col => Id(1, col))), ["shrineId"] = shrine.Id, ["bossId"] = boss.Id, ["floors"] = plan.Floors, ["columns"] = columns };
            }
        }
        private static int Count(List<Node> nodes, string type) => nodes.Count(x => x.Type == type);
        private static int Lowest(List<Node> nodes, string type) => nodes.Where(x => x.Type == type).Select(x => x.Floor).DefaultIfEmpty(int.MaxValue).Min();
        private static void Relax(List<Node> nodes, string type, int minimum, Plan plan, RandomStreams random)
        {
            var have = Count(nodes, type); if (have >= minimum) return;
            if (type == "elite" && plan.RestBeforeElite && Lowest(nodes, "shrine") >= plan.EliteFrom)
            {
                var rest = nodes.Where(x => plan.RestFloors.Contains(x.Floor)).ToList(); var monsters = rest.Where(x => x.Type == "monster").ToList();
                var pool = monsters.Count > 0 ? monsters : rest;
                if (pool.Count > 0) pool[random.Int("map", 0, pool.Count - 1)].Type = "shrine";
            }
            var restFloor = plan.RestBeforeElite ? Lowest(nodes, "shrine") : int.MinValue;
            var eligible = nodes.Where(x => x.Type == "monster" && !(type == "elite" && x.Floor < plan.EliteFrom) && !(type == "elite" && plan.RestBeforeElite && x.Floor <= restFloor)).ToList();
            while (have < minimum && eligible.Count > 0) { var index = random.Int("map", 0, eligible.Count - 1); eligible[index].Type = type; eligible.RemoveAt(index); have++; }
        }
        private static void EnsureRest(List<Node> nodes, Plan plan, RandomStreams random)
        {
            var eliteFloor = Lowest(nodes, "elite");
            if (!plan.RestBeforeElite || eliteFloor == int.MaxValue || Lowest(nodes, "shrine") < eliteFloor) return;
            var below = nodes.Where(x => x.Floor < eliteFloor && plan.RestFloors.Contains(x.Floor)).ToList(); if (below.Count == 0) return;
            var monsters = below.Where(x => x.Type == "monster").ToList(); var spares = below.Where(x => plan.Minimum(x.Type) == 0 || Count(nodes, x.Type) > plan.Minimum(x.Type)).ToList();
            var pool = monsters.Count > 0 ? monsters : spares.Count > 0 ? spares : below;
            pool[random.Int("map", 0, pool.Count - 1)].Type = "shrine";
            foreach (var type in new[] { "elite", "merchant" })
            {
                var minimum = plan.Minimum(type);
                while (Count(nodes, type) < minimum)
                {
                    Relax(nodes, type, minimum, plan, random); if (Count(nodes, type) >= minimum) break;
                    bool ShrineSpare(Node node) => node.Floor != plan.Floors && (node.Floor >= eliteFloor || nodes.Any(x => x != node && x.Type == "shrine" && x.Floor < eliteFloor));
                    var donor = nodes.FirstOrDefault(x => x.Type != type && x.Type != "boss" && (x.Type != "shrine" || ShrineSpare(x)) && !plan.Fixed.ContainsKey(x.Floor) && (plan.Minimum(x.Type) == 0 || Count(nodes, x.Type) > plan.Minimum(x.Type)));
                    if (donor == null) break; donor.Type = type;
                }
            }
        }
    }
}
