using System;
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools;
using VRCLearn.GestureManagerPro.Extensions.Editor;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    public sealed class GameSimulationTool : GmgDynamicFunction
    {
        private readonly GestureManager _manager;
        private readonly ControlledModeSession _session = new ControlledModeSession();
        private readonly MicrophoneLipSync _lipSync = new MicrophoneLipSync();
        private KeyboardMotionInputSource _keyboard;
        private float _speed = 2f;
        private float _turnSpeed = 120f;
        private bool _collisions = true;
        private float _lookSensitivity = 2f;
        private SimulationViewMode _viewMode = SimulationViewMode.SceneCamera;
        private bool _followObserverCamera;
        private float _cameraDistance = 2f;
        private bool _showControls;
        private readonly VrSimulationSession _vr;
        private readonly VrSimulationSettings _vrSettings = new VrSimulationSettings();
        // Desktop input is the default for each new simulator; VR is chosen explicitly.
        private int _inputMode;
        private bool _switching;
        private string _inputError;

        protected override string Name => EditorLocalization.Tr("Game Simulation");
        protected override Texture2D Icon => Resources.Load<Texture2D>("GestureManagerPro/Vrc3/BSX_GM_Velocity");
        protected override string Description => EditorLocalization.Tr("Control the avatar with PC or VR input.");
        protected override bool Active => _session.Running;

        [InitializeOnLoadMethod]
        private static void Register() => AvatarTools.RegisterTool(manager => new GameSimulationTool(manager));

        public GameSimulationTool(GestureManager manager)
        {
            _manager = manager;
            _vr = new VrSimulationSession(_session);
            _session.Stopped += _lipSync.Stop;
        }

        protected override void Gui(ModuleVrc3 module)
        {
            using (new EditorGUI.DisabledScope(_switching))
            {
                var inputMode = EditorGUILayout.Popup(EditorLocalization.Tr("Input Source"), _inputMode,
                    new[] { EditorLocalization.Tr("PC (Keyboard and Mouse)"), EditorLocalization.Tr("VR (SteamVR)") });
                if (inputMode != _inputMode)
                {
                    SetInputMode(module, inputMode);
                    GUIUtility.ExitGUI();
                }
            }
            using (new EditorGUI.DisabledScope(!Active && (!EditorApplication.isPlaying || EditorApplication.isPaused)))
                if (GmgLayoutHelper.DebugButton(EditorLocalization.Tr(Active ? "Exit and Restore" : "Enter Game Simulation"))) Toggle(module);
            var error = InputError();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(EditorLocalization.Tr(error), MessageType.Error);
            if (_inputMode == 1) { DrawVr(module); return; }
            using (new EditorGUI.DisabledScope(Active))
                _collisions = EditorGUILayout.Toggle(EditorLocalization.Tr("Scene Collisions"), _collisions);
            if (Active)
            {
                _viewMode = _session.ViewMode;
                _followObserverCamera = _session.FollowObserverCamera;
                _cameraDistance = _session.CameraDistance;
            }
            var modes = new[] { SimulationViewMode.SceneCamera, SimulationViewMode.FirstPerson, SimulationViewMode.ThirdPerson };
            var view = EditorGUILayout.Popup(EditorLocalization.Tr("View"), Array.IndexOf(modes, _viewMode),
                new[] { EditorLocalization.Tr("Scene Camera"), EditorLocalization.Tr("First Person"), EditorLocalization.Tr("Third Person") });
            var selectedView = modes[view];
            if (selectedView != _viewMode)
            {
                _viewMode = selectedView;
                if (Active) _session.SetViewMode(_viewMode);
            }
            var followObserver = EditorGUILayout.Toggle(new GUIContent(EditorLocalization.Tr("Follow Avatar in Observer View"),
                EditorLocalization.Tr("Move the Scene view and its Game-view camera with the avatar. Rotation and zoom stay under your control.")), _followObserverCamera);
            if (followObserver != _followObserverCamera)
            {
                _followObserverCamera = followObserver;
                if (Active) _session.SetObserverCameraFollow(_followObserverCamera);
            }
            var distance = Sanitize(EditorGUILayout.FloatField(new GUIContent(EditorLocalization.Tr("Third-Person Distance"),
                EditorLocalization.Tr("Third-Person Distance (avatar heights; zero returns to first person)")), _cameraDistance), 2f, 6f);
            if (distance != _cameraDistance)
            {
                _cameraDistance = distance;
                if (Active) _session.SetCameraDistance(_cameraDistance);
            }
            _lookSensitivity = Sanitize(EditorGUILayout.FloatField(EditorLocalization.Tr("Mouse Look Sensitivity"), _lookSensitivity), 2f, 20f);
            _speed = Sanitize(EditorGUILayout.FloatField(EditorLocalization.Tr("Move Speed (m/s)"), _speed), 2f, 20f);
            _turnSpeed = Sanitize(EditorGUILayout.FloatField(EditorLocalization.Tr("Turn Speed (degrees/s)"), _turnSpeed), 120f, 720f);
            if (_keyboard != null) _keyboard.LookSensitivity = _lookSensitivity;
            if (_session.Motor)
            {
                _session.Motor.MoveSpeed = _speed;
                _session.Motor.TurnSpeed = _turnSpeed;
            }
            _lipSync.DrawSettings(module, Active);
            if (Active && _keyboard != null && ((!_keyboard.ObserverView && !_keyboard.Captured) || _keyboard.Suspended) &&
                GmgLayoutHelper.DebugButton(EditorLocalization.Tr("Resume Input")))
            {
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                _keyboard.RequestCapture();
            }
            if (Active)
            {
                if (_session.LocomotionBlocked) EditorGUILayout.HelpBox(EditorLocalization.Tr("Movement is blocked by avatar locomotion or pose settings."), MessageType.Info);
                else if (_keyboard != null && (_keyboard.Suspended || (!_keyboard.ObserverView && !_keyboard.Captured && !KeyboardMotionInputSource.HasSceneViewFocus)))
                    EditorGUILayout.HelpBox(EditorLocalization.Tr("Input suspended: click the Game view or use Resume Input, or focus the Scene view, then release all control keys."), MessageType.Info);
                else
                {
                    var result = _session.Motor.LastResult;
                    EditorGUILayout.LabelField(EditorLocalization.Format(KeyboardMotionInputSource.HasSceneViewFocus
                        ? "Scene avatar control — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}"
                        : "Game Simulation — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}",
                        result.LocalVelocity.magnitude, result.AngularDegreesPerSecond, EditorLocalization.Tr(result.Grounded ? "Yes" : "No")), EditorStyles.wordWrappedLabel);
                }
            }
            _showControls = EditorGUILayout.Foldout(_showControls, EditorLocalization.Tr("Controls"), true);
            if (_showControls)
            {
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Game view: WASD moves, < / > turns, F5 toggles third-person mode, Shift runs forward / backward, C toggles crouch, and Z toggles prone. While third-person mode is enabled, the wheel adjusts distance; zooming fully in returns to first person. Mouse look and wheel zoom require captured input. Space jumps when Scene Collisions is enabled and the avatar is grounded."), MessageType.Info);
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Third person: Tab enters free camera rotation without turning the avatar. New W/S input resumes camera-relative movement; A/D turns the avatar in place during free rotation. < / > rotates only the camera during free rotation. Mouse look requires captured Game input."), MessageType.Info);
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Scene view: WASD moves, < / > turns, Shift runs forward / backward, C toggles crouch, Z toggles prone, and Space jumps when Scene Collisions is enabled and the avatar is grounded. Observer following is off by default. Alt, mouse navigation and the wheel remain available to the editor."), MessageType.Info);
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Esc releases captured Game-view input. Click the Game view or use Resume Input to continue. Select Scene Camera in View for a free observer cursor. First person hides the local avatar from the simulation camera; observer and third-person views still show it. Visible first-person body / arms are not implemented yet. Exit restores the avatar pose, motion parameters, cameras and cursor."), MessageType.Info);
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Keyboard input requires Active Input Handling: Input Manager (Old) or Both."), MessageType.Info);
            }
        }

        protected override void Toggle(ModuleVrc3 module)
        {
            if (_switching) return;
            _inputError = null;
            if (Active)
            {
                StopInput();
                _inputMode = 0;
                return;
            }
            if (_inputMode == 1 && VrInputProviders.Providers.Count == 0) { VrSetupWindow.ShowSetup(); return; }
            StartInput();
        }

        private bool SetInputMode(ModuleVrc3 module, int mode)
        {
            if (_switching || mode < 0 || mode > 1) return false;
            if (mode == _inputMode) return true;
            if (!Active)
            {
                _inputMode = mode;
                _inputError = null;
                return true;
            }

            _switching = true;
            IVrInputSource preparedSource = null;
            var previousMode = _inputMode;
            try
            {
                // Factory failures must not interrupt the current session.
                if (mode == 1) preparedSource = CreateVrSource();
                else KeyboardMotionInputSource.EnsureAvailable();
                var avatar = module.Avatar.transform;
                var pose = new Pose(avatar.position, avatar.rotation);
                if (previousMode == 0)
                {
                    _viewMode = _session.ViewMode;
                    _followObserverCamera = _session.FollowObserverCamera;
                    _cameraDistance = _session.CameraDistance;
                }

                // Stop restores the first entry baseline. Start captures that same baseline,
                // then Reposition resumes at the current root without changing final Exit.
                StopInput();
                _inputMode = mode;
                var source = preparedSource;
                preparedSource = null;
                if (StartInput(source) && RepositionInput(pose)) return true;
                var switchError = InputError();
                StopInput();
                _inputMode = previousMode;
                if (StartInput() && RepositionInput(pose))
                {
                    _inputError = EditorLocalization.Format("Could not switch input. The previous input is still active.\n{0}",
                        EditorLocalization.Tr(switchError));
                    return false;
                }
                var restoreError = InputError();
                StopInput();
                _inputMode = 0;
                _inputError = EditorLocalization.Format("Could not switch input or restore the previous input.\n{0}\n{1}",
                    EditorLocalization.Tr(switchError), EditorLocalization.Tr(restoreError));
                return false;
            }
            catch (Exception exception)
            {
                _inputError = exception.Message;
                return false;
            }
            finally
            {
                try { preparedSource?.Dispose(); }
                catch (Exception exception) { _inputError = exception.Message; }
                finally { _switching = false; }
            }
        }

        private static IVrInputSource CreateVrSource()
        {
            if (VrInputProviders.Providers.Count == 0)
                throw new InvalidOperationException("Install SteamVR support from Tools > Gesture Manager Pro - VR Setup before starting VR control.");
            return VrInputProviders.Providers[0].Create() ??
                throw new InvalidOperationException("Install SteamVR support from Tools > Gesture Manager Pro - VR Setup before starting VR control.");
        }

        private bool StartInput(IVrInputSource preparedSource = null)
        {
            _inputError = null;
            try
            {
                return StartInputCore(preparedSource);
            }
            catch (Exception exception)
            {
                StopInput();
                _inputError = exception.Message;
                return false;
            }
        }

        private bool StartInputCore(IVrInputSource preparedSource)
        {
            if (_inputMode == 1)
            {
                if (!_vr.Start(_manager, preparedSource ?? CreateVrSource(), _vrSettings, _speed, _turnSpeed, _collisions)) return false;
                _lipSync.Start(_manager);
                return true;
            }
            _keyboard = new KeyboardMotionInputSource(gameSimulation: true) { LookSensitivity = _lookSensitivity };
            if (_session.Start(_manager, _keyboard, _speed, _turnSpeed, _collisions, gravity: true, previewCamera: true,
                viewMode: _viewMode, cameraDistance: _cameraDistance, followObserverCamera: _followObserverCamera,
                mode: ControlledSessionMode.GameSimulation))
            {
                _lipSync.Start(_manager);
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                _keyboard.RequestCapture();
                return true;
            }
            else
            {
                _keyboard.Dispose();
                _keyboard = null;
                return false;
            }
        }

        private bool RepositionInput(Pose pose)
        {
            try
            {
                if (_inputMode == 1) _vr.Reposition(pose);
                else _session.Reposition(pose);
                if (Active && _keyboard != null) _keyboard.RequestCapture();
                return Active;
            }
            catch (Exception exception)
            {
                _inputError = exception.Message;
                return false;
            }
        }

        private void StopInput()
        {
            _vr.Stop();
            _session.Stop();
            _keyboard = null;
        }

        private string InputError() => !string.IsNullOrEmpty(_inputError) ? _inputError :
            _inputMode == 1 && !string.IsNullOrEmpty(_vr.Error) ? _vr.Error : _session.Error;

        public override void Dispose()
        {
            _vr.Dispose();
            _session.Dispose();
            _lipSync.Dispose();
            _keyboard = null;
        }

        private static float Sanitize(float value, float fallback, float maximum) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, 0f, maximum);

        private void DrawVr(ModuleVrc3 module)
        {
            if (GmgLayoutHelper.DebugButton(EditorLocalization.Tr("VR Setup"))) VrSetupWindow.ShowSetup();
            using (new EditorGUI.DisabledScope(Active))
            {
                _collisions = EditorGUILayout.Toggle(EditorLocalization.Tr("Scene Collisions"), _collisions);
                _vrSettings.OscEnabled = EditorGUILayout.Toggle(EditorLocalization.Tr("OSC Trackers"), _vrSettings.OscEnabled);
                if (_vrSettings.OscEnabled)
                    _vrSettings.OscPort = Mathf.Clamp(EditorGUILayout.IntField(EditorLocalization.Tr("OSC Listen Port"), _vrSettings.OscPort), 1, 65535);
            }
            _vrSettings.Measurement = (TrackingMeasurement)EditorGUILayout.Popup(EditorLocalization.Tr("Calibration Measurement"),
                (int)_vrSettings.Measurement, new[] { EditorLocalization.Tr("Eye Height"), EditorLocalization.Tr("Arm Span") });
            if (_vrSettings.Measurement == TrackingMeasurement.Height)
                _vrSettings.UserHeight = Sanitize(EditorGUILayout.FloatField(EditorLocalization.Tr("Standing Eye Height (m)"), _vrSettings.UserHeight), 1.7f, 3f);
            else
                _vrSettings.UserArmSpan = Sanitize(EditorGUILayout.FloatField(EditorLocalization.Tr("User Arm Span (m)"), _vrSettings.UserArmSpan), 1.7f, 3f);
            var locks = new[] { TrackingLockMode.Head, TrackingLockMode.Hip, TrackingLockMode.Both };
            _vrSettings.LockMode = locks[EditorGUILayout.Popup(EditorLocalization.Tr("IK Lock"), Array.IndexOf(locks, _vrSettings.LockMode),
                new[] { EditorLocalization.Tr("Lock Head"), EditorLocalization.Tr("Lock Hip"), EditorLocalization.Tr("Lock Both") })];
            _vrSettings.SnapTurning = EditorGUILayout.Toggle(EditorLocalization.Tr("Snap Turning"), _vrSettings.SnapTurning);
            if (_vrSettings.SnapTurning)
                _vrSettings.SnapAngle = Mathf.Clamp(EditorGUILayout.FloatField(EditorLocalization.Tr("Snap Angle (degrees)"), _vrSettings.SnapAngle), 15f, 90f);
            _speed = Sanitize(EditorGUILayout.FloatField(EditorLocalization.Tr("Move Speed (m/s)"), _speed), 2f, 20f);
            _turnSpeed = Sanitize(EditorGUILayout.FloatField(EditorLocalization.Tr("Turn Speed (degrees/s)"), _turnSpeed), 120f, 720f);
            if (_session.Motor) { _session.Motor.MoveSpeed = _speed; _session.Motor.TurnSpeed = _turnSpeed; }
            _lipSync.DrawSettings(module, Active);
            if (_vr.Running)
            {
                if (!string.IsNullOrEmpty(_vr.Status)) EditorGUILayout.LabelField("SteamVR", EditorLocalization.Tr(_vr.Status), EditorStyles.wordWrappedLabel);
                EditorGUILayout.HelpBox(EditorLocalization.Tr(_vr.Calibrating
                    ? "Stand upright, align the trackers, release both triggers, then squeeze both triggers to calibrate."
                    : "VR tracking is active. Open the headset menu for expressions and session controls."), MessageType.Info);
                if (GmgLayoutHelper.DebugButton(EditorLocalization.Tr("Calibrate Full Body"))) _vr.RequestCalibration();
                if (_vr.Calibrating && GmgLayoutHelper.DebugButton(EditorLocalization.Tr("Confirm Calibration"))) _vr.ConfirmCalibration();
                if (GmgLayoutHelper.DebugButton(EditorLocalization.Tr(_vr.Paused ? "Resume VR" : "Pause VR"))) _vr.TogglePause();
                if (GmgLayoutHelper.DebugButton(EditorLocalization.Tr("Recenter Tracking"))) _vr.Recenter();
                if (GmgLayoutHelper.DebugButton(EditorLocalization.Tr("Controller Bindings"))) _vr.OpenBindings();
                EditorGUILayout.LabelField(EditorLocalization.Tr("Tracker Roles"), EditorStyles.boldLabel);
                var roles = (TrackingRole[])Enum.GetValues(typeof(TrackingRole));
                var labels = new string[roles.Length];
                for (var index = 0; index < roles.Length; index++) labels[index] = EditorLocalization.Tr(RoleLabel(roles[index]));
                // Role changes invalidate calibration; device identifiers are never translated.
                foreach (var device in _vr.Devices)
                {
                    var selected = EditorGUILayout.Popup(device.Id, Array.IndexOf(roles, device.Role), labels);
                    if (selected >= 0 && roles[selected] != device.Role) _vr.Assign(device.Id, roles[selected]);
                }
            }
            else if (VrInputProviders.Providers.Count == 0)
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Install SteamVR support from Tools > Gesture Manager Pro - VR Setup before starting VR control."), MessageType.Info);
        }

        private static string RoleLabel(TrackingRole role)
        {
            switch (role)
            {
                case TrackingRole.Head: return "Head";
                case TrackingRole.LeftHand: return "Left Hand";
                case TrackingRole.RightHand: return "Right Hand";
                case TrackingRole.Hip: return "Hip";
                case TrackingRole.LeftFoot: return "Left Foot";
                case TrackingRole.RightFoot: return "Right Foot";
                case TrackingRole.Chest: return "Chest";
                case TrackingRole.LeftKnee: return "Left Knee";
                case TrackingRole.RightKnee: return "Right Knee";
                case TrackingRole.LeftElbow: return "Left Upper Arm";
                case TrackingRole.RightElbow: return "Right Upper Arm";
                default: return "Unassigned";
            }
        }
    }

}
