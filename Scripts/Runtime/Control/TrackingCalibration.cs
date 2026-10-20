using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control
{
    public enum TrackingMeasurement { Height, ArmSpan }

    /// <summary>Maps a standing calibration frame to an avatar without changing its asset or tracking provider.</summary>
    public sealed class TrackingCalibration
    {
        private readonly Dictionary<TrackingRole, Pose> _offsets = new Dictionary<TrackingRole, Pose>();
        private readonly Dictionary<TrackingRole, string> _devices = new Dictionary<TrackingRole, string>();
        private Transform _avatar;
        private Pose _avatarAnchor;
        private Vector3 _headAnchor;
        private Quaternion _trackingYaw;
        private float _avatarScale;

        public TrackingMeasurement Measurement { get; set; }
        public float UserHeightOverride { get; set; }
        public float UserArmSpanOverride { get; set; }
        public bool Calibrated { get; private set; }
        public float UserHeight { get; private set; }
        public float AvatarHeight { get; private set; }
        public float UserArmSpan { get; private set; }
        public float AvatarArmSpan { get; private set; }
        public float Scale { get; private set; } = 1f;

        public bool Calibrate(Animator avatar, Vector3 worldViewPosition, TrackingFrame frame, out string error)
        {
            Reset();
            error = null;
            if (!avatar || !avatar.avatar || !avatar.avatar.isValid || !avatar.isHuman)
                return Fail("VR calibration requires a valid Humanoid avatar.", out error);
            if (frame == null || !TrackingMixer.IsValidPose(frame.Origin) || !frame.TryGet(TrackingRole.Head, out var head))
                return Fail("Keep the headset tracked before calibrating.", out error);
            var headPose = TrackingMixer.TransformPose(frame.Origin, head.Pose);
            var measuredHeight = Vector3.Dot(headPose.position - frame.Origin.position, frame.Origin.rotation * Vector3.up);
            UserHeight = Positive(UserHeightOverride) ? UserHeightOverride : measuredHeight;
            AvatarHeight = Vector3.Dot(worldViewPosition - avatar.transform.position, Vector3.up);
            if (!Positive(UserHeight) || !Positive(AvatarHeight))
                return Fail("Calibration requires positive standing eye heights in metres.", out error);
            var left = avatar.GetBoneTransform(HumanBodyBones.LeftHand);
            var right = avatar.GetBoneTransform(HumanBodyBones.RightHand);
            AvatarArmSpan = left && right ? Vector3.Distance(left.position, right.position) : 0f;
            if (frame.TryGet(TrackingRole.LeftHand, out var leftDevice) && frame.TryGet(TrackingRole.RightHand, out var rightDevice))
                UserArmSpan = Vector3.Distance(TrackingMixer.TransformPose(frame.Origin, leftDevice.Pose).position,
                    TrackingMixer.TransformPose(frame.Origin, rightDevice.Pose).position);
            if (Positive(UserArmSpanOverride)) UserArmSpan = UserArmSpanOverride;
            if (Measurement == TrackingMeasurement.ArmSpan && (!Positive(UserArmSpan) || !Positive(AvatarArmSpan)))
                return Fail("Arm-span calibration requires both hands held apart or a measured arm span.", out error);
            Scale = Measurement == TrackingMeasurement.ArmSpan ? AvatarArmSpan / UserArmSpan : AvatarHeight / UserHeight;
            if (!Positive(Scale)) return Fail("The calibration scale is invalid.", out error);
            _avatar = avatar.transform;
            _avatarScale = Mathf.Abs(_avatar.lossyScale.y);
            _avatarAnchor = new Pose(worldViewPosition, Yaw(_avatar.rotation));
            _headAnchor = headPose.position;
            _trackingYaw = Yaw(headPose.rotation);
            foreach (var device in frame.Devices)
            {
                if (!device.Valid || device.Role < TrackingRole.Head || device.Role > TrackingRole.RightElbow || _offsets.ContainsKey(device.Role)) continue;
                var bone = avatar.GetBoneTransform(BoneFor(device.Role));
                if (!bone) continue;
                var mapped = Map(TrackingMixer.TransformPose(frame.Origin, device.Pose));
                var inverse = Quaternion.Inverse(mapped.rotation);
                _offsets.Add(device.Role, new Pose(inverse * (bone.position - mapped.position), inverse * bone.rotation));
                _devices.Add(device.Role, device.Id);
            }
            Calibrated = _offsets.ContainsKey(TrackingRole.Head);
            if (!Calibrated) return Fail("The Humanoid avatar has no tracked head bone.", out error);
            return true;
        }

        public bool TryGetWorldPose(TrackingRole role, TrackingFrame frame, out Pose pose)
        {
            pose = default;
            if (!Calibrated || !_avatar || frame == null || !TrackingMixer.IsValidPose(frame.Origin) || !_offsets.TryGetValue(role, out var offset) ||
                !frame.TryGet(role, out var device) || _devices[role] != device.Id) return false;
            var mapped = Map(TrackingMixer.TransformPose(frame.Origin, device.Pose));
            pose = new Pose(mapped.position + mapped.rotation * (offset.position * ScaleRatio), mapped.rotation * offset.rotation);
            return true;
        }

        public bool TryGetViewPose(TrackingFrame frame, out Pose pose)
        {
            pose = default;
            if (!Calibrated || !_avatar || frame == null || !TrackingMixer.IsValidPose(frame.Origin) ||
                !frame.TryGet(TrackingRole.Head, out var head) || _devices[TrackingRole.Head] != head.Id) return false;
            pose = Map(TrackingMixer.TransformPose(frame.Origin, head.Pose));
            return true;
        }

        /// <summary>Maps an already origin-transformed provider pose, including unassigned tracker visualizations.</summary>
        public Pose MapTrackingPose(Pose pose) => Calibrated ? Map(pose) : pose;

        public Pose UnmapTrackingPose(Pose worldPose)
        {
            if (!Calibrated) return worldPose;
            var inverse = Quaternion.Inverse(_avatarAnchor.rotation * Quaternion.Inverse(_trackingYaw));
            return new Pose(_headAnchor + inverse * (worldPose.position - _avatarAnchor.position) / Mathf.Max(Scale * ScaleRatio, 0.000001f),
                TrackingMixer.Normalize(inverse * worldPose.rotation));
        }

        public bool Recenter(TrackingFrame frame, Vector3 worldViewPosition)
        {
            if (!Calibrated || !_avatar || frame == null || !TrackingMixer.IsValidPose(frame.Origin) ||
                !frame.TryGet(TrackingRole.Head, out var head) || _devices[TrackingRole.Head] != head.Id) return false;
            var pose = TrackingMixer.TransformPose(frame.Origin, head.Pose);
            _headAnchor = pose.position;
            _trackingYaw = Yaw(pose.rotation);
            _avatarAnchor = new Pose(worldViewPosition, Yaw(_avatar.rotation));
            return true;
        }

        public void ApplyLocomotion(Pose previousRoot, Pose currentRoot)
        {
            if (!Calibrated) return;
            var rotation = currentRoot.rotation * Quaternion.Inverse(previousRoot.rotation);
            _avatarAnchor = new Pose(currentRoot.position + rotation * (_avatarAnchor.position - previousRoot.position),
                rotation * _avatarAnchor.rotation);
        }

        public void TransformOrigin(Pose delta)
        {
            if (Calibrated) _avatarAnchor = TrackingMixer.TransformPose(delta, _avatarAnchor);
        }

        public void Reset()
        {
            Calibrated = false;
            _avatar = null;
            _offsets.Clear();
            _devices.Clear();
            UserHeight = AvatarHeight = UserArmSpan = AvatarArmSpan = 0f;
            Scale = 1f;
        }

        private float ScaleRatio => _avatar && _avatarScale > 0.00001f ? Mathf.Abs(_avatar.lossyScale.y) / _avatarScale : 1f;

        private Pose Map(Pose device)
        {
            var rotation = _avatarAnchor.rotation * Quaternion.Inverse(_trackingYaw);
            return new Pose(_avatarAnchor.position + rotation * ((device.position - _headAnchor) * (Scale * ScaleRatio)),
                TrackingMixer.Normalize(rotation * device.rotation));
        }

        private static bool Positive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0.05f;
        private static bool Fail(string message, out string error) { error = message; return false; }
        private static Quaternion Yaw(Quaternion rotation)
        {
            var forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            return forward.sqrMagnitude > 0.00001f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
        }

        public static HumanBodyBones BoneFor(TrackingRole role)
        {
            switch (role)
            {
                case TrackingRole.Head: return HumanBodyBones.Head;
                case TrackingRole.LeftHand: return HumanBodyBones.LeftHand;
                case TrackingRole.RightHand: return HumanBodyBones.RightHand;
                case TrackingRole.Hip: return HumanBodyBones.Hips;
                case TrackingRole.LeftFoot: return HumanBodyBones.LeftFoot;
                case TrackingRole.RightFoot: return HumanBodyBones.RightFoot;
                case TrackingRole.Chest: return HumanBodyBones.Chest;
                case TrackingRole.LeftKnee: return HumanBodyBones.LeftLowerLeg;
                case TrackingRole.RightKnee: return HumanBodyBones.RightLowerLeg;
                // SteamVR's elbow role represents the upper-arm tracker used by VRChat.
                case TrackingRole.LeftElbow: return HumanBodyBones.LeftUpperArm;
                case TrackingRole.RightElbow: return HumanBodyBones.RightUpperArm;
                default: return HumanBodyBones.LastBone;
            }
        }
    }
}
