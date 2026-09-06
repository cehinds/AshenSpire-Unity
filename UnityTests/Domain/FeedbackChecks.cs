using System.Text.Json;
using AshenSpire.Domain;

public static class FeedbackChecks
{
    public static void Run(string root)
    {
        var json = File.ReadAllText(Path.Combine(root, "GameContent/Unity/campaign.json"));
        var options = new JsonSerializerOptions { IncludeFields = true };
        CampaignDefinition Content() => JsonSerializer.Deserialize<CampaignDefinition>(json, options)!;
        var count = 0;
        void Check(bool valid, string name) { if (!valid) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
        void Reject(Action<CampaignDefinition> edit, string name) { var bad = Content(); edit(bad); try { bad.Validate(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
        var content = Content(); content.Validate();
        Check(content.Feedback.ForCard(content.Cards.First(c => c.Id == "venom")).Id == "poison", "poison tag takes precedence over attack presentation");
        Check(content.Feedback.ForCard(content.Cards.First(c => c.Id == "starbolt")).Id == "magic", "magic cards select authored cue");
        Check(content.Feedback.ForCard(content.Cards.First(c => c.Id == "strike")).Id == "attack", "plain strike selects attack cue");
        Reject(c => c.Feedback.Cues[0].MatchTag = "missing", "unknown feedback tag rejected");
        Reject(c => c.Feedback.Cues[0].Poses = new[] { "missing" }, "unknown sprite pose rejected");
        Reject(c => c.Feedback.Cues[0].Id = "attack", "duplicate cue ID rejected");
        Reject(c => c.Feedback.Cues = c.Feedback.Cues.Where(x => x.Id != "hit").ToArray(), "required cue omission rejected");
        Reject(c => c.Feedback.Cues[0].Milliseconds = 0, "zero timeline rejected");
        Reject(c => c.Feedback.Cues[0].Color = "#GGGGGG", "invalid effect color rejected");
        Reject(c => c.Feedback.Cues[0].SoundDuration = float.NaN, "nonfinite audio duration rejected");
        Reject(c => c.Feedback.Cues[0].Noise = 2, "out of range sound mix rejected");
        var run = new CampaignSession(content, "reaver", 3); run.Enter(0);
        run.State.Hand = new() { "strike", "defend" }; run.State.EnemyBlock = 4;
        var before = new FeedbackSnapshot(run.State); run.Play(0); var outcome = before.Compare(run.State);
        Check(outcome.Damage == 2 && outcome.Describe().Contains("FOE -2"), "impact label reports health damage after block");
        before = new FeedbackSnapshot(run.State); run.Play(0); outcome = before.Compare(run.State);
        Check(outcome.Block == 5 && outcome.Damage == 0, "guard label reports actual block gained");
        run.State.Health = run.State.MaxHealth - 2; before = new FeedbackSnapshot(run.State); run.DrinkPotion();
        Check(before.Compare(run.State).Healing == 2, "healing label respects maximum health");
        var state = JsonSerializer.Serialize(run.State, options); new FeedbackSnapshot(run.State).Compare(run.State).Describe();
        foreach (var cue in content.Feedback.Cues)
        {
            var samples = FeedbackSound.Synthesize(cue);
            Check(samples.Length == (int)(22050 * cue.SoundDuration) && samples.All(v => float.IsFinite(v) && Math.Abs(v) <= .6f) && samples.Any(v => Math.Abs(v) > .01f) && samples[0] == 0 && Math.Abs(samples[^1]) < .001f && samples.SequenceEqual(FeedbackSound.Synthesize(cue)), cue.Id + " sound is deterministic, bounded and fades to silence");
        }
        Check(JsonSerializer.Serialize(run.State, options) == state, "feedback and synthesis leave model state and RNG unchanged");
        Console.WriteLine($"Feedback: {count} checks passed");
    }
}
