using UnityEditor;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    public sealed class KeyboardMotionInputSource : IMotionInputSource
    {
        private bool _needsRelease = true;
        public bool Suspended { get; private set; } = true;
        public static bool HasGameViewFocus => Application.isFocused && EditorWindow.focusedWindow &&
            EditorWindow.focusedWindow.GetType().FullName == "UnityEditor.GameView";

        public static void EnsureAvailable() => Input.GetKey(KeyCode.W);

        public MotionInputFrame Sample()
        {
            if (!HasGameViewFocus) { Reset(); return default; }
            var move = new Vector2(Axis(KeyCode.A, KeyCode.D), Axis(KeyCode.S, KeyCode.W));
            var turn = Axis(KeyCode.Comma, KeyCode.Period);
            if (_needsRelease)
            {
                _needsRelease = move != Vector2.zero || turn != 0f || AnyControlKeyHeld();
                Suspended = _needsRelease;
                return default;
            }
            Suspended = false;
            return new MotionInputFrame(move, turn);
        }

        public void Reset() { _needsRelease = true; Suspended = true; }
        private static float Axis(KeyCode negative, KeyCode positive) => (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
        private static bool AnyControlKeyHeld() => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) ||
            Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Comma) || Input.GetKey(KeyCode.Period);
    }
}
