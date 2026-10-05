// Readable card details shared by solo and co-op. Callers own navigation and
// contextual actions; opening this view never spends resources or changes a run.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    internal static class OriginalCardInspection
    {
        internal static VisualElement Content(OriginalContentCatalog catalog, JObject card, JObject cost, JObject player = null)
        {
            var body = new VisualElement(); body.AddToClassList("original-card-inspection");
            var face = new OriginalCardView(catalog, card, cost, player, false, null, "card-inspection-face");
            face.pickingMode = PickingMode.Ignore; face.focusable = false; face.AddToClassList("inspection-card"); body.Add(face);
            var details = new VisualElement(); details.AddToClassList("original-card-details"); body.Add(details);
            Add(details, OriginalCardText.Humanize((string)card["rarity"] ?? "common") + " · " + OriginalCardText.Humanize((string)card["type"]), "stat");
            Add(details, OriginalCardCostText.Describe(cost), "stat");
            Add(details, OriginalCardText.Describe(card, catalog), "lead");
            var shortage = player == null ? null : OriginalCardCostText.Shortage(cost, player);
            if (shortage != null) Add(details, shortage, "notice");
            var tags = ((JArray)catalog.Data()["tags"]).OfType<JObject>().ToDictionary(t => (string)t["id"]);
            var ids = card["cardTags"] is JArray explicitTags ? explicitTags.Values<string>() : catalog.Tags("card", card);
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
            {
                if (!tags.TryGetValue(id, out var tag)) continue;
                Add(details, (string)tag["label"] ?? id, "stat");
                Add(details, (string)tag["blurb"] ?? "", "caption");
            }
            return body;
        }

        internal static string PreviewText(JObject preview, Func<string, string> name, OriginalContentCatalog catalog)
        {
            if ((bool?)preview?["available"] != true) return "Preview · " + ((string)preview?["reason"] ?? "Choose a legal target.");
            var lines = new List<string>();
            foreach (var change in (JArray)preview["changes"])
            {
                var values = new List<string>();
                foreach (var v in (JArray)change["values"])
                {
                    var label = (string)v["key"] == "energy" ? "actions" : (string)v["key"] == "block" ? "guard" : (string)v["key"] == "hp" ? "HP" : (string)v["key"];
                    values.Add(label + " " + v["before"] + " to " + v["after"]);
                }
                foreach (var s in (JArray)change["statuses"]) values.Add(OriginalStatusText.PreviewName(catalog.Record("statuses", (string)s["id"])) + " " + s["before"] + " to " + s["after"]);
                lines.Add(name((string)change["id"]) + ": " + string.Join(", ", values));
            }
            return "Preview · " + (lines.Count == 0 ? "No immediate pool or status change." : string.Join(" · ", lines));
        }

        internal static VisualElement Grid()
        {
            var grid = new VisualElement(); grid.AddToClassList("original-card-grid"); return grid;
        }
        internal static (string Id, VisualElement Element) DropTarget((string Id, VisualElement Element)[] targets, UnityEngine.Vector2 position, bool flick)
        {
            var direct = targets.FirstOrDefault(t => t.Element != null && t.Element.worldBound.Contains(position));
            if (direct.Element != null || !flick) return direct;
            return targets.Where(t => t.Element != null).OrderBy(t => UnityEngine.Vector2.Distance(t.Element.worldBound.center, position)).ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        internal static void Browse(VisualElement host, IEnumerable<JObject> instances, Func<JObject, JObject> resolve,
            Func<JObject, JObject> cost, OriginalContentCatalog catalog, string prefix, Action<JObject> inspect, Action report, OriginalCardBrowseState state = null)
        {
            state = state ?? new OriginalCardBrowseState();
            // Resolve each visible record once. Filtering is presentation-only and
            // alphabetical by default, including draw piles whose order is hidden.
            var rows = instances.Select(i => new { Instance = i, Card = resolve(i), Cost = cost(i) }).ToArray();
            var tools = new VisualElement(); tools.AddToClassList("original-card-browser-tools"); host.Add(tools);
            var search = new TextField("Search cards") { name = prefix + "-search", value = state.Query }; tools.Add(search);
            var types = new List<string> { "All types" }; types.AddRange(rows.Select(r => (string)r.Card["type"]).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t));
            var type = new DropdownField("Type", types, Math.Max(0, types.IndexOf(state.Type))) { name = prefix + "-type" }; tools.Add(type);
            var sorts = new List<string> { "Name", "Action cost", "Type" };
            var sort = new DropdownField("Sort", sorts, Math.Max(0, sorts.IndexOf(state.Sort))) { name = prefix + "-sort" }; tools.Add(sort);
            KeyboardChoices(type); KeyboardChoices(sort);
            var count = Add(host, "", "caption"); var grid = Grid(); host.Add(grid);
            Action refresh = () =>
            {
                grid.Clear();
                state.Query = search.value; state.Type = type.value; state.Sort = sort.value;
                var query = (search.value ?? "").Trim();
                var found = rows.Where(r => (type.index == 0 || (string)r.Card["type"] == type.value) &&
                    (query.Length == 0 || ((string)r.Card["name"] ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || OriginalCardText.Describe(r.Card, catalog).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
                var ordered = sort.index == 1 ? found.OrderBy(r => (int?)r.Cost["action"] ?? 0).ThenBy(r => (string)r.Card["name"], StringComparer.Ordinal)
                    : sort.index == 2 ? found.OrderBy(r => (string)r.Card["type"], StringComparer.Ordinal).ThenBy(r => (string)r.Card["name"], StringComparer.Ordinal)
                    : found.OrderBy(r => (string)r.Card["name"], StringComparer.Ordinal);
                var visible = ordered.ToArray(); count.text = visible.Length + " of " + rows.Length + " cards";
                foreach (var row in visible)
                {
                    Action read = () => inspect(row.Instance);
                    grid.Add(new OriginalCardView(catalog, row.Card, row.Cost, null, false, read, prefix + "-card-" + (row.Instance["instanceId"] ?? row.Instance["instance"]?["instanceId"]), inspect: read));
                }
                if (visible.Length == 0) Add(grid, "No cards match these filters.", "caption");
                report?.Invoke();
            };
            search.RegisterValueChangedCallback(_ => refresh()); type.RegisterValueChangedCallback(_ => refresh()); sort.RegisterValueChangedCallback(_ => refresh()); refresh();
        }

        private static void KeyboardChoices(DropdownField field)
        {
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != UnityEngine.KeyCode.UpArrow && e.keyCode != UnityEngine.KeyCode.DownArrow) return;
                field.index = (field.index + (e.keyCode == UnityEngine.KeyCode.DownArrow ? 1 : -1) + field.choices.Count) % field.choices.Count;
                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }

        private static Label Add(VisualElement host, string text, string style)
        {
            var label = new Label(text ?? "") { enableRichText = false }; label.AddToClassList(style); host.Add(label); return label;
        }
    }
}
