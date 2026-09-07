// StartingOptionsChecks.cs — original choice/identity oracle and owner requirement gates.
using System;
using System.IO;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
internal static class StartingOptionsChecks
{
    internal static int Run(OriginalContentCatalog catalog, JObject mechanics, JObject progressionData, string fixturePath)
    {
        var checks = 0; var service = new OriginalStartingOptions(catalog); var fixtures = JObject.Parse(File.ReadAllText(fixturePath));
        JToken Normal(JToken t) { if(t==null || t is JValue v && v.Value==null)return JValue.CreateNull();if(t is JObject o)return new JObject(o.Properties().OrderBy(p=>p.Name).Select(p=>new JProperty(p.Name,Normal(p.Value))));if(t is JArray a)return new JArray(a.Select(Normal));if(t.Type==JTokenType.Integer||t.Type==JTokenType.Float)return new JValue((double)t);return t.DeepClone(); }
        void Equal(JToken a,JToken b,string label) { if(!JToken.DeepEquals(Normal(a),Normal(b)))throw new Exception(label+"\nactual="+a+"\nexpected="+b);checks++; }
        void Refuses(Action action,string label) { try{action();throw new Exception(label);}catch(ArgumentException){checks++;} }
        foreach(var fixture in fixtures["fixtures"])
        {
            var cls=(string)fixture["classId"];var meta=(JObject)fixture["meta"];var attributes=(JObject)fixture["attributes"];
            Equal(service.AvailableKits(cls,meta),fixture["kits"],"Original discovered kit views");Equal(service.AvailableArmour(cls,meta),fixture["armour"],"Original free/authored/earned armour views");
            foreach(var hand in new[]{"leftHand","rightHand"})Equal(new JArray(service.AvailableHands(cls,hand).Select(p=>p["id"].DeepClone())),fixture["hands"][hand],"Original hand-fit choice IDs");
            Equal(new JArray(service.AvailableRelics(cls).Select(p=>p["id"].DeepClone())),fixture["relics"],"Original class relic choices");
            foreach(var request in fixture["cases"])
            {
                JObject result=null;Exception error=null;try{result=service.Resolve(cls,(string)request["kitId"],attributes,meta,(JObject)request["options"]);}catch(ArgumentException e){error=e;}
                Equal(new JValue(error!=null),new JValue(request["error"]?.Type==JTokenType.String),"Original starting request eligibility "+error);
                if(error!=null)continue;Equal(result,request["result"],"Original starting identity and authored requirement receipts");
                var run=new JObject{["classId"]=cls,["startingKitId"]=result["startingKitId"].DeepClone(),["startingKitSnapshot"]=result["startingKitSnapshot"].DeepClone(),["loadout"]=result["loadout"].DeepClone(),["profileMeta"]=meta.DeepClone()};
                service.ValidateSaved(run,meta);checks++;service.ValidateSaved(JObject.Parse(run.ToString()),meta);checks++;
            }
        }
        var allFound=new JObject{["discoveredArmaments"]=new JArray(catalog.Table("equipment.armaments").Select(a=>a["id"].DeepClone())),["unlocked"]=new JArray(catalog.Table("unlocks").Select(u=>u["id"].DeepClone()))};
        var progression=new AttributeProgression(progressionData);var builder=new OriginalCharacterBuilder(catalog,progression,mechanics);
        foreach(var hero in catalog.Table("classes"))
        {
            var cls=(string)hero["id"];var creation=new CreationModel(catalog,cls,"standard",progression);var player=builder.Build(creation,null,allFound);
            service.ValidateSaved(JObject.Parse(player.ToString()));checks++;
            var preview=builder.Preview(creation,null,allFound);Equal(player["maxHp"],preview["resources"]["hp"],"Builder matches selected starting resource preview");
            var prior=player.DeepClone();new OriginalPlayerProjection(catalog,mechanics).Reconcile(player);Equal(player["hp"],prior["hp"],"Run initialization does not add starter relic bonus twice");
            var corrupt=(JObject)player.DeepClone();corrupt["startingKitSnapshot"]["classId"]="missing";Refuses(()=>service.ValidateSaved(corrupt),"Cross-class saved identity accepted");
            var legacy=(JObject)player.DeepClone();legacy.Remove("startingKitId");legacy.Remove("startingKitSnapshot");Refuses(()=>service.ValidateSaved(legacy),"Implicit migration accepted");service.ValidateSaved(legacy,legacy:true);Equal(legacy["startingKitId"],player["startingKitId"],"Explicit legacy baseline adoption");
        }
        var stats=new JObject(catalog.Table("attributes").Select(a=>new JProperty((string)a["id"],5)));
        var baseline=service.Resolve("reaver",null,stats);Equal(baseline["canBegin"],new JValue(true),"Uncustomized baseline retains birth waiver");
        var custom=service.Resolve("reaver",null,stats,null,new JObject{["startingHands"]=new JObject{["rightHand"]="straightSword",["leftHand"]=null}});Equal(custom["canBegin"],new JValue(false),"Explicit custom hands must meet requirements");
        var badHands=new JObject{["startingHands"]=new JObject{["rightHand"]="straightSword",["leftHand"]="straightSword"}};Refuses(()=>service.Resolve("reaver",null,stats,null,badHands),"Duplicate starting weapon accepted");
        Equal(OriginalStartingOptions.SelectHand(new JObject{["rightHand"]="dagger",["leftHand"]="kiteShield"},"leftHand","dagger"),JObject.Parse("{\"rightHand\":null,\"leftHand\":\"dagger\"}"),"Moving starting weapon clears former hand");
        return checks;
    }
}
