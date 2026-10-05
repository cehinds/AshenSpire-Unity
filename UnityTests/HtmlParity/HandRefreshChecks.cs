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
        return checks;
    }
}
