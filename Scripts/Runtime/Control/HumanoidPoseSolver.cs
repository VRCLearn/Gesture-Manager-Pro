using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control
{
    [Flags]
    public enum TrackingLockMode { Head = 1, Hip = 2, Both = Head | Hip }

    /// <summary>
    /// Local geometric tracking preview. It preserves limb lengths and authored animation for absent targets;
    /// it does not implement the VRChat client's IK solver, foot planting or replacement locomotion clips.
    /// Call Solve after animation evaluation, before camera rendering.
    /// </summary>
    public sealed class HumanoidPoseSolver : IDisposable
    {
        private readonly Animator _avatar;
        private readonly Dictionary<HumanBodyBones, Transform> _bones = new Dictionary<HumanBodyBones, Transform>();
        private readonly Dictionary<Transform, LocalPose> _original = new Dictionary<Transform, LocalPose>();
        private readonly Dictionary<Transform, LocalPose> _input = new Dictionary<Transform, LocalPose>();
        private readonly Dictionary<Transform, LocalPose> _output = new Dictionary<Transform, LocalPose>();
        private bool _disposed;

        public TrackingLockMode LockMode { get; set; } = TrackingLockMode.Both;

        public HumanoidPoseSolver(Animator avatar)
        {
            if (!avatar || !avatar.avatar || !avatar.avatar.isValid || !avatar.isHuman)
                throw new ArgumentException("VR pose solving requires a valid Humanoid avatar.", nameof(avatar));
            _avatar = avatar;
            foreach (HumanBodyBones id in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (id == HumanBodyBones.LastBone) continue;
                var bone = avatar.GetBoneTransform(id);
                if (!bone) continue;
                _bones[id] = bone;
                _original[bone] = new LocalPose(bone);
            }
        }

        public void Solve(TrackingFrame frame, TrackingCalibration calibration, Func<TrackingRole, bool> canTrack = null)
        {
            if (_disposed) return;
            ReleaseTracking();
            if (!_avatar || calibration == null || !calibration.Calibrated) return;
            foreach (var bone in _original.Keys) if (bone) _input[bone] = new LocalPose(bone);

            var hips = Bone(HumanBodyBones.Hips);
            var head = Bone(HumanBodyBones.Head);
            var hasHead = Target(TrackingRole.Head, frame, calibration, canTrack, out var headTarget);
            var hasHip = Target(TrackingRole.Hip, frame, calibration, canTrack, out var hipTarget);
            var canMoveHips = canTrack == null || canTrack(TrackingRole.Hip);
            var lockHead = (LockMode & TrackingLockMode.Head) != 0;
            var lockHip = hasHip && (LockMode & TrackingLockMode.Hip) != 0;
            if (hips && hasHip)
            {
                if (lockHip) hips.position = hipTarget.position;
                hips.rotation = hipTarget.rotation;
            }
            if (hips && head && hasHead && lockHead && !lockHip && canMoveHips) hips.position += headTarget.position - head.position;
            if (Target(TrackingRole.Chest, frame, calibration, canTrack, out var chestTarget))
            {
                var chest = Bone(HumanBodyBones.Chest);
                if (chest)
                {
                    if (LockMode == TrackingLockMode.Both) BendSpine(chest, chestTarget.position);
                    chest.rotation = chestTarget.rotation;
                }
            }
            if (head && hasHead)
            {
                if (lockHead)
                {
                    BendSpine(head, headTarget.position);
                    if (hips && !lockHip && canMoveHips) hips.position += headTarget.position - head.position;
                }
                head.rotation = headTarget.rotation;
            }
            SolveLimb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                TrackingRole.LeftHand, TrackingRole.LeftElbow, frame, calibration, canTrack);
            SolveLimb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                TrackingRole.RightHand, TrackingRole.RightElbow, frame, calibration, canTrack);
            SolveLimb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                TrackingRole.LeftFoot, TrackingRole.LeftKnee, frame, calibration, canTrack);
            SolveLimb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
                TrackingRole.RightFoot, TrackingRole.RightKnee, frame, calibration, canTrack);
            _output.Clear();
            foreach (var bone in _original.Keys) if (bone) _output[bone] = new LocalPose(bone);
        }

        private void SolveLimb(HumanBodyBones upperId, HumanBodyBones lowerId, HumanBodyBones endId,
            TrackingRole endRole, TrackingRole bendRole, TrackingFrame frame, TrackingCalibration calibration, Func<TrackingRole, bool> canTrack)
        {
            var upper = Bone(upperId);
            var lower = Bone(lowerId);
            var end = Bone(endId);
            if (!upper || !lower || !end) return;
            var hasHint = Target(bendRole, frame, calibration, canTrack, out var hint);
            var upperArm = bendRole == TrackingRole.LeftElbow || bendRole == TrackingRole.RightElbow;
            var bendPosition = hint.position;
            if (hasHint && upperArm)
            {
                // Upper-arm trackers constrain humerus orientation, not the elbow joint's world position.
                var localAxis = Quaternion.Inverse(upper.rotation) * (lower.position - upper.position);
                upper.rotation = hint.rotation;
                bendPosition = upper.position + hint.rotation * localAxis;
            }
            if (Target(endRole, frame, calibration, canTrack, out var target))
                SolveTwoBone(upper, lower, end, target, hasHint ? (Vector3?)bendPosition : null);
            else if (hasHint && !upperArm)
            {
                // Only knee roles target the lower bone. An upper-arm-only frame was handled above;
                // its forearm retains the animation's local rotation as it follows its parent.
                var from = lower.position - upper.position;
                var to = hint.position - upper.position;
                if (from.sqrMagnitude > 0.000001f && to.sqrMagnitude > 0.000001f)
                    upper.rotation = Quaternion.FromToRotation(from, to) * upper.rotation;
                lower.rotation = hint.rotation;
            }
        }

        private void BendSpine(Transform end, Vector3 target)
        {
            for (var iteration = 0; iteration < 6; iteration++)
            {
                for (var joint = end.parent; joint && joint != Bone(HumanBodyBones.Hips) && joint != _avatar.transform; joint = joint.parent)
                {
                    if (!_original.ContainsKey(joint)) continue;
                    var from = end.position - joint.position;
                    var to = target - joint.position;
                    if (from.sqrMagnitude < 0.000001f || to.sqrMagnitude < 0.000001f) continue;
                    var rotation = Quaternion.FromToRotation(from, to) * joint.rotation;
                    joint.rotation = Quaternion.RotateTowards(joint.rotation, rotation, 20f);
                    if (_input.TryGetValue(joint, out var pose))
                        joint.localRotation = Quaternion.RotateTowards(pose.Rotation, joint.localRotation, 65f);
                }
            }
        }

        /// <summary>Analytic two-bone reach with a bend hint; unreachable endpoints are clamped without stretching.</summary>
        public static void SolveTwoBone(Transform upper, Transform lower, Transform end, Pose target, Vector3? hint = null)
        {
            if (!upper || !lower || !end) return;
            var root = upper.position;
            var first = Vector3.Distance(root, lower.position);
            var second = Vector3.Distance(lower.position, end.position);
            var offset = target.position - root;
            if (first < 0.00001f || second < 0.00001f || offset.sqrMagnitude < 0.00000001f) return;
            var direction = offset.normalized;
            var distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(first - second) + 0.00001f, first + second);
            var along = (first * first + distance * distance - second * second) / (2f * distance);
            var height = Mathf.Sqrt(Mathf.Max(0f, first * first - along * along));
            var bend = Vector3.ProjectOnPlane((hint ?? lower.position) - root, direction);
            if (bend.sqrMagnitude < 0.000001f) bend = Vector3.ProjectOnPlane(upper.up, direction);
            if (bend.sqrMagnitude < 0.000001f) bend = Vector3.ProjectOnPlane(upper.forward, direction);
            var middle = root + direction * along + bend.normalized * height;
            upper.rotation = Quaternion.FromToRotation(lower.position - root, middle - root) * upper.rotation;
            var endpoint = root + direction * distance;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, endpoint - lower.position) * lower.rotation;
            end.rotation = target.rotation;
        }

        private bool Target(TrackingRole role, TrackingFrame frame, TrackingCalibration calibration,
            Func<TrackingRole, bool> canTrack, out Pose target)
        {
            target = default;
            return (canTrack == null || canTrack(role)) && calibration.TryGetWorldPose(role, frame, out target);
        }

        private Transform Bone(HumanBodyBones id) => _bones.TryGetValue(id, out var bone) ? bone : null;

        /// <summary>Releases only prior IK channels that animation has not subsequently replaced.</summary>
        public void ReleaseTracking()
        {
            foreach (var pair in _output)
                if (pair.Key && _input.TryGetValue(pair.Key, out var before))
                    pair.Value.ReleaseUnanimatedChannels(pair.Key, before);
            _input.Clear();
            _output.Clear();
        }

        public void Restore()
        {
            foreach (var pair in _original) if (pair.Key) pair.Value.Apply(pair.Key);
            _input.Clear();
            _output.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Restore();
            _disposed = true;
        }

        private readonly struct LocalPose
        {
            public readonly Vector3 Position, Scale;
            public readonly Quaternion Rotation;
            public LocalPose(Transform bone) { Position = bone.localPosition; Rotation = bone.localRotation; Scale = bone.localScale; }
            public void Apply(Transform bone) { bone.localPosition = Position; bone.localRotation = Rotation; bone.localScale = Scale; }
            public void ReleaseUnanimatedChannels(Transform bone, LocalPose before)
            {
                if (Vector3.SqrMagnitude(Position - bone.localPosition) < 0.0000000001f) bone.localPosition = before.Position;
                if (SameRotation(Rotation, bone.localRotation)) bone.localRotation = before.Rotation;
                if (Vector3.SqrMagnitude(Scale - bone.localScale) < 0.0000000001f) bone.localScale = before.Scale;
            }

            private static bool SameRotation(Quaternion left, Quaternion right)
            {
                var same = Mathf.Abs(left.x - right.x) + Mathf.Abs(left.y - right.y) + Mathf.Abs(left.z - right.z) + Mathf.Abs(left.w - right.w);
                var reversed = Mathf.Abs(left.x + right.x) + Mathf.Abs(left.y + right.y) + Mathf.Abs(left.z + right.z) + Mathf.Abs(left.w + right.w);
                return Mathf.Min(same, reversed) < 0.000001f;
            }
        }
    }
}
