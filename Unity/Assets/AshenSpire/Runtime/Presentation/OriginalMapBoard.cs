// OriginalMapBoard.cs — shared solo/co-op map; construct inside a bounded flex column.
// EDIT: names, colors and tap size in GameContent/Unity/Original/map-presentation.json.
// OriginalMapKnowledge owns visibility; OriginalMapViewport owns geometry. The choose
// callback is the only gameplay command. Camera/mode preferences are local display data.
// INPUT: one vertical drag or wheel scroll; dragging/cancellation never chooses a room.
// Detach cancels pointer capture and scheduled reports. No MonoBehaviour or global events.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class OriginalMapBoard : VisualElement
    {
        private readonly JObject _map, _authored;
        private readonly string[] _path;
        private readonly string _current, _identity, _scope, _prefix;
        private readonly int _act;
        private readonly bool _reveal, _diagnostics;
        private readonly HashSet<string> _legal;
        private readonly IDictionary<string, string[]> _votes;
        private readonly OriginalMapViewServices _services;
        private readonly Action<string> _choose;
        private readonly VisualElement _viewport = new VisualElement(), _content = new VisualElement();
        private readonly VisualElement _overlay = new VisualElement();
        private readonly Label _hint = new Label(), _detail = new Label();
        private readonly Button _fit, _modeButton, _glowButton;
        private OriginalMapKnowledge.Projection _knowledge;
        private OriginalMapViewport.Projection _camera;
        private OriginalMapViewport.Snapshot _saved;
        private string _mode, _setting;
        private bool _glow, _detached, _dragged, _cancelled;
        private double _scroll, _scale = 1;
        private int _pointer = -1;
        private Vector2 _down;
        private double _downScroll;
        private string _downNode;
        private float _suppressUntil;
        private readonly HashSet<int> _pointers = new HashSet<int>();
        private IVisualElementScheduledItem _reportJob;
        private static int _diagnosticSequence;

        public OriginalMapBoard(JObject map, IEnumerable<string> path, string currentId,
            IEnumerable<string> legalIds, int actNumber, string identity, string scope,
            bool revealUnknown, OriginalMapViewServices services, Action<string> choose,
            bool diagnostics = false, IDictionary<string, string[]> votes = null)
        {
            _map = (JObject)map.DeepClone(); _path = (path ?? Array.Empty<string>()).ToArray();
            _current = currentId; _legal = new HashSet<string>(legalIds ?? Array.Empty<string>());
            _act = actNumber; _identity = identity; _scope = scope; _reveal = scope == "solo" && revealUnknown;
            _prefix = scope == "coop" ? "coop" : "native"; _services = services ?? new OriginalMapViewServices();
            _choose = choose; _diagnostics = diagnostics; _votes = votes ?? new Dictionary<string, string[]>();
            _authored = JObject.Parse(Resources.Load<TextAsset>("Original/map-presentation").text);
            var preferences = _services.Read?.Invoke(scope) ?? new JObject();
            string Preference(string key) => preferences[key]?.Type == JTokenType.String ? (string)preferences[key] : null;
            _mode = scope == "coop" ? "path" : Preference("mode") == "path" ? "path" : "fog";
            _setting = OriginalMapViewport.SavedZoom(Preference("setting")).HasValue ? Preference("setting") : "Fit";
            _glow = preferences["shrineGlow"]?.Type != JTokenType.Boolean || (bool)preferences["shrineGlow"];
            if (Preference("identity") == identity) _saved = OriginalMapViewport.Snapshot.FromJson(preferences["camera"]);
            name = _prefix + "-map"; AddToClassList("map-board");
            var toolbar = Row(); Add(toolbar);
            Control(toolbar, "zoom-out", "-", () => Zoom(-1)).tooltip = "Zoom out";
            _fit = Control(toolbar, "fit", "Fit", () => { _setting = "Fit"; Place(true); });
            Control(toolbar, "zoom-in", "+", () => Zoom(1)).tooltip = "Zoom in";
            Control(toolbar, "recenter", "Recenter", () => { _setting = "Fit"; Place(true); });
            var options = Row(); Add(options);
            if (scope == "solo") _modeButton = Control(options, "mode", ModeLabel, () => { _mode = _mode == "fog" ? "path" : "fog"; _modeButton.text = ModeLabel; Place(true); });
            _glowButton = Control(options, "glow", GlowLabel, () => { _glow = !_glow; _glowButton.text = GlowLabel; Place(); });
            Control(options, "routes", "Routes", Routes);
            Control(options, "legend", "Key", Legend);
            _viewport.name = _prefix + "-map-viewport"; _viewport.AddToClassList("map-viewport"); _viewport.focusable = true;
            _content.AddToClassList("map-content"); _viewport.Add(_content); Add(_viewport);
            _overlay.AddToClassList("map-overlay"); _overlay.style.display = DisplayStyle.None; _viewport.Add(_overlay);
            _hint.AddToClassList("map-hint"); Add(_hint); _detail.AddToClassList("map-detail"); Add(_detail);
            _detail.text = "Tap a lit route to climb. Drag to explore the map.";
            _viewport.RegisterCallback<GeometryChangedEvent>(_ => Place());
            _viewport.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
            _viewport.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
            _viewport.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
            _viewport.RegisterCallback<PointerCancelEvent>(Cancel, TrickleDown.TrickleDown);
            _viewport.RegisterCallback<PointerCaptureOutEvent>(e => { if (e.pointerId == _pointer) CancelGesture(); });
            _viewport.RegisterCallback<WheelEvent>(Wheel, TrickleDown.TrickleDown);
            _viewport.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            RegisterCallback<AttachToPanelEvent>(_ => { _detached = false; });
            RegisterCallback<DetachFromPanelEvent>(_ => { Persist(); _detached = true; _reportJob?.Pause(); CancelGesture(); });
        }
        private string ModeLabel => _mode == "fog" ? "Fog" : "Paths";
        private string GlowLabel => _glow ? "Shrines on" : "Shrines off";
        public void RefreshDisplayScale()
        {
            if (_camera == null || Math.Abs(_scale - (_services.DisplayScale?.Invoke() ?? 1)) < .0001) return;
            Place(_camera.Framing == "fit");
        }
        public void CancelGesture()
        {
            _suppressUntil = Time.realtimeSinceStartup + .3f;
            Release();
            foreach (var pointer in _pointers.ToArray()) if (_viewport.HasPointerCapture(pointer)) _viewport.ReleasePointer(pointer);
            _pointers.Clear(); _dragged = _cancelled = false;
        }
        private bool DecisionFits => _camera != null && _knowledge.Nodes.Where(n => n.Current || _legal.Contains(n.Id)).All(n => {
            var b = _camera.NodeBounds(n.Id, false);
            return b.X0 >= -.5 && b.X1 <= _viewport.contentRect.width + .5 && b.Y0 - _scroll >= -.5 && b.Y1 - _scroll <= _viewport.contentRect.height + .5;
        });
        private static VisualElement Row() { var row = new VisualElement(); row.AddToClassList("map-tools"); return row; }
        private Button Control(VisualElement parent, string id, string text, Action action)
        {
            var button = new Button(action) { name = _prefix + "-map-" + id, text = text };
            button.AddToClassList("button"); button.AddToClassList("map-tool"); parent.Add(button); return button;
        }
        private void Zoom(int direction)
        {
            if (_camera == null) return;
            var zoom = OriginalMapViewport.StepZoom(_camera.Zoom, direction);
            _setting = (zoom * 100).ToString("0", CultureInfo.InvariantCulture); Place(false, zoom);
        }
        private void Place(bool recenter = false, double? zoom = null)
        {
            var width = _viewport.contentRect.width; var height = _viewport.contentRect.height;
            if (_detached || width < 1 || height < 1 || float.IsNaN(width) || float.IsNaN(height)) return;
            if (recenter) _setting = "Fit";
            _scale = Math.Max(.1, _services.DisplayScale?.Invoke() ?? 1);
            _knowledge = OriginalMapKnowledge.Project(_map, _path, _current, _mode == "fog", _reveal, _glow);
            var layout = new OriginalMapViewport(OriginalMapViewport.NodesFromMap(_map), (int)_map["columns"], (double)_authored["tapPixels"], _scale);
            _camera = layout.Project(new OriginalMapViewport.Request {
                ActNumber = _act, CurrentId = _current, ReachableIds = _legal, VisibleIds = _knowledge.VisibleIds,
                StartIds = (_map["startIds"] as JArray ?? new JArray()).Values<string>(), Width = width, Height = height,
                Setting = _setting, SavedState = _saved, ForceRecenter = recenter, ManualZoom = zoom
            });
            _scroll = _camera.ScrollTop; _saved = _camera.CaptureScroll(_scroll);
            _fit.text = _setting == "Fit" ? "Fit" : _setting + "%";
            _content.Clear(); _content.style.width = width; _content.style.height = (float)_camera.ContentHeight;
            var lines = new VisualElement { pickingMode = PickingMode.Ignore }; lines.AddToClassList("map-lines");
            lines.generateVisualContent += DrawEdges; _content.Add(lines);
            foreach (var node in _knowledge.Nodes)
            {
                var id = node.Id; var row = Room(node.ShownType); var legal = _legal.Contains(id);
                var bounds = _camera.NodeBounds(id, false);
                var state = node.Current ? "Current" : node.Visited ? "Visited" : legal ? "Available" : "Not connected";
                var button = new Button(() => Choose(id)) { name = _prefix + "-route-" + id, tooltip = (string)row["name"] + " · " + state + "\n" + row["description"] };
                button.AddToClassList("map-node"); button.EnableInClassList("available", legal); button.EnableInClassList("visited", node.Visited); button.EnableInClassList("current", node.Current); button.EnableInClassList("revealed", node.Revealed); button.EnableInClassList("shrine-lane", node.ShrineLane);
                button.style.left = (float)bounds.X0; button.style.top = (float)bounds.Y0; button.style.width = (float)bounds.Width; button.style.height = (float)bounds.Height;
                ColorUtility.TryParseHtmlString((string)row["color"], out var color);
                var icon = new MapIcon((string)row["icon"], node.Current ? new Color(.18f,.16f,.12f) : color); button.Add(icon);
                button.SetEnabled(legal); _content.Add(button);
                if (node.Current || node.Revealed || _votes.ContainsKey(id))
                {
                    var label = new Label(_votes.TryGetValue(id, out var names) ? string.Join(", ", names) : node.Current ? "YOU" : "REVEALED") { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("map-marker"); label.style.left = (float)bounds.X0 - 24; label.style.top = (float)bounds.Y1 + 2; label.style.width = (float)bounds.Width + 48; _content.Add(label);
                }
                button.RegisterCallback<PointerEnterEvent>(_ => _detail.text = (string)row["name"] + " · " + state + ". " + row["description"]);
                button.RegisterCallback<FocusInEvent>(_ => _detail.text = (string)row["name"] + " · " + state + ". " + row["description"]);
            }
            Scroll(_scroll);
        }
        private JObject Room(string type) => (JObject)(_authored["rooms"]?[type ?? "event"] ?? _authored["rooms"]["event"]);
        private void Choose(string id, bool fromOverlay = false)
        {
            if (_detached || _dragged || _cancelled || !fromOverlay && _overlay.style.display == DisplayStyle.Flex || Time.realtimeSinceStartup < _suppressUntil || !_legal.Contains(id)) return;
            Persist(); _choose?.Invoke(id);
        }
        private void DrawEdges(MeshGenerationContext context)
        {
            if (_camera == null || _knowledge == null) return;
            var painter = context.painter2D;
            foreach (var edge in _knowledge.Edges)
            {
                var a = _camera.NodeCenter(edge.From, false); var b = _camera.NodeCenter(edge.To, false);
                painter.lineWidth = edge.Traveled ? 3 : edge.ShrineLane ? 2.5f : 1.5f;
                painter.strokeColor = edge.Traveled ? new Color(.83f,.64f,.35f) : edge.ShrineLane ? new Color(.39f,.70f,.69f,.8f) : new Color(.47f,.44f,.40f,.55f);
                painter.BeginPath(); painter.MoveTo(new Vector2((float)a.X,(float)a.Y)); painter.LineTo(new Vector2((float)b.X,(float)b.Y)); painter.Stroke();
            }
        }
        private void Scroll(double value)
        {
            if (_camera == null) return;
            _scroll = Math.Max(0, Math.Min(_camera.MaximumScrollTop, value)); _content.style.top = -(float)_scroll;
            _hint.text = "ACT " + _act + " · " + (_mode == "fog" ? "FOG OF WAR" : "ALL PATHS") + " · " + _legal.Count + " routes" + (DecisionFits ? "" : " · Use Routes for off-screen choices");
            _saved = _camera.CaptureScroll(_scroll); Persist(); ReportSoon();
        }
        private void Persist()
        {
            if (_saved == null) return;
            _services.Write?.Invoke(_scope, new JObject { ["identity"] = _identity, ["mode"] = _mode,
                ["setting"] = _setting, ["shrineGlow"] = _glow, ["camera"] = _saved.ToJson() });
        }
        private static string NodeAt(VisualElement target)
        {
            for (var node = target; node != null; node = node.parent)
                if (node.ClassListContains("map-node")) return node.name;
            return null;
        }
        private void Down(PointerDownEvent e)
        {
            if (_overlay.style.display == DisplayStyle.Flex || e.button != 0) return;
            _pointers.Add(e.pointerId);
            _viewport.CapturePointer(e.pointerId);
            if (_pointers.Count > 1) { _cancelled = true; e.StopImmediatePropagation(); return; }
            _pointer = e.pointerId; _down = e.position; _downScroll = _scroll; _dragged = _cancelled = false; _downNode = NodeAt(e.target as VisualElement);
            // Own the whole gesture, including releases outside this viewport.
            // Buttons retain keyboard activation; pointer travel commits only on Up.
            e.StopImmediatePropagation();
        }
        private void Move(PointerMoveEvent e)
        {
            if (e.pointerId != _pointer || _pointer < 0) return;
            var delta = (Vector2)e.position - _down;
            if (delta.magnitude * _scale > 8) _dragged = true;
            if (!_dragged && !_cancelled) return;
            if (!_viewport.HasPointerCapture(_pointer)) _viewport.CapturePointer(_pointer);
            if (!_cancelled) Scroll(_downScroll - delta.y);
            e.StopImmediatePropagation();
        }
        private void Up(PointerUpEvent e)
        {
            _pointers.Remove(e.pointerId);
            if (e.pointerId != _pointer) { if (_viewport.HasPointerCapture(e.pointerId)) _viewport.ReleasePointer(e.pointerId); e.StopImmediatePropagation(); return; }
            var outside = !_viewport.worldBound.Contains(e.position);
            var node = _downNode == null ? null : _content.Q<Button>(_downNode);
            var valid = !_dragged && !_cancelled && !outside && node != null && node.enabledInHierarchy && node.worldBound.Contains(e.position);
            if (!valid) _suppressUntil = Time.realtimeSinceStartup + .3f;
            e.StopImmediatePropagation();
            Release();
            _dragged = _cancelled = false;
            if (valid) Choose(_downNode.Substring((_prefix + "-route-").Length));
        }
        private void Cancel(PointerCancelEvent e)
        {
            CancelGesture(); e.StopImmediatePropagation();
        }
        private void Release()
        {
            var pointer = _pointer; _pointer = -1;
            if (pointer >= 0 && _viewport.HasPointerCapture(pointer)) _viewport.ReleasePointer(pointer);
        }
        private void Wheel(WheelEvent e)
        {
            if (_overlay.style.display == DisplayStyle.Flex) return;
            if (e.ctrlKey || e.commandKey) Zoom(e.delta.y < 0 ? 1 : -1); else Scroll(_scroll + e.delta.y * 22 / _scale);
            e.StopImmediatePropagation();
        }
        private void Key(KeyDownEvent e)
        {
            if (e.target != _viewport) return;
            if (e.keyCode == KeyCode.PageUp) Scroll(_scroll - _viewport.contentRect.height * .8);
            else if (e.keyCode == KeyCode.PageDown) Scroll(_scroll + _viewport.contentRect.height * .8);
            else if (e.keyCode == KeyCode.Home) Scroll(0);
            else if (e.keyCode == KeyCode.End) Scroll(_camera?.MaximumScrollTop ?? 0);
            else return;
            e.StopImmediatePropagation();
        }
        private VisualElement OpenOverlay(string title)
        {
            _overlay.Clear(); _overlay.style.display = DisplayStyle.Flex;
            CancelGesture(); _content.SetEnabled(false);
            Control(_overlay, "close", "Back to map · " + title, () => { _overlay.Clear(); _overlay.style.display = DisplayStyle.None; _content.SetEnabled(true); ReportSoon(); });
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow = 1; _overlay.Add(scroll); return scroll;
        }
        private void Routes()
        {
            if (_knowledge == null) return;
            var list = OpenOverlay("Routes");
            foreach (var id in _legal)
            {
                var node = _knowledge.Nodes.First(n => n.Id == id); var row = Room(node.ShownType);
                Control(list, "choice-" + id, "Floor " + _map["nodes"][id]["floor"] + " · " + row["name"], () => Choose(id, true));
            }
            ReportSoon();
        }
        private void Legend()
        {
            var list = OpenOverlay("Map key");
            foreach (var type in new[] { "monster", "elite", "boss", "shrine", "merchant", "treasure", "event" })
            {
                var row = Room(type); var line = Row(); ColorUtility.TryParseHtmlString((string)row["color"], out var color);
                var icon = new MapIcon((string)row["icon"], color); icon.style.width = 38; icon.style.height = 38; icon.style.flexShrink = 0; line.Add(icon);
                var text = new Label(row["name"] + " - " + row["description"]); text.AddToClassList("map-key-text"); line.Add(text); list.Add(line);
            }
            var note = new Label("Gold route: traveled. Teal route: nearest shrine, where already visible. Bright ring: available. YOU: current. Revealed ring: Sealstone knowledge. Fog remembers the places and branches you have seen."); note.AddToClassList("map-key-text"); list.Add(note); ReportSoon();
        }
        private void ReportSoon()
        {
            _reportJob?.Pause(); _reportJob = schedule.Execute(() => {
                if (_detached) return; _services.Report?.Invoke();
                if (!_diagnostics || _camera == null) return;
                var nodes = new JArray(_knowledge.Nodes.Select(node => { var b = _content.Q<Button>(_prefix + "-route-" + node.Id).worldBound;
                    return new JObject { ["id"] = node.Id, ["type"] = node.ShownType, ["current"] = node.Current, ["visited"] = node.Visited, ["revealed"] = node.Revealed, ["legal"] = _legal.Contains(node.Id), ["x"] = b.x, ["y"] = b.y, ["width"] = b.width, ["height"] = b.height }; }));
                var rect = _viewport.worldBound;
                // Keep fixed chunks ASCII-safe even when future labels use emoji.
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(new JObject { ["scope"] = _scope, ["mode"] = _mode, ["shrineGlow"] = _glow, ["camera"] = _saved.ToJson(), ["decisionFits"] = DecisionFits, ["displayScale"] = _scale,
                    ["viewport"] = new JObject { ["x"] = rect.x, ["y"] = rect.y, ["width"] = rect.width, ["height"] = rect.height }, ["nodes"] = nodes,
                    ["edges"] = new JArray(_knowledge.Edges.Select(e => new JObject { ["from"] = e.From, ["to"] = e.To, ["traveled"] = e.Traveled, ["shrine"] = e.ShrineLane })) },
                    new Newtonsoft.Json.JsonSerializerSettings { StringEscapeHandling = Newtonsoft.Json.StringEscapeHandling.EscapeNonAscii });
                var sequence = ++_diagnosticSequence; var count = (json.Length + 2499) / 2500;
                for (var i = 0; i < count; i++) Debug.Log("ASHENSPIRE_MAP_VIEW_CHUNK " + new JObject { ["sequence"] = sequence, ["index"] = i, ["count"] = count, ["text"] = json.Substring(i * 2500, Math.Min(2500, json.Length - i * 2500)) }.ToString(Newtonsoft.Json.Formatting.None));
            }).StartingIn(100);
        }
        // Small deterministic map glyphs avoid platform-dependent emoji/font fallback.
        private sealed class MapIcon : VisualElement
        {
            private readonly string _icon; private readonly Color _color;
            public MapIcon(string icon, Color color) { _icon = icon; _color = color; pickingMode = PickingMode.Ignore; AddToClassList("map-icon"); generateVisualContent += Draw; }
            private void Draw(MeshGenerationContext context)
            {
                var p = context.painter2D; var s = Math.Min(contentRect.width, contentRect.height); var origin = new Vector2((contentRect.width-s)/2,(contentRect.height-s)/2);
                Vector2 V(float x,float y) => origin + new Vector2(x*s,y*s);
                void Line(params float[] points) { p.BeginPath(); p.MoveTo(V(points[0],points[1])); for (var i=2;i<points.Length;i+=2) p.LineTo(V(points[i],points[i+1])); p.Stroke(); }
                void Circle(float x,float y,float r) { p.BeginPath(); for(var i=0;i<=32;i++) { var a=i*Math.PI/16; var v=V(x+(float)Math.Cos(a)*r,y+(float)Math.Sin(a)*r); if(i==0)p.MoveTo(v);else p.LineTo(v); } p.Stroke(); }
                p.strokeColor = _color; p.lineWidth = Math.Max(1.5f,s*.05f); p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                switch(_icon)
                {
                    case "swords": Line(.2f,.8f,.75f,.2f,.8f,.2f,.8f,.25f,.2f,.8f); Line(.2f,.2f,.8f,.8f); Line(.2f,.62f,.38f,.8f); Line(.62f,.8f,.8f,.62f); break;
                    case "skull": Circle(.5f,.4f,.26f); Line(.32f,.62f,.32f,.8f,.68f,.8f,.68f,.62f); Circle(.4f,.4f,.04f); Circle(.6f,.4f,.04f); Line(.45f,.65f,.45f,.8f); Line(.55f,.65f,.55f,.8f); break;
                    case "eye": Line(.12f,.5f,.3f,.28f,.5f,.22f,.7f,.28f,.88f,.5f,.7f,.72f,.5f,.78f,.3f,.72f,.12f,.5f); Circle(.5f,.5f,.16f); break;
                    case "flame": Line(.5f,.12f,.72f,.4f,.77f,.6f,.65f,.8f,.35f,.8f,.23f,.6f,.3f,.42f,.4f,.55f,.5f,.12f); Line(.4f,.8f,.43f,.6f,.52f,.49f,.62f,.8f); break;
                    case "scales": Line(.5f,.18f,.5f,.82f); Line(.27f,.82f,.73f,.82f); Line(.2f,.32f,.8f,.32f); Line(.25f,.32f,.12f,.62f,.38f,.62f,.25f,.32f); Line(.75f,.32f,.62f,.62f,.88f,.62f,.75f,.32f); break;
                    case "chest": Line(.18f,.35f,.26f,.22f,.74f,.22f,.82f,.35f,.82f,.77f,.18f,.77f,.18f,.35f,.82f,.35f); Line(.47f,.35f,.47f,.53f,.56f,.53f,.56f,.35f); break;
                    default: Line(.3f,.3f,.35f,.2f,.6f,.2f,.7f,.3f,.7f,.42f,.5f,.55f,.5f,.62f); Circle(.5f,.8f,.025f); break;
                }
            }
        }
    }
}
