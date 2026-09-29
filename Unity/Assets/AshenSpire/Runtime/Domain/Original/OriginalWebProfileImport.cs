// Original profile import is a one-time additive transaction. Work on copies;
// the caller previews, checks the destination has not changed, then journals it.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public static class OriginalWebProfileImport
    {
        private static int Count(JToken value, int fallback = 0)
        {
            if (value == null) return fallback;
            if (value.Type != JTokenType.Integer || (long)value < 0 || (long)value > int.MaxValue)
                throw new ArgumentException("Original profile counters must be nonnegative whole numbers.");
            return (int)value;
        }
        private static JArray Ids(JToken value)
        {
            if (value == null) return new JArray();
            if (!(value is JArray rows) || rows.Any(x => x.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)x))
                || rows.Values<string>().Distinct().Count() != rows.Count) throw new ArgumentException("Invalid original profile IDs.");
            return (JArray)rows.DeepClone();
        }
        private static JArray Union(JArray first, JArray second) => new JArray(first.Values<string>().Concat(second.Values<string>()).Distinct());
        public static void ValidateReceipt(JObject profile)
        {
            if (profile["originalProfileImport"] == null) return;
            if (!(profile["originalProfileImport"] is JObject receipt) || (int?)receipt["version"] != 1
                || !(receipt["original"] is JObject original) || (string)receipt["sha256"] != OriginalWebSaveImport.Hash(original))
                throw new ArgumentException("Original profile import receipt is damaged or unsupported.");
        }
        public static OriginalProfile Convert(string text, OriginalContentCatalog catalog, OriginalProfile current)
        {
            var source = OriginalWebSaveImport.ParseOriginal(text);
            if (source["profile"] != null)
            {
                if (source["profile"].Type != JTokenType.String) throw new ArgumentException("The exported profile must contain its original JSON text.");
                source = OriginalWebSaveImport.ParseOriginal((string)source["profile"]);
            }
            else if (source["archive"] is JObject archive)
            {
                if ((string)archive["kind"] != "meta" || archive["save"]?.Type != JTokenType.String)
                    throw new ArgumentException("Choose an original profile archive, not a run archive.");
                source = OriginalWebSaveImport.ParseOriginal((string)archive["save"]);
            }
            if (source["schemaVersion"] != null && (source["schemaVersion"].Type != JTokenType.Integer || (int)source["schemaVersion"] < 0 || (int)source["schemaVersion"] > 2))
                throw new ArgumentException("This original profile version is not supported. Your original file is unchanged.");
            var fields = new[] { "schemaVersion", "settings", "results", "progress", "found", "unlocked", "discoveredArmaments", "discoveryReceipts", "seen" };
            if (source.Properties().Any(p => !fields.Contains(p.Name))) throw new ArgumentException("This profile contains newer or unsupported progress. No records were changed.");
            if (!(source["settings"] is JObject) || !(source["results"] is JArray history) || history.Count > OriginalProfile.ResultArchiveLimit)
                throw new ArgumentException("Choose a readable original profile with settings and a run history.");
            var draft = current.Snapshot();
            if (draft["originalProfileImport"] != null) throw new InvalidOperationException("An original profile has already been imported. Another import would count that progress twice.");
            var hash = OriginalWebSaveImport.Hash(source);
            var importedIds = new JArray(); var results = new JArray();
            var reconstructed = new JObject { ["runs"] = 0, ["wins"] = 0, ["maxAct"] = 1, ["bosses"] = new JArray(), ["wonClasses"] = new JArray() };
            foreach (var value in history)
            {
                if (!(value is JObject row) || row["victory"]?.Type != JTokenType.Boolean) throw new ArgumentException("Invalid original run history.");
                var result = (JObject)row.DeepClone();
                var classId = (string)result["class"];
                if (classId == null) classId = (string)catalog.Table("classes").SingleOrDefault(c => (string)c["name"] == (string)result["className"])?["id"];
                var hero = catalog.Record("classes", classId);
                result["class"] = classId; result["className"] = hero["name"].DeepClone();
                foreach (var key in new[] { "act", "floor", "fightsWon", "damageDealt", "damageTaken", "ascension" }) result[key] = Count(result[key], key == "act" ? 1 : 0);
                if (result["custom"] != null && result["custom"].Type != JTokenType.Boolean) throw new ArgumentException("Invalid original run mode.");
                result["custom"] = (bool?)result["custom"] ?? false;
                foreach (var key in new[] { "name", "className" }) if (result[key] != null && result[key].Type != JTokenType.String) throw new ArgumentException("Invalid original character name.");
                if (result["seed"] != null && result["seed"].Type != JTokenType.String && result["seed"].Type != JTokenType.Integer) throw new ArgumentException("Invalid original history seed.");
                var bosses = Ids(result["bosses"]); foreach (var boss in bosses.Values<string>()) _ = catalog.Record("enemies", boss);
                result["bosses"] = bosses;
                reconstructed["runs"] = (int)reconstructed["runs"] + 1;
                reconstructed["maxAct"] = Math.Max((int)reconstructed["maxAct"], (int)result["act"]);
                reconstructed["bosses"] = Union((JArray)reconstructed["bosses"], bosses);
                if ((bool)result["victory"])
                {
                    reconstructed["wins"] = (int)reconstructed["wins"] + 1;
                    reconstructed["wonClasses"] = Union((JArray)reconstructed["wonClasses"], new JArray(classId));
                }
                importedIds.Add("web-profile-" + hash + "-" + results.Count); results.Add(result);
            }
            if (source["progress"] != null && !(source["progress"] is JObject)) throw new ArgumentException("Invalid original progress totals.");
            var progress = (JObject)(source["progress"] ?? reconstructed).DeepClone();
            if (progress.Properties().Any(p => !new[] { "runs", "wins", "maxAct", "bosses", "wonClasses" }.Contains(p.Name))) throw new ArgumentException("Unsupported original progression system.");
            foreach (var key in new[] { "runs", "wins", "maxAct" }) progress[key] = Count(progress[key], key == "maxAct" ? 1 : 0);
            if ((int)progress["wins"] > (int)progress["runs"] || (int)progress["runs"] < history.Count || (int)progress["wins"] < (int)reconstructed["wins"] || (int)progress["maxAct"] < (int)reconstructed["maxAct"])
                throw new ArgumentException("Original progress totals disagree with its history.");
            progress["bosses"] = Ids(progress["bosses"]); progress["wonClasses"] = Ids(progress["wonClasses"]);
            foreach (var key in new[] { "bosses", "wonClasses" })
                if (((JArray)reconstructed[key]).Values<string>().Except(progress[key].Values<string>()).Any()) throw new ArgumentException("Original progression is missing recorded victories or bosses.");
            foreach (var id in progress["bosses"].Values<string>()) _ = catalog.Record("enemies", id);
            foreach (var id in progress["wonClasses"].Values<string>()) _ = catalog.Record("classes", id);
            foreach (var key in new[] { "found", "unlocked", "discoveredArmaments" })
            {
                var values = Ids(source[key] ?? (key == "discoveredArmaments" && ((int?)source["schemaVersion"] ?? 0) < 2 ? source["found"] : null));
                foreach (var id in values.Values<string>()) _ = catalog.Record(key == "unlocked" ? "unlocks" : "equipment.armaments", id);
                draft[key] = Union((JArray)draft[key], values);
            }
            if (source["discoveryReceipts"] != null && (!(source["discoveryReceipts"] is JArray receipts) || receipts.Any(r => !(r is JObject))))
                throw new ArgumentException("Invalid original discovery history.");
            if (source["seen"] != null && !(source["seen"] is JObject)) throw new ArgumentException("Invalid original collection record.");
            // Preserve all original receipts/settings/seen markers in the import record.
            // Unity preferences remain in force; browser-only settings are not guessed.
            foreach (var receipt in source["discoveryReceipts"] as JArray ?? new JArray()) ((JArray)draft["discoveryReceipts"]).Add(receipt.DeepClone());
            var nativeIds = (JArray)draft["completedRunIds"];
            var missing = ((JArray)draft["results"]).Count - nativeIds.Count;
            for (var i = 0; i < missing; i++) importedIds.Add("native-preserved-" + OriginalWebSaveImport.Hash(draft) + "-" + i);
            foreach (var id in nativeIds) importedIds.Add(id.DeepClone());
            foreach (var row in (JArray)draft["results"]) results.Add(row.DeepClone());
            draft["completedRunIds"] = importedIds;
            draft["results"] = new JArray(results.Skip(Math.Max(0, results.Count - OriginalProfile.ResultArchiveLimit)));
            foreach (var key in new[] { "runs", "wins" }) draft["progress"][key] = checked(Count(draft["progress"][key]) + (int)progress[key]);
            draft["progress"]["maxAct"] = Math.Max(Count(draft["progress"]["maxAct"]), (int)progress["maxAct"]);
            foreach (var key in new[] { "bosses", "wonClasses" }) draft["progress"][key] = Union((JArray)draft["progress"][key], (JArray)progress[key]);
            draft["originalProfileImport"] = new JObject { ["version"] = 1, ["sha256"] = hash, ["original"] = source.DeepClone(), ["runsAdded"] = progress["runs"].DeepClone() };
            return OriginalProfile.Restore(catalog, draft);
        }
    }
}
