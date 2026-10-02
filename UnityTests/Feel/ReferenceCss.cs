// ReferenceCss.cs — a small reader for the HTML reference's @keyframes, so the feel profile is
// compared against the live stylesheets rather than a copy that would agree with itself.
// Scope: opacity, letter-spacing (em), transform (translate/translateX/translateY/scale/scaleX/
// rotate, calc(var(--rot) ± Ndeg) as a relative angle, % translations ignored as centring) and
// filter (brightness/saturate/grayscale/blur; drop-shadow ignored). Enough for every keyframe
// the profile encodes; anything else is ignored rather than guessed.
using System.Globalization;
using System.Text.RegularExpressions;
using AshenSpire.Domain;

static class ReferenceCss
{
    private static readonly Regex Function = new Regex(@"([a-zA-Z]+)\(((?:[^()]|\((?:[^()]|\([^()]*\))*\))*)\)");

    public static string StripComments(string css) => Regex.Replace(css, @"/\*.*?\*/", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);

    /// <summary>Per-property tracks of `@keyframes name`, with CSS implicit 0%/100% keys at rest values; null if absent.</summary>
    public static Dictionary<string, List<(double T, double V)>> Keyframes(string css, string name)
    {
        css = StripComments(css);
        var head = Regex.Match(css, @"@keyframes\s+" + Regex.Escape(name) + @"\s*\{");
        if (!head.Success) return null;
        int depth = 1, i = head.Index + head.Length, start = i;
        while (depth > 0 && i < css.Length) { if (css[i] == '{') depth++; else if (css[i] == '}') depth--; i++; }
        var body = css.Substring(start, i - 1 - start);
        // CSS property group -> offset -> sub-property values set at that keyframe
        var groups = new Dictionary<string, SortedDictionary<double, Dictionary<string, double>>>();
        foreach (Match block in Regex.Matches(body, @"([^{}]+)\{([^{}]*)\}"))
        {
            var offsets = block.Groups[1].Value.Split(',').Select(s => s.Trim()).Select(s => s == "from" ? 0 : s == "to" ? 1 : double.Parse(s.TrimEnd('%'), CultureInfo.InvariantCulture) / 100).ToArray();
            foreach (var decl in block.Groups[2].Value.Split(';'))
            {
                var colon = decl.IndexOf(':');
                if (colon < 0) continue;
                var prop = decl.Substring(0, colon).Trim();
                var value = decl.Substring(colon + 1).Trim();
                Dictionary<string, double> subs;
                switch (prop)
                {
                    case "opacity": subs = new Dictionary<string, double> { [FeelProperty.Opacity] = Num(value) }; break;
                    case "letter-spacing": subs = new Dictionary<string, double> { [FeelProperty.LetterSpacing] = Num(value) }; break;
                    case "transform": subs = Transform(value); break;
                    case "filter": subs = Filter(value); break;
                    default: continue;
                }
                if (!groups.TryGetValue(prop, out var byOffset)) groups[prop] = byOffset = new SortedDictionary<double, Dictionary<string, double>>();
                foreach (var o in offsets) byOffset[o] = subs;
            }
        }
        var tracks = new Dictionary<string, List<(double, double)>>();
        foreach (var byOffset in groups.Values)
        {
            if (!byOffset.ContainsKey(0)) byOffset[0] = new Dictionary<string, double>();
            if (!byOffset.ContainsKey(1)) byOffset[1] = new Dictionary<string, double>();
            foreach (var sub in byOffset.Values.SelectMany(d => d.Keys).Distinct())
            {
                var rest = FeelProperty.Rest(sub);
                var keys = byOffset.Select(kv => (kv.Key, kv.Value.TryGetValue(sub, out var v) ? v : rest)).ToList();
                if (keys.All(k => Math.Abs(k.Item2 - rest) < 1e-9)) continue;
                tracks[sub] = keys;
            }
        }
        return tracks;
    }

    private static double Num(string s)
    {
        s = s.Trim();
        foreach (var unit in new[] { "px", "deg", "em", "%" }) if (s.EndsWith(unit, StringComparison.Ordinal)) s = s.Substring(0, s.Length - unit.Length);
        return double.Parse(s, CultureInfo.InvariantCulture);
    }

    private static double? Length(string s)
    {
        s = s.Trim();
        if (s.Contains("calc("))
        {
            var rel = Regex.Match(s, @"([+-])\s*([\d.]+)deg\s*\)\s*$");
            return rel.Success ? (rel.Groups[1].Value == "-" ? -1 : 1) * double.Parse(rel.Groups[2].Value, CultureInfo.InvariantCulture) : (double?)null;
        }
        if (s.EndsWith("%", StringComparison.Ordinal) || s.EndsWith("rem", StringComparison.Ordinal)) return null; // centring / text units: not a feel value
        return Num(s);
    }

    private static Dictionary<string, double> Transform(string value)
    {
        var subs = new Dictionary<string, double>();
        if (value == "none") return subs;
        foreach (Match f in Function.Matches(value))
        {
            var args = SplitArgs(f.Groups[2].Value);
            void Set(string key, string arg) { var v = Length(arg); if (v.HasValue) subs[key] = v.Value; }
            switch (f.Groups[1].Value)
            {
                case "translate": Set(FeelProperty.X, args[0]); Set(FeelProperty.Y, args.Length > 1 ? args[1] : "0"); break;
                case "translateX": Set(FeelProperty.X, args[0]); break;
                case "translateY": Set(FeelProperty.Y, args[0]); break;
                case "scale": Set(FeelProperty.Scale, args[0]); break;
                case "scaleX": Set(FeelProperty.ScaleX, args[0]); break;
                case "rotate": Set(FeelProperty.Rotate, args[0]); break;
            }
        }
        return subs;
    }

    private static Dictionary<string, double> Filter(string value)
    {
        var subs = new Dictionary<string, double>();
        if (value == "none") return subs;
        foreach (Match f in Function.Matches(value))
        {
            switch (f.Groups[1].Value)
            {
                case "brightness": subs[FeelProperty.Brightness] = Num(f.Groups[2].Value); break;
                case "saturate": subs[FeelProperty.Saturate] = Num(f.Groups[2].Value); break;
                case "grayscale": subs[FeelProperty.Grayscale] = Num(f.Groups[2].Value); break;
                case "blur": subs[FeelProperty.Blur] = Num(f.Groups[2].Value); break;
            }
        }
        return subs;
    }

    private static string[] SplitArgs(string s)
    {
        var parts = new List<string>(); int depth = 0, start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++; else if (s[i] == ')') depth--;
            else if (s[i] == ',' && depth == 0) { parts.Add(s.Substring(start, i - start)); start = i + 1; }
        }
        parts.Add(s.Substring(start));
        return parts.Select(p => p.Trim()).ToArray();
    }
}
