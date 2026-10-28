// Core2CardText.cs — the static text of a card, relic or flask outside combat (reward, shop, deck editor, Armoury rows).
// JS: src/model/validate.js computeTokenBindings / relicTokens, src/model/playingCard.js staticCardTokens,
// src/ui/components/card.js relicText. A token the item's own data does not bind stays visible as {token}, exactly as
// JS leaves it ("a visible brace is a bug report"). Live combat numbers are the combat screen's, not these.
// Engine-free.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation.Core2
{
    public static class Core2CardText
    {
        /// <summary>JS TOKENIZABLE_OPS.</summary>
        public static readonly IReadOnlyList<string> TokenizableOps = new[]
        {
            "gainBarrier", "buildup", "discard", "removeStatus", "grantCardCharge", "damage", "block", "gainPoise", "gainWard", "heal",
            "loseHp", "applyStatus", "poiseDamage", "draw", "gainEnergy", "restoreMana", "restoreStamina", "addCinders", "loseMaxHpPct",
        };
        private static readonly Regex TokenRe = new Regex(@"\{([A-Za-z0-9_.\-]+)\}", RegexOptions.CultureInvariant);

        /// <summary>JS computeTokenBindings: (token, effect index, field) per tokenizable op, repeats as base.2, base.3…</summary>
        public static List<(string Token, int Index, string Field)> Bindings(IReadOnlyList<JToken> effects)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var output = new List<(string, int, string)>();
            void Push(string b, int i, string field)
            {
                counts[b] = (counts.TryGetValue(b, out var n) ? n : 0) + 1;
                output.Add((counts[b] == 1 ? b : b + "." + counts[b], i, field));
            }
            for (var i = 0; i < (effects?.Count ?? 0); i++)
            {
                if (!(effects[i] is JObject eff) || eff["op"]?.Type != JTokenType.String) continue;
                var op = (string)eff["op"];
                if (!TokenizableOps.Contains(op)) continue;
                if (op == "grantCardCharge")
                {
                    foreach (var field in new[] { "damage", "manaDiscount", "block", "heal", "break", "buildup" })
                        if (eff[field] != null) Push("charge" + char.ToUpperInvariant(field[0]) + field.Substring(1), i, field);
                    continue;
                }
                var f = op == "applyStatus" ? "stacks" : op == "loseMaxHpPct" ? "pct" : "amount";
                var b = op == "applyStatus" || op == "buildup" ? (string)eff["status"] : op;
                if (b == null) continue;
                Push(b, i, f);
                if (op == "damage" && eff["hits"] != null) Push("hits", i, "hits");
            }
            return output;
        }

        /// <summary>A constant: a number, or an add formula of constants (JS staticCardTokens constant()).</summary>
        private static double? Constant(JToken value)
        {
            if (value == null) return null;
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return (double)value;
            if (value is JObject f && (string)f["f"] == "add" && f["args"] is JArray args)
            {
                var parts = args.Select(Constant).ToList();
                return parts.All(p => p.HasValue) ? parts.Sum(p => p.Value) : (double?)null;
            }
            return null;
        }

        private static Dictionary<string, string> Tokens(IReadOnlyList<JToken> effects, Core2Content content = null)
        {
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (token, index, field) in Bindings(effects))
            {
                var raw = effects[index][field];
                var value = Constant(raw);
                if (value.HasValue) { tokens[token] = Core2Text.Num(value.Value); continue; }
                var m = raw as JObject;
                // JS staticCardTokens: the configured Evade grant reads the rules (evadeStackGainPhrase), then describeFormula.
                if (m != null && content != null && (string)m["f"] == "metric" && (string)m["metric"] == "evadeStackGain" && (string)m["of"] == "self" && m["per"] == null)
                {
                    var accuracy = content.Mechanics?["fixCoreMechanics"]?["accuracy"];
                    var b = Core2Json2.Int(accuracy?["evadeStackBase"], 1); var dex = Core2Json2.Int(accuracy?["evadeStackDex"]);
                    tokens[token] = dex == 0 ? b.ToString(System.Globalization.CultureInfo.InvariantCulture) : b + " plus " + (dex == 1 ? "" : dex + "× ") + "DEX";
                    continue;
                }
                var described = Describe(raw);
                if (described != null) tokens[token] = described;
                else if (m != null && (string)m["f"] == "metric" && (string)m["metric"] == "weaponDamage" && (string)m["of"] == "self" && m["per"] != null)
                    tokens[token] = Core2Text.Num((double)m["per"]) + "×";
            }
            return tokens;
        }

        /// <summary>JS describeFormula (src/model/formulas.js) for the shapes Core 2 cards author: add, and a self metric.</summary>
        public static string Describe(JToken formula)
        {
            if (!(formula is JObject f)) return null;
            var keys = f.Properties().Select(p => p.Name).ToList();
            string Bounds(string text)
            {
                if (f["min"] != null) text += " (minimum " + Core2Text.Num((double)f["min"]) + ")";
                if (f["max"] != null) text += " (maximum " + Core2Text.Num((double)f["max"]) + ")";
                return text;
            }
            if ((string)f["f"] == "add")
            {
                if (!(f["args"] is JArray args) || args.Count == 0 || keys.Any(k => k != "f" && k != "args" && k != "min" && k != "max")) return null;
                var parts = args.Select(a => a.Type == JTokenType.Integer || a.Type == JTokenType.Float ? Core2Text.Num((double)a) : Describe(a)).ToList();
                return parts.Any(p => p == null) ? null : Bounds(string.Join(" plus ", parts));
            }
            if ((string)f["f"] == "metric" && keys.All(k => k == "f" || k == "metric" || k == "of" || k == "statuses" || k == "per" || k == "min" || k == "max"))
            {
                var of = (string)f["of"];
                var metric = (string)f["metric"];
                var text = metric == "damageTakenThisTurn" && of == "self" ? "HP lost this turn"
                    : metric == "distinctTypes" && of == "self" ? "distinct card type played this turn"
                    : metric == "sameCardPlays" && of == "self" ? "play of this card this combat"
                    : metric == "evadeStackGain" && of == "self" ? "1"
                    : metric == "negativeStatusTypes" && of == "target" && f["statuses"] is JArray s && s.Count > 0 ? "distinct negative status" : null;
                if (text == null) return null;
                var per = f["per"] != null ? (double)f["per"] : 1;
                if (!(per > 0)) return null;
                var scale = per == 0.5 && metric == "damageTakenThisTurn" ? "half of " : metric == "evadeStackGain" && per == 1 ? "" : Core2Text.Num(per) + " per ";
                var result = Bounds(scale + text);
                if (Math.Abs(per - Math.Round(per)) > 1e-9) result += " (rounded down)";
                return result;
            }
            return null;
        }

        private static string Fill(string template, IReadOnlyDictionary<string, string> tokens) =>
            TokenRe.Replace(template ?? "", m => tokens.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value).Trim();

        /// <summary>A card's glance text (its template with every constant it binds).</summary>
        public static string Card(Core2Content content, string cardId)
        {
            if (!content.HasCard(cardId)) return "";
            var def = content.Card(cardId).Raw;
            var effects = ((def["effects"] as JArray) ?? new JArray()).ToList();
            if (def["onTurnEndInHand"] is JArray hook) effects.AddRange(hook);
            return Fill((string)def["textTemplate"], Tokens(effects, content));
        }

        /// <summary>JS relicText: the relic's own ops, its modifier rows, and its property rules' trigger ops.</summary>
        public static string Relic(Core2RunData data, string relicId)
        {
            if (!data.HasRelic(relicId)) return "";
            var def = data.Relic(relicId);
            var ops = new List<JToken>();
            foreach (var t in (def["triggers"] as JArray ?? new JArray()).OfType<JObject>()) ops.AddRange(t["do"] as JArray ?? new JArray());
            ops.AddRange(def["effects"] as JArray ?? new JArray());
            ops.AddRange(def["do"] as JArray ?? new JArray());
            var tokens = Tokens(ops);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in (def["passives"]?["modifiers"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var tag = (string)row["tag"];
                string b = tag == "resource.flat" ? (string)row["resource"] + "Flat" : tag == "resource.attributeTier" ? (string)row["resource"] + "PerTier" : tag == "damage.school.flat" ? (string)row["school"] + "DamageFlat" : null;
                var value = tag == "resource.attributeTier" ? row["amountPerTier"] : row["amount"];
                if (b == null) continue;
                counts[b] = (counts.TryGetValue(b, out var n) ? n : 0) + 1;
                var token = counts[b] == 1 ? b : b + "." + counts[b];
                if (value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)) tokens[token] = Core2Text.Num((double)value);
            }
            foreach (var tag in Core2Json2.Strings(def["propertyTags"]))
            {
                JObject rule;
                try { rule = data.PropertyRule(tag); } catch (KeyNotFoundException) { continue; }
                var ruleOps = new List<JToken>();
                foreach (var t in (rule["triggers"] as JArray ?? new JArray()).OfType<JObject>()) ruleOps.AddRange(t["do"] as JArray ?? new JArray());
                foreach (var pair in Tokens(ruleOps)) tokens[pair.Key] = pair.Value;
            }
            return Fill((string)def["textTemplate"], tokens);
        }

        /// <summary>A flask's text (its authored template; flasks author their numbers in words).</summary>
        public static string Flask(Core2RunData data, string flaskId) =>
            data.HasFlask(flaskId) ? Fill((string)data.Flask(flaskId)["textTemplate"], Tokens((data.Flask(flaskId)["effects"] as JArray ?? new JArray()).ToList())) : "";
    }
}
