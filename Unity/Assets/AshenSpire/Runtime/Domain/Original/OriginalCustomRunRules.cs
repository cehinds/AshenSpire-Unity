// OriginalCustomRunRules.cs — original Custom Climb setup and modifier receipts.
// Author magnitudes in balance.customMods/endless and keepsakes in characterCreation.
// Configuration is frozen inside the run snapshot; views submit choices, never rolls.
// Draft offers are committed before rendering so loading cannot reroll an offer.
// Run shape is validated against every frozen act; there is no separate practice mode.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalCustomRunRules
    {
        public static readonly string[] AscensionOrder = { "toughElites", "deadlyEnemies", "lessHealing", "bigBosses", "cursedStart", "expensiveShops" };
        private static readonly string[] Chaos = { "allElite", "hoarder", "chaosRewards", "glassCannon", "endless" };
        private static readonly string[] Basics = { "strike", "strike", "strike", "strike", "defend", "defend", "defend" };
        private readonly JObject _data;
        public OriginalCustomRunRules(JObject content) { _data = (JObject)content.DeepClone(); }
        public static JObject Normalize(JObject custom)
        {
            custom = custom == null ? new JObject() : (JObject)custom.DeepClone();
            if (custom.Properties().Any(p => !new[] { "ascension", "mods", "deckMode", "mapShape" }.Contains(p.Name))) throw new ArgumentException("Unknown custom-run option.");
            var mapShape = OriginalMapShape.Normalize(custom["mapShape"]);
            var asc = custom["ascension"] ?? new JValue(0);
            if (asc.Type != JTokenType.Integer || (int)asc < 0 || (int)asc > AscensionOrder.Length) throw new ArgumentException("Ascension must be a whole number from 0 to " + AscensionOrder.Length);
            var deck = (string)custom["deckMode"] ?? "standard";
            if (!new[] { "standard", "sealed", "draft" }.Contains(deck)) throw new ArgumentException("Unknown starting deck mode: " + deck);
            var mods = custom["mods"] == null ? new JObject() : custom["mods"] as JObject ?? throw new ArgumentException("Custom modifiers must be an object.");
            foreach (var p in mods.Properties()) if (!AscensionOrder.Concat(Chaos).Contains(p.Name) || p.Value.Type != JTokenType.Boolean) throw new ArgumentException("Invalid custom modifier: " + p.Name);
            var normalized = new JObject { ["ascension"] = asc.DeepClone(), ["mods"] = mods.DeepClone(), ["deckMode"] = deck };
            if (mapShape != null) normalized["mapShape"] = mapShape;
            return normalized;
        }
        public static JObject ActiveMods(JObject custom)
        {
            var normalized = Normalize(custom); var mods = (JObject)normalized["mods"].DeepClone();
            foreach (var id in AscensionOrder.Take((int)normalized["ascension"])) mods[id] = true;
            return mods;
        }
        public static bool IsCustom(JObject custom)
        {
            var normalized = Normalize(custom);
            return normalized["mapShape"] is JObject shape && shape.HasValues || (int)normalized["ascension"] > 0 || (string)normalized["deckMode"] != "standard" || ((JObject)normalized["mods"]).Properties().Any(p => (bool)p.Value);
        }
        public static bool Enabled(JObject run, string id) => (bool?)ActiveMods(run["custom"] as JObject)[id] == true;
        public int ContentAct(JObject run)
        {
            var act = (int)run["actNumber"]; if (act < 1) throw new ArgumentException("Act must be positive.");
            return Enabled(run,"endless") ? (act - 1) % (int)_data["balance"]["endless"]["actsPerCycle"] + 1 : act;
        }
        public JObject CombatOptions(JObject run, string pool)
        {
            var mods = ActiveMods(run["custom"] as JObject); var balance = _data["balance"]["customMods"]; double hp = 1;
            bool On(string key) => (bool?)mods[key] == true;
            var enemy = new JArray(); var player = new JArray();
            if ((pool == "elite" || pool == "boss") && On("toughElites")) hp *= (double)balance["toughElitesHpMult"];
            if (pool == "boss" && On("bigBosses")) hp *= (double)balance["bigBossesHpMult"];
            if (On("deadlyEnemies")) enemy.Add(new JObject { ["status"] = "strength", ["stacks"] = 1 });
            if (On("glassCannon")) player.Add(new JObject { ["status"] = "glassCannon", ["stacks"] = 1 });
            if (On("endless"))
            {
                var endless = _data["balance"]["endless"]; var loop = ((int)run["actNumber"] - 1) / (int)endless["actsPerCycle"];
                if (loop > 0) { hp *= 1 + (double)endless["hpPerLoop"] * loop; enemy.Add(new JObject { ["status"] = "strength", ["stacks"] = checked((int)endless["strPerLoop"] * loop) }); }
            }
            return new JObject { ["hpMult"] = hp, ["enemyStatuses"] = enemy, ["playerStatuses"] = player };
        }
        public double ShopPriceMultiplier(JObject run)
        {
            var balance = _data["balance"]["customMods"]; double value = 1;
            if (Enabled(run,"expensiveShops")) value *= (double)balance["expensiveShopsMult"];
            if (Enabled(run,"hoarder")) value *= (double)balance["hoarderShopMult"];
            return value;
        }
        public double HealMultiplier(JObject run) => Enabled(run,"lessHealing") ? (double)_data["balance"]["customMods"]["lessHealingMult"] : 1;
        public static JObject Card(string id,string instance) => new JObject { ["instanceId"] = instance, ["cardId"] = id, ["upgraded"] = false };
        private JArray ClassPool(JObject run) => (JArray)_data["classes"].First(c => (string)c["id"] == (string)run["class"])["cardPool"].DeepClone();
        public void Initialize(JObject run, RandomStreams rng, IOriginalRunContent callbacks)
        {
            if (run["custom"] != null && !(run["custom"] is JObject)) throw new ArgumentException("Custom rules must be an object.");
            run["custom"] = Normalize(run["custom"] as JObject);
            run["isCustom"] = IsCustom((JObject)run["custom"]); run["countsForWinRate"] = !(bool)run["isCustom"];
            run["progressionMode"] = (bool)run["isCustom"] ? "custom" : "normal"; run["seedString"] = RandomStreams.DisplaySeed((uint)run["seed"]);
            if (run["customization"] == null) run["customization"] = new JObject { ["name"] = "Forsaken", ["glyph"] = "⚔", ["tint"] = "gold" };
            if (!(run["customization"] is JObject cosmetic) || cosmetic.Properties().Any(p => !new[] { "name", "glyph", "tint", "spriteStyle", "figureId" }.Contains(p.Name) || p.Value.Type != JTokenType.String || ((string)p.Value).Length > 128)) throw new ArgumentException("Invalid character cosmetic setup.");
            var keepsakeId = (string)run["keepsakeId"] ?? "none";
            var keepsake = _data["characterCreation"]["keepsakes"].FirstOrDefault(k => (string)k["id"] == keepsakeId) ?? throw new ArgumentException("Unknown keepsake.");
            run["keepsakeId"] = keepsakeId;
            callbacks.ApplyEffects(run,(JArray)keepsake["effects"].DeepClone(),rng);
            var mode = (string)run["custom"]["deckMode"];
            if (mode != "standard")
            {
                var ids = Basics.ToList(); var pool = ClassPool(run);
                if (mode == "sealed") for (var i = 0; i < 3 && pool.Count > 0; i++) { var index = rng.Int("misc",0,pool.Count-1); ids.Add((string)pool[index]); pool.RemoveAt(index); }
                run["deck"] = new JArray(ids.Select((id,index) => Card(id,"rc" + (index + 1))));
            }
            if (Enabled(run,"cursedStart")) ((JArray)run["deck"]).Add(Card("guilt","cx1"));
            if (Enabled(run,"hoarder")) run["cinders"] = checked((int)run["cinders"] + (int)_data["balance"]["customMods"]["hoarderCinders"]);
            run["equipmentAttackSlotCount"] = ((JArray)run["deck"]).Count(card => (string)card["equipmentRole"] == "attack");
            if (mode == "draft")
            {
                run["phase"] = "Draft"; run["room"] = new JObject { ["draft"] = new JObject { ["round"] = 0, ["rounds"] = 3, ["choices"] = 3, ["pool"] = ClassPool(run), ["picked"] = new JArray() } };
                RollDraft(run,rng);
            }
        }
        public void RollDraft(JObject run,RandomStreams rng)
        {
            var draft = (JObject)run["room"]["draft"]; var pool = ((JArray)draft["pool"]).Values<string>().ToList(); var offer = new JArray();
            for (var i = 0; i < (int)draft["choices"] && pool.Count > 0; i++) { var index = rng.Int("cardRewards",0,pool.Count-1); offer.Add(pool[index]); pool.RemoveAt(index); }
            if (offer.Count == 0) throw new InvalidOperationException("Class pool cannot complete the draft.");
            draft["offer"] = offer;
        }
        public bool PickDraft(JObject run,string cardId,RandomStreams rng)
        {
            var draft = run["room"]?["draft"] as JObject ?? throw new ArgumentException("No pending draft.");
            if (!(draft["offer"] as JArray ?? new JArray()).Values<string>().Contains(cardId)) return false;
            var round = (int)draft["round"] + 1; var card = Card(cardId,"df" + round);
            ((JArray)draft["picked"]).Add(cardId); ((JArray)run["deck"]).Add(card); draft["pool"].First(id => (string)id == cardId).Remove(); draft["round"] = round;
            if (round < (int)draft["rounds"]) RollDraft(run,rng);
            return true;
        }
        public void Validate(JObject run)
        {
            if (run["custom"] != null && !(run["custom"] is JObject)) throw new ArgumentException("Invalid saved custom rules.");
            var custom = Normalize(run["custom"] as JObject); var isCustom = IsCustom(custom);
            if (custom["mapShape"] is JObject shape) OriginalMapShape.ResolveAll((JObject)_data["mapConfigs"], shape, run["mapShapeLimits"] as JObject);
            if (run["isCustom"] != null && (bool)run["isCustom"] != isCustom || run["countsForWinRate"] != null && (bool)run["countsForWinRate"] == isCustom) throw new ArgumentException("Custom-run eligibility differs from its frozen rules.");
            if ((string)run["phase"] != "Draft") return;
            var draft = run["room"]?["draft"] as JObject ?? throw new ArgumentException("Missing saved draft.");
            if ((int)run["actNumber"] != 1 || (string)custom["deckMode"] != "draft" || (int?)draft["rounds"] != 3 || (int?)draft["choices"] != 3 || draft["round"]?.Type != JTokenType.Integer || (int)draft["round"] < 0 || (int)draft["round"] >= 3 || !(draft["pool"] is JArray pool) || !(draft["offer"] is JArray offer) || !(draft["picked"] is JArray picked)) throw new ArgumentException("Invalid saved draft configuration.");
            var classPool = ClassPool(run).Values<string>().ToArray(); var chosen = picked.Values<string>().ToArray();
            if (chosen.Length != (int)draft["round"] || chosen.Distinct().Count() != chosen.Length || chosen.Any(id => !classPool.Contains(id)) || !pool.Values<string>().SequenceEqual(classPool.Where(id => !chosen.Contains(id))) || offer.Count != Math.Min(3,pool.Count) || offer.Values<string>().Distinct().Count() != offer.Count || offer.Values<string>().Any(id => !pool.Values<string>().Contains(id))) throw new ArgumentException("Invalid saved draft offer or pool.");
        }
    }
}
