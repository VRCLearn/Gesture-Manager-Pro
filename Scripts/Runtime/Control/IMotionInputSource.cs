using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control
{
    public interface IMotionInputSource
    {
        MotionInputFrame Sample();
        void Reset();
    }

    public readonly struct MotionInputFrame
    {
        public readonly Vector2 Move;
        public readonly float Turn;

        public MotionInputFrame(Vector2 move, float turn)
        {
            Move = Vector2.ClampMagnitude(new Vector2(Finite(move.x), Finite(move.y)), 1f);
            Turn = Mathf.Clamp(Finite(turn), -1f, 1f);
        }

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    public readonly struct MotionResult
    {
        public readonly Vector3 LocalVelocity;
        public readonly float AngularDegreesPerSecond;
        public readonly bool Grounded;

        public MotionResult(Vector3 localVelocity, float angularDegreesPerSecond, bool grounded)
        {
            LocalVelocity = localVelocity;
            AngularDegreesPerSecond = angularDegreesPerSecond;
            Grounded = grounded;
        }
    }
}
