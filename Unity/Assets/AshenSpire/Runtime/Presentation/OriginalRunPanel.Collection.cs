// OriginalRunPanel.Collection.cs — JS 07a–07d: deck, equipment, inventory and character as one
// tabbed modal. Top tabs (Character | Armory), a left rail (Character, Equipment, Inventory, Cards;
// a wrapping row in narrow windows) and a full-width Back footer. Deck() and Equipment() in
// OriginalRunPanel.cs fill the Cards and Equipment panes; this file owns the frame and the two
// read-only panes (Character, Inventory). Presentation only: every command still goes through the
// session exactly as before, and the old control ids (native-deck-back, native-equipment,
// native-equipment-back, native-set-*, native-equip-*, native-empty-*, native-deck-*) are kept.
// STYLE: Resources/JsCollection.uss (attached to the app root once; uses the --ash-* palette).
using System;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    /// <summary>Loads Resources/JsCollection.uss once and adds it to a root after the theme sheets.</summary>
    internal static class JsCollectionSheet
    {
        private static StyleSheet _sheet;
        internal static void Attach(VisualElement root)
        {
            if (root == null) return;
            if (_sheet == null) _sheet = Resources.Load<StyleSheet>("JsCollection");
            if (_sheet != null && !root.styleSheets.Contains(_sheet)) root.styleSheets.Add(_sheet);
        }
    }

    public sealed partial class OriginalRunPanel
    {
        private const string CollectionHostClass = "js-collection-host";
        private string _inventorySelection;

        /// <summary>Clears the run surface, mounts the modal frame and returns the pane to fill.</summary>
        private VisualElement Collection(string pane)
        {
            _mapView?.SetMapSurface?.Invoke(false);
            OriginalCombatLayout.SetSurface(_root, false);
            _root.Clear(); _actions?.RemoveFromHierarchy();
            JsCollectionSheet.Attach(_actionHost);
            var host = _root; host.AddToClassList(CollectionHostClass);
            var modal = new VisualElement { name = "native-collection" }; modal.AddToClassList("js-collection");
            // Render() rebuilds the run screen without knowing about this class; drop it when the modal goes.
            modal.RegisterCallback<DetachFromPanelEvent>(_ => { if (host.Q(className: "js-collection") == null) host.RemoveFromClassList(CollectionHostClass); });
            _root.Add(modal);

            var head = new VisualElement(); head.AddToClassList("js-collection-head"); modal.Add(head);
            Button("native-collection-tab-character", "Character", Character, head).EnableInClassList("selected", pane == "character");
            Button("native-collection-tab-armory", "Armory", Deck, head).EnableInClassList("selected", pane != "character");

            _notice = Label("", "notice"); _notice.style.display = DisplayStyle.None; modal.Add(_notice);
            var main = new VisualElement(); main.AddToClassList("js-collection-main"); modal.Add(main);
            var rail = new VisualElement(); rail.AddToClassList("js-collection-rail"); main.Add(rail);
            void Rail(string id, string text, Action open, string key, string tooltip = null)
            {
                var button = Button(id, text, open, rail); button.AddToClassList("js-rail-entry");
                button.EnableInClassList("selected", pane == key); if (tooltip != null) button.tooltip = tooltip;
            }
            Rail("native-collection-character", "Character", Character, "character");
            if (!RunSummary.IsTerminal(_game.Phase))
                Rail("native-equipment", "Equipment", Equipment, "equipment", _game.Phase == OriginalRunPhase.Combat ? "Switch prepared weapon sets" : "Change equipment and weapon sets");
            Rail("native-collection-inventory", "Inventory", Inventory, "inventory");
            Rail("native-collection-cards", "Cards", Deck, "cards");
            var body = new VisualElement(); body.AddToClassList("js-collection-pane"); body.AddToClassList("js-pane-" + pane); main.Add(body);

            var foot = new VisualElement(); foot.AddToClassList("js-collection-foot"); modal.Add(foot);
            // Equipment keeps its old Back target (the deck); the other panes close the modal.
            if (pane == "equipment") Button("native-equipment-back", "Back", Deck, foot);
            else Button("native-deck-back", "Back", Render, foot);
            return body;
        }

        private static void PaneTitle(VisualElement pane, string title, string subtitle = null)
        {
            pane.Add(Label(title, "js-pane-title"));
            if (!string.IsNullOrEmpty(subtitle)) pane.Add(Label(subtitle, "js-pane-subtitle"));
        }
        private static Label Chip(VisualElement parent, string text, string style = null)
        {
            var chip = Label(text, "js-chip"); if (style != null) chip.AddToClassList(style); parent.Add(chip); return chip;
        }
        private static VisualElement Row(VisualElement parent, string style)
        {
            var row = new VisualElement(); row.AddToClassList(style); parent.Add(row); return row;
        }
        private static Texture2D ItemArt(JObject item, string itemRef = null)
        {
            if (item == null) return null;
            if (itemRef != null && itemRef.StartsWith("relic/", StringComparison.Ordinal)) return Resources.Load<Texture2D>("Art/js1815/combat/relics/" + item["id"]);
            return Resources.Load<Texture2D>((string)item["kind"] == "armor" ? "Art/js1815/collection/armour/" + item["classId"] + "-" + item["id"] : "Art/js1815/collection/armaments/" + item["id"]);
        }
        private string KindLabel(JObject item, string itemRef = null)
        {
            if (itemRef != null && itemRef.StartsWith("relic/", StringComparison.Ordinal)) return "Relic";
            var kind = (string)item?["kind"];
            return string.IsNullOrEmpty(kind) ? "Item" : kind == "armor" ? "Armor" : OriginalCardText.Humanize(kind);
        }

        /// <summary>JS .as-detailcard item card: title bar, art window, gold type band, stat pair, flavour line.</summary>
        private VisualElement ItemCard(JObject item, string itemRef = null)
        {
            var level = itemRef == null ? 0 : (int?)_game.RunPlayer["itemUpgradeLevels"]?[itemRef] ?? 0;
            if (level > 0) try { item = new ItemUpgradeService(_game.Catalog).ResolveItem(itemRef, level); } catch (ArgumentException) { }
            var card = new VisualElement(); card.AddToClassList("js-item-card");
            card.Add(Label((string)item["name"] ?? OriginalCardText.Humanize((string)item["id"]), "js-item-card-title"));
            var art = ItemArt(item, itemRef);
            var window = Row(card, "js-item-card-art");
            if (art != null) window.Add(new Image { image = art, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
            var cls = (string)item["classId"];
            card.Add(Label((KindLabel(item, itemRef) + (string.IsNullOrEmpty(cls) ? "" : " · " + OriginalCardText.Humanize(cls))).ToUpperInvariant(), "js-item-card-band"));
            var stats = Row(card, "js-item-card-stats");
            void Stat(string key, string label) { if (item[key] == null) return; var cell = Row(stats, "js-item-stat"); cell.Add(Label(item[key].ToString(), "js-item-stat-value")); cell.Add(Label(label, "js-item-stat-label")); }
            Stat("attackRating", "ATTACK"); Stat("defenseRating", "DEFENSE"); Stat("poiseThreshold", "POISE"); Stat("weight", "WEIGHT");
            if (stats.childCount == 0) stats.RemoveFromHierarchy();
            if (level > 0) card.Add(Label("Improved +" + level, "js-item-card-note"));
            var blurb = (string)item["blurb"] ?? (string)item["description"];
            if (!string.IsNullOrEmpty(blurb)) card.Add(Label(blurb, "js-item-card-flavour"));
            return card;
        }

        /// <summary>JS 07d: identity, the painted figure on the spire vista, and framed summary rows.</summary>
        private void Character()
        {
            _backAction = Render;
            var pane = Collection("character"); var run = _game.RunPlayer; var player = _game.Player;
            var classId = (string)run["classId"]; var record = _game.Catalog.Record("classes", classId);
            var level = (int?)run["level"] ?? 1;
            pane.Add(Label(((string)record?["name"] ?? OriginalCardText.Humanize(classId)).ToUpperInvariant() + " · LEVEL " + level + " · ACT " + _game.ActNumber, "js-pane-eyebrow"));
            PaneTitle(pane, (string)record?["name"] ?? OriginalCardText.Humanize(classId), (string)record?["description"]);
            var split = Row(pane, "js-character");
            var painting = Row(split, "js-character-painting");
            var figure = Resources.Load<Texture2D>("Art/js1815/combat/player/" + classId + "-ready");
            if (figure != null) painting.Add(new Image { image = figure, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
            var facts = Row(split, "js-character-facts");
            VisualElement Fact(string title)
            {
                var row = Row(facts, "js-fact"); row.Add(Label(title, "js-fact-title"));
                var line = Row(row, "js-fact-line"); return line;
            }
            void Value(VisualElement line, string label, string value) { var pair = Row(line, "js-fact-pair"); pair.Add(Label(label + ":", "js-fact-label")); pair.Add(Label(value, "js-fact-value")); }
            var sheet = Fact("Character Sheet");
            Value(sheet, "HP", player["hp"] + "/" + player["maxHp"]);
            if (player["maxStamina"] != null) Value(sheet, "SP", player["stamina"] + "/" + player["maxStamina"]);
            if (player["maxMana"] != null) Value(sheet, "MP", player["mana"] + "/" + player["maxMana"]);
            Value(sheet, "Cinders", ((int?)run["cinders"] ?? 0).ToString());
            Value(sheet, "Stones", ((int?)run["smithingStones"] ?? 0).ToString());
            if (run["attributes"] is JObject attributes && attributes.Count > 0)
            {
                var line = Fact("Attributes");
                foreach (var attribute in _game.Catalog.Table("attributes").OfType<JObject>().Where(a => attributes[(string)a["id"]] != null))
                    Value(line, (string)attribute["shortLabel"] ?? (string)attribute["label"], attributes[(string)attribute["id"]].ToString());
            }
            var relics = (run["relics"] as JArray ?? new JArray()).Values<string>().Select(id => (string)_game.Catalog.Record("relics", id)?["name"] ?? id).ToArray();
            Fact("Relics").Add(Label(relics.Length == 0 ? "None carried" : relics.Length + " carried · " + string.Join(" · ", relics), "js-fact-text"));
            var pieces = new WeaponLoadout(_game.Catalog).Pieces((JObject)run["loadout"], classId).OfType<JObject>().Select(x => (string)x["name"]).ToArray();
            Fact("Equipment").Add(Label(pieces.Length == 0 ? "Nothing equipped" : string.Join(" · ", pieces), "js-fact-text"));
            _report();
        }

        /// <summary>JS 07c: framed item rows at left, the selected item's card at right.</summary>
        private void Inventory()
        {
            _backAction = Render;
            var pane = Collection("inventory"); var run = _game.RunPlayer;
            var upgrades = new ItemUpgradeService(_game.Catalog);
            var equipped = new WeaponLoadout(_game.Catalog).Pieces((JObject)run["loadout"], (string)run["classId"]).OfType<JObject>()
                .Select(WeaponLoadout.ItemRef).ToHashSet(StringComparer.Ordinal);
            var split = Row(pane, "js-inventory");
            var list = Row(split, "js-inventory-list");
            var detail = Row(split, "js-inventory-detail");
            JObject selected = null;
            foreach (var itemRef in upgrades.OwnedRefs(run))
            {
                JObject item; try { item = upgrades.Definition(itemRef); } catch (ArgumentException) { continue; }
                if (item == null) continue;
                var id = itemRef; var row = Button("native-collection-item-" + itemRef.Replace('/', '-'), "", () => { _inventorySelection = id; Inventory(); }, list);
                row.AddToClassList("js-item-row"); row.EnableInClassList("selected", itemRef == _inventorySelection);
                var thumb = Row(row, "js-item-thumb"); thumb.pickingMode = PickingMode.Ignore;
                var art = ItemArt(item, itemRef);
                if (art != null) thumb.Add(new Image { image = art, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
                var text = Row(row, "js-item-text"); text.pickingMode = PickingMode.Ignore;
                var name = Label((string)item["name"] ?? OriginalCardText.Humanize((string)item["id"]), "js-item-name"); name.pickingMode = PickingMode.Ignore; text.Add(name);
                var chips = Row(text, "js-chips"); chips.pickingMode = PickingMode.Ignore;
                Chip(chips, KindLabel(item, itemRef)).pickingMode = PickingMode.Ignore;
                if (equipped.Contains(itemRef)) Chip(chips, "EQUIPPED", "js-chip-go").pickingMode = PickingMode.Ignore;
                if (itemRef == _inventorySelection) selected = item;
            }
            if (list.childCount == 0) list.Add(Label("Nothing carried yet.", "caption"));
            if (selected == null) detail.Add(Label("Select an item to see its details and available actions.", "js-pane-subtitle"));
            else
            {
                detail.Add(ItemCard(selected, _inventorySelection));
                if (equipped.Contains(_inventorySelection)) detail.Add(Label("Equipped. Change it from Equipment.", "caption"));
            }
            _report();
        }
    }
}
