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
        private bool _gravity;
        private float _lookSensitivity = 2f;
        private SimulationViewMode _viewMode = SimulationViewMode.FirstPerson;
        private float _cameraDistance = 2f;
        private bool _showControls;

        protected override string Name => EditorLocalization.Tr("Game Simulation");
        protected override Texture2D Icon => Resources.Load<Texture2D>("GestureManagerPro/Vrc3/BSX_GM_Velocity");
        protected override string Description => EditorLocalization.Tr("Control the avatar in Game or Scene view.\nKeyboard movement and mouse look.");
        protected override bool Active => _session.Running;

        [InitializeOnLoadMethod]
        private static void Register() => AvatarTools.RegisterTool(manager => new GameSimulationTool(manager));

        public GameSimulationTool(GestureManager manager)
        {
            _manager = manager;
            _session.Stopped += _lipSync.Stop;
        }

        protected override void Gui(ModuleVrc3 module)
        {
            using (new EditorGUI.DisabledScope(Active))
            {
                _collisions = EditorGUILayout.Toggle(EditorLocalization.Tr("Scene Collisions"), _collisions);
                using (new EditorGUI.DisabledScope(!_collisions))
                    _gravity = EditorGUILayout.Toggle(new GUIContent(EditorLocalization.Tr("Gravity / Jump"),
                        EditorLocalization.Tr("Gravity / Jump (requires a ground collider)")), _gravity);
            }
            if (Active) _viewMode = _session.ViewMode;
            var view = EditorGUILayout.Popup(EditorLocalization.Tr("View"), _viewMode == SimulationViewMode.FirstPerson ? 0 : 1,
                new[] { EditorLocalization.Tr("First Person"), EditorLocalization.Tr("Third Person") });
            var selectedView = view == 0 ? SimulationViewMode.FirstPerson : SimulationViewMode.ThirdPerson;
            if (selectedView != _viewMode)
            {
                _viewMode = selectedView;
                if (Active) _session.SetViewMode(_viewMode);
            }
            var distance = Mathf.Max(0.5f, Sanitize(EditorGUILayout.FloatField(new GUIContent(EditorLocalization.Tr("Third-Person Distance"),
                EditorLocalization.Tr("Third-Person Distance (avatar heights)")), _cameraDistance), 2f, 6f));
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
            using (new EditorGUI.DisabledScope(!Active && (!EditorApplication.isPlaying || EditorApplication.isPaused)))
                if (GmgLayoutHelper.DebugButton(EditorLocalization.Tr(Active ? "Exit and Restore" : "Enter Game Simulation"))) Toggle(module);
            if (Active && _keyboard != null && (!_keyboard.Captured || _keyboard.Suspended) &&
                GmgLayoutHelper.DebugButton(EditorLocalization.Tr("Resume Input")))
            {
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                _keyboard.RequestCapture();
            }
            if (!string.IsNullOrEmpty(_session.Error)) EditorGUILayout.HelpBox(EditorLocalization.Tr(_session.Error), MessageType.Error);
            else if (Active)
            {
                if (_session.LocomotionBlocked) EditorGUILayout.HelpBox(EditorLocalization.Tr("Movement is blocked by avatar locomotion or pose settings."), MessageType.Info);
                else if (_keyboard != null && (_keyboard.Suspended || (!_keyboard.Captured && !KeyboardMotionInputSource.HasSceneViewFocus)))
                    EditorGUILayout.HelpBox(EditorLocalization.Tr("Input suspended: click the Game view or use Resume Input, or focus the Scene view, then release all control keys."), MessageType.Info);
                else
                {
                    var result = _session.Motor.LastResult;
                    EditorGUILayout.LabelField(EditorLocalization.Format(KeyboardMotionInputSource.HasSceneViewFocus
                        ? "Scene avatar control — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}; Scene camera unchanged"
                        : "Game Simulation — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}",
                        result.LocalVelocity.magnitude, result.AngularDegreesPerSecond, EditorLocalization.Tr(result.Grounded ? "Yes" : "No")), EditorStyles.wordWrappedLabel);
                }
            }
            _showControls = EditorGUILayout.Foldout(_showControls, EditorLocalization.Tr("Controls"), true);
            if (_showControls)
            {
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Game view: WASD moves, the mouse looks, < / > turns, V switches first / third person, and Shift runs forward / backward. C toggles crouch; Z toggles prone. Space jumps when gravity is enabled and the avatar is grounded."), MessageType.Info);
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Scene view: WASD moves, < / > turns, Shift runs forward / backward, C toggles crouch, Z toggles prone, and Space jumps when gravity is enabled and the avatar is grounded. The Scene camera stays unchanged; Alt or right / middle mouse suspends avatar input for camera navigation."), MessageType.Info);
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Esc releases the Game-view mouse and suspends input. Click the Game view or use Resume Input to continue. First person hides the local avatar from the simulation camera; the Scene view and third person still show it. Visible first-person body / arms are not implemented yet. Exit restores the avatar pose, motion parameters, cameras and cursor."), MessageType.Info);
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Keyboard input requires Active Input Handling: Input Manager (Old) or Both. The motion input interface is ready for future VR adapters; headset tracking and IK are not implemented yet."), MessageType.Info);
            }
        }

        protected override void Toggle(ModuleVrc3 module)
        {
            if (Active)
            {
                _session.Stop();
                _keyboard = null;
                return;
            }
            _keyboard = new KeyboardMotionInputSource(gameSimulation: true) { LookSensitivity = _lookSensitivity };
            if (_session.Start(_manager, _keyboard, _speed, _turnSpeed, _collisions, _gravity, previewCamera: true,
                viewMode: _viewMode, cameraDistance: _cameraDistance))
            {
                _lipSync.Start(_manager);
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                _keyboard.RequestCapture();
            }
            else
            {
                _keyboard.Dispose();
                _keyboard = null;
            }
        }

        public override void Dispose()
        {
            _session.Dispose();
            _lipSync.Dispose();
            _keyboard = null;
        }

        private static float Sanitize(float value, float fallback, float maximum) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, 0f, maximum);
    }

}
