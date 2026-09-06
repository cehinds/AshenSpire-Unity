// CampaignSession.cs — deterministic campaign commands; no Unity, file or timing access.
// Definitions describe content. Saved state owns piles, statuses, choices and purchases.
// Add a capability to the effect registry, then author cards that compose it; never
// branch on a hero/card ID. Equipment queries card or hero tags for its contribution.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain
{
    [Serializable]
    public sealed class CampaignState
    {
        public int SchemaVersion = 1;
        public string HeroId;
        public uint Seed;
        public uint RandomState;
        public int Health, MaxHealth, Energy, Block, EnemyHealth, EnemyBlock, EnemyPoison, Poison, Weak, Strength, EnemyStrength;
        public int Encounter, Route, Turn, Cinders, Potions, FoesDefeated;
        public bool Rested;
        public RunPhase Phase;
        public List<string> Deck = new List<string>(), Draw = new List<string>(), Hand = new List<string>(), Discard = new List<string>(), Items = new List<string>(), Rewards = new List<string>();
    }
    public sealed class CampaignSession
    {
        private readonly CampaignDefinition _content;
        private readonly Dictionary<string, CardDefinition> _cards;
        private readonly Dictionary<string, Action<int>> _effects;
        private CardDefinition _playing;
        // Feedback is session-local, bounded, and never part of deterministic saved state.
        private readonly List<string> _recentActions = new List<string>();
        public IReadOnlyList<string> RecentActions => _recentActions.AsReadOnly();
        public CampaignState State
        {
            get;
        }
        public CampaignDefinition Content => _content;
        public HeroDefinition Hero => _content.Heroes.First(x => x.Id == State.HeroId);
        public EncounterDefinition Encounter => _content.Encounters[State.Encounter];
        public FoeDefinition Enemy => GetFoe(Encounter.Options[State.Route]);
        public EffectDefinition Intent => Enemy.Intents[(Math.Max(1, State.Turn) - 1) % Enemy.Intents.Length];
        public event Action Changed;
        public string LastAction { get; private set; } = "Your expedition begins.";
        private static readonly string[] Operations = { "damage", "block", "draw", "poison", "weak", "strength", "heal", "energy" };
        public static bool Supports(string operation) => Operations.Contains(operation);
        public CardDefinition Card(string id) => _cards.TryGetValue(id, out var card) ? card : throw new ArgumentException("Unknown card: " + id);
        public FoeDefinition GetFoe(string id) => _content.Foes.First(x => x.Id == id);
        public string Describe(CardDefinition card)
        {
            return string.Join(". ", card.Effects.Select(effect =>
            {
                var amount = effect.Amount;
                if (effect.Operation == "damage")
                    amount += (State.Phase == RunPhase.Combat ? State.Strength : 0) + Bonus("damage", card.Tags);
                if (effect.Operation == "block")
                    amount += Bonus("block", card.Tags);
                switch (effect.Operation)
                {
                    case "damage":
                        return "Deal " + amount + " damage";
                    case "block":
                        return "Gain " + amount + " block";
                    case "draw":
                        return "Draw " + amount;
                    case "poison":
                        return "Apply " + amount + " poison";
                    case "weak":
                        return "Weaken for " + amount + " turns";
                    case "strength":
                        return "Gain " + amount + " strength";
                    case "heal":
                        return "Recover " + amount + " vitality";
                    case "energy":
                        return "Gain " + amount + " energy";
                    default:
                        throw new ArgumentException("Unsupported description operation.");
                }
            })) + ".";
        }
        public CampaignSession(CampaignDefinition content, string heroId, uint seed) : this(content, Create(content, heroId, seed)) { }
        private static CampaignState Create(CampaignDefinition content, string heroId, uint seed)
        {
            content.Validate();
            var hero = content.Heroes.FirstOrDefault(x => x.Id == heroId) ?? throw new ArgumentException("Unknown hero: " + heroId);
            return new CampaignState { HeroId = heroId, Seed = seed == 0 ? 1u : seed, RandomState = seed == 0 ? 1u : seed, Health = hero.Health, MaxHealth = hero.Health, Potions = content.StartingPotions, Deck = new List<string>(hero.Deck), Phase = RunPhase.Map };
        }
        public CampaignSession(CampaignDefinition content, CampaignState state)
        {
            content.Validate();
            _content = content;
            _cards = content.Cards.ToDictionary(x => x.Id);
            State = state;
            ValidateSave();
            _effects = new Dictionary<string, Action<int>>
            {
                ["damage"] = amount => Damage(amount + State.Strength + Bonus("damage", _playing.Tags)),
                ["block"] = amount => State.Block += amount + Bonus("block", _playing.Tags),
                ["draw"] = DrawCards,
                ["poison"] = amount => State.EnemyPoison += amount,
                ["weak"] = amount => State.Weak += amount,
                ["strength"] = amount => State.Strength += amount,
                ["heal"] = amount => State.Health = Math.Min(State.MaxHealth, State.Health + amount),
                ["energy"] = amount => State.Energy = Math.Min(20, State.Energy + amount)
            };
        }
        private void ValidateSave()
        {
            var s = State;
            if (s == null || s.SchemaVersion != 1 || s.RandomState == 0 || !_content.Heroes.Any(x => x.Id == s.HeroId) || s.Encounter < 0 || s.Encounter >= _content.Encounters.Length || s.Route < 0 || s.Route >= _content.Encounters[s.Encounter].Options.Length || !Enum.IsDefined(typeof(RunPhase), s.Phase) || s.MaxHealth < 1 || s.Health < 0 || s.Health > s.MaxHealth || s.Energy < 0 || s.Energy > 20 || s.Potions < 0 || s.Cinders < 0 || s.Turn < 0 || s.EnemyHealth < 0 || s.Block < 0 || s.EnemyBlock < 0 || s.Poison < 0 || s.EnemyPoison < 0 || s.Weak < 0 || s.Strength < 0 || s.EnemyStrength < 0 || s.Deck == null || s.Draw == null || s.Hand == null || s.Discard == null || s.Items == null || s.Rewards == null)
                throw new ArgumentException("Campaign save: invalid version, references or resource values. Original save preserved.");
            foreach (var id in s.Deck.Concat(s.Draw).Concat(s.Hand).Concat(s.Discard).Concat(s.Rewards))
                Card(id);
            if (s.Items.Distinct().Count() != s.Items.Count || s.Items.Any(id => !_content.Equipment.Any(x => x.Id == id)))
                throw new ArgumentException("Campaign save: unknown or duplicate equipment.");
            if (s.Hand.Count > 10 || s.Deck.Count < _content.HandSize)
                throw new ArgumentException("Campaign save: invalid deck or hand size.");
        }
        public bool Enter(int route)
        {
            if (State.Phase != RunPhase.Map || route < 0 || route >= Encounter.Options.Length)
                return false;
            State.Route = route;
            State.EnemyHealth = Enemy.Health;
            State.EnemyBlock = State.EnemyPoison = State.Poison = State.Weak = State.Strength = State.EnemyStrength = 0;
            State.Draw = new List<string>(State.Deck);
            State.Hand.Clear();
            State.Discard.Clear();
            State.Rewards.Clear();
            Shuffle(State.Draw);
            State.Turn = 0;
            State.Phase = RunPhase.Combat;
            BeginTurn();
            Notify("Facing " + Enemy.Name + ".");
            return true;
        }
        public bool Play(int index)
        {
            if (State.Phase != RunPhase.Combat || index < 0 || index >= State.Hand.Count)
                return false;
            var card = Card(State.Hand[index]);
            if (card.Cost > State.Energy)
                return false;
            _playing = card;
            State.Energy -= card.Cost;
            State.Hand.RemoveAt(index);
            var results = new List<string>();
            foreach (var effect in card.Effects)
            {
                var health = State.Health;
                var enemyHealth = State.EnemyHealth;
                var enemyBlock = State.EnemyBlock;
                var block = State.Block;
                var hand = State.Hand.Count;
                var energy = State.Energy;
                _effects[effect.Operation](effect.Amount);
                switch (effect.Operation)
                {
                    case "damage": results.Add((enemyHealth - State.EnemyHealth) + " damage dealt; " + (enemyBlock - State.EnemyBlock) + " blocked"); break;
                    case "block": results.Add((State.Block - block) + " block gained"); break;
                    case "draw": results.Add((State.Hand.Count - hand) + " cards drawn"); break;
                    case "heal": results.Add((State.Health - health) + " vitality restored"); break;
                    case "energy": results.Add((State.Energy - energy) + " energy gained"); break;
                    case "poison": results.Add(effect.Amount + " enemy poison added"); break;
                    case "weak": results.Add(effect.Amount + " enemy weak turns added"); break;
                    case "strength": results.Add(effect.Amount + " strength gained"); break;
                }
            }
            State.Discard.Add(card.Id);
            CheckVictory();
            Notify(card.Name + " · " + card.Cost + " energy spent. " + string.Join(". ", results) + ".");
            return true;
        }
        public int AttackIntent => Math.Max(0, Intent.Amount + State.EnemyStrength - (State.Weak > 0 ? 3 : 0));
        public string DescribeIntent()
        {
            var intent = Intent;
            switch (intent.Operation)
            {
                case "attack": return Enemy.Name + " will attack for " + AttackIntent + ". Your " + State.Block + " block absorbs up to that amount; currently " + Math.Max(0, AttackIntent - State.Block) + " damage gets through. Enemy poison resolves first and can prevent this action.";
                case "guard": return Enemy.Name + " will gain " + intent.Amount + " block, lasting through your next turn. Direct damage consumes block; poison bypasses it.";
                case "charge": return Enemy.Name + " will gain " + intent.Amount + " strength. Strength increases each later attack in this fight; charging itself deals no direct damage.";
                case "poison": return Enemy.Name + " will add " + intent.Amount + " poison to you. Your poison then deals damage immediately, bypassing block, and decreases by one.";
                default: throw new ArgumentException("Unsupported intent.");
            }
        }
        public string DescribeStatuses() =>
            "YOUR BLOCK · " + State.Block + "\nAbsorbs direct enemy attack damage. Resets when your next turn begins.\n\n" +
            "ENEMY BLOCK · " + State.EnemyBlock + "\nAbsorbs direct card damage. Expires before the enemy's next action.\n\n" +
            "YOUR POISON · " + State.Poison + "\nDeals damage after the enemy acts, bypassing block; then decreases by one. New poison from the enemy also ticks immediately.\n\n" +
            "ENEMY POISON · " + State.EnemyPoison + "\nDeals damage before the enemy acts, bypassing block; then decreases by one. A lethal tick prevents retaliation.\n\n" +
            "YOUR STRENGTH · " + State.Strength + "\nAdds to each damage effect on your cards for this battle. Card descriptions include this and applicable equipment bonuses.\n\n" +
            "ENEMY STRENGTH · " + State.EnemyStrength + "\nAdds to enemy attacks for this battle. The displayed intent includes it.\n\n" +
            "ENEMY WEAK · " + State.Weak + " turns\nReduces enemy attack damage by 3, to a minimum of zero. Decreases after every enemy action, including non-attacks.";
        public bool EndTurn()
        {
            if (State.Phase != RunPhase.Combat)
                return false;
            var turn = State.Turn;
            var enemyPoisonDamage = Math.Min(State.EnemyHealth, State.EnemyPoison);
            State.EnemyHealth = Math.Max(0, State.EnemyHealth - State.EnemyPoison);
            if (State.EnemyPoison > 0)
                State.EnemyPoison--;
            if (CheckVictory())
            {
                Notify("Turn " + turn + " · Enemy poison deals " + enemyPoisonDamage + ", defeating " + Enemy.Name + " before it acts.");
                return true;
            }
            State.EnemyBlock = 0;
            var intent = Intent;
            var action = "";
            switch (intent.Operation)
            {
                case "attack":
                    var damage = Math.Max(0, AttackIntent - State.Block);
                    State.Health = Math.Max(0, State.Health - damage);
                    action = Enemy.Name + " attacks for " + AttackIntent + ": " + Math.Min(State.Block, AttackIntent) + " blocked, " + damage + " damage gets through.";
                    break;
                case "guard":
                    State.EnemyBlock = intent.Amount;
                    action = Enemy.Name + " gains " + intent.Amount + " block.";
                    break;
                case "charge":
                    State.EnemyStrength += intent.Amount;
                    action = Enemy.Name + " gains " + intent.Amount + " strength (now " + State.EnemyStrength + ").";
                    break;
                case "poison":
                    State.Poison += intent.Amount;
                    action = Enemy.Name + " adds " + intent.Amount + " poison to you.";
                    break;
            }
            if (State.Weak > 0)
                State.Weak--;
            var playerPoisonDamage = Math.Min(State.Health, State.Poison);
            State.Health = Math.Max(0, State.Health - State.Poison);
            if (State.Poison > 0)
                State.Poison--;
            if (State.Health == 0)
                State.Phase = RunPhase.Defeat;
            else
            {
                State.Discard.AddRange(State.Hand);
                State.Hand.Clear();
                BeginTurn();
            }
            Notify("Turn " + turn + " · " + (enemyPoisonDamage > 0 ? "Enemy poison deals " + enemyPoisonDamage + ". " : "") + action + (playerPoisonDamage > 0 ? " Your poison deals " + playerPoisonDamage + " vitality damage, bypassing block." : ""));
            return true;
        }
        private bool CheckVictory()
        {
            if (State.EnemyHealth > 0)
                return false;
            State.Phase = RunPhase.Reward;
            State.Cinders += Enemy.Reward;
            State.FoesDefeated++;
            var choices = new List<string>(_content.RewardCards);
            Shuffle(choices);
            State.Rewards = choices.Take(3).ToList();
            return true;
        }
        public bool Reward(string id)
        {
            if (State.Phase != RunPhase.Reward || (id != null && !State.Rewards.Contains(id)))
                return false;
            if (id == null)
                State.Health = Math.Min(State.MaxHealth, State.Health + _content.RestHealing);
            else
                State.Deck.Add(id);
            State.Rewards.Clear();
            State.Rested = false;
            if (State.Encounter == _content.Encounters.Length - 1)
                State.Phase = RunPhase.Victory;
            else
            {
                State.Encounter++;
                State.Route = 0;
                State.Phase = RunPhase.Map;
            }
            Notify(id == null ? "You rest beside the ember." : Card(id).Name + " joins your deck.");
            return true;
        }
        public bool Buy(string id)
        {
            var item = _content.Equipment.FirstOrDefault(x => x.Id == id);
            if (State.Phase != RunPhase.Map || item == null || State.Items.Contains(id) || item.Price > State.Cinders)
                return false;
            State.Cinders -= item.Price;
            State.Items.Add(id);
            if (item.Operation == "health" && Hero.Tags.Contains(item.RequiredTag))
            {
                State.MaxHealth += item.Amount;
                State.Health += item.Amount;
            }
            Notify("Equipped " + item.Name + ".");
            return true;
        }
        public bool Rest()
        {
            if (State.Phase != RunPhase.Map || State.Rested || State.Cinders < 15 || State.Health == State.MaxHealth)
                return false;
            State.Cinders -= 15;
            State.Rested = true;
            State.Health = Math.Min(State.MaxHealth, State.Health + _content.RestHealing);
            Notify("Restored " + _content.RestHealing + " vitality.");
            return true;
        }
        public bool RemoveCard(int index)
        {
            if (State.Phase != RunPhase.Map || State.Cinders < 25 || State.Deck.Count <= _content.HandSize || index < 0 || index >= State.Deck.Count)
                return false;
            State.Cinders -= 25;
            var name = Card(State.Deck[index]).Name;
            State.Deck.RemoveAt(index);
            Notify("Forgot " + name + ".");
            return true;
        }
        public bool DrinkPotion()
        {
            if (State.Phase != RunPhase.Combat || State.Potions < 1 || State.Health == State.MaxHealth)
                return false;
            State.Potions--;
            var restored = Math.Min(State.MaxHealth - State.Health, _content.PotionHealing);
            State.Health = Math.Min(State.MaxHealth, State.Health + _content.PotionHealing);
            Notify("Crimson flask restores " + restored + " vitality.");
            return true;
        }
        private int Bonus(string operation, string[] tags) => _content.Equipment.Where(x => State.Items.Contains(x.Id) && x.Operation == operation && tags.Contains(x.RequiredTag)).Sum(x => x.Amount);
        private void Damage(int amount)
        {
            var absorbed = Math.Min(State.EnemyBlock, amount);
            State.EnemyBlock -= absorbed;
            State.EnemyHealth = Math.Max(0, State.EnemyHealth - (amount - absorbed));
        }
        private void BeginTurn()
        {
            State.Turn++;
            State.Energy = _content.Energy;
            State.Block = 0;
            DrawCards(_content.HandSize);
        }
        private void DrawCards(int amount)
        {
            for (var i = 0; i < amount && State.Hand.Count < 10; i++)
            {
                if (State.Draw.Count == 0)
                {
                    State.Draw.AddRange(State.Discard);
                    State.Discard.Clear();
                    Shuffle(State.Draw);
                }
                if (State.Draw.Count == 0)
                    return;
                var last = State.Draw.Count - 1;
                State.Hand.Add(State.Draw[last]);
                State.Draw.RemoveAt(last);
            }
        }
        private void Shuffle(List<string> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var x = State.RandomState;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                State.RandomState = x;
                var j = (int)(x % (uint)(i + 1));
                var temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }
        private void Notify(string message)
        {
            LastAction = message;
            _recentActions.Add(message);
            if (_recentActions.Count > 12)
                _recentActions.RemoveAt(0);
            Changed?.Invoke();
        }
    }
}
