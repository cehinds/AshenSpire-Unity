// Integrated original-source differential fixtures.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
internal static class WeaponChecks { internal static int Run(string fixturePath) {
var fixture=JObject.Parse(File.ReadAllText(fixturePath));
var catalog=new OriginalContentCatalog(fixture["content"].ToString());
var composer=new WeaponCardComposer(catalog); var projection=new WeaponCardProjection(catalog); var locations=new WeaponLoadout(catalog);
int checks=0;
void Equal(JToken actual,JToken expected,string label){if(!JToken.DeepEquals(Normal(actual),Normal(expected)))throw new Exception(label+"\nactual="+actual+"\nexpected="+expected);checks++;}
JToken Normal(JToken token){if(token==null || token is JValue value && value.Value==null)return JValue.CreateNull();if(token is JObject obj)return new JObject(obj.Properties().OrderBy(x=>x.Name).Select(x=>new JProperty(x.Name,Normal(x.Value))));if(token is JArray array)return new JArray(array.Select(Normal));if(token.Type==JTokenType.Integer||token.Type==JTokenType.Float)return new JValue((double)token);return token.DeepClone();}
foreach(var entry in fixture["cases"]){
 var loadout=(JObject)entry["loadout"];var hero=(string)entry["classId"];var attributes=(JObject)entry["attributes"];
 Equal(composer.BuildAttackPlan(loadout,hero),entry["plan"],"Attack plan "+hero+" "+loadout);
 Equal(composer.StartingPlan(loadout,hero),entry["startingPlan"],"Starting plan");
 Equal(new EquipmentRunModifiers(catalog).Resolve(loadout,hero),entry["runMods"],"Equipment run modifiers");
 var deck=composer.CreateStartingDeck(loadout,hero);Equal(deck,entry["starting"],"Starting refs");
 Equal(composer.Recompose(deck,loadout,hero),deck,"Idempotent recomposition");
 foreach(var card in deck.OfType<JObject>()){
  var actual=projection.Resolve(card,loadout,hero,attributes);var expected=entry["resolved"].First(x=>(string)x["instanceId"]==(string)card["instanceId"]);
  Equal(actual["profileReceipt"],expected["profileReceipt"],"Profile receipt");
  Equal(actual["card"],expected["card"],"Resolved card "+card["instanceId"]+" "+loadout);
 }
}
var attrs=new JObject(fixture["content"]["attributes"].Select(x=>new JProperty((string)x["id"],20)));
var original=locations.Create("reaver"); var owned=new HashSet<string>{"armament/dagger","armament/straightSword"};
Equal(locations.Equip(original,"reaver","rightHand",0,"dagger",new HashSet<string>(),attrs,false)["ok"],new JValue(false),"Unowned refusal");
var changed=locations.Equip(original,"reaver","rightHand",0,"dagger",owned,attrs,false);Equal(changed["ok"],new JValue(true),"Equip succeeds");
Equal(original,locations.Create("reaver"),"Equip does not mutate input");
var low=(JObject)attrs.DeepClone();low["dexterity"]=5;Equal(locations.Equip(original,"reaver","rightHand",0,"dagger",owned,low,false)["ok"],new JValue(false),"Requirement refusal");
var before=composer.CreateStartingDeck(original,"reaver");before.Add(new JObject{["instanceId"]="earned:1",["cardId"]="strike",["upgraded"]=true});
var empty=(JObject)original.DeepClone();empty["sets"]["rightHand"][0]=JValue.CreateNull();empty["sets"]["leftHand"][0]=JValue.CreateNull();
var after=composer.Recompose(before,empty,"reaver");Equal(after.First(x=>(string)x["instanceId"]=="earned:1"),before.Last,"Earned upgrade preserved");
Equal(new JValue(after.Count(x=>(string)x["equipmentRole"]=="attack")),new JValue(before.Count(x=>(string)x["equipmentRole"]=="attack")),"Born attack quota preserved");
var profile=catalog.Record("equipment.basicCardProfiles","bladeAttack");var patch=JObject.Parse("{bladeAttack:{pointsPerTier:1,pointsOffset:10}}");var a=projection.ProfileReceipt(profile,null,attrs,patch);attrs["strength"]=21;var b=projection.ProfileReceipt(profile,null,attrs,patch);Equal(new JValue((double)b["value"]-(double)a["value"]),new JValue(1),"Per point offense seam");
var authored=catalog.Data();
JObject Weapon(string id)=>(JObject)authored["equipment"]["armaments"].First(x=>(string)x["id"]==id);
Weapon("straightSword")["weaponCardPackage"]=JObject.Parse("{compatibility:'attack-v1',fillerAttackProfileId:'bladeAttack',priorityAttackRefs:['gorefireSlash'],grantedCards:[{cardId:'strike',count:2}],weaponArtDefaults:['technique','defend']}");
Weapon("dagger")["weaponCardPackage"]=JObject.Parse("{compatibility:'attack-v1',fillerAttackProfileId:'daggerPierceAttack',weaponArtDefaults:['technique','dodgeRoll']}");
var custom=new OriginalContentCatalog(authored.ToString());var cc=new WeaponCardComposer(custom);var cl=new WeaponLoadout(custom);
var pair=cl.Create("reaver");pair["sets"]["leftHand"][0]="dagger";
var packageDeck=cc.CreateStartingDeck(pair,"reaver");
Equal(new JValue(packageDeck.Count(x=>(string)x["equipmentRole"]=="granted")),new JValue(2),"Package grants create real instances");
Equal(new JValue(packageDeck.Count(x=>(string)x["equipmentRole"]=="weaponArt")),new JValue(3),"Shared weapon art installs once");
Equal(packageDeck.First(x=>(string)x["instanceId"]=="weaponArt:straightSword:technique")["grantedBy"],new JValue("straightSword"),"Right hand owns shared art");
Equal(cc.Recompose(packageDeck,pair,"reaver"),packageDeck,"Package composition is idempotent");
var noSword=(JObject)pair.DeepClone();noSword["sets"]["rightHand"][0]=JValue.CreateNull();
var removed=cc.Recompose(packageDeck,noSword,"reaver");
Equal(new JValue(removed.Any(x=>(string)x["grantedBy"]=="straightSword")),new JValue(false),"Unequipped grants disappear");
var piles=new JObject{["hand"]=new JArray(packageDeck.Take(3).Select(x=>x.DeepClone())),["draw"]=new JArray(packageDeck.Skip(3).Select(x=>x.DeepClone())),["discard"]=new JArray(),["exhaust"]=new JArray()};
var changedPiles=cc.ReconcileCombat(piles,noSword,"reaver",packageDeck.Count(x=>(string)x["equipmentRole"]=="attack"));
Equal(new JValue(changedPiles.Properties().SelectMany(x=>(JArray)x.Value).Any(x=>(string)x["grantedBy"]=="straightSword")),new JValue(false),"Mid-combat departed grants swept across piles");
Equal(new JValue(((JArray)changedPiles["discard"]).Any(x=>(string)x["instanceId"]=="weaponArt:dagger:technique")),new JValue(true),"New art owner enters discard");
Equal(cc.ReconcileCombat(changedPiles,noSword,"reaver",packageDeck.Count(x=>(string)x["equipmentRole"]=="attack")),changedPiles,"Mid-combat reconcile idempotent");
var inventory=new EquipmentInventory(catalog,"reaver",attrs);
Equal(EquipmentInventory.Restore(catalog,JObject.Parse(inventory.Snapshot().ToString())).Snapshot(),inventory.Snapshot(),"Inventory JSON restore");
Equal(inventory.AcquireArmament("dagger")["ok"],new JValue(true),"Acquired item enters storage");
Equal(inventory.Equip("rightHand",0,"dagger",false)["ok"],new JValue(true),"Owned inventory equips");
Equal(EquipmentInventory.Restore(catalog,JObject.Parse(inventory.Snapshot().ToString())).Snapshot(),inventory.Snapshot(),"Changed inventory restores");
var saved=inventory.Snapshot();var exposed=inventory.Loadout();exposed["sets"]["rightHand"][0]="greatsword";Equal(inventory.Snapshot(),saved,"Inventory reads isolated");
Equal(inventory.Equip("rightHand",0,"greatsword",false)["ok"],new JValue(false),"Inventory cannot equip unowned item");Equal(inventory.Snapshot(),saved,"Refusal leaves entire inventory unchanged");
var upgrades=new ItemUpgradeService(catalog);
foreach(var entry in fixture["upgradeCases"]){
 var itemRef=(string)entry["itemRef"];var level=(int)entry["level"];var actual=upgrades.ResolveItem(itemRef,level);var expected=(JObject)entry["item"].DeepClone();expected.Remove("tags");expected.Remove("itemTypeTags");expected.Remove("entityTags");expected.Remove("itemTypes");Equal(actual,expected,"Upgraded item "+itemRef);
 foreach(var attribute in ((JObject)entry["requirements"]).Properties())Equal(new JValue(upgrades.RequirementDelta(itemRef,attribute.Name,level)),attribute.Value,"Requirement reduction");
 foreach(var card in entry["cards"])Equal(upgrades.ResolveCard((JObject)card["definition"],(string)card["role"],itemRef,level),card["resolved"],"Item card upgrade");
}
var smithRun=new JObject{["class"]="reaver",["loadout"]=locations.Create("reaver"),["attributes"]=attrs.DeepClone(),["deck"]=composer.CreateStartingDeck(locations.Create("reaver"),"reaver"),["smithingStones"]=100,["itemUpgradeLevels"]=new JObject(),["smithingRewardClaims"]=new JArray()};
var smithBefore=smithRun.ToString();var smithResult=upgrades.Commit(smithRun,"armament/straightSword");Equal(new JValue(smithRun.ToString()),new JValue(smithBefore),"Smithing transaction preserves input");Equal(smithResult["run"]["itemUpgradeLevels"]["armament/straightSword"],new JValue(1),"Smithing level belongs to item");
Equal(new JValue(((JArray)smithResult["run"]["deck"]).Where(x=>(string)x["sourceArmamentId"]=="straightSword").All(x=>(int)x["smithingLevel"]==1)),new JValue(true),"Smithing stamps all item cards");
var reward=upgrades.GrantReward(smithRun,"boss","boss:1");var duplicate=upgrades.GrantReward((JObject)reward["run"],"boss","boss:1");Equal(duplicate["receipt"]["amount"],new JValue(0),"Smithing reward cannot be claimed twice");
var mountCatalog=new OriginalContentCatalog(fixture["mountContent"].ToString());var mounts=new CardMountService(mountCatalog);
foreach(var entry in fixture["mountCases"]){Equal(mounts.MountRows("armament/straightSword",(JObject)entry["itemMounts"]),entry["rows"],"Original mount rows");Equal(mounts.ApplyOverrides(mounts.AuthoredMounts(mountCatalog.Record("equipment.armaments","straightSword")),(JObject)entry["itemMounts"]),entry["desired"],"Original mount overrides");}
var mountRun=(JObject)smithRun.DeepClone();mountRun["deck"]=new WeaponCardComposer(mountCatalog).CreateStartingDeck((JObject)mountRun["loadout"],"reaver");
var extracted=mounts.Extract(mountRun,"armament/straightSword","weaponArt:straightSword:strike");
Equal(new JValue(((JArray)extracted["run"]["deck"]).Any(x=>(string)x["instanceId"]=="extracted:1:strike" && x["equipmentRole"]==null)),new JValue(true),"Extraction gives run-owned card");
Equal(((JArray)extracted["run"]["deck"]).First(x=>(string)x["instanceId"]=="weaponArt:straightSword:strike")["cardId"],new JValue("dodgeRoll"),"Extracted mount seats fallback");
var installed=mounts.Install((JObject)extracted["run"],"armament/straightSword","weaponArt:straightSword:strike","extracted:1:strike");
Equal(new JValue(((JArray)installed["run"]["deck"]).Any(x=>(string)x["instanceId"]=="extracted:1:strike")),new JValue(false),"Installed card leaves run-owned deck");
Equal(((JArray)installed["run"]["deck"]).First(x=>(string)x["instanceId"]=="weaponArt:straightSword:strike")["cardId"],new JValue("strike"),"Installed card retains stable mount ID");
Equal(new EquipmentRunModifiers(catalog).Resolve(locations.Create("reaver"),"reaver")["maxHp"],new JValue(0),"Baseline equipment resources");
var smaller=EquipmentRunModifiers.MovePool(100,10,20);var larger=EquipmentRunModifiers.MovePool(20,(int)smaller["current"],100,(int)smaller["deficit"]);Equal(larger["current"],new JValue(10),"Equipment resize preserves hidden deficit");
var mechanics=JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixturePath)!, "weapon-mechanics.json")));var playerProjection=new OriginalPlayerProjection(catalog,mechanics);
foreach(var entry in fixture["playerCases"]){var run=(JObject)entry["run"];var preview=playerProjection.Preview(run);Equal(preview["resources"],entry["resources"],"Player resource parity");Equal(preview["relicModifiers"],entry["relicModifiers"],"Relic modifier parity");Equal(preview["poiseThreshold"],entry["poise"],"Player poise projection");foreach(var key in new[]{"load","capacity","percent"})Equal(preview["weight"][key],entry["load"][key],"Player weight projection");}
var state=(JObject)smithRun.DeepClone();state["maxHp"]=70;state["hp"]=40;state["maxMana"]=4;state["mana"]=1;state["maxStamina"]=4;state["stamina"]=2;state["relics"]=new JArray();playerProjection.Reconcile(state);var savedProjection=state.DeepClone();playerProjection.Reconcile(state);Equal(state,savedProjection,"Player reconciliation is idempotent");
var beforeHp=(int)state["hp"];var beforeMax=(int)state["maxHp"];((JArray)state["relics"]).Add("forsakenMedallion");playerProjection.Reconcile(state);Equal(new JValue((int)state["maxHp"]-beforeMax),new JValue(10),"New relic grows maximum");Equal(new JValue((int)state["hp"]-beforeHp),new JValue(10),"New relic preserves deficit");
var armourRef="armor/reaver/default";state["itemUpgradeLevels"][armourRef]=1;playerProjection.Reconcile(state);Equal(state["weights"]["armorWeight"],upgrades.ResolveItem(armourRef,1)["poiseThreshold"],"Smithed armour updates weight");
var progressed=(JObject)smithRun.DeepClone();progressed["progression"]=JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixturePath)!, "../../GameContent/Unity/Original/progression.json")));playerProjection.Reconcile(progressed);Equal(progressed["energy"],new JValue(1+(int)attrs["dexterity"]/5),"Owner five-point action gate");
Console.WriteLine($"Weapon deck checks passed: {checks}");
var profileFixture=JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixturePath)!, "profile-reference.json")));var nativeProfile=new OriginalProfile(catalog);var profileIndex=0;
foreach(var example in profileFixture["profiles"]){var receipt=nativeProfile.Finish("run-"+profileIndex++,(JObject)example["run"],(bool)example["result"]["victory"]);Equal(receipt["result"],example["result"],"Profile original run summary");Equal(receipt["newUnlocks"],example["fresh"],"Profile original unlock awards");Equal(nativeProfile.Snapshot()["progress"],example["progress"],"Profile original durable progress");Equal(nativeProfile.Snapshot()["unlocked"],example["unlocked"],"Profile original earned ledger");Equal(nativeProfile.UnlockView(),example["view"],"Profile original reveal view");foreach(var slot in example["slots"]){Equal(new JValue(nativeProfile.OpenedSets((string)slot["id"])),slot["opened"],"Profile original opened sets");Equal(new JValue(nativeProfile.VisibleSets((string)slot["id"])),slot["visible"],"Profile original visible sets");}}
var beforeProfile=nativeProfile.Snapshot();var repeated=nativeProfile.Finish("run-0",new JObject(),false);Equal(repeated["duplicate"],new JValue(true),"Archived finish is still idempotent");Equal(nativeProfile.Snapshot(),beforeProfile,"Duplicate finish cannot change progression");Equal(new JValue(((JArray)nativeProfile.Snapshot()["results"]).Count),new JValue(20),"History bounded to twenty");Equal(nativeProfile.Snapshot()["progress"]["runs"],new JValue(30),"Progress survives history cap");Equal(OriginalProfile.Restore(catalog,nativeProfile.Snapshot()).Snapshot(),nativeProfile.Snapshot(),"Profile save round trip");
var legacy=locations.Create("reaver");legacy["sets"]["rightHand"][2]="dagger";Equal(new JValue(new OriginalProfile(catalog).OpenedSets("rightHand",legacy)),new JValue(3),"Legacy occupied set remains usable");
var collection=new OriginalProfile(catalog);var collectedRun=new JObject{["loadout"]=locations.Create("reaver"),["seedString"]="COLLECT"};((JArray)collectedRun["loadout"]["storage"]).Add("dagger");var discovery=collection.CollectArmament(collectedRun,"dagger","monster");Equal(discovery["recorded"],new JValue(true),"Successfully collected weapon becomes permanent");Equal(collection.Snapshot()["discoveredArmaments"],new JArray("dagger"),"Normal discovery opens kit ledger");Equal(collection.CollectArmament(collectedRun,"dagger","monster")["recorded"],new JValue(false),"Collection is idempotent");
var badProfile=nativeProfile.Snapshot();badProfile["schemaVersion"]=999;try{OriginalProfile.Restore(catalog,badProfile);throw new Exception("Accepted invalid schema");}catch(ArgumentException){checks++;}
Console.WriteLine($"Weapon, player and nativeProfile checks passed: {checks}");


