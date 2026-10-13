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
        [SerializeField] private bool _previewCamera;
        [SerializeField] private bool _cameraConfigured;
        private ControlledModeSession _session;
        private KeyboardMotionInputSource _keyboard;
        private VisualElement _configuration;
        private HelpBox _status;
        private Button _start;
        private Button _stop;
        private ObjectField _managerField;

        [MenuItem("Tools/Gesture Manager Pro/Controlled Mode")]
        public static void Open() => GetWindow<ControlledModeWindow>();

        private void OnEnable()
        {
            _session = new ControlledModeSession();
            if (!_cameraConfigured) { _previewCamera = Camera.allCamerasCount == 0; _cameraConfigured = true; }
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
            titleContent = new GUIContent(EditorLocalization.Tr("Controlled Mode"));
            minSize = new Vector2(340f, 370f);
            rootVisualElement.Clear();
            rootVisualElement.styleSheets.Clear();
            var scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            var stylePath = Path.Combine(Path.GetDirectoryName(scriptPath) ?? "", "ControlledModeWindow.uss").Replace('\\', '/');
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(stylePath);
            if (style) rootVisualElement.styleSheets.Add(style);
            var scroll = new ScrollView();
            scroll.AddToClassList("control-panel");
            rootVisualElement.Add(scroll);
            var title = new Label(EditorLocalization.Tr("Keyboard Avatar Control"));
            title.AddToClassList("control-title");
            scroll.Add(title);
            scroll.Add(new HelpBox(EditorLocalization.Tr("Enter Play Mode, enable control, then focus the Game view. WASD moves; comma / period (< / >) turns. Release all control keys after changing focus."), HelpBoxMessageType.Info));
            _configuration = new VisualElement();
            scroll.Add(_configuration);
            _managerField = new ObjectField(EditorLocalization.Tr("Gesture Manager Pro")) { objectType = typeof(GestureManager), allowSceneObjects = true, value = _manager };
            _managerField.RegisterValueChangedCallback(evt => _manager = evt.newValue as GestureManager);
            _configuration.Add(_managerField);
            _configuration.Add(new Button(() => { PickManager(); _managerField.SetValueWithoutNotify(_manager); }) { text = EditorLocalization.Tr("Use Selected / Active Manager") });
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
            var collisions = new Toggle(EditorLocalization.Tr("Scene Collisions")) { value = _collisions };
            var gravity = new Toggle(EditorLocalization.Tr("Gravity (requires a ground collider)")) { value = _gravity };
            gravity.SetEnabled(_collisions);
            collisions.RegisterValueChangedCallback(evt => { _collisions = evt.newValue; gravity.SetEnabled(_collisions); });
            gravity.RegisterValueChangedCallback(evt => _gravity = evt.newValue);
            _configuration.Add(collisions);
            _configuration.Add(gravity);
            var preview = new Toggle(EditorLocalization.Tr("Temporary Follow Camera")) { value = _previewCamera };
            preview.RegisterValueChangedCallback(evt => _previewCamera = evt.newValue);
            _configuration.Add(preview);
            var note = new Label(EditorLocalization.Tr("The follow camera takes priority in the Game view while control is active. Existing cameras stay unchanged. Stop restores the avatar's original position, rotation and motion parameters."));
            note.AddToClassList("control-note");
            scroll.Add(note);
            var buttons = new VisualElement();
            buttons.AddToClassList("control-buttons");
            _start = new Button(StartControl) { text = EditorLocalization.Tr("Enable Control") };
            _stop = new Button(() => { _session?.Stop(); RefreshStatus(); }) { text = EditorLocalization.Tr("Stop and Restore") };
            buttons.Add(_start);
            buttons.Add(_stop);
            scroll.Add(buttons);
            _status = new HelpBox("", HelpBoxMessageType.Info);
            scroll.Add(_status);
            var limitations = new Label(EditorLocalization.Tr("Keyboard input requires Active Input Handling: Input Manager (Old) or Both. The motion input interface is ready for future VR adapters; headset tracking and IK are not implemented yet."));
            limitations.AddToClassList("control-note");
            scroll.Add(limitations);
            RefreshStatus();
        }

        private void StartControl()
        {
            _keyboard = new KeyboardMotionInputSource();
            if (_session.Start(_manager, _keyboard, _speed, _turnSpeed, _collisions, _gravity, _previewCamera))
                EditorApplication.ExecuteMenuItem("Window/General/Game");
            RefreshStatus();
        }

        private void PickManager()
        {
            _manager = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<GestureManager>() : null;
            if (_manager) return;
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
            _status.messageType = string.IsNullOrEmpty(_session.Error) ? HelpBoxMessageType.Info : HelpBoxMessageType.Error;
            if (!string.IsNullOrEmpty(_session.Error)) _status.text = EditorLocalization.Tr(_session.Error);
            else if (!running) _status.text = EditorLocalization.Tr("Control is stopped. Enter Play Mode to enable it.");
            else if (_session.LocomotionBlocked) _status.text = EditorLocalization.Tr("Movement is blocked by avatar locomotion or pose settings.");
            else if (_keyboard.Suspended) _status.text = EditorLocalization.Tr("Input suspended: focus the Game view and release all control keys.");
            else
            {
                var result = _session.Motor.LastResult;
                _status.text = EditorLocalization.Format("Controlling avatar — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}",
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
            { "Controlled Mode", "受控模式" },
            { "Keyboard Avatar Control", "键盘角色控制" },
            { "Enter Play Mode, enable control, then focus the Game view. WASD moves; comma / period (< / >) turns. Release all control keys after changing focus.", "进入播放模式，启用控制后聚焦 Game 视图。WASD 移动；逗号 / 句号（< / >）转向。切换焦点后，请先松开所有控制按键。" },
            { "Gesture Manager Pro", "Gesture Manager Pro" },
            { "Use Selected / Active Manager", "使用选中 / 活跃的管理器" },
            { "Move Speed (m/s)", "移动速度（米/秒）" },
            { "Turn Speed (degrees/s)", "转向速度（度/秒）" },
            { "Scene Collisions", "场景碰撞" },
            { "Gravity (requires a ground collider)", "重力（需要地面碰撞体）" },
            { "Temporary Follow Camera", "临时跟随相机" },
            { "The follow camera takes priority in the Game view while control is active. Existing cameras stay unchanged. Stop restores the avatar's original position, rotation and motion parameters.", "控制期间，跟随相机在 Game 视图中优先显示。已有相机的设置保持不变。停止后恢复角色原来的位置、旋转和运动参数。" },
            { "Enable Control", "启用控制" },
            { "Stop and Restore", "停止并恢复" },
            { "Keyboard input requires Active Input Handling: Input Manager (Old) or Both. The motion input interface is ready for future VR adapters; headset tracking and IK are not implemented yet.", "键盘输入需要将 Active Input Handling 设置为 Input Manager (Old) 或 Both。已预留运动输入接口；头显跟踪和 IK 尚未实现。" },
            { "Control is stopped. Enter Play Mode to enable it.", "控制已停止。进入播放模式后可启用。" },
            { "Movement is blocked by avatar locomotion or pose settings.", "角色的移动或姿态设置已阻止移动。" },
            { "Input suspended: focus the Game view and release all control keys.", "输入已暂停：请聚焦 Game 视图，并松开所有控制按键。" },
            { "Controlling avatar — speed {0:F2} m/s, turn {1:F1} degrees/s, grounded {2}", "正在控制角色 — 速度 {0:F2} 米/秒，转向 {1:F1} 度/秒，着地 {2}" },
            { "Yes", "是" },
            { "No", "否" },
            { "Enter Play Mode and resume the editor before enabling controlled mode.", "启用受控模式前，请进入播放模式并取消暂停。" },
            { "Select an active Gesture Manager Pro controlling an SDK3 avatar.", "请选择正在控制 SDK3 角色的活跃 Gesture Manager Pro。" },
            { "This manager is already owned by another motion controller.", "该管理器已由另一个运动控制器接管。" },
            { "This avatar is already owned by another motion controller.", "该角色已由另一个运动控制器接管。" },
            { "Stop custom animation and dummy modes before enabling controlled mode.", "启用受控模式前，请停止自定义动画和替身模式。" },
            { "The simulator has not initialized its motion parameters yet.", "模拟器尚未初始化运动参数。" },
            { "Keyboard control requires Active Input Handling set to Input Manager (Old) or Both.", "键盘控制需要将 Active Input Handling 设置为 Input Manager (Old) 或 Both。" }
        });
    }
}
