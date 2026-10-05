#nullable enable
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

internal static class ArcaneBuildupChecks
{
    public static int Run(string root)
    {
        var checks=0;
        void Check(bool value,string message) { if(!value) throw new Exception(message);checks++; }
        var fixture=JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/coop-reference.json")));
        var content=(JObject)fixture["content"]!.DeepClone();
        var enemies=content["enemies"]!.OfType<JObject>().ToArray();
        var source=enemies.First(e=>(string?)e["id"]=="wanderingSoldier");
        var other=enemies.First(e=>(string?)e["id"]=="blightHound");
        other["arcaneExposure"]=source["arcaneExposure"]!.DeepClone();other["arcaneExposure"]!["threshold"]=20;
        var immune=enemies.First(e=>(string?)e["id"]=="charredColossus");
        foreach(var enemy in new[]{source,other,immune}) { enemy["firstMove"]="wait";enemy["moves"]=JObject.Parse("{wait:{weight:1,intent:'defend',block:1}}");enemy["phases"]=new JArray(); }
        var relic=content["relics"]!.OfType<JObject>().First();
        relic["triggers"]=JArray.Parse("[{on:'arcaneBreak',if:{p:'eventSourceIsOwner'},do:[{op:'arcaneBuildup',target:'otherEnemies',pct:50}]}]");
        relic["passives"]=new JObject();
        var catalog=new OriginalContentCatalog(content.ToString());
        var rules=JObject.Parse(File.ReadAllText(Path.Combine(root,"GameContent/Unity/Original/mechanics.json")));
        var ids=new[]{"break","percent","single","zero","negative","overflow","self"};
        var deck=new JArray(ids.Select(id=>new JObject{["instanceId"]=id,["cardId"]="defend"}));
        JObject Player(JObject row,bool resonance)
        {
            var p=(JObject)row.DeepClone();p["deck"]=deck.DeepClone();p["drawPerTurn"]=ids.Length;
            p["relicIds"]=resonance?new JArray(relic["id"]!.DeepClone()):new JArray();return p;
        }
        JObject Resolve(JObject card)
        {
            var id=(string?)card["instanceId"];var face=catalog.Record("cards","defend");
            face["cost"]=0;face["manaCost"]=0;face["staminaCost"]=0;face["keywords"]=new JArray();
            var effect=new JObject{["op"]="arcaneBuildup",["target"]=id=="self"?"self":"enemy"};
            if(id=="percent") effect["pct"]=25;
            else effect["amount"]=id=="break"?8:id=="zero"?0:id=="negative"?-3:id=="overflow"?99:1;
            face["effects"]=new JArray(effect);return face;
        }
        CombatSession Game(bool resonance)=>new CombatSession(catalog,rules,new RandomStreams(19),Player((JObject)fixture["fixtures"]![0]!["players"]![0]!,resonance),deck.OfType<JObject>(),new[]{"wanderingSoldier","blightHound","charredColossus"},Resolve);
        foreach(var resonance in new[]{false,true})
        {
            var game=Game(resonance);var a=(string)game.Enemies[0]!["id"]!;var b=(string)game.Enemies[1]!["id"]!;var c=(string)game.Enemies[2]!["id"]!;
            var before=game.Snapshot();var restored=CombatSession.Restore(catalog,rules,before,Resolve);
            var ev=game.PlayCard("break",a);
            Check(JToken.DeepEquals(ev,restored.PlayCard("break",a)),"Arcane-break receipts replay exactly");
            Check(JToken.DeepEquals(game.Snapshot(),restored.Snapshot()),"Arcane-break state and trigger gates replay exactly");
            Check((int)game.Enemies[0]!["arcaneExposure"]!["value"]! == 0,"Filled meter resets to zero");
            Check((int)game.Enemies[0]!["statuses"]!["magicVulnerable"]!["stacks"]! == 25,"Break applies its configured value");
            Check((int)game.Enemies[0]!["statuses"]!["magicVulnerable"]!["duration"]! == 2,"Break preserves configured duration");
            Check((int)game.Enemies[1]!["arcaneExposure"]!["value"]! == (resonance?4:0),"Resonance pours half of firing threshold 8, not recipient threshold 20");
            Check(!ev.Any(e=>(string?)e["type"]=="arcaneExposureRefused" && (string?)e["targetId"]==a),"otherEnemies excludes the originating enemy");
            Check(JToken.DeepEquals(before["rng"],game.Snapshot()["rng"]),"Direct buildup and fanout consume no random draws");
            if(resonance) Check(ev.Any(e=>(string?)e["type"]=="arcaneExposureRefused" && (string?)e["targetId"]==c && (string?)e["reason"]=="immune" && e["attempted"]?.Type==JTokenType.Null),"Fanout reports immune refusal without invented attempted amount");
            ev=game.PlayCard("single",a);
            Check(ev.Any(e=>(string?)e["type"]=="arcaneExposureRefused" && (string?)e["reason"]=="locked"),"Active break status locks further buildup");
            Check(!ev.Any(e=>(string?)e["type"]=="arcaneExposureChanged"),"Locked meter remains empty");
            game.PlayCard("percent",b);
            Check((int)game.Enemies[1]!["arcaneExposure"]!["value"]! == (resonance?9:5),"No firing event uses recipient threshold");
            foreach(var id in new[]{"zero","negative","self"})
            {
                ev=game.PlayCard(id,b);
                Check(!ev.Any(e=>((string?)e["type"])?.StartsWith("arcane") == true),"Nonpositive buildup and player targets do nothing: "+id);
            }
            game.PlayCard("overflow",b);
            Check((int)game.Enemies[1]!["arcaneExposure"]!["value"]! == 0,"Overflow is discarded rather than carried across breaks");
        }
        var inputs=fixture["fixtures"]![0]!["players"]!.OfType<JObject>().Select(p=>Player(p,true)).ToArray();
        JObject Seat(string member,JObject card)=>Resolve(card);
        var party=new OriginalCoopCombat(catalog,rules,new RandomStreams(19),inputs,new[]{"wanderingSoldier","blightHound","charredColossus"},Seat);
        var saved=party.Snapshot();var copy=OriginalCoopCombat.Restore(catalog,rules,saved,Seat);var target=(string)party.Enemies[0]!["id"]!;
        Check(JToken.DeepEquals(party.Play("p1","break",target),copy.Play("p1","break",target)),"Co-op direct buildup events replay exactly");
        Check(JToken.DeepEquals(party.Snapshot(),copy.Snapshot()),"Co-op direct buildup saves exact shared enemy meters");
        Check((int)party.Enemies[1]!["arcaneExposure"]!["value"]! == 4,"Co-op resonance fires once for the owning seat");
        // The other new executable opcode belongs to run locations, never combat.
        var runContent=new OriginalRunContent(catalog);
        for(var capacity=1;capacity<=6;capacity++) for(var health=0;health<=capacity;health++)
        {
            var pool=new FlaskChargePool(capacity,health,capacity-health);pool.Spend("hp");pool.Spend("mana");
            var run=new JObject{["hp"]=7,["maxHp"]=13,["flaskCharges"]=pool.Snapshot(),["flasks"]=JArray.Parse("[{flaskId:'held'}]")};
            var rng=new RandomStreams(8);var counters=JObject.FromObject(rng.Snapshot());var utility=run["flasks"]!.DeepClone();
            runContent.ApplyEffects(run,JArray.Parse("[{op:'refillFlasks'}]"),rng);
            Check((int)run["flaskCharges"]!["hpCurrent"]! == health && (int)run["flaskCharges"]!["manaCurrent"]! == capacity-health,"Refill respects each saved allocation");
            Check((int)run["flaskCharges"]!["capacity"]! == capacity && (int)run["hp"]! == 7 && JToken.DeepEquals(run["flasks"],utility),"Refill does not grant capacity, healing or utility items");
            var after=run.DeepClone();runContent.ApplyEffects(run,JArray.Parse("[{op:'refillFlasks'}]"),rng);
            Check(JToken.DeepEquals(run,after) && JToken.DeepEquals(counters,JObject.FromObject(rng.Snapshot())),"Repeated refill is idempotent and consumes no randomness");
        }
        var runFixture=JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/run-reference.json")));
        var runCatalog=new OriginalContentCatalog(runFixture["content"]!.ToString());
        var callbacks=new OriginalRunContent(runCatalog);
        var player=(JObject)runFixture["players"]![0]!.DeepClone();
        player["sideboard"]=JArray.Parse("[{instanceId:'nativeRun1',cardId:'defend',upgraded:false},{instanceId:'nativeRun2',cardId:'defend',upgraded:false}]");
        var sideboard=player["sideboard"]!.DeepClone();var random=new RandomStreams(2);var initialCounters=JObject.FromObject(random.Snapshot());
        Check(callbacks.CollectReward(player,"card",JObject.Parse("{cardId:'defend'}"),random),"A new card can be collected while copies are set aside");
        Check((string?)player["deck"]!.Last()!["instanceId"] == "nativeRun3","Reward identity skips all reserved sideboard IDs");
        Check(JToken.DeepEquals(sideboard,player["sideboard"]) && JToken.DeepEquals(initialCounters,JObject.FromObject(random.Snapshot())),"Allocation preserves sideboard cards and randomness");
        var supplement=JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/event-choices.json")));
        var session=OriginalRunSession.Start(runCatalog,supplement,player,1,callbacks);var snapshot=session.Snapshot();
        Check(JToken.DeepEquals(snapshot,OriginalRunSession.Restore(snapshot,callbacks).Snapshot()),"Run saves preserve valid cross-zone card identities");
        foreach(var invalid in new JToken[]{JArray.Parse("[{instanceId:'nativeRun3',cardId:'defend'}]"),JArray.Parse("[{instanceId:'aside',cardId:'unknown'}]"),JArray.Parse("[{instanceId:'aside',cardId:'defend'},{instanceId:'aside',cardId:'defend'}]"),new JObject()})
        {
            var corrupt=(JObject)snapshot.DeepClone();corrupt["run"]!["sideboard"]=invalid.DeepClone();var refused=false;
            try { OriginalRunSession.Restore(corrupt,callbacks); } catch(ArgumentException) { refused=true; }
            Check(refused,"Save loader refuses malformed, unknown or duplicate sideboard ownership");
            Check(JToken.DeepEquals(snapshot,session.Snapshot()),"Invalid sideboard load leaves the live session unchanged");
        }
        return checks;
    }
}
