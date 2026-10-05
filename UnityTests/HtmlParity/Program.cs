#nullable enable
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

var root = args.Length > 0 ? args[0] : ".";
if(args.Contains("--arcane")) { Console.WriteLine("Direct Arcane Buildup: "+ArcaneBuildupChecks.Run(root)+" checks passed.");return; }
if(args.Contains("--ward")) { Console.WriteLine("Arcane Ward: "+WardChecks.Run(root)+" checks passed.");return; }
if(args.Contains("--legacy-coop")) { Console.WriteLine("Legacy frozen co-op: "+CoopCombatChecks.Run(root)+" checks passed.");return; }
if(args.Contains("--catalog")) { var catalogCheck=new OriginalContentCatalog(File.ReadAllText(Path.Combine(root,"TestResults/HtmlParity/test898/content-reference.json")));Console.WriteLine("Published catalog accepted: "+catalogCheck.Version);return; }
var checks = 0;
checks += ArcaneBuildupChecks.Run(root);
checks += WardChecks.Run(root);
checks += HandRefreshChecks.Run(root);
checks += TurnBudgetChecks.Run(root);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var compactCard=JObject.Parse("{\"textTemplate\":\"Deal {damage} damage.\",\"effects\":[{\"op\":\"damage\",\"amount\":2,\"attributeBonus\":4}],\"attributeProgression\":[{\"operation\":\"damage\",\"bonus\":4,\"label\":\"Power Rating\"}]}");
var compactBefore=compactCard.ToString();
Check(OriginalCardText.Describe(compactCard,null!,includeRatingBreakdown:false)=="Deal 6 damage.","Compact card retains the full resolved damage");
Check(OriginalCardText.Describe(compactCard,null!).Contains("Includes +4 total damage from Power Rating."),"Inspector retains the rating explanation");
Check(compactCard.ToString()==compactBefore,"Compact rendering does not mutate a card");
compactCard["effects"]![0]!["hits"]=2;
Check(OriginalCardText.Describe(compactCard,null!,includeRatingBreakdown:false).Contains("+4 total damage from Power Rating."),"Multi-hit card retains the separate total bonus");
compactCard["effects"]![0]!["hits"]=1;compactCard["effects"]![0]!["repeat"]=2;
Check(OriginalCardText.Describe(compactCard,null!,includeRatingBreakdown:false).Contains("+4 total damage from Power Rating."),"Repeated card retains the separate total bonus");
JToken Normal(JToken token) => token switch {
    JValue { Value: null } => JValue.CreateNull(),
    JObject obj => new JObject(obj.Properties().Select(p => new JProperty(p.Name,Normal(p.Value)))),
    JArray array => new JArray(array.Select(Normal)),
    { Type: JTokenType.Integer or JTokenType.Float } => new JValue((double)token),
    _ => token.DeepClone()
};
void Equal(JToken? actual,JToken? expected,string label) {
    var a=actual??JValue.CreateNull(); var b=expected??JValue.CreateNull();
    Check(JToken.DeepEquals(Normal(a),Normal(b)),label+": "+a.ToString(Newtonsoft.Json.Formatting.None)+" != "+b.ToString(Newtonsoft.Json.Formatting.None));
}
var oracle = JObject.Parse(File.ReadAllText(Path.Combine(root,"TestResults/HtmlParity/test898/xp-curve-reference.json")));
foreach (var row in oracle["cases"]!)
    Check(OriginalXpCurve.StepCost((JObject)row["curve"]!, (int)row["step"]!) == (double)row["value"]!, "Published JS XP arithmetic differs at step " + row["step"]);

var progressionOracle=JObject.Parse(File.ReadAllText(Path.Combine(root,"TestResults/HtmlParity/test898/earned-progression-reference.json")));
var publishedContent=JObject.Parse(File.ReadAllText(Path.Combine(root,"TestResults/HtmlParity/test898/content-reference.json")));
var paymentOracle=JObject.Parse(File.ReadAllText(Path.Combine(root,"TestResults/HtmlParity/test898/card-payment-reference.json")));
foreach(var sample in paymentOracle["cases"]!)
    Equal(OriginalCardPayment.Profile((JObject)sample["def"]!,(int)sample["powerCostReduction"]!,sample["weightClass"] as JObject),sample["profile"],"Published card cost profile");
