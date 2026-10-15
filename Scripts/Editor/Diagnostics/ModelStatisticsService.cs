using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

namespace VRCLearn.GestureManagerPro.Diagnostics
{
    /// <summary>SDK statistics with Actual Performance Window semantics and separate processing stages.</summary>
    [InitializeOnLoad]
    public static class ModelStatisticsService
    {
        private const string HistoryKey = "VRCLearn.GestureManagerPro.ModelStatistics.History.v1";
        private const int HistoryLimit = 48;
        private const string CurrentEditNotes = "Current Edit Mode state. No build or optimizer was invoked by this capture.";
        private const string BeforePlayNotes = "Captured while leaving Edit Mode, before this package's Play Mode sampling. Changes made by earlier transition callbacks may already be present.";
        private const string BeforeBuildNotes = "Captured from the original avatar at SDK build start, before SDK preprocessing.";
        private const string CurrentPlayNotes = "Current Play Mode state. This snapshot may include changes made by simulators and other runtime scripts.";
        private const string ProcessedPlayNotes = "Captured on the first Editor update in Play Mode, using the same SDK calculation as Actual Performance Window. Only processing completed before capture is included.";
        private const string ProcessedSdkNotes = "SDK preprocessing snapshot at the last callback priority. Later SDK processing and bundle generation are not included; build success is recorded only when the SDK reports it.";
        private static readonly List<ModelStatisticsSnapshot> Snapshots = new List<ModelStatisticsSnapshot>();
        private static readonly List<string> PendingBuildSnapshotIds = new List<string>();
        private static IVRCSdkAvatarBuilderApi builder;
        private static string buildAvatarIdentity;
        private static string buildAvatarName;
        private static bool buildActive;
        private static bool buildMobile;
        private static bool playModeCaptured;
        private static bool leavingPlayMode;

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
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += DisconnectBuilder;
        }

        private static void RestoreHistory()
        {
            Snapshots.Clear();
            var stored = SessionState.GetString(HistoryKey, string.Empty);
            if (string.IsNullOrEmpty(stored)) return;
            try
            {
                var history = JsonUtility.FromJson<StoredHistory>(stored);
                if (history?.Snapshots != null) Snapshots.AddRange(history.Snapshots);
                Snapshots.RemoveAll(snapshot => snapshot == null);
                if (Snapshots.Count > HistoryLimit) Snapshots.RemoveRange(HistoryLimit, Snapshots.Count - HistoryLimit);
                var recoveredPending = false;
                for (var index = 0; index < Snapshots.Count; index++)
                {
                    if (Snapshots[index].BuildStatus != ModelStatisticsBuildStatus.Pending) continue;
                    Snapshots[index] = Snapshots[index].MarkBuildUnconfirmed();
                    recoveredPending = true;
                }
                // A domain reload loses SDK event correlation, while the immutable observations remain useful.
                if (recoveredPending) PersistHistory();
            }
            catch (ArgumentException)
            {
                SessionState.EraseString(HistoryKey);
            }
        }

        /// <summary>Reads the current avatar without running a build or any preprocessing.</summary>
        public static ModelStatisticsSnapshot Capture(GameObject avatar, bool mobile)
        {
            return EditorApplication.isPlaying
                ? CaptureAndStore(avatar, mobile, ModelStatisticsSource.CurrentPlayMode,
                    ModelStatisticsBuildStatus.NotApplicable, null, null, CurrentPlayNotes)
                : CaptureBeforeProcessing(avatar, mobile);
        }

