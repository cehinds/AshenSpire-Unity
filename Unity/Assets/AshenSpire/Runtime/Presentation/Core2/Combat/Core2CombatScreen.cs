// Core2CombatScreen.cs — the Core 2 combat screen in UI Toolkit (ICore2FightScreen), after the JS combat screen at the pin
// (src/ui/screens/combat.js, build 1854). It only paints Core2CombatController.Model() and turns input into the
// controller's commands; sequencing, words and numbers are the plain C# view models next to it (Core2CombatViewModel,
// Core2CombatBeats, Core2CombatController), which UnityTests/Core2CombatView checks.
// LAYOUT: one fixed full-viewport root (`c2c-root`), the JS bands — top HUD, battlefield, hand, tools row, footer — with
// absolute geometry from Resources/Core2Ui/Core2CombatTokens.uss; `c2c-narrow` below a 700 px wide panel (the narrow
// PanelSettings is 430 px wide, the wide one 1200), as the JS layout decider switches at 520 CSS px.
// REGISTRATION: Core2Screens.FightScreenFactory is set before the first scene loads, unless the host set its own.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    /// <summary>What the run host may hand the combat screen before mounting it (all optional).</summary>
    public sealed class Core2CombatHostOptions
    {
        /// <summary>The player's combat settings (JS keys: autoCombat, combatSpeed, autoCombatSpeed, combatStepPause,
        /// animationStepDuration, combatPacingPreset, oneTapPlay; AnimSpeed; ReducedMotion). Defaults are the JS defaults.</summary>
        public Core2CombatSettings Settings { get; set; } = new Core2CombatSettings();
        /// <summary>The menu door (JS Quick menu). Null hides the button.</summary>
        public Action OpenMenu { get; set; }
        /// <summary>The Armoury door (read-only in a fight). Null hides the button.</summary>
        public Action OpenArmoury { get; set; }
        /// <summary>The log's name colours from Settings (combatLogPlayerColor / combatLogEnemyColor, #rrggbb); null reads the defaults.</summary>
        public string LogPlayerColor { get; set; }
        public string LogEnemyColor { get; set; }
    }

    public sealed partial class Core2CombatScreen : ICore2FightScreen
    {
        public const string CardArtResource = "Core2Ui/card-art";
        public static Core2CombatHostOptions Options { get; } = new Core2CombatHostOptions();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (Core2Screens.FightScreenFactory == null) Core2Screens.FightScreenFactory = () => new Core2CombatScreen();
        }

        private Core2CombatController _controller;
        private Core2CombatScreenModel _model;
        private VisualElement _root, _backdrop, _field, _lanes, _top, _hand, _tools, _footer, _fx, _overlay;
        private readonly Dictionary<string, VisualElement> _fighters = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private IVisualElementScheduledItem _ticker;
        private bool _grid = true;
        private bool _narrow;
        private string _paintedBackdrop;

        public Core2CombatController Controller => _controller;

        public void Mount(VisualElement host, Core2RunSession session, Action onFightOver)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            _controller = new Core2CombatController(session, onFightOver, Options.Settings, LoadCardArt());
            _controller.Log.PlayerColorSetting = Options.LogPlayerColor;
            _controller.Log.EnemyColorSetting = Options.LogEnemyColor;
            _root = new VisualElement { name = "core2-combat", focusable = true };
            _root.AddToClassList("c2c-root");
            foreach (var sheet in new[] { "Generated/JsTokens", "Core2Ui/Core2CombatTokens", "Core2Ui/Core2Combat" })
            {
                var asset = Resources.Load<StyleSheet>(sheet);
                if (asset != null) _root.styleSheets.Add(asset);
                else Debug.LogWarning("Core 2 combat: missing style sheet Resources/" + sheet);
            }
            _backdrop = Layer("c2c-backdrop");
            _field = Layer("c2c-field");
            _lanes = new VisualElement { pickingMode = PickingMode.Ignore };
            _lanes.AddToClassList("c2c-lanes");
            _field.Add(_lanes);
            _top = Layer("c2c-top");
            _hand = Layer("c2c-hand");
            _hand.pickingMode = PickingMode.Ignore;
            _tools = Layer("c2c-tools");
            _footer = Layer("c2c-footer");
            _fx = Layer("c2c-fx");
            _fx.pickingMode = PickingMode.Ignore;
            _overlay = Layer("c2c-overlay");
            _overlay.pickingMode = PickingMode.Ignore;
            host.Add(_root);
            _root.RegisterCallback<GeometryChangedEvent>(_ => OnGeometry());
            _hand.RegisterCallback<GeometryChangedEvent>(_ => LayoutHand());
            _field.RegisterCallback<GeometryChangedEvent>(_ => LayoutField());
            BindInput();
            _controller.Changed += Repaint;
            _controller.StagePlayed += OnStage;
            _ticker = _root.schedule.Execute(Tick).Every(16);
            Repaint();
            _root.Focus();
        }

        public void Unmount()
        {
            _ticker?.Pause();
            _ticker = null;
            if (_controller != null) { _controller.Changed -= Repaint; _controller.StagePlayed -= OnStage; }
            UnbindInput();
            _root?.RemoveFromHierarchy();
            _root = null;
            _fighters.Clear();
        }

        private VisualElement Layer(string className)
        {
            var layer = new VisualElement();
            layer.AddToClassList(className);
            _root.Add(layer);
            return layer;
        }

        private static IReadOnlyDictionary<string, string> LoadCardArt()
        {
            var asset = Resources.Load<TextAsset>(CardArtResource);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (asset == null) { Debug.LogWarning("Core 2 combat: missing Resources/" + CardArtResource + ".json; cards show no painting."); return map; }
            var json = JObject.Parse(asset.text);
            foreach (var row in (json["cards"] as JObject ?? new JObject()).Properties())
                if ((string)row.Value["resource"] is string resource) map[row.Name] = resource;
            return map;
        }

        // The screen's own clock: real time that stops while the interruption cover is up (Core2Screens.IsInterrupted), so
        // Auto, beats and holds neither run behind the cover nor rush to catch up after the player returns.
        private double _clockMs, _lastRealMs = -1;
        private double NowMs => _clockMs;

        private void Tick()
        {
            var real = Time.realtimeSinceStartupAsDouble * 1000.0;
            var delta = _lastRealMs < 0 ? 0 : Math.Max(0, real - _lastRealMs);
            _lastRealMs = real;
            if (Core2Screens.IsInterrupted) return;
            _clockMs += Math.Min(delta, 250);
            if (_controller == null || _root?.panel == null) return;
            _controller.Tick(NowMs);
            TickEndTurnHold();
        }

        private void OnGeometry()
        {
            var narrow = _root.resolvedStyle.width > 0 && _root.resolvedStyle.width < 700;
            if (narrow != _narrow || !_root.ClassListContains(narrow ? "c2c-narrow" : "c2c-wide"))
            {
                _narrow = narrow;
                _root.EnableInClassList("c2c-narrow", narrow);
                _root.EnableInClassList("c2c-wide", !narrow);
                LayoutField();
                LayoutHand();
            }
        }

        // ================================================================================================================
        // Painting
        // ================================================================================================================

        private void Repaint()
        {
            if (_root == null || _controller == null) return;
            _model = _controller.Model();
            _root.EnableInClassList("c2c-grid-off", !_grid);
            PaintBackdrop();
            PaintTop();
            PaintField();
            PaintHand();
            PaintTools();
            PaintFooter();
            PaintOverlay();
        }

        internal static Texture2D Art(string resource) => string.IsNullOrEmpty(resource) ? null : Resources.Load<Texture2D>(resource);

        internal static void SetArt(VisualElement element, string resource, bool markMissing = false)
        {
            var texture = Art(resource);
            element.style.backgroundImage = texture != null ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.None);
            if (markMissing) element.EnableInClassList("c2c-figure-missing", texture == null);
        }

        internal static Label Text(string text, params string[] classes)
        {
            var label = new Label(text ?? "") { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) label.AddToClassList(c);
            return label;
        }

        internal static VisualElement Box(params string[] classes)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) box.AddToClassList(c);
            return box;
        }

        private void PaintBackdrop()
        {
            if (_paintedBackdrop == _model.Backdrop) return;
            _paintedBackdrop = _model.Backdrop;
            SetArt(_backdrop, _model.Backdrop);
        }

        private void PaintTop()
        {
            _top.Clear();
            var top = _model.Top;
            var hp = Box("c2c-top-hp");
            var heart = Box("c2c-heart");
            SetArt(heart, "Art/js1815/ui/icon-health");
            hp.Add(heart);
            hp.Add(Text(top.HpText, "c2c-top-hp-label"));
            var rule = Box("c2c-top-rule");
            var fill = Box("c2c-top-rule-fill");
            fill.style.width = Length.Percent(top.MaxHp > 0 ? 100f * top.Hp / top.MaxHp : 0);
            rule.Add(fill);
            hp.Add(rule);
            _top.Add(hp);
            var relics = Box("c2c-relics");
            foreach (var relic in top.Relics)
            {
                var chip = new VisualElement { tooltip = relic.Name + (string.IsNullOrEmpty(relic.Text) ? "" : "\n" + relic.Text), focusable = true };
                chip.AddToClassList("c2c-relic");
                SetArt(chip, Art(relic.Art) != null ? relic.Art : "Art/js1815/ui/icon-relic");
                relics.Add(chip);
            }
            relics.pickingMode = PickingMode.Position;
            _top.Add(relics);
            _top.Add(Text(_model.TurnRibbon, "c2c-ribbon"));
            var doors = Box("c2c-doors");
            doors.pickingMode = PickingMode.Position;
            if (Options.OpenArmoury != null) doors.Add(Door("Art/js1815/combat/footer/button-armoury", "Armoury", Options.OpenArmoury));
            if (Options.OpenMenu != null) doors.Add(Door("Art/js1815/combat/footer/button-menu", "Quick menu", Options.OpenMenu));
            _top.Add(doors);
            var cinders = Box("c2c-cinders");
            cinders.Add(Text(Core2CombatText.T("hud.cinders").ToUpperInvariant(), "c2c-strong"));
            cinders.Add(Text(Core2CombatText.Number(top.Cinders), "c2c-cinders-value"));
            _top.Add(cinders);
        }

        private static Button Door(string art, string label, Action open)
        {
            var button = new Button(open) { text = "", tooltip = label };
            button.AddToClassList("c2c-door");
            SetArt(button, art);
            return button;
        }

        private void PaintField()
        {
            var ids = new HashSet<string>(new[] { _model.Player.Id }.Concat(_model.Enemies.Select(e => e.Id)), StringComparer.Ordinal);
            foreach (var stale in _fighters.Keys.Where(id => !ids.Contains(id)).ToList()) { _fighters[stale].RemoveFromHierarchy(); _fighters.Remove(stale); }
            PaintFighter(_model.Player);
            foreach (var enemy in _model.Enemies) PaintFighter(enemy);
            var threat = _model.Reaction?.SourceId;
            foreach (var pair in _fighters) pair.Value.EnableInClassList("c2c-threat", pair.Key == threat);
            LayoutField();
        }

        private void PaintFighter(Core2CombatantModel m)
        {
            if (!_fighters.TryGetValue(m.Id, out var node))
            {
                node = new VisualElement { name = "fighter-" + m.Id, focusable = !m.IsPlayer };
                node.AddToClassList("c2c-fighter");
                node.AddToClassList(m.IsPlayer ? "c2c-player" : "c2c-enemy");
                var id = m.Id;
                if (!m.IsPlayer)
                {
                    node.RegisterCallback<ClickEvent>(e => { _controller.TapEnemy(id); e.StopPropagation(); });
                    node.RegisterCallback<NavigationSubmitEvent>(e => { _controller.TapEnemy(id); e.StopPropagation(); });
                }
                _field.Add(node);
                _fighters[m.Id] = node;
            }
            node.Clear();
            node.EnableInClassList("c2c-dead", !m.Alive);
            node.EnableInClassList("c2c-targetable", m.Targetable);
            node.EnableInClassList("c2c-poised", m.Poised);
            foreach (var tone in new[] { "block", "ward", "barrier" }) node.EnableInClassList("c2c-guard-" + tone, m.Guard == tone);
            node.tooltip = m.Name + " · " + m.HpText + (m.PoiseLabel != null ? " · " + m.PoiseLabel : "");
            var overhead = Box("c2c-overhead");
            if (m.Intent != null) overhead.Add(IntentNode(m.Intent));
            node.Add(overhead);
            var meters = Box("c2c-meters");
            var hp = Box("c2c-hp");
            var fill = Box("c2c-hp-fill");
            fill.style.width = Length.Percent((float)(m.HpFraction * 100));
            hp.Add(fill);
            hp.Add(Text(m.HpText, "c2c-hp-value"));
            meters.Add(hp);
            var badges = Box("c2c-badges");
            foreach (var badge in m.Badges)
            {
                var b = Box("c2c-badge", "c2c-badge-" + badge.Kind);
                b.tooltip = badge.Label + " " + badge.Value;
                b.Add(Text(Core2CombatText.Number(badge.Value), "c2c-badge-value"));
                badges.Add(b);
            }
            meters.Add(badges);
            if (m.Statuses.Count > 0)
            {
                var statuses = Box("c2c-statuses");
                foreach (var status in m.Statuses)
                {
                    var chip = new VisualElement { tooltip = status.Name + " " + status.Stacks + (string.IsNullOrEmpty(status.Tooltip) ? "" : "\n" + status.Tooltip) };
                    chip.AddToClassList("c2c-status");
                    SetArt(chip, status.Icon);
                    chip.Add(Text(Core2CombatText.Number(status.Stacks), "c2c-status-stacks"));
                    statuses.Add(chip);
                }
                meters.Add(statuses);
            }
            node.Add(meters);
            var figure = Box("c2c-figure");
            SetArt(figure, m.Figure, markMissing: true);
            figure.Add(Box("c2c-target-ring"));
            node.Add(figure);
            if (m.HitBadge != null) node.Add(Text(m.HitBadge, "c2c-hit-badge", "c2c-hit-" + m.HitTone));
            if (!m.IsPlayer && m.Alive)
            {
                var id = m.Id;
                var info = new Button(() => _controller.Inspect(id)) { text = "i", tooltip = Core2CombatText.T("js.combatantOverhead.inspect", ("name", m.Name)) };
                info.AddToClassList("c2c-info");
                node.Add(info);
            }
        }

        private static VisualElement IntentNode(Core2IntentBadge intent)
        {
            var node = Box("c2c-intent", "c2c-intent-" + intent.Visibility);
            if (intent.Tone == "green") node.AddToClassList("c2c-tone-green");
            node.tooltip = intent.Visibility == "unknown" ? Core2CombatText.T("combat.targetHud.unknown") : intent.Title;
            if (intent.Visibility == "unknown" || intent.Parts.Count == 0)
            {
                node.Add(Text(intent.Visibility == "unknown" ? "?" : intent.Title, "c2c-intent-value"));
                if (intent.Parts.Count == 0 && intent.Visibility != "unknown") { var icon = Box("c2c-intent-icon"); SetArt(icon, Core2CombatViewModel.IconArt + "intent-prepare"); node.Add(icon); }
            }
            foreach (var part in intent.Parts)
            {
                var read = Box("c2c-intent-read");
                if (part.Value != null) read.Add(Text(part.Value, "c2c-intent-value"));
                if (part.Summary != null) read.Add(Text(part.Summary, "c2c-intent-scope"));
                node.Add(read);
                var icon = Box("c2c-intent-icon");
                SetArt(icon, Core2CombatViewModel.IconArt + "intent-" + (part.Icon == "evade" ? "defend" : part.Icon));
                node.Add(icon);
            }
            if (intent.FollowUps.Count > 0)
            {
                var follow = Box("c2c-followups");
                foreach (var symbol in intent.FollowUps)
                {
                    var icon = Box("c2c-followup");
                    SetArt(icon, Core2CombatViewModel.IconArt + (symbol == "unknown" ? "status-unknown" : "intent-" + symbol));
                    follow.Add(icon);
                }
                node.Add(follow);
            }
            return node;
        }

        /// <summary>
        /// Fighter placement (JS battlefieldStage formation, measured at the pin): wide — the player front-left, enemies
        /// across the back of the plaza; narrow — the enemies on the top row, the player below on the left.
        /// </summary>
        private void LayoutField()
        {
            if (_model == null || _field == null) return;
            var enemies = _model.Enemies;
            var n = Math.Max(1, enemies.Count);
            for (var i = 0; i < enemies.Count; i++)
            {
                if (!_fighters.TryGetValue(enemies[i].Id, out var node)) continue;
                var center = _narrow
                    ? (n == 1 ? 72f : 22f + 56f * i / (n - 1))
                    : (n == 1 ? 62f : 34f + (n == 2 ? 38f : 48f) * i / (n - 1));
                node.style.left = Length.Percent(center);
                if (_narrow) node.style.bottom = StyleKeyword.Auto;
                else node.style.bottom = Length.Percent(36);
            }
            if (_fighters.TryGetValue(_model.Player.Id, out var player))
            {
                player.style.left = Length.Percent(_narrow ? 22f : 21f);
                player.style.bottom = Length.Percent(_narrow ? 6f : 2f);
            }
            _lanes.Clear();
            for (var lane = 0; lane < 3; lane++)
            {
                var line = Box("c2c-lane");
                line.style.top = Length.Percent(_narrow ? 30 + lane * 25 : 40 + lane * 22);
                _lanes.Add(line);
                var label = Text(Core2CombatText.Capitalize(Core2CombatViewModel.Lanes[2 - lane]), "c2c-lane-label");
                label.style.top = Length.Percent(_narrow ? 30 + lane * 25 : 40 + lane * 22);
                _lanes.Add(label);
            }
        }

        // ---- the hand --------------------------------------------------------------------------------------------------

        private void PaintHand()
        {
            _hand.Clear();
            foreach (var card in _model.Hand) _hand.Add(CardNode(card));
            LayoutHand();
        }

        private VisualElement CardNode(Core2HandCardModel card)
        {
            var node = new VisualElement { name = "card-" + card.InstanceId, focusable = true, tooltip = card.Name + (card.Refusal != null ? " (" + card.Refusal + ")" : "") };
            node.AddToClassList("c2c-card");
            node.EnableInClassList("c2c-card-selected", card.Selected);
            node.EnableInClassList("c2c-card-unplayable", !card.Playable);
            node.EnableInClassList("c2c-card-has-mana", card.ShowMp);
            var face = Box("c2c-card-face");
            face.Add(Box("c2c-card-base"));
            var art = Box("c2c-card-art");
            SetArt(art, card.Art);
            face.Add(art);
            face.Add(Box("c2c-card-title-fade"));
            face.Add(Box("c2c-card-panel"));
            face.Add(Box("c2c-card-frame"));
            face.Add(Text(card.Name, "c2c-card-title"));
            var rules = Box("c2c-card-rules");
            foreach (var line in card.FaceLines.Take(2)) rules.Add(Text(line, "c2c-card-line"));
            face.Add(rules);
            var tags = Box("c2c-card-tags");
            var sigil = Box("c2c-card-sigil");
            SetArt(sigil, card.Sigil);
            tags.Add(sigil);
            tags.Add(Text(card.TypeLabel, "c2c-card-tag-label"));
            face.Add(tags);
            face.Add(Box("c2c-card-flag"));
            face.Add(Box("c2c-card-sp-icon"));
            face.Add(Text(Core2CombatText.Number(card.Sp), "c2c-card-sp"));
            face.Add(Box("c2c-card-mp-icon"));
            face.Add(Text(Core2CombatText.Number(card.Mp), "c2c-card-mp"));
            if (!string.IsNullOrEmpty(card.Key)) face.Add(Text(card.Key, "c2c-card-key"));
            node.Add(face);
            node.Add(Box("c2c-card-glow"));
            var id = card.InstanceId;
            node.RegisterCallback<ClickEvent>(e => { _controller.TapCard(id); e.StopPropagation(); });
            node.RegisterCallback<NavigationSubmitEvent>(e => { _controller.TapCard(id); e.StopPropagation(); });
            return node;
        }

        /// <summary>JS hand fan: an overlapping row centred on the hand band, outer cards tilted and lowered along an arc.</summary>
        private void LayoutHand()
        {
            if (_hand == null) return;
            var width = _hand.resolvedStyle.width;
            var cards = _hand.Children().ToList();
            if (cards.Count == 0 || float.IsNaN(width) || width <= 0) return;
            var cardWidth = cards[0].resolvedStyle.width;
            if (float.IsNaN(cardWidth) || cardWidth <= 0) return;
            var n = cards.Count;
            var step = n == 1 ? 0 : Math.Min(cardWidth * 0.66f, (width - cardWidth - 8) / (n - 1));
            var total = cardWidth + step * (n - 1);
            var start = (width - total) / 2;
            var mid = (n - 1) / 2f;
            for (var i = 0; i < n; i++)
            {
                var card = cards[i];
                var off = i - mid;
                card.style.left = start + step * i;
                card.style.bottom = -Math.Abs(off) * Math.Abs(off) * 1.6f;
                card.style.rotate = new Rotate(new Angle(off * 2.2f, AngleUnit.Degree));
            }
            // The armed card rises above its neighbours (JS .card.selected z-index).
            cards.FirstOrDefault(c => c.ClassListContains("c2c-card-selected"))?.BringToFront();
        }

        // ---- tools row and footer ------------------------------------------------------------------------------------

        private void PaintTools()
        {
            _tools.Clear();
            var left = Box("c2c-tools-group");
            left.pickingMode = PickingMode.Position;
            var log = new Button { text = _controller.Log.Title, tooltip = _controller.Log.Description };
            log.AddToClassList("c2c-tool");
            log.AddToClassList("c2c-log-button");
            BindLogButton(log);
            left.Add(log);
            var grid = new Button(() => { _grid = !_grid; Repaint(); }) { text = "", tooltip = "Show battlefield grid" };
            grid.AddToClassList("c2c-check");
            grid.EnableInClassList("c2c-check-on", _grid);
            left.Add(grid);
            left.Add(Text(Core2CombatText.T("combat.grid.label"), "c2c-tool"));
            _tools.Add(left);
            var right = Box("c2c-tools-group");
            right.pickingMode = PickingMode.Position;
            var footer = _model.Footer;
            right.Add(Text(Core2CombatText.T("combat.reactions.label"), "c2c-tool"));
            right.Add(Switch(footer.ReactionsEnabled, footer.ReactionState, Core2CombatText.T("combat.reactions.state", ("state", footer.ReactionState)),
                footer.ReactionSwitchEnabled, () => _controller.SetReactions(!_controller.Fight.ReactionsEnabled)));
            right.Add(Text(Core2CombatText.T("combat.auto.label"), "c2c-tool"));
            right.Add(Switch(footer.Auto, footer.AutoState, Core2CombatText.T("combat.auto.state", ("state", footer.AutoState)), true, () => _controller.SetAuto(!_controller.Settings.Auto)));
            _tools.Add(right);
        }

        private static Button Switch(bool on, string state, string label, bool enabled, Action toggle)
        {
            var button = new Button(toggle) { text = "", tooltip = label };
            button.AddToClassList("c2c-switch");
            button.AddToClassList(on ? "c2c-switch-on" : "c2c-switch-off");
            button.EnableInClassList("c2c-switch-disabled", !enabled);
            button.SetEnabled(enabled);
            button.Add(Text(state, "c2c-switch-text"));
            button.Add(Box("c2c-switch-knob"));
            return button;
        }

        private Button _endTurn;
        private VisualElement _holdFill;

        private void PaintFooter()
        {
            _footer.Clear();
            var f = _model.Footer;
            var left = Box("c2c-footer-group");
            var orb = Box("c2c-orb");
            orb.EnableInClassList("c2c-orb-spent", f.Sp == 0);
            orb.tooltip = f.Sp + "/" + f.MaxSp + " " + Core2CombatText.T("js.orb.sp") + " · " + f.Mp + "/" + f.MaxMp + " " + Core2CombatText.T("js.orb.mp");
            orb.pickingMode = PickingMode.Position;
            orb.Add(Box("c2c-orb-frame"));
            orb.Add(Box("c2c-orb-core"));
            orb.Add(Text(Core2CombatText.Number(f.Sp), "c2c-orb-value"));
            orb.Add(Text(Core2CombatText.T("js.orb.sp"), "c2c-orb-label"));
            var pips = Box("c2c-pips");
            for (var i = 0; i < f.MaxMp; i++) pips.Add(Box("c2c-pip", i < f.Mp ? "c2c-pip-full" : "c2c-pip-spent"));
            orb.Add(pips);
            left.Add(orb);
            var draw = Box("c2c-pile", "c2c-pile-draw");
            draw.Add(Box("c2c-pile-art"));
            draw.Add(Text(Core2CombatText.Number(f.Draw), "c2c-pile-count"));
            draw.Add(Text(_narrow ? f.DrawLabel : f.DrawLabel.ToUpperInvariant(), "c2c-pile-label"));
            left.Add(draw);
            _footer.Add(left);
            _endTurn = new Button { text = "" };
            _endTurn.AddToClassList("c2c-end-turn");
            _endTurn.EnableInClassList("c2c-end-disabled", !f.EndTurnEnabled);
            _endTurn.Add(Text(f.EndTurnLabel, "c2c-end-label"));
            _endTurn.Add(Text(f.EndTurnKey, "c2c-end-key"));
            _endTurn.Add(Text(f.EndTurnNeedsHold ? f.EndTurnHint : "", "c2c-end-hint"));
            _holdFill = Box("c2c-hold-fill");
            _endTurn.Add(_holdFill);
            BindEndTurn(_endTurn);
            _footer.Add(_endTurn);
            var right = Box("c2c-footer-group");
            var spent = Box("c2c-pile", "c2c-pile-spent");
            spent.Add(Box("c2c-pile-art"));
            spent.Add(Text(f.DiscardLabel, "c2c-pile-label"));
            spent.Add(Text(f.ExhaustLabel, "c2c-pile-label"));
            right.Add(spent);
            var potions = new Button(() => { _trayOpen = !_trayOpen; PaintOverlay(); }) { text = "", tooltip = Core2CombatText.T("potions.run.title") };
            potions.AddToClassList("c2c-potions");
            potions.Add(Box("c2c-potions-art"));
            potions.Add(Text(Core2CombatText.T("potions.run.title"), "c2c-pile-label"));
            right.Add(potions);
            _footer.Add(right);
        }
    }
}
