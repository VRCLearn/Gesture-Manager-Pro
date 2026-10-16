using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    public sealed class KeyboardMotionInputSource : IMotionInputSource, IDisposable
    {
        private bool _needsRelease = true;
        private bool _captureRequested;
        private bool _captured;
        private bool _disposed;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;
        private readonly HashSet<KeyCode> _sceneKeys = new HashSet<KeyCode>();
        private readonly HashSet<KeyCode> _scenePressedKeys = new HashSet<KeyCode>();
        private readonly HashSet<KeyCode> _sceneReleaseKeys = new HashSet<KeyCode>();
        private EditorWindow _focusedWindow;
        private int _sceneNavigationButtons;
        private bool _sceneNavigation;
        private bool _observerView;

        public KeyboardMotionInputSource(bool gameSimulation = false)
        {
            GameSimulation = gameSimulation;
            if (gameSimulation) SceneView.duringSceneGui += OnSceneGUI;
        }

        public bool GameSimulation { get; }
        public bool ObserverView
        {
            get => _observerView;
            set { if (_observerView == value) return; Reset(); _observerView = value; }
        }
        public bool Captured => _captured;
        public bool FreeLook { get; set; }
        public float LookSensitivity { get; set; } = 2f;
        public bool Suspended { get; private set; } = true;
        public bool HasInputFocus => HasGameViewFocus || (GameSimulation && HasSceneViewFocus);
        public event Action ViewToggleRequested;
        public event Action<float> CameraZoomRequested;
        public event Action FreeLookRequested;
        public event Action ForwardMovementRequested;
        public static bool HasGameViewFocus => Application.isFocused && !EditorGUIUtility.editingTextField && EditorWindow.focusedWindow &&
            EditorWindow.focusedWindow.GetType().FullName == "UnityEditor.GameView";
        public static bool HasSceneViewFocus => UnityEditorInternal.InternalEditorUtility.isApplicationActive && !EditorGUIUtility.editingTextField &&
            EditorWindow.focusedWindow is SceneView;

        public static void EnsureAvailable() => Input.GetKey(KeyCode.W);

        public MotionInputFrame Sample()
        {
            if (_disposed) return default;
            if (!HasInputFocus) { Reset(); return default; }
            ObserveFocus(EditorWindow.focusedWindow);
            if (GameSimulation && HasSceneViewFocus) return SampleSceneInput();
            if (GameSimulation && !ObserverView)
            {
                if (Input.GetKey(KeyCode.Escape)) { Reset(); return default; }
                if (_captured && Cursor.lockState != CursorLockMode.Locked) { Reset(); return default; }
                if (!_captured)
                {
                    if (Input.GetMouseButtonDown(0)) _captureRequested = true;
                    if (!_captureRequested || AnyControlKeyHeld()) return default;
                    _previousCursorLock = Cursor.lockState;
                    _previousCursorVisible = Cursor.visible;
                    _captured = true;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    _captureRequested = false;
                    _needsRelease = false;
                    _sceneReleaseKeys.Clear();
                    _sceneNavigationButtons = 0;
                    Suspended = false;
                    // Capture changes the cursor position. Discard this frame's axes
                    // so the click or warp cannot rotate the avatar.
                    return default;
                }
            }
            var move = new Vector2(Axis(KeyCode.A, KeyCode.D), Axis(KeyCode.S, KeyCode.W));
            var turn = Axis(KeyCode.Comma, KeyCode.Period);
            if (_needsRelease)
            {
                _needsRelease = move != Vector2.zero || turn != 0f || AnyControlKeyHeld();
                Suspended = _needsRelease;
                return default;
            }
            Suspended = false;
            if (!GameSimulation) return new MotionInputFrame(move, turn);
            var sensitivity = float.IsNaN(LookSensitivity) || float.IsInfinity(LookSensitivity) ? 2f : Mathf.Clamp(LookSensitivity, 0f, 20f);
            var look = ObserverView ? Vector2.zero : new Vector2(Input.GetAxisRaw("Mouse X"), -Input.GetAxisRaw("Mouse Y")) * sensitivity;
            var modified = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ||
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            var sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            ProcessGameCameraInput(Input.GetKeyDown(KeyCode.F5), Input.mouseScrollDelta.y, modified, sprint);
            ProcessGameFreeLookInput(Input.GetKeyDown(KeyCode.Tab), Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.S), modified, sprint);
            if (Suspended) return default;
            var pose = Input.GetKeyDown(KeyCode.C) ? MotionPose.Crouch :
                Input.GetKeyDown(KeyCode.Z) ? MotionPose.Prone : MotionPose.None;
            return CreateGameFrame(move, turn, look, sprint, Input.GetKeyDown(KeyCode.Space), pose);
        }

        private MotionInputFrame CreateGameFrame(Vector2 move, float turn, Vector2 look, bool sprint, bool jump, MotionPose pose)
        {
            var viewTurn = 0f;
            if (FreeLook)
            {
                viewTurn = turn;
                turn = move.x;
                move.x = 0f;
            }
            return new MotionInputFrame(move, turn, look, sprint ? 2f : 1f, jump, pose, viewTurn);
        }

        private void ProcessGameFreeLookInput(bool tab, bool forwardPressed, bool modified, bool shift)
        {
            if (_disposed || !GameSimulation || Suspended || ObserverView || !_captured || modified) return;
            if (tab && !shift) FreeLookRequested?.Invoke();
            if (FreeLook && forwardPressed) ForwardMovementRequested?.Invoke();
        }

        private void ProcessGameCameraInput(bool toggle, float scroll, bool modified, bool shift)
        {
            if (_disposed || Suspended) return;
            if (toggle && !modified && !shift) ViewToggleRequested?.Invoke();
            if (_disposed || Suspended || ObserverView || !_captured || modified ||
                float.IsNaN(scroll) || float.IsInfinity(scroll) || scroll == 0f) return;
            CameraZoomRequested?.Invoke(scroll);
        }

        public void RequestCapture()
        {
            if (_disposed || !GameSimulation || ObserverView) return;
            _captureRequested = true;
            _needsRelease = true;
            Suspended = true;
        }

        public void Reset()
        {
            if (_captured)
            {
                Cursor.lockState = _previousCursorLock;
                Cursor.visible = _previousCursorVisible;
                _captured = false;
            }
            _sceneKeys.Clear();
            _scenePressedKeys.Clear();
            _sceneReleaseKeys.Clear();
            _focusedWindow = null;
            _sceneNavigationButtons = 0;
            _sceneNavigation = false;
            _captureRequested = false;
            _needsRelease = true;
            Suspended = true;
        }

        public void Dispose()
        {
            Reset();
            if (GameSimulation) SceneView.duringSceneGui -= OnSceneGUI;
            _disposed = true;
            ViewToggleRequested = null;
            CameraZoomRequested = null;
            FreeLookRequested = null;
            ForwardMovementRequested = null;
            FreeLook = false;
        }

        private void ObserveFocus(EditorWindow window)
        {
            if (_focusedWindow == window) return;
            // Never carry held keys or captured Game mouse input into another view.
            // Preserve a request made by the Enter / Resume button before its first sample.
            var requested = _captureRequested;
            Reset();
            _focusedWindow = window;
            _captureRequested = requested && HasGameViewFocus;
        }

        private void OnSceneGUI(SceneView view)
        {
            if (_disposed) return;
            if (!HasSceneViewFocus || EditorWindow.focusedWindow != view)
            {
                if (_focusedWindow == view) ObserveFocus(HasGameViewFocus ? EditorWindow.focusedWindow : null);
                return;
            }
            ObserveFocus(view);
            if (Event.current != null) ProcessSceneEvent(Event.current);
        }

        private void ProcessSceneEvent(Event current)
        {
            var type = current.rawType;
            if (current.button == 1 || current.button == 2)
            {
                var bit = 1 << current.button;
                if (type == EventType.MouseDown) _sceneNavigationButtons |= bit;
                else if (type == EventType.MouseUp) _sceneNavigationButtons &= ~bit;
            }
            var navigation = _sceneNavigationButtons != 0 || current.alt || current.control || current.command;
            if (navigation && !_sceneNavigation) BlockSceneKeysUntilRelease();
            _sceneNavigation = navigation;
            if (IsSceneMovementKey(current.keyCode))
            {
                if (type == EventType.KeyUp)
                {
                    _sceneKeys.Remove(current.keyCode);
                    _sceneReleaseKeys.Remove(current.keyCode);
                    if (!navigation && current.type != EventType.Used) current.Use();
                }
                else if (type == EventType.KeyDown)
                {
                    if (navigation || _sceneReleaseKeys.Count != 0)
                    {
                        _sceneReleaseKeys.Add(current.keyCode);
                        if (!navigation && current.type != EventType.Used) current.Use();
                    }
                    else if (!_sceneReleaseKeys.Contains(current.keyCode) && current.type != EventType.Used)
                    {
                        if (_sceneKeys.Add(current.keyCode)) _scenePressedKeys.Add(current.keyCode);
                        current.Use();
                    }
                }
            }
            Suspended = _sceneNavigation || _sceneReleaseKeys.Count != 0;
        }

        private MotionInputFrame SampleSceneInput()
        {
            // Scene View owns its camera and mouse navigation. Only model movement
            // keys captured from duringSceneGui are sampled here; legacy Input is Game-only.
            Suspended = _sceneNavigation || _sceneReleaseKeys.Count != 0;
            var frame = Suspended ? default : new MotionInputFrame(
                new Vector2(SceneAxis(KeyCode.A, KeyCode.D), SceneAxis(KeyCode.S, KeyCode.W)),
                SceneAxis(KeyCode.Comma, KeyCode.Period), default,
                SceneHeld(KeyCode.LeftShift) || SceneHeld(KeyCode.RightShift) ? 2f : 1f,
                _scenePressedKeys.Contains(KeyCode.Space),
                _scenePressedKeys.Contains(KeyCode.C) ? MotionPose.Crouch :
                    _scenePressedKeys.Contains(KeyCode.Z) ? MotionPose.Prone : MotionPose.None);
            // Scene GUI can deliver a complete short press between player updates.
            // Keep its first press until one motor sample, then consume it once.
            _scenePressedKeys.Clear();
            return frame;
        }

        private float SceneAxis(KeyCode negative, KeyCode positive) =>
            (SceneHeld(positive) ? 1f : 0f) - (SceneHeld(negative) ? 1f : 0f);

        private bool SceneHeld(KeyCode key) => _sceneKeys.Contains(key) || _scenePressedKeys.Contains(key);

        private static bool IsSceneMovementKey(KeyCode key) => key == KeyCode.W || key == KeyCode.A ||
            key == KeyCode.S || key == KeyCode.D || key == KeyCode.Comma || key == KeyCode.Period ||
            key == KeyCode.Space || key == KeyCode.LeftShift || key == KeyCode.RightShift ||
            key == KeyCode.C || key == KeyCode.Z;

        private void BlockSceneKeysUntilRelease()
        {
            foreach (var key in _sceneKeys) _sceneReleaseKeys.Add(key);
            _sceneKeys.Clear();
            _scenePressedKeys.Clear();
        }

        private static float Axis(KeyCode negative, KeyCode positive) => (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
        private static bool AnyControlKeyHeld() => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) ||
            Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Comma) || Input.GetKey(KeyCode.Period) || Input.GetKey(KeyCode.F5) || Input.GetKey(KeyCode.Tab) ||
            Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
            Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.Z) ||
            Input.GetKey(KeyCode.Escape) || AnyMouseButtonHeld();

        private static bool AnyMouseButtonHeld()
        {
            for (var button = 0; button < 7; button++)
                if (Input.GetKey((KeyCode)((int)KeyCode.Mouse0 + button))) return true;
            return false;
        }
    }
}
