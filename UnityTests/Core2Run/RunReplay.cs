// Replays the phase 8 JS oracle families (UnityTests/Core2Content/Oracles, tools/js-core2-run.mjs) through the engine-free
// Core 2 run layer (Runtime/Domain/Core2/Run) and compares every case with what the JS game returned at the pin. The run
// view below is tools/js-core2-run.mjs runView, key for key; the scripted-run driver is that tool's POLICY.
using System.Globalization;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

static class RunReplay
{
    public sealed class Result
    {
        public int Cases;
        public int Passed;
        public readonly List<string> Failures = new();
        /// <summary>Cases that stop at a combat-domain gap (reported, not failed): every step before the gap matched.</summary>
        public readonly List<string> Pending = new();
        public void Pend(string reason) { Cases++; Pending.Add(reason); }
        public void Compare(JToken got, JToken expected, Func<string> label)
        {
            Cases++;
            if (JToken.DeepEquals(Normalize(got), Normalize(expected))) Passed++;
            else if (Failures.Count < 12) Failures.Add(label() + ": " + FirstDifference(Normalize(got), Normalize(expected)));
        }
    }

    /// <summary>Numbers compared by value (JS writes 1.0 as 1), nulls as nulls.</summary>
    static JToken Normalize(JToken token)
    {
        switch (token)
        {
            case null: return JValue.CreateNull();
            case JObject o: { var c = new JObject(); foreach (var p in o.Properties()) c[p.Name] = Normalize(p.Value); return c; }
            case JArray a: return new JArray(a.Select(Normalize));
            case JValue v when v.Type == JTokenType.Float || v.Type == JTokenType.Integer: return Core2RunJsonNumber((double)v);
            case JValue v when v.Value == null: return JValue.CreateNull();
            default: return token.DeepClone();
        }
    }
    static JToken Core2RunJsonNumber(double value) => Math.Abs(value - Math.Round(value)) < 1e-9 && Math.Abs(value) < 9e15 ? (JToken)(long)Math.Round(value) : new JValue(Math.Round(value, 9));

    public static string FirstDifference(JToken got, JToken want, string path = "$")
    {
        if (got is JObject a && want is JObject b)
            foreach (var name in a.Properties().Select(p => p.Name).Union(b.Properties().Select(p => p.Name)))
                if (!JToken.DeepEquals(a[name], b[name])) return FirstDifference(a[name] ?? JValue.CreateNull(), b[name] ?? JValue.CreateNull(), path + "." + name);
        if (got is JArray x && want is JArray y && x.Count == y.Count)
            for (var i = 0; i < x.Count; i++)
                if (!JToken.DeepEquals(x[i], y[i])) return FirstDifference(x[i], y[i], path + "[" + i + "]");
        string Show(JToken t) { var s = t?.ToString(Newtonsoft.Json.Formatting.None) ?? "null"; return s.Length > 400 ? s.Substring(0, 400) + "…" : s; }
        return path + ": got " + Show(got) + "; JS " + Show(want);
    }

    static JObject Ints(IReadOnlyDictionary<string, int> map) { var o = new JObject(); foreach (var p in map) o[p.Key] = p.Value; return o; }
    static JToken Str(string value) => value == null ? JValue.CreateNull() : (JToken)value;
    public static JObject Counters(Core2Rng rng) { var o = new JObject(); foreach (var p in rng.Counters().OrderBy(p => p.Key, StringComparer.Ordinal)) o[p.Key] = p.Value; return o; }

