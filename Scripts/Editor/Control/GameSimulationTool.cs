using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    public sealed class GameSimulationTool : GmgDynamicFunction
    {
        private readonly GestureManager _manager;
        private readonly ControlledModeSession _session = new ControlledModeSession();
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
        protected override string Description => EditorLocalization.Tr("Control the avatar in Game or Scene view.\nKeyboard movement and mouse look.");
        protected override bool Active => _session.Running;

        [InitializeOnLoadMethod]
        private static void Register() => AvatarTools.RegisterTool(manager => new GameSimulationTool(manager));

        public GameSimulationTool(GestureManager manager) => _manager = manager;

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
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Game view: WASD moves, the mouse looks, < / > turns, V switches first / third person, and Shift sprints. Space jumps when gravity is enabled and the avatar is grounded."), MessageType.Info);
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Scene view: WASD and < / > move and turn the avatar without moving the Scene camera. Alt or right / middle mouse preserves Scene camera navigation and suspends avatar input."), MessageType.Info);
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
            _keyboard = null;
        }

        private static float Sanitize(float value, float fallback, float maximum) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, 0f, maximum);
    }

    [InitializeOnLoad]
    internal static class ControlledModeTranslations
    {
        static ControlledModeTranslations() => EditorLocalization.RegisterTranslations(new Dictionary<string, string>
        {
            { "Game Simulation", "游戏模拟" },
            { "Control the avatar in Game or Scene view.\nKeyboard movement and mouse look.", "在 Game 或 Scene 视图控制角色。\n使用键盘移动、鼠标环顾。" },
            { "Gravity / Jump", "重力 / 跳跃" },
            { "Third-Person Distance", "第三人称距离" },
            { "Controls", "操作说明" },
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
