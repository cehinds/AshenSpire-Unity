// Core2CombatScreen.Overlays.cs — what opens above the battlefield: the reaction window (JS reactionChoiceModal), the
// combat log drawer and its Full log door (JS combatTools / combatLogModal), the enemy inspector (JS combatantInspector),
// the potions tray (JS combatActionRow potions) and the hover tooltip that reads a damage receipt (JS damageReceipt).
// While an offer is pending the reaction window is the only thing that takes input: the overlay covers the screen.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    public sealed partial class Core2CombatScreen
    {
        private bool _trayOpen;
        private VisualElement _tip;

        private void PaintOverlay()
        {
            _overlay.Clear();
            var modal = false;
            PaintLogDrawer();
            if (_model.Reaction != null) { _overlay.Add(ReactionModal(_model.Reaction)); modal = true; }
            else if (_controller.Log.FullOpen) { _overlay.Add(FullLogDoor()); modal = true; }
            else if (_controller.Inspecting != null)
            {
                var enemy = _controller.Fight.Enemies.FirstOrDefault(e => e.Id == _controller.Inspecting);
                if (enemy != null) { _overlay.Add(Inspector(Core2CombatViewModel.Inspect(_controller.Fight, enemy))); modal = true; }
            }
            else if (_trayOpen) _overlay.Add(PotionTray());
            _overlay.pickingMode = modal ? PickingMode.Position : PickingMode.Ignore;
        }

        private VisualElement Scrim(Action onTap)
        {
            var scrim = new VisualElement();
            scrim.AddToClassList("c2c-scrim");
            if (onTap != null) scrim.RegisterCallback<ClickEvent>(e => { onTap(); e.StopPropagation(); });
            return scrim;
        }

        private static Button ModalButton(string text, Action onClick, bool primary = false)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("c2c-button");
            if (primary) button.AddToClassList("c2c-button-primary");
            return button;
        }

        private static VisualElement ModalHead(string eyebrow, string title, Action close)
        {
            var head = new VisualElement();
            head.AddToClassList("c2c-modal-head");
            var titles = new VisualElement();
            if (!string.IsNullOrEmpty(eyebrow)) titles.Add(Text(eyebrow.ToUpperInvariant(), "c2c-modal-eyebrow"));
            titles.Add(Text(title, "c2c-modal-title"));
            head.Add(titles);
            if (close != null)
            {
                var x = new Button(close) { text = "", tooltip = Core2CombatText.T("common.close") };
                x.AddToClassList("c2c-modal-close");
                head.Add(x);
            }
            return head;
        }

        // ---- the reaction window -----------------------------------------------------------------------------------------

        private VisualElement ReactionModal(Core2ReactionModalModel m)
        {
            var layer = new VisualElement();
            layer.AddToClassList("c2c-overlay");
            // No other input while the offer is pending: the scrim swallows taps (Back is the only way to decline).
            layer.Add(Scrim(null));
            var modal = new VisualElement { name = "reaction-choice" };
            modal.AddToClassList("c2c-modal");
            modal.Add(ModalHead(m.Eyebrow, m.Title, () => _controller.AnswerReaction(null)));
            var body = new VisualElement();
            body.AddToClassList("c2c-modal-body");
            if (m.Threat != null) body.Add(Text(m.Threat, "c2c-modal-threat"));
            if (m.Incoming != null) body.Add(Text(m.Incoming, "c2c-modal-text", "c2c-strong"));
            body.Add(Text(m.Help, "c2c-modal-text"));
            if (m.Stack.Count > 0)
            {
                body.Add(Text(m.StackLabel, "c2c-stack-label"));
                for (var i = 0; i < m.Stack.Count; i++) body.Add(Text((i + 1) + ". " + m.Stack[i], "c2c-stack-entry"));
            }
            var options = new VisualElement();
            options.AddToClassList("c2c-options");
            VisualElement first = null;
            foreach (var option in m.Options)
            {
                var tile = new VisualElement { name = "reaction-option-" + option.Id, focusable = true };
                tile.AddToClassList("c2c-option");
                tile.EnableInClassList("c2c-option-selected", option.Id == m.Selected);
                tile.Add(Text(option.Name, "c2c-option-name"));
                if (!string.IsNullOrEmpty(option.Summary)) tile.Add(Text(option.Summary, "c2c-option-summary"));
                var costs = new VisualElement { pickingMode = PickingMode.Ignore };
                costs.AddToClassList("c2c-option-costs");
                foreach (var cost in option.Costs) costs.Add(Text(cost, "c2c-pill"));
                tile.Add(costs);
                var id = option.Id;
                tile.RegisterCallback<ClickEvent>(e => { _controller.SelectReactionOption(id); e.StopPropagation(); });
                tile.RegisterCallback<NavigationSubmitEvent>(e => { _controller.SelectReactionOption(id); e.StopPropagation(); });
                options.Add(tile);
                first ??= tile;
            }
            body.Add(options);
            modal.Add(body);
            var foot = new VisualElement();
            foot.AddToClassList("c2c-modal-foot");
            foot.Add(ModalButton(m.DeclineLabel, () => _controller.AnswerReaction(null)));
            var play = ModalButton(m.PlayLabel, () => { if (_model.Reaction?.Selected != null) _controller.AnswerReaction(_model.Reaction.Selected); }, primary: true);
            play.SetEnabled(m.PlayEnabled);
            foot.Add(play);
            modal.Add(foot);
            layer.Add(modal);
            // JS initialFocus: the first option, so a pad or keyboard player starts on a card.
            if (first != null) layer.schedule.Execute(() => { if (m.Selected == null) first.Focus(); });
            return layer;
        }

        // ---- the combat log drawer and the Full log door ---------------------------------------------------------------

        private VisualElement _logDrawer;

        private void PaintLogDrawer()
        {
            _logDrawer?.RemoveFromHierarchy();
            _logDrawer = null;
            var log = _controller.Log;
            if (!log.Open) return;
            var drawer = new VisualElement { name = "combat-log" };
            drawer.AddToClassList("c2c-log");
            drawer.AddToClassList("c2c-log-" + log.Size);
            var head = new VisualElement();
            head.AddToClassList("c2c-log-head");
            head.Add(Text(log.Title, "c2c-log-title"));
            var full = new Button(() => { log.OpenFull(); PaintOverlay(); }) { text = log.FullLabel };
            full.AddToClassList("c2c-tool");
            head.Add(full);
            drawer.Add(head);
            drawer.Add(LogList(door: false));
            // The drawer sits under the overlay (so a modal covers it) and above the hand.
            _root.Insert(_root.IndexOf(_overlay), drawer);
            _logDrawer = drawer;
        }

        private ScrollView LogList(bool door)
        {
            var list = new ScrollView(ScrollViewMode.Vertical) { name = door ? "combat-log-door-entries" : "combat-log-entries", focusable = true };
            list.AddToClassList("c2c-log-list");
            var (player, enemy) = _controller.Log.Palette;
            var playerColor = Colour(player); var enemyColor = Colour(enemy);
            foreach (var row in _controller.Log.Rows(_controller.Fight, door))
            {
                switch (row.Kind)
                {
                    case "legend":
                    {
                        var legend = Box("c2c-legend");
                        var roles = row.Text.Split('|');
                        legend.Add(Dot(playerColor)); legend.Add(Text(roles[0].Trim(), "c2c-log-text"));
                        legend.Add(Dot(enemyColor)); legend.Add(Text(roles[roles.Length - 1].Trim(), "c2c-log-text"));
                        list.Add(legend);
                        break;
                    }
                    case "heading": list.Add(Text(row.Text, "c2c-log-row", "c2c-log-heading")); break;
                    case "detail": list.Add(Text(row.Text, "c2c-log-row", "c2c-log-detail")); break;
                    case "empty": list.Add(Text(row.Text, "c2c-log-row", "c2c-log-empty")); break;
                    default:
                    {
                        // One wrapping label: the actors' names coloured inline (rich text), the words between kept as written.
                        var rich = string.Concat((row.Entry?.Segments ?? new List<Core2LogSegment> { new Core2LogSegment { Text = row.Text } }).Select(segment =>
                        {
                            var words = (segment.Text ?? "").Replace("<", "<noparse><</noparse>");
                            return segment.Side == null ? words : "<b><color=#" + ColorUtility.ToHtmlStringRGB(segment.Side == "player" ? playerColor : enemyColor) + ">" + words + "</color></b>";
                        }));
                        var entry = new Label(rich) { enableRichText = true };
                        entry.AddToClassList("c2c-log-row");
                        entry.AddToClassList("c2c-log-entry");
                        entry.AddToClassList("c2c-log-text");
                        var receipt = row.Entry?.DamageReceipt;
                        if (receipt != null) AttachTip(entry, () => Core2CombatLog.DamageReceiptLines(_controller.Content, receipt));
                        list.Add(entry);
                        break;
                    }
                }
            }
            list.schedule.Execute(() => list.scrollOffset = new Vector2(0, float.MaxValue));
            return list;
        }

        private static VisualElement Dot(Color colour)
        {
            var dot = Box("c2c-legend-dot");
            dot.style.backgroundColor = colour;
            return dot;
        }

        private static Color Colour(string hex) => ColorUtility.TryParseHtmlString(hex, out var colour) ? colour : Color.white;

        private VisualElement FullLogDoor()
        {
            var log = _controller.Log;
            var door = new VisualElement { name = "combat-log-door" };
            door.AddToClassList("c2c-door-full");
            door.Add(ModalHead(null, log.Title, () => { log.CloseFull(); PaintOverlay(); }));
            door.Add(LogList(door: true));
            var foot = new VisualElement();
            foot.AddToClassList("c2c-modal-foot");
            foot.Add(ModalButton(log.CloseLabel, () => { log.CloseFull(); PaintOverlay(); }));
            foot.Add(ModalButton(log.DetailToggleLabel, () => { log.ToggleDetail(); PaintOverlay(); }));
            door.Add(foot);
            return door;
        }

        // ---- the enemy inspector ---------------------------------------------------------------------------------------

        private VisualElement Inspector(Core2InspectorModel m)
        {
            var layer = new VisualElement();
            layer.AddToClassList("c2c-overlay");
            layer.Add(Scrim(() => _controller.Inspect(null)));
            var modal = new VisualElement { name = "combatant-inspector" };
            modal.AddToClassList("c2c-modal");
            modal.AddToClassList("c2c-inspector");
            modal.Add(ModalHead(m.Subtitle, m.Name, () => _controller.Inspect(null)));
            var body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("c2c-modal-body");
            foreach (var section in m.Sections)
            {
                body.Add(Text(section.Title.ToUpperInvariant(), "c2c-inspector-section"));
                if (section.Knowledge != "known")
                {
                    body.Add(Text(Core2CombatText.T(section.Knowledge == "none" ? "inspector.empty.none" : "inspector.empty.unknown"), "c2c-inspector-label"));
                    continue;
                }
                foreach (var (label, value) in section.Rows)
                {
                    var row = Box("c2c-inspector-row");
                    row.Add(Text(label, "c2c-inspector-label"));
                    if (!string.IsNullOrEmpty(value)) row.Add(Text(value, "c2c-inspector-value"));
                    body.Add(row);
                }
            }
            modal.Add(body);
            layer.Add(modal);
            return layer;
        }

        // ---- potions -----------------------------------------------------------------------------------------------------

        private VisualElement PotionTray()
        {
            var tray = new VisualElement { name = "potions" };
            tray.AddToClassList("c2c-tray");
            foreach (var flask in _model.Footer.Flasks)
            {
                var row = new Button(() => UseFlask(flask)) { text = "" };
                row.AddToClassList("c2c-tray-row");
                row.SetEnabled(flask.Usable && flask.Charges > 0);
                var icon = Box("c2c-tray-icon");
                SetArt(icon, flask.Art);
                row.Add(icon);
                row.Add(Text(flask.Name + (flask.ChargeKind != null ? " ×" + flask.Charges : "") + "\n" + flask.Text, "c2c-tray-text"));
                tray.Add(row);
            }
            return tray;
        }

        private void UseFlask(Core2FlaskModel flask)
        {
            _trayOpen = false;
            if (flask.NeedsTarget) { _controller.ArmFlask(flask.Slot); return; }
            _controller.UseFlask(flask.ChargeKind, flask.Slot);
        }

        // ---- hover tooltips (runtime panels draw no `tooltip`; JS attachTooltip) ----------------------------------------

        private void AttachTip(VisualElement element, Func<IEnumerable<string>> lines)
        {
            element.RegisterCallback<PointerEnterEvent>(_ => ShowTip(element, lines()));
            element.RegisterCallback<PointerLeaveEvent>(_ => HideTip());
            element.RegisterCallback<FocusInEvent>(_ => ShowTip(element, lines()));
            element.RegisterCallback<FocusOutEvent>(_ => HideTip());
        }

        private void ShowTip(VisualElement anchor, IEnumerable<string> lines)
        {
            HideTip();
            if (_root == null) return;
            var tip = new VisualElement { name = "combat-tooltip", pickingMode = PickingMode.Ignore };
            tip.AddToClassList("c2c-tooltip");
            foreach (var line in lines) tip.Add(Text(line, "c2c-tooltip-line"));
            var at = _root.WorldToLocal(anchor.worldBound.position);
            tip.style.left = Mathf.Clamp(at.x, 0, Mathf.Max(0, _root.resolvedStyle.width - 300));
            tip.style.top = Mathf.Max(0, at.y - 8);
            tip.style.translate = new Translate(0, Length.Percent(-100));
            _root.Add(tip);
            _tip = tip;
        }

        private void HideTip()
        {
            _tip?.RemoveFromHierarchy();
            _tip = null;
        }
    }
}
