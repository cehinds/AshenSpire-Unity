// CampaignView.cs — reusable touch controls render campaign snapshots and emit commands.
// No game rules or save writes belong here. Content uses Resources/Art and Expedition.uss.
// Named buttons emit read-only geometry in development, allowing real pointer tests.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class CampaignView
    {
        private readonly VisualElement _root;
        private readonly bool _diagnostics;
        private VisualElement _body;
        private ScrollView _scroll;
        private Image _player;
        private Image _enemy;
        private string _heroArt;
        private int _selected = -1;
        private bool _reducedMotion;
        private bool _fast;
        private bool _muted;
        private CampaignSession _session;
        public event Action<string, uint> StartRequested;
        public event Action ContinueRequested, EndTurnRequested, PotionRequested, RestRequested, MenuRequested;
        public event Action<int> EnterRequested, CardRequested, RemoveRequested;
        public event Action<string> RewardRequested, BuyRequested;
        public event Action<bool, bool> SettingsRequested;
        public event Action<bool> MuteRequested;
        public CampaignView(VisualElement root, bool diagnostics, bool reducedMotion, bool fast, bool muted)
        {
            _root = root;
            _diagnostics = diagnostics;
            _reducedMotion = reducedMotion;
            _fast = fast;
            _muted = muted;
            root.AddToClassList("app");
            root.styleSheets.Add(Resources.Load<StyleSheet>("Expedition"));
        }
        public void Title(CampaignDefinition content, bool canResume, string notice = null)
        {
            Shell("ASHEN SPIRE", "THREE ACTS · ONE EMBER · YOUR PATH");
            var art = Picture("reaver_idle", "campaign-title-art");
            _body.Add(art);
            _body.Add(Text("THE EMBER ENDURES", "title"));
            _body.Add(Text("Read their intent. Shape your deck.\nReach the heart of the Spire.", "lead"));
            if (canResume)
                AddButton("continue", "Continue expedition", () => ContinueRequested?.Invoke(), "primary");
            AddButton("new", "Choose your wanderer", () => Heroes(content), "primary");
            AddButton("settings", "Settings & how to play", () => Settings(() => Title(content, canResume)));
            if (_diagnostics)
                AddButton("gallery", "Component gallery", () => Gallery(() => Title(content, canResume)));
            if (notice != null)
                _body.Add(Text(notice, "notice"));
            _body.Add(Text("Nine encounters · four classes · seeded expeditions\nProgress saves after every command.", "caption"));
            Report();
        }
        private void Heroes(CampaignDefinition content)
        {
            Shell("CHOOSE YOUR WANDERER", "Each loadout changes how you survive.");
            var seed = new TextField("Expedition seed (optional)") { name = "seed" };
            seed.AddToClassList("seed-field");
            _body.Add(seed);
            foreach (var hero in content.Heroes)
            {
                var panel = new VisualElement();
                panel.AddToClassList("panel");
                var portrait = Picture(hero.Art + "_idle", "portrait");
                panel.Add(portrait);
                panel.Add(Text(hero.Name + " · " + hero.Health + " vitality", "node-title"));
                panel.Add(Text(hero.Description, "lead"));
                var selectedHero = hero;
                panel.Add(Control("hero-" + hero.Id, "Begin as " + hero.Name, () =>
                {
                    uint value;
                    if (string.IsNullOrWhiteSpace(seed.value))
                        value = (uint)DateTime.UtcNow.Ticks;
                    else if (!uint.TryParse(seed.value, out value))
                    {
                        seed.label = "Seed must be a whole number from 0 to 4294967295";
                        return;
                    }
                    StartRequested?.Invoke(selectedHero.Id, value);
                }, "primary"));
                _body.Add(panel);
            }
            AddButton("back", "Back", () => MenuRequested?.Invoke());
            Report();
        }
        public void Render(CampaignSession session)
        {
            _session = session;
            _selected = -1;
            _heroArt = session.Hero.Art;
            var s = session.State;
            Shell("ASHEN SPIRE", session.Hero.Name.ToUpperInvariant() + " · ACT " + session.Encounter.Act + " · " + s.Cinders + " CINDERS");
            var stats = new VisualElement();
            stats.AddToClassList("stats");
            stats.Add(Text(s.Health + " / " + s.MaxHealth + " VITALITY", "stat"));
            stats.Add(Control("deck", "Deck · " + s.Deck.Count, () => Deck(session), "small"));
            stats.Add(Control("menu", "Menu", () => MenuRequested?.Invoke(), "small"));
            _body.Add(stats);
            if (s.Phase == RunPhase.Map)
                Map(session);
            else if (s.Phase == RunPhase.Combat)
                Combat(session);
            else if (s.Phase == RunPhase.Reward)
                Reward(session);
            else
                End(session);
            Report();
        }
        private void Map(CampaignSession session)
        {
            var s = session.State;
            _body.Add(Text(session.Encounter.Name.ToUpperInvariant(), "heading"));
            _body.Add(Text("Encounter " + (s.Encounter + 1) + " of " + session.Content.Encounters.Length + " · " + s.FoesDefeated + " defeated", "caption"));
            _body.Add(Text(string.Join("  ", Enumerable.Range(0, session.Content.Encounters.Length).Select(i => i < s.FoesDefeated ? "◆" : "◇")), "energy"));
            _body.Add(Text("Choose your route", "node-title"));
            for (var i = 0; i < session.Encounter.Options.Length; i++)
            {
                var route = i;
                var foe = session.GetFoe(session.Encounter.Options[i]);
                var panel = new VisualElement();
                panel.AddToClassList("panel");
                panel.Add(Text(foe.Name, "node-title"));
                panel.Add(Text(foe.Health + " vitality · " + foe.Reward + " cinders · " + string.Join(" / ", foe.Intents.Select(x => x.Operation + " " + x.Amount)), "caption"));
                panel.Add(Control("enter-" + i, "Face " + foe.Name, () => EnterRequested?.Invoke(route), "primary"));
                _body.Add(panel);
            }
            AddButton("shop", "Visit the forge · equipment", () => Shop(session));
            var rest = AddButton("rest", "Rest · 15 cinders · recover " + session.Content.RestHealing, () => RestRequested?.Invoke());
            rest.SetEnabled(!s.Rested && s.Cinders >= 15 && s.Health < s.MaxHealth);
            _body.Add(Text("Equipment stays with this expedition. Rest once per stop.\nYour route and every reward are saved.", "caption"));
            if (_diagnostics)
                AddButton("diagnostics", "Inspect expedition", () => Diagnostics(session));
        }
        private void Combat(CampaignSession session)
        {
            var s = session.State;
            var stage = new VisualElement();
            stage.AddToClassList("stage");
            stage.AddToClassList("campaign-stage");
            stage.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("Art/" + session.Encounter.Background));
            _player = Picture(_heroArt + "_idle", "fighter");
            _enemy = Picture(session.Enemy.Art, "fighter");
            _enemy.AddToClassList("enemy");
            _enemy.RegisterCallback<ClickEvent>(_ => { if (_selected >= 0) CardRequested?.Invoke(_selected); });
            stage.Add(_player);
            stage.Add(_enemy);
            _body.Add(stage);
            _body.Add(Text(session.Enemy.Name + " · " + s.EnemyHealth + " / " + session.Enemy.Health, "node-title"));
            var intent = session.Intent;
            _body.Add(Text("INTENT: " + intent.Operation.ToUpperInvariant() + " " + (intent.Operation == "attack" ? session.AttackIntent : intent.Amount) + "    |    ENEMY BLOCK " + s.EnemyBlock, "intent"));
            _body.Add(Text("TURN " + s.Turn + "   ·   " + s.Energy + " ENERGY   ·   " + s.Block + " BLOCK", "energy"));
            _body.Add(Text("Poison: you " + s.Poison + " / foe " + s.EnemyPoison + "   ·   Strength " + s.Strength + "   ·   Weak turns " + s.Weak, "caption"));
            var hand = new VisualElement();
            hand.AddToClassList("hand");
            _body.Add(hand);
            var help = Text(session.LastAction, "notice");
            var play = Control("play", "Select a card", () => { if (_selected >= 0) CardRequested?.Invoke(_selected); }, "primary");
            play.SetEnabled(false);
            for (var i = 0; i < s.Hand.Count; i++)
            {
                var index = i;
                var card = session.Card(s.Hand[i]);
                var control = new Button { name = "card-" + i };
                control.AddToClassList("card");
                control.Add(Text(card.Cost + " ◇", "cost"));
                control.Add(Text(card.Name, "card-name"));
                control.Add(Text(session.Describe(card), "card-description"));
                control.Add(Text(string.Join(" · ", card.Tags), "card-tags"));
                control.SetEnabled(card.Cost <= s.Energy);
                control.clicked += () =>
                {
                    var cancel = _selected == index;
                    foreach (var child in hand.Children()) child.RemoveFromClassList("selected");
                    _selected = cancel ? -1 : index;
                    if (!cancel) control.AddToClassList("selected");
                    play.text = cancel ? "Select a card" : "Play " + card.Name;
                    play.SetEnabled(!cancel); help.text = cancel ? session.LastAction : session.Describe(card); Report();
                };
                hand.Add(control);
            }
            _body.Add(help);
            _body.Add(Text("Draw " + s.Draw.Count + " · Discard " + s.Discard.Count, "caption"));
            var flask = AddButton("potion", "Crimson flask · " + s.Potions + " left · heal " + session.Content.PotionHealing, () => PotionRequested?.Invoke());
            flask.SetEnabled(s.Potions > 0 && s.Health < s.MaxHealth);
            var actions = new VisualElement();
            actions.AddToClassList("actions");
            actions.Add(play);
            actions.Add(Control("end-turn", "End turn", () => EndTurnRequested?.Invoke()));
            _root.Add(actions);
        }
        private void Reward(CampaignSession session)
        {
            _body.Add(Text("VICTORY · " + session.Enemy.Name, "heading"));
            _body.Add(Text("+" + session.Enemy.Reward + " cinders. Choose a card or recover vitality.", "lead"));
            foreach (var id in session.State.Rewards)
            {
                var card = session.Card(id);
                AddButton("reward-" + id, card.Name + " · " + card.Cost + " energy\n" + session.Describe(card), () => RewardRequested?.Invoke(id));
            }
            AddButton("reward-rest", "Rest · recover " + session.Content.RestHealing + " vitality", () => RewardRequested?.Invoke(null), "primary");
        }
        private void End(CampaignSession session)
        {
            var won = session.State.Phase == RunPhase.Victory;
            _body.Add(Picture(_heroArt + "_idle", "end-art"));
            _body.Add(Text(won ? "THE EMBER ENDURES" : "YOU PERISHED", "heading"));
            _body.Add(Text(won ? "The Wyrm Lord falls. Dawn returns to the Spire.\nYour expedition is complete." : "The Spire keeps its secrets. A different deck, a wiser route—try again.", "lead"));
            _body.Add(Text(session.State.FoesDefeated + " foes defeated · " + session.State.Cinders + " cinders\nSeed " + session.State.Seed + " · " + session.State.Deck.Count + " cards · " + session.State.Items.Count + " equipment", "caption"));
            AddButton("retry", "Choose another wanderer", () => Heroes(session.Content), "primary");
            AddButton("menu", "Main menu", () => MenuRequested?.Invoke());
        }
        private void Shop(CampaignSession session)
        {
            Shell("THE EMBER FORGE", session.State.Cinders + " cinders · purchased gear equips immediately");
            foreach (var item in session.Content.Equipment)
            {
                var owned = session.State.Items.Contains(item.Id);
                var control = AddButton("buy-" + item.Id, item.Name + " · " + (owned ? "EQUIPPED" : item.Price + " cinders") + "\n" + item.Description, () => BuyRequested?.Invoke(item.Id));
                control.SetEnabled(!owned && item.Price <= session.State.Cinders);
            }
            AddButton("back", "Return to the path", () => Render(session), "primary");
            Report();
        }
        private void Deck(CampaignSession session)
        {
            Shell("YOUR DECK", session.State.Deck.Count + " cards · " + session.State.Cinders + " cinders");
            _body.Add(Text("At a map stop, spend 25 cinders to remove a card. Keep at least five.", "caption"));
            for (var i = 0; i < session.State.Deck.Count; i++)
            {
                var index = i;
                var card = session.Card(session.State.Deck[i]);
                var control = AddButton("remove-" + i, card.Name + " · " + card.Cost + " energy\n" + session.Describe(card), () => RemoveRequested?.Invoke(index));
                control.SetEnabled(session.State.Phase == RunPhase.Map && session.State.Cinders >= 25 && session.State.Deck.Count > session.Content.HandSize);
            }
            _body.Add(Text("Equipped: " + (session.State.Items.Count == 0 ? "none" : string.Join(", ", session.State.Items.Select(id => session.Content.Equipment.First(x => x.Id == id).Name))), "lead"));
            AddButton("back", "Return", () => Render(session), "primary");
            Report();
        }
        private void Settings(Action back)
        {
            Shell("MAKE IT YOURS", "Changes save on this device.");
            var motion = new Toggle("Reduced motion") { value = _reducedMotion };
            var fast = new Toggle("Quick animations") { value = _fast };
            motion.AddToClassList("setting");
            fast.AddToClassList("setting");
            _body.Add(motion);
            _body.Add(fast);
            var mute = new Toggle("Mute sound") { value = _muted };
            mute.AddToClassList("setting");
            mute.RegisterValueChangedCallback(e => { _muted = e.newValue; MuteRequested?.Invoke(e.newValue); });
            _body.Add(mute);
            motion.RegisterValueChangedCallback(e => { _reducedMotion = e.newValue; SettingsRequested?.Invoke(_reducedMotion, _fast); });
            fast.RegisterValueChangedCallback(e => { _fast = e.newValue; SettingsRequested?.Invoke(_reducedMotion, _fast); });
            _body.Add(Text("HOW TO PLAY", "heading"));
            _body.Add(Text("Tap a card, then Play or the enemy. Energy refills each turn. Block expires when your next turn begins.\n\nRead the next intent: attack hurts, guard blocks, charge increases future attacks, poison hurts every turn. Poison bypasses block and decays. Weak reduces enemy attacks by 3. Strength boosts every damage effect this battle.\n\nUse flasks before you fall. Between battles, buy equipment, remove unwanted cards, or rest. Deck and equipment persist for this run.\n\nScroll to see all cards on smaller screens. No hover or keyboard is required.", "lead"));
            AddButton("back", "Back", back, "primary");
            Report();
        }
        private void Diagnostics(CampaignSession session)
        {
            Shell("EXPEDITION INSPECTOR", "Development only · read-only state");
            _body.Add(Text("Seed " + session.State.Seed + " · RNG " + session.State.RandomState + "\nEncounter " + session.Encounter.Id + " · Phase " + session.State.Phase + "\nHero tags: " + string.Join(", ", session.Hero.Tags), "lead"));
            var report = new TextField("Bug report preview") { multiline = true, value = "AshenSpire 0.2.0 / Unity " + UnityEngine.Application.unityVersion + "\nSteps: describe what you did\nExpected: \nActual: \nState: " + JsonUtility.ToJson(session.State), isReadOnly = false };
            report.AddToClassList("report-field");
            _body.Add(report);
            AddButton("copy-report", "Copy report", () => { GUIUtility.systemCopyBuffer = report.value; });
            AddButton("back", "Return", () => Render(session), "primary");
            Report();
        }
        private void Gallery(Action back)
        {
            Shell("COMPONENT GALLERY", "Production controls · development only");
            _body.Add(Text("Heading and body text", "heading"));
            _body.Add(Text("Readable descriptions wrap within the phone width.", "lead"));
            AddButton("sample-normal", "Normal button", () => { });
            AddButton("sample-primary", "Primary action", () => { }, "primary");
            AddButton("sample-disabled", "Unavailable action", () => { }).SetEnabled(false);
            _body.Add(Text("Validation error: unknown card ID. Fix the source record and import again.", "notice"));
            _body.Add(Text("Empty state: no equipment owned.", "caption"));
            AddButton("back", "Back", back);
            Report();
        }
        public void Animate(string pose, bool hitEnemy = false)
        {
            if (_reducedMotion || _player == null || _player.panel == null)
                return;
            var player = _player;
            var enemy = _enemy;
            var art = _heroArt;
            var duration = _fast ? 90 : 180;
            player.image = Resources.Load<Texture2D>("Art/" + art + "_" + pose);
            player.style.translate = new Translate(pose.StartsWith("attack") ? 10 : -4, 0);
            if (hitEnemy && enemy != null)
                enemy.tintColor = new Color(1, 0.55f, 0.35f);
            if (pose.StartsWith("attack"))
                player.schedule.Execute(() => { if (player.panel != null) player.image = Resources.Load<Texture2D>("Art/" + art + "_attack2"); }).StartingIn(duration);
            for (var step = 0; step <= 8; step++)
            {
                var frame = step;
                player.schedule.Execute(() => { if(player.panel!=null) player.style.translate = new Translate(Mathf.Sin(frame / 8f * Mathf.PI) * (hitEnemy ? 16 : -6), 0); }).StartingIn(duration * 2 * frame / 8);
            }
            player.schedule.Execute(() => { if (player.panel == null) return; player.image = Resources.Load<Texture2D>("Art/" + art + "_idle"); player.style.translate = new Translate(0, 0); if (enemy != null) enemy.tintColor = Color.white; }).StartingIn(duration * 2);
        }
        private void Shell(string title, string subtitle)
        {
            _root.Clear();
            _player = null;
            _enemy = null;
            _scroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Hidden };
            _scroll.AddToClassList("scroll");
            _root.Add(_scroll);
            _scroll.verticalScroller.valueChanged += _ => Report();
            _body = new VisualElement();
            _body.AddToClassList("body");
            _scroll.Add(_body);
            _body.Add(Text(title, "eyebrow"));
            _body.Add(Text(subtitle, "subtitle"));
        }
        private Button AddButton(string id, string label, Action clicked, string style = null)
        {
            var button = Control(id, label, clicked, style);
            _body.Add(button);
            return button;
        }
        private static Button Control(string id, string label, Action clicked, string style = null)
        {
            var result = new Button(clicked) { text = label, name = id };
            result.AddToClassList("button");
            if (style != null)
                result.AddToClassList(style);
            return result;
        }
        private static Label Text(string value, string style)
        {
            var label = new Label(value);
            label.AddToClassList(style);
            return label;
        }
        private static Image Picture(string name, string style)
        {
            var image = new Image { image = Resources.Load<Texture2D>("Art/" + name), scaleMode = ScaleMode.ScaleToFit };
            image.AddToClassList(style);
            return image;
        }
        [Serializable]
        private sealed class ControlBounds
        {
            public string Id; public float X, Y, Width, Height; public bool Enabled;
        }
        [Serializable]
        private sealed class ControlList
        {
            public ControlBounds[] Controls; public float PanelWidth, PanelHeight;
        }
        private void Report()
        {
            if (!_diagnostics)
                return;
            _root.schedule.Execute(() => { var controls = _root.Query<Button>().ToList().Where(x => !string.IsNullOrEmpty(x.name)).Select(x => new ControlBounds { Id = x.name, X = x.worldBound.x, Y = x.worldBound.y, Width = x.worldBound.width, Height = x.worldBound.height, Enabled = x.enabledInHierarchy }).ToArray(); Debug.Log("ASHENSPIRE_CONTROLS " + JsonUtility.ToJson(new ControlList { Controls = controls, PanelWidth = _root.resolvedStyle.width, PanelHeight = _root.resolvedStyle.height })); }).StartingIn(180);
        }
    }
}
