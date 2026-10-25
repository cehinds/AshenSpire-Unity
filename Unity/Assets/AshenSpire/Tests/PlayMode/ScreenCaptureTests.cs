// ScreenCaptureTests.cs — the Unity half of the JS-to-Unity parity harness (DEVELOPER.md, "Parity harness").
// Ported from AshenedSpire's fixture-driven RenderTexture capture, adapted to this project's single scene:
// for every screen in tools/parity-screens.json whose `unity` is not null and every view (desktop 1280x720,
// portrait 390x844) it loads Scenes/Expedition, points the scene's PanelSettings at a RenderTexture of the
// view size, replays the screen's `unity.steps` on the named controls (the ids the WebGL harness clicks),
// and writes <view>-<id>.png plus <view>-<id>.json (control rects) and manifest.json, the same names as
// tools/js-reference-capture.mjs, so tools/parity-report.mjs can pair them.
// GATED: runs only with ASHEN_PARITY_CAPTURE=1 (AshenSpire.EditorTools.Cli.CaptureScreens sets it);
// every normal EditMode/PlayMode run reports it as ignored. Needs a graphics device: never -nographics.
// Optional: ASHEN_PARITY_OUT (default <repo>/TestResults/Parity/unity-editor), ASHEN_PARITY_ONLY (regex
// on screen ids), ASHEN_PARITY_VIEWS (comma list, e.g. "desktop").
// SAVES: the game keeps saves and settings in PlayerPrefs. Each screen starts from an empty profile; the
// keys below are backed up first and restored afterwards, so the editor's own saves survive a capture.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AshenSpire.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AshenSpire.Tests.PlayMode
{
    [TestFixture, Category("ParityCapture")]
    public sealed class ScreenCaptureTests
    {
        public const string CaptureVariable = "ASHEN_PARITY_CAPTURE";
        private const string SceneName = "Expedition";
        private const int SettleFrames = 8;
        private const float SettleSeconds = 0.8f;
        private const float WaitSeconds = 10f;

        private static string RepoRoot => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", ".."));

        [UnityTest, Timeout(3600000)]
        public IEnumerator CaptureScreens()
        {
            if (Environment.GetEnvironmentVariable(CaptureVariable) != "1")
                Assert.Ignore("Parity capture runs only with " + CaptureVariable + "=1 (AshenSpire.EditorTools.Cli.CaptureScreens).");
            var screens = JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot, "tools", "parity-screens.json")));
            var outDir = Environment.GetEnvironmentVariable("ASHEN_PARITY_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(RepoRoot, "TestResults", "Parity", "unity-editor");
            Directory.CreateDirectory(outDir);
            var only = Environment.GetEnvironmentVariable("ASHEN_PARITY_ONLY");
            var onlyPattern = string.IsNullOrEmpty(only) ? null : new Regex(only);
            var viewFilter = (Environment.GetEnvironmentVariable("ASHEN_PARITY_VIEWS") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            var shots = new JArray();
            var failures = new List<string>();
            var prefs = PrefsSandbox.Capture();
            try
            {
                foreach (var view in screens["views"].OfType<JObject>())
                {
                    var viewId = (string)view["id"];
                    if (viewFilter.Length > 0 && !viewFilter.Contains(viewId)) continue;
                    foreach (var screen in screens["screens"].OfType<JObject>())
                    {
                        var id = (string)screen["id"];
                        if (screen["unity"] == null || screen["unity"].Type == JTokenType.Null) continue;
                        if (onlyPattern != null && !onlyPattern.IsMatch(id)) continue;
                        var entry = new JObject { ["id"] = id, ["view"] = viewId, ["png"] = viewId + "-" + id + ".png", ["dom"] = null, ["status"] = "ok" };
                        var shot = new Shot(outDir, viewId, id, (int)view["width"], (int)view["height"], (JArray)screen["unity"]["steps"]);
                        RetireControllers(); // the previous shot's controller saves on disable; let it do so before the reset
                        prefs.ResetForCapture();
                        yield return shot.Run();
                        if (shot.Error != null)
                        {
                            entry["status"] = "failed"; entry["png"] = null; entry["error"] = shot.Error;
                            failures.Add(viewId + "-" + id + ": " + shot.Error);
                        }
                        else entry["dom"] = viewId + "-" + id + ".json";
                        shots.Add(entry);
                        WriteManifest(outDir, screens, shots);
                        Debug.Log("PARITY_CAPTURE " + (shot.Error == null ? "OK   " : "FAIL ") + viewId + "-" + id + (shot.Error != null ? ": " + shot.Error : ""));
                    }
                }
            }
            finally
            {
                RetireControllers();
                prefs.Restore();
            }
            Debug.Log("CaptureScreens wrote " + shots.Count(s => (string)s["status"] == "ok") + " of " + shots.Count + " screenshots to " + outDir);
            Assert.That(shots, Is.Not.Empty, "no screen matched the filters");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        /// <summary>Disables every live RunController now, so its OnDisable save lands before the prefs are reset or restored.</summary>
        private static void RetireControllers()
        {
            foreach (var controller in UnityEngine.Object.FindObjectsByType<RunController>(FindObjectsSortMode.None))
                controller.enabled = false;
        }

        private static void WriteManifest(string outDir, JObject screens, JArray shots)
        {
            var manifest = new JObject
            {
                ["schema"] = "ashen.parity-capture/1",
                ["side"] = "unity",
                ["tool"] = "Unity/Assets/AshenSpire/Tests/PlayMode/ScreenCaptureTests.cs",
                ["source"] = new JObject
                {
                    ["repo"] = "AshenSpire-Unity (editor PlayMode)",
                    ["version"] = UnityEngine.Application.version,
                    ["unityVersion"] = UnityEngine.Application.unityVersion,
                    ["commit"] = Environment.GetEnvironmentVariable("ASHEN_PARITY_COMMIT"),
                },
                ["views"] = new JArray(screens["views"].OfType<JObject>().Select(v => new JObject { ["id"] = v["id"], ["width"] = v["width"], ["height"] = v["height"], ["touch"] = (bool?)v["touch"] ?? false })),
                ["shots"] = shots,
            };
            File.WriteAllText(Path.Combine(outDir, "manifest.json"), manifest.ToString(Formatting.Indented) + "\n");
        }

        /// <summary>One screen at one size: fresh scene, RenderTexture target, steps, PNG and control dump.</summary>
        private sealed class Shot
        {
            private readonly string _outDir, _view, _id;
            private readonly int _width, _height;
            private readonly JArray _steps;
            private VisualElement _root;
            public string Error { get; private set; }

            public Shot(string outDir, string view, string id, int width, int height, JArray steps)
            {
                _outDir = outDir; _view = view; _id = id; _width = width; _height = height; _steps = steps;
            }

            public IEnumerator Run()
            {
                GameViewSize.TrySet(_width, _height); // so Screen.width/height (safe area, reference height) match the view
                var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                for (var i = 0; i < SettleFrames; i++) yield return null;
                var controller = UnityEngine.Object.FindAnyObjectByType<RunController>();
                if (controller == null) { Error = "Scenes/" + SceneName + " has no RunController"; yield break; }
                var document = controller.GetComponent<UIDocument>();
                var settings = document.panelSettings;
                var rt = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                var oldTarget = settings.targetTexture; var oldClear = settings.clearColor; var oldClearValue = settings.colorClearValue;
                settings.targetTexture = rt; settings.clearColor = true; settings.colorClearValue = Color.black;
                _root = document.rootVisualElement;
                try
                {
                    for (var i = 0; i < SettleFrames; i++) yield return null;
                    foreach (var step in _steps.OfType<JObject>())
                    {
                        yield return RunStep(step);
                        if (Error != null) yield break;
                    }
                    for (var i = 0; i < SettleFrames; i++) yield return null;
                    yield return new WaitForSecondsRealtime(SettleSeconds);
                    // WaitForEndOfFrame never fires in batch mode; the panel renders into its own RenderTexture,
                    // so one more frame is enough before reading it back.
                    yield return null;
                    var previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    var texture = new Texture2D(_width, _height, TextureFormat.RGB24, false);
                    texture.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
                    texture.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(Path.Combine(_outDir, _view + "-" + _id + ".png"), texture.EncodeToPNG());
                    UnityEngine.Object.Destroy(texture);
                    File.WriteAllText(Path.Combine(_outDir, _view + "-" + _id + ".json"), Dump().ToString(Formatting.Indented) + "\n");
                }
                finally
                {
                    settings.targetTexture = oldTarget; settings.clearColor = oldClear; settings.colorClearValue = oldClearValue;
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                }
            }

            // Step vocabulary (shared with tools/unity-parity-capture.mjs): click | match(+index) | standard | fill | key | wait,
            // with command (wait for the game), ifPresent (skip when absent) and repeat.
            private IEnumerator RunStep(JObject step)
            {
                var repeat = (int?)step["repeat"] ?? 1;
                var ifPresent = (bool?)step["ifPresent"] ?? false;
                for (var n = 0; n < repeat; n++)
                {
                    if (step["wait"] != null) { yield return new WaitForSecondsRealtime((float)step["wait"] / 1000f); continue; }
                    if ((bool?)step["standard"] == true)
                    {
                        // As NativeUiDriver.useStandard: choose the Standard preset only when points are unspent.
                        if (Controls().Any(e => Regex.IsMatch(e.name, "^attribute-[a-z]+-up$") && e.enabledInHierarchy)) Submit(Find("foundation-mode-leanStandard"));
                        yield return Settle();
                        continue;
                    }
                    if (step["fill"] is JArray fill)
                    {
                        var fieldId = (string)fill[0];
                        for (var tries = 0; tries < 6 && Find(fieldId) == null; tries++)
                        {
                            var next = Find("native-creation-next"); // creation is a sequence of steps; walk forward to the field
                            if (next == null || !next.enabledInHierarchy) break;
                            Submit(next);
                            yield return Settle();
                        }
                        if (!(Find(fieldId) is TextField field)) { Error = "no text field " + fieldId; yield break; }
                        field.value = (string)fill[1];
                        yield return Settle();
                        continue;
                    }
                    if (step["key"] != null)
                    {
                        if (!Enum.TryParse((string)step["key"], true, out KeyCode code)) { Error = "unknown key " + step["key"]; yield break; }
                        var target = _root.focusController?.focusedElement as VisualElement ?? _root;
                        using (var down = KeyDownEvent.GetPooled('\0', code, EventModifiers.None)) { down.target = target; target.SendEvent(down); }
                        using (var up = KeyUpEvent.GetPooled('\0', code, EventModifiers.None)) { up.target = target; target.SendEvent(up); }
                        yield return Settle();
                        continue;
                    }
                    VisualElement control = null;
                    var deadline = Time.realtimeSinceStartup + (ifPresent ? 1.5f : WaitSeconds);
                    while (control == null && Time.realtimeSinceStartup < deadline)
                    {
                        if (step["match"] != null)
                        {
                            var pattern = new Regex((string)step["match"]);
                            control = Controls().Where(e => e.enabledInHierarchy && pattern.IsMatch(e.name)).Skip((int?)step["index"] ?? 0).FirstOrDefault();
                        }
                        else
                        {
                            var found = Find((string)step["click"]);
                            control = found != null && found.enabledInHierarchy ? found : null;
                        }
                        if (control == null) yield return null;
                    }
                    if (control == null)
                    {
                        if (ifPresent) yield break;
                        Error = "no enabled control " + (step["match"] ?? step["click"]);
                        yield break;
                    }
                    Submit(control);
                    yield return Settle();
                }
            }

            private static IEnumerator Settle()
            {
                for (var i = 0; i < SettleFrames; i++) yield return null;
                yield return new WaitForSecondsRealtime(0.3f);
            }

            /// <summary>Activate a control the way GamepadNavigator does: a navigation submit on the element.</summary>
            private static void Submit(VisualElement element)
            {
                if (element == null) return;
                using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = element; element.SendEvent(submit); }
            }

            private VisualElement Find(string name) => _root.Query<VisualElement>(name: name).ToList().FirstOrDefault(e => e.resolvedStyle.display != DisplayStyle.None);

            /// <summary>The named controls, in the order CampaignView reports them to the WebGL harness.</summary>
            private IEnumerable<VisualElement> Controls() =>
                _root.Query<Button>().ToList().Cast<VisualElement>()
                    .Concat(_root.Query<TextField>().ToList()).Concat(_root.Query<Toggle>().ToList()).Concat(_root.Query<DropdownField>().ToList())
                    .Concat(_root.Query<SliderInt>().ToList()).Concat(_root.Query<IntegerField>().ToList())
                    .Where(e => !string.IsNullOrEmpty(e.name) && e.resolvedStyle.display != DisplayStyle.None);

            /// <summary>Control rects in RenderTexture pixels, keyed by control name (the tools/unity-parity-capture.mjs format).</summary>
            private JObject Dump()
            {
                var panel = _root.worldBound;
                var sx = panel.width > 0 ? _width / panel.width : 1f;
                var sy = panel.height > 0 ? _height / panel.height : 1f;
                var elements = new JObject();
                foreach (var e in Controls())
                {
                    var b = e.worldBound;
                    if (!(elements[e.name] is JArray list)) elements[e.name] = list = new JArray();
                    list.Add(new JObject
                    {
                        ["rect"] = new JArray(Round(b.x * sx), Round(b.y * sy), Round(b.width * sx), Round(b.height * sy)),
                        ["text"] = e is Button button ? (button.text ?? "").Trim() : "",
                        ["enabled"] = e.enabledInHierarchy,
                    });
                }
                return new JObject
                {
                    ["side"] = "unity", ["id"] = _id, ["view"] = _view,
                    ["viewport"] = new JObject { ["width"] = _width, ["height"] = _height },
                    ["panel"] = new JObject { ["width"] = Round(panel.width), ["height"] = Round(panel.height) },
                    ["screen"] = new JObject { ["width"] = Screen.width, ["height"] = Screen.height },
                    ["rootClasses"] = new JArray(_root.GetClasses().ToArray()),
                    ["labels"] = new JArray(_root.Query<Label>().ToList().Select(l => l.text).Take(120).ToArray()),
                    ["elements"] = elements,
                };
            }

            private static double Round(float value) => Math.Round(value * 10) / 10;
        }

        /// <summary>
        /// Backs up the game's PlayerPrefs keys (saves, settings, welcome flag), clears them before each screen so
        /// every capture starts from an empty profile, and restores the originals at the end.
        /// </summary>
        private sealed class PrefsSandbox
        {
            private const string Channel = "desktop"; // RunController's save channel inside the editor
            private const string Missing = "\u0001parity-missing";
            private readonly Dictionary<string, object> _saved = new Dictionary<string, object>();
            private readonly HashSet<string> _present = new HashSet<string>();

            private static IEnumerable<string> Keys()
            {
                var campaign = "AshenSpire.Unity.Campaign.v1." + Channel;
                var original = "AshenSpire.Unity.Original.v1." + Channel;
                var profile = "AshenSpire.Unity.Profile.v1." + Channel;
                var journals = new List<string> { campaign, original, profile };
                for (var slot = 0; slot < 3; slot++) journals.Add(original + ".slot" + slot);
                foreach (var key in journals) { yield return key; yield return key + ".backup"; yield return key + ".corrupt"; }
                yield return original + ".slots";
                yield return "AshenSpire.Settings.v1";
                yield return "AshenSpire.Unity.Welcome.v1";
                foreach (var legacy in new[] { "ReducedMotion", "FastMotion", "Muted", "MasterVolume", "MusicVolume", "MusicEnabled" }) yield return "AshenSpire." + legacy;
            }

            public static PrefsSandbox Capture()
            {
                var sandbox = new PrefsSandbox();
                foreach (var key in Keys())
                {
                    if (!PlayerPrefs.HasKey(key)) continue;
                    sandbox._present.Add(key);
                    var text = PlayerPrefs.GetString(key, Missing);
                    if (text != Missing) { sandbox._saved[key] = text; continue; }
                    var number = PlayerPrefs.GetInt(key, int.MinValue);
                    if (number != int.MinValue) sandbox._saved[key] = number;
                    else sandbox._saved[key] = PlayerPrefs.GetFloat(key, 0f);
                }
                return sandbox;
            }

            public void ResetForCapture()
            {
                foreach (var key in Keys()) PlayerPrefs.DeleteKey(key);
                PlayerPrefs.SetInt("AshenSpire.Unity.Welcome.v1", 1); // the JS reference has no first-run welcome card
                PlayerPrefs.SetInt("AshenSpire.Muted", 1);
                PlayerPrefs.Save();
            }

            public void Restore()
            {
                foreach (var key in Keys())
                {
                    PlayerPrefs.DeleteKey(key);
                    if (!_present.Contains(key)) continue;
                    switch (_saved[key])
                    {
                        case string s: PlayerPrefs.SetString(key, s); break;
                        case int i: PlayerPrefs.SetInt(key, i); break;
                        case float f: PlayerPrefs.SetFloat(key, f); break;
                    }
                }
                PlayerPrefs.Save();
            }
        }

        /// <summary>Sets the editor Game view to the capture size when the editor exposes it (reflection: no hard API dependency).</summary>
        private static class GameViewSize
        {
            public static void TrySet(int width, int height)
            {
#if UNITY_EDITOR
                try
                {
                    var type = Type.GetType("UnityEditor.PlayModeWindow, UnityEditor");
                    var method = type?.GetMethod("SetCustomRenderingResolution", new[] { typeof(uint), typeof(uint), typeof(string) });
                    if (method == null) { Debug.LogWarning("PARITY_CAPTURE: cannot set the Game view size; Screen is " + Screen.width + "x" + Screen.height); return; }
                    method.Invoke(null, new object[] { (uint)width, (uint)height, "Parity " + width + "x" + height });
                }
                catch (Exception error) { Debug.LogWarning("PARITY_CAPTURE: Game view size not set: " + error.Message); }
#endif
            }
        }
    }
}
