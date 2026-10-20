using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Params;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    [Serializable]
    public sealed class VrSimulationSettings
    {
        public bool OscEnabled;
        public int OscPort = 9000;
        public TrackingMeasurement Measurement;
        public float UserHeight = 1.7f;
        public float UserArmSpan = 1.7f;
        public TrackingLockMode LockMode = TrackingLockMode.Both;
        public bool SnapTurning = true;
        public float SnapAngle = 45f;
    }

    /// <summary>Owns a VR session; no Valve or XR-loader types leak into the main package.</summary>
    public sealed class VrSimulationSession : IDisposable
    {
        private static readonly string[] OwnedParameters = { "VRMode", "TrackingType", "GestureLeft", "GestureRight",
            "GestureLeftWeight", "GestureRightWeight", "Upright", "AFK" };
        private readonly ControlledModeSession _motion;
        private readonly TrackingMixer _mixer = new TrackingMixer();
        private readonly TrackingCalibration _calibration = new TrackingCalibration();
        private readonly Dictionary<string, (Vrc3Param parameter, float value)> _parameters = new Dictionary<string, (Vrc3Param, float)>();
        private readonly Dictionary<Camera, StereoTargetEyeMask> _cameraEyes = new Dictionary<Camera, StereoTargetEyeMask>();
        private readonly Dictionary<string, GameObject> _markers = new Dictionary<string, GameObject>();
        private readonly List<TrackedDevice> _devices = new List<TrackedDevice>();
        private GestureManager _manager;
        private ModuleVrc3 _module;
        private IVrInputSource _source;
        private OscTrackingInputSource _osc;
        private HumanoidPoseSolver _solver;
        private ExpressionMenuController _expressions;
        private VrExpressionMenuView _menu;
        private GameObject _objects;
        private Camera _camera;
        private Material _markerMaterial;
        private VrSimulationSettings _settings;
        private TrackingFrame _frame = TrackingFrame.Empty;
        private Pose _previousRoot;
        private Vector3 _scale;
        private string _spaceId;
        private bool _headChop;
        private float _poseWeight;
        private bool _paused, _stopping, _calibrating, _bothTriggers, _snapHeld, _avatarStateCaptured;
        private MotionInputFrame _sample;

        public bool Running => _source != null && _motion.Running;
        public bool Paused => _paused;
        public bool Calibrating => _calibrating;
        public string Error { get; private set; }
        public string Status => _source?.Status;
        public TrackingFrame Tracking => _frame;
        public IReadOnlyList<TrackedDevice> Devices => _devices;
        public TrackingCalibration Calibration => _calibration;
        public Camera Camera => _camera;

        public VrSimulationSession(ControlledModeSession motion)
        {
            _motion = motion ?? throw new ArgumentNullException(nameof(motion));
            _motion.Stopped += OnMotionStopped;
        }

        public bool Start(GestureManager manager, IVrInputSource source, VrSimulationSettings settings,
            float speed = 2f, float turnSpeed = 120f, bool collisions = true)
        {
            Stop();
            Error = null;
            _source = source;
            _settings = settings ?? new VrSimulationSettings();
            try
            {
                _manager = manager;
                _module = manager ? manager.Module as ModuleVrc3 : null;
                if (_module == null || !_module.AvatarAnimator || !_module.AvatarAnimator.isHuman ||
                    !_module.AvatarAnimator.avatar || !_module.AvatarAnimator.avatar.isValid)
                    throw new InvalidOperationException("VR control requires a valid Humanoid avatar.");
                if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                    throw new InvalidOperationException("Enter Play Mode and resume the editor before enabling controlled mode.");
                _headChop = _module.GlobalHeadChopEnabled;
                _poseWeight = _module.CalibrationPoseWeight;
                _avatarStateCaptured = true;
                if (source == null) throw new InvalidOperationException("Install SteamVR support from Tools > Gesture Manager Pro - VR Setup before starting VR control.");
                if (!source.Start(out var startError)) throw new InvalidOperationException(startError);
                if (_settings.OscEnabled)
                {
                    _osc = new OscTrackingInputSource(_settings.OscPort);
                    if (!_osc.Start(out var oscError)) throw new InvalidOperationException(oscError);
                }
                foreach (var name in OwnedParameters)
                {
                    var parameter = _module.GetParam(name);
                    if (parameter != null) _parameters[name] = (parameter, parameter.FloatValue());
                }
                _module.SetGlobalHeadChop(false);
                Set("VRMode", 1f);
                var adapter = new MotionAdapter(this);
                if (!_motion.Start(manager, adapter, speed, turnSpeed, collisions, gravity: true,
                    mode: ControlledSessionMode.VirtualReality)) throw new InvalidOperationException(_motion.Error);
                _previousRoot = RootPose();
                _scale = _module.Avatar.transform.lossyScale;
                _solver = new HumanoidPoseSolver(_module.AvatarAnimator) { LockMode = _settings.LockMode };
                _objects = new GameObject("Gesture Manager Pro VR Session") { hideFlags = HideFlags.HideAndDontSave };
                var pump = _objects.AddComponent<VrSimulationPump>();
                pump.Tick = Tick;
                var cameraObject = new GameObject("Gesture Manager Pro XR Camera") { hideFlags = HideFlags.HideAndDontSave };
                cameraObject.transform.SetParent(_objects.transform, false);
                _camera = cameraObject.AddComponent<Camera>();
                _camera.stereoTargetEye = StereoTargetEyeMask.Both;
                // Calibration owns the world pose; Unity must not apply the raw HMD pose again.
                UnityEngine.XR.XRDevice.DisableAutoXRCameraTracking(_camera, true);
                _camera.nearClipPlane = 0.01f;
                _camera.farClipPlane = 1000f;
                _camera.depth = 10f;
                foreach (var camera in UnityEngine.Camera.allCameras)
                    if (camera != _camera && camera.cameraType == CameraType.Game)
                    {
                        _cameraEyes[camera] = camera.stereoTargetEye;
                        camera.stereoTargetEye = StereoTargetEyeMask.None;
                    }
                var visibility = cameraObject.AddComponent<VrHeadVisibility>();
                visibility.LocalCamera = _camera;
                visibility.Apply = _module.BeginLocalHeadRender;
                visibility.Restore = _module.EndLocalHeadRender;
                _motion.UseExternalCamera(_camera);
                _expressions = new ExpressionMenuController(_module, _module.AvatarDescriptor.expressionsMenu);
                _menu = new VrExpressionMenuView(_expressions);
                _menu.Calibrate += RequestCalibration;
                _menu.Recenter += Recenter;
                _menu.Pause += TogglePause;
                _menu.Exit += Stop;
                _module.BeforePosePublish += SolvePose;
                _module.Resetting += OnAvatarReset;
                Application.onBeforeRender += UpdateCamera;
                ReadTracking();
                _spaceId = _source.Tracking.SpaceId;
                Set("AFK", _source.InputAvailable ? 0f : 1f);
                Set("TrackingType", TrackingType());
                ConfigureCalibration();
                // A view reference is needed immediately; full-body offsets are confirmed in calibration mode.
                if (!_calibration.Calibrate(_module.AvatarAnimator, ViewPosition(), _frame, out var calibrationError))
                    throw new InvalidOperationException(calibrationError);
                RequestCalibration();
                UpdateCamera();
                return true;
            }
            catch (Exception exception)
            {
                if (!(exception is InvalidOperationException) && !(exception is ArgumentException)) Debug.LogException(exception);
                Error = exception.Message;
                Stop();
                return false;
            }
        }

        public void Assign(string deviceId, TrackingRole role)
        {
            _mixer.Assign(deviceId, role);
            if (Running) RequestCalibration();
        }

        public void OpenBindings() => _source?.OpenBindings();

        public void RequestCalibration()
        {
            if (!Running) return;
            _calibrating = true;
            _bothTriggers = true; // Require release before a new two-trigger confirmation.
            if (!_calibration.Calibrated)
            {
                ConfigureCalibration();
                _calibration.Calibrate(_module.AvatarAnimator, ViewPosition(), _frame, out _);
            }
            _module.SetCalibrationPose(1f);
            _menu?.Close();
            _sample = default;
            _motion.Motor.enabled = false;
            _motion.Motor.Halt();
        }

        public bool ConfirmCalibration()
        {
            if (!Running || !_calibrating) return false;
            ConfigureCalibration();
            if (!_calibration.Calibrate(_module.AvatarAnimator, ViewPosition(), _frame, out var error))
            { Error = error; return false; }
            Error = null;
            _calibrating = false;
            _spaceId = _source.Tracking.SpaceId;
            _previousRoot = RootPose();
            _module.SetCalibrationPose(_poseWeight);
            UpdateMarkers();
            return true;
        }

        public void Recenter()
        {
            if (!Running) return;
            _calibration.Recenter(_frame, ViewPosition());
            _previousRoot = RootPose();
        }

        /// <summary>Moves an active tracking space with the avatar while retaining its calibration offsets.</summary>
        public void Reposition(Pose pose)
        {
            if (!Running) throw new InvalidOperationException("An active VR session is required before repositioning.");
            _motion.Reposition(pose);
            if (!Running) return;
            var root = RootPose();
            _calibration.ApplyLocomotion(_previousRoot, root);
            _previousRoot = root;
            UpdateCamera();
            UpdateMarkers();
        }

        public void TogglePause()
        {
            if (!Running) return;
            _paused = !_paused;
            if (_menu != null) _menu.Paused = _paused;
            _menu?.Close();
            _sample = default;
            _motion.Motor.enabled = false;
            _motion.Motor.Halt();
            if (_paused) _solver.ReleaseTracking();
        }

        private void ConfigureCalibration()
        {
            _calibration.Measurement = _settings.Measurement;
            _calibration.UserHeightOverride = _settings.UserHeight;
            _calibration.UserArmSpanOverride = _settings.UserArmSpan;
        }

        private void ReadTracking()
        {
            _source.Poll();
            if (_source.Tracking.TryGet(TrackingRole.Head, out var head))
            {
                var reference = TrackingMixer.TransformPose(_source.Tracking.Origin, head.Pose);
                // VRChat's OSC head-position reference is the head-bone root, not the eyes.
                if (_calibration.TryGetWorldPose(TrackingRole.Head, _source.Tracking, out var headBone))
                    reference.position = _calibration.UnmapTrackingPose(headBone).position;
                _osc?.SetHeadReference(reference);
            }
            _osc?.Poll();
            _frame = _mixer.Mix(_source.Tracking, _osc?.Tracking ?? TrackingFrame.Empty);
            _devices.Clear();
            foreach (var sourceFrame in new[] { _source.Tracking, _osc?.Tracking ?? TrackingFrame.Empty })
                foreach (var device in sourceFrame.Devices)
                {
                    var role = _mixer.Assignments.TryGetValue(device.Id, out var assigned) ? assigned : device.Role;
                    _devices.Add(new TrackedDevice(device.Id, role, TrackingMixer.TransformPose(sourceFrame.Origin, device.Pose), device.Valid, device.Timestamp));
                }
        }

        public void Tick()
        {
            if (!Running || _stopping) return;
            try
            {
                if (!_objects || !_camera || !_manager || !ReferenceEquals(_manager.Module, _module) ||
                    !_source.Running || _module.IsPoseModeActive || !SameParameters()) { Stop(); return; }
                ReadTracking();
                if (!_calibrating && NeedsCalibration()) RequestCalibration();
                var input = _source.Actions;
                var tracked = _frame.TryGet(TrackingRole.Head, out _);
                var blocked = _paused || _calibrating || !_source.InputAvailable || !tracked;
                _motion.Motor.enabled = !blocked;
                if (blocked) _motion.Motor.Halt();
                _sample = blocked ? default : ReadMotion();
                if (input.RecenterPressed) Recenter();
                var both = input.LeftTrigger > 0.8f && input.RightTrigger > 0.8f;
                if (_calibrating && both && !_bothTriggers) ConfirmCalibration();
                _bothTriggers = both;
                if (_source.InputAvailable && tracked)
                {
                    Set("GestureLeft", input.GestureLeft);
                    Set("GestureRight", input.GestureRight);
                    Set("GestureLeftWeight", input.GestureLeftWeight);
                    Set("GestureRightWeight", input.GestureRightWeight);
                    if (_calibration.TryGetViewPose(_frame, out var view))
                    {
                        var left = GetPose(TrackingRole.LeftHand);
                        var right = GetPose(TrackingRole.RightHand);
                        _menu.Update(input, view, left, right);
                    }
                }
                else _menu.Close();
                if (!Running) return;
                if (_menu.IsOpen) { _sample = default; _motion.Motor.StopHorizontalMotion(); }
                Set("AFK", _source.InputAvailable && tracked ? 0f : 1f);
                Set("TrackingType", TrackingType());
                if (!_calibrating && !_paused && _calibration.TryGetViewPose(_frame, out var headView))
                {
                    var upright = (headView.position.y - _module.Avatar.transform.position.y) /
                        Mathf.Max(0.1f, _calibration.AvatarHeight);
                    Set("Upright", Mathf.Clamp01(upright));
                }
                UpdateMarkers();
            }
            catch (Exception exception) { Error = exception.Message; Stop(); }
        }

        private MotionInputFrame ReadMotion()
        {
            var input = _source.Sample();
            var movement = input.Move;
            if (_calibration.TryGetViewPose(_frame, out var view))
            {
                var relative = Mathf.DeltaAngle(_motion.Motor.transform.eulerAngles.y, view.rotation.eulerAngles.y);
                var direction = Quaternion.Euler(0f, relative, 0f) * new Vector3(movement.x, 0f, movement.y);
                movement = new Vector2(direction.x, direction.z);
            }
            var turn = input.Turn;
            var snap = 0f;
            if (_settings.SnapTurning)
            {
                if (Mathf.Abs(turn) > 0.7f && !_snapHeld) snap = Mathf.Sign(turn) * Mathf.Clamp(_settings.SnapAngle, 15f, 90f);
                if (Mathf.Abs(turn) < 0.3f) _snapHeld = false;
                else if (Mathf.Abs(turn) > 0.7f) _snapHeld = true;
                turn = 0f;
            }
            return new MotionInputFrame(movement, turn, new Vector2(snap, 0f), input.SpeedMultiplier, input.Jump, input.Pose);
        }

        private int TrackingType()
        {
            var hip = _frame.TryGet(TrackingRole.Hip, out _);
            var feet = _frame.TryGet(TrackingRole.LeftFoot, out _) && _frame.TryGet(TrackingRole.RightFoot, out _);
            return hip && feet ? 6 : feet ? 5 : hip ? 4 : 3;
        }

        private bool NeedsCalibration()
        {
            if (_spaceId != _source.Tracking.SpaceId)
            { _spaceId = _source.Tracking.SpaceId; _calibration.Reset(); return true; }
            foreach (var device in _frame.Devices)
            {
                if (!device.Valid || device.Role == TrackingRole.Unassigned) continue;
                var bone = TrackingCalibration.BoneFor(device.Role);
                if (bone != HumanBodyBones.LastBone && _module.AvatarAnimator.GetBoneTransform(bone) &&
                    !_calibration.TryGetWorldPose(device.Role, _frame, out _)) return true;
            }
            return false;
        }

        private bool CanTrack(TrackingRole role)
        {
            var key = role == TrackingRole.LeftHand || role == TrackingRole.LeftElbow ? "Left Hand" :
                role == TrackingRole.RightHand || role == TrackingRole.RightElbow ? "Right Hand" :
                role == TrackingRole.LeftFoot || role == TrackingRole.LeftKnee ? "Left Foot" :
                role == TrackingRole.RightFoot || role == TrackingRole.RightKnee ? "Right Foot" :
                role == TrackingRole.Head ? "Head" : "Hip";
            return !_module.TrackingStates.TryGetValue(key, out var state) || state.ToString() != "Animation";
        }

        private void SolvePose(ModuleVrc3 module)
        {
            if (!Running || _stopping) return;
            try
            {
                var scale = module.Avatar.transform.lossyScale;
                if ((scale - _scale).sqrMagnitude > 0.000001f)
                {
                    _scale = scale;
                    _motion.RefreshAvatarDimensions();
                    _calibration.Reset();
                    RequestCalibration();
                }
                var root = RootPose();
                _calibration.ApplyLocomotion(_previousRoot, root);
                _previousRoot = root;
                if (!_paused && !_calibrating && _source.InputAvailable && _frame.TryGet(TrackingRole.Head, out _))
                {
                    _solver.LockMode = _settings.LockMode;
                    _solver.Solve(_frame, _calibration, CanTrack);
                }
                else _solver.ReleaseTracking();
                UpdateCamera();
            }
            catch (Exception exception) { Error = exception.Message; Stop(); }
        }

        private Pose RootPose() => new Pose(_module.Avatar.transform.position, _module.Avatar.transform.rotation);
        private Vector3 ViewPosition() => _module.AvatarDescriptor.transform.TransformPoint(_module.AvatarDescriptor.ViewPosition);
        private Pose? GetPose(TrackingRole role) => _calibration.TryGetWorldPose(role, _frame, out var pose) ? pose : (Pose?)null;

        private void UpdateCamera()
        {
            if (!Running || !_camera) return;
            if (_calibration.TryGetViewPose(_frame, out var pose)) _camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        private void UpdateMarkers()
        {
            if (!_objects) return;
            foreach (var marker in _markers.Values) if (marker) marker.SetActive(false);
            if (!_calibrating) return;
            foreach (var device in _devices)
            {
                if (!device.Valid) continue;
                if (!_markers.TryGetValue(device.Id, out var marker) || !marker)
                {
                    marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    marker.name = "Tracking: " + device.Id;
                    marker.hideFlags = HideFlags.HideAndDontSave;
                    marker.transform.SetParent(_objects.transform, false);
                    UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
                    if (!_markerMaterial)
                    {
                        var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                        if (shader) _markerMaterial = new Material(shader) { color = Color.green, hideFlags = HideFlags.HideAndDontSave };
                    }
                    if (_markerMaterial) marker.GetComponent<Renderer>().sharedMaterial = _markerMaterial;
                    _markers[device.Id] = marker;
                }
                var position = _calibration.MapTrackingPose(device.Pose).position;
                if (device.Role != TrackingRole.Unassigned && _calibration.TryGetWorldPose(device.Role, _frame, out var target)) position = target.position;
                marker.transform.position = position;
                marker.transform.localScale = Vector3.one * 0.04f;
                marker.SetActive(true);
            }
        }

        private void Set(string name, float value) => _module?.GetParam(name)?.Set(_module, value, this);

        private bool SameParameters()
        {
            foreach (var entry in _parameters)
                if (!ReferenceEquals(_module.GetParam(entry.Key), entry.Value.parameter)) return false;
            return true;
        }

        private void OnMotionStopped() { if (!_stopping) Stop(); }
        private void OnAvatarReset(ModuleVrc3 module) => Stop();

        public void Stop()
        {
            if (_stopping) return;
            _stopping = true;
            try
            {
                Application.onBeforeRender -= UpdateCamera;
                if (_module != null) _module.BeforePosePublish -= SolvePose;
                if (_module != null) _module.Resetting -= OnAvatarReset;
                Cleanup(() => _module?.EndLocalHeadRender());
                Cleanup(() => _menu?.Dispose());
                Cleanup(() => _expressions?.Dispose());
                Cleanup(() => _solver?.Dispose());
                Cleanup(() => _osc?.Dispose());
                Cleanup(_motion.Stop);
                if (_objects) Cleanup(() => UnityEngine.Object.DestroyImmediate(_objects));
                foreach (var camera in _cameraEyes) if (camera.Key) camera.Key.stereoTargetEye = camera.Value;
                _cameraEyes.Clear();
                if (_markerMaterial) Cleanup(() => UnityEngine.Object.DestroyImmediate(_markerMaterial));
                if (_avatarStateCaptured && _module != null && _module.Active && !_module.IsInvalid() && SameParameters())
                {
                    Cleanup(() => _module.SetCalibrationPose(_poseWeight));
                    Cleanup(() => _module.SetGlobalHeadChop(_headChop));
                    foreach (var name in OwnedParameters)
                        if (_parameters.TryGetValue(name, out var parameter) && ReferenceEquals(_module.GetParam(name), parameter.parameter))
                            Cleanup(() => parameter.parameter.Set(_module, parameter.value, this));
                }
                Cleanup(() => _source?.Dispose());
            }
            finally
            {
                _source = null; _osc = null; _solver = null; _menu = null; _expressions = null;
                _module = null; _manager = null; _objects = null; _camera = null; _markerMaterial = null;
                _parameters.Clear(); _markers.Clear(); _calibration.Reset(); _frame = TrackingFrame.Empty;
                _devices.Clear(); _avatarStateCaptured = false;
                _spaceId = null;
                _paused = false; _calibrating = false; _snapHeld = false; _sample = default;
                _stopping = false;
            }
        }

        private void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception exception) { if (string.IsNullOrEmpty(Error)) Error = exception.Message; }
        }

        public void Dispose() { Stop(); _motion.Stopped -= OnMotionStopped; }

        private sealed class MotionAdapter : IMotionInputSource
        {
            private readonly VrSimulationSession _owner;
            public MotionAdapter(VrSimulationSession owner) { _owner = owner; }
            public MotionInputFrame Sample() => _owner._sample;
            public void Reset() { _owner._sample = default; _owner._source?.Reset(); }
        }
    }

}
