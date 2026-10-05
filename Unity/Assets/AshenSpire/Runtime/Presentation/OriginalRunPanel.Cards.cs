using System;
using System.Linq;
using System.Runtime.CompilerServices;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;
using UnityEngine;
using System.Collections.Generic;

namespace AshenSpire.Presentation
{
    public sealed partial class OriginalRunPanel
    {
        private void SelectOrPlayCard(string id)
        {
            if (_selected == id)
            {
                // Same affordance as the reference: select, then click again to play.
                // Authoritative command validation handles affordability and legal targets.
                Execute(() => PlayCard(id, ArmedFor("enemy") ? _target : null));
            }
            else { _selected = id; Render(); }
        }
        private sealed class AimMemory { internal string Room, Target; }
        private static readonly ConditionalWeakTable<OriginalGameSession, AimMemory> Aims = new ConditionalWeakTable<OriginalGameSession, AimMemory>();
        private readonly AimMemory _aim;
        private string _target { get => _aim.Target; set => _aim.Target = value; }
        private static AimMemory AimFor(OriginalGameSession game)
        {
            // Accepted commands rebuild the panel. Keep aiming only for this
            // session/fight; no presentation choice enters a player save.
            var aim = Aims.GetValue(game, _ => new AimMemory());
            var room = game.ActNumber + "/" + game.RunPlayer["mapNodeId"];
            if (aim.Room != room) { aim.Room = room; aim.Target = null; }
            return aim;
        }
        private readonly OriginalCardBrowseState _deckBrowse = new OriginalCardBrowseState(), _pileBrowse = new OriginalCardBrowseState();
        private Label _cardPreview;
        private string _dragPreviewKey;
        private string PreviewName(string id) => id == "player" ? "You" : (string)_game.Catalog.Record("enemies", (string)_game.Enemies.First(e => (string)e["id"] == id)["enemyId"])["name"];
        private string Preview(string instance, string target)
        {
            var card = _game.Hand.OfType<JObject>().FirstOrDefault(c => (string)c["instanceId"] == instance);
            if (card != null && _game.CardChoice(card) != null) return "Choose a stance before playing this card.";
            return OriginalCardInspection.PreviewText(_game.PreviewCard(instance, target), PreviewName, _game.Catalog);
        }
        private void PlayCard(string instanceId, string targetId)
        {
            var instance = _game.Hand.OfType<JObject>().FirstOrDefault(c => (string)c["instanceId"] == instanceId)
                ?? throw new ArgumentException("Card is no longer in hand.");
            var plan = _game.CardChoice(instance);
            if (plan == null) { _game.Play(instanceId, targetId); return; }
            Inspection("Choose a stance", Render);
            Text("Choose the stance to enter. Cancel keeps your card and resources.", "lead");
            foreach (var option in plan["options"].OfType<JObject>())
            {
                var id = (string)option["id"]; var active = (bool?)option["active"] == true;
                var button = Button("native-card-stance-" + id, (string)option["name"] + (active ? " · Active" : ""), () => _game.Play(instanceId, targetId, id));
                button.SetEnabled(!active); button.tooltip = (string)option["tooltip"];
            }
            Button("native-card-stance-cancel", "Cancel", Render); _root.Focus(); _report();
        }
        private (string Id, VisualElement Element)[] DragTargets(string instanceId)
        {
            var instance = _game.Hand.OfType<JObject>().FirstOrDefault(c => (string)c["instanceId"] == instanceId);
            if (instance == null || OriginalCardCostText.Shortage(_game.Cost(instance), _game.Player) != null) return Array.Empty<(string, VisualElement)>();
            var mechanics = CardMechanics.FromDefinition(_game.Resolve(instance));
            if (CardMechanics.HasProperty(mechanics, "internal.unplayable")) return Array.Empty<(string, VisualElement)>();
            if (EnemyTargeting(mechanics)) return _game.Enemies.OfType<JObject>().Where(e => (bool?)e["alive"] == true).Select(e => ((string)e["id"], _root.Q("native-target-" + e["id"]))).ToArray();
            return new[] { ((string)null, _root.Q("native-self-target")) };
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
            if (target.Id != null) _target = target.Id;
            Execute(() => PlayCard(instanceId, target.Id)); return true;
        }
        private bool ArmedFor(string target)
        {
            var instance = _game.Hand.OfType<JObject>().FirstOrDefault(c => (string)c["instanceId"] == _selected);
            if (instance == null) return false;
            var mechanics = CardMechanics.FromDefinition(_game.Resolve(instance));
            return (target == "enemy" ? EnemyTargeting(mechanics) : CardMechanics.HasProperty(mechanics, "targeting." + target)) && !CardMechanics.HasProperty(mechanics, "internal.unplayable") && OriginalCardCostText.Shortage(_game.Cost(instance), _game.Player) == null;
        }
        private static bool EnemyTargeting(JObject mechanics) => new[] { "enemy", "allEnemies", "randomEnemy" }.Any(t => CardMechanics.HasProperty(mechanics, "targeting." + t));
        private void PickCombatTarget(string id)
        {
            _target = id;
            if (ArmedFor("enemy")) PlayCard(_selected, id);
            else Render();
        }
        private void CardOffer(VisualElement grid, JObject card, string id, string verb, Action command, bool enabled = true, string reason = null)
        {
            var cost = CardMechanics.CostProfile(card);
            var tile = new VisualElement(); tile.AddToClassList("original-card-offer"); grid.Add(tile);
            Action read = () => ReadCard(card, cost, Render, verb, command, enabled, reason);
            tile.Add(new OriginalCardView(_game.Catalog, card, cost, null, false, read, id + "-inspect", inspect: read));
            Button(id, verb, command, tile).SetEnabled(enabled);
            if (reason != null) tile.Add(Label(reason, "caption"));
        }

        private void ReadCard(JObject card, JObject cost, Action back, string verb = null, Action command = null, bool enabled = true, string reason = null, JObject player = null)
        {
            Inspection((string)card["name"], back);
            Button("native-card-inspection-back", "Back", back);
            _root.Add(OriginalCardInspection.Content(_game.Catalog, card, cost, player));
            if (player != null && _selected != null) Text(Preview(_selected, _target), "lead");
            if (reason != null) Text(reason, "notice");
            if (command != null) Button("native-card-inspection-action", verb, command).SetEnabled(enabled);
            _root.Focus(); _report();
        }

        private void InspectHandCard()
        {
            var instance = _game.Hand.OfType<JObject>().FirstOrDefault(c => (string)c["instanceId"] == _selected);
            if (instance == null) return;
            var card = _game.Resolve(instance); var cost = _game.Cost(instance);
            var refusal = CardMechanics.HasProperty(CardMechanics.FromDefinition(card), "internal.unplayable") ? "This card cannot be played." : OriginalCardCostText.Shortage(cost, _game.Player);
            var targetName = _game.Enemies.OfType<JObject>().FirstOrDefault(e => (string)e["id"] == _target);
            var verb = "Play " + card["name"] + (CardMechanics.HasProperty(CardMechanics.FromDefinition(card), "targeting.enemy") && targetName != null ? " on " + _game.Catalog.Record("enemies", (string)targetName["enemyId"])["name"] : "");
            ReadCard(card, cost, () => { _selected = null; Render(); }, verb, () => PlayCard((string)instance["instanceId"], _target), refusal == null, refusal, _game.Player);
        }
    }
}
