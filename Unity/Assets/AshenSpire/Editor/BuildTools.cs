// BuildTools.cs — owner-facing import, scene setup and packaged-player commands.
// Menu: AshenSpire. CLI: -executeMethod AshenSpire.Editor.BuildTools.BuildWeb.
// GameContent/Unity/expedition.json is authoritative; Resources/expedition.json is generated.
// Setup creates only this adaptation's scene/prefab/panel when absent. Existing owner
// wiring is preserved. Outputs go to repository Builds/, outside Unity Assets.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AshenSpire.Application;
using AshenSpire.Domain;
using AshenSpire.Editor.Rendering;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Editor
{
    public static class BuildTools
    {
        private const string Root = "Assets/AshenSpire";
        private const string ScenePath = Root + "/Scenes/Expedition.unity";
        private static string Repository => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../.."));
        // One authored version for UI, platform metadata and package manifests.
        // Format: game release.roadmap milestone.incremental upgrade.patch.
        [Serializable]
        private sealed class VersionDefinition
        {
            public string Version;
            public int BuildNumber;
            public string Stage;
        }
        private static VersionDefinition ReadVersion()
        {
            var version = JsonUtility.FromJson<VersionDefinition>(File.ReadAllText(Path.Combine(Repository, "GameContent/Unity/version.json")));
            if (version == null || !Regex.IsMatch(version.Version ?? "", @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$") || version.BuildNumber < 1 || string.IsNullOrWhiteSpace(version.Stage))
                throw new InvalidDataException("version.json requires a four-part Version, positive BuildNumber and Stage.");
            return version;
        }

        [MenuItem("AshenSpire/1. Validate and Import Content")]
        public static void ImportContent()
        {
            var source = Path.Combine(Repository, "GameContent/Unity/expedition.json");
            var json = File.ReadAllText(source);
            var content = JsonUtility.FromJson<ContentDefinition>(json);
            if (content == null)
                throw new InvalidDataException(source + ": not a content object.");
            content.Validate();
            var campaignJson = File.ReadAllText(Path.Combine(Repository, "GameContent/Unity/campaign.json"));
            var campaign = JsonUtility.FromJson<CampaignDefinition>(campaignJson);
            CampaignAuthoringValidation.Validate(campaign, Root + "/Resources/Art");
            var originalJson = File.ReadAllText(Path.Combine(Repository, "GameContent/Unity/Original/content.json"));
            _ = new AshenSpire.Domain.Original.OriginalContentCatalog(originalJson);
            Directory.CreateDirectory(Root + "/Resources/Original");
            File.WriteAllText(Root + "/Resources/Original/content.json", originalJson);
            foreach (var file in new[] { "mechanics", "progression", "event-choices", "custom-run-options", "appearance-options", "sprite-styles" })
            {
                var authored = File.ReadAllText(Path.Combine(Repository, "GameContent/Unity/Original/" + file + ".json"));
                var parsed = Newtonsoft.Json.Linq.JObject.Parse(authored);
                if (file == "mechanics")
                {
                    _ = new AshenSpire.Domain.Original.WeightSystem(parsed);
                    _ = new AshenSpire.Domain.Original.ResourceWallet(0, 0, 0, parsed);
                }
                if (file == "progression") _ = new AshenSpire.Domain.Original.AttributeProgression(parsed);
                File.WriteAllText(Root + "/Resources/Original/" + file + ".json", authored);
            }
            File.WriteAllText(Root + "/Resources/campaign.json", campaignJson);
            var target = Root + "/Resources/expedition.json";
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (!File.Exists(target) || File.ReadAllText(target) != json)
                File.WriteAllText(target, json);
            AssetDatabase.Refresh();
            OriginalSpriteImport.Configure();
            Debug.Log($"Content import: {content.Cards.Length} cards and {content.Enemies.Length} enemies validated.");
        }

        [MenuItem("AshenSpire/2. Prepare Playable Scene")]
        public static void Prepare()
        {
            ImportContent();
            if (File.Exists(ScenePath))
                return;
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(430, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 1;
            AssetDatabase.CreateAsset(panel, Root + "/ExpeditionPanel.asset");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("MainCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            camera.orthographic = true;
            var root = new GameObject("ExpeditionRoot", typeof(UIDocument));
            root.GetComponent<UIDocument>().panelSettings = panel;
            root.AddComponent<RunController>().Configure(panel);
            PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/ExpeditionRoot.prefab");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        [MenuItem("AshenSpire/3. Build Web Preview")]
        public static void BuildWeb() => Build(BuildTarget.WebGL, "Web");

        [MenuItem("AshenSpire/4. Build Windows Player")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Windows/AshenSpire.exe");

        [MenuItem("AshenSpire/5. Build Android APK")]
        public static void BuildAndroid() => Build(BuildTarget.Android, "Android/AshenSpire.apk");

        private static void Build(BuildTarget target, string suffix)
        {
            if (UnityEngine.Application.isBatchMode && EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException($"Start this Editor with -buildTarget {target} so platform-specific assemblies are compiled before export.");
            Prepare();
            PlayerSettings.companyName = "AshenSpire";
            PlayerSettings.productName = "AshenSpire Unity";
            var version = ReadVersion();
            PlayerSettings.bundleVersion = version.Version;
            PlayerSettings.Android.bundleVersionCode = version.BuildNumber;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.ashenspire.expedition");
            PlayerSettings.defaultScreenWidth = 430;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.runInBackground = false;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.template = "PROJECT:Mobile";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = target,
                locationPathName = Path.Combine(Repository, "Builds", suffix),
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"{target} build failed: {report.summary.totalErrors} errors.");
            // The exporter, rather than a later copy command, records the source it built.
            AssetDatabase.SaveAssets();
            var digest = SourceDigest();
            if (target == BuildTarget.WebGL)
            {
                WebStagingBuildProcessor.WriteReceipt(report.summary.outputPath);
                var index = Path.Combine(report.summary.outputPath, "index.html");
                File.WriteAllText(index, File.ReadAllText(index).Replace("__ASHENSPIRE_BUILD_TOKEN__", digest).Replace("__ASHENSPIRE_VERSION__", PlayerSettings.bundleVersion));
            }
            var outputDirectory = target == BuildTarget.WebGL ? report.summary.outputPath : Path.GetDirectoryName(report.summary.outputPath);
            var platform = target == BuildTarget.WebGL ? "Web" : target == BuildTarget.Android ? "Android" : "Windows";
            var exportedFiles = new Newtonsoft.Json.Linq.JObject();
            foreach (var path in Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories).OrderBy(value => value, StringComparer.Ordinal))
            {
                var relative = path.Substring(outputDirectory.Length + 1).Replace('\\', '/');
                if (relative == "build-source.json") continue;
                using (var sha = SHA256.Create())
                    exportedFiles[relative] = string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(value => value.ToString("x2")));
            }
            File.WriteAllText(Path.Combine(outputDirectory, "build-source.json"),
                new Newtonsoft.Json.Linq.JObject { ["sourceDigest"] = digest, ["builtAt"] = DateTime.UtcNow.ToString("O"), ["version"] = version.Version, ["buildNumber"] = version.BuildNumber, ["target"] = platform, ["files"] = exportedFiles }.ToString(Newtonsoft.Json.Formatting.None));
            Debug.Log($"ASHENSPIRE BUILD PASSED: {target}; {report.summary.totalSize} bytes; {report.summary.outputPath}");
        }

        private static string SourceDigest()
        {
            var paths = new[] { "Unity/Assets", "Unity/Packages", "Unity/ProjectSettings", "GameContent/Unity" }
                .SelectMany(folder => Directory.GetFiles(Path.Combine(Repository, folder), "*", SearchOption.AllDirectories))
                .Select(path => path.Substring(Repository.Length + 1).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal);
            using (var hash = SHA256.Create())
            using (var stream = new MemoryStream())
            {
                foreach (var path in paths)
                {
                    var name = Encoding.UTF8.GetBytes(path);
                    stream.Write(name, 0, name.Length);
                    var bytes = File.ReadAllBytes(Path.Combine(Repository, path));
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    if (!new[] { ".png", ".jpg", ".webp", ".ttf", ".otf" }.Contains(extension))
                        bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"));
                    stream.Write(bytes, 0, bytes.Length);
                }
                stream.Position = 0;
                return string.Concat(hash.ComputeHash(stream).Select(value => value.ToString("x2")));
            }
        }
    }
}