var historyRows=((JArray)nativeProfile.Snapshot()["results"]).OfType<JObject>().ToArray();var standardRows=historyRows.Where(r=>(bool?)r["custom"]!=true).ToArray();Equal(nativeProfile.Telemetry()["runs"],new JValue(standardRows.Length),"Custom runs excluded from telemetry");Equal(nativeProfile.Telemetry()["wins"],new JValue(standardRows.Count(r=>(bool?)r["victory"]==true)),"Only standard victories counted");Equal(new JValue(OriginalProfile.IsCustomRun(new JObject{["mapShape"]=new JObject{["floors"]=3}})),new JValue(true),"Shortened maps are custom");
var customCollection=new OriginalProfile(catalog);collectedRun["custom"]=new JObject{["ascension"]=1};customCollection.CollectArmament(collectedRun,"dagger","monster");Equal(customCollection.Snapshot()["found"],new JArray("dagger"),"Custom collection retains original wardrobe find");Equal(customCollection.Snapshot()["discoveredArmaments"],new JArray(),"Custom collection cannot unlock starting kits");
try{collection.CollectArmament(collectedRun,"greatsword","monster");throw new Exception("Uncollected item entered profile");}catch(ArgumentException){checks++;}
Console.WriteLine($"Final native equipment/player/profile assertions: {checks}");
Console.WriteLine("Standalone profile wrapper checks: " + ProfileChecks.Run(catalog,Path.Combine(Path.GetDirectoryName(fixturePath)!, "profile-reference.json")));
return checks;

} }
