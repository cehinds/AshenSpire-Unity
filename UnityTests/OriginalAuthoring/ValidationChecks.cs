// ValidationChecks.cs — US-16.3: schema validation collects every problem with file/row/field paths.
// Negative cases edit a copy of the shipped content.json; the shipped file itself must report zero errors.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

static class ValidationChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool yes, string name) { if (!yes) throw new Exception("FAIL: " + name); checks++; }
        var text = File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/content.json"));
        var shipped = JObject.Parse(text);
        var baseErrors = OriginalContentValidation.Validate(shipped);
        if (baseErrors.Count > 0) Console.Error.WriteLine(OriginalContentValidation.Describe(baseErrors, 60));
        Check(baseErrors.Count == 0, "shipped content.json validates with zero errors");
        Check(new OriginalContentCatalog(text).Version == (string)shipped["version"], "shipped content loads through the throwing catalog API");

        IReadOnlyList<OriginalContentError> Errors(Action<JObject> edit, string file = "content.json")
        { var copy = (JObject)shipped.DeepClone(); edit(copy); return OriginalContentValidation.Validate(copy, file); }
        JObject Row(JObject data, string table, string id) => (JObject)((JArray)data.SelectToken(table)!).First(r => (string)r["id"]! == id);
        bool Has(IReadOnlyList<OriginalContentError> errors, string path, string words) => errors.Any(e => e.Path == path && e.Message.Contains(words));
        string Dump(IReadOnlyList<OriginalContentError> errors) => string.Join(" | ", errors.Select(e => e.ToString()));

        // Missing required field.
        var missing = Errors(d => Row(d, "cards", "strike").Remove("cost"));
        Check(missing.Count == 1 && Has(missing, "cards[strike].cost", "Missing required field 'cost'"), "missing required card field is reported at cards[strike].cost: " + Dump(missing));
        var missingEnemy = Errors(d => Row(d, "enemies", "wanderingSoldier").Remove("poiseMax"));
        Check(Has(missingEnemy, "enemies[wanderingSoldier].poiseMax", "Missing required field"), "missing required enemy field is reported with the enemy id");

        // Wrong type.
        var wrongType = Errors(d => Row(d, "cards", "strike")["cost"] = "one");
        Check(wrongType.Count == 1 && Has(wrongType, "cards[strike].cost", "got string \"one\""), "wrong card cost type is reported: " + Dump(wrongType));
        var nested = Errors(d => Row(d, "enemies", "wanderingSoldier")["moves"]!["slash"]!["damage"] = 7.5);
        Check(Has(nested, "enemies[wanderingSoldier].moves.slash.damage", "Expected integer"), "nested enemy move field is reported with its full path: " + Dump(nested));
        var hpType = Errors(d => Row(d, "enemies", "wanderingSoldier")["hp"]![1] = "lots");
        Check(Has(hpType, "enemies[wanderingSoldier].hp[1]", "Expected integer"), "array element type errors carry the index");

        // Bad enum.
        var badEnum = Errors(d => Row(d, "cards", "strike")["rarity"] = "legendary");
        Check(badEnum.Count == 1 && Has(badEnum, "cards[strike].rarity", "Expected one of [starter, common, uncommon, rare, special]"), "bad rarity enum lists the legal values: " + Dump(badEnum));
        var badTarget = Errors(d => Row(d, "cards", "strike")["effects"]![0]!["target"] = "everyone");
        Check(Has(badTarget, "cards[strike].effects[0].target", "Unknown target"), "bad effect target is reported inside the effect");
        var badOp = Errors(d => Row(d, "cards", "strike")["effects"]![0]!["op"] = "explode");
        Check(Has(badOp, "cards[strike].effects[0].op", "Unknown opcode 'explode'"), "unknown opcode is reported");

        // Dangling references.
        var dangling = Errors(d => d["encounters"]![0]!["enemies"]![0] = "missingEnemy");
        var encounterId = (string)shipped["encounters"]![0]!["id"]!;
        Check(Has(dangling, "encounters[" + encounterId + "].enemies[0]", "Unknown enemy 'missingEnemy'"), "dangling encounter enemy is reported: " + Dump(dangling));
        var status = Errors(d => Row(d, "cards", "hex")["effects"]![0]!["status"] = "notAStatus");
        Check(status.Count == 1 && Has(status, "cards[hex].effects[0].status", "Unknown status 'notAStatus'"), "dangling status in an effect is reported: " + Dump(status));
        var kit = Errors(d => d["equipment"]!["startingKits"]![0]!["rightHand"] = "missingWeapon");
        Check(kit.Any(e => e.Path.StartsWith("equipment.startingKits[") && e.Path.EndsWith("].rightHand") && e.Message.Contains("Unknown armament 'missingWeapon'")), "dangling starting-kit armament is reported");
        var profile = Errors(d => d["equipment"]!["armaments"]![0]!["attackProfile"] = "missingProfile");
        Check(profile.Any(e => e.Path.EndsWith(".attackProfile") && e.Message.Contains("missingProfile")), "dangling armament card profile is reported");

        // Several errors at once: every one is reported with its own path, none masks another.
        var many = Errors(d =>
        {
            Row(d, "cards", "strike").Remove("cost");
            Row(d, "cards", "defend")["type"] = "spell";
            Row(d, "relics", (string)d["relics"]![0]!["id"]!)["rarity"] = 3;
            Row(d, "enemies", "wanderingSoldier")["hp"] = new JArray(30, 20);
            d["classes"]![0]!["startingRelic"] = "missingRelic";
            ((JArray)d["cards"]!).Add(new JObject { ["name"] = "No Id" });
            ((JArray)d["cards"]!).Add(Row(d, "cards", "bashingBlow").DeepClone());
        }, "Mods/demo/content.json");
        var relicId = (string)shipped["relics"]![0]!["id"]!; var classId = (string)shipped["classes"]![0]!["id"]!; var count = ((JArray)shipped["cards"]!).Count;
        Check(Has(many, "cards[strike].cost", "Missing required field"), "several: missing field reported");
        Check(Has(many, "cards[defend].type", "Expected one of"), "several: bad enum reported");
        Check(Has(many, "relics[" + relicId + "].rarity", "got number 3"), "several: wrong type reported");
        Check(Has(many, "enemies[wanderingSoldier].hp", "must not exceed"), "several: inverted HP range reported");
        Check(Has(many, "classes[" + classId + "].startingRelic", "Unknown relic 'missingRelic'"), "several: dangling reference reported");
        Check(Has(many, "cards[" + count + "].id", "Missing required field 'id'"), "several: row without id is named by index");
        Check(Has(many, "cards[" + (count + 1) + "].id", "Duplicate id 'bashingBlow'"), "several: duplicate id is named by index");
        Check(many.All(e => e.File == "Mods/demo/content.json"), "several: every error names its source file");
        Check(many.Count >= 7, "several: at least seven independent errors collected (" + many.Count + ")");

        // Throwing convenience API: aggregate message, first N listed plus the count.
        var bulk = (JObject)shipped.DeepClone();
        foreach (var card in ((JArray)bulk["cards"]!).Take(30)) ((JObject)card).Remove("textTemplate");
        try { OriginalContentValidation.ThrowIfInvalid(bulk); Check(false, "invalid content must throw"); }
        catch (OriginalContentValidationException error)
        {
            Check(error.Errors.Count == 30 && error is ArgumentException, "aggregate exception keeps all 30 errors and stays an ArgumentException");
            Check(error.Message.StartsWith("Original content is invalid: 30 errors.") && error.Message.Contains("content.json: cards[strike].textTemplate: Missing required field 'textTemplate'"), "aggregate message names file, row and field");
            Check(error.Message.Split('\n').Length == OriginalContentValidation.MessageLimit + 2 && error.Message.EndsWith("… and 10 more."), "aggregate message lists the first " + OriginalContentValidation.MessageLimit + " and counts the rest");
        }

        // Catalog load keeps its earlier reference-only scope (content frozen in older saves still loads),
        // but now reports every reference problem at once.
        Check(new OriginalContentCatalog(bulk.ToString()).Table("cards").Count == ((JArray)shipped["cards"]!).Count, "catalog load does not apply the field schema (save compatibility)");
        Check(OriginalContentValidation.Validate(bulk, "content.json", OriginalValidationScope.References).Count == 0, "reference scope ignores schema-only problems");
        var refs = (JObject)shipped.DeepClone();
        refs["encounters"]![0]!["enemies"]![0] = "missingEnemy"; refs["classes"]![0]!["startingRelic"] = "missingRelic"; refs["equipment"]!["startingKits"]![0]!["rightHand"] = "missingWeapon";
        try { _ = new OriginalContentCatalog(refs.ToString(), "edited.json"); Check(false, "catalog must refuse dangling references"); }
        catch (OriginalContentValidationException error)
        {
            Check(error.Errors.Count == 3 && error.Errors.All(e => e.File == "edited.json"), "catalog load reports all three dangling references, naming the source file: " + Dump(error.Errors));
            Check(error.Message.Contains("edited.json: encounters[" + encounterId + "].enemies[0]: Unknown enemy 'missingEnemy'") && error.Message.Contains("Unknown relic 'missingRelic'") && error.Message.Contains("Unknown armament 'missingWeapon'"), "catalog aggregate message lists every reference problem");
        }

        // Garbage never crashes the validator.
        var garbage = OriginalContentValidation.Validate(new JObject { ["cards"] = "nope", ["equipment"] = new JArray() });
        Check(garbage.Any(e => e.Path == "cards" && e.Message.Contains("Expected an array")) && garbage.Any(e => e.Path == "classes" && e.Message.Contains("Missing required table")), "malformed top-level shapes are reported, not thrown");
        return checks;
    }
}
