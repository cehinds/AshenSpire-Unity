// ParityCaptureCli.cs — editor entry for the Unity half of the parity harness (DEVELOPER.md, "Parity harness").
//   Unity -batchmode -projectPath Unity -executeMethod AshenSpire.EditorTools.Cli.CaptureScreens   (no -quit, no -nographics)
// or the menu AshenSpire > Parity > Capture Screens. It sets ASHEN_PARITY_CAPTURE=1 and runs the PlayMode
// test AshenSpire.Tests.PlayMode.ScreenCaptureTests.CaptureScreens, which writes TestResults/Parity/unity-editor
// (override with ASHEN_PARITY_OUT). In batch mode the editor exits with the test result.
using System;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AshenSpire.EditorTools
{
    public static class Cli
    {
        private const string CaptureVariable = "ASHEN_PARITY_CAPTURE";
        private const string CaptureTest = "AshenSpire.Tests.PlayMode.ScreenCaptureTests.CaptureScreens";

        /// <summary>Runs the capture PlayMode test with the gate open. Do not pass -quit.</summary>
        [MenuItem("AshenSpire/Parity/Capture Screens")]
        public static void CaptureScreens()
        {
            Environment.SetEnvironmentVariable(CaptureVariable, "1");
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new CaptureCallbacks());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode, testNames = new[] { CaptureTest } }));
        }

        // Entering play mode reloads the domain; re-register the exit callback in the reloaded domain.
        [InitializeOnLoad]
        private static class CaptureHook
        {
            static CaptureHook()
            {
                if (Environment.GetEnvironmentVariable(CaptureVariable) != "1" || !UnityEngine.Application.isBatchMode) return;
                ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new CaptureCallbacks());
            }
        }

        private sealed class CaptureCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                Debug.Log("CaptureScreens finished: " + result.PassCount + " passed, " + result.FailCount + " failed, " + result.SkipCount + " skipped");
                if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(result.FailCount > 0 || result.PassCount == 0 ? 1 : 0);
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.FailCount > 0) Debug.LogError(result.Name + ": " + result.Message);
            }
        }
    }
}
