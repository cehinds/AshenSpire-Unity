// OwnerCreationChecks.cs — explicit fork settings, separate from the original oracle.
// Exercises the full allocation transaction for every class, including bounds,
// exhaustion, refund, mode changes and rejected inputs. Update only for owner rules.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

// Owner, 2026-09-24: "I'd like everyone to have low stats 1's in most stats, and starseer to have a 3 in
// int"; creation "should have the option of standard (pre assigned class presets) and assign points (x points
// to assign but configurable in advanced settings)". Two modes on the lean scale (baseline 1, 1–4, total 8):
//   Standard ("leanStandard") — these per-class rows (web src/model/attributes.js:171-174), nothing unspent;
//   Assign points ("lean")    — all 1s with bonusPool (3) to place.
// Order: str, dex, con, wis, int.
internal static class LeanAllocation
{
    internal const string StandardMode = "leanStandard";
    internal static readonly string[] Order = { "strength", "dexterity", "constitution", "wisdom", "intelligence" };
    internal static readonly Dictionary<string, int[]> Targets = new()
    {
        ["reaver"] = new[] { 3, 1, 2, 1, 1 },
        ["starseer"] = new[] { 1, 1, 1, 2, 3 },
        ["herald"] = new[] { 1, 1, 2, 3, 1 },
        ["rogue"] = new[] { 1, 3, 2, 1, 1 },
    };
    // The Standard preset, as a bot or a player who accepts it begins.
    internal static CreationModel Create(OriginalContentCatalog catalog, string classId, AttributeProgression progression = null)
    {
        var creation = new CreationModel(catalog, classId, StandardMode, progression);
        if (!creation.CanBegin || !Matches(creation)) throw new Exception("Standard preset for " + classId + " is not the class row");
        return creation;
    }
    // Assign points: Adjust(id, +1) (target - 1) times from all 1s, exactly as a player would.
    internal static void Spend(CreationModel creation)
    {
        var targets = Targets[creation.ClassId];
        for (var i = 0; i < Order.Length; i++)
            for (var n = 1; n < targets[i]; n++)
                if (!creation.Adjust(Order[i], 1)) throw new Exception("Lean allocation refused " + creation.ClassId + " " + Order[i]);
        if (!creation.CanBegin) throw new Exception("Lean allocation left points unspent for " + creation.ClassId);
    }
    internal static bool Matches(CreationModel creation) => Order.Select((id, i) => (int)creation.Attributes()[id]! == Targets[creation.ClassId][i]).All(x => x);
}

