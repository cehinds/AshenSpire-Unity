using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalCardChoices
    {
        private readonly JArray _stances;
        public OriginalCardChoices(JArray stances) { _stances = (JArray)(stances ?? throw new ArgumentNullException(nameof(stances))).DeepClone(); }
        public JObject Plan(JObject definition, string classId, string activeStance = null)
        {
            var effect = (definition?["effects"] as JArray ?? new JArray()).FirstOrDefault(e => (string)e["op"] == "enterStance" && !string.IsNullOrEmpty((string)e["choose"]));
            if (effect == null) return null;
            if ((string)effect["choose"] != "classStance") throw new ArgumentException("Unknown stance selector.");
            var owned = _stances.Where(s => !string.IsNullOrEmpty((string)s["class"]) && (string)s["class"] == classId).ToArray();
            if (owned.Length == 0) owned = _stances.Where(s => !string.IsNullOrEmpty((string)s["class"]) && (string)s["class"] == (string)definition["class"]).ToArray();
            var options = new JArray();
            foreach (var row in owned)
            {
                var option = new JObject { ["id"] = row["id"]?.DeepClone(), ["active"] = !string.IsNullOrEmpty(activeStance) && (string)row["id"] == activeStance };
                foreach (var key in new[] { "name", "icon", "tooltip" }) if (row[key] != null) option[key] = row[key].DeepClone();
                options.Add(option);
            }
            return new JObject { ["kind"] = "stance", ["options"] = options };
        }
        public static string Assert(JObject plan, string choice)
        {
            if (plan == null) { if (choice != null) throw new ArgumentException("This card offers no choice."); return null; }
            if (!((JArray)plan["options"]).Any(o => (string)o["id"] == choice)) throw new ArgumentException("Choose a stance.");
            return choice;
        }
    }
}
