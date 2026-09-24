// OwnerCreationChecks.cs — explicit fork settings, separate from the original oracle.
// Exercises the full allocation transaction for every class, including bounds,
// exhaustion, refund, mode changes and rejected inputs. Update only for owner rules.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

// Owner, 2026-09-24: "the numbers should be 1's with 3 points to spend (total of 8, not 35)".
// Every class opens lean at all 1s with 3 unspent (O-1). These per-class rows are the web's
// bot/driver allocations (web src/model/attributes.js:171-174), used only by tests and bots
// that must begin a run; players still start from the all-1s preset. Order: str, dex, con, wis, int.
internal static class LeanAllocation
{
    internal static readonly string[] Order = { "strength", "dexterity", "constitution", "wisdom", "intelligence" };
    internal static readonly Dictionary<string, int[]> Targets = new()
    {
        ["reaver"] = new[] { 3, 1, 2, 1, 1 },
        ["starseer"] = new[] { 1, 1, 1, 2, 3 },
        ["herald"] = new[] { 1, 1, 2, 3, 1 },
        ["rogue"] = new[] { 1, 3, 2, 1, 1 },
    };
    internal static CreationModel Create(OriginalContentCatalog catalog, string classId, AttributeProgression progression = null)
    {
        var creation = new CreationModel(catalog, classId, "lean", progression);
        Spend(creation);
        return creation;
    }
    // Adjust(id, +1) (target - 1) times from the all-1s preset, exactly as a player would.
    internal static void Spend(CreationModel creation)
    {
        var targets = Targets[creation.ClassId];
        for (var i = 0; i < Order.Length; i++)
            for (var n = 1; n < targets[i]; n++)
                if (!creation.Adjust(Order[i], 1)) throw new Exception("Lean allocation refused " + creation.ClassId + " " + Order[i]);
        if (!creation.CanBegin) throw new Exception("Lean allocation left points unspent for " + creation.ClassId);
    }
}

internal static class OwnerCreationChecks
{
    internal static void Run(OriginalContentCatalog catalog)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var data = catalog.Data();
        var mode = (string)data["attributeRules"]!["defaultMode"]!;
        Check(mode == "lean", "Assigned (lean) must be the default");
        Check(catalog.Table("creationModes").All(row => (string)row["id"]! != "tuned"), "Tuned is still available");
        // Owner, 2026-09-24: only the lean scale is offered; Assign points (pointbuy) and
        // Standard stay in the table so existing saves still resolve them.
        var visible = CreationModel.VisibleModes(catalog);
        Check(visible.Count == 1 && (string)visible[0]["id"]! == "lean", "Only lean is offered at creation");
        Check((string)visible[0]["label"]! == "Assigned", "The offered mode is labelled Assigned");
        var pointbuy = catalog.Table("creationModes").FirstOrDefault(row => (string)row["id"]! == "pointbuy");
        Check(pointbuy != null && (string)pointbuy["label"]! == "Assign points", "Assign points (pointbuy) must stay resolvable for existing saves");
        Check(catalog.Table("creationModes").Any(row => (string)row["id"]! == "standard"), "Standard must stay resolvable for existing saves");
        foreach (var hero in catalog.Table("classes"))
        {
            var id = (string)hero["id"]!;
            var creation = new CreationModel(catalog, id, mode);
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
            Check(creation.Remaining == 35 && creation.TotalPoints == 60 && creation.Attributes().Properties().All(row => (int)row.Value == 5), "Assign points presets changed");
            creation.Select(id, "standard");
            Check(creation.Remaining == 0 && creation.CanBegin && creation.TotalPoints == 55, "Standard presets changed");
            creation.Select(id, mode);
            Check(creation.Remaining == 3 && creation.Attributes().Properties().All(row => (int)row.Value == 1), "Returning to Assigned did not reset to all ones");
            var before = creation.Attributes();
            try { creation.Select(id, "tuned"); throw new Exception("Removed Tuned mode accepted"); }
            catch (ArgumentException) { Check(JToken.DeepEquals(before, creation.Attributes()) && creation.ModeId == mode, "Rejected mode changed creation"); }
            var bot = LeanAllocation.Create(catalog, id);
            Check(bot.CanBegin && bot.Attributes().Properties().Sum(row => (int)row.Value) == 8, "Bot allocation must spend exactly the lean total");
        }
        Console.WriteLine($"Owner creation defaults: {checks} checks passed");
    }
}
