using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

namespace VRCLearn.GestureManagerPro.Diagnostics
{
    /// <summary>Read-only SDK statistics, with a bounded Editor-session history.</summary>
    [InitializeOnLoad]
    public static class ModelStatisticsService
    {
        private const string HistoryKey = "VRCLearn.GestureManagerPro.ModelStatistics.History.v1";
        private const int HistoryLimit = 12;
        private static readonly List<ModelStatisticsSnapshot> Snapshots = new List<ModelStatisticsSnapshot>();
        private static IVRCSdkAvatarBuilderApi builder;
        private static string buildAvatarIdentity;
        private static string buildAvatarName;
        private static string pendingBuildSnapshotId;
        private static bool buildActive;
        private static bool buildMobile;

        [Serializable]
        private sealed class StoredHistory
        {
            public List<ModelStatisticsSnapshot> Snapshots = new List<ModelStatisticsSnapshot>();
        }

        public static event Action Changed;
        public static IReadOnlyList<ModelStatisticsSnapshot> History => Snapshots.AsReadOnly();

        static ModelStatisticsService()
        {
            RestoreHistory();
            VRCSdkControlPanel.OnSdkPanelEnable += OnSdkPanelEnabled;
            VRCSdkControlPanel.OnSdkPanelDisable += OnSdkPanelDisabled;
            EditorApplication.delayCall += ConnectBuilder;
            AssemblyReloadEvents.beforeAssemblyReload += DisconnectBuilder;
        }

        private static void RestoreHistory()
        {
            Snapshots.Clear();
            var stored = SessionState.GetString(HistoryKey, string.Empty);
            if (!string.IsNullOrEmpty(stored))
            {
                try
                {
                    var history = JsonUtility.FromJson<StoredHistory>(stored);
                    if (history?.Snapshots != null) Snapshots.AddRange(history.Snapshots);
                    if (Snapshots.Count > HistoryLimit) Snapshots.RemoveRange(HistoryLimit, Snapshots.Count - HistoryLimit);
                    var recoveredPending = false;
                    for (var index = 0; index < Snapshots.Count; index++)
                    {
                        if (Snapshots[index].BuildStatus != ModelStatisticsBuildStatus.Pending) continue;
                        Snapshots[index] = Snapshots[index].MarkBuildUnconfirmed();
                        recoveredPending = true;
                    }
                    // A domain reload loses SDK event correlation. Keep the observed preprocessing data,
                    // but do not leave an old record claiming that completion can still be observed.
                    if (recoveredPending)
                        SessionState.SetString(HistoryKey, JsonUtility.ToJson(new StoredHistory { Snapshots = Snapshots }));
                }
                catch (ArgumentException)
                {
                    SessionState.EraseString(HistoryKey);
                }
            }
        }

        /// <summary>Reads the avatar as it exists now. It does not invoke a build or preprocessing.</summary>
        public static ModelStatisticsSnapshot Capture(GameObject avatar, bool mobile)
        {
            return CaptureAndStore(avatar, mobile, ModelStatisticsSource.CurrentAvatar,
                ModelStatisticsBuildStatus.NotApplicable, null, null,
                EditorApplication.isPlaying
                    ? "Current Play Mode state; simulator-generated temporary components are excluded. This is not an upload result."
                    : "Current avatar before SDK build preprocessing. This is not an upload result.");
        }

        /// <summary>Call before the simulator modifies the Play Mode avatar.</summary>
        public static ModelStatisticsSnapshot CapturePreview(GameObject avatar, bool mobile)
        {
            return CaptureAndStore(avatar, mobile, ModelStatisticsSource.PlayModePreview,
                ModelStatisticsBuildStatus.NotApplicable, null, null,
                "Play Mode preview before Gesture Manager simulation. Only processors already run at capture time are included; this is not an upload result.");
        }

        public static void ClearHistory()
        {
            Snapshots.Clear();
            pendingBuildSnapshotId = null;
            PersistAndNotify();
        }

