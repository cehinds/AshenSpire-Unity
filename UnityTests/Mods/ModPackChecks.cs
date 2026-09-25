// ModPackChecks.cs — OriginalModPacks against the shipped content.json and in-memory packs.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

static class ModPackChecks
{
    public static void Run(string root, Action<bool, string> Check)
    {
        var baseJson = File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/content.json"));
        var baseCatalog = new OriginalContentCatalog(baseJson);
        var baseData = baseCatalog.Data();
        var baseCards = baseCatalog.Table("cards");
        JObject Card(string id) => (JObject)baseCards.First(x => (string)x["id"] == id).DeepClone();
        string Manifest(string id, int loadOrder = 0, string[] dependsOn = null, string gameVersionMin = null)
        {
            var json = new JObject { ["id"] = id, ["name"] = "Pack " + id, ["version"] = "1.0.0", ["loadOrder"] = loadOrder, ["dependsOn"] = new JArray(dependsOn ?? new string[0]) };
            if (gameVersionMin != null) json["gameVersionMin"] = gameVersionMin;
            return json.ToString();
        }
        string Cards(params JObject[] records) => new JObject { ["cards"] = new JArray(records) }.ToString();
        JObject Damage(string id, int amount) { var card = Card(id); card["effects"]![0]!["amount"] = amount; return card; }
        int DamageOf(OriginalModLoadResult result, string id) => (int)result.Catalog.Record("cards", id)["effects"]![0]!["amount"]!;
        bool Has(OriginalModLoadResult result, string code, string mod) => result.Errors.Any(e => e.Code == code && e.ModId == mod);
        bool Unchanged(OriginalModLoadResult result) => JToken.DeepEquals(result.Catalog.Data(), baseData) && result.ContentJson == baseJson && result.Changes.Count == 0;

        // Zero mods: record- and byte-identical to today's catalog.
        var none = OriginalModPacks.Load(baseJson, null);
        Check(none.Succeeded && none.Loaded.Count == 0 && ReferenceEquals(none.ContentJson, baseJson), "zero mods: content text is the base string, unchanged");
        Check(JToken.DeepEquals(none.Catalog.Data(), baseData) && none.Catalog.Data().ToString() == baseData.ToString(), "zero mods: catalog records identical to base catalog");
        Check(none.Catalog.Version == baseCatalog.Version && none.Catalog.Table("cards").Count == baseCards.Count, "zero mods: version and card count identical");
        var emptyFolder = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Other/readme.txt", "x"));
        Check(emptyFolder.Succeeded && Unchanged(emptyFolder), "missing Mods folder behaves exactly like zero mods");
        Check(Unchanged(OriginalModPacks.Load(baseJson, new OriginalModDirectorySource(Path.Combine(root, "does-not-exist")))), "missing disk folder behaves exactly like zero mods");

        // Add.
        var ember = Card("bashingBlow"); ember["id"] = "testEmber"; ember["name"] = "Test Ember";
        var add = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/adder/mod.json", Manifest("adder")).Add("Mods/adder/cards.json", Cards(ember)));
        Check(add.Succeeded && add.Catalog.Table("cards").Count == baseCards.Count + 1, "add: new card id appended");
        Check((string)add.Catalog.Record("cards", "testEmber")["name"] == "Test Ember" && (string)add.Catalog.Table("cards").Last()["id"] == "testEmber", "add: record readable through the existing catalog API");
        Check(add.Changes.SequenceEqual(new[] { "adder add cards/testEmber" }), "add: change receipt names the record");
        Check(add.Catalog.Tags("card", add.Catalog.Record("cards", "testEmber")).Length == 0, "add: untagged new card is accepted by the tag index");

        // Override.
        var over = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/tuner/mod.json", Manifest("tuner")).Add("Mods/tuner/cards.json", Cards(Damage("shieldBash", 11))));
        var index = baseCards.Select(x => (string)x["id"]).ToList().IndexOf("shieldBash");
        Check(over.Succeeded && DamageOf(over, "shieldBash") == 11 && over.Catalog.Table("cards").Count == baseCards.Count, "override: same id replaces the record, count unchanged");
        Check((string)over.Catalog.Table("cards")[index]["id"] == "shieldBash", "override: record keeps its original position");
        Check(over.Changes.SequenceEqual(new[] { "tuner override cards/shieldBash" }), "override: change receipt names the record");
        var others = over.Catalog.Data(); ((JArray)others["cards"]!)[index] = baseCards[index].DeepClone();
        Check(JToken.DeepEquals(others, baseData), "override: nothing else in the catalog changes");
        var relic = (JObject)baseCatalog.Table("relics")[0].DeepClone();
        var relicOver = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/r/mod.json", Manifest("r")).Add("Mods/r/relics.json", new JObject { ["relics"] = new JArray(relic) }.ToString()));
        Check(relicOver.Succeeded && relicOver.Changes.Single() == "r override relics/" + relic["id"], "override works for other tables (relics)");

        // Remove: an unreferenced card succeeds; a referenced one is refused by the existing validation.
        var free = baseCards.Select(x => (string)x["id"]!).First(id => baseJson.Split("\"" + id + "\"").Length == 2);
        var remove = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/trim/mod.json", Manifest("trim")).Add("Mods/trim/remove.json", new JObject { ["remove"] = new JObject { ["cards"] = new JArray(free) } }.ToString()));
        Check(remove.Succeeded && remove.Catalog.Table("cards").Count == baseCards.Count - 1 && remove.Catalog.Table("cards").All(x => (string)x["id"] != free), "remove: unreferenced card (" + free + ") is removed");
        var removeUsed = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/trim/mod.json", Manifest("trim")).Add("Mods/trim/remove.json", new JObject { ["remove"] = new JObject { ["cards"] = new JArray("shieldBash") } }.ToString()));
        Check(Has(removeUsed, OriginalModErrorCodes.ValidationFailed, "trim") && Unchanged(removeUsed), "remove: card still used by a class is refused, catalog untouched");
        var removeMissing = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/trim/mod.json", Manifest("trim")).Add("Mods/trim/remove.json", new JObject { ["remove"] = new JObject { ["cards"] = new JArray("noSuchCard") } }.ToString()));
        Check(Has(removeMissing, OriginalModErrorCodes.RemoveUnknown, "trim") && Unchanged(removeMissing), "remove: unknown id is reported");

        // Load order: (loadOrder, id), dependencies first; later packs win.
        var ordered = new OriginalModMemorySource()
            .Add("Mods/b-late/mod.json", Manifest("b-late", 10)).Add("Mods/b-late/cards.json", Cards(Damage("shieldBash", 30)))
            .Add("Mods/a-late/mod.json", Manifest("a-late", 10)).Add("Mods/a-late/cards.json", Cards(Damage("shieldBash", 20)))
            .Add("Mods/z-early/mod.json", Manifest("z-early", -5)).Add("Mods/z-early/cards.json", Cards(Damage("shieldBash", 10)));
        var order = OriginalModPacks.Load(baseJson, ordered);
        Check(order.Succeeded && order.Loaded.Select(x => x.Id).SequenceEqual(new[] { "z-early", "a-late", "b-late" }), "load order: loadOrder ascending, then id");
        Check(DamageOf(order, "shieldBash") == 30, "load order: last applied override wins");
        var again = OriginalModPacks.Load(baseJson, ordered);
        Check(again.ContentJson == order.ContentJson && again.Changes.SequenceEqual(order.Changes), "load order: repeat loads are identical");
        var dependent = OriginalModPacks.Load(baseJson, new OriginalModMemorySource()
            .Add("Mods/base-pack/mod.json", Manifest("base-pack", 50)).Add("Mods/base-pack/cards.json", Cards(Damage("shieldBash", 40)))
            .Add("Mods/addon/mod.json", Manifest("addon", 0, new[] { "base-pack" })).Add("Mods/addon/cards.json", Cards(Damage("shieldBash", 41))));
        Check(dependent.Succeeded && dependent.Loaded.Select(x => x.Id).SequenceEqual(new[] { "base-pack", "addon" }) && DamageOf(dependent, "shieldBash") == 41, "load order: a dependency loads before its dependent despite loadOrder");

        // Dependencies.
        var missing = OriginalModPacks.Load(baseJson, new OriginalModMemorySource()
            .Add("Mods/needy/mod.json", Manifest("needy", 0, new[] { "absent" })).Add("Mods/needy/cards.json", Cards(Damage("shieldBash", 12)))
            .Add("Mods/chain/mod.json", Manifest("chain", 0, new[] { "needy" }))
            .Add("Mods/fine/mod.json", Manifest("fine")).Add("Mods/fine/cards.json", Cards(Damage("bashingBlow", 13))));
        Check(Has(missing, OriginalModErrorCodes.MissingDependency, "needy") && Has(missing, OriginalModErrorCodes.DependencyRejected, "chain"), "missing dependency: pack and its dependents are refused");
        Check(missing.Loaded.Select(x => x.Id).SequenceEqual(new[] { "fine" }) && DamageOf(missing, "shieldBash") == 5 && DamageOf(missing, "bashingBlow") == 13, "missing dependency: unrelated packs still load");
        var cycle = OriginalModPacks.Load(baseJson, new OriginalModMemorySource()
            .Add("Mods/a/mod.json", Manifest("a", 0, new[] { "b" })).Add("Mods/b/mod.json", Manifest("b", 0, new[] { "a" }))
            .Add("Mods/c/mod.json", Manifest("c", 0, new[] { "a" })).Add("Mods/self/mod.json", Manifest("self", 0, new[] { "self" })));
        Check(Has(cycle, OriginalModErrorCodes.DependencyCycle, "a") && Has(cycle, OriginalModErrorCodes.DependencyCycle, "b") && Has(cycle, OriginalModErrorCodes.DependencyCycle, "self"), "cycle: every pack in a cycle is refused");
        Check(cycle.Errors.Any(e => e.ModId == "a" && e.Message.Contains("a -> b -> a")) && Has(cycle, OriginalModErrorCodes.DependencyRejected, "c"), "cycle: path is reported and packs behind it are refused");
        Check(cycle.Loaded.Count == 0 && Unchanged(cycle), "cycle: catalog untouched");

        // Validation reuse: bad references and shapes.
        var badRef = Card("hex"); badRef["effects"]![0]!["status"] = "notAStatus";
        var refResult = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/bad/mod.json", Manifest("bad")).Add("Mods/bad/cards.json", Cards(badRef)));
        Check(Has(refResult, OriginalModErrorCodes.ValidationFailed, "bad") && refResult.Errors.Single().Message.Contains("Unknown status") && Unchanged(refResult), "bad reference: unknown status id refused by existing catalog validation");
        var hero = (JObject)baseCatalog.Table("classes")[0].DeepClone(); ((JArray)hero["cardPool"]!).Add("ghostCard");
        var poolResult = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/pool/mod.json", Manifest("pool")).Add("Mods/pool/classes.json", new JObject { ["classes"] = new JArray(hero) }.ToString()));
        Check(Has(poolResult, OriginalModErrorCodes.ValidationFailed, "pool") && poolResult.Errors[0].Message.Contains("ghostCard"), "bad reference: class card pool naming an unknown card is refused");
        var noCost = Card("strike"); noCost.Remove("cost"); noCost["id"] = "costless"; var wrongType = Card("defend"); wrongType["effects"] = "block 5";
        var schema = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/shape/mod.json", Manifest("shape")).Add("Mods/shape/cards.json", Cards(noCost, wrongType)));
        Check(schema.Errors.Count(e => e.Code == OriginalModErrorCodes.SchemaMismatch) == 2 && schema.Errors.Any(e => e.Message.Contains("missing 'cost'")) && schema.Errors.Any(e => e.Message.Contains("'effects' should be array")), "schema: missing and mistyped fields are both reported");
        var tables = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/t/mod.json", Manifest("t")).Add("Mods/t/x.json", "{\"widgets\":[],\"tagging\":[]}"));
        Check(tables.Errors.Count(e => e.Code == OriginalModErrorCodes.UnknownTable) == 2, "schema: unknown and id-less tables are refused");
        var dup = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/d/mod.json", Manifest("d")).Add("Mods/d/a.json", Cards(Card("strike"))).Add("Mods/d/b.json", Cards(Card("strike"))));
        Check(Has(dup, OriginalModErrorCodes.DuplicateRecord, "d"), "schema: the same id twice in one pack is refused");
        var nested = (JObject)baseCatalog.Table("equipment.armaments")[0].DeepClone();
        var nestedResult = OriginalModPacks.Load(baseJson, new OriginalModMemorySource().Add("Mods/n/mod.json", Manifest("n")).Add("Mods/n/eq.json", new JObject { ["equipment.armaments"] = new JArray(nested) }.ToString()));
        Check(nestedResult.Succeeded && nestedResult.Changes.Single() == "n override equipment.armaments/" + nested["id"], "nested tables (equipment.armaments) are moddable");

