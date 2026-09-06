using System.Text.Json;
using AshenSpire.Domain;

var options = new JsonSerializerOptions { IncludeFields = true };
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
if(args.Length==2 && args[0]=="--validate-campaign")
{
    var strict=new JsonSerializerOptions {IncludeFields=true,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
    JsonSerializer.Deserialize<CampaignDefinition>(File.ReadAllText(args[1]),strict)!.Validate();
    Console.WriteLine("Campaign content: 1 complete schema validation passed");return;
}
var json = File.ReadAllText(Path.Combine(root, "GameContent/Unity/expedition.json"));
ContentDefinition Content() => JsonSerializer.Deserialize<ContentDefinition>(json, options)!;
var passed = 0;
void Check(bool result, string name) { if (!result) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
var content = Content(); content.Validate();
var first = new RunSession(content, 42); var second = new RunSession(Content(), 42);
Check(!first.PlayCard(0) && !first.EndTurn(), "commands cannot affect a map screen");
first.EnterEncounter(); second.EnterEncounter();
Check(first.State.Hand.SequenceEqual(second.State.Hand), "same seed produces same hand");
Check(!first.EnterEncounter(), "encounter cannot restart mid-combat");
var initialHealth = first.State.Health;
first.State.Hand = new List<string> { "defend", "strike", "strike", "strike", "technique" };
var defend = first.State.Hand.FindIndex(id => id == "defend");
Check(defend >= 0 && first.PlayCard(defend), "play a guard card");
first.EndTurn();
Check(first.State.Health == initialHealth - 2, "block absorbs damage and resets next turn");
Check(first.State.Block == 0 && first.State.Energy == 3, "next turn resources reset");
var snapshot = JsonSerializer.Serialize(first.State, options);
var resumed = new RunSession(Content(), JsonSerializer.Deserialize<RunState>(snapshot, options)!);
first.EndTurn(); resumed.EndTurn();
Check(JsonSerializer.Serialize(first.State, options) == JsonSerializer.Serialize(resumed.State, options), "resume preserves next draw and RNG");
var bad = Content(); bad.Cards[0].Tags = new[] { "unknown" };
Reject(() => bad.Validate(), "unknown tags name a validation error");
bad = Content(); bad.StartingDeck[0] = "missing";
Reject(() => bad.Validate(), "dangling card references rejected");
bad = Content(); bad.Cards[0].Effects[0].Operation = "teleport";
Reject(() => bad.Validate(), "unsupported behavior rejected");
var saved = JsonSerializer.Deserialize<RunState>(snapshot, options)!; saved.SchemaVersion = 99;
Reject(() => new RunSession(Content(), saved), "unsupported saves rejected");
var winner = new RunSession(Content(), 42); winner.EnterEncounter();
// Test fixture weakens the enemy state, then observes actual commands and reward guards.
winner.State.EnemyHealth = 1;
var strike = winner.State.Hand.FindIndex(id => id == "strike"); winner.PlayCard(strike);
Check(winner.State.Phase == RunPhase.Reward && winner.State.Cinders == 20, "enemy defeat grants reward exactly once");
Check(!winner.EndTurn() && !winner.PlayCard(0), "combat cannot continue after victory");
winner.ClaimReward("unity.heavyStrike");
Check(winner.State.Deck.Contains("unity.heavyStrike") && winner.State.Phase == RunPhase.Map && winner.State.Encounter == 1, "reward changes persistent deck and advances map");
Check(!winner.ClaimReward("unity.heavyStrike"), "reward cannot be claimed twice");
var loser = new RunSession(Content(), 9); loser.EnterEncounter(); loser.State.Health = 1; loser.EndTurn();
Check(loser.State.Phase == RunPhase.Defeat && !loser.EndTurn(), "defeat terminates turn loop");
var wins = 0;
for (uint seed = 1; seed <= 30; seed++)
{
    var run = new RunSession(Content(), seed);
    for (var steps = 0; steps < 1000 && run.State.Phase != RunPhase.Victory && run.State.Phase != RunPhase.Defeat; steps++)
    {
        if (run.State.Phase == RunPhase.Map) run.EnterEncounter();
        else if (run.State.Phase == RunPhase.Reward) run.ClaimReward(null);
        else
        {
            var index = run.State.Hand.FindIndex(id => run.GetCard(id).HasTag("attack") && run.GetCard(id).Cost <= run.State.Energy);
            if (index < 0) index = run.State.Hand.FindIndex(id => run.GetCard(id).Cost <= run.State.Energy);
            if (index >= 0) run.PlayCard(index); else run.EndTurn();
        }
    }
    if (run.State.Phase == RunPhase.Victory) wins++;
    Check(run.State.Phase == RunPhase.Victory || run.State.Phase == RunPhase.Defeat, $"seed {seed}: full run terminates");
}
Check(wins > 0, "full expedition victory is reachable");
Console.WriteLine($"Simulation: {wins}/30 simple-policy wins; not a fun or balance assessment.");
Console.WriteLine($"Domain: {passed} checks passed");
CampaignChecks.Run(root);
ClassIdentityChecks.Run(root);
FeedbackChecks.Run(root);
