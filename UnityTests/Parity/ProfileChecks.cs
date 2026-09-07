// ProfileChecks.cs — original-source differential progression and persistence checks.
using System;
using System.IO;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
internal static class ProfileChecks
{
    internal static int Run(OriginalContentCatalog catalog, string fixturePath)
    {
        var checks = 0; var locations = new WeaponLoadout(catalog);
        void Equal(JToken actual, JToken expected, string label)
        { if (!JToken.DeepEquals(Normal(actual), Normal(expected))) throw new Exception(label + "\nactual=" + actual + "\nexpected=" + expected); checks++; }
        JToken Normal(JToken token)
        { if(token==null || token is JValue value && value.Value==null)return JValue.CreateNull();if(token is JObject obj)return new JObject(obj.Properties().OrderBy(x=>x.Name).Select(x=>new JProperty(x.Name,Normal(x.Value))));if(token is JArray array)return new JArray(array.Select(Normal));if(token.Type==JTokenType.Integer||token.Type==JTokenType.Float)return new JValue((double)token);return token.DeepClone(); }
var profileFixture=JObject.Parse(File.ReadAllText(fixturePath));var nativeProfile=new OriginalProfile(catalog);var profileIndex=0;
foreach(var example in profileFixture["profiles"]){var receipt=nativeProfile.Finish("run-"+profileIndex++,(JObject)example["run"],(bool)example["result"]["victory"]);Equal(receipt["result"],example["result"],"Profile original run summary");Equal(receipt["newUnlocks"],example["fresh"],"Profile original unlock awards");Equal(nativeProfile.Snapshot()["progress"],example["progress"],"Profile original durable progress");Equal(nativeProfile.Snapshot()["unlocked"],example["unlocked"],"Profile original earned ledger");Equal(nativeProfile.UnlockView(),example["view"],"Profile original reveal view");foreach(var slot in example["slots"]){Equal(new JValue(nativeProfile.OpenedSets((string)slot["id"])),slot["opened"],"Profile original opened sets");Equal(new JValue(nativeProfile.VisibleSets((string)slot["id"])),slot["visible"],"Profile original visible sets");}}
var beforeProfile=nativeProfile.Snapshot();var repeated=nativeProfile.Finish("run-0",new JObject(),false);Equal(repeated["duplicate"],new JValue(true),"Archived finish is still idempotent");Equal(nativeProfile.Snapshot(),beforeProfile,"Duplicate finish cannot change progression");Equal(new JValue(((JArray)nativeProfile.Snapshot()["results"]).Count),new JValue(20),"History bounded to twenty");Equal(nativeProfile.Snapshot()["progress"]["runs"],new JValue(30),"Progress survives history cap");Equal(OriginalProfile.Restore(catalog,nativeProfile.Snapshot()).Snapshot(),nativeProfile.Snapshot(),"Profile save round trip");
var legacy=locations.Create("reaver");legacy["sets"]["rightHand"][2]="dagger";Equal(new JValue(new OriginalProfile(catalog).OpenedSets("rightHand",legacy)),new JValue(3),"Legacy occupied set remains usable");
var collection=new OriginalProfile(catalog);var collectedRun=new JObject{["loadout"]=locations.Create("reaver"),["seedString"]="COLLECT"};((JArray)collectedRun["loadout"]["storage"]).Add("dagger");var discovery=collection.CollectArmament(collectedRun,"dagger","monster");Equal(discovery["recorded"],new JValue(true),"Successfully collected weapon becomes permanent");Equal(collection.Snapshot()["discoveredArmaments"],new JArray("dagger"),"Normal discovery opens kit ledger");Equal(collection.CollectArmament(collectedRun,"dagger","monster")["recorded"],new JValue(false),"Collection is idempotent");
var badProfile=nativeProfile.Snapshot();badProfile["schemaVersion"]=999;try{OriginalProfile.Restore(catalog,badProfile);throw new Exception("Accepted invalid schema");}catch(ArgumentException){checks++;}



var historyRows=((JArray)nativeProfile.Snapshot()["results"]).OfType<JObject>().ToArray();var standardRows=historyRows.Where(r=>(bool?)r["custom"]!=true).ToArray();Equal(nativeProfile.Telemetry()["runs"],new JValue(standardRows.Length),"Custom runs excluded from telemetry");Equal(nativeProfile.Telemetry()["wins"],new JValue(standardRows.Count(r=>(bool?)r["victory"]==true)),"Only standard victories counted");Equal(new JValue(OriginalProfile.IsCustomRun(new JObject{["mapShape"]=new JObject{["floors"]=3}})),new JValue(true),"Shortened maps are custom");
var customCollection=new OriginalProfile(catalog);collectedRun["custom"]=new JObject{["ascension"]=1};customCollection.CollectArmament(collectedRun,"dagger","monster");Equal(customCollection.Snapshot()["found"],new JArray("dagger"),"Custom collection retains original wardrobe find");Equal(customCollection.Snapshot()["discoveredArmaments"],new JArray(),"Custom collection cannot unlock starting kits");
try{collection.CollectArmament(collectedRun,"greatsword","monster");throw new Exception("Uncollected item entered profile");}catch(ArgumentException){checks++;}


return checks;
}}