        // Version gate and manifest checks.
        var gate = new OriginalModMemorySource()
            .Add("Mods/future/mod.json", Manifest("future", 0, null, "99.0")).Add("Mods/now/mod.json", Manifest("now", 0, null, baseCatalog.Version))
            .Add("Mods/older/mod.json", Manifest("older", 0, null, "0.1"));
        var gated = OriginalModPacks.Load(baseJson, gate);
        Check(Has(gated, OriginalModErrorCodes.GameVersionTooOld, "future") && gated.Loaded.Select(x => x.Id).SequenceEqual(new[] { "now", "older" }), "version gate: newer gameVersionMin refused, equal/older accepted");
        Check(OriginalModPacks.Load(baseJson, gate, "Mods", "100.0").Loaded.Count == 3, "version gate: explicit game version is honoured");
        Check(OriginalModPacks.TryCompareVersions("0.10", "0.9.9", out var cmp) && cmp > 0 && !OriginalModPacks.TryCompareVersions("1.x", "1", out _), "version compare is numeric per part and refuses junk");
        var manifests = OriginalModPacks.Load(baseJson, new OriginalModMemorySource()
            .Add("Mods/nomanifest/cards.json", Cards(Card("strike")))
            .Add("Mods/wrong-folder/mod.json", Manifest("other"))
            .Add("Mods/broken/mod.json", "{ not json")
            .Add("Mods/extra/mod.json", new JObject { ["id"] = "extra", ["name"] = "x", ["version"] = "1", ["surprise"] = 1 }.ToString())
            .Add("Mods/badver/mod.json", new JObject { ["id"] = "badver", ["name"] = "x", ["version"] = "v1" }.ToString())
            .Add("Mods/Upper/mod.json", Manifest("Upper")));
        Check(Has(manifests, OriginalModErrorCodes.ManifestMissing, "nomanifest") && Has(manifests, OriginalModErrorCodes.ManifestInvalid, "wrong-folder") && Has(manifests, OriginalModErrorCodes.JsonInvalid, "broken"), "manifest: missing, mismatched id and malformed JSON reported");
        Check(Has(manifests, OriginalModErrorCodes.ManifestInvalid, "extra") && Has(manifests, OriginalModErrorCodes.ManifestInvalid, "badver") && Has(manifests, OriginalModErrorCodes.ManifestInvalid, "Upper"), "manifest: unknown field, bad version and bad id reported");
        Check(manifests.Loaded.Count == 0 && manifests.Rejected.Count == 6 && Unchanged(manifests), "manifest: every refused pack leaves the catalog untouched");
        var garbage = OriginalModPacks.Load("{ nope", null);
        Check(!garbage.Succeeded && garbage.Catalog == null && garbage.Errors.Single().Code == OriginalModErrorCodes.BaseContentInvalid, "invalid base content is reported, not thrown");
        var partial = OriginalModPacks.Load(baseJson, new OriginalModMemorySource()
            .Add("Mods/good/mod.json", Manifest("good")).Add("Mods/good/cards.json", Cards(Damage("shieldBash", 7)))
            .Add("Mods/zbad/mod.json", Manifest("zbad")).Add("Mods/zbad/cards.json", Cards(badRef)));
        Check(!partial.Succeeded && partial.Loaded.Single().Id == "good" && partial.Rejected.Single() == "zbad" && DamageOf(partial, "shieldBash") == 7, "one bad pack does not block good packs");

