// ContentDefinition.cs — plain C# authoring records for the first Unity slice.
// Edit GameContent/Unity/expedition.json, then use AshenSpire > Import Content.
// These definitions contain tuning only. RunSession owns all changing values.
using System;
using System.Collections.Generic;

namespace AshenSpire.Domain
{
    [Serializable]
    public sealed class ContentDefinition
    {
        public int SchemaVersion = 1;
        public int StartingHealth = 60;
        public int EnergyPerTurn = 3;
        public int HandSize = 5;
        public string[] Tags;
        public string[] StartingDeck;
        public CardDefinition[] Cards;
        public EnemyDefinition[] Enemies;

        public void Validate()
        {
            if (SchemaVersion != 1) throw new ArgumentException("SchemaVersion: expected 1.");
            if (StartingHealth < 1 || EnergyPerTurn < 1 || HandSize < 1 || HandSize > 10)
                throw new ArgumentException("StartingHealth/EnergyPerTurn/HandSize: values are outside supported ranges.");
            if (Tags == null || Cards == null || Enemies == null || StartingDeck == null || Enemies.Length == 0)
                throw new ArgumentException("Tags, Cards, Enemies and StartingDeck are required.");
            var tags = new HashSet<string>(Tags);
            if (tags.Count != Tags.Length) throw new ArgumentException("Tags: duplicate ID.");
            var ids = new HashSet<string>();
            foreach (var card in Cards)
            {
                if (string.IsNullOrWhiteSpace(card.Id) || !ids.Add(card.Id)) throw new ArgumentException($"Cards/{card.Id}: missing or duplicate Id.");
                if (card.Cost < 0 || card.Cost > EnergyPerTurn || card.Effects == null || card.Tags == null)
                    throw new ArgumentException($"Cards/{card.Id}: invalid Cost, Effects or Tags.");
                foreach (var tag in card.Tags) if (!tags.Contains(tag)) throw new ArgumentException($"Cards/{card.Id}/Tags: unknown '{tag}'.");
                foreach (var effect in card.Effects)
                    if (effect.Amount < 0 || (effect.Operation != "damage" && effect.Operation != "block" && effect.Operation != "draw"))
                        throw new ArgumentException($"Cards/{card.Id}/Effects: unsupported '{effect.Operation}' or negative Amount.");
            }
            if (StartingDeck.Length < HandSize) throw new ArgumentException("StartingDeck: must contain at least HandSize cards.");
            foreach (var id in StartingDeck) if (!ids.Contains(id)) throw new ArgumentException($"StartingDeck: unknown card '{id}'.");
            ids.Clear();
            foreach (var enemy in Enemies)
            {
                if (string.IsNullOrWhiteSpace(enemy.Id) || !ids.Add(enemy.Id) || enemy.Health < 1 || enemy.Damage < 0 || enemy.Reward < 0)
                    throw new ArgumentException($"Enemies/{enemy.Id}: duplicate Id or invalid numeric value.");
                if (enemy.Tags == null) throw new ArgumentException($"Enemies/{enemy.Id}/Tags: required.");
                foreach (var tag in enemy.Tags) if (!tags.Contains(tag)) throw new ArgumentException($"Enemies/{enemy.Id}/Tags: unknown '{tag}'.");
            }
        }
    }

    [Serializable]
    public sealed class CardDefinition
    {
        public string Id;
        public string Name;
        public string Description;
        public int Cost;
        public string[] Tags;
        public EffectDefinition[] Effects;
        public bool HasTag(string tag) => Array.IndexOf(Tags, tag) >= 0;
    }

    [Serializable]
    public sealed class EffectDefinition { public string Operation; public int Amount; }

    [Serializable]
    public sealed class EnemyDefinition
    {
        public string Id;
        public string Name;
        public string Art;
        public int Health;
        public int Damage;
        public int Reward;
        public string[] Tags;
    }
}
