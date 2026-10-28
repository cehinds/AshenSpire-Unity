// Replays the phase 9 and 10 JS oracle families (tools/js-core2-run.mjs progressFamilies) through the engine-free Core 2
// run layer: adept-entitlements, deck-edits, armoury and skill-progression. Each case's input carries the run state its
// operations start from and the operations themselves; the output is built key for key as the JS family builds it.
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

static class ProgressReplay
{
    static JToken Null => JValue.CreateNull();
    static JToken Int(int? value) => value.HasValue ? (JToken)value.Value : Null;

    static JObject PileView(Core2Run run) => new JObject
    {
        ["deck"] = new JArray(run.Deck.Select(c => c.InstanceId + "=" + c.CardId)), ["sideboard"] = new JArray(run.Sideboard.Select(c => c.InstanceId + "=" + c.CardId)),
        ["collectedCardIds"] = new JArray(run.CollectedCardIds ?? new List<string>()), ["editMintCounter"] = Int(run.EditMintCounter),
    };

    static Core2Run State(Core2RunData data, JToken view) => RunReplay.FromView(data, (JObject)view);

    // ---- adept entitlements -----------------------------------------------------------------------------------------
    public static RunReplay.Result Adept(Core2RunData data, JArray cases)
    {
        var result = new RunReplay.Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            JToken got;
            try
            {
                var run = State(data, input["run"]);
                if ((string)input["label"] == "granted")
                {
                    var granted = ((JArray)input["granted"]).Select(Core2RunCard.FromJson).ToList();
                    var worn = new JArray(granted.Select(c => Core2RunDeck.EquipmentEligible(data, run, c)));
                    var probe = Core2RunSave.Clone(run);
                    probe.Loadout.Sets["rightHand"] = new List<string> { null, null, null };
                    got = new JObject { ["worn"] = worn, ["bare"] = new JArray(granted.Select(c => Core2RunDeck.EquipmentEligible(data, probe, c))) };
                }
                else
                {
                    var problems = Core2RunDeck.CollectedCardProblems(data, run);
                    JToken recorded = Null;
                    if (problems.Count == 0) recorded = Core2RunDeck.RecordCollectedCards(data, run);
                    var cards = Core2Json.Strings(input["cards"]);
                    var limits = new JObject();
                    foreach (var id in cards) limits[id] = Int(Core2RunDeck.CopyLimit(data, run, id));
                    var eligibility = new JObject();
                    foreach (var hand in (JArray)input["hands"])
                    {
                        var probe = Core2RunSave.Clone(run);
                        probe.Loadout.Sets["rightHand"] = new List<string> { hand.Type == JTokenType.Null ? null : (string)hand, null, null };
                        probe.Loadout.Sets["leftHand"] = new List<string> { null, null, null };
                        eligibility[hand.Type == JTokenType.Null ? "none" : (string)hand] = new JArray(cards.Where(id => Core2RunDeck.EquipmentEligible(data, probe, new Core2RunCard { CardId = id })));
                    }
                    got = new JObject
                    {
                        ["problems"] = new JArray(problems), ["recorded"] = recorded, ["known"] = new JArray(Core2RunDeck.KnownCollectedCardIds(data, run)),
                        ["training"] = Core2RunDeck.AdeptTraining(data, run)?.DeepClone() ?? Null, ["adeptIds"] = new JArray(Core2RunDeck.AdeptCardIds(data, run)),
                        ["unlimited"] = new JArray(Core2RunDeck.UnlimitedCardIds(data, run)), ["copyLimits"] = limits, ["collectedAfter"] = new JArray(run.CollectedCardIds),
                        ["eligibility"] = eligibility,
                    };
                }
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message }; }
            result.Compare(got, row["expected"], () => "adept " + input["classId"] + " " + input["label"]);
        }
        return result;
    }

    // ---- deck edits -------------------------------------------------------------------------------------------------
    static JArray Tiles(Core2RunData data, Core2Run run) => new JArray(Core2RunDeck.Tiles(data, run).OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => new JObject
    {
        ["key"] = t.Key, ["unlimited"] = t.Unlimited, ["owned"] = Int(t.Owned), ["inDeck"] = t.InDeck, ["addable"] = t.Addable, ["refused"] = t.Refusal != null,
        ["removeInstanceId"] = t.RemoveInstanceId == null ? Null : (JToken)t.RemoveInstanceId,
    }));

    public static RunReplay.Result DeckEdits(Core2RunData data, JArray cases)
    {
        var result = new RunReplay.Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            JToken got;
            try
            {
                var run = State(data, input["run"]);
                var settings = Core2DeckSettings.From(data, (JObject)input["settings"]);
                var session = Core2DeckEdit.Open(data, run, settings);
                var results = new JArray { new JObject { ["op"] = "open", ["tiles"] = Tiles(data, run), ["piles"] = PileView(run) } };
                var closed = false;
                foreach (var op in ((JArray)input["ops"]).OfType<JObject>())
                {
                    var kind = (string)op["op"];
                    var outRow = new JObject { ["op"] = kind };
                    try
                    {
                        switch (kind)
                        {
                            case "add": { var r = session.Add((string)op["key"]); outRow["ok"] = r.Ok; outRow["refused"] = !string.IsNullOrEmpty(r.Refusal); break; }
                            case "remove": outRow["ok"] = session.Remove((string)op["instanceId"]).Ok; break;
                            case "undo": outRow["ok"] = session.Undo(); break;
                            case "moveTo": outRow["ok"] = session.MoveTo((string)op["instanceId"], (int)op["index"]); break;
                            case "tiles": outRow["tiles"] = Tiles(data, run); break;
                            case "confirm":
                                if (closed) outRow["skipped"] = true;
                                else
                                {
                                    var r = session.Confirm(); outRow["ok"] = r.Ok; outRow["refusal"] = r.Refusal;
                                    if (!r.Ok) { session.Cancel(); outRow["cancelled"] = true; }
                                    closed = true;
                                }
                                break;
                        }
                    }
                    catch (InvalidOperationException error) { outRow["error"] = error.Message; }
                    outRow["piles"] = PileView(run);
                    results.Add(outRow);
                }
                // A won card straight into the deck, on the offer JS rolled (its run state carried with it).
                var offerRun = State(data, input["offerRun"]);
                offerRun.Deck = run.Deck; offerRun.Sideboard = run.Sideboard; offerRun.CollectedCardIds = run.CollectedCardIds; offerRun.EditMintCounter = run.EditMintCounter;
                var offer = Core2RewardOffer.FromJson(input["offer"]);
                offerRun.RewardOffers[offer.Id] = offer;
                var refusals = new JArray(offer.Options.Select((_, i) => (JToken)Core2RunDeck.RewardDeckAddRefusal(data, offerRun, offer.Id, i, settings)));
                var cardIndex = offer.Options.FindIndex(o => o.Kind == "combatCards" || o.Kind == "weaponArts");
                JToken toDeck = Null;
                if (cardIndex >= 0)
                {
                    JObject t;
                    try { t = new JObject { ["index"] = cardIndex, ["option"] = Core2RunDeck.ClaimRewardToDeck(data, offerRun, offer.Id, cardIndex, settings).ToJson(), ["piles"] = PileView(offerRun) }; }
                    catch (Core2DeckRefused refused) { t = new JObject { ["index"] = cardIndex, ["error"] = refused.Message, ["code"] = refused.Code }; }
                    t["againRefusal"] = Core2RunDeck.RewardDeckAddRefusal(data, offerRun, offer.Id, cardIndex, settings);
                    toDeck = t;
                }
                got = new JObject
                {
                    ["results"] = results, ["refusals"] = refusals, ["toDeck"] = toDeck,
                    ["bounds"] = new JObject { ["count10"] = Core2RunDeck.EditRefusal(10, settings), ["count9"] = Core2RunDeck.EditRefusal(9, settings),
                        ["count41"] = Core2RunDeck.EditRefusal(41, settings), ["count1"] = Core2RunDeck.EditRefusal(1, settings) },
                };
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message + " " + error.StackTrace?.Split('\n').FirstOrDefault() }; }
            result.Compare(got, row["expected"], () => "deck edits " + input["classId"] + " seed " + input["seed"] + " " + input["settingsLabel"]);
        }
        return result;
    }

    // ---- armoury ----------------------------------------------------------------------------------------------------
    public static RunReplay.Result Armoury(Core2RunData data, JArray cases)
    {
        var result = new RunReplay.Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            JToken got;
            try
            {
                var run = State(data, input["run"]);
                var rng = new Core2Rng((uint)(int)input["seed"], ((JObject)input["run"]["rng"]).Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.Ordinal));
                var newBefore = new JArray(Core2RunArmoury.NewItems(run));
                var glow = Core2RunArmoury.HasNew(data, run);
                var results = new JArray();
                foreach (var op in ((JArray)input["ops"]).OfType<JObject>())
                {
                    if ((bool?)op["fillStorage"] == true)
                    {
                        var cap = (int)data.EquipmentBalance["storageSlots"];
                        foreach (var id in data.Content.Armaments.Select(a => a.Id))
                        {
                            if (run.Loadout.Storage.Count >= cap) break;
                            if (!run.Loadout.Storage.Contains(id) && !run.Loadout.Sets.Values.SelectMany(v => v).Contains(id)) run.Loadout.Storage.Add(id);
                        }
                        results.Add(new JObject { ["op"] = "fillStorage", ["storage"] = new JArray(run.Loadout.Storage) });
                        continue;
                    }
                    var itemId = op["itemId"]?.Type == JTokenType.String ? (string)op["itemId"] : null;
                    var refusal = Core2RunArmoury.Equip(data, run, (string)op["slotId"], (int)op["setIndex"], itemId);
                    var v = RunReplay.View(data, run, rng);
                    results.Add(new JObject
                    {
                        ["op"] = "equip", ["slotId"] = op["slotId"], ["setIndex"] = op["setIndex"], ["itemId"] = op["itemId"], ["ok"] = refusal == null, ["loadout"] = v["loadout"], ["deck"] = v["deck"],
                        ["pools"] = new JArray(v["hp"], v["maxHp"], v["mana"], v["maxMana"], v["stamina"], v["maxStamina"]), ["ratings"] = v["ratings"], ["equipmentPoolBonuses"] = v["equipmentPoolBonuses"],
                    });
                }
                var candidates = new JObject();
                foreach (var slotId in new[] { "rightHand", "leftHand", "armor" }) candidates[slotId] = new JArray(Core2RunArmoury.ReplaceCandidates(data, run, slotId));
                var newAfter = new JArray(Core2RunArmoury.NewItems(run));
                var inspect = new JArray(Core2RunArmoury.Positions(data, run).Where(p => p.ItemId != null).Select(p =>
                {
                    var o = p.ToJson();
                    if (Core2RunGear.GearItem(data.Content, p.ItemId, p.SlotId == "armor", run.Class) == null) o["values"] = new JObject();
                    return o;
                }));
                Core2RunArmoury.MarkSeen(run);
                got = new JObject
                {
                    ["newBefore"] = newBefore, ["glow"] = glow, ["results"] = results, ["candidates"] = candidates, ["newAfter"] = newAfter, ["inspect"] = inspect,
                    ["seenAfter"] = new JArray(run.ArmorySeen), ["newAfterLook"] = new JArray(Core2RunArmoury.NewItems(run)),
                    // JS editingLocked(run, { result }) for an unresolved and a resolved fight; ProgressChecks locks live fights.
                    ["locked"] = new JObject { ["unresolved"] = Core2RunArmoury.NoncombatEditing(data), ["resolved"] = false, ["none"] = Core2RunArmoury.EditingLocked(data, null) },
                };
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message + " " + error.StackTrace?.Split('\n').FirstOrDefault() }; }
            result.Compare(got, row["expected"], () => "armoury " + input["classId"] + " seed " + input["seed"]);
        }
        return result;
    }

    // ---- skill progression ------------------------------------------------------------------------------------------
    static JObject Award(Core2SkillAward a) => a == null ? null : a.ToJson();

    public static RunReplay.Result Skills(Core2RunData data, JArray cases)
    {
        var result = new RunReplay.Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            JToken got;
            try
            {
                switch ((string)input["kind"])
                {
                    case "xpToNext": got = new JObject { ["xp"] = Core2RunSkills.XpToNext(data, (string)input["track"], (int)input["level"]) }; break;
                    case "award":
                    {
                        var (run, _) = Core2RunCreation.Create(data, (string)input["classId"], 21, ignoreUnlocks: true);
                        var awards = new JArray();
                        foreach (var skillId in new[] { "item:blade", "item:shield", "item:magic-focus", "class:" + run.Class })
                            foreach (var gain in (JArray)input["gains"]) awards.Add(Core2RunSkills.Award(data, run, skillId, (double)gain).ToJson());
                        got = new JObject { ["awards"] = awards, ["skills"] = Core2RunSave.SkillsJson(run.Skills) };
                        break;
                    }
                    case "settle":
                    {
                        var (run, _) = Core2RunCreation.Create(data, (string)input["classId"], 22, ignoreUnlocks: true);
                        foreach (var p in ((JObject)input["skills"]).Properties()) run.Skills[p.Name] = Core2SkillRow.FromJson(p.Value);
                        var m = (JObject)input["multipliers"];
                        var rules = new Core2ProgressionRules { CharacterLevels = true, WeaponXpMultiplier = Core2ProgressionRules.Multiplier(m["weapon"]), ClassXpMultiplier = Core2ProgressionRules.Multiplier(m["cls"]) };
                        var receipt = ((JArray)input["receipt"]).Select(r => new KeyValuePair<string, int>((string)r[0], (int)r[1])).ToList();
                        var settled = Core2RunSkills.Settle(data, run, receipt, (bool)input["victory"], (string)input["pool"], rules);
                        var all = settled.Awards.ToList();
                        var used = Core2Json.Strings(input["usedArmaments"]);
                        got = new JObject
                        {
                            ["weaponMultiplier"] = rules.WeaponXpMultiplier, ["classMultiplier"] = rules.ClassXpMultiplier,
                            ["classAward"] = (JToken)Award(settled.ClassAward) ?? Null, ["trackAwards"] = new JArray(settled.TrackAwards.Select(Award)),
                            ["notices"] = Core2RunSkills.LevelNotices(data, all, used), ["gains"] = Core2RunSkills.XpGains(receipt, all, 37, 3),
                            ["victoryGains"] = Core2RunSkills.XpGains(null, all, 12), ["skills"] = Core2RunSave.SkillsJson(run.Skills),
                        };
                        break;
                    }
                    default:
                    {
                        var run = State(data, input["run"]);
                        var graph = new Core2MapGraph();
                        graph.Add(new Core2MapNode { Id = "n3_0", Floor = 3, Type = "shrine", ActualType = "rest" });
                        run.MapGraph = graph; run.MapNodeId = "n3_0"; run.Floor = 3;
                        var characterLevels = (bool)input["characterLevels"];
                        Core2RunRest.Begin(data, run);
                        JObject levelUp;
                        try
                        {
                            Core2RunRest.Commit(data, run, "levelUp", attributeId: "strength", characterLevels: characterLevels);
                            var attributes = new JObject(); foreach (var id in Core2Run.AttributeIds) attributes[id] = run.Attributes[id];
                            levelUp = new JObject { ["ok"] = true, ["attributes"] = attributes, ["level"] = run.Level.ToJson() };
                        }
                        catch (InvalidOperationException error) { levelUp = new JObject { ["error"] = error.Message.Split(new[] { " (level" }, StringSplitOptions.None)[0] }; }
                        got = new JObject { ["characterLevels"] = characterLevels, ["levelXpOf100"] = characterLevels ? 100 : 0, ["levelUp"] = levelUp };
                        break;
                    }
                }
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message + " " + error.StackTrace?.Split('\n').FirstOrDefault() }; }
            result.Compare(got, row["expected"], () => "skills " + input.ToString(Newtonsoft.Json.Formatting.None).Substring(0, Math.Min(160, input.ToString(Newtonsoft.Json.Formatting.None).Length)));
        }
        return result;
    }
}
