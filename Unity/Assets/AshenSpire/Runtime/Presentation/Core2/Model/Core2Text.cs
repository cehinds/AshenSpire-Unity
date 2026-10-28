// Core2Text.cs — the JS interface copy for the Core 2 run screens: a port of src/ui/strings.js over the generated table
// (Generated/Core2UiText.g.cs, tools/unity-core2-text.mjs). Engine-free: UnityTests/Core2RunView compiles it.
//
// THREE FORMS PER ID (short, full, tip); a blank cell inherits from the row named in `extends`; {tokens} are filled from
// the caller's values and an unresolved token THROWS (JS: "prose that names a number the caller never passed").
// The run screens' other JS tables live here too: map node kinds, act titles, speakers, attribute labels.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AshenSpire.Presentation.Core2
{
    public static class Core2Text
    {
        private static readonly Dictionary<string, string[]> Rows = BuildRows();
        private static readonly Dictionary<string, string[]> Resolved = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private static readonly Regex Token = new Regex(@"\{(\w+)\}", RegexOptions.CultureInvariant);

        private static Dictionary<string, string[]> BuildRows()
        {
            var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var row in Core2UiData.Strings)
            {
                var id = (row[0] ?? "").Trim();
                if (id.Length == 0) throw new InvalidOperationException("uiStrings: a row has no id");
                if (rows.ContainsKey(id)) throw new InvalidOperationException("uiStrings: duplicate id " + id);
                rows[id] = row;
            }
            return rows;
        }

        public static bool Has(string id, string form = "short") => Rows.ContainsKey(id) && Resolve(id)[FormIndex(form)].Length > 0;

        private static int FormIndex(string form) => form == "short" ? 0 : form == "full" ? 1 : form == "tip" ? 2 : throw new ArgumentException("Unknown form " + form);

        private static string[] Resolve(string id, List<string> seen = null)
        {
            lock (Resolved)
            {
                if (Resolved.TryGetValue(id, out var done)) return done;
            }
            if (!Rows.TryGetValue(id, out var row)) throw new KeyNotFoundException("uiStrings: unknown id '" + id + "'");
            seen = seen ?? new List<string>();
            if (seen.Contains(id)) throw new InvalidOperationException("uiStrings: extends cycle " + string.Join(" → ", seen.Concat(new[] { id })));
            seen.Add(id);
            var parentId = (row[1] ?? "").Trim();
            var parent = parentId.Length > 0 ? Resolve(parentId, seen) : null;
            var output = new string[3];
            for (var i = 0; i < 3; i++)
            {
                var own = row[2 + i] ?? "";
                if (own.Contains("|")) throw new InvalidOperationException("uiStrings: '" + id + "' contains '|' — a cell is one sentence, not a list");
                output[i] = own.Length > 0 ? own : parent != null ? parent[i] : "";
            }
            lock (Resolved) Resolved[id] = output;
            return output;
        }

        private static string Read(string id, string form, object tokens)
        {
            var text = Resolve(id)[FormIndex(form)];
            if (text.Length == 0) throw new InvalidOperationException("uiStrings: '" + id + "' has no " + form + " form");
            return Fill(text, Bag(tokens), id + "." + form);
        }

        /// <summary>JS t(id, tokens): the control's own words. Tokens: an anonymous object or a dictionary.</summary>
        public static string T(string id, object tokens = null) => Read(id, "short", tokens);
        /// <summary>JS tFull: the sentence the control means.</summary>
        public static string Full(string id, object tokens = null) => Read(id, "full", tokens);
        /// <summary>JS tTip: the tooltip title.</summary>
        public static string Tip(string id, object tokens = null) => Read(id, "tip", tokens);

        public static string Fill(string text, IReadOnlyDictionary<string, string> tokens, string where = "text") =>
            Token.Replace(text, m =>
            {
                var key = m.Groups[1].Value;
                if (tokens == null || !tokens.TryGetValue(key, out var value)) throw new InvalidOperationException("uiStrings: '" + where + "' wants {" + key + "} and the caller passed none");
                return value;
            });

        private static IReadOnlyDictionary<string, string> Bag(object tokens)
        {
            if (tokens == null) return null;
            if (tokens is IReadOnlyDictionary<string, string> ready) return ready;
            var bag = new Dictionary<string, string>(StringComparer.Ordinal);
            if (tokens is IDictionary<string, object> map) { foreach (var p in map) bag[p.Key] = Str(p.Value); return bag; }
            foreach (var property in tokens.GetType().GetProperties()) bag[property.Name] = Str(property.GetValue(tokens));
            return bag;
        }

        /// <summary>A value as JS String(v) prints it (numbers invariant, integral doubles without a point).</summary>
        public static string Str(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case double d: return Num(d);
                case float f: return Num(f);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        public static string Num(double d) => Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 9e15 ? ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture) : d.ToString("0.############", CultureInfo.InvariantCulture);

        // ---- the other JS tables -------------------------------------------------------------------------------------

        /// <summary>uiContent nodeTypes[type] (icon, name, blurb); an unknown type reads as its own name (JS nodeName).</summary>
        public static (string Icon, string Name, string Blurb) NodeType(string type)
        {
            var row = Core2UiData.NodeTypes.FirstOrDefault(r => r[0] == type);
            return row == null ? ("?", type ?? "", "") : (row[1], row[2], row[3]);
        }

        public static string NodeTint(string type) => Core2UiData.NodeTint.FirstOrDefault(r => r[0] == type)?[1];

        /// <summary>JS actTitle(actNumber, seatName): with a seat, ACT &lt;tier&gt; — &lt;SEAT&gt;; a cycle past the named acts adds its suffix.</summary>
        public static string ActTitle(int actNumber, string seatName)
        {
            var count = Core2UiData.ActNames.Length;
            var loop = (actNumber - 1) / count;
            var plate = ((actNumber - 1) % count) + 1;
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            string title;
            if (seatName != null)
            {
                tokens["tier"] = plate - 1 < Core2UiData.TierNumerals.Length ? Core2UiData.TierNumerals[plate - 1] : plate.ToString(CultureInfo.InvariantCulture);
                tokens["seat"] = seatName.ToUpperInvariant();
                title = Fill(Core2UiData.ActTitleTiered, tokens);
            }
            else
            {
                var named = Core2UiData.ActNames.FirstOrDefault(r => r[0] == plate.ToString(CultureInfo.InvariantCulture));
                tokens["act"] = actNumber.ToString(CultureInfo.InvariantCulture);
                title = named != null ? named[1] : Fill(Core2UiData.ActTitleNumbered, tokens);
            }
            if (loop > 0) title += Fill(Core2UiData.ActTitleCycleSuffix, new Dictionary<string, string> { ["cycle"] = (loop + 1).ToString(CultureInfo.InvariantCulture) });
            return title;
        }

        /// <summary>The speaker of a quest-chain event (JS registries.speakers[eventSpeakers[id]]), or null for a plain event.</summary>
        public static (string Id, string Name, string PortraitKey)? Speaker(string eventId)
        {
            var link = Core2UiData.EventSpeakers.FirstOrDefault(r => r[0] == eventId);
            var row = link == null ? null : Core2UiData.Speakers.FirstOrDefault(r => r[0] == link[1]);
            return row == null ? ((string, string, string)?)null : (row[0], row[1], row[2]);
        }

        public static string AttributeShort(string id) => Core2UiData.Attributes.FirstOrDefault(r => r[0] == id)?[2] ?? id;
        public static string AttributeLabel(string id) => Core2UiData.Attributes.FirstOrDefault(r => r[0] == id)?[1] ?? id;
    }
}
