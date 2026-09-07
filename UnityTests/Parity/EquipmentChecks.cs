using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AshenSpire.Domain.Original;
public static class EquipmentChecks
{
    public static int Run(JObject reference,JObject supplement)
    {
        var catalog=new OriginalContentCatalog(reference["content"].ToString());int checks=0;
        void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
        bool Same(JToken a,JToken b)=>a.ToString(Formatting.None)==b.ToString(Formatting.None);
        var rules=new OriginalRunRules((JObject)reference["content"]);
        foreach(var fixture in reference["openedSetCases"])Check(rules.OpenedSets((JObject)fixture["run"],(string)fixture["slotId"])==(int)fixture["result"],"Original opened-set rule mismatch");
        var player=(JObject)reference["players"][0].DeepClone();player["profileMeta"]=new JObject{["unlocked"]=new JArray("rack2Right","rack3Right")};
        foreach(var attribute in ((JObject)player["attributes"]).Properties())attribute.Value=30;
        player["hp"]=(int)player["maxHp"]-7;
        player["testBaseMaximum"] = new JObject();
        var initialMods=new EquipmentRunModifiers(catalog).Resolve((JObject)player["loadout"],(string)player["class"]);
        foreach(var maximum in new[]{"maxHp","maxMana","maxStamina"})player["testBaseMaximum"][maximum]=(int)player[maximum]-(int)initialMods[maximum];
        void Reconcile(JObject run)
        {
            var mods=new EquipmentRunModifiers(catalog).Resolve((JObject)run["loadout"],(string)run["class"]);
            if(!(run["equipmentPoolDeficits"] is JObject))run["equipmentPoolDeficits"]=new JObject();
            foreach(var pair in new[]{("hp","maxHp"),("mana","maxMana"),("stamina","maxStamina")})
            {
                var moved=EquipmentRunModifiers.MovePool((int)run[pair.Item2],(int)run[pair.Item1],(int)run["testBaseMaximum"][pair.Item2]+(int)mods[pair.Item2],(int?)run["equipmentPoolDeficits"][pair.Item1]);
                run[pair.Item1]=moved["current"].DeepClone();run[pair.Item2]=moved["maximum"].DeepClone();run["equipmentPoolDeficits"][pair.Item1]=moved["deficit"].DeepClone();
            }
            run["reconcileCalls"]=((int?)run["reconcileCalls"]??0)+1;
        }
        var callbacks=new OriginalRunContent(catalog,reconcile:Reconcile);
        Check(callbacks.CollectReward(player,"armament",new JObject{["armamentId"]="dagger"},new RandomStreams(1)),"Acquire test dagger");
        var upgrade=new ItemUpgradeService(catalog);player=(JObject)upgrade.Commit(player,"armament/straightSword",true)["run"];
        var mounts=new CardMountService(catalog);var mount=mounts.MountRows("armament/straightSword",null).FirstOrDefault(row=>(bool?)row["extractable"]==true);
        if(mount!=null)player=(JObject)mounts.Extract(player,"armament/straightSword",(string)mount["mountKey"],true)["run"];
        var session=OriginalRunSession.Start(catalog,supplement,player,1,callbacks);var first=session.Snapshot();var quota=(int)session.Player()["equipmentAttackSlotCount"];
        var ordinary=new JArray(session.Player()["deck"].Where(c=>c["equipmentRole"]==null).Select(c=>c.DeepClone()));
        Check(!session.Equip("rightHand",0,"greatsword"),"Unowned equipment refused");Check(Same(first,session.Snapshot()),"Unowned equip leaves state/RNG unchanged");
        Check(session.Equip("rightHand",1,"dagger"),"Owned spare can occupy an unlocked set");
        Check(session.SelectSet("rightHand",1),"Active set can change outside combat");
        var changed=session.Player();Check((int)changed["equipmentAttackSlotCount"]==quota,"Attack quota preserved");
        Check(Same(new JArray(changed["deck"].Where(c=>c["equipmentRole"]==null)),ordinary),"Ordinary and extracted cards survive recomposition");
        Check((int)changed["maxHp"]-(int)changed["hp"]==7,"Equipment change does not heal existing wounds");
        Check((int)changed["reconcileCalls"]>=2,"Successful commands reconcile resources");
        Check(session.SelectSet("rightHand",0),"Return to original active set");
        Check(session.Player()["deck"].Any(c=>(string)c["sourceArmamentId"]=="straightSword"&&(int?)c["smithingLevel"]==1),"Weapon tier restamped when re-equipped");
        if(mount!=null)Check(Same(session.Player()["itemMounts"],player["itemMounts"]),"Extracted mount state survives equipment changes");
        var current=session.Snapshot();Check(!session.SelectSet("rightHand",0),"Selecting active set is no-op");Check(Same(current,session.Snapshot()),"No-op leaves state/RNG unchanged");
        Check(!session.SelectSet("leftHand",1),"Unopened set refused");Check(Same(current,session.Snapshot()),"Locked set leaves state/RNG unchanged");
        Check(session.Equip("armor",0,null),"Owned armor may be unequipped");
        Check(session.Equip("armor",0,(string)player["loadout"]["sets"]["armor"][0]),"Armor ownership survives unequip");
        var saved=session.Snapshot();var resumed=OriginalRunSession.Restore(saved,callbacks);Check(Same(saved,resumed.Snapshot()),"Equipment save/resume is exact");
        var poor=(JObject)player.DeepClone();poor["attributes"]["dexterity"]=0;var denied=OriginalRunSession.Start(catalog,supplement,poor,1,callbacks);var before=denied.Snapshot();
        Check(!denied.Equip("rightHand",1,"dagger"),"Attribute requirements enforced");Check(Same(before,denied.Snapshot()),"Failed requirements are atomic");
        var broken=OriginalRunSession.Start(catalog,supplement,player,1,new OriginalRunContent(catalog,reconcile:run=>throw new InvalidOperationException("Fixture reconcile failed")));before=broken.Snapshot();bool threw=false;
        try{broken.Equip("rightHand",1,"dagger");}catch(InvalidOperationException){threw=true;}Check(threw,"Failed reconciliation refuses entire command");Check(Same(before,broken.Snapshot()),"Failed reconciliation leaves state/RNG unchanged");
        var noCallback=OriginalRunSession.Start(catalog,supplement,player,1,new OriginalRunContent(catalog));before=noCallback.Snapshot();threw=false;try{noCallback.Equip("rightHand",1,"dagger");}catch(NotSupportedException){threw=true;}Check(threw,"Missing pool/weight reconciliation is explicit");Check(Same(before,noCallback.Snapshot()),"Missing callback preserves state");
        var combat=OriginalRunSession.Start(catalog,supplement,player,1,callbacks);combat.EnterNode(combat.LegalNodeIds()[0]);Check(combat.Phase==OriginalRunPhase.Combat,"Fixture enters first combat");before=combat.Snapshot();Check(!combat.Equip("rightHand",1,"dagger")&&!combat.SelectSet("rightHand",1),"Unpaid combat equipment commands refused");Check(Same(before,combat.Snapshot()),"Combat refusal preserves state/RNG");
        void Reach(OriginalRunSession target,string nodeType)
        {
            var graph=target.Map();var queue=new System.Collections.Generic.Queue<string[]>();foreach(var id in graph["startIds"].Values<string>())queue.Enqueue(new[]{id});string[] found=null;
            while(queue.Count>0){var path=queue.Dequeue();var node=graph["nodes"][path.Last()];if((string)node["type"]==nodeType){found=path;break;}foreach(var id in node["next"].Values<string>())queue.Enqueue(path.Concat(new[]{id}).ToArray());}
            if(found==null)throw new Exception("Fixture has no target room");
            foreach(var id in found){Check(target.EnterNode(id),"Traverse authored edge to service");if(id==found.Last())return;int guard=0;
                while(target.Phase!=OriginalRunPhase.Map){if(++guard>10)throw new Exception("Fixture room stuck");switch(target.Phase){case OriginalRunPhase.Combat:target.CompleteCombat("victory",target.Player(),target.CreateRandom());break;case OriginalRunPhase.Rewards:target.ContinueRewards(false);break;case OriginalRunPhase.Shop:target.LeaveShop();break;case OriginalRunPhase.Shrine:target.LeaveShrine();break;case OriginalRunPhase.Event:target.ChooseEvent((string)target.EventChoices().Last()["id"]);break;case OriginalRunPhase.EventResult:target.LeaveEvent();break;default:throw new Exception("Unexpected service path phase");}}
            }
        }
        var replacementCallbacks=new OriginalRunContent(catalog,reconcile:run=>{var copy=(JObject)run.DeepClone();Reconcile(copy);run.RemoveAll();foreach(var property in copy.Properties())run.Add(property.Name,property.Value.DeepClone());});
        var shopper=(JObject)player.DeepClone();shopper["cinders"]=1000;var shop=OriginalRunSession.Start(catalog,supplement,shopper,1,replacementCallbacks);Reach(shop,"merchant");
        Check(shop.BuyShopItem("relic",0),"Shop purchase survives replacing reconciler");Check((bool?)shop.Room()["relics"][0]["sold"]==true,"Sold flag attaches to live room after replacement");before=shop.Snapshot();Check(!shop.BuyShopItem("relic",0),"Replacing reconciler cannot sell same item twice");Check(Same(before,shop.Snapshot()),"Repeated purchase preserves state after replacement");
        var cursed=(JObject)player.DeepClone();var noRest=catalog.Table("relics").First(r=>(bool?)r["passives"]?["shrineNoRest"]==true);((JArray)cursed["relics"]).Add(noRest["id"].DeepClone());
        Check(!rules.CanRest(cursed),"Original shrineNoRest passive blocks rest");var shrine=OriginalRunSession.Start(catalog,supplement,cursed,1,callbacks);Reach(shrine,"shrine");before=shrine.Snapshot();threw=false;try{shrine.Rest();}catch(InvalidOperationException){threw=true;}Check(threw,"No-rest relic refuses heal");Check(Same(before,shrine.Snapshot()),"Refused rest is atomic");var pools=shrine.Player();shrine.LeaveShrine();Check(shrine.Phase==OriginalRunPhase.Map,"No-rest player can leave shrine");Check((int)shrine.Player()["hp"]==(int)pools["hp"]&&(int)shrine.Player()["mana"]==(int)pools["mana"]&&(int)shrine.Player()["cinders"]==(int)pools["cinders"],"Leaving does not heal or spend currency");Check(Same(shrine.Player()["flaskCharges"],pools["flaskCharges"]),"Leaving does not refill charges again");
        var relicProjection=new OriginalRunContent(catalog,reconcile:run=>
        {
            var copy=(JObject)run.DeepClone();var maximum=copy["relics"].Values<string>().Contains("forsakenMedallion")?60:50;
            var moved=EquipmentRunModifiers.MovePool((int)copy["maxHp"],(int)copy["hp"],maximum,null);
            copy["maxHp"]=moved["maximum"].DeepClone();copy["hp"]=moved["current"].DeepClone();copy["equipmentPoolDeficits"]=new JObject{["hp"]=moved["deficit"].DeepClone()};
            run.RemoveAll();foreach(var property in copy.Properties())run.Add(property.Name,property.Value.DeepClone());
        });
        foreach(var wound in new[]{0,7})
        {
            var eventPlayer=(JObject)reference["players"][1].DeepClone();eventPlayer["maxHp"]=50;eventPlayer["hp"]=50-wound;
            var rewardPlayer=(JObject)eventPlayer.DeepClone();relicProjection.CollectReward(rewardPlayer,"relic",new JObject{["relicId"]="forsakenMedallion"},new RandomStreams(1));
            relicProjection.ApplyEffects(eventPlayer,new JArray(new JObject{["op"]="addRelic",["id"]="forsakenMedallion"}),new RandomStreams(1));
            Check((int)eventPlayer["hp"]==60-wound&&(int)eventPlayer["maxHp"]==60,"Event acquisition preserves reconciled HP");
            Check((int)eventPlayer["hp"]==(int)rewardPlayer["hp"],"Event and reward relic acquisition agree");
            Check((int)eventPlayer["equipmentPoolDeficits"]["hp"]==wound,"Event resource deficit agrees with current HP");
        }
        var woundedEvent=(JObject)reference["players"][1].DeepClone();woundedEvent["maxHp"]=50;woundedEvent["hp"]=50;
        relicProjection.ApplyEffects(woundedEvent,new JArray(new JObject{["op"]="damage",["amount"]=7},new JObject{["op"]="addRelic",["id"]="forsakenMedallion"},new JObject{["op"]="damage",["amount"]=3}),new RandomStreams(1));
        Check((int)woundedEvent["hp"]==50&&(int)woundedEvent["maxHp"]==60,"Damage on both sides of replacing reconciliation persists");
        var report=combat.Player();report["damageDealt"]=17;report["damageTaken"]=4;combat.CompleteCombat("victory",report,combat.CreateRandom());
        Check((int)combat.Player()["stats"]["damageDealt"]==17&&(int)combat.Player()["stats"]["damageTaken"]==4,"Battle totals retained at completion");
        Check((int)combat.Player()["stats"]["fightsWon"]==1,"Won battle recorded in original statistics shape");
        before=combat.Snapshot();threw=false;try{combat.CompleteCombat("victory",report,combat.CreateRandom());}catch(InvalidOperationException){threw=true;}Check(threw&&Same(before,combat.Snapshot()),"Repeated completion cannot duplicate battle statistics");
        var bossRoute=OriginalRunSession.Start(catalog,supplement,player,25,callbacks);var bossIds=new System.Collections.Generic.List<string>();
        while(bossRoute.Phase!=OriginalRunPhase.Victory)
        {
            switch(bossRoute.Phase)
            {
                case OriginalRunPhase.Map:bossRoute.EnterNode(bossRoute.LegalNodeIds()[0]);break;
                case OriginalRunPhase.Combat:
                    if((string)bossRoute.Room()["pool"]=="boss")bossIds.AddRange(catalog.Record("encounters",(string)bossRoute.Room()["encounterId"])["enemies"].Values<string>());
                    bossRoute.CompleteCombat("victory",bossRoute.Player(),bossRoute.CreateRandom());break;
                case OriginalRunPhase.Rewards:bossRoute.ContinueRewards(false);break;
                case OriginalRunPhase.Shop:bossRoute.LeaveShop();break;
                case OriginalRunPhase.Shrine:bossRoute.LeaveShrine();break;
                case OriginalRunPhase.Event:bossRoute.ChooseEvent((string)bossRoute.EventChoices().Last()["id"]);break;
                case OriginalRunPhase.EventResult:bossRoute.LeaveEvent();break;
                default:throw new Exception("Unexpected boss statistics phase");
            }
        }
        Check(bossIds.Count>=3&&Same(bossRoute.Player()["bossesBeaten"],new JArray(bossIds.Distinct())),"All three act bosses retain actual enemy IDs for authored profile unlocks");
        return checks;
    }
}

