// OriginalLanGameFactory.cs — server-side birth validation and real native run adapter.
// Never accept created-player bodies, HP, deck lists, unlocks or arbitrary formulas
// from a client. Configure earned profile grants on the trusted host in a later
// persistence layer; this foundation uses the authored fresh-profile choices.
using AshenSpire.Domain.Original;
using AshenSpire.Transport;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Companion;

public sealed class OriginalLanGameFactory : ILanGameFactory
{
    private readonly OriginalContentCatalog _catalog;
    private readonly JObject _supplement, _mechanics;
    private readonly AttributeProgression _progression;
    public OriginalLanGameFactory(string contentRoot)
    {
        var data = JObject.Parse(File.ReadAllText(Path.Combine(contentRoot,"content.json")));
        var appearancePath = Path.Combine(contentRoot,"appearance-options.json");
        if (File.Exists(appearancePath))
        {
            var appearance = JObject.Parse(File.ReadAllText(appearancePath));
            // Freeze authored identity lists with the run. Shape/color rendering
            // data remains in the UI sidecar, never in client-submitted effects.
            data["characterCreation"]!["appearance"] = new JObject {
                ["glyphs"] = ChoiceIds(appearance["sigils"],"sigils"),
                ["tints"] = ChoiceIds(appearance["tints"],"tints"),
                ["styles"] = ChoiceIds(appearance["originalStyles"],"styles") };
        }
        _catalog = new OriginalContentCatalog(data.ToString());
        _supplement = JObject.Parse(File.ReadAllText(Path.Combine(contentRoot,"event-choices.json")));
        _mechanics = JObject.Parse(File.ReadAllText(Path.Combine(contentRoot,"mechanics.json")));
        _progression = new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(contentRoot,"progression.json"))));
    }
    private static JArray ChoiceIds(JToken? value,string label)
    {
        if (value is not JArray rows || rows.Count == 0) throw new ArgumentException("Missing authored appearance " + label + ".");
        var ids = rows.Select(row => RequiredString(row.Type == JTokenType.String ? row : row["id"],label)).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ArgumentException("Duplicate appearance IDs.");
        return new JArray(ids);
    }
    private OriginalLanGameFactory(JObject snapshot)
    {
        _catalog = new OriginalContentCatalog(snapshot["content"]!.ToString());
        _supplement = (JObject)snapshot["supplement"]!.DeepClone(); _mechanics = (JObject)snapshot["mechanics"]!.DeepClone();
        _progression = new AttributeProgression((JObject)snapshot["state"]!["members"]![0]!["run"]!["progression"]!.DeepClone());
    }
    public JObject ValidatePlayerSetup(JObject setup)
    {
        if (setup.Properties().Any(p => !new[] { "classId", "modeId", "attributes", "kitId", "startingHands", "startingArmourId", "startingRelicId", "keepsakeId", "customization" }.Contains(p.Name))) throw new ArgumentException("Unknown player setup field.");
        var classId = RequiredString(setup["classId"],"classId");
        var modeId = setup["modeId"] == null ? "pointbuy" : RequiredString(setup["modeId"],"modeId");
        var creation = new CreationModel(_catalog,classId,modeId,_progression);
        var desired = setup["attributes"] as JObject;
        if (setup["attributes"] != null && desired == null) throw new ArgumentException("Attributes must be an object.");
        if (desired != null)
        {
            var names = creation.Attributes().Properties().Select(p => p.Name).ToArray();
            if (desired.Count != names.Length || desired.Properties().Any(p => !names.Contains(p.Name) || p.Value.Type != JTokenType.Integer)) throw new ArgumentException("Supply exactly the five integer attributes.");
            foreach (var key in names) if ((long)desired[key]! < creation.Minimum || (long)desired[key]! > creation.Maximum) throw new ArgumentException("Attribute outside the creation bounds.");
            foreach (var delta in new[] { -1, 1 }) foreach (var key in names)
                while (delta < 0 ? (int)creation.Attributes()[key]! > (int)desired[key]! : (int)creation.Attributes()[key]! < (int)desired[key]!)
                    if (!creation.Adjust(key,delta)) throw new ArgumentException("The allocation exceeds its point budget.");
        }
        var selected = new JObject();
        foreach (var key in new[] { "startingHands", "startingArmourId", "startingRelicId" }) if (setup[key] != null) selected[key] = setup[key]!.DeepClone();
        if (selected["startingHands"] is JObject hands && hands.Properties().Any(p => !new[] { "leftHand", "rightHand" }.Contains(p.Name))) throw new ArgumentException("Unknown starting hand.");
        var kitId = setup["kitId"]?.Type == JTokenType.String ? RequiredString(setup["kitId"],"kitId") : null;
        if (setup["kitId"] != null && setup["kitId"]!.Type is not (JTokenType.Null or JTokenType.String)) throw new ArgumentException("Kit ID must be a string.");
        new OriginalCharacterBuilder(_catalog,_progression,_mechanics).Build(creation,kitId,null,selected);
        var normalized = new JObject { ["classId"] = classId, ["modeId"] = modeId, ["attributes"] = creation.Attributes() };
        if (kitId != null) normalized["kitId"] = kitId;
        foreach (var property in selected.Properties()) normalized[property.Name] = property.Value.DeepClone();
        var keepsakeId = setup["keepsakeId"] == null ? "none" : RequiredString(setup["keepsakeId"],"keepsakeId");
        if (!_catalog.Data()["characterCreation"]!["keepsakes"]!.Any(row => (string?)row["id"] == keepsakeId)) throw new ArgumentException("Unknown keepsake.");
        normalized["keepsakeId"] = keepsakeId;
        normalized["customization"] = ValidateCustomization(setup["customization"]);
        return normalized;
    }
    private JObject ValidateCustomization(JToken? requested)
    {
        if (requested != null && requested is not JObject) throw new ArgumentException("Customization must be an object.");
        var value = new JObject { ["name"] = "Forsaken", ["glyph"] = "⚔", ["tint"] = "gold", ["spriteStyle"] = "animated" };
        if (requested is JObject fields) foreach (var property in fields.Properties())
        {
            if (!new[] { "name", "glyph", "tint", "spriteStyle" }.Contains(property.Name) || property.Value.Type != JTokenType.String) throw new ArgumentException("Unknown or invalid customization field.");
            value[property.Name] = property.Value.DeepClone();
        }
        var name = (string)value["name"]!;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl)) throw new ArgumentException("Wanderer name must contain 1 to 128 visible characters.");
        var appearance = _catalog.Data()["characterCreation"]?["appearance"] as JObject;
        bool Contains(string key,string selected,string[] original)
        {
            if (appearance?[key] is JArray rows && rows.Count > 0) return rows.Any(row => (row.Type == JTokenType.String ? (string?)row : (string?)row["id"]) == selected);
            return original.Contains(selected);
        }
        if (!Contains("glyphs",(string)value["glyph"]!,new[] { "⚔", "🛡", "🔥", "🌙", "☀", "🐺" })) throw new ArgumentException("Unknown character sigil.");
        if (!Contains("tints",(string)value["tint"]!,new[] { "gold", "ember", "frost", "rot", "grace" })) throw new ArgumentException("Unknown character tint.");
        if (!Contains("styles",(string)value["spriteStyle"]!,new[] { "animated", "rendered", "classic", "glyph" })) throw new ArgumentException("Unknown character sprite style.");
        return value;
    }
    private static string RequiredString(JToken? token,string field) => token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)token) && ((string)token!).Length <= 100 ? (string)token! : throw new ArgumentException("Invalid " + field + ".");
    private JObject BuildPlayer(JObject setup)
    {
        setup = ValidatePlayerSetup(setup);
        var creation = new CreationModel(_catalog,(string)setup["classId"]!,(string)setup["modeId"]!,_progression);
        foreach (var delta in new[] { -1, 1 }) foreach (var property in ((JObject)setup["attributes"]!).Properties())
            while (delta < 0 ? (int)creation.Attributes()[property.Name]! > (int)property.Value : (int)creation.Attributes()[property.Name]! < (int)property.Value)
                if (!creation.Adjust(property.Name,delta)) throw new InvalidOperationException("Validated allocation changed.");
        var player = new OriginalCharacterBuilder(_catalog,_progression,_mechanics).Build(creation,(string?)setup["kitId"],null,setup);
        // Keep only validated choice IDs. OriginalRunSession applies authored
        // keepsake effects once at birth; reconnect never reapplies them.
        player["keepsakeId"] = setup["keepsakeId"]!.DeepClone();
        player["customization"] = setup["customization"]!.DeepClone();
        return player;
    }
    public ILanGame Create(uint seed,bool endless,IReadOnlyList<LanMember> members)
    {
        var game = new OriginalCoopRun(_catalog,_supplement,_mechanics,seed,endless);
        foreach (var member in members) game.AddMember(member.Id,member.Name,BuildPlayer(member.Setup));
        return new OriginalLanGame(game,this);
    }
    public ILanGame Restore(JObject snapshot,bool disconnectMembers = true) => new OriginalLanGame(OriginalCoopRun.Restore(snapshot,disconnectMembers),new OriginalLanGameFactory(snapshot));
    private sealed class OriginalLanGame(OriginalCoopRun run,OriginalLanGameFactory factory) : ILanGame
    {
        public JObject View(string? memberId = null) => run.View(memberId);
        public JObject Snapshot() => run.Snapshot();
        public JObject Execute(string memberId,long sequence,JObject intent) => run.Execute(memberId,sequence,intent);
        public void SetConnected(string memberId,bool connected) => run.SetConnected(memberId,connected);
        public JObject ValidatePlayerSetup(JObject setup) => factory.ValidatePlayerSetup(setup);
        public void AddMember(LanMember member)
        {
            var before = run.Snapshot();
            try { run.AddMember(member.Id,member.Name,factory.BuildPlayer(member.Setup)); }
            catch { run = OriginalCoopRun.Restore(before,false); throw; }
        }
    }
}
