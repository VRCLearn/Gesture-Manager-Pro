#if VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Params;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools
{
    internal sealed class MmdPreviewSession : IDisposable
    {
        private readonly ModuleVrc3 _module;
        private readonly PlayableGraph _graph;
        private readonly AnimationLayerMixerPlayable _mixer;
        private readonly int _stationIndex;
        private readonly AnimationClip _clip;
        private readonly bool _hasMotion;
        private readonly bool _loop;
        private readonly Vrc3Param _inStation;
        private readonly Vrc3Param _seated;
        private readonly float _inStationValue;
        private readonly float _seatedValue;
        private readonly bool _locomotionDisabled;
        private readonly Dictionary<string, VRC_AnimatorTrackingControl.TrackingType> _tracking;
        private readonly AnimatorControllerWeight _sitting;
        private readonly float _sittingWeight;
        private readonly AnimatorControllerWeight _gesture;
        private readonly float _gestureWeight;
        private readonly AnimatorControllerPlayable _fx;
        private readonly Dictionary<int, float> _fxWeights = new();
        private readonly List<TransformState> _transforms = new();
        private readonly List<BlendShapeState> _blendShapes = new();
        private readonly float _stationWeight;
        private readonly float _startedAt;
        private AnimationClipPlayable _playable;
        private float _timeSeconds;
        private bool _disposed;

        internal bool Running { get; private set; }
        internal float TimeSeconds => Running && _playable.IsValid() ? (float)_playable.GetTime() : _timeSeconds;

        internal MmdPreviewSession(ModuleVrc3 module, PlayableGraph graph, int stationIndex,
            Dictionary<VRCAvatarDescriptor.AnimLayerType, ModuleVrc3.LayerData> layers,
            AnimationClip clip, bool disableGesture, bool disableFxLayers, bool loop)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Enter Play Mode and resume the editor before enabling MMD preview.");
            _module = module ?? throw new ArgumentNullException(nameof(module));
            if (module.IsInvalid() || !module.Active || !graph.IsValid() || graph.GetOutputCount() == 0)
                throw new InvalidOperationException("MMD preview requires an active avatar animation graph.");
            if (layers == null) throw new ArgumentNullException(nameof(layers));
            var source = graph.GetOutput(0).GetSourcePlayable();
            if (!source.IsValid() || source.GetPlayableType() != typeof(AnimationLayerMixerPlayable))
                throw new InvalidOperationException("MMD preview requires the avatar's animation layer mixer.");
            _mixer = (AnimationLayerMixerPlayable)source;
            if (stationIndex < 0 || stationIndex >= _mixer.GetInputCount() || _mixer.GetInput(stationIndex).IsValid())
                throw new InvalidOperationException("The station animation input is unavailable.");
            _graph = graph;
            _stationIndex = stationIndex;
            _stationWeight = _mixer.GetInputWeight(stationIndex);
            _clip = clip;
            _hasMotion = clip && !clip.empty;
            _loop = loop;
            _startedAt = UnityEngine.Time.time;
            _inStation = module.GetParam(Vrc3DefaultParams.InStation);
            _seated = module.GetParam(Vrc3DefaultParams.Seated);
            if (_inStation == null || _seated == null)
                throw new InvalidOperationException("The simulator has not initialized its station parameters yet.");
            _inStationValue = _inStation.FloatValue();
            _seatedValue = _seated.FloatValue();
            _locomotionDisabled = module.LocomotionDisabled;
            _tracking = new Dictionary<string, VRC_AnimatorTrackingControl.TrackingType>(module.TrackingControls);
            if (layers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.Sitting, out var sitting))
            {
                _sitting = sitting.Weight;
                _sittingWeight = _sitting.Weight;
            }
            if (disableGesture && layers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.Gesture, out var gesture))
            {
                _gesture = gesture.Weight;
                _gestureWeight = _gesture.Weight;
            }
            if (disableFxLayers && layers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx) && fx.Playable.IsValid())
            {
                _fx = fx.Playable;
                for (var index = 1; index <= 2 && index < _fx.GetLayerCount(); index++)
                    _fxWeights.Add(index, _fx.GetLayerWeight(index));
            }
            foreach (var transform in module.Avatar.GetComponentsInChildren<Transform>(true))
                _transforms.Add(new TransformState(transform));
            foreach (var renderer in module.Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh) _blendShapes.Add(new BlendShapeState(renderer));

            try
            {
                module.Resetting += OnResetting;
                EditorApplication.pauseStateChanged += OnPauseChanged;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                AssemblyReloadEvents.beforeAssemblyReload += Dispose;
                Running = true;
                if (_hasMotion)
                {
                    _playable = AnimationClipPlayable.Create(graph, clip);
                    _playable.SetApplyFootIK(false);
                    _playable.SetApplyPlayableIK(false);
                    _playable.SetTime(0d);
                    _mixer.ConnectInput(stationIndex, _playable, 0, 1f);
                }
                // Apply the station preset once. SDK Tracking Control and locomotion
                // callbacks remain observable during the preview, including conflicts.
                foreach (var key in _tracking.Keys)
                    module.TrackingControls[key] = VRC_AnimatorTrackingControl.TrackingType.Animation;
                module.LocomotionDisabled = true;
                Apply();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Apply()
        {
            if (!Running) return;
            if (!_graph.IsValid() || !_mixer.IsValid() || _module.IsInvalid() || !_module.Active ||
                !EditorApplication.isPlaying || EditorApplication.isPaused)
            {
                Dispose();
                return;
            }
            if (!ReferenceEquals(_clip, null) && !_clip)
            {
                Dispose();
                return;
            }
            if (_hasMotion)
            {
                if (!_clip || !_playable.IsValid())
                {
                    Dispose();
                    return;
                }
                var time = _playable.GetTime();
                var duration = _clip.length;
                if (duration > 0f && time >= duration)
                {
                    if (!_loop)
                    {
                        _timeSeconds = duration;
                        Dispose();
                        return;
                    }
                    time %= duration;
                    _playable.SetTime(time);
                }
                _timeSeconds = (float)time;
            }
            else _timeSeconds = UnityEngine.Time.time - _startedAt;
            _inStation.Set(_module, true, this);
            if (!Running) return;
            _seated.Set(_module, false, this);
            if (!Running) return;
            _sitting?.Set(0f);
            _gesture?.Set(0f);
            if (_fx.IsValid())
                foreach (var pair in _fxWeights) _fx.SetLayerWeight(pair.Key, 0f);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Running = false;
            _module.Resetting -= OnResetting;
            EditorApplication.pauseStateChanged -= OnPauseChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            Restore(() =>
            {
                if (_mixer.IsValid() && _stationIndex < _mixer.GetInputCount())
                {
                    if (_playable.IsValid() && _mixer.GetInput(_stationIndex).Equals((Playable)_playable))
                        _mixer.DisconnectInput(_stationIndex);
                    _mixer.SetInputWeight(_stationIndex, _stationWeight);
                }
            });
            Restore(() => { if (_graph.IsValid() && _playable.IsValid()) _graph.DestroyPlayable(_playable); });
            Restore(() => RestoreParameter(Vrc3DefaultParams.InStation, _inStation, _inStationValue));
            Restore(() => RestoreParameter(Vrc3DefaultParams.Seated, _seated, _seatedValue));
            if (_mixer.IsValid())
            {
                Restore(() => _sitting?.Set(_sittingWeight));
                Restore(() => _gesture?.Set(_gestureWeight));
            }
            if (_fx.IsValid())
                foreach (var pair in _fxWeights)
                    Restore(() => _fx.SetLayerWeight(pair.Key, pair.Value));
            _module.TrackingControls.Clear();
            foreach (var pair in _tracking) _module.TrackingControls.Add(pair.Key, pair.Value);
            _module.LocomotionDisabled = _locomotionDisabled;
            foreach (var state in _transforms) Restore(state.Restore);
            foreach (var state in _blendShapes) Restore(state.Restore);
        }

        private void RestoreParameter(string name, Vrc3Param parameter, float value)
        {
            if (!ReferenceEquals(_module.GetParam(name), parameter)) return;
            if (_mixer.IsValid()) parameter.Set(_module, value, this);
            else parameter.InternalSet(value, this);
        }

        private static void Restore(Action restore)
        {
            try { restore(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private void OnResetting(ModuleVrc3 module) => Dispose();
        private void OnPauseChanged(PauseState state) { if (state == PauseState.Paused) Dispose(); }
        private void OnPlayModeChanged(PlayModeStateChange state) { if (state != PlayModeStateChange.EnteredPlayMode) Dispose(); }

        private sealed class TransformState
        {
            private readonly Transform _transform;
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly Vector3 _scale;

            internal TransformState(Transform transform)
            {
                _transform = transform;
                _position = transform.localPosition;
                _rotation = transform.localRotation;
                _scale = transform.localScale;
            }

            internal void Restore()
            {
                if (!_transform) return;
                _transform.localPosition = _position;
                _transform.localRotation = _rotation;
                _transform.localScale = _scale;
            }
        }

        private sealed class BlendShapeState
        {
            private readonly SkinnedMeshRenderer _renderer;
            private readonly Mesh _mesh;
            private readonly float[] _weights;

            internal BlendShapeState(SkinnedMeshRenderer renderer)
            {
                _renderer = renderer;
                _mesh = renderer.sharedMesh;
                _weights = new float[_mesh.blendShapeCount];
                for (var index = 0; index < _weights.Length; index++)
                    _weights[index] = renderer.GetBlendShapeWeight(index);
            }

            internal void Restore()
            {
                if (!_renderer || _renderer.sharedMesh != _mesh) return;
                for (var index = 0; index < _weights.Length && index < _mesh.blendShapeCount; index++)
                    _renderer.SetBlendShapeWeight(index, _weights[index]);
            }
        }
    }
}
#endif
