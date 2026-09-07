// Owner progression checks: boundaries, conservation, immutable projection and receipts.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
internal static class AttributeProgressionChecks
{
    internal static void Run(string root)
    {
        var catalog = new OriginalContentCatalog(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/content.json")));
        var rules = JObject.Parse(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/progression.json")));
        var progression = new AttributeProgression(rules); var checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
        void Refuses(Action action) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Invalid progression accepted"); }
        var attrs = JObject.FromObject(new { strength = 5, dexterity = 5, intelligence = 5, wisdom = 5, constitution = 5 });
        var derived = DerivedStatCalculator.Resolve((JObject)catalog.Data()["derivedStatRules"]!, progression.DerivedLayer());
        var hero = catalog.Record("classes", "reaver");
        foreach (var stat in attrs.Properties().Select(x => x.Name).ToArray())
        {
            for (var score = 5; score < 30; score++)
            {
                var sample = (JObject)attrs.DeepClone(); sample[stat] = score;
                var receipt = progression.Benefits(sample).First(x => (string)x["id"]! == stat);
                Check((int)receipt["investment"]! == score - 5, "Every point needs a visible investment receipt");
                if (stat != "strength") Check((int)receipt["nextAt"]! == (score / 5 + 1) * 5, "Incorrect next milestone");
                if (stat == "constitution") Check((int)DerivedStatCalculator.Receipt(derived, "hp", sample, hero)["value"]! == 30 + 2 * score, "Health must improve each point");
                var resource = stat == "dexterity" ? "energy" : stat == "intelligence" ? "draw" : stat == "wisdom" ? "mana" : stat == "constitution" ? "stamina" : null;
                if (resource != null) Check((int)DerivedStatCalculator.Receipt(derived, resource, sample, hero)["value"]! == score / 5 + (resource == "energy" ? 1 : resource == "draw" ? 3 : 0), "Mechanical benefit must occur every five points");
            }
        }
        for (var bonus = 0; bonus <= 60; bonus++) for (var hits = 1; hits <= 8; hits++)
        {
            var values = Enumerable.Range(0, hits).Select(hit => AttributeProgression.BonusForHit(bonus, hit, hits)).ToArray();
            Check(values.Sum() == bonus, "Multi-hit card multiplied or lost investment");
            Check(values.Max() - values.Min() <= 1, "Bonus was distributed unevenly");
        }
        var composer = new WeaponCardComposer(catalog); var projection = new WeaponCardProjection(catalog);
        foreach (var cls in catalog.Table("classes"))
        {
            var classId = (string)cls["id"]!; var loadout = new WeaponLoadout(catalog).Create(classId);
            foreach (var instance in composer.CreateStartingDeck(loadout, classId).OfType<JObject>())
            {
                var input = projection.Resolve(instance, loadout, classId, attrs, progression.BaselineProfiles(catalog)); var before = input.ToString();
                var resolved = progression.ResolveCard(input, attrs, catalog);
                Check(input.ToString() == before, "Progression mutated weapon projection");
                Check(resolved["card"]!["attributeProgression"]!.All(x => (int)x["bonus"]! == 0), "Baseline should receive no extra bonus");
                Refuses(() => progression.ResolveCard(resolved, attrs, catalog));
            }
        }
        var attack = JObject.Parse("{\"card\":{\"damageSchool\":\"magic\",\"effects\":[{\"op\":\"damage\",\"amount\":3,\"hits\":2},{\"op\":\"damage\",\"amount\":2},{\"op\":\"heal\",\"amount\":1}]}}");
        attrs["intelligence"] = 11; attrs["wisdom"] = 7;
        var result = progression.ResolveCard(attack, attrs, catalog);
        Check((int)result["card"]!["effects"]![0]!["attributeBonus"]! == 6 && result["card"]!["effects"]![1]!["attributeBonus"] == null, "Apply offensive benefit only once per card");
        Check((int)result["card"]!["effects"]![2]!["attributeBonus"]! == 2, "Wisdom should improve card healing");
        Refuses(() => AttributeProgression.BonusForHit(-1, 0, 1));
        Refuses(() => AttributeProgression.BonusForHit(1, 1, 1));
        var copy = progression.Data(); copy["baseline"] = 99; Check((int)progression.Data()["baseline"]! == 5, "Saved tuning must be isolated");
        var storage = new Dictionary<string,string>(); var writes = 0;
        var journal = new OriginalSaveJournal("native", key => storage.GetValueOrDefault(key, ""), (key, value) => storage[key] = value, () => writes++);
        Check(!journal.HasSave, "Empty journal should not offer Continue");
        journal.Save(JObject.Parse("{\"turn\":1}")); journal.Save(JObject.Parse("{\"turn\":2}"));
        Check(writes == 2 && (int)journal.Load(_ => {}, out var recovered)["turn"]! == 2 && !recovered, "Current command boundary must restore");
        storage["native"] = "corrupted";
        Check((int)journal.Load(_ => {}, out recovered)["turn"]! == 1 && recovered && storage["native"] == "corrupted", "Backup restores without deleting evidence");
        journal.Save(JObject.Parse("{\"turn\":3}"));
        Check(JObject.Parse(storage["native.backup"])["data"]!.ToString().Contains("1"), "A corrupt primary must not overwrite a valid backup");
        Check((int)journal.Load(candidate => { if ((int)candidate["turn"]! == 3) throw new ArgumentException("Unsupported snapshot"); }, out recovered)["turn"]! == 1 && recovered, "Semantic validation should fall back too");
        var builder = new OriginalCharacterBuilder(catalog, progression, JObject.Parse(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/mechanics.json"))));
        foreach (var cls in catalog.Table("classes"))
        {
            var creation = new CreationModel(catalog, (string)cls["id"]!, "standard", progression); var kit = (string)cls["eligibleStartingKitIds"]![0]!;
            var player = builder.Build(creation, kit);
            Check((int)player["hp"]! == (int)creation.Resources()["hp"]! + ((string)cls["id"]! == "reaver" ? 10 : 0) && player["deck"]!.Count() > 0, "Completed creation includes the Reaver medallion's ten HP and weapon cards");
            Check(player["relicIds"]!.Count() == 1 && (string)player["relicIds"]![0]! == (string)cls["startingRelic"]!, "Starting class relic must be retained");
            creation.Select((string)cls["id"]!, "pointbuy"); Refuses(() => builder.Build(creation, kit));
        }
        Console.WriteLine($"Owner attribute progression: {checks} checks passed");
    }
}
