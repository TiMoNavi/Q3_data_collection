using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Q3DataCollection.Editor
{
    [InitializeOnLoad]
    public static class Q3DataCaptureBuild
    {
        private const string DefaultOutputPath = "Builds/Q3DataCollection_autotest.apk";
        private const string BuildTriggerPath = "Library/Q3DC_RequestAndroidAutoTestBuild.txt";

        static Q3DataCaptureBuild()
        {
            EditorApplication.update -= CheckBuildTrigger;
            EditorApplication.update += CheckBuildTrigger;
        }

        [MenuItem("DataCapture/Build/Android Auto Test APK")]
        public static void BuildAndroidAutoTestFromMenu()
        {
            BuildAndroidAutoTest(DefaultOutputPath);
        }

        public static void BuildAndroidAutoTest()
        {
            string outputPath = GetArgumentValue("-q3dcOutput", DefaultOutputPath);
            BuildAndroidAutoTest(outputPath);
        }

        private static void BuildAndroidAutoTest(string outputPath)
        {
            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Q3 DataCapture Android build failed: {summary.result}, errors={summary.totalErrors}, warnings={summary.totalWarnings}");
            }

            UnityEngine.Debug.Log(
                $"Q3 DataCapture Android build succeeded: {outputPath}, size={summary.totalSize} bytes, warnings={summary.totalWarnings}");
        }

        private static void CheckBuildTrigger()
        {
            if (!File.Exists(BuildTriggerPath) || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            string outputPath = File.ReadAllText(BuildTriggerPath).Trim();
            File.Delete(BuildTriggerPath);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = DefaultOutputPath;
            }

            try
            {
                BuildAndroidAutoTest(outputPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("Q3 DataCapture Android trigger build failed: " + ex);
            }
        }

        private static string GetArgumentValue(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return fallback;
        }
    }
}
