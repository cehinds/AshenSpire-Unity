// RunSession.cs — deterministic card combat and three-encounter expedition.
// No Unity objects or timers: commands resolve immediately; visuals observe results.
// Add effect primitives in Apply; ordinary cards belong in GameContent/Unity/expedition.json.
// Verify: dotnet run --project UnityTests/Domain. Seed and draw piles are saved, so resume
// restores the same next draw instead of reseeding System.Random.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain
{
    public enum RunPhase { Map, Combat, Reward, Victory, Defeat }

    [Serializable]
    public sealed class RunState
    {
        public int SchemaVersion = 1;
        public uint RandomState;
        public int Health;
        public int MaxHealth;
        public int Energy;
        public int Block;
        public int EnemyHealth;
        public int Encounter;
        public int Cinders;
        public int Turn;
        public RunPhase Phase;
        public List<string> Deck = new List<string>();
        public List<string> Draw = new List<string>();
        public List<string> Hand = new List<string>();
        public List<string> Discard = new List<string>();
    }

    public sealed class RunSession
    {
        private readonly ContentDefinition _content;
        public RunState State { get; }
        public EnemyDefinition Enemy => _content.Enemies[State.Encounter];
        public IReadOnlyList<EnemyDefinition> Enemies => _content.Enemies;
        public IReadOnlyList<CardDefinition> Cards => _content.Cards;
        public event Action Changed;

        public RunSession(ContentDefinition content, uint seed = 1024)
        {
            content.Validate();
            _content = content;
            State = new RunState { Health = content.StartingHealth, MaxHealth = content.StartingHealth,
                RandomState = seed == 0 ? 1u : seed, Deck = new List<string>(content.StartingDeck), Phase = RunPhase.Map };
        }

        public RunSession(ContentDefinition content, RunState saved)
        {
            content.Validate();
            _content = content;
            if (saved == null || saved.SchemaVersion != 1 || saved.Encounter < 0 || saved.Encounter >= content.Enemies.Length ||
                saved.MaxHealth < 1 || saved.Health < 0 || saved.Health > saved.MaxHealth || saved.RandomState == 0 ||
                !Enum.IsDefined(typeof(RunPhase), saved.Phase) || saved.Deck == null || saved.Draw == null || saved.Hand == null || saved.Discard == null)
                throw new ArgumentException("Saved expedition has an invalid version or state; the original save was preserved.");
            foreach (var id in saved.Deck.Concat(saved.Draw).Concat(saved.Hand).Concat(saved.Discard)) GetCard(id);
            if (saved.Energy < 0 || saved.Energy > content.EnergyPerTurn || saved.Block < 0 || saved.EnemyHealth < 0)
                throw new ArgumentException("Saved combat contains invalid resources.");
            State = saved;
        }

        public CardDefinition GetCard(string id) => _content.Cards.FirstOrDefault(c => c.Id == id)
            ?? throw new ArgumentException($"Unknown card '{id}'.");

        public bool EnterEncounter()
        {
            if (State.Phase != RunPhase.Map) return false;
            State.Phase = RunPhase.Combat;
            State.EnemyHealth = Enemy.Health;
            State.Draw = new List<string>(State.Deck);
            State.Hand.Clear(); State.Discard.Clear();
            Shuffle(State.Draw); State.Turn = 0;
            BeginTurn(); Changed?.Invoke(); return true;
        }

        public bool PlayCard(int index)
        {
            if (State.Phase != RunPhase.Combat || index < 0 || index >= State.Hand.Count) return false;
            var card = GetCard(State.Hand[index]);
            if (card.Cost > State.Energy) return false;
            State.Energy -= card.Cost;
            State.Hand.RemoveAt(index);
            // Played cards enter discard after effects, so a draw effect cannot redraw itself.
            foreach (var effect in card.Effects) Apply(effect);
            State.Discard.Add(card.Id);
            if (State.EnemyHealth == 0) { State.Phase = RunPhase.Reward; State.Cinders += Enemy.Reward; }
            Changed?.Invoke(); return true;
        }

        public bool EndTurn()
        {
            if (State.Phase != RunPhase.Combat) return false;
            State.Health = Math.Max(0, State.Health - Math.Max(0, Enemy.Damage - State.Block));
            if (State.Health == 0) State.Phase = RunPhase.Defeat;
            else { State.Discard.AddRange(State.Hand); State.Hand.Clear(); BeginTurn(); }
            Changed?.Invoke(); return true;
        }

        public bool ClaimReward(string cardId)
        {
            if (State.Phase != RunPhase.Reward) return false;
            if (cardId != null) State.Deck.Add(GetCard(cardId).Id);
            else State.Health = Math.Min(State.MaxHealth, State.Health + 12);
            if (State.Encounter == _content.Enemies.Length - 1) State.Phase = RunPhase.Victory;
            else { State.Encounter++; State.Phase = RunPhase.Map; }
            Changed?.Invoke(); return true;
        }

        private void BeginTurn()
        { State.Turn++; State.Energy = _content.EnergyPerTurn; State.Block = 0; DrawCards(_content.HandSize); }

        private void Apply(EffectDefinition effect)
        {
            switch (effect.Operation)
            {
                case "damage": State.EnemyHealth = Math.Max(0, State.EnemyHealth - effect.Amount); break;
                case "block": State.Block += effect.Amount; break;
                case "draw": DrawCards(effect.Amount); break;
            }
        }

        private void DrawCards(int count)
        {
            for (var i = 0; i < count && State.Hand.Count < 10; i++)
            {
                if (State.Draw.Count == 0) { State.Draw.AddRange(State.Discard); State.Discard.Clear(); Shuffle(State.Draw); }
                if (State.Draw.Count == 0) return;
                var last = State.Draw.Count - 1; State.Hand.Add(State.Draw[last]); State.Draw.RemoveAt(last);
            }
        }

        private void Shuffle(List<string> cards)
        {
            for (var i = cards.Count - 1; i > 0; i--)
            {
                var x = State.RandomState; x ^= x << 13; x ^= x >> 17; x ^= x << 5; State.RandomState = x;
                var j = (int)(x % (uint)(i + 1)); var temp = cards[i]; cards[i] = cards[j]; cards[j] = temp;
            }
        }
    }
}
