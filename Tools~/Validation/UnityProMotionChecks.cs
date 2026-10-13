using System;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;
using UnityEngine;

// Deterministic injected-input tests. This does not simulate physical key presses
// or claim to verify Game View focus, a headset, or a VR pose solver.
public static class UnityProMotionChecks
{
    public static void Run(Action<bool, string> check)
    {
        GameObject avatar = null;
        GameObject motorObject = null;
        try
        {
            var input = new TestMotionInput();
            var malformed = new MotionInputFrame(new Vector2(float.NaN, float.PositiveInfinity), float.NegativeInfinity);
            check(malformed.Move == Vector2.zero && malformed.Turn == 0f, "External input sanitizes nonfinite movement and rotation");
            var diagonal = new MotionInputFrame(Vector2.one, 5f);
            check(Mathf.Abs(diagonal.Move.magnitude - 1f) < 0.0001f && diagonal.Turn == 1f, "Diagonal and rotation inputs respect the public interface bounds");

            avatar = new GameObject("Pro validation motion avatar");
            avatar.transform.SetPositionAndRotation(new Vector3(3f, 2f, -1f), Quaternion.Euler(8f, 90f, 4f));
            var start = avatar.transform.position;
            var initialTilt = Quaternion.Inverse(Quaternion.Euler(0f, avatar.transform.eulerAngles.y, 0f)) * avatar.transform.rotation;
            motorObject = new GameObject("Pro validation motor");
            var motor = motorObject.AddComponent<CharacterMotionMotor>();
            motor.Collisions = false;
            motor.UseGravity = false;
            motor.MoveSpeed = 2f;
            motor.TurnSpeed = 120f;
            var canMove = true;
            var publishCount = 0;
            var published = default(MotionResult);
            motor.Initialize(avatar.transform, input, 1.8f, 0.2f, 0f, () => canMove,
                result => { published = result; publishCount++; });
            check(input.Resets == 1, "Starting a motor clears input state");
            check(publishCount == 1 && published.LocalVelocity == Vector3.zero, "Initialization publishes a stationary state");
            check(!motor.Controller.enabled, "Collision-free preview leaves its controller disabled");

            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            var before = avatar.transform.position;
            var expectedDirection = motor.transform.forward;
            motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, before + expectedDirection * 0.2f) < 0.0001f, "Forward input moves relative to the avatar yaw");
            check(Vector3.Distance(published.LocalVelocity, new Vector3(0f, 0f, 2f)) < 0.0001f, "Published forward velocity uses avatar-local coordinates in meters per second");
            check(published.AngularDegreesPerSecond == 0f, "Forward movement does not add turning");
            check(Vector3.Distance(avatar.transform.position, motor.transform.position) < 0.0001f, "The motor applies its displacement to the avatar");
            check(Quaternion.Angle(avatar.transform.rotation, motor.transform.rotation * initialTilt) < 0.001f, "Movement preserves the avatar's original tilt");

