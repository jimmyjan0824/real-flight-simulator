using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Command-line and menu build entry points.
///
/// Batch mode (see scripts/build_ios.sh):
///   Unity -batchmode -quit -projectPath . -buildTarget iOS -executeMethod BuildScript.BuildIOS
///
/// Optional environment variables:
///   BUNDLE_ID     overrides the bundle identifier (default below; must be unique for your Apple ID)
///   IOS_TEAM_ID   Apple Developer Team ID (10 chars). If empty, pick the team in Xcode instead.
///   BUILD_PATH    Xcode project output folder (default: build/iOS)
/// </summary>
public static class BuildScript
{
    public const string MainScenePath = "Assets/Scenes/Main.unity";

    private const string DefaultBundleId = "com.jimmyjan.flightsimmvp";
    private const string CompanyName = "JimmyJan";
    private const string ProductName = "Flight Sim MVP";
    private const string DefaultBuildPath = "build/iOS";

    // ------------------------------------------------------------------ menu

    [MenuItem("FlightSim/Create Or Reset Main Scene")]
    public static void CreateOrResetMainSceneMenu() => EnsureMainScene(forceRecreate: true);

    [MenuItem("FlightSim/Apply iOS Player Settings")]
    public static void ApplyIOSSettingsMenu() => ApplyIOSSettings();

    [MenuItem("FlightSim/Build iOS (Xcode project)")]
    public static void BuildIOSMenu() => BuildIOS();

    // ------------------------------------------------------------------ build

    public static void BuildIOS()
    {
        try
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                Debug.Log("[BuildScript] Switching active build target to iOS...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
                    throw new Exception("Could not switch to iOS. Is 'iOS Build Support' installed for this Unity version?");
            }

            EnsureMainScene(forceRecreate: false);
            ApplyIOSSettings();

            string buildPath = Env("BUILD_PATH", DefaultBuildPath);
            Directory.CreateDirectory(buildPath);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = buildPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.None,
            };

            Debug.Log($"[BuildScript] Building Xcode project to '{buildPath}'...");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildScript] SUCCESS: {summary.outputPath} ({summary.totalTime})");
            }
            else
            {
                Debug.LogError($"[BuildScript] FAILED: {summary.result}, errors={summary.totalErrors}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildScript] Exception: {e}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    // ------------------------------------------------------------------ settings (code-first)

    /// <summary>
    /// All iOS player settings live here instead of hand-edited ProjectSettings YAML,
    /// so they are reviewable in git and re-applied on every build.
    /// </summary>
    public static void ApplyIOSSettings()
    {
        PlayerSettings.companyName = CompanyName;
        PlayerSettings.productName = ProductName;
        PlayerSettings.bundleVersion = "0.0.1";
        PlayerSettings.iOS.buildNumber = "1";

        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, Env("BUNDLE_ID", DefaultBundleId));
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);

        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;

        // Flight sim: landscape only.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

        // Signing: automatic. With a free Personal Team you normally choose the team in Xcode.
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        string teamId = Env("IOS_TEAM_ID", "");
        if (!string.IsNullOrEmpty(teamId))
            PlayerSettings.iOS.appleDeveloperTeamID = teamId;

        AssetDatabase.SaveAssets();
        Debug.Log("[BuildScript] iOS player settings applied.");
    }

    // ------------------------------------------------------------------ scene

    /// <summary>
    /// The only scene: one GameObject holding GameBootstrapper. Generated by code so the
    /// repo never needs a hand-written .unity file.
    /// </summary>
    public static void EnsureMainScene(bool forceRecreate)
    {
        bool exists = File.Exists(MainScenePath);
        if (!exists || forceRecreate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MainScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("GameBootstrapper");
            go.AddComponent<FlightSim.GameBootstrapper>();
            if (!EditorSceneManager.SaveScene(scene, MainScenePath))
                throw new Exception($"Failed to save {MainScenePath}");
            AssetDatabase.Refresh();
            Debug.Log($"[BuildScript] Created {MainScenePath}");
        }

        bool listed = EditorBuildSettings.scenes.Any(s => s.path == MainScenePath && s.enabled);
        if (!listed || EditorBuildSettings.scenes.Length != 1)
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
    }

    private static string Env(string key, string fallback)
    {
        string v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
    }
}
