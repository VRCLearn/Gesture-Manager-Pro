using System;
using System.Collections.Generic;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Params;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    public enum ControlledSessionMode { Automatic, MotionAdapter, GameSimulation, VirtualReality }

    public sealed class ControlledModeSession : IDisposable, AvatarTools.UpdateSceneCamera.ISimulationView
    {
        private static readonly Dictionary<GestureManager, ControlledModeSession> Owners = new Dictionary<GestureManager, ControlledModeSession>();
        private static ControlledModeSession _gameSimulationOwner;
        private static readonly string[] ParameterNames = { "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude", "AngularY", "Grounded" };
        private readonly Dictionary<string, (Vrc3Param parameter, float value)> _originalParameters = new Dictionary<string, (Vrc3Param, float)>();
        private readonly Dictionary<SceneView, Vector3> _observerOffsets = new Dictionary<SceneView, Vector3>();
        private GestureManager _manager;
        private ModuleVrc3 _module;
        private Transform _avatar;
        private IMotionInputSource _input;
        private GameObject _motorObject;
        private GameObject _cameraObject;
        private GameObject _groundObject;
        private Vector3 _position;
        private Quaternion _rotation;
        private bool _originalControlled;
        private Camera _originalModuleCamera;
        private Camera _ownedCamera;
        private bool _hasPreviewCamera;
        private bool _stopping;
        private float _height;
        private float _cameraDistance = 2f;
        private float _lastThirdPersonDistance = 2f;
        private Vector3 _observerPosition;
        private Vector3 _fallbackObserverOffset;
        private bool _thirdPersonBeforeObserver;
        private float _playerFieldOfView;
        private float _playerFarClipPlane;
        private float _playerOrthographicSize;

        public CharacterMotionMotor Motor { get; private set; }
        public MotionPreviewCamera ViewCamera { get; private set; }
        public SimulationViewMode ViewMode { get; private set; } = SimulationViewMode.SceneCamera;
        public bool FollowObserverCamera { get; private set; }
        public bool ThirdPersonEnabled { get; private set; }
        public float CameraDistance => _cameraDistance;
        public bool SceneCameraEnabled
        {
            get => ViewMode == SimulationViewMode.SceneCamera;
            set
            {
                if (!Running || value == SceneCameraEnabled) return;
                if (value) SetViewMode(SimulationViewMode.SceneCamera);
                else
                {
                    ThirdPersonEnabled = _thirdPersonBeforeObserver;
                    ApplyViewMode(ThirdPersonEnabled && _cameraDistance > 0f
                        ? SimulationViewMode.ThirdPerson : SimulationViewMode.FirstPerson);
                    if (_input is KeyboardMotionInputSource keyboard) keyboard.RequestCapture();
                }
            }
        }
        public bool Running { get; private set; }
        public ControlledSessionMode Mode { get; private set; }
        public string Error { get; private set; }
        public event Action Stopped;
        public bool LocomotionBlocked => _module != null && (_module.IsLocomotionDisabled || _module.IsPoseSpaceActive || _module.IsPoseModeActive || _module.IsDummyModeActive);

        public bool Start(GestureManager manager, IMotionInputSource input, float speed = 2f, float turnSpeed = 120f,
            bool collisions = true, bool gravity = false, bool previewCamera = false,
            SimulationViewMode viewMode = SimulationViewMode.SceneCamera, float cameraDistance = 2f,
            bool followObserverCamera = false, ControlledSessionMode mode = ControlledSessionMode.Automatic)
        {
            if (_stopping) return false;
            Stop();
            Error = null;
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                    throw new InvalidOperationException("Enter Play Mode and resume the editor before enabling controlled mode.");
                var module = manager ? manager.Module as ModuleVrc3 : null;
                if (!manager || !manager.isActiveAndEnabled || module == null || !module.Active || module.IsInvalid())
                    throw new InvalidOperationException("Select an active Gesture Manager Pro controlling an SDK3 avatar.");
                if (input == null) throw new ArgumentNullException(nameof(input));
                if (input is KeyboardMotionInputSource) KeyboardMotionInputSource.EnsureAvailable();
                Mode = mode == ControlledSessionMode.Automatic
                    ? (input is KeyboardMotionInputSource keyboard && keyboard.GameSimulation
                        ? ControlledSessionMode.GameSimulation : ControlledSessionMode.MotionAdapter) : mode;
                var gameSimulation = Mode == ControlledSessionMode.GameSimulation || Mode == ControlledSessionMode.VirtualReality;
                if (gameSimulation && _gameSimulationOwner != null && _gameSimulationOwner.Running)
                    throw new InvalidOperationException("Another game simulation is already active.");
                if (Owners.ContainsKey(manager) || manager.IsPlayerControlled)
                    throw new InvalidOperationException("This manager is already owned by another motion controller.");
                foreach (var owner in Owners.Values)
                    if (owner._avatar == module.Avatar.transform)
                        throw new InvalidOperationException("This avatar is already owned by another motion controller.");
                if (module.IsDummyModeActive || module.PlayingCustomAnimation)
                    throw new InvalidOperationException("Stop custom animation and dummy modes before enabling controlled mode.");
                foreach (var name in ParameterNames)
                {
                    var parameter = module.GetParam(name);
                    if (parameter == null) throw new InvalidOperationException("The simulator has not initialized its motion parameters yet.");
                    _originalParameters[name] = (parameter, parameter.FloatValue());
                }
                _manager = manager;
                _module = module;
                _avatar = module.Avatar.transform;
                _position = _avatar.position;
                _observerPosition = _position;
                _rotation = _avatar.rotation;
                _originalControlled = manager.IsPlayerControlled;
                _input = input;
                if (gameSimulation) _gameSimulationOwner = this;
                if (input is KeyboardMotionInputSource keyboardInput)
                {
                    keyboardInput.ViewToggleRequested += ToggleThirdPerson;
                    keyboardInput.CameraZoomRequested += AdjustCameraDistance;
                    keyboardInput.FreeLookRequested += EnableFreeLook;
                    keyboardInput.ForwardMovementRequested += ResumeCameraFollow;
                }
                Owners.Add(manager, this);
                manager.IsPlayerControlled = true;
                var height = CapsuleHeight(module);
                _height = height;
                _thirdPersonBeforeObserver = false;
                SetObserverCameraFollow(followObserverCamera);
                SetViewMode(viewMode);
                SetCameraDistance(cameraDistance);
                var radius = Mathf.Clamp(height * 0.12f, 0.05f, 0.5f);
                _motorObject = TemporaryObject("Gesture Manager Pro Motion", _avatar.gameObject.scene);
                Motor = _motorObject.AddComponent<CharacterMotionMotor>();
                Motor.MoveSpeed = speed;
                Motor.TurnSpeed = turnSpeed;
                Motor.Collisions = collisions;
                Motor.UseGravity = gravity && collisions;
                Motor.Failed += OnInputFailed;
                Motor.PoseRequested += TogglePose;
                Running = true;
                Motor.Initialize(_avatar, input, height, radius, 0f, CanMove, Publish);
                if (previewCamera || gameSimulation)
                {
                    CreateGroundIfNeeded();
                    if (Mode != ControlledSessionMode.VirtualReality) CreatePreviewCamera(height);
                }
                if (gameSimulation)
                {
                    if (AvatarTools.UpdateSceneCamera.SimulationView != null)
                        throw new InvalidOperationException("Another game simulation is already active.");
                    AvatarTools.UpdateSceneCamera.SimulationView = this;
                }
                EditorApplication.update += Poll;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                EditorApplication.pauseStateChanged += OnPauseChanged;
                AssemblyReloadEvents.beforeAssemblyReload += Stop;
                return true;
            }
            catch (Exception exception)
            {
                if (!(exception is InvalidOperationException) && !(exception is ArgumentException)) Debug.LogException(exception);
                Error = Message(exception);
                Stop();
                return false;
            }
        }

        public void Poll()
        {
            if (!Running) return;
            if (!IsValid() || EditorApplication.isPaused || _module.IsDummyModeActive || _module.PlayingCustomAnimation)
            {
                Stop();
                return;
            }
            if (_input is KeyboardMotionInputSource keyboard && !keyboard.HasInputFocus)
            {
                _input.Reset();
                Motor.Halt();
            }
            UpdateObserverView();
        }

        private bool IsValid() => Running && EditorApplication.isPlaying && !EditorApplication.isPaused &&
            _manager && _manager.isActiveAndEnabled && ReferenceEquals(_manager.Module, _module) &&
            _module.Active && !_module.IsInvalid() && _avatar && Motor &&
            (ReferenceEquals(_groundObject, null) || _groundObject) &&
            (!_hasPreviewCamera || (_ownedCamera && ViewCamera));

        private bool CanMove() => IsValid() && !LocomotionBlocked &&
            (!(_input is KeyboardMotionInputSource keyboard) || !keyboard.Suspended);

        private void Publish(MotionResult result)
        {
            if (!IsValid()) return;
            if (ViewCamera)
            {
                ViewCamera.Pitch = Motor.ViewPitch;
                ViewCamera.YawOverride = Motor.FreeLook ? Motor.ViewYaw : (float?)null;
            }
            Set("VelocityX", result.LocalVelocity.x);
            Set("VelocityY", result.LocalVelocity.y);
            Set("VelocityZ", result.LocalVelocity.z);
            Set("VelocityMagnitude", result.LocalVelocity.magnitude);
            Set("AngularY", result.AngularDegreesPerSecond);
            Set("Grounded", result.Grounded ? 1f : 0f);
            UpdateObserverView();
        }

        private void TogglePose(MotionPose pose)
        {
            if (!IsValid()) return;
            var parameter = _module.GetParam("Upright");
            if (parameter == null) return;
            if (!_originalParameters.ContainsKey("Upright"))
                _originalParameters["Upright"] = (parameter, parameter.FloatValue());
            var upright = pose == MotionPose.Crouch ? 0.6f : 0.35f;
            var current = parameter.FloatValue();
            var selected = pose == MotionPose.Crouch ? current > 0.475f && current <= 0.8f : current <= 0.475f;
            parameter.Set(_module, selected ? 1f : upright, this);
        }

        private void Set(string name, float value) => _module.GetParam(name)?.Set(_module, value, this);

        public void SetViewMode(SimulationViewMode mode)
        {
            if (Mode == ControlledSessionMode.VirtualReality) return;
            var selected = mode == SimulationViewMode.FirstPerson || mode == SimulationViewMode.ThirdPerson
                ? mode : SimulationViewMode.SceneCamera;
            if (selected == SimulationViewMode.SceneCamera && ViewMode != SimulationViewMode.SceneCamera)
                _thirdPersonBeforeObserver = ThirdPersonEnabled;
            ThirdPersonEnabled = selected == SimulationViewMode.ThirdPerson;
            if (ThirdPersonEnabled && _cameraDistance == 0f) _cameraDistance = _lastThirdPersonDistance;
            ApplyViewMode(selected);
        }

        private void ApplyViewMode(SimulationViewMode mode)
        {
            if (mode != SimulationViewMode.ThirdPerson) ResetFreeLook();
            var wasObserver = ViewMode == SimulationViewMode.SceneCamera;
            if (!wasObserver && mode == SimulationViewMode.SceneCamera && _ownedCamera)
                CapturePlayerProjection();
            ViewMode = mode;
            if (_input is KeyboardMotionInputSource keyboard) keyboard.ObserverView = ViewMode == SimulationViewMode.SceneCamera;
            if (!ViewCamera) return;
            ViewCamera.Mode = ViewMode;
            ViewCamera.Distance = _height * _cameraDistance;
            if (wasObserver && ViewMode != SimulationViewMode.SceneCamera)
            {
                _ownedCamera.orthographic = false;
                _ownedCamera.orthographicSize = _playerOrthographicSize;
                _ownedCamera.fieldOfView = _playerFieldOfView;
                _ownedCamera.farClipPlane = _playerFarClipPlane;
            }
            ViewCamera.ApplyView();
            SyncObserverCamera();
        }

        public void SetObserverCameraFollow(bool enabled)
        {
            if (FollowObserverCamera == enabled) return;
            FollowObserverCamera = enabled;
            if (_avatar) _observerPosition = _avatar.position;
        }

        private void UpdateObserverView()
        {
            if (!_avatar) return;
            var delta = _avatar.position - _observerPosition;
            _observerPosition = _avatar.position;
            var view = SceneView.lastActiveSceneView;
            if (FollowObserverCamera && delta.sqrMagnitude > 0f)
            {
                if (view)
                {
                    view.pivot += delta;
                    _observerOffsets.TryGetValue(view, out var offset);
                    _observerOffsets[view] = offset + delta;
                    view.Repaint();
                }
                else
                {
                    _fallbackObserverOffset += delta;
                    if (_ownedCamera && ViewMode == SimulationViewMode.SceneCamera) _ownedCamera.transform.position += delta;
                }
            }
            SyncObserverCamera();
        }

        private void SyncObserverCamera()
        {
            if (!_ownedCamera || ViewMode != SimulationViewMode.SceneCamera) return;
            var view = SceneView.lastActiveSceneView;
            var source = view && view.camera ? view.camera : _originalModuleCamera;
            if (!source)
                foreach (var camera in Camera.allCameras)
                    if (camera != _ownedCamera && camera.cameraType == CameraType.Game && camera.targetDisplay == 0 &&
                        !camera.targetTexture && (camera.gameObject.hideFlags & HideFlags.DontSave) == 0)
                    {
                        source = camera;
                        break;
                    }
            if (!source || source == _ownedCamera) return;
            var position = source.transform.position + (view ? Vector3.zero : _fallbackObserverOffset);
            _ownedCamera.transform.SetPositionAndRotation(position, source.transform.rotation);
            _ownedCamera.orthographic = source.orthographic;
            _ownedCamera.orthographicSize = source.orthographicSize;
            _ownedCamera.fieldOfView = source.fieldOfView;
            _ownedCamera.nearClipPlane = source.nearClipPlane;
            _ownedCamera.farClipPlane = source.farClipPlane;
        }

        public void SetCameraDistance(float avatarHeights)
        {
            _cameraDistance = float.IsNaN(avatarHeights) || float.IsInfinity(avatarHeights)
                ? 2f : Mathf.Clamp(avatarHeights, 0f, 6f);
            if (_cameraDistance <= 0.05f) _cameraDistance = 0f;
            if (_cameraDistance > 0f) _lastThirdPersonDistance = _cameraDistance;
            if (ViewCamera) ViewCamera.Distance = _height * _cameraDistance;
            if (ThirdPersonEnabled && ViewMode != SimulationViewMode.SceneCamera)
                ApplyViewMode(_cameraDistance == 0f ? SimulationViewMode.FirstPerson : SimulationViewMode.ThirdPerson);
        }

        public void AdjustCameraDistance(float scrollDelta)
        {
            if (!Running || !ThirdPersonEnabled || ViewMode == SimulationViewMode.SceneCamera ||
                float.IsNaN(scrollDelta) || float.IsInfinity(scrollDelta) || scrollDelta == 0f) return;
            // Editor tuning in avatar heights per wheel unit; not a measured client zoom curve.
            const float wheelStep = 0.25f;
            SetCameraDistance(_cameraDistance - scrollDelta * wheelStep);
        }

        private void ToggleThirdPerson()
        {
            if (!IsValid()) return;
            if (ThirdPersonEnabled)
            {
                ThirdPersonEnabled = false;
                ApplyViewMode(SimulationViewMode.FirstPerson);
                return;
            }
            var wasObserver = ViewMode == SimulationViewMode.SceneCamera;
            SetViewMode(SimulationViewMode.ThirdPerson);
            if (wasObserver && _input is KeyboardMotionInputSource keyboard) keyboard.RequestCapture();
        }

        private void EnableFreeLook()
        {
            if (!IsValid() || LocomotionBlocked || ViewMode != SimulationViewMode.ThirdPerson || !ThirdPersonEnabled) return;
            Motor.EnableFreeLook();
            if (_input is KeyboardMotionInputSource keyboard) keyboard.FreeLook = Motor.FreeLook;
            if (ViewCamera) ViewCamera.YawOverride = Motor.ViewYaw;
        }

        private void ResumeCameraFollow()
        {
            if (!IsValid() || LocomotionBlocked || !Motor.FreeLook) return;
            Motor.DisableFreeLook(alignToView: true);
            if (_input is KeyboardMotionInputSource keyboard) keyboard.FreeLook = false;
        }

        private void ResetFreeLook()
        {
            if (Motor) Motor.DisableFreeLook();
            if (_input is KeyboardMotionInputSource keyboard) keyboard.FreeLook = false;
            if (ViewCamera) ViewCamera.YawOverride = null;
        }

        public void Stop()
        {
            if (_stopping) return;
            var wasRunning = Running;
            _stopping = true;
            try
            {
                Running = false;
                EditorApplication.update -= Poll;
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                EditorApplication.pauseStateChanged -= OnPauseChanged;
                AssemblyReloadEvents.beforeAssemblyReload -= Stop;
                if (_input is KeyboardMotionInputSource keyboard)
                {
                    keyboard.ViewToggleRequested -= ToggleThirdPerson;
                    keyboard.CameraZoomRequested -= AdjustCameraDistance;
                    keyboard.FreeLookRequested -= EnableFreeLook;
                    keyboard.ForwardMovementRequested -= ResumeCameraFollow;
                }
                TryRestore(() => _input?.Reset());
                if (_input is KeyboardMotionInputSource ownedKeyboard) TryRestore(ownedKeyboard.Dispose);
                if (Motor)
                {
                    Motor.Failed -= OnInputFailed;
                    Motor.PoseRequested -= TogglePose;
                }
                if (_module != null && !ReferenceEquals(_ownedCamera, null) && ReferenceEquals(_module.SimulationCamera, _ownedCamera))
                    _module.SimulationCamera = _originalModuleCamera;
                if (_motorObject) TryRestore(() => UnityEngine.Object.DestroyImmediate(_motorObject));
                if (_cameraObject) TryRestore(() => UnityEngine.Object.DestroyImmediate(_cameraObject));
                if (_groundObject) TryRestore(() => UnityEngine.Object.DestroyImmediate(_groundObject));
                foreach (var entry in _observerOffsets)
                    if (entry.Key) TryRestore(() => { entry.Key.pivot -= entry.Value; entry.Key.Repaint(); });
                if (_avatar) TryRestore(() => _avatar.SetPositionAndRotation(_position, _rotation));
                if (_module != null && _module.Active && !_module.IsInvalid())
                    foreach (var entry in _originalParameters)
                        if (ReferenceEquals(_module.GetParam(entry.Key), entry.Value.parameter))
                            TryRestore(() => entry.Value.parameter.Set(_module, entry.Value.value, this));
            }
            finally
            {
                if (!ReferenceEquals(_manager, null))
                {
                    if (_manager) _manager.IsPlayerControlled = _originalControlled;
                    if (Owners.TryGetValue(_manager, out var owner) && ReferenceEquals(owner, this)) Owners.Remove(_manager);
                }
                _originalParameters.Clear();
                _observerOffsets.Clear();
                _fallbackObserverOffset = Vector3.zero;
                _manager = null;
                _module = null;
                _avatar = null;
                _input = null;
                _motorObject = null;
                _cameraObject = null;
                _groundObject = null;
                ViewCamera = null;
                _originalModuleCamera = null;
                _ownedCamera = null;
                _hasPreviewCamera = false;
                ThirdPersonEnabled = false;
                if (ReferenceEquals(AvatarTools.UpdateSceneCamera.SimulationView, this))
                    AvatarTools.UpdateSceneCamera.SimulationView = null;
                if (ReferenceEquals(_gameSimulationOwner, this)) _gameSimulationOwner = null;
                Motor = null;
                if (wasRunning) TryRestore(() => Stopped?.Invoke());
                _stopping = false;
            }
        }

        private void TryRestore(Action restore)
        {
            try { restore(); }
            catch (Exception exception) { if (string.IsNullOrEmpty(Error)) Error = Message(exception); }
        }

        public void Dispose() => Stop();

        public void UseExternalCamera(Camera camera)
        {
            if (!Running || Mode != ControlledSessionMode.VirtualReality || !camera)
                throw new InvalidOperationException("An active VR session and a camera are required.");
            _originalModuleCamera = _module.SimulationCamera;
            _ownedCamera = camera;
            _module.SimulationCamera = camera;
            ViewMode = SimulationViewMode.FirstPerson;
        }

        /// <summary>Preserves a backend handoff position without changing the session's exit restore pose.</summary>
        public void Reposition(Pose pose)
        {
            if (!Running || !Motor || !_avatar)
                throw new InvalidOperationException("An active controlled session is required before repositioning.");
            // Halt publishes synchronously; the handoff must not count as observer-follow movement.
            var observerPosition = _observerPosition;
            _observerPosition = pose.position;
            try { Motor.Reposition(pose); }
            catch { _observerPosition = observerPosition; throw; }
            // Publishing parameters may synchronously stop and destroy this session.
            if (!Running || !Motor || !_avatar) return;
            if (_input is KeyboardMotionInputSource keyboard) keyboard.FreeLook = false;
            if (ViewCamera)
            {
                ViewCamera.Pitch = Motor.ViewPitch;
                ViewCamera.YawOverride = null;
                ViewCamera.ApplyView();
            }
            SyncObserverCamera();
        }

        public void RefreshAvatarDimensions()
        {
            if (!Running || !Motor || _module == null) return;
            _height = CapsuleHeight(_module);
            Motor.Resize(_height, Mathf.Clamp(_height * 0.12f, 0.05f, 0.5f));
        }
        private void OnPlayModeChanged(PlayModeStateChange state) { if (state != PlayModeStateChange.EnteredPlayMode) Stop(); }
        private void OnPauseChanged(PauseState state) { if (state == PauseState.Paused) Stop(); }
        private void OnInputFailed(Exception exception) { Error = Message(exception); Stop(); }
        private static string Message(Exception exception) => exception is InvalidOperationException && exception.Message.Contains("Input System")
            ? "Keyboard control requires Active Input Handling set to Input Manager (Old) or Both."
            : exception.Message;

        private static GameObject TemporaryObject(string name, Scene scene)
        {
            var gameObject = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private void CreateGroundIfNeeded()
        {
            if (Motor.LastResult.Grounded) return;
            const float size = 100f;
            const float thickness = 0.1f;
            _groundObject = TemporaryObject("Gesture Manager Pro Ground", _avatar.gameObject.scene);
            _groundObject.transform.SetPositionAndRotation(_position - Vector3.up * (thickness * 0.5f), Quaternion.identity);
            _groundObject.transform.localScale = new Vector3(size, thickness, size);
            _groundObject.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            Motor.Halt();
        }

        private static float CapsuleHeight(ModuleVrc3 module)
        {
            var avatar = module.Avatar.transform;
            var eyeHeight = module.AvatarDescriptor.transform.TransformPoint(module.AvatarDescriptor.ViewPosition).y - avatar.position.y;
            if (eyeHeight > 0.1f) return Mathf.Clamp(eyeHeight / 0.9f, 0.2f, 10f);
            var height = 0f;
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>())
                if (renderer.enabled) height = Mathf.Max(height, renderer.bounds.max.y - avatar.position.y);
            return height > 0.1f ? Mathf.Clamp(height, 0.2f, 10f) : 1.8f;
        }

        private void CapturePlayerProjection()
        {
            _playerFieldOfView = _ownedCamera.fieldOfView;
            _playerFarClipPlane = _ownedCamera.farClipPlane;
            _playerOrthographicSize = _ownedCamera.orthographicSize;
        }

        private void CreatePreviewCamera(float height)
        {
            _cameraObject = TemporaryObject("Gesture Manager Pro Preview Camera", _avatar.gameObject.scene);
            var camera = _cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = Mathf.Clamp(height * 0.02f, 0.01f, 0.1f);
            camera.farClipPlane = 1000f;
            camera.depth = 1f;
            foreach (var existing in Camera.allCameras)
                if (existing != camera) camera.depth = Mathf.Max(camera.depth, existing.depth + 1f);
            var follow = _cameraObject.AddComponent<MotionPreviewCamera>();
            follow.Target = _avatar;
            follow.Height = height;
            follow.ViewPosition = _avatar.InverseTransformPoint(
                _module.AvatarDescriptor.transform.TransformPoint(_module.AvatarDescriptor.ViewPosition));
            if (float.IsNaN(follow.ViewPosition.x) || float.IsNaN(follow.ViewPosition.y) || float.IsNaN(follow.ViewPosition.z) ||
                float.IsInfinity(follow.ViewPosition.x) || float.IsInfinity(follow.ViewPosition.y) || float.IsInfinity(follow.ViewPosition.z))
                follow.ViewPosition = _avatar.InverseTransformPoint(_avatar.position + Vector3.up * (height * 0.9f));
            follow.IgnoredCollider = Motor.Controller;
            follow.Mode = ViewMode == SimulationViewMode.SceneCamera ? SimulationViewMode.ThirdPerson : ViewMode;
            follow.Distance = height * _cameraDistance;
            follow.Pitch = Motor.ViewPitch;
            ViewCamera = follow;
            _originalModuleCamera = _module.SimulationCamera;
            _ownedCamera = camera;
            _module.SimulationCamera = camera;
            _hasPreviewCamera = true;
            follow.ApplyView();
            CapturePlayerProjection();
            follow.Mode = ViewMode;
            SyncObserverCamera();
        }
    }
}
