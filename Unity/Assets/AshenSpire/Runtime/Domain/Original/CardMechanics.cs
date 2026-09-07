// CardMechanics.cs — canonical properties, costs and zone decisions for a RESOLVED card.
// Pass the definition after weapon profiles/upgrades/modifiers have been applied.
// To tune a card, edit its authored data; damage school never invents a resource cost.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class CardMechanics
    {
        private static readonly Dictionary<string, string> Types = new Dictionary<string, string> { ["attack"] = "classification.attack", ["skill"] = "classification.skill", ["power"] = "classification.power", ["curse"] = "classification.curse", ["status"] = "classification.statusCard" };
        private static readonly Dictionary<string, string> Keywords = new Dictionary<string, string> { ["exhaust"] = "lifecycle.exhaust", ["ethereal"] = "lifecycle.ethereal", ["innate"] = "lifecycle.innate", ["retain"] = "lifecycle.retain", ["unplayable"] = "internal.unplayable" };
        private static readonly HashSet<string> Schools = new HashSet<string>(new[] { "physical", "magic", "arcane" }, StringComparer.Ordinal);
        private static readonly HashSet<string> Targets = new HashSet<string>(new[] { "self", "enemy", "allEnemies", "randomEnemy", "ally" }, StringComparer.Ordinal);
        public static JObject FromDefinition(JObject definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var id = (string)definition["id"];
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Card requires an ID.");
            var properties = new JArray();
            Add(properties, Map(Types, (string)definition["type"], "card type"));
            if (definition["cost"] != null)
            {
                var variable = definition["cost"].Type == JTokenType.String && (string)definition["cost"] == "X";
                var parameters = new JObject { ["amount"] = variable ? 0 : Nonnegative(definition["cost"], "action cost") };
                if (variable) parameters["variable"] = true;
                Add(properties, "cost.action", parameters);
            }
            foreach (var name in new[] { "mana", "stamina" })
                if (definition[name + "Cost"] != null && definition[name + "Cost"].Type != JTokenType.Null)
                    Add(properties, "cost." + name, new JObject { ["amount"] = Nonnegative(definition[name + "Cost"], name + " cost") });
            foreach (var keyword in definition["keywords"] as JArray ?? new JArray()) Add(properties, Map(Keywords, (string)keyword, "keyword"));
            var school = (string)definition["damageSchool"];
            if (!string.IsNullOrEmpty(school)) { if (!Schools.Contains(school)) throw new ArgumentException("Unknown damage school: " + school); Add(properties, "damage." + school); }
            var effects = definition["effects"] as JArray ?? new JArray();
            foreach (var target in effects.Select(x => (string)x["target"]).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            { if (!Targets.Contains(target)) throw new ArgumentException("Unknown effect target: " + target); Add(properties, "targeting." + target); }
            if (effects.Any(x => (string)x["op"] == "dodgeRoll")) Add(properties, "utility.evasion");
            return new JObject { ["id"] = id, ["properties"] = properties, ["overrides"] = new JObject() };
        }
        public static bool HasProperty(JObject view, string propertyId) => ((JArray)view["properties"]).Any(x => (string)x["propertyId"] == propertyId);
        public static string AfterPlay(JObject view, bool legal = true, bool cancelled = false, bool sealConditionMet = false)
        {
            if (cancelled || !legal) return "HAND";
            if (HasProperty(view, "lifecycle.seal") && sealConditionMet) return "SEALED";
            if (HasProperty(view, "lifecycle.exhaust")) return "EXHAUST_PILE";
            if (HasProperty(view, "lifecycle.recall.afterUse")) return "HAND";
            return HasProperty(view, "classification.power") ? "REMOVED_FROM_PLAY" : "DISCARD_PILE";
        }
        public static string EndTurnFate(JObject view) => HasProperty(view, "lifecycle.retain") ? "keep" : HasProperty(view, "lifecycle.ethereal") ? "exhaust" : "discard";
        public static JObject CostProfile(JObject definition, int powerCostReduction = 0, JObject weightClass = null)
        {
            if (powerCostReduction < 0) throw new ArgumentOutOfRangeException(nameof(powerCostReduction));
            var view = FromDefinition(definition);
            var effects = definition["effects"] as JArray ?? new JArray();
            if (weightClass != null && effects.Count > 0 && effects.All(x => (string)x["op"] == "dodgeRoll"))
                return new JObject { ["action"] = Nonnegative(weightClass["dodgeActionCost"], "dodge action cost"), ["mana"] = 0, ["stamina"] = Nonnegative(weightClass["dodgeStaminaCost"], "dodge stamina cost"), ["variable"] = false, ["classPriced"] = true };
            var mods = new JArray();
            if (powerCostReduction > 0 && HasProperty(view, "classification.power")) mods.Add(new JObject { ["resource"] = "action", ["delta"] = -powerCostReduction });
            var entries = (JArray)CompileCosts(view, mods)["entries"];
            var action = ((JArray)view["properties"]).FirstOrDefault(x => (string)x["propertyId"] == "cost.action");
            return new JObject { ["action"] = Amount(entries, "action"), ["mana"] = Amount(entries, "mana"), ["stamina"] = Amount(entries, "stamina"), ["variable"] = (bool?)action?["parameters"]?["variable"] ?? false };
        }
        public static JObject CompileCosts(JObject view, JArray modifiers = null)
        {
            var entries = new JArray(); var alternatives = new JArray();
            foreach (var prop in (JArray)view["properties"])
            {
                var id = (string)prop["propertyId"];
                if (id == "cost.action" || id == "cost.mana" || id == "cost.stamina")
                    entries.Add(new JObject { ["resource"] = id.Substring(5), ["amount"] = prop["parameters"]?["amount"] == null ? 1 : Nonnegative(prop["parameters"]["amount"], "cost") });
                if (id == "cost.alternative" && alternatives.Count == 0)
                {
                    var options = prop["parameters"]?["options"] as JArray;
                    if (options == null || options.Count == 0) throw new ArgumentException("Alternative cost has no complete option.");
                    foreach (var option in options)
                    {
                        var costs = option["entries"] as JArray;
                        if (costs == null || costs.Count == 0) throw new ArgumentException("Alternative option has no entries.");
                        ResourceWallet.TotalCosts(costs);
                        alternatives.Add(new JObject { ["mode"] = "ALL_REQUIRED", ["entries"] = costs.DeepClone() });
                    }
                }
            }
            foreach (var modifier in modifiers ?? new JArray())
            {
                var entry = entries.FirstOrDefault(x => (string)x["resource"] == (string)modifier["resource"]);
                if (entry == null) continue;
                if (modifier["set"] != null && modifier["set"].Type != JTokenType.Null) entry["amount"] = Nonnegative(modifier["set"], "cost override");
                else entry["amount"] = Math.Max(0, checked((int)entry["amount"] + Integer(modifier["delta"] ?? new JValue(0), "cost modifier")));
            }
            var overrides = view["overrides"] as JObject;
            if (HasProperty(view, "lifecycle.recall.afterUse") && !entries.Any(x => (int)x["amount"] > 0) && !HasProperty(view, "lifecycle.exhaust") && !HasProperty(view, "lifecycle.seal") && !Truthy(overrides?["cooldown"]) && !Truthy(overrides?["oncePerTurn"]))
                throw new ArgumentException("Recall After Use requires a repeat limiter.");
            return new JObject { ["mode"] = alternatives.Count > 0 ? "CHOOSE_ONE" : "ALL_REQUIRED", ["entries"] = entries, ["alternatives"] = alternatives };
        }
        internal static int Integer(JToken value, string name)
        { if (value == null || value.Type != JTokenType.Integer && value.Type != JTokenType.Float || double.IsNaN((double)value) || double.IsInfinity((double)value) || (double)value != Math.Floor((double)value) || (double)value < int.MinValue || (double)value > int.MaxValue) throw new ArgumentException("Invalid integer: " + name); return (int)value; }
        internal static int Nonnegative(JToken value, string name) { var number = Integer(value, name); if (number < 0) throw new ArgumentException("Negative " + name); return number; }
        private static bool Truthy(JToken value) => value != null && value.Type != JTokenType.Null && (value.Type == JTokenType.Boolean ? (bool)value : value.Type == JTokenType.Integer ? (long)value != 0 : value.Type == JTokenType.String && ((string)value).Length > 0);
        private static int Amount(JArray entries, string resource) => (int?)entries.FirstOrDefault(x => (string)x["resource"] == resource)?["amount"] ?? 0;
        private static string Map(Dictionary<string, string> map, string key, string name) { if (key == null || !map.TryGetValue(key, out var value)) throw new ArgumentException("Unknown " + name + ": " + key); return value; }
        private static void Add(JArray properties, string id, JObject parameters = null) { var prop = new JObject { ["propertyId"] = id, ["source"] = "AUTHORED" }; if (parameters != null) prop["parameters"] = parameters; properties.Add(prop); }
    }
}
