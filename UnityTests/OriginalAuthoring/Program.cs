// Authoring acceptance: independently imported CSV additions execute in native combat.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
var root = Directory.GetCurrentDirectory();
var catalog = new OriginalContentCatalog(File.ReadAllText(args[0]));
var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(root,"GameContent/Unity/Original/mechanics.json")));
var progression = new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(root,"GameContent/Unity/Original/progression.json"))));
var checks = 0;
void Check(bool yes,string name) { if (!yes) throw new Exception(name); checks++; }
var creation = new CreationModel(catalog,"reaver","standard",progression);
var player = new OriginalCharacterBuilder(catalog,progression,mechanics).Build(creation,"authoringKit");
Check((string)player["loadout"]!["sets"]!["rightHand"]![0] == "authoringSword","CSV weapon equipped through authored kit");
Check(player["deck"]!.Any(c => (string)c["equipmentRole"] == "attack"),"weapon supplies stable attack cards");
var projection = new WeaponCardProjection(catalog);
JObject Resolve(JObject card) => (JObject)progression.ResolveCard(projection.Resolve(card,(JObject)player["loadout"]!,"reaver",creation.Attributes(),progression.BaselineProfiles(catalog)),creation.Attributes(),catalog)["card"]!;
var attack = (JObject)player["deck"]!.First(c => (string)c["equipmentRole"] == "attack");
Check(Resolve(attack)["effects"]!.Any(e => (string)e["op"] == "damage"),"CSV weapon projects a live damage operation");
var enemyIds = catalog.Record("encounters","authoringEncounter")["enemies"]!.Values<string>();
player["energyMax"] = player["energy"]!.DeepClone(); player["drawPerTurn"] = 2;
var authoredCard = new JObject { ["cardId"] = "authoringStrike", ["instanceId"] = "authoring:1", ["upgraded"] = false };
var combat = new CombatSession(catalog,mechanics,new RandomStreams(13),player,new[]{authoredCard,attack},enemyIds!,Resolve);
Check((string)combat.Enemies[0]["enemyId"]! == "authoringSoldier","CSV encounter spawns CSV enemy");
var hp = (int)combat.Enemies[0]["hp"]!;
combat.PlayCard("authoring:1","e1");
Check((int)combat.Enemies[0]["hp"]! < hp,"CSV card changes actual enemy HP");
var saved = combat.Snapshot(); var restored = CombatSession.Restore(catalog,mechanics,saved,Resolve);
Check(JToken.DeepEquals(JObject.Parse(saved.ToString()),JObject.Parse(restored.Snapshot().ToString())),"authored records survive native combat restore");
foreach (var edit in new Action<JObject>[] {
    d => d["encounters"]![0]!["enemies"]![0] = "missingEnemy",
    d => d["equipment"]!["armaments"]![0]!["attackProfile"] = "missingProfile",
    d => d["equipment"]!["startingKits"]![0]!["rightHand"] = "missingWeapon",
    d => d["classes"]![0]!["startingRelic"] = "missingRelic",
    d => ((JArray)d["equipment"]!["armour"]!).Add(d["equipment"]!["armour"]![0]!.DeepClone()) })
{
    var invalid = catalog.Data(); edit(invalid); var refused = false;
    try { _ = new OriginalContentCatalog(invalid.ToString()); } catch (ArgumentException) { refused = true; }
    Check(refused,"broken cross-table reference refused before source replacement");
}
Console.WriteLine($"CSV additions execute in native combat: {checks} checks passed");
