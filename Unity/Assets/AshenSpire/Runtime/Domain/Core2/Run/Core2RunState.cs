// Core2RunState.cs — the state of a Core 2 run (port plan phase 8), engine-free.
//
// The fields are the JS run's own (src/model/state.js createRunState / RUN_SHAPE, schema 25) for the parts a Core 2 run
// uses, under the same names, so a run serializes to the JS field names (Core2RunSave) and an oracle's run state loads
// directly. Legacy bookkeeping a Core 2 run never reads (class mastery, sigils, companions, smithing stones, the heal
// ledger, Ashen Blight, the legacy level ladder) is not carried.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>One owned card instance (JS { instanceId, cardId, upgraded, equipmentRole?, grantedBy? }).</summary>
    public sealed class Core2RunCard
    {
        public string InstanceId { get; set; }
        public string CardId { get; set; }
        public bool Upgraded { get; set; }
        /// <summary>The item that lends this card (JS grantedBy): an armament id, or armor/&lt;class&gt;/&lt;id&gt;.</summary>
        public string GrantedBy { get; set; }
        /// <summary>JS equipmentRole ("granted" for a gear grant); null for a loose card.</summary>
        public string EquipmentRole { get; set; }
        /// <summary>JS isItemOwned: a card an item lends, which the item takes back when it is unequipped.</summary>
        public bool ItemOwned => GrantedBy != null || EquipmentRole != null;
        public Core2RunCard Clone() => (Core2RunCard)MemberwiseClone();

        public JObject ToJson()
        {
            var row = new JObject { ["instanceId"] = InstanceId, ["cardId"] = CardId, ["upgraded"] = Upgraded };
            if (EquipmentRole != null) row["equipmentRole"] = EquipmentRole;
            if (GrantedBy != null) row["grantedBy"] = GrantedBy;
            return row;
        }
        public static Core2RunCard FromJson(JToken token)
        {
            var row = (JObject)token;
            var card = new Core2RunCard
            {
                InstanceId = (string)row["instanceId"], CardId = (string)row["cardId"], Upgraded = (bool?)row["upgraded"] ?? false,
                GrantedBy = (string)row["grantedBy"], EquipmentRole = (string)row["equipmentRole"],
            };
            // A view without the role fields still names a gear grant by its mount id (JS cardMounts granted:<item>:<card>:<n>).
            if (card.GrantedBy == null && card.InstanceId != null && card.InstanceId.StartsWith("granted:", StringComparison.Ordinal))
            {
                var parts = card.InstanceId.Split(':');
                if (parts.Length >= 4) { card.GrantedBy = string.Join(":", parts.Skip(1).Take(parts.Length - 3)); card.EquipmentRole = "granted"; }
            }
            return card;
        }
    }

    /// <summary>JS run.loadout: per slot the item sets and the active index, carried storage and bought armour.</summary>
    public sealed class Core2RunLoadout
    {
        public Dictionary<string, List<string>> Sets { get; set; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<string, int> Active { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<string> Storage { get; set; } = new List<string>();
        /// <summary>JS loadout.boughtArmour: { classId, id } rows owned but not worn.</summary>
        public List<KeyValuePair<string, string>> BoughtArmour { get; set; } = new List<KeyValuePair<string, string>>();
        public JObject CreationArmourGrant { get; set; }

        public string Equipped(string slotId)
        {
            if (!Sets.TryGetValue(slotId, out var ids)) return null;
            Active.TryGetValue(slotId, out var index);
            return index >= 0 && index < ids.Count ? ids[index] : null;
        }
        /// <summary>Every armament id carried: storage and every hand set (JS carriedIds).</summary>
        public IEnumerable<string> Carried() => Storage.Concat(Sets.Where(p => p.Key != "armor").SelectMany(p => p.Value)).Where(id => id != null);

        public Core2RunLoadout Clone() => FromJson(ToJson());
        public JObject ToJson()
        {
            var sets = new JObject();
            foreach (var pair in Sets) sets[pair.Key] = new JArray(pair.Value.Select(id => (JToken)id ?? JValue.CreateNull()));
            var active = new JObject();
            foreach (var pair in Active) active[pair.Key] = pair.Value;
            var row = new JObject { ["sets"] = sets, ["active"] = active, ["storage"] = new JArray(Storage),
                ["boughtArmour"] = new JArray(BoughtArmour.Select(p => new JObject { ["classId"] = p.Key, ["id"] = p.Value })) };
            if (CreationArmourGrant != null) row["creationArmourGrant"] = CreationArmourGrant.DeepClone();
            return row;
        }
        public static Core2RunLoadout FromJson(JToken token)
        {
            var row = (JObject)token;
            var loadout = new Core2RunLoadout();
            foreach (var set in ((JObject)row["sets"]).Properties()) loadout.Sets[set.Name] = ((JArray)set.Value).Select(t => t.Type == JTokenType.Null ? null : (string)t).ToList();
            foreach (var active in ((JObject)row["active"] ?? new JObject()).Properties()) loadout.Active[active.Name] = (int)active.Value;
            loadout.Storage = Core2Json.Strings(row["storage"]).ToList();
            loadout.BoughtArmour = ((JArray)row["boughtArmour"] ?? new JArray()).OfType<JObject>().Select(r => new KeyValuePair<string, string>((string)r["classId"], (string)r["id"])).ToList();
            loadout.CreationArmourGrant = row["creationArmourGrant"] as JObject;
            return loadout;
        }
    }

    /// <summary>JS run.flaskCharges: the healing/mana charge ledger (capacity = base + grown + granted).</summary>
    public sealed class Core2FlaskCharges
    {
        public int Capacity { get; set; }
        public int Base { get; set; }
        public int Hp { get; set; }
        public int Mana { get; set; }
        public int HpCurrent { get; set; }
        public int ManaCurrent { get; set; }
        public int GrownHp { get; set; }
        public int GrownMana { get; set; }
        public int Granted { get; set; }
        public Core2FlaskCharges Clone() => (Core2FlaskCharges)MemberwiseClone();
        /// <summary>JS refillFlaskCharges.</summary>
        public void Refill() { HpCurrent = Hp; ManaCurrent = Mana; }
        public JObject ToJson() => new JObject
        {
            ["capacity"] = Capacity, ["base"] = Base, ["hp"] = Hp, ["mana"] = Mana, ["hpCurrent"] = HpCurrent, ["manaCurrent"] = ManaCurrent,
            ["grown"] = new JObject { ["hp"] = GrownHp, ["mana"] = GrownMana }, ["granted"] = Granted,
        };
        public static Core2FlaskCharges FromJson(JToken token)
        {
            var row = (JObject)token;
            return new Core2FlaskCharges
            {
                Capacity = (int)row["capacity"], Base = (int)row["base"], Hp = (int)row["hp"], Mana = (int)row["mana"], HpCurrent = (int)row["hpCurrent"],
                ManaCurrent = (int)row["manaCurrent"], GrownHp = (int?)row["grown"]?["hp"] ?? 0, GrownMana = (int?)row["grown"]?["mana"] ?? 0, Granted = (int?)row["granted"] ?? 0,
            };
        }
    }

    /// <summary>JS run.level: the character ledger (XP banked toward the next level, the level, points waiting for a rest).</summary>
    public sealed class Core2LevelLedger
    {
        public int Xp { get; set; }
        public int Level { get; set; } = 1;
        public int UnspentPoints { get; set; }
        public Core2LevelLedger Clone() => (Core2LevelLedger)MemberwiseClone();
        public JObject ToJson() => new JObject { ["xp"] = Xp, ["level"] = Level, ["unspentPoints"] = UnspentPoints };
        public static Core2LevelLedger FromJson(JToken token) => new Core2LevelLedger { Xp = (int)token["xp"], Level = (int)token["level"], UnspentPoints = (int)token["unspentPoints"] };
    }

    /// <summary>JS run.skills[trackId]: one skill track's ledger (XP toward the next level, the level, drafts queued).</summary>
    public sealed class Core2SkillRow
    {
        public int Xp { get; set; }
        public int Level { get; set; }
        public int PendingDrafts { get; set; }
        public Core2SkillRow Clone() => (Core2SkillRow)MemberwiseClone();
        public JObject ToJson() => new JObject { ["xp"] = Xp, ["level"] = Level, ["pendingDrafts"] = PendingDrafts };
        public static Core2SkillRow FromJson(JToken token) => new Core2SkillRow { Xp = (int?)token["xp"] ?? 0, Level = (int?)token["level"] ?? 0, PendingDrafts = (int?)token["pendingDrafts"] ?? 0 };
    }

    /// <summary>One reward or shop option (JS { kind, id }).</summary>
    public sealed class Core2RewardOption
    {
        public string Kind { get; set; }
        public string Id { get; set; }
        public JObject ToJson() => new JObject { ["kind"] = Kind, ["id"] = Id };
    }

    /// <summary>A persisted mixed offer (JS gameProfileRewardOffers row): the options, how many to take, what was taken.</summary>
    public sealed class Core2RewardOffer
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public List<Core2RewardOption> Options { get; set; } = new List<Core2RewardOption>();
        public int TakeCount { get; set; }
        public List<int> Taken { get; set; } = new List<int>();
        public int? Claimed { get; set; }
        /// <summary>A shop's flat price (JS stock.price); null for a reward.</summary>
        public int? Price { get; set; }
        public JObject ToJson()
        {
            var row = new JObject
            {
                ["id"] = Id, ["source"] = Source, ["options"] = new JArray(Options.Select(o => o.ToJson())), ["takeCount"] = TakeCount,
                ["taken"] = new JArray(Taken), ["claimed"] = Claimed.HasValue ? (JToken)Claimed.Value : JValue.CreateNull(),
            };
            if (Price.HasValue) row["price"] = Price.Value;
            return row;
        }
        public static Core2RewardOffer FromJson(JToken token)
        {
            var row = (JObject)token;
            return new Core2RewardOffer
            {
                Id = (string)row["id"], Source = (string)row["source"], TakeCount = (int)row["takeCount"],
                Options = ((JArray)row["options"]).OfType<JObject>().Select(o => new Core2RewardOption { Kind = (string)o["kind"], Id = (string)o["id"] }).ToList(),
                Taken = ((JArray)row["taken"]).Select(t => (int)t).ToList(),
                Claimed = row["claimed"] == null || row["claimed"].Type == JTokenType.Null ? (int?)null : (int)row["claimed"],
                Price = row["price"] == null || row["price"].Type == JTokenType.Null ? (int?)null : (int)row["price"],
            };
        }
    }

    /// <summary>JS run.gameProfilePendingReward: the spoils screen in progress and where it leads.</summary>
    public sealed class Core2PendingReward
    {
        public string OfferId { get; set; }
        public string Source { get; set; }
        /// <summary>map, advanceAct, victory or defeat.</summary>
        public string After { get; set; }
        public int LevelOffers { get; set; }
        public bool NoticeOnly { get; set; }
        /// <summary>JS pending.progression: the skill level notices this fight earned (shown once, then acknowledged).</summary>
        public JArray Progression { get; set; }
        /// <summary>JS pending.victoryReceipt: { title, xpBefore, xpGains } (shown once, then acknowledged).</summary>
        public JObject VictoryReceipt { get; set; }
        public JObject ToJson()
        {
            var row = new JObject { ["offerId"] = OfferId, ["source"] = Source, ["after"] = After, ["levelOffers"] = LevelOffers };
            if (Progression != null && Progression.Count > 0) row["progression"] = Progression.DeepClone();
            if (NoticeOnly) row["noticeOnly"] = true;
            if (VictoryReceipt != null) row["victoryReceipt"] = VictoryReceipt.DeepClone();
            return row;
        }
        public static Core2PendingReward FromJson(JToken token) => token == null || token.Type == JTokenType.Null ? null : new Core2PendingReward
        {
            OfferId = (string)token["offerId"], Source = (string)token["source"], After = (string)token["after"], LevelOffers = (int?)token["levelOffers"] ?? 0,
            NoticeOnly = (bool?)token["noticeOnly"] ?? false,
            Progression = token["progression"] is JArray p && p.Count > 0 ? (JArray)p.DeepClone() : null,
            VictoryReceipt = token["victoryReceipt"] is JObject v ? (JObject)v.DeepClone() : null,
        };
    }

    /// <summary>JS run.gameProfileRestVisits[visitId]: one stay at a rest site (healed on arrival, each service once).</summary>
    public sealed class Core2RestVisit
    {
        public string Id { get; set; }
        public string Service { get; set; }
        public Dictionary<string, int> Healed { get; set; }
        public Dictionary<string, bool> Used { get; set; }
        public JObject ToJson()
        {
            var row = new JObject { ["id"] = Id, ["service"] = Service };
            if (Healed != null) { var healed = new JObject(); foreach (var p in Healed) healed[p.Key] = p.Value; row["healed"] = healed; }
            if (Used != null) { var used = new JObject(); foreach (var p in Used) used[p.Key] = p.Value; row["used"] = used; }
            return row;
        }
        public static Core2RestVisit FromJson(JToken token) => new Core2RestVisit
        {
            Id = (string)token["id"], Service = (string)token["service"],
            Healed = token["healed"] is JObject h ? h.Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.Ordinal) : null,
            Used = token["used"] is JObject u ? u.Properties().ToDictionary(p => p.Name, p => (bool)p.Value, StringComparer.Ordinal) : null,
        };
    }

    /// <summary>JS run.combatEntered: the fight a node opened, with the run's RNG counters at entry (the restart checkpoint).</summary>
    public sealed class Core2CombatEntry
    {
        public string NodeId { get; set; }
        public string EncounterId { get; set; }
        public Dictionary<string, int> Counters { get; set; }
        /// <summary>The flask inventory at entry: a fight spends from its own copy in JS (combat.player), so a restart gives it back.</summary>
        public Core2FlaskCharges FlaskCharges { get; set; }
        public List<string> Flasks { get; set; }
        public JObject ToJson()
        {
            var row = new JObject { ["nodeId"] = NodeId, ["encounterId"] = EncounterId };
            if (Counters != null) row["counters"] = Core2RunJson.Counters(Counters);
            if (FlaskCharges != null) row["flaskCharges"] = FlaskCharges.ToJson();
            if (Flasks != null) row["flasks"] = new JArray(Flasks);
            return row;
        }
        public static Core2CombatEntry FromJson(JToken token) => token == null || token.Type == JTokenType.Null ? null : new Core2CombatEntry
        {
            NodeId = (string)token["nodeId"], EncounterId = (string)token["encounterId"],
            Counters = token["counters"] is JObject c ? c.Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.Ordinal) : null,
            FlaskCharges = token["flaskCharges"] is JObject fc ? Core2FlaskCharges.FromJson(fc) : null,
            Flasks = token["flasks"] is JArray f ? Core2Json.Strings(f).ToList() : null,
        };
    }

    /// <summary>The terminal fact of a finished run (JS showFinishedRun / gameProfileCompletedRunMeta input).</summary>
    public sealed class Core2RunFinish
    {
        public bool Victory { get; set; }
        public List<string> Unlocked { get; set; } = new List<string>();
        public JObject ToJson() => new JObject { ["victory"] = Victory, ["unlocked"] = new JArray(Unlocked) };
        public static Core2RunFinish FromJson(JToken token) => token == null || token.Type == JTokenType.Null ? null
            : new Core2RunFinish { Victory = (bool)token["victory"], Unlocked = Core2Json.Strings(token["unlocked"]).ToList() };
    }

    public sealed class Core2Run
    {
        public static readonly IReadOnlyList<string> AttributeIds = new[] { "strength", "dexterity", "constitution", "wisdom", "intelligence" };

        public string RunId { get; set; }
        public uint Seed { get; set; }
        public string Class { get; set; }
        public string AttributeMode { get; set; }
        public Dictionary<string, int> Attributes { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>JS run.derivedStatRuleSnapshot.rules: the rule table the run was born with.</summary>
        public JObject DerivedStatRules { get; set; }
        public Core2LevelLedger Level { get; set; } = new Core2LevelLedger();
        /// <summary>JS run.skills: the class and weapon track ledgers (phase 10), in the order tracks were first paid.</summary>
        public Dictionary<string, Core2SkillRow> Skills { get; set; } = new Dictionary<string, Core2SkillRow>(StringComparer.Ordinal);
        public int LevelUps { get; set; }
        public int LevelPoints { get; set; }

        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Mana { get; set; }
        public int MaxMana { get; set; }
        public int Stamina { get; set; }
        public int MaxStamina { get; set; }
        public int EnergyMax { get; set; }
        public int DrawPerTurn { get; set; }
        public int MaxHpAdjustment { get; set; }
        public Dictionary<string, int> EquipmentPoolBonuses { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal) { ["maxHp"] = 0, ["maxMana"] = 0, ["maxStamina"] = 0 };
        public Dictionary<string, int> EquipmentPoolDeficits { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal) { ["hp"] = 0, ["mana"] = 0, ["stamina"] = 0 };
        public int Cinders { get; set; }

        public List<Core2RunCard> Deck { get; set; } = new List<Core2RunCard>();
        public List<Core2RunCard> Sideboard { get; set; } = new List<Core2RunCard>();
        public List<string> CollectedCardIds { get; set; } = new List<string>();
        /// <summary>JS run.editMintCounter: the last `edit:N` instance id the deck editor minted (null until it mints).</summary>
        public int? EditMintCounter { get; set; }
        /// <summary>JS run.armorySeen: carried item ids and armour refs the player has looked at in the Armoury (null: never opened).</summary>
        public List<string> ArmorySeen { get; set; }
        public List<string> Relics { get; set; } = new List<string>();
        /// <summary>Utility flasks carried (JS run.flasks [{ flaskId }]).</summary>
        public List<string> Flasks { get; set; } = new List<string>();
        public Core2FlaskCharges FlaskCharges { get; set; } = new Core2FlaskCharges();
        public Core2RunLoadout Loadout { get; set; } = new Core2RunLoadout();
        public Dictionary<string, int> ItemUpgradeLevels { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        public int ActNumber { get; set; } = 1;
        public int Floor { get; set; }
        public string MapNodeId { get; set; }
        public Core2MapGraph MapGraph { get; set; }
        public List<string> Path { get; set; } = new List<string>();
        public List<string> SeatOrder { get; set; } = new List<string>();
        /// <summary>JS run.history: eventChoice and questCompleted rows (the quest gates read it).</summary>
        public List<JObject> History { get; set; } = new List<JObject>();
        public List<string> SeenEvents { get; set; } = new List<string>();
        public List<string> LastEncounters { get; set; } = new List<string>();
        public List<string> BossesBeaten { get; set; } = new List<string>();
        public int FightsWon { get; set; }

        public Dictionary<string, Core2RewardOffer> RewardOffers { get; set; } = new Dictionary<string, Core2RewardOffer>(StringComparer.Ordinal);
        public Core2PendingReward PendingReward { get; set; }
        public Dictionary<string, Core2RestVisit> RestVisits { get; set; } = new Dictionary<string, Core2RestVisit>(StringComparer.Ordinal);
        public string RestVisitId { get; set; }
        public string OpenShop { get; set; }
        /// <summary>The event the run stands at, between the choice and Continue (null otherwise).</summary>
        public string OpenEvent { get; set; }
        public bool OpenEventChosen { get; set; }
        public Core2CombatEntry CombatEntered { get; set; }
        public Core2RunFinish Finish { get; set; }
        /// <summary>The run's RNG counters at its last checkpoint (JS run.streamCounters).</summary>
        public Dictionary<string, int> StreamCounters { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        public int AttributeValue(string id) => Attributes.TryGetValue(id, out var v) ? v : 0;
        public IEnumerable<Core2RunCard> Owned => Deck.Concat(Sideboard);
    }

    internal static class Core2RunJson
    {
        public static JObject Counters(IReadOnlyDictionary<string, int> counters)
        {
            var row = new JObject();
            foreach (var pair in counters.OrderBy(p => p.Key, StringComparer.Ordinal)) row[pair.Key] = pair.Value;
            return row;
        }
        public static JObject IntMap(IReadOnlyDictionary<string, int> map, IEnumerable<string> order = null)
        {
            var row = new JObject();
            foreach (var key in order ?? map.Keys) if (map.TryGetValue(key, out var v)) row[key] = v;
            return row;
        }
        /// <summary>A number as JSON writes it: integral doubles as integers (JS prints 1.0 as 1).</summary>
        public static JToken Number(double value) => Math.Abs(value - Math.Round(value)) < 1e-12 && Math.Abs(value) < 9e15
            ? (JToken)(long)Math.Round(value) : new JValue(value);
        public static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
        public static bool IsNull(JToken token) => token == null || token.Type == JTokenType.Null || (token is JValue v && v.Value == null);
    }
}
