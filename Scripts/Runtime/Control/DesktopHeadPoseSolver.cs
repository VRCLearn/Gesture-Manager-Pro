using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control
{
    /// <summary>Tracks the desktop player view after animation without changing the body's authored pose.</summary>
    public sealed class DesktopHeadPoseSolver : IDisposable
    {
        private readonly Animator _avatar;
        private readonly Transform _head;
        private readonly Quaternion _viewToHeadRotation;
        private Quaternion _inputRotation;
        private Quaternion _outputRotation;
        private bool _hasOutput;
        private bool _disposed;

        public DesktopHeadPoseSolver(Animator avatar)
        {
            if (!avatar || !avatar.avatar || !avatar.avatar.isValid || !avatar.isHuman)
                throw new ArgumentException("Desktop head posing requires a valid Humanoid avatar.", nameof(avatar));
            _avatar = avatar;
            _head = avatar.GetBoneTransform(HumanBodyBones.Head);
            if (!_head)
                throw new ArgumentException("Desktop head posing requires a mapped Humanoid head.", nameof(avatar));
            _viewToHeadRotation = GetNeutralHeadRotation(avatar, _head);
        }

        /// <summary>Sets the tracked head to an absolute world-space player view, including a level view.</summary>
        public void Solve(Quaternion viewRotation)
        {
            if (_disposed) return;
            Release();
            if (!_avatar || !_head || !IsFinite(viewRotation.x) || !IsFinite(viewRotation.y) ||
                !IsFinite(viewRotation.z) || !IsFinite(viewRotation.w)) return;
            var magnitude = Quaternion.Dot(viewRotation, viewRotation);
            if (!IsFinite(magnitude) || magnitude < 0.000001f) return;
            _inputRotation = _head.localRotation;
            // Tracking owns head orientation. Assigning a world target also compensates for animated
            // ancestors, instead of adding view pitch to the animation's head and torso rotations.
            _head.rotation = viewRotation.normalized * _viewToHeadRotation;
            _outputRotation = _head.localRotation;
            _hasOutput = true;
        }

        /// <summary>Removes only this solver's unchanged rotation, preserving newly evaluated animation.</summary>
        public void Release()
        {
            if (_hasOutput && _head && SameRotation(_head.localRotation, _outputRotation))
                _head.localRotation = _inputRotation;
            _hasOutput = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Release();
            _disposed = true;
        }

        private static Quaternion GetNeutralHeadRotation(Animator avatar, Transform head)
        {
            var paths = new List<string>();
            var parents = new List<int>();
            var headIndex = -1;
            AddJointPaths(avatar.transform, string.Empty, -1, head, paths, parents, ref headIndex);
            // A handler constructed from paths retargets an internal pose without writing to the
            // live hierarchy. Its neutral Humanoid pose supplies the imported head bone basis even
            // when the avatar is already animated, and does not assume head.forward is its gaze.
            using (var handler = new HumanPoseHandler(avatar.avatar, paths.ToArray()))
            using (var pose = new NativeArray<float>(paths.Count * 7, Allocator.Temp))
            {
                var neutral = new HumanPose { bodyRotation = Quaternion.identity };
                handler.SetInternalHumanPose(ref neutral);
                handler.GetInternalAvatarPose(pose);
                var rotation = Quaternion.identity;
                for (var index = headIndex; index >= 0; index = parents[index])
                {
                    var offset = index * 7 + 3;
                    rotation = new Quaternion(pose[offset], pose[offset + 1], pose[offset + 2], pose[offset + 3]) * rotation;
                }
                return rotation.normalized;
            }
        }

        private static void AddJointPaths(Transform joint, string path, int parent, Transform head,
            List<string> paths, List<int> parents, ref int headIndex)
        {
            var index = paths.Count;
            paths.Add(path);
            parents.Add(parent);
            if (joint == head) headIndex = index;
            foreach (Transform child in joint)
            {
                var childPath = path.Length == 0 ? child.name : path + "/" + child.name;
                AddJointPaths(child, childPath, index, head, paths, parents, ref headIndex);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool SameRotation(Quaternion left, Quaternion right)
        {
            var same = Mathf.Abs(left.x - right.x) + Mathf.Abs(left.y - right.y) + Mathf.Abs(left.z - right.z) + Mathf.Abs(left.w - right.w);
            var reversed = Mathf.Abs(left.x + right.x) + Mathf.Abs(left.y + right.y) + Mathf.Abs(left.z + right.z) + Mathf.Abs(left.w + right.w);
            return Mathf.Min(same, reversed) < 0.000001f;
        }
    }
}
