using System;
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

        public KeyboardMotionInputSource(bool gameSimulation = false) => GameSimulation = gameSimulation;

        public bool GameSimulation { get; }
        public bool Captured => _captured;
        public float LookSensitivity { get; set; } = 2f;
        public bool Suspended { get; private set; } = true;
        public event Action ViewToggleRequested;
        public static bool HasGameViewFocus => Application.isFocused && !EditorGUIUtility.editingTextField && EditorWindow.focusedWindow &&
            EditorWindow.focusedWindow.GetType().FullName == "UnityEditor.GameView";

        public static void EnsureAvailable() => Input.GetKey(KeyCode.W);

        public MotionInputFrame Sample()
        {
            if (_disposed) return default;
            if (!HasGameViewFocus) { Reset(); return default; }
            if (GameSimulation)
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
            var look = new Vector2(Input.GetAxisRaw("Mouse X"), -Input.GetAxisRaw("Mouse Y")) * sensitivity;
            if (Input.GetKeyDown(KeyCode.V)) ViewToggleRequested?.Invoke();
            var sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return new MotionInputFrame(move, turn, look, sprint ? 2f : 1f, Input.GetKeyDown(KeyCode.Space));
        }

        public void RequestCapture()
        {
            if (_disposed || !GameSimulation) return;
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
            _captureRequested = false;
            _needsRelease = true;
            Suspended = true;
        }

        public void Dispose()
        {
            Reset();
            _disposed = true;
            ViewToggleRequested = null;
        }

        private static float Axis(KeyCode negative, KeyCode positive) => (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
        private static bool AnyControlKeyHeld() => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) ||
            Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Comma) || Input.GetKey(KeyCode.Period) || Input.GetKey(KeyCode.V) ||
            Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
            Input.GetKey(KeyCode.Escape) || AnyMouseButtonHeld();

        private static bool AnyMouseButtonHeld()
        {
            for (var button = 0; button < 7; button++)
                if (Input.GetKey((KeyCode)((int)KeyCode.Mouse0 + button))) return true;
            return false;
        }
    }
}