        /// <summary>Called by the last-order SDK preprocess observer; failures never block the SDK pipeline.</summary>
        internal static void ObservePreprocessedAvatar(GameObject avatar)
        {
            if (!avatar || !avatar.GetComponent<VRCAvatarDescriptor>()) return;
            try
            {
                var belongsToBuild = buildActive &&
                    (avatar.name == buildAvatarName || avatar.name == buildAvatarName + "(Clone)");
                var source = belongsToBuild ? ModelStatisticsSource.SdkPreprocess
                    : EditorApplication.isPlayingOrWillChangePlaymode ? ModelStatisticsSource.PlayModePreview
                    : ModelStatisticsSource.SdkPreprocess;
                var snapshot = CaptureAndStore(avatar, belongsToBuild ? buildMobile : IsCurrentPlatformMobile(), source,
                    belongsToBuild ? ModelStatisticsBuildStatus.Pending : ModelStatisticsBuildStatus.NotApplicable,
                    belongsToBuild ? buildAvatarIdentity : null, belongsToBuild ? buildAvatarName : null,
                    "SDK preprocessing snapshot at the last callback priority. Later SDK processing and bundle generation are not included; build success is recorded only when the SDK reports it.");
                if (belongsToBuild) pendingBuildSnapshotId = snapshot.Id;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Gesture Manager Pro could not capture model statistics: " + exception.Message);
            }
        }

        public static bool IsCurrentPlatformMobile()
        {
            return EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ||
                EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;
        }

        private static ModelStatisticsSnapshot CaptureAndStore(GameObject avatar, bool mobile,
            ModelStatisticsSource source, ModelStatisticsBuildStatus buildStatus,
            string identityOverride, string nameOverride, string notes)
        {
            if (!avatar || !avatar.GetComponent<VRCAvatarDescriptor>())
                throw new ArgumentException("Select an avatar with a VRChat Avatar Descriptor.", nameof(avatar));

            var stats = new AvatarPerformanceStats(mobile);
            var originalIgnore = AvatarPerformance.ShouldIgnoreComponent;
            var excludeSimulatorComponents = GestureManager.ControlledAvatars.ContainsKey(avatar);
            try
            {
                if (excludeSimulatorComponents)
                {
                    AvatarPerformance.ShouldIgnoreComponent = component =>
                        component && component.hideFlags == HideFlags.HideAndDontSave ||
                        originalIgnore != null && originalIgnore(component);
                }
                AvatarPerformance.CalculatePerformanceStats(avatar.name, avatar, stats, mobile);
                if (excludeSimulatorComponents && stats.contactCount.HasValue && stats.contactCompleteCount.HasValue)
                {
                    // SDK 3.10.x's Contact scanner enumerates ContactBase directly and does not apply
                    // the SDK ignore delegate. Mirror its localOnly counting, applying our filter here.
                    var completeCount = 0;
                    var nonLocalCount = 0;
                    foreach (var contact in avatar.GetComponentsInChildren<ContactBase>(true))
                    {
                        if (!contact || AvatarPerformance.ShouldIgnoreComponent(contact)) continue;
                        completeCount++;
                        if (!contact.localOnly) nonLocalCount++;
                    }
                    stats.contactCount = nonLocalCount;
                    stats.contactCompleteCount = completeCount;
                    stats.CalculateAllPerformanceRatings(mobile);
                }
            }
            finally
            {
                AvatarPerformance.ShouldIgnoreComponent = originalIgnore;
            }

            var snapshot = new ModelStatisticsSnapshot(nameOverride ?? avatar.name,
                identityOverride ?? GetAvatarIdentity(avatar), mobile, source, buildStatus,
                stats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.Overall), BuildMetrics(stats), notes);
            Snapshots.Insert(0, snapshot);
            if (Snapshots.Count > HistoryLimit) Snapshots.RemoveAt(Snapshots.Count - 1);
            PersistAndNotify();
            return snapshot;
        }

        public static string GetAvatarIdentity(GameObject avatar)
        {
            if (!avatar) throw new ArgumentException("Select a model to identify.", nameof(avatar));
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(avatar);
            if (globalId.identifierType != 0) return globalId.ToString();
            // Unsaved scenes and transient build clones have no durable global object id.
            return "session:" + avatar.GetInstanceID().ToString(CultureInfo.InvariantCulture);
        }

