using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control
{
    public enum MotionPose
    {
        None,
        Crouch,
        Prone
    }

    public interface IMotionInputSource
    {
        MotionInputFrame Sample();
        void Reset();
    }

    public readonly struct MotionInputFrame
    {
        public readonly Vector2 Move;
        public readonly float Turn;
        public readonly Vector2 LookDelta;
        public readonly float SpeedMultiplier;
        public readonly bool Jump;
        public readonly MotionPose Pose;

        public MotionInputFrame(Vector2 move, float turn, Vector2 lookDelta = default,
            float speedMultiplier = 1f, bool jump = false, MotionPose pose = MotionPose.None)
        {
            Move = Vector2.ClampMagnitude(new Vector2(Finite(move.x), Finite(move.y)), 1f);
            Turn = Mathf.Clamp(Finite(turn), -1f, 1f);
            LookDelta = new Vector2(Mathf.Clamp(Finite(lookDelta.x), -180f, 180f), Mathf.Clamp(Finite(lookDelta.y), -180f, 180f));
            SpeedMultiplier = Mathf.Clamp(Finite(speedMultiplier), 1f, 3f);
            Jump = jump;
            Pose = pose == MotionPose.Crouch || pose == MotionPose.Prone ? pose : MotionPose.None;
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
