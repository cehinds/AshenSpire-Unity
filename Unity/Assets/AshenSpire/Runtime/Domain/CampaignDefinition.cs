// CampaignDefinition.cs — authoritative campaign records, separate from runtime state.
// Author GameContent/Unity/campaign.json; import through the AshenSpire editor menu.
// Stable IDs join records. Tags classify capabilities; they are never Unity object tags.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain
{
    [Serializable]
    public sealed class CampaignDefinition
    {
        public int SchemaVersion = 1;
        public int Energy = 3;
        public int HandSize = 5;
        public int RestHealing = 16;
        public int PotionHealing = 20;
        public int StartingPotions = 3;
        public SoundDefinition Audio = new SoundDefinition();
        public TagDefinition[] Tags;
        public CardDefinition[] Cards;
        public HeroDefinition[] Heroes;
        public FoeDefinition[] Foes;
        public EncounterDefinition[] Encounters;
        public EquipmentDefinition[] Equipment;
        public string[] RewardCards;

        public void Validate()
        {
            void Require(bool condition, string field)
            {
                if (!condition)
                    throw new ArgumentException("campaign.json/" + field);
            }
            Require(SchemaVersion == 1 && Energy > 0 && HandSize > 0 && HandSize <= 10 && RestHealing >= 0 && PotionHealing > 0 && StartingPotions >= 0, "Settings: invalid version or range.");
            Require(Audio != null && Audio.Volume >= 0 && Audio.Volume <= 1 && Audio.Duration >= 0.05f && Audio.Duration <= 2 && Audio.AttackFrequency > 0 && Audio.GuardFrequency > 0 && Audio.HitFrequency > 0 && Audio.RewardFrequency > 0, "Audio: invalid volume, duration or frequency.");
            Require(Tags != null && Cards != null && Heroes != null && Foes != null && Encounters != null && Equipment != null && RewardCards != null, "Collections: required.");
            HashSet<string> Ids(IEnumerable<string> values, string field)
            {
                var result = new HashSet<string>();
                foreach (var id in values)
                    Require(!string.IsNullOrWhiteSpace(id) && result.Add(id), field + "/" + id + ": empty or duplicate ID.");
                return result;
            }
            var tags = Ids(Tags.Select(x => x.Id), "Tags");
            foreach (var tag in Tags)
                Require(!string.IsNullOrWhiteSpace(tag.Domain) && !string.IsNullOrWhiteSpace(tag.Family), "Tags/" + tag.Id + ": Domain and Family required.");
            void CheckTags(string[] values, string field)
            {
                Require(values != null, field + ": required.");
                foreach (var tag in values)
                    Require(tags.Contains(tag), field + ": unknown tag " + tag);
            }
            var cards = Ids(Cards.Select(x => x.Id), "Cards");
            foreach (var card in Cards)
            {
                Require(!string.IsNullOrWhiteSpace(card.Name) && card.Cost >= 0 && card.Cost <= Energy && card.Effects != null && card.Effects.Length > 0, "Cards/" + card.Id + ": invalid name, cost or effects.");
                CheckTags(card.Tags, "Cards/" + card.Id + "/Tags");
                foreach (var effect in card.Effects)
                    Require(effect.Amount >= 0 && CampaignSession.Supports(effect.Operation), "Cards/" + card.Id + "/Effects: unknown operation or negative amount " + effect.Operation);
            }
            Ids(Heroes.Select(x => x.Id), "Heroes");
            Require(Heroes.Length > 0, "Heroes: at least one required.");
            foreach (var hero in Heroes)
            {
                Require(hero.Health > 0 && !string.IsNullOrWhiteSpace(hero.Art) && hero.Deck != null && hero.Deck.Length >= HandSize, "Heroes/" + hero.Id + ": health, art or deck invalid.");
                foreach (var id in hero.Deck)
                    Require(cards.Contains(id), "Heroes/" + hero.Id + "/Deck: missing card " + id);
                CheckTags(hero.Tags, "Heroes/" + hero.Id + "/Tags");
            }
            var foes = Ids(Foes.Select(x => x.Id), "Foes");
            foreach (var foe in Foes)
            {
                Require(foe.Health > 0 && foe.Reward >= 0 && !string.IsNullOrWhiteSpace(foe.Art) && foe.Intents != null && foe.Intents.Length > 0, "Foes/" + foe.Id + ": invalid health, art or intent pattern.");
                CheckTags(foe.Tags, "Foes/" + foe.Id + "/Tags");
                foreach (var intent in foe.Intents)
                    Require(intent.Amount >= 0 && new[] { "attack", "guard", "charge", "poison" }.Contains(intent.Operation), "Foes/" + foe.Id + "/Intents: unsupported intent.");
            }
            Ids(Encounters.Select(x => x.Id), "Encounters");
            Require(Encounters.Length > 0, "Encounters: required.");
            foreach (var encounter in Encounters)
            {
                Require(encounter.Act >= 1 && encounter.Options != null && encounter.Options.Length > 0 && !string.IsNullOrWhiteSpace(encounter.Background), "Encounters/" + encounter.Id + ": invalid act, background or options.");
                foreach (var id in encounter.Options)
                    Require(foes.Contains(id), "Encounters/" + encounter.Id + ": missing foe " + id);
            }
            Ids(Equipment.Select(x => x.Id), "Equipment");
            foreach (var item in Equipment)
            {
                Require(item.Price >= 0 && item.Amount >= 0 && new[] { "damage", "block", "health" }.Contains(item.Operation), "Equipment/" + item.Id + ": invalid price or operation.");
                CheckTags(item.Tags, "Equipment/" + item.Id + "/Tags");
                Require(tags.Contains(item.RequiredTag), "Equipment/" + item.Id + "/RequiredTag: unknown tag.");
            }
            Require(RewardCards.Length >= 3 && RewardCards.Distinct().Count() == RewardCards.Length, "RewardCards: at least three unique IDs required.");
            foreach (var id in RewardCards)
                Require(cards.Contains(id), "RewardCards: missing card " + id);
        }
    }
    [Serializable]
    public sealed class TagDefinition
    {
        public string Id; public string Domain; public string Family;
    }
    [Serializable]
    public sealed class SoundDefinition
    {
        public float Volume = 0.3f; public float Duration = 0.16f; public float AttackFrequency = 150; public float GuardFrequency = 360; public float HitFrequency = 90; public float RewardFrequency = 620;
    }
    [Serializable]
    public sealed class HeroDefinition
    {
        public string Id; public string Name; public string Description; public string Art; public int Health; public string[] Deck; public string[] Tags;
    }
    [Serializable]
    public sealed class FoeDefinition
    {
        public string Id; public string Name; public string Art; public int Health; public int Reward; public string[] Tags; public EffectDefinition[] Intents;
    }
    [Serializable]
    public sealed class EncounterDefinition
    {
        public string Id; public string Name; public int Act; public string Background; public string[] Options;
    }
    [Serializable]
    public sealed class EquipmentDefinition
    {
        public string Id; public string Name; public string Description; public string Art; public string Operation; public int Amount; public int Price; public string RequiredTag; public string[] Tags;
    }
}
