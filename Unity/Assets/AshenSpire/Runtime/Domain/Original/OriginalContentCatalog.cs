// OriginalContentCatalog.cs — immutable boundary around the imported original tables.
// Author GameContent/Unity/Original/content.json (or original-table.py CSV exports).
// Source import pins the upstream SHA; runtime edits are copies, never shared assets.
// Importing a row does not implement its effects. Capability coverage is reported
// separately so unsupported gameplay cannot silently become a no-op.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalContentCatalog
    {
        private readonly JObject _content;
        private readonly TagCatalog _tags;
        public string Version => (string)_content["version"];
        public OriginalContentCatalog(string json)
        {
            _content = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            foreach (var table in new[] { "cards", "classes", "statuses", "stances", "enemies", "encounters", "events", "flasks", "relics", "attributes", "creationModes", "unlocks" })
            {
                var rows = _content[table] as JArray ?? throw new ArgumentException("Missing original table: " + table);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in rows)
                {
                    if (!(row is JObject)) throw new ArgumentException("Invalid original record in " + table);
                    var id = (string)row["id"];
                    if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new ArgumentException("Duplicate or missing ID: " + table + "/" + id);
                }
            }
            _tags = new TagCatalog(_content);
            OriginalContentValidation.ValidateReferences(_content);
            var cards = ((JArray)_content["cards"]).Select(x => (string)x["id"]).ToHashSet();
            foreach (var hero in _content["classes"])
            {
                foreach (var id in hero["cardPool"]) if (!cards.Contains((string)id)) throw new ArgumentException("Missing class card: " + id);
                var allocation = hero["startingFlaskAllocation"];
                _ = new FlaskChargePool((int)_content["balance"]["flaskCapacity"], (int)allocation["hp"], (int)allocation["mana"]);
            }
            foreach (var config in ((JObject)_content["mapConfigs"]).Properties()) ActMapGenerator.Validate((JObject)config.Value);
            foreach (var table in new[] { "cards", "statuses", "stances", "enemies", "relics", "events", "flasks", "scripts" }) ValidateBehaviors(_content[table]);
            var rules = DerivedStatCalculator.Resolve((JObject)_content["derivedStatRules"]);
            foreach (var mode in ((JObject)_content["attributeRules"]["presets"]).Properties()) foreach (var hero in _content["classes"])
                foreach (var rule in rules.Properties()) DerivedStatCalculator.Receipt(rules, rule.Name, (JObject)mode.Value[(string)hero["id"]], (JObject)hero);
        }
        public JObject Data() => (JObject)_content.DeepClone();
        private void ValidateBehaviors(JToken node)
        {
            if (node is JObject record)
            {
                if (record["op"] != null)
                {
                    var operation = (string)record["op"];
                    var known = new[] { "damage", "block", "dodgeRoll", "applyStatus", "removeStatus", "draw", "discard", "exhaust", "addCard", "gainEnergy", "restoreMana", "loseHp", "heal", "shuffleDiscardIntoDraw", "enterStance", "poiseDamage", "stagger", "addCinders", "addCardToDeck", "removeCardFromDeck", "upgradeCard", "addRelic", "addFlask", "addFlaskCapacity", "loseMaxHpPct", "startCombat" };
                    if (!known.Contains(operation)) throw new ArgumentException("Unknown original effect at " + record.Path + ": " + operation);
                    foreach (var reference in new[] { ("status", "statuses"), ("stance", "stances"), ("card", "cards"), ("relic", "relics"), ("flask", "flasks") })
                        if (record[reference.Item1]?.Type == JTokenType.String && !_content[reference.Item2].Any(x => (string)x["id"] == (string)record[reference.Item1])) throw new ArgumentException("Unknown " + reference.Item1 + " at " + record.Path);
                }
                foreach (var property in record.Properties()) ValidateBehaviors(property.Value);
            }
            else if (node is JArray array) foreach (var value in array) ValidateBehaviors(value);
        }
        public JArray Table(string name) => (JArray)(_content.SelectToken(name) as JArray ?? throw new ArgumentException("Unknown table: " + name)).DeepClone();
        public JObject Record(string table, string id, string scope = null)
        {
            var rows = _content.SelectToken(table) as JArray ?? throw new ArgumentException("Unknown table: " + table);
            var matches = rows.OfType<JObject>().Where(x => (string)x["id"] == id && (scope == null || (string)x["classId"] == scope)).ToArray();
            if (matches.Length != 1) throw new ArgumentException("Unknown or ambiguous original record: " + table + "/" + id);
            return (JObject)matches[0].DeepClone();
        }
        public string[] Tags(string family, JObject record) => _tags.For(family, record);
    }
}
