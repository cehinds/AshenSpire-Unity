// Core2RunHost.Map.cs — the act route strip and the act map (JS src/ui/components/actRouteStrip.js, mapboard.js,
// src/ui/screens/map.js): ENTRANCE → one slot per floor → BOSS; the board left to right by floor under fog (a hidden
// node is never built), reachable nodes ringed gold, a tap selects and opens the tray (Floor, kind, blurb, Back /
// "Enter <kind>"), a second tap or Enter travels. The footer bar holds Deck / Equipment / Stats / Menu.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    public sealed partial class Core2RunHost
    {
        private string _selectedNode;

        private void BuildMap(VisualElement screen, bool interactive)
        {
            var model = Core2MapModel.From(_flow.Data, _flow.Session);
            screen.Add(RouteStrip(model));
            var narrow = _root.ClassListContains("layout-narrow");
            // JS mapboard geometry at the reference sizes: the climb runs upward (floor 1 at the bottom, the bosses at the top),
            // the lanes across; 88 px rings 148 px apart on the wide layout (desktop-03), 50 px on the phone.
            var node = narrow ? 50f : 88f; // the phone ring stays at the 50 px tap floor CampaignView gives every button
            var stepX = narrow ? 70f : 148f;
            var stepY = narrow ? 84f : 140f;
            var pad = narrow ? 24f : 56f;
            var map = Core2Ui.Div("c2-map");
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            Core2Ui.Classes(scroll, "c2-map-scroll");
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            var board = Core2Ui.Div("c2-map-board");
            var maxCol = Math.Max(1, model.Nodes.Select(n => n.Col).DefaultIfEmpty(0).Max());
            var width = pad * 2 + stepX * (float)Math.Max(model.Columns - 1, maxCol) + node;
            var height = pad * 2 + stepY * Math.Max(1, model.Floors - 1) + node;
            board.style.width = width; board.style.height = height;
            Core2Ui.Surface(board, "Art/js1815/ui/map-island-act" + Math.Min(3, Math.Max(1, ((_flow.Session.Run.ActNumber - 1) % 3) + 1)));
            board.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
            Vector2 At(Core2MapNodeView n) => new Vector2(pad + stepX * (float)n.Col, pad + stepY * (model.Floors - n.Floor));
            var byId = model.Nodes.ToDictionary(n => n.Id);
            foreach (var (from, to, travelled) in model.Edges)
            {
                var a = At(byId[from]) + new Vector2(node / 2, node / 2);
                var b = At(byId[to]) + new Vector2(node / 2, node / 2);
                var edge = Core2Ui.Div("c2-map-edge" + (travelled ? " is-travelled" : ""));
                edge.pickingMode = PickingMode.Ignore;
                edge.style.left = a.x; edge.style.top = a.y;
                edge.style.width = Vector2.Distance(a, b);
                edge.style.rotate = new Rotate(new Angle(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, AngleUnit.Degree));
                board.Add(edge);
            }
            foreach (var n in model.Nodes)
            {
                var id = n.Id;
                var classes = "c2-map-node " + n.ShownType + (n.Reachable ? " is-reachable" : "") + (n.Visited ? " is-visited" : "") + (n.Current ? " is-current" : "") + (id == _selectedNode ? " is-selected" : "");
                var button = Core2Ui.Btn("map-node-" + id, "", () => Pick(id, interactive), classes, enabled: true, tooltip: n.Name);
                var p = At(n);
                button.style.left = p.x; button.style.top = p.y; button.style.width = node; button.style.height = node; button.style.minHeight = node;
                button.Add(Core2Ui.Icon(Core2Ui.NodeArt(n.ShownType), "c2-map-node-glyph"));
                button.focusable = interactive && n.Reachable;
                board.Add(button);
            }
            // A board narrower or shorter than the viewport sits centred on the parchment, as the JS board does.
            scroll.contentContainer.style.minWidth = Length.Percent(100);
            scroll.contentContainer.style.minHeight = Length.Percent(100);
            scroll.contentContainer.style.alignItems = Align.Center;
            scroll.contentContainer.style.justifyContent = Justify.Center;
            scroll.Add(board);
            map.Add(scroll);
            screen.Add(map);
            // Start the camera on the frontier (JS centerOnNode): the current node, else the entrance row.
            var focus = model.Nodes.FirstOrDefault(n => n.Current) ?? model.Nodes.FirstOrDefault(n => n.Reachable);
            if (focus != null) scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(Math.Max(0, At(focus).x + node / 2 - scroll.contentViewport.layout.width / 2), Math.Max(0, At(focus).y + node / 2 - scroll.contentViewport.layout.height / 2)));
            if (!interactive) return;
            screen.Add(Tray(model));
            var bar = Core2Ui.Div("c2-footbar");
            bar.Add(Core2Ui.Btn("open-deck", "D  Deck", () => Open(Overlay.Deck)));
            bar.Add(Core2Ui.Btn("open-equipment", "R  Equipment", () => Open(Overlay.Armoury)));
            bar.Add(Core2Ui.Btn("open-stats", "T  Stats", () => Open(Overlay.Armoury)));
            bar.Add(Core2Ui.Btn("open-quick-menu", "M  Menu", () => Open(Overlay.Menu)));
            screen.Add(bar);
        }

        /// <summary>JS pickMapNode: a reachable node selects; the selected one, tapped again, travels.</summary>
        private void Pick(string id, bool interactive)
        {
            if (!interactive || !_flow.Session.TravelOptions().Contains(id)) return;
            if (_selectedNode == id) { Travel(id); return; }
            _selectedNode = id;
            Render();
            _screen?.Q<Button>("map-enter")?.Focus();
        }

        private void Travel(string id)
        {
            _selectedNode = null;
            Run(() => _flow.Travel(id));
        }

        private VisualElement Tray(Core2MapModel model)
        {
            var selected = model.Nodes.FirstOrDefault(n => n.Id == _selectedNode);
            var ctx = Core2MapModel.Context(selected);
            var tray = Core2Ui.Div("c2-map-tray");
            if (ctx.Empty) tray.Add(Core2Ui.Text(ctx.Lines[0], "c2-caption"));
            else
            {
                tray.Add(Core2Ui.Text(ctx.Floor, "c2-micro"));
                tray.Add(Core2Ui.Text(ctx.Title, "c2-title-s"));
                foreach (var line in ctx.Lines) tray.Add(Core2Ui.Text(line, "c2-muted c2-text"));
                var pair = Core2Ui.Div("c2-map-tray-pair");
                pair.Add(Core2Ui.Btn("map-back", Core2Text.T("map.back"), () => { _selectedNode = null; Render(); }));
                pair.Add(Core2Ui.Btn("map-enter", ctx.EnterLabel, () => Travel(selected.Id), "c2-btn-primary", ctx.CanEnter));
                tray.Add(pair);
            }
            if (!string.IsNullOrEmpty(_status)) tray.Add(StatusLine());
            return tray;
        }

        private VisualElement RouteStrip(Core2MapModel model)
        {
            var strip = Core2Ui.Div("c2-route");
            strip.Add(Core2Ui.Text(model.ActTitle, "c2-route-title"));
            var rail = Core2Ui.Div("c2-route-rail");
            rail.Add(Core2Ui.Text(model.EntranceLabel, "c2-route-end"));
            foreach (var (floor, type, current) in model.Route)
            {
                rail.Add(Core2Ui.Div("c2-route-line"));
                var dot = Core2Ui.Div("c2-route-node" + (type != null ? " is-visited" : "") + (current ? " is-current" : ""));
                dot.tooltip = floor + ": " + (type != null ? Core2Text.NodeType(type).Name : "—");
                rail.Add(dot);
            }
            rail.Add(Core2Ui.Div("c2-route-line"));
            rail.Add(Core2Ui.Text(model.BossLabel, "c2-route-end c2-route-boss"));
            strip.Add(rail);
            return strip;
        }
    }
}
