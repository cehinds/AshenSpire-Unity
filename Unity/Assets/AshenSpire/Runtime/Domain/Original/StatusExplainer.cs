// StatusExplainer.cs — plain-text explanations of active statuses and card tags (US-4.4, US-13.4).
// Pure: reads authored `statuses[].tooltip` and `tags[].blurb`; never changes snapshots.
// Token substitution and value/duration wording port src/ui/uiContent.js
// (statusTooltipText, statusInstancePresentation). Presentation shows the result on
// tap/long-press, so no explanation depends on hover. MODIFY wording in content.json.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class StatusExplanation
    {
        public string Id { get; }
        public string Name { get; }
        /// <summary>Stack count, or the build-up meter value for proc statuses.</summary>
        public int Stacks { get; }
        /// <summary>Build-up threshold for meter statuses; otherwise null.</summary>
        public int? MeterMax { get; }
        public int? Duration { get; }
        /// <summary>`Bleed 3 / 7`, `Weak ×2`, `Magic Vulnerable 25% · 2 turns`.</summary>
        public string Label { get; }
        /// <summary>Authored tooltip with its tokens bound, plus `Turns left: N.` when timed.</summary>
        public string Text { get; }
        /// <summary>False when the id has no authored status row and the fallback wording is used.</summary>
        public bool Authored { get; }
        internal StatusExplanation(string id, string name, int stacks, int? meterMax, int? duration, string label, string text, bool authored)
        { Id = id; Name = name; Stacks = stacks; MeterMax = meterMax; Duration = duration; Label = label; Text = text; Authored = authored; }
        /// <summary>One readable line: `Weak ×2 — Deals 25% less attack damage…`.</summary>
        public string Line => Label + " — " + Text;
    }

    public sealed class CardTagExplanation
    {
        public string Id { get; }
        public string Label { get; }
        public string Glyph { get; }
        public string Blurb { get; }
        internal CardTagExplanation(string id, string label, string glyph, string blurb) { Id = id; Label = label; Glyph = glyph; Blurb = blurb; }
        public string Line => string.IsNullOrEmpty(Blurb) ? Label : Label + " — " + Blurb;
    }

    public static class StatusExplainer
    {
        public const string UnknownText = "No description is recorded for this status.";

        /// <summary>Port of `statusTooltipText`: binds a row's own knobs; unresolved tokens stay visible.</summary>
        public static string TooltipText(JObject definition)
        {
            var text = (string)definition?["tooltip"] ?? "";
            void Sub(string token, JToken value)
            {
                if (value == null || value.Type == JTokenType.Null) return;
                text = text.Replace("{" + token + "}", Format(value));
            }
            if (definition?["proc"] is JObject proc)
                foreach (var key in new[] { "threshold", "burstPercent", "burstMin", "burstMax", "poiseDamage" }) Sub("proc." + key, proc[key]);
            if (definition?["resists"] is JObject resists) Sub("resists.percent", resists["percent"]);
            if (definition?["taggedVulnerability"] is JObject tv && tv["mult"] != null && tv["mult"].Type != JTokenType.Null)
                Sub("tv.pct", new JValue((long)Math.Round(((double)tv["mult"] - 1) * 100, MidpointRounding.AwayFromZero)));
            if (definition?["decay"] is JObject decay) Sub("decay.duration", decay["duration"]);
            return text;
        }

        /// <summary>Every status on a combatant snapshot, in snapshot order.</summary>
        public static IReadOnlyList<StatusExplanation> Describe(OriginalContentCatalog catalog, JObject statuses)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            return Describe(id => { try { return catalog.Record("statuses", id); } catch (ArgumentException) { return null; } }, statuses);
        }

        /// <summary>Same as above against a raw `statuses` table (e.g. a co-op content copy).</summary>
        public static IReadOnlyList<StatusExplanation> Describe(JArray statusTable, JObject statuses)
        {
            var rows = (statusTable ?? new JArray()).OfType<JObject>().Where(x => x["id"] != null)
                .GroupBy(x => (string)x["id"], StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            return Describe(id => rows.TryGetValue(id, out var row) ? row : null, statuses);
        }

        private static IReadOnlyList<StatusExplanation> Describe(Func<string, JObject> lookup, JObject statuses)
        {
            var result = new List<StatusExplanation>();
            if (statuses == null) return result;
            foreach (var property in statuses.Properties())
            {
                var instance = property.Value as JObject ?? new JObject { ["stacks"] = property.Value };
                result.Add(Explain(property.Name, lookup(property.Name), instance));
            }
            return result;
        }

        /// <summary>Port of `statusInstancePresentation` plus the meter wording of the HTML proc tooltip.</summary>
        public static StatusExplanation Explain(string id, JObject definition, JObject instance)
        {
            var authored = definition != null && !string.IsNullOrWhiteSpace((string)definition["tooltip"]);
            var name = (string)definition?["name"];
            if (string.IsNullOrWhiteSpace(name)) name = OriginalCardText.Humanize(id);
            var meter = instance?["meter"] as JObject;
            var stacks = Int(meter != null ? meter["value"] : instance?["stacks"]) ?? 0;
            var meterMax = meter == null ? null : Int(meter["max"]);
            var duration = Int(instance?["duration"]);
            var durationText = duration == null ? "" : duration + (duration == 1 ? " turn" : " turns");
            var percent = (string)definition?["instancePresentation"]?["valueToken"] == "percent";
            string label;
            if (percent) label = name + " " + stacks + "%" + (durationText.Length > 0 ? " · " + durationText : "");
            else if (meterMax != null) label = name + " " + stacks + " / " + meterMax;
            else label = name + " ×" + stacks;
            var text = authored ? TooltipText(definition) : UnknownText + " (" + id + ")";
            if (duration != null) text += " Turns left: " + duration + ".";
            return new StatusExplanation(id, name, stacks, meterMax, duration, label, text, authored);
        }

        /// <summary>Panel text for the tooltip helper: first line is the title, one line per status.</summary>
        public static string Panel(string title, IReadOnlyList<StatusExplanation> rows, string guard = null)
        {
            var lines = new List<string> { title };
            if (!string.IsNullOrEmpty(guard)) lines.Add(guard);
            if (rows == null || rows.Count == 0) lines.Add("No active statuses.");
            else lines.AddRange(rows.Select(r => r.Line));
            return string.Join("\n", lines);
        }

        /// <summary>Tag → blurb rows for a card, using the same tag ids the card face draws.</summary>
        public static IReadOnlyList<CardTagExplanation> CardTags(OriginalContentCatalog catalog, JObject card)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (card == null) throw new ArgumentNullException(nameof(card));
            return CardTags(TagIds(catalog, card), id => { try { return catalog.Record("tags", id); } catch (ArgumentException) { return null; } });
        }

        /// <summary>Explicit (possibly empty) `cardTags` win; otherwise the catalog's card tag junction.</summary>
        public static IEnumerable<string> TagIds(OriginalContentCatalog catalog, JObject card)
            => (card["cardTags"] is JArray explicitTags ? explicitTags.Values<string>() : catalog.Tags("card", card)).Distinct(StringComparer.Ordinal);

        public static IReadOnlyList<CardTagExplanation> CardTags(IEnumerable<string> ids, Func<string, JObject> lookup)
        {
            var result = new List<CardTagExplanation>();
            foreach (var id in ids ?? Enumerable.Empty<string>())
            {
                var tag = lookup(id);
                if (tag == null) continue; // the card face skips unknown tags too
                result.Add(new CardTagExplanation(id, (string)tag["label"] ?? id, (string)tag["glyph"] ?? "", (string)tag["blurb"] ?? ""));
            }
            return result;
        }

        private static int? Int(JToken token)
        {
            if (token == null) return null;
            if (token.Type == JTokenType.Integer) return (int)token;
            if (token.Type == JTokenType.Float) return (int)Math.Floor((double)token);
            return null;
        }

        private static string Format(JToken value)
            => value.Type == JTokenType.Float ? ((double)value).ToString(CultureInfo.InvariantCulture) : value.ToString();
    }
}