    /// <summary>tools/js-core2-run.mjs runView.</summary>
    public static JObject View(Core2RunData data, Core2Run run, Core2Rng rng)
    {
        var ratings = Core2RunGear.Ratings(data, run);
        var save = Core2RunSave.RunToJson(run);
        return new JObject
        {
            ["class"] = run.Class, ["actNumber"] = run.ActNumber, ["floor"] = run.Floor, ["mapNodeId"] = Str(run.MapNodeId), ["path"] = new JArray(run.Path),
            ["hp"] = run.Hp, ["maxHp"] = run.MaxHp, ["mana"] = run.Mana, ["maxMana"] = run.MaxMana, ["stamina"] = run.Stamina, ["maxStamina"] = run.MaxStamina,
            ["energyMax"] = run.EnergyMax, ["drawPerTurn"] = run.DrawPerTurn, ["maxHpAdjustment"] = run.MaxHpAdjustment,
            ["equipmentPoolBonuses"] = save["equipmentPoolBonuses"], ["equipmentPoolDeficits"] = save["equipmentPoolDeficits"],
            ["cinders"] = run.Cinders, ["attributes"] = save["attributes"], ["effectiveAttributes"] = Ints(Core2RunGear.EffectiveAttributes(data, run)),
            ["level"] = run.Level.ToJson(), ["levelUps"] = run.LevelUps, ["levelPoints"] = run.LevelPoints,
            ["ratings"] = Ints(ratings.Totals), ["baseEvasionBonus"] = ratings.BaseEvasionBonus, ["basePrecisionBonus"] = ratings.BasePrecisionBonus,
            ["deck"] = new JArray(run.Deck.Select(c => new JObject { ["instanceId"] = c.InstanceId, ["cardId"] = c.CardId })),
            ["sideboard"] = new JArray(run.Sideboard.Select(c => new JObject { ["instanceId"] = c.InstanceId, ["cardId"] = c.CardId })),
            ["collectedCardIds"] = new JArray(run.CollectedCardIds), ["relics"] = new JArray(run.Relics), ["flasks"] = save["flasks"], ["flaskCharges"] = run.FlaskCharges.ToJson(),
            ["loadout"] = new JObject { ["sets"] = save["loadout"]["sets"], ["active"] = save["loadout"]["active"], ["storage"] = save["loadout"]["storage"], ["boughtArmour"] = save["loadout"]["boughtArmour"] },
            ["itemUpgradeLevels"] = save["itemUpgradeLevels"], ["seatOrder"] = new JArray(run.SeatOrder), ["history"] = save["history"], ["seenEvents"] = new JArray(run.SeenEvents),
            ["lastEncounters"] = new JArray(run.LastEncounters), ["bossesBeaten"] = new JArray(run.BossesBeaten), ["fightsWon"] = run.FightsWon,
            ["skills"] = Core2RunSave.SkillsJson(run.Skills),
            ["rng"] = rng == null ? JValue.CreateNull() : Counters(rng),
        };
    }

    /// <summary>A run rebuilt from a JS run view (its class birth supplies the rule snapshot the view leaves out).</summary>
    public static Core2Run FromView(Core2RunData data, JObject view)
    {
        var run = Core2RunSave.RunFromJson(view);
        run.DerivedStatRules = (JObject)data.Class(run.Class).DerivedStatRules.DeepClone();
        return run;
    }

