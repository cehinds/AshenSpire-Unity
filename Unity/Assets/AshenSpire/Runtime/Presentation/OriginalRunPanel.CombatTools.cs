// Combat inspection, retained-card choices and keyboard commands for solo runs.
// Rules and state remain in OriginalGameSession. Closing a viewer never saves or
// consumes RNG. Callbacks belong to this panel's tree and leave with that tree.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed partial class OriginalRunPanel
    {
        private readonly OriginalPlayerSettings _settings;
        private Action _backAction;
        private readonly HashSet<KeyCode> _pressedKeys = new HashSet<KeyCode>();
        private static string PileName(string kind) => kind == "exhaust" ? "Exhausted" : kind == "draw" ? "Draw pile" : "Discard pile";
        private void InspectEnemy()
        {
            var enemy = _game.Enemies.OfType<JObject>().FirstOrDefault(e => (string)e["id"] == _target);
            if (enemy == null) return;
            Inspection((string)_game.Catalog.Record("enemies", (string)enemy["enemyId"])["name"], Render);
            Text("HP " + enemy["hp"] + "/" + enemy["maxHp"] + " · Guard " + enemy["block"], "stat");
            var telegraph = EnemyTelegraphView.FromSnapshot(enemy, _game.Player, (JObject)_game.Catalog.Data()["balance"]);
            EnemyTelegraphView.Attach(_root, telegraph);
            foreach (var status in (enemy["statuses"] as JObject ?? new JObject()).Properties())
            {
                var definition = _game.Catalog.Record("statuses", status.Name);
                Text(OriginalStatusText.Describe(status.Name, status.Value as JObject, (string)definition["name"]), "stat");
                Text(OriginalStatusText.Description(definition, status.Value as JObject), "lead");
            }
            Button("native-inspection-back", "Back to combat", Render); _root.Focus(); _report();
        }

        private void Inspection(string title, Action back)
        {
            _backAction = back;
            _mapView?.SetMapSurface?.Invoke(false);
            OriginalCombatLayout.SetSurface(_root, false);
            _root.RemoveFromClassList("combat-reframed");
            _root.Clear(); _actions?.RemoveFromHierarchy();
            _root.AddToClassList("combat-inspection");
            Text(title, "heading");
            _notice = Text("", "notice"); _notice.style.display = DisplayStyle.None;
        }

        private void ShowPile(string kind)
        {
            Inspection(PileName(kind).ToUpperInvariant(), Render);
            var cards = _game.Pile(kind).OfType<JObject>()
                .Select(instance => new { Instance = instance, Card = _game.Resolve(instance) })
                .OrderBy(row => (string)row.Card["name"], StringComparer.Ordinal)
                .ThenBy(row => (string)row.Instance["instanceId"], StringComparer.Ordinal).ToArray();
            Text(cards.Length + " cards · grouped by name", "caption");
            Text(kind == "draw" ? "Draw order stays hidden. Looking through these cards does not change your next draw."
                : kind == "discard" ? "These cards can return when the draw pile is reshuffled."
                : "These cards are out for the rest of this fight.", "caption");
            Button("native-pile-back", "Back to combat", Render);
            if (cards.Length == 0) Text("This pile is empty.", "node-title");
            OriginalCardInspection.Browse(_root, cards.Select(row => row.Instance), _game.Resolve, _game.Cost, _game.Catalog, "native-pile",
                instance => ReadCard(_game.Resolve(instance), _game.Cost(instance), () => ShowPile(kind)), _report, _pileBrowse);
            _root.Focus(); _report();
        }

        private void ShowCombatKeys()
        {
            Inspection("COMBAT CONTROLS", Render);
            Text("Tap a card to select it, then tap a highlighted target or use Play. Selection spends nothing. Hold or right-click a card to read its details, or use Inspect selected card. Closing hand inspection cancels selection. Release shortcut keys to act; holding them never repeats a turn.", "caption");
            Text("Esc · cancel selection or close an inspection\nTab · move between controls\nEnter · activate the focused button", "caption");
            Text("Change shortcuts in Settings → Controls. Pile, inventory and help screens block combat shortcuts while you read.", "caption");
            Button("native-combat-keys-back", "Back to combat", Render);
            // Gamepad hints (US-15.3), derived from the saved pad bindings; listed first when a pad was the last input.
            var pad = _settings?.GamepadBindings ?? OriginalGamepad.DefaultBindings;
            var padLines = "Gamepad · d-pad or left stick moves between controls · right stick scrolls\n" + string.Join("\n",
                OriginalGamepadNavigation.Bound(pad, OriginalGamepadNavigation.Actions.Where(a => !a.StartsWith("map", StringComparison.Ordinal)))
                    .Select(b => OriginalKeyBindings.PadLabel(b.Action) + " · " + OriginalGamepad.Label(b.Button)));
            if (GamepadNavigator.LastInputWasGamepad) Text(padLines, "caption").name = "native-combat-pad-hints";
            var keys = _settings?.KeyBindings ?? OriginalPlayerSettings.DefaultKeyBindings;
            foreach (var action in OriginalKeyBindings.CombatActions)
                Text(OriginalKeyBindings.Label(action) + " · " + (keys.TryGetValue(action, out var key) ? OriginalKeyBindings.DisplayKey(key) : "unbound"), "stat");
            if (!GamepadNavigator.LastInputWasGamepad) Text(padLines, "caption").name = "native-combat-pad-hints";
            _root.Focus(); _report();
        }

        private void EndTurnChoice()
        {
            var plan = _game.DiscardPlan;
            if ((bool)plan["prompt"]) ShowDiscardChoice(plan, new HashSet<string>(StringComparer.Ordinal));
            else _game.EndTurn();
        }

        private void ShowDiscardChoice(JObject plan, HashSet<string> chosen)
        {
            Inspection("CHOOSE CARDS TO DISCARD", Render);
            var minimum = (int)plan["minimum"]; var maximum = (int)plan["maximum"];
            Text(minimum == 0 ? "Keep your cards, or discard up to " + maximum + "."
                : "Discard at least " + minimum + " and at most " + maximum + " retained cards before ending this turn.", "caption");
            var count = Text("", "stat");
            // Use hand order, not hash-set enumeration or toggle order: discarded
            // card order can affect a later seeded reshuffle on another platform.
            var confirm = Button("native-discard-confirm", "Confirm and end turn", () => _game.EndTurn(((JArray)plan["cardIds"]).Values<string>().Where(chosen.Contains)));
            Button("native-discard-cancel", "Cancel · keep playing", Render);
            var eligible = new HashSet<string>(((JArray)plan["cardIds"]).Values<string>(), StringComparer.Ordinal);
            var choices = new Dictionary<string, (Button Button, string Text)>(StringComparer.Ordinal);
            void UpdateChoices()
            {
                count.text = chosen.Count + " selected · " + minimum + "–" + maximum + " allowed";
                confirm.SetEnabled(chosen.Count >= minimum && chosen.Count <= maximum);
                foreach (var pair in choices)
                {
                    var selected = chosen.Contains(pair.Key);
                    pair.Value.Button.text = (selected ? "Selected · " : "Keep · ") + pair.Value.Text;
                    pair.Value.Button.SetEnabled(selected || chosen.Count < maximum);
                    pair.Value.Button.EnableInClassList("primary", selected);
                }
            }
            foreach (var instance in _game.Hand.OfType<JObject>().Where(card => eligible.Contains((string)card["instanceId"])))
            {
                var id = (string)instance["instanceId"]; var card = _game.Resolve(instance);
                var button = Button("native-discard-choice-" + id, "", () =>
                {
                    if (!chosen.Remove(id)) chosen.Add(id);
                    // Keep the same controls/focus/scroll position while choosing.
                    UpdateChoices(); _report();
                });
                choices.Add(id, (button, (string)card["name"] + "\n" + OriginalCardText.Describe(card, _game.Catalog)));
            }
            UpdateChoices();
            _root.Focus(); _report();
        }

        private void BindCombatKeys()
        {
            _root.focusable = true;
            _root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (!OwnsKey(e.keyCode, e.target, e.altKey || e.ctrlKey || e.commandKey)) return;
                _pressedKeys.Add(e.keyCode);
                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            _root.RegisterCallback<KeyUpEvent>(e =>
            {
                if (!_pressedKeys.Remove(e.keyCode)) return;
                e.StopImmediatePropagation();
                if (!OwnsKey(e.keyCode, e.target, e.altKey || e.ctrlKey || e.commandKey)) return;
                Execute(() => CombatKey(e.keyCode));
            }, TrickleDown.TrickleDown);
            _root.RegisterCallback<FocusOutEvent>(_ => _pressedKeys.Clear());
            _root.Focus();
        }

        private bool OwnsKey(KeyCode key, IEventHandler target, bool modified)
        {
            if (modified || key == KeyCode.None) return false;
            // Reading pages own input. Let fields, dropdowns and buttons receive
            // their native keys; no combat shortcut exists on this surface.
            if (_backAction != null) return key == KeyCode.Escape;
            if (_game.Phase != OriginalRunPhase.Combat) return false;
            // Enter/Space retain normal UI activation for a focused button, including
            // confirmation inside inspection screens. Tab navigation stays native.
            if (target is Button && (key == KeyCode.Return || key == KeyCode.Space || key == KeyCode.KeypadEnter)) return false;
            if (target is TextField) return false;
            return key == KeyCode.Escape || OriginalKeyBindings.CombatAction(_settings?.KeyBindings, key) != null;
        }

        private void CombatKey(KeyCode key)
        {
            if (key == KeyCode.Escape)
            {
                if (_backAction != null) _backAction();
                else { _selected = null; Render(); _root.Focus(); }
                return;
            }
            if (_backAction != null) return; // Inspection owns input: never mutate the fight underneath it.
            var action = OriginalKeyBindings.CombatAction(_settings?.KeyBindings, key);
            if (action != null && action.StartsWith("card", StringComparison.Ordinal) && int.TryParse(action.Substring(4), out var slot))
            {
                var card = _game.Hand.OfType<JObject>().ElementAtOrDefault(slot - 1);
                if (card != null) { var id = (string)card["instanceId"]; _selected = _selected == id ? null : id; Render(); _root.Focus(); }
                return;
            }
            switch (action)
            {
                case "combatPlay": IfEnabled("native-play", () => PlayCard(_selected, _target)); break;
                case "endTurn": EndTurnChoice(); break;
                case "drawPile": ShowPile("draw"); break;
                case "discardPile": ShowPile("discard"); break;
                case "exhaustPile": ShowPile("exhaust"); break;
                case "combatDeck": Deck(); break;
                case "flask1": IfEnabled("native-crimson", () => _game.DrinkCharge("hp")); break;
                case "flask2": IfEnabled("native-azure", () => _game.DrinkCharge("mana")); break;
                case "flask3": IfEnabled("native-flask-0", () =>
                    _game.DrinkFlask(0, (bool?)_game.Catalog.Record("flasks", (string)_game.Player["flasks"][0]["flaskId"])["targeted"] == true ? _target : null)); break;
                case "targetNext": CycleTarget(1); break;
                case "targetPrevious": CycleTarget(-1); break;
            }
        }

        private void IfEnabled(string control, Action action)
        {
            if (_root.Q<Button>(control)?.enabledInHierarchy == true) action();
        }

        private void CycleTarget(int delta)
        {
            var ids = _game.Enemies.OfType<JObject>().Where(e => (bool?)e["alive"] == true).Select(e => (string)e["id"]).ToArray();
            if (ids.Length == 0) return;
            _target = ids[(Math.Max(0, Array.IndexOf(ids, _target)) + delta + ids.Length) % ids.Length];
            Render(); _root.Focus();
        }
    }
}
