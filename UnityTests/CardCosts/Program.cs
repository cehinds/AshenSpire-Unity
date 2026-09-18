// Readability examples and comparisons with real resolved costs/payment/recovery.
// This executable checks presentation and resource rules, not browser layout.
using System.Globalization;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
void Equal(string actual, string expected, string message) => Check(actual == expected, message + ": " + actual + " != " + expected);
JObject Cost(int action, int mana = 0, int stamina = 0, bool variable = false) => new() { ["action"] = action, ["mana"] = mana, ["stamina"] = stamina, ["variable"] = variable };
JObject Player(int energy, int mana = 0, int stamina = 0) => new() { ["energy"] = energy, ["mana"] = mana, ["stamina"] = stamina };
var policy = new JObject { ["mana"] = new JObject { ["naturalRecoveryPerTurn"] = 0 }, ["stamina"] = new JObject { ["idleRecoveryPerTurn"] = 1, ["refundErasesSpend"] = false } };
JObject FromWallet(ResourceWallet wallet) { var value = wallet.Snapshot(); value["energy"] = value["action"].DeepClone(); return value; }

Equal(OriginalCardCostText.Describe(Cost(0)), "Free", "all-zero costs");
Equal(OriginalCardCostText.Describe(Cost(1)), "1 action", "singular action without resource clutter");
Equal(OriginalCardCostText.Describe(Cost(2)), "2 actions", "plural action");
Equal(OriginalCardCostText.Describe(Cost(1, 2)), "1 action · 2 MP", "mana-only surcharge");
Equal(OriginalCardCostText.Describe(Cost(1, 0, 2)), "1 action · 2 stamina", "stamina-only surcharge");
Equal(OriginalCardCostText.Describe(Cost(0, 2)), "0 actions · 2 MP", "zero actions still distinguishes a mana cost");
Equal(OriginalCardCostText.Describe(Cost(2, 3, 1)), "2 actions · 3 MP · 1 stamina", "all three resources");
Equal(OriginalCardCostText.Describe(Cost(0, variable: true)), "X actions", "X is never Free");
Equal(OriginalCardCostText.Describe(Cost(0, 1, 2, true)), "X actions · 1 MP · 2 stamina", "X with other costs");
Equal(OriginalCardCostText.Shortage(Cost(2, 3, 2), Player(1, 1, 1)), "Need 1 more action, 2 MP, 1 stamina", "combined deficits only");
Equal(OriginalCardCostText.Shortage(Cost(3), Player(1)), "Need 2 more actions", "plural action shortage");
Equal(OriginalCardCostText.Shortage(Cost(1, 2, 3), Player(9, 1, 9)), "Need 1 MP", "surplus resources omitted");
Equal(OriginalCardCostText.Shortage(Cost(1, 2, 3), Player(9, 9, 2)), "Need 1 stamina", "stamina-only deficit");
Equal(OriginalCardCostText.Shortage(Cost(0), Player(0)), null, "zero cost at zero pools");
Equal(OriginalCardCostText.Shortage(Cost(3, 2, 1), Player(3, 2, 1)), null, "exact resource boundary");
Equal(OriginalCardCostText.Shortage(Cost(99, variable: true), Player(0)), null, "X action field does not impose a minimum");
Equal(OriginalCardCostText.Shortage(Cost(99, 2, 1, true), Player(0)), "Need 2 MP, 1 stamina", "X waives actions only");
Check(OriginalCardCostText.IsAffordable(Cost(0, variable: true), Player(0)), "X at zero is resource-affordable");

// CostProfile includes actual power reductions and weight-derived dodge prices.
var power = new JObject { ["id"] = "fixturePower", ["type"] = "power", ["cost"] = 2 };
Equal(OriginalCardCostText.Describe(CardMechanics.CostProfile(power, 1)), "1 action", "resolved power reduction");
Equal(OriginalCardCostText.Describe(CardMechanics.CostProfile(power, 9)), "Free", "power reduction clamps at zero");
var dodge = new JObject { ["id"] = "fixtureDodge", ["type"] = "skill", ["cost"] = 0, ["effects"] = new JArray(new JObject { ["op"] = "dodgeRoll", ["target"] = "self" }) };
var dodgeCost = CardMechanics.CostProfile(dodge, 0, new JObject { ["dodgeActionCost"] = 1, ["dodgeStaminaCost"] = 2 });
Equal(OriginalCardCostText.Describe(dodgeCost), "1 action · 2 stamina", "actual weight-priced dodge");

