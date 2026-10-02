#nullable enable
// Original-game ACTIVE-ROOM import checks against saves written by the real JavaScript
// save manager (export-room-reference.mjs → room-reference.json + receipt). Each fixture
// carries what the original does next; the imported native run must do the same.
using System.Security.Cryptography;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

static class RoomImportChecks
{
    public static int Run(string root, OriginalContentCatalog catalog, Func<string, JObject> json)
    {
        var checks = 0;
        void Check(bool yes, string label) { if (!yes) throw new Exception("room import: " + label); checks++; }
        bool Same(JToken? a, JToken? b)
        {
            bool Number(JToken? t) => t?.Type == JTokenType.Integer || t?.Type == JTokenType.Float;
            if (Number(a) && Number(b)) return (decimal)a! == (decimal)b!;
            if ((a == null || a.Type == JTokenType.Null) && (b == null || b.Type == JTokenType.Null)) return true;
            if (a is JObject ao && b is JObject bo) return ao.Count == bo.Count && ao.Properties().All(p => Same(p.Value, bo[p.Name]));
            if (a is JArray aa && b is JArray ba) return aa.Count == ba.Count && aa.Zip(ba, Same).All(x => x);
            return JToken.DeepEquals(a, b);
        }
        string Diff(JToken? a, JToken? b, string at)
        {
            if (a is JObject ao && b is JObject bo) { foreach (var p in ao.Properties().Select(p => p.Name).Union(bo.Properties().Select(p => p.Name))) { var d = Diff(ao[p], bo[p], at + "." + p); if (d != "") return d; } return ""; }
            if (a is JArray aa && b is JArray ba && aa.Count == ba.Count) { for (var i = 0; i < aa.Count; i++) { var d = Diff(aa[i], ba[i], at + "[" + i + "]"); if (d != "") return d; } return ""; }
            return JToken.DeepEquals(a, b) ? "" : at + ": " + a?.ToString(Newtonsoft.Json.Formatting.None) + " vs " + b?.ToString(Newtonsoft.Json.Formatting.None);
        }
        void Equal(JToken? actual, JToken? expected, string label) => Check(Same(actual, expected), label + " expected " + expected?.ToString(Newtonsoft.Json.Formatting.None) + " actual " + actual?.ToString(Newtonsoft.Json.Formatting.None));
        string? Refusal(Action action)
        {
            try { action(); } catch (ArgumentException e) { return e.Message; } catch (InvalidOperationException e) { return e.Message; } catch (Newtonsoft.Json.JsonException e) { return e.Message; }
            return null;
        }
        void Refuse(Action action, string label, string? contains = null)
        { var message = Refusal(action); Check(message != null && (contains == null || message.Contains(contains)), label + (message == null ? " (accepted)" : " (" + message + ")")); }

        var folder = Path.Combine(root, "UnityTests/WebSaveImport");
        var bytes = File.ReadAllBytes(Path.Combine(folder, "room-reference.json"));
        var receipt = JObject.Parse(File.ReadAllText(Path.Combine(folder, "room-reference.receipt.json")));
        Check(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == (string)receipt["outputSha256"]!, "fixture bytes match their receipt");
        var fixtures = (JObject)JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes))["fixtures"]!;
        string Save(string name) => (string)fixtures[name]!["save"]!;
        JObject Expect(string name) => (JObject)fixtures[name]!["expect"]!;
        JObject Import(string text) => OriginalWebSaveImport.Convert(text, catalog, json("event-choices"), json("mechanics"), json("progression"));
        OriginalGameSession Game(string name)
        {
            var snapshot = Import(Save(name)); var game = OriginalGameSession.Restore(snapshot);
            Check(JToken.DeepEquals(OriginalGameSession.Restore(game.Snapshot()).Snapshot(), game.Snapshot()), name + ": Restore → Snapshot round-trips");
            Check(JToken.DeepEquals(game.Snapshot(), snapshot), name + ": imported snapshot restores unchanged");
            return game;
        }
        OriginalRunSession Run(OriginalGameSession game)
        {
            var snapshot = game.Snapshot(); var content = new OriginalContentCatalog(snapshot["content"]!.ToString());
            return OriginalRunSession.Restore(snapshot, new OriginalRunContent(content, reconcile: new OriginalPlayerProjection(content, (JObject)snapshot["supplement"]!["mechanics"]!).Reconcile));
        }
        // The fields export-room-reference.mjs view() records from the original run.
        void View(JObject run, JToken expected, string label)
        {
            foreach (var key in new[] { "cinders", "hp", "maxHp", "actNumber", "floor", "mapNodeId", "relics", "flaskCharges", "streamCounters" }) Equal(run[key], expected[key], label + " " + key);
            Equal(new JArray(run["deck"]!.Select(c => c["cardId"])), expected["deck"], label + " deck");
            Equal(new JArray(run["flasks"]!.Select(f => f["flaskId"])), expected["flasks"], label + " flasks");
            Equal(run["loadout"]!["storage"], expected["storage"], label + " armament storage");
            Equal((int?)run["smithingStones"] ?? 0, expected["smithingStones"], label + " smithing stones");
            Equal((int?)run["removesPurchased"] ?? 0, expected["removesPurchased"], label + " removes purchased");
            Equal(run["flaskChancePct"], expected["flaskChancePct"], label + " flask pity");
        }
        void Opening(OriginalGameSession game, JToken expected, string label)
        {
            Check(game.Phase == OriginalRunPhase.Combat && game.Turn == (int)expected["turn"]!, label + ": the fight opens on the original turn");
            Equal(new JArray(game.Hand.Select(c => c["instanceId"])), expected["hand"], label + " opening hand");
            Equal(new JArray(game.Pile("draw").Select(c => c["instanceId"])), expected["draw"], label + " draw pile order");
            Equal(new JArray(game.Pile("discard").Select(c => c["instanceId"])), expected["discard"], label + " discard pile");
            Equal(game.Player["energy"], expected["energy"], label + " actions");
            Equal(new JArray(game.Enemies.Select(e => new JObject { ["enemyId"] = e["enemyId"]!.DeepClone(), ["hp"] = e["hp"]!.DeepClone(), ["maxHp"] = e["maxHp"]!.DeepClone(), ["intent"] = e["intent"]?["moveId"]?.DeepClone() })), expected["enemies"], label + " enemies and intents");
            Equal(game.Snapshot()["run"]!["room"]!["combatSnapshot"]!["rng"], expected["counters"], label + " RNG stream positions");
        }

        // ---- combat: the fight-entry receipt continues; an exact mid-fight snapshot is refused
        foreach (var cls in new[] { "reaver", "starseer", "rogue", "herald" })
        {
            var expect = Expect(cls + "-combat-entry"); var game = Game(cls + "-combat-entry");
            Check((string)game.Room["encounterId"]! == (string)expect["encounterId"]!, cls + ": saved encounter kept");
            Opening(game, expect["opening"]!, cls + " fight entry");
            game.EndTurn(); Check(game.Turn == 2 || game.Phase != OriginalRunPhase.Combat, cls + ": imported fight plays an enemy turn");
            Refuse(() => Import(Save(cls + "-combat-snapshot")), cls + ": mid-fight snapshot refused with the player message", OriginalWebSaveImport.MidFightMessage);
        }
        Check(OriginalWebSaveImport.MidFightMessage.StartsWith("This save is mid-fight; finish the fight in the original game, then import."), "mid-fight message wording");

        // ---- rewards
        {
            var expect = Expect("reward-normal-fresh"); var game = Game("reward-normal-fresh");
            Check(game.Phase == OriginalRunPhase.Rewards && (string)game.Room["states"]!["cinders"]! == "taken", "pending cinders granted on arrival, as the original screen does");
            View(game.RunPlayer, expect["afterCinders"]!, "fresh reward");
        }
        {
            var card = Expect("reward-normal-card"); var game = Game("reward-normal");
            Check(game.Room["states"]!["card"] == null && (string)game.Room["states"]!["cinders"]! == "taken", "taken and pending rows kept");
            game.Reward("card", (string)card["pick"]!); View(game.RunPlayer, card["afterCardTake"]!, "take the same card");
            Refuse(() => game.Reward("card", (string)card["pick"]!), "a taken card cannot be taken twice");
            var taken = Game("reward-normal-card"); Check(taken.Room["states"]!["card"]!.ToString() == "taken", "taken card state imported");
            Refuse(() => taken.Reward("cinders"), "imported taken cinders are not granted again");
            var manual = Game("reward-normal-card"); manual.ContinueRewards(); View(manual.RunPlayer, card["manualContinue"]!, "manual continue");
            Check(manual.Phase == OriginalRunPhase.Map && manual.LegalNodeIds.SequenceEqual(manual.Map["nodes"]![(string)manual.RunPlayer["mapNodeId"]!]!["next"]!.Values<string>()), "continue returns to the map onward from the fight");
            var auto = Run(Game("reward-normal-card")); auto.ContinueRewards(true); View(auto.Player(), card["autoContinue"]!, "auto-collect continue");
        }
        foreach (var name in new[] { "reward-elite", "reward-boss" })
        {
            var expect = Expect(name); var game = Game(name); var offer = (JObject)expect["offer"]!;
            foreach (var key in new[] { "cinders", "cardIds", "relicId", "armamentId" }) Equal(game.Room["rewards"]![key], offer[key], name + " offer " + key);
            Equal(game.Room["source"], name == "reward-boss" ? "boss" : "elite", name + " source");
            foreach (var kind in new[] { "relic", "armament", "flask" })
            {
                var take = Game(name);
                if (expect[kind + "Take"] == null) { Refuse(() => take.Reward(kind), name + " absent " + kind + " cannot be taken"); continue; }
                take.Reward(kind); View(take.RunPlayer, expect[kind + "Take"]!, name + " take " + kind);
            }
            var manual = Game(name); manual.ContinueRewards(); View(manual.RunPlayer, expect["manualContinue"]!, name + " manual continue");
            if (name == "reward-boss")
            {
                Check(manual.ActNumber == 2 && manual.Phase == OriginalRunPhase.Map, "boss reward advances the act");
                Equal(manual.Map, expect["nextMap"], "next act map matches the original");
            }
            var auto = Run(Game(name)); auto.ContinueRewards(true); View(auto.Player(), expect["autoContinue"]!, name + " auto continue");
        }

        // ---- merchant (the original removes sold rows; native marks them sold)
        JArray Unsold(OriginalGameSession game, string plural) => new JArray(((JArray)game.Room[plural]!).Where(r => (bool?)r["sold"] != true).Select(r => new JObject { ["id"] = r["id"]!.DeepClone(), ["cost"] = r["cost"]!.DeepClone() }));
        void Stock(OriginalGameSession game, JToken expected, string label)
        {
            foreach (var plural in new[] { "cards", "relics", "flasks" }) Equal(Unsold(game, plural), expected[plural], label + " " + plural);
            Equal(game.Room["removeCost"], expected["removeCost"], label + " removal price");
            if (expected["smith"] != null) Equal(game.Room["smith"], expected["smith"], label + " smith");
        }
        int Index(OriginalGameSession game, string plural, int remaining) => ((JArray)game.Room[plural]!).Select((r, i) => (r, i)).Where(x => (bool?)x.r["sold"] != true).ElementAt(remaining).i;
        {
            var expect = Expect("merchant-fresh"); var game = Game("merchant-fresh");
            Check(game.Phase == OriginalRunPhase.Shop, "merchant room imported");
            Stock(game, expect["stock"]!, "fresh merchant");
            foreach (var (kind, i) in new[] { ("card", 0), ("card", 2), ("relic", 1), ("flask", 0) })
            {
                var key = kind + i; if (expect[key] == null) continue;
                var buy = Game("merchant-fresh"); buy.Buy(kind, Index(buy, kind + "s", i));
                View(buy.RunPlayer, expect[key]!, "buy " + key); Stock(buy, expect[key]!["stock"]!, "after buying " + key);
            }
            var remove = Game("merchant-fresh"); remove.Service("removeCard", new JObject { ["instanceId"] = expect["remove"]!["instanceId"]!.DeepClone() });
            View(remove.RunPlayer, expect["remove"]!, "remove a card"); Stock(remove, expect["remove"]!["stock"]!, "after removing");
        }
        {
            var expect = Expect("merchant-partial"); var game = Game("merchant-partial");
            Stock(game, expect["stock"]!, "partly bought merchant"); View(game.RunPlayer, expect["state"]!, "partly bought run");
            foreach (var (kind, i) in new[] { ("card", 0), ("flask", 1) })
            {
                var key = kind + i; if (expect[key] == null) continue;
                var buy = Game("merchant-partial"); buy.Buy(kind, Index(buy, kind + "s", i)); View(buy.RunPlayer, expect[key]!, "partial buy " + key);
            }
            var remove = Game("merchant-partial"); remove.Service("removeCard", new JObject { ["instanceId"] = expect["remove"]!["instanceId"]!.DeepClone() });
            View(remove.RunPlayer, expect["remove"]!, "second removal"); Equal(remove.Room["removeCost"], expect["remove"]!["removeCost"], "removal price rises as in the original");
            game.LeaveShop(); Check(game.Phase == OriginalRunPhase.Map && game.LegalNodeIds.Length > 0, "leaving the merchant continues the route");
        }
        {
            var expect = Expect("merchant-custom"); var game = Game("merchant-custom");
            Stock(game, expect["stock"]!, "custom-priced merchant");
            game.Buy("card", 0); View(game.RunPlayer, expect["card0"]!, "custom-priced purchase");
        }

        // ---- event: the original resumes an entered event on the map
        {
            var expect = Expect("event-entered"); var game = Game("event-entered");
            Check(game.Phase == OriginalRunPhase.Map && game.RunPlayer["seenEvents"]!.Values<string>().Contains((string)expect["eventId"]!), "entered event imports as its map checkpoint");
            Equal(new JArray(game.LegalNodeIds), expect["next"], "event checkpoint continues onward like the original");
        }

        // ---- every fixture records the original load door's verdict; the importer agrees with it
        foreach (var fixture in fixtures.Properties())
        {
            var reloads = (string)fixture.Value["originalReload"]!["state"]! == "ok";
            if (fixture.Name.EndsWith("-combat-snapshot")) { Check(reloads, fixture.Name + ": the original itself resumes this fight"); continue; }
            var message = Refusal(() => Import(Save(fixture.Name)));
            Check(reloads == (message == null), fixture.Name + ": import follows the original load door (" + (message ?? "accepted") + ")");
        }
        // ---- modes: Sealed and Draft decks are dealt from a pool; their birth attack quota is
        // the slots dealt (none), as in the original after its reload fix and natively.
        foreach (var name in new[] { "mode-sealed", "mode-draft" })
        {
            var game = Game(name); var dealt = JObject.Parse(Save(name));
            Equal(game.RunPlayer["equipmentAttackSlotCount"], new JValue(0), name + ": dealt deck's attack quota");
            Equal(dealt["equipmentAttackSlotCount"], new JValue(0), name + ": the original writes the dealt quota");
            Equal(dealt["poolDeckRule"], new JValue(1), name + ": the original marks the dealt-deck rule");
            Equal(new JArray(game.RunPlayer["deck"]!.Select(c => c["instanceId"])), new JArray(dealt["deck"]!.Select(c => c["instanceId"])), name + ": card identities unchanged (no kit or weapon arts dealt back)");
            Equal(game.RunPlayer["custom"]!["deckMode"], dealt["custom"]!["deckMode"], name + ": deck mode kept");
            Run(game);
            // A save written before the original's fix kept the composed deck's quota and had no
            // marker. The original load door heals it to the dealt count (tests/pool-deck-reload.test.mjs);
            // so does the import.
            // Those builds wrote run schema 5; the original's bump to 6 came with the marker.
            Equal(dealt["schemaVersion"], new JValue(6), name + ": the original writes run schema 6");
            var stale = (JObject)dealt.DeepClone(); stale["equipmentAttackSlotCount"] = 3; stale.Remove("poolDeckRule"); stale["schemaVersion"] = 5;
            var staleSnapshot = Import(stale.ToString()); var healed = OriginalGameSession.Restore(staleSnapshot);
            Equal(healed.RunPlayer["equipmentAttackSlotCount"], new JValue(0), name + ": pre-fix composed quota converts to the dealt count");
            Equal(healed.RunPlayer["deck"], game.RunPlayer["deck"], name + ": pre-fix save imports the same deck");
            Check((int?)staleSnapshot["run"]?["webImport"]?["original"]?["equipmentAttackSlotCount"] == 3, name + ": import receipt keeps the original bytes' quota");
            // A schema-6 save without the marker was not written by the original's newRun: both refuse it.
            RejectPool(dealt, s => s.Remove("poolDeckRule"), name + ": schema-6 save without the dealt-deck rule refused", "missing its dealt-deck rule");
            RejectPool(dealt, s => { s.Remove("poolDeckRule"); s.Remove("equipmentAttackSlotCount"); }, name + ": schema-6 save without the rule or a quota refused", "missing its dealt-deck rule");
            // A present but malformed marker is refused even with no quota to check it against.
            RejectPool(dealt, s => { s["poolDeckRule"] = JValue.CreateNull(); s.Remove("equipmentAttackSlotCount"); }, name + ": null dealt-deck rule with no quota refused", "Malformed original dealt-deck rule");
            RejectPool(dealt, s => { s["poolDeckRule"] = 2; s.Remove("equipmentAttackSlotCount"); }, name + ": unknown dealt-deck rule with no quota refused", "Malformed original dealt-deck rule");
            RejectPool(dealt, s => { s["poolDeckRule"] = JValue.CreateNull(); s.Remove("equipmentAttackSlotCount"); s["schemaVersion"] = 5; }, name + ": null dealt-deck rule with no quota refused at schema 5 too", "Malformed original dealt-deck rule");
            // A marked schema-5 save (a build of the fix before the bump) imports as it is.
            var marked5 = (JObject)dealt.DeepClone(); marked5["schemaVersion"] = 5;
            Equal(OriginalGameSession.Restore(Import(marked5.ToString())).RunPlayer["deck"], game.RunPlayer["deck"], name + ": a marked schema-5 save imports the same deck");
            RejectPool(dealt, s => ((JArray)s["deck"]!).Add(new JObject { ["instanceId"] = "x1", ["cardId"] = "strike", ["upgraded"] = false, ["equipmentRole"] = "attack", ["equipmentAttackSlotId"] = "attack:1" }), name + ": gap in dealt attack slots refused", "not the ones it was dealt");
            RejectPool(dealt, s => ((JArray)s["deck"]!).Add(new JObject { ["instanceId"] = "x1", ["cardId"] = "strike", ["upgraded"] = false, ["equipmentRole"] = "attack", ["equipmentAttackSlotId"] = "attack:0" }), name + ": more attack slots than the quota refused", "not the ones it was dealt");
            RejectPool(dealt, s => s["equipmentAttackSlotCount"] = -1, name + ": malformed quota refused", "Malformed original equipment attack quota");
            // A marked save is held to its quota, as the original holds it: a lost attack card is corruption.
            RejectPool(dealt, s => s["equipmentAttackSlotCount"] = 1, name + ": marked save missing an attack card refused", "not the ones it was dealt");
            RejectPool(dealt, s => s["poolDeckRule"] = 2, name + ": unknown dealt-deck rule refused", "Malformed original dealt-deck rule");
            // Only an absent marker is a pre-fix save; a present string or null is refused like the original refuses it.
            RejectPool(dealt, s => s["poolDeckRule"] = "1", name + ": string dealt-deck rule refused", "Malformed original dealt-deck rule");
            RejectPool(dealt, s => s["poolDeckRule"] = JValue.CreateNull(), name + ": null dealt-deck rule refused", "Malformed original dealt-deck rule");
            // `poolDeck` is a fight's flag; on a saved run the original refuses it, and so does the import.
            RejectPool(dealt, s => s["poolDeck"] = true, name + ": run-level poolDeck flag refused", "poolDeck");
        }
        void RejectPool(JObject source, Action<JObject> edit, string label, string contains) { var copy = (JObject)source.DeepClone(); edit(copy); Refuse(() => Import(copy.ToString()), label, contains); }
        // A Standard deck keeps the composed rule: a quota its deck does not hold is refused,
        // because the original's load door still archives it.
        RejectPool(JObject.Parse(Save("mode-chaos")), s => s["equipmentAttackSlotCount"] = (int)s["equipmentAttackSlotCount"]! + 1, "standard deck missing a composed attack slot refused", "cannot reload it either");
        RejectPool(JObject.Parse(Save("mode-sealed")), s => { s["custom"]!["deckMode"] = "standard"; s.Remove("poolDeckRule"); s["equipmentAttackSlotCount"] = 3; }, "Sealed-shaped deck under Standard rules refused", "cannot reload it either");
        // ---- modes: Sealed, Draft, ascension/chaos rules and Endless act 4 continue like the original
        foreach (var name in new[] { "mode-sealed", "mode-draft", "mode-chaos", "mode-endless" })
        {
            var expect = Expect(name); var game = Game(name);
            Equal(new JArray(game.RunPlayer["deck"]!.Select(c => c["cardId"])), expect["deck"], name + " deck");
            Equal(game.RunPlayer["cinders"], expect["cinders"], name + " cinders");
            game.Enter((string)expect["start"]!);
            Check((string)game.Room["encounterId"]! == (string)expect["encounterId"]!, name + ": next encounter matches the original");
            Equal(game.RunPlayer["lastEncounters"], expect["lastEncounters"], name + " encounter history");
            Opening(game, expect["opening"]!, name);
        }
        Check(Game("mode-endless").ActNumber == 4, "Endless act 4 imports");

        // ---- refusals
        var reward = JObject.Parse(Save("reward-normal-card")); var store = JObject.Parse(Save("merchant-fresh")); var entry = JObject.Parse(Save("reaver-combat-entry"));
        void RejectEdit(JObject source, Action<JObject> edit, string label, string? contains = null) { var copy = (JObject)source.DeepClone(); edit(copy); Refuse(() => Import(copy.ToString()), label, contains); }
        RejectEdit(reward, s => s["pendingReward"]!["schemaVersion"] = 2, "future reward-room format refused");
        RejectEdit(reward, s => s["pendingReward"]!["futureRow"] = 1, "unknown reward-room field refused", "futureRow");
        RejectEdit(reward, s => s["pendingReward"]!["rewards"]!["skillId"] = "x", "unknown reward kind refused", "skillId");
        RejectEdit(reward, s => s["pendingReward"]!["source"] = "boss", "reward source must match its node");
        RejectEdit(reward, s => s["pendingReward"]!["after"] = "advanceAct", "reward continuation must match its source");
        RejectEdit(reward, s => s["pendingReward"]!["chosenCardId"] = "not-offered", "chosen card outside the offer refused");
        RejectEdit(reward, s => s["pendingReward"]!["states"]!["card"] = "skipped", "card state without matching choice refused");
        RejectEdit(reward, s => s["pendingReward"]!["states"]!["relic"] = "lost", "unknown reward state refused");
        RejectEdit(reward, s => s["pendingReward"]!["rewards"]!["cardIds"] = new JArray("missing-card"), "unknown offered card refused");
        RejectEdit(reward, s => s["pendingReward"]!["rewards"]!["cinders"] = -5, "negative reward refused");
        var elite = JObject.Parse(Save("reward-elite"));
        RejectEdit(elite, s => s["smithingRewardClaims"] = new JArray(), "unclaimed Smithing Stone reward refused");
        RejectEdit(elite, s => s["pendingReward"]!["rewards"]!["relicId"] = "missing-relic", "unknown offered relic refused");
        RejectEdit(JObject.Parse(Save("reward-boss")), s => { s["actNumber"] = 3; }, "final-boss reward refused as a continuing climb");
        RejectEdit(store, s => s["shopStock"]!["cards"]![0]!["id"] = "missing-card", "unknown merchant card refused");
        RejectEdit(store, s => s["shopStock"]!["cards"]![0]!["cost"] = -1, "negative merchant price refused");
        RejectEdit(store, s => s["shopStock"]!["armaments"] = new JArray(), "unknown merchant shelf refused", "armaments");
        RejectEdit(store, s => s["shopStock"]!["smith"]!["offered"] = !(bool)s["shopStock"]!["smith"]!["offered"]!, "merchant smith outside original rules refused");
        RejectEdit(reward, s => s["shopStock"] = store["shopStock"]!.DeepClone(), "two active rooms refused", "more than one active room");
        RejectEdit(JObject.Parse(Save("event-entered")), s => s["shopStock"] = store["shopStock"]!.DeepClone(), "merchant stock off a merchant node refused");
        RejectEdit(entry, s => s["combatEntered"]!["nodeId"] = "n9_9", "fight off the saved route refused");
        RejectEdit(entry, s => s["combatEntered"]!["encounterId"] = "fellWarden", "fight from another pool refused");
        RejectEdit(entry, s => s["combatEntered"] = "blightHounds", "string fight receipt refused");
        RejectEdit(entry, s => s["combatEntered"]!["snapshot"] = new JObject(), "any combat snapshot refused as mid-fight", "mid-fight");
        RejectEdit(reward, s => s["flaskChancePct"] = 101, "malformed flask pity refused");
        RejectEdit(store, s => s["removesPurchased"] = -1, "malformed removal count refused");
        RejectEdit(JObject.Parse(Save("mode-chaos")), s => s["custom"]!["mapShape"] = new JObject { ["floors"] = 8 }, "custom map-shape climb refused by name", "map-shape");
        RejectEdit(JObject.Parse(Save("mode-chaos")), s => s["custom"]!["mods"]!["futureMod"] = true, "unknown custom modifier refused");
        RejectEdit(JObject.Parse(Save("mode-sealed")), s => s["custom"]!["deckMode"] = "gauntlet", "unknown deck mode refused");
        RejectEdit(reward, s => s["schemaVersion"] = 7, "newer original run schema refused", "newer original-game save formats");

        Check(OriginalWebRoomImport.Resumes(Import(Save("reward-elite"))) == "Resumes at the fight's rewards" && OriginalWebRoomImport.Resumes(Import(Save("merchant-fresh"))) == "Resumes at the merchant"
            && OriginalWebRoomImport.Resumes(Import(Save("rogue-combat-entry"))) == "Resumes at the start of the fight" && OriginalWebRoomImport.Resumes(Import(Save("event-entered"))) == "Resumes on the map", "preview names where each import resumes");
        // ---- an imported room commits through the verified journal and reloads exactly
        var memory = new OriginalMemorySaveStorage(); var slots = new OriginalSaveSlots(memory, "room-import", "test");
        foreach (var (name, slot) in new[] { ("reward-normal-card", 0), ("merchant-partial", 1), ("herald-combat-entry", 2) })
        {
            var snapshot = Import(Save(name)); Check(slots.ImportWebRun(slot, snapshot), name + ": committed to an empty slot");
            var loaded = slots.Load(slot, s => OriginalGameSession.Restore(s), out _, out _);
            // The journal stores JSON text, so compare with the snapshot's own serialized form.
            var stored = JObject.Parse(snapshot.ToString(Newtonsoft.Json.Formatting.None));
            Check(JToken.DeepEquals(loaded, stored), name + ": journal reloads the exact room " + Diff(loaded, stored, ""));
        }
        Console.WriteLine("Room import: " + checks + " checks passed");
        return checks;
    }
}
