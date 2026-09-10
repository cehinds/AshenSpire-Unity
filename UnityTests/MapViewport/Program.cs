// Differential numeric camera checks, not a browser or physical-touch claim.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using V = AshenSpire.Domain.Original.OriginalMapViewport;

var root = args.Length > 0 ? args[0] : ".";
var folder = Path.Combine(root, "UnityTests/MapViewport");
var oracle = JObject.Parse(File.ReadAllText(Path.Combine(folder, "reference.json")));
var receipt = JObject.Parse(File.ReadAllText(Path.Combine(folder, "reference.receipt.json")));
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
void Near(double got, double expected, string name) => Check(!double.IsNaN(got) && !double.IsInfinity(got) && Math.Abs(got - expected) <= 1e-7 * Math.Max(1, Math.Abs(expected)), name + ": " + got + " != " + expected);
void Refuses(Action action, string name) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Accepted " + name); }
Check((string)oracle["sourceCommit"] == "b17a7f4543e1710f49fae8b58880121690a314de", "pinned original commit");
Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, "reference.json")))).ToLowerInvariant() == (string)receipt["outputSha256"], "oracle bytes match source receipt");
var maps = (JArray)oracle["maps"];
Check(oracle["fixtures"].Any(f => (double)f["expected"]["decisionMiss"] > .5), "corpus includes decisions that cannot fit the legal zoom");
Check(oracle["fixtures"].Any(f => (bool?)f["expected"]["entranceEndsFit"] == true) && oracle["fixtures"].Any(f => (bool?)f["expected"]["entranceEndsFit"] == false), "corpus includes fitting and clipped entrance ends");
var models = maps.Select(m => new V(V.NodesFromMap((JObject)m["graph"]), (int)m["graph"]["columns"], 44, .9)).ToArray();
for (var i = 0; i < maps.Count; i++)
{
    foreach (var p in ((JObject)maps[i]["positions"]).Properties())
    {
        var point = models[i].Position(p.Name); Near(point.X, (double)p.Value["x"], "original node X"); Near(point.Y, (double)p.Value["y"], "original node Y");
        var box = models[i].FramingBox(new[] { p.Name }).Value;
        Near(box.X0, (double)p.Value["box"]["x0"], "original circle left"); Near(box.Y0, (double)p.Value["box"]["y0"], "original circle top");
        Near(box.X1, (double)p.Value["box"]["x1"], "original circle right"); Near(box.Y1, (double)p.Value["box"]["y1"], "original circle bottom");
    }
}
var originalInput = oracle.ToString(); int fixtureIndex = 0;
foreach (var fixture in oracle["fixtures"])
{
    var request = fixture["request"].ToObject<V.Request>();
    request.SavedState = V.Snapshot.FromJson(fixture["request"]["savedState"]);
    var model = models[(int)fixture["mapIndex"]]; var p = model.Project(request); var e = fixture["expected"];
    var label = "fixture " + fixtureIndex++;
    Near(p.Zoom, (double)e["zoom"], label + " zoom"); Near(p.AimX, (double)e["aimX"], label + " aim"); Near(p.ScrollTop, (double)e["scrollTop"], label + " scroll");
    Near(p.Content.X0, (double)e["content"]["x0"], label + " content left"); Near(p.Content.Y0, (double)e["content"]["y0"], label + " content top");
    Near(p.Content.X1, (double)e["content"]["x1"], label + " content right"); Near(p.Content.Y1, (double)e["content"]["y1"], label + " content bottom");
    Near(p.DecisionMiss, (double)e["decisionMiss"], label + " decision miss"); Near(p.EntranceMiss, (double)e["entranceMiss"], label + " entrance miss");
    Check(p.EntranceEndsFit == (bool?)e["entranceEndsFit"], label + " entrance status"); Check(p.Framing == (string)e["framing"] && p.Restored == (bool)e["restored"] && p.TitleVisible == (bool)e["titleVisible"], label + " camera mode");
    Check(p.ContentWidth == request.Width && p.ScrollLeft == 0, label + " zero horizontal extent");
    Check(p.ScrollTop >= 0 && p.ScrollTop <= p.MaximumScrollTop, label + " legal vertical scroll");
    foreach (var id in request.ReachableIds.Concat(request.CurrentId == null ? Array.Empty<string>() : new[] { request.CurrentId }))
    {
        var view = p.NodeBounds(id); var content = p.NodeBounds(id, false);
        Near(view.Y0 + p.ScrollTop, content.Y0, label + " content-to-viewport Y"); Near(view.X0, content.X0, label + " no horizontal scrolling");
        if (p.DecisionFits) Check(view.X0 >= -.500001 && view.X1 <= p.Width + .500001 && view.Y0 >= -.500001 && view.Y1 <= p.Height + .500001, label + " every decision node actually in viewport");
    }
    var frozen = p.State.ToJson(); request.SavedState = V.Snapshot.FromJson(frozen); var restored = model.Project(request);
    Check(restored.Restored, label + " valid camera restores"); Near(restored.ScrollTop, p.ScrollTop, label + " restored scroll exact"); Near(restored.AimX, p.AimX, label + " restored aim exact");
    Check(JToken.DeepEquals(frozen, restored.State.ToJson()), label + " camera JSON round trip");
}
Check(originalInput == oracle.ToString(), "all input graph/requests and original RNG receipts unchanged");
foreach (var helper in oracle["helpers"])
{
    var tap = (double)helper["tap"]; var scale = (double)helper["scale"]; var z = (double)helper["z"];
    Near(V.RadiusForTap(tap, z, scale), (double)helper["radius"], "original inverse tap equation");
    var original = new V(V.NodesFromMap((JObject)maps[0]["graph"]), (int)maps[0]["graph"]["columns"], tap, .9);
    Near(original.NodeRadius, (double)helper["authoredRadius"], "authored tenth-radius rounding");
    Near(2 * original.NodeRadius * z * scale, (double)helper["delivered"], "actual delivered tap pixels");
}
var model0 = models[0]; var req = oracle["fixtures"][0]["request"].ToObject<V.Request>(); var fit = model0.Project(req);
var good = fit.State.ToJson();
foreach (var key in new[] { "zoom", "scrollLeft", "scrollTop", "aimX", "viewportWidth", "viewportHeight" })
    foreach (var bad in new JToken[] { JValue.CreateNull(), new JValue("NaN"), new JValue(double.NaN), new JValue(double.PositiveInfinity), new JArray(), new JObject(), new JValue(true) })
    { var value = (JObject)good.DeepClone(); value[key] = bad; Check(V.Snapshot.FromJson(value) == null, "corrupt numeric camera refused " + key); }
