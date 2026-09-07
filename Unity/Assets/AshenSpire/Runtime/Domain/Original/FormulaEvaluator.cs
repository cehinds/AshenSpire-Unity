// FormulaEvaluator.cs — structured original formula language; no eval or string code.
// Preview and command resolution call the same evaluator. Intermediate nodes retain
// fractions; floor only at the outer result (except explicit per-stack grouping).
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class FormulaEvaluator
    {
        public static int Evaluate(JToken formula, JObject context)
        {
            var value = Node(formula, context ?? new JObject(), 0);
            if (double.IsNaN(value) || double.IsInfinity(value) || value < int.MinValue || value > int.MaxValue)
                throw new ArgumentException("Formula result is not a finite int32 value.");
            return (int)Math.Floor(value);
        }
        private static double Node(JToken node, JObject context, int depth)
        {
            if (depth > 64) throw new ArgumentException("Formula nesting exceeds 64.");
            if (node == null) throw new ArgumentException("Missing formula.");
            if (node.Type == JTokenType.Integer || node.Type == JTokenType.Float) return (double)node;
            if (!(node is JObject obj)) throw new ArgumentException("Expected a number or formula object.");
            var op = (string)obj["f"];
            double value;
            switch (op)
            {
                case "add": value = Args(obj).Sum(x => Node(x, context, depth + 1)); break;
                case "mul": value = Args(obj).Aggregate(1d, (product, x) => product * Node(x, context, depth + 1)); break;
                case "percentMaxHp": value = Number(One(obj, context), "maxHp") * Number(obj, "pct") / 100; break;
                case "missingHp": { var entity = One(obj, context); value = Math.Max(0, Number(entity, "maxHp") - Number(entity, "hp")); break; }
                case "blockOf": value = Number(One(obj, context), "block"); break;
                case "hpOf": value = Number(One(obj, context), "hp"); break;
                case "stacks":
                    var status = (string)obj["status"] ?? throw new ArgumentException("stacks requires status ID.");
                    var entities = Resolve(obj, context);
                    var list = entities is JArray array ? array : new JArray(entities.DeepClone());
                    value = list.Sum(entity => (double?)(entity["statuses"]?[status]?["meter"]?["value"] ?? entity["statuses"]?[status]?["stacks"]) ?? 0);
                    if (Present(obj["per"])) { var per = Number(obj, "per"); if (per <= 0) throw new ArgumentException("Stack grouping must be positive."); value = Math.Floor(value / per); }
                    break;
                case "energySpent": value = ((double?)context["energySpent"] ?? 0) * (Present(obj["per"]) ? Number(obj, "per") : 1); break;
                case "cardsPlayedThisTurn": value = ((double?)context["cardsPlayedThisTurn"] ?? 0) * (Present(obj["per"]) ? Number(obj, "per") : 1); break;
                default: throw new ArgumentException("Unknown formula operation: " + op);
            }
            if (Present(obj["min"])) value = Math.Max(value, Number(obj, "min"));
            if (Present(obj["max"])) value = Math.Min(value, Number(obj, "max"));
            return value;
        }
        private static JArray Args(JObject node) => node["args"] as JArray ?? throw new ArgumentException("Formula requires args array.");
        private static bool Present(JToken value) => value != null && value.Type != JTokenType.Null;
        internal static double Number(JToken node, string field)
        {
            var value = node?[field];
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) throw new ArgumentException("Expected numeric " + field);
            var result = (double)value;
            if (double.IsNaN(result) || double.IsInfinity(result)) throw new ArgumentException("Non-finite " + field);
            return result;
        }
        private static JToken Resolve(JObject node, JObject context)
        {
            var name = (string)node["of"] ?? throw new ArgumentException("Formula requires an entity ref.");
            var entity = context["entities"]?[name];
            return Present(entity) ? entity : throw new ArgumentException("Unresolved entity: " + name);
        }
        private static JObject One(JObject node, JObject context) => Resolve(node, context) as JObject ?? throw new ArgumentException("Formula requires a single entity, not a group.");
    }
}
