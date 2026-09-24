// HandRules.cs — the web game's optional hand-management rules (SPEC §4.1, web
// src/model/handRules.js). Authored once in content.json "handRules"; a solo
// fight snapshots them at creation and carries them in its save. A fight with no
// snapshot (co-op, LAN, saves made before the rules existed) keeps the legacy
// derived-Draw turn draw and balance.handMax capacity.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class HandRules
    {
        private static readonly string[] Groups = { "starting", "turn", "capacity" };
        private static readonly string[] Attributes = { "strength", "dexterity", "constitution", "wisdom", "intelligence" };

        /// <summary>Every problem with a hand-rules object; empty when valid (web handRulesProblems).</summary>
        public static List<string> Problems(JToken token)
        {
            if (!(token is JObject rules)) return new List<string> { "Hand rules must be an object" };
            var problems = new List<string>();
            foreach (var key in new[] { "retain", "promptDiscard", "replaceDiscards", "reshuffle" })
                if (rules[key]?.Type != JTokenType.Boolean) problems.Add("Hand rules: " + key + " must be boolean");
            var drawMode = rules["drawMode"]?.Type == JTokenType.String ? (string)rules["drawMode"] : null;
            var overflow = rules["overflow"]?.Type == JTokenType.String ? (string)rules["overflow"] : null;
            if ((drawMode != "fill" && drawMode != "fixed") || (overflow != "keep" && overflow != "discard")) problems.Add("Hand rules: invalid draw or overflow mode");
            if (!WholeIn(rules["discardLimit"], 0, 99)) problems.Add("Hand rules: invalid discard limit");
            foreach (var group in Groups)
            {
                if (!(rules[group] is JObject rule)) { problems.Add("Hand rules: missing " + group); continue; }
                var stat = rule["stat"]?.Type == JTokenType.String ? (string)rule["stat"] : null;
                if (rule["statEnabled"]?.Type != JTokenType.Boolean || !Attributes.Contains(stat)) problems.Add("Hand rules: invalid " + group + " stat");
                foreach (var key in new[] { "base", "baseline", "pointsPerCard", "minimum", "maximum" })
                {
                    var min = key == "pointsPerCard" || (group == "capacity" && (key == "base" || key == "minimum" || key == "maximum")) ? 1 : 0;
                    if (!WholeIn(rule[key], min, 99)) problems.Add("Hand rules: invalid " + group + "." + key);
                }
                if (WholeIn(rule["minimum"], 0, 99) && WholeIn(rule["maximum"], 0, 99) && (long)rule["minimum"] > (long)rule["maximum"]) problems.Add("Hand rules: " + group + " minimum must not exceed maximum");
            }
            return problems;
        }

        /// <summary>Throws ArgumentException naming every problem; returns a private copy.</summary>
        public static JObject Validate(JToken token)
        {
            var problems = Problems(token);
            if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
            return (JObject)token.DeepClone();
        }

        /// <summary>
        /// The card count a rule states: base + floor(max(0, attribute − baseline) / pointsPerCard)
        /// while statEnabled, clamped to minimum..maximum (web scaledCards). The attribute is the
        /// sheet value as shown — no creation-scale divisor.
        /// </summary>
        public static int ScaledCards(JObject rule, JObject attributes) => (int)ScaledCardsReceipt(rule, attributes)["value"];

        /// <summary>Every term ScaledCards used, for a worked example (web scaledCardsReceipt).</summary>
        public static JObject ScaledCardsReceipt(JObject rule, JObject attributes)
        {
            var stat = (string)rule["stat"];
            var points = PointsOf(attributes?[stat]);
            var statEnabled = (bool)rule["statEnabled"];
            int baseline = (int)rule["baseline"], perCard = (int)rule["pointsPerCard"], @base = (int)rule["base"], minimum = (int)rule["minimum"], maximum = (int)rule["maximum"];
            var bonus = statEnabled ? (int)Math.Floor(Math.Max(0d, points - baseline) / perCard) : 0;
            var raw = @base + bonus;
            return new JObject
            {
                ["stat"] = stat, ["statEnabled"] = statEnabled, ["points"] = points, ["baseline"] = baseline, ["pointsPerCard"] = perCard,
                ["base"] = @base, ["bonus"] = bonus, ["raw"] = raw, ["minimum"] = minimum, ["maximum"] = maximum,
                ["value"] = Math.Min(maximum, Math.Max(minimum, raw)),
            };
        }

        // Web: Number(attributes?.[stat]) || 0 — a missing or non-numeric attribute reads as 0.
        private static double PointsOf(JToken value)
        {
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return 0;
            var number = (double)value; return double.IsNaN(number) || double.IsInfinity(number) ? 0 : number;
        }

        private static bool WholeIn(JToken value, long min, long max)
        {
            if (value == null) return false;
            if (value.Type == JTokenType.Integer) { var n = (long)value; return n >= min && n <= max; }
            if (value.Type == JTokenType.Float) { var d = (double)value; return d == Math.Floor(d) && d >= min && d <= max; }
            return false;
        }
    }
}
