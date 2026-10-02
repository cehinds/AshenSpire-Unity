// Owner progression checks: boundaries, conservation, immutable projection and receipts.
// The shipped rules are progression schemaVersion 2 (web ratingFormula: AR/DR/PR ratings over
// the lean scale, derived-stat ruleset 6). Saved runs still carry schemaVersion 1, so the
// original five-point rules are kept below as an inlined v1 document with their old numbers.

using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
internal static class AttributeProgressionChecks
{
    // The v1 progression document exactly as shipped before the lean rescale.
    private const string V1Json = @"{
  ""schemaVersion"": 1,
  ""baseline"": 5,
  ""damagePerPoint"": 1,
  ""healingPerPoint"": 1,
  ""healingAttribute"": ""wisdom"",
  ""damageTags"": { ""pierce"": ""dexterity"" },
  ""damageSchools"": {
    ""physical"": ""strength"",
    ""pierce"": ""dexterity"",
    ""piercing"": ""dexterity"",
    ""magic"": ""intelligence"",
    ""arcane"": ""wisdom""
  },
  ""attributes"": {
    ""strength"": { ""perPoint"": ""+1 total damage on strength attacks; +1 carry capacity."" },
    ""dexterity"": { ""perPoint"": ""+1 total damage on finesse attacks."", ""milestone"": { ""interval"": 5, ""description"": ""+1 action each turn."" } },
    ""intelligence"": { ""perPoint"": ""+1 total magic damage."", ""milestone"": { ""interval"": 5, ""description"": ""+1 card drawn each turn."" } },
    ""wisdom"": { ""perPoint"": ""+1 total arcane damage and +1 card healing."", ""milestone"": { ""interval"": 5, ""description"": ""+1 maximum mana."" } },
    ""constitution"": { ""perPoint"": ""+2 maximum health and +2 carry capacity."", ""milestone"": { ""interval"": 5, ""description"": ""+1 maximum stamina."" } }
  },
  ""derivedStats"": {
    ""rules"": {
      ""energy"": { ""base"": 1, ""pointsPerTier"": 5 },
      ""draw"": { ""base"": 3, ""pointsPerTier"": 5 }
    }
  }
}";
    internal static JObject V1Rules() => JObject.Parse(V1Json);

    internal static void Run(string root)
    {
        var catalog = new OriginalContentCatalog(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/content.json")));
        var rules = JObject.Parse(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/progression.json")));
        var checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
        void Refuses(Action action) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Invalid progression accepted"); }
        JObject Attrs(int str, int dex, int con, int wis, int intel) => new JObject { ["strength"] = str, ["dexterity"] = dex, ["constitution"] = con, ["wisdom"] = wis, ["intelligence"] = intel };

        // ---- v2: ratings over the lean scale --------------------------------------------
        Check((int)rules["schemaVersion"]! == 2, "Shipped progression is schemaVersion 2");
        var progression = new AttributeProgression(rules);
        Check(progression.DerivedLayer() == null, "v2 has no derived layer: content ruleset 6 alone owns the pools");
        var contentDerived = (JObject)catalog.Data()["derivedStatRules"]!;
        Check((int)contentDerived["rulesetVersion"]! == 6, "Derived stats use ruleset 6");
        var derived = DerivedStatCalculator.Resolve(contentDerived, progression.DerivedLayer());
        var reaver = catalog.Record("classes", "reaver");
        var ones = Attrs(1, 1, 1, 1, 1);
        // Ruleset 6 at all 1s, level 1: sum(floor(weight * stat)) over a flat base.
        var baseline = new Dictionary<string, int> { ["hp"] = 34, ["energy"] = 3, ["draw"] = 3, ["stamina"] = 1, ["mana"] = 1 };
        foreach (var pair in baseline)
        {
            var receipt = DerivedStatCalculator.Receipt(derived, pair.Key, ones, reaver, 1);
            Check((int)receipt["value"]! == pair.Value, "All-1s " + pair.Key + " = " + pair.Value + ", got " + receipt["value"]);
            Check(new[] { "id", "weights", "terms", "points", "base", "perLevel", "level", "levelBonus", "raw", "cap", "value" }.All(((JObject)receipt).ContainsKey) && ((JObject)receipt).Count == 11, "Weighted receipt shape for " + pair.Key);
            Check((int)receipt["levelBonus"]! == 0, "Level 1 carries no level bonus");
        }
        // Owner's Reaver bot row {3,1,2,1,1}: hp 30+1+8 = 39, stamina 1+0+0+1 = 2 (replay expects 49 with the medallion).
        var leanReaver = Attrs(3, 1, 2, 1, 1);
        Check((int)DerivedStatCalculator.Receipt(derived, "hp", leanReaver, reaver, 1)["value"]! == 39, "Lean Reaver hp 39");
        Check((int)DerivedStatCalculator.Receipt(derived, "stamina", leanReaver, reaver, 1)["value"]! == 2, "Lean Reaver stamina 2");
        Check((int)DerivedStatCalculator.Receipt(derived, "mana", leanReaver, reaver, 1)["value"]! == 1, "Lean Reaver mana 1");
        var hp3 = DerivedStatCalculator.Receipt(derived, "hp", ones, reaver, 3);
        Check((int)hp3["levelBonus"]! == 4 && (int)hp3["value"]! == 38, "Level 3 adds floor((3-1) * 2) = 4 health");
        for (var con = 1; con <= 4; con++)
            Check((int)DerivedStatCalculator.Receipt(derived, "hp", Attrs(1, 1, con, 1, 1), reaver, 1)["value"]! == 30 + 4 * con, "Each constitution point adds 4 health");

        // Independent rating oracle: floor(sum(floor(w * attr)) * multiplier) (web ratingFormula.js:17-35).
        var formula = (JObject)rules["ratingFormula"]!;
        int Rating(string id, JObject attributes)
        {
            var row = (JObject)formula["ratings"]![id]!; var sum = 0;
            foreach (var attr in LeanAllocation.Order) if (row[attr] != null) sum += (int)Math.Floor((double)row[attr]! * (int)attributes[attr]! + 1e-9);
            return (int)Math.Floor(sum * (double)formula["multiplier"]! + 1e-9);
        }
        foreach (var id in new[] { "ar", "dr", "pr" }) Check(Rating(id, ones) == 0, "All-1s " + id + " attribute term is zero");
        var profileRatings = (JObject)rules["profileRatings"]!;
        foreach (var profile in catalog.Table("equipment.basicCardProfiles"))
            Check(profileRatings[(string)profile["id"]!] != null, "Every card profile names a rating: " + profile["id"]);

        var composer = new WeaponCardComposer(catalog); var projection = new WeaponCardProjection(catalog);
        var baselines = progression.BaselineProfiles(catalog);
        foreach (var row in catalog.Table("equipment.basicCardProfiles"))
        {
            var over = baselines[(string)row["id"]!];
            Check(over != null && (double)over["gainPerTier"]! == 0 && (double)over["baseValue"]! == (double)row["baseValue"]!, "v2 baseline keeps " + row["id"] + " base value and removes per-tier scaling");
        }
        int EquipmentTerm(JObject resolved, string rating)
        {
            var pieceId = (string)(resolved["profileReceipt"] as JObject)?["pieceId"] ?? (string)resolved["weaponId"];
            if (pieceId == null || !(bool)rules["equipmentRatingAddend"]!) return 0;
            var piece = catalog.Record("equipment.armaments", pieceId);
            return (int?)(rating == "dr" ? piece["defenseRating"] : piece["attackRating"]) ?? 0;
        }
        foreach (var cls in catalog.Table("classes"))
        {
            var classId = (string)cls["id"]!; var loadout = new WeaponLoadout(catalog).Create(classId);
            foreach (var attributes in new[] { ones, LeanAllocation.Create(catalog, classId, progression).Attributes() })
            foreach (var instance in composer.CreateStartingDeck(loadout, classId).OfType<JObject>())
            {
                var input = projection.Resolve(instance, loadout, classId, attributes, baselines); var before = input.ToString();
                var resolved = progression.ResolveCard(input, attributes, catalog);
                Check(input.ToString() == before, "Progression mutated weapon projection");
                var card = (JObject)resolved["card"]!;
                foreach (var receipt in card["attributeProgression"]!.OfType<JObject>())
                {
                    var rating = (string)receipt["rating"]!;
                    Check((string)receipt["attribute"]! == rating && receipt["label"] != null, "Receipt names its rating");
                    Check((int)receipt["attributeTerm"]! == Rating(rating, attributes), classId + " " + card["id"] + " " + rating + " attribute term");
                    if ((string)receipt["operation"]! != "heal")
                        Check((int)receipt["equipmentTerm"]! == EquipmentTerm(resolved, rating), classId + " " + card["id"] + " equipment rating addend");
                    Check((int)receipt["bonus"]! == (int)receipt["attributeTerm"]! + (int)receipt["equipmentTerm"]!, "Rating bonus is attribute + equipment");
                    if (JToken.DeepEquals(attributes, ones)) Check((int)receipt["attributeTerm"]! == 0, "All-1s rating term is zero");
                }
                Refuses(() => progression.ResolveCard(resolved, attributes, catalog));
            }
        }

        // A magic card at INT 3 / WIS 2: PR = floor(.5*2) + floor(.75*3) = 1 + 2 = 3; heals with PR too.
        var caster = Attrs(1, 1, 1, 2, 3);
        Check(Rating("pr", caster) == 3, "Oracle PR for INT 3 / WIS 2");
        var magic = JObject.Parse("{\"card\":{\"damageSchool\":\"magic\",\"effects\":[{\"op\":\"damage\",\"amount\":3,\"hits\":2},{\"op\":\"damage\",\"amount\":2},{\"op\":\"heal\",\"amount\":1}]}}");
        var magicResult = progression.ResolveCard(magic, caster, catalog);
        var magicEffects = magicResult["card"]!["effects"]!;
        Check((int)magicEffects[0]!["attributeBonus"]! == 3 && magicEffects[1]!["attributeBonus"] == null, "Magic damage gets PR 3 once per card");
        Check((int)magicEffects[2]!["attributeBonus"]! == 3, "Magic card healing gets PR");
        var damageReceipt = magicResult["card"]!["attributeProgression"]!.First(x => (string)x["operation"]! == "damage");
        Check((string)damageReceipt["rating"]! == "pr" && (int)damageReceipt["equipmentTerm"]! == 0, "Unprojected magic card has no equipment addend");
        var arcane = JObject.Parse("{\"card\":{\"damageSchool\":\"arcane\",\"effects\":[{\"op\":\"heal\",\"amount\":1}]}}");
        Check((int)progression.ResolveCard(arcane, caster, catalog)["card"]!["effects"]![0]!["attributeBonus"]! == 3, "Arcane card healing gets PR");
        foreach (var school in new[] { "physical", "pierce" })
        {
            var mundane = JObject.Parse("{\"card\":{\"damageSchool\":\"" + school + "\",\"effects\":[{\"op\":\"damage\",\"amount\":1},{\"op\":\"heal\",\"amount\":1}]}}");
            var heal = progression.ResolveCard(mundane, caster, catalog)["card"]!["effects"]![1]!;
            Check(((int?)heal["attributeBonus"] ?? 0) == 0, school + " card healing gets no PR");
        }
        var physical = JObject.Parse("{\"card\":{\"damageSchool\":\"physical\",\"effects\":[{\"op\":\"damage\",\"amount\":1}]}}");
        Check((int)progression.ResolveCard(physical, leanReaver, catalog)["card"]!["effects"]![0]!["attributeBonus"]! == Rating("ar", leanReaver), "Physical damage uses AR");

        // Guard cards: DR lands on the first block effect only, and combat applies it once.
        var reaverLoadout = new WeaponLoadout(catalog).Create("reaver");
        var guardInstance = composer.CreateStartingDeck(reaverLoadout, "reaver").OfType<JObject>().First(x => (string)x["equipmentRole"] == "guard");
        var nimble = Attrs(1, 4, 1, 1, 1);
        var guardInput = projection.Resolve(guardInstance, reaverLoadout, "reaver", nimble, baselines);
        var guardCard = (JObject)guardInput["card"]!;
        var firstBlock = guardCard["effects"]!.OfType<JObject>().First(x => (string)x["op"] == "block");
        ((JArray)guardCard["effects"]!).Add(firstBlock.DeepClone());
        var guarded = (JObject)progression.ResolveCard(guardInput, nimble, catalog)["card"]!;
        var blocks = guarded["effects"]!.OfType<JObject>().Where(x => (string)x["op"] == "block").ToArray();
        var dr = Rating("dr", nimble) + EquipmentTerm(guardInput, "dr");
        Check(dr > 0 && (int)blocks[0]["attributeBonus"]! == dr && blocks.Skip(1).All(x => x["attributeBonus"] == null), "Guard DR bonus lands once on the first block");
        Check(guarded["attributeProgression"]!.Any(x => (string)x["operation"]! == "block" && (string)x["rating"]! == "dr"), "Guard receipt names Defense Rating");
        var combat = JObject.Parse(File.ReadAllText(Path.Combine(root, "UnityTests/Parity/combat-reference.json")));
        var fixture = combat["fixtures"]![0]!;
        var combatCatalog = new OriginalContentCatalog(combat["content"]!.ToString());
        int Block(JObject definition)
        {
            var engine = new CombatSession(combatCatalog, (JObject)combat["mechanics"]!, new RandomStreams((uint)fixture["seed"]!), (JObject)fixture["player"]!.DeepClone(),
                fixture["deck"]!.OfType<JObject>(), fixture["enemyIds"]!.Values<string>(), _ => (JObject)definition.DeepClone(), (double)fixture["hpMult"]!);
            engine.PlayCard("probe", "e1"); return (int)engine.Player["block"]!;
        }
        var plain = (JObject)guarded.DeepClone(); foreach (var effect in plain["effects"]!.OfType<JObject>()) effect.Remove("attributeBonus");
        Check(Block(guarded) - Block(plain) == dr, "Combat adds the DR bonus once across two block effects");

        // Benefits: next score at which some rating or pool steps up.
        var benefits = progression.Benefits(ones, contentDerived);
        foreach (var receipt in benefits.OfType<JObject>())
        {
            Check(new[] { "id", "points", "perPoint", "nextAt", "milestone" }.All(receipt.ContainsKey) && !receipt.ContainsKey("investment"), "v2 benefit receipt shape for " + receipt["id"]);
            Check((int)receipt["points"]! == 1 && (int)receipt["nextAt"]! == 2, receipt["id"] + " steps up at 2 from all 1s");
        }

        var copy = progression.Data(); copy["ratingFormula"]!["multiplier"] = 99;
        Check((double)progression.Data()["ratingFormula"]!["multiplier"]! == 1, "Saved rating formula must be isolated");

        var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/mechanics.json")));
        var builder = new OriginalCharacterBuilder(catalog, progression, mechanics);
        foreach (var cls in catalog.Table("classes"))
        {
            var classId = (string)cls["id"]!; var kit = (string)cls["eligibleStartingKitIds"]![0]!;
            var creation = LeanAllocation.Create(catalog, classId, progression);
            var player = builder.Build(creation, kit);
            Check((int)player["hp"]! == (int)creation.Resources()["hp"]! + (classId == "reaver" ? 10 : 0) && player["deck"]!.Count() > 0, "Completed lean creation includes the Reaver medallion's ten HP and weapon cards");
            Check(player["relicIds"]!.Count() == 1 && (string)player["relicIds"]![0]! == (string)cls["startingRelic"]!, "Starting class relic must be retained");
            creation.Select(classId, "lean"); Refuses(() => builder.Build(creation, kit));
        }
        Console.WriteLine($"Owner attribute progression (v2): {checks} checks passed");

        // ---- v1: saved runs keep the original five-point rules ---------------------------
        var legacy = new AttributeProgression(V1Rules());
        var attrs = Attrs(5, 5, 5, 5, 5);
        Check(JToken.DeepEquals(legacy.DerivedLayer(), V1Rules()["derivedStats"]), "v1 derived layer unchanged");
        foreach (var stat in attrs.Properties().Select(x => x.Name).ToArray())
        {
            for (var score = 5; score < 30; score++)
            {
                var sample = (JObject)attrs.DeepClone(); sample[stat] = score;
                var receipt = legacy.Benefits(sample).First(x => (string)x["id"]! == stat);
                Check((int)receipt["investment"]! == score - 5, "Every point needs a visible investment receipt");
                if (stat != "strength") Check((int)receipt["nextAt"]! == (score / 5 + 1) * 5, "Incorrect next milestone");
            }
        }
        for (var bonus = 0; bonus <= 60; bonus++) for (var hits = 1; hits <= 8; hits++)
        {
            var values = Enumerable.Range(0, hits).Select(hit => AttributeProgression.BonusForHit(bonus, hit, hits)).ToArray();
            Check(values.Sum() == bonus, "Multi-hit card multiplied or lost investment");
            Check(values.Max() - values.Min() <= 1, "Bonus was distributed unevenly");
        }
        foreach (var cls in catalog.Table("classes"))
        {
            var classId = (string)cls["id"]!; var loadout = new WeaponLoadout(catalog).Create(classId);
            foreach (var instance in composer.CreateStartingDeck(loadout, classId).OfType<JObject>())
            {
                var input = projection.Resolve(instance, loadout, classId, attrs, legacy.BaselineProfiles(catalog)); var before = input.ToString();
                var resolved = legacy.ResolveCard(input, attrs, catalog);
                Check(input.ToString() == before, "Progression mutated weapon projection");
                Check(resolved["card"]!["attributeProgression"]!.All(x => (int)x["bonus"]! == 0), "Baseline should receive no extra bonus");
                Refuses(() => legacy.ResolveCard(resolved, attrs, catalog));
            }
        }
        var attack = JObject.Parse("{\"card\":{\"damageSchool\":\"magic\",\"effects\":[{\"op\":\"damage\",\"amount\":3,\"hits\":2},{\"op\":\"damage\",\"amount\":2},{\"op\":\"heal\",\"amount\":1}]}}");
        attrs["intelligence"] = 11; attrs["wisdom"] = 7;
        var result = legacy.ResolveCard(attack, attrs, catalog);
        Check((int)result["card"]!["effects"]![0]!["attributeBonus"]! == 6 && result["card"]!["effects"]![1]!["attributeBonus"] == null, "Apply offensive benefit only once per card");
        Check((int)result["card"]!["effects"]![2]!["attributeBonus"]! == 2, "Wisdom should improve card healing");
        Refuses(() => AttributeProgression.BonusForHit(-1, 0, 1));
        Refuses(() => AttributeProgression.BonusForHit(1, 1, 1));
        var legacyCopy = legacy.Data(); legacyCopy["baseline"] = 99; Check((int)legacy.Data()["baseline"]! == 5, "Saved tuning must be isolated");

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
        Console.WriteLine($"Owner attribute progression (v2 + v1 saves): {checks} checks passed");
    }
}
