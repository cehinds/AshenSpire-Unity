using System;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;
using UnityEngine;

namespace AshenSpire.Presentation
{
    public sealed partial class OriginalCoopPanel
    {
        private Label _cardPreview;
        private string _dragPreviewKey;
        private JObject HandRow(string instanceId) => (Local["hand"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(r => (string)r["instance"]?["instanceId"] == instanceId);
        private string Preview(string instanceId, string targetId)
        {
            var row = HandRow(instanceId); var previews = row?["previews"] as JObject;
            var preview = previews?[targetId ?? "_"] ?? previews?["_"];
            return OriginalCardInspection.PreviewText(preview as JObject, id => id.StartsWith("e", StringComparison.Ordinal) && (Scene["enemies"] as JArray ?? new JArray()).Any(e => (string)e["id"] == id) ? Name("enemies", (string)Scene["enemies"].First(e => (string)e["id"] == id)["enemyId"]) : id == Id ? "You" : MemberName(id), _catalog);
        }
        private (string Id, VisualElement Element)[] DragTargets(string instanceId)
        {
            var row = HandRow(instanceId); var seat = Local["combat"];
            if (row == null || (string)Scene["phase"] != "player" || (bool?)seat?["entity"]?["alive"] != true || (bool?)seat?["connected"] != true || (bool?)seat?["ended"] == true || OriginalCardCostText.Shortage((JObject)row["cost"], Body) != null) return Array.Empty<(string, VisualElement)>();
            var mechanics = CardMechanics.FromDefinition((JObject)row["card"]);
            if (CardMechanics.HasProperty(mechanics, "internal.unplayable")) return Array.Empty<(string, VisualElement)>();
            if ((bool?)row["targets"]?["active"] == true) return row["targets"]["legalIds"].Values<string>().Select(id => (id, _body.Q(id == Id ? "coop-self-target" : "coop-ally-" + id))).ToArray();
            if (EnemyTargeting(mechanics)) return (Scene["enemies"] as JArray ?? new JArray()).Where(e => (bool?)e["alive"] == true).Select(e => ((string)e["id"], _body.Q("coop-target-" + e["id"]))).ToArray();
            return new[] { ((string)null, _body.Q("coop-self-target")) };
        }
        private void AimDrag(string instanceId, Vector2? position, bool flick)
        {
            var targets = DragTargets(instanceId);
            foreach (var t in targets) { t.Element?.EnableInClassList("card-drag-target", position.HasValue); t.Element?.EnableInClassList("card-drag-hover", position.HasValue && t.Element.worldBound.Contains(position.Value)); }
            var hover = position.HasValue ? OriginalCardInspection.DropTarget(targets, position.Value, flick) : default;
            var key = hover.Element == null ? "" : hover.Id ?? "self";
            if (_cardPreview == null) return;
            if (position.HasValue && key != _dragPreviewKey) _cardPreview.text = hover.Element == null ? "Release over a highlighted target · release elsewhere cancels" : Preview(instanceId, hover.Id);
            if (!position.HasValue) _cardPreview.text = _selected == null ? "Drag a card upward onto its target · hold or right-click to inspect" : Preview(_selected, _target);
            _dragPreviewKey = key;
        }
        private bool DropCard(string instanceId, Vector2 position, bool flick)
        {
            var target = OriginalCardInspection.DropTarget(DragTargets(instanceId), position, flick);
            if (target.Element == null) return false;
            _target = target.Id;
            if (target.Id != null && (Scene["enemies"] as JArray ?? new JArray()).Any(e => (string)e["id"] == target.Id)) _ui.HostileTarget = target.Id;
            Send(new JObject { ["type"] = "playCard", ["cardInstanceId"] = instanceId, ["targetId"] = target.Id }); return true;
        }
        private bool ArmedAt(string targetId, bool friendly)
        {
            var row = (Local["hand"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(r => (string)r["instance"]?["instanceId"] == _selected);
            var seat = Local["combat"];
            if (row == null || (string)Scene["phase"] != "player" || (bool?)seat?["entity"]?["alive"] != true || (bool?)seat?["connected"] != true || (bool?)seat?["ended"] == true) return false;
            var mechanics = CardMechanics.FromDefinition((JObject)row["card"]);
            if (CardMechanics.HasProperty(mechanics, "internal.unplayable") || OriginalCardCostText.Shortage((JObject)row["cost"], Body) != null) return false;
            if (friendly) return (bool?)row["targets"]?["active"] == true && (row["targets"]?["legalIds"] as JArray ?? new JArray()).Values<string>().Contains(targetId);
            return (bool?)row["targets"]?["active"] != true && EnemyTargeting(mechanics) && (Scene["enemies"] as JArray ?? new JArray()).Any(e => (string)e["id"] == targetId && (bool?)e["alive"] == true);
        }
        private static bool EnemyTargeting(JObject mechanics) => new[] { "enemy", "allEnemies", "randomEnemy" }.Any(t => CardMechanics.HasProperty(mechanics, "targeting." + t));
        private void PickCombatTarget(string id, bool friendly)
        {
            _target = id;
            if (!friendly) _ui.HostileTarget = id;
            if (ArmedAt(id, friendly)) Send(new JObject { ["type"] = "playCard", ["cardInstanceId"] = _selected, ["targetId"] = id });
            else Render();
        }
        private void InspectHandCard()
        {
            var row = (Local["hand"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(r => (string)r["instance"]?["instanceId"] == _selected);
            if (row == null) return;
            var card = (JObject)row["card"]; var cost = (JObject)row["cost"];
            var seat = Local["combat"];
            var mayPlay = (string)Scene["phase"] == "player" && (bool?)seat?["entity"]?["alive"] == true && (bool?)seat?["connected"] == true && (bool?)seat?["ended"] != true;
            var refusal = CardMechanics.HasProperty(CardMechanics.FromDefinition(card), "internal.unplayable") ? "This card cannot be played." : OriginalCardCostText.Shortage(cost, Body);
            var legal = (bool?)row["targets"]?["active"] != true || (row["targets"]?["legalIds"] as JArray ?? new JArray()).Values<string>().Contains(_target);
            var target = (Scene["enemies"] as JArray ?? new JArray()).FirstOrDefault(e => (string)e["id"] == _target);
            var destination = (bool?)row["targets"]?["active"] == true ? MemberName(_target) : target == null ? null : Name("enemies", (string)target["enemyId"]);
            ReadCard(card, cost, () => { _selected = null; Main(); }, "Play " + card["name"] + (destination == null ? "" : " on " + destination), () => Send(new JObject { ["type"] = "playCard", ["cardInstanceId"] = _selected, ["targetId"] = _target }),
                mayPlay && refusal == null && legal, refusal ?? (!mayPlay ? "Waiting for your turn." : !legal ? "Choose a legal recipient." : null), Body);
        }
        private void CardOffer(VisualElement grid, JObject card, string id, string verb, Action command, bool enabled = true, string reason = null, bool selected = false)
        {
            var cost = CardMechanics.CostProfile(card); var tile = new VisualElement(); tile.AddToClassList("original-card-offer"); grid.Add(tile);
            Action read = () => ReadCard(card, cost, Main, verb, command, enabled, reason);
            tile.Add(new OriginalCardView(_catalog, card, cost, null, selected, read, "coop-" + id + "-inspect", inspect: read));
            var button = Button(id, verb, command); tile.Add(button); button.SetEnabled(enabled);
            if (selected) button.AddToClassList("primary");
            if (reason != null) tile.Add(Label(reason, "caption"));
        }
        private void ReadCard(JObject card, JObject cost, Action back, string verb = null, Action command = null, bool enabled = true, string reason = null, JObject player = null)
        {
            SetMapSurface(false); _body.Clear();
            // A fresh authoritative snapshot closes the reading view. No captured
            // action survives a server revision, and all commits still go to Send.
            _ui.Surface = "main";
            _readBack = () => { _readBack = null; back(); };
            Text((string)card["name"], "heading"); Button("card-inspection-back", "Back", _readBack);
            _body.Add(OriginalCardInspection.Content(_catalog, card, cost, player));
            if (player != null && _selected != null) Text(Preview(_selected, _target), "lead");
            if (reason != null) Text(reason, "notice");
            if (command != null) Button("card-inspection-action", verb, command).SetEnabled(enabled);
            _root.focusable = true; _root.Focus();
            _report?.Invoke();
        }
    }
}