foreach (var key in new[] { "actNumber", "setting", "framing", "nodeId" }) { var value = (JObject)good.DeepClone(); value[key] = new JArray(); Check(V.Snapshot.FromJson(value) == null, "corrupt identity refused " + key); }
Check(V.Snapshot.FromJson(JValue.CreateNull()) == null && V.Snapshot.FromJson(new JArray()) == null, "invalid whole save ignored");
foreach (var key in new[] { "zoom", "scrollLeft", "scrollTop", "viewportWidth", "viewportHeight" }) { var value = (JObject)good.DeepClone(); value[key] = -1; Check(V.Snapshot.FromJson(value) == null, "negative camera refused " + key); }
foreach (var change in new[] { "act", "node", "setting", "width", "height" })
{
    var r = oracle["fixtures"][0]["request"].ToObject<V.Request>(); r.SavedState = V.Snapshot.FromJson(good);
    if (change == "act") r.ActNumber++; if (change == "node") r.CurrentId = r.ReachableIds.First(); if (change == "setting") r.Setting = "150"; if (change == "width") r.Width += 2; if (change == "height") r.Height += 2;
    Check(!model0.Project(r).Restored, "stale Fit recomputed " + change);
}
var manual = oracle["fixtures"][0]["request"].ToObject<V.Request>(); manual.ManualZoom = 1.5; var mp = model0.Project(manual);
Check(mp.Framing == "manual" && mp.Zoom == 1.5, "manual zoom recentered"); manual.ManualZoom = null; manual.SavedState = mp.CaptureScroll(17); manual.Width = 1000;
var resized = model0.Project(manual); Check(resized.Restored && resized.ScrollTop == 17 && resized.Zoom == 1.5, "manual camera survives viewport resize");
manual.ForceRecenter = true; Check(model0.Project(manual).Framing == "fit" && !model0.Project(manual).Restored, "recenter clears manual override");
Check(mp.CaptureScroll(-3).ScrollTop == 0 && mp.CaptureScroll(double.MaxValue).ScrollTop == mp.MaximumScrollTop, "scroll snapshots clamp both bounds");
var hugeCamera = oracle["fixtures"][0]["request"].ToObject<V.Request>(); hugeCamera.SavedState = V.Snapshot.FromJson(good); hugeCamera.SavedState.AimX = double.MaxValue;
Check(!model0.Project(hugeCamera).Restored, "finite saved aim that overflows projected coordinates is recomputed");
var oversizedInteger = (JObject)good.DeepClone(); oversizedInteger["zoom"] = new JValue(System.Numerics.BigInteger.Parse(new string('9', 400))); Check(V.Snapshot.FromJson(oversizedInteger) == null, "overflowing numeric token ignored");
Check(V.StepZoom(1, -1) == 1 && V.StepZoom(2, 1) == 2 && V.StepZoom(1.8, -1) == 1 && V.StepZoom(1.8, 1) == 1.3, "original ladder including off-rung Fit stepping");
Check(V.SavedZoom("Fit") == null && V.SavedZoom("garbage") == null && V.SavedZoom("0") == null && V.SavedZoom("126") == 1.3, "original saved-setting fallback/snap");
var visible = req.VisibleIds.ToArray(); req.VisibleIds = Array.Empty<string>(); var empty = model0.Project(req); Check(empty.ContentWidth == req.Width && double.IsFinite(empty.ScrollTop), "empty painted geometry has finite fallback"); req.VisibleIds = visible;
foreach (var bad in new[] { 0, -1, double.NaN, double.PositiveInfinity }) { Refuses(() => model0.Project(new V.Request { Width = bad, Height = 600 }), "invalid width"); Refuses(() => V.RadiusForTap(44, 1, bad), "invalid display scale"); }
Refuses(() => mp.CaptureScroll(double.NaN), "nonfinite scroll"); Refuses(() => V.ClampZoom(double.NaN), "nonfinite zoom"); Refuses(() => V.StepZoom(1, 0), "zero direction");
var cloned = V.NodesFromMap((JObject)maps[0]["graph"]); var protectedModel = new V(cloned, (int)maps[0]["graph"]["columns"], 44, .9); var id0 = cloned[0].Id; var x0 = protectedModel.Position(id0).X; cloned[0].Column++; cloned[0].NextIds = new[] { "missing" }; Check(protectedModel.Position(id0).X == x0, "model freezes supplied node DTOs");
foreach (var scale in new[] { .6, .74, .9, 1.0, 1.5, 2.0 })
{
    var actualScaleModel = new V(V.NodesFromMap((JObject)maps[0]["graph"]), (int)maps[0]["graph"]["columns"], 44, scale);
    var measured = 2 * actualScaleModel.NodeRadius * V.ReferenceZoom * scale;
    Check(Math.Abs(measured - 44) <= .1 * V.ReferenceZoom * scale + 1e-9, "reference radius uses supplied Unity panel-to-CSS scale, within authored tenth-unit rounding");
}
var malformedGraph = (JObject)maps[0]["graph"].DeepClone(); var firstNode = ((JObject)malformedGraph["nodes"]).Properties().First().Value; firstNode["next"] = "bad";
Refuses(() => V.NodesFromMap(malformedGraph), "invalid edge collection");
var result = new JObject { ["success"] = true, ["checks"] = checks, ["cameraFixtures"] = fixtureIndex, ["maps"] = maps.Count, ["sourceCommit"] = oracle["sourceCommit"], ["browserProof"] = false };
Console.WriteLine(result.ToString());
