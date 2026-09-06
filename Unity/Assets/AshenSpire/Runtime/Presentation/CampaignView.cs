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
    public sealed class CampaignView : IDisposable
    {
        private readonly VisualElement _root;
        private readonly bool _diagnostics;
        private bool _disposed;
        private IVisualElementScheduledItem _controlReport;
        private int _controlReportAttempts;
        private VisualElement _body;
        private ScrollView _scroll;
        private Image _player;
        private Image _enemy;
        private string _heroArt;
        private int _selected = -1;
        private bool _reducedMotion;
        private bool _fast;
        private bool _muted;
        private int _displayHeight = 900;
        private CampaignSession _session;
        private readonly CombatFeedback _feedback = new CombatFeedback();
        private VisualElement _stage;
        private VisualElement _interruptionCover;
        private Button _returnButton;
        private readonly List<VisualElement> _interruptionDisabled = new List<VisualElement>();
        public event Action ReturnRequested;
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
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }
        private void OnGeometryChanged(GeometryChangedEvent change)
        {
            Report();
        }
        public void SetDisplayHeight(int height)
        {
            _displayHeight = Math.Max(1, height);
            RefreshTouchTargets();
        }
        private void RefreshTouchTargets()
        {
            var minimum = ViewportLayout.MinimumTouchHeight(_displayHeight);
            foreach (var button in _root.Query<Button>().ToList())
                button.style.minHeight = Mathf.Max(button.ClassListContains("card") ? 132 : 50, minimum);
            foreach (var toggle in _root.Query<Toggle>().ToList())
                toggle.style.minHeight = Mathf.Max(52, minimum);
            foreach (var field in _root.Query<TextField>().ToList())
                field.style.minHeight = Mathf.Max(field.ClassListContains("report-field") ? 240 : 52, minimum);
        }
        public void Dispose() { _disposed = true; _controlReport?.Pause(); _feedback.Dispose(); _root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged); }
        public void ShowInterruption(bool canReturn)
        {
            _feedback.Cancel();
            if (_interruptionCover == null)
            {
                // Disable siblings (including fixed combat actions) without rebuilding:
                // selection, scroll offset and unfinished input stay in their own views.
                (_root.focusController?.focusedElement as VisualElement)?.Blur();
                foreach (var child in _root.Children().ToList())
                {
                    if (!child.enabledSelf) continue;
                    _interruptionDisabled.Add(child);
                    child.SetEnabled(false);
                }
                _interruptionCover = new ScrollView(ScrollViewMode.Vertical);
                _interruptionCover.AddToClassList("interruption-cover");
                var panel = new VisualElement();
                panel.AddToClassList("interruption-panel");
                panel.Add(Text("THE EMBER CAN WAIT", "eyebrow"));
                panel.Add(Text("Take your time.", "title"));
                panel.Add(Text("Your place is waiting. Return when you are ready.", "lead"));
                _returnButton = Control("return-to-game", "Return to the game", () => ReturnRequested?.Invoke(), "primary");
                panel.Add(_returnButton);
                _interruptionCover.Add(panel);
                _root.Add(_interruptionCover);
            }
            _returnButton.SetEnabled(canReturn);
            if (canReturn) _returnButton.Focus();
            Report();
        }
        public void HideInterruption()
        {
            _interruptionCover?.RemoveFromHierarchy();
            _interruptionCover = null;
            _returnButton = null;
            foreach (var child in _interruptionDisabled) child.SetEnabled(true);
            _interruptionDisabled.Clear();
            Report();
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
                panel.Add(Text("Starting deck · " + string.Join(", ", hero.Deck.GroupBy(id => id).Select(group => group.Count() + " × " + content.Cards.First(card => card.Id == group.Key).Name)), "caption"));
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
            _stage = stage;
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
            _body.Add(Text(session.Enemy.Name + " · " + s.EnemyHealth + " / " + session.Enemy.Health + "\nEnemy block · " + s.EnemyBlock, "node-title"));
            var intent = session.Intent;
            AddButton("intent-details", "INTENT: " + intent.Operation.ToUpperInvariant() + " " + (intent.Operation == "attack" ? session.AttackIntent : intent.Amount) + " · Tap to explain", () => Details(session, "ENEMY INTENT", session.DescribeIntent()), "intent-control");
            _body.Add(Text("TURN " + s.Turn + "   ·   " + s.Energy + " ENERGY   ·   " + s.Block + " BLOCK", "energy"));
            AddButton("status-details", "Statuses · poison " + s.Poison + " / " + s.EnemyPoison + " · strength " + s.Strength + " · enemy weak " + s.Weak, () => Details(session, "COMBAT STATUSES", session.DescribeStatuses()), "small");
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
                if (card.Cost > s.Energy) control.AddToClassList("unaffordable");
                control.clicked += () =>
                {
                    var cancel = _selected == index;
                    foreach (var child in hand.Children()) child.RemoveFromClassList("selected");
                    _selected = cancel ? -1 : index;
                    if (!cancel) control.AddToClassList("selected");
                    play.text = cancel ? "Select a card" : card.Cost > s.Energy ? "Need " + card.Cost + " energy" : "Play " + card.Name;
                    play.SetEnabled(!cancel && card.Cost <= s.Energy);
                    help.text = cancel ? session.LastAction : card.Name + " · " + card.Cost + " energy\n" + session.Describe(card) + (card.Cost > s.Energy ? "\nNot enough energy. Tap again to cancel." : "\nTap again to cancel, or Play to confirm.");
                    Report();
                };
                hand.Add(control);
            }
            _body.Add(help);
            var piles = new VisualElement();
            piles.AddToClassList("inspection-row");
            piles.Add(Control("draw-pile", "Draw · " + s.Draw.Count, () => Pile(session, true), "small"));
            piles.Add(Control("discard-pile", "Discard · " + s.Discard.Count, () => Pile(session, false), "small"));
            _body.Add(piles);
            AddButton("action-history", "Recent actions", () => Details(session, "RECENT ACTIONS", session.RecentActions.Count == 0 ? "No actions recorded in this session. History starts again after reloading; your expedition state is preserved." : "Newest first · this session only\n\n" + string.Join("\n\n", session.RecentActions.Reverse())), "small");
            var flask = AddButton("potion", "Crimson flask · " + s.Potions + " left · heal " + session.Content.PotionHealing, () => PotionRequested?.Invoke());
            flask.SetEnabled(s.Potions > 0 && s.Health < s.MaxHealth);
            var actions = new VisualElement();
            actions.AddToClassList("actions");
            actions.Add(play);
            actions.Add(Control("end-turn", "End turn", () => EndTurnRequested?.Invoke()));
            _root.Add(actions);
        }
        private void Details(CampaignSession session, string title, string description)
        {
            Shell(title, "Read-only · your turn waits for you");
            _body.Add(Text(description, "detail-copy"));
            InspectionReturn(session);
        }
        private void Pile(CampaignSession session, bool draw)
        {
            var cards = draw ? session.State.Draw : session.State.Discard;
            Shell(draw ? "DRAW PILE" : "DISCARD PILE", cards.Count + " cards · grouped by name, not draw order");
            _body.Add(Text(draw ? "Draw order stays hidden. When this pile empties, discards shuffle into it as more cards are drawn." : "Played cards and unplayed end-of-turn cards go here. They shuffle into the draw pile when it empties.", "detail-copy"));
            if (cards.Count == 0) _body.Add(Text("This pile is empty.", "node-title"));
            foreach (var group in cards.GroupBy(id => id).OrderBy(group => session.Card(group.Key).Name, StringComparer.Ordinal).ThenBy(group => group.Key, StringComparer.Ordinal))
            {
                var card = session.Card(group.Key);
                var panel = new VisualElement();
                panel.AddToClassList("panel");
                panel.Add(Text(group.Count() + " × " + card.Name + " · " + card.Cost + " energy", "node-title"));
                panel.Add(Text(session.Describe(card), "detail-copy"));
                _body.Add(panel);
            }
            InspectionReturn(session);
        }
        private void InspectionReturn(CampaignSession session)
        {
            var actions = new VisualElement();
            actions.AddToClassList("actions");
            actions.Add(Control("inspection-back", "Return to combat", () => Render(session), "primary"));
            _root.Add(actions);
            Report();
        }
        private void Reward(CampaignSession session)
        {
            _body.Add(Text("VICTORY · " + session.Enemy.Name, "heading"));
            _body.Add(Text("+" + session.Enemy.Reward + " cinders. Choose a card or recover vitality.", "lead"));
            foreach (var id in session.State.Rewards)
            {
                var card = session.Card(id);
                _body.Add(Text(session.RewardCategory(card) + " card · " + session.State.Deck.Count(owned => owned == id) + " already in deck", "caption"));
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
                var relevance = item.Operation == "health" ? (session.Hero.Tags.Contains(item.RequiredTag) ? "Applies to your wanderer." : "No benefit for this wanderer.") : "Supports " + session.MatchingCards(item) + " of " + session.State.Deck.Count + " cards in your deck.";
                var control = AddButton("buy-" + item.Id, item.Name + " · " + (owned ? "EQUIPPED" : item.Price + " cinders") + "\n" + item.Description + "\n" + relevance, () => BuyRequested?.Invoke(item.Id));
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
            motion.name = "reduced-motion";
            motion.AddToClassList("setting");
            fast.name = "fast-motion";
            fast.AddToClassList("setting");
            _body.Add(motion);
            _body.Add(fast);
            var mute = new Toggle("Mute sound") { value = _muted };
            mute.name = "mute-sound";
            mute.AddToClassList("setting");
            mute.RegisterValueChangedCallback(e => { _muted = e.newValue; MuteRequested?.Invoke(e.newValue); Report(); });
            _body.Add(mute);
            motion.RegisterValueChangedCallback(e => { _reducedMotion = e.newValue; SettingsRequested?.Invoke(_reducedMotion, _fast); Report(); });
            fast.RegisterValueChangedCallback(e => { _fast = e.newValue; SettingsRequested?.Invoke(_reducedMotion, _fast); Report(); });
            _body.Add(Text("HOW TO PLAY", "heading"));
            _body.Add(Text("Tap a card, then Play or the enemy. Energy refills each turn. Block expires when your next turn begins.\n\nRead the next intent: attack hurts, guard blocks, charge increases future attacks, poison hurts every turn. Poison bypasses block and decays. Weak reduces enemy attacks by 3. Strength boosts every damage effect this battle.\n\nUse flasks before you fall. Between battles, buy equipment, remove unwanted cards, or rest. Deck and equipment persist for this run.\n\nScroll to see all cards on smaller screens. No hover or keyboard is required.", "lead"));
            AddButton("back", "Back", back, "primary");
            Report();
        }
        private void Diagnostics(CampaignSession session)
        {
            Shell("EXPEDITION INSPECTOR", "Development only · read-only state");
            _body.Add(Text("Seed " + session.State.Seed + " · RNG " + session.State.RandomState + "\nEncounter " + session.Encounter.Id + " · Phase " + session.State.Phase + "\nHero tags: " + string.Join(", ", session.Hero.Tags), "lead"));
            var report = new TextField("Bug report preview") { multiline = true, value = "AshenSpire " + UnityEngine.Application.version + " / Unity " + UnityEngine.Application.unityVersion + "\nSteps: describe what you did\nExpected: \nActual: \nState: " + JsonUtility.ToJson(session.State), isReadOnly = false };
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
        public void Feedback(FeedbackCue cue, FeedbackOutcome outcome, bool enemyTurn = false)
        {
            _feedback.Play(_stage ?? _root, _player, _enemy, _heroArt, cue, outcome, enemyTurn, _reducedMotion, _fast, _diagnostics);
        }
        private void Shell(string title, string subtitle)
        {
            _feedback.Cancel();
            _stage = null;
            _root.Clear();
            _player = null;
            _enemy = null;
            _scroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Hidden };
            _scroll.AddToClassList("scroll");
            _root.Add(_scroll);
            _scroll.verticalScroller.valueChanged += _ => Report(false);
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
        private Button Control(string id, string label, Action clicked, string style = null)
        {
            var result = new Button(clicked) { text = label, name = id };
            result.AddToClassList("button");
            if (style != null)
                result.AddToClassList(style);
            result.style.minHeight = Mathf.Max(50, ViewportLayout.MinimumTouchHeight(_displayHeight));
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
            public ControlBounds[] Controls; public float PanelWidth, PanelHeight; public string[] Labels;
            public int LayoutAttempts;
        }
        private void Report(bool refreshTouchTargets = true)
        {
            // Screen construction and geometry changes both refresh controls, even when
            // diagnostics are disabled in a release/native player. No per-frame queries.
            if (refreshTouchTargets) RefreshTouchTargets();
            if (!_diagnostics || _disposed)
                return;
            // Coalesce changes and keep a bounded measurement pending until child
            // layout settles. A cover can lay out without changing root geometry.
            _controlReport?.Pause();
            _controlReportAttempts = 0;
            _controlReport = _root.schedule.Execute(TryReportControls).Every(180).StartingIn(180);
        }
        private void TryReportControls()
        {
            if (_disposed || ReportControls())
            {
                _controlReport?.Pause();
                return;
            }
            if (++_controlReportAttempts < 20) return;
            _controlReport?.Pause();
            Debug.LogError("Control diagnostics did not obtain finite layout after 20 measurements.");
        }
        private bool ReportControls()
        {
            var surface = _interruptionCover ?? _root;
            var controls = surface.Query<Button>().ToList().Cast<VisualElement>()
                .Concat(surface.Query<TextField>().ToList()).Concat(surface.Query<Toggle>().ToList())
                .Where(x => !string.IsNullOrEmpty(x.name))
                .Select(x => new ControlBounds { Id = x.name, X = x.worldBound.x, Y = x.worldBound.y,
                    Width = x.worldBound.width, Height = x.worldBound.height, Enabled = x.enabledInHierarchy }).ToArray();
            var width = _root.resolvedStyle.width;
            var height = _root.resolvedStyle.height;
            // Rotation and detached elements can expose unmeasured bounds. Never
            // export those bounds; the pending report will measure the next layout.
            if (!Finite(width) || !Finite(height) || controls.Any(x =>
                !Finite(x.X) || !Finite(x.Y) || !Finite(x.Width) || !Finite(x.Height))) return false;
            Debug.Log("ASHENSPIRE_CONTROLS " + JsonUtility.ToJson(new ControlList {
                Controls = controls, PanelWidth = width, PanelHeight = height,
                LayoutAttempts = _controlReportAttempts + 1,
                Labels = surface.Query<Label>().ToList().Select(label => label.text).ToArray() }));
            return true;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