var choices=new OriginalCardChoices((JArray)publishedContent["stances"]!);
foreach(var sample in paymentOracle["choiceCases"]!) {
    var plan=choices.Plan((JObject)sample["def"]!,(string)sample["classId"]!,(string?)sample["active"]);
    Equal(plan,sample["plan"],"Published stance offer");
    foreach(var answer in sample["answers"]!) {
        string? result=null;var accepted=true;
        try { result=OriginalCardChoices.Assert(plan!,(string?)answer["choice"]!); } catch(ArgumentException) { accepted=false; }
        Check(accepted==(bool)answer["accepted"]!,"Published stance choice legality");
        if(accepted) Equal(result==null?null:new JValue(result),answer["value"],"Accepted stance choice");
    }
}
foreach(var sample in paymentOracle["aliasCases"]!) {
    var player=new JObject { ["energy"]=9,["energyMax"]=99,["stamina"]=sample["stamina"]!.DeepClone(),["maxStamina"]=7,["mana"]=2 };
    var pool=new OriginalTurnStamina(player);Equal(pool.Snapshot(),sample["initial"],"Bound Stamina aliases");
    var i=0;foreach(var write in sample["writes"]!) { pool.Write((string)write[0]!,write[1]!);Equal(pool.Snapshot(),sample["states"]![i++],"Stamina alias write"); }
}
foreach(var variable in new[]{false,true}) foreach(var stamina in new[]{0,1,3}) foreach(var mana in new[]{0,1,2}) foreach(var confirmed in new[]{false,true}) {
    var player=new JObject { ["stamina"]=stamina,["energy"]=stamina,["mana"]=mana,["staminaSpentThisTurn"]=2 };
    var before=(JObject)player.DeepClone();var profile=new JObject { ["action"]=1,["stamina"]=1,["mana"]=1,["variable"]=variable };
    var paid=OriginalCardPayment.TryPay(player,profile,confirmed);var spend=variable?stamina:1;
    var accepted=confirmed&&stamina>=spend&&mana>=1;Check((paid!=null)==accepted,"Atomic single-pool affordability");
    if(!accepted) Equal(player,before,"Refused or cancelled play preserves all pools");
    else { Check((int)player["stamina"]! == stamina-spend && (int)player["energy"]! == stamina-spend,"Stamina charged once with alias");Check((int)player["mana"]! == mana-1 && (int)player["staminaSpentThisTurn"]! == 2+spend,"Mana and spend counter commit together"); }
}
var combatXpOracle=JObject.Parse(File.ReadAllText(Path.Combine(root,"TestResults/HtmlParity/test898/combat-xp-reference.json")));
var combatXp=new OriginalCombatXp(publishedContent);
foreach(var sample in combatXpOracle["powerCases"]!) Check(Math.Abs(combatXp.Power((JObject)sample["enemy"]!) - (double)sample["value"]!)<1e-12,"Published enemy combat power");
foreach(var sample in combatXpOracle["awardCases"]!) {
    var receipt=combatXp.Receipt((JObject)sample["options"]!);Equal(receipt,sample["receipt"],"Cumulative combat XP receipt");
    Check(((JArray)receipt["rows"]!).Sum(row=>(double)row["amount"]!)==(double)receipt["total"]!,"Displayed XP terms sum to exact payout");
}
var statTagOracle=JObject.Parse(File.ReadAllText(Path.Combine(root,"TestResults/HtmlParity/test898/stat-tag-reference.json")));
foreach(var sample in statTagOracle["cases"]!) {
    var table=OriginalStatRows.Resolve((JObject)publishedContent["derivedStatRules"]!,sample["layers"]!.OfType<JObject>().ToArray());
    Equal(table,sample["resolved"],"Resolved current stat rows");
    foreach(var receipt in ((JObject)sample["receipts"]!).Properties())
        Equal(OriginalStatRows.Receipt(table,receipt.Name,(JObject)sample["attributes"]!,(JObject)sample["classDef"]!,(int?)sample["level"]),receipt.Value,"Current stat "+receipt.Name);
}
var tags=new TagCatalog(publishedContent,(JObject)statTagOracle["externalObjects"]!);
foreach(var sample in statTagOracle["tagCases"]!) {
    Equal(new JArray(tags.For((string)sample["family"]!,(JObject)sample["record"]!)),sample["tags"],"Current property tags");
    Equal(new JArray(tags.KindsFor((string)sample["family"]!,(JObject)sample["record"]!)),sample["kinds"],"Current classification tags");
}
void Refused(Action action,string label) { var refused=false;try { action(); } catch(ArgumentException) { refused=true; } Check(refused,label); }
var badLocation=(JObject)publishedContent.DeepClone();
((JArray)badLocation["tagging"]!).Add(new JObject { ["family"]="location",["scope"]="",["objectId"]="unknown-place",["tagId"]="restHpPartial" });
Refused(()=>new TagCatalog(badLocation,(JObject)statTagOracle["externalObjects"]!),"Unknown external location refused");
var asideRow=publishedContent["tagging"]!.First(row=>publishedContent["tags"]!.Any(tag=>(string?)tag["id"]==(string?)row["tagId"]&&publishedContent["tagDomains"]!.Any(domain=>(string?)domain["id"]==(string?)tag["domain"]&&(bool?)domain["aside"]==true)));
var duplicateAside=(JObject)publishedContent.DeepClone();((JArray)duplicateAside["tagging"]!).Add(asideRow.DeepClone());
Refused(()=>new TagCatalog(duplicateAside,(JObject)statTagOracle["externalObjects"]!),"Duplicate aside tag refused");
Refused(()=>OriginalStatRows.Resolve((JObject)publishedContent["derivedStatRules"]!,new JObject { ["rules"]=new JObject { ["hp"]=new JObject { ["constitutionn"]=1 } } }),"Misspelled stat-row weight refused");
Refused(()=>OriginalStatRows.Resolve((JObject)publishedContent["derivedStatRules"]!,new JObject { ["rules"]=new JObject { ["hp"]=new JObject { ["sourceStat"]="strength" } } }),"Current row refuses ambiguous old override");
var publishedMechanics=(JObject)progressionOracle["mechanics"]!;
foreach(var sample in progressionOracle["cases"]!) {
    var data=new JObject { ["balance"]=publishedContent["balance"]!.DeepClone() }; data["balance"]!["level"]!["maxLevelsPerFight"]=sample["cap"]!.DeepClone();
    data["balance"]!["levelUp"]!["maxLevels"]=sample["ceiling"]!.DeepClone();
    var engine=new OriginalEarnedProgression(data,publishedMechanics);
    var run=new JObject { ["level"]=new JObject { ["level"]=sample["level"]!.DeepClone(),["xp"]=sample["xp"]!.DeepClone(),["unspentPoints"]=2 },["cinders"]=999 };
    Equal(engine.Climb((int)sample["level"]!,(double)sample["xp"]!,(double)sample["gain"]!),sample["climb"],"Character climb");
    Equal(engine.BankCharacter(run,(double)sample["gain"]!),sample["bank"],"Character bank");
    var claims=new JArray(); while(engine.PendingCharacterLevels(run)>0)claims.Add(engine.ClaimCharacter(run));
    Equal(claims,sample["claims"],"Character claims"); Equal(engine.ClaimCharacter(run),sample["refused"],"Unpaid claim");
    Equal(run["level"],sample["ledger"],"Character ledger"); Equal(run["cinders"],sample["cinders"],"Character claim preserves purse");
}
var progression=new OriginalEarnedProgression(publishedContent,publishedMechanics);
var callbackCount=0;
var claimRun=new JObject { ["level"]=new JObject { ["level"]=1,["xp"]=100,["unspentPoints"]=2 },["hp"]=51,["maxHp"]=70,["cinders"]=191 };
var claimEngine=new OriginalEarnedProgression(publishedContent,publishedMechanics,draft=>{callbackCount++;draft["hp"]=(int)draft["hp"]!+1;draft["maxHp"]=(int)draft["maxHp"]!+1;return 1;});
claimEngine.BankCharacter(claimRun,0);Check(callbackCount==0,"Banking never reconciles pools");
var claimReceipt=claimEngine.ClaimCharacter(claimRun);Check(callbackCount==1,"Claim reconciles exactly once");
Check((int)claimRun["hp"]! - (int)claimRun["maxHp"]! == -19,"Claim callback carries HP deficit");
Check((int)claimRun["cinders"]! == 191 && (int)claimReceipt!["thresholds"]! == 1,"Claim preserves purse and returns reconciliation receipt");
var failedRun=new JObject { ["level"]=new JObject { ["level"]=1,["xp"]=100,["unspentPoints"]=2 },["hp"]=51,["cinders"]=191 };
var beforeFailure=(JObject)failedRun.DeepClone();
var failureEngine=new OriginalEarnedProgression(publishedContent,publishedMechanics,draft=>{draft["hp"]=0;throw new ArgumentException("Rejected pool projection");});
Refused(()=>failureEngine.ClaimCharacter(failedRun),"Failed claim projection propagates refusal");Equal(failedRun,beforeFailure,"Rejected level claim preserves all original state");
Equal(progression.Tracks(),progressionOracle["tracks"],"Derived skill tracks");
foreach(var sample in progressionOracle["skillCases"]!) {
    var id=(string)sample["id"]!; var run=new JObject { ["skills"]=new JObject { [id]=new JObject { ["xp"]=25,["level"]=sample["level"]!.DeepClone(),["pendingDrafts"]=2 } },["deck"]=new JArray(),["sideboard"]=new JArray() };
    Equal(progression.BankSkill(run,id,(double)sample["gain"]!),sample["bank"],"Skill bank");
    var claims=new JArray();while(progression.PendingSkillLevels(run,id)>0)claims.Add(progression.ClaimSkill(run,id));
    Equal(claims,sample["claims"],"Skill claims");Equal(progression.ClaimSkill(run,id),sample["refused"],"Unpaid skill claim");
    Equal(new JValue(progression.SpendSkillDraft(run,id)),sample["spent"],"Spend draft");Equal(run["skills"]![id],sample["ledger"],"Skill ledger");
}
foreach(var sample in progressionOracle["heldCases"]!) {
    var run=(JObject)sample["run"]!.DeepClone();var id=(string)sample["id"]!;
    Equal(new JArray(progression.Schools((JObject)run["loadout"]!,id)),sample["schools"],"Held schools "+sample["piece"]);
    Equal(progression.ClaimSkill(run,id),sample["claim"],"Standing upgrade "+sample["piece"]);Equal(run,sample["after"],"Owned cards "+sample["piece"]);
}

