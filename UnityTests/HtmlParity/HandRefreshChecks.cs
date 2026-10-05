#nullable enable
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

internal static class HandRefreshChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
        void Equal(JToken a, JToken b, string label) => Check(JToken.DeepEquals(a, b), label);
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(root, "UnityTests/Parity/coop-reference.json")));
        var content = (JObject)fixture["content"]!.DeepClone();
        var enemy = content["enemies"]!.OfType<JObject>().First(e => (string?)e["id"] == "wanderingSoldier");
        enemy["firstMove"] = "wait";
        enemy["moves"] = new JObject { ["wait"] = new JObject { ["weight"] = 1, ["intent"] = "defend", ["block"] = 1 } };
        var catalog = new OriginalContentCatalog(content.ToString());
        var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/mechanics.json")));
        JObject Row(int count) => new JObject { ["stat"]="intelligence", ["statEnabled"]=false, ["base"]=count,
            ["baseline"]=0, ["pointsPerCard"]=1, ["minimum"]=count, ["maximum"]=Math.Max(15,count) };
        JObject Rules(bool? shuffle, int draw = 0) {
            var value = new JObject { ["retain"]=false, ["promptDiscard"]=false, ["replaceDiscards"]=false,
                ["reshuffle"]=true, ["discardLimit"]=10, ["drawMode"]="fixed", ["overflow"]="discard",
                ["starting"]=Row(6), ["turn"]=Row(draw), ["capacity"]=Row(15) };
            if (shuffle.HasValue) value["shuffleHand"]=shuffle.Value;
            return value;
        }
        var deck = new JArray(Enumerable.Range(0,6).Select(i => new JObject { ["instanceId"]="refresh"+i, ["cardId"]="defend" }));
        JObject Resolve(JObject card) {
            var def = catalog.Record("cards", "defend");
            def["cost"]=0; def["manaCost"]=0; def["staminaCost"]=0;
            def["keywords"] = (string?)card["instanceId"] == "refresh0" ? new JArray("retain")
                : (string?)card["instanceId"] == "refresh1" ? new JArray("ethereal") : new JArray();
            return def;
        }
        JObject Player() => (JObject)fixture["fixtures"]![0]!["players"]![0]!.DeepClone();
        foreach (var shuffle in new bool?[] { null, false, true })
        {
            var rules = Rules(shuffle);
            var game = new CombatSession(catalog, mechanics, new RandomStreams(9), Player(), deck.OfType<JObject>(), new[]{"wanderingSoldier"}, Resolve, handRules:rules);
            Check(game.Hand.Count==6, "Opening hand uses frozen rules");
            game.PlayCard("refresh2");
            var saved = game.Snapshot();
            var restored = CombatSession.Restore(catalog, mechanics, saved, Resolve);
            Equal(saved, restored.Snapshot(), "Hand rules and stream counters round-trip exactly");
            Equal(game.EndTurn(), restored.EndTurn(), "Reload preserves refresh event order");
            Equal(game.Snapshot(), restored.Snapshot(), "Reload preserves shuffled piles and RNG");
            Check(game.Hand.Count==1 && (string?)game.Hand[0]?["instanceId"]=="refresh0", "Retain survives while ordinary cards leave");
            Check(game.Pile("exhaust").Count==1 && (string?)game.Pile("exhaust")[0]?["instanceId"]=="refresh1", "Ethereal exhausts rather than returning");
            Check(game.Pile("discard").Any(c=>(string?)c["instanceId"]=="refresh2"), "Played card stays discarded");
            Check(game.Pile("discard").Count==(shuffle==true?1:4), "Absent or false flag preserves old discard behavior");
            Check(game.Pile("draw").Count==(shuffle==true?3:0), "Only unplayed ordinary cards return to draw");
            var refresh = ((JArray)game.Snapshot()["events"]!).Where(e=>(string?)e["reason"]=="handRefresh").ToArray();
            Check(refresh.Length==(shuffle==true?1:0), "One refresh receipt; none for legacy cleanup");
            if(shuffle==true) Check(((JArray)refresh[0]["cardInstanceIds"]!).Count==3, "Refresh receipt identifies returned instances");
        }
        var full = new CombatSession(catalog, mechanics, new RandomStreams(9), Player(), deck.OfType<JObject>(), new[]{"wanderingSoldier"}, Resolve, handRules:Rules(true,4));
        full.EndTurn(); Check(full.Hand.Count==5, "Fixed turn draw adds four cards to retained card");
        var bad=Rules(true); bad["shuffleHand"]="yes";
        Check(HandRules.Problems(bad).Count>0, "Invalid saved refresh flag is refused");

        foreach(var shuffle in new bool?[]{null,true})
        {
            var players=fixture["fixtures"]![0]!["players"]!.OfType<JObject>().Select(p=>{
                var result=(JObject)p.DeepClone(); result["deck"]=deck.DeepClone();
                if(shuffle.HasValue) result["handRules"]=Rules(shuffle);
                return result;
            }).ToArray();
            JObject ResolveSeat(string id,JObject card)=>Resolve(card);
            var party=new OriginalCoopCombat(catalog,mechanics,new RandomStreams(9),players,new[]{"wanderingSoldier"},ResolveSeat);
            if(shuffle==true) Check(party.Players.All(p=>p["piles"]!["hand"]!.Count()==6),"Each seat gets its snapshotted opening hand");
            party.EndTurn("p1");
            var saved=party.Snapshot(); var resumed=OriginalCoopCombat.Restore(catalog,mechanics,saved,ResolveSeat);
            Equal(saved,resumed.Snapshot(),"Partly ended party saves hand rules exactly");
            Equal(party.EndTurn("p2"),resumed.EndTurn("p2"),"Party refresh events deterministic after reload");
            Equal(party.Snapshot(),resumed.Snapshot(),"Party refresh state deterministic after reload");
            if(shuffle==true) foreach(var seat in party.Players) {
                Check(seat["piles"]!["draw"]!.Count()==4,"Every seat returns ordinary unplayed cards");
                Check(seat["piles"]!["hand"]!.Count()==1,"Every seat retains its own card");
                Check(seat["piles"]!["exhaust"]!.Count()==1,"Every seat exhausts its own Ethereal card");
                Check(!seat["piles"]!["discard"]!.Any(),"Refresh creates no turn-end discard");
            }
        }
        JObject OrderedCard(JObject card)
        {
            var def = catalog.Record("cards", "defend");
            def["cost"] = 0; def["manaCost"] = 0; def["staminaCost"] = 0;
            def["keywords"] = (string?)card["instanceId"] == "refresh3" ? new JArray("innate") : new JArray();
            if ((string?)card["instanceId"] == "shuffle") def["effects"] = new JArray(new JObject { ["op"]="shuffleDiscardIntoDraw", ["target"]="self" });
            return def;
        }
        string Ids(JToken cards) => string.Join(",", cards.Select(c => (string?)c["instanceId"]));
        CombatSession Ordered(JObject rules) => new CombatSession(catalog, mechanics, new RandomStreams(9), Player(),
            deck.OfType<JObject>(), new[]{"wanderingSoldier"}, OrderedCard, handRules:rules, orderedDraw:true);
        var openingRules=Rules(true,4); openingRules["starting"]=Row(3);
        var ordered=Ordered(openingRules);
        Check(Ids(ordered.Hand)=="refresh3,refresh0,refresh1", "Ordered opening puts Innate first and keeps relative order");
        Check((int)ordered.Snapshot()["rng"]!["shuffle"]! == 0, "Ordered opening consumes no shuffle RNG");
        var orderedSave=ordered.Snapshot();
        var orderedReload=CombatSession.Restore(catalog,mechanics,orderedSave,OrderedCard);
        Equal(orderedSave,orderedReload.Snapshot(),"Ordered solo state round-trips exactly");
        Equal(ordered.EndTurn(),orderedReload.EndTurn(),"Ordered refresh receipts survive reload");
        Equal(ordered.Snapshot(),orderedReload.Snapshot(),"Ordered refresh state survives reload");
        Check(Ids(ordered.Hand)=="refresh2,refresh4,refresh5,refresh0", "Refresh keeps remaining draw ahead of returned original-order cards");
        Check(Ids(ordered.Pile("draw"))=="refresh1,refresh3", "Innate promotion applies only to opening, not refresh");
        Check((int)ordered.Snapshot()["rng"]!["shuffle"]! == 0, "Ordered refresh consumes no shuffle RNG");
        Check(ordered.Snapshot()["events"]!.Any(e=>(string?)e["reason"]=="handRefresh"&&(bool?)e["ordered"]==true),"Ordered refresh identifies its receipt");
        var recycle=Ordered(Rules(false,6));
        foreach(var id in new[]{"refresh2","refresh5","refresh0"})recycle.PlayCard(id);
        recycle.EndTurn();
        Check(Ids(recycle.Hand)=="refresh0,refresh1,refresh2,refresh3,refresh4,refresh5", "Empty-pile return uses original order, not played/discarded order");
        Check((int)recycle.Snapshot()["rng"]!["shuffle"]! == 0,"Empty-pile ordered return consumes no shuffle RNG");
        var generated=Ordered(Rules(false,8)).Snapshot();
        ((JArray)generated["piles"]!["hand"]!).Insert(0,new JObject{["instanceId"]="generatedB",["cardId"]="defend"});
        ((JArray)generated["piles"]!["hand"]!).Insert(0,new JObject{["instanceId"]="generatedA",["cardId"]="defend"});
        var withGenerated=CombatSession.Restore(catalog,mechanics,generated,OrderedCard);withGenerated.EndTurn();
        Check(Ids(withGenerated.Hand)=="refresh0,refresh1,refresh2,refresh3,refresh4,refresh5,generatedA,generatedB", "Generated cards follow originals in stable discard order");
        var explicitShuffle=Ordered(Rules(false,0)).Snapshot();
        ((JArray)explicitShuffle["piles"]!["hand"]!).Add(new JObject{["instanceId"]="shuffle",["cardId"]="defend"});
        var shuffleGame=CombatSession.Restore(catalog,mechanics,explicitShuffle,OrderedCard);
        shuffleGame.PlayCard("refresh5");shuffleGame.PlayCard("refresh1");var shuffleEvents=shuffleGame.PlayCard("shuffle");
        Check((int)shuffleGame.Snapshot()["rng"]!["shuffle"]! > 0,"Explicit shuffle effect still consumes RNG in ordered mode");
        Check(shuffleEvents.Any(e=>(string?)e["type"]=="deckShuffled"&&e["ordered"]==null),"Explicit shuffle keeps its ordinary receipt");
        foreach(var malformed in new JToken[]{new JValue(true),new JObject(),new JObject{["order"]=new JArray("x","x")},new JObject{["order"]=new JArray(1)},new JObject{["order"]=new JArray("")}})
        {
            var invalid=(JObject)orderedSave.DeepClone();invalid["orderedDraw"]=malformed.DeepClone();var bytes=invalid.ToString();
            var refused=false;try{CombatSession.Restore(catalog,mechanics,invalid,OrderedCard);}catch(ArgumentException){refused=true;}
            Check(refused,"Malformed saved order is refused");Check(invalid.ToString()==bytes,"Refused order does not mutate saved bytes");
        }
        var orderedPlayers=fixture["fixtures"]![0]!["players"]!.OfType<JObject>().Select(p=>{
            var result=(JObject)p.DeepClone();result["deck"]=deck.DeepClone();result["handRules"]=openingRules.DeepClone();result["orderedDraw"]=true;return result;
        }).ToArray();
        JObject OrderedSeat(string id,JObject card)=>OrderedCard(card);
        var orderedParty=new OriginalCoopCombat(catalog,mechanics,new RandomStreams(9),orderedPlayers,new[]{"wanderingSoldier"},OrderedSeat);
        foreach(var seat in orderedParty.Players)Check(Ids(seat["piles"]!["hand"]!)=="refresh3,refresh0,refresh1","Each ordered seat opens its own stable deck");
        orderedParty.EndTurn("p1");var partySave=orderedParty.Snapshot();
        var partyReload=OriginalCoopCombat.Restore(catalog,mechanics,partySave,OrderedSeat);
        Equal(partySave,partyReload.Snapshot(),"Partly ended ordered co-op state round-trips exactly");
        Equal(orderedParty.EndTurn("p2"),partyReload.EndTurn("p2"),"Ordered co-op receipts replay after restore");
        Equal(orderedParty.Snapshot(),partyReload.Snapshot(),"Ordered co-op state replays after restore");
        foreach(var seat in orderedParty.Players)Check(Ids(seat["piles"]!["hand"]!)=="refresh2,refresh4,refresh5,refresh0","Every seat preserves its ordered return");
        Check((int)orderedParty.Snapshot()["rng"]!["shuffle"]! == 0,"Ordered party never consumes shuffle RNG for draw/refresh");
        return checks;
    }
}
