// OriginalProfile.cs — durable, engine-independent profile progression.
// Persist Snapshot in a separate profile slot after successful game transactions.
// Author unlock conditions/reveal modes and equipment discovery rules in content;
// no currency is minted here because original finishRun only awards unlocks.
// Finish requires a unique persisted run ID, independent of the replayable seed.
// Completed IDs outlive the 20-result history, preventing duplicate end-run awards.
// Collection must be reported only AFTER the run accepted the item into storage.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalProfile
    {
        private readonly OriginalContentCatalog _catalog;
        private JObject _state;
        public OriginalProfile(OriginalContentCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _state = new JObject { ["schemaVersion"] = 1, ["settings"] = new JObject(), ["results"] = new JArray(), ["completedRunIds"] = new JArray(), ["found"] = new JArray(), ["discoveredArmaments"] = new JArray(), ["discoveryReceipts"] = new JArray(), ["unlocked"] = new JArray(), ["progress"] = EmptyProgress() };
        }
        private static JObject EmptyProgress() => new JObject { ["runs"] = 0, ["wins"] = 0, ["maxAct"] = 1, ["bosses"] = new JArray(), ["wonClasses"] = new JArray() };
        private static void AddOnce(JArray rows, string value) { if (!string.IsNullOrEmpty(value) && !rows.Values<string>().Contains(value)) rows.Add(value); }
        private static int Count(JToken token, int fallback = 0)
        { if (token == null) return fallback; if (token.Type != JTokenType.Integer || (long)token < 0 || (long)token > int.MaxValue) throw new ArgumentException("Profile counters must be nonnegative integers."); return (int)token; }
        public JObject Snapshot() => (JObject)_state.DeepClone();
        public static OriginalProfile Restore(OriginalContentCatalog catalog, JObject snapshot)
        {
            if ((int?)snapshot?["schemaVersion"] != 1) throw new ArgumentException("Unsupported native profile schema version.");
            var profile = new OriginalProfile(catalog); var draft = (JObject)snapshot.DeepClone();
            foreach (var key in new[] { "results", "completedRunIds", "found", "discoveredArmaments", "discoveryReceipts", "unlocked" })
                if (!(draft[key] is JArray)) throw new ArgumentException("Missing profile array " + key);
            foreach (var key in new[] { "completedRunIds", "found", "discoveredArmaments", "unlocked" })
            { var rows = (JArray)draft[key]; if (rows.Any(x => x.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)x)) || rows.Values<string>().Distinct().Count() != rows.Count) throw new ArgumentException("Invalid unique profile IDs " + key); }
            if (!(draft["settings"] is JObject) || !(draft["progress"] is JObject progress)) throw new ArgumentException("Missing profile settings/progress.");
            foreach (var key in new[] { "runs", "wins", "maxAct" }) _ = Count(progress[key]);
            if (Count(progress["wins"]) > Count(progress["runs"])) throw new ArgumentException("Profile wins exceed runs.");
            foreach (var key in new[] { "bosses", "wonClasses" }) if (!(progress[key] is JArray rows) || rows.Any(x => x.Type != JTokenType.String)) throw new ArgumentException("Invalid profile progress " + key);
            if (((JArray)draft["results"]).Count > 20 || ((JArray)draft["results"]).Any(x => !(x is JObject))) throw new ArgumentException("Invalid profile history.");
            profile._state = draft; return profile;
        }
        public static bool IsCustomRun(JObject custom)
        {
            if (custom == null) return false;
            return Count(custom["ascension"]) > 0 || !string.IsNullOrEmpty((string)custom["deckMode"]) && (string)custom["deckMode"] != "standard"
                || custom["mapShape"] is JObject shape && shape.HasValues || custom["mods"] is JObject mods && mods.Properties().Any(p => (bool?)p.Value == true);
        }
        public JObject Finish(string runId, JObject run, bool victory)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("A durable unique run ID is required.");
            if (((JArray)_state["completedRunIds"]).Values<string>().Contains(runId)) return new JObject { ["duplicate"] = true, ["newUnlocks"] = new JArray() };
            var classId = (string)run["classId"] ?? (string)run["class"]; var hero = _catalog.Record("classes", classId);
            var custom = run["custom"] as JObject; var result = new JObject {
                ["victory"] = victory, ["seed"] = run["seedString"]?.DeepClone() ?? run["seed"]?.DeepClone(), ["class"] = classId, ["className"] = hero["name"].DeepClone(),
                ["act"] = Count(run["actNumber"] ?? run["act"], 1), ["floor"] = Count(run["floor"]), ["fightsWon"] = Count(run["stats"]?["fightsWon"] ?? run["fightsWon"]),
                ["damageDealt"] = Count(run["stats"]?["damageDealt"] ?? run["damageDealt"]), ["damageTaken"] = Count(run["stats"]?["damageTaken"] ?? run["damageTaken"]),
                ["custom"] = IsCustomRun(custom), ["ascension"] = Count(custom?["ascension"]), ["bosses"] = run["bossesBeaten"]?.DeepClone() ?? new JArray() };
            if (run["customization"]?["name"] != null) result["name"] = run["customization"]["name"].DeepClone();
            if (!(result["bosses"] is JArray bosses) || bosses.Any(x => x.Type != JTokenType.String)) throw new ArgumentException("Invalid defeated bosses.");
            var draft = Snapshot(); var p = (JObject)draft["progress"]; p["runs"] = checked(Count(p["runs"]) + 1); p["maxAct"] = Math.Max(Count(p["maxAct"]), Count(result["act"], 1));
            foreach (var id in bosses.Values<string>()) AddOnce((JArray)p["bosses"], id);
            if (victory) { p["wins"] = checked(Count(p["wins"]) + 1); AddOnce((JArray)p["wonClasses"], classId); }
            var fresh = EvaluateUnlocks(draft); foreach (var id in fresh.Values<string>()) AddOnce((JArray)draft["unlocked"], id);
            ((JArray)draft["completedRunIds"]).Add(runId); var results = (JArray)draft["results"]; results.Add(result); while (results.Count > 20) results[0].Remove();
            _state = draft; return new JObject { ["duplicate"] = false, ["result"] = result.DeepClone(), ["newUnlocks"] = fresh };
        }
        public JArray EvaluateUnlocks(JObject profile = null)
        {
            var state = profile ?? _state; var p = (JObject)state["progress"]; var earned = ((JArray)state["unlocked"]).Values<string>().ToHashSet(); var fresh = new JArray();
            foreach (var row in _catalog.Table("unlocks"))
            {
                if (earned.Contains((string)row["id"])) continue; var parameter = (string)row["param"]; bool pass;
                switch ((string)row["condition"])
                {
                    case "winAsClass": pass = ((JArray)p["wonClasses"]).Values<string>().Contains(parameter); break;
                    case "beatBoss": pass = ((JArray)p["bosses"]).Values<string>().Contains(parameter); break;
                    case "reachAct": pass = double.TryParse(parameter, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var act) && Count(p["maxAct"]) >= act; break;
                    case "winRuns": pass = double.TryParse(parameter, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wins) && Count(p["wins"]) >= wins; break;
                    default: pass = false; break; // Original saves survive newly introduced conditions.
                }
                if (pass) fresh.Add(row["id"].DeepClone());
            }
            return fresh;
        }
        public JObject CollectArmament(JObject run, string id, string source, string progressionMode = null)
        {
            _catalog.Record("equipment.armaments", id); var loadout = run["loadout"] as JObject ?? throw new ArgumentException("Missing collected loadout.");
            var carried = ((JObject)loadout["sets"]).Properties().SelectMany(p => ((JArray)p.Value).Values<string>()).Concat(((JArray)loadout["storage"]).Values<string>());
            if (!carried.Contains(id)) throw new ArgumentException("Profile collection requires successful run ownership first.");
            var cfg = _catalog.Data()["balance"]["equipment"]; if ((bool?)cfg["drops"]?["permanentOnFind"] != true || ((JArray)_state["found"]).Values<string>().Contains(id)) return new JObject { ["recorded"] = false, ["receipt"] = null };
            var mode = progressionMode ?? (IsCustomRun(run["custom"] as JObject) ? "custom" : "normal");
            if (!new[] { "normal", "custom", "debug", "showcase" }.Contains(mode)) throw new ArgumentException("Unknown discovery progression mode.");
            var draft = Snapshot(); AddOnce((JArray)draft["found"], id); JObject receipt = null;
            if (mode == "normal" && !((JArray)draft["discoveredArmaments"]).Values<string>().Contains(id))
            {
                var receipts = (JArray)draft["discoveryReceipts"]; receipt = new JObject { ["kind"] = "armamentDiscovery", ["pieceId"] = id, ["first"] = true, ["source"] = source ?? "unknown", ["runSeed"] = (run["seedString"] ?? run["seed"])?.ToString(), ["sequence"] = receipts.Count + 1 };
                AddOnce((JArray)draft["discoveredArmaments"], id); receipts.Add(receipt); var limit = Count(cfg["startingKitDiscovery"]?["receiptLimit"], 64); if (limit == 0) limit = 64; while (receipts.Count > limit) receipts[0].Remove();
            }
            _state = draft; return new JObject { ["recorded"] = true, ["receipt"] = receipt?.DeepClone() };
        }
        public int OpenedSets(string slotId, JObject loadout = null)
        {
            var slot = _catalog.Record("equipment.slots", slotId); var cap = Math.Max(1, Count(slot["sets"], 1)); var earned = ((JArray)_state["unlocked"]).Values<string>().ToHashSet();
            var opened = 1 + _catalog.Table("unlocks").Count(u => (string)u["kind"] == "slot" && (string)u["ref"] == slotId && earned.Contains((string)u["id"]));
            var ids = loadout?["sets"]?[slotId] as JArray ?? new JArray(); for (var i = 0; i < ids.Count; i++) if (!string.IsNullOrEmpty((string)ids[i])) opened = Math.Max(opened, i + 1);
            return Math.Min(cap, opened);
        }
        public int VisibleSets(string slotId, JObject loadout = null)
        { var opened = OpenedSets(slotId, loadout); var ceiling = Math.Min(Math.Max(1, Count(_catalog.Record("equipment.slots", slotId)["sets"], 1)), 1 + _catalog.Table("unlocks").Count(u => (string)u["kind"] == "slot" && (string)u["ref"] == slotId)); return opened < ceiling ? opened + 1 : opened; }
        public JArray UnlockView()
        {
            var earned = ((JArray)_state["unlocked"]).Values<string>().ToHashSet(); var result = new JArray();
            foreach (var entry in _catalog.Table("unlocks")) { var row = (JObject)entry.DeepClone(); var held = earned.Contains((string)row["id"]); var state = held ? "held" : (string)row["reveal"]; if (!new[] { "held", "hidden", "teased", "listed" }.Contains(state)) state = "teased"; if (state == "hidden") continue; row["state"] = state; row["earned"] = held; if (held) row["hint"] = ""; result.Add(row); }
            return result;
        }
        public JObject Telemetry()
        {
            var results = ((JArray)_state["results"]).OfType<JObject>().ToArray(); var standard = results.Where(r => (bool?)r["custom"] != true).ToArray(); var wins = standard.Count(r => (bool?)r["victory"] == true); var byClass = new JObject();
            foreach (var group in standard.GroupBy(r => (string)r["className"] ?? (string)r["class"] ?? "—")) byClass[group.Key] = new JObject { ["runs"] = group.Count(), ["wins"] = group.Count(r => (bool?)r["victory"] == true) };
            return new JObject { ["runs"] = standard.Length, ["wins"] = wins, ["percent"] = standard.Length == 0 ? 0 : (int)Math.Floor(100d * wins / standard.Length + .5), ["customCount"] = results.Length - standard.Length, ["byClass"] = byClass };
        }
        public void SetSettings(JObject settings) { var next = Snapshot(); foreach (var property in settings.Properties()) next["settings"][property.Name] = property.Value.DeepClone(); _state = next; }
    }
}