        // The shipped sample pack, from disk and from memory.
        var streaming = Path.Combine(root, "Unity/Assets/StreamingAssets");
        var sample = OriginalModPacks.Load(baseJson, new OriginalModDirectorySource(streaming));
        Check(sample.Succeeded && sample.Loaded.Single().Id == "sample-ember-pack" && sample.Loaded[0].LoadOrder == 100, "sample pack loads from StreamingAssets/Mods on disk (.meta files ignored)");
        Check(sample.Changes.SequenceEqual(new[] { "sample-ember-pack add cards/emberBrand", "sample-ember-pack override cards/shieldBash" }), "sample pack: one new card and one balance override");
        Check(DamageOf(sample, "shieldBash") == 6 && (string)sample.Catalog.Record("cards", "emberBrand")["effects"]![1]!["status"] == "burn", "sample pack: values visible through the catalog");
        var copy = new OriginalModMemorySource(); var packDir = Path.Combine(streaming, "Mods/sample-ember-pack");
        foreach (var file in Directory.GetFiles(packDir).Where(f => !f.EndsWith(".meta"))) copy.Add("Mods/sample-ember-pack/" + Path.GetFileName(file), File.ReadAllText(file));
        Check(OriginalModPacks.Load(baseJson, copy).ContentJson == sample.ContentJson, "sample pack: memory and disk sources give the same result");
        Check(new OriginalContentCatalog(sample.ContentJson).Table("cards").Count == baseCards.Count + 1, "merged ContentJson rebuilds an equal catalog");
    }
}
