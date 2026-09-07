// OwnerCreationChecks.cs — explicit fork settings, separate from the original oracle.
// Exercises the full allocation transaction for every class, including bounds,
// exhaustion, refund, mode changes and rejected inputs. Update only for owner rules.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

internal static class OwnerCreationChecks
{
    internal static void Run(OriginalContentCatalog catalog)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var data = catalog.Data();
        var mode = (string)data["attributeRules"]!["defaultMode"]!;
        Check(mode == "pointbuy", "Assign points must be the default");
        Check(catalog.Table("creationModes").All(row => (string)row["id"]! != "tuned"), "Tuned is still available");
        foreach (var hero in catalog.Table("classes"))
        {
            var creation = new CreationModel(catalog, (string)hero["id"]!, mode);
            var attributes = creation.Attributes().Properties().Select(row => row.Name).ToArray();
            Check(attributes.Length == 5 && creation.Attributes().Properties().All(row => (int)row.Value == 5), "All attributes must start at five");
            Check(creation.TotalPoints == 60 && creation.Remaining == 35 && !creation.CanBegin, "60 - (5 * 5) must leave 35 unspent");
            var changed = 0; creation.Changed += () => changed++;
            foreach (var attribute in attributes)
                Check(!creation.CanAdjust(attribute, -1) && !creation.Adjust(attribute, -1), "Minimum allowed an underflow");
            Check(changed == 0 && creation.Remaining == 35, "Rejected minimum edit changed state");
            foreach (var attribute in attributes.Take(3))
            {
                for (var i = 0; i < 10; i++) Check(creation.Adjust(attribute, 1), "Legal point assignment failed");
                Check((int)creation.Attributes()[attribute]! == 15 && !creation.CanAdjust(attribute, 1) && !creation.Adjust(attribute, 1), "Maximum allowed an overflow");
            }
            for (var i = 0; i < 5; i++) Check(creation.Adjust(attributes[3], 1), "Remaining points could not be assigned");
            Check(creation.Remaining == 0 && creation.CanBegin && changed == 35, "Fully assigned state is incorrect");
            Check(!creation.CanAdjust(attributes[4], 1) && !creation.Adjust(attributes[4], 1) && changed == 35, "Allocation overspent its budget");
            Check(creation.Adjust(attributes[0], -1) && creation.Remaining == 1 && !creation.CanBegin, "Refund did not return one point");
            Check(creation.Adjust(attributes[4], 1) && creation.Remaining == 0 && creation.CanBegin, "Refunded point could not move between attributes");
            creation.Select((string)hero["id"]!, "standard");
            Check(creation.Remaining == 0 && creation.CanBegin && creation.TotalPoints == 55, "Standard presets changed");
            creation.Select((string)hero["id"]!, mode);
            Check(creation.Remaining == 35 && creation.Attributes().Properties().All(row => (int)row.Value == 5), "Returning to Assign points did not reset to minimum");
            var before = creation.Attributes();
            try { creation.Select((string)hero["id"]!, "tuned"); throw new Exception("Removed Tuned mode accepted"); }
            catch (ArgumentException) { Check(JToken.DeepEquals(before, creation.Attributes()) && creation.ModeId == mode, "Rejected mode changed creation"); }
        }
        Console.WriteLine($"Owner creation defaults: {checks} checks passed");
    }
}