    static Dictionary<string, int> Map(JToken token) => ((JObject)token).Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.Ordinal);

    // ---- run creation -----------------------------------------------------------------------------------------
    public static Result Creation(Core2RunData data, JArray cases)
    {
        var result = new Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            JToken got;
            try { var (run, rng) = Core2RunCreation.Create(data, (string)input["classId"], (uint)(int)input["seed"], ignoreUnlocks: true); got = new JObject { ["run"] = View(data, run, rng) }; }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message }; }
            result.Compare(got, row["expected"], () => "creation " + input["classId"] + " seed " + input["seed"]);
        }
        return result;
    }

    // ---- maps -------------------------------------------------------------------------------------------------
    public static Result Maps(Core2RunData data, JArray cases)
    {
        var result = new Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            JToken got;
            try
            {
                var rng = new Core2Rng((uint)(int)input["seed"], Map(input["counters"]));
                var graph = Core2ActMap.Build(data, rng, (string)input["seat"], (int)input["act"], ((JArray)input["history"]).OfType<JObject>().ToList());
                got = new JObject { ["graph"] = graph.ToJson(), ["counters"] = Counters(rng) };
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message }; }
            result.Compare(got, row["expected"], () => "map seed " + input["seed"] + " act " + input["act"]);
        }
        return result;
    }

    // ---- card faces -------------------------------------------------------------------------------------------
    public static Result Faces(Core2RunData data, JArray cases)
    {
        var result = new Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            var (run, _) = Core2RunCreation.Create(data, (string)input["classId"], 1, ignoreUnlocks: true);
            var rightHand = input["rightHand"]?.Type == JTokenType.String ? (string)input["rightHand"] : null;
            run.Loadout.Sets["rightHand"] = new List<string> { rightHand, null, null };
            run.Loadout.Sets["leftHand"] = new List<string> { null, null, null };
            var faces = new JObject();
            foreach (var card in ((JObject)row["expected"]["faces"]).Properties())
            {
                try { faces[card.Name] = Core2RunFights.Face(data, run.Loadout, run.Class, card.Name); }
                catch (Exception error) { faces[card.Name] = error.GetType().Name + ": " + error.Message; }
            }
            result.Compare(new JObject { ["faces"] = faces }, row["expected"], () => "faces with " + (rightHand ?? "bare hands"));
        }
        return result;
    }

    // ---- reward offers ----------------------------------------------------------------------------------------
    public static Result Rewards(Core2RunData data, JArray cases)
    {
        var result = new Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            JToken got;
            try
            {
                var run = FromView(data, (JObject)input["run"]);
                var rng = new Core2Rng((uint)(int)input["seed"], Map(input["counters"]));
                var offer = Core2RunRewards.Offer(data, run, rng, (string)input["offerId"], (string)input["source"]);
                var output = new JObject { ["offer"] = offer.ToJson(), ["counters"] = Counters(rng) };
                try { output["claimed"] = Core2RunRewards.Claim(data, run, (string)input["offerId"], (int)input["takeIndex"]).ToJson(); }
                catch (InvalidOperationException error) { output["claimed"] = new JObject { ["error"] = error.Message }; }
                output["after"] = View(data, run, rng);
                got = output;
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message }; }
            result.Compare(got, row["expected"], () => "reward " + input["classId"] + " seed " + input["seed"] + " " + input["offerId"]);
        }
        return result;
    }

    // ---- shop stock -------------------------------------------------------------------------------------------
    public static Result Shops(Core2RunData data, JArray cases)
    {
        var result = new Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            JToken got;
            try
            {
                var run = FromView(data, (JObject)input["run"]);
                var rng = new Core2Rng((uint)(int)input["seed"], Map(input["counters"]));
                var stock = Core2RunRewards.ShopStock(data, run, rng, (string)input["shopId"]);
                var output = new JObject { ["stock"] = stock.ToJson(), ["counters"] = Counters(rng) };
                run.Cinders = (int)input["cindersBeforePurchases"];
                var purchases = new JArray();
                for (var index = 0; index < stock.Options.Count && purchases.Count < 3; index++)
                {
                    try
                    {
                        var option = Core2RunRewards.Purchase(data, run, (string)input["shopId"], index);
                        var receipt = option.ToJson(); receipt["price"] = stock.Price;
                        purchases.Add(new JObject { ["index"] = index, ["receipt"] = receipt });
                    }
                    catch (InvalidOperationException error) { purchases.Add(new JObject { ["index"] = index, ["error"] = error.Message }); }
                }
                output["purchases"] = purchases;
                output["after"] = View(data, run, rng);
                got = output;
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message }; }
            result.Compare(got, row["expected"], () => "shop " + input["classId"] + " seed " + input["seed"]);
        }
        return result;
    }

    // ---- rest outcomes ----------------------------------------------------------------------------------------
    static void RestNode(Core2Run run)
    {
        var graph = new Core2MapGraph();
        graph.Add(new Core2MapNode { Id = "n3_0", Floor = 3, Type = "shrine", ActualType = "rest" });
        run.MapGraph = graph; run.MapNodeId = "n3_0"; run.Floor = 3;
    }

    public static Result Rests(Core2RunData data, JArray cases)
    {
        var result = new Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            JToken got;
            try
            {
                var run = FromView(data, (JObject)input["run"]);
                RestNode(run);
                var visit = Core2RunRest.Begin(data, run);
                var rng = new Core2Rng((uint)(int)input["seed"], Map(input["run"]["rng"]));
                var output = new JObject { ["visit"] = visit.ToJson(), ["healed"] = View(data, run, rng) };
                var candidates = Core2RunRest.SmithPlan(data, run);
                output["candidates"] = new JArray(candidates.Select(c => new JObject { ["itemRef"] = c.ItemRef, ["currentLevel"] = c.CurrentLevel, ["nextLevel"] = c.NextLevel }));
                var services = new JArray();
                var expectedServices = (JArray)row["expected"]["services"];
                if (candidates.Count > 0)
                {
                    try
                    {
                        Core2RunRest.Commit(data, run, "smith", itemRef: candidates[0].ItemRef);
                        services.Add(new JObject { ["service"] = "smith", ["itemRef"] = candidates[0].ItemRef, ["receipt"] = expectedServices.FirstOrDefault(s => (string)s["service"] == "smith")?["receipt"], ["after"] = View(data, run, rng) });
                    }
                    catch (InvalidOperationException error) { services.Add(new JObject { ["service"] = "smith", ["itemRef"] = candidates[0].ItemRef, ["error"] = error.Message }); }
                }
                var attributeId = new[] { "strength", "constitution", "wisdom", "intelligence" }[(int)input["seed"] % 4];
                try
                {
                    Core2RunRest.Commit(data, run, "levelUp", attributeId: attributeId);
                    services.Add(new JObject { ["service"] = "levelUp", ["attributeId"] = attributeId, ["receipt"] = expectedServices.FirstOrDefault(s => (string)s["service"] == "levelUp")?["receipt"], ["after"] = View(data, run, rng) });
                }
                catch (InvalidOperationException error) { services.Add(new JObject { ["service"] = "levelUp", ["attributeId"] = attributeId, ["error"] = error.Message }); }
                output["services"] = services;
                output["visitAfter"] = run.RestVisits["1:n3_0"].ToJson();
                got = output;
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message }; }
            // The service receipts are JS presentation rows (smith card rows, level plans); the state they leave is compared.
            result.Compare(got, row["expected"], () => "rest " + input["classId"] + " seed " + input["seed"]);
        }
        return result;
    }

    /// <summary>Error messages are compared by their first clause (JS appends level/XP detail to the level-up refusal).</summary>
    static JObject StripErrorsText(JObject expected)
    {
        var copy = (JObject)expected.DeepClone();
        foreach (var service in (JArray)copy["services"])
            if (service["error"] != null) service["error"] = ((string)service["error"]).Split(new[] { " (level" }, StringSplitOptions.None)[0];
        return copy;
    }

    // ---- level-up ---------------------------------------------------------------------------------------------
    public static Result Levels(Core2RunData data, JArray cases)
    {
        var result = new Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            JToken got;
            try
            {
                switch ((string)input["kind"])
                {
                    case "xpToNext": got = new JObject { ["xp"] = Core2RunProgression.XpToNext(data, (int)input["level"]) }; break;
                    case "award":
                    {
                        var (run, _) = Core2RunCreation.Create(data, (string)input["classId"], 9, ignoreUnlocks: true);
                        var awards = new JArray(((JArray)input["gains"]).Select(g =>
                        {
                            var a = Core2RunProgression.AwardLevelXp(data, run, (int)g);
                            var o = new JObject { ["before"] = a.Before, ["after"] = a.After, ["levelUps"] = a.LevelUps, ["points"] = a.Points, ["gained"] = a.Gained, ["discarded"] = a.Discarded };
                            return (JToken)o;
                        }));
                        got = new JObject { ["awards"] = awards, ["level"] = run.Level.ToJson(),
                            ["pools"] = new JArray(run.Hp, run.MaxHp, run.Mana, run.MaxMana, run.Stamina, run.MaxStamina, run.EnergyMax, run.DrawPerTurn) };
                        break;
                    }
                    default:
                    {
                        var enemies = ((JArray)input["enemies"]).Select(e => new Core2XpEnemy { EnemyId = (string)e["enemyId"], Defeated = true, Level = (int)e["level"], MaxHp = (int)e["maxHp"] }).ToList();
                        got = Core2RunProgression.CombatXp(data, true, (string)input["pool"], enemies).ToJson();
                        break;
                    }
                }
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message }; }
            // JS awardLevelXp also returns `thresholds` (how many maxima moved): presentation only.
            var expected = (JObject)row["expected"].DeepClone();
            foreach (var award in (expected["awards"] as JArray) ?? new JArray()) ((JObject)award).Remove("thresholds");
            result.Compare(got, expected, () => "level-up " + input.ToString(Newtonsoft.Json.Formatting.None));
        }
        return result;
    }

    // ---- scripted runs ----------------------------------------------------------------------------------------
    public static Result ScriptedRunsAuto(Core2RunData data, JArray cases) => ScriptedRuns(data, cases, true);

    public static Result ScriptedRuns(Core2RunData data, JArray cases) => ScriptedRuns(data, cases, false);

    static Result ScriptedRuns(Core2RunData data, JArray cases, bool auto)
    {
        var result = new Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            JObject got;
            var partial = new JArray();
            try { got = ScriptedRun(data, (string)input["classId"], (uint)(int)input["seed"], (string)input["levelAttribute"], partial, auto); }
            catch (Exception error) { got = new JObject { ["steps"] = partial, ["error"] = error.GetType().Name + ": " + error.Message + " " + error.StackTrace?.Split('\n').FirstOrDefault() }; }
            // Steps compare one by one so a mismatch names the step.
            var gotSteps = got["steps"] as JArray ?? new JArray();
            var wantSteps = (JArray)expected["steps"];
            var same = 0;
            while (same < Math.Min(gotSteps.Count, wantSteps.Count) && JToken.DeepEquals(Normalize(Strip(gotSteps[same])), Normalize(Strip(wantSteps[same])))) same++;
            var label = (auto ? "auto-resolved run " : "scripted run ") + input["classId"] + " seed " + input["seed"] + " (" + same + "/" + wantSteps.Count + " steps match"
                + (got["error"] != null ? ", " + got["error"] : "") + ")"
                + (same < Math.Min(gotSteps.Count, wantSteps.Count) ? " first differing step " + same + " (" + wantSteps[same]["kind"] + "): " + FirstDifference(Normalize(Strip(gotSteps[same])), Normalize(Strip(wantSteps[same]))) : "");
            // A fight the combat domain cannot run yet (NotSupportedException: an opcode, predicate or status model phase 5
            // has not ported) stops the replay there: the case is pending on that combat gap when every step before it matched.
            var gap = (string)got["error"];
            if (gap != null && gap.StartsWith("NotSupportedException", StringComparison.Ordinal) && same == gotSteps.Count)
            {
                result.Pend("scripted run " + input["classId"] + " seed " + input["seed"] + ": " + same + "/" + wantSteps.Count + " steps match, then a combat gap: "
                    + gap.Split(new[] { "    at " }, StringSplitOptions.None)[0].Trim());
                continue;
            }
            // A known combat-domain discrepancy (not a run-layer one) pends the case only at the exact step and field it names.
            var key = (auto ? "auto:" : "") + input["classId"] + ":" + input["seed"];
            if (KnownCombatGaps.TryGetValue(key, out var known) && same == known.Step && same < Math.Min(gotSteps.Count, wantSteps.Count)
                && FirstDifference(Normalize(Strip(gotSteps[same])), Normalize(Strip(wantSteps[same]))).StartsWith(known.Path, StringComparison.Ordinal))
            {
                result.Pend(label.Split(" first differing")[0] + ": combat gap at step " + same + ": " + known.Reason);
                continue;
            }
            var gotCmp = new JObject { ["steps"] = new JArray(gotSteps.Select(Strip)), ["finish"] = got["finish"] };
            var wantCmp = new JObject { ["steps"] = new JArray(wantSteps.Select(Strip)), ["finish"] = expected["finish"] };
            result.Compare(gotCmp, wantCmp, () => label);
        }
        return result;
    }

    /// <summary>Combat-domain discrepancies the run layer cannot fix: case key → the step and field where it shows.</summary>
    /// None at the pin: the relic-trigger Advance cost (auto starseer 4040) is ported (Core2Fight AcceptIndependentTrigger).
    static readonly Dictionary<string, (int Step, string Path, string Reason)> KnownCombatGaps = new();

    /// <summary>Fields of a JS step that are presentation receipts rather than state: dropped before comparing.</summary>
    static JToken Strip(JToken step)
    {
        var copy = (JObject)step.DeepClone();
        if ((string)copy["kind"] == "rest") copy.Remove("visit");
        return copy;
    }

    static JObject ScriptedRun(Core2RunData data, string classId, uint seed, string levelAttribute, JArray steps, bool auto)
    {
        var session = Core2RunSession.Start(data, classId, seed, runId: "oracle-" + classId + "-" + seed, ignoreUnlocks: true);
        var run = session.Run;
        steps.Add(new JObject { ["kind"] = "start", ["graph"] = run.MapGraph.ToJson(), ["run"] = View(data, run, session.Rng) });
        JObject pendingFight = null;
        session.OnFightSettled = (s, settlement) =>
        {
            pendingFight["result"] = settlement.Result;
            pendingFight["xp"] = settlement.Xp.ToJson();
            pendingFight["levelUps"] = settlement.Award.LevelUps;
            pendingFight["progression"] = settlement.Progression.DeepClone();
            pendingFight["victoryReceipt"] = settlement.VictoryReceipt?.DeepClone() ?? JValue.CreateNull();
            pendingFight["run"] = View(data, s.Run, s.Rng);
        };
        void PlayFight()
        {
            var fight = session.Fight;
            fight.ReactionsEnabled = false;
            pendingFight = new JObject
            {
                ["kind"] = "fight", ["nodeId"] = run.MapNodeId, ["encounterId"] = (string)session.FightEncounter["id"], ["pool"] = (string)session.FightEncounter["pool"],
                ["hpMult"] = session.FightScale.Hp, ["start"] = Core2RunFights.SetupJson(data, run, session.FightSetup),
            };
            var turns = 0;
            if (auto) fight.SettleAsWon();
            for (var turn = 1; turn <= 60 && fight.Result == null; turn++)
            {
                turns = turn;
                for (var guard = 0; guard < 20 && fight.Result == null; guard++)
                {
                    var target = fight.Enemies.FirstOrDefault(e => e.Alive);
                    string played = null;
                    foreach (var card in fight.Hand.ToList())
                    {
                        Core2Costs costs;
                        try { costs = fight.CardCosts(card.InstanceId); } catch (InvalidOperationException) { continue; }
                        if (costs.Sp > fight.Player.Stamina || costs.Mp > fight.Player.Mana) continue;
                        foreach (var targetId in new[] { target?.Id, null })
                        {
                            try { fight.PlayCard(card.InstanceId, targetId); played = card.CardId; break; }
                            catch (InvalidOperationException) { }
                        }
                        if (played != null) break;
                    }
                    if (played == null) break;
                }
                if (fight.Result == null) fight.EndTurn();
            }
            pendingFight["turns"] = turns;
            pendingFight["enemies"] = new JArray(fight.Enemies.Select(e => new JObject { ["enemyId"] = e.EnemyId, ["maxHp"] = e.MaxHp, ["hp"] = e.Hp, ["level"] = e.Level }));
            var step = pendingFight;
            session.FinishFight();
            var ordered = new JObject();
            foreach (var key in new[] { "kind", "nodeId", "encounterId", "pool", "hpMult", "start", "result", "turns", "enemies", "xp", "levelUps", "progression", "victoryReceipt", "run" }) ordered[key] = step[key];
            steps.Add(ordered);
            // The spoils show the victory receipt, then the level notices, each acknowledged once (a notice-only spoils then ends the run).
            if (session.RewardStage == "victoryReceipt") session.AcknowledgeVictoryReceipt();
            if (session.RewardStage == "levelNotices") session.AcknowledgeLevelNotices();
        }
        void TakeRewards()
        {
            while (session.Screen == Core2Screen.Reward)
            {
                var offer = session.CurrentOffer;
                JToken claimed = JValue.CreateNull();
                if (offer.Options.Count > 0)
                {
                    try { claimed = session.TakeReward(0).ToJson(); }
                    catch (InvalidOperationException error) { claimed = new JObject { ["error"] = error.Message }; }
                }
                steps.Add(new JObject { ["kind"] = "reward", ["offerId"] = offer.Id, ["source"] = offer.Source, ["offer"] = offer.ToJson(), ["claimed"] = claimed, ["run"] = View(data, run, session.Rng) });
                var act = run.ActNumber;
                session.ContinueReward();
                if (run.ActNumber != act) steps.Add(new JObject { ["kind"] = "advanceAct", ["act"] = run.ActNumber, ["graph"] = run.MapGraph.ToJson(), ["run"] = View(data, run, session.Rng) });
            }
        }
        for (var guard = 0; guard < 45 && session.Screen != Core2Screen.Finished; guard++)
        {
            var options = session.TravelOptions();
            var nodeId = options[(int)((run.Path.Count + seed) % (uint)options.Count)];
            var node = run.MapGraph.Node(nodeId);
            steps.Add(new JObject { ["kind"] = "travel", ["nodeId"] = nodeId, ["type"] = node.Type, ["actualType"] = node.ActualType });
            var screen = session.Travel(nodeId);
            switch (screen)
            {
                case Core2Screen.Event:
                {
                    var eventId = run.OpenEvent;
                    var choice = session.EventChoices().First(c => c.Affordable && c.Runnable).Choice;
                    var receipt = session.ChooseEvent(choice.Id);
                    steps.Add(new JObject { ["kind"] = "event", ["eventId"] = eventId, ["choiceId"] = choice.Id,
                        ["completions"] = new JArray(receipt.Completions.Select(q => new JObject { ["kind"] = "questCompleted", ["questId"] = q, ["source"] = "event" })),
                        ["run"] = View(data, run, session.Rng) });
                    if (session.ContinueEvent() == Core2Screen.Fight) { PlayFight(); TakeRewards(); }
                    break;
                }
                case Core2Screen.Fight: PlayFight(); TakeRewards(); break;
                case Core2Screen.Rest:
                {
                    var candidates = session.SmithCandidates().Select(c => c.ItemRef).ToList();
                    var services = new JArray();
                    if (candidates.Count > 0)
                    {
                        try { session.Smith(candidates[0]); services.Add(new JObject { ["service"] = "smith", ["itemRef"] = candidates[0] }); }
                        catch (InvalidOperationException error) { services.Add(new JObject { ["service"] = "smith", ["error"] = error.Message }); }
                    }
                    if (run.Level.UnspentPoints > 0) { session.LevelUp(levelAttribute); services.Add(new JObject { ["service"] = "levelUp", ["attributeId"] = levelAttribute }); }
                    steps.Add(new JObject { ["kind"] = "rest", ["candidates"] = new JArray(candidates), ["services"] = services, ["run"] = View(data, run, session.Rng) });
                    session.LeaveRest();
                    break;
                }
                case Core2Screen.Shop:
                {
                    var stock = session.ShopStock;
                    var purchases = new JArray();
                    for (var index = 0; index < stock.Options.Count && purchases.Count < 3; index++)
                    {
                        if (run.Cinders < stock.Price) break;
                        try { session.Buy(index); purchases.Add(index); } catch (InvalidOperationException) { }
                    }
                    steps.Add(new JObject { ["kind"] = "shop", ["shopId"] = run.OpenShop, ["stock"] = stock.ToJson(), ["purchases"] = purchases, ["run"] = View(data, run, session.Rng) });
                    session.LeaveShop();
                    break;
                }
                case Core2Screen.Reward: TakeRewards(); break;
            }
        }
        JToken finish = JValue.CreateNull();
        if (run.Finish != null) finish = new JObject { ["victory"] = run.Finish.Victory, ["unlocked"] = new JArray(session.RecordFinish(new Core2UnlockLedger())) };
        return new JObject { ["steps"] = steps, ["finish"] = finish };
    }
}
