using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
static class ProfileImportChecks
{
    public static int Run(string folder, OriginalContentCatalog catalog)
    {
        var checks=0;
        void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
        void Refuse(Action action,string label){bool refused=false;try{action();}catch(Exception e) when(e is ArgumentException||e is InvalidOperationException||e is Newtonsoft.Json.JsonException||e is OverflowException){refused=true;}Check(refused,label);}
        var text=File.ReadAllText(Path.Combine(folder,"profile.json"));var source=JObject.Parse(text);
        var current=new OriginalProfile(catalog);current.SetSettings(new JObject{["mapViewer"]=new JObject{["zoom"]=1.4}});
        current.Finish("unity-existing",new JObject{["classId"]="rogue",["actNumber"]=1,["floor"]=2,["seed"]="UNITY"},false);
        var before=current.Snapshot();
        OriginalProfile Import(string value,OriginalProfile destination=null)=>OriginalWebProfileImport.Convert(value,catalog,destination??current);
        var imported=Import(text);var snapshot=imported.Snapshot();
        Check(JToken.DeepEquals(current.Snapshot(),before),"profile preview never mutates existing progress");
        Check((int)snapshot["progress"]["runs"]==24&&(int)snapshot["progress"]["wins"]==8,"retained history does not replace lifetime totals");
        Check(JToken.DeepEquals(snapshot["settings"],before["settings"]),"Unity preferences preserved");
        Check(JToken.DeepEquals(snapshot["originalProfileImport"]["original"],source),"entire source profile preserved including settings");
        Check(source["unlocked"].All(id=>snapshot["unlocked"].Any(x=>JToken.DeepEquals(x,id))),"earned original unlocks retained");
        Check(JToken.DeepEquals(snapshot["found"],source["found"])&&JToken.DeepEquals(snapshot["discoveredArmaments"],source["discoveredArmaments"]),"equipment discovery retained");
        Check(snapshot["results"].Count()==20&&(string)snapshot["results"].Last()["seed"]=="UNITY","native history retained within normal archive limit");
        Check((string)imported.ResultArchive().Last()["key"]=="unity-existing","native archive keys still align");
        Check(imported.ResultArchive().Take(19).All(r=>((string)r["key"]).StartsWith("web-profile-")),"imported result keys are durable");
        Check(JToken.DeepEquals(Import(File.ReadAllText(Path.Combine(folder,"profile-export.json"))).Snapshot(),snapshot),"actual original export wrapper matches raw profile");
        var archive=new JObject{["archive"]=new JObject{["kind"]="meta",["save"]=text}};
        Check(JToken.DeepEquals(Import(archive.ToString()).Snapshot(),snapshot),"original recovery archive accepted");
        var reordered=new JObject(source.Properties().Reverse().Select(p=>new JProperty(p.Name,p.Value.DeepClone())));
        Check(JToken.DeepEquals(Import(reordered.ToString()).Snapshot()["originalProfileImport"]["sha256"],snapshot["originalProfileImport"]["sha256"]),"profile fingerprint ignores formatting and key order");
        Refuse(()=>Import(text,imported),"duplicate import refused");
        var changed=(JObject)source.DeepClone();changed["progress"]["runs"]=25;Refuse(()=>Import(changed.ToString(),imported),"changed original cannot double count progress");
        var damaged=(JObject)snapshot.DeepClone();damaged["originalProfileImport"]["sha256"]="broken";Refuse(()=>OriginalProfile.Restore(catalog,damaged),"damaged profile receipt refused");
        foreach(var edit in new Action<JObject>[]{s=>s["schemaVersion"]=3,s=>s["schemaVersion"]="2",s=>s["futureProgress"]=new JObject(),s=>s["progress"]["runs"]=-1,s=>s["progress"]["wins"]=99,s=>s["results"][0]["victory"]="yes",s=>s["unlocked"]=new JArray("missing-unlock"),s=>s["found"]=new JArray("missing-weapon"),s=>s["discoveredArmaments"]=new JArray("missing-weapon"),s=>s["results"][0]["class"]="unknown"}){
            var invalid=(JObject)source.DeepClone();edit(invalid);Refuse(()=>Import(invalid.ToString()),"unsupported/corrupt profile refused");Check(JToken.DeepEquals(before,current.Snapshot()),"refusal preserves native profile");
        }
        foreach(var invalid in new[]{"", "{broken", text+"{}", "{\"schemaVersion\":2,\"schemaVersion\":2}",new string('x',OriginalWebSaveImport.MaximumBytes+1)})Refuse(()=>Import(invalid),"invalid profile JSON refused");
        Refuse(()=>Import(File.ReadAllText(Path.Combine(folder,"reaver-map.json"))),"run file cannot become profile");
        var overflow=(JObject)source.DeepClone();overflow["progress"]["runs"]=int.MaxValue;Refuse(()=>Import(overflow.ToString()),"combined lifetime counter overflow refused");
        var wrongAct=(JObject)source.DeepClone();wrongAct["progress"]["maxAct"]=1;Refuse(()=>Import(wrongAct.ToString()),"progress act cannot contradict history");
        var lostWin=(JObject)source.DeepClone();lostWin["progress"]["wonClasses"]=new JArray();Refuse(()=>Import(lostWin.ToString()),"won classes cannot contradict history");
        foreach(var schema in new int?[]{null,0,1,2}){
            var old=(JObject)source.DeepClone();if(schema==null)old.Remove("schemaVersion");else old["schemaVersion"]=schema.Value;
            Check((int)Import(old.ToString()).Snapshot()["progress"]["runs"]==24,"original profile schema compatibility "+schema);
        }
        var memory=new OriginalMemorySaveStorage();var slots=new OriginalSaveSlots(memory,"profile-import","test");
        Check(slots.SaveProfile(current),"existing profile fixture persisted");memory.Write("sote_meta_v1",text);
        Check(slots.ImportWebProfile(catalog,before,imported),"profile import journal commits");
        Check(JToken.DeepEquals(slots.LoadProfile(catalog,out _).Snapshot(),snapshot),"imported profile reloads exactly");
        Check(memory.Read("sote_meta_v1")==text,"original browser profile untouched");
        Check(slots.List().All(s=>s.State==OriginalSaveSlotState.Empty),"profile import uses no run slot");
        Refuse(()=>slots.ImportWebProfile(catalog,before,imported),"stale destination and duplicate commit refused");
        // UI Toolkit map-camera coordinates are floats, serialized JSON reloads them
        // as doubles. An unchanged saved preference must not look like a new profile.
        var floatProfile=new OriginalProfile(catalog);
        floatProfile.SetSettings(new JObject{["mapViewer"]=new JObject{["solo"]=new JObject{["x"]=123.45678f,["zoom"]=1.2345678f}}});
        var floatSlots=new OriginalSaveSlots(new OriginalMemorySaveStorage(),"float-import","test");
        Check(floatSlots.SaveProfile(floatProfile),"float map preferences persisted");
        var floatImport=Import(text,floatProfile);
        Check(floatSlots.ImportWebProfile(catalog,floatProfile.Snapshot(),floatImport),"serialized map-camera floats do not cause false stale-preview refusal");
        var pendingProfile=new OriginalProfile(catalog);
        var pendingSlots=new OriginalSaveSlots(new OriginalMemorySaveStorage(),"pending-import","test");
        Check(pendingSlots.SaveProfile(pendingProfile),"pending-preferences baseline persisted");
        var storedBaseline=pendingSlots.LoadProfile(catalog,out _).Snapshot();
        pendingProfile.SetSettings(new JObject{["mapViewer"]=new JObject{["zoom"]=1.7}});
        var pendingImport=Import(text,pendingProfile);
        Check(pendingSlots.ImportWebProfile(catalog,storedBaseline,pendingImport),"unchanged durable baseline permits preserved pending local preferences");
        Check((double)pendingSlots.LoadProfile(catalog,out _).Snapshot()["settings"]["mapViewer"]["zoom"]==1.7,"pending local preferences included in verified imported profile");
        var conflictSlots=new OriginalSaveSlots(new OriginalMemorySaveStorage(),"conflict-import","test");
        Check(conflictSlots.SaveProfile(current),"concurrent-change baseline persisted");
        var changedProfile=OriginalProfile.Restore(catalog,before);
        changedProfile.Finish("another-climb",new JObject{["classId"]="rogue",["actNumber"]=1},false);
        Check(conflictSlots.SaveProfile(changedProfile),"genuine concurrent progress persisted");
        Refuse(()=>conflictSlots.ImportWebProfile(catalog,before,imported),"genuine durable change still refuses stale import");
        Check(JToken.DeepEquals(conflictSlots.LoadProfile(catalog,out _).Snapshot(),changedProfile.Snapshot()),"concurrent progress survives refusal");
        foreach(var failure in new[]{"write","flush","truncate"}){
            var store=new OriginalMemorySaveStorage();var armed=false;
            var faulty=new OriginalDelegateSaveStorage(store.Read,(key,value)=>{if(armed&&failure=="write")throw new IOException("full");store.Write(key,armed&&failure=="truncate"&&key.EndsWith("profile-import")?"bad":value);},()=>{if(armed&&failure=="flush")throw new IOException("full");store.Flush();},store.Delete);
            var target=new OriginalSaveSlots(faulty,"profile-import","test");Check(target.SaveProfile(current),"failure fixture persisted");var old=store.Read(target.ProfileKey);armed=true;
            Check(!target.ImportWebProfile(catalog,before,imported),"profile failure reported "+failure);armed=false;
            Check(JToken.DeepEquals(target.LoadProfile(catalog,out _).Snapshot(),before),"previous profile recoverable "+failure);
            Check(target.ImportWebProfile(catalog,before,imported),"profile import retries safely "+failure);
        }
        Console.WriteLine("Original profile import: "+checks+" checks passed");return checks;
    }
}
