// SwapChecks.cs — actual original swap receipts, paid state and live card identities.
using System;
using System.IO;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
internal static class SwapChecks
{
    internal static int Run(OriginalContentCatalog source, JObject mechanics, string fixturePath)
    {
        var checks = 0;
        JToken Normal(JToken t) { if (t == null || t.Type == JTokenType.Null || t is JValue value && value.Value == null) return JValue.CreateNull(); if (t is JObject o) return new JObject(o.Properties().OrderBy(p => p.Name).Select(p => new JProperty(p.Name, Normal(p.Value)))); if (t is JArray a) return new JArray(a.Select(Normal)); if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) return new JValue((double)t); return t.DeepClone(); }
        void Equal(JToken a, JToken b, string label) { if (!JToken.DeepEquals(Normal(a), Normal(b))) throw new Exception(label + "\nactual=" + a + "\nexpected=" + b); checks++; }
        var fixtures = JObject.Parse(File.ReadAllText(fixturePath));
        foreach (var fixture in fixtures["fixtures"])
        {
            var data = source.Data(); data["balance"]["equipment"]["swapCostKind"] = fixture["kind"].DeepClone(); var catalog = new OriginalContentCatalog(data.ToString());
            var service = new OriginalCombatEquipment(catalog, mechanics); var run = (JObject)fixture["run"].DeepClone(); var battle = (JObject)fixture["before"].DeepClone();
            foreach (var step in fixture["steps"])
            {
                var beforeRun = run.DeepClone(); var beforeBattle = battle.DeepClone();
                Equal(service.Price(run, battle, "rightHand", (int)step["setIndex"]), step["price"], "Original swap price");
                JObject changed = null; Exception failure = null;
                try { changed = service.Apply(run, battle, "rightHand", (int)step["setIndex"]); } catch (ArgumentException e) { failure = e; }
                Equal(new JValue(failure != null), new JValue(step["error"]?.Type == JTokenType.String), "Original swap acceptance " + failure);
                Equal(run, beforeRun, "Swap input run stays immutable"); Equal(battle, beforeBattle, "Swap input combat stays immutable");
                if (failure != null) continue;
                run = (JObject)changed["run"]; battle = (JObject)changed["combat"];
                Equal(run["loadout"], step["loadout"], "Original paid active set"); Equal(changed["events"], step["events"], "Original swap event receipts");
                foreach (var key in new[] { "hp", "maxHp", "mana", "maxMana", "stamina", "maxStamina", "energy", "counters", "statuses", "poiseMeter" }) Equal(battle["player"][key], step["player"][key], "Original swap player field " + key);
                Equal(new JValue(service.SwapsLeft(battle)), step["swapsLeft"], "Original allowance spending");
                var projector = new WeaponCardProjection(catalog);
                foreach (var pile in new[] { "hand", "draw", "discard", "exhaust" })
                {
                    Equal(new JArray(battle["piles"][pile].Select(c => c["instanceId"].DeepClone())), new JArray(step["piles"][pile].Select(c => c["instanceId"].DeepClone())), "Original stable pile identities");
                    foreach (var card in battle["piles"][pile].OfType<JObject>())
                    { var expected = step["definitions"][pile].First(c => (string)c["id"] == (string)card["instanceId"])["card"]; Equal(projector.Resolve(card, (JObject)run["loadout"], "reaver", (JObject)run["attributes"])["card"], expected, "Original swapped card definition"); }
                }
                var restored = CombatSession.Restore(catalog, mechanics, battle, c => (JObject)projector.Resolve(c, (JObject)run["loadout"], "reaver", (JObject)run["attributes"])["card"]);
                Equal(restored.FinishEquipmentSwap((JArray)changed["events"], false), changed["events"], "Swap events emitted through native combat");
                battle = restored.Snapshot();
            }
        }
        var sample = fixtures["fixtures"][0]; var sampleRun = (JObject)sample["run"].DeepClone(); var sampleBattle = (JObject)sample["before"].DeepClone();
        var normalService = new OriginalCombatEquipment(source, mechanics);
        void Refuses(JObject r, JObject b, string slot, int index)
        { var priorRun = r.DeepClone(); var priorBattle = b.DeepClone(); try { normalService.Apply(r, b, slot, index); throw new Exception("Invalid swap accepted"); } catch (ArgumentException) { checks++; } Equal(r, priorRun, "Rejected swap preserves run"); Equal(b, priorBattle, "Rejected swap preserves battle"); }
        var noActions = (JObject)sampleBattle.DeepClone(); noActions["player"]["energy"] = 0; Refuses(sampleRun, noActions, "rightHand", 1);
        Refuses(sampleRun, sampleBattle, "armor", 0); Refuses(sampleRun, sampleBattle, "rightHand", -1); Refuses(sampleRun, sampleBattle, "rightHand", 2);
        var twoHands = (JObject)sampleRun.DeepClone(); twoHands["loadout"]["sets"]["rightHand"][1] = "dagger"; twoHands["loadout"]["sets"]["leftHand"][0] = "dagger"; Refuses(twoHands, sampleBattle, "rightHand", 1);
        var ordinary = (JObject)sampleRun.DeepClone(); ordinary["deck"] = new JArray(new JObject { ["instanceId"] = "draft:1", ["cardId"] = "strike", ["upgraded"] = false }); ordinary["equipmentAttackSlotCount"] = 0;
        var ordinaryBattle = (JObject)sampleBattle.DeepClone(); ordinaryBattle["piles"] = new JObject { ["hand"] = ordinary["deck"].DeepClone(), ["draw"] = new JArray(), ["discard"] = new JArray(), ["exhaust"] = new JArray(), ["sealed"] = new JArray(), ["removed"] = new JArray() };
        var zero = normalService.Apply(ordinary, ordinaryBattle, "rightHand", 1); Equal(new JValue(zero["combat"]["piles"].Children<JProperty>().SelectMany(p => p.Value).Count(c => (string)c["equipmentRole"] == "attack")), new JValue(0), "Draft birth quota stays zero");
        Equal(zero["combat"]["piles"]["hand"][0], ordinary["deck"][0], "Ordinary draft card keeps identity and definition");
        var poolData = source.Data(); ((JArray)poolData["equipment"]["armaments"].First(p => (string)p["id"] == "dagger")["mods"]).Add("self.maxHp=+20");
        var poolCatalog = new OriginalContentCatalog(poolData.ToString()); var poolService = new OriginalCombatEquipment(poolCatalog, mechanics); var grown = poolService.Apply(sampleRun, sampleBattle, "rightHand", 1);
        Equal(grown["combat"]["player"]["maxHp"], new JValue(120), "Swap updates resource vessel"); Equal(grown["combat"]["player"]["hp"], new JValue(100), "Swap preserves missing health");
        var shrunk = poolService.Apply((JObject)grown["run"], (JObject)grown["combat"], "rightHand", 0); Equal(shrunk["combat"]["player"]["hp"], new JValue(80), "Swap resource round trip does not heal");
        var turnData = source.Data(); turnData["balance"]["equipment"]["swapCostKind"] = "allowance"; turnData["balance"]["equipment"]["swapEndsTurn"] = true;
        var turnCatalog = new OriginalContentCatalog(turnData.ToString()); var turnService = new OriginalCombatEquipment(turnCatalog, mechanics); var ended = turnService.Apply(sampleRun, sampleBattle, "rightHand", 1);
        var turnProjection = new WeaponCardProjection(turnCatalog); var turnRun = (JObject)ended["run"];
        var turnCombat = CombatSession.Restore(turnCatalog, mechanics, (JObject)ended["combat"], c => (JObject)turnProjection.Resolve(c, (JObject)turnRun["loadout"], "reaver", (JObject)turnRun["attributes"])["card"]);
        var turnEvents = turnCombat.FinishEquipmentSwap((JArray)ended["events"], (bool)ended["endsTurn"]);
        Equal(new JValue(turnCombat.Turn), new JValue((int)sampleBattle["turn"] + 1), "Authored swap ends the turn"); Equal(new JValue(turnService.SwapsLeft(turnCombat.Snapshot())), new JValue(1), "Allowance resets on next player turn");
        Equal(new JValue(turnEvents.Any(e => (string)e["type"] == "armamentSwapped")), new JValue(true), "Turn-ending swap retains receipt");
        return checks;
    }
}