            input.Frame = new MotionInputFrame(Vector2.right, 0f);
            before = avatar.transform.position;
            var expectedRight = motor.transform.right;
            motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, before + expectedRight * 0.2f) < 0.0001f, "Strafe input moves along the avatar's right axis");
            check(Vector3.Distance(published.LocalVelocity, new Vector3(2f, 0f, 0f)) < 0.0001f, "Published strafe velocity uses the local X parameter axis");

            input.Frame = new MotionInputFrame(Vector2.one, 0f);
            before = avatar.transform.position;
            motor.Step(0.1f);
            check(Mathf.Abs(Vector3.Distance(avatar.transform.position, before) - 0.2f) < 0.0001f, "Diagonal movement does not exceed cardinal speed");
            check(Mathf.Abs(published.LocalVelocity.magnitude - 2f) < 0.0001f, "Diagonal velocity magnitude matches configured movement speed");

            input.Frame = new MotionInputFrame(Vector2.zero, 1f);
            before = avatar.transform.position;
            var beforeYaw = motor.transform.eulerAngles.y;
            motor.Step(0.1f);
            check(Mathf.Abs(Mathf.DeltaAngle(beforeYaw, motor.transform.eulerAngles.y) - 12f) < 0.001f, "Positive turn input applies the configured yaw rate");
            check(Mathf.Abs(published.AngularDegreesPerSecond - 120f) < 0.01f, "Published angular velocity is degrees per second");
            check(Vector3.Distance(avatar.transform.position, before) < 0.0001f, "Turning in place does not translate the avatar");
            input.Frame = new MotionInputFrame(Vector2.zero, -1f);
            motor.Step(0.1f);
            check(Mathf.Abs(Mathf.DeltaAngle(beforeYaw, motor.transform.eulerAngles.y)) < 0.001f, "Negative turn input reverses the yaw direction");

            input.Frame = new MotionInputFrame(Vector2.up, 1f);
            canMove = false;
            before = avatar.transform.position;
            var rotation = avatar.transform.rotation;
            motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, before) < 0.0001f && Quaternion.Angle(avatar.transform.rotation, rotation) < 0.001f, "Locomotion gate blocks both displacement and turning");
            check(published.LocalVelocity == Vector3.zero && published.AngularDegreesPerSecond == 0f, "Blocked locomotion clears motion parameters");
            canMove = true;

            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            before = avatar.transform.position;
            motor.Step(5f);
            check(Mathf.Abs(Vector3.Distance(avatar.transform.position, before) - 0.2f) < 0.0001f, "A long editor frame cannot produce an unbounded movement jump");
            before = avatar.transform.position;
            motor.Step(float.NaN);
            check(Vector3.Distance(avatar.transform.position, before) < 0.0001f && published.LocalVelocity == Vector3.zero, "Invalid delta time stops movement and publishes zero velocity");

            motor.MoveSpeed = float.NaN;
            motor.TurnSpeed = float.PositiveInfinity;
            input.Frame = new MotionInputFrame(Vector2.up, 1f);
            rotation = avatar.transform.rotation;
            motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, before) < 0.0001f && Quaternion.Angle(avatar.transform.rotation, rotation) < 0.001f, "Nonfinite speed settings cannot corrupt the avatar transform");
            motor.Halt();
            check(motor.LastResult.LocalVelocity == Vector3.zero && motor.LastResult.AngularDegreesPerSecond == 0f, "Explicit halt clears the last motion result");
            check(avatar.transform.position.y == start.y, "Gravity-disabled movement preserves avatar height");

            var inputError = new InvalidOperationException("Injected adapter failure");
            Exception reported = null;
            motor.Failed += exception => reported = exception;
            input.Error = inputError;
            motor.Step(0.1f);
            check(ReferenceEquals(reported, inputError), "External input failure is reported to the session owner");
            check(!motor.enabled && motor.LastResult.LocalVelocity == Vector3.zero, "External input failure disables the motor and stops the avatar");
            check(Vector3.Distance(avatar.transform.position, before) < 0.0001f, "An input adapter exception cannot displace the avatar");

            var keyboard = new KeyboardMotionInputSource();
            keyboard.Reset();
            check(keyboard.Suspended, "Keyboard reset requires input to be released before resuming");
            using (var session = new ControlledModeSession())
            {
                check(!session.Start(null, input), "Controlled mode rejects a start outside Play Mode");
                check(!session.Running && session.Motor == null && !string.IsNullOrEmpty(session.Error), "Rejected controlled mode start retains no active motor");
                session.Stop();
                session.Stop();
            }
        }
        finally
        {
            if (motorObject) UnityEngine.Object.DestroyImmediate(motorObject);
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
        }
    }

    public sealed class TestMotionInput : IMotionInputSource
    {
        public MotionInputFrame Frame;
        public int Resets;
        public Exception Error;
        public MotionInputFrame Sample() { if (Error != null) throw Error; return Frame; }
        public void Reset() { Resets++; Frame = default; }
    }
}
