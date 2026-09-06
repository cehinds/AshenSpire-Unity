// WebStagingBuildProcessor.cs — Web-only build workaround; no scene/Inspector setup.
// Unity copies player DLLs to Temp before this callback. Never patch the Editor install.
// UPDATE: review/remove with a supported Unity fix; unknown versions/hashes stop builds.
// EVIDENCE: Builds/WebStagingPatch.json records the input/output assembly SHA-256.
// Gameplay, UI styling, native players and saves do not depend on this Editor component.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AshenSpire.Editor.Rendering
{
    public sealed class WebStagingBuildProcessor : IPostBuildPlayerScriptDLLs, IPreprocessBuildWithReport
    {
        private const string OriginalHash = "bfe25a4e12ab85d84fd4f905c17c472599a76e42bceeb5ed02349c3dbe50bcac";
        private static string _receipt;
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => _receipt = null;

        public static void WriteReceipt(string outputDirectory)
        {
            if (_receipt == null) throw new BuildFailedException("No Web staging receipt was produced by this build.");
            File.WriteAllText(Path.Combine(outputDirectory, "renderer-workaround.json"), _receipt);
        }

        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL) return;
            if (UnityEngine.Application.unityVersion != "6000.6.0f1")
                throw new BuildFailedException("Review the Web staging workaround for this Unity version.");
            var path = report.GetFiles().Where(file => file.role == "ManagedEngineAPI")
                .Select(file => file.path).Single(value => Path.GetFileName(value) == "UnityEngine.UIElementsModule.dll");
            path = Path.GetFullPath(path);
            var project = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            var staging = Path.GetFullPath(Path.Combine(project, "Temp/StagingArea/Data/Managed")) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(staging, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Refusing to patch outside this project's temporary player staging directory.");
            var before = Digest(path);
            if (before != OriginalHash)
                throw new BuildFailedException("Web UIElements assembly differs from the reviewed baseline; do not apply the workaround.");
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(path));
            using (resolver)
            using (var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true, ReadSymbols = true, AssemblyResolver = resolver }))
            {
                WebStagingPatch.Apply(module);
                module.Write(path, new WriterParameters { WriteSymbols = true });
            }
            var receipt = new PatchReceipt { UnityVersion = UnityEngine.Application.unityVersion, InputSha256 = before, OutputSha256 = Digest(path), PlayerAssembly = "Temp/StagingArea/Data/Managed/UnityEngine.UIElementsModule.dll", Algorithm = "consolidate-then-count-plus-two-per-range-v1" };
            _receipt = JsonUtility.ToJson(receipt, true);
            var output = Path.GetFullPath(Path.Combine(project, "../Builds"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "WebStagingPatch.json"), _receipt);
            Debug.Log("ASHENSPIRE WEB STAGING PATCH: " + receipt.OutputSha256);
        }

        private static string Digest(string path)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return string.Concat(hash.ComputeHash(stream).Select(value => value.ToString("x2")));
        }

        [Serializable]
        private sealed class PatchReceipt
        {
            public string UnityVersion, InputSha256, OutputSha256, PlayerAssembly, Algorithm;
        }
    }
}
