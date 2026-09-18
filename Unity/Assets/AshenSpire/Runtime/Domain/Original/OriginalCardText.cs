// OriginalCardText.cs — authored card numbers for native combat, deck and reward views.
// Binding order/grammar mirrors pinned original validate.js and card.js staticTokens.
// Author textTemplate tokens such as {block}, {block.2}, and {hits} alongside effects.
// Unbound tokens stay visibly braced; never hide content errors by humanizing a key.
// Single-hit literal attribute bonuses can be included in the shown base amount.
// Multi-hit/repeated/formula bonuses stay separate totals, never multiplied per hit.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalCardText
    {
        private static readonly HashSet<string> TokenizableOperations = new HashSet<string>(StringComparer.Ordinal)
        { "damage", "block", "heal", "loseHp", "applyStatus", "poiseDamage", "draw", "gainEnergy", "restoreMana", "addCinders", "loseMaxHpPct" };
        private static readonly Regex TemplateToken = new Regex(@"\{([A-Za-z][A-Za-z0-9_.]*)\}");

        /// <summary>Original binding receipt: token, effect index, field, opcode, numeric literal.</summary>
        public static JArray ComputeTokenBindings(JArray effects)
        {
            var result = new JArray();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(string name, int index, string field, JObject effect)
            {
                counts.TryGetValue(name, out var count);
                counts[name] = ++count;
                result.Add(new JObject
                {
                    ["token"] = count == 1 ? name : name + "." + count.ToString(CultureInfo.InvariantCulture),
                    ["index"] = index, ["field"] = field, ["op"] = effect["op"].DeepClone(),
                    ["literal"] = IsNumber(effect[field])
                });
            }
            for (var i = 0; i < (effects?.Count ?? 0); i++)
            {
                if (!(effects[i] is JObject effect) || effect["op"]?.Type != JTokenType.String) continue;
                var op = (string)effect["op"];
                if (!TokenizableOperations.Contains(op)) continue;
                var field = op == "applyStatus" ? "stacks" : op == "loseMaxHpPct" ? "pct" : "amount";
                if (op == "applyStatus" && effect["status"]?.Type != JTokenType.String) continue;
                Add(op == "applyStatus" ? (string)effect["status"] : op, i, field, effect);
                if (op == "damage" && effect["hits"] != null && effect["hits"].Type != JTokenType.Null) Add("hits", i, "hits", effect);
            }
            return result;
        }

        /// <summary>Only numeric authored values, exactly as the original static card renderer.</summary>
        public static JObject StaticTokens(JObject card)
        {
            var result = new JObject();
            var effects = card["effects"] as JArray;
            foreach (var binding in ComputeTokenBindings(effects))
            {
                var value = effects[(int)binding["index"]][(string)binding["field"]];
                if (IsNumber(value)) result[(string)binding["token"]] = value.DeepClone();
            }
            return result;
        }

        public static string Describe(JObject card, OriginalContentCatalog catalog, JObject formulaContext = null)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            var effects = card["effects"] as JArray ?? new JArray();
            var values = StaticTokens(card);
            var included = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in ComputeTokenBindings(effects))
            {
                var effect = (JObject)effects[(int)binding["index"]];
                var field = (string)binding["field"];
                var token = (string)binding["token"];
                if (effect[field] is JObject formula && formulaContext != null)
                    values[token] = FormulaEvaluator.Evaluate(formula, formulaContext);
                var op = (string)effect["op"];
                // The executor adds a total bonus to the first repetition. Only an
                // integer, single-hit/single-repeat amount is unambiguous to fold.
                var bonus = (int?)effect["attributeBonus"] ?? 0;
                if (field == "amount" && (op == "damage" || op == "heal") && bonus > 0 &&
                    WholeAmount(effect["amount"], out var amount) && One(effect["repeat"]) &&
                    (op != "damage" || One(effect["hits"])))
                {
                    values[token] = checked(amount + bonus);
                    included.Add(op);
                }
            }
            var template = (string)card["textTemplate"] ?? (string)card["text"] ?? "";
            var text = TemplateToken.Replace(template, match =>
            {
                var value = values[match.Groups[1].Value];
                return IsNumber(value) ? Convert.ToString(((JValue)value).Value, CultureInfo.InvariantCulture) : match.Value;
            });
            foreach (var bonus in (card["attributeProgression"] as JArray ?? new JArray()).Where(x => (int)x["bonus"] > 0))
            {
                var op = (string)bonus["operation"];
                text += included.Contains(op) ? " Includes +" : " +";
                text += bonus["bonus"] + (op == "heal" ? " healing" : " total damage") + " from " + Humanize((string)bonus["attribute"]) + ".";
            }
            return text.Trim();
        }

        private static bool One(JToken value) => value == null || (IsNumber(value) && (double)value == 1);
        // Equipment modifiers and smithing preserve numbers as doubles. A 6.0
        // profile has the same combat amount as authored integer 6; JSON storage
        // type must not change whether its displayed damage includes the bonus.
        private static bool WholeAmount(JToken value, out int amount)
        {
            amount = 0;
            if (!IsNumber(value)) return false;
            var number = (double)value;
            if (double.IsNaN(number) || double.IsInfinity(number) || number < int.MinValue ||
                number > int.MaxValue || number != Math.Truncate(number)) return false;
            amount = (int)number;
            return true;
        }
        private static bool IsNumber(JToken value) => value?.Type == JTokenType.Integer || value?.Type == JTokenType.Float;

        // Humanize is for labels only. It must never resolve an unknown template token.
        public static string Humanize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var words = Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
            return char.ToUpperInvariant(words[0]) + words.Substring(1);
        }
    }
}
