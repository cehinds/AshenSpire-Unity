// CatalogBuilder.cs — builds music-catalog.json from the HTML sources of truth:
// src/content/music.js (SCALES, BEDS → procedural bed tracks) and
// music/manifest.json (owner file tracks, credited via its optional "_credits"
// object, which the HTML ignores because it only reads context keys).
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AshenSpire.Domain;

static class CatalogBuilder
{
    public const string CatalogPath = "Unity/Assets/AshenSpire/Resources/Audio/music-catalog.json";
    public const string BedCredit = "Procedural music beds";
    public static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

    public static MusicCatalog Build(string root)
    {
        var js = File.ReadAllText(Path.Combine(root, "src/content/music.js"));
        var inv = CultureInfo.InvariantCulture;
        var catalog = new MusicCatalog
        {
            ManifestPath = "music/manifest.json",
            BedsPath = "src/content/music.js",
            FileResourceRoot = "Audio/Music",
            AllowedLicenses = new[] { "CC0", "CC BY 3.0", "CC BY 4.0" },
            Bus = new MusicBusDefinition(),
            Crossfade = new MusicCrossfadeDefinition(),
        };
        var scalesBlock = Regex.Match(js, @"export const SCALES = \{(.*?)\n\};", RegexOptions.Singleline).Groups[1].Value;
        catalog.Scales = Regex.Matches(scalesBlock, @"^\s+(\w+): \[([\d,\s]+)\]", RegexOptions.Multiline)
            .Select(m => new MusicScaleDefinition { Id = m.Groups[1].Value, Steps = m.Groups[2].Value.Split(',').Select(s => int.Parse(s.Trim(), inv)).ToArray() }).ToArray();
        var bedsBlock = Regex.Match(js, @"export const BEDS = \{(.*?)\n\};", RegexOptions.Singleline).Groups[1].Value;
        var contexts = new List<MusicContextDefinition>();
        var tracks = new List<MusicTrackDefinition>();
        foreach (Match silence in Regex.Matches(bedsBlock, @"^  (\w+): 'silence',", RegexOptions.Multiline))
            contexts.Add(new MusicContextDefinition { Id = silence.Groups[1].Value, Silence = true });
        foreach (Match bed in Regex.Matches(bedsBlock, @"^  (\w+): \{ drone: (true|false), gain: ([\d.]+)(, pulse: true)?, variants: \[(.*?)\] \},", RegexOptions.Multiline | RegexOptions.Singleline))
        {
            var id = bed.Groups[1].Value;
            contexts.Add(new MusicContextDefinition { Id = id, Gain = float.Parse(bed.Groups[3].Value, inv) });
            var n = 0;
            foreach (Match v in Regex.Matches(bed.Groups[5].Value, @"\{ root: ([\d.]+), scale: '(\w+)', cadence: (\d+), wave: '(\w+)', lift: (\d+) \}"))
            {
                tracks.Add(new MusicTrackDefinition
                {
                    Id = "bed." + id + "." + (n + 1), Context = id, Kind = MusicCatalog.KindBed, Path = catalog.BedsPath, Loop = true,
                    Variant = n, Root = float.Parse(v.Groups[1].Value, inv), Scale = v.Groups[2].Value, CadenceMs = int.Parse(v.Groups[3].Value, inv),
                    Wave = v.Groups[4].Value, Lift = int.Parse(v.Groups[5].Value, inv), Drone = bed.Groups[2].Value == "true", Pulse = bed.Groups[4].Success,
                    Credit = BedCredit, Author = "AshenSpire", License = "CC0",
                });
                n++;
            }
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, catalog.ManifestPath)));
        manifest.RootElement.TryGetProperty("_credits", out var credits);
        foreach (var ctx in contexts)
        {
            if (!manifest.RootElement.TryGetProperty(ctx.Id, out var list) || list.ValueKind != JsonValueKind.Array) continue;
            var n = 0;
            foreach (var entry in list.EnumerateArray())
            {
                var rel = entry.GetString();
                string Credit(string key) => credits.ValueKind == JsonValueKind.Object && credits.TryGetProperty(rel, out var c) && c.TryGetProperty(key, out var s) ? s.GetString() : null;
                tracks.Add(new MusicTrackDefinition
                {
                    Id = "file." + ctx.Id + "." + (++n), Context = ctx.Id, Kind = MusicCatalog.KindFile, Path = "music/" + rel, Loop = false,
                    ResourcePath = catalog.FileResourceRoot + "/" + Path.ChangeExtension(rel, null).Replace('\\', '/'),
                    Credit = Credit("credit"), Author = Credit("author"), License = Credit("license"),
                });
            }
        }
        catalog.Contexts = contexts.ToArray();
        catalog.Tracks = tracks.ToArray();
        return catalog;
    }

    public static string Serialize(MusicCatalog catalog) => JsonSerializer.Serialize(catalog, Json).Replace("\r\n", "\n") + "\n";
}
