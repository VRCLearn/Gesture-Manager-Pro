using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Diagnostics;
using VRCLearn.GestureManagerPro.Localization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

// Runs against the installed SDK calculator, using disposable scene objects.
// A preprocess observer is exercised directly; this is not an upload or a claim
// that a real SDK build, external processor, or production avatar was verified.
public static class UnityProStatisticsChecks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const string HistoryKey = "VRCLearn.GestureManagerPro.ModelStatistics.History.v1";

    [Serializable]
    private sealed class StoredHistory
    {
        public List<ModelStatisticsSnapshot> Snapshots = new List<ModelStatisticsSnapshot>();
    }

    public static void Run(Action<bool, string> check)
    {
        var service = typeof(ModelStatisticsService);
        var originalHistory = ModelStatisticsService.History.ToArray();
        var originalStored = SessionState.GetString(HistoryKey, string.Empty);
        var originalIgnore = AvatarPerformance.ShouldIgnoreComponent;
        var originalSelection = Selection.objects;
        var originalLanguage = EditorLocalization.Language;
        GameObject avatar = null;
        GameObject invalidAvatar = null;
        GameObject duplicateAvatar = null;
        Material fixtureMaterial = null;
        var changes = 0;
        Action onChange = () => changes++;
        ModelStatisticsService.Changed += onChange;
        try
        {
            ModelStatisticsService.ClearHistory();
            AvatarPerformance.ShouldIgnoreComponent = null;
            check(ModelStatisticsService.History.Count == 0 && changes == 1,
                "Statistics clear removes session history and notifies listeners");

            avatar = new GameObject("Tools");
            avatar.AddComponent<Animator>();
            avatar.AddComponent<VRCAvatarDescriptor>();
            var body = Cube(avatar.transform, "Statistics visible geometry");
            var hidden = Cube(avatar.transform, "Statistics user hidden geometry");
            hidden.hideFlags = HideFlags.HideInHierarchy;
            // Reading the built-in Default-Material for the first time initializes
            // its native shader state. Use an explicitly initialized test asset so
            // that this assertion measures scanner mutation, not Unity's lazy setup.
            fixtureMaterial = new Material(Shader.Find("Unlit/Color")) { name = "Statistics fixture material" };
            fixtureMaterial.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 1f));
            body.GetComponent<Renderer>().sharedMaterial = fixtureMaterial;
            hidden.GetComponent<Renderer>().sharedMaterial = fixtureMaterial;
            var userSender = body.AddComponent<VRCContactSender>();
            userSender.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            userSender.radius = 0.1f;
            userSender.collisionTags.Add("Statistics user contact");
            Selection.activeGameObject = avatar;

            var sourceComponents = avatar.GetComponentsInChildren<Component>(true);
            var sourceJson = sourceComponents.Select(component => EditorJsonUtility.ToJson(component)).ToArray();
            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            var material = body.GetComponent<Renderer>().sharedMaterial;
            var meshJson = EditorJsonUtility.ToJson(mesh);
            var materialJson = EditorJsonUtility.ToJson(material);
            var meshDirty = EditorUtility.IsDirty(mesh);
            var materialDirty = EditorUtility.IsDirty(material);
            var sceneDirty = avatar.scene.isDirty;
            var beforeObjects = Resources.FindObjectsOfTypeAll<GameObject>().Length;

            var desktopStats = new AvatarPerformanceStats(false);
            AvatarPerformance.CalculatePerformanceStats(avatar.name, avatar, desktopStats, false);
            var beforeCaptureChanges = changes;
            var desktop = ModelStatisticsService.Capture(avatar, false);
            check(changes == beforeCaptureChanges + 1 && ReferenceEquals(ModelStatisticsService.History[0], desktop),
                "Statistics capture publishes exactly one newest-first history entry");
            check(desktop.Source == ModelStatisticsSource.BeforeProcessing &&
                desktop.BuildStatus == ModelStatisticsBuildStatus.NotApplicable && !desktop.IsMobile,
                "Edit Mode measurement is labeled before processing rather than a completed SDK build");
            check(desktop.AvatarName == "Tools" && !string.IsNullOrEmpty(desktop.AvatarIdentity),
                "Statistics retain the exact user avatar name and a source identity");
            check(DateTime.TryParse(desktop.CapturedUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var captured) && captured.Kind == DateTimeKind.Utc &&
                Math.Abs((DateTime.UtcNow - captured).TotalSeconds) < 30,
                "Statistics record a usable UTC capture timestamp");
            check(Number(desktop, AvatarPerformanceCategory.PolyCount) == 24 &&
                Number(desktop, AvatarPerformanceCategory.MeshCount) == 2,
                "SDK measures both twelve-triangle cubes, including user-hidden geometry");
            check(Number(desktop, AvatarPerformanceCategory.ContactCount) == 1,
                "A normal hidden user contact is included in SDK contact counts");
            check(desktop.OverallRating == desktopStats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.Overall) &&
                Metric(desktop, AvatarPerformanceCategory.MaterialCount).Rating ==
                desktopStats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.MaterialCount),
                "Snapshot overall and material ratings match an independent SDK calculation");
            check(desktop.Metrics.Count == 31 && desktop.Metrics.Select(metric => metric.Category).Distinct().Count() == 31,
                "Statistics expose one row per SDK metric category");
            CheckSdkMetrics(desktop, desktopStats, check);
            check(desktop.BundleBytes == -1, "Before-processing measurements do not invent a built bundle file size");
            check(Metric(desktop, AvatarPerformanceCategory.ParticleTrailsEnabled).Value == bool.FalseString &&
                Metric(desktop, AvatarPerformanceCategory.ParticleCollisionEnabled).Value == bool.FalseString,
                "SDK boolean metrics retain their machine-readable values for localized display");

            var mobileStats = new AvatarPerformanceStats(true);
            AvatarPerformance.CalculatePerformanceStats(avatar.name, avatar, mobileStats, true);
            var mobile = ModelStatisticsService.Capture(avatar, true);
            check(mobile.IsMobile && mobile.AvatarIdentity == desktop.AvatarIdentity && mobile.Id != desktop.Id,
                "PC and Mobile measurements retain avatar identity but use distinct snapshots");
            check(mobile.OverallRating == mobileStats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.Overall) &&
                Metric(mobile, AvatarPerformanceCategory.MaterialCount).Rating ==
                mobileStats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.MaterialCount),
                "Mobile grades use Mobile SDK thresholds independently of the active editor build target");
            check(Metric(mobile, AvatarPerformanceCategory.MaterialCount).Rating !=
                Metric(desktop, AvatarPerformanceCategory.MaterialCount).Rating,
                "Two material slots demonstrate the different PC and Mobile performance thresholds");
            CheckSdkMetrics(mobile, mobileStats, check);
            check(sourceComponents.SequenceEqual(avatar.GetComponentsInChildren<Component>(true)) &&
                sourceComponents.Select(component => EditorJsonUtility.ToJson(component)).SequenceEqual(sourceJson),
                "Calculating statistics preserves source components, transforms and serialized avatar settings");
            check(EditorJsonUtility.ToJson(mesh) == meshJson && EditorJsonUtility.ToJson(material) == materialJson &&
                EditorUtility.IsDirty(mesh) == meshDirty && EditorUtility.IsDirty(material) == materialDirty,
                "Statistics preserve shared mesh/material asset content and dirty state");
            check(Selection.activeGameObject == avatar && avatar.scene.isDirty == sceneDirty &&
                Resources.FindObjectsOfTypeAll<GameObject>().Length == beforeObjects,
                "Statistics do not change selection, dirty the scene or leave temporary scene objects");

            CheckTemporaryComponents(avatar, body, userSender, check);
            CheckOptionalSdkMetrics(check);

            var preview = ModelStatisticsService.CaptureBeforeProcessing(avatar, false);
            check(preview.Source == ModelStatisticsSource.BeforeProcessing &&
                preview.BuildStatus == ModelStatisticsBuildStatus.NotApplicable && preview.BundleBytes == -1,
                "The dedicated before-processing action remains separate from processed or uploaded results");
            check(!string.IsNullOrWhiteSpace(preview.Notes), "Before-processing records the limits of its capture stage");
            avatar.name = "Statistics renamed source";
            check(desktop.AvatarName == "Tools" && Number(desktop, AvatarPerformanceCategory.PolyCount) == 24,
                "Historical snapshots remain independent of later source avatar changes");
            avatar.name = "Tools";

            var observer = new ModelStatisticsPreprocessCallback();
            check(observer.callbackOrder == int.MaxValue,
                "SDK statistics observer runs at the final preprocess callback priority");
            var beforeObserver = ModelStatisticsService.History.Count;
            check(observer.OnPreprocessAvatar(avatar) && ModelStatisticsService.History.Count == beforeObserver + 2,
                "SDK preprocessing observer captures PC and Mobile without stopping the pipeline");
            var observed = ModelStatisticsService.History.Take(2).ToArray();
            check(observed.All(snapshot => snapshot.Source == ModelStatisticsSource.SdkPreprocess &&
                snapshot.BuildStatus == ModelStatisticsBuildStatus.NotApplicable) &&
                observed.Select(snapshot => snapshot.IsMobile).Distinct().Count() == 2,
                "A preprocess callback alone does not claim successful bundle generation");
            CheckSdkMetrics(observed.Single(snapshot => !snapshot.IsMobile), desktopStats, check);
            CheckSdkMetrics(observed.Single(snapshot => snapshot.IsMobile), mobileStats, check);
            invalidAvatar = new GameObject("Statistics object without descriptor");
            beforeObserver = ModelStatisticsService.History.Count;
            check(observer.OnPreprocessAvatar(invalidAvatar) && observer.OnPreprocessAvatar(null) &&
                ModelStatisticsService.History.Count == beforeObserver,
                "Statistics observer ignores invalid targets without blocking SDK processing");

            duplicateAvatar = new GameObject("Tools");
            duplicateAvatar.AddComponent<VRCAvatarDescriptor>();
            check(ModelStatisticsService.GetAvatarIdentity(avatar) != ModelStatisticsService.GetAvatarIdentity(duplicateAvatar),
                "Distinct unsaved avatars with identical names retain different statistics identities");

            CheckBuildLifecycle(avatar, observer, check);

            var beforeInvalid = ModelStatisticsService.History.Count;
            var beforeInvalidChanges = changes;
            check(ThrowsArgument(() => ModelStatisticsService.Capture(null, false)) &&
                ThrowsArgument(() => ModelStatisticsService.Capture(invalidAvatar, false)),
                "Manual statistics requests reject missing avatars and missing descriptors");
            check(ModelStatisticsService.History.Count == beforeInvalid && changes == beforeInvalidChanges,
                "Rejected statistics requests neither publish history nor notify observers");

            var oldestId = desktop.Id;
            for (var index = 0; index < 50; index++) ModelStatisticsService.Capture(avatar, index % 2 == 0);
            check(ModelStatisticsService.History.Count == 48 &&
                !ModelStatisticsService.History.Any(snapshot => snapshot.Id == oldestId),
                "Session history retains the newest 48 measurements and evicts older entries");
            var stored = JsonUtility.FromJson<StoredHistory>(SessionState.GetString(HistoryKey, string.Empty));
            check(stored != null && stored.Snapshots.Select(snapshot => snapshot.Id)
                    .SequenceEqual(ModelStatisticsService.History.Select(snapshot => snapshot.Id)),
                "Session serialization retains the bounded newest-first snapshot list");
            check(stored.Snapshots[0].AvatarIdentity == ModelStatisticsService.History[0].AvatarIdentity &&
                Number(stored.Snapshots[0], AvatarPerformanceCategory.PolyCount) == 24,
                "Session snapshots round-trip their source identity and SDK metric values");

            CheckWindow(avatar, duplicateAvatar, check);
            CheckLegacyHistory(avatar, check);
            ModelStatisticsService.ClearHistory();
            check(ModelStatisticsService.History.Count == 0 &&
                JsonUtility.FromJson<StoredHistory>(SessionState.GetString(HistoryKey, string.Empty)).Snapshots.Count == 0,
                "Clearing statistics also removes persisted measurements");
        }
        finally
        {
            ModelStatisticsService.Changed -= onChange;
            AvatarPerformance.ShouldIgnoreComponent = originalIgnore;
            if (avatar) GestureManager.ControlledAvatars.Remove(avatar);
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            if (invalidAvatar) UnityEngine.Object.DestroyImmediate(invalidAvatar);
            if (duplicateAvatar) UnityEngine.Object.DestroyImmediate(duplicateAvatar);
            if (fixtureMaterial) UnityEngine.Object.DestroyImmediate(fixtureMaterial);
            Selection.objects = originalSelection;
            EditorLocalization.Language = originalLanguage;
            // Restore pre-existing session diagnostics when this runner is invoked interactively.
            var history = (IList)service.GetField("Snapshots", Any).GetValue(null);
            history.Clear();
            foreach (var snapshot in originalHistory) history.Add(snapshot);
            if (string.IsNullOrEmpty(originalStored)) SessionState.EraseString(HistoryKey);
            else SessionState.SetString(HistoryKey, originalStored);
        }
    }

    private static void CheckTemporaryComponents(GameObject avatar, GameObject body, VRCContactSender userSender,
        Action<bool, string> check)
    {
        var originalIgnore = AvatarPerformance.ShouldIgnoreComponent;
        var helper = body.AddComponent<VRCContactSender>();
        helper.hideFlags = HideFlags.HideAndDontSave;
        helper.radius = 0.12f;
        helper.collisionTags.Add("Statistics temporary contact");
        try
        {
            var unowned = ModelStatisticsService.Capture(avatar, false);
            check(Number(unowned, AvatarPerformanceCategory.ContactCount) == 2,
                "An unmanaged avatar keeps hidden components in its ordinary SDK measurement");
            GestureManager.ControlledAvatars.Add(avatar, null);
            var controlled = ModelStatisticsService.Capture(avatar, false);
            check(Number(controlled, AvatarPerformanceCategory.ContactCount) == 2 &&
                Number(controlled, AvatarPerformanceCategory.PolyCount) == 24,
                "APW-compatible measurements retain the SDK's temporary and user contacts while simulation is active");
            AvatarPerformance.ShouldIgnoreComponent = component => component == userSender;
            var priorIgnore = AvatarPerformance.ShouldIgnoreComponent;
            var sdk = new AvatarPerformanceStats(false);
            AvatarPerformance.CalculatePerformanceStats(avatar.name, avatar, sdk, false);
            var filtered = ModelStatisticsService.Capture(avatar, false);
            check(Number(filtered, AvatarPerformanceCategory.ContactCount) == sdk.contactCount,
                "Statistics retain raw SDK contact counting instead of applying a custom simulator correction");
            check(ReferenceEquals(AvatarPerformance.ShouldIgnoreComponent, priorIgnore),
                "Statistics leave the SDK's existing component filter unchanged");
            check(helper && helper.hideFlags == HideFlags.HideAndDontSave && userSender &&
                userSender.hideFlags == (HideFlags.HideInHierarchy | HideFlags.HideInInspector),
                "Raw SDK measurements never remove or change simulator or user-authored source components");
        }
        finally
        {
            AvatarPerformance.ShouldIgnoreComponent = originalIgnore;
            GestureManager.ControlledAvatars.Remove(avatar);
            if (helper) UnityEngine.Object.DestroyImmediate(helper);
        }
    }

    private static void CheckBuildLifecycle(GameObject avatar, ModelStatisticsPreprocessCallback observer,
        Action<bool, string> check)
    {
        // Injects the SDK's event payloads; it does not execute a real build.
        var service = typeof(ModelStatisticsService);
        var bundle = Path.GetTempFileName();
        try
        {
            service.GetMethod("OnBuildStart", Any).Invoke(null, new object[] { null, avatar });
            observer.OnPreprocessAvatar(avatar);
            var actualMobile = ModelStatisticsService.IsCurrentPlatformMobile();
            var pending = ModelStatisticsService.History.Take(2).ToArray();
            check(pending.Length == 2 && pending.Select(snapshot => snapshot.IsMobile).Distinct().Count() == 2 &&
                pending.All(snapshot => snapshot.Source == ModelStatisticsSource.SdkPreprocess &&
                    snapshot.BuildStatus == ModelStatisticsBuildStatus.Pending && snapshot.BundleBytes == -1 &&
                    snapshot.HasBuildPlatform && snapshot.BuildPlatformIsMobile == actualMobile),
                "An SDK build observes both rating platforms and records its actual frozen build target");
            service.GetMethod("OnBuildError", Any).Invoke(null, new object[] { null, "Injected validation build error" });
            var failed = ModelStatisticsService.History.Take(2).ToArray();
            check(SameIds(pending, failed) && failed.All(snapshot => snapshot.Source == ModelStatisticsSource.SdkPreprocess &&
                snapshot.BuildStatus == ModelStatisticsBuildStatus.Failed && snapshot.BundleBytes == -1),
                "SDK failure updates both related platform snapshots without reporting a completed bundle");
            check(pending.All(snapshot => snapshot.BuildStatus == ModelStatisticsBuildStatus.Pending),
                "Build completion replaces both history entries without mutating returned snapshot objects");
            service.GetMethod("OnBuildFinish", Any).Invoke(null, new object[] { null, "Injected validation finish" });
            check(ModelStatisticsService.History.Take(2).SequenceEqual(failed),
                "A later SDK finish notification preserves the failure on both platform records");

            File.WriteAllBytes(bundle, new byte[23]);
            service.GetMethod("OnBuildStart", Any).Invoke(null, new object[] { null, avatar });
            observer.OnPreprocessAvatar(avatar);
            var pendingSuccess = ModelStatisticsService.History.Take(2).ToArray();
            service.GetMethod("OnBuildSuccess", Any).Invoke(null, new object[] { null, bundle });
            var succeeded = ModelStatisticsService.History.Take(2).ToArray();
            check(SameIds(pendingSuccess, succeeded) && succeeded.All(snapshot =>
                snapshot.Source == ModelStatisticsSource.SdkBuild && snapshot.BuildStatus == ModelStatisticsBuildStatus.Succeeded &&
                snapshot.BundleBytes == (snapshot.IsMobile == actualMobile ? 23 : -1) &&
                snapshot.HasBuildPlatform && snapshot.BuildPlatformIsMobile == actualMobile),
                "SDK success updates both grades and associates bundle bytes only with the actual build platform");
            check(succeeded.All(snapshot => !Metric(snapshot, AvatarPerformanceCategory.DownloadSize).IsAvailable &&
                !Metric(snapshot, AvatarPerformanceCategory.UncompressedSize).IsAvailable),
                "A local bundle file size is kept separate from unavailable official download/uncompressed metrics");
            service.GetMethod("OnBuildFinish", Any).Invoke(null, new object[] { null, "Injected validation finish" });
            check(ModelStatisticsService.History.Take(2).SequenceEqual(succeeded),
                "A later SDK finish notification preserves success on both platform records");

            service.GetMethod("OnBuildStart", Any).Invoke(null, new object[] { null, avatar });
            observer.OnPreprocessAvatar(avatar);
            var pendingDisconnect = ModelStatisticsService.History.Take(2).ToArray();
            service.GetMethod("DisconnectBuilder", Any).Invoke(null, null);
            var disconnected = ModelStatisticsService.History.Take(2).ToArray();
            check(SameIds(pendingDisconnect, disconnected) && disconnected.All(snapshot =>
                snapshot.BuildStatus == ModelStatisticsBuildStatus.Unconfirmed &&
                snapshot.Source == ModelStatisticsSource.SdkPreprocess && snapshot.BundleBytes == -1),
                "Disconnecting SDK observation leaves both platform records unconfirmed instead of failed");
            check(pendingDisconnect.All(snapshot => snapshot.BuildStatus == ModelStatisticsBuildStatus.Pending),
                "SDK disconnect preserves both previously returned pending snapshot objects");
            service.GetMethod("OnBuildSuccess", Any).Invoke(null, new object[] { null, bundle });
            service.GetMethod("OnBuildFinish", Any).Invoke(null, new object[] { null, "Injected disconnected finish" });
            check(ModelStatisticsService.History.Take(2).SequenceEqual(disconnected),
                "SDK notifications after disconnect cannot complete either abandoned platform record");

            service.GetMethod("OnBuildStart", Any).Invoke(null, new object[] { null, avatar });
            observer.OnPreprocessAvatar(avatar);
            var pendingReload = ModelStatisticsService.History.Take(2).ToArray();
            var storedPending = SessionState.GetString(HistoryKey, string.Empty);
            service.GetMethod("OnBuildError", Any).Invoke(null, new object[] { null, "Reset injected build correlation" });
            SessionState.SetString(HistoryKey, storedPending);
            service.GetMethod("RestoreHistory", Any).Invoke(null, null);
            var recovered = ModelStatisticsService.History.Take(2).ToArray();
            check(SameIds(pendingReload, recovered) && recovered.All(snapshot =>
                snapshot.BuildStatus == ModelStatisticsBuildStatus.Unconfirmed &&
                snapshot.Source == ModelStatisticsSource.SdkPreprocess && snapshot.BundleBytes == -1 &&
                Number(snapshot, AvatarPerformanceCategory.PolyCount) == 24),
                "Restoring session history preserves both sets of metrics and marks pending completion unconfirmed");
            var restoredStored = JsonUtility.FromJson<StoredHistory>(SessionState.GetString(HistoryKey, string.Empty));
            check(SameIds(pendingReload, restoredStored.Snapshots.Take(2)) &&
                restoredStored.Snapshots.Take(2).All(snapshot => snapshot.BuildStatus == ModelStatisticsBuildStatus.Unconfirmed),
                "Both recovered unconfirmed platform records are written back to SessionState");
        }
        finally
        {
            service.GetMethod("OnBuildFinish", Any).Invoke(null, new object[] { null, "Validation cleanup" });
            File.Delete(bundle);
        }
    }

    private static void CheckWindow(GameObject avatar, GameObject otherAvatar, Action<bool, string> check)
    {
        ModelStatisticsWindow window = null;
        StyleSheet externalStyle = null;
        var language = EditorLocalization.Language;
        try
        {
            EditorLocalization.Language = ProLanguage.English;
            new ModelStatisticsPreprocessCallback().OnPreprocessAvatar(avatar);
            var otherSnapshot = ModelStatisticsService.Capture(otherAvatar, false);
            window = ScriptableObject.CreateInstance<ModelStatisticsWindow>();
            var defaultStyles = Enumerable.Range(0, window.rootVisualElement.styleSheets.count)
                .Select(index => window.rootVisualElement.styleSheets[index]).ToArray();
            externalStyle = ScriptableObject.CreateInstance<StyleSheet>();
            window.rootVisualElement.styleSheets.Add(externalStyle);
            window.Show();
            window.CreateGUI();
            check(window.rootVisualElement.panel != null &&
                defaultStyles.All(window.rootVisualElement.styleSheets.Contains) &&
                window.rootVisualElement.styleSheets.Contains(externalStyle),
                "Statistics window attaches to Unity's panel and retains editor/external stylesheets");
            check(window.rootVisualElement.Q<ObjectField>("avatarField") != null &&
                window.rootVisualElement.Q<DropdownField>("platformField") != null &&
                window.rootVisualElement.Q<DropdownField>("stageField") != null &&
                window.rootVisualElement.Q<DropdownField>("snapshotField") != null &&
                window.rootVisualElement.Q<Button>("refreshButton") != null &&
                window.rootVisualElement.Q<Button>("useSelectedButton") != null &&
                window.rootVisualElement.Q<Button>("allAvatarsButton") != null &&
                window.rootVisualElement.Q("statusLabel") != null &&
                window.rootVisualElement.Q("metricsTable") != null,
                "Statistics window exposes avatar, platform, history, refresh and metric controls");
            window.rootVisualElement.Q<ObjectField>("avatarField").value = avatar;
            var platform = window.rootVisualElement.Q<DropdownField>("platformField");
            platform.index = 0;
            typeof(ModelStatisticsWindow).GetMethod("CaptureSelected", Any).Invoke(window, null);
            check(ModelStatisticsService.History[0].AvatarName == "Tools" &&
                ModelStatisticsService.History[0].Source == ModelStatisticsSource.BeforeProcessing,
                "Edit Mode refresh records before-processing data without running processors or a build");
            var visibleSnapshots = VisibleSnapshots(window);
            check(ModelStatisticsService.History.Any(snapshot => snapshot.Id == otherSnapshot.Id) &&
                visibleSnapshots.Any() && visibleSnapshots.All(snapshot => snapshot.AvatarIdentity != otherSnapshot.AvatarIdentity),
                "Window history excludes snapshots from a different unsaved avatar with the same name");
            var selectionId = ModelStatisticsService.History[0].Id;
            check(window.rootVisualElement.Q("metricsTable").childCount ==
                ModelStatisticsService.History[0].Metrics.Count + 1 &&
                window.rootVisualElement.Q<HelpBox>("statusLabel").text.Contains("Before Processing"),
                "Window renders every SDK metric and explicitly labels the before-processing source");
            CheckRatingResources(window, check);
            CheckRenderedRatings(window, check);
            var stage = window.rootVisualElement.Q<DropdownField>("stageField");
            check(stage.choices.SequenceEqual(new[] { "All Stages", "Before Processing", "After Play Mode Processing",
                "After SDK Processing", "Current Play Mode State", "Legacy Snapshots" }),
                "Statistics window separates before-processing, Play, SDK, current-state and legacy stages");
            stage.index = 3;
            platform.index = 1;
            var sdkRows = VisibleSnapshots(window).ToArray();
            check(sdkRows.Length > 0 && sdkRows.All(snapshot => snapshot.IsMobile &&
                (snapshot.Source == ModelStatisticsSource.SdkPreprocess || snapshot.Source == ModelStatisticsSource.SdkBuild)),
                "SDK stage selection filters history to the selected platform's processed snapshots");
            selectionId = (string)typeof(ModelStatisticsWindow).GetField("_snapshotId", Any).GetValue(window);
            var latestId = ModelStatisticsService.History[0].Id;
            check(window.titleContent.text == "Model Statistics", "Statistics window has its English title");
            EditorLocalization.Language = ProLanguage.SimplifiedChinese;
            check(window.titleContent.text == "模型统计" &&
                window.rootVisualElement.Q<Button>("refreshButton").text == EditorLocalization.Tr("Measure Before Processing"),
                "Open statistics window updates its Chinese title and action captions live");
            check(window.rootVisualElement.Q<ObjectField>("avatarField").value == avatar && avatar.name == "Tools" &&
                ModelStatisticsService.History[0].Id == latestId &&
                window.rootVisualElement.Q<DropdownField>("platformField").index == 1 &&
                window.rootVisualElement.Q<DropdownField>("stageField").index == 3 &&
                (string)typeof(ModelStatisticsWindow).GetField("_snapshotId", Any).GetValue(window) == selectionId,
                "Language switching retains avatar, platform, processing stage and selected snapshot without recapturing");
            CheckRenderedRatings(window, check);
            var chineseMetrics = window.rootVisualElement.Q("metricsTable").Query<Label>(className: "statistics-metric")
                .ToList().Select(label => label.text).ToArray();
            check(new[] { "三角面数", "纹理内存", "网格渲染器", "约束深度" }.All(chineseMetrics.Contains),
                "Statistics captions describe the actual SDK geometry, memory and constraint metrics");
            var styleCount = window.rootVisualElement.styleSheets.count;
            window.CreateGUI();
            EditorLocalization.Language = ProLanguage.English;
            check(window.rootVisualElement.styleSheets.count == styleCount &&
                defaultStyles.All(window.rootVisualElement.styleSheets.Contains) &&
                window.rootVisualElement.styleSheets.Contains(externalStyle),
                "Statistics rebuilds and language changes preserve Unity styling without duplicate stylesheets");
            window.rootVisualElement.Q<DropdownField>("stageField").index = 1;
            check(VisibleSnapshots(window).Any() && VisibleSnapshots(window).All(snapshot =>
                snapshot.Source == ModelStatisticsSource.BeforeProcessing && snapshot.IsMobile),
                "Before-processing history remains available as an independent stage");
            window.rootVisualElement.Q<DropdownField>("stageField").index = 3;
            typeof(ModelStatisticsWindow).GetMethod("CaptureSelected", Any).Invoke(window, null);
            check(ModelStatisticsService.History[0].IsMobile &&
                window.rootVisualElement.Q<DropdownField>("stageField").index == 1 &&
                (string)typeof(ModelStatisticsWindow).GetField("_snapshotId", Any).GetValue(window) ==
                ModelStatisticsService.History[0].Id,
                "Manual measurement switches from the SDK stage to its new before-processing Mobile record");
            window.rootVisualElement.Q<DropdownField>("platformField").index = 0;
            var beforeAll = ModelStatisticsService.History.Count;
            typeof(ModelStatisticsWindow).GetMethod("ShowAllAvatars", Any).Invoke(window, null);
            check(window.rootVisualElement.Q<ObjectField>("avatarField").value == null &&
                ModelStatisticsService.History.Count == beforeAll &&
                VisibleSnapshots(window).Any(snapshot => snapshot.AvatarIdentity == otherSnapshot.AvatarIdentity) &&
                window.rootVisualElement.Q<DropdownField>("stageField").index == 1,
                "All Avatars removes identity filtering while preserving stage and measurements");
            var beforeInvalid = ModelStatisticsService.History[0].Id;
            window.rootVisualElement.Q<ObjectField>("avatarField").value = null;
            typeof(ModelStatisticsWindow).GetMethod("CaptureSelected", Any).Invoke(window, null);
            check(ModelStatisticsService.History[0].Id == beforeInvalid &&
                window.rootVisualElement.Q<HelpBox>("statusLabel").messageType == HelpBoxMessageType.Error,
                "Window reports an absent avatar without publishing a false measurement");
            Selection.activeGameObject = avatar.transform.GetChild(0).gameObject;
            typeof(ModelStatisticsWindow).GetMethod("UseSelection", Any).Invoke(window, null);
            check(window.rootVisualElement.Q<ObjectField>("avatarField").value == avatar &&
                ModelStatisticsService.History[0].AvatarName == "Tools",
                "Using a selected avatar child resolves its descriptor root before measurement");
            CheckUnratedAndEmptyDisplay(window, check);
        }
        finally
        {
            if (window) UnityEngine.Object.DestroyImmediate(window);
            if (externalStyle) UnityEngine.Object.DestroyImmediate(externalStyle);
            EditorLocalization.Language = language;
        }
    }

    private static readonly Dictionary<PerformanceRating, string> RatingResources = new Dictionary<PerformanceRating, string>
    {
        { PerformanceRating.Excellent, "Great" },
        { PerformanceRating.Good, "Good" },
        { PerformanceRating.Medium, "Medium" },
        { PerformanceRating.Poor, "Poor" },
        { PerformanceRating.VeryPoor, "Horrible" }
    };

    private static Texture ExpectedRatingIcon(PerformanceRating rating)
    {
        return RatingResources.TryGetValue(rating, out var resource)
            ? Resources.Load<Texture2D>("PerformanceIcons/Perf_" + resource + "_32") : null;
    }

    private static bool MatchesRatingIcon(Texture actual, PerformanceRating rating)
    {
        var sdkIcon = ExpectedRatingIcon(rating);
        if (sdkIcon) return actual == sdkIcon;
        var sharedHelp = EditorGUIUtility.Load("Icons/d__Help@2x.png") as Texture;
        return actual && actual != sharedHelp && actual.width == 32 && actual.height == 32 &&
            actual.filterMode == FilterMode.Bilinear &&
            (actual.hideFlags & HideFlags.DontSave) == HideFlags.DontSave;
    }

    private static void CheckRatingResources(ModelStatisticsWindow window, Action<bool, string> check)
    {
        var getter = typeof(ModelStatisticsWindow).GetMethod("RatingIcon", Any);
        foreach (var pair in RatingResources)
        {
            var sdkIcon = ExpectedRatingIcon(pair.Key);
            var actual = getter.Invoke(window, new object[] { pair.Key }) as Texture;
            check(sdkIcon && actual == sdkIcon &&
                AssetDatabase.GetAssetPath(actual).StartsWith("Packages/com.vrchat.base/", StringComparison.Ordinal),
                "Statistics reuse the SDK's actual " + pair.Value + " performance icon for " + pair.Key);
        }
        var helpIcon = EditorGUIUtility.Load("Icons/d__Help@2x.png") as Texture;
        var originalFilter = helpIcon.filterMode;
        var unrated = getter.Invoke(window, new object[] { PerformanceRating.None }) as Texture;
        check(helpIcon && helpIcon.width == 32 && helpIcon.height == 32 &&
            MatchesRatingIcon(unrated, PerformanceRating.None) &&
            getter.Invoke(window, new object[] { (PerformanceRating)999 }) as Texture == unrated &&
            getter.Invoke(window, new object[] { PerformanceRating.None }) as Texture == unrated,
            "Unrated grades reuse a private native-resolution help icon with smooth filtering at scaled UI sizes");
        check(helpIcon.filterMode == originalFilter,
            "Statistics do not change the filter mode of Unity's shared help icon");
        check(unrated is Texture2D copy && copy.graphicsFormat == helpIcon.graphicsFormat &&
            copy.mipmapCount == helpIcon.mipmapCount && copy.wrapMode == helpIcon.wrapMode,
            "The private help texture preserves the native format, mip levels and edge wrapping");
        var temporaryWindow = ScriptableObject.CreateInstance<ModelStatisticsWindow>();
        Texture ownedIcon = null;
        try
        {
            ownedIcon = getter.Invoke(temporaryWindow, new object[] { PerformanceRating.None }) as Texture;
            check(ownedIcon && ownedIcon != unrated && MatchesRatingIcon(ownedIcon, PerformanceRating.None),
                "Each statistics window owns its filtered help texture without sharing mutable state");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temporaryWindow);
        }
        check(!ownedIcon && unrated && helpIcon && helpIcon.filterMode == originalFilter,
            "Closing a statistics window releases its help texture and preserves other windows and shared icons");
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            CheckNonReadableIconCopy(check);
        else Debug.Log("[Pro statistics validation] SKIP: non-readable GPU texture copy requires a graphics-enabled invocation.");
    }

    private static void CheckNonReadableIconCopy(Action<bool, string> check)
    {
        var source = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Texture2D copy = null;
        var errors = new List<string>();
        Application.LogCallback onLog = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        };
        var previous = RenderTexture.active;
        Application.logMessageReceived += onLog;
        try
        {
            var pixels = Enumerable.Range(0, 32 * 32).Select(index =>
                new Color32((byte)(index % 32 * 8), (byte)(index / 32 * 8), 128, (byte)(index % 3 * 127))).ToArray();
            source.SetPixels32(pixels);
            source.Apply(false, true);
            copy = (Texture2D)typeof(ModelStatisticsWindow).GetMethod("CopyUnratedIcon", Any).Invoke(null, new object[] { source });
            check(!source.isReadable && copy && errors.Count == 0,
                "Copying a CPU-unreadable help texture does not emit errors");
            var renderedSource = ReadGpuPixels(source);
            check(renderedSource.Select(pixel => pixel.a).SequenceEqual(pixels.Select(pixel => pixel.a)) &&
                renderedSource.SequenceEqual(ReadGpuPixels(copy)),
                "The GPU help copy preserves every rendered color and alpha pixel");
            check(errors.Count == 0 && RenderTexture.active == previous &&
                !source.isReadable && source.filterMode == FilterMode.Point && source.wrapMode == TextureWrapMode.Clamp,
                "Help copying and verification preserve shared texture settings and the active render target");
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            if (copy) UnityEngine.Object.DestroyImmediate(copy);
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static Color32[] ReadGpuPixels(Texture texture)
    {
        var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            readable.Apply(false, false);
            return readable.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(readable);
        }
    }

    private static void CheckRenderedRatings(ModelStatisticsWindow window, Action<bool, string> check)
    {
        var selected = (string)typeof(ModelStatisticsWindow).GetField("_snapshotId", Any).GetValue(window);
        var snapshot = VisibleSnapshots(window).Single(item => item.Id == selected);
        var chinese = EditorLocalization.Language == ProLanguage.SimplifiedChinese;
        var captions = new Dictionary<PerformanceRating, string>
        {
            { PerformanceRating.Excellent, chinese ? "优秀" : "Excellent" },
            { PerformanceRating.Good, chinese ? "良好" : "Good" },
            { PerformanceRating.Medium, chinese ? "中等" : "Medium" },
            { PerformanceRating.Poor, chinese ? "较差" : "Poor" },
            { PerformanceRating.VeryPoor, chinese ? "极差" : "Very Poor" }
        };
        var unrated = chinese ? "未评级" : "Not Rated";
        var labelGetter = typeof(ModelStatisticsWindow).GetMethod("RatingLabel", Any);
        check(captions.All(pair => (string)labelGetter.Invoke(null, new object[] { pair.Key }) == pair.Value) &&
            (string)labelGetter.Invoke(null, new object[] { PerformanceRating.None }) == unrated &&
            (string)labelGetter.Invoke(null, new object[] { (PerformanceRating)999 }) == unrated,
            "Performance captions preserve all five SDK grades and explicitly distinguish unrated results");
        var overallCaption = captions.TryGetValue(snapshot.OverallRating, out var caption) ? caption : unrated;
        var overallIcon = window.rootVisualElement.Q<Image>("overallRatingIcon");
        check(overallIcon != null && MatchesRatingIcon(overallIcon.image, snapshot.OverallRating) &&
            overallIcon.tooltip == overallCaption &&
            window.rootVisualElement.Q<Label>("overallRating").text == EditorLocalization.Format("Overall: {0}", overallCaption),
            "Overall performance displays the selected snapshot's matching SDK icon and localized caption");

        var rows = window.rootVisualElement.Q("metricsTable").Query<VisualElement>(className: "statistics-row").ToList();
        check(rows.Count == snapshot.Metrics.Count && snapshot.Metrics.Select((metric, index) =>
        {
            var cell = rows[index].Q<VisualElement>(className: "statistics-rating");
            var label = cell?.Q<Label>(className: "statistics-rating-label");
            if (label == null) return false;
            var expectedCaption = captions.TryGetValue(metric.Rating, out var metricCaption) ? metricCaption : unrated;
            var icons = cell.Query<Image>(className: "statistics-rating-icon").ToList();
            return label.text == expectedCaption && icons.Count == 1 && MatchesRatingIcon(icons[0].image, metric.Rating) &&
                (cell.tooltip == expectedCaption || label.tooltip == expectedCaption ||
                    icons.Any(icon => icon.tooltip == expectedCaption));
        }).All(matches => matches),
            "All metric rows render their own SDK grade with the matching icon, caption and accessible tooltip");
    }

    private static void CheckUnratedAndEmptyDisplay(ModelStatisticsWindow window, Action<bool, string> check)
    {
        var previousHistory = ModelStatisticsService.History.ToArray();
        var previousStored = SessionState.GetString(HistoryKey, string.Empty);
        var selected = (string)typeof(ModelStatisticsWindow).GetField("_snapshotId", Any).GetValue(window);
        var template = VisibleSnapshots(window).Single(snapshot => snapshot.Id == selected);
        var history = (IList)typeof(ModelStatisticsService).GetField("Snapshots", Any).GetValue(null);
        try
        {
            foreach (var grade in new[] { PerformanceRating.None, (PerformanceRating)999 })
            {
                var unknown = JsonUtility.FromJson<ModelStatisticsSnapshot>(JsonUtility.ToJson(template));
                typeof(ModelStatisticsSnapshot).GetField("id", Any).SetValue(unknown, Guid.NewGuid().ToString("N"));
                typeof(ModelStatisticsSnapshot).GetField("overallRating", Any).SetValue(unknown, grade);
                typeof(ModelStatisticsMetric).GetField("rating", Any).SetValue(unknown.Metrics[0], grade);
                history.Insert(0, unknown);
                typeof(ModelStatisticsWindow).GetField("_snapshotId", Any).SetValue(window, unknown.Id);
                typeof(ModelStatisticsWindow).GetMethod("RefreshSnapshots", Any).Invoke(window, null);
                var firstRow = window.rootVisualElement.Q("metricsTable")
                    .Query<VisualElement>(className: "statistics-row").First();
                var cell = firstRow.Q<VisualElement>(className: "statistics-rating");
                var icons = cell.Query<Image>(className: "statistics-rating-icon").ToList();
                var helpIcon = window.rootVisualElement.Q<Image>("overallRatingIcon").image;
                check(MatchesRatingIcon(helpIcon, grade) &&
                    window.rootVisualElement.Q<Label>("overallRating").text.Contains("Not Rated") &&
                    icons.Count == 1 && icons[0].image == helpIcon &&
                    cell.Q<Label>(className: "statistics-rating-label").text == "Not Rated",
                    "Unrated snapshot " + (int)grade + " renders the SDK help icon with its explicit caption");
            }
            ModelStatisticsService.ClearHistory();
            check(window.rootVisualElement.Q<Image>("overallRatingIcon").image == null &&
                string.IsNullOrEmpty(window.rootVisualElement.Q<Label>("overallRating").text) &&
                window.rootVisualElement.Q("metricsTable").childCount == 0,
                "Clearing statistics removes the previous overall icon and all metric grade displays");
        }
        finally
        {
            history.Clear();
            foreach (var snapshot in previousHistory) history.Add(snapshot);
            if (string.IsNullOrEmpty(previousStored)) SessionState.EraseString(HistoryKey);
            else SessionState.SetString(HistoryKey, previousStored);
            typeof(ModelStatisticsWindow).GetField("_snapshotId", Any).SetValue(window, selected);
            typeof(ModelStatisticsWindow).GetMethod("RefreshSnapshots", Any).Invoke(window, null);
        }
    }

    private static void CheckLegacyHistory(GameObject avatar, Action<bool, string> check)
    {
        var previousHistory = ModelStatisticsService.History.ToArray();
        var previousStored = SessionState.GetString(HistoryKey, string.Empty);
        var previousLanguage = EditorLocalization.Language;
        ModelStatisticsWindow window = null;
        try
        {
            var beforeProcessing = ModelStatisticsService.CaptureBeforeProcessing(avatar, false);
            new ModelStatisticsPreprocessCallback().OnPreprocessAvatar(avatar);
            var currentSdk = ModelStatisticsService.History.First(snapshot => !snapshot.IsMobile);
            check(beforeProcessing.FormatVersion == 1 && currentSdk.FormatVersion == 1,
                "New before-processing and SDK measurements explicitly identify the current snapshot format");

            var oldPreprocess = JsonUtility.FromJson<ModelStatisticsSnapshot>(JsonUtility.ToJson(currentSdk));
            var oldBuild = JsonUtility.FromJson<ModelStatisticsSnapshot>(JsonUtility.ToJson(currentSdk));
            typeof(ModelStatisticsSnapshot).GetField("id", Any).SetValue(oldPreprocess, "legacy-sdk-preprocess");
            typeof(ModelStatisticsSnapshot).GetField("id", Any).SetValue(oldBuild, "legacy-sdk-build");
            typeof(ModelStatisticsSnapshot).GetField("source", Any).SetValue(oldBuild, ModelStatisticsSource.SdkBuild);
            typeof(ModelStatisticsSnapshot).GetField("buildStatus", Any).SetValue(oldBuild, ModelStatisticsBuildStatus.Succeeded);
            foreach (var oldSnapshot in new[] { oldPreprocess, oldBuild })
                typeof(ModelStatisticsMetric).GetField("value", Any)
                    .SetValue(Metric(oldSnapshot, AvatarPerformanceCategory.AABB), "1 × 1 × 1");

            var oldPreprocessJson = WithoutFormatVersion(JsonUtility.ToJson(oldPreprocess));
            var oldBuildJson = WithoutFormatVersion(JsonUtility.ToJson(oldBuild));
            check(!oldPreprocessJson.Contains("formatVersion") && !oldBuildJson.Contains("formatVersion"),
                "Legacy fixture JSON omits the format marker as historical SDK records did");
            SessionState.SetString(HistoryKey, "{\"Snapshots\":[" + oldPreprocessJson + "," + oldBuildJson + "," +
                JsonUtility.ToJson(currentSdk) + "]}");
            typeof(ModelStatisticsService).GetMethod("RestoreHistory", Any).Invoke(null, null);
            var restored = ModelStatisticsService.History.ToArray();
            check(restored.Length == 3 && restored[0].FormatVersion == 0 && restored[1].FormatVersion == 0 &&
                restored[0].Source == ModelStatisticsSource.SdkPreprocess && restored[1].Source == ModelStatisticsSource.SdkBuild &&
                restored[2].FormatVersion == 1,
                "Restoring old SDK JSON preserves source identifiers and separates missing format markers from current records");
            check(restored.Take(2).All(snapshot => Metric(snapshot, AvatarPerformanceCategory.AABB).Value == "1 × 1 × 1"),
                "Legacy SDK bounds retain their original size-only values without a misleading data conversion");

            EditorLocalization.Language = ProLanguage.English;
            window = ScriptableObject.CreateInstance<ModelStatisticsWindow>();
            window.Show();
            window.CreateGUI();
            window.rootVisualElement.Q<ObjectField>("avatarField").value = avatar;
            window.rootVisualElement.Q<DropdownField>("platformField").index = 0;
            window.rootVisualElement.Q<DropdownField>("stageField").index = 5;
            var legacyRows = VisibleSnapshots(window).ToArray();
            check(legacyRows.Length == 2 && legacyRows.All(snapshot => snapshot.FormatVersion == 0) &&
                legacyRows.Any(snapshot => snapshot.Source == ModelStatisticsSource.SdkPreprocess) &&
                legacyRows.Any(snapshot => snapshot.Source == ModelStatisticsSource.SdkBuild),
                "Legacy stage includes old SDK preprocess and build records despite their unchanged source identifiers");
            check(window.rootVisualElement.Q<HelpBox>("statusLabel").text.Contains("Legacy Snapshot") &&
                window.rootVisualElement.Q("metricsTable").Query<Label>().ToList().Any(label => label.text == "Bounds Size"),
                "Legacy SDK display identifies historical semantics and labels its bounds as size only");
            window.rootVisualElement.Q<DropdownField>("stageField").index = 3;
            var currentRows = VisibleSnapshots(window).ToArray();
            check(currentRows.Length == 1 && currentRows[0].Id == currentSdk.Id && currentRows[0].FormatVersion == 1 &&
                !window.rootVisualElement.Q<HelpBox>("statusLabel").text.Contains("Legacy Snapshot"),
                "Current SDK stage excludes historical format-zero records and retains newly measured SDK data");
        }
        finally
        {
            if (window) UnityEngine.Object.DestroyImmediate(window);
            EditorLocalization.Language = previousLanguage;
            var history = (IList)typeof(ModelStatisticsService).GetField("Snapshots", Any).GetValue(null);
            history.Clear();
            foreach (var snapshot in previousHistory) history.Add(snapshot);
            if (string.IsNullOrEmpty(previousStored)) SessionState.EraseString(HistoryKey);
            else SessionState.SetString(HistoryKey, previousStored);
        }
    }

    private static string WithoutFormatVersion(string json)
    {
        var withoutLeading = Regex.Replace(json, "\"formatVersion\"\\s*:\\s*\\d+\\s*,", string.Empty);
        return Regex.Replace(withoutLeading, ",\\s*\"formatVersion\"\\s*:\\s*\\d+\\s*(?=\\})", string.Empty);
    }

    private static GameObject Cube(Transform parent, string name)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        return cube;
    }

    private static IEnumerable<ModelStatisticsSnapshot> VisibleSnapshots(ModelStatisticsWindow window)
    {
        return (IEnumerable<ModelStatisticsSnapshot>)typeof(ModelStatisticsWindow).GetField("_snapshots", Any).GetValue(window);
    }

    private static bool SameIds(IEnumerable<ModelStatisticsSnapshot> first, IEnumerable<ModelStatisticsSnapshot> second)
    {
        return first.Select(snapshot => snapshot.Id).OrderBy(id => id)
            .SequenceEqual(second.Select(snapshot => snapshot.Id).OrderBy(id => id));
    }

    private static void CheckSdkMetrics(ModelStatisticsSnapshot snapshot, AvatarPerformanceStats sdk,
        Action<bool, string> check)
    {
        var expected = new Dictionary<AvatarPerformanceCategory, object>
        {
            { AvatarPerformanceCategory.DownloadSize, sdk.downloadSizeBytes / 1048576.0 },
            { AvatarPerformanceCategory.UncompressedSize, sdk.uncompressedSizeBytes / 1048576.0 },
            { AvatarPerformanceCategory.PolyCount, sdk.polyCount },
            { AvatarPerformanceCategory.AABB, sdk.aabb?.ToString() },
            { AvatarPerformanceCategory.SkinnedMeshCount, sdk.skinnedMeshCount },
            { AvatarPerformanceCategory.MeshCount, sdk.meshCount },
            { AvatarPerformanceCategory.MaterialCount, sdk.materialCount },
            { AvatarPerformanceCategory.PhysBoneComponentCount, sdk.physBone?.componentCount ?? 0 },
            { AvatarPerformanceCategory.PhysBoneTransformCount, sdk.physBone?.transformCount ?? 0 },
            { AvatarPerformanceCategory.PhysBoneColliderCount, sdk.physBone?.colliderCount ?? 0 },
            { AvatarPerformanceCategory.PhysBoneCollisionCheckCount, sdk.physBone?.collisionCheckCount ?? 0 },
            { AvatarPerformanceCategory.ContactCount, sdk.contactCount },
            { AvatarPerformanceCategory.AnimatorCount, sdk.animatorCount },
            { AvatarPerformanceCategory.BoneCount, sdk.boneCount },
            { AvatarPerformanceCategory.LightCount, sdk.lightCount },
            { AvatarPerformanceCategory.ParticleSystemCount, sdk.particleSystemCount },
            { AvatarPerformanceCategory.ParticleTotalCount, sdk.particleTotalCount },
            { AvatarPerformanceCategory.ParticleMaxMeshPolyCount, sdk.particleMaxMeshPolyCount },
            { AvatarPerformanceCategory.ParticleTrailsEnabled, sdk.particleTrailsEnabled },
            { AvatarPerformanceCategory.ParticleCollisionEnabled, sdk.particleCollisionEnabled },
            { AvatarPerformanceCategory.TrailRendererCount, sdk.trailRendererCount },
            { AvatarPerformanceCategory.LineRendererCount, sdk.lineRendererCount },
            { AvatarPerformanceCategory.ClothCount, sdk.clothCount },
            { AvatarPerformanceCategory.ClothMaxVertices, sdk.clothMaxVertices },
            { AvatarPerformanceCategory.PhysicsColliderCount, sdk.physicsColliderCount },
            { AvatarPerformanceCategory.PhysicsRigidbodyCount, sdk.physicsRigidbodyCount },
            { AvatarPerformanceCategory.AudioSourceCount, sdk.audioSourceCount },
            { AvatarPerformanceCategory.TextureMegabytes, sdk.textureMegabytes },
            { AvatarPerformanceCategory.ConstraintsCount, sdk.constraintsCount },
            { AvatarPerformanceCategory.ConstraintDepth, sdk.constraintDepth },
            { AvatarPerformanceCategory.RaycastCount, sdk.raycastCount }
        };
        foreach (var pair in expected)
        {
            var metric = Metric(snapshot, pair.Key);
            var formatted = pair.Value == null ? null :
                pair.Key == AvatarPerformanceCategory.TextureMegabytes ||
                pair.Key == AvatarPerformanceCategory.DownloadSize ||
                pair.Key == AvatarPerformanceCategory.UncompressedSize
                    ? ((IFormattable)pair.Value).ToString("F2", CultureInfo.CurrentCulture)
                    : Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
            check(metric.IsAvailable == (pair.Value != null) && metric.Value == formatted &&
                metric.Rating == sdk.GetPerformanceRatingForCategory(pair.Key),
                "Statistics match independent SDK value, availability and rating for " +
                (snapshot.IsMobile ? "Mobile " : "PC ") + pair.Key);
        }
    }

    // The Play lifecycle runner calls this after entering Play Mode with its own
    // disposable fixture; Run() above remains an Edit Mode acceptance entry.
    public static void CheckPlayModeCapture(GameObject avatar, Action<bool, string> check)
    {
        check(EditorApplication.isPlaying, "Current Play Mode capture is verified inside a running Play session");
        var desktop = ModelStatisticsService.Capture(avatar, false);
        var mobile = ModelStatisticsService.Capture(avatar, true);
        check(desktop.Source == ModelStatisticsSource.CurrentPlayMode && mobile.Source == ModelStatisticsSource.CurrentPlayMode &&
            !desktop.IsMobile && mobile.IsMobile && desktop.AvatarIdentity == mobile.AvatarIdentity &&
            desktop.BuildStatus == ModelStatisticsBuildStatus.NotApplicable && mobile.BuildStatus == ModelStatisticsBuildStatus.NotApplicable,
            "Manual Play measurements use the current-state stage for both rating platforms");
        var desktopSdk = new AvatarPerformanceStats(false);
        AvatarPerformance.CalculatePerformanceStats(avatar.name, avatar, desktopSdk, false);
        var mobileSdk = new AvatarPerformanceStats(true);
        AvatarPerformance.CalculatePerformanceStats(avatar.name, avatar, mobileSdk, true);
        CheckSdkMetrics(desktop, desktopSdk, check);
        CheckSdkMetrics(mobile, mobileSdk, check);
        var beforeReject = ModelStatisticsService.History[0].Id;
        var rejected = false;
        try { ModelStatisticsService.CaptureBeforeProcessing(avatar, false); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && ModelStatisticsService.History[0].Id == beforeReject,
            "Play Mode cannot publish a misleading new before-processing observation");
    }

    private static void CheckOptionalSdkMetrics(Action<bool, string> check)
    {
        var sdk = new AvatarPerformanceStats(false)
        {
            downloadSizeBytes = null,
            uncompressedSizeBytes = null,
            constraintDepth = null,
            physBone = null
        };
        sdk.CalculateAllPerformanceRatings(false);
        var buildMetrics = typeof(ModelStatisticsService).GetMethod("BuildMetrics", Any);
        var unavailable = ((IEnumerable<ModelStatisticsMetric>)buildMetrics.Invoke(null, new object[] { sdk })).ToArray();
        foreach (var category in new[] { AvatarPerformanceCategory.DownloadSize, AvatarPerformanceCategory.UncompressedSize,
            AvatarPerformanceCategory.ConstraintDepth })
        {
            var metric = unavailable.Single(item => item.Category == category);
            check(!metric.IsAvailable && metric.Value == null && metric.Rating == sdk.GetPerformanceRatingForCategory(category),
                "An absent SDK " + category + " remains unavailable with its original SDK grade");
        }
        check(new[] { AvatarPerformanceCategory.PhysBoneComponentCount, AvatarPerformanceCategory.PhysBoneTransformCount,
            AvatarPerformanceCategory.PhysBoneColliderCount, AvatarPerformanceCategory.PhysBoneCollisionCheckCount }
            .All(category => unavailable.Single(item => item.Category == category).Value == "0" &&
                unavailable.Single(item => item.Category == category).IsAvailable &&
                unavailable.Single(item => item.Category == category).Rating == sdk.GetPerformanceRatingForCategory(category)),
            "Missing SDK PhysBone statistics display APW's zero counts without inventing different grades");

        sdk.downloadSizeBytes = 1310720;
        sdk.uncompressedSizeBytes = 2621440;
        sdk.textureMegabytes = 12.3456f;
        sdk.constraintDepth = 7;
        sdk.aabb = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 6f, 8f));
        sdk.CalculateAllPerformanceRatings(false);
        var available = ((IEnumerable<ModelStatisticsMetric>)buildMetrics.Invoke(null, new object[] { sdk })).ToArray();
        check(available.Single(item => item.Category == AvatarPerformanceCategory.DownloadSize).Value == 1.25.ToString("F2", CultureInfo.CurrentCulture) &&
            available.Single(item => item.Category == AvatarPerformanceCategory.UncompressedSize).Value == 2.5.ToString("F2", CultureInfo.CurrentCulture) &&
            available.Single(item => item.Category == AvatarPerformanceCategory.TextureMegabytes).Value == 12.35.ToString("F2", CultureInfo.CurrentCulture),
            "SDK byte sizes and texture memory are represented in megabytes to two decimal places");
        check(available.Single(item => item.Category == AvatarPerformanceCategory.ConstraintDepth).IsAvailable &&
            available.Single(item => item.Category == AvatarPerformanceCategory.ConstraintDepth).Value == "7" &&
            available.Single(item => item.Category == AvatarPerformanceCategory.ConstraintDepth).Rating ==
                sdk.GetPerformanceRatingForCategory(AvatarPerformanceCategory.ConstraintDepth),
            "A known SDK constraint depth is displayed with its original SDK grade");
        check(available.Single(item => item.Category == AvatarPerformanceCategory.AABB).Value == sdk.aabb.Value.ToString(),
            "Bounds retain the SDK's full center and extents rather than only the size");
    }

    private static ModelStatisticsMetric Metric(ModelStatisticsSnapshot snapshot, AvatarPerformanceCategory category)
    {
        return snapshot.Metrics.Single(metric => metric.Category == category);
    }

    private static long Number(ModelStatisticsSnapshot snapshot, AvatarPerformanceCategory category)
    {
        return long.Parse(Metric(snapshot, category).Value, CultureInfo.InvariantCulture);
    }

    private static bool ThrowsArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return true; }
        return false;
    }
}
