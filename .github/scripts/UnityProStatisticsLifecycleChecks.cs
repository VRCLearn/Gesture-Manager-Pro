using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.SDKBase.Validation.Performance;
using VRCLearn.GestureManagerPro.Diagnostics;

// Isolated acceptance fixture: a real Play transition and the SDK callback pipeline.
// No real avatar assets or third-party optimizers are used by this controlled processor.
[InitializeOnLoad]
public static class UnityProStatisticsLifecycleChecks
{
    internal const string AvatarName = "Pro processing-stage acceptance avatar";
    private const string PendingKey = "VRCLearn.GestureManagerPro.Validation.StatisticsLifecycle";
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    static UnityProStatisticsLifecycleChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            var avatar = GameObject.Find(AvatarName);
            if (!avatar || !VRCBuildPipelineCallbacks.OnPreprocessAvatar(avatar))
                Debug.LogError("[Pro statistics lifecycle] SDK preprocessing did not complete.");
        };
    }

    public static void Prepare()
    {
        var avatar = new GameObject(AvatarName);
        avatar.AddComponent<Animator>();
        avatar.AddComponent<VRCAvatarDescriptor>();
        var material = new Material(Shader.Find("Unlit/Color")) { name = "Owned stage acceptance material" };
        material.color = Color.white;
        for (var index = 0; index < 2; index++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = index == 0 ? "Retained geometry" : "Removed during processing";
            cube.transform.SetParent(avatar.transform, false);
            cube.transform.localPosition = Vector3.right * index;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        }
        SessionState.SetBool(PendingKey, true);
    }

    public static void Run(Action<bool, string> check)
    {
        SessionState.SetBool(PendingKey, false);
        var avatar = GameObject.Find(AvatarName);
        check(avatar && !avatar.transform.Find("Removed during processing"), "Actual SDK pipeline executed the controlled processor on entering Play Mode");
        var history = ModelStatisticsService.History.Where(snapshot => snapshot.AvatarName == AvatarName).ToArray();
        foreach (var mobile in new[] { false, true })
        {
            var before = history.FirstOrDefault(snapshot => snapshot.IsMobile == mobile && snapshot.Source == ModelStatisticsSource.BeforeProcessing);
            var sdk = history.FirstOrDefault(snapshot => snapshot.IsMobile == mobile && snapshot.Source == ModelStatisticsSource.SdkPreprocess);
            var play = history.FirstOrDefault(snapshot => snapshot.IsMobile == mobile && snapshot.Source == ModelStatisticsSource.PlayModeProcessed);
            check(before != null && sdk != null && play != null, "Automatic before/SDK/first-Play records coexist for mobile=" + mobile);
            check(PolyCount(before) == "24" && PolyCount(sdk) == "12" && PolyCount(play) == "12", "Processing preserves 24-triangle before data and independently records 12-triangle processed data");
            check(before.Id != sdk.Id && sdk.Id != play.Id, "Each processing stage has an independent immutable record");
            CompareApw(avatar, play, check);
        }
        UnityProStatisticsChecks.CheckPlayModeCapture(avatar, check);
    }

    private static string PolyCount(ModelStatisticsSnapshot snapshot)
    {
        return snapshot.Metrics.Single(metric => metric.Category == AvatarPerformanceCategory.PolyCount).Value;
    }

    public static void CompareApw(GameObject avatar, ModelStatisticsSnapshot snapshot, Action<bool, string> check)
    {
        // Compile only the upstream DTO/calculator into a fixture-only DLL. Never
        // load APW's window, preprocessing callback, or fallback-status writer.
        var reference = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("ReadOnlyStatsComparison.ApwAlgorithm"))
            .FirstOrDefault(type => type != null);
        if (reference == null)
        {
            check(!Environment.GetCommandLineArgs().Contains("-apwReferenceRequired"), "APW reference DLL is present when required");
            Debug.Log("[Pro APW comparison] SKIP: fixture-only upstream calculator is absent.");
            return;
        }
        var computer = reference.GetNestedType("PerformanceRankComputer", Any);
        var info = computer.GetMethod("Compute", Any).Invoke(null, new object[] { avatar, snapshot.IsMobile });
        var entries = ((IEnumerable)info.GetType().GetField("info", Any).GetValue(info)).Cast<object>().ToArray();
        foreach (var metric in snapshot.Metrics)
        {
            var entry = entries.Single(value => (AvatarPerformanceCategory)value.GetType().GetField("category", Any).GetValue(value) == metric.Category);
            var expected = (string)entry.GetType().GetField("data", Any).GetValue(entry);
            var actual = metric.IsAvailable ? metric.Value : "(unknown)";
            if (metric.IsAvailable && (metric.Category == AvatarPerformanceCategory.DownloadSize ||
                metric.Category == AvatarPerformanceCategory.UncompressedSize || metric.Category == AvatarPerformanceCategory.TextureMegabytes)) actual += "MB";
            if (metric.IsAvailable && (metric.Category == AvatarPerformanceCategory.ParticleTrailsEnabled ||
                metric.Category == AvatarPerformanceCategory.ParticleCollisionEnabled)) actual = actual == "True" ? "Enabled" : "Disabled";
            var rating = (PerformanceRating)entry.GetType().GetField("rating", Any).GetValue(entry);
            check(actual == expected && metric.Rating == rating, "APW original calculator value/rating matches " + metric.Category + " mobile=" + snapshot.IsMobile + ": " + actual + " / " + expected);
        }
        var overall = info.GetType().GetField("overall", Any).GetValue(info);
        check(snapshot.OverallRating == (PerformanceRating)overall.GetType().GetField("rating", Any).GetValue(overall), "APW overall rating matches mobile=" + snapshot.IsMobile);
        Debug.Log("[Pro APW comparison] PASS: " + snapshot.Metrics.Count + " metrics and overall, mobile=" + snapshot.IsMobile);
    }
}

public sealed class ProStatisticsAcceptanceProcessor : IVRCSDKPreprocessAvatarCallback
{
    public int callbackOrder => int.MinValue;

    public bool OnPreprocessAvatar(GameObject avatar)
    {
        if (!EditorApplication.isPlaying || avatar.name != UnityProStatisticsLifecycleChecks.AvatarName) return true;
        var removed = avatar.transform.Find("Removed during processing");
        if (removed) UnityEngine.Object.DestroyImmediate(removed.gameObject);
        return true;
    }
}
