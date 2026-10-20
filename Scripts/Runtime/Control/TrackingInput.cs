using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control
{
    public enum TrackingRole
    {
        Unassigned = -1,
        Head, LeftHand, RightHand, Hip, LeftFoot, RightFoot,
        Chest, LeftKnee, RightKnee, LeftElbow, RightElbow
    }

    /// <summary>A device pose in its frame's tracking space, in Unity axes and real-world metres.</summary>
    public readonly struct TrackedDevice
    {
        public readonly string Id;
        public readonly TrackingRole Role;
        public readonly Pose Pose;
        public readonly bool Valid;
        public readonly double Timestamp;

        public TrackedDevice(string id, TrackingRole role, Pose pose, bool valid, double timestamp)
        {
            Id = id ?? string.Empty;
            Role = role;
            Pose = pose;
            Timestamp = timestamp;
            Valid = valid && Finite(pose.position.x) && Finite(pose.position.y) && Finite(pose.position.z) &&
                Finite(pose.rotation.x) && Finite(pose.rotation.y) && Finite(pose.rotation.z) && Finite(pose.rotation.w) &&
                Quaternion.Dot(pose.rotation, pose.rotation) > 0.00001f && !double.IsNaN(timestamp) && !double.IsInfinity(timestamp);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class TrackingFrame
    {
        public static readonly TrackingFrame Empty = new TrackingFrame(Array.Empty<TrackedDevice>(), 0d);
        public readonly IReadOnlyList<TrackedDevice> Devices;
        public readonly double Timestamp;
        public readonly string SpaceId;
        public readonly Pose Origin;

        public TrackingFrame(IEnumerable<TrackedDevice> devices, double timestamp, string spaceId = "tracking", Pose? origin = null)
        {
            Devices = new List<TrackedDevice>(devices ?? Array.Empty<TrackedDevice>()).AsReadOnly();
            Timestamp = timestamp;
            SpaceId = spaceId ?? "tracking";
            Origin = origin ?? new Pose(Vector3.zero, Quaternion.identity);
        }

        public bool TryGet(TrackingRole role, out TrackedDevice device)
        {
            foreach (var candidate in Devices)
                if (candidate.Role == role && candidate.Valid) { device = candidate; return true; }
            device = default;
            return false;
        }
    }

    public readonly struct VrInputFrame
    {
        public readonly Vector2 MenuAxis;
        public readonly bool MenuPressed, BackPressed, SelectPressed, SelectReleased, PausePressed, RecenterPressed;
        public readonly float LeftTrigger, RightTrigger, LeftGrip, RightGrip;
        public readonly int GestureLeft, GestureRight;
        public readonly float GestureLeftWeight, GestureRightWeight;

        public VrInputFrame(Vector2 menuAxis, bool menuPressed = false, bool backPressed = false,
            bool selectPressed = false, bool selectReleased = false, bool pausePressed = false, bool recenterPressed = false,
            float leftTrigger = 0f, float rightTrigger = 0f, float leftGrip = 0f, float rightGrip = 0f,
            int gestureLeft = 0, int gestureRight = 0, float gestureLeftWeight = 0f, float gestureRightWeight = 0f)
        {
            MenuAxis = Vector2.ClampMagnitude(new Vector2(Finite(menuAxis.x), Finite(menuAxis.y)), 1f);
            MenuPressed = menuPressed; BackPressed = backPressed;
            SelectPressed = selectPressed; SelectReleased = selectReleased;
            PausePressed = pausePressed; RecenterPressed = recenterPressed;
            LeftTrigger = Mathf.Clamp01(Finite(leftTrigger)); RightTrigger = Mathf.Clamp01(Finite(rightTrigger));
            LeftGrip = Mathf.Clamp01(Finite(leftGrip)); RightGrip = Mathf.Clamp01(Finite(rightGrip));
            GestureLeft = Mathf.Clamp(gestureLeft, 0, 7); GestureRight = Mathf.Clamp(gestureRight, 0, 7);
            GestureLeftWeight = Mathf.Clamp01(Finite(gestureLeftWeight)); GestureRightWeight = Mathf.Clamp01(Finite(gestureRightWeight));
        }

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    public interface ITrackingInputSource
    {
        TrackingFrame Tracking { get; }
        void Poll();
    }

    public interface IVrInputSource : IMotionInputSource, ITrackingInputSource, IDisposable
    {
        bool Start(out string error);
        bool Running { get; }
        bool InputAvailable { get; }
        string Status { get; }
        VrInputFrame Actions { get; }
        void OpenBindings();
    }

    public static class VrInputProviders
    {
        public sealed class Provider
        {
            public readonly string Id, DisplayName;
            public readonly Func<IVrInputSource> Create;
            public readonly Action Configure;
            public readonly Func<bool> IsConfigured;
            public Provider(string id, string displayName, Func<IVrInputSource> create, Action configure = null, Func<bool> isConfigured = null)
            { Id = id; DisplayName = displayName; Create = create; Configure = configure; IsConfigured = isConfigured; }
        }

        private static readonly List<Provider> Registered = new List<Provider>();
        public static IReadOnlyList<Provider> Providers => Registered.AsReadOnly();

        public static void Register(string id, string displayName, Func<IVrInputSource> create, Action configure = null, Func<bool> isConfigured = null)
        {
            if (string.IsNullOrEmpty(id) || create == null) throw new ArgumentException("A VR provider needs an identifier and factory.");
            Registered.RemoveAll(provider => provider.Id == id);
            Registered.Add(new Provider(id, displayName, create, configure, isConfigured));
        }
    }
}
