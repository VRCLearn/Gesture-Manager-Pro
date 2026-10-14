using System;
using System.Collections.Generic;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Params;
using VRCLearn.GestureManagerPro.Modules;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    public sealed class ControlledModeSession : IDisposable
    {
        private static readonly Dictionary<GestureManager, ControlledModeSession> Owners = new Dictionary<GestureManager, ControlledModeSession>();
        private static ControlledModeSession _gameSimulationOwner;
        private static readonly string[] ParameterNames = { "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude", "AngularY", "Grounded", "VRMode", "TrackingType", "Upright" };
        private readonly Dictionary<string, (Vrc3Param parameter, float value)> _originalParameters = new Dictionary<string, (Vrc3Param, float)>();
        private GestureManager _manager;
        private ModuleVrc3 _module;
        private Transform _avatar;
        private IMotionInputSource _input;
        private GameObject _motorObject;
        private GameObject _cameraObject;
        private Vector3 _position;
        private Quaternion _rotation;
        private bool _originalControlled;
        private ModuleSettings _settings;
        private bool _originalVrMode;
        private Camera _originalModuleCamera;
        private Camera _ownedCamera;
        private bool _hasPreviewCamera;
        private bool _stopping;
        private float _height;
        private float _cameraDistance = 2f;

        public CharacterMotionMotor Motor { get; private set; }
        public MotionPreviewCamera ViewCamera { get; private set; }
        public SimulationViewMode ViewMode { get; private set; } = SimulationViewMode.ThirdPerson;
        public bool Running { get; private set; }
        public string Error { get; private set; }
        public bool LocomotionBlocked => _module != null && (_module.IsLocomotionDisabled || _module.IsPoseSpaceActive || _module.IsPoseModeActive || _module.IsDummyModeActive);

        public bool Start(GestureManager manager, IMotionInputSource input, float speed = 2f, float turnSpeed = 120f,
            bool collisions = true, bool gravity = false, bool previewCamera = false,
            SimulationViewMode viewMode = SimulationViewMode.ThirdPerson, float cameraDistance = 2f)
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
                var gameSimulation = input is KeyboardMotionInputSource keyboard && keyboard.GameSimulation;
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
                _rotation = _avatar.rotation;
                _originalControlled = manager.IsPlayerControlled;
                _settings = module.Settings;
                _originalVrMode = _settings != null && _settings.vrMode;
                _input = input;
                if (gameSimulation) _gameSimulationOwner = this;
                if (input is KeyboardMotionInputSource keyboardInput) keyboardInput.ViewToggleRequested += ToggleView;
                Owners.Add(manager, this);
                manager.IsPlayerControlled = true;
                var height = CapsuleHeight(module);
                _height = height;
                SetViewMode(viewMode);
                SetCameraDistance(cameraDistance);
                var radius = Mathf.Clamp(height * 0.12f, 0.05f, 0.5f);
                _motorObject = TemporaryObject("Gesture Manager Pro Motion", _avatar.gameObject.scene);
                Motor = _motorObject.AddComponent<CharacterMotionMotor>();
                Motor.MoveSpeed = speed;
                Motor.TurnSpeed = turnSpeed;
                Motor.Collisions = collisions;
                Motor.UseGravity = gravity && collisions;
                Motor.JumpHeight = height * 0.55f;
                Motor.Failed += OnInputFailed;
                Running = true;
                Motor.Initialize(_avatar, input, height, radius, 0f, CanMove, Publish);
                if (previewCamera || gameSimulation) CreatePreviewCamera(height);
                EditorApplication.update += Poll;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                EditorApplication.pauseStateChanged += OnPauseChanged;
                AssemblyReloadEvents.beforeAssemblyReload += Stop;
                return true;
            }
            catch (Exception exception)
            {
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
        }

        private bool IsValid() => Running && EditorApplication.isPlaying && !EditorApplication.isPaused &&
            _manager && _manager.isActiveAndEnabled && ReferenceEquals(_manager.Module, _module) &&
            _module.Active && !_module.IsInvalid() && _avatar && Motor &&
            (!_hasPreviewCamera || (_ownedCamera && ViewCamera));

        private bool CanMove() => IsValid() && !LocomotionBlocked &&
            (!(_input is KeyboardMotionInputSource keyboard) || !keyboard.Suspended);

        private void Publish(MotionResult result)
        {
            if (!IsValid()) return;
            if (ViewCamera) ViewCamera.Pitch = Motor.ViewPitch;
            Set("VelocityX", result.LocalVelocity.x);
            Set("VelocityY", result.LocalVelocity.y);
            Set("VelocityZ", result.LocalVelocity.z);
            Set("VelocityMagnitude", result.LocalVelocity.magnitude);
            Set("AngularY", result.AngularDegreesPerSecond);
            Set("Grounded", result.Grounded ? 1f : 0f);
            Set("VRMode", 0f);
            Set("TrackingType", _module.AvatarAnimator.isHuman ? 3f : 1f);
            Set("Upright", 1f);
        }

        private void Set(string name, float value) => _module.GetParam(name)?.Set(_module, value, this);

        public void SetViewMode(SimulationViewMode mode)
        {
            ViewMode = mode == SimulationViewMode.FirstPerson ? mode : SimulationViewMode.ThirdPerson;
            if (!ViewCamera) return;
            ViewCamera.Mode = ViewMode;
            ViewCamera.ApplyView();
        }

        public void SetCameraDistance(float avatarHeights)
        {
            _cameraDistance = float.IsNaN(avatarHeights) || float.IsInfinity(avatarHeights)
                ? 2f : Mathf.Clamp(avatarHeights, 0.5f, 6f);
            if (ViewCamera) ViewCamera.Distance = _height * _cameraDistance;
        }

        private void ToggleView() => SetViewMode(ViewMode == SimulationViewMode.FirstPerson
            ? SimulationViewMode.ThirdPerson : SimulationViewMode.FirstPerson);

        public void Stop()
        {
            if (_stopping) return;
            _stopping = true;
            try
            {
                Running = false;
                EditorApplication.update -= Poll;
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                EditorApplication.pauseStateChanged -= OnPauseChanged;
                AssemblyReloadEvents.beforeAssemblyReload -= Stop;
                if (_input is KeyboardMotionInputSource keyboard) keyboard.ViewToggleRequested -= ToggleView;
                TryRestore(() => _input?.Reset());
                if (_input is KeyboardMotionInputSource ownedKeyboard) TryRestore(ownedKeyboard.Dispose);
                if (Motor) Motor.Failed -= OnInputFailed;
                if (_module != null && !ReferenceEquals(_ownedCamera, null) && ReferenceEquals(_module.SimulationCamera, _ownedCamera))
                    _module.SimulationCamera = _originalModuleCamera;
                if (_motorObject) TryRestore(() => UnityEngine.Object.DestroyImmediate(_motorObject));
                if (_cameraObject) TryRestore(() => UnityEngine.Object.DestroyImmediate(_cameraObject));
                if (_avatar) TryRestore(() => _avatar.SetPositionAndRotation(_position, _rotation));
                if (_module != null && _module.Active && !_module.IsInvalid())
                    foreach (var entry in _originalParameters)
                        if (ReferenceEquals(_module.GetParam(entry.Key), entry.Value.parameter))
                            TryRestore(() => entry.Value.parameter.Set(_module, entry.Value.value, this));
                if (_settings != null) _settings.vrMode = _originalVrMode;
            }
            finally
            {
                if (!ReferenceEquals(_manager, null))
                {
                    if (_manager) _manager.IsPlayerControlled = _originalControlled;
                    if (Owners.TryGetValue(_manager, out var owner) && ReferenceEquals(owner, this)) Owners.Remove(_manager);
                }
                _originalParameters.Clear();
                _manager = null;
                _module = null;
                _avatar = null;
                _input = null;
                _settings = null;
                _motorObject = null;
                _cameraObject = null;
                ViewCamera = null;
                _originalModuleCamera = null;
                _ownedCamera = null;
                _hasPreviewCamera = false;
                if (ReferenceEquals(_gameSimulationOwner, this)) _gameSimulationOwner = null;
                Motor = null;
                _stopping = false;
            }
        }

        private void TryRestore(Action restore)
        {
            try { restore(); }
            catch (Exception exception) { if (string.IsNullOrEmpty(Error)) Error = Message(exception); }
        }

        public void Dispose() => Stop();
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
            follow.Mode = ViewMode;
            follow.Distance = height * _cameraDistance;
            follow.Pitch = Motor.ViewPitch;
            ViewCamera = follow;
            _originalModuleCamera = _module.SimulationCamera;
            _ownedCamera = camera;
            _module.SimulationCamera = camera;
            _hasPreviewCamera = true;
            follow.ApplyView();
        }
    }
}
