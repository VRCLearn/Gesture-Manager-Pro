using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Params;
using VRCLearn.GestureManagerPro.Modules;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

// Optional second batch invocation: -executeMethod UnityProPlayChecks.Run.
// SessionState survives the Editor's domain reload when entering Play Mode.
// This fixture validates motion session ownership/publishing/cleanup, with a
// minimal active module. It does not claim full humanoid graph or hardware tests.
[InitializeOnLoad]
public static class UnityProPlayChecks
{
    private const string PendingKey = "VRCLearn.GestureManagerPro.Validation.PlayPending";
    private const string DeadlineKey = "VRCLearn.GestureManagerPro.Validation.PlayDeadline";
    private static int _assertions;
    private static readonly string[] ParameterNames =
    {
        "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude", "AngularY", "Grounded", "VRMode", "TrackingType", "Upright"
    };

    static UnityProPlayChecks()
    {
        if (SessionState.GetBool(PendingKey, false)) EditorApplication.update += AwaitPlayMode;
    }

    public static void Run()
    {
        SessionState.SetBool(PendingKey, true);
        SessionState.SetString(DeadlineKey, DateTime.UtcNow.AddSeconds(90).Ticks.ToString());
        EditorApplication.update -= AwaitPlayMode;
        EditorApplication.update += AwaitPlayMode;
        if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
    }

    private static void AwaitPlayMode()
    {
        if (!SessionState.GetBool(PendingKey, false)) { EditorApplication.update -= AwaitPlayMode; return; }
        if (long.TryParse(SessionState.GetString(DeadlineKey, "0"), out var deadline) && DateTime.UtcNow.Ticks > deadline)
        {
            ClearPending();
            Debug.LogError("[Pro Play validation] FAIL: entering Play Mode exceeded the 90 second timeout.");
            EditorApplication.Exit(1);
            return;
        }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        ClearPending();
        var exitCode = 1;
        try
        {
            _assertions = 0;
            ExecuteChecks();
            UnityProCameraChecks.Run(Check);
            UnityProGraphChecks.RunChecks(Check);
            UnityProSimulationRenderChecks.RunChecks(Check);
            Debug.Log("[Pro Play validation] PASS: " + _assertions + " assertions. Injected motion input, simulator parameter publishing, exclusive ownership, failure/pause/unlink handling, and restoration.");
            exitCode = 0;
        }
        catch (Exception exception)
        {
            Debug.LogError("[Pro Play validation] FAIL after " + _assertions + " assertions: " + exception);
        }
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(exitCode);
    }

    private static void ClearPending()
    {
        EditorApplication.update -= AwaitPlayMode;
        SessionState.SetBool(PendingKey, false);
        SessionState.EraseString(DeadlineKey);
    }