        private static List<ModelStatisticsMetric> BuildMetrics(AvatarPerformanceStats stats)
        {
            var metrics = new List<ModelStatisticsMetric>();
            Add(metrics, stats, AvatarPerformanceCategory.DownloadSize, null);
            Add(metrics, stats, AvatarPerformanceCategory.UncompressedSize, null);
            Add(metrics, stats, AvatarPerformanceCategory.PolyCount, stats.polyCount);
            Add(metrics, stats, AvatarPerformanceCategory.AABB, stats.aabb.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.###} × {1:0.###} × {2:0.###}",
                    stats.aabb.Value.size.x, stats.aabb.Value.size.y, stats.aabb.Value.size.z) : null);
            Add(metrics, stats, AvatarPerformanceCategory.SkinnedMeshCount, stats.skinnedMeshCount);
            Add(metrics, stats, AvatarPerformanceCategory.MeshCount, stats.meshCount);
            Add(metrics, stats, AvatarPerformanceCategory.MaterialCount, stats.materialCount);
            Add(metrics, stats, AvatarPerformanceCategory.PhysBoneComponentCount, stats.physBone?.componentCount);
            Add(metrics, stats, AvatarPerformanceCategory.PhysBoneTransformCount, stats.physBone?.transformCount);
            Add(metrics, stats, AvatarPerformanceCategory.PhysBoneColliderCount, stats.physBone?.colliderCount);
            Add(metrics, stats, AvatarPerformanceCategory.PhysBoneCollisionCheckCount, stats.physBone?.collisionCheckCount);
            Add(metrics, stats, AvatarPerformanceCategory.ContactCount, stats.contactCount);
            Add(metrics, stats, AvatarPerformanceCategory.AnimatorCount, stats.animatorCount);
            Add(metrics, stats, AvatarPerformanceCategory.BoneCount, stats.boneCount);
            Add(metrics, stats, AvatarPerformanceCategory.LightCount, stats.lightCount);
            Add(metrics, stats, AvatarPerformanceCategory.ParticleSystemCount, stats.particleSystemCount);
            Add(metrics, stats, AvatarPerformanceCategory.ParticleTotalCount, stats.particleTotalCount);
            Add(metrics, stats, AvatarPerformanceCategory.ParticleMaxMeshPolyCount, stats.particleMaxMeshPolyCount);
            Add(metrics, stats, AvatarPerformanceCategory.ParticleTrailsEnabled, stats.particleTrailsEnabled);
            Add(metrics, stats, AvatarPerformanceCategory.ParticleCollisionEnabled, stats.particleCollisionEnabled);
            Add(metrics, stats, AvatarPerformanceCategory.TrailRendererCount, stats.trailRendererCount);
            Add(metrics, stats, AvatarPerformanceCategory.LineRendererCount, stats.lineRendererCount);
            Add(metrics, stats, AvatarPerformanceCategory.ClothCount, stats.clothCount);
            Add(metrics, stats, AvatarPerformanceCategory.ClothMaxVertices, stats.clothMaxVertices);
            Add(metrics, stats, AvatarPerformanceCategory.PhysicsColliderCount, stats.physicsColliderCount);
            Add(metrics, stats, AvatarPerformanceCategory.PhysicsRigidbodyCount, stats.physicsRigidbodyCount);
            Add(metrics, stats, AvatarPerformanceCategory.AudioSourceCount, stats.audioSourceCount);
            Add(metrics, stats, AvatarPerformanceCategory.TextureMegabytes, stats.textureMegabytes.HasValue
                ? stats.textureMegabytes.Value.ToString("0.##", CultureInfo.InvariantCulture) : null);
            Add(metrics, stats, AvatarPerformanceCategory.ConstraintsCount, stats.constraintsCount);
            // SDK validation refreshes internal execution groups before computing this value. That API is
            // not public to tools, so a raw scan cannot promise an accurate constraint depth.
            Add(metrics, stats, AvatarPerformanceCategory.ConstraintDepth, null);
            Add(metrics, stats, AvatarPerformanceCategory.RaycastCount, stats.raycastCount);
            return metrics;
        }

        private static void Add(List<ModelStatisticsMetric> metrics, AvatarPerformanceStats stats,
            AvatarPerformanceCategory category, object value)
        {
            var formatted = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            metrics.Add(new ModelStatisticsMetric(category, formatted,
                value == null ? PerformanceRating.None : stats.GetPerformanceRatingForCategory(category), value != null));
        }

        private static void PersistAndNotify()
        {
            SessionState.SetString(HistoryKey, JsonUtility.ToJson(new StoredHistory { Snapshots = Snapshots }));
            if (Changed == null) return;
            foreach (Action listener in Changed.GetInvocationList())
            {
                try { listener(); }
                catch (Exception exception)
                {
                    Debug.LogWarning("Gesture Manager Pro could not refresh the model statistics view: " + exception.Message);
                }
            }
        }

        private static void OnSdkPanelEnabled(object sender, EventArgs args)
        {
            ConnectBuilder();
            EditorApplication.delayCall += ConnectBuilder;
        }

        private static void OnSdkPanelDisabled(object sender, EventArgs args)
        {
            DisconnectBuilder();
        }

        private static void ConnectBuilder()
        {
            if (!VRCSdkControlPanel.window) return;
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var next)) return;
            if (ReferenceEquals(builder, next)) return;
            DisconnectBuilder();
            builder = next;
            builder.OnSdkBuildStart += OnBuildStart;
            builder.OnSdkBuildSuccess += OnBuildSuccess;
            builder.OnSdkBuildError += OnBuildError;
            builder.OnSdkBuildFinish += OnBuildFinish;
        }

        private static void DisconnectBuilder()
        {
            if (builder != null)
            {
                builder.OnSdkBuildStart -= OnBuildStart;
                builder.OnSdkBuildSuccess -= OnBuildSuccess;
                builder.OnSdkBuildError -= OnBuildError;
                builder.OnSdkBuildFinish -= OnBuildFinish;
            }
            if (buildActive)
            {
                for (var index = 0; index < Snapshots.Count; index++)
                {
                    if (Snapshots[index].Id != pendingBuildSnapshotId) continue;
                    Snapshots[index] = Snapshots[index].MarkBuildUnconfirmed();
                    PersistAndNotify();
                    break;
                }
            }
            builder = null;
            buildActive = false;
            buildAvatarIdentity = null;
            buildAvatarName = null;
            pendingBuildSnapshotId = null;
            buildMobile = false;
        }

        private static void OnBuildStart(object sender, object target)
        {
            var avatar = target as GameObject;
            buildActive = avatar && avatar.GetComponent<VRCAvatarDescriptor>();
            buildAvatarIdentity = buildActive ? GetAvatarIdentity(avatar) : null;
            buildAvatarName = buildActive ? avatar.name : null;
            buildMobile = IsCurrentPlatformMobile();
            pendingBuildSnapshotId = null;
        }

        private static void OnBuildSuccess(object sender, string bundlePath)
        {
            long bytes = -1;
            try
            {
                if (!string.IsNullOrEmpty(bundlePath) && File.Exists(bundlePath)) bytes = new FileInfo(bundlePath).Length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            CompletePendingBuild(true, bytes);
        }

        private static void OnBuildError(object sender, string message)
        {
            CompletePendingBuild(false, -1);
        }

        private static void OnBuildFinish(object sender, string message)
        {
            if (buildActive) CompletePendingBuild(false, -1);
        }

        private static void CompletePendingBuild(bool succeeded, long bytes)
        {
            if (!buildActive) return;
            buildActive = false;
            for (var index = 0; index < Snapshots.Count; index++)
            {
                if (Snapshots[index].Id != pendingBuildSnapshotId) continue;
                Snapshots[index] = Snapshots[index].CompleteBuild(succeeded, bytes);
                PersistAndNotify();
                break;
            }
            pendingBuildSnapshotId = null;
        }
    }
}
