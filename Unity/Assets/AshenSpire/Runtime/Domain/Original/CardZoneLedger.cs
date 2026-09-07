// CardZoneLedger.cs — one stable instance ID in exactly one card zone.
// Give each deck copy a distinct ID; definition IDs are not instance IDs.
// The combat controller owns draw/shuffle order and commits movement only after legal play.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class CardZoneLedger
    {
        private static readonly HashSet<string> Zones = new HashSet<string>(new[] { "DRAW_PILE", "HAND", "DISCARD_PILE", "EXHAUST_PILE", "SEALED", "REMOVED_FROM_PLAY" }, StringComparer.Ordinal);
        private readonly Dictionary<string, string> _zones = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();
        public CardZoneLedger(IEnumerable<string> instanceIds)
        {
            if (instanceIds == null) throw new ArgumentNullException(nameof(instanceIds));
            foreach (var id in instanceIds) Add(id, "DRAW_PILE");
        }
        public CardZoneLedger(JObject snapshot)
        {
            var cards = snapshot?["instances"] as JArray ?? throw new ArgumentException("Missing zone instances.");
            foreach (var card in cards) Add((string)card["id"], (string)card["zone"]);
        }
        public string ZoneOf(string instanceId) { if (instanceId == null || !_zones.TryGetValue(instanceId, out var zone)) throw new ArgumentException("Unknown card instance: " + instanceId); return zone; }
        public void Move(string instanceId, string zone) { ValidateZone(zone); ZoneOf(instanceId); _zones[instanceId] = zone; }
        public JArray InZone(string zone) { ValidateZone(zone); var result = new JArray(); foreach (var id in _order) if (_zones[id] == zone) result.Add(id); return result; }
        public JObject Snapshot() { var instances = new JArray(); foreach (var id in _order) instances.Add(new JObject { ["id"] = id, ["zone"] = _zones[id] }); return new JObject { ["instances"] = instances }; }
        private void Add(string id, string zone) { ValidateZone(zone); if (string.IsNullOrWhiteSpace(id) || _zones.ContainsKey(id)) throw new ArgumentException("Missing or duplicate card instance: " + id); _zones.Add(id, zone); _order.Add(id); }
        private static void ValidateZone(string zone) { if (zone == null || !Zones.Contains(zone)) throw new ArgumentException("Unknown card zone: " + zone); }
    }
}