    private static void ExecuteChecks()
    {
        GameObject avatar = null;
        GestureManager manager = null;
        GestureManager secondManager = null;
        ControlledModeSession session = null;
        ControlledModeSession competing = null;
        GameObject originalCameraObject = null;
        GameObject otherAvatar = null;
        var paused = EditorApplication.isPaused;
        try
        {
            Check(EditorApplication.isPlaying, "Session tests run in actual Play Mode");
            EditorApplication.isPaused = false;
            avatar = new GameObject("Pro session validation avatar");
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            avatar.transform.SetPositionAndRotation(new Vector3(4f, 2f, 6f), Quaternion.Euler(0f, 35f, 0f));
            var originalPosition = avatar.transform.position;
            var originalRotation = avatar.transform.rotation;
            var module = new ModuleVrc3(descriptor) { Active = true, Settings = new ModuleSettings { vrMode = true } };
            var values = new Dictionary<string, float>();
            for (var i = 0; i < ParameterNames.Length; i++)
            {
                var name = ParameterNames[i];
                var parameter = new Vrc3Param(name, AnimatorControllerParameterType.Float);
                module.Params.Add(name, parameter);
                var value = (i + 1) * 0.13f;
                parameter.Set(module, value);
                values.Add(name, value);
            }
            manager = new GameObject("Pro session validation manager").AddComponent<GestureManager>();
            manager.Module = module;
            secondManager = new GameObject("Pro session validation second manager").AddComponent<GestureManager>();
            secondManager.Module = module;
            var input = new SessionMotionInput();
            originalCameraObject = new GameObject("Pro original camera snapshot");
            var originalCamera = originalCameraObject.AddComponent<Camera>();
            originalCamera.transform.SetPositionAndRotation(new Vector3(30f, 8f, 20f), Quaternion.Euler(15f, 22f, 0f));
            var originalCameraPosition = originalCamera.transform.position;
            var originalCameraRotation = originalCamera.transform.rotation;
            originalCamera.fieldOfView = 47f;
            originalCamera.depth = 7f;
            module.SimulationCamera = originalCamera;
            session = new ControlledModeSession();
            Check(session.Start(manager, input, 2f, 120f, false, false, true), "Session starts for an active simulator: " + session.Error);
            Check(session.Running && manager.IsPlayerControlled && session.Motor, "Session owns manager and creates a motor");
            Check(input.Resets >= 1, "Session startup resets input");
            var motionObject = session.Motor.gameObject;
            var camera = Resources.FindObjectsOfTypeAll<MotionPreviewCamera>().Single(component => component.Target == avatar.transform);
            var cameraObject = camera.gameObject;
            Check(cameraObject.GetComponent<Camera>(), "Preview creates a temporary follow camera");
            Check(module.SimulationCamera == camera.PreviewCamera && camera.PreviewCamera.depth > originalCamera.depth,
                "The simulation camera takes Game view priority and owns the module camera override");
            session.SetViewMode(SimulationViewMode.FirstPerson);
            Check(session.ViewMode == SimulationViewMode.FirstPerson && camera.Mode == SimulationViewMode.FirstPerson,
                "An active session switches to first person without rebuilding its motor");
            Check(Vector3.Distance(camera.transform.position, avatar.transform.TransformPoint(descriptor.ViewPosition)) < 0.0001f,
                "First person uses the descriptor's actual viewpoint");
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(12f, -18f));
            session.Motor.Step(0.1f);
            camera.ApplyView();
            Check(camera.Pitch == -18f && Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.y, avatar.transform.eulerAngles.y)) < 0.001f,
                "Mouse input publishes pitch to the simulation camera and yaw to the avatar");
            session.SetViewMode(SimulationViewMode.ThirdPerson);
            session.SetCameraDistance(3f);
            camera.ApplyView();
            Check(session.ViewMode == SimulationViewMode.ThirdPerson && camera.Distance > 4f &&
                Vector3.Distance(camera.transform.position, avatar.transform.TransformPoint(descriptor.ViewPosition)) > 4f,
                "Third person switches live and respects the configurable distance");

            competing = new ControlledModeSession();
            Check(!competing.Start(manager, new SessionMotionInput(), collisions: false), "A second session cannot take the same manager");
            Check(session.Running && manager.IsPlayerControlled, "Rejected competitor leaves original session active");
            Check(!competing.Start(secondManager, new SessionMotionInput(), collisions: false), "A second manager cannot take the same avatar");
            Check(!secondManager.IsPlayerControlled, "Rejected avatar competitor retains no ownership flag");

            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            session.Motor.Step(0.1f);
            Check(Mathf.Abs(module.GetParam("VelocityZ").FloatValue() - 2f) < 0.001f, "Session publishes forward velocity to VelocityZ");
            Check(Mathf.Abs(module.GetParam("VelocityMagnitude").FloatValue() - 2f) < 0.001f, "Session publishes velocity magnitude in meters per second");
            Check(Mathf.Abs(module.GetParam("VelocityX").FloatValue()) < 0.001f && Mathf.Abs(module.GetParam("VelocityY").FloatValue()) < 0.001f, "Forward motion leaves other velocity axes zero");
            Check(module.GetParam("VRMode").FloatValue() == 0f && module.GetParam("TrackingType").FloatValue() == 1f, "Keyboard generic-avatar tracking parameters use desktop values");
            Check(module.GetParam("Upright").FloatValue() == 1f, "Standing keyboard control publishes upright state");
            Check(Vector3.Distance(avatar.transform.position, originalPosition) > 0.1f, "Injected input moves the actual avatar transform");
            var positionBeforeBlock = avatar.transform.position;
            var rotationBeforeBlock = avatar.transform.rotation;
            var locomotionFlag = typeof(ModuleVrc3).GetField("LocomotionDisabled", BindingFlags.NonPublic | BindingFlags.Instance);
            locomotionFlag.SetValue(module, true);
            input.Frame = new MotionInputFrame(Vector2.up, 1f);
            session.Motor.Step(0.1f);
            Check(session.LocomotionBlocked, "Session observes the simulator's locomotion-disabled state");
            Check(Vector3.Distance(avatar.transform.position, positionBeforeBlock) < 0.0001f && Quaternion.Angle(avatar.transform.rotation, rotationBeforeBlock) < 0.001f, "Simulator locomotion state blocks movement and turning together");
            Check(module.GetParam("VelocityMagnitude").FloatValue() == 0f && module.GetParam("AngularY").FloatValue() == 0f, "Simulator locomotion block publishes stationary parameters");
            locomotionFlag.SetValue(module, false);
            input.Frame = new MotionInputFrame(Vector2.zero, -1f);
            session.Motor.Step(0.1f);
            Check(Mathf.Abs(module.GetParam("AngularY").FloatValue() + 120f) < 0.01f, "Session publishes negative turn rate to AngularY");
            // The minimal fixture does not install InitParams' VRMode callback.
            // Change the saved setting explicitly to test snapshot restoration.
            module.Settings.vrMode = false;
            session.Stop();
            Check(!session.Running && !manager.IsPlayerControlled && session.Motor == null, "Stop releases session and manager ownership");
            Check(module.Settings.vrMode, "Stop restores the simulator's original VR mode setting");
            Check(!motionObject && !cameraObject, "Stop destroys temporary motor and camera");
            Check(module.SimulationCamera == originalCamera, "Stop restores the module's original camera selection");
            Check(originalCamera.transform.position == originalCameraPosition && originalCamera.transform.rotation == originalCameraRotation &&
                originalCamera.fieldOfView == 47f && originalCamera.depth == 7f, "The simulation does not change the original scene camera");
            Check(Vector3.Distance(avatar.transform.position, originalPosition) < 0.0001f && Quaternion.Angle(avatar.transform.rotation, originalRotation) < 0.001f, "Stop restores the avatar's original position and rotation");
            foreach (var pair in values)
                Check(Mathf.Abs(module.GetParam(pair.Key).FloatValue() - pair.Value) < 0.0001f, "Stop restores parameter " + pair.Key);
            session.Stop();
            Check(!manager.IsPlayerControlled, "Repeated stop remains safe");

            Check(session.Start(manager, input, collisions: false), "Session can restart after restoration");
            input.Frame = new MotionInputFrame(Vector2.right, 0f);
            session.Motor.Step(0.1f);
            manager.Module = null;
            session.Poll();
            Check(!session.Running && !manager.IsPlayerControlled, "Unlinking the manager stops controlled mode");
            Check(Vector3.Distance(avatar.transform.position, originalPosition) < 0.0001f, "Unlink polling restores avatar position");
            manager.Module = module;

            Check(session.Start(manager, input, collisions: false), "Session can restart after unlink cleanup");
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            session.Motor.Step(0.1f);
            EditorApplication.isPaused = true;
            session.Poll();
            Check(!session.Running && !manager.IsPlayerControlled, "Pausing the Editor stops controlled mode");
            Check(Vector3.Distance(avatar.transform.position, originalPosition) < 0.0001f, "Pause cleanup restores avatar position");
            EditorApplication.isPaused = false;

            Check(session.Start(manager, input, collisions: false), "Session can restart after pause cleanup");
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            session.Motor.Step(0.1f);
            input.Error = new InvalidOperationException("Injected session adapter failure");
            session.Motor.Step(0.1f);
            Check(!session.Running && !manager.IsPlayerControlled && session.Error.Contains("Injected session adapter failure"), "Input adapter failure reports a diagnostic and releases ownership");
            Check(Vector3.Distance(avatar.transform.position, originalPosition) < 0.0001f, "Input adapter failure restores avatar position");
            Check(!Resources.FindObjectsOfTypeAll<CharacterMotionMotor>().Any(component => component.gameObject.name == "Gesture Manager Pro Motion"), "No temporary movement motor remains after all scenarios");
            Check(!Resources.FindObjectsOfTypeAll<MotionPreviewCamera>().Any(component => component.Target == avatar.transform), "No temporary follow camera remains after all scenarios");

            input.Error = null;
            Check(session.Start(manager, input, collisions: false, previewCamera: true), "Session restarts for lost-camera cleanup");
            var ownedCamera = session.ViewCamera.PreviewCamera;
            var cameraRule = typeof(ModuleVrc3).GetMethod("CameraRule", BindingFlags.NonPublic | BindingFlags.Static);
            Check(!(bool)cameraRule.Invoke(null, new object[] { ownedCamera }), "Scene Camera auto-selection excludes the temporary game camera");
            UnityEngine.Object.DestroyImmediate(session.ViewCamera.gameObject);
            session.Poll();
            Check(!session.Running && module.SimulationCamera == originalCamera && !manager.IsPlayerControlled,
                "Losing the preview camera stops simulation and restores the original camera reference");
            var velocityZ = module.GetParam("VelocityZ");
            var reentrantStartRejected = false;
            velocityZ.SetOnChange((parameter, value) =>
            {
                if (!session.Running)
                {
                    reentrantStartRejected = !session.Start(manager, input, collisions: false);
                    throw new InvalidOperationException("Injected restore callback failure");
                }
            });
            Check(session.Start(manager, input, collisions: false, previewCamera: true), "Session restarts for callback-failure cleanup");
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            session.Motor.Step(0.1f);
            session.Stop();
            Check(!session.Running && !manager.IsPlayerControlled && session.Motor == null &&
                module.SimulationCamera == originalCamera && reentrantStartRejected && session.Error.Contains("Injected restore callback failure"),
                "A restoration callback exception cannot retain movement ownership or the simulation camera");
            velocityZ.SetOnChange(null);
            foreach (var pair in values)
                Check(Mathf.Abs(module.GetParam(pair.Key).FloatValue() - pair.Value) < 0.0001f,
                    "Restoration continues after callback failure for " + pair.Key);
            using (var keyboard = new KeyboardMotionInputSource(true))
            using (var otherKeyboard = new KeyboardMotionInputSource(true))
            {
                var originalCursorLock = Cursor.lockState;
                var originalCursorVisible = Cursor.visible;
                Check(session.Start(manager, keyboard, collisions: false, viewMode: SimulationViewMode.FirstPerson),
                    "Game simulation always creates its camera even when the optional legacy preview flag is false");
                Check(session.ViewCamera && session.ViewCamera.Mode == SimulationViewMode.FirstPerson, "Game entry uses the requested first-person view");
                var toggle = (Action)typeof(KeyboardMotionInputSource).GetField("ViewToggleRequested", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(keyboard);
                toggle();
                Check(session.ViewMode == SimulationViewMode.ThirdPerson, "The keyboard view event reaches the active session");
                otherAvatar = new GameObject("Pro other game simulation avatar");
                otherAvatar.AddComponent<Animator>();
                var otherDescriptor = otherAvatar.AddComponent<VRCAvatarDescriptor>();
                var otherModule = new ModuleVrc3(otherDescriptor) { Active = true };
                secondManager.Module = otherModule;
                Check(!competing.Start(secondManager, otherKeyboard, collisions: false) && competing.Error == "Another game simulation is already active.",
                    "Only one game simulation can capture the shared Game view even for distinct avatars");
                session.Stop();
                Check(Cursor.lockState == originalCursorLock && Cursor.visible == originalCursorVisible && keyboard.Suspended,
                    "Exiting game simulation preserves cursor state and suspends input");
                Check(typeof(KeyboardMotionInputSource).GetField("ViewToggleRequested", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(keyboard) == null,
                    "Stop removes view input subscriptions");
            }
        }
        finally
        {
            session?.Dispose();
            competing?.Dispose();
            if (manager) { manager.Module = null; UnityEngine.Object.DestroyImmediate(manager.gameObject); }
            if (secondManager) { secondManager.Module = null; UnityEngine.Object.DestroyImmediate(secondManager.gameObject); }
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            if (otherAvatar) UnityEngine.Object.DestroyImmediate(otherAvatar);
            if (originalCameraObject) UnityEngine.Object.DestroyImmediate(originalCameraObject);
            EditorApplication.isPaused = paused;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }

    private sealed class SessionMotionInput : IMotionInputSource
    {
        public MotionInputFrame Frame;
        public Exception Error;
        public int Resets;
        public MotionInputFrame Sample() { if (Error != null) throw Error; return Frame; }
        public void Reset() { Frame = default; Resets++; }
    }
}
