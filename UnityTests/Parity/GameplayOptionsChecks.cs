// GameplayOptionsChecks.cs — US-15.2 gameplay settings against the shipped content and the native session:
// reward collection (HTML reward.js collectMode + rewardplan.resolveContinue), weapon swap cost rule
// (loadout.resolveSwapCostRule/swapCostFor) and merchant buy-back (shop.js shopSell). Each non-default
// value must change only its own behaviour; the defaults must reproduce the game as it was.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
internal static class GameplayOptionsChecks
{
    internal static int Run(string root)
    {
        var contentRoot = Path.Combine(root, "GameContent/Unity/Original");
        var catalog = new OriginalContentCatalog(File.ReadAllText(Path.Combine(contentRoot, "content.json"))); var data = catalog.Data();
        var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(contentRoot, "mechanics.json"))); var supplement = JObject.Parse(File.ReadAllText(Path.Combine(contentRoot, "event-choices.json")));
        var progression = new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(contentRoot, "progression.json"))));
        var checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("Gameplay options: " + label); checks++; }
        JObject Without(JObject snapshot, params string[] runKeys) { var copy = (JObject)snapshot.DeepClone(); foreach (var key in runKeys) ((JObject)copy["run"]).Remove(key); return copy; }
        JObject Player(string classId) { var creator = LeanAllocation.Create(catalog, classId, progression); var kit = (string)catalog.Table("equipment.startingKits").First(k => (string)k["classId"] == classId && (bool?)k["baseline"] == true)["id"]; return new OriginalCharacterBuilder(catalog, progression, mechanics).Build(creator, kit); }
        OriginalGameSession Start(uint seed) => OriginalGameSession.StartConfigured(catalog, supplement, mechanics, Player("reaver"), seed, new JObject { ["custom"] = new JObject(), ["keepsakeId"] = "none" });
        var callbacks = new OriginalRunContent(catalog, reconcile: new OriginalPlayerProjection(catalog, mechanics).Reconcile);

        // ---- Defaults: the setting-free game is unchanged ----------------------------------------
        var defaults = new OriginalPlayerSettings();
        // Owner decision 2026-10-02: Unity's default is manual (only cinders come along), not the content def (auto).
        Check(defaults.RewardCollect == "manual" && OriginalGameplayOptions.RewardCollectMode(data, defaults.RewardCollect) == "manual", "default reward mode is manual (owner decision 2026-10-02)");
        Check((string)data["balance"]["ui"]["rewardCollect"]["def"] == "auto" && OriginalGameplayOptions.RewardCollectMode(data, null) == "manual", "the Unity default overrides the content def when no mode is set");
        var fresh = Start(11); var before = fresh.Snapshot();
        Check(fresh.ApplyProfileSettings(OriginalGameplayOptions.ProfileSettings(defaults)), "binding writes the gameplay keys into a run that lacks them");
        var after = fresh.Snapshot();
        Check(JToken.DeepEquals(Without(after, "profileMeta"), Without(before, "profileMeta")), "binding the settings changes nothing but run.profileMeta");
        Check((bool)after["run"]["profileMeta"]["settings"]["shopSell"] && (string)after["run"]["profileMeta"]["settings"]["swapCostRule"] == "flat", "default gameplay keys: shopSell on, flat rule");
        Check(!fresh.ApplyProfileSettings(OriginalGameplayOptions.ProfileSettings(defaults)) && JToken.DeepEquals(fresh.Snapshot(), after), "re-binding identical values is a no-op");
        Check(JToken.DeepEquals(OriginalGameSession.Restore(after).Snapshot(), after), "the bound settings survive save and restore");

        // ---- Reward collection --------------------------------------------------------------------
        JObject AtRewards(uint seed)
        {
            var run = OriginalRunSession.Restore(Start(seed).Snapshot(), callbacks);
            var combat = run.LegalNodeIds().First(id => (string)run.Map()["nodes"][id]["type"] == "monster");
            if (!run.EnterNode(combat) || run.Phase != OriginalRunPhase.Combat) throw new Exception("Fixture did not enter combat");
            run.CompleteCombat("victory", run.Player(), run.CreateRandom());
            if (run.Phase != OriginalRunPhase.Rewards) throw new Exception("Fixture did not reach rewards");
            return run.Snapshot();
        }
        var rewards = AtRewards(5); var offer = rewards["run"]["room"]["rewards"]; var deckBefore = ((JArray)rewards["run"]["deck"]).Count; var cindersBefore = (int)rewards["run"]["cinders"];
        Check((int?)offer["cinders"] > 0 && ((JArray)offer["cardIds"]).Count > 1, "fixture offers cinders and a card choice");
        var legacy = OriginalGameSession.Restore(rewards); legacy.ContinueRewards();
        var manual = OriginalGameSession.Restore(rewards); manual.ContinueRewards("manual");
        var tapped = OriginalGameSession.Restore(rewards); tapped.Reward("cinders"); tapped.ContinueRewards();
        Check(JToken.DeepEquals(manual.Snapshot(), tapped.Snapshot()), "manual is the pre-setting Continue with the cinders collected");
        Check(((JArray)legacy.RunPlayer["deck"]).Count == deckBefore && (int)legacy.RunPlayer["cinders"] == cindersBefore && legacy.Phase == OriginalRunPhase.Map, "the argument-free Continue still leaves everything (replays)");
        Check(((JArray)manual.RunPlayer["deck"]).Count == deckBefore && (int)manual.RunPlayer["cinders"] == cindersBefore + (int)offer["cinders"] && manual.Phase == OriginalRunPhase.Map, "manual: cinders come along automatically, nothing else unchosen does");
        Check((int)manual.RunPlayer["streamCounters"]["cardRewards"] == (int)legacy.RunPlayer["streamCounters"]["cardRewards"], "manual draws no card pick");
        var manualSkipped = OriginalGameSession.Restore(rewards); manualSkipped.SkipReward("cinders"); manualSkipped.ContinueRewards("manual");
        Check(JToken.DeepEquals(Without(manualSkipped.Snapshot(), "streamCounters"), Without(legacy.Snapshot(), "streamCounters")) && (int)manualSkipped.RunPlayer["cinders"] == cindersBefore, "manual respects an explicit cinders Skip");
        var manualTaken = OriginalGameSession.Restore(rewards); manualTaken.Reward("cinders"); manualTaken.ContinueRewards("manual");
        Check((int)manualTaken.RunPlayer["cinders"] == cindersBefore + (int)offer["cinders"], "manual never grants cinders twice");
        var auto = OriginalGameSession.Restore(rewards); auto.ContinueRewards("auto");
        var reference = OriginalRunSession.Restore(rewards, callbacks); reference.ContinueRewards(true);
        Check(JToken.DeepEquals(auto.Snapshot()["run"], reference.Snapshot()["run"]), "auto is the run session's auto-collect (resolveContinue take-all)");
        Check(((JArray)auto.RunPlayer["deck"]).Count == deckBefore + 1 && (int)auto.RunPlayer["cinders"] == cindersBefore + (int)offer["cinders"], "auto: cinders and one offered card are taken");
        var picked = (string)((JArray)auto.RunPlayer["deck"]).Last["cardId"];
        var pickRng = new RandomStreams((uint)rewards["run"]["seed"], ((JObject)rewards["run"]["streamCounters"]).Properties().ToDictionary(p => p.Name, p => (uint)p.Value));
        Check(picked == (string)offer["cardIds"][pickRng.Int("cardRewards", 0, ((JArray)offer["cardIds"]).Count - 1)], "auto: the card is picked on the seeded cardRewards stream, as reward.js pickFn");
        foreach (var key in new[] { "hp", "maxHp", "floor", "mapNodeId", "phase", "actNumber", "path", "mapGraph" })
            Check(JToken.DeepEquals(auto.RunPlayer[key], manual.RunPlayer[key]), "auto changes only rewards, not " + key);
        Check(JToken.DeepEquals(auto.RunPlayer["streamCounters"]["map"], manual.RunPlayer["streamCounters"]["map"]), "auto leaves the map stream alone");
        var skipped = OriginalGameSession.Restore(rewards); skipped.SkipReward("card"); skipped.ContinueRewards("auto");
        Check(((JArray)skipped.RunPlayer["deck"]).Count == deckBefore && (int)skipped.RunPlayer["cinders"] == cindersBefore + (int)offer["cinders"], "auto respects an explicit Skip");
        var everything = OriginalGameSession.Restore(rewards); foreach (var kind in new[] { "cinders", "card", "flask", "armament", "relic" }) { try { everything.SkipReward(kind); } catch (ArgumentException) { } }
        everything.ContinueRewards("auto");
        Check(JToken.DeepEquals(Without(everything.Snapshot(), "streamCounters"), Without(legacy.Snapshot(), "streamCounters")) && (int)everything.RunPlayer["streamCounters"]["cardRewards"] == (int)legacy.RunPlayer["streamCounters"]["cardRewards"], "auto with every kind skipped equals the argument-free Continue (the playtest harness path)");
        var unknown = OriginalGameSession.Restore(rewards); unknown.ContinueRewards("sometimes");
        Check(JToken.DeepEquals(unknown.Snapshot(), manual.Snapshot()), "an unknown mode is the Unity default (manual)");
        var taken = OriginalGameSession.Restore(rewards); taken.Reward("card", (string)offer["cardIds"][0]); taken.ContinueRewards("auto");
        Check(((JArray)taken.RunPlayer["deck"]).Count == deckBefore + 1 && (string)((JArray)taken.RunPlayer["deck"]).Last["cardId"] == (string)offer["cardIds"][0], "auto never re-takes a row taken at tap time");
        var refused = OriginalGameSession.Restore(rewards); var threw = false;
        try { refused.SkipReward("card"); refused.SkipReward("card"); refused.SkipReward("bogus"); } catch (ArgumentException) { threw = true; }
        Check(threw, "skipping an unknown kind is refused");

        // ---- Weapon swap cost rule ----------------------------------------------------------------
        var equipment = (JObject)data["balance"]["equipment"];
        var fixture = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(root, "UnityTests/Parity/swap-reference.json")))["fixtures"].First(f => (string)f["kind"] == "energy");
        var service = new OriginalCombatEquipment(catalog, mechanics);
        JObject PriceUnder(string rule, int set)
        { var run = (JObject)fixture["run"].DeepClone(); run["profileMeta"] = new JObject { ["settings"] = OriginalGameplayOptions.ProfileSettings(new OriginalPlayerSettings { SwapCostRule = rule }) }; return service.Price(run, (JObject)fixture["before"], "rightHand", set); }
        int Expected(string ruleId, int set)
        {
            // HTML loadout.swapCostFor: base 'category' → first matching swapCostByCategory tag of the DRAWN piece, else swapCost; gear adds deltas.
            var row = equipment["swapCostRules"].First(r => (string)r["id"] == ruleId); var cost = (int)equipment["swapCost"];
            if ((string)row["base"] == "category")
            {
                var id = (string)fixture["run"]["loadout"]["sets"]["rightHand"][set]; var piece = catalog.Table("equipment.armaments").First(p => (string)p["id"] == id);
                var tags = catalog.Tags("armament", (JObject)piece); var hit = equipment["swapCostByCategory"].FirstOrDefault(r => tags.Contains((string)r["tag"]));
                if (hit != null) cost = (int)hit["cost"];
            }
            return cost; // the fixture wears no swap-cost gear, so gear contributes 0
        }
        foreach (var ruleId in OriginalPlayerSettings.SwapCostRuleIds)
            for (var set = 0; set < 2; set++)
            {
                var price = PriceUnder(ruleId, set);
                Check((string)price["ruleId"] == ruleId && (int)price["cost"] == Expected(ruleId, set), "rule " + ruleId + " set " + set + " prices " + Expected(ruleId, set));
                Check((string)price["base"] == (string)equipment["swapCostRules"].First(r => (string)r["id"] == ruleId)["base"] && (bool)price["gearOn"] == (bool)equipment["swapCostRules"].First(r => (string)r["id"] == ruleId)["gear"], "rule " + ruleId + " selects its own rungs only");
                Check(OriginalGameplayOptions.DescribeSwapPrice(price, equipment).StartsWith("Weapon swap cost · " + (string)equipment["swapCostRules"].First(r => (string)r["id"] == ruleId)["label"] + ":") && OriginalGameplayOptions.DescribeSwapPrice(price, equipment).EndsWith(price["cost"] + ((int)price["cost"] == 1 ? " action" : " actions")), "swap price line names " + ruleId + " and its price");
            }
        string Flat(JToken t) => t.ToString(Newtonsoft.Json.Formatting.None); // null categoryTag compares as text
        Check(Flat(PriceUnder("flat", (int)fixture["steps"][0]["setIndex"])) == Flat(fixture["steps"][0]["price"]), "flat (the default) reproduces the recorded original receipt");
        var noMeta = (JObject)fixture["run"].DeepClone(); noMeta.Remove("profileMeta");
        Check(JToken.DeepEquals(service.Price(noMeta, (JObject)fixture["before"], "rightHand", 1), PriceUnder("flat", 1)), "an absent setting prices as the content default rule");
        var bogus = (JObject)fixture["run"].DeepClone(); bogus["profileMeta"] = new JObject { ["settings"] = new JObject { ["swapCostRule"] = "both" } };
        Check(JToken.DeepEquals(service.Price(bogus, (JObject)fixture["before"], "rightHand", 1), PriceUnder("flat", 1)), "an unreadable rule id prices as the default (resolveSwapCostRule)");
        var swapped = service.Apply((JObject)PriceUnderRun("category"), (JObject)fixture["before"], "rightHand", 1);
        Check((string)swapped["combat"]["player"]["equipmentSwapRule"]["id"] == "category" && (int)swapped["receipt"]["cost"] == Expected("category", 1), "a swap charges and freezes the chosen rule for the fight");
        JObject PriceUnderRun(string rule) { var run = (JObject)fixture["run"].DeepClone(); run["profileMeta"] = new JObject { ["settings"] = OriginalGameplayOptions.ProfileSettings(new OriginalPlayerSettings { SwapCostRule = rule }) }; return run; }
        var frozenRun = PriceUnderRun("gear");
        Check((string)service.Price(frozenRun, (JObject)swapped["combat"], "rightHand", 0)["ruleId"] == "category", "a changed setting does not reprice a fight whose rule is frozen");

        // Threading through a live session in combat.
        var fight = OriginalRunSession.Restore(Start(5).Snapshot(), callbacks); fight.EnterNode(fight.LegalNodeIds().First(id => (string)fight.Map()["nodes"][id]["type"] == "monster"));
        var live = OriginalGameSession.Restore(fight.Snapshot());
        Check(live.Phase == OriginalRunPhase.Combat && (string)live.SwapPrice("rightHand", 0)["ruleId"] == "flat", "live fight prices with the default rule");
        var combatBefore = live.Snapshot()["run"]["room"]["combatSnapshot"].DeepClone();
        Check(live.ApplyProfileSettings(OriginalGameplayOptions.ProfileSettings(new OriginalPlayerSettings { SwapCostRule = "category" })) && (string)live.SwapPrice("rightHand", 0)["ruleId"] == "category", "a bound category setting reaches the live swap price");
        Check(JToken.DeepEquals(live.Snapshot()["run"]["room"]["combatSnapshot"], combatBefore) && live.Phase == OriginalRunPhase.Combat, "binding settings mid-fight leaves the fight untouched");

        // ---- Merchant buy-back --------------------------------------------------------------------
        var services = new OriginalRunServices(catalog);
        var shopRun = (JObject)Start(9).RunPlayer.DeepClone();
        shopRun["flasks"] = new JArray(catalog.Table("flasks").Select(f => new JObject { ["flaskId"] = f["id"].DeepClone() }));
        var sellOn = services.Sellables(shopRun).Count;
        shopRun["profileMeta"] = new JObject { ["settings"] = OriginalGameplayOptions.ProfileSettings(new OriginalPlayerSettings()) };
        Check(sellOn > 0 && services.Sellables(shopRun).Count == sellOn, "buy-back on (default) keeps every sell row");
        shopRun["profileMeta"]["settings"] = OriginalGameplayOptions.ProfileSettings(new OriginalPlayerSettings { ShopSell = false });
        Check(services.Sellables(shopRun).Count == 0, "buy-back off removes the sell rows");
        var noSell = Start(9); noSell.ApplyProfileSettings(OriginalGameplayOptions.ProfileSettings(new OriginalPlayerSettings { ShopSell = false }));
        Check(services.Sellables(noSell.RunPlayer).Count == 0 && (string)noSell.RunPlayer["profileMeta"]["settings"]["swapCostRule"] == "flat", "buy-back off reaches the live run and leaves the swap rule at its default");
        return checks;
    }
}
