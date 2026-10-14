using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase.Validation.Performance;

namespace VRCLearn.GestureManagerPro.Diagnostics
{
    public enum ModelStatisticsSource
    {
        CurrentAvatar,
        PlayModePreview,
        SdkPreprocess,
        SdkBuild
    }

    public enum ModelStatisticsBuildStatus
    {
        NotApplicable,
        Pending,
        Succeeded,
        Failed,
        Unconfirmed
    }

    [Serializable]
    public sealed class ModelStatisticsMetric
    {
        [SerializeField] private AvatarPerformanceCategory category;
        [SerializeField] private string value;
        [SerializeField] private PerformanceRating rating;
        [SerializeField] private bool isAvailable;

        public AvatarPerformanceCategory Category => category;
        public string Value => value;
        public PerformanceRating Rating => rating;
        public bool IsAvailable => isAvailable;

        internal ModelStatisticsMetric(AvatarPerformanceCategory category, string value,
            PerformanceRating rating, bool isAvailable)
        {
            this.category = category;
            this.value = value;
            this.rating = rating;
            this.isAvailable = isAvailable;
        }
    }

    /// <summary>An immutable record containing no references to the processed avatar or its assets.</summary>
    [Serializable]
    public sealed class ModelStatisticsSnapshot
    {
        [SerializeField] private string id;
        [SerializeField] private string avatarName;
        [SerializeField] private string avatarIdentity;
        [SerializeField] private string capturedUtc;
        [SerializeField] private bool isMobile;
        [SerializeField] private ModelStatisticsSource source;
        [SerializeField] private ModelStatisticsBuildStatus buildStatus;
        [SerializeField] private PerformanceRating overallRating;
        [SerializeField] private List<ModelStatisticsMetric> metrics = new List<ModelStatisticsMetric>();
        [SerializeField] private string notes;
        [SerializeField] private long bundleBytes = -1;

        public string Id => id;
        public string AvatarName => avatarName;
        public string AvatarIdentity => avatarIdentity;
        public string CapturedUtc => capturedUtc;
        public bool IsMobile => isMobile;
        public ModelStatisticsSource Source => source;
        public ModelStatisticsBuildStatus BuildStatus => buildStatus;
        public PerformanceRating OverallRating => overallRating;
        public IReadOnlyList<ModelStatisticsMetric> Metrics => metrics.AsReadOnly();
        public string Notes => notes;
        public long BundleBytes => bundleBytes;

        internal ModelStatisticsSnapshot(string avatarName, string avatarIdentity, bool isMobile,
            ModelStatisticsSource source, ModelStatisticsBuildStatus buildStatus,
            PerformanceRating overallRating, List<ModelStatisticsMetric> metrics, string notes)
        {
            id = Guid.NewGuid().ToString("N");
            this.avatarName = avatarName;
            this.avatarIdentity = avatarIdentity;
            capturedUtc = DateTime.UtcNow.ToString("O");
            this.isMobile = isMobile;
            this.source = source;
            this.buildStatus = buildStatus;
            this.overallRating = overallRating;
            this.metrics = metrics;
            this.notes = notes;
        }

        internal ModelStatisticsSnapshot CompleteBuild(bool succeeded, long bytes)
        {
            var completed = JsonUtility.FromJson<ModelStatisticsSnapshot>(JsonUtility.ToJson(this));
            completed.buildStatus = succeeded ? ModelStatisticsBuildStatus.Succeeded : ModelStatisticsBuildStatus.Failed;
            completed.source = succeeded ? ModelStatisticsSource.SdkBuild : ModelStatisticsSource.SdkPreprocess;
            completed.bundleBytes = succeeded ? bytes : -1;
            return completed;
        }

        internal ModelStatisticsSnapshot MarkBuildUnconfirmed()
        {
            var unconfirmed = JsonUtility.FromJson<ModelStatisticsSnapshot>(JsonUtility.ToJson(this));
            unconfirmed.buildStatus = ModelStatisticsBuildStatus.Unconfirmed;
            unconfirmed.source = ModelStatisticsSource.SdkPreprocess;
            unconfirmed.bundleBytes = -1;
            return unconfirmed;
        }
    }
}
