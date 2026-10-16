using System;
using System.Reflection;
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
            var malformedLook = new MotionInputFrame(Vector2.zero, 0f,
                new Vector2(float.NaN, float.PositiveInfinity), float.NaN);
            check(malformedLook.LookDelta == Vector2.zero && malformedLook.SpeedMultiplier == 1f, "External input sanitizes nonfinite look and sprint values");
            check(new MotionInputFrame(Vector2.zero, 0f).LookDelta == Vector2.zero &&
                new MotionInputFrame(Vector2.zero, 0f).SpeedMultiplier == 1f &&
                !new MotionInputFrame(Vector2.zero, 0f).Jump &&
                new MotionInputFrame(Vector2.zero, 0f).Pose == MotionPose.None,
                "Existing two-argument input adapters retain their default behavior without requesting a pose");
            check(new MotionInputFrame(Vector2.zero, 0f, pose: (MotionPose)99).Pose == MotionPose.None &&
                new MotionInputFrame(Vector2.zero, 0f, pose: MotionPose.Crouch).Pose == MotionPose.Crouch &&
                new MotionInputFrame(Vector2.zero, 0f, pose: MotionPose.Prone).Pose == MotionPose.Prone,
                "External pose input preserves supported commands and rejects unknown enum values");
            check(new MotionInputFrame(Vector2.zero, 0f, default, 20f).SpeedMultiplier == 3f &&
                new MotionInputFrame(Vector2.zero, 0f, default, -2f).SpeedMultiplier == 1f, "Sprint input respects the public speed multiplier bounds");
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
            var poseCount = 0;
            var requestedPose = MotionPose.None;
            motor.PoseRequested += pose => { requestedPose = pose; poseCount++; };
            motor.Initialize(avatar.transform, input, 1.8f, 0.2f, 0f, () => canMove,
                result => { published = result; publishCount++; });
            check(input.Resets == 1, "Starting a motor clears input state");
            check(publishCount == 1 && published.LocalVelocity == Vector3.zero, "Initialization publishes a stationary state");
            check(!motor.Controller.enabled, "Collision-free preview leaves its controller disabled");

            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            var before = avatar.transform.position;
            var expectedDirection = motor.transform.forward;
            motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, before + expectedDirection * 0.105f) < 0.0001f,
                "Forward input accelerates from rest at 10.5 meters per second squared along the avatar yaw");
            check(Vector3.Distance(published.LocalVelocity, new Vector3(0f, 0f, 1.05f)) < 0.0001f,
                "The first forward frame publishes actual accelerated displacement in avatar-local meters per second");
            motor.Step(0.1f);
            check(Mathf.Abs(published.LocalVelocity.z - 2f) < 0.0001f,
                "Continued forward input reaches the two meter per second walking target without overshooting");
            check(published.AngularDegreesPerSecond == 0f, "Forward movement does not add turning");
            check(Vector3.Distance(avatar.transform.position, motor.transform.position) < 0.0001f, "The motor applies its displacement to the avatar");
            check(Quaternion.Angle(avatar.transform.rotation, motor.transform.rotation * initialTilt) < 0.001f, "Movement preserves the avatar's original tilt");

            motor.Halt();
            input.Frame = new MotionInputFrame(Vector2.right, 0f);
            before = avatar.transform.position;
            var expectedRight = motor.transform.right;
            motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, before + expectedRight * 0.105f) < 0.0001f,
                "Strafe input accelerates from rest along the avatar's right axis");
            motor.Step(0.1f);
            check(Vector3.Distance(published.LocalVelocity, new Vector3(2f, 0f, 0f)) < 0.0001f, "Published strafe velocity uses the local X parameter axis");

            motor.Halt();
            input.Frame = new MotionInputFrame(Vector2.one, 0f);
            before = avatar.transform.position;
            motor.Step(0.1f);
            check(Mathf.Abs(Vector3.Distance(avatar.transform.position, before) - 0.105f) < 0.0001f,
                "Diagonal acceleration has the same magnitude as cardinal acceleration");
            motor.Step(0.1f);
            check(Mathf.Abs(published.LocalVelocity.magnitude - 2f) < 0.0001f, "Diagonal velocity magnitude matches configured movement speed");

            input.Frame = new MotionInputFrame(Vector2.one, 0f, default, 2f);
            for (var step = 0; step < 4; step++) motor.Step(0.1f);
            before = avatar.transform.position;
            motor.Step(0.1f);
            var diagonalSprint = new Vector3(Mathf.Sqrt(2f), 0f, 2f * Mathf.Sqrt(2f));
            check(Vector3.Distance(published.LocalVelocity, diagonalSprint) < 0.0001f &&
                Mathf.Abs(Vector3.Distance(avatar.transform.position, before) - Mathf.Sqrt(10f) * 0.1f) < 0.0001f,
                "Diagonal sprint scales only the forward component of normalized input");
            input.Frame = new MotionInputFrame(Vector2.right, 0f, speedMultiplier: 2f);
            for (var step = 0; step < 4; step++) motor.Step(0.1f);
            check(Vector3.Distance(published.LocalVelocity, new Vector3(2f, 0f, 0f)) < 0.0001f,
                "Sprint leaves pure side movement at the two meter per second strafe speed");

            motor.Halt();
            input.Frame = new MotionInputFrame(Vector2.up, 0f, speedMultiplier: 2f);
            for (var step = 1; step <= 4; step++)
            {
                motor.Step(0.1f);
                check(Mathf.Abs(published.LocalVelocity.z - Mathf.Min(step * 1.05f, 4f)) < 0.0001f,
                    "Forward sprint accelerates toward four meters per second without overshooting, step " + step);
            }
            input.Frame = default;
            before = avatar.transform.position;
            motor.Step(0.1f);
            check(Mathf.Abs(published.LocalVelocity.z - 2.95f) < 0.0001f &&
                Vector3.Distance(avatar.transform.position, before + expectedDirection * 0.295f) < 0.0001f,
                "Releasing movement decelerates normally and publishes the remaining real displacement");
            motor.Step(0.1f);
            check(Mathf.Abs(published.LocalVelocity.z - 1.9f) < 0.0001f,
                "Continued release subtracts acceleration consistently on the next frame");
            motor.Step(0.1f);
            motor.Step(0.1f);
            check(published.LocalVelocity == Vector3.zero,
                "Deceleration reaches rest without reversing the avatar");
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            motor.Step(0.1f);
            motor.Step(0.1f);
            input.Frame = new MotionInputFrame(Vector2.down, 0f);
            motor.Step(0.1f);
            check(Mathf.Abs(published.LocalVelocity.z - 0.95f) < 0.0001f,
                "Reversing direction first decelerates existing forward motion instead of snapping backward");
            motor.Step(0.1f);
            check(Mathf.Abs(published.LocalVelocity.z + 0.1f) < 0.0001f,
                "Direction reversal crosses zero smoothly on a later frame");

            motor.Halt();
            before = avatar.transform.position;
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, pose: MotionPose.Crouch);
            motor.Step(0.02f);
            check(poseCount == 1 && requestedPose == MotionPose.Crouch && avatar.transform.position == before,
                "An explicit crouch frame raises one pose request after the movement gate accepts it");
            input.Frame = default;
            motor.Step(0.02f);
            check(poseCount == 1, "A frame without an explicit pose does not repeat the previous pose command");
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, pose: MotionPose.Prone);
            motor.Step(0.02f);
            check(poseCount == 2 && requestedPose == MotionPose.Prone,
                "An explicit prone frame reaches the motion owner as a separate pose request");

            motor.Halt();
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

            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(15f, -10f));
            beforeYaw = motor.transform.eulerAngles.y;
            motor.Step(0.01f);
            var shortFrameYaw = Mathf.DeltaAngle(beforeYaw, motor.transform.eulerAngles.y);
            beforeYaw = motor.transform.eulerAngles.y;
            motor.Step(0.1f);
            check(Mathf.Abs(shortFrameYaw - 15f) < 0.001f &&
                Mathf.Abs(Mathf.DeltaAngle(beforeYaw, motor.transform.eulerAngles.y) - 15f) < 0.001f, "Mouse look is a degree delta independent of frame duration");
            check(Mathf.Abs(motor.ViewPitch + 20f) < 0.001f, "Mouse look accumulates a separate view pitch");
            check(Quaternion.Angle(avatar.transform.rotation, motor.transform.rotation * initialTilt) < 0.001f, "Looking vertically does not tilt the avatar body");
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(0f, -500f));
            motor.Step(0.1f);
            check(motor.ViewPitch == -85f, "Looking up clamps pitch before the camera can invert");
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(0f, 500f));
            motor.Step(0.1f);
            check(motor.ViewPitch == 85f, "Looking down clamps pitch before the camera can invert");

            input.Frame = new MotionInputFrame(Vector2.zero, 0f, default, 1f, true);
            before = avatar.transform.position;
            motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, before) < 0.0001f, "Jump is ignored when gravity and collisions are disabled");

            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            motor.Step(0.1f);
            check(published.LocalVelocity.z > 1f, "The movement gate fixture starts with existing forward momentum");
            input.Frame = new MotionInputFrame(Vector2.up, 1f, pose: MotionPose.Crouch);
            canMove = false;
            before = avatar.transform.position;
            var rotation = avatar.transform.rotation;
            motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, before) < 0.0001f && Quaternion.Angle(avatar.transform.rotation, rotation) < 0.001f, "Locomotion gate blocks both displacement and turning");
            check(published.LocalVelocity == Vector3.zero && published.AngularDegreesPerSecond == 0f, "Blocked locomotion clears motion parameters");
            check(poseCount == 2, "Blocked locomotion does not apply a pose command");
            canMove = true;

            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            before = avatar.transform.position;
            motor.Step(5f);
            check(Mathf.Abs(Vector3.Distance(avatar.transform.position, before) - 0.105f) < 0.0001f,
                "A long editor frame is capped before both acceleration and displacement are integrated");
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

            using (var keyboard = new KeyboardMotionInputSource())
            {
                keyboard.Reset();
                check(keyboard.Suspended && !keyboard.GameSimulation && !keyboard.Captured, "Keyboard reset retains legacy input behavior without capturing the mouse");
            }
            using (var keyboard = new KeyboardMotionInputSource(true))
            {
                var cursorLock = Cursor.lockState;
                var cursorVisible = Cursor.visible;
                keyboard.RequestCapture();
                check(keyboard.GameSimulation && keyboard.Suspended && !keyboard.Captured, "Requesting capture defers input until a focused Game View sample");
                if (!keyboard.HasInputFocus)
                {
                    var frame = keyboard.Sample();
                    check(frame.Move == Vector2.zero && frame.LookDelta == Vector2.zero && !frame.Jump &&
                        keyboard.Suspended && !keyboard.Captured, "An unfocused supported view cannot move, look, jump, or capture the cursor");
                }
                keyboard.Reset();
                check(keyboard.Suspended && !keyboard.Captured && Cursor.lockState == cursorLock && Cursor.visible == cursorVisible, "Resetting inactive game input preserves the original cursor state");
                keyboard.RequestCapture();
                keyboard.ObserverView = true;
                keyboard.RequestCapture();
                var captureRequested = typeof(KeyboardMotionInputSource).GetField("_captureRequested", BindingFlags.Instance | BindingFlags.NonPublic);
                check(keyboard.ObserverView && !keyboard.Captured && !(bool)captureRequested.GetValue(keyboard) &&
                    Cursor.lockState == cursorLock && Cursor.visible == cursorVisible,
                    "Observer input clears a pending mouse capture and keeps the cursor free");
                keyboard.ObserverView = false;
                check(keyboard.Suspended && !keyboard.Captured,
                    "Switching from observer input to a player camera requires fresh focused input before capture");
                keyboard.Dispose();
                keyboard.RequestCapture();
                check(keyboard.Sample().Move == Vector2.zero && !keyboard.Captured, "Disposed keyboard input cannot capture or resume movement");
            }
            CheckGameCameraInput(check);
            CheckGameFreeLookInput(check);
            CheckSceneInput(check);
            CheckFreeLook(check);
            CheckStandingHeight(check);
            CheckJump(check);
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

    private static void CheckGameCameraInput(Action<bool, string> check)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
        var cursorLock = Cursor.lockState;
        var cursorVisible = Cursor.visible;
        using (var keyboard = new KeyboardMotionInputSource(true))
        {
            var toggleCount = 0;
            var zoomCount = 0;
            var zoom = 0f;
            keyboard.ViewToggleRequested += () => toggleCount++;
            keyboard.CameraZoomRequested += delta => { zoom += delta; zoomCount++; };
            var process = typeof(KeyboardMotionInputSource).GetMethod("ProcessGameCameraInput", any);
            Action<bool, float, bool, bool> send = (toggle, scroll, modified, shift) =>
                process.Invoke(keyboard, new object[] { toggle, scroll, modified, shift });
            var captured = typeof(KeyboardMotionInputSource).GetField("_captured", any);
            var suspended = typeof(KeyboardMotionInputSource).GetField("<Suspended>k__BackingField", any);
            typeof(KeyboardMotionInputSource).GetField("_previousCursorLock", any).SetValue(keyboard, cursorLock);
            typeof(KeyboardMotionInputSource).GetField("_previousCursorVisible", any).SetValue(keyboard, cursorVisible);

            keyboard.ObserverView = true;
            suspended.SetValue(keyboard, false);
            send(true, 1f, false, false);
            check(toggleCount == 1 && zoomCount == 0 && !keyboard.Captured,
                "The post-gate Game camera dispatcher allows F5 from observer mode while ignoring observer wheel input");
            send(true, 1f, true, false);
            send(true, 0f, false, true);
            send(false, 0f, false, false);
            check(toggleCount == 1 && zoomCount == 0,
                "Modified or Shift F5 does not toggle the player camera and a later frame cannot replay an old toggle");
            keyboard.ObserverView = false;
            suspended.SetValue(keyboard, false);
            send(false, 1f, false, false);
            check(zoomCount == 0, "An uncaptured player view cannot consume Game camera wheel input");
            captured.SetValue(keyboard, true);
            suspended.SetValue(keyboard, true);
            send(false, 1f, false, false);
            check(zoomCount == 0, "A suspended player camera cannot consume wheel input even while capture is retained");
            suspended.SetValue(keyboard, false);
            send(false, 1f, false, false);
            send(true, -0.5f, false, true);
            check(toggleCount == 1 && zoomCount == 2 && Mathf.Abs(zoom - 0.5f) < 0.0001f,
                "A captured Game camera receives signed wheel deltas while Shift filters only F5");
            send(true, 2f, true, false);
            send(false, float.NaN, false, false);
            send(false, float.PositiveInfinity, false, false);
            send(false, float.NegativeInfinity, false, false);
            send(false, 0f, false, false);
            check(toggleCount == 1 && zoomCount == 2,
                "Editor modifiers and nonfinite or zero scroll values produce no Game camera events");
            send(true, 0f, false, false);
            check(toggleCount == 2, "A new unmodified F5 request produces exactly one new toggle event");
            keyboard.Reset();
            send(false, 0f, false, false);
            if (!keyboard.HasInputFocus) keyboard.Sample();
            check(toggleCount == 2 && zoomCount == 2 && !keyboard.Captured && keyboard.Suspended &&
                Cursor.lockState == cursorLock && Cursor.visible == cursorVisible,
                "Reset and unfocused sampling release camera input without replaying toggle or wheel events");
            keyboard.Dispose();
            check(typeof(KeyboardMotionInputSource).GetField("ViewToggleRequested", any).GetValue(keyboard) == null &&
                typeof(KeyboardMotionInputSource).GetField("CameraZoomRequested", any).GetValue(keyboard) == null,
                "Disposing Game input clears both player camera event subscriptions");
            send(true, 1f, false, false);
            check(toggleCount == 2 && zoomCount == 2,
                "Disposed Game input cannot deliver deferred player camera actions");
        }
        check(Cursor.lockState == cursorLock && Cursor.visible == cursorVisible,
            "Deterministic Game camera dispatch checks leave the real cursor state unchanged");
    }

    private static void CheckGameFreeLookInput(Action<bool, string> check)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
        var cursorLock = Cursor.lockState;
        var cursorVisible = Cursor.visible;
        var process = typeof(KeyboardMotionInputSource).GetMethod("ProcessGameFreeLookInput", any);
        var captured = typeof(KeyboardMotionInputSource).GetField("_captured", any);
        var suspended = typeof(KeyboardMotionInputSource).GetField("<Suspended>k__BackingField", any);
        using (var legacy = new KeyboardMotionInputSource())
        {
            var events = 0;
            legacy.FreeLookRequested += () => events++;
            legacy.ForwardMovementRequested += () => events++;
            legacy.FreeLook = true;
            captured.SetValue(legacy, true);
            suspended.SetValue(legacy, false);
            typeof(KeyboardMotionInputSource).GetField("_previousCursorLock", any).SetValue(legacy, cursorLock);
            typeof(KeyboardMotionInputSource).GetField("_previousCursorVisible", any).SetValue(legacy, cursorVisible);
            process.Invoke(legacy, new object[] { true, true, false, false });
            check(events == 0, "Legacy motion input cannot dispatch Game-only free-look or forward-resume events");
        }
        using (var keyboard = new KeyboardMotionInputSource(true))
        {
            var orbitCount = 0;
            var resumeCount = 0;
            keyboard.FreeLookRequested += () => { orbitCount++; keyboard.FreeLook = true; };
            keyboard.ForwardMovementRequested += () => { resumeCount++; keyboard.FreeLook = false; };
            Action<bool, bool, bool, bool> send = (tab, forward, modified, shift) =>
                process.Invoke(keyboard, new object[] { tab, forward, modified, shift });
            typeof(KeyboardMotionInputSource).GetField("_previousCursorLock", any).SetValue(keyboard, cursorLock);
            typeof(KeyboardMotionInputSource).GetField("_previousCursorVisible", any).SetValue(keyboard, cursorVisible);
            keyboard.ObserverView = true;
            suspended.SetValue(keyboard, false);
            send(true, true, false, false);
            keyboard.ObserverView = false;
            suspended.SetValue(keyboard, false);
            send(true, true, false, false);
            captured.SetValue(keyboard, true);
            suspended.SetValue(keyboard, true);
            send(true, true, false, false);
            suspended.SetValue(keyboard, false);
            send(true, true, true, false);
            send(true, false, false, true);
            send(false, true, false, false);
            check(orbitCount == 0 && resumeCount == 0 && !keyboard.FreeLook,
                "Observer, uncaptured, suspended, modified, and Shift-Tab input cannot enter free look or spuriously resume movement");
            send(true, false, false, false);
            send(false, false, false, false);
            check(orbitCount == 1 && resumeCount == 0 && keyboard.FreeLook,
                "Captured Tab enters free look while an already-held forward key does not act as a new forward press");
            send(true, false, false, false);
            check(orbitCount == 2 && keyboard.FreeLook,
                "A repeated Tab request remains an enable request instead of disabling free look");
            var create = typeof(KeyboardMotionInputSource).GetMethod("CreateGameFrame", any);
            var look = new Vector2(4f, -3f);
            MotionInputFrame frame(Vector2 move, float turn, bool sprint = false) =>
                (MotionInputFrame)create.Invoke(keyboard, new object[] { move, turn, look, sprint, true, MotionPose.Crouch });
            var turning = frame(Vector2.right, -0.25f, true);
            var advancing = frame(new Vector2(-1f, 1f), 0f);
            check(turning.Move == Vector2.zero && turning.Turn == 0.75f && turning.LookDelta == look &&
                turning.SpeedMultiplier == 2f && turning.Jump && turning.Pose == MotionPose.Crouch,
                "Free-look A/D become additive body turning without losing mouse, sprint, jump or posture input");
            check(advancing.Move == Vector2.up && advancing.Turn == -1f,
                "Free-look forward movement retains full speed while simultaneous A turns the body without strafing");
            send(false, true, false, false);
            var strafing = frame(Vector2.right, -0.25f);
            check(resumeCount == 1 && !keyboard.FreeLook && strafing.Move == Vector2.right && strafing.Turn == -0.25f,
                "A new W/S press resumes normal movement, restoring A/D strafing independently of comma/period turning");
            send(true, false, false, false);
            send(false, true, false, true);
            check(resumeCount == 2 && !keyboard.FreeLook,
                "Shift plus a new forward press can resume normal movement even though Shift-Tab is reserved");
            send(true, false, false, false);
            var beforeReset = orbitCount;
            keyboard.Reset();
            send(true, true, false, false);
            check(keyboard.FreeLook && !keyboard.Captured && keyboard.Suspended && orbitCount == beforeReset && resumeCount == 2,
                "Reset releases input while preserving the orbit mode and cannot replay Tab or forward-resume requests");
            var sceneEvent = typeof(KeyboardMotionInputSource).GetMethod("ProcessSceneEvent", any);
            var sceneSample = typeof(KeyboardMotionInputSource).GetMethod("SampleSceneInput", any);
            var tab = new Event { type = EventType.KeyDown, keyCode = KeyCode.Tab };
            sceneEvent.Invoke(keyboard, new object[] { tab });
            sceneEvent.Invoke(keyboard, new object[] { new Event { type = EventType.KeyDown, keyCode = KeyCode.A } });
            var sceneFrame = (MotionInputFrame)sceneSample.Invoke(keyboard, null);
            check(tab.type == EventType.KeyDown && sceneFrame.Move == Vector2.left && sceneFrame.Turn == 0f &&
                orbitCount == beforeReset && resumeCount == 2,
                "Scene input leaves Tab with the editor and keeps A/D strafing even when Game free look is enabled");
            keyboard.Dispose();
            check(!keyboard.FreeLook &&
                typeof(KeyboardMotionInputSource).GetField("FreeLookRequested", any).GetValue(keyboard) == null &&
                typeof(KeyboardMotionInputSource).GetField("ForwardMovementRequested", any).GetValue(keyboard) == null,
                "Disposing keyboard input clears the orbit state and both free-look event subscriptions");
            send(true, true, false, false);
            check(orbitCount == beforeReset && resumeCount == 2,
                "Disposed keyboard input cannot deliver free-look or forward-resume actions");
        }
        check(Cursor.lockState == cursorLock && Cursor.visible == cursorVisible,
            "Free-look input checks preserve the real cursor state");
    }

    private static void CheckFreeLook(Action<bool, string> check)
    {
        GameObject avatar = null;
        GameObject motorObject = null;
        GameObject floor = null;
        GameObject wall = null;
        try
        {
            var start = new Vector3(1200f, 0f, 1200f);
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = start - Vector3.up * 0.1f;
            floor.transform.localScale = new Vector3(10f, 0.2f, 10f);
            avatar = new GameObject("Pro validation free-look avatar");
            avatar.transform.SetPositionAndRotation(start, Quaternion.Euler(8f, 90f, 4f));
            var tilt = Quaternion.Inverse(Quaternion.Euler(0f, avatar.transform.eulerAngles.y, 0f)) * avatar.transform.rotation;
            motorObject = new GameObject("Pro validation free-look motor");
            var motor = motorObject.AddComponent<CharacterMotionMotor>();
            motor.UseGravity = true;
            var input = new TestMotionInput();
            var canMove = true;
            motor.Initialize(avatar.transform, input, 1.8f, 0.2f, 0f, () => canMove, _ => { });
            Physics.SyncTransforms();
            motor.Step(0.02f);
            var body = motor.transform.rotation;
            var position = avatar.transform.position;
            motor.EnableFreeLook();
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(75f, -15f));
            motor.Step(0.02f);
            check(motor.FreeLook && Mathf.Abs(Mathf.DeltaAngle(165f, motor.ViewYaw)) < 0.001f && motor.ViewPitch == -15f &&
                Quaternion.Angle(body, motor.transform.rotation) < 0.001f && Vector3.Distance(position, avatar.transform.position) < 0.0005f &&
                motor.LastResult.AngularDegreesPerSecond == 0f,
                "Free mouse look changes only camera yaw and pitch while the standing avatar remains still");
            motor.EnableFreeLook();
            check(Mathf.Abs(Mathf.DeltaAngle(165f, motor.ViewYaw)) < 0.001f,
                "Repeated free-look enable calls do not recenter the existing camera orbit");
            input.Frame = new MotionInputFrame(Vector2.zero, 1f, new Vector2(0f, 5f));
            motor.Step(0.1f);
            check(Mathf.Abs(Mathf.DeltaAngle(102f, motor.transform.eulerAngles.y)) < 0.001f &&
                Mathf.Abs(Mathf.DeltaAngle(165f, motor.ViewYaw)) < 0.001f && motor.ViewPitch == -10f &&
                Mathf.Abs(motor.LastResult.AngularDegreesPerSecond - 120f) < 0.01f &&
                Vector3.Distance(position, avatar.transform.position) < 0.0005f,
                "Free-look body turning rotates in place and publishes AngularY without rotating the camera yaw");
            input.Frame = new MotionInputFrame(Vector2.zero, -1f);
            motor.Step(0.1f);
            body = motor.transform.rotation;
            motor.DisableFreeLook();
            check(!motor.FreeLook && Quaternion.Angle(body, motor.transform.rotation) < 0.001f &&
                Mathf.Abs(Mathf.DeltaAngle(motor.ViewYaw, motor.transform.eulerAngles.y)) < 0.001f,
                "Leaving free look without alignment restores body-relative camera yaw without rotating the avatar");
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            motor.Step(0.1f);
            motor.EnableFreeLook();
            position = avatar.transform.position;
            input.Frame = new MotionInputFrame(Vector2.up, 0f, new Vector2(-60f, 0f));
            motor.Step(0.1f);
            check(motor.FreeLook && Mathf.Abs(Mathf.DeltaAngle(30f, motor.ViewYaw)) < 0.001f &&
                Quaternion.Angle(body, motor.transform.rotation) < 0.001f && avatar.transform.position.x > position.x + 0.19f,
                "Enabling free look during held forward movement keeps walking along the existing body heading");
            motor.Halt();
            motor.DisableFreeLook(true);
            motor.DisableFreeLook(true);
            position = avatar.transform.position;
            check(Quaternion.Angle(body, motor.transform.rotation) < 0.001f,
                "Requesting forward resumption queues alignment instead of rotating outside the physics step");
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, position + Quaternion.Euler(0f, 30f, 0f) * Vector3.forward * 0.105f) < 0.0005f &&
                Mathf.Abs(Mathf.DeltaAngle(30f, motor.transform.eulerAngles.y)) < 0.001f &&
                Mathf.Abs(motor.LastResult.AngularDegreesPerSecond + 600f) < 0.01f,
                "A new forward press aligns to the former camera yaw, moves in that direction and publishes the alignment in AngularY");
            motor.Halt();
            motor.EnableFreeLook();
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(120f, 0f));
            motor.Step(0.02f);
            motor.DisableFreeLook(true);
            position = avatar.transform.position;
            input.Frame = new MotionInputFrame(Vector2.down, 0f);
            motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, position - Quaternion.Euler(0f, 150f, 0f) * Vector3.forward * 0.105f) < 0.0005f &&
                Mathf.Abs(Mathf.DeltaAngle(150f, motor.transform.eulerAngles.y)) < 0.001f &&
                Mathf.Abs(motor.LastResult.AngularDegreesPerSecond - 1200f) < 0.01f,
                "A new backward press aligns the body to the camera yaw and then moves backward rather than facing the travel direction");
            motor.Halt();
            motor.EnableFreeLook();
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(35f, 0f));
            motor.Step(0.02f);
            motor.DisableFreeLook(true);
            body = motor.transform.rotation;
            canMove = false;
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            motor.Step(0.1f);
            check(Quaternion.Angle(body, motor.transform.rotation) < 0.001f && motor.LastResult.AngularDegreesPerSecond == 0f,
                "Blocked locomotion cannot apply a queued free-look alignment");
            canMove = true;
            motor.Step(0.1f);
            check(Mathf.Abs(Mathf.DeltaAngle(185f, motor.transform.eulerAngles.y)) < 0.001f &&
                Mathf.Abs(motor.LastResult.AngularDegreesPerSecond - 350f) < 0.01f,
                "Queued alignment is consumed only after the movement gate accepts a physics step");
            motor.Halt();
            motor.EnableFreeLook();
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(55f, 0f));
            motor.Step(0.02f);
            motor.DisableFreeLook(true);
            motor.DisableFreeLook();
            body = motor.transform.rotation;
            input.Frame = default;
            motor.Step(0.02f);
            check(Quaternion.Angle(body, motor.transform.rotation) < 0.001f && motor.LastResult.AngularDegreesPerSecond == 0f,
                "A view change can cancel pending free-look alignment without rotating the model later");
            motor.EnableFreeLook();
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(180f, 180f));
            for (var step = 0; step < 5; step++) motor.Step(0.02f);
            var viewYaw = motor.ViewYaw;
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(float.NaN, float.PositiveInfinity));
            motor.Step(0.02f);
            check(motor.ViewYaw >= 0f && motor.ViewYaw < 360f && motor.ViewYaw == viewYaw && motor.ViewPitch == 85f &&
                Quaternion.Angle(body, motor.transform.rotation) < 0.001f,
                "Free-look angles wrap and clamp while nonfinite external look input cannot corrupt camera or body state");
            motor.DisableFreeLook();
            motor.transform.SetPositionAndRotation(start, Quaternion.identity);
            avatar.transform.SetPositionAndRotation(start, tilt);
            Physics.SyncTransforms();
            motor.Halt();
            motor.EnableFreeLook();
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(90f, 0f));
            motor.Step(0.02f);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = start + new Vector3(0f, 1.5f, 1.2f);
            wall.transform.localScale = new Vector3(4f, 3f, 0.1f);
            Physics.SyncTransforms();
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            for (var step = 0; step < 120; step++) motor.Step(0.02f);
            check(motor.FreeLook && Mathf.Abs(Mathf.DeltaAngle(90f, motor.ViewYaw)) < 0.001f &&
                motor.LastResult.Grounded && Mathf.Abs(motor.LastResult.LocalVelocity.z) < 0.001f &&
                avatar.transform.position.z + motor.Controller.radius <= wall.GetComponent<Collider>().bounds.min.z + 0.0005f &&
                Mathf.Abs(avatar.transform.position.y) < 0.0005f,
                "Free-look movement retains real wall blocking and grounded standing height while the camera faces independently");
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, jump: true);
            motor.Step(0.02f);
            check(avatar.transform.position.y > 0.01f && !motor.LastResult.Grounded && motor.FreeLook,
                "Free-look orbit does not disable grounded jumping or its airborne transition");
            input.Frame = default;
            for (var step = 0; step < 120; step++) motor.Step(0.02f);
            check(motor.LastResult.Grounded && Mathf.Abs(avatar.transform.position.y) < 0.0005f &&
                Mathf.Abs(Mathf.DeltaAngle(90f, motor.ViewYaw)) < 0.001f &&
                Quaternion.Angle(avatar.transform.rotation, motor.transform.rotation * tilt) < 0.001f,
                "Free-look jumping lands normally and preserves the original model tilt and independent camera yaw");
        }
        finally
        {
            if (motorObject) UnityEngine.Object.DestroyImmediate(motorObject);
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            if (wall) UnityEngine.Object.DestroyImmediate(wall);
            if (floor) UnityEngine.Object.DestroyImmediate(floor);
            Physics.SyncTransforms();
        }
    }

    private static void CheckSceneInput(Action<bool, string> check)
    {
        // Inject Scene GUI events into the same state machine used by
        // SceneView.duringSceneGui. This checks event ownership and motor behavior,
        // without claiming to reproduce physical input or editor focus changes.
        GameObject avatar = null;
        GameObject motorObject = null;
        var cursorLock = Cursor.lockState;
        var cursorVisible = Cursor.visible;
        using (var keyboard = new KeyboardMotionInputSource(true))
        {
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var process = typeof(KeyboardMotionInputSource).GetMethod("ProcessSceneEvent", flags);
                var sample = typeof(KeyboardMotionInputSource).GetMethod("SampleSceneInput", flags);
                Action<Event> send = current => process.Invoke(keyboard, new object[] { current });
                Func<MotionInputFrame> read = () => (MotionInputFrame)sample.Invoke(keyboard, null);
                avatar = new GameObject("Pro Scene input avatar");
                motorObject = new GameObject("Pro Scene input motor");
                var motor = motorObject.AddComponent<CharacterMotionMotor>();
                motor.Collisions = false;
                var input = new TestMotionInput();
                motor.Initialize(avatar.transform, input, 1.8f, 0.2f, 0f, () => !keyboard.Suspended, _ => { });
                var forward = new Event { type = EventType.KeyDown, keyCode = KeyCode.W };
                send(forward);
                input.Frame = read();
                motor.Step(0.1f);
                check(forward.type == EventType.Used && Vector3.Distance(avatar.transform.position, Vector3.forward * 0.105f) < 0.0001f,
                    "Scene WASD consumes the model movement key and accelerates the actual avatar through its motor");
                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.D });
                input.Frame = read();
                motor.Halt();
                var before = avatar.transform.position;
                motor.Step(0.1f);
                check(Mathf.Abs(Vector3.Distance(before, avatar.transform.position) - 0.105f) < 0.0001f &&
                    Mathf.Abs(input.Frame.Move.magnitude - 1f) < 0.0001f,
                    "Scene diagonal input retains normalized cardinal acceleration");
                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.Period });
                input.Frame = read();
                var beforeYaw = avatar.transform.eulerAngles.y;
                motor.Step(0.1f);
                check(Mathf.Abs(Mathf.DeltaAngle(beforeYaw, avatar.transform.eulerAngles.y) - 12f) < 0.001f,
                    "Scene period input turns the avatar using the configured turn rate");
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.W });
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.D });
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.Period });
                var frame = read();
                check(frame.Move == Vector2.zero && frame.Turn == 0f, "Scene key release clears movement and turning");

                motor.Halt();
                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.W });
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.W });
                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.Period });
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.Period });
                input.Frame = read();
                before = avatar.transform.position;
                beforeYaw = avatar.transform.eulerAngles.y;
                motor.Step(0.1f);
                check(input.Frame.Move == Vector2.up && input.Frame.Turn == 1f &&
                    Mathf.Abs(Vector3.Distance(before, avatar.transform.position) - 0.105f) < 0.0001f &&
                    Mathf.Abs(Mathf.DeltaAngle(beforeYaw, avatar.transform.eulerAngles.y) - 12f) < 0.001f,
                    "Scene movement and turn taps completed before a motor update still produce one input frame");
                input.Frame = read();
                before = avatar.transform.position;
                beforeYaw = avatar.transform.eulerAngles.y;
                motor.Step(0.1f);
                check(input.Frame.Move == Vector2.zero && input.Frame.Turn == 0f &&
                    avatar.transform.position == before && Mathf.Abs(Mathf.DeltaAngle(beforeYaw, avatar.transform.eulerAngles.y)) < 0.001f,
                    "A completed Scene tap is consumed once and cannot replay on the next motor update");
                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.W });
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.W });
                send(new Event { type = EventType.MouseDown, button = 1 });
                send(new Event { type = EventType.MouseUp, button = 1 });
                check(read().Move == Vector2.zero && !keyboard.Suspended,
                    "Starting Scene navigation before sampling discards a queued movement tap");
                send(new Event { type = EventType.MouseDown, button = 1 });
                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.W });
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.W });
                send(new Event { type = EventType.MouseUp, button = 1 });
                check(read().Move == Vector2.zero && !keyboard.Suspended,
                    "A movement tap reserved for Scene navigation is never queued for later avatar movement");
                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.Period });
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.Period });
                keyboard.Reset();
                send(new Event { type = EventType.Repaint });
                check(read().Turn == 0f && !keyboard.Suspended,
                    "Reset discards unsampled Scene turn taps so focus recovery cannot replay them");

                var right = new Event { type = EventType.MouseDown, button = 1 };
                var rightType = right.type;
                send(right);
                var flyKey = new Event { type = EventType.KeyDown, keyCode = KeyCode.W };
                send(flyKey);
                check(right.type == rightType && right.rawType == EventType.MouseDown && flyKey.type == EventType.KeyDown && read().Move == Vector2.zero && keyboard.Suspended,
                    "Right-mouse Scene navigation retains its mouse and WASD events while pausing avatar input");
                send(new Event { type = EventType.MouseUp, button = 1 });
                check(read().Move == Vector2.zero && keyboard.Suspended,
                    "Ending Scene fly navigation cannot resume an avatar key that is still held");
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.W });
                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.W });
                check(read().Move == Vector2.up && !keyboard.Suspended, "Releasing and pressing the fly key again resumes Scene avatar input");
                var pan = new Event { type = EventType.MouseDown, button = 2 };
                var panType = pan.type;
                send(pan);
                check(pan.type == panType && pan.rawType == EventType.MouseDown && read().Move == Vector2.zero && keyboard.Suspended,
                    "Middle-mouse Scene panning keeps its event and pauses existing avatar movement");
                send(new Event { type = EventType.MouseUp, button = 2 });
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.W });

                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.W });
                var orbit = new Event { type = EventType.KeyDown, keyCode = KeyCode.LeftAlt, modifiers = EventModifiers.Alt };
                send(orbit);
                var orbitKey = new Event { type = EventType.KeyDown, keyCode = KeyCode.D, modifiers = EventModifiers.Alt };
                send(orbitKey);
                check(orbit.type == EventType.KeyDown && orbitKey.type == EventType.KeyDown && read().Move == Vector2.zero,
                    "Alt Scene orbit leaves navigation events untouched and suspends avatar movement");
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.LeftAlt });
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.W });
                check(read().Move == Vector2.zero && keyboard.Suspended, "Every movement key used during navigation must be released before Scene control resumes");
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.D });
                check(!keyboard.Suspended && read().Move == Vector2.zero, "Scene control rearms after all navigation movement keys are released");
                var shortcut = new Event { type = EventType.KeyDown, keyCode = KeyCode.W, modifiers = EventModifiers.Control };
                send(shortcut);
                check(shortcut.type == EventType.KeyDown && read().Move == Vector2.zero, "Modified editor shortcuts are not consumed as Scene avatar movement");

                // The release may occur in another window, so reset must drop both
                // held keys and navigation buttons rather than waiting indefinitely.
                send(new Event { type = EventType.MouseDown, button = 1 });
                keyboard.Reset();
                check(keyboard.Suspended && !keyboard.Captured, "A focus reset immediately suspends Scene input without capturing the mouse");
                send(new Event { type = EventType.Repaint });
                check(!keyboard.Suspended && read().Move == Vector2.zero,
                    "Scene focus recovery clears a missed off-view key or mouse release without getting stuck");
                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.D });
                check(read().Move == Vector2.right, "A fresh Scene key works after focus recovery and does not retain the previous forward key");
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.D });

                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.LeftShift, modifiers = EventModifiers.Shift });
                var sprint = new Event { type = EventType.KeyDown, keyCode = KeyCode.W, modifiers = EventModifiers.Shift };
                send(sprint);
                frame = read();
                check(sprint.type == EventType.Used && frame.Move == Vector2.up && frame.SpeedMultiplier == 2f,
                    "Scene Shift plus forward movement requests running without reserving the key for editor navigation");
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.W, modifiers = EventModifiers.Shift });
                send(new Event { type = EventType.KeyDown, keyCode = KeyCode.D, modifiers = EventModifiers.Shift });
                frame = read();
                check(frame.Move == Vector2.right && frame.SpeedMultiplier == 2f,
                    "Scene input carries the held Shift modifier while the motor owns forward-only sprint scaling");
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.D, modifiers = EventModifiers.Shift });
                send(new Event { type = EventType.KeyUp, keyCode = KeyCode.LeftShift });
                check(read().SpeedMultiplier == 1f, "Releasing Scene Shift returns to the walking multiplier");

                void CheckSceneAction(KeyCode key, MotionPose pose)
                {
                    var down = new Event { type = EventType.KeyDown, keyCode = key };
                    var up = new Event { type = EventType.KeyUp, keyCode = key };
                    send(down);
                    send(up);
                    var action = read();
                    check(down.type == EventType.Used && up.type == EventType.Used &&
                        action.Jump == (key == KeyCode.Space) && action.Pose == pose,
                        "A complete short Scene action press survives until one motor sample: " + key);
                    action = read();
                    check(!action.Jump && action.Pose == MotionPose.None,
                        "A completed Scene action is consumed once instead of replaying: " + key);
                    send(new Event { type = EventType.KeyDown, keyCode = key });
                    action = read();
                    check(action.Jump == (key == KeyCode.Space) && action.Pose == pose,
                        "A fresh Scene action press can request another command: " + key);
                    send(new Event { type = EventType.KeyDown, keyCode = key });
                    action = read();
                    check(!action.Jump && action.Pose == MotionPose.None,
                        "Holding or repeating a Scene action key cannot toggle it every frame: " + key);
                    send(new Event { type = EventType.KeyUp, keyCode = key });
                    read();

                    send(new Event { type = EventType.KeyDown, keyCode = key });
                    send(new Event { type = EventType.KeyUp, keyCode = key });
                    send(new Event { type = EventType.MouseDown, button = 1 });
                    send(new Event { type = EventType.MouseUp, button = 1 });
                    action = read();
                    check(!action.Jump && action.Pose == MotionPose.None,
                        "Entering Scene navigation discards a queued action: " + key);
                    send(new Event { type = EventType.MouseDown, button = 1 });
                    var navigationDown = new Event { type = EventType.KeyDown, keyCode = key };
                    send(navigationDown);
                    send(new Event { type = EventType.KeyUp, keyCode = key });
                    send(new Event { type = EventType.MouseUp, button = 1 });
                    action = read();
                    check(navigationDown.type == EventType.KeyDown && !action.Jump && action.Pose == MotionPose.None,
                        "An action reserved for Scene navigation is not replayed on returning to avatar input: " + key);

                    send(new Event { type = EventType.KeyDown, keyCode = key });
                    send(new Event { type = EventType.KeyUp, keyCode = key });
                    keyboard.Reset();
                    send(new Event { type = EventType.Repaint });
                    action = read();
                    check(!action.Jump && action.Pose == MotionPose.None,
                        "Reset discards queued Scene actions along with held keys: " + key);
                }
                CheckSceneAction(KeyCode.Space, MotionPose.None);
                CheckSceneAction(KeyCode.C, MotionPose.Crouch);
                CheckSceneAction(KeyCode.Z, MotionPose.Prone);
                var toggle = new Event { type = EventType.KeyDown, keyCode = KeyCode.V };
                var playerCamera = new Event { type = EventType.KeyDown, keyCode = KeyCode.F5 };
                var wheel = new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, -3f) };
                var viewChanges = 0;
                var zoomChanges = 0;
                keyboard.ViewToggleRequested += () => viewChanges++;
                keyboard.CameraZoomRequested += _ => zoomChanges++;
                var look = new Event { type = EventType.MouseDrag, button = 0, delta = new Vector2(30f, 20f) };
                var lookType = look.type;
                send(toggle);
                send(playerCamera);
                send(wheel);
                send(look);
                frame = read();
                check(toggle.type == EventType.KeyDown && playerCamera.type == EventType.KeyDown && wheel.type == EventType.ScrollWheel &&
                    look.type == lookType && look.rawType == EventType.MouseDrag && viewChanges == 0 && zoomChanges == 0 &&
                    frame.LookDelta == Vector2.zero && !frame.Jump && frame.Pose == MotionPose.None && !keyboard.Captured,
                    "Scene control leaves F5, V, wheel navigation, and mouse look to the editor without player camera events or cursor capture");
                keyboard.Dispose();
                check(keyboard.Sample().Move == Vector2.zero && keyboard.Suspended && !keyboard.Captured,
                    "Disposing Scene input clears latched movement and prevents further sampling");
                check(Cursor.lockState == cursorLock && Cursor.visible == cursorVisible,
                    "Scene model movement and navigation leave the original cursor state unchanged");
            }
            finally
            {
                if (motorObject) UnityEngine.Object.DestroyImmediate(motorObject);
                if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            }
        }
    }

    private static void CheckStandingHeight(Action<bool, string> check)
    {
        // Height, radius and support-foot offset. Include the reported small
        // avatar, clamped skin widths, and adapters with a nonzero foot offset.
        var capsules = new[]
        {
            new Vector3(1.1555555f, 0.1386667f, 0f),
            new Vector3(0.2f, 0.04f, 0f),
            new Vector3(1.8f, 0.2f, 0.12f),
            new Vector3(4f, 0.5f, -0.08f)
        };
        const float heightTolerance = 0.0005f;
        foreach (var capsule in capsules)
        {
            GameObject floor = null;
            GameObject avatar = null;
            GameObject motorObject = null;
            try
            {
                var start = new Vector3(1150f, 0f, 1150f);
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.position = start + Vector3.up * (capsule.z - 0.1f);
                floor.transform.localScale = new Vector3(10f, 0.2f, 10f);
                avatar = new GameObject("Pro standing-height avatar");
                avatar.transform.position = start;
                var visual = new GameObject("Unchanged model transform").transform;
                visual.SetParent(avatar.transform, false);
                visual.localPosition = new Vector3(0.04f, 0.6f, -0.1f);
                visual.localRotation = Quaternion.Euler(3f, 8f, 11f);
                visual.localScale = new Vector3(0.7f, 1.2f, 0.9f);
                var visualPosition = visual.localPosition;
                var visualRotation = visual.localRotation;
                var visualScale = visual.localScale;
                Physics.SyncTransforms();
                motorObject = new GameObject("Pro standing-height motor");
                var motor = motorObject.AddComponent<CharacterMotionMotor>();
                motor.UseGravity = true;
                var input = new TestMotionInput();
                motor.Initialize(avatar.transform, input, capsule.x, capsule.y, capsule.z, () => true, _ => { });
                check(Vector3.Distance(avatar.transform.position, start) < heightTolerance && motor.LastResult.Grounded,
                    "Initialization preserves standing height on an existing floor for capsule " + capsule);
                motor.Step(0.02f);
                check(Mathf.Abs(avatar.transform.position.y - start.y) < heightTolerance && motor.LastResult.Grounded,
                    "The first gravity/collision frame does not lift the avatar by its skin width for capsule " + capsule);
                var maximumDrift = 0f;
                var stayedGrounded = true;
                for (var frame = 0; frame < 180; frame++)
                {
                    motor.Step(0.02f);
                    maximumDrift = Mathf.Max(maximumDrift, Mathf.Abs(avatar.transform.position.y - start.y));
                    stayedGrounded &= motor.LastResult.Grounded;
                }
                check(maximumDrift < heightTolerance && stayedGrounded && motor.LastResult.LocalVelocity.magnitude < 0.001f,
                    "Repeated stationary contact preserves height and publishes no motion for capsule " + capsule);
                check(visual.parent == avatar.transform && visual.localPosition == visualPosition &&
                    Quaternion.Angle(visual.localRotation, visualRotation) < 0.001f && visual.localScale == visualScale,
                    "Contact alignment leaves model-local transforms unchanged for capsule " + capsule);
            }
            finally
            {
                if (motorObject) UnityEngine.Object.DestroyImmediate(motorObject);
                if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
                if (floor) UnityEngine.Object.DestroyImmediate(floor);
            }
        }
    }

    private static void CheckJump(Action<bool, string> check)
    {
        GameObject floor = null;
        GameObject wall = null;
        GameObject ceiling = null;
        GameObject avatar = null;
        GameObject motorObject = null;
        try
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Pro validation jump floor";
            floor.transform.position = new Vector3(1000f, -0.1f, 1000f);
            floor.transform.localScale = new Vector3(10f, 0.2f, 10f);
            avatar = new GameObject("Pro validation jump avatar");
            avatar.transform.position = new Vector3(1000f, 0f, 1000f);
            motorObject = new GameObject("Pro validation jump motor");
            var input = new TestMotionInput();
            var motor = motorObject.AddComponent<CharacterMotionMotor>();
            motor.UseGravity = true;
            check(motor.JumpHeight == 0f && motor.JumpImpulse == 3f && Physics.gravity.y < 0f,
                "Default jumping uses the VRChat world impulse without deriving a jump height from avatar size");
            motor.Initialize(avatar.transform, input, 1.8f, 0.2f, 0f, () => true, _ => { });
            Physics.SyncTransforms();
            motor.Step(0.02f);
            check(motor.LastResult.Grounded, "A collision-enabled motor detects a level floor before jumping");
            var floorHeight = avatar.transform.position.y;
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, default, 1f, true);
            motor.Step(0.02f);
            var firstJumpVelocity = motor.LastResult.LocalVelocity.y;
            check(avatar.transform.position.y > floorHeight && firstJumpVelocity > 0f && !motor.LastResult.Grounded, "A grounded jump launches the character upward and clears Grounded");
            check(Mathf.Abs(firstJumpVelocity - (3f + Physics.gravity.y * 0.02f)) < 0.005f,
                "The first airborne frame integrates the default three meter per second impulse with the actual scene gravity");
            motor.Step(0.02f);
            check(motor.LastResult.LocalVelocity.y > 0f && motor.LastResult.LocalVelocity.y < firstJumpVelocity, "A second airborne jump request cannot reset upward velocity");
            motor.Halt();
            check(motor.LastResult.LocalVelocity == Vector3.zero,
                "Explicit halt immediately clears an airborne motor's published vertical and horizontal motion");
            input.Frame = default;
            motor.Step(0.02f);
            check(Mathf.Abs(motor.LastResult.LocalVelocity.y - Physics.gravity.y * 0.02f) < 0.005f,
                "Resuming an airborne halted motor starts gravity from zero instead of retaining the old jump velocity");
            for (var step = 0; step < 120; step++) motor.Step(0.02f);
            check(motor.LastResult.Grounded && Mathf.Abs(avatar.transform.position.y - floorHeight) < 0.05f, "Gravity returns the character to the floor after a jump");
            motor.JumpHeight = 0.7f;
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, jump: true);
            motor.Step(0.02f);
            var heightOverrideVelocity = Mathf.Sqrt(-2f * Physics.gravity.y * 0.7f) + Physics.gravity.y * 0.02f;
            check(!motor.LastResult.Grounded && Mathf.Abs(motor.LastResult.LocalVelocity.y - heightOverrideVelocity) < 0.005f,
                "An explicit JumpHeight setting still overrides the default impulse for external adapters");
            input.Frame = default;
            for (var step = 0; step < 120; step++) motor.Step(0.02f);
            motor.JumpHeight = 0f;

            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(1001f, 1.5f, 1000f);
            wall.transform.localScale = new Vector3(0.1f, 3f, 4f);
            Physics.SyncTransforms();
            input.Frame = new MotionInputFrame(Vector2.right, 0f);
            for (var step = 0; step < 120; step++) motor.Step(0.02f);
            check(avatar.transform.position.x > 1000.1f &&
                avatar.transform.position.x + motor.Controller.radius <= wall.GetComponent<Collider>().bounds.min.x + 0.0005f &&
                Mathf.Abs(motor.LastResult.LocalVelocity.x) < 0.001f &&
                Mathf.Abs(avatar.transform.position.y) < 0.0005f && motor.LastResult.Grounded,
                "A real wall blocks walking and publishes zero blocked velocity without lifting the standing avatar");
            input.Frame = default;
            for (var step = 0; step < 20; step++) motor.Step(0.02f);
            ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.transform.position = new Vector3(1000f, motor.Controller.height + 2f * motor.Controller.skinWidth + 0.17f, 1000f);
            ceiling.transform.localScale = new Vector3(4f, 0.1f, 4f);
            Physics.SyncTransforms();
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, jump: true);
            motor.Step(0.02f);
            input.Frame = default;
            var hitCeiling = false;
            var highest = avatar.transform.position.y;
            for (var step = 0; step < 120; step++)
            {
                motor.Step(0.02f);
                highest = Mathf.Max(highest, avatar.transform.position.y);
                hitCeiling |= (motor.Controller.collisionFlags & CollisionFlags.Above) != 0;
            }
            check(hitCeiling && highest > 0.01f && highest <= 0.1205f && motor.LastResult.Grounded &&
                Mathf.Abs(avatar.transform.position.y) < 0.0005f,
                "Jumping still collides with a low ceiling and lands back at the original standing height");
            motor.Collisions = false;
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, default, 1f, true);
            var before = avatar.transform.position;
            motor.Step(0.02f);
            check(Vector3.Distance(before, avatar.transform.position) < 0.0001f, "Gravity and jump remain disabled when collision preview is off");
        }
        finally
        {
            if (motorObject) UnityEngine.Object.DestroyImmediate(motorObject);
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            if (ceiling) UnityEngine.Object.DestroyImmediate(ceiling);
            if (wall) UnityEngine.Object.DestroyImmediate(wall);
            if (floor) UnityEngine.Object.DestroyImmediate(floor);
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
