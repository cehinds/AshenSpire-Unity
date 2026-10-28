// Core2RunSave.cs — the Core 2 run save (new format only; SPEC §0.1, §0.6).
//
// A save is { format, formatVersion, profileHash, core2ActionRulesVersion, run } where `run` carries the JS run's own
// field names (src/model/state.js) for everything a Core 2 run uses. Loading refuses, by name and without touching
// the stored text, a save of another format, a newer format version, or a profile hash other than the build's (JS §16:
// a run snapshots its rules and does not migrate silently). Old 0.5.5 saves use other storage keys and are never read.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public static class Core2RunSave
    {
        public const string Format = "ashen.core2.run";
        public const int FormatVersion = 1;

        public static JObject RunToJson(Core2Run run)
        {
            var attributes = new JObject();
            foreach (var id in Core2Run.AttributeIds) if (run.Attributes.TryGetValue(id, out var v)) attributes[id] = v;
            var offers = new JObject();
            foreach (var pair in run.RewardOffers) offers[pair.Key] = pair.Value.ToJson();
            var visits = new JObject();
            foreach (var pair in run.RestVisits) visits[pair.Key] = pair.Value.ToJson();
            var levels = new JObject();
            foreach (var pair in run.ItemUpgradeLevels) levels[pair.Key] = pair.Value;
            return new JObject
            {
                ["runId"] = run.RunId, ["seed"] = run.Seed, ["class"] = run.Class, ["attributeMode"] = run.AttributeMode, ["attributes"] = attributes,
                ["derivedStatRuleSnapshot"] = new JObject { ["rules"] = run.DerivedStatRules?.DeepClone() },
                ["level"] = run.Level.ToJson(), ["levelUps"] = run.LevelUps, ["levelPoints"] = run.LevelPoints,
                ["hp"] = run.Hp, ["maxHp"] = run.MaxHp, ["mana"] = run.Mana, ["maxMana"] = run.MaxMana, ["stamina"] = run.Stamina, ["maxStamina"] = run.MaxStamina,
                ["energyMax"] = run.EnergyMax, ["drawPerTurn"] = run.DrawPerTurn, ["maxHpAdjustment"] = run.MaxHpAdjustment,
                ["equipmentPoolBonuses"] = Core2RunJson.IntMap(run.EquipmentPoolBonuses, new[] { "maxHp", "maxMana", "maxStamina" }),
                ["equipmentPoolDeficits"] = Core2RunJson.IntMap(run.EquipmentPoolDeficits, new[] { "hp", "mana", "stamina" }),
                ["cinders"] = run.Cinders,
                ["deck"] = new JArray(run.Deck.Select(c => c.ToJson())), ["sideboard"] = new JArray(run.Sideboard.Select(c => c.ToJson())),
                ["collectedCardIds"] = new JArray(run.CollectedCardIds), ["relics"] = new JArray(run.Relics),
                ["flasks"] = new JArray(run.Flasks.Select(id => new JObject { ["flaskId"] = id })), ["flaskCharges"] = run.FlaskCharges.ToJson(),
                ["loadout"] = run.Loadout.ToJson(), ["itemUpgradeLevels"] = levels,
                ["actNumber"] = run.ActNumber, ["floor"] = run.Floor, ["mapNodeId"] = run.MapNodeId, ["mapGraph"] = run.MapGraph?.ToJson(),
                ["path"] = new JArray(run.Path), ["seatOrder"] = new JArray(run.SeatOrder), ["history"] = new JArray(run.History.Select(h => h.DeepClone())),
                ["seenEvents"] = new JArray(run.SeenEvents), ["lastEncounters"] = new JArray(run.LastEncounters), ["bossesBeaten"] = new JArray(run.BossesBeaten),
                ["stats"] = new JObject { ["fightsWon"] = run.FightsWon },
                ["gameProfileRewardOffers"] = offers, ["gameProfilePendingReward"] = run.PendingReward?.ToJson(),
                ["gameProfileRestVisits"] = visits, ["gameProfileRestVisitId"] = run.RestVisitId, ["gameProfileOpenShop"] = run.OpenShop,
                ["openEvent"] = run.OpenEvent == null ? null : new JObject { ["eventId"] = run.OpenEvent, ["chosen"] = run.OpenEventChosen },
                ["combatEntered"] = run.CombatEntered?.ToJson(), ["finish"] = run.Finish?.ToJson(),
                ["streamCounters"] = Core2RunJson.Counters(run.StreamCounters),
            };
        }

        public static Core2Run RunFromJson(JObject row)
        {
            int Int(string key, int fallback = 0) => row[key] == null || row[key].Type == JTokenType.Null ? fallback : (int)row[key];
            string Str(string key) => row[key] == null || row[key].Type == JTokenType.Null ? null : (string)row[key];
            Dictionary<string, int> Map(JToken token) => token is JObject o ? o.Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.Ordinal) : new Dictionary<string, int>(StringComparer.Ordinal);
            var run = new Core2Run
            {
                RunId = Str("runId"), Seed = row["seed"] == null ? 0u : (uint)(long)row["seed"], Class = Str("class"), AttributeMode = Str("attributeMode"),
                Attributes = Map(row["attributes"]), DerivedStatRules = (row["derivedStatRuleSnapshot"] as JObject)?["rules"] as JObject,
                Level = row["level"] is JObject level ? Core2LevelLedger.FromJson(level) : new Core2LevelLedger(), LevelUps = Int("levelUps"), LevelPoints = Int("levelPoints"),
                Hp = Int("hp"), MaxHp = Int("maxHp"), Mana = Int("mana"), MaxMana = Int("maxMana"), Stamina = Int("stamina"), MaxStamina = Int("maxStamina"),
                EnergyMax = Int("energyMax"), DrawPerTurn = Int("drawPerTurn"), MaxHpAdjustment = Int("maxHpAdjustment"),
                EquipmentPoolBonuses = row["equipmentPoolBonuses"] is JObject ? Map(row["equipmentPoolBonuses"]) : new Dictionary<string, int>(StringComparer.Ordinal) { ["maxHp"] = 0, ["maxMana"] = 0, ["maxStamina"] = 0 },
                EquipmentPoolDeficits = row["equipmentPoolDeficits"] is JObject ? Map(row["equipmentPoolDeficits"]) : new Dictionary<string, int>(StringComparer.Ordinal) { ["hp"] = 0, ["mana"] = 0, ["stamina"] = 0 },
                Cinders = Int("cinders"),
                Deck = ((JArray)row["deck"] ?? new JArray()).Select(Core2RunCard.FromJson).ToList(),
                Sideboard = ((JArray)row["sideboard"] ?? new JArray()).Select(Core2RunCard.FromJson).ToList(),
                CollectedCardIds = Core2Json.Strings(row["collectedCardIds"]).ToList(), Relics = Core2Json.Strings(row["relics"]).ToList(),
                Flasks = ((JArray)row["flasks"] ?? new JArray()).Select(t => t is JObject f ? (string)f["flaskId"] : (string)t).ToList(),
                FlaskCharges = row["flaskCharges"] is JObject fc ? Core2FlaskCharges.FromJson(fc) : new Core2FlaskCharges(),
                Loadout = row["loadout"] is JObject lo ? Core2RunLoadout.FromJson(lo) : new Core2RunLoadout(),
                ItemUpgradeLevels = Map(row["itemUpgradeLevels"]),
                ActNumber = Int("actNumber", 1), Floor = Int("floor"), MapNodeId = Str("mapNodeId"), MapGraph = Core2MapGraph.FromJson(row["mapGraph"]),
                Path = Core2Json.Strings(row["path"]).ToList(), SeatOrder = Core2Json.Strings(row["seatOrder"]).ToList(),
                History = ((JArray)row["history"] ?? new JArray()).OfType<JObject>().Select(h => (JObject)h.DeepClone()).ToList(),
                SeenEvents = Core2Json.Strings(row["seenEvents"]).ToList(), LastEncounters = Core2Json.Strings(row["lastEncounters"]).ToList(),
                BossesBeaten = Core2Json.Strings(row["bossesBeaten"]).ToList(), FightsWon = (int?)(row["stats"] as JObject)?["fightsWon"] ?? (int?)row["fightsWon"] ?? 0,
                PendingReward = Core2PendingReward.FromJson(row["gameProfilePendingReward"]), RestVisitId = Str("gameProfileRestVisitId"),
                OpenShop = Str("gameProfileOpenShop"), OpenEvent = (string)(row["openEvent"] as JObject)?["eventId"], OpenEventChosen = (bool?)(row["openEvent"] as JObject)?["chosen"] ?? false,
                CombatEntered = Core2CombatEntry.FromJson(row["combatEntered"]), Finish = Core2RunFinish.FromJson(row["finish"]),
                StreamCounters = Map(row["streamCounters"]),
            };
            foreach (var pair in ((row["gameProfileRewardOffers"] as JObject) ?? new JObject()).Properties()) run.RewardOffers[pair.Name] = Core2RewardOffer.FromJson(pair.Value);
            foreach (var pair in ((row["gameProfileRestVisits"] as JObject) ?? new JObject()).Properties()) run.RestVisits[pair.Name] = Core2RestVisit.FromJson(pair.Value);
            return run;
        }

        /// <summary>A deep copy (JS structuredClone of a run): what a refused transaction is tried on.</summary>
        public static Core2Run Clone(Core2Run run) => RunFromJson(RunToJson(run));

        /// <summary>Commit a draft: every field of <paramref name="source"/> onto <paramref name="target"/> (JS Object.assign(run, draft)).</summary>
        public static void CopyInto(Core2Run source, Core2Run target)
        {
            foreach (var property in typeof(Core2Run).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.CanWrite && property.CanRead) property.SetValue(target, property.GetValue(source));
        }

        /// <summary>The save text for a run (the profile hash pins it to this build's rules).</summary>
        public static string Serialize(Core2Content content, Core2Run run) => new JObject
        {
            ["format"] = Format, ["formatVersion"] = FormatVersion, ["profileHash"] = content.Manifest.ProfileHash,
            ["core2ActionRulesVersion"] = content.Manifest.Core2ActionRulesVersion, ["run"] = RunToJson(run),
        }.ToString(Formatting.None);

        /// <summary>Load a save, or throw naming why it is refused (another format, a newer version, other rules).</summary>
        public static Core2Run Deserialize(Core2Content content, string text)
        {
            JObject row;
            try { row = Core2Json.ParseObject(text, "Core 2 save"); }
            catch (InvalidDataException error) { throw new Core2SaveRefused("unreadable", "The save is not readable: " + error.Message); }
            if ((string)row["format"] != Format) throw new Core2SaveRefused("format", "The save is not a Core 2 run (format '" + (string)row["format"] + "').");
            var version = (int?)row["formatVersion"] ?? 0;
            if (version > FormatVersion) throw new Core2SaveRefused("newer", "The save was written by a newer build (format version " + version + ").");
            if (version < 1) throw new Core2SaveRefused("format", "The save has no format version.");
            var hash = (string)row["profileHash"];
            if (hash != content.Manifest.ProfileHash) throw new Core2SaveRefused("rules", "The save was made under other Core 2 rules (" + hash + ", this build plays " + content.Manifest.ProfileHash + ").");
            if (!(row["run"] is JObject run)) throw new Core2SaveRefused("unreadable", "The save holds no run.");
            return RunFromJson(run);
        }
    }

    /// <summary>A Core 2 save that cannot be loaded; <see cref="Reason"/> is unreadable, format, newer or rules.</summary>
    public sealed class Core2SaveRefused : Exception
    {
        public string Reason { get; }
        public Core2SaveRefused(string reason, string message) : base(message) { Reason = reason; }
    }
}
