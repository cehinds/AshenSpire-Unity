// Core2Rows.cs — typed views over the JS Core 2 export (tools/export-js-core2.mjs).
// Each row exposes the fields later phases need by name and keeps its JSON as Raw, so
// nothing the JS game authored is lost before the phase that uses it is written.
// Values are copied as the JS build ships them at the pin (SPEC.md §0.4 rule 4).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    internal static class Core2Json
    {
        public static JObject ParseObject(string json, string label) => Parse(json, label) as JObject
            ?? throw new InvalidDataException(label + ": expected a JSON object");
        public static JArray ParseArray(string json, string label) => Parse(json, label) as JArray
            ?? throw new InvalidDataException(label + ": expected a JSON array");
        public static JToken Parse(string json, string label)
        {
            if (string.IsNullOrEmpty(json)) throw new InvalidDataException(label + ": empty");
            try
            {
                using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double };
                var token = JToken.ReadFrom(reader);
                while (reader.Read()) if (reader.TokenType != JsonToken.Comment) throw new InvalidDataException(label + ": trailing content after the JSON value");
                return token;
            }
            catch (JsonException error) { throw new InvalidDataException(label + ": " + error.Message, error); }
        }
        public static string RequiredString(JObject row, string key, string label)
        {
            var value = row?[key];
            if (value == null || value.Type != JTokenType.String || string.IsNullOrEmpty((string)value))
                throw new InvalidDataException(label + "." + key + " must be a non-empty string");
            return (string)value;
        }
        public static string OptionalString(JObject row, string key) => row?[key]?.Type == JTokenType.String ? (string)row[key] : null;
        public static int Int(JObject row, string key, int fallback = 0)
        {
            var value = row?[key];
            return value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) ? (int)Math.Floor((double)value) : fallback;
        }
        public static IReadOnlyList<string> Strings(JToken token) => token is JArray array
            ? array.Where(item => item.Type == JTokenType.String).Select(item => (string)item).ToList()
            : (IReadOnlyList<string>)Array.Empty<string>();
        public static IReadOnlyDictionary<string, int> IntMap(JToken token) => token is JObject obj
            ? obj.Properties().Where(p => p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float).ToDictionary(p => p.Name, p => (int)Math.Floor((double)p.Value), StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>A Core 2 card (player, base or enemy card): pack/cards.json, pack/enemyCards.json, adapter/cards.json.</summary>
    public sealed class Core2Card
    {
        public string Id { get; }
        public string Name { get; }
        public string Class { get; }
        public string Rarity { get; }
        public string Type { get; }
        /// <summary>True for an X-cost card (JS cost "X"): SP is everything the player has (JS profileCardCosts).</summary>
        public bool XCost { get; }
        /// <summary>The authored generic cost; 0 for an X-cost card.</summary>
        public int Cost { get; }
        public int StaminaCost { get; }
        public int ManaCost { get; }
        public IReadOnlyList<string> Tags { get; }
        public string TextTemplate { get; }
        public JObject Raw { get; }

        public Core2Card(JObject raw, string label)
        {
            Raw = raw;
            Id = Core2Json.RequiredString(raw, "id", label);
            Name = Core2Json.RequiredString(raw, "name", label + "[" + Id + "]");
            Class = Core2Json.OptionalString(raw, "class");
            Rarity = Core2Json.OptionalString(raw, "rarity");
            Type = Core2Json.RequiredString(raw, "type", label + "[" + Id + "]");
            XCost = raw["cost"]?.Type == JTokenType.String && (string)raw["cost"] == "X";
            if (raw["cost"]?.Type == JTokenType.String && !XCost) throw new InvalidDataException(label + "[" + Id + "].cost must be a number or \"X\"");
            Cost = XCost ? 0 : Core2Json.Int(raw, "cost");
            StaminaCost = Core2Json.Int(raw, "staminaCost");
            ManaCost = Core2Json.Int(raw, "manaCost");
            Tags = Core2Json.Strings(raw["tags"]);
            TextTemplate = Core2Json.OptionalString(raw, "textTemplate");
            if (!(raw["effects"] is JArray)) throw new InvalidDataException(label + "[" + Id + "].effects must be an array");
        }
        public JArray Effects => (JArray)Raw["effects"];
    }

    /// <summary>A playable class (pack/classes.json): six-point attributes, fixed loadout, special card.</summary>
    public sealed class Core2Class
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyDictionary<string, int> Attributes { get; }
        public IReadOnlyList<string> DefaultDeck { get; }
        public string RightHand { get; }
        public string LeftHand { get; }
        public string SpecialCardId { get; }
        public int MaxHp { get; }
        public string Description { get; }
        public JObject Raw { get; }

        public Core2Class(JObject raw)
        {
            Raw = raw;
            Id = Core2Json.RequiredString(raw, "id", "classes");
            Name = Core2Json.RequiredString(raw, "name", "classes[" + Id + "]");
            Attributes = Core2Json.IntMap(raw["attributes"]);
            DefaultDeck = Core2Json.Strings(raw["defaultDeck"]);
            RightHand = Core2Json.OptionalString(raw["loadout"] as JObject, "rightHand");
            LeftHand = Core2Json.OptionalString(raw["loadout"] as JObject, "leftHand");
            SpecialCardId = Core2Json.RequiredString(raw, "specialCardId", "classes[" + Id + "]");
            MaxHp = Core2Json.Int(raw, "maxHp");
            Description = Core2Json.OptionalString(raw, "description");
            if (Attributes.Count == 0 || MaxHp <= 0) throw new InvalidDataException("classes[" + Id + "] needs attributes and a positive maxHp");
        }
    }

    /// <summary>A Core character row (pack/characters.json): the four classes and the 33 enemies as authored characters.</summary>
    public sealed class Core2Character
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<string> Tags { get; }
        public bool IsPlayer => Raw["player"] is JObject;
        public bool IsEnemy => Raw["enemy"] is JObject;
        /// <summary>Gear keys to item ids (defaultGear).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> DefaultGear { get; }
        public JObject Raw { get; }

        public Core2Character(JObject raw)
        {
            Raw = raw;
            Id = Core2Json.RequiredString(raw, "id", "characters");
            Name = Core2Json.RequiredString(raw, "name", "characters[" + Id + "]");
            Tags = Core2Json.Strings(raw["tags"]);
            DefaultGear = ((raw["defaultGear"] as JArray) ?? new JArray()).OfType<JObject>()
                .Select(row => new KeyValuePair<string, string>(Core2Json.RequiredString(row, "key", "characters[" + Id + "].defaultGear"), Core2Json.RequiredString(row, "id", "characters[" + Id + "].defaultGear"))).ToList();
        }
    }

    /// <summary>A Core gear piece (pack/gear.json): rightHand, leftHand or armor.</summary>
    public sealed class Core2Gear
    {
        public string Id { get; }
        public string Name { get; }
        public string Slot { get; }
        public string Kind { get; }
        public string Rarity { get; }
        public string Range { get; }
        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyDictionary<string, int> Points { get; }
        public JObject Raw { get; }

        public Core2Gear(JObject raw)
        {
            Raw = raw;
            Id = Core2Json.RequiredString(raw, "id", "gear");
            Name = Core2Json.RequiredString(raw, "name", "gear[" + Id + "]");
            Slot = Core2Json.RequiredString(raw, "slot", "gear[" + Id + "]");
            Kind = Core2Json.RequiredString(raw, "kind", "gear[" + Id + "]");
            Rarity = Core2Json.OptionalString(raw, "rarity");
            Range = Core2Json.OptionalString(raw, "range");
            Tags = Core2Json.Strings(raw["tags"]);
            Points = Core2Json.IntMap(raw["points"]);
        }
    }

    /// <summary>An enemy as the Core 2 runtime registry compiles it (adapter/enemies.json): HP range, Poise, moves.</summary>
    public sealed class Core2Enemy
    {
        public string Id { get; }
        public string Name { get; }
        public int HpMin { get; }
        public int HpMax { get; }
        public int PoiseMax { get; }
        public IReadOnlyList<Core2EnemyMove> Moves { get; }
        public IReadOnlyDictionary<string, int> Attributes { get; }
        public JObject Raw { get; }

        public Core2Enemy(JObject raw)
        {
            Raw = raw;
            Id = Core2Json.RequiredString(raw, "id", "enemies");
            Name = Core2Json.RequiredString(raw, "name", "enemies[" + Id + "]");
            var hp = raw["hp"];
            if (hp is JArray range && range.Count == 2) { HpMin = (int)range[0]; HpMax = (int)range[1]; }
            else if (hp != null && hp.Type == JTokenType.Integer) HpMin = HpMax = (int)hp;
            else throw new InvalidDataException("enemies[" + Id + "].hp must be a number or [min, max]");
            PoiseMax = Core2Json.Int(raw, "poiseMax");
            Attributes = Core2Json.IntMap(raw["attributes"]);
            // JS object order is preserved by the export and is the order the game reads moves in.
            Moves = ((raw["moves"] as JObject) ?? throw new InvalidDataException("enemies[" + Id + "].moves is required"))
                .Properties().Select(p => new Core2EnemyMove(Id, p.Name, (JObject)p.Value)).ToList();
        }
        public Core2EnemyMove Move(string id) => Moves.FirstOrDefault(m => m.Id == id);
    }

    public sealed class Core2EnemyMove
    {
        public string EnemyId { get; }
        public string Id { get; }
        public string Intent { get; }
        /// <summary>Authored SP; null when JS derives it.</summary>
        public int? Sp { get; }
        public int? Damage { get; }
        public int Hits { get; }
        public int Weight { get; }
        public IReadOnlyList<string> Tags { get; }
        public JObject Raw { get; }

        public Core2EnemyMove(string enemyId, string id, JObject raw)
        {
            EnemyId = enemyId; Id = id; Raw = raw;
            Intent = Core2Json.RequiredString(raw, "intent", "enemies[" + enemyId + "].moves[" + id + "]");
            Sp = raw["sp"] != null ? Core2Json.Int(raw, "sp") : (int?)null;
            Damage = raw["damage"] != null && raw["damage"].Type != JTokenType.Null ? Core2Json.Int(raw, "damage") : (int?)null;
            Hits = Core2Json.Int(raw, "hits", 1);
            Weight = Core2Json.Int(raw, "weight");
            Tags = Core2Json.Strings(raw["tags"]);
        }
    }

    /// <summary>An id/name row from a legacy-adapter table (relics, events, flasks, statuses, enemy relics, seats, encounters).</summary>
    public sealed class Core2Row
    {
        public string Id { get; }
        public string Name { get; }
        public string Rarity { get; }
        public JObject Raw { get; }

        public Core2Row(JObject raw, string label, bool nameRequired = true)
        {
            Raw = raw;
            Id = Core2Json.RequiredString(raw, "id", label);
            Name = nameRequired ? Core2Json.RequiredString(raw, "name", label + "[" + Id + "]") : Core2Json.OptionalString(raw, "name");
            Rarity = Core2Json.OptionalString(raw, "rarity");
        }
    }

    /// <summary>packs/core/routes.json as resolved: three acts, fog map, rest floors, branching.</summary>
    public sealed class Core2Routes
    {
        public int Acts { get; }
        public int PrebossNodes { get; }
        public int Bosses { get; }
        public int RestsPerAct { get; }
        public IReadOnlyList<int> RestFloors { get; }
        public int BranchColumns { get; }
        /// <summary>Null when the act does not split into branches (JS maxBranches absent).</summary>
        public int? MaxBranches { get; }
        public int MinimumThreatFloor { get; }
        public string MapMode { get; }
        public string FirstFloor { get; }
        public double ShopShare { get; }
        public bool RequirePrebossRest { get; }
        public IReadOnlyList<string> HiddenTypes { get; }
        public IReadOnlyList<string> VisibleTypes { get; }
        public JObject Raw { get; }

        public Core2Routes(JObject raw)
        {
            Raw = raw;
            Acts = Core2Json.Int(raw, "acts"); PrebossNodes = Core2Json.Int(raw, "prebossNodes"); Bosses = Core2Json.Int(raw, "bosses");
            RestsPerAct = Core2Json.Int(raw, "restsPerAct");
            RestFloors = ((raw["restFloors"] as JArray) ?? new JArray()).Select(v => (int)v).ToList();
            BranchColumns = Core2Json.Int(raw, "branchColumns");
            MaxBranches = raw["maxBranches"] != null && raw["maxBranches"].Type != JTokenType.Null ? Core2Json.Int(raw, "maxBranches") : (int?)null;
            MinimumThreatFloor = Core2Json.Int(raw, "minimumThreatFloor");
            MapMode = Core2Json.OptionalString(raw, "mapMode"); FirstFloor = Core2Json.OptionalString(raw, "firstFloor");
            ShopShare = (double?)raw["shopShare"] ?? 0;
            RequirePrebossRest = (bool?)raw["requirePrebossRest"] ?? false;
            HiddenTypes = Core2Json.Strings(raw["hiddenTypes"]); VisibleTypes = Core2Json.Strings(raw["visibleTypes"]);
            if (Acts < 1 || PrebossNodes < 1 || BranchColumns < 2 || BranchColumns > 5 || (MaxBranches.HasValue && (MaxBranches < 1 || MaxBranches > 5)))
                throw new InvalidDataException("routes.json: acts, prebossNodes, branchColumns (2-5) and maxBranches (1-5) are out of range");
            if (RestFloors.Any(f => f < 1 || f > PrebossNodes)) throw new InvalidDataException("routes.json: restFloors must lie within 1.." + PrebossNodes);
        }
    }
}
