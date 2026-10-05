#nullable enable
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

internal static class PropertyCarrierChecks
{
    public static int Run(string root)
    {
        var checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        var fixture=JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/coop-reference.json")));
        var published=JObject.Parse(File.ReadAllText(Path.Combine(root,"TestResults/HtmlParity/test898/content-reference.json")));
        var content=(JObject)fixture["content"]!.DeepClone();content["propertyRules"]=published["propertyRules"]!.DeepClone();
        var relic=content["relics"]!.OfType<JObject>().First();relic["triggers"]=new JArray();relic["passives"]=new JObject();relic["propertyTags"]=new JArray("resonance","overcharge","siphon");
        var hero=content["classes"]!.OfType<JObject>().First(c=>(string?)c["id"]=="reaver");hero["propertyTags"]=new JArray("favored");
        content["classTree"]=JArray.Parse("[{classId:'reaver',nodeId:'bloodTempo'},{classId:'starseer',nodeId:'warlord'}]");
        content["companions"]=JArray.Parse("[{id:'squire',propertyTags:['hollowSquire','onceGuard','turnGuard']}]");
        ((JArray)content["propertyRules"]!).Add(JObject.Parse("{tag:'onceGuard',triggers:[{on:'cardPlayed',once:true,do:[{op:'block',target:'self',amount:2}]}]}"));
        ((JArray)content["propertyRules"]!).Add(JObject.Parse("{tag:'turnGuard',triggers:[{on:'cardPlayed',limitPerTurn:1,do:[{op:'block',target:'self',amount:1}]}]}"));
        content["sigils"]=JArray.Parse("[{id:'legend',rarity:'legendary',propertyTags:['overcharge']},{id:'ordinary',rarity:'common',propertyTags:['overcharge']}]");
        var sword=content["equipment"]!["armaments"]!.OfType<JObject>().First(r=>(string?)r["id"]=="straightSword");sword["propertyTags"]=new JArray("overcharge");
        var first=content["enemies"]!.OfType<JObject>().First(r=>(string?)r["id"]=="wanderingSoldier");
        var second=content["enemies"]!.OfType<JObject>().First(r=>(string?)r["id"]=="blightHound");second["arcaneExposure"]=first["arcaneExposure"]!.DeepClone();second["arcaneExposure"]!["threshold"]=20;
        foreach(var enemy in new[]{first,second}){enemy["firstMove"]="wait";enemy["moves"]=JObject.Parse("{wait:{weight:1,intent:'defend',block:1}}");enemy["phases"]=new JArray();}
        content["balance"]!["arcaneExposure"]!["schoolBuildupMultipliers"]!["arcane"]=1;
        var catalog=new OriginalContentCatalog(content.ToString());var model=new OriginalPropertyCarriers(catalog);
        var runFixture=JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/run-reference.json")));
        var player=(JObject)runFixture["players"]![0]!.DeepClone();player["classId"]="reaver";
        player["coreTags"]=new JArray("bloodTempo","warlord");player["companions"]=JArray.Parse("[{id:'squire',remaining:2}]");
        player["attunedSigils"]=new JArray("legend","ordinary");player["sigilSlots"]=JObject.Parse("{'armament/straightSword':['ordinary','ordinary']}");
        var input=OriginalPropertyCarriers.Input(player);var original=input.ToString();var mounts=model.Build(input);
        Check(input.ToString()==original,"Deriving held properties is read only");
        Check(mounts["class:reaver"]!["rules"]!.Count()==2,"Only the class's own chosen tree nodes mount");
        Check(mounts["companion:squire"]!=null,"Travelling companion identity reaches combat mounts");
        Check(mounts["sigil:legend"]!=null && mounts["sigil:ordinary"]?["heldBy"]!=null,"Legendary attunement and worn slotted sigils have distinct ownership windows");
        Check(mounts.Properties().Select(p=>p.Name).SequenceEqual(mounts.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal)),"Sources use deterministic ordinal order");
        var aside=(JObject)input.DeepClone();aside["classUnequipped"]=true;aside["loadout"]=null;aside["companionIds"]=new JArray();
        var removed=model.Build(aside);
        Check(removed["class:reaver"]==null && removed["companion:squire"]==null && removed["sigil:ordinary"]==null && removed["sigil:legend"]!=null,"Unmounting equipment/class/companions retains only genuinely held sources");
        Check(!model.Rules(new[]{"warlord","bulwarkKing"}).Any(r=>(string?)r["tag"]=="warlord"),"Authored exclusions suppress the excluded property");
        var unknownRefused=false;try{model.Rules(new[]{"missing"});}catch(ArgumentException){unknownRefused=true;}Check(unknownRefused,"Unknown held property is refused by name");
        var rules=JObject.Parse(File.ReadAllText(Path.Combine(root,"GameContent/Unity/Original/mechanics.json")));rules["properties"]=JObject.Parse("{mounted:true}");
        var deck=new[]{"a","b"}.Select(id=>new JObject{["instanceId"]=id,["cardId"]="strike"}).ToArray();
        JObject Resolve(JObject card)
        {
            var face=catalog.Record("cards","strike");face["cost"]=0;face["manaCost"]=0;face["staminaCost"]=0;face["keywords"]=new JArray();
            face["damageSchool"]="arcane";face["exposureBuildupPerHit"]=3;face["effects"]=JArray.Parse("[{op:'damage',target:'enemy',amount:1}]");return face;
        }
        JObject CombatPlayer(int skill)
        {
            var p=(JObject)fixture["fixtures"]![0]!["players"]![0]!.DeepClone();p.Remove("loadout");p["classId"]="reaver";p["mana"]=0;p["maxMana"]=5;p["drawPerTurn"]=2;
            p["relicIds"]=new JArray(relic["id"]!.DeepClone());p["skills"]=JObject.Parse("{'item:magic-focus':{level:0,xp:0,pendingDrafts:0}}");p["skills"]!["item:magic-focus"]!["level"]=skill;return p;
        }
        foreach(var skill in new[]{0,6,7})
        {
            var game=new CombatSession(catalog,rules,new RandomStreams(17),CombatPlayer(skill),deck,new[]{"wanderingSoldier","blightHound"},Resolve);
            game.PlayCard("a","e1");Check((int)game.Enemies[0]!["arcaneExposure"]!["value"]! == 4,"Overcharge multiplies hit buildup and floors once");
            var saved=game.Snapshot();Check(saved["propertyInput"]!=null && saved["propertyMounts"]==null,"Save freezes carrier input, not duplicate rule definitions");
            var restored=CombatSession.Restore(catalog,rules,saved,Resolve);Check(JToken.DeepEquals(saved,restored.Snapshot()),"Mounted property state restores exactly");
            var events=game.PlayCard("b","e1");Check(JToken.DeepEquals(events,restored.PlayCard("b","e1")),"Property queue and announcements replay exactly");
            Check((int)game.Player["mana"]! == (skill<7?1:2),"Siphon reads the owning player's saved skill level");
            Check((int)game.Enemies[1]!["arcaneExposure"]!["value"]! == 4,"Resonance uses the broken enemy's threshold, without multiplying a direct pour");
            Check(events.Count(e=>(string?)e["type"]=="relicTriggered")==2,"Each triggered relic property announces once");
        }
        var legacy=(JObject)rules.DeepClone();legacy.Remove("properties");
        var old=new CombatSession(catalog,legacy,new RandomStreams(17),CombatPlayer(7),deck,new[]{"wanderingSoldier","blightHound"},Resolve);old.PlayCard("a","e1");
        Check((int)old.Enemies[0]!["arcaneExposure"]!["value"]! == 3 && old.Snapshot()["propertyInput"]==null,"Absent saved rule preserves legacy hits and snapshots");
        var enabled=new CombatSession(catalog,rules,new RandomStreams(17),CombatPlayer(0),deck,new[]{"wanderingSoldier"},Resolve).Snapshot();enabled.Remove("propertyInput");
        var refused=false;try{CombatSession.Restore(catalog,rules,enabled,Resolve);}catch(ArgumentException){refused=true;}Check(refused,"Current-rule save cannot silently lose its carriers");
        var guarded=CombatPlayer(0);guarded["companionIds"]=new JArray("squire");
        var guardGame=new CombatSession(catalog,rules,new RandomStreams(17),guarded,deck,new[]{"wanderingSoldier"},Resolve);
        guardGame.PlayCard("a","e1");Check((int)guardGame.Player["block"]! == 3,"Held companion contributes its once and per-turn hooks");
        var guardSave=guardGame.Snapshot();guardSave["propertyInput"]!["companionIds"]=new JArray();
        var unmounted=CombatSession.Restore(catalog,rules,guardSave,Resolve);var detached=unmounted.Snapshot();
        detached["propertyInput"]!["companionIds"]=new JArray("squire");
        guardGame=CombatSession.Restore(catalog,rules,detached,Resolve);
        var secondPlay=guardGame.PlayCard("b","e1");
        Check(!secondPlay.Any(e=>(string?)e["type"]=="blockGained") && (int)guardGame.Player["block"]! == 3,"Re-deriving a returning carrier does not reset saved once/per-turn gates");
        guardGame.EndTurn();guardGame.PlayCard((string)guardGame.Hand[0]!["instanceId"]!,"e1");
        Check((int)guardGame.Player["block"]! == 1,"A new turn refreshes the per-turn gate but not the once-per-fight gate");
        foreach(var actor in new[]{"a","b"})
        {
            var inputs=new[]{CombatPlayer(0),CombatPlayer(7)};
            for(var i=0;i<inputs.Length;i++){inputs[i]["id"]=i==0?"a":"b";inputs[i]["deck"]=new JArray(deck);inputs[i]["companionIds"]=new JArray("squire");}
            var party=new OriginalCoopCombat(catalog,rules,new RandomStreams(17),inputs,new[]{"wanderingSoldier","blightHound"},(id,card)=>Resolve(card));
            party.Play(actor,"a","e1");var savedParty=party.Snapshot();
            var replay=OriginalCoopCombat.Restore(catalog,rules,savedParty,(id,card)=>Resolve(card));
            Check(JToken.DeepEquals(savedParty,replay.Snapshot()),"Every party carrier and gate restores exactly");
            var partyEvents=party.Play(actor,"b","e1");
            Check(JToken.DeepEquals(partyEvents,replay.Play(actor,"b","e1")) && JToken.DeepEquals(party.Snapshot(),replay.Snapshot()),"Party property commands replay receipts and whole state");
            foreach(var row in party.Snapshot()["seats"]!)
            {
                var owns=(string?)row["id"]==actor;
                Check((int)row["combat"]!["player"]!["mana"]! == (owns?(actor=="a"?1:2):0),"Siphon reads its own seat's skill ledger and mana pool");
                Check((int)row["combat"]!["player"]!["block"]! == (owns?3:0),"Once/per-turn gates belong to the acting seat");
            }
        }
        checks+=PartyHealing(content,rules,CombatPlayer);
        checks+=PredicatesAndValidation(content,rules,CombatPlayer);
        var swapFixture=JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/swap-reference.json")))["fixtures"]![0]!;
        var swapRun=(JObject)swapFixture["run"]!.DeepClone();var swapBattle=(JObject)swapFixture["before"]!.DeepClone();
        swapBattle["propertyInput"]=OriginalPropertyCarriers.Input(swapRun);
        var beforeSwapRun=swapRun.DeepClone();var beforeSwapBattle=swapBattle.DeepClone();
        var swap=new OriginalCombatEquipment(catalog,rules).Apply(swapRun,swapBattle,"rightHand",1);
        var changedMounts=model.Build((JObject)swap["combat"]!["propertyInput"]!);
        Check(changedMounts["armament:armament/straightSword"]==null,"A real equipment swap removes the unworn sword's property carrier");
        Check(JToken.DeepEquals(swap["combat"]!["propertyInput"],OriginalPropertyCarriers.Input((JObject)swap["run"]!)),"Equipment transaction carries the new run's property identities");
        Check(JToken.DeepEquals(beforeSwapRun,swapRun) && JToken.DeepEquals(beforeSwapBattle,swapBattle),"Property-aware equipment swapping leaves both inputs unchanged");
        return checks;
    }
    private static int PredicatesAndValidation(JObject content,JObject rules,Func<int,JObject> makePlayer)
    {
        var checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        foreach(var table in new JToken[]{new JObject(),new JArray(3),JArray.Parse("[{tag:'a'},{tag:'a'}]")})
        {
            var bad=(JObject)content.DeepClone();bad["propertyRules"]=table;var refused=false;
            try{new OriginalPropertyCarriers(new OriginalContentCatalog(bad.ToString()));}catch(ArgumentException){refused=true;}
            Check(refused,"Malformed/duplicate property definitions fail explicitly");
        }
        var data=(JObject)content.DeepClone();
        ((JArray)data["companions"]!).Add(JObject.Parse("{id:'scholar',propertyTags:['study']}"));
        ((JArray)data["propertyRules"]!).Add(JObject.Parse("{tag:'study',triggers:[{on:'cardPlayed',if:{p:'all',preds:[{p:'classLevelAtLeast',level:5},{p:'cardTagIs',tag:'blessed'}]},do:[{op:'block',target:'self',amount:7}]}]}"));
        var catalog=new OriginalContentCatalog(data.ToString());
        foreach(var level in new[]{0d,4d,5d,5.5d})foreach(var tagSource in new[]{"authored","derived","inherited"})
        {
            var player=makePlayer(0);player["relicIds"]=new JArray();player["companionIds"]=new JArray("scholar");
            player["skills"]!["class:reaver"]=new JObject{["level"]=level};
            JObject Resolve(JObject card)
            {
                var face=catalog.Record("cards","strike");face["cost"]=0;face["manaCost"]=0;face["staminaCost"]=0;face["keywords"]=new JArray();face["effects"]=new JArray();
                face["tags"]=new JArray("blessed");face["authoredTags"]=tagSource=="authored"?new JArray("blessed"):new JArray();
                face["derivedTags"]=tagSource=="derived"?new JArray("blessed"):new JArray();return face;
            }
            var game=new CombatSession(catalog,rules,new RandomStreams(1),player,new[]{new JObject{["instanceId"]="a",["cardId"]="strike"}},new[]{"wanderingSoldier"},Resolve);
            var played=game.PlayCard("a","e1");
            Check((int)game.Player["block"]! == (level==5 && tagSource!="inherited"?7:0),"Class gates require an integer level and the card's own or derived tag");
            var unequipped=(JObject)player.DeepClone();unequipped["classUnequipped"]=true;
            var noClass=new CombatSession(catalog,rules,new RandomStreams(1),unequipped,new[]{new JObject{["instanceId"]="a",["cardId"]="strike"}},new[]{"wanderingSoldier"},Resolve);
            noClass.PlayCard("a","e1");Check((int)noClass.Player["block"]! == 0,"Setting the class aside disables its class-level predicate");
        }
        return checks;
    }
    private static int PartyHealing(JObject content,JObject rules,Func<int,JObject> makePlayer)
    {
        var checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        var data=(JObject)content.DeepClone();
        ((JArray)data["companions"]!).Add(JObject.Parse("{id:'healer',propertyTags:['warmth','sealOfPlenty','unsealedScroll','partyGenerated']}"));
        ((JArray)data["propertyRules"]!).Add(JObject.Parse("{tag:'partyGenerated',triggers:[{on:'healed',once:true,if:{p:'all',preds:[{p:'eventTargetIsOwner'},{p:'healPositive'}]},do:[{op:'addCard',card:'strike',pile:'discard',position:'bottom'}]}]}"));
        var catalog=new OriginalContentCatalog(data.ToString());
        JObject Resolve(string seat,JObject card)
        {
            var face=catalog.Record("cards",(string)card["cardId"]!);face["cost"]=0;face["manaCost"]=0;face["staminaCost"]=0;face["keywords"]=new JArray();
            face["effects"]=(string?)card["cardId"]=="defend"
                ? JArray.Parse("[{op:'heal',target:'ally',amount:3},{op:'block',target:'self',amount:4}]") : new JArray();
            return face;
        }
        foreach(var size in new[]{2,4})
        {
            var players=Enumerable.Range(0,size).Select(i=>
            {
                var p=makePlayer(i*7);p["id"]="p"+i;p["relicIds"]=new JArray();p["companionIds"]=new JArray("healer");p["orderedDraw"]=true;
                p["hp"]=(int)p["maxHp"]!-20;
                p["deck"]=new JArray(Enumerable.Range(0,8).Select(n=>new JObject{["instanceId"]="p"+i+"c"+n,["cardId"]=n<2?"defend":"strike"}));return p;
            }).ToArray();
            var party=new OriginalCoopCombat(catalog,rules,new RandomStreams(39),players,new[]{"wanderingSoldier"},Resolve);
            JObject Seat(JObject saved,string id)=>(JObject)saved["seats"]!.First(s=>(string?)s["id"]==id)!["combat"]!;
            var original=party.Snapshot();var received=party.Play("p0","p0c0","p1");var saved=party.Snapshot();
            Check((int)Seat(saved,"p1")["player"]!["mana"]! == 1 && (int)Seat(saved,"p0")["player"]!["mana"]! == 0,"An ally heal restores only the recipient's property mana");
            Check((int)Seat(saved,"p1")["player"]!["block"]! == 2 && (int)Seat(saved,"p0")["player"]!["block"]! == 4,"Ally healing uses the recipient's property owner");
            Check(Seat(saved,"p1")["piles"]!["hand"]!.Count()==Seat(original,"p1")["piles"]!["hand"]!.Count()+2,"An inactive recipient draws into its own hand");
            Check(Seat(saved,"p0")["piles"]!["hand"]!.Count()==Seat(original,"p0")["piles"]!["hand"]!.Count()-1,"The healer's hand only spends the played card");
            var blocks=received.Where(e=>(string?)e["type"]=="blockGained").ToArray();
            Check(blocks.Length==2 && (int)blocks[0]["amount"]! == 4 && (int)blocks[1]["amount"]! == 2,"All original effects execute before queued ally reactions");
            Check(received.Any(e=>(string?)e["type"]=="healed" && (string?)e["targetPlayerId"]=="p1"),"Heal receipt names the recipient before triggers scan");
            var replay=OriginalCoopCombat.Restore(catalog,rules,saved,Resolve);
            var later=party.Play("p0","p0c1","p1");
            Check(JToken.DeepEquals(later,replay.Play("p0","p0c1","p1")) && JToken.DeepEquals(party.Snapshot(),replay.Snapshot()),"Shared FIFO and inactive-owner gates survive reload exactly");
            Check(!later.Any(e=>(string?)e["type"]=="manaRestored" || (string?)e["type"]=="cardDrawn"),"Recipient's once gates cannot refire after reload");
            party.Play("p1","p1c0","p0");
            var afterReverse=party.Snapshot();
            Check((int)Seat(afterReverse,"p0")["player"]!["mana"]! == 1,"A second seat has independent once-per-fight gates");
            var generated=afterReverse["seats"]!.SelectMany(s=>s["combat"]!["piles"]!["discard"]!).Where(c=>((string)c["instanceId"]!).StartsWith("gen")).Select(c=>(string)c["instanceId"]!).ToArray();
            Check(generated.Length==2 && generated.Distinct().Count()==2 && (int)afterReverse["idCounter"]! >=2,"Owned queue execution retains one party-wide generated-card counter");
            var beforeRefusal=party.Snapshot();var refused=false;try{party.Play("p0","missing","p1");}catch(ArgumentException){refused=true;}
            Check(refused && JToken.DeepEquals(beforeRefusal,party.Snapshot()),"Rejected party command preserves all property state");
            var disconnected=OriginalCoopCombat.Restore(catalog,rules,afterReverse,Resolve);disconnected.DisconnectForHostRestore();
            foreach(var p in players)disconnected.Join(p);
            Check(JToken.DeepEquals(afterReverse,disconnected.Snapshot()),"Host rejoin preserves mounted carriers, generated IDs and gates");
            for(var i=2;i<size;i++)Check((int)Seat(afterReverse,"p"+i)["player"]!["mana"]! == 0,"Uninvolved party seat does not receive another seat's reaction");
        }
        return checks;
    }
}
