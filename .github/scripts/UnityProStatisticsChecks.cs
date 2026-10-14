using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
            check(desktop.Source == ModelStatisticsSource.CurrentAvatar &&
                desktop.BuildStatus == ModelStatisticsBuildStatus.NotApplicable && !desktop.IsMobile,
                "Edit Mode measurement is labeled current PC state rather than a completed SDK build");
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
            check(desktop.Metrics.Select(metric => metric.Category).Distinct().Count() == desktop.Metrics.Count,
                "Statistics expose one row per SDK metric category");
            check(!Metric(desktop, AvatarPerformanceCategory.DownloadSize).IsAvailable &&
                !Metric(desktop, AvatarPerformanceCategory.UncompressedSize).IsAvailable &&
                !Metric(desktop, AvatarPerformanceCategory.ConstraintDepth).IsAvailable && desktop.BundleBytes == -1,
                "Unbuilt sizes and unsupported constraint depth remain unavailable instead of reporting a misleading zero");
            check(Metric(desktop, AvatarPerformanceCategory.DownloadSize).Rating == PerformanceRating.None,
                "An unavailable package size is not assigned a performance grade");
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

            var preview = ModelStatisticsService.CapturePreview(avatar, false);
            check(preview.Source == ModelStatisticsSource.PlayModePreview &&
                preview.BuildStatus == ModelStatisticsBuildStatus.NotApplicable && preview.BundleBytes == -1,
                "Play Mode preview is identified separately from successful upload/build results");
            check(!string.IsNullOrWhiteSpace(preview.Notes), "Preview records the limits of its capture stage");
            avatar.name = "Statistics renamed source";
            check(desktop.AvatarName == "Tools" && Number(desktop, AvatarPerformanceCategory.PolyCount) == 24,
                "Historical snapshots remain independent of later source avatar changes");
            avatar.name = "Tools";

            var observer = new ModelStatisticsPreprocessCallback();
            check(observer.callbackOrder == int.MaxValue,
                "SDK statistics observer runs at the final preprocess callback priority");
            var beforeObserver = ModelStatisticsService.History.Count;
            check(observer.OnPreprocessAvatar(avatar) && ModelStatisticsService.History.Count == beforeObserver + 1,
                "SDK preprocessing observer captures an avatar without stopping the pipeline");
            check(ModelStatisticsService.History[0].Source == ModelStatisticsSource.SdkPreprocess &&
                ModelStatisticsService.History[0].BuildStatus == ModelStatisticsBuildStatus.NotApplicable,
                "A preprocess callback alone does not claim successful bundle generation");
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
            for (var index = 0; index < 14; index++) ModelStatisticsService.Capture(avatar, index % 2 == 0);
            check(ModelStatisticsService.History.Count <= 12 && ModelStatisticsService.History.Count > 1 &&
                !ModelStatisticsService.History.Any(snapshot => snapshot.Id == oldestId),
                "Session history evicts oldest measurements instead of growing without bound");
            var stored = JsonUtility.FromJson<StoredHistory>(SessionState.GetString(HistoryKey, string.Empty));
            check(stored != null && stored.Snapshots.Select(snapshot => snapshot.Id)
                    .SequenceEqual(ModelStatisticsService.History.Select(snapshot => snapshot.Id)),
                "Session serialization retains the bounded newest-first snapshot list");
            check(stored.Snapshots[0].AvatarIdentity == ModelStatisticsService.History[0].AvatarIdentity &&
                Number(stored.Snapshots[0], AvatarPerformanceCategory.PolyCount) == 24,
                "Session snapshots round-trip their source identity and SDK metric values");

            CheckWindow(avatar, duplicateAvatar, check);
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
            check(Number(controlled, AvatarPerformanceCategory.ContactCount) == 1 &&
                Number(controlled, AvatarPerformanceCategory.PolyCount) == 24,
                "Active simulator measurement excludes its temporary contact but preserves user geometry and hidden contact");
            AvatarPerformance.ShouldIgnoreComponent = component => component == userSender;
            var priorIgnore = AvatarPerformance.ShouldIgnoreComponent;
            var filtered = ModelStatisticsService.Capture(avatar, false);
            check(Number(filtered, AvatarPerformanceCategory.ContactCount) == 0,
                "Simulator filtering composes with an existing SDK component filter");
            check(ReferenceEquals(AvatarPerformance.ShouldIgnoreComponent, priorIgnore),
                "Statistics restore the exact existing SDK filter after calculation");
            check(helper && helper.hideFlags == HideFlags.HideAndDontSave && userSender &&
                userSender.hideFlags == (HideFlags.HideInHierarchy | HideFlags.HideInInspector),
                "Filtering never removes or changes simulator or user-authored source components");
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
            var pending = ModelStatisticsService.History[0];
            check(pending.Source == ModelStatisticsSource.SdkPreprocess &&
                pending.BuildStatus == ModelStatisticsBuildStatus.Pending && pending.BundleBytes == -1,
                "An active SDK build begins with a pending preprocess snapshot and unavailable bundle size");
            service.GetMethod("OnBuildError", Any).Invoke(null, new object[] { null, "Injected validation build error" });
            var failed = ModelStatisticsService.History[0];
            check(failed.Id == pending.Id && failed.Source == ModelStatisticsSource.SdkPreprocess &&
                failed.BuildStatus == ModelStatisticsBuildStatus.Failed && failed.BundleBytes == -1,
                "SDK build error updates only the pending snapshot and never reports a successful upload result");
            check(pending.BuildStatus == ModelStatisticsBuildStatus.Pending,
                "Updating build completion replaces the history entry without mutating a previously returned snapshot");
            service.GetMethod("OnBuildFinish", Any).Invoke(null, new object[] { null, "Injected validation finish" });
            check(ReferenceEquals(ModelStatisticsService.History[0], failed),
                "A later SDK finish notification does not overwrite the recorded build failure");

            File.WriteAllBytes(bundle, new byte[23]);
            service.GetMethod("OnBuildStart", Any).Invoke(null, new object[] { null, avatar });
            observer.OnPreprocessAvatar(avatar);
            var pendingSuccess = ModelStatisticsService.History[0];
            service.GetMethod("OnBuildSuccess", Any).Invoke(null, new object[] { null, bundle });
            var succeeded = ModelStatisticsService.History[0];
            check(succeeded.Id == pendingSuccess.Id && succeeded.Source == ModelStatisticsSource.SdkBuild &&
                succeeded.BuildStatus == ModelStatisticsBuildStatus.Succeeded && succeeded.BundleBytes == 23,
                "SDK success notification associates the built file size with its own avatar snapshot");
            check(!Metric(succeeded, AvatarPerformanceCategory.DownloadSize).IsAvailable &&
                !Metric(succeeded, AvatarPerformanceCategory.UncompressedSize).IsAvailable,
                "A local bundle file size is kept separate from unavailable official download/uncompressed metrics");
            service.GetMethod("OnBuildFinish", Any).Invoke(null, new object[] { null, "Injected validation finish" });
            check(ReferenceEquals(ModelStatisticsService.History[0], succeeded),
                "A later SDK finish notification preserves a successful build result");

            service.GetMethod("OnBuildStart", Any).Invoke(null, new object[] { null, avatar });
            observer.OnPreprocessAvatar(avatar);
            var pendingDisconnect = ModelStatisticsService.History[0];
            service.GetMethod("DisconnectBuilder", Any).Invoke(null, null);
            var disconnected = ModelStatisticsService.History[0];
            check(disconnected.Id == pendingDisconnect.Id &&
                disconnected.BuildStatus == ModelStatisticsBuildStatus.Unconfirmed &&
                disconnected.Source == ModelStatisticsSource.SdkPreprocess && disconnected.BundleBytes == -1,
                "Disconnecting SDK observation records unconfirmed completion without reporting failure or a bundle");
            check(pendingDisconnect.BuildStatus == ModelStatisticsBuildStatus.Pending,
                "SDK disconnect preserves the previously returned pending snapshot object");
            service.GetMethod("OnBuildSuccess", Any).Invoke(null, new object[] { null, bundle });
            service.GetMethod("OnBuildFinish", Any).Invoke(null, new object[] { null, "Injected disconnected finish" });
            check(ReferenceEquals(ModelStatisticsService.History[0], disconnected),
                "SDK notifications after disconnect cannot complete the abandoned pending build");

            service.GetMethod("OnBuildStart", Any).Invoke(null, new object[] { null, avatar });
            observer.OnPreprocessAvatar(avatar);
            var pendingReload = ModelStatisticsService.History[0];
            var storedPending = SessionState.GetString(HistoryKey, string.Empty);
            service.GetMethod("OnBuildError", Any).Invoke(null, new object[] { null, "Reset injected build correlation" });
            SessionState.SetString(HistoryKey, storedPending);
            service.GetMethod("RestoreHistory", Any).Invoke(null, null);
            var recovered = ModelStatisticsService.History[0];
            check(recovered.Id == pendingReload.Id && recovered.BuildStatus == ModelStatisticsBuildStatus.Unconfirmed &&
                recovered.Source == ModelStatisticsSource.SdkPreprocess && recovered.BundleBytes == -1 &&
                Number(recovered, AvatarPerformanceCategory.PolyCount) == 24,
                "Restoring a session after domain reload retains metrics and marks pending completion unconfirmed");
            var restoredStored = JsonUtility.FromJson<StoredHistory>(SessionState.GetString(HistoryKey, string.Empty));
            check(restoredStored.Snapshots[0].Id == pendingReload.Id &&
                restoredStored.Snapshots[0].BuildStatus == ModelStatisticsBuildStatus.Unconfirmed,
                "Recovered unconfirmed status is written back to SessionState instead of repeatedly restoring pending");
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
                window.rootVisualElement.Q<DropdownField>("snapshotField") != null &&
                window.rootVisualElement.Q<Button>("refreshButton") != null &&
                window.rootVisualElement.Q<Button>("useSelectedButton") != null &&
                window.rootVisualElement.Q("statusLabel") != null &&
                window.rootVisualElement.Q("metricsTable") != null,
                "Statistics window exposes avatar, platform, history, refresh and metric controls");
            window.rootVisualElement.Q<ObjectField>("avatarField").value = avatar;
            var platform = window.rootVisualElement.Q<DropdownField>("platformField");
            platform.index = 0;
            typeof(ModelStatisticsWindow).GetMethod("CaptureSelected", Any).Invoke(window, null);
            check(ModelStatisticsService.History[0].AvatarName == "Tools" &&
                ModelStatisticsService.History[0].Source == ModelStatisticsSource.CurrentAvatar,
                "Window refresh measures the selected avatar without running a build");
            var visibleSnapshots = (IEnumerable<ModelStatisticsSnapshot>)typeof(ModelStatisticsWindow)
                .GetField("_snapshots", Any).GetValue(window);
            check(ModelStatisticsService.History.Any(snapshot => snapshot.Id == otherSnapshot.Id) &&
                visibleSnapshots.Any() && visibleSnapshots.All(snapshot => snapshot.AvatarIdentity != otherSnapshot.AvatarIdentity),
                "Window history excludes snapshots from a different unsaved avatar with the same name");
            var selectionId = ModelStatisticsService.History[0].Id;
            check(window.rootVisualElement.Q("metricsTable").childCount ==
                ModelStatisticsService.History[0].Metrics.Count + 1 &&
                window.rootVisualElement.Q<HelpBox>("statusLabel").text.Contains("Current Avatar Snapshot"),
                "Window renders every SDK metric with an explicit current-state source label");
            check(window.titleContent.text == "Model Statistics", "Statistics window has its English title");
            EditorLocalization.Language = ProLanguage.SimplifiedChinese;
            check(window.titleContent.text == "模型指标" &&
                window.rootVisualElement.Q<Button>("refreshButton").text == EditorLocalization.Tr("Measure Current Avatar"),
                "Open statistics window updates its Chinese title and action captions live");
            check(window.rootVisualElement.Q<ObjectField>("avatarField").value == avatar && avatar.name == "Tools" &&
                ModelStatisticsService.History[0].Id == selectionId &&
                (string)typeof(ModelStatisticsWindow).GetField("_snapshotId", Any).GetValue(window) == selectionId,
                "Language switching retains the chosen avatar and snapshot without changing identifiers or recapturing");
            var styleCount = window.rootVisualElement.styleSheets.count;
            window.CreateGUI();
            EditorLocalization.Language = ProLanguage.English;
            check(window.rootVisualElement.styleSheets.count == styleCount &&
                defaultStyles.All(window.rootVisualElement.styleSheets.Contains) &&
                window.rootVisualElement.styleSheets.Contains(externalStyle),
                "Statistics rebuilds and language changes preserve Unity styling without duplicate stylesheets");
            window.rootVisualElement.Q<DropdownField>("platformField").index = 1;
            typeof(ModelStatisticsWindow).GetMethod("CaptureSelected", Any).Invoke(window, null);
            check(ModelStatisticsService.History[0].IsMobile &&
                (string)typeof(ModelStatisticsWindow).GetField("_snapshotId", Any).GetValue(window) ==
                ModelStatisticsService.History[0].Id,
                "Window platform selector captures and selects Mobile SDK statistics");
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
            var menu = typeof(ModelStatisticsWindow).GetMethod("Open", Any).GetCustomAttributes(typeof(MenuItem), false)
                .Cast<MenuItem>().ToArray();
            check(menu.Any(item => item.menuItem == "Tools/Model Statistics" && !item.validate),
                "Statistics window registers directly under Tools/Model Statistics");
        }
        finally
        {
            if (window) UnityEngine.Object.DestroyImmediate(window);
            if (externalStyle) UnityEngine.Object.DestroyImmediate(externalStyle);
            EditorLocalization.Language = language;
        }
    }

    private static GameObject Cube(Transform parent, string name)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        return cube;
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