var reference = JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/coop-reference.json")));
{
    var choiceContent=(JObject)reference["content"]!.DeepClone();
    foreach(var stance in choiceContent["stances"]!.OfType<JObject>()) stance["class"]="reaver";
    var strike=choiceContent["cards"]!.OfType<JObject>().First(c=>(string?)c["id"]=="strike");
    strike["class"]="reaver";strike["cost"]=1;strike["effects"]=new JArray(new JObject { ["op"]="enterStance",["choose"]="classStance",["target"]="self" });
    var catalog=new OriginalContentCatalog(choiceContent.ToString());
    var mechanics=JObject.Parse(File.ReadAllText(Path.Combine(root,"GameContent/Unity/Original/mechanics.json")));
    var input=(JObject)reference["fixtures"]![0]!["players"]![0]!.DeepClone();
    var deck=new[]{new JObject { ["instanceId"]="choice1",["cardId"]="strike",["upgraded"]=false },new JObject { ["instanceId"]="choice2",["cardId"]="strike",["upgraded"]=false }};
    JObject ResolveChoice(JObject instance)=>catalog.Record("cards",(string)instance["cardId"]!);
    var combat=new CombatSession(catalog,mechanics,new RandomStreams(3),input,deck,new[]{"wanderingSoldier"},ResolveChoice);
    var before=combat.Snapshot();
    Refused(()=>combat.PlayCard("choice1"),"Missing stance choice refuses before payment");Equal(combat.Snapshot(),before,"Missing choice preserves complete fight");
    Refused(()=>combat.PlayCard("choice1",null,"unknown"),"Unknown stance refuses before payment");Equal(combat.Snapshot(),before,"Unknown choice preserves complete fight");
    var copy=CombatSession.Restore(catalog,mechanics,before,ResolveChoice);
    var choiceEvents=copy.PlayCard("choice1",null,"gorefire");Equal(combat.Snapshot(),before,"Choice preview copy preserves original fight");
    Equal(combat.PlayCard("choice1",null,"gorefire"),choiceEvents,"Chosen stance replay has identical events");
    Equal(combat.Snapshot(),copy.Snapshot(),"Chosen stance replay has identical state");
    Check((string?)combat.Player["stanceId"]=="gorefire" && (int)combat.Player["energy"]! == (int)before["player"]!["energy"]!-1 && combat.Hand.Count==1,"Legal choice enters stance and pays once");
    combat.PlayCard("choice2",null,"gorefire");Check(combat.Hand.Count==0,"Active stance retains published legacy no-op payment behavior");
    var seats=reference["fixtures"]![0]!["players"]!.OfType<JObject>().Select(p=>{var seat=(JObject)p.DeepClone();seat["classId"]="reaver";seat["deck"]=new JArray(deck.Select(c=>c.DeepClone()));return seat;}).ToArray();
    JObject ResolveSeat(string member,JObject instance)=>ResolveChoice(instance);
    var party=new OriginalCoopCombat(catalog,mechanics,new RandomStreams(3),seats,new[]{"wanderingSoldier"},ResolveSeat);
    var partyBefore=party.Snapshot();Refused(()=>party.Play("p1","choice1",null,"unknown"),"Party invalid stance choice refuses");Equal(party.Snapshot(),partyBefore,"Party invalid choice restores all seats and RNG");
    var partyCopy=OriginalCoopCombat.Restore(catalog,mechanics,partyBefore,ResolveSeat);
    Equal(party.Play("p1","choice1",null,"bulwark"),partyCopy.Play("p1","choice1",null,"bulwark"),"Party stance events resume exactly");
    Equal(party.Snapshot(),partyCopy.Snapshot(),"Party stance state resumes exactly");
}
foreach (var delayed in new[] { false, true }) foreach (var target in new string?[] { null, "self", "player" })
{
    var content = (JObject)reference["content"]!.DeepClone();
    var enemy = content["enemies"]!.OfType<JObject>().First(row => (string?)row["id"] == "wanderingSoldier");
    var inject = new JObject { ["op"]="addCard", ["card"]="dazed", ["pile"]="discard", ["count"]=1 };
    if (target != null) inject["target"] = target;
    var move = new JObject { ["weight"]=1, ["intent"]="debuff", ["block"]=7, ["effects"]=new JArray(new JObject { ["op"]="applyStatus", ["target"]="self", ["status"]="strength", ["stacks"]=1 }) };
    if (delayed) move["delay"] = new JObject { ["turns"]=1, ["whileCharging"]=new JObject { ["effects"]=new JArray(inject), ["block"]=7 } };
    else ((JArray)move["effects"]!).Insert(0,inject);
    enemy["firstMove"]="parity"; enemy["moves"]=new JObject { ["parity"]=move };
    var catalog = new OriginalContentCatalog(content.ToString());
    var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(root,"GameContent/Unity/Original/mechanics.json")));
    mechanics["coop"] = new JObject { ["seatPileEffectsFanOut"]=true };
    var players = reference["fixtures"]![0]!["players"]!.OfType<JObject>().Select(row => (JObject)row.DeepClone()).ToArray();
    JObject Resolve(string member, JObject card) => catalog.Record("cards",(string)card["cardId"]!);
    var game = new OriginalCoopCombat(catalog,mechanics,new RandomStreams(1),players,new[] { "wanderingSoldier" },Resolve);
    game.EndTurn("p1");
    var save = game.Snapshot();
    game = OriginalCoopCombat.Restore(catalog,mechanics,save,Resolve);
    Check(JToken.DeepEquals(save,game.Snapshot()),"Mid-turn party restore differs.");
    game.EndTurn("p2");
    foreach (var player in game.Players)
    {
        var cards = ((JObject)player["piles"]!).Properties().SelectMany(p => p.Value.OfType<JObject>());
        Check(cards.Count(card => (string?)card["cardId"] == "dazed") == 1,"Enemy pile injection missed seat " + player["id"] + "; target=" + target + "; delayed=" + delayed);
    }
    Check((int)game.Enemies[0]!["block"]! == 7,"Enemy self block was duplicated.");
    if (!delayed) Check((int)game.Enemies[0]!["statuses"]!["strength"]!["stacks"]! == 1,"Enemy self status was duplicated.");
}
Console.WriteLine($"Published test-898 migration: {checks} checks passed.");