        public static ModelStatisticsSnapshot CaptureBeforeProcessing(GameObject avatar, bool mobile)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Before-processing statistics can only be captured outside Play Mode.");
            return CaptureAndStore(avatar, mobile, ModelStatisticsSource.BeforeProcessing,
                ModelStatisticsBuildStatus.NotApplicable, null, null, CurrentEditNotes);
        }

        public static void ClearHistory()
        {
            Snapshots.Clear();
            PendingBuildSnapshotIds.Clear();
            PersistAndNotify();
        }

        /// <summary>Observes the last SDK preprocessing priority; never starts or repeats a processor.</summary>
        internal static void ObservePreprocessedAvatar(GameObject avatar)
        {
            if (!avatar || !avatar.GetComponent<VRCAvatarDescriptor>()) return;
            var belongsToBuild = buildActive &&
                (avatar.name == buildAvatarName || avatar.name == buildAvatarName + "(Clone)");
            CapturePairSafely(avatar, ModelStatisticsSource.SdkPreprocess,
                belongsToBuild ? ModelStatisticsBuildStatus.Pending : ModelStatisticsBuildStatus.NotApplicable,
                belongsToBuild ? buildAvatarIdentity : null, belongsToBuild ? buildAvatarName : null,
                ProcessedSdkNotes, belongsToBuild);
        }

        public static bool IsCurrentPlatformMobile()
        {
            return EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ||
                EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                playModeCaptured = false;
                leavingPlayMode = false;
                // Unity has no priority for this event: earlier tools may already have changed the model.
                CaptureSceneAvatars(ModelStatisticsSource.BeforeProcessing, BeforePlayNotes);
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                leavingPlayMode = true;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                playModeCaptured = false;
                leavingPlayMode = false;
            }
        }

        private static void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying)
            {
                playModeCaptured = false;
                leavingPlayMode = false;
                return;
            }
            if (playModeCaptured || leavingPlayMode) return;
            playModeCaptured = true;
            if (buildActive) return;
            CaptureSceneAvatars(ModelStatisticsSource.PlayModeProcessed, ProcessedPlayNotes);
        }

        private static void CaptureSceneAvatars(ModelStatisticsSource source, string notes)
        {
            try
            {
                // Match APW's loaded-scene traversal and active-avatar selection. Manual capture can
                // inspect inactive avatars. Runtime clones retain their own identity without name guessing.
                for (var index = 0; index < SceneManager.sceneCount; index++)
                {
                    var scene = SceneManager.GetSceneAt(index);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var descriptor in root.GetComponentsInChildren<VRCAvatarDescriptor>())
                            if (descriptor) CapturePairSafely(descriptor.gameObject, source,
                                ModelStatisticsBuildStatus.NotApplicable, null, null, notes, false);
                }
            }
            catch (Exception exception)
            {
                ReportCaptureFailure(exception);
            }
        }

        private static void CapturePairSafely(GameObject avatar, ModelStatisticsSource source,
            ModelStatisticsBuildStatus status, string identity, string name, string notes, bool associatedBuild)
        {
            for (var platform = 0; platform < 2; platform++)
            {
                try
                {
                    var snapshot = CaptureAndStore(avatar, platform != 0, source, status, identity, name, notes,
                        associatedBuild, associatedBuild && buildMobile);
                    if (associatedBuild && status == ModelStatisticsBuildStatus.Pending)
                        PendingBuildSnapshotIds.Add(snapshot.Id);
                }
                catch (Exception exception)
                {
                    ReportCaptureFailure(exception);
                }
            }
        }

        private static void ReportCaptureFailure(Exception exception)
        {
            Debug.LogWarning("Gesture Manager Pro could not capture model statistics: " + exception.Message);
        }

        private static ModelStatisticsSnapshot CaptureAndStore(GameObject avatar, bool mobile,
            ModelStatisticsSource source, ModelStatisticsBuildStatus buildStatus,
            string identityOverride, string nameOverride, string notes,
            bool hasBuildPlatform = false, bool buildPlatformIsMobile = false)
        {
            if (!avatar || !avatar.GetComponent<VRCAvatarDescriptor>())
                throw new ArgumentException("Select an avatar with a VRChat Avatar Descriptor.", nameof(avatar));

            // APW delegates every metric and grade to the SDK, including simulator/runtime additions.
            // Do not alter SDK ignore delegates, components, constraint execution groups or processor state.
            var stats = new AvatarPerformanceStats(mobile);
            AvatarPerformance.CalculatePerformanceStats(avatar.name, avatar, stats, mobile);
            var snapshot = new ModelStatisticsSnapshot(nameOverride ?? avatar.name,
                identityOverride ?? GetAvatarIdentity(avatar), mobile, source, buildStatus,
                stats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.Overall), BuildMetrics(stats), notes,
                hasBuildPlatform, buildPlatformIsMobile);
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
            return "session:" + avatar.GetInstanceID().ToString(CultureInfo.InvariantCulture);
        }

        private static List<ModelStatisticsMetric> BuildMetrics(AvatarPerformanceStats stats)
        {
            var metrics = new List<ModelStatisticsMetric>();
            Add(metrics, stats, AvatarPerformanceCategory.DownloadSize, FormatBytes(stats.downloadSizeBytes));
            Add(metrics, stats, AvatarPerformanceCategory.UncompressedSize, FormatBytes(stats.uncompressedSizeBytes));
            Add(metrics, stats, AvatarPerformanceCategory.PolyCount, stats.polyCount);
            Add(metrics, stats, AvatarPerformanceCategory.AABB, stats.aabb?.ToString());
            Add(metrics, stats, AvatarPerformanceCategory.SkinnedMeshCount, stats.skinnedMeshCount);
            Add(metrics, stats, AvatarPerformanceCategory.MeshCount, stats.meshCount);
            Add(metrics, stats, AvatarPerformanceCategory.MaterialCount, stats.materialCount);
            Add(metrics, stats, AvatarPerformanceCategory.PhysBoneComponentCount, stats.physBone?.componentCount ?? 0);
            Add(metrics, stats, AvatarPerformanceCategory.PhysBoneTransformCount, stats.physBone?.transformCount ?? 0);
            Add(metrics, stats, AvatarPerformanceCategory.PhysBoneColliderCount, stats.physBone?.colliderCount ?? 0);
            Add(metrics, stats, AvatarPerformanceCategory.PhysBoneCollisionCheckCount, stats.physBone?.collisionCheckCount ?? 0);
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
            Add(metrics, stats, AvatarPerformanceCategory.TextureMegabytes,
                stats.textureMegabytes?.ToString("F2", CultureInfo.CurrentCulture));
            Add(metrics, stats, AvatarPerformanceCategory.ConstraintsCount, stats.constraintsCount);
            Add(metrics, stats, AvatarPerformanceCategory.ConstraintDepth, stats.constraintDepth);
            Add(metrics, stats, AvatarPerformanceCategory.RaycastCount, stats.raycastCount);
            return metrics;
        }

        private static string FormatBytes(int? bytes)
        {
            return bytes.HasValue ? (bytes.Value / 1024.0 / 1024.0).ToString("F2", CultureInfo.CurrentCulture) : null;
        }

        private static void Add(List<ModelStatisticsMetric> metrics, AvatarPerformanceStats stats,
            AvatarPerformanceCategory category, object value)
        {
            var formatted = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            metrics.Add(new ModelStatisticsMetric(category, formatted,
                stats.GetPerformanceRatingForCategory(category), value != null));
        }

        private static void PersistHistory()
        {
            SessionState.SetString(HistoryKey, JsonUtility.ToJson(new StoredHistory { Snapshots = Snapshots }));
        }

        private static void PersistAndNotify()
        {
            PersistHistory();
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
            var changed = false;
            if (buildActive)
            {
                for (var index = 0; index < Snapshots.Count; index++)
                {
                    if (!PendingBuildSnapshotIds.Contains(Snapshots[index].Id)) continue;
                    Snapshots[index] = Snapshots[index].MarkBuildUnconfirmed();
                    changed = true;
                }
            }
            builder = null;
            buildActive = false;
            buildAvatarIdentity = null;
            buildAvatarName = null;
            PendingBuildSnapshotIds.Clear();
            buildMobile = false;
            if (changed) PersistAndNotify();
        }

        private static void OnBuildStart(object sender, object target)
        {
            // Every automatic entry point must leave the SDK pipeline usable if diagnostics fails.
            try
            {
                var avatar = target as GameObject;
                buildActive = avatar && avatar.GetComponent<VRCAvatarDescriptor>();
                buildAvatarIdentity = buildActive ? GetAvatarIdentity(avatar) : null;
                buildAvatarName = buildActive ? avatar.name : null;
                buildMobile = IsCurrentPlatformMobile();
                PendingBuildSnapshotIds.Clear();
                if (buildActive) CapturePairSafely(avatar, ModelStatisticsSource.BeforeProcessing,
                    ModelStatisticsBuildStatus.NotApplicable, buildAvatarIdentity, buildAvatarName,
                    BeforeBuildNotes, true);
            }
            catch (Exception exception)
            {
                buildActive = false;
                ReportCaptureFailure(exception);
            }
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
            var changed = false;
            for (var index = 0; index < Snapshots.Count; index++)
            {
                if (!PendingBuildSnapshotIds.Contains(Snapshots[index].Id)) continue;
                Snapshots[index] = Snapshots[index].CompleteBuild(succeeded, bytes);
                changed = true;
            }
            PendingBuildSnapshotIds.Clear();
            if (changed) PersistAndNotify();
        }
    }
}
