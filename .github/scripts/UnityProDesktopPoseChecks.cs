using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Modules;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Pose = UnityEngine.Pose;

// Deterministic desktop look checks with real Humanoid bones and the production pose-publish callback.
// These do not certify physical mouse input or equivalence with the VRChat client's complete IK solver.
[InitializeOnLoad]
public static class UnityProDesktopPoseChecks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string Pending = "VRCLearn.GestureManagerPro.Validation.DesktopPosePending";
    private const string Deadline = "VRCLearn.GestureManagerPro.Validation.DesktopPoseDeadline";

    static UnityProDesktopPoseChecks()
    {
        if (SessionState.GetBool(Pending, false)) EditorApplication.update += AwaitPlayMode;
    }

    public static void RunBatch()
    {
        SessionState.SetBool(Pending, true);
        SessionState.SetString(Deadline, DateTime.UtcNow.AddSeconds(90).Ticks.ToString());
        EditorApplication.update -= AwaitPlayMode;
        EditorApplication.update += AwaitPlayMode;
        if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
    }

    private static void AwaitPlayMode()
    {
        if (EditorApplication.isCompiling) return;
        if (!EditorApplication.isPlaying && long.TryParse(SessionState.GetString(Deadline, "0"), out var deadline) &&
            DateTime.UtcNow.Ticks < deadline) return;
        EditorApplication.update -= AwaitPlayMode;
        SessionState.EraseBool(Pending);
        SessionState.EraseString(Deadline);
        var exitCode = 1;
        var assertions = 0;
        try
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Desktop pose fixture Play Mode startup timed out.");
            Run((condition, message) =>
            {
                assertions++;
                if (!condition) throw new InvalidOperationException(message);
            });
            Debug.Log("[Pro desktop pose validation] PASS: " + assertions + " assertions.");
            exitCode = 0;
        }
        catch (Exception exception) { Debug.LogError("[Pro desktop pose validation] FAIL after " + assertions + " assertions: " + exception); }
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(exitCode);
    }

    public static void Run(Action<bool, string> check)
    {
        CheckHeadSolver(check);
        CheckSession(check);
    }

    private static void CheckHeadSolver(Action<bool, string> check)
    {
        foreach (var headBasis in new[] { Quaternion.identity, Quaternion.Euler(0f, 0f, 90f) })
        {
            Animator avatar = null;
            Avatar human = null;
            DesktopHeadPoseSolver solver = null;
            try
            {
                avatar = UnityProTrackingChecks.CreateHumanoid(out human, out var bones, headBasis);
                var head = bones[HumanBodyBones.Head];
                var chest = bones[HumanBodyBones.Chest];
                avatar.transform.rotation = Quaternion.Euler(8f, 67f, -4f);
                chest.localRotation = Quaternion.Euler(-20f, 25f, -15f);
                var animatedHead = head.localRotation = Quaternion.Euler(30f, -40f, 10f) * headBasis;
                var headPosition = head.position;
                var chestRotation = chest.localRotation;
                var handRotation = bones[HumanBodyBones.LeftHand].localRotation;
                solver = new DesktopHeadPoseSolver(avatar);
                check(Quaternion.Angle(head.localRotation, animatedHead) < 0.001f &&
                    Quaternion.Angle(chest.localRotation, chestRotation) < 0.001f,
                    "Humanoid look-axis calibration leaves the currently animated skeleton unchanged");

                // These gaze axes come from the fixture's authored bind orientation, not the solver's formula.
                var gazeForward = Quaternion.Inverse(headBasis) * Vector3.forward;
                var gazeUp = Quaternion.Inverse(headBasis) * Vector3.up;
                bool Facing(Quaternion view) => Vector3.Angle(head.TransformDirection(gazeForward), view * Vector3.forward) < 0.05f &&
                    Vector3.Angle(head.TransformDirection(gazeUp), view * Vector3.up) < 0.05f;
                var levelView = Quaternion.Euler(0f, 67f, 0f);
                solver.Solve(levelView);
                check(Facing(levelView),
                    "Head Tracking points along a level player view even when startup head and torso animation are tilted");
                var upView = Quaternion.Euler(-35f, 154f, 0f);
                solver.Solve(upView);
                check(Facing(upView), "Head Tracking follows absolute upward view yaw and pitch with imported head-bone axes");
                for (var frame = 0; frame < 200; frame++) solver.Solve(upView);
                check(Facing(upView) && Vector3.Distance(head.position, headPosition) < 0.0001f &&
                    Quaternion.Angle(chest.localRotation, chestRotation) < 0.001f &&
                    Quaternion.Angle(bones[HumanBodyBones.LeftHand].localRotation, handRotation) < 0.001f,
                    "Repeated head tracking has no drift, translation or unrelated body animation changes");
                solver.Release();
                check(Quaternion.Angle(head.localRotation, animatedHead) < 0.001f,
                    "Releasing head tracking immediately restores the latest head animation input");

                chest.localRotation = Quaternion.Euler(25f, -70f, 12f);
                animatedHead = head.localRotation = Quaternion.Euler(-12f, 21f, 65f) * headBasis;
                var downView = Quaternion.Euler(40f, -35f, 0f);
                solver.Solve(downView);
                check(Facing(downView), "Fresh torso and head animation do not add their angles to the tracked view direction");
                solver.Release();
                check(Quaternion.Angle(head.localRotation, animatedHead) < 0.001f,
                    "Switching Tracking back to Animation restores the newly evaluated head pose");
                solver.Solve(downView);
                animatedHead = head.localRotation = Quaternion.Euler(14f, 26f, -5f) * headBasis;
                var animatedHand = bones[HumanBodyBones.LeftHand].localRotation = Quaternion.Euler(20f, 15f, 10f);
                solver.Dispose();
                check(Quaternion.Angle(head.localRotation, animatedHead) < 0.001f &&
                    Quaternion.Angle(bones[HumanBodyBones.LeftHand].localRotation, animatedHand) < 0.001f,
                    "Disposal preserves head animation written after the last solve and unrelated body animation");
            }
            finally
            {
                solver?.Dispose();
                if (avatar) UnityEngine.Object.DestroyImmediate(avatar.gameObject);
                if (human) UnityEngine.Object.DestroyImmediate(human);
            }
        }
    }

    private static void CheckSession(Action<bool, string> check)
    {
        ProValidationWindow host = null;
        GestureManager manager = null;
        Animator avatar = null;
        Avatar human = null;
        ModuleVrc3 module = null;
        ControlledModeSession session = null;
        AnimatorController poseController = null;
        VRCAnimatorTemporaryPoseSpace initialPoseSpace = null;
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
            var head = bones[HumanBodyBones.Head];
            var descriptor = avatar.gameObject.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = head.position + new Vector3(0f, 0.04f, 0.06f);
            descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
            poseController = new AnimatorController();
            poseController.AddLayer("Base");
            var initialState = poseController.layers[0].stateMachine.AddState("Initial Pose Space");
            poseController.layers[0].stateMachine.defaultState = initialState;
            initialPoseSpace = initialState.AddStateMachineBehaviour<VRCAnimatorTemporaryPoseSpace>();
            initialPoseSpace.enterPoseSpace = true;
            initialPoseSpace.delayTime = 0f;
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Base,
                    isDefault = false, animatorController = poseController }, Layer(VRCAvatarDescriptor.AnimLayerType.Additive),
                Layer(VRCAvatarDescriptor.AnimLayerType.Gesture), Layer(VRCAvatarDescriptor.AnimLayerType.Action),
                Layer(VRCAvatarDescriptor.AnimLayerType.FX)
            };
            descriptor.specialAnimationLayers = new[] { Layer(VRCAvatarDescriptor.AnimLayerType.Sitting),
                Layer(VRCAvatarDescriptor.AnimLayerType.TPose), Layer(VRCAvatarDescriptor.AnimLayerType.IKPose) };
            manager = new GameObject("Pro desktop look manager").AddComponent<GestureManager>();
            manager.settings = new ModuleSettings();
            module = new ModuleVrc3(descriptor);
            manager.SetModule(module);
            var graph = (PlayableGraph)typeof(ModuleVrc3).GetField("_playableGraph", Any).GetValue(module);
            graph.Evaluate(0.016f);
            Debug.Log("[Pro initial Pose Space] active=" + module.IsPoseSpaceActive + " behaviours=" +
                avatar.GetBehaviours<VRCAnimatorTemporaryPoseSpace>().Length + " sourceSubscribed=" +
                (initialPoseSpace.ApplySettings != null));
            check(module.IsPoseSpaceActive,
                "A Pose Space behaviour in the actual initial Animator state reaches the module during initial graph evaluation");
            module.SetGlobalHeadChop(false);
            var input = new DesktopInput();
            session = new ControlledModeSession();
            check(session.Start(manager, input, collisions: false, mode: ControlledSessionMode.GameSimulation),
                "Desktop look starts through the real Humanoid game-simulation session: " + session.Error);
            if (!session.Running) return;

            void Look(float delta)
            {
                input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(0f, delta));
                session.Motor.Step(0.02f);
                input.Frame = default;
                module.LateUpdate();
            }
            Quaternion Expected(float pitch) => Quaternion.Euler(pitch, avatar.transform.eulerAngles.y, 0f);

            Look(-30f);
            check(Quaternion.Angle(head.rotation, Expected(-30f)) < 0.01f && head.forward.y > 0.4f,
                "PC mouse look drives the actual head upward at the simulator's pose-publication callback");
            module.LateUpdate();
            check(Quaternion.Angle(head.rotation, Expected(-30f)) < 0.01f,
                "Repeated module LateUpdate calls preserve the same desktop head pitch without drift");
            Look(60f);
            check(Quaternion.Angle(head.rotation, Expected(30f)) < 0.01f && head.forward.y < -0.4f,
                "PC mouse look drives the actual head downward as well as the preview camera");

            var tracking = (Dictionary<string, VRC_AnimatorTrackingControl.TrackingType>)module.TrackingStates;
            tracking["Head"] = VRC_AnimatorTrackingControl.TrackingType.Animation;
            var authoredHead = Quaternion.Euler(7f, 18f, 3f);
            head.localRotation = authoredHead;
            module.LateUpdate();
            check(Quaternion.Angle(head.localRotation, authoredHead) < 0.001f,
                "SDK Head Animation control preserves the authored head pose during desktop simulation");
            tracking["Head"] = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            module.LateUpdate();
            check(Quaternion.Angle(head.rotation, Expected(30f)) < 0.01f,
                "Returning SDK Head control to Tracking resumes desktop look");

            // The incoming animation deliberately equals the previous tracking output. Value
            // comparisons alone cannot distinguish this fresh pose from a stale solver result.
            Look(-30f);
            var neutralClip = new AnimationClip { name = "Pro explicit neutral head animation" };
            var neutralGraph = PlayableGraph.Create("Pro animation/tracking ownership collision");
            try
            {
                const string headPath = "Hips/Spine/Chest/Neck/Head";
                foreach (var axis in new[] { "x", "y", "z", "w" })
                    neutralClip.SetCurve(headPath, typeof(Transform), "m_LocalRotation." + axis,
                        AnimationCurve.Constant(0f, 1f, axis == "w" ? 1f : 0f));
                var playable = AnimationClipPlayable.Create(neutralGraph, neutralClip);
                AnimationPlayableOutput.Create(neutralGraph, "Neutral head", avatar).SetSourcePlayable(playable);
                neutralGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                neutralGraph.Play();
                module.Update();
                neutralGraph.Evaluate(0.02f);
                check(Quaternion.Angle(head.localRotation, Quaternion.identity) < 0.001f,
                    "An actual animation graph evaluates a fresh neutral head pose equal to the previous tracking output");
                tracking["Head"] = VRC_AnimatorTrackingControl.TrackingType.Animation;
                module.LateUpdate();
                check(Quaternion.Angle(head.localRotation, Quaternion.identity) < 0.001f,
                    "Animation ownership retains a fresh pose even when it numerically matches the previous solver output");
            }
            finally
            {
                if (neutralGraph.IsValid()) neutralGraph.Destroy();
                UnityEngine.Object.DestroyImmediate(neutralClip);
            }
            head.localRotation = authoredHead;
            tracking["Head"] = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            Look(30f);

            typeof(ModuleVrc3).GetField("LocomotionDisabled", Any).SetValue(module, true);
            var blockedRoot = new Pose(avatar.transform.position, avatar.transform.rotation);
            input.Frame = new MotionInputFrame(Vector2.up, 1f, new Vector2(20f, 10f));
            session.Motor.Step(0.02f);
            input.Frame = default;
            module.LateUpdate();
            check(Quaternion.Angle(head.rotation, Expected(40f)) < 0.01f &&
                Mathf.Abs(session.ViewCamera.Pitch - 40f) < 0.001f &&
                Vector3.Distance(avatar.transform.position, blockedRoot.position) < 0.0001f &&
                Quaternion.Angle(avatar.transform.rotation, blockedRoot.rotation) < 0.001f,
                "SDK Locomotion Control blocks movement and turning while retaining vertical camera and head look");
            typeof(ModuleVrc3).GetField("LocomotionDisabled", Any).SetValue(module, false);

            session.SetViewMode(SimulationViewMode.ThirdPerson);
            module.LateUpdate();
            typeof(ControlledModeSession).GetMethod("EnableFreeLook", Any).Invoke(session, null);
            Look(-70f);
            check(session.Motor.FreeLook && Mathf.Abs(session.Motor.ViewPitch + 30f) < 0.001f &&
                Quaternion.Angle(head.rotation, Expected(40f)) < 0.01f,
                "Third-person free rotation changes camera pitch while preserving the avatar's previous look direction");
            session.SetViewMode(SimulationViewMode.FirstPerson);
            Look(0f);
            check(!session.Motor.FreeLook && Quaternion.Angle(head.rotation, Expected(-30f)) < 0.01f,
                "Leaving free rotation resumes head look from the active player camera");
            session.Stop();
            check(Quaternion.Angle(head.localRotation, authoredHead) < 0.001f,
                "Stopping desktop simulation immediately removes its last head-look rotation before further animation");
            head.localRotation = authoredHead;
            module.LateUpdate();
            check(!session.Running && !manager.IsPlayerControlled && Quaternion.Angle(head.localRotation, authoredHead) < 0.001f,
                "Stopping desktop simulation removes the pose callback and releases head ownership");

            check(session.Start(manager, input, collisions: false, mode: ControlledSessionMode.GameSimulation),
                "A stopped desktop pose session can start again before a simulator graph reset: " + session.Error);
            CheckPoseSpace(check, module, session, input, bones, descriptor);
            Look(20f);
            check(Quaternion.Angle(head.localRotation, authoredHead) > 10f,
                "The restarted desktop session owns an active head-look rotation before reset");
            module.ResetAvatar();
            var callbacksReleased = true;
            foreach (var eventName in new[] { "BeforeAnimationEvaluation", "BeforePosePublish", "Resetting" })
            {
                var callback = typeof(ModuleVrc3).GetField(eventName, Any).GetValue(module) as Delegate;
                if (callback == null) continue;
                foreach (var subscriber in callback.GetInvocationList())
                    callbacksReleased &= !ReferenceEquals(subscriber.Target, session);
            }
            check(!session.Running && !manager.IsPlayerControlled && callbacksReleased,
                "ResetAvatar stops desktop pose ownership and removes animation, pose and graph-reset callbacks before rebuilding");
        }
        finally
        {
            session?.Dispose();
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
            if (human) UnityEngine.Object.DestroyImmediate(human);
            if (initialPoseSpace) UnityEngine.Object.DestroyImmediate(initialPoseSpace);
            if (poseController)
            {
                var machine = poseController.layers[0].stateMachine;
                foreach (var child in machine.states) UnityEngine.Object.DestroyImmediate(child.state);
                UnityEngine.Object.DestroyImmediate(machine);
                UnityEngine.Object.DestroyImmediate(poseController);
            }
            if (host) UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CheckPoseSpace(Action<bool, string> check, ModuleVrc3 module, ControlledModeSession session,
        DesktopInput input, Dictionary<HumanBodyBones, Transform> bones, VRCAvatarDescriptor descriptor)
    {
        var avatar = module.AvatarAnimator;
        var root = avatar.transform;
        var head = bones[HumanBodyBones.Head];
        var hips = bones[HumanBodyBones.Hips];
        var chest = bones[HumanBodyBones.Chest];
        var originalRoot = new Pose(root.position, root.rotation);
        var originalParent = root.parent;
        var originalScale = root.localScale;
        var originalHips = hips.localPosition;
        var originalChest = chest.localRotation;
        var originalHead = head.localRotation;
        var originalView = descriptor.ViewPosition;
        var referenceHead = new Vector3(0f, 1.8f, 0f); // The generated fixture's authored joint positions.
        var tracking = (Dictionary<string, VRC_AnimatorTrackingControl.TrackingType>)module.TrackingStates;
        var originalTracking = tracking["Head"];
        var control = ScriptableObject.CreateInstance<VRCAnimatorTemporaryPoseSpace>();
        GameObject parent = null;
        try
        {
            check(control.ApplySettings != null && Array.Exists(control.ApplySettings.GetInvocationList(),
                callback => ReferenceEquals(callback.Target, module)),
                "The real SDK Pose Space behaviour initializes the active module's ApplySettings subscription");
            void PublishView() { module.LateUpdate(); session.ViewCamera.ApplyView(); }
            void Enter()
            {
                control.enterPoseSpace = true;
                control.fixedDelay = true;
                control.delayTime = 0f;
                control.OnStateEnter(avatar, default, 0);
                PublishView();
            }
            void Exit()
            {
                control.enterPoseSpace = false;
                control.fixedDelay = true;
                control.delayTime = 0f;
                control.OnStateEnter(avatar, default, 0);
                PublishView();
            }
            Vector3 ExpectedView() => originalView + root.InverseTransformPoint(head.position) - referenceHead;
            bool CameraAt(Vector3 localView) => Vector3.Distance(session.ViewCamera.transform.position,
                descriptor.transform.TransformPoint(localView)) < 0.0002f;
            void Step(MotionInputFrame frame)
            {
                input.Frame = frame;
                session.Motor.Step(0.02f);
                input.Frame = default;
                PublishView();
            }

            hips.localPosition = originalHips + Vector3.down * 0.45f;
            chest.localRotation = Quaternion.Euler(15f, 25f, 10f);
            tracking["Head"] = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            var firstView = ExpectedView();
            Enter();
            check(module.IsPoseSpaceActive && !session.LocomotionBlocked &&
                Vector3.Distance(module.SimulationViewPosition, firstView) < 0.0002f && CameraAt(firstView) &&
                descriptor.ViewPosition == originalView,
                "SDK Pose Space snapshots the posed viewpoint without changing the descriptor or locking locomotion");
            Step(new MotionInputFrame(Vector2.zero, 0f, new Vector2(0f, -25f)));
            check(Vector3.Angle(head.forward, session.ViewCamera.transform.forward) < 0.05f,
                "Head Tracking continues to follow the player's view while Pose Space is active");
            hips.localPosition += Vector3.down * 0.15f;
            PublishView();
            check(Vector3.Distance(module.SimulationViewPosition, firstView) < 0.0002f && CameraAt(firstView),
                "Later animated head displacement does not continuously drag a captured Pose Space viewpoint");
            var secondView = ExpectedView();
            Enter();
            check(Vector3.Distance(module.SimulationViewPosition, secondView) < 0.0002f && CameraAt(secondView) &&
                Vector3.Distance(firstView, secondView) > 0.1f,
                "Entering Pose Space again refreshes the viewpoint even when it was already active");

            var beforeMove = new Pose(root.position, root.rotation);
            Step(new MotionInputFrame(Vector2.up, 1f, new Vector2(5f, 10f)));
            check(Vector3.Distance(root.position, beforeMove.position) > 0.0001f &&
                Quaternion.Angle(root.rotation, beforeMove.rotation) > 1f && CameraAt(secondView) &&
                Vector3.Angle(head.forward, session.ViewCamera.transform.forward) < 0.05f,
                "Pose Space alone permits movement, turning and look while its local viewpoint moves with the avatar");
            tracking["Head"] = VRC_AnimatorTrackingControl.TrackingType.Animation;
            var authoredHead = head.localRotation = Quaternion.Euler(25f, -18f, 5f);
            Step(new MotionInputFrame(Vector2.zero, 0f, new Vector2(0f, 10f)));
            check(Quaternion.Angle(head.localRotation, authoredHead) < 0.001f &&
                Mathf.Abs(session.ViewCamera.Pitch + 5f) < 0.001f && CameraAt(secondView),
                "Head Animation and Pose Space preserve authored head motion while vertical camera look remains available");

            typeof(ModuleVrc3).GetField("LocomotionDisabled", Any).SetValue(module, true);
            tracking["Head"] = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            var lockedRoot = new Pose(root.position, root.rotation);
            Step(new MotionInputFrame(Vector2.up, 1f, new Vector2(20f, 10f), jump: true, pose: MotionPose.Crouch));
            check(Vector3.Distance(root.position, lockedRoot.position) < 0.0001f &&
                Quaternion.Angle(root.rotation, lockedRoot.rotation) < 0.001f &&
                Mathf.Abs(session.ViewCamera.Pitch - 5f) < 0.001f &&
                Vector3.Angle(head.forward, session.ViewCamera.transform.forward) < 0.05f,
                "Combining Pose Space and SDK locomotion locking blocks displacement/yaw but keeps vertical view and head tracking");
            typeof(ModuleVrc3).GetField("LocomotionDisabled", Any).SetValue(module, false);

            parent = new GameObject("Pro Pose Space parent transform");
            parent.transform.SetPositionAndRotation(new Vector3(2f, 1f, -3f), Quaternion.Euler(8f, 31f, -6f));
            parent.transform.localScale = Vector3.one * 1.4f;
            root.SetParent(parent.transform, true);
            root.localScale = originalScale;
            PublishView();
            check(CameraAt(secondView) && Vector3.Distance(module.SimulationViewPosition, secondView) < 0.0002f,
                "A captured local viewpoint respects avatar scale and a translated/rotated/scaled parent");
            Exit();
            check(!module.IsPoseSpaceActive && module.SimulationViewPosition == originalView && CameraAt(originalView),
                "SDK Pose Space Exit immediately restores the normal descriptor viewpoint");

            control.enterPoseSpace = true;
            control.fixedDelay = true;
            control.delayTime = 10f;
            control.OnStateEnter(avatar, default, 0);
            PublishView();
            check(!module.IsPoseSpaceActive && CameraAt(originalView), "A fixed SDK delay does not relocate the view on state entry");
            typeof(VRC_AnimatorTemporaryPoseSpace).GetField("enterTime", Any).SetValue(control, Time.time - 11f);
            control.OnStateUpdate(avatar, default, 0);
            PublishView();
            var delayedView = module.SimulationViewPosition;
            check(module.IsPoseSpaceActive && CameraAt(delayedView), "The real SDK fixed-delay callback captures the view after its deadline");
            hips.localPosition += Vector3.down * 0.05f;
            control.OnStateUpdate(avatar, default, 0);
            PublishView();
            check(module.SimulationViewPosition == delayedView,
                "Repeated SDK state updates after the delay do not reapply an already executed Pose Space behaviour");
            Exit();

            AnimatorStateInfo StateAt(float normalizedTime)
            {
                object boxed = default(AnimatorStateInfo);
                typeof(AnimatorStateInfo).GetField("m_NormalizedTime", Any).SetValue(boxed, normalizedTime);
                return (AnimatorStateInfo)boxed;
            }
            control.enterPoseSpace = true;
            control.fixedDelay = false;
            control.delayTime = 0.5f;
            control.OnStateEnter(avatar, StateAt(0f), 0);
            control.OnStateUpdate(avatar, StateAt(0.25f), 0);
            PublishView();
            check(!module.IsPoseSpaceActive, "A proportional SDK delay waits for its normalized animation time");
            control.OnStateUpdate(avatar, StateAt(0.75f), 0);
            PublishView();
            check(module.IsPoseSpaceActive && CameraAt(module.SimulationViewPosition),
                "The real SDK normalized-time callback relocates the view after its proportional threshold");
            Exit();

            typeof(ModuleVrc3).GetField("PoseMode", Any).SetValue(module, true);
            var editorPitch = session.Motor.ViewPitch;
            Step(new MotionInputFrame(Vector2.zero, 0f, new Vector2(0f, 20f)));
            check(session.LocomotionBlocked && session.Motor.ViewPitch == editorPitch,
                "The editor's explicit Pose Mode remains a pose/input lock independently of SDK Pose Space");
            typeof(ModuleVrc3).GetField("PoseMode", Any).SetValue(module, false);
        }
        finally
        {
            typeof(ModuleVrc3).GetField("LocomotionDisabled", Any).SetValue(module, false);
            typeof(ModuleVrc3).GetField("PoseMode", Any).SetValue(module, false);
            control.enterPoseSpace = false;
            control.delayTime = 0f;
            control.OnStateEnter(avatar, default, 0);
            root.SetParent(originalParent, true);
            root.localScale = originalScale;
            session.Reposition(originalRoot);
            hips.localPosition = originalHips;
            chest.localRotation = originalChest;
            head.localRotation = originalHead;
            tracking["Head"] = originalTracking;
            input.Frame = new MotionInputFrame(Vector2.zero, 0f, new Vector2(0f, -session.Motor.ViewPitch));
            session.Motor.Step(0.02f);
            input.Frame = default;
            if (parent) UnityEngine.Object.DestroyImmediate(parent);
            UnityEngine.Object.DestroyImmediate(control);
        }
    }

    private static VRCAvatarDescriptor.CustomAnimLayer Layer(VRCAvatarDescriptor.AnimLayerType type) =>
        new VRCAvatarDescriptor.CustomAnimLayer { type = type, isDefault = true };

    private sealed class DesktopInput : IMotionInputSource
    {
        public MotionInputFrame Frame;
        public MotionInputFrame Sample() => Frame;
        public void Reset() => Frame = default;
    }
}
