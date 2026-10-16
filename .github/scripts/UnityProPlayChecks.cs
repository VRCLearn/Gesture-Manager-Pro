using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;
using VRCLearn.GestureManagerPro.Extensions.Editor;
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
    private const string FirstUpdateKey = "VRCLearn.GestureManagerPro.Validation.PlayFirstUpdate";
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
        UnityProStatisticsLifecycleChecks.Prepare();
        SessionState.SetBool(FirstUpdateKey, false);
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
        // Let every first-Play-update observer run before checking its snapshots.
        if (!SessionState.GetBool(FirstUpdateKey, false)) { SessionState.SetBool(FirstUpdateKey, true); return; }
        ClearPending();
        var exitCode = 1;
        try
        {
            _assertions = 0;
            UnityProStatisticsLifecycleChecks.Run(Check);
            ExecuteChecks();
            CheckTemporaryGround();
            CheckSimulationMicrophone();
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

    private static void CheckSimulationMicrophone()
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        GameObject avatar = null;
        GestureManager manager = null;
        GameSimulationTool tool = null;
        var paused = EditorApplication.isPaused;
        try
        {
            EditorApplication.isPaused = false;
            avatar = new GameObject("Pro microphone validation avatar");
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.lipSync = VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
            var module = new ModuleVrc3(descriptor) { Active = true, Settings = new ModuleSettings() };
            foreach (var name in ParameterNames.Concat(new[] { "Viseme", "Voice" }))
                module.Params.Add(name, new Vrc3Param(name, AnimatorControllerParameterType.Float));
            module.GetParam("Viseme").Set(module, 3f);
            module.GetParam("Voice").Set(module, 0.37f);
            manager = new GameObject("Pro microphone validation manager").AddComponent<GestureManager>();
            manager.Module = module;
            tool = new GameSimulationTool(manager);
            var microphone = (MicrophoneLipSync)typeof(GameSimulationTool).GetField("_lipSync", any).GetValue(tool);
            var session = (ControlledModeSession)typeof(GameSimulationTool).GetField("_session", any).GetValue(tool);
            var audio = typeof(MicrophoneLipSync).GetField("_session", any).GetValue(microphone);
            var toggle = typeof(GameSimulationTool).GetMethod("Toggle", any);
            Action click = () => toggle.Invoke(tool, new object[] { module });
            var deviceField = typeof(MicrophoneLipSync).GetField("_microphones", any);
            var devices = (string[])deviceField.GetValue(microphone);

            microphone.Enabled = false;
            click();
            Check(session.Running && !microphone.Running, "Game simulation can run with microphone lip sync disabled");
            click();
            microphone.Enabled = true;
            deviceField.SetValue(microphone, new string[0]);
            click();
            Check(session.Running && !microphone.Running &&
                (string)audio.GetType().GetProperty("Error", any).GetValue(audio) == "No microphone is selected.",
                "A missing microphone reports an audio error while movement stays available");
            Check(audio.GetType().GetField("_audioObject", any).GetValue(audio) == null,
                "A missing microphone leaves no temporary audio object");
            click();
            Check(!session.Running && !manager.IsPlayerControlled, "The combined simulation still exits after microphone startup failure");
            deviceField.SetValue(microphone, devices);

            if (devices.Length == 0 || Application.platform != RuntimePlatform.WindowsEditor || IntPtr.Size != 8)
            {
                Debug.Log("[Pro microphone validation] SKIP: native recording checks require an available Windows microphone.");
                return;
            }
            click();
            Check(session.Running && microphone.Running && Microphone.IsRecording(devices[0]),
                "Starting game simulation automatically starts the selected microphone");
            var audioObject = (GameObject)audio.GetType().GetField("_audioObject", any).GetValue(audio);
            Check(!audioObject.GetComponent<AudioListener>(), "The microphone filter is not attached to a global AudioListener");
            var tap = audio.GetType().GetField("_tap", any).GetValue(audio);
            Check(!(bool)tap.GetType().GetField("Monitor", any).GetValue(tap),
                "Game simulation analyzes microphone audio without speaker monitoring");
            var samples = Enumerable.Repeat(0.25f, 1024).ToArray();
            audio.GetType().GetMethod("OnSamples", any).Invoke(audio, new object[] { samples });
            audio.GetType().GetMethod("ApplyFrame", any).Invoke(audio, null);
            Check(Mathf.Abs(module.GetParam("Voice").FloatValue() - 0.25f) < 0.001f &&
                module.GetParam("Viseme").FloatValue() >= 0f && module.GetParam("Viseme").FloatValue() <= 14f,
                "The active native analyzer publishes injected PCM volume and a valid viseme to the simulator");
            session.Stop();
            Check(!microphone.Running && !Microphone.IsRecording(devices[0]) &&
                audio.GetType().GetField("_audioObject", any).GetValue(audio) == null,
                "Stopping the motion session synchronously releases its microphone and audio object");
            Check(module.GetParam("Viseme").FloatValue() == 3f && Mathf.Abs(module.GetParam("Voice").FloatValue() - 0.37f) < 0.001f,
                "Exiting the combined simulation restores the original speech parameters");
            session.Stop();
            Check(!microphone.Running && !Microphone.IsRecording(devices[0]), "Repeated combined-session cleanup is safe");

            click();
            Check(microphone.Running, "Restarting game simulation reacquires the microphone");
            EditorApplication.isPaused = true;
            session.Poll();
            Check(!session.Running && !microphone.Running && !Microphone.IsRecording(devices[0]),
                "Pausing releases microphone recording along with movement ownership");
            EditorApplication.isPaused = false;
            click();
            manager.Module = null;
            session.Poll();
            Check(!microphone.Running && !Microphone.IsRecording(devices[0]) && !manager.IsPlayerControlled,
                "Losing the controlled avatar stops both sessions and releases the microphone");
            manager.Module = module;
        }
        finally
        {
            tool?.Dispose();
            if (manager) { manager.Module = null; UnityEngine.Object.DestroyImmediate(manager.gameObject); }
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            EditorApplication.isPaused = paused;
        }
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
            session.Motor.Halt();
            session.Motor.Step(0.1f);
            Check(Mathf.Abs(module.GetParam("VelocityZ").FloatValue() - 1.05f) < 0.001f,
                "Session publishes actual accelerating forward velocity instead of requested walk speed");
            session.Motor.Step(0.1f);
            Check(Mathf.Abs(module.GetParam("VelocityZ").FloatValue() - 2f) < 0.001f, "Session publishes steady forward velocity to VelocityZ");
            Check(Mathf.Abs(module.GetParam("VelocityMagnitude").FloatValue() - 2f) < 0.001f, "Session publishes velocity magnitude in meters per second");
            Check(Mathf.Abs(module.GetParam("VelocityX").FloatValue()) < 0.001f && Mathf.Abs(module.GetParam("VelocityY").FloatValue()) < 0.001f, "Forward motion leaves other velocity axes zero");
            Check(module.GetParam("VRMode").FloatValue() == values["VRMode"] &&
                module.GetParam("TrackingType").FloatValue() == values["TrackingType"] && module.Settings.vrMode,
                "Movement preserves the simulator's existing VR and tracking selections");
            Check(module.GetParam("Upright").FloatValue() == values["Upright"], "Movement preserves the existing upright parameter");
            module.GetParam("Upright").Set(module, 0.6f);
            input.Frame = default;
            session.Motor.Step(0.1f);
            session.Poll();
            Check(module.GetParam("Upright").FloatValue() == 0.6f,
                "Idle updates and polling preserve a posture selected through the original GM controls");
            // No pose input has owned this parameter yet. Keep the external edit on exit.
            values["Upright"] = 0.6f;
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
            // VR and tracking state belongs to the existing simulator controls.
            module.Settings.vrMode = false;
            module.GetParam("TrackingType").Set(module, 6f);
            values["TrackingType"] = 6f;
            session.Stop();
            Check(!session.Running && !manager.IsPlayerControlled && session.Motor == null, "Stop releases session and manager ownership");
            Check(!module.Settings.vrMode, "Stop preserves an external VR mode setting change");
            Check(!motionObject && !cameraObject, "Stop destroys temporary motor and camera");
            Check(module.SimulationCamera == originalCamera, "Stop restores the module's original camera selection");
            Check(originalCamera.transform.position == originalCameraPosition && originalCamera.transform.rotation == originalCameraRotation &&
                originalCamera.fieldOfView == 47f && originalCamera.depth == 7f, "The simulation does not change the original scene camera");
            Check(Vector3.Distance(avatar.transform.position, originalPosition) < 0.0001f && Quaternion.Angle(avatar.transform.rotation, originalRotation) < 0.001f, "Stop restores the avatar's original position and rotation");
            foreach (var pair in values)
                Check(Mathf.Abs(module.GetParam(pair.Key).FloatValue() - pair.Value) < 0.0001f, "Stop restores parameter " + pair.Key);
            session.Stop();
            Check(!manager.IsPlayerControlled, "Repeated stop remains safe");

            Check(session.Start(manager, input, collisions: false), "Session restarts for explicit posture control");
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, pose: MotionPose.Crouch);
            session.Motor.Step(0.02f);
            Check(module.GetParam("Upright").FloatValue() == 1f, "Crouch input toggles an already crouched avatar back to standing");
            session.Motor.Step(0.02f);
            Check(module.GetParam("Upright").FloatValue() == 0.6f, "A new crouch command selects the reference upright value");
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, pose: MotionPose.Prone);
            session.Motor.Step(0.02f);
            Check(module.GetParam("Upright").FloatValue() == 0.35f, "Prone input switches directly from crouching to prone");
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            session.Motor.Step(0.02f);
            Check(module.GetParam("Upright").FloatValue() == 0.35f, "Subsequent movement does not reset the selected posture");
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, pose: MotionPose.Prone);
            session.Motor.Step(0.02f);
            Check(module.GetParam("Upright").FloatValue() == 1f, "A second prone command returns to standing");
            session.Stop();
            Check(module.GetParam("Upright").FloatValue() == 0.6f, "Stop restores posture only after explicit session posture commands");

            var uprightParameter = module.GetParam("Upright");
            Check(session.Start(manager, input, collisions: false, previewCamera: true), "Session starts for posture-callback failure cleanup");
            var poseMotor = session.Motor;
            var poseCamera = session.ViewCamera;
            uprightParameter.SetOnChange((parameter, value) =>
            {
                if (session.Running) throw new InvalidOperationException("Injected posture callback failure");
            });
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, pose: MotionPose.Crouch);
            poseMotor.Step(0.02f);
            Check(!session.Running && !manager.IsPlayerControlled && !poseMotor && !poseCamera &&
                session.Error.Contains("Injected posture callback failure") && uprightParameter.FloatValue() == 0.6f &&
                !Resources.FindObjectsOfTypeAll<BoxCollider>().Any(collider => collider.gameObject.name == "Gesture Manager Pro Ground"),
                "A posture callback exception reports failure and restores posture, motor, camera and floor ownership");
            uprightParameter.SetOnChange(null);

            Check(session.Start(manager, input, collisions: false, previewCamera: true), "Session starts for reentrant posture-stop cleanup");
            poseMotor = session.Motor;
            poseCamera = session.ViewCamera;
            uprightParameter.SetOnChange((parameter, value) => { if (session.Running) session.Stop(); });
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, pose: MotionPose.Prone);
            poseMotor.Step(0.02f);
            Check(!session.Running && !manager.IsPlayerControlled && !poseMotor && !poseCamera &&
                string.IsNullOrEmpty(session.Error) && uprightParameter.FloatValue() == 0.6f,
                "A synchronous stop during posture publication cannot continue using a destroyed motor");
            uprightParameter.SetOnChange(null);

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

    private static void CheckTemporaryGround()
    {
        GameObject avatar = null;
        GameObject existingGround = null;
        GestureManager manager = null;
        ControlledModeSession session = null;
        var paused = EditorApplication.isPaused;
        var originalGravity = Physics.gravity;
        var startPosition = new Vector3(520f, 100f, 580f);
        try
        {
            EditorApplication.isPaused = false;
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            avatar = new GameObject("Pro temporary ground validation avatar");
            avatar.transform.position = startPosition;
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            var module = new ModuleVrc3(descriptor) { Active = true, Settings = new ModuleSettings() };
            foreach (var name in ParameterNames)
                module.Params.Add(name, new Vrc3Param(name, AnimatorControllerParameterType.Float));
            manager = new GameObject("Pro temporary ground validation manager").AddComponent<GestureManager>();
            manager.Module = module;
            session = new ControlledModeSession();
            var input = new SessionMotionInput();
            Physics.SyncTransforms();
            Check(SupportBelow(startPosition).Length == 0, "Temporary-ground checks start without a scene floor");

            GameObject StartGround(bool gravity = false)
            {
                Check(session.Start(manager, input, gravity: gravity, previewCamera: true),
                    "Game simulation starts without an existing floor: " + session.Error);
                var hits = SupportBelow(startPosition, session.Motor.Controller);
                Check(hits.Length == 1 && hits[0].collider.gameObject.name == "Gesture Manager Pro Ground",
                    "Game simulation creates a real supporting collider at the starting feet");
                return hits[0].collider.gameObject;
            }

            void CheckRemoved(GameObject ground, string reason)
            {
                Physics.SyncTransforms();
                Check(!session.Running && !manager.IsPlayerControlled && !ground &&
                    SupportBelow(startPosition).Length == 0,
                    reason + " releases ownership and removes the temporary floor from physics");
                Check(Vector3.Distance(avatar.transform.position, startPosition) < 0.0001f,
                    reason + " restores the avatar's original position");
            }

            var ground = StartGround();
            var groundCollider = ground.GetComponent<BoxCollider>();
            Check(ground.GetComponentsInChildren<Renderer>(true).Length == 0 && !ground.GetComponent<MeshFilter>() &&
                !groundCollider.isTrigger && ground.transform.parent == null,
                "The temporary floor has no rendering components, remains solid, and is independent of the avatar");
            Check(ground.hideFlags == HideFlags.HideAndDontSave && ground.scene == avatar.scene &&
                Vector3.Distance(groundCollider.bounds.size, new Vector3(100f, 0.1f, 100f)) < 0.01f &&
                Mathf.Abs(groundCollider.bounds.max.y - startPosition.y) < 0.001f,
                "The unsaved floor covers 100 meters and places its top at the starting foot height");
            Check(!session.Motor.UseGravity && session.Motor.LastResult.Grounded &&
                module.GetParam("Grounded").BoolValue(),
                "Default simulation immediately publishes real grounded state with gravity disabled");
            input.Frame = new MotionInputFrame(Vector2.zero, 0f);
            session.Motor.Step(0.1f);
            Check(module.GetParam("Grounded").BoolValue() && module.GetParam("Upright").FloatValue() == 0f &&
                module.GetParam("VelocityMagnitude").FloatValue() == 0f,
                "Standing on the generated floor publishes stationary motion without overwriting posture");
            var floorPosition = ground.transform.position;
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            for (var step = 0; step < 10; step++)
            {
                session.Motor.Step(0.1f);
                Check(module.GetParam("Grounded").BoolValue(),
                    "Walking remains grounded after collision contact adjustment, step " + step);
            }
            Check(module.GetParam("Grounded").BoolValue() && module.GetParam("Upright").FloatValue() == 0f &&
                Mathf.Abs(module.GetParam("VelocityZ").FloatValue() - 2f) < 0.01f &&
                Mathf.Abs(module.GetParam("VelocityY").FloatValue()) < 0.01f &&
                Mathf.Abs(avatar.transform.position.y - startPosition.y) < session.Motor.Controller.skinWidth + 0.001f,
                "Walking keeps the avatar grounded and publishes horizontal locomotion instead of falling");
            Check(ground.transform.position == floorPosition &&
                SupportBelow(avatar.transform.position, session.Motor.Controller).Any(hit => hit.collider == groundCollider),
                "The avatar moves across the generated floor without dragging the floor along");
            session.Motor.transform.position = startPosition + Vector3.right * 60f;
            Physics.SyncTransforms();
            input.Frame = default;
            session.Motor.Step(0.02f);
            Check(!session.Motor.LastResult.Grounded && !module.GetParam("Grounded").BoolValue() &&
                SupportBelow(avatar.transform.position, session.Motor.Controller).Length == 0,
                "Moving beyond the temporary floor still reports genuinely unsupported airborne state");
            session.Stop();
            CheckRemoved(ground, "Normal stop");
            session.Stop();

            ground = StartGround(true);
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, jump: true);
            session.Motor.Step(0.02f);
            Check(!session.Motor.LastResult.Grounded && !module.GetParam("Grounded").BoolValue() &&
                module.GetParam("VelocityY").FloatValue() > 0f && avatar.transform.position.y > startPosition.y,
                "Jumping with gravity leaves the physical floor and publishes airborne upward motion");
            input.Frame = default;
            var landed = false;
            for (var step = 0; step < 150; step++)
            {
                session.Motor.Step(0.02f);
                if (!session.Motor.LastResult.Grounded) continue;
                landed = true;
                break;
            }
            Check(landed && module.GetParam("Grounded").BoolValue() &&
                Mathf.Abs(avatar.transform.position.y - startPosition.y) < 0.05f,
                "Gravity returns the avatar to the generated floor and publishes grounded state on landing");
            session.Stop();
            CheckRemoved(ground, "Stopping after a jump");

            existingGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
            existingGround.name = "Pro existing scene floor";
            existingGround.transform.position = startPosition - Vector3.up * 0.05f;
            existingGround.transform.localScale = new Vector3(6f, 0.1f, 6f);
            var existingCollider = existingGround.GetComponent<Collider>();
            Physics.SyncTransforms();
            Check(session.Start(manager, input, previewCamera: true), "Simulation starts on an existing scene floor");
            var existingHits = SupportBelow(startPosition, session.Motor.Controller);
            Check(existingHits.Length == 1 && existingHits[0].collider == existingCollider &&
                session.Motor.LastResult.Grounded && module.GetParam("Grounded").BoolValue(),
                "Existing scene support is reused without creating an overlapping temporary floor");
            session.Stop();
            Physics.SyncTransforms();
            Check(existingGround && SupportBelow(startPosition).Single().collider == existingCollider,
                "Stopping preserves an existing scene floor and its collider");
            UnityEngine.Object.DestroyImmediate(existingGround);
            Physics.SyncTransforms();

            ground = StartGround();
            EditorApplication.isPaused = true;
            session.Poll();
            CheckRemoved(ground, "Pausing");
            EditorApplication.isPaused = false;

            ground = StartGround();
            manager.Module = null;
            session.Poll();
            CheckRemoved(ground, "Unlinking the avatar");
            manager.Module = module;

            ground = StartGround();
            input.Error = new InvalidOperationException("Injected temporary floor input failure");
            session.Motor.Step(0.02f);
            CheckRemoved(ground, "Input adapter failure");
            Check(session.Error.Contains("Injected temporary floor input failure"),
                "Floor cleanup preserves the input adapter diagnostic");
            input.Error = null;

            ground = StartGround();
            UnityEngine.Object.DestroyImmediate(ground);
            session.Poll();
            CheckRemoved(ground, "Losing the temporary floor");
            Check(session.Motor == null && !Resources.FindObjectsOfTypeAll<MotionPreviewCamera>()
                .Any(component => component.Target == avatar.transform),
                "Losing the temporary floor also releases the motor and preview camera");

            var startupSawFloor = false;
            var grounded = module.GetParam("Grounded");
            grounded.SetOnChange((parameter, value) =>
            {
                if (value < 0.5f) return;
                startupSawFloor = SupportBelow(startPosition, session.Motor.Controller)
                    .Any(hit => hit.collider.gameObject.name == "Gesture Manager Pro Ground");
                throw new InvalidOperationException("Injected temporary floor startup failure");
            });
            Check(!session.Start(manager, input, previewCamera: true) && startupSawFloor &&
                session.Error.Contains("Injected temporary floor startup failure"),
                "A grounded-parameter callback can fail after the temporary floor has entered physics");
            grounded.SetOnChange(null);
            CheckRemoved(null, "Startup failure after creating the floor");
            Check(session.Motor == null, "Startup failure leaves no movement motor");

            Check(session.Start(manager, input), "Plain motion sessions still start without a simulation floor");
            Check(!session.Motor.LastResult.Grounded &&
                SupportBelow(startPosition, session.Motor.Controller).Length == 0,
                "A plain input adapter without game simulation does not create scene geometry");
            session.Stop();
            using (var keyboard = new KeyboardMotionInputSource(true))
            {
                Check(session.Start(manager, keyboard), "Keyboard game simulation starts without the legacy preview flag");
                var hits = SupportBelow(startPosition, session.Motor.Controller);
                Check(hits.Length == 1 && hits[0].collider.gameObject.name == "Gesture Manager Pro Ground" &&
                    session.Motor.LastResult.Grounded && module.GetParam("Grounded").BoolValue(),
                    "The keyboard game-simulation entry creates its supporting floor automatically");
                ground = hits[0].collider.gameObject;
                session.Dispose();
                CheckRemoved(ground, "Disposing keyboard game simulation");
            }
        }
        finally
        {
            session?.Dispose();
            if (manager) { manager.Module = null; UnityEngine.Object.DestroyImmediate(manager.gameObject); }
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            if (existingGround) UnityEngine.Object.DestroyImmediate(existingGround);
            Physics.gravity = originalGravity;
            EditorApplication.isPaused = paused;
            Physics.SyncTransforms();
        }
    }

    private static RaycastHit[] SupportBelow(Vector3 feet, CharacterController ignoredController = null) =>
        Physics.RaycastAll(feet + Vector3.up * 0.25f, Vector3.down, 0.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            .Where(hit => hit.collider != ignoredController).ToArray();

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
