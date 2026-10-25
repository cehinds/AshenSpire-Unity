// Conservative text measurement from a TrueType font's own metrics (cmap + hmtx + hhea/OS2),
// used to check that the illustrated face's rules fit (OriginalCardRulesFit) without Unity.
// Widths are the font's advances × Slack (covers faux-bold highlighted numbers and kerning
// differences); line height is the larger of the hhea and Windows ascent/descent spans.
using System.Buffers.Binary;

sealed class FontMetrics
{
    /// <summary>The illustrated card face's text font (OriginalCards.uss .published-card-text):
    /// Liberation Serif, metric-compatible with Times and the bundled stand-in for the JS card's
    /// Georgia. Change it together with that USS rule.</summary>
    public const string CardFaceFont = "Unity/Assets/AshenSpire/Resources/Fonts/LiberationSerif-Regular.ttf";
    public const float Slack = 1.12f;
    private readonly Dictionary<int, int> _glyphOf = new();
    private readonly ushort[] _advance;
    private readonly int _unitsPerEm, _lineUnits, _fallbackAdvance;

    public FontMetrics(string path)
    {
        var data = File.ReadAllBytes(path);
        ushort U16(int at) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));
        short S16(int at) => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(at));
        uint U32(int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
        var tables = new Dictionary<string, int>();
        for (int i = 0, count = U16(4); i < count; i++)
        {
            var record = 12 + i * 16;
            tables[System.Text.Encoding.ASCII.GetString(data, record, 4)] = (int)U32(record + 8);
        }
        _unitsPerEm = U16(tables["head"] + 18);
        var hhea = tables["hhea"];
        var hheaLine = S16(hhea + 4) - S16(hhea + 6) + S16(hhea + 8);
        var winLine = tables.TryGetValue("OS/2", out var os2) ? U16(os2 + 74) + U16(os2 + 76) : 0;
        _lineUnits = Math.Max(hheaLine, winLine);
        var metrics = U16(hhea + 34);
        var glyphs = U16(tables["maxp"] + 4);
        _advance = new ushort[glyphs];
        for (var g = 0; g < glyphs; g++) _advance[g] = U16(tables["hmtx"] + Math.Min(g, metrics - 1) * 4);
        _fallbackAdvance = _advance.Max();
        var cmap = tables["cmap"];
        for (int i = 0, count = U16(cmap + 2); i < count; i++)
        {
            var sub = cmap + (int)U32(cmap + 4 + i * 8 + 4);
            var format = U16(sub);
            if (format == 4)
            {
                var segments = U16(sub + 6) / 2;
                int ends = sub + 14, starts = ends + segments * 2 + 2, deltas = starts + segments * 2, offsets = deltas + segments * 2;
                for (var s = 0; s < segments; s++)
                {
                    int end = U16(ends + s * 2), start = U16(starts + s * 2), delta = S16(deltas + s * 2), offset = U16(offsets + s * 2);
                    for (var c = start; c <= end && c != 0xFFFF; c++)
                    {
                        int glyph;
                        if (offset == 0) glyph = (c + delta) & 0xFFFF;
                        else { var at = offsets + s * 2 + offset + (c - start) * 2; glyph = U16(at); if (glyph != 0) glyph = (glyph + delta) & 0xFFFF; }
                        if (glyph != 0) _glyphOf.TryAdd(c, glyph);
                    }
                }
            }
            else if (format == 12)
            {
                for (int g = 0, groups = (int)U32(sub + 12); g < groups; g++)
                {
                    var at = sub + 16 + g * 12;
                    for (long c = U32(at), first = (long)U32(at + 8); c <= U32(at + 4); c++) _glyphOf.TryAdd((int)c, (int)(first + c - U32(at)));
                }
            }
        }
    }

    /// <summary>Width of a string at a font size (missing glyphs count as the widest advance).</summary>
    public float Width(string text, float size)
        => text.Sum(ch => (float)(_glyphOf.TryGetValue(ch, out var g) && g < _advance.Length ? _advance[g] : _fallbackAdvance)) * size / _unitsPerEm * Slack;

    public float LineHeight(float size) => _lineUnits * size / _unitsPerEm;

    /// <summary>Greedy word wrap honouring forced breaks; the wrapped height.</summary>
    public float Height(string text, float size, float width)
    {
        var lines = 0;
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            lines++;
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Width(candidate, size) <= width) { line = candidate; continue; }
                if (line.Length > 0) { lines++; line = word; }
                // A single word wider than the box breaks across lines like the text engine does.
                while (Width(line, size) > width && line.Length > 1)
                {
                    var take = line.Length - 1;
                    while (take > 1 && Width(line[..take], size) > width) take--;
                    line = line[take..]; lines++;
                }
            }
        }
        return lines * LineHeight(size);
    }
}
