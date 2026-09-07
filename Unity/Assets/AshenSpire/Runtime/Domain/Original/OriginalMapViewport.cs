// OriginalMapViewport.cs — pure map geometry and local viewer camera, never run rules.
// EDIT: geometry/aim policy here; pass authored tap size, actual display scale and
// measured title band from the UI. Width/Height are bounded panel units, not Screen pixels.
// Coordinates: graph -> content (scaled) -> viewport (subtract ScrollTop only).
// Persist Snapshot synchronously with its original act/node/setting identity; a
// later debounce must not relabel it. Invalid/stale saved cameras are recomputed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalMapViewport
    {
        public const double ColumnPitch = 75, RowPitch = 79, ReferenceZoom = 1.15;
        public const double MinimumZoom = 1, MaximumZoom = 2, BossRatio = 20.0 / 15;
        private static readonly double[] Ladder = { 1, 1.15, 1.3, 1.5, 1.75, 2 };
        public static double[] ZoomSteps => (double[])Ladder.Clone();
        public sealed class Node
        {
            public string Id { get; set; }
            public string Type { get; set; }
            public int Column { get; set; }
            public int Floor { get; set; }
            public string[] NextIds { get; set; } = Array.Empty<string>();
        }
        public readonly struct Point
        {
            public double X { get; }
            public double Y { get; }
            public Point(double x, double y) { X = x; Y = y; }
        }
        public readonly struct Rect
        {
            public double X0 { get; }
            public double Y0 { get; }
            public double X1 { get; }
            public double Y1 { get; }
            public double Width => X1 - X0;
            public double Height => Y1 - Y0;
            public Point Center => new Point((X0 + X1) / 2, (Y0 + Y1) / 2);
            public Rect(double x0, double y0, double x1, double y1)
            {
                if (!Finite(x0) || !Finite(y0) || !Finite(x1) || !Finite(y1) || x1 < x0 || y1 < y0) throw new ArgumentException("Invalid map rectangle");
                X0 = x0; Y0 = y0; X1 = x1; Y1 = y1;
            }
        }
        public sealed class Snapshot
        {
            public int ActNumber { get; set; }
            public string NodeId { get; set; }
            public string Setting { get; set; }
            public double Zoom { get; set; }
            public string Framing { get; set; }
            public double ScrollLeft { get; set; }
            public double ScrollTop { get; set; }
            public double AimX { get; set; }
            public double ViewportWidth { get; set; }
            public double ViewportHeight { get; set; }
            public JObject ToJson() => new JObject { ["actNumber"] = ActNumber, ["nodeId"] = NodeId,
                ["setting"] = Setting, ["zoom"] = Zoom, ["framing"] = Framing, ["scrollLeft"] = ScrollLeft,
                ["scrollTop"] = ScrollTop, ["aimX"] = AimX, ["viewportWidth"] = ViewportWidth, ["viewportHeight"] = ViewportHeight };
            public static Snapshot FromJson(JToken token)
            {
                try { return ReadJson(token); }
                catch (Exception error) when (error is OverflowException || error is FormatException || error is InvalidCastException || error is ArgumentException) { return null; }
            }
            private static Snapshot ReadJson(JToken token)
            {
                if (!(token is JObject value)) return null;
                bool Number(string key) => value[key] != null && (value[key].Type == JTokenType.Integer || value[key].Type == JTokenType.Float) && Finite((double)value[key]);
                if (!new[] { "actNumber", "zoom", "scrollLeft", "scrollTop", "aimX", "viewportWidth", "viewportHeight" }.All(Number)) return null;
                if (value["nodeId"] != null && value["nodeId"].Type != JTokenType.Null && value["nodeId"].Type != JTokenType.String) return null;
                if (value["setting"]?.Type != JTokenType.String || value["framing"]?.Type != JTokenType.String) return null;
                var act = (double)value["actNumber"];
                if (act < 1 || act > int.MaxValue || act != Math.Floor(act)) return null;
                var result = new Snapshot { ActNumber = (int)act, NodeId = (string)value["nodeId"], Setting = (string)value["setting"],
                    Zoom = (double)value["zoom"], Framing = (string)value["framing"], ScrollLeft = (double)value["scrollLeft"],
                    ScrollTop = (double)value["scrollTop"], AimX = (double)value["aimX"], ViewportWidth = (double)value["viewportWidth"], ViewportHeight = (double)value["viewportHeight"] };
                return Valid(result) ? result : null;
            }
        }
        public sealed class Request
        {
            public int ActNumber { get; set; } = 1;
            public string CurrentId { get; set; }
            public IEnumerable<string> ReachableIds { get; set; } = Array.Empty<string>();
            public IEnumerable<string> VisibleIds { get; set; }
            public IEnumerable<string> StartIds { get; set; } = Array.Empty<string>();
            public double Width { get; set; }
            public double Height { get; set; }
            public string Setting { get; set; } = "Fit";
            public Snapshot SavedState { get; set; }
            public Rect? TitleBand { get; set; }
            public double HaloPad { get; set; } = 6;
            public double HaloPeak { get; set; } = 1.35;
            // For +/-: bypass restoration, apply manual zoom and recenter using
            // the original policy. Recenter/Fit uses ForceRecenter with no override.
            public double? ManualZoom { get; set; }
            public bool ForceRecenter { get; set; }
        }
        public sealed class Projection
        {
            private readonly OriginalMapViewport _owner;
            internal Projection(OriginalMapViewport owner) { _owner = owner; }
            public double Zoom { get; internal set; }
            public string Framing { get; internal set; }
            public double AimX { get; internal set; }
            public double ScrollTop { get; internal set; }
            public double ScrollLeft => 0;
            public Rect Content { get; internal set; }
            public double Width { get; internal set; }
            public double Height { get; internal set; }
            public double ContentWidth => Width;
            public double ContentHeight => Content.Height * Zoom;
            public double MaximumScrollTop => Math.Max(0, ContentHeight - Height);
            public double DecisionMiss { get; internal set; }
            public double EntranceMiss { get; internal set; }
            public bool DecisionFits => DecisionMiss <= .5;
            public bool? EntranceEndsFit { get; internal set; }
            public bool TitleVisible { get; internal set; } = true;
            public bool Restored { get; internal set; }
            public Snapshot State { get; internal set; }
            public Point ToContent(Point p) => new Point((p.X - Content.X0) * Zoom, (p.Y - Content.Y0) * Zoom);
            public Point ToViewport(Point p) { var c = ToContent(p); return new Point(c.X, c.Y - ScrollTop); }
            public Point NodeCenter(string id, bool viewport = true) => viewport ? ToViewport(_owner.Position(id)) : ToContent(_owner.Position(id));
            public Rect NodeBounds(string id, bool viewport = true)
            {
                var c = NodeCenter(id, viewport); var r = _owner.Radius(id) * Zoom;
                return new Rect(c.X - r, c.Y - r, c.X + r, c.Y + r);
            }
            public double DeliveredNodePixels(double actualDisplayScale) { Positive(actualDisplayScale, "display scale"); return 2 * _owner.NodeRadius * Zoom * actualDisplayScale; }
            public Snapshot CaptureScroll(double scrollTop)
            {
                if (!Finite(scrollTop)) throw new ArgumentException("Invalid scroll position");
                var state = Snapshot.FromJson(State.ToJson());
                state.ScrollTop = Math.Min(MaximumScrollTop, Math.Max(0, scrollTop));
                return state;
            }
        }
        private readonly Dictionary<string, Node> _nodes;
        public double MapWidth { get; }
        public double MapHeight { get; }
        public double NodeRadius { get; }
        public double BossRadius { get; }
        public OriginalMapViewport(IEnumerable<Node> nodes, int columns, double authoredTapPixels, double referenceDisplayScale = 1)
        {
            Positive(authoredTapPixels, "tap size"); Positive(referenceDisplayScale, "display scale");
            if (columns < 1 || nodes == null) throw new ArgumentException("Invalid map geometry");
            _nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (var n in nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.Id) || n.Column < 0 || n.Column >= columns || n.Floor < 0 || _nodes.ContainsKey(n.Id)) throw new ArgumentException("Invalid map node");
                _nodes.Add(n.Id, new Node { Id = n.Id, Type = n.Type, Column = n.Column, Floor = n.Floor, NextIds = (n.NextIds ?? Array.Empty<string>()).ToArray() });
            }
            if (_nodes.Count == 0) throw new ArgumentException("Map must contain nodes");
            MapWidth = columns * ColumnPitch + 60; MapHeight = (_nodes.Values.Max(n => n.Floor) + 1.0) * RowPitch + 30;
            NodeRadius = Math.Floor(authoredTapPixels / (2 * ReferenceZoom * referenceDisplayScale) * 10 + .5) / 10;
            BossRadius = Math.Floor(NodeRadius * BossRatio * 10 + .5) / 10;
            Positive(NodeRadius, "derived node radius"); Positive(BossRadius, "derived boss radius");
        }
        public static Node[] NodesFromMap(JObject map)
        {
            if (map == null) throw new ArgumentException("Map is required");
            var rows = map["nodes"] is JObject byId ? byId.Properties().Select(p => p.Value) : (map["nodes"] as JArray)?.AsEnumerable();
            if (rows == null) throw new ArgumentException("Map nodes are required");
            return rows.Select(token =>
            {
                if (!(token is JObject n) || n["id"]?.Type != JTokenType.String || n["type"]?.Type != JTokenType.String) throw new ArgumentException("Invalid map node identity");
                int Integer(string key) { var v = n[key]; if (v == null || v.Type != JTokenType.Integer || (double)v < 0 || (double)v > int.MaxValue) throw new ArgumentException("Invalid map node " + key); return (int)v; }
                if (n["next"] != null && n["next"].Type != JTokenType.Null && !(n["next"] is JArray)) throw new ArgumentException("Invalid map edges");
                var next = n["next"] as JArray;
                if (next != null && next.Any(x => x.Type != JTokenType.String)) throw new ArgumentException("Invalid map edges");
                return new Node { Id = (string)n["id"], Type = (string)n["type"], Column = Integer("col"), Floor = Integer("floor"), NextIds = next?.Values<string>().ToArray() ?? Array.Empty<string>() };
            }).ToArray();
        }
        public Point Position(string id) { var n = _nodes[id]; return new Point(60 + n.Column * ColumnPitch, MapHeight - n.Floor * RowPitch); }
        public double Radius(string id) => _nodes[id].Type == "boss" ? BossRadius : NodeRadius;
        public Rect? FramingBox(IEnumerable<string> ids)
        {
            var list = (ids ?? Array.Empty<string>()).Where(id => id != null && _nodes.ContainsKey(id)).Distinct().ToArray();
            if (list.Length == 0) return null;
            return new Rect(list.Min(id => Position(id).X - Radius(id)), list.Min(id => Position(id).Y - Radius(id)), list.Max(id => Position(id).X + Radius(id)), list.Max(id => Position(id).Y + Radius(id)));
        }
        public static double FitZoom(Rect? box, double width, double height)
        {
            Positive(width, "viewport width"); Positive(height, "viewport height");
            return !box.HasValue || box.Value.Width <= 0 || box.Value.Height <= 0 ? MaximumZoom : Math.Min(width / box.Value.Width, height / box.Value.Height);
        }
        public static double RadiusForTap(double tapPixels, double zoom, double displayScale)
        {
            Positive(tapPixels, "tap size"); Positive(zoom, "zoom"); Positive(displayScale, "display scale");
            var result = tapPixels / (2 * zoom * displayScale); Positive(result, "derived radius"); return result;
        }
        public static double ClampZoom(double zoom) { if (!Finite(zoom)) throw new ArgumentException("Invalid zoom"); return Math.Min(MaximumZoom, Math.Max(MinimumZoom, zoom)); }
        public static double StepZoom(double zoom, int direction)
        {
            if (!Finite(zoom) || (direction != -1 && direction != 1)) throw new ArgumentException("Invalid zoom step");
            var index = Array.FindIndex(Ladder, z => Math.Abs(z - zoom) < .001);
            return Ladder[Math.Min(Ladder.Length - 1, Math.Max(0, (index < 0 ? 1 : index) + direction))];
        }
        public static double? SavedZoom(string setting)
        {
            if (setting == null || setting == "Fit" || !double.TryParse(setting, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !Finite(n) || n <= 0) return null;
            var z = n / 100; return Ladder.Aggregate((a, b) => Math.Abs(b - z) < Math.Abs(a - z) ? b : a);
        }
        public Projection Project(Request request)
        {
            if (request == null || request.ActNumber < 1) throw new ArgumentException("Invalid viewport request");
            Positive(request.Width, "viewport width"); Positive(request.Height, "viewport height");
            if (!Finite(request.HaloPad) || request.HaloPad < 0 || !Finite(request.HaloPeak) || request.HaloPeak < 1) throw new ArgumentException("Invalid halo metrics");
            var visible = new HashSet<string>(request.VisibleIds ?? _nodes.Keys);
            var reachable = new HashSet<string>(request.ReachableIds ?? Array.Empty<string>());
            var current = request.CurrentId != null && _nodes.ContainsKey(request.CurrentId) ? request.CurrentId : null;
            var decision = _nodes.Keys.Where(reachable.Contains).ToList(); if (current != null) decision.Insert(0, current);
            var context = new HashSet<string>(decision);
            foreach (var id in decision) foreach (var next in _nodes[id].NextIds) if (_nodes.ContainsKey(next) && visible.Contains(next)) context.Add(next);
            var ink = FramingBox(visible) ?? new Rect(0, 0, MapWidth, MapHeight);
            var box = FramingBox(decision); var setting = request.Setting ?? "Fit";
            var saved = request.SavedState;
            var restored = !request.ForceRecenter && !request.ManualZoom.HasValue && Valid(saved) && Finite((MapWidth + Math.Abs(saved.AimX) + request.Width) * MaximumZoom) && saved.ActNumber == request.ActNumber && saved.NodeId == request.CurrentId && saved.Setting == setting &&
                (saved.Framing != "fit" || Math.Abs(saved.ViewportWidth - request.Width) <= 1 && Math.Abs(saved.ViewportHeight - request.Height) <= 1);
            var preferred = request.ForceRecenter ? null : SavedZoom(setting);
            var framing = request.ManualZoom.HasValue ? "manual" : restored ? saved.Framing : preferred.HasValue ? "saved" : "fit";
            var zoom = request.ManualZoom.HasValue ? ClampZoom(request.ManualZoom.Value) : restored ? ClampZoom(saved.Zoom) : preferred ?? MinimumZoom;
            if (!restored && framing == "fit" && box.HasValue) zoom = ClampZoom(Math.Min(FitZoom(box, request.Width, request.Height), FitZoom(FramingBox(context), request.Width, request.Height)));
            var result = new Projection(this) { Width = request.Width, Height = request.Height, Framing = framing, Zoom = zoom, Restored = restored };
            Rect? endBox = null; var aim = box ?? ink;
            if (current != null) aim = FramingBox(new[] { current }).Value;
            else if (box.HasValue)
            {
                var starts = new HashSet<string>(request.StartIds ?? Array.Empty<string>());
                var doors = decision.Where(starts.Contains).ToArray(); if (doors.Length == 0) doors = decision.ToArray();
                aim = FramingBox(doors) ?? box.Value;
                var end = _nodes.Values.FirstOrDefault(n => n.Type == "boss" && visible.Contains(n.Id));
                if (end != null)
                {
                    endBox = FramingBox(new[] { end.Id }); var ends = FramingBox(doors.Concat(new[] { end.Id })).Value;
                    var pad = doors.Select(id => Math.Max(0, (Radius(id) + request.HaloPad) * request.HaloPeak - Radius(id))).DefaultIfEmpty(0).Max();
                    var both = new Rect(ends.X0 - pad, ends.Y0 - pad, ends.X1 + pad, ends.Y1 + pad);
                    bool Fits(Rect r) => r.Width * zoom <= request.Width && r.Height * zoom <= request.Height;
                    result.TitleVisible = false;
                    if (request.TitleBand.HasValue && request.TitleBand.Value.Height > 0)
                    {
                        var titled = new Rect(both.X0, Math.Min(both.Y0, request.TitleBand.Value.Y0), both.X1, both.Y1);
                        if (Fits(titled)) { aim = titled; result.TitleVisible = true; }
                        else if (Fits(both)) aim = both;
                    }
                    else if (Fits(both)) aim = both;
                }
            }
            var aimX = restored ? saved.AimX : aim.Center.X;
            var half = request.Width / zoom / 2;
            if (!restored && box.HasValue && box.Value.Width <= 2 * half) aimX = Math.Min(box.Value.X0 + half, Math.Max(box.Value.X1 - half, aimX));
            var padY = request.Height / (2 * zoom);
            result.AimX = aimX; result.Content = new Rect(aimX - half, ink.Y0 - padY, aimX + half, ink.Y1 + padY);
            var top = restored ? saved.ScrollTop : (aim.Center.Y - result.Content.Y0) * zoom - request.Height / 2;
            if (!restored && box.HasValue && box.Value.Height * zoom <= request.Height)
            {
                var bt = (box.Value.Y0 - result.Content.Y0) * zoom; var bb = (box.Value.Y1 - result.Content.Y0) * zoom;
                top = Math.Min(bt, Math.Max(bb - request.Height, top));
            }
            result.ScrollTop = Math.Min(result.MaximumScrollTop, Math.Max(0, top));
            double Miss(Rect r) { var a = result.ToViewport(new Point(r.X0, r.Y0)); var b = result.ToViewport(new Point(r.X1, r.Y1)); return Math.Max(0, Math.Max(Math.Max(-a.X, b.X - request.Width), Math.Max(-a.Y, b.Y - request.Height))); }
            result.DecisionMiss = box.HasValue ? Miss(box.Value) : 0;
            result.EntranceMiss = endBox.HasValue ? Miss(endBox.Value) : 0;
            result.EntranceEndsFit = endBox.HasValue ? (bool?)(result.EntranceMiss <= .5) : null;
            result.State = new Snapshot { ActNumber = request.ActNumber, NodeId = request.CurrentId, Setting = setting, Zoom = zoom, Framing = framing, ScrollLeft = 0,
                ScrollTop = result.ScrollTop, AimX = aimX, ViewportWidth = request.Width, ViewportHeight = request.Height };
            return result;
        }
        private static bool Valid(Snapshot s) => s != null && s.ActNumber > 0 && s.Setting != null && new[] { "fit", "saved", "manual" }.Contains(s.Framing) &&
            Finite(s.Zoom) && s.Zoom > 0 && Finite(s.ScrollLeft) && s.ScrollLeft >= 0 && Finite(s.ScrollTop) && s.ScrollTop >= 0 && Finite(s.AimX) &&
            Finite(s.ViewportWidth) && s.ViewportWidth > 0 && Finite(s.ViewportHeight) && s.ViewportHeight > 0;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void Positive(double value, string name) { if (!Finite(value) || value <= 0) throw new ArgumentException("Invalid " + name); }
    }
}
