// FeedbackDefinition.cs — presentation records and read-only command outcome snapshots.
// EDIT: campaign.json/Feedback; ordered MatchTag rules select the first matching cue.
// These records never mutate combat, consume RNG, or enter campaign saves.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain
{
    [Serializable]
    public sealed class FeedbackDefinition
    {
        public FeedbackCue[] Cues;
        public FeedbackCue Cue(string id) => Cues.First(cue => cue.Id == id);
        public FeedbackCue ForCard(CardDefinition card) => Cues.FirstOrDefault(cue => !string.IsNullOrEmpty(cue.MatchTag) && card.HasTag(cue.MatchTag)) ?? Cue("guard");
        public void Validate(IEnumerable<string> tags)
        {
            void Require(bool valid, string message) { if (!valid) throw new ArgumentException("campaign.json/Feedback: " + message); }
            Require(Cues != null && Cues.Length <= 32, "one to 32 cues required.");
            var ids = new HashSet<string>();
            foreach (var cue in Cues)
            {
                Require(cue != null && !string.IsNullOrWhiteSpace(cue.Id) && ids.Add(cue.Id), "empty or duplicate cue ID.");
                Require(string.IsNullOrEmpty(cue.MatchTag) || tags.Contains(cue.MatchTag), cue.Id + ": unknown MatchTag.");
                Require(cue.Milliseconds >= 200 && cue.Milliseconds <= 1600 && cue.Distance >= 0 && cue.Distance <= 40, cue.Id + ": motion range.");
                Require(cue.Poses != null && cue.Poses.Length > 0 && cue.Poses.Length <= 12 && cue.Poses.All(pose => new[] { "idle", "attack1", "attack2", "guard", "hit" }.Contains(pose)), cue.Id + ": unknown pose.");
                Require(cue.Color != null && cue.Color.Length == 7 && cue.Color[0] == '#' && cue.Color.Skip(1).All(Uri.IsHexDigit), cue.Id + ": use #RRGGBB color.");
                Require(cue.Frequency >= 40 && cue.Frequency <= 2000 && cue.EndFrequency >= 40 && cue.EndFrequency <= 2000 && cue.SoundDuration >= .05f && cue.SoundDuration <= .8f && cue.Noise >= 0 && cue.Noise <= 1, cue.Id + ": sound range.");
            }
            Require(new[] { "attack", "guard", "hit", "heal", "reward" }.All(ids.Contains), "attack, guard, hit, heal and reward cues required.");
        }
    }
    [Serializable]
    public sealed class FeedbackCue
    {
        public string Id; public string MatchTag; public string[] Poses;
        public int Milliseconds = 600; public float Distance = 18; public string Color = "#F5C27A";
        public float Frequency = 180; public float EndFrequency = 90; public float SoundDuration = .2f; public float Noise = .1f;
    }
    public sealed class FeedbackSnapshot
    {
        private readonly int _health, _enemyHealth, _block, _poison;
        public FeedbackSnapshot(CampaignState state) { _health = state.Health; _enemyHealth = state.EnemyHealth; _block = state.Block; _poison = state.EnemyPoison; }
        public FeedbackOutcome Compare(CampaignState state) => new FeedbackOutcome
        {
            Damage = Math.Max(0, _enemyHealth - state.EnemyHealth),
            Hurt = Math.Max(0, _health - state.Health),
            Healing = Math.Max(0, state.Health - _health),
            Block = Math.Max(0, state.Block - _block),
            Poison = Math.Max(0, state.EnemyPoison - _poison)
        };
    }
    public sealed class FeedbackOutcome
    {
        public int Damage, Hurt, Healing, Block, Poison;
        public string Action;
        public string Describe()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Action)) parts.Add(Action);
            if (Damage > 0) parts.Add("FOE -" + Damage);
            if (Hurt > 0) parts.Add("VITALITY -" + Hurt);
            if (Healing > 0) parts.Add("VITALITY +" + Healing);
            if (Block > 0) parts.Add("BLOCK +" + Block);
            if (Poison > 0) parts.Add("POISON +" + Poison);
            return string.Join("  ·  ", parts);
        }
    }
    // Shared by the Unity audio adapter and .NET validation; independent from game RNG.
    public static class FeedbackSound
    {
        public static float[] Synthesize(FeedbackCue cue, int rate = 22050)
        {
            var samples = new float[(int)(rate * cue.SoundDuration)];
            uint noise = 2166136261;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = (double)i / rate;
                var phase = 2 * Math.PI * (cue.Frequency * t + (cue.EndFrequency - cue.Frequency) * t * t / (2 * cue.SoundDuration));
                noise = unchecked(noise * 1664525 + 1013904223);
                var hiss = (noise / (double)uint.MaxValue) * 2 - 1;
                var envelope = Math.Min(1, t / .008) * Math.Pow(1 - (double)i / samples.Length, 3);
                var tone = (Math.Sin(phase) + .25 * Math.Sin(phase * 2.01)) / 1.25;
                samples[i] = (float)((tone * (1 - cue.Noise) + hiss * cue.Noise) * envelope * .6);
            }
            return samples;
        }
    }
}
