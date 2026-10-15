using System;
using System.Collections.Generic;
using System.IO;
using VRCLearn.GestureManagerPro.Localization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    public sealed class ControlledModeWindow : EditorWindow
    {
        [SerializeField] private GestureManager _manager;
        [SerializeField] private float _speed = 2f;
        [SerializeField] private float _turnSpeed = 120f;
        [SerializeField] private bool _collisions = true;
        [SerializeField] private bool _gravity;
        [SerializeField] private float _lookSensitivity = 2f;
        [SerializeField] private SimulationViewMode _viewMode = SimulationViewMode.FirstPerson;
        [SerializeField] private float _cameraDistance = 2f;
        private ControlledModeSession _session;
        private KeyboardMotionInputSource _keyboard;
        private VisualElement _configuration;
        private HelpBox _status;
        private Button _start;
        private Button _stop;
        private Button _resume;
        private DropdownField _view;
        private ObjectField _managerField;

        [MenuItem("Tools/Game Simulation")]
        public static void Open() => GetWindow<ControlledModeWindow>();

        private void OnEnable()
        {
            _session = new ControlledModeSession();
            if (!_manager) PickManager();
            EditorApplication.update += RefreshStatus;
            EditorLocalization.Changed += Rebuild;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RefreshStatus;
            EditorLocalization.Changed -= Rebuild;
            _session?.Dispose();
            _session = null;
        }

        public void CreateGUI() => Rebuild();

        private void Rebuild()
        {
            titleContent = new GUIContent(EditorLocalization.Tr("Game Simulation"));
            minSize = new Vector2(360f, 440f);
            rootVisualElement.Clear();
            EditorLocalization.ApplyFontPreferences(rootVisualElement);
            var scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            var stylePath = Path.Combine(Path.GetDirectoryName(scriptPath) ?? "", "ControlledModeWindow.uss").Replace('\\', '/');
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(stylePath);
            if (style && !rootVisualElement.styleSheets.Contains(style)) rootVisualElement.styleSheets.Add(style);
            var scroll = new ScrollView();
            scroll.AddToClassList("control-panel");
            rootVisualElement.Add(scroll);
            var title = new Label(EditorLocalization.Tr("Game Simulation"));
            title.AddToClassList("control-title");
            scroll.Add(title);
            scroll.Add(new HelpBox(EditorLocalization.Tr("Enter Play Mode with an initialized Pro avatar, then enter Game Simulation. WASD controls the avatar in Game or Scene view. Esc releases the Game-view mouse."), HelpBoxMessageType.Info));
            var buttons = new VisualElement();
            buttons.AddToClassList("control-buttons");
            _start = new Button(StartControl) { text = EditorLocalization.Tr("Enter Game Simulation") };
            _stop = new Button(() => { _session?.Stop(); RefreshStatus(); }) { text = EditorLocalization.Tr("Exit and Restore") };
            buttons.Add(_start);
            buttons.Add(_stop);
            scroll.Add(buttons);
            _resume = new Button(ResumeInput) { text = EditorLocalization.Tr("Resume Input") };
            scroll.Add(_resume);
            _status = new HelpBox("", HelpBoxMessageType.Info);
            scroll.Add(_status);
            _configuration = new VisualElement();
            scroll.Add(_configuration);
            _managerField = new ObjectField(EditorLocalization.Tr("Gesture Manager Pro")) { objectType = typeof(GestureManager), allowSceneObjects = true, value = _manager };
            _managerField.RegisterValueChangedCallback(evt => _manager = evt.newValue as GestureManager);
            _configuration.Add(_managerField);
            _configuration.Add(new Button(() => { PickManager(); _managerField.SetValueWithoutNotify(_manager); }) { text = EditorLocalization.Tr("Use Selected / Active Manager") });
            var collisions = new Toggle(EditorLocalization.Tr("Scene Collisions")) { value = _collisions };
            var gravity = new Toggle(EditorLocalization.Tr("Gravity / Jump (requires a ground collider)")) { value = _gravity };
            gravity.SetEnabled(_collisions);
            collisions.RegisterValueChangedCallback(evt => { _collisions = evt.newValue; gravity.SetEnabled(_collisions); });
            gravity.RegisterValueChangedCallback(evt => _gravity = evt.newValue);
            _configuration.Add(collisions);
            _configuration.Add(gravity);
            _view = new DropdownField(EditorLocalization.Tr("View"), new List<string>
            {
                EditorLocalization.Tr("First Person"),
                EditorLocalization.Tr("Third Person")
            }, _viewMode == SimulationViewMode.FirstPerson ? 0 : 1) { name = "viewMode" };
            _view.RegisterValueChangedCallback(evt =>
            {
                _viewMode = _view.index == 0 ? SimulationViewMode.FirstPerson : SimulationViewMode.ThirdPerson;
                if (_session != null && _session.Running) _session.SetViewMode(_viewMode);
            });
            scroll.Add(_view);
            var distance = new FloatField(EditorLocalization.Tr("Third-Person Distance (avatar heights)")) { value = _cameraDistance };
            distance.RegisterValueChangedCallback(evt =>
            {
                _cameraDistance = Mathf.Max(0.5f, Sanitize(evt.newValue, 2f, 6f));
                distance.SetValueWithoutNotify(_cameraDistance);
                if (_session != null && _session.Running) _session.SetCameraDistance(_cameraDistance);
            });
            scroll.Add(distance);
            var sensitivity = new FloatField(EditorLocalization.Tr("Mouse Look Sensitivity")) { value = _lookSensitivity };
            sensitivity.RegisterValueChangedCallback(evt =>
            {
                _lookSensitivity = Sanitize(evt.newValue, 2f, 20f);
                sensitivity.SetValueWithoutNotify(_lookSensitivity);
                if (_keyboard != null) _keyboard.LookSensitivity = _lookSensitivity;
            });
            scroll.Add(sensitivity);
            var speed = new FloatField(EditorLocalization.Tr("Move Speed (m/s)")) { value = _speed };
            speed.RegisterValueChangedCallback(evt =>
            {
                _speed = Sanitize(evt.newValue, 2f, 20f);
                speed.SetValueWithoutNotify(_speed);
                if (_session?.Motor) _session.Motor.MoveSpeed = _speed;
            });
            scroll.Add(speed);
            var turn = new FloatField(EditorLocalization.Tr("Turn Speed (degrees/s)")) { value = _turnSpeed };
            turn.RegisterValueChangedCallback(evt =>
            {
                _turnSpeed = Sanitize(evt.newValue, 120f, 720f);
                turn.SetValueWithoutNotify(_turnSpeed);
                if (_session?.Motor) _session.Motor.TurnSpeed = _turnSpeed;
            });
            scroll.Add(turn);
            scroll.Add(new HelpBox(EditorLocalization.Tr("Game view: WASD moves, the mouse looks, < / > turns, V switches first / third person, and Shift sprints. Space jumps when gravity is enabled and the avatar is grounded."), HelpBoxMessageType.Info));
            scroll.Add(new HelpBox(EditorLocalization.Tr("Scene view: WASD and < / > move and turn the avatar without moving the Scene camera. Alt or right / middle mouse preserves Scene camera navigation and suspends avatar input."), HelpBoxMessageType.Info));
            var note = new Label(EditorLocalization.Tr("Esc releases the Game-view mouse and suspends input. Click the Game view or use Resume Input to continue. First person hides the local avatar from the simulation camera; the Scene view and third person still show it. Visible first-person body / arms are not implemented yet. Exit restores the avatar pose, motion parameters, cameras and cursor."));
            note.AddToClassList("control-note");
            scroll.Add(note);
            var limitations = new Label(EditorLocalization.Tr("Keyboard input requires Active Input Handling: Input Manager (Old) or Both. The motion input interface is ready for future VR adapters; headset tracking and IK are not implemented yet."));
            limitations.AddToClassList("control-note");
            scroll.Add(limitations);
            RefreshStatus();
        }

        private void StartControl()
        {
            if (!_manager || !_manager.isActiveAndEnabled || _manager.Module == null || !_manager.Module.Active)
            {
                PickManager();
                _managerField?.SetValueWithoutNotify(_manager);
            }
            _keyboard = new KeyboardMotionInputSource(gameSimulation: true) { LookSensitivity = _lookSensitivity };
            if (_session.Start(_manager, _keyboard, _speed, _turnSpeed, _collisions, _gravity, previewCamera: true,
                viewMode: _viewMode, cameraDistance: _cameraDistance))
            {
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                _keyboard.RequestCapture();
            }
            else
            {
                _keyboard.Dispose();
                _keyboard = null;
            }
            RefreshStatus();
        }

        private void ResumeInput()
        {
            if (_session == null || !_session.Running || _keyboard == null) return;
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            _keyboard.RequestCapture();
            RefreshStatus();
        }

        private void PickManager()
        {
            _manager = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<GestureManager>() : null;
            if (_manager && _manager.isActiveAndEnabled && _manager.Module != null && _manager.Module.Active) return;
            foreach (var manager in FindObjectsOfType<GestureManager>())
                if (manager.isActiveAndEnabled && manager.Module != null && manager.Module.Active) { _manager = manager; return; }
        }

        private void RefreshStatus()
        {
            if (_status == null || _session == null) return;
            var running = _session.Running;
            _configuration.SetEnabled(!running);
            _start.SetEnabled(!running && EditorApplication.isPlaying && !EditorApplication.isPaused);
            _stop.SetEnabled(running);
            _resume.SetEnabled(running && _keyboard != null && (!_keyboard.Captured || _keyboard.Suspended));
            if (running)
            {
                _viewMode = _session.ViewMode;
                _view.SetValueWithoutNotify(EditorLocalization.Tr(_viewMode == SimulationViewMode.FirstPerson ? "First Person" : "Third Person"));
            }
            _status.messageType = string.IsNullOrEmpty(_session.Error) ? HelpBoxMessageType.Info : HelpBoxMessageType.Error;
            if (!string.IsNullOrEmpty(_session.Error)) _status.text = EditorLocalization.Tr(_session.Error);
            else if (!running) _status.text = EditorLocalization.Tr("Game Simulation is stopped. Enter Play Mode to begin.");
            else if (_session.LocomotionBlocked) _status.text = EditorLocalization.Tr("Movement is blocked by avatar locomotion or pose settings.");
            else if (_keyboard != null && (_keyboard.Suspended || (!_keyboard.Captured && !KeyboardMotionInputSource.HasSceneViewFocus)))
                _status.text = EditorLocalization.Tr(KeyboardMotionInputSource.HasSceneViewFocus
                    ? "Scene input suspended: release control keys, Alt and the mouse buttons."
                    : "Input suspended: click the Game view or use Resume Input, or focus the Scene view, then release all control keys.");
            else
            {
                var result = _session.Motor.LastResult;
                _status.text = EditorLocalization.Format(KeyboardMotionInputSource.HasSceneViewFocus
                    ? "Scene avatar control — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}; Scene camera unchanged"
                    : "Game Simulation — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}",
                    result.LocalVelocity.magnitude, result.AngularDegreesPerSecond, EditorLocalization.Tr(result.Grounded ? "Yes" : "No"));
            }
        }

        private static float Sanitize(float value, float fallback, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, 0f, maximum);
    }

    [InitializeOnLoad]
    internal static class ControlledModeTranslations
    {
        static ControlledModeTranslations() => EditorLocalization.RegisterTranslations(new Dictionary<string, string>
        {
            { "Game Simulation", "游戏模拟" },
            { "Enter Play Mode with an initialized Pro avatar, then enter Game Simulation. WASD controls the avatar in Game or Scene view. Esc releases the Game-view mouse.", "进入播放模式，等待 Pro 角色初始化后即可进入游戏模拟。WASD 可在 Game 或 Scene 视图控制角色。Esc 释放 Game 视图中的鼠标。" },
            { "Game view: WASD moves, the mouse looks, < / > turns, V switches first / third person, and Shift sprints. Space jumps when gravity is enabled and the avatar is grounded.", "Game 视图：WASD 移动，鼠标环顾，< / > 转向，V 切换第一 / 第三人称，Shift 加速。启用重力且角色着地时，Space 跳跃。" },
            { "Scene view: WASD and < / > move and turn the avatar without moving the Scene camera. Alt or right / middle mouse preserves Scene camera navigation and suspends avatar input.", "Scene 视图：WASD 和 < / > 控制角色移动和转向，Scene 相机保持不动。按 Alt 或鼠标右键 / 中键时保留 Scene 相机导航，并暂停角色输入。" },
            { "Gesture Manager Pro", "Gesture Manager Pro" },
            { "Use Selected / Active Manager", "使用选中 / 活跃的管理器" },
            { "Move Speed (m/s)", "移动速度（米/秒）" },
            { "Turn Speed (degrees/s)", "转向速度（度/秒）" },
            { "Scene Collisions", "场景碰撞" },
            { "Gravity / Jump (requires a ground collider)", "重力 / 跳跃（需要地面碰撞体）" },
            { "View", "视角" },
            { "First Person", "第一人称" },
            { "Third Person", "第三人称" },
            { "Third-Person Distance (avatar heights)", "第三人称距离（角色身高倍数）" },
            { "Mouse Look Sensitivity", "鼠标环顾灵敏度" },
            { "Esc releases the Game-view mouse and suspends input. Click the Game view or use Resume Input to continue. First person hides the local avatar from the simulation camera; the Scene view and third person still show it. Visible first-person body / arms are not implemented yet. Exit restores the avatar pose, motion parameters, cameras and cursor.", "Esc 释放 Game 视图中的鼠标并暂停输入。点击 Game 视图或使用“继续控制”恢复。第一人称仅在模拟相机中隐藏本地角色，Scene 视图和第三人称仍会显示角色。尚未实现第一人称可见的身体 / 手臂。退出后恢复角色姿态、运动参数、相机和光标。" },
            { "Enter Game Simulation", "进入游戏模拟" },
            { "Exit and Restore", "退出并恢复" },
            { "Resume Input", "继续控制" },
            { "Keyboard input requires Active Input Handling: Input Manager (Old) or Both. The motion input interface is ready for future VR adapters; headset tracking and IK are not implemented yet.", "键盘输入需要将 Active Input Handling 设置为 Input Manager (Old) 或 Both。已预留运动输入接口；头显跟踪和 IK 尚未实现。" },
            { "Game Simulation is stopped. Enter Play Mode to begin.", "游戏模拟已停止。进入播放模式后可开始。" },
            { "Movement is blocked by avatar locomotion or pose settings.", "角色的移动或姿态设置已阻止移动。" },
            { "Input suspended: click the Game view or use Resume Input, or focus the Scene view, then release all control keys.", "输入已暂停：请点击 Game 视图或使用“继续控制”，或聚焦 Scene 视图，然后松开所有控制按键。" },
            { "Scene input suspended: release control keys, Alt and the mouse buttons.", "Scene 输入已暂停：请松开控制按键、Alt 和鼠标按键。" },
            { "Scene avatar control — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}; Scene camera unchanged", "Scene 角色控制 — 速度 {0:F2} 米/秒，转向 {1:F1} 度/秒，着地 {2}；Scene 相机保持不动" },
            { "Game Simulation — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}", "游戏模拟 — 速度 {0:F2} 米/秒，转向 {1:F1} 度/秒，着地 {2}" },
            { "Yes", "是" },
            { "No", "否" },
            { "Enter Play Mode and resume the editor before enabling controlled mode.", "启用受控模式前，请进入播放模式并取消暂停。" },
            { "Select an active Gesture Manager Pro controlling an SDK3 avatar.", "请选择正在控制 SDK3 角色的活跃 Gesture Manager Pro。" },
            { "This manager is already owned by another motion controller.", "该管理器已由另一个运动控制器接管。" },
            { "This avatar is already owned by another motion controller.", "该角色已由另一个运动控制器接管。" },
            { "Another game simulation is already active.", "另一个游戏模拟已在运行。" },
            { "Stop custom animation and dummy modes before enabling controlled mode.", "启用受控模式前，请停止自定义动画和替身模式。" },
            { "The simulator has not initialized its motion parameters yet.", "模拟器尚未初始化运动参数。" },
            { "Keyboard control requires Active Input Handling set to Input Manager (Old) or Both.", "键盘控制需要将 Active Input Handling 设置为 Input Manager (Old) 或 Both。" }
        });
    }
}