// A real wallet changes affordability without helper caching or mutation.
var wallet = new ResourceWallet(0, 3, 2, policy, 0, 0);
var mixed = Cost(1, 2, 1);
Equal(OriginalCardCostText.Shortage(mixed, FromWallet(wallet)), "Need 1 more action, 2 MP, 1 stamina", "empty wallet");
wallet.GainActions(1);
Equal(OriginalCardCostText.Shortage(mixed, FromWallet(wallet)), "Need 2 MP, 1 stamina", "action recovery removes one shortage");
wallet.RecoverMana(2);
Equal(OriginalCardCostText.Shortage(mixed, FromWallet(wallet)), "Need 1 stamina", "mana recovery removes one shortage");
wallet.EndTurn();
Equal(OriginalCardCostText.Shortage(mixed, FromWallet(wallet)), null, "authored idle stamina recovery permits card");
Check(wallet.TryPayProfile(mixed), "same cost can actually be paid");
Equal(OriginalCardCostText.Shortage(mixed, FromWallet(wallet)), "Need 1 more action, 2 MP, 1 stamina", "payment updates all shortages");

// Compare all authored card profiles at their exact and one-below boundaries
// against actual wallet payment. No simulated combat or balance is changed.
var root = args.Length > 0 ? args[0] : ".";
var content = JObject.Parse(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/content.json")));
var cardCount = 0;
foreach (var definition in ((JArray)content["cards"]).OfType<JObject>())
{
    var cost = CardMechanics.CostProfile(definition);
    var action = (int)cost["action"]; var mana = (int)cost["mana"]; var stamina = (int)cost["stamina"]; var variable = (bool)cost["variable"];
    var exact = Player(action, mana, stamina);
    var pools = new List<JObject> { exact, Player(0) };
    if (action > 0) pools.Add(Player(action - 1, mana, stamina));
    if (mana > 0) pools.Add(Player(action, mana - 1, stamina));
    if (stamina > 0) pools.Add(Player(action, mana, stamina - 1));
    foreach (var player in pools)
    {
        var beforeCost = cost.ToString(); var beforePlayer = player.ToString();
        var description = OriginalCardCostText.Describe(cost);
        var shortage = OriginalCardCostText.Shortage(cost, player);
        var expected = (variable || action <= (int)player["energy"]) && mana <= (int)player["mana"] && stamina <= (int)player["stamina"];
        Check(OriginalCardCostText.IsAffordable(cost, player) == expected && (shortage == null) == expected, "original UI predicate: " + definition["id"]);
        var actualWallet = new ResourceWallet((int)player["energy"], mana, stamina, policy, (int)player["mana"], (int)player["stamina"]);
        Check(actualWallet.TryPayProfile(cost, variableActionAmount: variable ? (int?)player["energy"] : null) == expected, "actual payment agreement: " + definition["id"]);
        Check(cost.ToString() == beforeCost && player.ToString() == beforePlayer, "formatting mutated inputs");
        Check(!string.IsNullOrWhiteSpace(description) && !description.Contains("0 MP") && !description.Contains("0 stamina"), "omit zero resource clutter");
    }
    cardCount++;
}
Check(cardCount > 0, "authored cards were exercised");

// Integral domain boundaries and invalid presentation inputs fail explicitly.
Equal(OriginalCardCostText.Shortage(Cost(int.MaxValue, int.MaxValue, int.MaxValue), Player(0)), "Need 2147483647 more actions, 2147483647 MP, 2147483647 stamina", "deficits do not overflow");
foreach (var invalid in new JToken[] { new JValue(-1), new JValue(.5), new JValue(double.NaN), new JValue(double.PositiveInfinity), new JValue("1"), new JObject(), JValue.CreateNull() })
{
    var cost = Cost(1); cost["mana"] = invalid.DeepClone();
    try { OriginalCardCostText.Describe(cost); throw new Exception("Accepted invalid cost"); } catch (ArgumentException) { checks++; }
}
var priorCulture = CultureInfo.CurrentCulture;
try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Equal(OriginalCardCostText.Describe(Cost(2, 3)), "2 actions · 3 MP", "stable numeric cost labels"); }
finally { CultureInfo.CurrentCulture = priorCulture; }
Console.WriteLine($"PASS {checks} checks across {cardCount} authored card profiles; pure cost text, payment boundaries and recovery.");
