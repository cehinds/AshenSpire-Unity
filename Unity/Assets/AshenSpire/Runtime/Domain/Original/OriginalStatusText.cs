// Read status values using the same meter-first precedence as StatusSystem.
// Formatting never mutates the combat instance or its authored definition.
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalStatusText
    {
        private static readonly Regex TemplateToken = new Regex(@"\{([A-Za-z][A-Za-z0-9_.]*)\}");

        public static string Describe(string id, JObject instance, string displayName = null)
        {
            var name = displayName ?? OriginalCardText.Humanize(id);
            var meter = instance?["meter"] as JObject;
            var value = (int?)(meter?["value"] ?? instance?["stacks"]) ?? 0;
            if (meter == null) return name + " " + value;
            var maximum = (int?)meter["max"] ?? 0;
            return name + " buildup " + value + (maximum > 0 ? "/" + maximum : "");
        }

        public static string PreviewName(JObject definition)
        {
            var name = (string)definition["name"] ?? OriginalCardText.Humanize((string)definition["id"]);
            return name + (definition["meter"] is JObject || definition["proc"] is JObject ? " buildup" : "");
        }

        public static string Description(JObject definition, JObject instance = null)
        {
            var text = (string)definition["tooltip"] ?? (string)definition["description"] ?? OriginalCardText.Humanize((string)definition["id"]);
            return TemplateToken.Replace(text, match =>
            {
                var path = match.Groups[1].Value;
                JToken value = definition;
                foreach (var part in path.Split('.')) value = (value as JObject)?[part];
                // A grown meter's live threshold is more useful than its initial
                // authored threshold. Unknown/non-scalar bindings stay visible.
                if ((path == "proc.threshold" || path == "meter.max") && (int?)instance?["meter"]?["max"] > 0)
                    value = instance["meter"]["max"];
                return value is JValue scalar && scalar.Value != null ? Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) : match.Value;
            }).Replace(" — ", "; ");
        }
    }
}
