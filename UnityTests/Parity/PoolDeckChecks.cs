// PoolDeckChecks.cs — the dealt-deck rule (cehinds/AshenSpire#1479) on native Unity runs.
// A Sealed or Draft run keeps exactly the deck it was dealt: no reconcile door deals it the
// equipment's lent cards (kit grants, weapon arts, the empty hand's Dodge Roll). The original
// rules this in model/loadout.js reconcileGrantedCards / reconcileGrantedCardsInCombat (early
// return on isPoolDeckRun) and proves it in tests/pool-deck-reload.test.mjs; Unity's doors are
// WeaponCardComposer.Recompose / ReconcileCombat, keyed on OriginalCustomRunRules.IsPoolDeckRun.
// Each door is driven through the public session: Armoury equip and set select, a relic and the
// projection reconcile every service runs, a mid-fight swap, and the end of that fight. Two
// contents run: the shipped one (whose only lent card is the empty hand's Dodge Roll) and one
// that also authors a weapon package and a bound armour grant. A Standard run walks the same
// doors and must still be dealt, and stripped of, its lent cards. A pool run also never
// extracts a card at the smith (CardMountService.ExtractionRefusal, owner ruling 2026-10-02).
using AshenSpire.Domain.Original;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
internal static class PoolDeckChecks
{
    internal static int Run(string root)
    {
        var contentRoot = Path.Combine(root, "GameContent/Unity/Original");
        var shipped = new OriginalContentCatalog(File.ReadAllText(Path.Combine(contentRoot, "content.json")));
        var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(contentRoot, "mechanics.json")));
        var supplement = JObject.Parse(File.ReadAllText(Path.Combine(contentRoot, "event-choices.json")));
        var progression = new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(contentRoot, "progression.json"))));
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        string Text(JToken token) => token.ToString(Formatting.None);
        bool Lent(JToken card) => card["grantedBy"] != null || new[] { "granted", "weaponArt" }.Contains((string)card["equipmentRole"]);
        string[] Ids(IEnumerable<JToken> cards) => cards.Select(c => (string)c["instanceId"]).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        JArray Piles(OriginalGameSession game) => new JArray(new[] { "hand", "draw", "discard", "exhaust" }.SelectMany(kind => game.Pile(kind)).Select(c => c.DeepClone()));
        // The first armament this build can wield in its main hand beside its kit.
        string Spare(OriginalContentCatalog catalog, JObject player, string classId)
        {
            var loadout = (JObject)player["loadout"]; var composer = new WeaponCardComposer(catalog); var locations = new WeaponLoadout(catalog);
            var carried = ((JObject)loadout["sets"]).Properties().SelectMany(p => p.Value.Values<string>()).ToHashSet();
            return catalog.Table("equipment.armaments").Select(a => (string)a["id"]).First(id =>
            {
                if (carried.Contains(id) || (string)catalog.Record("equipment.armaments", id)["kind"] != "weapon") return false;
                var trial = (JObject)loadout.DeepClone(); trial["sets"]["rightHand"][0] = id;
                try { composer.BuildAttackPlan(trial, classId); } catch (ArgumentException) { return false; }
                return (bool)locations.RequirementReceipt(locations.Equipped(trial, classId, "rightHand"), (JObject)player["attributes"])["ok"];
            });
        }
        JObject Player(OriginalContentCatalog catalog, string classId)
        {
            var creator = LeanAllocation.Create(catalog, classId, progression);
            var kit = (string)catalog.Table("equipment.startingKits").First(k => (string)k["classId"] == classId && (bool?)k["baseline"] == true)["id"];
            // The second main-hand set is open, so the Armoury and a mid-fight swap have a set to change to.
            return new OriginalCharacterBuilder(catalog, progression, mechanics).Build(creator, kit, new JObject { ["unlocked"] = new JArray("rack2Right") });
        }
        // Content that lends more than Dodge Roll: the spare weapon carries a package (a granted
        // card and a weapon art) and the class's starting armour is bound to a kit grant.
        OriginalContentCatalog Lending(string classId, out string spare)
        {
            var player = Player(shipped, classId); spare = Spare(shipped, player, classId); var spareId = spare;
            var data = shipped.Data(); var weapon = (JObject)data["equipment"]["armaments"].First(a => (string)a["id"] == spareId);
            weapon["weaponCardPackage"] = new JObject { ["compatibility"] = "attack-v1", ["fillerAttackProfileId"] = weapon["attackProfile"].DeepClone(), ["grantedCards"] = new JArray(new JObject { ["cardId"] = "strike", ["count"] = 1 }), ["weaponArtDefaults"] = new JArray("quickstep") };
            var armourId = (string)player["loadout"]["sets"]["armor"][0];
            ((JArray)data["tagging"]).Add(new JObject { ["family"] = "armour", ["scope"] = classId, ["objectId"] = armourId, ["tagId"] = "bound" });
            ((JArray)data["equipment"]["equipmentGrants"]).Add(new JObject { ["sourceId"] = armourId, ["family"] = "armour", ["scope"] = classId, ["cards"] = new JArray("defend") });
            return new OriginalContentCatalog(data.ToString());
        }
        OriginalGameSession Dealt(OriginalContentCatalog catalog, string mode, string classId, uint seed, string spare)
        {
            var player = Player(catalog, classId);
            ((JArray)player["loadout"]["storage"]).Add(spare);
            if (!(player["ownedItemRefs"] is JArray)) player["ownedItemRefs"] = new JArray();
            ((JArray)player["ownedItemRefs"]).Add("armament/" + spare);
            var game = OriginalGameSession.StartConfigured(catalog, supplement, mechanics, player, seed, new JObject { ["custom"] = mode == "standard" ? new JObject() : new JObject { ["deckMode"] = mode } });
            if (mode == "draft") for (var round = 0; round < 3; round++) game.PickDraft((string)game.DraftChoices[0]);
            Check(game.Phase == OriginalRunPhase.Map, mode + " run reaches the map");
            return game;
        }
        OriginalGameSession Win(OriginalGameSession game)
        {
            // Every enemy is left one hit point, so any attack in hand ends the fight.
            var snapshot = game.Snapshot();
            foreach (var enemy in snapshot["run"]["room"]["combatSnapshot"]["enemies"]) enemy["hp"] = 1;
            var fight = OriginalGameSession.Restore(snapshot);
            for (var guard = 0; fight.Phase == OriginalRunPhase.Combat; guard++)
            {
                if (guard > 30) throw new Exception("Fixture fight did not end");
                var played = false;
                foreach (var card in fight.Hand.ToArray())
                {
                    var target = (string)fight.Enemies.FirstOrDefault(e => (int)e["hp"] > 0)?["id"];
                    try { fight.Play((string)card["instanceId"], target); played = true; break; }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
                if (!played && fight.Phase == OriginalRunPhase.Combat) fight.EndTurn();
            }
            Check(fight.Phase == OriginalRunPhase.Rewards, "fixture fight is won");
            return fight;
        }
        // Two kits on shipped content; the authored-lending content once (its cost is the same door walk).
        foreach (var (lending, classId, seed) in new[] { (false, "reaver", 123u), (false, "rogue", 77u), (true, "reaver", 123u) })
        {
            string spare = null;
            var catalog = lending ? Lending(classId, out spare) : shipped;
            spare ??= Spare(shipped, Player(shipped, classId), classId);
            foreach (var mode in new[] { "sealed", "draft", "standard" })
            {
                var pool = mode != "standard"; var label = (lending ? "lending" : "shipped") + "/" + mode + "/" + classId;
                var game = Dealt(catalog, mode, classId, seed, spare);
                var dealt = (JArray)game.RunPlayer["deck"].DeepClone();
                Check(OriginalCustomRunRules.IsPoolDeckRun(game.RunPlayer) == pool, label + ": pool-deck predicate");
                Check(pool ? !dealt.Any(Lent) : dealt.Any(Lent) == lending, label + ": opening deck lent cards");
                // Held: a pool deck is exactly as dealt, a standard deck took (or shed) lent cards; a resume
                // (checked at the Armoury, the swap, the fight's end and after it) keeps either unchanged.
                void Held(string door, JArray before, bool lends, bool resume = false)
                {
                    var deck = (JArray)game.RunPlayer["deck"];
                    if (pool) Check(Text(deck) == Text(dealt), label + " " + door + ": deck stays exactly as dealt\nEXPECTED " + Text(dealt) + "\nACTUAL " + Text(deck));
                    else if (lends) Check(!Ids(deck).SequenceEqual(Ids(before)), label + " " + door + ": standard deck is still reconciled to its equipment");
                    if (resume) Check(Text(OriginalGameSession.Restore(JObject.Parse(game.Snapshot().ToString())).RunPlayer["deck"]) == Text(deck), label + " " + door + ": resume keeps the deck");
                }
                JArray Now() => (JArray)game.RunPlayer["deck"].DeepClone();
                // Armoury: the empty off-hand (Dodge Roll), the spare weapon's set (its package), armour off and on (its bound grant).
                var shield = (string)game.RunPlayer["loadout"]["sets"]["leftHand"][0];
                var before = Now(); game.Equip("leftHand", 0, null); Held("armoury off-hand emptied", before, true);
                if (!pool) Check(game.RunPlayer["deck"].Any(c => (string)c["cardId"] == "dodgeRoll" && Lent(c)), label + ": standard empty hand lends Dodge Roll");
                // A relic and the player projection reconcile (the door levelling, resale and the smith run) while a hand is empty.
                var content = new OriginalRunContent(catalog, reconcile: new OriginalPlayerProjection(catalog, mechanics).Reconcile);
                var run = game.RunPlayer;
                var relic = catalog.Table("relics").Select(r => (string)r["id"]).First(id => ((string)catalog.Record("relics", id)["pool"] ?? "reward") == "reward" && !run["relics"].Values<string>().Contains(id) && OriginalRewardAvailability.Refusal(catalog.Data(), run, "relic", id) == null);
                Check(content.CollectReward(run, "relic", new JObject { ["relicId"] = relic }, new RandomStreams(seed)), label + ": relic collected");
                Check(pool ? Text(run["deck"]) == Text(dealt) : Ids(run["deck"]).SequenceEqual(Ids(game.RunPlayer["deck"])), label + " relic reconcile: " + (pool ? "deck stays exactly as dealt" : "standard deck already reconciled"));
                var projected = game.RunPlayer; new OriginalPlayerProjection(catalog, mechanics).Reconcile(projected);
                Check(pool ? Text(projected["deck"]) == Text(dealt) : Ids(projected["deck"]).SequenceEqual(Ids(game.RunPlayer["deck"])), label + " service reconcile: " + (pool ? "deck stays exactly as dealt" : "standard deck already reconciled"));
                before = Now(); game.Equip("leftHand", 0, shield); Held("armoury off-hand restored", before, true);
                before = Now(); game.Equip("rightHand", 1, spare); Held("armoury equip spare set", before, false);
                before = Now(); game.SelectSet("rightHand", 1); Held("armoury select spare", before, lending);
                Check((int)game.RunPlayer["loadout"]["active"]["rightHand"] == 1, label + ": spare set is active");
                before = Now(); game.SelectSet("rightHand", 0); Held("armoury select back", before, lending);
                var armour = (string)game.RunPlayer["loadout"]["sets"]["armor"][0];
                before = Now(); game.Equip("armor", 0, null); Held("armour off", before, lending);
                before = Now(); game.Equip("armor", 0, armour); Held("armour on", before, lending, true);
                // A fight: it deals the run deck; a mid-fight swap changes the main hand; the fight ends.
                // Shipped content swaps from the spare weapon to an emptied kit set (Dodge Roll); lending
                // content from the kit weapon to the spare (its package). A fight offers only filled sets
                // up to the last carried one, so the emptied set is the first.
                var from = lending ? 0 : 1; var to = 1 - from;
                if (!lending)
                {
                    before = Now(); game.SelectSet("rightHand", 1); Held("armoury select spare", before, false);
                    before = Now(); game.Equip("rightHand", 0, null); Held("armoury empty kit set", before, false);
                }
                var fightNode = game.LegalNodeIds.First(id => new[] { "monster", "fight" }.Contains((string)game.Map["nodes"][id]["type"]));
                game.Enter(fightNode); Check(game.Phase == OriginalRunPhase.Combat, label + ": enters a fight");
                Check(Ids(Piles(game)).SequenceEqual(Ids(game.RunPlayer["deck"])), label + ": fight deals the run deck");
                var openingPiles = Piles(game); before = Now();
                game.SwapSet("rightHand", to);
                Check((int)game.RunPlayer["loadout"]["active"]["rightHand"] == to, label + ": mid-fight swap made");
                var piles = Piles(game);
                if (pool)
                {
                    Check(Ids(piles).SequenceEqual(Ids(dealt)) && !piles.Any(Lent), label + " mid-fight swap: piles hold exactly the dealt cards");
                    foreach (var card in piles) Check(Text(card) == Text(dealt.First(d => (string)d["instanceId"] == (string)card["instanceId"])), label + " mid-fight swap: dealt card " + card["instanceId"] + " keeps its face");
                }
                else Check(!Ids(piles).SequenceEqual(Ids(openingPiles)) && piles.Any(c => Lent(c) && !openingPiles.Any(o => (string)o["instanceId"] == (string)c["instanceId"])), label + ": standard mid-fight swap still deals lent cards into the fight");
                Held("mid-fight swap", before, true, true);
                before = Now(); game = Win(game); Held("fight end", before, false, true);
                if (!pool) Check(Ids(game.RunPlayer["deck"]).SequenceEqual(Ids(before)), label + ": standard fight end keeps the swapped loadout's cards");
                before = Now(); game.SelectSet("rightHand", from); Held("post-fight Armoury", before, true, true);
            }
        }
        // Smith mounts: a card the player installs into a mount is the player's, not lent, so a
        // dealt deck keeps it at every door. The lending content also opens one extra mount per
        // item and makes the loose basics extractable; the reaver's bound armour is the item.
        {
            const string classId = "reaver"; const uint seed = 123u;
            var data = Lending(classId, out var spare).Data();
            data["balance"]["equipment"]["cardMounts"]["extraMounts"]["enabled"] = true;
            foreach (var card in new[] { "strike", "defend", "gorefireSlash" }) ((JArray)data["tagging"]).Add(new JObject { ["family"] = "card", ["scope"] = "", ["objectId"] = card, ["tagId"] = "extractable" });
            var catalog = new OriginalContentCatalog(data.ToString());
            foreach (var mode in new[] { "sealed", "draft", "standard" })
            {
                var pool = mode != "standard"; var label = "mounts/" + mode + "/" + classId;
                var game = Dealt(catalog, mode, classId, seed, spare);
                var armourRef = "armor/" + classId + "/" + (string)game.RunPlayer["loadout"]["sets"]["armor"][0];
                var extraKey = "mount:" + armourRef + ":0"; var boundKey = "bound:" + armourRef + ":defend:0";
                // The real smith door (OriginalRunContent.ApplyService): an offered smith, the service, its reconcile.
                void Smith(string service, JObject request)
                {
                    var snapshot = game.Snapshot(); var run = (JObject)snapshot["run"]; var room = run["room"].DeepClone();
                    run["room"]["smith"] = new JObject { ["offered"] = true, ["services"] = new JArray("upgrade", "extract", "install") };
                    var content = new OriginalRunContent(catalog, reconcile: new OriginalPlayerProjection(catalog, mechanics).Reconcile);
                    Check(content.ApplyService(run, service, request, new RandomStreams(seed)), label + ": smith " + service);
                    run["room"] = room; game = OriginalGameSession.Restore(snapshot);
                }
                string Loose(params string[] skip) => (string)game.RunPlayer["deck"].First(c => c["equipmentRole"] == null && new[] { "strike", "defend", "gorefireSlash" }.Contains((string)c["cardId"]) && !skip.Contains((string)c["instanceId"]))["instanceId"];
                var first = Loose(); var firstCard = (string)game.RunPlayer["deck"].First(c => (string)c["instanceId"] == first)["cardId"];
                Smith("install", new JObject { ["itemRef"] = armourRef, ["mountKey"] = extraKey, ["instanceId"] = first });
                if (pool)
                {
                    // No extraction (owner ruling, 2026-10-02): the bound mount is emptied the way a save
                    // from before that rule carries it, so the install below still has an open mount.
                    var refusedAt = Text(game.RunPlayer); ExtractRefused(game, armourRef, boundKey, label);
                    Check(Text(game.RunPlayer) == refusedAt, label + ": a refused extraction changes nothing");
                    var snapshot = game.Snapshot(); var run = (JObject)snapshot["run"];
                    if (!(run["itemMounts"] is JObject)) run["itemMounts"] = new JObject();
                    if (!(run["itemMounts"][armourRef] is JObject)) run["itemMounts"][armourRef] = new JObject();
                    run["itemMounts"][armourRef][boundKey] = new JObject { ["card"] = null, ["extractions"] = 1 };
                    game = OriginalGameSession.Restore(snapshot);
                }
                else Smith("extract", new JObject { ["itemRef"] = armourRef, ["mountKey"] = boundKey });
                var second = Loose(first); var secondCard = (string)game.RunPlayer["deck"].First(c => (string)c["instanceId"] == second)["cardId"];
                Smith("install", new JObject { ["itemRef"] = armourRef, ["mountKey"] = boundKey, ["instanceId"] = second });
                var installed = (JArray)game.RunPlayer["deck"].DeepClone();
                bool Holds(IEnumerable<JToken> cards) => cards.Any(c => (string)c["instanceId"] == extraKey && (string)c["cardId"] == firstCard) && cards.Any(c => (string)c["instanceId"] == boundKey && (string)c["cardId"] == secondCard) && !cards.Any(c => (string)c["instanceId"] == first || (string)c["instanceId"] == second);
                Check(Holds(installed), label + ": installed cards materialize in their mounts\nACTUAL " + Text(installed));
                if (pool) Check(installed.Where(Lent).All(c => (string)c["instanceId"] == extraKey || (string)c["instanceId"] == boundKey), label + ": the installs bring no lent card with them");
                string Canon(IEnumerable<JToken> cards) => Text(new JArray(cards.OrderBy(c => (string)c["instanceId"], StringComparer.Ordinal).Select(c => c.DeepClone())));
                void Kept(string door, bool resume = false)
                {
                    var deck = (JArray)game.RunPlayer["deck"];
                    Check(Holds(deck), label + " " + door + ": installed cards stay in their mounts\nACTUAL " + Text(deck));
                    // Order-free: re-wearing the armour appends its mounted cards again, as on a Standard run.
                    if (pool) Check(Canon(deck) == Canon(installed), label + " " + door + ": deck stays as dealt plus its installs\nEXPECTED " + Text(installed) + "\nACTUAL " + Text(deck));
                    if (resume) Check(Text(OriginalGameSession.Restore(JObject.Parse(game.Snapshot().ToString())).RunPlayer["deck"]) == Text(deck), label + " " + door + ": resume keeps the deck");
                }
                Kept("after the smith", true);
                var shield = (string)game.RunPlayer["loadout"]["sets"]["leftHand"][0];
                game.Equip("leftHand", 0, null); Kept("armoury off-hand emptied");
                var projected = game.RunPlayer; new OriginalPlayerProjection(catalog, mechanics).Reconcile(projected);
                Check(Text(projected["deck"]) == Text(game.RunPlayer["deck"]), label + ": service reconcile keeps the installs");
                game.Equip("leftHand", 0, shield); Kept("armoury off-hand restored");
                game.Equip("rightHand", 1, spare); game.SelectSet("rightHand", 1); Kept("armoury select spare");
                game.SelectSet("rightHand", 0); Kept("armoury select back");
                var armour = (string)game.RunPlayer["loadout"]["sets"]["armor"][0];
                game.Equip("armor", 0, null);
                Check(!game.RunPlayer["deck"].Any(c => (string)c["instanceId"] == extraKey || (string)c["instanceId"] == boundKey), label + ": armour off takes its mounted cards with it");
                game.Equip("armor", 0, armour); Kept("armour back on", true);
                var fightNode = game.LegalNodeIds.First(id => new[] { "monster", "fight" }.Contains((string)game.Map["nodes"][id]["type"]));
                game.Enter(fightNode); Check(game.Phase == OriginalRunPhase.Combat, label + ": enters a fight");
                Check(Holds(Piles(game)), label + ": the fight deals the installed cards");
                game.SwapSet("rightHand", 1);
                var piles = Piles(game);
                Check(Holds(piles), label + ": a mid-fight swap keeps the installed cards in the piles");
                if (pool) Check(Ids(piles).SequenceEqual(Ids(installed)), label + ": a mid-fight swap deals no lent card");
                Kept("mid-fight swap", true);
                game = Win(game); Kept("fight end", true);
                game.SelectSet("rightHand", 0); Kept("post-fight Armoury", true);
            }
        }
        // Extraction (owner ruling, 2026-10-02; the original's model/cardExtraction.js
        // extractionRefusal, tests/pool-deck-extraction.test.mjs): a Sealed or Draft run never lifts
        // a lent card out of a mount, however the smith is reached — the service door returns false,
        // the commit throws (a free grant too) — and no save carries it in: a resumed snapshot, one
        // whose room offers extraction outright, is refused the same. A Standard run still extracts.
        void ExtractRefused(OriginalGameSession at, string itemRef, string mountKey, string label)
        {
            var catalog = at.Catalog;
            Check(CardMountService.ExtractionRefusal(at.RunPlayer) == CardMountService.PoolDeckRefusal, label + ": extraction refusal is named");
            var snapshot = at.Snapshot(); var run = (JObject)snapshot["run"];
            run["room"]["smith"] = new JObject { ["offered"] = true, ["services"] = new JArray("upgrade", "extract", "install") };
            var before = Text(run);
            var content = new OriginalRunContent(catalog, reconcile: new OriginalPlayerProjection(catalog, mechanics).Reconcile);
            Check(!content.ApplyService(run, "extract", new JObject { ["itemRef"] = itemRef, ["mountKey"] = mountKey }, new RandomStreams(1)), label + ": the smith door refuses extraction");
            Check(Text(run) == before, label + ": the refused smith door changes nothing");
            foreach (var free in new[] { false, true })
            {
                try { new CardMountService(catalog).Extract(run, itemRef, mountKey, free); Check(false, label + ": Extract (free " + free + ") was not refused"); }
                catch (ArgumentException error) { Check(error.Message == CardMountService.PoolDeckRefusalText, label + ": Extract (free " + free + ") refused by name, got " + error.Message); }
            }
            // The same save, resumed from its bytes, with the smith's extraction on offer.
            var resumed = OriginalGameSession.Restore(JObject.Parse(snapshot.ToString()));
            Check(CardMountService.ExtractionRefusal(resumed.RunPlayer) == CardMountService.PoolDeckRefusal, label + ": a resumed save is still refused");
            var resumedRun = (JObject)resumed.Snapshot()["run"]; resumedRun["room"]["smith"] = run["room"]["smith"].DeepClone();
            Check(!content.ApplyService(resumedRun, "extract", new JObject { ["itemRef"] = itemRef, ["mountKey"] = mountKey }, new RandomStreams(1)), label + ": a resumed save offering extraction is refused");
        }
        // Shipped content tags no lent card extractable, so the lending content makes its bound
        // armour's basic and the spare weapon's art extractable (the spare rides in storage).
        {
            const string classId = "reaver";
            var data = Lending(classId, out var spare).Data();
            foreach (var card in new[] { "defend", "quickstep" }) ((JArray)data["tagging"]).Add(new JObject { ["family"] = "card", ["scope"] = "", ["objectId"] = card, ["tagId"] = "extractable" });
            var catalog = new OriginalContentCatalog(data.ToString());
            foreach (var mode in new[] { "sealed", "draft", "standard" })
            {
                var label = "extract/" + mode;
                var game = Dealt(catalog, mode, classId, 123u, spare);
                // Every authored extractable mount on every owned item: what a Standard run could lift.
                var mounts = new CardMountService(catalog); var run = game.RunPlayer;
                var targets = new ItemUpgradeService(catalog).OwnedRefs(run).Where(item => !item.StartsWith("relic/", StringComparison.Ordinal))
                    .SelectMany(item => mounts.MountRows(item, run["itemMounts"] as JObject).Where(row => (bool)row["extractable"]).Select(row => (item, key: (string)row["mountKey"]))).ToArray();
                Check(targets.Length > 0, label + ": the fixture carries an extractable mount");
                if (mode != "standard") { foreach (var (item, key) in targets) ExtractRefused(game, item, key, label + " " + key); continue; }
                Check(CardMountService.ExtractionRefusal(run) == null, label + ": a Standard run is not refused");
                var (standardItem, standardKey) = targets[0];
                var snapshot = game.Snapshot(); var standardRun = (JObject)snapshot["run"];
                standardRun["room"]["smith"] = new JObject { ["offered"] = true, ["services"] = new JArray("extract") };
                var content = new OriginalRunContent(catalog, reconcile: new OriginalPlayerProjection(catalog, mechanics).Reconcile);
                var deckBefore = ((JArray)standardRun["deck"]).Count;
                Check(content.ApplyService(standardRun, "extract", new JObject { ["itemRef"] = standardItem, ["mountKey"] = standardKey }, new RandomStreams(1)), label + ": a Standard run still extracts");
                Check(((JArray)standardRun["deck"]).Any(c => ((string)c["instanceId"]).StartsWith("extracted:", StringComparison.Ordinal)), label + ": the extracted card joins the deck");
            }
        }
        Console.WriteLine($"PoolDeckChecks: {checks} dealt-deck reconcile checks passed");
        return checks;
    }
}
