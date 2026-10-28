// Core2RunData.cs — what a Core 2 run reads from the export beyond the combat tables (port plan phase 8).
//
// DATA: GameContent/Unity/Core2/adapter/run.json (tools/export-js-core2.mjs, never edited by hand): the creation
// resolvers' output per class (starting relics, fixed loadout, flask charges and the derived-stat rule snapshot the JS
// run is born with), the balance rows a run reads, the property rules a run's carriers mount, the boss destination
// labels, the run-scoped equipment mod fields, the damage-source rules a card face reads and the Smith's tier ladder.
// The other run tables are already typed on Core2Content (routes, rewards, shops, rests, unlocks, encounters, events).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>A class's legacy row and the state JS createRunState gives it at birth (adapter/run.json classes).</summary>
    public sealed class Core2ClassBirth
    {
        public string ClassId { get; set; }
        public int MaxHp { get; set; }
        public IReadOnlyList<string> PropertyTags { get; set; }
        /// <summary>The class card's own tags: the scope its passives (favored skillXpMult) speak for.</summary>
        public IReadOnlyList<string> Tags { get; set; }
        public IReadOnlyDictionary<string, int> Attributes { get; set; }
        public IReadOnlyList<string> Relics { get; set; }
        public JObject Loadout { get; set; }
        public JObject FlaskCharges { get; set; }
        public int Cinders { get; set; }
        public string AttributeMode { get; set; }
        /// <summary>The derived-stat rule table (ruleset 7) the run is born with: its rows already fold the starting relics.</summary>
        public JObject DerivedStatRules { get; set; }
    }

    public sealed class Core2RunData
    {
        public const string Path = "adapter/run.json";
        public Core2Content Content { get; }
        public JObject Raw { get; }
        public IReadOnlyDictionary<string, Core2ClassBirth> Classes { get; }
        public JObject Balance { get; }
        public IReadOnlyDictionary<string, string> BossLabels { get; }
        public JObject ModFields { get; }
        public JObject Faces { get; }
        public IReadOnlyList<JObject> ItemUpgradeChanges { get; }
        private readonly JObject _propertyRules;
        private readonly Dictionary<string, JObject> _armaments;
        private readonly Dictionary<string, JObject> _relics;
        private readonly Dictionary<string, JObject> _flasks;
        private readonly Dictionary<string, JObject> _encounters;
        private readonly Dictionary<string, JObject> _events;

        public Core2RunData(Core2Content content)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Raw = content.Table(Path) as JObject ?? throw new InvalidDataException(Path + ": expected an object");
            Balance = (JObject)Raw["balance"] ?? throw new InvalidDataException(Path + ": balance is required");
            _propertyRules = (JObject)Raw["propertyRules"] ?? new JObject();
            ModFields = (JObject)Raw["modFields"] ?? new JObject();
            Faces = (JObject)Raw["faces"] ?? new JObject();
            BossLabels = ((JObject)Raw["bossLabels"] ?? new JObject()).Properties().ToDictionary(p => p.Name, p => (string)p.Value, StringComparer.Ordinal);
            ItemUpgradeChanges = ((JArray)Raw["itemUpgradeChanges"] ?? new JArray()).OfType<JObject>().ToList();
            var classes = new Dictionary<string, Core2ClassBirth>(StringComparer.Ordinal);
            foreach (var row in ((JObject)Raw["classes"] ?? new JObject()).Properties())
            {
                var value = (JObject)row.Value;
                var birth = (JObject)value["birth"];
                classes[row.Name] = new Core2ClassBirth
                {
                    ClassId = row.Name, MaxHp = (int)value["maxHp"], PropertyTags = Core2Json.Strings(value["propertyTags"]), Tags = Core2Json.Strings(value["tags"]),
                    Attributes = Core2Json.IntMap(birth["attributes"]), Relics = Core2Json.Strings(birth["relics"]), Loadout = (JObject)birth["loadout"],
                    FlaskCharges = (JObject)birth["flaskCharges"], Cinders = (int)birth["cinders"], AttributeMode = (string)birth["attributeMode"],
                    DerivedStatRules = (JObject)birth["derivedStatRules"],
                };
            }
            Classes = classes;
            _armaments = content.Armaments.ToDictionary(a => a.Id, a => a.Raw, StringComparer.Ordinal);
            _relics = content.Relics.ToDictionary(r => r.Id, r => r.Raw, StringComparer.Ordinal);
            _flasks = content.Flasks.ToDictionary(r => r.Id, r => r.Raw, StringComparer.Ordinal);
            _encounters = content.Encounters.ToDictionary(r => r.Id, r => r.Raw, StringComparer.Ordinal);
            _events = content.Events.ToDictionary(r => r.Id, r => r.Raw, StringComparer.Ordinal);
            foreach (var cls in content.Classes)
                if (!classes.ContainsKey(cls.Id)) throw new InvalidDataException(Path + ": class " + cls.Id + " has no birth row");
        }

        public Core2ClassBirth Class(string id) => Classes.TryGetValue(id ?? "", out var row) ? row : throw new KeyNotFoundException("Core 2 class " + id + " has no birth row");
        public JObject Armament(string id) => id != null && _armaments.TryGetValue(id, out var row) ? row : null;
        public JObject Relic(string id) => id != null && _relics.TryGetValue(id, out var row) ? row : throw new KeyNotFoundException("Unknown relic " + id);
        public bool HasRelic(string id) => id != null && _relics.ContainsKey(id);
        public JObject Flask(string id) => id != null && _flasks.TryGetValue(id, out var row) ? row : throw new KeyNotFoundException("Unknown flask " + id);
        public bool HasFlask(string id) => id != null && _flasks.ContainsKey(id);
        public JObject Encounter(string id) => id != null && _encounters.TryGetValue(id, out var row) ? row : throw new KeyNotFoundException("Unknown encounter " + id);
        public JObject Event(string id) => id != null && _events.TryGetValue(id, out var row) ? row : throw new KeyNotFoundException("Unknown event " + id);
        public JObject Armour(string classId, string id) => Content.Armour.FirstOrDefault(a => a.Id == id && (string)a.Raw["classId"] == classId)?.Raw;
        public JObject PropertyRule(string tag) => _propertyRules[tag] as JObject ?? throw new KeyNotFoundException("Unknown property tag " + tag);
        public JObject Module(string id) => Content.Profile["modules"]?[id] as JObject;

        /// <summary>JS carrierRules: each tag's rule, kept only when every tag it requires is held and none it excludes is.</summary>
        public List<JObject> CarrierRules(IReadOnlyList<string> tags)
        {
            var held = new HashSet<string>(tags ?? Array.Empty<string>(), StringComparer.Ordinal);
            var rules = new List<JObject>();
            foreach (var tag in tags ?? Array.Empty<string>())
            {
                var rule = PropertyRule(tag);
                if (Core2Json.Strings(rule["requires"]).Any(t => !held.Contains(t))) continue;
                if (Core2Json.Strings(rule["excludes"]).Any(t => held.Contains(t))) continue;
                rules.Add(rule);
            }
            return rules;
        }

        /// <summary>JS passiveMult over held relics (no mounts): the product of each relic's numeric passive.</summary>
        public double RelicPassiveMult(IEnumerable<string> relicIds, string key)
        {
            var m = 1.0;
            foreach (var id in relicIds ?? Enumerable.Empty<string>())
                if (Relic(id)["passives"]?[key] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)) m *= (double)v;
            return m;
        }

        public int StartingCinders => (int?)Balance["startingCinders"] ?? 0;
        public int FlaskSlots => (int)Balance["flaskSlots"];
        public int ActsPerCycle => (int)Balance["actsPerCycle"];
        public double SeatTier(int tier) => (double)Balance["seatTiers"][tier.ToString(System.Globalization.CultureInfo.InvariantCulture)];
    }
}
