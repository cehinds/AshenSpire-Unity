// ExpeditionView.cs — touch-first UI Toolkit views composed from shared controls.
// RunController supplies snapshots and handles semantic commands. This view never
// changes health or rewards. Theme: Resources/Expedition.uss; artwork: Resources/Art.
// To add a screen, extend Render's phase switch and test it at 390x844 and landscape.
using System;
using AshenSpire.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class ExpeditionView
    {
        private readonly VisualElement _root;
        private VisualElement _body;
        private Image _player;
        private int _selected = -1;
        public event Action StartRequested;
        public event Action ContinueRequested;
        public event Action EnterRequested;
        public event Action<int> CardRequested;
        public event Action EndTurnRequested;
        public event Action<string> RewardRequested;
        public event Action MenuRequested;

        public ExpeditionView(VisualElement root)
        {
            _root = root;
            _root.AddToClassList("app");
            var theme = Resources.Load<StyleSheet>("Expedition");
            if (theme == null) throw new InvalidOperationException("Resources/Expedition.uss is missing.");
            _root.styleSheets.Add(theme);
        }

        public void ShowTitle(bool canResume, string notice = null)
        {
            Shell("THE GOLDBOUGH EXPEDITION", "A spark survives the fall.");
            var hero = new VisualElement(); hero.AddToClassList("title-art");
            hero.Add(Picture("reaver_idle", "hero")); _body.Add(hero);
            _body.Add(Text("ASHEN SPIRE", "title"));
            _body.Add(Text("Choose your cards. Read their intent.\nCarry your cinders into the next fight.", "lead"));
            _body.Add(Button("Begin expedition", () => StartRequested?.Invoke(), "primary"));
            if (canResume) _body.Add(Button("Continue expedition", () => ContinueRequested?.Invoke()));
            if (!string.IsNullOrEmpty(notice)) _body.Add(Text(notice, "notice"));
            _body.Add(Text("UNITY • EARLY PLAYABLE SLICE\nOne class · three encounters · touch controls", "caption"));
        }

        public void Render(RunSession session)
        {
            _selected = -1;
            var state = session.State;
            Shell("THE GOLDBOUGH EXPEDITION", $"REAVER    •    {state.Cinders} CINDERS");
            var stats = new VisualElement(); stats.AddToClassList("stats");
            stats.Add(Text($"{state.Health}/{state.MaxHealth}  VITALITY", "stat"));
            stats.Add(Text($"{state.Block}  BLOCK", "stat"));
            stats.Add(Button("Menu", () => MenuRequested?.Invoke(), "small")); _body.Add(stats);
            switch (state.Phase)
            {
                case RunPhase.Map: Map(session); break;
                case RunPhase.Combat: Combat(session); break;
                case RunPhase.Reward: Reward(session); break;
                case RunPhase.Victory:
                case RunPhase.Defeat:
                    _body.Add(Picture("reaver_idle", "end-art"));
                    _body.Add(Text(state.Phase == RunPhase.Victory ? "THE EMBER ENDURES" : "YOU PERISHED", "heading"));
                    _body.Add(Text(state.Phase == RunPhase.Victory ? "The path is yours. Return with a different deck." : "The next expedition starts with what you learned.", "lead"));
                    _body.Add(Button("Begin another expedition", () => StartRequested?.Invoke(), "primary"));
                    break;
            }
        }

        private void Map(RunSession session)
        {
            _body.Add(Text("THE ASHEN PATH", "heading"));
            _body.Add(Text("Three trials beneath the Goldbough", "lead"));
            for (var i = 0; i < session.Enemies.Count; i++)
            {
                var enemy = session.Enemies[i];
                var node = new VisualElement(); node.AddToClassList("panel");
                node.Add(Text($"0{i + 1}  /  {(i < session.State.Encounter ? "CLEARED" : i == session.State.Encounter ? "NEXT ENCOUNTER" : "AHEAD")}", "eyebrow"));
                node.Add(Text(enemy.Name, "node-title"));
                node.Add(Text($"{enemy.Health} vitality · {enemy.Reward} cinders", "caption"));
                if (i == session.State.Encounter) node.Add(Button("Enter encounter", () => EnterRequested?.Invoke(), "primary"));
                _body.Add(node);
            }
            _body.Add(Text($"{session.State.Deck.Count} cards in your deck • Progress saves after each action", "caption"));
        }

        private void Combat(RunSession session)
        {
            var state = session.State;
            var stage = new VisualElement(); stage.AddToClassList("stage");
            var ground = Resources.Load<Texture2D>("Art/background");
            if (ground != null) stage.style.backgroundImage = new StyleBackground(ground);
            _player = Picture("reaver_idle", "fighter"); stage.Add(_player);
            var enemy = Picture(session.Enemy.Art, "fighter");
            enemy.AddToClassList("enemy");
            enemy.RegisterCallback<ClickEvent>(_ => { if (_selected >= 0) CardRequested?.Invoke(_selected); });
            stage.Add(enemy); _body.Add(stage);
            _body.Add(Text(session.Enemy.Name.ToUpperInvariant(), "node-title"));
            _body.Add(Text($"{state.EnemyHealth}/{session.Enemy.Health} vitality  •  Intent: attack for {session.Enemy.Damage}", "intent"));
            var summary = new VisualElement(); summary.AddToClassList("stats");
            summary.Add(Text($"TURN {state.Turn}", "eyebrow"));
            summary.Add(Text($"{state.Energy} ENERGY", "energy"));
            _body.Add(summary);
            var help = Text("Select a card, then tap Play or the enemy.", "caption"); _body.Add(help);
            var hand = new VisualElement(); hand.AddToClassList("hand"); _body.Add(hand);
            var play = Button("Select a card", () => { if (_selected >= 0) CardRequested?.Invoke(_selected); }, "primary");
            play.SetEnabled(false);
            for (var i = 0; i < state.Hand.Count; i++)
            {
                var index = i; var card = session.GetCard(state.Hand[i]);
                var control = new Button(); control.AddToClassList("card");
                control.Add(Text(card.Cost.ToString(), "cost"));
                control.Add(Text(card.Name, "card-name"));
                control.Add(Text(card.Description, "card-description"));
                control.Add(Text(string.Join(" · ", card.Tags), "card-tags"));
                control.SetEnabled(card.Cost <= state.Energy);
                control.clicked += () =>
                {
                    _selected = index;
                    foreach (var child in hand.Children()) child.RemoveFromClassList("selected");
                    control.AddToClassList("selected");
                    play.text = "Play " + card.Name; play.SetEnabled(true);
                    help.text = card.Description;
                };
                hand.Add(control);
            }
            var actions = new VisualElement(); actions.AddToClassList("actions");
            actions.Add(play); actions.Add(Button("End turn", () => EndTurnRequested?.Invoke())); _root.Add(actions);
            _body.Add(Text($"DRAW {state.Draw.Count}   /   DISCARD {state.Discard.Count}   /   DECK {state.Deck.Count}", "caption"));
        }

        private void Reward(RunSession session)
        {
            _body.Add(Text("A MOMENT OF RESPITE", "heading"));
            _body.Add(Text($"{session.Enemy.Name} falls.\n{session.Enemy.Reward} cinders collected.", "lead"));
            _body.Add(Text("Choose one reward", "node-title"));
            foreach (var card in session.Cards)
            {
                if (!card.Id.StartsWith("unity.")) continue;
                _body.Add(Button($"{card.Name}  ·  {card.Cost} energy\n{card.Description}", () => RewardRequested?.Invoke(card.Id)));
            }
            _body.Add(Button("Rest at the ember\nRecover 12 vitality", () => RewardRequested?.Invoke(null), "primary"));
        }

        public void PlayAttack()
        {
            if (_player == null || _player.panel == null) return;
            var player = _player;
            player.image = Resources.Load<Texture2D>("Art/reaver_attack");
            player.schedule.Execute(() => { if (player.panel != null) player.image = Resources.Load<Texture2D>("Art/reaver_idle"); }).StartingIn(220);
        }

        private void Shell(string eyebrow, string subtitle)
        {
            _root.Clear(); _player = null;
            var scroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("scroll"); _root.Add(scroll);
            _body = new VisualElement(); _body.AddToClassList("body"); scroll.Add(_body);
            _body.Add(Text(eyebrow, "eyebrow")); _body.Add(Text(subtitle, "subtitle"));
        }

        private static Label Text(string text, string className)
        { var label = new Label(text); label.AddToClassList(className); return label; }
        private static Button Button(string text, Action clicked, string className = null)
        { var button = new Button(clicked) { text = text }; button.AddToClassList("button"); if (className != null) button.AddToClassList(className); return button; }
        private static Image Picture(string name, string className)
        { var image = new Image { image = Resources.Load<Texture2D>("Art/" + name), scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList(className); return image; }
    }
}
