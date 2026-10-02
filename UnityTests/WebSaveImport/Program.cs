using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
var root=Directory.GetCurrentDirectory();
var folder=Path.Combine(root,"GameContent/Unity/Original");
JObject Json(string name)=>JObject.Parse(File.ReadAllText(Path.Combine(folder,name+".json")));
var catalog=new OriginalContentCatalog(Json("content").ToString());
var checks=0;
void Check(bool yes,string label){if(!yes)throw new Exception(label);checks++;}
bool Equivalent(JToken a,JToken b){
 bool Number(JToken t)=>t?.Type==JTokenType.Integer||t?.Type==JTokenType.Float;
 if(Number(a)&&Number(b))return (decimal)a==(decimal)b;
 if(a is JObject ao&&b is JObject bo)return ao.Count==bo.Count&&ao.Properties().All(p=>Equivalent(p.Value,bo[p.Name]));
 if(a is JArray aa&&b is JArray ba)return aa.Count==ba.Count&&aa.Zip(ba,Equivalent).All(x=>x);
 return JToken.DeepEquals(a,b);
}
JObject Import(string text)=>OriginalWebSaveImport.Convert(text,catalog,Json("event-choices"),Json("mechanics"),Json("progression"));
foreach(var cls in new[]{"reaver","starseer","rogue","herald"}){
 var text=File.ReadAllText(Path.Combine(args[0],cls+"-map.json"));var before=JObject.Parse(text);var converted=Import(text);var game=OriginalGameSession.Restore(converted);
 Check(game.Phase==OriginalRunPhase.Map,"original map import");
 foreach(var key in new[]{"hp","maxHp","mana","maxMana","stamina","maxStamina","attributes","mapGraph","streamCounters","path","cinders","flaskCharges","stats","seenEvents","customization","seedString"})Check(JToken.DeepEquals(before[key],game.RunPlayer[key]),cls+": unchanged "+key);
 Check(game.RunPlayer["deck"].Select(c=>(string)c["instanceId"]).SequenceEqual(before["deck"].Select(c=>(string)c["instanceId"])),"original card identities");
 var originalCards=JArray.Parse(File.ReadAllText(Path.Combine(args[0],cls+"-cards.json")));
 foreach(var expected in originalCards){
  var instance=game.RunPlayer["deck"].OfType<JObject>().Single(c=>(string)c["instanceId"]==(string)expected["instanceId"]);var actual=game.Resolve(instance);
  foreach(var key in new[]{"cost","manaCost","staminaCost","effects","damageSchool","exposureBuildupPerHit"})Check(Equivalent(actual[key],expected["card"][key]),cls+": original card "+instance["instanceId"]+" "+key+" expected "+expected["card"][key]+" actual "+actual[key]);
 }
 var checkpoint=game.Snapshot();var restored=OriginalGameSession.Restore(checkpoint);game.Enter(game.LegalNodeIds[0]);restored.Enter(restored.LegalNodeIds[0]);Check(JToken.DeepEquals(game.Snapshot(),restored.Snapshot()),"identical next encounter after reload");
 game.EndTurn();Check(game.Turn>1,"imported run plays an actual enemy turn");
 Check(File.ReadAllText(Path.Combine(args[0],cls+"-map.json"))==text,"source bytes untouched");
 Console.WriteLine(cls+": imported map and actual combat passed");
}
var input=File.ReadAllText(Path.Combine(args[0],"reaver-map.json"));
var source=JObject.Parse(input);
void Refuse(Action action,string label){var rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}catch(Newtonsoft.Json.JsonException){rejected=true;}Check(rejected,label);}
void RejectEdit(Action<JObject> edit,string label){var copy=(JObject)source.DeepClone();edit(copy);Refuse(()=>Import(copy.ToString()),label);}
Refuse(()=>Import(""),"empty input refused");
Refuse(()=>Import(new string('x',OriginalWebSaveImport.MaximumBytes+1)),"oversize input refused");
Refuse(()=>Import("{broken"),"corrupt JSON refused");
Refuse(()=>Import(input+"{}"),"trailing JSON refused");
Refuse(()=>Import("{\"schemaVersion\":5,\"schemaVersion\":5}"),"duplicate JSON fields refused");
RejectEdit(s=>s["schemaVersion"]=6,"future schema refused");
RejectEdit(s=>s["schemaVersion"]=4,"older unsupported schema refused");
foreach(var field in new[]{"combatEntered","pendingReward","shopStock","draft","skillDraft","skills","classAbilities","handRuleSnapshot"})
 foreach(var value in new JToken[]{new JObject(),new JArray(),new JValue("unexpected")})RejectEdit(s=>s[field]=value.DeepClone(),"active/unsupported "+field+" "+value.Type+" refused");
