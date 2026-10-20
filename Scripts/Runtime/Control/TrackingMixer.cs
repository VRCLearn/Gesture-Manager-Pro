using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control
{
    /// <summary>Combines provider poses in world space without driving a body role twice.</summary>
    public sealed class TrackingMixer
    {
        private readonly Dictionary<string, TrackingRole> _assignments = new Dictionary<string, TrackingRole>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, TrackingRole> Assignments => _assignments;

        public void Assign(string deviceId, TrackingRole role)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("A tracking assignment needs a stable device identifier.", nameof(deviceId));
            if (role < TrackingRole.Unassigned || role > TrackingRole.RightElbow) throw new ArgumentOutOfRangeException(nameof(role));
            // A manual assignment owns its role, including when another source advertises it automatically.
            if (role != TrackingRole.Unassigned)
            {
                var displaced = new List<string>();
                foreach (var entry in _assignments)
                    if (entry.Value == role && entry.Key != deviceId) displaced.Add(entry.Key);
                foreach (var id in displaced) _assignments[id] = TrackingRole.Unassigned;
            }
            _assignments[deviceId] = role;
        }

        public void ClearAssignment(string deviceId)
        {
            if (deviceId != null) _assignments.Remove(deviceId);
        }

        public TrackingFrame Mix(params TrackingFrame[] frames)
        {
            var candidates = new List<TrackedDevice>();
            var seenDevices = new HashSet<string>(StringComparer.Ordinal);
            var timestamp = 0d;
            foreach (var frame in frames ?? Array.Empty<TrackingFrame>())
            {
                if (frame == null || !IsValidPose(frame.Origin)) continue;
                if (!double.IsNaN(frame.Timestamp) && !double.IsInfinity(frame.Timestamp)) timestamp = Math.Max(timestamp, frame.Timestamp);
                foreach (var device in frame.Devices)
                {
                    if (!device.Valid || string.IsNullOrEmpty(device.Id) || !seenDevices.Add(device.Id)) continue;
                    var role = _assignments.TryGetValue(device.Id, out var assigned) ? assigned : device.Role;
                    var pose = TransformPose(frame.Origin, device.Pose);
                    var normalized = new TrackedDevice(device.Id, role, pose, true, device.Timestamp);
                    if (normalized.Valid) candidates.Add(normalized);
                }
            }
            var selected = new List<TrackedDevice>(11);
            var roles = new HashSet<TrackingRole>();
            // Do both passes so an automatically advertised role never hides its manual owner.
            foreach (var candidate in candidates)
                if (_assignments.ContainsKey(candidate.Id)) Add(candidate, roles, selected);
            foreach (var candidate in candidates)
                if (!_assignments.ContainsKey(candidate.Id)) Add(candidate, roles, selected);
            return new TrackingFrame(selected, timestamp, "world", new Pose(Vector3.zero, Quaternion.identity));
        }

        private static void Add(TrackedDevice device, HashSet<TrackingRole> roles, List<TrackedDevice> devices)
        {
            if (device.Role >= TrackingRole.Head && device.Role <= TrackingRole.RightElbow && roles.Add(device.Role)) devices.Add(device);
        }

        public static Pose TransformPose(Pose origin, Pose pose)
        {
            var rotation = Normalize(origin.rotation);
            return new Pose(origin.position + rotation * pose.position, Normalize(rotation * pose.rotation));
        }

        internal static bool IsValidPose(Pose pose) => Finite(pose.position.x) && Finite(pose.position.y) && Finite(pose.position.z) &&
            Finite(pose.rotation.x) && Finite(pose.rotation.y) && Finite(pose.rotation.z) && Finite(pose.rotation.w) &&
            Quaternion.Dot(pose.rotation, pose.rotation) > 0.00001f;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static Quaternion Normalize(Quaternion rotation)
        {
            var length = Mathf.Sqrt(Quaternion.Dot(rotation, rotation));
            return length > 0.00001f ? new Quaternion(rotation.x / length, rotation.y / length, rotation.z / length, rotation.w / length) : Quaternion.identity;
        }
    }
}
