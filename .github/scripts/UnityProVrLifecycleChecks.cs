using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.XR;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Extensions.Editor;
using VRCLearn.GestureManagerPro.Modules;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using MenuControl = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;
using Pose = UnityEngine.Pose;

// Runs the production VR session in Play Mode with a real Humanoid animation graph and injected device input.
// It validates resource ownership and state transitions, not a physical headset or VRChat client parity.
public static class UnityProVrLifecycleChecks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly string[] Owned = { "VRMode", "TrackingType", "GestureLeft", "GestureRight",
        "GestureLeftWeight", "GestureRightWeight", "Upright", "AFK" };

    public static void Run(Action<bool, string> check)
    {
        check(EditorApplication.isPlaying && !EditorApplication.isPaused, "VR lifecycle checks run in resumed Play Mode");
        var nativeXr = new NativeXrState();
        check(nativeXr.Idle, "Play Mode without Game Simulation has no native XR display/input subsystem or GMP SteamVR loader");
        ProValidationWindow host = null;
        GestureManager manager = null;
        Animator avatar = null;
        Avatar human = null;
        ModuleVrc3 module = null;
        ControlledModeSession motion = null;
        VrSimulationSession session = null;
        Camera observer = null;
        try
        {
            host = ScriptableObject.CreateInstance<ProValidationWindow>();
            host.Show();
            var owner = typeof(EditorWindow).GetField("m_Parent", Any).GetValue(host) as ScriptableObject;
            typeof(GUIUtility).GetMethod("BeginContainerFromOwner", Any).Invoke(null, new object[] { owner });
            typeof(GUIUtility).GetMethod("BeginGUI", Any).Invoke(null, new object[] { 1, host.GetInstanceID(), 1 });
            typeof(EditorStyles).GetMethod("UpdateSkinCache", Any, null, new[] { typeof(int) }, null)
                .Invoke(null, new object[] { EditorGUIUtility.isProSkin ? 1 : 0 });

            avatar = UnityProTrackingChecks.CreateHumanoid(out human, out var bones);
            check(human && human.isValid && human.isHuman, "The VR lifecycle fixture has a real valid Humanoid skeleton");
            if (!human || !human.isValid || !human.isHuman) return;
            var descriptor = avatar.gameObject.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = bones[HumanBodyBones.Head].position + new Vector3(0f, 0.04f, 0.06f);
            descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
            descriptor.baseAnimationLayers = new[]
            {
                Layer(VRCAvatarDescriptor.AnimLayerType.Base), Layer(VRCAvatarDescriptor.AnimLayerType.Additive),
                Layer(VRCAvatarDescriptor.AnimLayerType.Gesture), Layer(VRCAvatarDescriptor.AnimLayerType.Action),
                Layer(VRCAvatarDescriptor.AnimLayerType.FX)
            };
            descriptor.specialAnimationLayers = new[] { Layer(VRCAvatarDescriptor.AnimLayerType.Sitting),
                Layer(VRCAvatarDescriptor.AnimLayerType.TPose), Layer(VRCAvatarDescriptor.AnimLayerType.IKPose) };
            manager = new GameObject("Pro VR lifecycle manager").AddComponent<GestureManager>();
            manager.settings = new ModuleSettings();
            module = new ModuleVrc3(descriptor);
            manager.SetModule(module);
            Evaluate(module);
            check(module.Active && !module.IsInvalid() && module.GetParam("GestureLeft") != null,
                "The VR fixture connects the production eight-layer simulator and built-in parameter catalog");
            var originalParameters = new Dictionary<string, float>();
            for (var index = 0; index < Owned.Length; index++)
            {
                var value = Owned[index] == "VRMode" ? 0f : Owned[index] == "TrackingType" ? 4f :
                    Owned[index] == "GestureLeft" ? 2f : Owned[index] == "GestureRight" ? 5f : (index + 1) * 0.1f;
                module.GetParam(Owned[index]).Set(module, value);
            }
            foreach (var name in Owned) originalParameters[name] = module.GetParam(name).FloatValue();
            module.SetGlobalHeadChop(true);
            module.SetCalibrationPose(0.35f);
            var originalPose = module.CalibrationPoseWeight;
            var originalRoot = new Pose(avatar.transform.position, avatar.transform.rotation);
            observer = new GameObject("Pro VR lifecycle observer").AddComponent<Camera>();
            observer.stereoTargetEye = StereoTargetEyeMask.Right;
            module.SimulationCamera = observer;
            var source = new FakeVrInput { Tracking = Frame(bones, descriptor, false) };
            motion = new ControlledModeSession();
            session = new VrSimulationSession(motion);
            var settings = new VrSimulationSettings { UserHeight = 0f, UserArmSpan = 0f };
            check(session.Start(manager, source, settings, collisions: false), "The production VR session starts with injected head and two controllers: " + session.Error);
            if (!session.Running) return;
            check(session.Calibrating && manager.IsPlayerControlled && !module.GlobalHeadChopEnabled &&
                Mathf.Abs(module.CalibrationPoseWeight - 1f) < 0.0001f,
                "Starting VR owns the avatar, enters calibration pose and suspends global head clipping");
            check(session.Camera && session.Camera.stereoTargetEye == StereoTargetEyeMask.Both &&
                observer.stereoTargetEye == StereoTargetEyeMask.None && module.SimulationCamera == session.Camera &&
                motion.Mode == ControlledSessionMode.VirtualReality,
                "The VR session owns one stereo camera while the previous game camera becomes a desktop observer");
            Confirm(session, source, module);
            check(!session.Calibrating && session.Calibration.Calibrated && Mathf.Abs(module.CalibrationPoseWeight - originalPose) < 0.0001f,
                "Releasing and pressing both triggers confirms calibration and restores the previous pose-layer weight");
            session.Tick();
            module.LateUpdate();
            check(module.GetParam("TrackingType").IntValue() == 3 && module.GetParam("VRMode").BoolValue(),
                "Three tracked head/hand devices publish VRMode and the head-and-hands TrackingType");
            session.Calibration.TryGetViewPose(session.Tracking, out var expectedView);
            check(Vector3.Distance(session.Camera.transform.position, expectedView.position) < 0.0001f,
                "The actual stereo camera follows the calibrated eye pose rather than the head-bone target");

            source.Tracking = Move(source.Tracking, TrackingRole.Head, new Vector3(0.06f, 0f, 0.02f), Quaternion.Euler(0f, 25f, 0f));
            source.Motion = new MotionInputFrame(Vector2.up, 0f);
            typeof(ModuleVrc3).GetField("LocomotionDisabled", Any).SetValue(module, true);
            var blockedPosition = avatar.transform.position;
            var previousCamera = session.Camera.transform.position;
            var previousHeadRotation = bones[HumanBodyBones.Head].rotation;
            session.Tick();
            motion.Motor.Step(0.1f);
            module.LateUpdate();
            var rootDistance = Vector3.Distance(avatar.transform.position, blockedPosition);
            var cameraDistance = session.Camera ? Vector3.Distance(session.Camera.transform.position, previousCamera) : -1f;
            var headAngle = Quaternion.Angle(bones[HumanBodyBones.Head].rotation, expectedView.rotation);
            var headChange = Quaternion.Angle(bones[HumanBodyBones.Head].rotation, previousHeadRotation);
            var headState = module.TrackingStates.TryGetValue("Head", out var state) ? state.ToString() : "Missing";
            var hasHeadTarget = session.Calibration.TryGetWorldPose(TrackingRole.Head, session.Tracking, out var headTarget);
            var targetError = hasHeadTarget ? Quaternion.Angle(bones[HumanBodyBones.Head].rotation, headTarget.rotation) : -1f;
            Debug.Log("[Pro VR blocked tracking] rootDistance=" + rootDistance.ToString("F6") +
                " cameraDistance=" + cameraDistance.ToString("F6") + " headAngle=" + headAngle.ToString("F3") +
                " headChange=" + headChange.ToString("F3") + " targetError=" + targetError.ToString("F3") +
                " headTarget=" + hasHeadTarget + " Head=" + headState + " Paused=" + session.Paused +
                " Calibrating=" + session.Calibrating + " Running=" + session.Running + " Error=" + session.Error);
            check(rootDistance < 0.0001f,
                "Disabling avatar locomotion prevents motor displacement; rootDistance=" + rootDistance.ToString("F6"));
            check(cameraDistance > 0.02f,
                "The headset camera continues position tracking while locomotion is blocked; cameraDistance=" + cameraDistance.ToString("F6"));
            check(headChange > 10f && hasHeadTarget && targetError < 0.1f,
                "The avatar head follows its calibrated bone target while locomotion is blocked; headChange=" + headChange.ToString("F3") + " targetError=" + targetError.ToString("F3") + " Head=" + headState);
            typeof(ModuleVrc3).GetField("LocomotionDisabled", Any).SetValue(module, false);
            source.Motion = default;
            Evaluate(module);
            var animationHips = bones[HumanBodyBones.Hips].localPosition;
            var trackingStates = (Dictionary<string, VRC_AnimatorTrackingControl.TrackingType>)module.TrackingStates;
            trackingStates["Hip"] = VRC_AnimatorTrackingControl.TrackingType.Animation;
            source.Tracking = Move(source.Tracking, TrackingRole.Head, new Vector3(0.08f, 0f, 0.04f));
            session.Tick();
            module.LateUpdate();
            check(Vector3.Distance(bones[HumanBodyBones.Hips].localPosition, animationHips) < 0.0001f,
                "The production SDK Hip Animation tracking gate blocks pelvis inference while the HMD continues tracking");
            trackingStates["Hip"] = VRC_AnimatorTrackingControl.TrackingType.Tracking;

            source.Tracking = Frame(bones, descriptor, true);
            session.RequestCalibration();
            Confirm(session, source, module);
            session.Tick();
            module.LateUpdate();
            check(module.GetParam("TrackingType").IntValue() == 6 && session.Tracking.Devices.Count == 11 &&
                session.Calibration.TryGetWorldPose(TrackingRole.Hip, session.Tracking, out _),
                "Eleven mapped points calibrate head, hands, hip, feet, chest, knees and upper arms and publish full-body TrackingType");
            var reassigned = new List<TrackedDevice>(source.Tracking.Devices);
            var rawHip = Find(source.Tracking, TrackingRole.Hip);
            reassigned.Add(new TrackedDevice("manual:hip", TrackingRole.Unassigned, rawHip.Pose, true, 2d));
            source.Tracking = new TrackingFrame(reassigned, 2d);
            session.Assign("manual:hip", TrackingRole.Hip);
            session.Tick();
            check(session.Calibrating && session.Tracking.TryGet(TrackingRole.Hip, out var assigned) && assigned.Id == "manual:hip" &&
                session.Tracking.Devices.Count == 11,
                "Assigning a stable device ID requests recalibration and replaces the automatic source for that role");
            Confirm(session, source, module);

            source.Motion = new MotionInputFrame(Vector2.up, 0f);
            session.Tick();
            var beforePause = avatar.transform.position;
            session.TogglePause();
            motion.Motor.Step(0.1f);
            check(session.Paused && Vector3.Distance(avatar.transform.position, beforePause) < 0.0001f,
                "Pausing synchronously clears the pending movement frame before another motor step can run");
            source.Motion = default;
            source.Tracking = Move(source.Tracking, TrackingRole.Head, new Vector3(0.04f, 0f, 0f));
            session.Tick();
            module.LateUpdate();
            check(session.Paused && !motion.Motor.enabled && session.Camera,
                "A paused session retains its headset camera and disables avatar locomotion");
            session.TogglePause();
            session.Tick();
            module.LateUpdate();
            check(!session.Paused && motion.Motor.enabled && session.Running,
                "Resuming restores live tracking and motor availability within the same owned session");
            Evaluate(module);
            trackingStates["Hip"] = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            var animationHipPosition = bones[HumanBodyBones.Hips].localPosition;
            source.Tracking = MoveDevice(source.Tracking, "manual:hip", new Vector3(0.02f, -0.03f, 0f));
            session.Tick();
            module.LateUpdate();
            check(Vector3.Distance(bones[HumanBodyBones.Hips].localPosition, animationHipPosition) > 0.01f,
                "The live session applies a real hip-tracker displacement before testing tracking-loss release");
            var connectedFrame = source.Tracking;
            var animatedHipRotation = Quaternion.Euler(0f, 17f, 0f) * bones[HumanBodyBones.Hips].localRotation;
            bones[HumanBodyBones.Hips].localRotation = animatedHipRotation;
            var headLost = new List<TrackedDevice>();
            foreach (var device in source.Tracking.Devices) if (device.Role != TrackingRole.Head) headLost.Add(device);
            source.Tracking = new TrackingFrame(headLost, connectedFrame.Timestamp + 1d, connectedFrame.SpaceId, connectedFrame.Origin);
            session.Tick();
            module.LateUpdate();
            check(session.Running && !motion.Motor.enabled && module.GetParam("AFK").BoolValue() &&
                Vector3.Distance(bones[HumanBodyBones.Hips].localPosition, animationHipPosition) < 0.0001f &&
                Quaternion.Angle(bones[HumanBodyBones.Hips].localRotation, animatedHipRotation) < 0.001f,
                "Losing the headset releases prior IK position channels while preserving current animation rotation and session ownership");
            source.Tracking = connectedFrame;
            session.Tick();
            module.LateUpdate();
            var focusHipPosition = bones[HumanBodyBones.Hips].localPosition;
            source.InputAvailable = false;
            session.Tick();
            module.LateUpdate();
            check(session.Running && !motion.Motor.enabled && module.GetParam("AFK").BoolValue() &&
                Vector3.Distance(focusHipPosition, animationHipPosition) > 0.01f &&
                Vector3.Distance(bones[HumanBodyBones.Hips].localPosition, animationHipPosition) < 0.0001f,
                "Loss of VR input focus releases active IK and publishes AFK without destroying the session");
            source.InputAvailable = true;
            session.Tick();
            check(motion.Motor.enabled && !module.GetParam("AFK").BoolValue(),
                "Returning VR input focus resumes tracking and clears AFK");

            source.Tracking = new TrackingFrame(source.Tracking.Devices, source.Tracking.Timestamp + 1d,
                "standing:reset", source.Tracking.Origin);
            session.Tick();
            check(session.Calibrating && !motion.Motor.enabled,
                "A changed provider tracking-space identity suspends movement and requests fresh calibration");
            // A second reset can arrive while the user is already confirming calibration.
            source.Tracking = new TrackingFrame(source.Tracking.Devices, source.Tracking.Timestamp + 1d,
                "standing:reset-again", source.Tracking.Origin);
            Confirm(session, source, module);
            session.Tick();
            check(!session.Calibrating && session.Calibration.Calibrated && motion.Motor.enabled,
                "Calibration accepts the latest provider space without immediately requesting another calibration");

            var ownedCamera = session.Camera;
            session.Stop();
            check(!session.Running && !motion.Running && !manager.IsPlayerControlled && !ownedCamera &&
                source.Disposals == 1 && !source.Running,
                "Normal exit releases device ownership, movement ownership and the temporary XR camera exactly once");
            CheckRestored(check, module, observer, originalParameters, originalPose, originalRoot, avatar);

            var failedSource = new FakeVrInput { Tracking = Frame(bones, descriptor, false), FailStart = true };
            check(!session.Start(manager, failedSource, settings, collisions: false) && failedSource.Disposals == 1 &&
                module.GlobalHeadChopEnabled && Mathf.Abs(module.CalibrationPoseWeight - originalPose) < 0.0001f && !manager.IsPlayerControlled,
                "A provider startup failure preserves the avatar's head-chop and pose state and releases the failed provider");
            var missingHead = new FakeVrInput { Tracking = TrackingFrame.Empty };
            check(!session.Start(manager, missingHead, settings, collisions: false) && missingHead.Disposals == 1 &&
                !motion.Running && module.GlobalHeadChopEnabled && observer.stereoTargetEye == StereoTargetEyeMask.Right && module.SimulationCamera == observer,
                "A late startup failure after creating the XR rig restores all camera and avatar ownership");
            var throwingSource = new FakeVrInput { Tracking = Frame(bones, descriptor, false) };
            check(session.Start(manager, throwingSource, settings, collisions: false), "A new VR session can start after failed setup cleanup");
            throwingSource.ThrowOnPoll = true;
            session.Tick();
            check(!session.Running && !motion.Running && throwingSource.Disposals == 1 && session.Error == "Injected VR tracking failure.",
                "An exception in tracking polling tears down the complete production session and preserves its cause");
            CheckRestored(check, module, observer, originalParameters, originalPose, originalRoot, avatar);
            CheckGameSimulationEntry(check, manager, module, bones, descriptor, observer,
                originalParameters, originalPose, originalRoot, avatar, nativeXr);
            CheckInputSwitching(check, manager, module, bones, descriptor, observer,
                originalParameters, originalPose, originalRoot, avatar, nativeXr);
            var resettingSource = new FakeVrInput { Tracking = Frame(bones, descriptor, false) };
            check(session.Start(manager, resettingSource, settings, collisions: false), "The VR session can restart before testing simulator reset ownership");
            var previousParameter = module.GetParam("TrackingType");
            module.ResetAvatar();
            var rebuiltParameter = module.GetParam("TrackingType");
            var rebuiltValue = rebuiltParameter.FloatValue();
            session.Tick();
            check(!ReferenceEquals(previousParameter, rebuiltParameter) && !session.Running && !motion.Running && resettingSource.Disposals == 1,
                "Rebuilding the simulator graph in the same module terminates the session and releases its old parameter ownership");
            check(ReferenceEquals(rebuiltParameter, module.GetParam("TrackingType")) &&
                Mathf.Abs(module.GetParam("TrackingType").FloatValue() - rebuiltValue) < 0.0001f &&
                module.SimulationCamera == observer && observer.stereoTargetEye == StereoTargetEyeMask.Right &&
                module.GlobalHeadChopEnabled && Mathf.Abs(module.CalibrationPoseWeight - originalPose) < 0.0001f,
                "Reset cleanup preserves new simulator parameters and restores the observer camera, head chop and original pose weight");
        }
        finally
        {
            session?.Dispose();
            motion?.Dispose();
            if (manager)
            {
                if (ReferenceEquals(manager.Module, module) && module != null && module.GetParam("EyeHeightAsMeters") != null) manager.UnlinkModule();
                else manager.Module = null;
                UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }
            if (module != null)
            {
                typeof(ModuleVrc3).GetMethod("StopVrcHooks", Any).Invoke(module, null);
                module.Active = false;
                var clone = typeof(ModuleVrc3).GetField("_memoryClone", Any).GetValue(module) as VRCAvatarDescriptor;
                if (clone) UnityEngine.Object.DestroyImmediate(clone.gameObject);
                var graph = (PlayableGraph)typeof(ModuleVrc3).GetField("_playableGraph", Any).GetValue(module);
                if (graph.IsValid()) graph.Destroy();
            }
            if (avatar)
            {
                GestureManager.ControlledAvatars.Remove(avatar.gameObject);
                UnityEngine.Object.DestroyImmediate(avatar.gameObject);
            }
            if (observer) UnityEngine.Object.DestroyImmediate(observer.gameObject);
            if (human) UnityEngine.Object.DestroyImmediate(human);
            if (host) UnityEngine.Object.DestroyImmediate(host);
            nativeXr.CheckUnchanged(check, "Full VR lifecycle fixture cleanup");
        }
    }

    private static void CheckGameSimulationEntry(Action<bool, string> check, GestureManager manager,
        ModuleVrc3 module, Dictionary<HumanBodyBones, Transform> bones, VRCAvatarDescriptor descriptor,
        Camera observer, Dictionary<string, float> originalParameters, float originalPose, Pose originalRoot, Animator avatar,
        NativeXrState nativeXr)
    {
        // Exercise the same tool toggle used by the Inspector and radial tools menu,
        // including provider discovery. The native SteamVR provider must never run here.
        var providers = (List<VrInputProviders.Provider>)typeof(VrInputProviders).GetField("Registered", Any).GetValue(null);
        var registeredProviders = providers.ToArray();
        GameSimulationTool tool = null;
        FakeVrInput source = null;
        var creations = 0;
        var failStart = false;
        try
        {
            providers.Clear();
            VrInputProviders.Register("validation:tool", "Injected tool provider", () =>
            {
                creations++;
                return source = new FakeVrInput { Tracking = Frame(bones, descriptor, false), FailStart = failStart };
            });
            tool = new GameSimulationTool(manager);
            ((MicrophoneLipSync)typeof(GameSimulationTool).GetField("_lipSync", Any).GetValue(tool)).Enabled = false;
            typeof(GameSimulationTool).GetField("_collisions", Any).SetValue(tool, false);
            var inputMode = typeof(GameSimulationTool).GetField("_inputMode", Any);
            var keyboard = typeof(GameSimulationTool).GetField("_keyboard", Any);
            var motion = (ControlledModeSession)typeof(GameSimulationTool).GetField("_session", Any).GetValue(tool);
            var vr = (VrSimulationSession)typeof(GameSimulationTool).GetField("_vr", Any).GetValue(tool);
            var settings = (VrSimulationSettings)typeof(GameSimulationTool).GetField("_vrSettings", Any).GetValue(tool);
            settings.UserHeight = 0f;
            settings.UserArmSpan = 0f;
            var toggle = typeof(GameSimulationTool).GetMethod("Toggle", Any);
            nativeXr.CheckUnchanged(check, "Creating an idle Game Simulation tool");

            toggle.Invoke(tool, new object[] { module });
            check(creations == 0 && motion.Running && motion.Mode == ControlledSessionMode.GameSimulation &&
                keyboard.GetValue(tool) is KeyboardMotionInputSource && !vr.Running && (int)inputMode.GetValue(tool) == 0,
                "A new game simulation starts with PC input without constructing a VR provider");
            nativeXr.CheckUnchanged(check, "Running the default PC simulation");
            toggle.Invoke(tool, new object[] { module });
            check(!motion.Running && !vr.Running && (int)inputMode.GetValue(tool) == 0 && creations == 0,
                "Exiting the default PC session leaves PC selected for the next entry");
            CheckRestored(check, module, observer, originalParameters, originalPose, originalRoot, avatar);

            inputMode.SetValue(tool, 1);
            nativeXr.CheckUnchanged(check, "Selecting VR before starting Game Simulation");
            toggle.Invoke(tool, new object[] { module });
            check(creations == 1 && source.Starts == 1 && source.Running && vr.Running && motion.Running &&
                motion.Mode == ControlledSessionMode.VirtualReality && keyboard.GetValue(tool) == null &&
                manager.IsPlayerControlled && vr.Camera && module.SimulationCamera == vr.Camera,
                "The game-simulation entry routes the selected VR input through the registered provider factory and owns the stereo session: " + vr.Error);
            if (!vr.Running) return;
            Confirm(vr, source, module);
            source.Actions = new VrInputFrame(Vector2.zero, gestureLeft: 4, gestureRight: 6);
            vr.Tick();
            module.LateUpdate();
            check(!vr.Calibrating && module.GetParam("VRMode").BoolValue() &&
                module.GetParam("TrackingType").IntValue() == 3 && module.GetParam("GestureLeft").IntValue() == 4 &&
                module.GetParam("GestureRight").IntValue() == 6,
                "VR started through the game-simulation tool calibrates and publishes real tracking and gesture parameters");
            var camera = vr.Camera;
            toggle.Invoke(tool, new object[] { module });
            check(!motion.Running && !vr.Running && !manager.IsPlayerControlled && !camera && source.Disposals == 1 &&
                !source.Running && keyboard.GetValue(tool) == null && (int)inputMode.GetValue(tool) == 0,
                "The shared exit releases the VR provider and XR camera, then restores PC as the next entry's default");
            CheckRestored(check, module, observer, originalParameters, originalPose, originalRoot, avatar);

            failStart = true;
            inputMode.SetValue(tool, 1);
            toggle.Invoke(tool, new object[] { module });
            check(creations == 2 && source.Starts == 1 && source.Disposals == 1 && !source.Running &&
                !motion.Running && !vr.Running && !vr.Camera && !manager.IsPlayerControlled &&
                vr.Error == "Injected VR startup failure." && keyboard.GetValue(tool) == null,
                "A provider failure through the game-simulation entry reports its cause without retaining avatar, camera or keyboard ownership");
            CheckRestored(check, module, observer, originalParameters, originalPose, originalRoot, avatar);

            failStart = false;
            toggle.Invoke(tool, new object[] { module });
            check(creations == 3 && vr.Running && motion.Mode == ControlledSessionMode.VirtualReality && source.Starts == 1,
                "The same VR entry can retry after a provider startup failure using a fresh provider instance: " + vr.Error);
            if (motion.Running) toggle.Invoke(tool, new object[] { module });
            check(source.Disposals == 1 && !motion.Running && !vr.Running,
                "Exiting a retried VR session releases the replacement provider once");

            inputMode.SetValue(tool, 0);
            toggle.Invoke(tool, new object[] { module });
            check(creations == 3 && source.Disposals == 1 && !source.Running && motion.Running &&
                motion.Mode == ControlledSessionMode.GameSimulation && keyboard.GetValue(tool) is KeyboardMotionInputSource &&
                !vr.Running && !vr.Camera && manager.IsPlayerControlled &&
                Mathf.Abs(module.GetParam("VRMode").FloatValue() - originalParameters["VRMode"]) < 0.0001f,
                "Selecting PC uses the keyboard game-simulation backend without constructing a VR provider or changing VRMode: " + motion.Error);
            nativeXr.CheckUnchanged(check, "Running PC after an injected VR session");
            if (motion.Running) toggle.Invoke(tool, new object[] { module });
            check(!motion.Running && !vr.Running && !manager.IsPlayerControlled && keyboard.GetValue(tool) == null && creations == 3,
                "The same game-simulation exit restores the PC backend without touching the VR provider registry");
            CheckRestored(check, module, observer, originalParameters, originalPose, originalRoot, avatar);
        }
        finally
        {
            tool?.Dispose();
            providers.Clear();
            providers.AddRange(registeredProviders);
        }
    }

    private static void CheckInputSwitching(Action<bool, string> check, GestureManager manager,
        ModuleVrc3 module, Dictionary<HumanBodyBones, Transform> bones, VRCAvatarDescriptor descriptor,
        Camera observer, Dictionary<string, float> originalParameters, float originalPose, Pose originalRoot, Animator avatar,
        NativeXrState nativeXr)
    {
        // Switch through the production Inspector input selector, without entering any native XR loader.
        // The simulator stays active across backend replacement and owns one original exit baseline.
        var providers = (List<VrInputProviders.Provider>)typeof(VrInputProviders).GetField("Registered", Any).GetValue(null);
        var registeredProviders = providers.ToArray();
        var originalMenu = descriptor.expressionsMenu;
        var previewMode = module.GetParam("PreviewMode");
        var originalPreview = previewMode.FloatValue();
        var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        GameSimulationTool tool = null;
        FakeVrInput source = null;
        var sources = new List<FakeVrInput>();
        var creations = 0;
        var failStart = false;
        try
        {
            providers.Clear();
            menu.controls = new List<MenuControl>
            {
                new MenuControl { name = "Held switching button", type = MenuControl.ControlType.Button,
                    parameter = new MenuControl.Parameter { name = "PreviewMode" }, value = 1f }
            };
            descriptor.expressionsMenu = menu;
            previewMode.Set(module, 0f);
            tool = new GameSimulationTool(manager);
            ((MicrophoneLipSync)typeof(GameSimulationTool).GetField("_lipSync", Any).GetValue(tool)).Enabled = false;
            typeof(GameSimulationTool).GetField("_collisions", Any).SetValue(tool, false);
            var inputMode = typeof(GameSimulationTool).GetField("_inputMode", Any);
            var inputError = typeof(GameSimulationTool).GetField("_inputError", Any);
            var keyboard = typeof(GameSimulationTool).GetField("_keyboard", Any);
            var captureRequested = typeof(KeyboardMotionInputSource).GetField("_captureRequested", Any);
            var motion = (ControlledModeSession)typeof(GameSimulationTool).GetField("_session", Any).GetValue(tool);
            var vr = (VrSimulationSession)typeof(GameSimulationTool).GetField("_vr", Any).GetValue(tool);
            var settings = (VrSimulationSettings)typeof(GameSimulationTool).GetField("_vrSettings", Any).GetValue(tool);
            settings.UserHeight = 0f;
            settings.UserArmSpan = 0f;
            var toggle = typeof(GameSimulationTool).GetMethod("Toggle", Any);
            var setInputMode = typeof(GameSimulationTool).GetMethod("SetInputMode", Any);
            var active = typeof(GameSimulationTool).GetProperty("Active", Any);
            check(setInputMode != null && inputError != null && active != null,
                "The game-simulation input selector exposes one shared switching operation and a persistent error state");
            if (setInputMode == null || inputError == null || active == null) return;
            Func<int, bool> select = mode => (bool)setInputMode.Invoke(tool, new object[] { module, mode });
            Func<bool> isActive = () => (bool)active.GetValue(tool);
            Func<Pose, bool> sameRoot = pose => Vector3.Distance(avatar.transform.position, pose.position) < 0.0001f &&
                Quaternion.Angle(avatar.transform.rotation, pose.rotation) < 0.001f;

            toggle.Invoke(tool, new object[] { module });
            // F5 and mouse-wheel commands update the running session before its Inspector GUI runs.
            // A backend handover must preserve these live values rather than stale GUI fields.
            motion.SetViewMode(SimulationViewMode.ThirdPerson);
            motion.AdjustCameraDistance(-5f);
            motion.SetObserverCameraFollow(true);
            var firstPcMotor = motion.Motor;
            var firstPcCamera = module.SimulationCamera;
            var firstKeyboard = keyboard.GetValue(tool);
            var firstPosition = new Pose(originalRoot.position + new Vector3(1.1f, 0.2f, -0.7f),
                Quaternion.Euler(0f, 47f, 0f) * originalRoot.rotation);
            motion.Reposition(firstPosition);
            check(isActive() && motion.Running && motion.Mode == ControlledSessionMode.GameSimulation && sameRoot(firstPosition),
                "Input switching begins from an active PC simulation after avatar movement and rotation");
            nativeXr.CheckUnchanged(check, "PC movement and camera changes");
            var missingProvider = select(1);
            check(!missingProvider && isActive() && motion.Running && !vr.Running && (int)inputMode.GetValue(tool) == 0 &&
                ReferenceEquals(motion.Motor, firstPcMotor) && ReferenceEquals(keyboard.GetValue(tool), firstKeyboard) &&
                module.SimulationCamera == firstPcCamera && sameRoot(firstPosition) &&
                !string.IsNullOrEmpty((string)inputError.GetValue(tool)),
                "Selecting VR without a registered provider keeps the existing PC motor, camera, input and position active and reports an error");

            VrInputProviders.Register("validation:switch", "Injected switching provider", () =>
            {
                creations++;
                throw new InvalidOperationException("Injected VR factory failure.");
            });
            var throwingFactory = select(1);
            check(!throwingFactory && creations == 1 && isActive() && !vr.Running && (int)inputMode.GetValue(tool) == 0 &&
                ReferenceEquals(motion.Motor, firstPcMotor) && ReferenceEquals(keyboard.GetValue(tool), firstKeyboard) &&
                module.SimulationCamera == firstPcCamera && sameRoot(firstPosition) &&
                ((string)inputError.GetValue(tool))?.Contains("Injected VR factory failure.") == true,
                "A throwing VR factory leaves the original PC backend running and exposes its cause without releasing ownership");

            VrInputProviders.Register("validation:switch", "Injected switching provider", () =>
            {
                creations++;
                source = new FakeVrInput { Tracking = Frame(bones, descriptor, false), FailStart = failStart };
                sources.Add(source);
                return source;
            });
            var switchedToVr = select(1);
            check(switchedToVr && isActive() && vr.Running && motion.Mode == ControlledSessionMode.VirtualReality &&
                (int)inputMode.GetValue(tool) == 1 && keyboard.GetValue(tool) == null && !firstPcMotor &&
                source.Starts == 1 && source.Disposals == 0 && sameRoot(firstPosition) &&
                string.IsNullOrEmpty((string)inputError.GetValue(tool)),
                "An active PC session switches directly to VR, preserves its moved root pose and replaces the old input resources without exiting");
            if (!vr.Running) return;
            Confirm(vr, source, module);
            source.Actions = new VrInputFrame(Vector2.up, menuPressed: true, selectPressed: true);
            vr.Tick();
            var oldVrSource = source;
            var oldVrCamera = vr.Camera;
            var oldMenu = (VrExpressionMenuView)typeof(VrSimulationSession).GetField("_menu", Any).GetValue(vr);
            var oldMenuRoot = (GameObject)typeof(VrExpressionMenuView).GetField("_root", Any).GetValue(oldMenu);
            check(oldMenu.IsOpen && oldMenuRoot && Mathf.Abs(previewMode.FloatValue() - 1f) < 0.0001f,
                "A live headset expression-menu button is held before switching back to PC");
            var switchedToPc = select(0);
            check(switchedToPc && isActive() && motion.Running && motion.Mode == ControlledSessionMode.GameSimulation &&
                !vr.Running && !vr.Camera && !oldVrCamera && !oldMenuRoot && !oldMenu.IsOpen &&
                keyboard.GetValue(tool) is KeyboardMotionInputSource && oldVrSource.Disposals == 1 && !oldVrSource.Running &&
                Mathf.Abs(previewMode.FloatValue()) < 0.0001f && (int)inputMode.GetValue(tool) == 0 && sameRoot(firstPosition),
                "An active VR session switches directly to PC, releases the held menu button and destroys its menu, camera and provider once while preserving the avatar root");
            check(motion.ViewMode == SimulationViewMode.ThirdPerson && motion.ThirdPersonEnabled &&
                Mathf.Abs(motion.CameraDistance - 3.25f) < 0.0001f && motion.FollowObserverCamera &&
                keyboard.GetValue(tool) is KeyboardMotionInputSource &&
                (bool)captureRequested.GetValue(keyboard.GetValue(tool)),
                "Returning from VR preserves the live third-person view, wheel-adjusted camera distance and observer-follow setting, and retains a mouse-capture request after repositioning");

            var secondPosition = new Pose(firstPosition.position + new Vector3(-0.4f, 0.1f, 0.8f),
                Quaternion.Euler(0f, -31f, 0f) * firstPosition.rotation);
            motion.SetViewMode(SimulationViewMode.FirstPerson);
            motion.Reposition(secondPosition);
            var replacementPcMotor = motion.Motor;
            failStart = true;
            var failedSwitch = select(1);
            var failedSource = source;
            check(!failedSwitch && isActive() && motion.Running && motion.Mode == ControlledSessionMode.GameSimulation &&
                !vr.Running && !vr.Camera && manager.IsPlayerControlled && !replacementPcMotor &&
                keyboard.GetValue(tool) is KeyboardMotionInputSource && (int)inputMode.GetValue(tool) == 0 &&
                motion.ViewMode == SimulationViewMode.FirstPerson && (bool)captureRequested.GetValue(keyboard.GetValue(tool)) &&
                failedSource.Starts == 1 && failedSource.Disposals == 1 && !failedSource.Running && sameRoot(secondPosition) &&
                ((string)inputError.GetValue(tool))?.Contains("Injected VR startup failure.") == true,
                "A VR startup failure after releasing PC restores an active first-person PC backend and mouse-capture request at the same pose and retains the failure message");
            nativeXr.CheckUnchanged(check, "PC recovery after a failed injected VR handover");
            failStart = false;
            var retry = select(1);
            var retrySource = source;
            check(retry && isActive() && vr.Running && motion.Mode == ControlledSessionMode.VirtualReality &&
                !ReferenceEquals(retrySource, failedSource) && retrySource.Starts == 1 && failedSource.Disposals == 1 &&
                sameRoot(secondPosition) && string.IsNullOrEmpty((string)inputError.GetValue(tool)),
                "Switching can retry VR after a failed handover with a fresh provider while clearing the previous error");
            var beforeSameMode = creations;
            var retryCamera = vr.Camera;
            var sameMode = select(1);
            check(sameMode && isActive() && creations == beforeSameMode && ReferenceEquals(source, retrySource) &&
                retrySource.Disposals == 0 && vr.Camera == retryCamera && sameRoot(secondPosition),
                "Selecting the currently active VR input leaves the existing provider and camera intact");

            var continuous = true;
            for (var index = 0; index < 3; index++)
            {
                var previousSource = source;
                var previousCamera = vr.Camera;
                continuous &= select(0) && isActive() && motion.Mode == ControlledSessionMode.GameSimulation &&
                    previousSource.Disposals == 1 && !previousCamera && sameRoot(secondPosition);
                nativeXr.CheckUnchanged(check, "PC handover " + (index + 1) + " after injected VR");
                continuous &= select(1) && isActive() && vr.Running && motion.Mode == ControlledSessionMode.VirtualReality &&
                    source.Starts == 1 && source.Disposals == 0 && sameRoot(secondPosition);
            }
            check(continuous, "Repeated PC-to-VR and VR-to-PC handovers keep the simulation active and preserve its accumulated root pose");
            var lastVrSource = source;
            var lastVrCamera = vr.Camera;
            toggle.Invoke(tool, new object[] { module });
            check(!isActive() && !motion.Running && !vr.Running && !manager.IsPlayerControlled &&
                lastVrSource.Disposals == 1 && !lastVrCamera && keyboard.GetValue(tool) == null &&
                (int)inputMode.GetValue(tool) == 0 && sameRoot(originalRoot),
                "Normal exit after repeated backend handovers restores the original entry root and selects PC for the next entry");
            CheckRestored(check, module, observer, originalParameters, originalPose, originalRoot, avatar);

            var creationsBeforePcEntry = creations;
            toggle.Invoke(tool, new object[] { module });
            check(isActive() && motion.Mode == ControlledSessionMode.GameSimulation && !vr.Running &&
                creations == creationsBeforePcEntry && keyboard.GetValue(tool) is KeyboardMotionInputSource,
                "A fresh game-simulation entry after switching defaults to PC without constructing a VR provider");
            toggle.Invoke(tool, new object[] { module });
            tool.Dispose();
            tool = null;
            var disposedExactlyOnce = true;
            foreach (var created in sources) disposedExactlyOnce &= created.Disposals == 1 && !created.Running;
            check(disposedExactlyOnce, "Failed and successful VR providers are each disposed exactly once after switching, exit and tool disposal");
            CheckRestored(check, module, observer, originalParameters, originalPose, originalRoot, avatar);
            nativeXr.CheckUnchanged(check, "Repeated input handovers, exit and tool disposal");
        }
        finally
        {
            tool?.Dispose();
            descriptor.expressionsMenu = originalMenu;
            previewMode.Set(module, originalPreview);
            UnityEngine.Object.DestroyImmediate(menu);
            providers.Clear();
            providers.AddRange(registeredProviders);
        }
    }

    private sealed class NativeXrState
    {
        private readonly Dictionary<object, bool> _subsystems = new Dictionary<object, bool>();
        private readonly HashSet<int> _loaders = new HashSet<int>();
        internal bool Idle => _subsystems.Count == 0 && _loaders.Count == 0;

        internal NativeXrState()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays) _subsystems.Add(display, display.running);
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var input in inputs) _subsystems.Add(input, input.running);
            // Use the runtime type name so this fixture also compiles without optional VR packages.
            foreach (var candidate in Resources.FindObjectsOfTypeAll<ScriptableObject>())
                if (candidate.GetType().FullName == "VRCLearn.GestureManagerPro.SteamVR.Editor.SteamVrOwnedLoader")
                    _loaders.Add(candidate.GetInstanceID());
        }

        internal void CheckUnchanged(Action<bool, string> check, string operation)
        {
            var current = new NativeXrState();
            var unchanged = current._subsystems.Count == _subsystems.Count;
            foreach (var entry in _subsystems)
                unchanged &= current._subsystems.TryGetValue(entry.Key, out var running) && running == entry.Value;
            check(unchanged && _loaders.SetEquals(current._loaders),
                operation + " leaves native XR display/input instances, running states and GMP SteamVR loader instances unchanged");
        }
    }

    private static void CheckRestored(Action<bool, string> check, ModuleVrc3 module, Camera observer,
        Dictionary<string, float> original, float poseWeight, Pose root, Animator avatar)
    {
        var restored = true;
        foreach (var entry in original) restored &= Mathf.Abs(module.GetParam(entry.Key).FloatValue() - entry.Value) < 0.0001f;
        check(restored && module.GlobalHeadChopEnabled && Mathf.Abs(module.CalibrationPoseWeight - poseWeight) < 0.0001f,
            "Exit restores all eight owned VR parameters, the global head-chop flag and the previous calibration-pose weight");
        check(module.SimulationCamera == observer && observer.stereoTargetEye == StereoTargetEyeMask.Right &&
            Vector3.Distance(avatar.transform.position, root.position) < 0.0001f && Quaternion.Angle(avatar.transform.rotation, root.rotation) < 0.001f,
            "Exit restores the previous simulation camera, stereo-eye selection and original avatar root transform");
    }

    private static void Confirm(VrSimulationSession session, FakeVrInput source, ModuleVrc3 module)
    {
        Evaluate(module);
        source.Actions = default;
        session.Tick();
        source.Actions = new VrInputFrame(Vector2.zero, leftTrigger: 1f, rightTrigger: 1f);
        session.Tick();
        source.Actions = default;
        session.Tick();
    }

    private static void Evaluate(ModuleVrc3 module)
    {
        var graph = (PlayableGraph)typeof(ModuleVrc3).GetField("_playableGraph", Any).GetValue(module);
        if (graph.IsValid()) graph.Evaluate(0.016f);
    }

    private static TrackingFrame Frame(Dictionary<HumanBodyBones, Transform> bones, VRCAvatarDescriptor descriptor, bool fullBody)
    {
        var devices = new List<TrackedDevice>();
        for (var role = TrackingRole.Head; role <= (fullBody ? TrackingRole.RightElbow : TrackingRole.RightHand); role++)
        {
            var bone = bones[TrackingCalibration.BoneFor(role)];
            var position = role == TrackingRole.Head ? descriptor.transform.TransformPoint(descriptor.ViewPosition) : bone.position;
            devices.Add(new TrackedDevice("fixture:" + role, role, new Pose(position, bone.rotation), true, 1d));
        }
        return new TrackingFrame(devices, 1d);
    }

    private static TrackedDevice Find(TrackingFrame frame, TrackingRole role)
    {
        frame.TryGet(role, out var device);
        return device;
    }

    private static TrackingFrame Move(TrackingFrame frame, TrackingRole role, Vector3 delta, Quaternion? rotation = null)
    {
        var devices = new List<TrackedDevice>();
        foreach (var device in frame.Devices)
            devices.Add(new TrackedDevice(device.Id, device.Role,
                new Pose(device.Pose.position + (device.Role == role ? delta : Vector3.zero),
                    device.Role == role && rotation.HasValue ? rotation.Value : device.Pose.rotation), device.Valid, device.Timestamp + 1d));
        return new TrackingFrame(devices, frame.Timestamp + 1d, frame.SpaceId, frame.Origin);
    }

    private static TrackingFrame MoveDevice(TrackingFrame frame, string id, Vector3 delta)
    {
        var devices = new List<TrackedDevice>();
        foreach (var device in frame.Devices)
            devices.Add(new TrackedDevice(device.Id, device.Role,
                new Pose(device.Pose.position + (device.Id == id ? delta : Vector3.zero), device.Pose.rotation), device.Valid, device.Timestamp + 1d));
        return new TrackingFrame(devices, frame.Timestamp + 1d, frame.SpaceId, frame.Origin);
    }

    private static VRCAvatarDescriptor.CustomAnimLayer Layer(VRCAvatarDescriptor.AnimLayerType type) =>
        new VRCAvatarDescriptor.CustomAnimLayer { type = type, isDefault = true };

    private sealed class FakeVrInput : IVrInputSource
    {
        public TrackingFrame Tracking { get; set; } = TrackingFrame.Empty;
        public VrInputFrame Actions { get; set; }
        public MotionInputFrame Motion;
        public bool FailStart, ThrowOnPoll;
        public int Disposals, Starts;
        public bool Running { get; private set; }
        public bool InputAvailable { get; set; } = true;
        public string Status => "Injected VR lifecycle provider";
        public bool Start(out string error) { Starts++; error = FailStart ? "Injected VR startup failure." : null; Running = !FailStart; return Running; }
        public void Poll() { if (ThrowOnPoll) throw new InvalidOperationException("Injected VR tracking failure."); }
        public MotionInputFrame Sample() => Motion;
        public void Reset() => Motion = default;
        public void OpenBindings() { }
        public void Dispose() { Disposals++; Running = false; }
    }
}