RejectEdit(s=>s["hp"]=(int)s["maxHp"]+1,"HP above maximum refused");
RejectEdit(s=>s["hp"]=0,"finished character cannot become a living imported climb");
RejectEdit(s=>s["mapGraph"]["startIds"]=new JArray(),"checkpoint with no continuing route refused");
RejectEdit(s=>s["energyMax"]=999,"changed action resource refused");
RejectEdit(s=>s["deck"][0]["cardId"]="missing-card","unknown card refused");
RejectEdit(s=>s["deck"][1]["instanceId"]=s["deck"][0]["instanceId"].DeepClone(),"duplicate card identity refused");
RejectEdit(s=>s["loadout"]["rightHand"]="missing-item","unknown loadout refused");
RejectEdit(s=>s["webImport"]=new JObject(),"native receipt in original input refused");
RejectEdit(s=>s["equipmentProfileRuleSnapshot"]["snapshotVersion"]=99,"future equipment rules refused");
RejectEdit(s=>s["derivedStatRuleSnapshot"]["snapshotVersion"]=99,"future derived rules refused");
RejectEdit(s=>s["futureRules"]=new JObject(),"unknown same-schema state refused");
RejectEdit(s=>s["modifiers"]=new JArray("unported-modifier"),"unsupported run modifiers refused");
RejectEdit(s=>s["seenEvents"]=new JObject(),"malformed event history refused");
RejectEdit(s=>s["seenEvents"]=new JArray("missing-event"),"unknown event history refused");
RejectEdit(s=>s["deck"][0]["mods"]=new JArray("damage=999"),"unsupported card modifiers refused");
RejectEdit(s=>s["loadout"]["sets"]["rightHand"][2]="missing-item","unknown inactive equipment refused");
RejectEdit(s=>s["loadout"]["storage"]=new JArray("missing-item"),"unknown stored equipment refused");
var snapshot=Import(input);
var clearedRoom=(JObject)source.DeepClone();clearedRoom["pendingReward"]=null;Check((string)Import(clearedRoom.ToString())["run"]["phase"]=="Map","explicit cleared reward is a map checkpoint");
var wrapped=new JObject{["exportedAt"]="2026-09-28",["game"]="Ashen Spire",["archive"]=new JObject{["kind"]="run",["save"]=input}};
Check(JToken.DeepEquals(Import(wrapped.ToString()),snapshot),"run archive wrapper converts identically");
var reordered=new JObject(source.Properties().Reverse().Select(p=>new JProperty(p.Name,p.Value.DeepClone())));
Check((string)Import(reordered.ToString())["run"]["runId"]==(string)snapshot["run"]["runId"],"JSON property order cannot bypass duplicate protection");
wrapped["archive"]["kind"]="meta";Refuse(()=>Import(wrapped.ToString()),"profile archive refused rather than overwritten");
var badReceipt=(JObject)snapshot.DeepClone();badReceipt["run"]["webImport"]["sha256"]="wrong";Refuse(()=>OriginalGameSession.Restore(badReceipt),"damaged native import receipt refused");
var memory=new OriginalMemorySaveStorage();var slots=new OriginalSaveSlots(memory,"import-test","test");
memory.Write("sote_run_v1",input);memory.Write(slots.ProfileKey,"existing-profile-bytes");
Check(slots.ImportWebRun(0,snapshot),"import commits empty slot");
Check(slots.List()[0].Meta.Origin=="original-web","slot records import origin");
Refuse(()=>slots.ImportWebRun(0,snapshot),"occupied slot cannot be overwritten");
Refuse(()=>slots.ImportWebRun(1,snapshot),"same checkpoint cannot be imported twice");
Check(memory.Read("sote_run_v1")==input&&memory.Read(slots.ProfileKey)=="existing-profile-bytes","original and profile bytes unchanged");
var reloaded=slots.Load(0,s=>OriginalGameSession.Restore(s),out _,out _);Check(JToken.DeepEquals(reloaded,snapshot),"journal persists exact imported snapshot");
memory.Write(slots.SlotKey(1),"corrupt");Refuse(()=>slots.ImportWebRun(1,Import(File.ReadAllText(Path.Combine(args[0],"rogue-map.json")))),"corrupt occupied slot preserved");Check(memory.Read(slots.SlotKey(1))=="corrupt","corrupt bytes unchanged");
foreach(var failAt in new[]{"write","flush","truncate"}){
 var store=new OriginalMemorySaveStorage();bool armed=true;
 var failing=new OriginalDelegateSaveStorage(store.Read,(key,value)=>{if(armed&&failAt=="write")throw new IOException("full");store.Write(key,armed&&failAt=="truncate"?"partial":value);},()=>{if(armed&&failAt=="flush")throw new IOException("full");store.Flush();},store.Delete);
 var targets=new OriginalSaveSlots(failing,"test","test");store.Write("sote_run_v1",input);
 Check(!targets.ImportWebRun(0,snapshot),"storage failure reported: "+failAt);
 Check(targets.List()[0].State==OriginalSaveSlotState.Empty&&store.Read("sote_run_v1")==input,"failed import leaves no partial save: "+failAt);
 armed=false;Check(targets.ImportWebRun(0,snapshot),"import can retry after storage recovers: "+failAt);
}
Check(!OriginalWebProfileImport.IsProfile(input)&&!OriginalWebProfileImport.IsProfile(wrapped.ToString().Replace("\"meta\"","\"run\"")),"run saves are not routed as profiles");
Refuse(()=>Import(new JObject{["profile"]="{}"}.ToString()),"profile export refused by run import");
checks+=ProfileImportChecks.Run(root,catalog);
checks+=RoomImportChecks.Run(root,catalog,Json);
Console.WriteLine("Web save import: "+checks+" checks passed");
