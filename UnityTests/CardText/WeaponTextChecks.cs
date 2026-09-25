// WeaponTextChecks.cs — actual equipment/progression/smithing, text and combat.
// Rating bonuses are Unity's owner-authored improvement, not original parity. They read the
// shipped content (lean scale, armament ratings) for a lean Reaver {3,1,2,1,1}; the pinned
// oracle catalog passed in only hosts the combat fixture that executes the card.
using AshenSpire.Domain;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

internal static class WeaponTextChecks
{
    internal static JArray Run(OriginalContentCatalog oracle, JObject combat, JObject rules,
        Action<bool, string> check, out int commands)
    {
        var receipts = new JArray();
        var catalog = new OriginalContentCatalog(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "GameContent/Unity/Original/content.json")));
        var projection = new WeaponCardProjection(catalog);
        var progression = new AttributeProgression(rules);
        var loadout = new WeaponLoadout(catalog).Create("reaver");
        // AR = floor(.75*3) + floor(.5*1) + floor(.25*2) = 2, plus the straight sword's attack rating 2.
        var attributes = new JObject { ["strength"] = 3, ["dexterity"] = 1, ["constitution"] = 2, ["wisdom"] = 1, ["intelligence"] = 1 };
        var deck = new WeaponCardComposer(catalog).CreateStartingDeck(loadout, "reaver");
        var instance = (JObject)deck.First(x => (string)x["profileId"] == "bladeAttack").DeepClone();
        commands = 0;
        foreach (var level in new[] { 0, 1 })
        {
            instance["smithingLevel"] = level;
            var resolved = projection.Resolve(instance, loadout, "reaver", attributes, progression.BaselineProfiles(catalog));
            var improved = (JObject)progression.ResolveCard(resolved, attributes, catalog)["card"];
            var damage = improved["effects"].OfType<JObject>().First(x => (string)x["op"] == "damage");
            check(damage["amount"].Type == JTokenType.Float, "weapon projection retains floating number at tier " + level);
            var expected = (int)damage["amount"] + 4;
            var text = OriginalCardText.Describe(improved, catalog);
            check(text.StartsWith("Deal " + expected + " damage."), "weapon tier " + level + " displays total damage");
            check(text.Contains("Includes +4 total damage from Attack Rating."), "weapon tier " + level + " names included contribution: " + text);
            if (level == 0) check(text.StartsWith("Deal 9 damage."), "lean Reaver Slashing Strike deals 9: " + text);
            var integral = (JObject)improved.DeepClone();
            integral["effects"][0]["amount"] = (int)damage["amount"];
            check(OriginalCardText.Describe(integral, catalog) == text, "integer/whole-float text equivalence tier " + level);
            var fixture = combat["fixtures"][0];
            var engine = new CombatSession(oracle, (JObject)combat["mechanics"], new RandomStreams((uint)fixture["seed"]),
                (JObject)fixture["player"].DeepClone(), fixture["deck"].OfType<JObject>(), fixture["enemyIds"].Values<string>(),
                _ => (JObject)improved.DeepClone(), (double)fixture["hpMult"]);
            var events = engine.PlayCard("probe", "e1"); commands++;
            var actual = events.Where(x => (string)x["type"] == "damageDealt" && (string)x["sourceId"] == "player").Sum(x => (int)x["amount"]);
            check(actual == expected, "actual weapon tier " + level + " damage matches displayed total before defenses");
            receipts.Add(new JObject { ["smithingLevel"] = level, ["text"] = text, ["actualDamage"] = actual });
        }
        var gore = projection.Resolve(new JObject { ["cardId"] = "gorefireSlash" }, loadout, "reaver", attributes);
        var goreCard = (JObject)progression.ResolveCard(gore, attributes, catalog)["card"];
        check(OriginalCardText.Describe(goreCard, catalog) == "Deal 7 damage. Apply 3 Bleed. Includes +2 total damage from Attack Rating.", "authored Gorefire carries AR only: " + OriginalCardText.Describe(goreCard, catalog));
        foreach (var field in new[] { "hits", "repeat" })
        {
            var probe = (JObject)goreCard.DeepClone();
            probe["effects"][0]["amount"] = 5.0;
            probe["effects"][0][field] = 2.0;
            check(!OriginalCardText.Describe(probe, catalog).Contains("Includes"), "whole-float amount does not fold across " + field);
        }
        foreach (var amount in new[] { 6.5, double.NaN, double.PositiveInfinity, double.NegativeInfinity, (double)int.MaxValue + 1 })
        {
            var probe = (JObject)goreCard.DeepClone(); probe["effects"][0]["amount"] = amount;
            check(!OriginalCardText.Describe(probe, catalog).Contains("Includes"), "unsafe/non-whole values never claim folded bonus: " + amount);
        }
        return receipts;
    }
}
