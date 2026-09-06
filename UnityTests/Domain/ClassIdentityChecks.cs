using System.Text.Json;
using AshenSpire.Domain;

public static class ClassIdentityChecks
{
    public static void Run(string root)
    {
        var json = File.ReadAllText(Path.Combine(root, "GameContent/Unity/campaign.json"));
        var options = new JsonSerializerOptions { IncludeFields = true };
        CampaignDefinition Content() => JsonSerializer.Deserialize<CampaignDefinition>(json, options)!;
        var count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
        void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception("FAIL: expected rejection " + name); }
        var content = Content();
        foreach (var hero in content.Heroes)
        {
            Check(hero.Deck.Count(id => content.Cards.First(card => card.Id == id).Tags.Intersect(hero.RewardTags).Any()) >= 4, hero.Id + " starts with at least four affinity cards");
            var valid = true;
            for (uint seed = 1; seed <= 16; seed++)
            {
                var run = new CampaignSession(content, hero.Id, seed); run.Enter(0);
                run.State.EnemyHealth = 1; run.State.EnemyPoison = 1;
                var saved = JsonSerializer.Serialize(run.State, options);
                var restored = new CampaignSession(content, JsonSerializer.Deserialize<CampaignState>(saved, options)!);
                run.EndTurn(); restored.EndTurn();
                valid &= run.State.Rewards.Count == 3 && run.State.Rewards.Distinct().Count() == 3 &&
                    run.State.Rewards.Count(id => run.Card(id).Tags.Intersect(hero.RewardTags).Any()) == 2 &&
                    run.State.Rewards.Count(id => run.Card(id).HasTag(content.CommonRewardTag)) == 1 &&
                    JsonSerializer.Serialize(run.State, options) == JsonSerializer.Serialize(restored.State, options);
            }
            Check(valid, hero.Id + " gets two affinity and one shared offer deterministically over sixteen seeds");
        }
        var old = new CampaignSession(content, "reaver", 7); old.Enter(0);
        old.State.Deck = new() { "strike", "strike", "strike", "defend", "defend", "defend", "cleave", "rage" };
        old.State.Phase = RunPhase.Reward; old.State.Rewards = new() { "starbolt", "venom", "smite" };
        var legacy = JsonSerializer.Serialize(old.State, options);
        var resumed = new CampaignSession(content, JsonSerializer.Deserialize<CampaignState>(legacy, options)!);
        Check(JsonSerializer.Serialize(resumed.State, options) == legacy, "saved decks and pre-affinity pending rewards are not replaced");
        Check(resumed.Reward("starbolt") && resumed.State.Deck.Contains("starbolt"), "a saved cross-class reward remains claimable after the update");

        var rogue = new CampaignSession(content, "rogue", 4); rogue.State.Cinders = 100;
        var vial = content.Equipment.First(item => item.Id == "venomVial");
        Check(rogue.MatchingCards(vial) == 2 && rogue.Buy(vial.Id), "poison gear identifies matching poison effects in the starter deck");
        rogue.Enter(0); rogue.State.Hand = new() { "venom", "strike" };
        Check(rogue.Describe(rogue.Card("venom")).Contains("Apply 4 poison"), "poison card description includes its equipment bonus");
        rogue.Play(0);
        Check(rogue.State.EnemyPoison == 4 && rogue.LastAction.Contains("4 enemy poison added"), "poison equipment and actual feedback agree");
        rogue.Play(0); Check(rogue.State.EnemyPoison == 4, "poison gear cannot add poison to an unrelated damage card");
        var herald = new CampaignSession(content, "herald", 4); herald.State.Cinders = 100; herald.Buy("sunwardSeal"); herald.Enter(0);
        herald.State.Health -= 10; herald.State.Hand = new() { "prayer" }; var health = herald.State.Health;
        Check(herald.Describe(herald.Card("prayer")).Contains("Recover 7 vitality"), "faith healing description includes equipment");
        herald.Play(0); Check(herald.State.Health == health + 7, "faith healing bonus resolves once per matching effect");
        herald.State.Health = 1; health = herald.State.Health; herald.DrinkPotion();
        Check(herald.State.Health == health + content.PotionHealing, "faith equipment does not modify flasks");
        var star = new CampaignSession(content, "starseer", 4); star.State.Cinders = 100; star.Buy("starLens"); star.Enter(0);
        star.State.Hand = new() { "prismLance" }; star.State.EnemyHealth = 24; star.Play(0);
        Check(star.State.EnemyHealth == 6, "magic equipment contributes to each of two damage effects");
        var quick = new CampaignSession(content, "rogue", 2); quick.Enter(0); quick.State.Hand = new() { "quickstep" }; var draw = quick.State.Draw.Count;
        quick.Play(0); Check(quick.State.Hand.Count == 0 && quick.State.Draw.Count == draw && quick.State.Block == 4, "free Quickstep spends hand space without replacing itself");
        Check(new CampaignSession(content, "reaver", 1).MatchingCards(content.Equipment.First(item => item.Id == "starLens")) == 0, "forge relevance excludes cards without matching tagged effects");
        var bad = Content(); bad.Heroes[0].RewardTags = new[] { "missing" }; Reject(bad.Validate, "unknown affinity tag rejected");
        bad = Content(); bad.Heroes[0].RewardTags = new[] { "hero" }; Reject(bad.Validate, "undersized affinity pool rejected");
        bad = Content(); bad.Cards.First(card => card.Id == "cleave").Tags = new[] { "attack", "reaver", "shared" }; Reject(bad.Validate, "shared and affinity overlap rejected");
        bad = Content(); bad.RewardCards = bad.RewardCards.Where(id => !bad.Cards.First(card => card.Id == id).HasTag("shared")).ToArray(); Reject(bad.Validate, "empty shared reward pool rejected");
        var fallback = Content(); fallback.CommonRewardTag = null; foreach (var hero in fallback.Heroes) hero.RewardTags = Array.Empty<string>();
        var original = new CampaignSession(fallback, "reaver", 2); original.Enter(0); original.State.EnemyHealth = 0; original.EndTurn();
        Check(original.State.Rewards.Count == 3 && original.State.Rewards.Distinct().Count() == 3, "definitions without affinity retain the legacy reward mode");
        Console.WriteLine($"Class identity: {count} checks passed");
    }
}
