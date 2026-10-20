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
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools;
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
    private const float StandingHeightTolerance = 0.0005f;
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
            CheckGameSimulationGravity();
            CheckThirdPersonCamera();
            CheckThirdPersonDescriptorHeights();
            CheckGameSimulationFreeLook();
            CheckObserverCamera();
            CheckSimulationMicrophone();
            UnityProCameraChecks.Run(Check);
            UnityProGraphChecks.RunChecks(Check);
            UnityProVrLifecycleChecks.Run(Check);
            UnityProVrVisibilityChecks.Run(Check);
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
            var boomLength = Vector3.Distance(camera.transform.position,
                BodyAxisViewpoint(avatar.transform, camera.ViewPosition));
            Check(session.ViewMode == SimulationViewMode.ThirdPerson && camera.Distance > 4f &&
                boomLength > 4f && boomLength <= camera.Distance + 0.001f,
                "Third person switches live and respects the configurable distance, allowing scene obstruction: " +
                boomLength + " / " + camera.Distance);

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
                Check(typeof(KeyboardMotionInputSource).GetField("CameraZoomRequested", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(keyboard) == null,
                    "Stop removes wheel input subscriptions");
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
                Check(Mathf.Abs(avatar.transform.position.y - startPosition.y) < StandingHeightTolerance &&
                    Mathf.Abs(session.Motor.transform.position.y - startPosition.y) < StandingHeightTolerance,
                    "Creating a temporary-floor session preserves the original avatar and motor standing height");
                return hits[0].collider.gameObject;
            }

            float StandingDrift(int frames)
            {
                input.Frame = default;
                var maximum = Mathf.Abs(avatar.transform.position.y - startPosition.y);
                var stayedGrounded = true;
                for (var frame = 0; frame < frames; frame++)
                {
                    session.Motor.Step(0.02f);
                    maximum = Mathf.Max(maximum, Mathf.Abs(avatar.transform.position.y - startPosition.y),
                        Mathf.Abs(session.Motor.transform.position.y - startPosition.y));
                    stayedGrounded &= session.Motor.LastResult.Grounded;
                }
                Check(stayedGrounded, "Standing height checks retain real physical support throughout the idle interval");
                return maximum;
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
                Mathf.Abs(groundCollider.bounds.max.y - startPosition.y) < StandingHeightTolerance,
                "The unsaved floor covers 100 meters and places its top at the starting foot height");
            Check(!session.Motor.UseGravity && session.Motor.LastResult.Grounded &&
                module.GetParam("Grounded").BoolValue(),
                "A low-level motion adapter can explicitly disable gravity while publishing real grounded state");
            input.Frame = new MotionInputFrame(Vector2.zero, 0f);
            session.Motor.Step(0.1f);
            Check(module.GetParam("Grounded").BoolValue() && module.GetParam("Upright").FloatValue() == 0f &&
                module.GetParam("VelocityMagnitude").FloatValue() == 0f &&
                Mathf.Abs(avatar.transform.position.y - startPosition.y) < StandingHeightTolerance,
                "The first stationary frame on the generated floor preserves the original root height and posture");
            Check(StandingDrift(300) < StandingHeightTolerance,
                "Long idle on a temporary floor cannot accumulate a standing-height offset");
            var floorPosition = ground.transform.position;
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            var walkingDrift = 0f;
            var walkingGrounded = true;
            for (var step = 0; step < 10; step++)
            {
                session.Motor.Step(0.1f);
                walkingDrift = Mathf.Max(walkingDrift, Mathf.Abs(avatar.transform.position.y - startPosition.y));
                walkingGrounded &= module.GetParam("Grounded").BoolValue();
            }
            Check(walkingGrounded && module.GetParam("Upright").FloatValue() == 0f &&
                Mathf.Abs(module.GetParam("VelocityZ").FloatValue() - 2f) < 0.01f &&
                Mathf.Abs(module.GetParam("VelocityY").FloatValue()) < 0.01f &&
                walkingDrift < StandingHeightTolerance,
                "Walking on the temporary floor preserves the original standing height with horizontal locomotion (max drift=" + walkingDrift + ")");
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
                Mathf.Abs(avatar.transform.position.y - startPosition.y) < StandingHeightTolerance,
                "Gravity lands on the generated floor at the original pre-simulation standing height");
            Check(StandingDrift(300) < StandingHeightTolerance,
                "Gravity-enabled idle after landing does not settle above or drift away from the original height");
            session.Stop();
            CheckRemoved(ground, "Stopping after a jump");
            for (var restart = 0; restart < 3; restart++)
            {
                ground = StartGround(true);
                Check(StandingDrift(60) < StandingHeightTolerance,
                    "Repeated collision-session entry does not accumulate vertical offsets, restart " + restart);
                session.Stop();
                CheckRemoved(ground, "Repeated temporary-floor session " + restart);
            }

            existingGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
            existingGround.name = "Pro existing scene floor";
            existingGround.transform.position = startPosition - Vector3.up * 0.05f;
            existingGround.transform.localScale = new Vector3(6f, 0.1f, 6f);
            var existingCollider = existingGround.GetComponent<Collider>();
            var existingPosition = existingGround.transform.position;
            var existingRotation = existingGround.transform.rotation;
            var existingScale = existingGround.transform.localScale;
            var existingComponents = existingGround.GetComponents<Component>().Select(component => component.GetInstanceID()).ToArray();
            Physics.SyncTransforms();
            Check(session.Start(manager, input, gravity: true, previewCamera: true), "Simulation starts on an existing scene floor");
            var existingHits = SupportBelow(startPosition, session.Motor.Controller);
            Check(existingHits.Length == 1 && existingHits[0].collider == existingCollider &&
                session.Motor.LastResult.Grounded && module.GetParam("Grounded").BoolValue(),
                "Existing scene support is reused without creating an overlapping temporary floor");
            Check(Mathf.Abs(avatar.transform.position.y - startPosition.y) < StandingHeightTolerance,
                "Entering on an existing scene floor preserves the original root height");
            session.Motor.Step(0.02f);
            Check(Mathf.Abs(avatar.transform.position.y - startPosition.y) < StandingHeightTolerance,
                "The first gravity frame on an existing scene floor cannot lift the avatar");
            Check(StandingDrift(300) < StandingHeightTolerance,
                "Long gravity-enabled idle on an existing scene floor preserves its original standing height");
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            walkingDrift = 0f;
            walkingGrounded = true;
            for (var frame = 0; frame < 40; frame++)
            {
                session.Motor.Step(0.02f);
                walkingDrift = Mathf.Max(walkingDrift, Mathf.Abs(avatar.transform.position.y - startPosition.y));
                walkingGrounded &= session.Motor.LastResult.Grounded;
            }
            Check(walkingGrounded && walkingDrift < StandingHeightTolerance,
                "Walking across an existing scene floor does not add a vertical standing offset");
            session.Motor.Halt();
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, jump: true);
            session.Motor.Step(0.02f);
            Check(!session.Motor.LastResult.Grounded && avatar.transform.position.y > startPosition.y + 0.02f,
                "The height-preserving capsule still jumps from an existing scene floor");
            input.Frame = default;
            for (var frame = 0; frame < 150 && !session.Motor.LastResult.Grounded; frame++) session.Motor.Step(0.02f);
            Check(session.Motor.LastResult.Grounded && Mathf.Abs(avatar.transform.position.y - startPosition.y) < StandingHeightTolerance,
                "Landing on an existing scene floor returns to the original pre-simulation root height");
            session.Stop();
            Physics.SyncTransforms();
            Check(existingGround && SupportBelow(startPosition).Single().collider == existingCollider && existingCollider.enabled &&
                !existingCollider.isTrigger && existingGround.transform.position == existingPosition &&
                existingGround.transform.rotation == existingRotation && existingGround.transform.localScale == existingScale &&
                existingGround.GetComponents<Component>().Select(component => component.GetInstanceID()).SequenceEqual(existingComponents),
                "Stopping preserves the existing floor's position, scale, rotation, and original components");
            Check(Vector3.Distance(avatar.transform.position, startPosition) < 0.0001f,
                "Stopping on an existing floor restores the avatar's complete original position");
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

    private static void CheckGameSimulationGravity()
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject avatar = null;
        GestureManager manager = null;
        GameSimulationTool tool = null;
        var paused = EditorApplication.isPaused;
        try
        {
            EditorApplication.isPaused = false;
            Check(Physics.gravity.y < 0f, "The game-tool gravity fixture uses an actual downward scene gravity");
            avatar = new GameObject("Pro game-tool gravity validation avatar");
            avatar.transform.position = new Vector3(1700f, 120f, 1550f);
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            var originalPosition = avatar.transform.position;
            var originalRotation = avatar.transform.rotation;
            var module = new ModuleVrc3(descriptor) { Active = true, Settings = new ModuleSettings { vrMode = true } };
            var originalParameters = new Dictionary<string, float>();
            for (var index = 0; index < ParameterNames.Length; index++)
            {
                var name = ParameterNames[index];
                var value = name == "VRMode" || name == "Grounded" ? 1f :
                    name == "TrackingType" ? 6f : name == "Upright" ? 0.6f : (index + 1) * 0.17f;
                var parameter = new Vrc3Param(name, AnimatorControllerParameterType.Float);
                module.Params.Add(name, parameter);
                parameter.Set(module, value);
                originalParameters.Add(name, value);
            }
            manager = new GameObject("Pro game-tool gravity validation manager").AddComponent<GestureManager>();
            manager.Module = module;
            tool = new GameSimulationTool(manager);
            ((MicrophoneLipSync)typeof(GameSimulationTool).GetField("_lipSync", any).GetValue(tool)).Enabled = false;
            var session = (ControlledModeSession)typeof(GameSimulationTool).GetField("_session", any).GetValue(tool);
            var toggle = typeof(GameSimulationTool).GetMethod("Toggle", any);
            var inputField = typeof(CharacterMotionMotor).GetField("_input", any);
            var suspendedField = typeof(KeyboardMotionInputSource).GetField("<Suspended>k__BackingField", any);
            Action click = () => toggle.Invoke(tool, new object[] { module });
            var injected = new SessionMotionInput();

            void InjectFrames()
            {
                var keyboard = (KeyboardMotionInputSource)typeof(GameSimulationTool).GetField("_keyboard", any).GetValue(tool);
                Check(keyboard != null && session.Running, "The actual game-tool entry owns its keyboard motion session");
                // Keep the real session and its keyboard ownership/cleanup. Only
                // replace sampled frames and resume the fixture's input gate.
                inputField.SetValue(session.Motor, injected);
                suspendedField.SetValue(keyboard, false);
                injected.Frame = default;
            }

            GameObject Floor()
            {
                var hits = SupportBelow(originalPosition, session.Motor.Controller);
                Check(hits.Length == 1 && hits[0].collider.gameObject.name == "Gesture Manager Pro Ground",
                    "The actual game-tool entry creates a real temporary supporting floor");
                return hits[0].collider.gameObject;
            }

            void CheckRestored(GameObject floor, GameObject motor, GameObject camera, string label)
            {
                Physics.SyncTransforms();
                Check(!session.Running && !manager.IsPlayerControlled && !floor && !motor && !camera &&
                    session.Motor == null && module.SimulationCamera == null && SupportBelow(originalPosition).Length == 0,
                    label + " releases movement ownership, the temporary floor, and the temporary cameras");
                Check(Vector3.Distance(avatar.transform.position, originalPosition) < 0.0001f &&
                    Quaternion.Angle(avatar.transform.rotation, originalRotation) < 0.001f,
                    label + " restores the avatar's original transform");
                foreach (var pair in originalParameters)
                    Check(Mathf.Abs(module.GetParam(pair.Key).FloatValue() - pair.Value) < 0.0001f,
                        label + " restores or preserves parameter " + pair.Key);
            }

            Physics.SyncTransforms();
            Check(SupportBelow(originalPosition).Length == 0, "The game-tool fixture starts without an existing scene floor");
            click();
            Check(session.Running && session.Motor.Collisions && session.Motor.UseGravity && session.Motor.Controller.enabled,
                "Entering game simulation through the default tool enables physical collisions and gravity automatically: " + session.Error);
            Check(Mathf.Abs(avatar.transform.position.y - originalPosition.y) < StandingHeightTolerance &&
                Mathf.Abs(session.Motor.transform.position.y - originalPosition.y) < StandingHeightTolerance,
                "The actual game-tool entry preserves the avatar's original standing height immediately");
            var floor = Floor();
            var motorObject = session.Motor.gameObject;
            var cameraObject = session.ViewCamera.gameObject;
            InjectFrames();
            session.Motor.Step(0.02f);
            Check(Mathf.Abs(avatar.transform.position.y - originalPosition.y) < StandingHeightTolerance &&
                Mathf.Abs(session.Motor.transform.position.y - originalPosition.y) < StandingHeightTolerance,
                "The first actual game-tool gravity frame cannot lift the avatar above its original height (drift=" +
                (avatar.transform.position.y - originalPosition.y) + ")");
            var idleDrift = 0f;
            var remainedGrounded = true;
            for (var frame = 0; frame < 300; frame++)
            {
                session.Motor.Step(0.02f);
                idleDrift = Mathf.Max(idleDrift, Mathf.Abs(avatar.transform.position.y - originalPosition.y));
                remainedGrounded &= session.Motor.LastResult.Grounded;
            }
            Check(remainedGrounded && idleDrift < StandingHeightTolerance,
                "Long idle through the actual game tool does not accumulate a gravity contact offset");
            injected.Frame = new MotionInputFrame(Vector2.up, 0f);
            var walkingDrift = 0f;
            remainedGrounded = true;
            for (var frame = 0; frame < 100; frame++)
            {
                session.Motor.Step(0.02f);
                walkingDrift = Mathf.Max(walkingDrift, Mathf.Abs(avatar.transform.position.y - originalPosition.y));
                remainedGrounded &= session.Motor.LastResult.Grounded;
            }
            Check(remainedGrounded && walkingDrift < StandingHeightTolerance,
                "Walking through the actual game tool preserves the original standing height on every sampled frame");
            injected.Frame = new MotionInputFrame(Vector2.zero, 0f, jump: true);
            session.Motor.Step(0.02f);
            Check(avatar.transform.position.y > originalPosition.y + 0.02f && !session.Motor.LastResult.Grounded &&
                !module.GetParam("Grounded").BoolValue() && module.GetParam("VelocityY").FloatValue() > 0f,
                "A jump frame through the default game-tool session really leaves its physical floor");
            injected.Frame = default;
            for (var frame = 0; frame < 150 && !session.Motor.LastResult.Grounded; frame++) session.Motor.Step(0.02f);
            Check(session.Motor.LastResult.Grounded && module.GetParam("Grounded").BoolValue() &&
                Mathf.Abs(avatar.transform.position.y - originalPosition.y) < StandingHeightTolerance,
                "Default game-tool gravity lands at the original standing height rather than a raised settled height");
            idleDrift = 0f;
            remainedGrounded = true;
            for (var frame = 0; frame < 150; frame++)
            {
                session.Motor.Step(0.02f);
                idleDrift = Mathf.Max(idleDrift, Mathf.Abs(avatar.transform.position.y - originalPosition.y));
                remainedGrounded &= session.Motor.LastResult.Grounded;
            }
            Check(remainedGrounded && idleDrift < StandingHeightTolerance,
                "Idle after an actual game-tool jump cannot settle at a different standing height");

            var bounds = floor.GetComponent<Collider>().bounds;
            var edgeStart = new Vector3(bounds.max.x - 0.5f, avatar.transform.position.y, originalPosition.z);
            // Arrange close to the edge; crossing the collider boundary itself
            // is driven by the same motor and collision path as normal walking.
            avatar.transform.position = edgeStart;
            session.Motor.transform.position = edgeStart;
            Physics.SyncTransforms();
            session.Motor.Halt();
            Check(session.Motor.LastResult.Grounded, "The game-tool walking-off fixture begins supported near the floor edge");
            injected.Frame = new MotionInputFrame(Vector2.right, 0f);
            for (var frame = 0; frame < 90; frame++) session.Motor.Step(0.02f);
            Check(avatar.transform.position.x > bounds.max.x + 0.3f && avatar.transform.position.y < bounds.max.y - 0.3f &&
                !session.Motor.LastResult.Grounded && !module.GetParam("Grounded").BoolValue() &&
                module.GetParam("VelocityY").FloatValue() < 0f,
                "Walking beyond the temporary floor really falls under default game-tool gravity");
            click();
            CheckRestored(floor, motorObject, cameraObject, "Exiting the default gravity-enabled game tool");

            typeof(GameSimulationTool).GetField("_collisions", any).SetValue(tool, false);
            click();
            Check(session.Running && !session.Motor.Collisions && !session.Motor.UseGravity && !session.Motor.Controller.enabled,
                "Turning off Scene Collisions also disables gravity and jump through the actual game-tool entry");
            floor = Floor();
            motorObject = session.Motor.gameObject;
            cameraObject = session.ViewCamera.gameObject;
            InjectFrames();
            injected.Frame = new MotionInputFrame(Vector2.zero, 0f, jump: true);
            session.Motor.Step(0.02f);
            Check(Mathf.Abs(avatar.transform.position.y - originalPosition.y) < 0.0001f &&
                module.GetParam("VelocityY").FloatValue() == 0f,
                "A jump frame cannot lift the game-tool avatar when Scene Collisions is disabled");
            var unsupported = originalPosition + Vector3.right * 51f;
            avatar.transform.position = unsupported;
            session.Motor.transform.position = unsupported;
            Physics.SyncTransforms();
            injected.Frame = new MotionInputFrame(Vector2.right, 0f, jump: true);
            session.Motor.Step(0.1f);
            injected.Frame = default;
            for (var frame = 0; frame < 30; frame++) session.Motor.Step(0.1f);
            Check(SupportBelow(avatar.transform.position).Length == 0 && !session.Motor.LastResult.Grounded &&
                Mathf.Abs(avatar.transform.position.y - unsupported.y) < 0.0001f &&
                module.GetParam("VelocityY").FloatValue() == 0f,
                "A collision-free game-tool avatar neither jumps nor falls even with no actual floor below it");
            click();
            CheckRestored(floor, motorObject, cameraObject, "Exiting the collision-free game tool");
        }
        finally
        {
            tool?.Dispose();
            if (manager) { manager.Module = null; UnityEngine.Object.DestroyImmediate(manager.gameObject); }
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            EditorApplication.isPaused = paused;
        }
    }

    private static void CheckThirdPersonCamera()
    {
        GameObject avatar = null;
        GameObject wall = null;
        GestureManager manager = null;
        ControlledModeSession session = null;
        var paused = EditorApplication.isPaused;
        using (var keyboard = new KeyboardMotionInputSource(true))
        {
            try
            {
                EditorApplication.isPaused = false;
                avatar = new GameObject("Pro third-person camera validation avatar");
                avatar.transform.position = new Vector3(1450f, 90f, 1450f);
                avatar.AddComponent<Animator>();
                var visible = avatar.AddComponent<MeshRenderer>();
                var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
                descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
                var module = new ModuleVrc3(descriptor) { Active = true, Settings = new ModuleSettings() };
                foreach (var name in ParameterNames)
                    module.Params.Add(name, new Vrc3Param(name, AnimatorControllerParameterType.Float));
                manager = new GameObject("Pro third-person camera validation manager").AddComponent<GestureManager>();
                manager.Module = module;
                session = new ControlledModeSession();
                Check(session.Start(manager, keyboard, viewMode: SimulationViewMode.FirstPerson),
                    "The third-person fixture starts from an explicit first-person session: " + session.Error);
                var preview = session.ViewCamera;
                var camera = preview.PreviewCamera;
                var cameraObject = preview.gameObject;
                const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
                var toggle = (Action)typeof(KeyboardMotionInputSource).GetField("ViewToggleRequested", any).GetValue(keyboard);
                var zoom = (Action<float>)typeof(KeyboardMotionInputSource).GetField("CameraZoomRequested", any).GetValue(keyboard);
                Action<string> render = method => typeof(MotionPreviewCamera).GetMethod(method, any)
                    .Invoke(preview, new object[] { camera });
                Check(!session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.FirstPerson,
                    "Explicit first person starts with the optional third-person feature disabled");
                var disabledDistance = session.CameraDistance;
                zoom(-1f);
                Check(session.CameraDistance == disabledDistance && session.ViewMode == SimulationViewMode.FirstPerson,
                    "Wheel input cannot enable third person when the feature is disabled");

                toggle();
                Check(session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.ThirdPerson &&
                    Mathf.Abs(session.CameraDistance - 2f) < 0.0001f,
                    "F5 enables third person at its default remembered distance");
                session.SetCameraDistance(3f);
                zoom(1f);
                Check(Mathf.Abs(session.CameraDistance - 2.75f) < 0.0001f &&
                    Mathf.Abs(preview.Distance - preview.Height * 2.75f) < 0.0001f,
                    "Positive wheel input zooms inward by a quarter avatar height and updates the public UI distance");
                var distance = session.CameraDistance;
                zoom(float.NaN);
                zoom(float.PositiveInfinity);
                zoom(float.NegativeInfinity);
                zoom(0f);
                Check(session.CameraDistance == distance, "Nonfinite and zero wheel input cannot corrupt camera distance");
                session.SetCameraDistance(float.NaN);
                Check(session.CameraDistance == 2f, "A nonfinite distance setting uses the two-height default");
                session.SetCameraDistance(float.PositiveInfinity);
                Check(session.CameraDistance == 2f, "An infinite distance setting uses the same finite default");
                session.SetCameraDistance(100f);
                Check(session.CameraDistance == 6f, "Distance settings retain the six-height outer limit");
                session.AdjustCameraDistance(-100f);
                Check(session.CameraDistance == 6f, "Wheel zoom-out cannot exceed the outer distance limit");
                session.SetCameraDistance(0.051f);
                Check(session.ViewMode == SimulationViewMode.ThirdPerson && session.ThirdPersonEnabled && preview.Distance < 0.1f,
                    "A positive distance just outside the snap threshold remains a genuinely close third-person view");
                session.SetCameraDistance(0.05f);
                Check(session.CameraDistance == 0f && session.ViewMode == SimulationViewMode.FirstPerson && session.ThirdPersonEnabled,
                    "Reaching the closest distance enters first person while keeping third person enabled");
                render("BeginCameraRendering");
                Check(visible.forceRenderingOff, "The closest first-person camera applies first-person avatar visibility");
                zoom(-1f);
                Check(session.CameraDistance == 0.25f && session.ViewMode == SimulationViewMode.ThirdPerson &&
                    session.ThirdPersonEnabled && !visible.forceRenderingOff,
                    "Zooming outward from the closest view returns to third person and immediately restores renderer visibility");
                render("EndCameraRendering");

                session.SetCameraDistance(3f);
                session.SetCameraDistance(0f);
                toggle();
                Check(!session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.FirstPerson,
                    "F5 disables the feature even when an enabled third-person camera is zoomed into first person");
                disabledDistance = session.CameraDistance;
                zoom(-2f);
                Check(session.CameraDistance == disabledDistance && session.ViewMode == SimulationViewMode.FirstPerson,
                    "Wheel input stays inactive after F5 disables a zoomed-in third-person camera");
                toggle();
                Check(session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.ThirdPerson && session.CameraDistance == 3f,
                    "Re-enabling third person restores the last positive distance rather than remaining at zero");
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    toggle();
                    Check(!session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.FirstPerson,
                        "Repeated F5 toggles consistently disable third person, cycle " + repeat);
                    toggle();
                    Check(session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.ThirdPerson && session.CameraDistance == 3f,
                        "Repeated F5 toggles consistently restore third person, cycle " + repeat);
                }

                preview.ApplyView();
                var bodyPivot = BodyAxisViewpoint(avatar.transform, preview.ViewPosition);
                var direction = (preview.transform.position - bodyPivot).normalized;
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Pro third-person near-body-pivot obstruction";
                wall.transform.position = bodyPivot + direction * 0.2f;
                wall.transform.localScale = Vector3.one * 0.15f;
                Physics.SyncTransforms();
                preview.ApplyView();
                Check(Vector3.Distance(preview.transform.position, bodyPivot) < 0.2f &&
                    session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.ThirdPerson && session.CameraDistance == 3f,
                    "A wall can shorten the physical camera boom without disabling third person or changing its configured distance");
                render("BeginCameraRendering");
                Check(!visible.forceRenderingOff, "Obstruction-shortened third person keeps third-person visibility rules");
                render("EndCameraRendering");
                UnityEngine.Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
                preview.ApplyView();
                Check(Vector3.Distance(preview.transform.position, bodyPivot) > 4f && session.ThirdPersonEnabled,
                    "Removing an obstruction restores the configured third-person distance without another toggle");

                session.SetViewMode(SimulationViewMode.SceneCamera);
                distance = session.CameraDistance;
                zoom(1f);
                Check(!session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.SceneCamera && session.CameraDistance == distance,
                    "Wheel input cannot zoom or leave an explicitly selected Scene observer");
                toggle();
                Check(session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.ThirdPerson && !keyboard.ObserverView &&
                    keyboard.Suspended && !keyboard.Captured &&
                    (bool)typeof(KeyboardMotionInputSource).GetField("_captureRequested", any).GetValue(keyboard),
                    "F5 from an observer enables third person and requests fresh Game input capture");
                session.Stop();
                Check(!cameraObject && !visible.forceRenderingOff && !keyboard.Captured &&
                    typeof(KeyboardMotionInputSource).GetField("ViewToggleRequested", any).GetValue(keyboard) == null &&
                    typeof(KeyboardMotionInputSource).GetField("CameraZoomRequested", any).GetValue(keyboard) == null,
                    "Stopping third-person simulation restores visibility and releases camera input and subscriptions");
            }
            finally
            {
                session?.Dispose();
                if (manager) { manager.Module = null; UnityEngine.Object.DestroyImmediate(manager.gameObject); }
                if (wall) UnityEngine.Object.DestroyImmediate(wall);
                if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
                EditorApplication.isPaused = paused;
            }
        }
    }

    private static Vector3 BodyAxisViewpoint(Transform target, Vector3 viewPosition) =>
        new Vector3(target.position.x, target.TransformPoint(viewPosition).y, target.position.z);

    private static void CheckThirdPersonDescriptorHeights()
    {
        foreach (var eyeHeight in new[] { 0.6f, 1.6f, 2.5f })
        {
            GameObject avatar = null;
            GestureManager manager = null;
            ControlledModeSession session = null;
            try
            {
                avatar = new GameObject("Pro live descriptor-height avatar");
                avatar.transform.SetPositionAndRotation(new Vector3(1200f, 40f + eyeHeight, 1300f), Quaternion.Euler(0f, 30f, 0f));
                avatar.AddComponent<Animator>();
                var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
                descriptor.ViewPosition = new Vector3(0.35f, eyeHeight, -0.45f);
                var module = new ModuleVrc3(descriptor) { Active = true, Settings = new ModuleSettings() };
                foreach (var name in ParameterNames)
                    module.Params.Add(name, new Vrc3Param(name, AnimatorControllerParameterType.Float));
                manager = new GameObject("Pro live descriptor-height manager").AddComponent<GestureManager>();
                manager.Module = module;
                session = new ControlledModeSession();
                Check(session.Start(manager, new SessionMotionInput(), collisions: false, previewCamera: true,
                    viewMode: SimulationViewMode.ThirdPerson), "A real session starts with descriptor height " + eyeHeight + ": " + session.Error);
                var preview = session.ViewCamera;
                var camera = preview.PreviewCamera;
                var expected = BodyAxisViewpoint(avatar.transform, descriptor.ViewPosition);
                var viewport = camera.WorldToViewportPoint(expected);
                Check(viewport.z > 0f && Mathf.Abs(viewport.x - 0.5f) < 0.0001f && Mathf.Abs(viewport.y - 0.5f) < 0.0001f &&
                    Vector3.Distance(preview.transform.position, expected + preview.transform.rotation * Vector3.back * preview.Distance) < 0.001f,
                    "The live session centers exact descriptor world height and body X/Z for eye height " + eyeHeight);
                session.Motor.EnableFreeLook();
                preview.YawOverride = -80f;
                var bodyRotation = avatar.transform.rotation;
                preview.ApplyView();
                var orbitPosition = preview.transform.position;
                avatar.transform.rotation = Quaternion.Euler(0f, 150f, 0f);
                preview.ApplyView();
                Check(Vector3.Distance(preview.transform.position, orbitPosition) < 0.001f &&
                    Quaternion.Angle(preview.transform.rotation, Quaternion.Euler(0f, -80f, 0f)) < 0.001f,
                    "Independent body yaw cannot shift live free-camera framing through descriptor X/Z offsets at height " + eyeHeight);
                avatar.transform.rotation = bodyRotation;
                session.SetViewMode(SimulationViewMode.FirstPerson);
                Check(Vector3.Distance(preview.transform.position, avatar.transform.TransformPoint(descriptor.ViewPosition)) < 0.001f,
                    "The same live descriptor retains complete X/Y/Z in first person at height " + eyeHeight);
                session.Stop();
            }
            finally
            {
                session?.Dispose();
                if (manager) { manager.Module = null; UnityEngine.Object.DestroyImmediate(manager.gameObject); }
                if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            }
        }
    }

    private static void CheckGameSimulationFreeLook()
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const string preference = "GM3 SceneCamera";
        var previousBridge = AvatarTools.UpdateSceneCamera.SimulationView;
        var hadPreference = EditorPrefs.HasKey(preference);
        var previousPreference = EditorPrefs.GetBool(preference);
        var previousFocus = EditorWindow.focusedWindow;
        var cursorLock = Cursor.lockState;
        var cursorVisible = Cursor.visible;
        var paused = EditorApplication.isPaused;
        GameObject avatar = null;
        GestureManager manager = null;
        GameSimulationTool tool = null;
        try
        {
            EditorApplication.isPaused = false;
            AvatarTools.UpdateSceneCamera.SimulationView = null;
            avatar = new GameObject("Pro Game free-look validation avatar");
            avatar.transform.SetPositionAndRotation(new Vector3(85f, 30f, 75f), Quaternion.Euler(0f, 25f, 0f));
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            var originalPosition = avatar.transform.position;
            var originalRotation = avatar.transform.rotation;
            var module = new ModuleVrc3(descriptor) { Active = true, Settings = new ModuleSettings() };
            foreach (var name in ParameterNames)
                module.Params.Add(name, new Vrc3Param(name, AnimatorControllerParameterType.Float));
            manager = new GameObject("Pro Game free-look validation manager").AddComponent<GestureManager>();
            manager.Module = module;
            tool = new GameSimulationTool(manager);
            typeof(GameSimulationTool).GetField("_viewMode", instance).SetValue(tool, SimulationViewMode.ThirdPerson);
            ((MicrophoneLipSync)typeof(GameSimulationTool).GetField("_lipSync", instance).GetValue(tool)).Enabled = false;
            var session = (ControlledModeSession)typeof(GameSimulationTool).GetField("_session", instance).GetValue(tool);
            var gameToggle = typeof(GameSimulationTool).GetMethod("Toggle", instance);
            var keyboardField = typeof(GameSimulationTool).GetField("_keyboard", instance);
            var inputField = typeof(CharacterMotionMotor).GetField("_input", instance);
            var suspendedField = typeof(KeyboardMotionInputSource).GetField("<Suspended>k__BackingField", instance);
            var capturedField = typeof(KeyboardMotionInputSource).GetField("_captured", instance);
            var freeLookInput = typeof(KeyboardMotionInputSource).GetMethod("ProcessGameFreeLookInput", instance);
            var cameraInput = typeof(KeyboardMotionInputSource).GetMethod("ProcessGameCameraInput", instance);
            KeyboardMotionInputSource keyboard = null;
            GameFrameMotionInput frames = null;
            void ClickGame() => gameToggle.Invoke(tool, new object[] { module });
            void Start()
            {
                ClickGame();
                Check(session.Running && session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.ThirdPerson,
                    "The real Game Simulation tool starts a third-person free-look fixture: " + session.Error);
                keyboard = (KeyboardMotionInputSource)keyboardField.GetValue(tool);
                frames = new GameFrameMotionInput(keyboard);
                inputField.SetValue(session.Motor, frames);
            }

            void Capture(bool captured = true, bool suspended = false)
            {
                // Keep actual keyboard/session subscriptions while supplying the
                // capture state and frames without depending on desktop input.
                typeof(KeyboardMotionInputSource).GetField("_previousCursorLock", instance).SetValue(keyboard, cursorLock);
                typeof(KeyboardMotionInputSource).GetField("_previousCursorVisible", instance).SetValue(keyboard, cursorVisible);
                capturedField.SetValue(keyboard, captured);
                suspendedField.SetValue(keyboard, suspended);
            }

            void GameKeys(bool tab, bool forwardPressed = false, bool modified = false, bool shift = false) =>
                freeLookInput.Invoke(keyboard, new object[] { tab, forwardPressed, modified, shift });
            void CameraKeys(bool f5 = false, float wheel = 0f) =>
                cameraInput.Invoke(keyboard, new object[] { f5, wheel, false, false });
            void Step(Vector2 move, Vector2 look = default, float turn = 0f)
            {
                frames.Move = move;
                frames.Look = look;
                frames.Turn = turn;
                session.Motor.Step(0.1f);
                if (session.Running) session.ViewCamera.ApplyView();
            }

            void EnterFreeLook()
            {
                session.SetViewMode(SimulationViewMode.ThirdPerson);
                Capture();
                GameKeys(true);
                Check(session.Motor.FreeLook && keyboard.FreeLook && session.ViewCamera.YawOverride.HasValue,
                    "Captured Tab enables third-person free look through the production keyboard event and session");
            }

            void CheckViewReset(string label)
            {
                Check(!session.Motor.FreeLook && !keyboard.FreeLook && !session.ViewCamera.YawOverride.HasValue,
                    label + " clears motor, keyboard, and camera free-look state together");
            }

            void CheckStopped(KeyboardMotionInputSource stoppedKeyboard, string label)
            {
                Check(!session.Running && AvatarTools.UpdateSceneCamera.SimulationView == null && !manager.IsPlayerControlled &&
                    module.SimulationCamera == null && !stoppedKeyboard.FreeLook && !stoppedKeyboard.Captured &&
                    typeof(KeyboardMotionInputSource).GetField("FreeLookRequested", instance).GetValue(stoppedKeyboard) == null &&
                    typeof(KeyboardMotionInputSource).GetField("ForwardMovementRequested", instance).GetValue(stoppedKeyboard) == null &&
                    typeof(KeyboardMotionInputSource).GetField("ViewToggleRequested", instance).GetValue(stoppedKeyboard) == null &&
                    typeof(KeyboardMotionInputSource).GetField("CameraZoomRequested", instance).GetValue(stoppedKeyboard) == null &&
                    !Resources.FindObjectsOfTypeAll<MotionPreviewCamera>().Any(camera => camera.Target == avatar.transform),
                    label + " disposes all free-look/camera subscriptions and releases the view bridge and temporary camera");
                Check(Vector3.Distance(avatar.transform.position, originalPosition) < 0.0001f &&
                    Quaternion.Angle(avatar.transform.rotation, originalRotation) < 0.001f,
                    label + " restores the original avatar position and body rotation");
            }

            Start();
            GameKeys(true);
            Capture(captured: false);
            GameKeys(true);
            Check(!keyboard.FreeLook && !session.Motor.FreeLook, "Tab cannot enable free look before Game input is captured");
            Capture(suspended: true);
            GameKeys(true);
            Check(!keyboard.FreeLook && !session.Motor.FreeLook, "Suspended captured input cannot enable free look");
            Capture();
            GameKeys(true, modified: true);
            GameKeys(true, shift: true);
            Check(!keyboard.FreeLook && !session.Motor.FreeLook, "Modified Tab and Shift+Tab cannot enter free look");
            var locomotionFlag = typeof(ModuleVrc3).GetField("LocomotionDisabled", instance);
            locomotionFlag.SetValue(module, true);
            GameKeys(true);
            Check(!session.Motor.FreeLook, "Avatar locomotion restrictions also block the session's free-look enable event");
            locomotionFlag.SetValue(module, false);
            session.SetViewMode(SimulationViewMode.FirstPerson);
            Capture();
            GameKeys(true);
            Check(!keyboard.FreeLook && !session.Motor.FreeLook, "Captured Tab in first person cannot enable third-person free look");

            EnterFreeLook();
            var bodyRotation = avatar.transform.rotation;
            var bodyPosition = avatar.transform.position;
            var initialYaw = avatar.transform.eulerAngles.y;
            Step(Vector2.zero, new Vector2(47f, 13f));
            var orbitYaw = session.Motor.ViewYaw;
            Check(Quaternion.Angle(avatar.transform.rotation, bodyRotation) < 0.001f &&
                Vector3.Distance(avatar.transform.position, bodyPosition) < StandingHeightTolerance &&
                Mathf.Abs(Mathf.DeltaAngle(initialYaw + 47f, orbitYaw)) < 0.001f && session.Motor.ViewPitch == 13f &&
                Mathf.Abs(Mathf.DeltaAngle(session.ViewCamera.transform.eulerAngles.y, orbitYaw)) < 0.001f &&
                session.ViewCamera.YawOverride.HasValue && Mathf.Abs(module.GetParam("AngularY").FloatValue()) < 0.001f,
                "Free mouse look orbits the real preview camera and changes pitch without rotating or moving the avatar");
            var orbitPosition = session.ViewCamera.transform.position;
            GameKeys(true);
            session.ViewCamera.ApplyView();
            Check(session.Motor.FreeLook && keyboard.FreeLook && session.Motor.ViewPitch == 13f &&
                Mathf.Abs(Mathf.DeltaAngle(session.Motor.ViewYaw, orbitYaw)) < 0.001f &&
                Vector3.Distance(session.ViewCamera.transform.position, orbitPosition) < 0.0001f,
                "Repeated Tab is idempotent and cannot recenter the free camera or reset its pitch");
            var distance = session.CameraDistance;
            CameraKeys(wheel: 1f);
            Step(Vector2.zero, new Vector2(0f, -4f));
            var bodyPivot = BodyAxisViewpoint(avatar.transform, session.ViewCamera.ViewPosition);
            Check(session.Motor.FreeLook && keyboard.FreeLook && session.CameraDistance == distance - 0.25f &&
                session.Motor.ViewPitch == 9f && Quaternion.Angle(avatar.transform.rotation, bodyRotation) < 0.001f &&
                Mathf.Abs(Vector3.Distance(session.ViewCamera.transform.position, bodyPivot) -
                    session.ViewCamera.Height * session.CameraDistance) < 0.001f,
                "Wheel zoom and vertical mouse look remain active while the horizontal free-camera yaw stays independent");

            for (var direction = -1; direction <= 1; direction += 2)
            {
                var beforeViewYaw = session.Motor.ViewYaw;
                var beforeRotation = avatar.transform.rotation;
                var beforePosition = avatar.transform.position;
                Step(Vector2.zero, turn: direction);
                Check(frames.LastFrame.Move == Vector2.zero && frames.LastFrame.Turn == 0f && frames.LastFrame.ViewTurn == direction &&
                    Mathf.Abs(Mathf.DeltaAngle(beforeViewYaw, session.Motor.ViewYaw) - direction * 12f) < 0.001f &&
                    Mathf.Abs(Mathf.DeltaAngle(session.ViewCamera.transform.eulerAngles.y, session.Motor.ViewYaw)) < 0.001f &&
                    Quaternion.Angle(avatar.transform.rotation, beforeRotation) < 0.001f &&
                    Vector3.Distance(avatar.transform.position, beforePosition) < StandingHeightTolerance &&
                    Mathf.Abs(module.GetParam("AngularY").FloatValue()) < 0.001f,
                    "After Tab, " + (direction < 0 ? "comma" : "period") + " rotates only the camera through the production Game input path");
            }
            orbitYaw = session.Motor.ViewYaw;
            orbitPosition = session.ViewCamera.transform.position;
            GameKeys(true);
            session.ViewCamera.ApplyView();
            Check(Mathf.Abs(Mathf.DeltaAngle(session.Motor.ViewYaw, orbitYaw)) < 0.001f &&
                Vector3.Distance(session.ViewCamera.transform.position, orbitPosition) < 0.0001f,
                "Repeated Tab preserves the camera orbit established by comma/period turning");

            for (var direction = -1; direction <= 1; direction += 2)
            {
                var beforeBodyYaw = avatar.transform.eulerAngles.y;
                var beforeViewYaw = session.Motor.ViewYaw;
                var beforePosition = avatar.transform.position;
                Step(Vector2.right * direction, turn: -direction);
                Check(frames.LastFrame.Move == Vector2.zero && frames.LastFrame.Turn == direction && frames.LastFrame.ViewTurn == -direction &&
                    Mathf.Abs(Mathf.DeltaAngle(beforeBodyYaw, avatar.transform.eulerAngles.y) - direction * 12f) < 0.001f &&
                    Mathf.Abs(Mathf.DeltaAngle(beforeViewYaw, session.Motor.ViewYaw) + direction * 12f) < 0.001f &&
                    Mathf.Abs(Mathf.DeltaAngle(session.ViewCamera.transform.eulerAngles.y, session.Motor.ViewYaw)) < 0.001f &&
                    Vector3.Distance(avatar.transform.position, beforePosition) < StandingHeightTolerance &&
                    Mathf.Abs(module.GetParam("AngularY").FloatValue() - direction * 120f) < 0.02f,
                    "Opposite A/D and comma/period inputs independently rotate the body and camera without cancellation, direction " + direction);
            }
            orbitYaw = session.Motor.ViewYaw;

            for (var direction = -1; direction <= 1; direction += 2)
            {
                var beforeYaw = avatar.transform.eulerAngles.y;
                var beforePosition = avatar.transform.position;
                Step(Vector2.right * direction);
                Check(frames.LastFrame.Move == Vector2.zero && frames.LastFrame.Turn == direction && frames.LastFrame.ViewTurn == 0f &&
                    Mathf.Abs(Mathf.DeltaAngle(beforeYaw, avatar.transform.eulerAngles.y) - direction * 12f) < 0.001f &&
                    Vector3.Distance(avatar.transform.position, beforePosition) < StandingHeightTolerance &&
                    Mathf.Abs(Mathf.DeltaAngle(session.Motor.ViewYaw, orbitYaw)) < 0.001f &&
                    Mathf.Abs(Mathf.DeltaAngle(session.ViewCamera.transform.eulerAngles.y, orbitYaw)) < 0.001f &&
                    Mathf.Abs(module.GetParam("AngularY").FloatValue() - direction * 120f) < 0.02f,
                    "Free-look " + (direction < 0 ? "A" : "D") + " turns the body in place through CreateGameFrame while camera yaw remains fixed");
            }

            for (var direction = 1; direction >= -1; direction -= 2)
            {
                if (!session.Motor.FreeLook) EnterFreeLook();
                Step(Vector2.zero, new Vector2(direction * 31f, 0f), turn: direction);
                session.Motor.Halt();
                var targetYaw = session.Motor.ViewYaw;
                var beforeYaw = avatar.transform.eulerAngles.y;
                var beforePosition = avatar.transform.position;
                GameKeys(false, forwardPressed: true);
                Check(!keyboard.FreeLook && !session.Motor.FreeLook &&
                    Mathf.Abs(Mathf.DeltaAngle(beforeYaw, avatar.transform.eulerAngles.y)) < 0.001f,
                    "A new " + (direction > 0 ? "W" : "S") + " exits free look and queues body alignment for the real motor step");
                Step(Vector2.up * direction);
                var delta = avatar.transform.position - beforePosition;
                delta.y = 0f;
                var expectedDirection = Quaternion.Euler(0f, targetYaw, 0f) * Vector3.forward * direction;
                Check(Mathf.Abs(Mathf.DeltaAngle(avatar.transform.eulerAngles.y, targetYaw)) < 0.001f &&
                    Vector3.Dot(delta, expectedDirection) > 0.05f && Vector3.Cross(delta.normalized, expectedDirection).magnitude < 0.001f &&
                    !session.ViewCamera.YawOverride.HasValue &&
                    Mathf.Abs(Mathf.DeltaAngle(session.ViewCamera.transform.eulerAngles.y, targetYaw)) < 0.001f &&
                    Mathf.Abs(module.GetParam("AngularY").FloatValue() - Mathf.DeltaAngle(beforeYaw, targetYaw) / 0.1f) < 0.02f,
                    "The new " + (direction > 0 ? "W" : "S") + " aligns the avatar to camera yaw, publishes the real turn, and moves " +
                    (direction > 0 ? "forward" : "backward") + " along the aligned body direction");
            }

            session.Motor.Halt();
            bodyRotation = avatar.transform.rotation;
            Step(Vector2.right);
            Check(frames.LastFrame.Move == Vector2.right && frames.LastFrame.Turn == 0f && frames.LastFrame.ViewTurn == 0f &&
                Quaternion.Angle(avatar.transform.rotation, bodyRotation) < 0.001f && session.Motor.LastResult.LocalVelocity.x > 0.5f,
                "After free look ends, the same production Game frame restores ordinary A/D strafing");
            for (var direction = -1; direction <= 1; direction += 2)
            {
                var beforeYaw = avatar.transform.eulerAngles.y;
                Step(Vector2.zero, turn: direction);
                Check(frames.LastFrame.Turn == direction && frames.LastFrame.ViewTurn == 0f &&
                    Mathf.Abs(Mathf.DeltaAngle(beforeYaw, avatar.transform.eulerAngles.y) - direction * 12f) < 0.001f &&
                    Mathf.Abs(Mathf.DeltaAngle(session.ViewCamera.transform.eulerAngles.y, avatar.transform.eulerAngles.y)) < 0.001f &&
                    Mathf.Abs(module.GetParam("AngularY").FloatValue() - direction * 120f) < 0.02f,
                    "After W/S leaves free look, comma/period again turn the body and camera together, direction " + direction);
            }
            Step(Vector2.up);
            Step(Vector2.up);
            var heldStart = avatar.transform.position;
            var heldYaw = avatar.transform.eulerAngles.y;
            GameKeys(true, forwardPressed: false);
            Step(Vector2.up, new Vector2(29f, 0f));
            GameKeys(false, forwardPressed: false);
            Step(Vector2.up);
            Check(keyboard.FreeLook && session.Motor.FreeLook &&
                Mathf.Abs(Mathf.DeltaAngle(avatar.transform.eulerAngles.y, heldYaw)) < 0.001f &&
                Mathf.Abs(Mathf.DeltaAngle(session.Motor.ViewYaw, heldYaw + 29f)) < 0.001f &&
                Vector3.Dot(avatar.transform.position - heldStart, Quaternion.Euler(0f, heldYaw, 0f) * Vector3.forward) > 0.2f,
                "W already held when Tab is pressed continues moving without acting as a new press or canceling free look");

            session.SetViewMode(SimulationViewMode.SceneCamera);
            CheckViewReset("Selecting the Scene observer");
            GameKeys(true);
            Check(!session.Motor.FreeLook && !keyboard.FreeLook, "The Scene observer cannot enter free look with Tab");
            EnterFreeLook();
            Step(Vector2.zero, new Vector2(23f, 0f), turn: 1f);
            CameraKeys(f5: true);
            CheckViewReset("F5 returning to first person");
            Check(session.ViewMode == SimulationViewMode.FirstPerson && !session.ThirdPersonEnabled,
                "F5 retains its normal third-person feature toggle while clearing free look");
            var firstPersonYaw = avatar.transform.eulerAngles.y;
            Step(Vector2.zero, turn: -1f);
            Check(frames.LastFrame.Turn == -1f && frames.LastFrame.ViewTurn == 0f &&
                Mathf.Abs(Mathf.DeltaAngle(firstPersonYaw, avatar.transform.eulerAngles.y) + 12f) < 0.001f &&
                Mathf.Abs(Mathf.DeltaAngle(session.ViewCamera.transform.eulerAngles.y, avatar.transform.eulerAngles.y)) < 0.001f,
                "F5 restores first-person comma/period body turning without retaining a camera-only turn channel");
            EnterFreeLook();
            Step(Vector2.zero, new Vector2(-17f, 0f), turn: -1f);
            CameraKeys(wheel: 100f);
            CheckViewReset("Zooming to the nearest first-person distance");
            Check(session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.FirstPerson && session.CameraDistance == 0f,
                "Nearest-distance free-look cleanup preserves the enabled third-person feature");
            CameraKeys(wheel: -1f);
            EnterFreeLook();
            Step(Vector2.zero, turn: 1f);
            var stoppedKeyboard = keyboard;
            ClickGame();
            CheckStopped(stoppedKeyboard, "Normal exit from free look");
            freeLookInput.Invoke(stoppedKeyboard, new object[] { true, true, false, false });
            Check(!stoppedKeyboard.FreeLook && AvatarTools.UpdateSceneCamera.SimulationView == null,
                "Disposed free-look helpers cannot replay Tab or forward movement into a stopped session");

            Start();
            EnterFreeLook();
            Step(Vector2.zero, turn: -1f);
            stoppedKeyboard = keyboard;
            EditorApplication.isPaused = true;
            session.Poll();
            CheckStopped(stoppedKeyboard, "Pausing during free look");
            EditorApplication.isPaused = false;
            Start();
            EnterFreeLook();
            Step(Vector2.zero, turn: 1f);
            stoppedKeyboard = keyboard;
            frames.Error = new InvalidOperationException("Injected free-look input failure");
            session.Motor.Step(0.1f);
            CheckStopped(stoppedKeyboard, "Input failure during free look");
            Check(session.Error.Contains("Injected free-look input failure"), "Free-look failure cleanup preserves the actual input diagnostic");
            Check(EditorPrefs.HasKey(preference) == hadPreference && EditorPrefs.GetBool(preference) == previousPreference,
                "All free-look input and mode transitions preserve the ordinary Scene Camera preference");
        }
        finally
        {
            tool?.Dispose();
            AvatarTools.UpdateSceneCamera.SimulationView = previousBridge;
            if (hadPreference) EditorPrefs.SetBool(preference, previousPreference);
            else EditorPrefs.DeleteKey(preference);
            if (manager) { manager.Module = null; UnityEngine.Object.DestroyImmediate(manager.gameObject); }
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            Cursor.lockState = cursorLock;
            Cursor.visible = cursorVisible;
            if (previousFocus) previousFocus.Focus();
            EditorApplication.isPaused = paused;
        }
    }

    private static void CheckObserverCamera()
    {
        SceneView observer = null;
        SceneView secondObserver = null;
        var previousObserver = SceneView.lastActiveSceneView;
        GameObject avatar = null;
        GameObject originalCameraObject = null;
        GestureManager manager = null;
        ControlledModeSession session = null;
        var paused = EditorApplication.isPaused;
        try
        {
            EditorApplication.isPaused = false;
            Check((int)SimulationViewMode.FirstPerson == 0 && (int)SimulationViewMode.ThirdPerson == 1 &&
                (int)SimulationViewMode.SceneCamera == 2, "Adding observer mode preserves the existing serialized view values");
            avatar = new GameObject("Pro observer camera validation avatar");
            avatar.transform.position = new Vector3(1200f, 70f, 1300f);
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            var module = new ModuleVrc3(descriptor) { Active = true, Settings = new ModuleSettings() };
            foreach (var name in ParameterNames)
                module.Params.Add(name, new Vrc3Param(name, AnimatorControllerParameterType.Float));
            module.GetParam("Upright").Set(module, 1f);
            manager = new GameObject("Pro observer camera validation manager").AddComponent<GestureManager>();
            manager.Module = module;
            originalCameraObject = new GameObject("Pro observer original camera");
            var originalCamera = originalCameraObject.AddComponent<Camera>();
            originalCamera.transform.SetPositionAndRotation(new Vector3(50f, 30f, 10f), Quaternion.Euler(13f, 27f, 0f));
            originalCamera.orthographic = true;
            originalCamera.orthographicSize = 9f;
            originalCamera.aspect = 1.25f;
            var originalCameraPosition = originalCamera.transform.position;
            var originalCameraRotation = originalCamera.transform.rotation;
            var originalProjection = originalCamera.projectionMatrix;
            module.SimulationCamera = originalCamera;
            var originalPosition = avatar.transform.position;
            var input = new SessionMotionInput();
            session = new ControlledModeSession();

            // This runner owns an isolated batch fixture. Close its editor Scene
            // windows so the existing scene-camera fallback is exercised for real.
            foreach (var view in SceneView.sceneViews.Cast<SceneView>().ToArray())
                if (view) view.Close();
            Check(!SceneView.lastActiveSceneView && SceneView.sceneViews.Count == 0,
                "The fallback fixture has no SceneView window or active Scene observer");
            Check(session.Start(manager, input, previewCamera: true),
                "Observer simulation starts with only its existing scene camera: " + session.Error);
            var fallbackObject = session.ViewCamera.gameObject;
            var fallbackCamera = session.ViewCamera.PreviewCamera;
            session.Poll();
            Check(session.ViewMode == SimulationViewMode.SceneCamera && !session.FollowObserverCamera &&
                Vector3.Distance(fallbackCamera.transform.position, originalCameraPosition) < 0.0001f &&
                Quaternion.Angle(fallbackCamera.transform.rotation, originalCameraRotation) < 0.001f,
                "The default Game observer mirrors the existing scene camera when Scene views are closed");
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            for (var frame = 0; frame < 3; frame++) { session.Motor.Step(0.1f); session.Poll(); }
            Check(Vector3.Distance(avatar.transform.position, originalPosition) > 0.3f &&
                Vector3.Distance(fallbackCamera.transform.position, originalCameraPosition) < 0.0001f &&
                originalCamera.transform.position == originalCameraPosition,
                "Movement leaves both the fixed Game fallback and its original scene camera in place by default");
            session.SetObserverCameraFollow(true);
            session.Poll();
            Check(Vector3.Distance(fallbackCamera.transform.position, originalCameraPosition) < 0.0001f,
                "Enabling fallback follow does not catch up movement from the fixed-view period");
            var fallbackBeforeMove = avatar.transform.position;
            input.Frame = new MotionInputFrame(Vector2.right, 0f);
            session.Motor.Step(0.1f);
            session.Poll();
            var fallbackDelta = avatar.transform.position - fallbackBeforeMove;
            Check(fallbackDelta.sqrMagnitude > 0.001f &&
                Vector3.Distance(fallbackCamera.transform.position, originalCameraPosition + fallbackDelta) < 0.001f &&
                originalCamera.transform.position == originalCameraPosition && originalCamera.transform.rotation == originalCameraRotation,
                "Following without a SceneView translates only the owned Game observer by actual avatar displacement");
            var followedFallbackPosition = fallbackCamera.transform.position;
            session.Poll();
            Check(Vector3.Distance(fallbackCamera.transform.position, followedFallbackPosition) < 0.0001f,
                "Repeated fallback polling does not add the same displacement twice");
            session.SetObserverCameraFollow(false);
            var beforeDisabledMove = avatar.transform.position;
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            session.Motor.Step(0.1f);
            session.Poll();
            Check(Vector3.Distance(avatar.transform.position, beforeDisabledMove) > 0.05f &&
                Vector3.Distance(fallbackCamera.transform.position, followedFallbackPosition) < 0.0001f,
                "Disabling fallback follow keeps its accumulated view position while the avatar continues moving");
            session.SetObserverCameraFollow(true);
            session.Poll();
            Check(Vector3.Distance(fallbackCamera.transform.position, followedFallbackPosition) < 0.0001f,
                "Re-enabling fallback follow does not replay movement accumulated while disabled");
            session.Stop();
            Check(!fallbackObject && module.SimulationCamera == originalCamera &&
                Vector3.Distance(avatar.transform.position, originalPosition) < 0.0001f &&
                originalCamera.transform.position == originalCameraPosition && originalCamera.transform.rotation == originalCameraRotation &&
                MatricesClose(originalCamera.projectionMatrix, originalProjection),
                "Fallback cleanup destroys its owned camera and restores the module reference without modifying the original camera");

            observer = ScriptableObject.CreateInstance<SceneView>();
            observer.titleContent = new GUIContent("Pro observer validation");
            observer.Show();
            observer.Focus();
            observer.LookAt(avatar.transform.position + new Vector3(0f, 1f, 0f), Quaternion.Euler(24f, 145f, 0f), 5.5f, true, true);
            observer.Repaint();
            observer.SendEvent(new Event { type = EventType.Repaint });
            Check(SceneView.lastActiveSceneView == observer && observer.camera,
                "The observer fixture owns a real active SceneView with a scene camera");
            CheckSceneCameraObserverSwitch(observer, avatar, manager, module, originalCamera);
            CheckSceneCameraLifecycle(observer, module);
            CheckGameSimulationSceneCamera(observer);
            var originalPivot = observer.pivot;
            var originalRotation = observer.rotation;
            var originalSize = observer.size;
            var originalOrthographic = observer.orthographic;
            Check(session.Start(manager, input, gravity: true, previewCamera: true),
                "Observer game simulation starts with its default view: " + session.Error);
            Check(session.ViewMode == SimulationViewMode.SceneCamera && !session.FollowObserverCamera &&
                session.ViewCamera && session.ViewCamera.Mode == SimulationViewMode.SceneCamera,
                "A default session keeps a temporary game camera in fixed Scene camera mode");
            var ownedCameraObject = session.ViewCamera.gameObject;

            void CheckMirroredCamera(SceneView view, string label)
            {
                view.Repaint();
                view.SendEvent(new Event { type = EventType.Repaint });
                session.Poll();
                var source = view.camera;
                var game = session.ViewCamera.PreviewCamera;
                Check(Vector3.Distance(game.transform.position, source.transform.position) < 0.001f &&
                    Quaternion.Angle(game.transform.rotation, source.transform.rotation) < 0.001f &&
                    game.orthographic == source.orthographic &&
                    Mathf.Abs(game.orthographicSize - source.orthographicSize) < 0.0001f &&
                    Mathf.Abs(game.fieldOfView - source.fieldOfView) < 0.0001f &&
                    Mathf.Abs(game.nearClipPlane - source.nearClipPlane) < 0.0001f &&
                    Mathf.Abs(game.farClipPlane - source.farClipPlane) < 0.01f,
                    label + " mirrors the actual Scene camera transform and projection settings after its view update");
                var position = game.transform.position;
                var rotation = game.transform.rotation;
                var projection = game.projectionMatrix;
                session.ViewCamera.ApplyView();
                Check(Vector3.Distance(game.transform.position, position) < 0.0001f &&
                    Quaternion.Angle(game.transform.rotation, rotation) < 0.001f && MatricesClose(game.projectionMatrix, projection),
                    "Runtime camera updates preserve the synchronized observer view instead of following the avatar");
            }

            CheckMirroredCamera(observer, "Initial observer view");
            input.Frame = new MotionInputFrame(Vector2.up, 0f);
            for (var frame = 0; frame < 3; frame++) { session.Motor.Step(0.1f); session.Poll(); }
            input.Frame = default;
            for (var frame = 0; frame < 2; frame++) { session.Motor.Step(0.1f); session.Poll(); }
            Check(Vector3.Distance(avatar.transform.position, originalPosition) > 0.3f &&
                Vector3.Distance(observer.pivot, originalPivot) < 0.001f &&
                Quaternion.Angle(observer.rotation, originalRotation) < 0.001f &&
                Mathf.Abs(observer.size - originalSize) < 0.0001f && observer.orthographic == originalOrthographic,
                "Default avatar movement leaves the Scene observer's pivot, rotation, zoom, and projection mode unchanged");
            CheckMirroredCamera(observer, "Fixed observer after avatar movement");

            session.SetObserverCameraFollow(true);
            session.Poll();
            Check(session.FollowObserverCamera && Vector3.Distance(observer.pivot, originalPivot) < 0.001f,
                "Enabling observer follow starts from the current avatar position without catching up prior movement");
            var ownedTranslation = Vector3.zero;
            Vector3 FollowStep(SceneView view, MotionInputFrame frame, float time, string label)
            {
                var pivot = view.pivot;
                var position = avatar.transform.position;
                var rotation = view.rotation;
                var size = view.size;
                var orthographic = view.orthographic;
                input.Frame = frame;
                session.Motor.Step(time);
                session.Poll();
                var delta = avatar.transform.position - position;
                Check(Vector3.Distance(view.pivot - pivot, delta) < 0.001f &&
                    Quaternion.Angle(view.rotation, rotation) < 0.001f && Mathf.Abs(view.size - size) < 0.0001f &&
                    view.orthographic == orthographic,
                    label + " translates the observer by actual avatar displacement while retaining manual rotation and zoom");
                var followedPivot = view.pivot;
                session.Poll();
                session.Poll();
                Check(Vector3.Distance(view.pivot, followedPivot) < 0.0001f,
                    "Repeated observer polling does not apply an already consumed avatar displacement again");
                return delta;
            }

            ownedTranslation += FollowStep(observer, new MotionInputFrame(Vector2.up, 0f), 0.1f, "Following horizontal movement");
            ownedTranslation += FollowStep(observer, new MotionInputFrame(Vector2.zero, 0f, pose: MotionPose.Crouch), 0.02f, "Changing posture during observer follow");
            Check(module.GetParam("Upright").FloatValue() == 0.6f, "Observer following leaves explicit posture control available");
            var jumpDelta = FollowStep(observer, new MotionInputFrame(Vector2.zero, 0f, jump: true), 0.02f, "Following a jump");
            ownedTranslation += jumpDelta;
            Check(jumpDelta.y > 0.04f, "Observer following includes actual vertical jump displacement");
            for (var frame = 0; frame < 100 && !session.Motor.LastResult.Grounded; frame++)
                ownedTranslation += FollowStep(observer, default, 0.02f, "Following airborne motion and landing");
            Check(session.Motor.LastResult.Grounded, "The observer jump fixture returns to its physical floor");
            session.Motor.Halt();
            ownedTranslation += FollowStep(observer, new MotionInputFrame(Vector2.zero, 1f), 0.1f, "Turning without observer orbit");

            session.SetObserverCameraFollow(false);
            var fixedPivot = observer.pivot;
            input.Frame = new MotionInputFrame(Vector2.right, 0f);
            for (var frame = 0; frame < 3; frame++) { session.Motor.Step(0.1f); session.Poll(); }
            Check(!session.FollowObserverCamera && Vector3.Distance(observer.pivot, fixedPivot) < 0.001f,
                "Disabling observer follow immediately stops translating the Scene view");
            session.SetObserverCameraFollow(true);
            session.Poll();
            Check(Vector3.Distance(observer.pivot, fixedPivot) < 0.001f,
                "Re-enabling observer follow does not replay movement accumulated while disabled");
            ownedTranslation += FollowStep(observer, new MotionInputFrame(Vector2.up, 0f), 0.1f, "Following after a live toggle");
            var manualPan = new Vector3(2f, -1f, 3f);
            var manualRotation = Quaternion.Euler(31f, 209f, 0f);
            observer.LookAt(observer.pivot + manualPan, manualRotation, 8f, false, true);
            CheckMirroredCamera(observer, "Manual Scene navigation");
            ownedTranslation += FollowStep(observer, new MotionInputFrame(Vector2.up, 0f), 0.1f, "Following after manual Scene navigation");

            secondObserver = ScriptableObject.CreateInstance<SceneView>();
            secondObserver.titleContent = new GUIContent("Pro second observer validation");
            secondObserver.Show();
            secondObserver.Focus();
            secondObserver.LookAt(avatar.transform.position + new Vector3(3f, 2f, -1f), Quaternion.Euler(18f, 70f, 0f), 4f, true, true);
            secondObserver.Repaint();
            secondObserver.SendEvent(new Event { type = EventType.Repaint });
            Check(SceneView.lastActiveSceneView == secondObserver && secondObserver.camera,
                "A second real SceneView can become the current observer during one session");
            var secondPivot = secondObserver.pivot;
            var firstInactivePivot = observer.pivot;
            session.Poll();
            Check(Vector3.Distance(secondObserver.pivot, secondPivot) < 0.001f,
                "Switching observers does not apply translations from the previously active Scene view");
            var secondOwnedTranslation = FollowStep(secondObserver, new MotionInputFrame(Vector2.down, 0f), 0.1f, "Following in the second observer");
            Check(Vector3.Distance(observer.pivot, firstInactivePivot) < 0.001f,
                "Avatar movement updates only the current observer rather than moving every open Scene view");
            var secondManualPan = new Vector3(-1f, 0.5f, 2f);
            var secondManualRotation = Quaternion.Euler(37f, 115f, 0f);
            secondObserver.LookAt(secondObserver.pivot + secondManualPan, secondManualRotation, 6f, false, true);
            CheckMirroredCamera(secondObserver, "Second observer after independent manual navigation");
            secondOwnedTranslation += FollowStep(secondObserver, default, 0.1f, "Following after switching observers");
            Check(Vector3.Distance(observer.pivot - ownedTranslation, originalPivot + manualPan) < 0.001f &&
                Vector3.Distance(secondObserver.pivot - secondOwnedTranslation, secondPivot + secondManualPan) < 0.001f,
                "The fixture distinguishes owned follow translations from each observer's manual navigation");
            session.Stop();
            Check(Vector3.Distance(observer.pivot, originalPivot + manualPan) < 0.001f &&
                Quaternion.Angle(observer.rotation, manualRotation) < 0.001f && Mathf.Abs(observer.size - 8f) < 0.0001f && !observer.orthographic,
                "Stop removes owned following from the first observer while preserving manual pan, rotation, and zoom");
            Check(Vector3.Distance(secondObserver.pivot, secondPivot + secondManualPan) < 0.001f &&
                Quaternion.Angle(secondObserver.rotation, secondManualRotation) < 0.001f && Mathf.Abs(secondObserver.size - 6f) < 0.0001f && !secondObserver.orthographic,
                "Stop independently restores the second observer's owned translation without undoing manual navigation");
            Check(!ownedCameraObject && module.SimulationCamera == originalCamera &&
                Vector3.Distance(avatar.transform.position, originalPosition) < 0.0001f &&
                originalCamera.transform.position == originalCameraPosition && originalCamera.transform.rotation == originalCameraRotation &&
                MatricesClose(originalCamera.projectionMatrix, originalProjection),
                "Observer cleanup restores the avatar and module camera without modifying the original camera object");
            session.Stop();
            Check(Vector3.Distance(observer.pivot, originalPivot + manualPan) < 0.001f,
                "Repeated observer cleanup cannot subtract the same owned translation twice");

            using (var keyboard = new KeyboardMotionInputSource(true))
            {
                var cursorLock = Cursor.lockState;
                var cursorVisible = Cursor.visible;
                module.SimulationCamera = null;
                Check(session.Start(manager, keyboard, collisions: false, followObserverCamera: true),
                    "Keyboard simulation starts in the default observer mode with the optional follow setting");
                Check(session.FollowObserverCamera && keyboard.ObserverView && !keyboard.Captured &&
                    Cursor.lockState == cursorLock && Cursor.visible == cursorVisible,
                    "Default Game observer input does not capture or move the cursor");
                session.SetViewMode(SimulationViewMode.FirstPerson);
                Check(!keyboard.ObserverView, "Selecting first person returns keyboard input to its capture-based mode");
                session.SetViewMode(SimulationViewMode.SceneCamera);
                Check(keyboard.ObserverView && !keyboard.Captured && keyboard.Suspended,
                    "Returning to observer mode resets the previous camera input state");
                session.SetViewMode(SimulationViewMode.ThirdPerson);
                var toggle = (Action)typeof(KeyboardMotionInputSource)
                    .GetField("ViewToggleRequested", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                toggle();
                Check(session.ViewMode == SimulationViewMode.FirstPerson && !session.ThirdPersonEnabled && !keyboard.ObserverView,
                    "F5 disables third-person mode and returns to the first-person player view");
                toggle();
                Check(session.ViewMode == SimulationViewMode.ThirdPerson && session.ThirdPersonEnabled && !keyboard.ObserverView,
                    "F5 enables third person again without cycling through the Scene observer");
                session.SetViewMode(SimulationViewMode.SceneCamera);
                Check(!session.ThirdPersonEnabled && keyboard.ObserverView,
                    "The Scene observer remains a separate explicit view selection");
                toggle();
                Check(session.ViewMode == SimulationViewMode.ThirdPerson && session.ThirdPersonEnabled && !keyboard.ObserverView,
                    "F5 from the Scene observer enables the third-person player camera");
                session.Stop();
                Check(module.SimulationCamera == null,
                    "Observer cleanup restores an originally empty module camera reference to null");
            }
        }
        finally
        {
            session?.Dispose();
            if (manager) { manager.Module = null; UnityEngine.Object.DestroyImmediate(manager.gameObject); }
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            if (originalCameraObject) UnityEngine.Object.DestroyImmediate(originalCameraObject);
            if (secondObserver) secondObserver.Close();
            if (observer) observer.Close();
            if (previousObserver) previousObserver.Focus();
            EditorApplication.isPaused = paused;
        }
    }

    private static void CheckSceneCameraObserverSwitch(SceneView observer, GameObject avatar, GestureManager manager,
        ModuleVrc3 module, Camera originalCamera)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var toolType = typeof(AvatarTools.UpdateSceneCamera);
        var targetField = toolType.GetField("_camera", BindingFlags.Static | BindingFlags.NonPublic);
        var update = toolType.GetMethod("Update", instance);
        var previousTarget = targetField.GetValue(null);
        var stateField = toolType.GetField("_originalState", BindingFlags.Static | BindingFlags.NonPublic);
        var instancesField = toolType.GetField("_instances", BindingFlags.Static | BindingFlags.NonPublic);
        var previousState = stateField.GetValue(null);
        var previousInstances = instancesField.GetValue(null);
        // Avoid rebinding the shared target or persisting the Scene Camera preference.
        // The lifecycle fixture below separately exercises the public constructor.
        var sceneCameraTool = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(toolType);
        var originalPosition = originalCamera.transform.position;
        var originalRotation = originalCamera.transform.rotation;
        var originalOrthographic = originalCamera.orthographic;
        var originalSize = originalCamera.orthographicSize;
        var originalFieldOfView = originalCamera.fieldOfView;
        var originalNear = originalCamera.nearClipPlane;
        var originalFar = originalCamera.farClipPlane;
        var originalAspect = originalCamera.aspect;
        var originalPivot = observer.pivot;
        var originalViewRotation = observer.rotation;
        var originalViewSize = observer.size;
        var originalViewOrthographic = observer.orthographic;
        var avatarPosition = avatar.transform.position;
        var worldPoints = new[] { avatarPosition, avatarPosition + new Vector3(0.37f, 1.27f, 0.18f) };
        try
        {
            stateField.SetValue(null, null);
            instancesField.SetValue(null, 0);
            targetField.SetValue(null, originalCamera);
            // Both Game cameras use their own full Game viewport, rather than
            // adopting the differently shaped Scene window's aspect ratio.
            originalCamera.ResetAspect();
            var gameAspect = originalCamera.aspect;
            for (var round = 0; round < 4; round++)
            {
                var orthographic = round % 2 != 0;
                var label = (orthographic ? "Orthographic" : "Perspective") + " observer switch " + (round + 1);
                observer.LookAt(avatarPosition + new Vector3(round * 0.15f, 0.85f + round * 0.07f, -round * 0.12f),
                    Quaternion.Euler(15f + round * 3f, 145f + round * 8f, 0f), 4.75f + round * 0.35f, orthographic, true);
                observer.Repaint();
                observer.SendEvent(new Event { type = EventType.Repaint });
                var source = observer.camera;
                var sourcePosition = source.transform.position;
                var sourceRotation = source.transform.rotation;
                var sourceProjection = source.projectionMatrix;
                var pivot = observer.pivot;
                var rotation = observer.rotation;
                var size = observer.size;

                void CheckSyncedCamera(Camera camera, string stage)
                {
                    var drift = Vector3.Distance(camera.transform.position, sourcePosition);
                    Check(drift < 0.0001f && Quaternion.Angle(camera.transform.rotation, sourceRotation) < 0.001f &&
                        camera.orthographic == source.orthographic &&
                        Mathf.Abs(camera.orthographicSize - source.orthographicSize) < 0.0001f &&
                        Mathf.Abs(camera.fieldOfView - source.fieldOfView) < 0.0001f &&
                        Mathf.Abs(camera.nearClipPlane - source.nearClipPlane) < 0.0001f &&
                        Mathf.Abs(camera.farClipPlane - source.farClipPlane) < 0.01f &&
                        Mathf.Abs(camera.aspect - gameAspect) < 0.0001f,
                        label + " " + stage + " exactly mirrors the Scene transform and projection settings while retaining Game aspect; position drift=" + drift);
                }

                update.Invoke(sceneCameraTool, new object[] { module });
                CheckSyncedCamera(originalCamera, "before simulation");
                var before = worldPoints.Select(point => originalCamera.WorldToViewportPoint(point)).ToArray();
                Check(before.All(point => point.z > 0f), label + " samples the avatar root and a second visible world point");
                using (var transition = new ControlledModeSession())
                {
                    Check(transition.Start(manager, new SessionMotionInput(), gravity: true, previewCamera: true),
                        label + " starts the real observer session: " + transition.Error);
                    var ownedObject = transition.ViewCamera.gameObject;
                    var game = transition.ViewCamera.PreviewCamera;
                    CheckSyncedCamera(game, "on entry");
                    Check(worldPoints.Select((point, index) => Vector3.Distance(game.WorldToViewportPoint(point), before[index]))
                            .All(drift => drift < 0.00001f),
                        label + " keeps both avatar viewport positions unchanged on the same-frame camera handoff");
                    update.Invoke(sceneCameraTool, new object[] { module });
                    transition.Poll();
                    CheckSyncedCamera(game, "after normal updates");
                    Check(Vector3.Distance(avatar.transform.position, avatarPosition) < 0.0001f &&
                        Vector3.Distance(observer.pivot, pivot) < 0.0001f &&
                        Quaternion.Angle(observer.rotation, rotation) < 0.001f && Mathf.Abs(observer.size - size) < 0.0001f &&
                        observer.orthographic == orthographic && source.transform.position == sourcePosition &&
                        source.transform.rotation == sourceRotation && MatricesClose(source.projectionMatrix, sourceProjection),
                        label + " leaves the avatar and fixed Scene camera position, rotation, zoom, and projection unchanged");
                    transition.Stop();
                    update.Invoke(sceneCameraTool, new object[] { module });
                    Check(!ownedObject && module.SimulationCamera == originalCamera,
                        label + " restores the original Game camera and destroys the temporary observer");
                    CheckSyncedCamera(originalCamera, "after stop");
                    Check(worldPoints.Select((point, index) => Vector3.Distance(originalCamera.WorldToViewportPoint(point), before[index]))
                            .All(drift => drift < 0.00001f) && Vector3.Distance(avatar.transform.position, avatarPosition) < 0.0001f,
                        label + " restores both viewport positions and the avatar without a stop-time jump");
                }
            }
        }
        finally
        {
            targetField.SetValue(null, previousTarget);
            stateField.SetValue(null, previousState);
            instancesField.SetValue(null, previousInstances);
            originalCamera.transform.SetPositionAndRotation(originalPosition, originalRotation);
            originalCamera.orthographic = originalOrthographic;
            originalCamera.orthographicSize = originalSize;
            originalCamera.fieldOfView = originalFieldOfView;
            originalCamera.nearClipPlane = originalNear;
            originalCamera.farClipPlane = originalFar;
            originalCamera.aspect = originalAspect;
            observer.LookAt(originalPivot, originalViewRotation, originalViewSize, originalViewOrthographic, true);
            observer.Repaint();
            observer.SendEvent(new Event { type = EventType.Repaint });
        }
    }

    private static void CheckSceneCameraLifecycle(SceneView observer, ModuleVrc3 module)
    {
        const string preference = "GM3 SceneCamera";
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags shared = BindingFlags.Static | BindingFlags.NonPublic;
        var type = typeof(AvatarTools.UpdateSceneCamera);
        var targetField = type.GetField("_camera", shared);
        var stateField = type.GetField("_originalState", shared);
        var instancesField = type.GetField("_instances", shared);
        var set = type.GetMethod("Set", shared);
        var restore = type.GetMethod("RestoreCamera", shared);
        var toggle = type.GetMethod("Toggle", instance);
        var update = type.GetMethod("Update", instance);
        var previousTarget = targetField.GetValue(null);
        var previousState = stateField.GetValue(null);
        var previousInstances = instancesField.GetValue(null);
        var hadPreference = EditorPrefs.HasKey(preference);
        var previousPreference = EditorPrefs.GetBool(preference);
        var previousPivot = observer.pivot;
        var previousRotation = observer.rotation;
        var previousSize = observer.size;
        var previousOrthographic = observer.orthographic;
        var existingCameras = Camera.allCameras.Where(camera => camera.targetDisplay == 0 &&
            (camera.gameObject.hideFlags & HideFlags.DontSave) == 0).Select(camera => (camera, camera.enabled)).ToArray();
        var tools = new List<AvatarTools.UpdateSceneCamera>();
        GameObject parent = null;
        GameObject secondObject = null;
        GameObject temporaryObject = null;
        try
        {
            targetField.SetValue(null, null);
            stateField.SetValue(null, null);
            instancesField.SetValue(null, 0);
            EditorPrefs.DeleteKey(preference);
            foreach (var entry in existingCameras) entry.camera.enabled = false;
            parent = new GameObject("Pro Scene Camera lifecycle parent");
            parent.transform.SetPositionAndRotation(new Vector3(13f, 9f, -7f), Quaternion.Euler(12f, 31f, 5f));
            var firstObject = new GameObject("Pro Scene Camera lifecycle first");
            firstObject.transform.SetParent(parent.transform, false);
            firstObject.transform.localPosition = new Vector3(2f, 3f, -5f);
            firstObject.transform.localRotation = Quaternion.Euler(7f, 13f, 4f);
            var first = firstObject.AddComponent<Camera>();
            first.orthographic = false;
            first.orthographicSize = 3.7f;
            first.fieldOfView = 47f;
            first.nearClipPlane = 0.17f;
            first.farClipPlane = 437f;
            first.aspect = 1.23f;
            secondObject = new GameObject("Pro Scene Camera lifecycle second");
            secondObject.transform.SetPositionAndRotation(new Vector3(-8f, 12f, 19f), Quaternion.Euler(17f, 213f, 3f));
            var second = secondObject.AddComponent<Camera>();
            second.enabled = false;
            second.orthographic = true;
            second.orthographicSize = 7.4f;
            second.fieldOfView = 61f;
            second.nearClipPlane = 0.3f;
            second.farClipPlane = 923f;
            second.aspect = 1.67f;
            var firstState = new SceneCameraState(first);
            var secondState = new SceneCameraState(second);

            AvatarTools.UpdateSceneCamera CreateTool()
            {
                var tool = new AvatarTools.UpdateSceneCamera();
                tools.Add(tool);
                return tool;
            }

            void Set(Camera camera) => set.Invoke(null, new object[] { camera });
            void Update(AvatarTools.UpdateSceneCamera tool) => update.Invoke(tool, new object[] { module });
            Camera Target() => (Camera)targetField.GetValue(null);
            bool Released() => !Target() && stateField.GetValue(null) == null;
            void Navigate(int step)
            {
                // Keep this parented-camera fixture near the origin: the session
                // fixture's distant avatar would add float error to world/local conversion.
                observer.LookAt(new Vector3(step * 0.2f, step * 0.1f, -step * 0.15f),
                    Quaternion.Euler(19f + step * 4f, 133f + step * 9f, 0f), 4.2f + step * 0.4f, step % 2 == 0, true);
                observer.Repaint();
                observer.SendEvent(new Event { type = EventType.Repaint });
            }

            var owner = CreateTool();
            Check(Released() && !EditorPrefs.HasKey(preference) && (int)instancesField.GetValue(null) == 1,
                "Constructing an inactive Scene Camera tool leaves an absent preference and target untouched");
            Set(first);
            var originalSnapshot = stateField.GetValue(null);
            Navigate(1);
            Update(owner);
            Check(Target() == first && EditorPrefs.GetBool(preference) && !firstState.Matches(first) &&
                Vector3.Distance(first.transform.position, observer.camera.transform.position) < 0.0001f,
                "The real Scene Camera Set and Update capture the original camera and synchronize the selected target; target=" +
                (Target() ? Target().name : "null") + ", preference=" + EditorPrefs.GetBool(preference) +
                ", unchanged=" + firstState.Matches(first) + ", targetPosition=" + first.transform.position +
                ", observerPosition=" + observer.camera.transform.position + ", activeView=" + SceneView.lastActiveSceneView);
            Set(first);
            Check(ReferenceEquals(stateField.GetValue(null), originalSnapshot),
                "Selecting the same Scene Camera target preserves its original snapshot");
            var secondOwner = CreateTool();
            Check(Target() == first && ReferenceEquals(stateField.GetValue(null), originalSnapshot) &&
                (int)instancesField.GetValue(null) == 2,
                "A second real Scene Camera constructor shares ownership without capturing the synchronized pose again");
            Navigate(2);
            Update(secondOwner);
            owner.Dispose();
            Check(Target() == first && !firstState.Matches(first) && (int)instancesField.GetValue(null) == 1,
                "Disposing one Scene Camera owner leaves the remaining owner's target active");
            secondOwner.Dispose();
            Check(Released() && firstState.Matches(first) && EditorPrefs.GetBool(preference) &&
                (int)instancesField.GetValue(null) == 0,
                "Disposing the last Scene Camera owner restores local pose and projection while retaining the enabled preference");
            owner.Dispose();
            secondOwner.Dispose();
            Check((int)instancesField.GetValue(null) == 0 && firstState.Matches(first),
                "Repeated Scene Camera disposal cannot decrement ownership or restore twice");

            var remainingOwner = CreateTool();
            Check(Target() == first && stateField.GetValue(null) != null && firstState.Matches(first),
                "A later Scene Camera constructor recaptures the restored Main Camera when the saved preference is enabled");
            Set(null);
            for (var round = 0; round < 3; round++)
            {
                toggle.Invoke(remainingOwner, new object[] { null });
                Check(Target() == first && EditorPrefs.GetBool(preference),
                    "Scene Camera Toggle without a module selects the real eligible Main Camera");
                Navigate(round + 3);
                Update(remainingOwner);
                Check(!firstState.Matches(first), "An enabled Scene Camera follows subsequent manual Scene navigation");
                toggle.Invoke(remainingOwner, new object[] { module });
                Check(Released() && firstState.Matches(first) && !EditorPrefs.GetBool(preference),
                    "Scene Camera Toggle off restores the original local pose and every synchronized projection setting");
                Navigate(round + 6);
                Update(remainingOwner);
                Check(firstState.Matches(first) && Released(),
                    "Scene navigation and updates after Toggle off leave the restored Game camera unchanged");
            }

            Set(first);
            Update(remainingOwner);
            Set(second);
            Check(firstState.Matches(first) && Target() == second && secondState.Matches(second),
                "Switching Scene Camera targets restores the old camera before capturing the second camera's own state");
            Update(remainingOwner);
            Check(!secondState.Matches(second), "The selected second camera receives the actual Scene transform and projection");
            Set(first);
            Check(secondState.Matches(second) && Target() == first,
                "Switching back restores the second camera independently of the first camera's snapshot");
            Update(remainingOwner);
            parent.transform.position += new Vector3(3f, -2f, 1f);
            parent.transform.rotation = Quaternion.Euler(23f, 77f, 9f);
            var movedParentPosition = parent.transform.position;
            var movedParentRotation = parent.transform.rotation;
            Set(null);
            Check(firstState.Matches(first) && parent.transform.position == movedParentPosition &&
                parent.transform.rotation == movedParentRotation && secondState.Matches(second),
                "Disabling Scene Camera restores its parent-relative pose without undoing independent parent movement");

            temporaryObject = new GameObject("Pro rejected temporary Scene Camera") { hideFlags = HideFlags.HideAndDontSave };
            var temporary = temporaryObject.AddComponent<Camera>();
            var temporaryState = new SceneCameraState(temporary);
            Set(first);
            Update(remainingOwner);
            Set(temporary);
            Update(remainingOwner);
            Check(Released() && !EditorPrefs.GetBool(preference) && firstState.Matches(first) && temporaryState.Matches(temporary),
                "Selecting a DontSave camera behaves as disabling Scene Camera and never mutates the temporary target");

            Set(first);
            Update(remainingOwner);
            restore.Invoke(null, null);
            Check(Released() && firstState.Matches(first) && EditorPrefs.GetBool(preference),
                "The shared assembly/play lifecycle cleanup restores the camera and releases its snapshot while preserving preference");
            restore.Invoke(null, null);
            Check(Released() && firstState.Matches(first) && EditorPrefs.GetBool(preference),
                "Repeated lifecycle cleanup is harmless and retains the saved enabled preference");
            first.enabled = false;
            Update(remainingOwner);
            Check(Released() && EditorPrefs.GetBool(preference),
                "An update with no eligible Main Camera preserves the preference for later recovery");
            var waitingOwner = CreateTool();
            Check(Released() && EditorPrefs.GetBool(preference),
                "Constructing another owner while no Main Camera exists also preserves the enabled preference");
            first.enabled = true;
            Update(waitingOwner);
            Check(Target() == first && !firstState.Matches(first),
                "A later update automatically captures and synchronizes the Main Camera after it becomes available");
            remainingOwner.Dispose();
            Check(Target() == first, "The recovered Scene Camera remains owned until the last live tool is disposed");
            waitingOwner.Dispose();
            Check(Released() && firstState.Matches(first) && EditorPrefs.GetBool(preference),
                "Final disposal after automatic recovery restores the original state and preserves the enabled preference");
            EditorPrefs.SetBool(preference, false);
            restore.Invoke(null, null);
            Check(Released() && !EditorPrefs.GetBool(preference), "Lifecycle cleanup also preserves a disabled Scene Camera preference");
        }
        finally
        {
            foreach (var tool in tools) tool.Dispose();
            restore.Invoke(null, null);
            targetField.SetValue(null, previousTarget);
            stateField.SetValue(null, previousState);
            instancesField.SetValue(null, previousInstances);
            if (hadPreference) EditorPrefs.SetBool(preference, previousPreference);
            else EditorPrefs.DeleteKey(preference);
            if (temporaryObject) UnityEngine.Object.DestroyImmediate(temporaryObject);
            if (secondObject) UnityEngine.Object.DestroyImmediate(secondObject);
            if (parent) UnityEngine.Object.DestroyImmediate(parent);
            foreach (var entry in existingCameras)
                if (entry.camera) entry.camera.enabled = entry.enabled;
            observer.LookAt(previousPivot, previousRotation, previousSize, previousOrthographic, true);
            observer.Repaint();
            observer.SendEvent(new Event { type = EventType.Repaint });
        }
    }

    private static void CheckGameSimulationSceneCamera(SceneView observer)
    {
        const string preference = "GM3 SceneCamera";
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags shared = BindingFlags.Static | BindingFlags.NonPublic;
        var sceneType = typeof(AvatarTools.UpdateSceneCamera);
        var targetField = sceneType.GetField("_camera", shared);
        var stateField = sceneType.GetField("_originalState", shared);
        var instancesField = sceneType.GetField("_instances", shared);
        var set = sceneType.GetMethod("Set", shared);
        var restore = sceneType.GetMethod("RestoreCamera", shared);
        var sceneToggle = sceneType.GetMethod("Toggle", instance);
        var sceneUpdate = sceneType.GetMethod("Update", instance);
        var active = sceneType.GetProperty("Active", instance);
        var previousTarget = targetField.GetValue(null);
        var previousState = stateField.GetValue(null);
        var previousInstances = instancesField.GetValue(null);
        var previousBridge = AvatarTools.UpdateSceneCamera.SimulationView;
        var hadPreference = EditorPrefs.HasKey(preference);
        var previousPreference = EditorPrefs.GetBool(preference);
        var previousPivot = observer.pivot;
        var previousRotation = observer.rotation;
        var previousSize = observer.size;
        var previousOrthographic = observer.orthographic;
        var paused = EditorApplication.isPaused;
        GameObject avatar = null;
        GameObject originalCameraObject = null;
        GestureManager manager = null;
        GameSimulationTool gameTool = null;
        GameSimulationTool duplicateTool = null;
        AvatarTools.UpdateSceneCamera sceneTool = null;
        GmgDynamicFunction[] registeredTools = null;
        try
        {
            EditorApplication.isPaused = false;
            AvatarTools.UpdateSceneCamera.SimulationView = null;
            targetField.SetValue(null, null);
            stateField.SetValue(null, null);
            instancesField.SetValue(null, 0);
            EditorPrefs.SetBool(preference, false);
            avatar = new GameObject("Pro game Scene Camera bridge avatar");
            avatar.transform.SetPositionAndRotation(new Vector3(35f, 20f, -45f), Quaternion.Euler(0f, 23f, 0f));
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            var module = new ModuleVrc3(descriptor) { Active = true, Settings = new ModuleSettings() };
            foreach (var name in ParameterNames)
                module.Params.Add(name, new Vrc3Param(name, AnimatorControllerParameterType.Float));
            manager = new GameObject("Pro game Scene Camera bridge manager").AddComponent<GestureManager>();
            manager.Module = module;
            originalCameraObject = new GameObject("Pro game Scene Camera bridge original Main Camera");
            var originalCamera = originalCameraObject.AddComponent<Camera>();
            originalCamera.transform.SetPositionAndRotation(new Vector3(29f, 24f, -52f), Quaternion.Euler(11f, 33f, 0f));
            originalCamera.fieldOfView = 51f;
            originalCamera.farClipPlane = 413f;
            originalCamera.nearClipPlane = 0.19f;
            originalCamera.orthographicSize = 6.7f;
            originalCamera.aspect = 1.31f;
            module.SimulationCamera = originalCamera;
            var initialMainState = new SceneCameraState(originalCamera);
            var moduleTools = (AvatarTools)typeof(ModuleVrc3).GetField("AvatarTools", instance).GetValue(module);
            registeredTools = ((IEnumerable<GmgDynamicFunction>)typeof(AvatarTools).GetMethod("GetRegisteredTools", instance)
                .Invoke(moduleTools, new object[] { module })).ToArray();
            Check(registeredTools.OfType<GameSimulationTool>().Count() == 1,
                "The module registers exactly one real Game Simulation tool for the bridge fixture");
            gameTool = registeredTools.OfType<GameSimulationTool>().Single();
            sceneTool = moduleTools.SceneCamera;
            Check(ReferenceEquals(sceneTool, moduleTools.SceneCamera), "The bridge fixture uses the module's ordinary Scene Camera tool instance");
            ((MicrophoneLipSync)typeof(GameSimulationTool).GetField("_lipSync", instance).GetValue(gameTool)).Enabled = false;
            var session = (ControlledModeSession)typeof(GameSimulationTool).GetField("_session", instance).GetValue(gameTool);
            var gameToggle = typeof(GameSimulationTool).GetMethod("Toggle", instance);
            var keyboardField = typeof(GameSimulationTool).GetField("_keyboard", instance);
            void ClickGame() => gameToggle.Invoke(gameTool, new object[] { module });
            void ClickScene() => sceneToggle.Invoke(sceneTool, new object[] { module });
            bool SceneActive() => (bool)active.GetValue(sceneTool);
            KeyboardMotionInputSource Keyboard() => (KeyboardMotionInputSource)keyboardField.GetValue(gameTool);
            Action F5() => (Action)typeof(KeyboardMotionInputSource).GetField("ViewToggleRequested", instance).GetValue(Keyboard());
            Action<float> Zoom() => (Action<float>)typeof(KeyboardMotionInputSource).GetField("CameraZoomRequested", instance).GetValue(Keyboard());
            void Navigate(bool orthographic, int step)
            {
                observer.Focus();
                observer.LookAt(avatar.transform.position + new Vector3(step * 0.1f, 0.9f, 0f),
                    Quaternion.Euler(17f + step * 2f, 151f + step * 7f, 0f), 4.3f + step * 0.2f, orthographic, true);
                observer.Repaint();
                observer.SendEvent(new Event { type = EventType.Repaint });
            }

            void CheckPlayerLens(Camera camera, float fieldOfView, float far, float size, string label)
            {
                Check(!camera.orthographic && Mathf.Abs(camera.fieldOfView - fieldOfView) < 0.0001f &&
                    Mathf.Abs(camera.farClipPlane - far) < 0.001f && Mathf.Abs(camera.orthographicSize - size) < 0.0001f &&
                    Mathf.Abs(camera.nearClipPlane - Mathf.Clamp(session.ViewCamera.Height * 0.01f, 0.005f, 0.1f)) < 0.0001f,
                    label + " restores the player's perspective lens and height-based near plane after the Scene projection");
            }

            void CheckCentered(string label)
            {
                var preview = session.ViewCamera;
                preview.ApplyView();
                var camera = preview.PreviewCamera;
                var bodyPivot = BodyAxisViewpoint(avatar.transform, preview.ViewPosition);
                var pivotViewport = camera.WorldToViewportPoint(bodyPivot);
                var rootViewport = camera.WorldToViewportPoint(avatar.transform.position);
                Check(pivotViewport.z > 0f && Mathf.Abs(pivotViewport.x - 0.5f) < 0.0001f &&
                    Mathf.Abs(pivotViewport.y - 0.5f) < 0.0001f && Mathf.Abs(rootViewport.x - 0.5f) < 0.0001f,
                    label + " centers the descriptor-height body-axis pivot without a third-person shoulder offset");
            }

            void CheckReleased(string label)
            {
                Check(!session.Running && AvatarTools.UpdateSceneCamera.SimulationView == null &&
                    !manager.IsPlayerControlled && module.SimulationCamera == originalCamera &&
                    !Resources.FindObjectsOfTypeAll<MotionPreviewCamera>().Any(camera => camera.Target == avatar.transform),
                    label + " clears the bridge, ownership, and temporary camera while restoring the module camera");
            }

            // Run with both ordinary preferences: Game Simulation must own the
            // button's displayed state without persisting its view selection.
            for (var preferenceRound = 0; preferenceRound < 2; preferenceRound++)
            {
                var ordinaryEnabled = preferenceRound != 0;
                Navigate(true, preferenceRound);
                set.Invoke(null, new object[] { ordinaryEnabled ? originalCamera : null });
                if (ordinaryEnabled) sceneUpdate.Invoke(sceneTool, new object[] { module });
                var ordinaryMainState = new SceneCameraState(originalCamera);
                var ordinarySnapshot = stateField.GetValue(null);
                ClickGame();
                Check(session.Running && ReferenceEquals(AvatarTools.UpdateSceneCamera.SimulationView, session) &&
                    session.ViewMode == SimulationViewMode.SceneCamera && SceneActive() && Keyboard().ObserverView,
                    "Registered Game Simulation owns the ordinary Scene Camera button in its default observer view: " + session.Error);
                var ownedObject = session.ViewCamera.gameObject;
                var camera = session.ViewCamera.PreviewCamera;
                Navigate(false, preferenceRound + 2);
                session.Poll();
                sceneUpdate.Invoke(sceneTool, new object[] { module });
                Check(ordinaryMainState.Matches(originalCamera) && ReferenceEquals(stateField.GetValue(null), ordinarySnapshot) &&
                    EditorPrefs.GetBool(preference) == ordinaryEnabled,
                    "While simulation owns the view, ordinary Scene Camera updates preserve Main Camera, its snapshot, and its preference");
                ClickScene();
                Check(session.ViewMode == SimulationViewMode.FirstPerson && !session.ThirdPersonEnabled && !SceneActive() &&
                    !Keyboard().ObserverView && (bool)typeof(KeyboardMotionInputSource).GetField("_captureRequested", instance).GetValue(Keyboard()),
                    "The real ordinary Scene Camera button leaves the initial observer for first person and requests fresh Game capture");
                Check(!camera.orthographic && Mathf.Abs(camera.farClipPlane - 1000f) < 0.001f,
                    "Leaving the initial Scene observer restores the player's perspective and original far clip instead of the Scene lens");
                camera.fieldOfView = 53f;
                camera.farClipPlane = 731f;
                camera.orthographicSize = 7.3f;
                var playerAspect = camera.aspect;
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    Navigate(repeat % 2 == 0, repeat + 4);
                    ClickScene();
                    session.Poll();
                    Check(SceneActive() && session.ViewMode == SimulationViewMode.SceneCamera &&
                        camera.orthographic == observer.camera.orthographic &&
                        Mathf.Abs(camera.fieldOfView - observer.camera.fieldOfView) < 0.0001f,
                        "The ordinary button re-enters the actual perspective or orthographic Scene observer");
                    ClickScene();
                    Check(!SceneActive() && session.ViewMode == SimulationViewMode.FirstPerson && !session.ThirdPersonEnabled,
                        "Repeated ordinary Scene Camera on/off restores the previous first-person mode");
                    CheckPlayerLens(camera, 53f, 731f, 7.3f, "Repeated first-person return");
                    Check(Mathf.Abs(camera.aspect - playerAspect) < 0.0001f,
                        "Scene/first-person transitions keep the Game camera's own aspect ratio");
                }

                F5()();
                Check(!SceneActive() && session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.ThirdPerson,
                    "F5 and the ordinary Scene Camera Active state reflect the same third-person player view");
                session.SetCameraDistance(2.5f);
                CheckCentered("The initial third-person player view");
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    Navigate(repeat == 0, repeat + 8);
                    ClickScene();
                    Check(SceneActive() && !session.ThirdPersonEnabled, "Entering the Scene observer remembers the enabled third-person feature");
                    ClickScene();
                    Check(!SceneActive() && session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.ThirdPerson &&
                        session.CameraDistance == 2.5f,
                        "Disabling the Scene observer returns to the previous third-person mode and distance");
                    CheckPlayerLens(camera, 53f, 731f, 7.3f, "Third-person return");
                    session.ViewCamera.Pitch = repeat == 0 ? 17f : -12f;
                    CheckCentered("Third person after an orthographic/perspective observer");
                }

                Zoom()(100f);
                Check(session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.FirstPerson && !SceneActive(),
                    "Wheel zoom to first person keeps the third-person feature enabled and the ordinary observer button inactive");
                ClickScene();
                ClickScene();
                Check(session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.FirstPerson && session.CameraDistance == 0f && !SceneActive(),
                    "A Scene Camera round trip preserves enabled third person at its zero-distance first-person limit");
                Zoom()(-1f);
                Check(session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.ThirdPerson && session.CameraDistance == 0.25f,
                    "Wheel zoom outward still works after returning from the Scene observer at zero distance");
                CheckCentered("Third person after the nearest-distance observer round trip");
                session.SetViewMode(SimulationViewMode.SceneCamera);
                Check(SceneActive(), "Selecting Scene Camera through the same session API as the dropdown activates the ordinary button");
                F5()();
                Check(!SceneActive() && session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.ThirdPerson,
                    "F5 leaves a dropdown-selected observer and updates the ordinary button immediately");
                F5()();
                Check(!SceneActive() && !session.ThirdPersonEnabled && session.ViewMode == SimulationViewMode.FirstPerson,
                    "F5 disabling third person keeps the ordinary button aligned with first person");
                session.SetViewMode(SimulationViewMode.ThirdPerson);
                Check(!SceneActive(), "The dropdown's explicit third-person selection leaves the ordinary observer inactive");
                session.SetViewMode(SimulationViewMode.SceneCamera);
                ClickScene();
                Check(!SceneActive() && session.ViewMode == SimulationViewMode.ThirdPerson,
                    "The ordinary button restores third person after a dropdown-selected observer");

                duplicateTool = new GameSimulationTool(manager);
                ((MicrophoneLipSync)typeof(GameSimulationTool).GetField("_lipSync", instance).GetValue(duplicateTool)).Enabled = false;
                gameToggle.Invoke(duplicateTool, new object[] { module });
                var duplicateSession = (ControlledModeSession)typeof(GameSimulationTool).GetField("_session", instance).GetValue(duplicateTool);
                Check(!duplicateSession.Running && session.Running && ReferenceEquals(AvatarTools.UpdateSceneCamera.SimulationView, session),
                    "A duplicate Game Simulation owner is rejected without replacing the active view bridge");
                duplicateTool.Dispose();
                Check(ReferenceEquals(AvatarTools.UpdateSceneCamera.SimulationView, session),
                    "Disposing a rejected duplicate cannot clear the successful session's bridge");
                duplicateTool = null;
                Check(ordinaryMainState.Matches(originalCamera) && EditorPrefs.GetBool(preference) == ordinaryEnabled,
                    "All ordinary-button, F5, wheel, and dropdown changes preserve the original Main Camera and ordinary preference");
                ClickGame();
                CheckReleased("Exiting through the registered Game Simulation tool");
                Check(!ownedObject && SceneActive() == ordinaryEnabled && ordinaryMainState.Matches(originalCamera) &&
                    EditorPrefs.GetBool(preference) == ordinaryEnabled,
                    "Exit restores ordinary Scene Camera Active to its unchanged saved preference");
                restore.Invoke(null, null);
                Check(initialMainState.Matches(originalCamera), "Ordinary Scene Camera cleanup still restores its own pre-simulation camera snapshot");
            }

            EditorPrefs.SetBool(preference, false);
            using (var adapter = new ControlledModeSession())
            using (var keyboard = new KeyboardMotionInputSource(false))
            {
                Check(adapter.Start(manager, keyboard, previewCamera: true), "A non-game keyboard adapter starts independently of the Game Simulation bridge");
                Check(AvatarTools.UpdateSceneCamera.SimulationView == null && !SceneActive(),
                    "An ordinary controlled-input adapter cannot claim the Game Simulation Scene Camera bridge");
            }

            var foreignView = new SimulationViewStub();
            AvatarTools.UpdateSceneCamera.SimulationView = foreignView;
            ClickGame();
            Check(!session.Running && ReferenceEquals(AvatarTools.UpdateSceneCamera.SimulationView, foreignView) &&
                module.SimulationCamera == originalCamera && !manager.IsPlayerControlled &&
                !Resources.FindObjectsOfTypeAll<MotionPreviewCamera>().Any(camera => camera.Target == avatar.transform),
                "A late startup failure with an occupied bridge restores temporary resources without clearing the other bridge owner");
            AvatarTools.UpdateSceneCamera.SimulationView = null;
            ClickGame();
            Check(session.Running && ReferenceEquals(AvatarTools.UpdateSceneCamera.SimulationView, session),
                "Game Simulation can claim its bridge again after a rejected late startup");
            var failingInput = new SessionMotionInput { Error = new InvalidOperationException("Injected bridge input failure") };
            typeof(CharacterMotionMotor).GetField("_input", instance).SetValue(session.Motor, failingInput);
            typeof(KeyboardMotionInputSource).GetField("<Suspended>k__BackingField", instance).SetValue(Keyboard(), false);
            session.Motor.Step(0.02f);
            CheckReleased("A real motor input failure");
            Check(session.Error.Contains("Injected bridge input failure"), "The failed bridge session retains the actual input diagnostic");
            ClickGame();
            Check(session.Running && ReferenceEquals(AvatarTools.UpdateSceneCamera.SimulationView, session),
                "The Game Simulation bridge restarts after input failure");
            EditorApplication.isPaused = true;
            session.Poll();
            CheckReleased("Pausing Game Simulation");
            EditorApplication.isPaused = false;
            ClickGame();
            Check(session.Running && ReferenceEquals(AvatarTools.UpdateSceneCamera.SimulationView, session),
                "The Game Simulation bridge restarts after pause cleanup");
            manager.Module = null;
            session.Poll();
            CheckReleased("Unlinking the controlled module");
            manager.Module = module;
            gameTool.Dispose();
            gameTool.Dispose();
            Check(AvatarTools.UpdateSceneCamera.SimulationView == null && initialMainState.Matches(originalCamera),
                "Repeated final tool disposal leaves no bridge and preserves the original camera");
        }
        finally
        {
            duplicateTool?.Dispose();
            if (registeredTools != null)
                foreach (var tool in registeredTools) tool.Dispose();
            else gameTool?.Dispose();
            sceneTool?.Dispose();
            restore.Invoke(null, null);
            AvatarTools.UpdateSceneCamera.SimulationView = previousBridge;
            targetField.SetValue(null, previousTarget);
            stateField.SetValue(null, previousState);
            instancesField.SetValue(null, previousInstances);
            if (hadPreference) EditorPrefs.SetBool(preference, previousPreference);
            else EditorPrefs.DeleteKey(preference);
            if (manager) { manager.Module = null; UnityEngine.Object.DestroyImmediate(manager.gameObject); }
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            if (originalCameraObject) UnityEngine.Object.DestroyImmediate(originalCameraObject);
            observer.LookAt(previousPivot, previousRotation, previousSize, previousOrthographic, true);
            observer.Repaint();
            observer.SendEvent(new Event { type = EventType.Repaint });
            EditorApplication.isPaused = paused;
        }
    }

    private sealed class SimulationViewStub : AvatarTools.UpdateSceneCamera.ISimulationView
    {
        public bool SceneCameraEnabled { get; set; }
    }

    private sealed class SceneCameraState
    {
        private readonly Transform _parent;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly Vector3 _scale;
        private readonly bool _orthographic;
        private readonly float _size;
        private readonly float _fieldOfView;
        private readonly float _near;
        private readonly float _far;
        private readonly float _aspect;

        public SceneCameraState(Camera camera)
        {
            _parent = camera.transform.parent;
            _position = camera.transform.localPosition;
            _rotation = camera.transform.localRotation;
            _scale = camera.transform.localScale;
            _orthographic = camera.orthographic;
            _size = camera.orthographicSize;
            _fieldOfView = camera.fieldOfView;
            _near = camera.nearClipPlane;
            _far = camera.farClipPlane;
            _aspect = camera.aspect;
        }

        public bool Matches(Camera camera) => camera.transform.parent == _parent &&
            Vector3.Distance(camera.transform.localPosition, _position) < 0.0001f &&
            Quaternion.Angle(camera.transform.localRotation, _rotation) < 0.001f && camera.transform.localScale == _scale &&
            camera.orthographic == _orthographic && Mathf.Abs(camera.orthographicSize - _size) < 0.0001f &&
            Mathf.Abs(camera.fieldOfView - _fieldOfView) < 0.0001f && Mathf.Abs(camera.nearClipPlane - _near) < 0.0001f &&
            Mathf.Abs(camera.farClipPlane - _far) < 0.001f && Mathf.Abs(camera.aspect - _aspect) < 0.0001f;
    }

    private static bool MatricesClose(Matrix4x4 first, Matrix4x4 second)
    {
        for (var index = 0; index < 16; index++)
            if (Mathf.Abs(first[index] - second[index]) > 0.0001f) return false;
        return true;
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

    private sealed class GameFrameMotionInput : IMotionInputSource
    {
        private readonly KeyboardMotionInputSource _keyboard;
        private readonly MethodInfo _createFrame = typeof(KeyboardMotionInputSource).GetMethod("CreateGameFrame",
            BindingFlags.Instance | BindingFlags.NonPublic);
        public Vector2 Move;
        public Vector2 Look;
        public float Turn;
        public Exception Error;
        public MotionInputFrame LastFrame;

        public GameFrameMotionInput(KeyboardMotionInputSource keyboard) => _keyboard = keyboard;

        public MotionInputFrame Sample()
        {
            if (Error != null) throw Error;
            LastFrame = (MotionInputFrame)_createFrame.Invoke(_keyboard, new object[] { Move, Turn, Look, false, false, MotionPose.None });
            return LastFrame;
        }

        public void Reset() { Move = default; Look = default; Turn = 0f; }
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