internal static class OwnerCreationChecks
{
    internal static void Run(OriginalContentCatalog catalog)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var data = catalog.Data();
        var standardId = LeanAllocation.StandardMode;
        Check((string)data["attributeRules"]!["defaultMode"]! == standardId, "Standard (leanStandard) must be the default");
        Check(catalog.Table("creationModes").All(row => (string)row["id"]! != "tuned"), "Tuned is still available");
        // Two modes are offered, Standard first; the legacy ids stay in the table so existing saves resolve them.
        var visible = CreationModel.VisibleModes(catalog);
        Check(visible.Select(row => (string)row["id"]!).SequenceEqual(new[] { standardId, "lean" }), "Standard and Assign points are the offered modes, in that order");
        Check((string)visible[0]["label"]! == "Standard" && (string)visible[1]["label"]! == "Assign points", "Offered labels are Standard and Assign points");
        var labels = catalog.Table("creationModes").Select(row => (string)row["label"]!).ToList();
        Check(labels.Distinct().Count() == labels.Count, "No two creation modes share a label");
        var pointbuy = catalog.Table("creationModes").FirstOrDefault(row => (string)row["id"]! == "pointbuy");
        Check(pointbuy != null && (string)pointbuy["label"]! == "Assign points (legacy)", "The old pointbuy mode stays resolvable for existing saves");
        var legacyStandard = catalog.Table("creationModes").FirstOrDefault(row => (string)row["id"]! == "standard");
        Check(legacyStandard != null && (string)legacyStandard["label"]! == "Standard (legacy)", "The old standard mode stays resolvable for existing saves");
        foreach (var hero in catalog.Table("classes"))
        {
            var id = (string)hero["id"]!;
            // Standard: the class row, 8 points, nothing unspent, can Begin immediately, still editable.
            var standard = new CreationModel(catalog, id, standardId);
            Check(LeanAllocation.Matches(standard) && standard.TotalPoints == 8 && standard.Remaining == 0 && standard.CanBegin, "Standard " + id + " must open on its class preset with nothing unspent");
            var stdAttributes = LeanAllocation.Order;
            var high = stdAttributes.First(a => (int)standard.Attributes()[a]! > 1);
            var low = stdAttributes.First(a => (int)standard.Attributes()[a]! < 4 && a != high);
            Check(standard.Adjust(high, -1) && standard.Remaining == 1 && !standard.CanBegin, "Standard lets a point come back");
            Check(standard.Adjust(low, 1) && standard.CanBegin, "Standard lets the point move");
            Check(!standard.CanAdjust(stdAttributes.First(a => (int)standard.Attributes()[a]! < 4), 1), "Standard cannot overspend");
            // Assign points: all 1s with 3 to place.
            var creation = new CreationModel(catalog, id, "lean");
            var attributes = creation.Attributes().Properties().Select(row => row.Name).ToArray();
            Check(attributes.Length == 5 && creation.Attributes().Properties().All(row => (int)row.Value == 1), "All attributes must start at one");
            Check(creation.TotalPoints == 8 && creation.Remaining == 3 && !creation.CanBegin, "8 - (5 * 1) must leave 3 unspent");
            var changed = 0; creation.Changed += () => changed++;
            foreach (var attribute in attributes)
                Check(!creation.CanAdjust(attribute, -1) && !creation.Adjust(attribute, -1), "Minimum allowed an underflow");
            Check(changed == 0 && creation.Remaining == 3, "Rejected minimum edit changed state");
            for (var i = 0; i < 3; i++) Check(creation.Adjust(attributes[0], 1), "Legal point assignment failed");
            Check((int)creation.Attributes()[attributes[0]]! == 4 && !creation.CanAdjust(attributes[0], 1) && !creation.Adjust(attributes[0], 1), "Maximum of four allowed an overflow");
            Check(creation.Remaining == 0 && creation.CanBegin && changed == 3, "Fully assigned state is incorrect");
            Check(!creation.CanAdjust(attributes[1], 1) && !creation.Adjust(attributes[1], 1) && changed == 3, "Allocation overspent its budget");
            Check(creation.Adjust(attributes[0], -1) && creation.Remaining == 1 && !creation.CanBegin, "Refund did not return one point");
            Check(creation.Adjust(attributes[1], 1) && creation.Remaining == 0 && creation.CanBegin, "Refunded point could not move between attributes");
            creation.Select(id, "pointbuy");
            Check(creation.Remaining == 35 && creation.TotalPoints == 60 && creation.Attributes().Properties().All(row => (int)row.Value == 5), "Legacy pointbuy presets changed");
            creation.Select(id, "standard");
            Check(creation.Remaining == 0 && creation.CanBegin && creation.TotalPoints == 55, "Legacy standard presets changed");
            creation.Select(id, standardId);
            Check(LeanAllocation.Matches(creation) && creation.CanBegin, "Switching to Standard loads the class preset");
            creation.Select(id, "lean");
            Check(creation.Remaining == 3 && creation.Attributes().Properties().All(row => (int)row.Value == 1), "Returning to Assign points did not reset to all ones");
            var before = creation.Attributes();
            try { creation.Select(id, "tuned"); throw new Exception("Removed Tuned mode accepted"); }
            catch (ArgumentException) { Check(JToken.DeepEquals(before, creation.Attributes()) && creation.ModeId == "lean", "Rejected mode changed creation"); }
            var bot = new CreationModel(catalog, id, "lean"); LeanAllocation.Spend(bot);
            Check(bot.CanBegin && LeanAllocation.Matches(bot), "Assigning the class row by hand reaches the Standard preset");
        }
        // The owner's two dials: bonusPool on each mode (content.json creationModes). A changed Standard pool
        // fits its presets: a larger pool leaves the extra unspent; a smaller one trims the highest stat,
        // keeping the class's primary stat last.
        JObject Pool(string modeId, int pool) { var edited = catalog.Data(); ((JArray)edited["creationModes"]!).First(r => (string)r["id"]! == modeId)["bonusPool"] = pool; return edited; }
        var five = new OriginalContentCatalog(Pool(standardId, 5).ToString());
        var bigger = new CreationModel(five, "starseer", standardId);
        Check(bigger.TotalPoints == 10 && bigger.Remaining == 2 && !bigger.CanBegin && (int)bigger.Attributes()["intelligence"]! == 3, "Standard pool 5 leaves two to assign on top of the preset");
        var one = new OriginalContentCatalog(Pool(standardId, 1).ToString());
        foreach (var (cls, expected) in new[] { ("reaver", new[] { 2, 1, 1, 1, 1 }), ("starseer", new[] { 1, 1, 1, 1, 2 }), ("herald", new[] { 1, 1, 1, 2, 1 }), ("rogue", new[] { 1, 2, 1, 1, 1 }) })
        {
            var smaller = new CreationModel(one, cls, standardId);
            Check(smaller.Remaining == 0 && smaller.CanBegin && LeanAllocation.Order.Select((a, i) => (int)smaller.Attributes()[a]! == expected[i]).All(x => x), "Standard pool 1 trims " + cls + " to " + string.Join(",", expected) + " (got " + smaller.Attributes().ToString(Newtonsoft.Json.Formatting.None) + ")");
        }
        var assignFive = new CreationModel(new OriginalContentCatalog(Pool("lean", 5).ToString()), "rogue", "lean");
        Check(assignFive.TotalPoints == 10 && assignFive.Remaining == 5, "Assign points pool 5 leaves five to place");
        Console.WriteLine($"Owner creation defaults: {checks} checks passed");
    }
}
