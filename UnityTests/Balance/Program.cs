// Compare authored content under fixed, deliberately simple policies. This measures
// reachability and pacing signals, not optimal play or human enjoyment.
using System.Text.Json;
using AshenSpire.Domain;

if (args.Length != 2) throw new ArgumentException("Usage: Balance <campaign.json> <report.json>");
var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
var content = JsonSerializer.Deserialize<CampaignDefinition>(File.ReadAllText(args[0]), options)!;
content.Validate();
var rows = new List<object>();
foreach (var hero in content.Heroes)
foreach (var policy in new[] { "rest-rewards", "build-deck" })
{
    var wins = 0; var totalTurns = 0; var cardsTaken = 0; var healthRatio = 0d; var deaths = 0;
    var purchases = new Dictionary<string, int>();
    for (uint seed = 1; seed <= 24; seed++)
    {
        var run = new CampaignSession(content, hero.Id, seed);
        for (var step = 0; step < 3000 && run.State.Phase != RunPhase.Victory && run.State.Phase != RunPhase.Defeat; step++)
        {
            var state = run.State;
            if (state.Phase == RunPhase.Map)
            {
                var item = content.Equipment.Where(item => !state.Items.Contains(item.Id) && item.Price <= state.Cinders)
                    .OrderByDescending(item => GearScore(run, item)).FirstOrDefault();
                if (item != null && GearScore(run, item) > 0) { run.Buy(item.Id); purchases[item.Id] = purchases.GetValueOrDefault(item.Id) + 1; continue; }
                if (state.Health < state.MaxHealth / 2) run.Rest();
                run.Enter((int)(seed % (uint)run.Encounter.Options.Length));
            }
            else if (state.Phase == RunPhase.Reward)
            {
                if (policy == "rest-rewards" || state.Health < state.MaxHealth / 3) run.Reward(null);
                else { run.Reward(state.Rewards.OrderByDescending(id => RewardScore(run, run.Card(id))).First()); cardsTaken++; }
            }
            else
            {
                if (state.Health <= state.MaxHealth - content.PotionHealing) run.DrinkPotion();
                var index = state.Hand.FindIndex(id => run.Card(id).Cost <= state.Energy && run.Card(id).Effects.Any(effect => effect.Operation == "strength"));
                if (index < 0) index = state.Hand.FindIndex(id => run.Card(id).Cost <= state.Energy && run.Card(id).HasTag("attack"));
                if (index < 0) index = state.Hand.FindIndex(id => run.Card(id).Cost <= state.Energy);
                if (index >= 0) run.Play(index); else { run.EndTurn(); totalTurns++; }
            }
        }
        if (run.State.Phase == RunPhase.Victory) wins++;
        else if (run.State.Phase == RunPhase.Defeat) deaths++;
        else throw new Exception($"Policy did not terminate: {hero.Id}, {policy}, {seed}");
        healthRatio += (double)run.State.Health / run.State.MaxHealth;
    }
    rows.Add(new { Hero = hero.Id, Policy = policy, Runs = 24, Wins = wins, Deaths = deaths, MeanEndedTurns = Math.Round(totalTurns / 24d, 2), MeanFinalHealthRatio = Math.Round(healthRatio / 24, 3), MeanCardRewards = Math.Round(cardsTaken / 24d, 2), Purchases = purchases });
}
File.WriteAllText(args[1], JsonSerializer.Serialize(new { Content = Path.GetFileName(args[0]), ContentSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(args[0]))).ToLowerInvariant(), Seeds = "1..24", Routes = "seed modulo available routes", Note = "Fixed diagnostic policies; no claim of optimal play or fun. Final health includes defeats as zero.", Rows = rows }, options) + "\n");
Console.WriteLine("Balance diagnostics: 192 campaigns terminated; " + args[1]);

static double GearScore(CampaignSession run, EquipmentDefinition item)
{
    if (item.Operation == "health") return run.Hero.Tags.Contains(item.RequiredTag) ? item.Amount * 0.4 / Math.Max(1, item.Price) : 0;
    var count = run.State.Deck.Count(id => run.Card(id).Tags.Contains(item.RequiredTag) && run.Card(id).Effects.Any(effect => effect.Operation == item.Operation));
    return count * item.Amount * (item.Operation == "poison" ? 2 : 1) / (double)Math.Max(1, item.Price);
}
static double RewardScore(CampaignSession run, CardDefinition card) =>
    (card.Tags.Intersect(run.Hero.Tags).Any() ? 6 : 0) + card.Effects.Sum(effect => effect.Amount * (effect.Operation == "strength" ? 3 : effect.Operation == "poison" ? 2 : effect.Operation == "draw" ? 2 : 1)) / (double)Math.Max(1, card.Cost + 1);
