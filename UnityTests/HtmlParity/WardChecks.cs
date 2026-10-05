#nullable enable
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

internal static class WardChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        void Refused(Action action, string message) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception(message); }
        var old = new JObject { ["block"] = 8 };
        Check(OriginalBlockPresentation.Defense(old) == 8 && OriginalBlockPresentation.Ward(old) == 0, "Old Block is ordinary defense");
        OriginalBlockPresentation.Reconcile(old); var receipt = new JObject(); OriginalBlockPresentation.Receipt(old, receipt);
        Check(old.Property("wardBlock") == null && receipt.Count == 0, "Old entities and receipts do not gain invented provenance");
        for (var total = 0; total <= 12; total++) for (var ward = 0; ward <= 12; ward++)
        {
            var body = new JObject { ["block"] = total, ["wardBlock"] = ward };
            var before = body.ToString();
            Check(OriginalBlockPresentation.Defense(body) + OriginalBlockPresentation.Ward(body) == total && body.ToString() == before, "Read-only presentation preserves total mitigation");
            OriginalBlockPresentation.Reconcile(body);
            Check((int)body["wardBlock"]! == Math.Min(total, ward), "Lower Block consumes ordinary defense before Ward");
        }
        Check(OriginalBlockPresentation.Label(JObject.Parse("{block:8,wardBlock:5}")) == "Block 3 · Arcane Ward 5", "Ward label separates portions without doubling Block");
        foreach (var face in new[] { "{damageSchool:'magic'}", "{tags:['school:holy']}", "{tags:[{id:'kind:spell'}]}", "{manaCost:1}" })
            Check(OriginalBlockPresentation.IsMagical(JObject.Parse(face)), "Magical authored face classification: " + face);
        Check(!OriginalBlockPresentation.IsMagical(JObject.Parse("{damageSchool:'physical',manaCost:4,tags:['spell']}")), "Explicit physical school wins over fallback tags/cost");
        foreach (var value in new JToken[] { -1, 9, 1.5, "2", JValue.CreateNull() })
            Refused(() => OriginalBlockPresentation.Validate(new JObject { ["block"] = 8, ["wardBlock"] = value }), "Malformed Ward must refuse");
        Refused(() => OriginalBlockPresentation.Enabled(JObject.Parse("{block:{wardProvenance:'yes'}}")), "Malformed Ward rule must refuse");
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/coop-reference.json")));
        var content = (JObject)fixture["content"]!.DeepClone();
        var enemy = content["enemies"]!.OfType<JObject>().First(e => (string?)e["id"] == "wanderingSoldier");
        enemy["firstMove"] = "wait"; enemy["moves"] = JObject.Parse("{wait:{weight:1,intent:'defend',block:1}}");
        var catalog = new OriginalContentCatalog(content.ToString());
        var legacy = JObject.Parse(File.ReadAllText(Path.Combine(root,"GameContent/Unity/Original/mechanics.json")));
        var current = (JObject)legacy.DeepClone(); current["block"] = JObject.Parse("{wardProvenance:true}");
        var deck = new JArray(new[] { "ward", "ordinary", "hit", "heavy", "ally", "extra" }.Select(id => new JObject { ["cardId"]="defend", ["instanceId"]=id }));
        JObject Player() { var p=(JObject)fixture["fixtures"]![0]!["players"]![0]!.DeepClone(); p["drawPerTurn"]=6; p["deck"]=deck.DeepClone(); return p; }
        JObject Resolve(JObject card)
        {
            var id=(string?)card["instanceId"]; var damage=id=="hit"||id=="heavy";
            var face=catalog.Record("cards","defend");face["cost"]=0;face["manaCost"]=0;face["staminaCost"]=0;face["keywords"]=new JArray();
            face["damageSchool"]=id=="ward"||id=="ally"?"magic":"physical";
            face["effects"]=new JArray(new JObject { ["op"]=damage?"damage":"block",["target"]=id=="ally"?"ally":"self",["amount"]=id=="heavy"?7:id=="hit"?4:id=="ward"||id=="ally"?5:3 });
            return face;
        }
        CombatSession Game(JObject rules) => new CombatSession(catalog,rules,new RandomStreams(7),Player(),deck.OfType<JObject>(),new[]{"wanderingSoldier"},Resolve);
        foreach(var rules in new[]{legacy,current})
        {
            var game=Game(rules);var tracking=ReferenceEquals(rules,current);var hp=(int)game.Player["hp"]!;
            var gain=game.PlayCard("ward");game.PlayCard("ordinary");
            Check((int)game.Player["block"]! == 8, "Ward does not add extra Block");
            Check(tracking ? (int?)game.Player["wardBlock"]==5 : game.Player.Property("wardBlock")==null,"Saved flag preserves legacy provenance behavior");
            Check(tracking == gain.Any(e=>e["wardBlockRemaining"]!=null),"Gain receipt includes provenance only when known");
            var damage=game.PlayCard("hit");
            Check((int)game.Player["hp"]! == hp && (int)game.Player["block"]! == 4,"Ordinary and magical portions absorb one shared hit");
            if(tracking) Check((int)game.Player["wardBlock"]! == 4 && damage.Any(e=>(int?)e["wardBlockRemaining"]==4),"Partial Ward depletion reaches damage receipt");
            var saved=game.Snapshot();var restored=CombatSession.Restore(catalog,rules,saved,Resolve);
            Check(JToken.DeepEquals(saved,restored.Snapshot()),"Mixed defense saves exactly");
            Check(JToken.DeepEquals(game.PlayCard("heavy"),restored.PlayCard("heavy")),"Damage receipts replay exactly");
            Check((int)game.Player["hp"]! == hp-3 && (int)game.Player["block"]! == 0,"Overflow touches HP once");
            if(tracking) Check((int)game.Player["wardBlock"]! == 0,"Exhausted Ward becomes zero");
        }
        var turn=Game(current);turn.PlayCard("ward");turn.EndTurn();
        Check((int)turn.Player["block"]! == 0 && (int)turn.Player["wardBlock"]! == 0,"Solo turn reset reconciles Ward");
        var corrupt=Game(current).Snapshot();corrupt["player"]!["wardBlock"]=1;
        Refused(()=>CombatSession.Restore(catalog,current,corrupt,Resolve),"Solo invalid provenance refuses before use");
        var inputs=fixture["fixtures"]![0]!["players"]!.OfType<JObject>().Select(row=>{var p=(JObject)row.DeepClone();p["drawPerTurn"]=6;p["deck"]=deck.DeepClone();return p;}).ToArray();
        JObject Seat(string id,JObject card)=>Resolve(card);
        var party=new OriginalCoopCombat(catalog,current,new RandomStreams(7),inputs,new[]{"wanderingSoldier"},Seat,annotateMembers:true);
        var ally=party.Play("p1","ally","p2");party.Play("p2","ordinary");
        Check((int)party.Players[0]!["entity"]!["block"]! == 0 && (int)party.Players[1]!["entity"]!["wardBlock"]! == 5,"Ally Ward belongs to the recipient seat");
        Check(ally.Any(e=>(int?)e["wardBlockRemaining"]==5 && (string?)e["targetPlayerId"]=="p2"),"Ally receipt identifies its recipient");
        var partySave=party.Snapshot();var resumed=OriginalCoopCombat.Restore(catalog,current,partySave,Seat);
        Check(JToken.DeepEquals(partySave,resumed.Snapshot()),"Co-op Ward restores every seat exactly");
        Check(JToken.DeepEquals(party.Play("p2","hit"),resumed.Play("p2","hit")),"Co-op Ward damage replay is exact");
        party.EndTurn("p1");party.EndTurn("p2");
        Check(party.Players.All(p=>(int)p["entity"]!["block"]! == 0 && ((int?)p["entity"]!["wardBlock"]??0)==0),"Co-op turn reset clears both portions");
        partySave["seats"]![1]!["combat"]!["player"]!["wardBlock"]=99;
        Refused(()=>OriginalCoopCombat.Restore(catalog,current,partySave,Seat),"Corrupt party provenance refuses");
        return checks;
    }
}
