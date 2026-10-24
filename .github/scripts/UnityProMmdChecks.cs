using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Modules;

// Runs inside the graph fixture's GUI and Play Mode context. All animation data
// authored here is transient test data, never a replacement for a user motion.
public static class UnityProMmdChecks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string Mouth = "Mouth & Jaw";

    public static void Run(ModuleVrc3 module, GestureManager manager, Action<bool, string> check)
    {
        CheckEmptyPreview(module, check);
        using (var fixture = new Fixture(check))
        {
            CheckLayersAndAnimation(fixture, check);
            CheckMouthTracking(fixture, check);
            CheckConflictsAndLifecycle(fixture, check);
        }
        CheckHumanoidAnimation(check);
        check(ReferenceEquals(manager.Module, module) && module.Active && !module.IsMmdPreviewActive,
            "MMD checks leave the caller's initialized avatar connected with no preview running");
    }

    private static void CheckEmptyPreview(ModuleVrc3 module, Action<bool, string> check)
    {
        var tool = Tool(module);
        var graph = Graph(module);
        var playableCount = graph.GetPlayableCount();
        var inStation = module.GetParam("InStation").FloatValue();
        var seated = module.GetParam("Seated").FloatValue();
        var avatarVersion = module.GetParam("AvatarVersion").FloatValue();
        var locomotion = module.IsLocomotionDisabled;
        var tracking = module.TrackingStates.ToDictionary(pair => pair.Key, pair => pair.Value);
        check(!Get<bool>(tool, "DisableGesture") && Get<bool>(tool, "DisableFxLayers") && Get<bool>(tool, "Loop"),
            "MMD defaults preserve Gesture, disable FX indices one and two, and enable looping");
        try
        {
            Set(tool, "Clip", null);
            for (var iteration = 0; iteration < 2; iteration++)
            {
                Call(tool, "Start", module);
                Call(tool, "Start", module);
                check(module.IsMmdPreviewActive && Graph(module).Equals(graph) && graph.GetPlayableCount() == playableCount,
                    "An empty MMD preview starts idempotently on the existing graph without a substitute animation");
                check(module.GetParam("InStation").BoolValue() && !module.GetParam("Seated").BoolValue() &&
                    module.GetParam("AvatarVersion").FloatValue() == avatarVersion && module.IsLocomotionDisabled &&
                    module.TrackingStates.Values.All(value => value == VRC_AnimatorTrackingControl.TrackingType.Animation),
                    "The non-seated MMD preset publishes station state and animation tracking without changing AvatarVersion");
                Call(tool, "Stop");
                Call(tool, "Stop");
                check(!module.IsMmdPreviewActive && graph.IsValid() && Graph(module).Equals(graph) &&
                    graph.GetPlayableCount() == playableCount && module.GetParam("InStation").FloatValue() == inStation &&
                    module.GetParam("Seated").FloatValue() == seated && module.IsLocomotionDisabled == locomotion &&
                    tracking.Count == module.TrackingStates.Count && tracking.All(pair => module.TrackingStates[pair.Key] == pair.Value),
                    "Repeated MMD stop restores the exact prior station, locomotion and per-part tracking state");
            }
        }
        finally { Call(tool, "Stop"); }
    }

    private static void CheckLayersAndAnimation(Fixture fixture, Action<bool, string> check)
    {
        var module = fixture.Module;
        var tool = Tool(module);
        var graph = Graph(module);
        var beforeCount = graph.GetPlayableCount();
        var fx = LayerPlayable(module, VRCAvatarDescriptor.AnimLayerType.FX);
        fx.SetLayerWeight(1, 0.67f);
        fx.SetLayerWeight(2, 0.43f);
        var fxZero = fx.GetLayerWeight(0);
        var weights = new Dictionary<VRCAvatarDescriptor.AnimLayerType, float>
        {
            { VRCAvatarDescriptor.AnimLayerType.Base, 0.83f },
            { VRCAvatarDescriptor.AnimLayerType.Additive, 0.41f },
            { VRCAvatarDescriptor.AnimLayerType.Gesture, 0.62f },
            { VRCAvatarDescriptor.AnimLayerType.Action, 0.29f }
        };
        foreach (var pair in weights) LayerWeight(module, pair.Key).Set(pair.Value);
        module.GetParam("Seated").Set(module, true);
        var originalControllers = fixture.Descriptor.baseAnimationLayers.Select(layer => layer.animatorController).ToArray();
        var serializedControllers = fixture.Controllers.Select(controller => EditorJsonUtility.ToJson(controller)).ToArray();
        var originalPosition = fixture.Motion.localPosition;
        fixture.Face.SetBlendShapeWeight(0, 25f);
        // Reading editor curves materializes Unity's binding/editor caches. Compare
        // complete serialization only after the same authored-data reads on both sides.
        var clipSource = ClipSource(fixture.Clip);
        var clipJson = EditorJsonUtility.ToJson(fixture.Clip);
        try
        {
            Set(tool, "Clip", fixture.Clip);
            Call(tool, "Start", module);
            check(Graph(module).Equals(graph) && graph.GetPlayableCount() == beforeCount + 1 &&
                fx.GetLayerWeight(0) == fxZero && fx.GetLayerWeight(1) == 0f && fx.GetLayerWeight(2) == 0f &&
                weights.All(pair => Mathf.Abs(LayerWeight(module, pair.Key).Weight - pair.Value) < 0.0001f),
                "MMD adds only its clip playable and disables FX indices one and two while preserving FX zero and all other requested layers");
            fx.SetLayerWeight(1, 0.9f);
            fx.SetLayerWeight(2, 0.8f);
            module.GetParam("InStation").Set(module, false);
            module.GetParam("Seated").Set(module, true);
            module.Update();
            check(fx.GetLayerWeight(1) == 0f && fx.GetLayerWeight(2) == 0f &&
                module.GetParam("InStation").BoolValue() && !module.GetParam("Seated").BoolValue() &&
                LayerWeight(module, VRCAvatarDescriptor.AnimLayerType.Sitting).Weight == 0f,
                "The normal module update reapplies selected station controls after other layer and parameter writes");
            graph.Evaluate(0.1f);
            Call(tool, "ApplyAnimationOverrides");
            var positionBefore = fixture.Motion.localPosition.x;
            var faceBefore = fixture.Face.GetBlendShapeWeight(0);
            graph.Evaluate(0.3f);
            Call(tool, "ApplyAnimationOverrides");
            check(fixture.Motion.localPosition.x > positionBefore + 0.1f && fixture.Face.GetBlendShapeWeight(0) > faceBefore + 5f,
                "A supplied MMD clip advances real transform and blendshape output through the live avatar graph");
            graph.Evaluate(2f);
            Call(tool, "ApplyAnimationOverrides");
            check(module.IsMmdPreviewActive && Graph(module).Equals(graph),
                "Looping an authored MMD clip keeps the original avatar graph running");
            Call(tool, "Stop");
            check(Vector3.Distance(fixture.Motion.localPosition, originalPosition) < 0.0001f &&
                Mathf.Abs(fixture.Face.GetBlendShapeWeight(0) - 25f) < 0.0001f && module.GetParam("Seated").BoolValue() &&
                Mathf.Abs(fx.GetLayerWeight(1) - 0.67f) < 0.0001f && Mathf.Abs(fx.GetLayerWeight(2) - 0.43f) < 0.0001f &&
                graph.GetPlayableCount() == beforeCount,
                "Stopping MMD restores animated transforms, blendshapes, seated state and original fractional FX weights");
            var clipSourceAfter = ClipSource(fixture.Clip);
            var clipUnchanged = RecordSerialization("generic-clip", clipJson, EditorJsonUtility.ToJson(fixture.Clip));
            var curvesUnchanged = RecordSerialization("generic-clip-source", clipSource, clipSourceAfter, ".txt");
            var controllerResults = fixture.Controllers.Select((controller, index) => RecordSerialization(
                "generic-controller-" + index, serializedControllers[index], EditorJsonUtility.ToJson(controller))).ToArray();
            var referencesUnchanged = originalControllers.SequenceEqual(
                fixture.Descriptor.baseAnimationLayers.Select(layer => layer.animatorController));
            Debug.Log("[Pro MMD immutability] generic clip=" + clipUnchanged + ", source curves=" + curvesUnchanged +
                ", controllers=" + string.Join(",", controllerResults) + ", descriptor references=" + referencesUnchanged);
            check(curvesUnchanged, "MMD playback preserves every authored clip curve, keyframe, event and clip setting");
            check(clipUnchanged, "MMD playback preserves the clip's complete serialized representation; inspect ValidationOutput~/Mmd/generic-clip diagnostics");
            check(referencesUnchanged, "MMD playback preserves the descriptor's original controller object references");
            for (var index = 0; index < controllerResults.Length; index++)
                check(controllerResults[index], "MMD playback preserves source controller serialization at index " + index);

            Set(tool, "Clip", null);
            Set(tool, "DisableGesture", true);
            Set(tool, "DisableFxLayers", false);
            Call(tool, "Start", module);
            check(LayerWeight(module, VRCAvatarDescriptor.AnimLayerType.Gesture).Weight == 0f &&
                Mathf.Abs(fx.GetLayerWeight(1) - 0.67f) < 0.0001f && Mathf.Abs(fx.GetLayerWeight(2) - 0.43f) < 0.0001f,
                "Gesture suppression is independently optional and disabling the FX option preserves both FX sublayers");
            Call(tool, "Stop");
            check(Mathf.Abs(LayerWeight(module, VRCAvatarDescriptor.AnimLayerType.Gesture).Weight - weights[VRCAvatarDescriptor.AnimLayerType.Gesture]) < 0.0001f,
                "The optional Gesture suppression restores its original fractional playable weight");

            Set(tool, "DisableGesture", false);
            Set(tool, "DisableFxLayers", true);
            Set(tool, "Loop", false);
            Set(tool, "Clip", fixture.EmptyClip);
            Call(tool, "Start", module);
            graph.Evaluate(0.1f);
            Call(tool, "ApplyAnimationOverrides");
            check(module.IsMmdPreviewActive && fixture.EmptyClip.empty && graph.GetPlayableCount() == beforeCount,
                "An explicitly assigned empty clip remains active without looping and creates no substitute animation playable");
            Call(tool, "Stop");

            Set(tool, "Clip", fixture.Clip);
            Set(tool, "Loop", false);
            Call(tool, "Start", module);
            // The first evaluation applies the newly connected playable's explicit time zero.
            graph.Evaluate(0f);
            graph.Evaluate(fixture.Clip.length + 0.1f);
            Call(tool, "ApplyAnimationOverrides");
            check(!module.IsMmdPreviewActive && graph.GetPlayableCount() == beforeCount,
                "A non-looping MMD clip releases its playable and restores state at its actual end");
            Set(tool, "Loop", true);

            Set(tool, "Clip", fixture.LegacyClip);
            check(Rejected(() => Call(tool, "Start", module)) && !module.IsMmdPreviewActive && Graph(module).Equals(graph),
                "A legacy clip is rejected before changing the active avatar graph");
            var source = Resources.Load<AnimatorController>("GestureManagerPro/Vrc3/Controllers/GmgBaseLayer");
            var humanoid = source.animationClips.FirstOrDefault(clip => clip && clip.humanMotion);
            check(humanoid, "The SDK controller supplies a real Humanoid clip for the incompatible-avatar check");
            Set(tool, "Clip", humanoid);
            check(Rejected(() => Call(tool, "Start", module)) && !module.IsMmdPreviewActive && !fixture.Animator.isHuman,
                "A Humanoid clip is rejected on a Generic avatar without substituting or retargeting its animation");
        }
        finally
        {
            Call(tool, "Stop");
            Set(tool, "Clip", null);
        }
    }

    private static void CheckMouthTracking(Fixture fixture, Action<bool, string> check)
    {
        var module = fixture.Module;
        var tool = Tool(module);
        Call(tool, "Start", module);
        try
        {
            module.GetParam("Voice").Set(module, 0.37f);
            fixture.Face.SetBlendShapeWeight(0, 57f);
            module.GetParam("Viseme").Set(module, 1);
            Call(module, "SetViseVisual", 0);
            check(Mathf.Abs(module.GetParam("Voice").FloatValue() - 0.37f) < 0.0001f &&
                Mathf.Abs(fixture.Face.GetBlendShapeWeight(0) - 57f) < 0.0001f,
                "Mouth and Jaw Animation leaves automatic Voice and viseme blendshapes under animation control");
            Tracking(module)[Mouth] = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            module.GetParam("Viseme").Set(module, 0);
            check(module.GetParam("Voice").FloatValue() == 0f && fixture.Face.GetBlendShapeWeight(0) == 100f,
                "Returning Mouth and Jaw to Tracking resumes the normal viseme callback without an MMD-specific bypass");
        }
        finally { Call(tool, "Stop"); }
    }

    private static void CheckHumanoidAnimation(Action<bool, string> check)
    {
        Animator animator = null;
        Avatar human = null;
        GestureManager manager = null;
        ModuleVrc3 module = null;
        HumanPoseHandler poseHandler = null;
        AnimationClip baseline = null;
        AnimationClip motion = null;
        var controllers = new List<AnimatorController>();
        try
        {
            animator = UnityProTrackingChecks.CreateHumanoid(out human, out var bones);
            check(human && human.isValid && human.isHuman && animator.isHuman,
                "The MMD muscle fixture uses Unity's valid Humanoid Avatar and actual mapped arm and leg bones");
            var muscleNames = HumanTrait.MuscleName;
            var armMuscle = Array.IndexOf(muscleNames, "Left Arm Down-Up");
            var legMuscle = Array.IndexOf(muscleNames, "Left Upper Leg Front-Back");
            check(armMuscle >= 0 && legMuscle >= 0,
                "Unity's muscle catalog resolves the explicitly authored MMD upper-arm and upper-leg channels");
            baseline = new AnimationClip { name = "Pro explicit original Humanoid pose", hideFlags = HideFlags.HideAndDontSave };
            motion = new AnimationClip { name = "Pro explicit Humanoid MMD muscle motion", hideFlags = HideFlags.HideAndDontSave };
            AnimationUtility.SetEditorCurve(baseline, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscleNames[armMuscle]),
                AnimationCurve.Constant(0f, 1f, -0.25f));
            AnimationUtility.SetEditorCurve(baseline, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscleNames[legMuscle]),
                AnimationCurve.Constant(0f, 1f, 0.2f));
            AnimationUtility.SetEditorCurve(motion, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscleNames[armMuscle]),
                AnimationCurve.Linear(0f, -0.6f, 1f, 0.6f));
            AnimationUtility.SetEditorCurve(motion, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscleNames[legMuscle]),
                AnimationCurve.Linear(0f, 0.6f, 1f, -0.6f));
            check(baseline.humanMotion && motion.humanMotion && motion.length == 1f &&
                AnimationUtility.GetCurveBindings(motion).All(binding => binding.type == typeof(Animator) && binding.path == string.Empty),
                "The authored MMD source is an actual Humanoid muscle clip without direct Transform animation curves");
            WarmClip(baseline, animator);
            WarmClip(motion, animator);
            var baselineSource = ClipSource(baseline);
            var motionSource = ClipSource(motion);
            var baselineJson = EditorJsonUtility.ToJson(baseline);
            var motionJson = EditorJsonUtility.ToJson(motion);
            var descriptor = animator.gameObject.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = bones[HumanBodyBones.Head].position + Vector3.forward * 0.05f;
            descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
            descriptor.baseAnimationLayers = new[]
            {
                MakeLayer(VRCAvatarDescriptor.AnimLayerType.Base, baseline),
                MakeLayer(VRCAvatarDescriptor.AnimLayerType.Additive),
                MakeLayer(VRCAvatarDescriptor.AnimLayerType.Gesture),
                MakeLayer(VRCAvatarDescriptor.AnimLayerType.Action),
                MakeLayer(VRCAvatarDescriptor.AnimLayerType.FX)
            };
            descriptor.specialAnimationLayers = new[]
            {
                MakeLayer(VRCAvatarDescriptor.AnimLayerType.Sitting),
                MakeLayer(VRCAvatarDescriptor.AnimLayerType.TPose),
                MakeLayer(VRCAvatarDescriptor.AnimLayerType.IKPose)
            };
            var controllerJson = controllers.Select(controller => EditorJsonUtility.ToJson(controller)).ToArray();
            var originalControllerReferences = descriptor.baseAnimationLayers.Select(layer => layer.animatorController).ToArray();
            manager = new GameObject("Pro Humanoid MMD fixture manager").AddComponent<GestureManager>();
            manager.settings = new ModuleSettings();
            module = new ModuleVrc3(descriptor);
            manager.SetModule(module);
            var graph = Graph(module);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            graph.Evaluate(0.05f);
            poseHandler = new HumanPoseHandler(human, animator.transform);
            var originalPose = new HumanPose();
            poseHandler.GetHumanPose(ref originalPose);
            var originalArm = originalPose.muscles[armMuscle];
            var originalLeg = originalPose.muscles[legMuscle];
            var arm = bones[HumanBodyBones.LeftUpperArm];
            var leg = bones[HumanBodyBones.LeftUpperLeg];
            var originalArmRotation = arm.localRotation;
            var originalLegRotation = leg.localRotation;
            var basePlayable = LayerPlayable(module, VRCAvatarDescriptor.AnimLayerType.Base);
            var playableCount = graph.GetPlayableCount();
            var tool = Tool(module);
            Set(tool, "Clip", motion);
            Set(tool, "DisableGesture", true);
            Call(tool, "Start", module);
            check(module.IsMmdPreviewActive && Graph(module).Equals(graph) && graph.GetPlayableCount() == playableCount + 1,
                "A valid Humanoid muscle clip starts through MMD's real station input on the existing avatar graph");
            graph.Evaluate(0.15f);
            Call(tool, "ApplyAnimationOverrides");
            var earlyPose = new HumanPose();
            poseHandler.GetHumanPose(ref earlyPose);
            var earlyArmRotation = arm.localRotation;
            var earlyLegRotation = leg.localRotation;
            graph.Evaluate(0.55f);
            Call(tool, "ApplyAnimationOverrides");
            var latePose = new HumanPose();
            poseHandler.GetHumanPose(ref latePose);
            check(latePose.muscles[armMuscle] - earlyPose.muscles[armMuscle] > 0.3f &&
                earlyPose.muscles[legMuscle] - latePose.muscles[legMuscle] > 0.3f,
                "Actual MMD graph evaluation advances the authored Humanoid arm and leg muscles in their opposite curve directions");
            check(Quaternion.Angle(earlyArmRotation, arm.localRotation) > 5f &&
                Quaternion.Angle(earlyLegRotation, leg.localRotation) > 5f,
                "Unity's Humanoid animation evaluation physically rotates both mapped bones without the test writing their transforms");
            Call(tool, "Stop");
            check(!module.IsMmdPreviewActive && Graph(module).Equals(graph) && graph.GetPlayableCount() == playableCount &&
                Quaternion.Angle(originalArmRotation, arm.localRotation) < 0.01f &&
                Quaternion.Angle(originalLegRotation, leg.localRotation) < 0.01f,
                "Stopping Humanoid MMD releases only its clip playable and restores the captured bone pose");
            graph.Evaluate(0.1f);
            var resumedPose = new HumanPose();
            poseHandler.GetHumanPose(ref resumedPose);
            check(Mathf.Abs(resumedPose.muscles[armMuscle] - originalArm) < 0.03f &&
                Mathf.Abs(resumedPose.muscles[legMuscle] - originalLeg) < 0.03f &&
                LayerPlayable(module, VRCAvatarDescriptor.AnimLayerType.Base).Equals(basePlayable),
                "The original Humanoid Base controller resumes its authored muscle output after MMD stops");
            var baselineSourceAfter = ClipSource(baseline);
            var motionSourceAfter = ClipSource(motion);
            var baselineUnchanged = RecordSerialization("humanoid-baseline", baselineJson, EditorJsonUtility.ToJson(baseline));
            var motionUnchanged = RecordSerialization("humanoid-motion", motionJson, EditorJsonUtility.ToJson(motion));
            var baselineCurvesUnchanged = RecordSerialization("humanoid-baseline-source", baselineSource, baselineSourceAfter, ".txt");
            var motionCurvesUnchanged = RecordSerialization("humanoid-motion-source", motionSource, motionSourceAfter, ".txt");
            var controllerResults = controllers.Select((controller, index) => RecordSerialization(
                "humanoid-controller-" + index, controllerJson[index], EditorJsonUtility.ToJson(controller))).ToArray();
            var referencesUnchanged = descriptor.baseAnimationLayers.Select(layer => layer.animatorController)
                .SequenceEqual(originalControllerReferences);
            Debug.Log("[Pro MMD immutability] humanoid baseline=" + baselineUnchanged + ", motion=" + motionUnchanged +
                ", source curves=" + baselineCurvesUnchanged + "/" + motionCurvesUnchanged +
                ", controllers=" + string.Join(",", controllerResults) + ", descriptor references=" + referencesUnchanged);
            check(baselineCurvesUnchanged && motionCurvesUnchanged,
                "Humanoid MMD preserves the authored source curves, keyframes, events and clip settings of both motions");
            check(baselineUnchanged, "Humanoid MMD preserves baseline clip serialization; inspect ValidationOutput~/Mmd/humanoid-baseline diagnostics");
            check(motionUnchanged, "Humanoid MMD preserves motion clip serialization; inspect ValidationOutput~/Mmd/humanoid-motion diagnostics");
            check(referencesUnchanged, "Humanoid MMD preserves descriptor controller object references");
            for (var index = 0; index < controllerResults.Length; index++)
                check(controllerResults[index], "Humanoid MMD preserves source controller serialization at index " + index);
        }
        finally
        {
            poseHandler?.Dispose();
            if (manager && ReferenceEquals(manager.Module, module)) manager.UnlinkModule();
            if (manager) UnityEngine.Object.DestroyImmediate(manager.gameObject);
            if (animator)
            {
                GestureManager.ControlledAvatars.Remove(animator.gameObject);
                UnityEngine.Object.DestroyImmediate(animator.gameObject);
            }
            foreach (var controller in controllers)
            {
                foreach (var layer in controller.layers)
                {
                    foreach (var state in layer.stateMachine.states) UnityEngine.Object.DestroyImmediate(state.state);
                    UnityEngine.Object.DestroyImmediate(layer.stateMachine);
                }
                UnityEngine.Object.DestroyImmediate(controller);
            }
            if (human) UnityEngine.Object.DestroyImmediate(human);
            if (baseline) UnityEngine.Object.DestroyImmediate(baseline);
            if (motion) UnityEngine.Object.DestroyImmediate(motion);
        }

        VRCAvatarDescriptor.CustomAnimLayer MakeLayer(VRCAvatarDescriptor.AnimLayerType type, AnimationClip clip = null)
        {
            var controller = new AnimatorController { name = "Pro Humanoid MMD fixture " + type, hideFlags = HideFlags.HideAndDontSave };
            controllers.Add(controller);
            controller.AddLayer("Fixture");
            var stateMachine = controller.layers[0].stateMachine;
            var state = stateMachine.AddState(clip ? "Authored Humanoid baseline" : "No authored animation");
            state.motion = clip;
            state.writeDefaultValues = false;
            stateMachine.defaultState = state;
            return new VRCAvatarDescriptor.CustomAnimLayer { type = type, isDefault = false, animatorController = controller };
        }
    }

    private static void CheckConflictsAndLifecycle(Fixture fixture, Action<bool, string> check)
    {
        var module = fixture.Module;
        var tool = Tool(module);
        using (var movement = new ControlledModeSession())
        {
            Call(tool, "Start", module);
            check(!movement.Start(fixture.Manager, new IdleInput(), collisions: false) && module.IsMmdPreviewActive,
                "Controlled movement cannot take ownership while MMD preview is active");
            Call(tool, "Stop");
            check(movement.Start(fixture.Manager, new IdleInput(), collisions: false),
                "The same initialized avatar accepts controlled movement after MMD stops");
            check(Rejected(() => Call(tool, "Start", module)) && movement.Running && !module.IsMmdPreviewActive,
                "MMD refuses an avatar already owned by controlled movement");
            movement.Stop();
        }
        var tools = Field(module, "AvatarTools");
        var pose = tools.GetType().GetProperty("PoseAvatar", Any).GetValue(tools);
        Call(tool, "Start", module);
        Call(pose, "Toggle", module);
        check(!module.IsMmdPreviewActive && module.IsPoseModeActive && Rejected(() => Call(tool, "Start", module)),
            "Entering Pose Avatar releases MMD, and an existing pose session blocks MMD entry");
        Call(pose, "Toggle", module);

        Call(tool, "Start", module);
        module.PlayCustomAnimation(fixture.Clip);
        check(!module.IsMmdPreviewActive && module.PlayingCustomAnimation && module.IsDummyModeActive &&
            Rejected(() => Call(tool, "Start", module)),
            "Test Animation releases MMD before entering its real dummy mode, which blocks MMD reentry");
        module.StopCustomAnimation();
        check(!module.IsDummyModeActive && !module.PlayingCustomAnimation && Graph(module).IsValid(),
            "Stopping Test Animation restores an initialized graph after the MMD conflict");

        Call(tool, "Start", module);
        Call(module, "EnableEditMode");
        check(!module.IsMmdPreviewActive && module.IsDummyModeActive && Rejected(() => Call(tool, "Start", module)),
            "An edit dummy releases MMD and prevents a second animation owner from starting");
        Call(Field(module, "DummyMode"), "StopExecution");

        Call(tool, "Start", module);
        var beforeReset = Graph(module);
        module.ResetAvatar();
        check(!module.IsMmdPreviewActive && !beforeReset.IsValid() && Graph(module).IsValid(),
            "Avatar reset stops MMD before destroying and recreating the avatar graph");
        Call(tool, "Start", module);
        var beforeUnlink = Graph(module);
        fixture.Manager.UnlinkModule();
        check(!module.IsMmdPreviewActive && !beforeUnlink.IsValid() && fixture.Manager.Module == null && !module.Active,
            "Unlink stops MMD and releases its graph, subscriptions and simulator ownership");
        Call(tool, "Stop");
    }

    private static void WarmClip(AnimationClip clip, Animator animator)
    {
        // Unity creates genericBindings lazily when a newly authored transient clip
        // first binds to an Animator. Establish that native cache independently of
        // the product graph before taking the strict complete-JSON baseline.
        var sourceBefore = ClipSource(clip);
        var transforms = animator.GetComponentsInChildren<Transform>(true).ToDictionary(transform => transform,
            transform => (position: transform.localPosition, rotation: transform.localRotation, scale: transform.localScale));
        var blendShapes = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(renderer => renderer.sharedMesh).ToDictionary(renderer => renderer,
                renderer => Enumerable.Range(0, renderer.sharedMesh.blendShapeCount).Select(renderer.GetBlendShapeWeight).ToArray());
        var graph = PlayableGraph.Create("Pro transient clip binding warmup");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(graph, "Transient clip binding", animator).SetSourcePlayable(playable);
            graph.Play();
            graph.Evaluate(0f);
        }
        finally
        {
            if (graph.IsValid()) graph.Destroy();
            foreach (var pair in transforms)
            {
                if (!pair.Key) continue;
                pair.Key.localPosition = pair.Value.position;
                pair.Key.localRotation = pair.Value.rotation;
                pair.Key.localScale = pair.Value.scale;
            }
            foreach (var pair in blendShapes)
                if (pair.Key)
                    for (var index = 0; index < pair.Value.Length; index++) pair.Key.SetBlendShapeWeight(index, pair.Value[index]);
        }
        if (!string.Equals(sourceBefore, ClipSource(clip), StringComparison.Ordinal))
            throw new InvalidOperationException("Transient clip binding warmup changed authored animation data: " + clip.name);
    }

    private static bool RecordSerialization(string name, string before, string after, string extension = ".json")
    {
        var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ValidationOutput~", "Mmd"));
        Directory.CreateDirectory(directory);
        var prefix = Path.Combine(directory, name);
        File.WriteAllText(prefix + ".before" + extension, before, new UTF8Encoding(false));
        File.WriteAllText(prefix + ".after" + extension, after, new UTF8Encoding(false));
        var equal = string.Equals(before, after, StringComparison.Ordinal);
        var firstDifference = 0;
        while (firstDifference < before.Length && firstDifference < after.Length && before[firstDifference] == after[firstDifference])
            firstDifference++;
        var summary = "Equal: " + equal + "\nBefore length: " + before.Length + "\nAfter length: " + after.Length +
            "\nFirst difference: " + firstDifference + "\n";
        File.WriteAllText(prefix + ".summary.txt", summary, new UTF8Encoding(false));
        Debug.Log("[Pro MMD immutability] " + name + ": " + summary.Replace('\n', ' ') + "Files: " + prefix);
        return equal;
    }

    private static string ClipSource(AnimationClip clip)
    {
        var result = new StringBuilder();
        result.AppendLine("Name=" + clip.name + "; legacy=" + clip.legacy + "; frameRate=" + Number(clip.frameRate) + "; wrap=" + clip.wrapMode);
        result.AppendLine("Settings=" + JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(clip)));
        foreach (var binding in AnimationUtility.GetCurveBindings(clip).OrderBy(binding => binding.path, StringComparer.Ordinal)
                     .ThenBy(binding => binding.type.FullName, StringComparer.Ordinal).ThenBy(binding => binding.propertyName, StringComparer.Ordinal))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            result.AppendLine("Curve=" + binding.path + ";" + binding.type.FullName + ";" + binding.propertyName +
                "; discrete=" + binding.isDiscreteCurve + "; wraps=" + curve.preWrapMode + "/" + curve.postWrapMode);
            for (var index = 0; index < curve.keys.Length; index++)
            {
                var key = curve.keys[index];
                result.AppendLine(string.Join(";", Number(key.time), Number(key.value), Number(key.inTangent), Number(key.outTangent),
                    Number(key.inWeight), Number(key.outWeight), key.weightedMode.ToString(),
                    AnimationUtility.GetKeyLeftTangentMode(curve, index).ToString(), AnimationUtility.GetKeyRightTangentMode(curve, index).ToString(),
                    AnimationUtility.GetKeyBroken(curve, index).ToString()));
            }
        }
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip).OrderBy(binding => binding.path, StringComparer.Ordinal)
                     .ThenBy(binding => binding.type.FullName, StringComparer.Ordinal).ThenBy(binding => binding.propertyName, StringComparer.Ordinal))
        {
            result.AppendLine("ObjectCurve=" + binding.path + ";" + binding.type.FullName + ";" + binding.propertyName);
            foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                result.AppendLine(Number(key.time) + ";" + (key.value ? key.value.GetInstanceID() : 0));
        }
        foreach (var animationEvent in AnimationUtility.GetAnimationEvents(clip))
            result.AppendLine("Event=" + Number(animationEvent.time) + ";" + animationEvent.functionName + ";" +
                Number(animationEvent.floatParameter) + ";" + animationEvent.intParameter + ";" + animationEvent.stringParameter + ";" +
                (animationEvent.objectReferenceParameter ? animationEvent.objectReferenceParameter.GetInstanceID() : 0) + ";" + animationEvent.messageOptions);
        return result.ToString();
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static object Tool(ModuleVrc3 module)
    {
        var tools = Field(module, "AvatarTools");
        return tools.GetType().GetProperty("MmdPreview", Any).GetValue(tools);
    }

    private static PlayableGraph Graph(ModuleVrc3 module) => (PlayableGraph)Field(module, "_playableGraph");
    private static Dictionary<string, VRC_AnimatorTrackingControl.TrackingType> Tracking(ModuleVrc3 module) =>
        (Dictionary<string, VRC_AnimatorTrackingControl.TrackingType>)Field(module, "TrackingControls");
    private static object Layer(ModuleVrc3 module, VRCAvatarDescriptor.AnimLayerType type) => ((IDictionary)Field(module, "_layers"))[type];
    private static AnimatorControllerPlayable LayerPlayable(ModuleVrc3 module, VRCAvatarDescriptor.AnimLayerType type) =>
        (AnimatorControllerPlayable)Field(Layer(module, type), "Playable");
    private static AnimatorControllerWeight LayerWeight(ModuleVrc3 module, VRCAvatarDescriptor.AnimLayerType type) =>
        (AnimatorControllerWeight)Field(Layer(module, type), "Weight");
    private static object Field(object target, string name) => target.GetType().GetField(name, Any).GetValue(target);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name, Any).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name, Any).SetValue(target, value);

    private static object Call(object target, string name, params object[] arguments)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var method = type.GetMethod(name, Any | BindingFlags.DeclaredOnly);
            if (method != null) return method.Invoke(target, arguments);
        }
        throw new MissingMethodException(target.GetType().FullName, name);
    }

    private static bool Rejected(Action start)
    {
        try { start(); return false; }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { return true; }
        catch (InvalidOperationException) { return true; }
    }

    private sealed class IdleInput : IMotionInputSource
    {
        public MotionInputFrame Sample() => default;
        public void Reset() { }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly GameObject Avatar;
        internal readonly Animator Animator;
        internal readonly VRCAvatarDescriptor Descriptor;
        internal readonly GestureManager Manager;
        internal readonly ModuleVrc3 Module;
        internal readonly Transform Motion;
        internal readonly SkinnedMeshRenderer Face;
        internal readonly AnimationClip Clip;
        internal readonly AnimationClip EmptyClip;
        internal readonly AnimationClip LegacyClip;
        internal readonly List<AnimatorController> Controllers = new List<AnimatorController>();
        private readonly Avatar _generic;
        private readonly Mesh _mesh;

        internal Fixture(Action<bool, string> check)
        {
            Avatar = new GameObject("Pro MMD animation fixture");
            var root = new GameObject("Root").transform;
            root.SetParent(Avatar.transform, false);
            Motion = new GameObject("MmdMotion").transform;
            Motion.SetParent(root, false);
            Motion.localPosition = new Vector3(0.2f, 0.3f, 0.4f);
            var faceObject = new GameObject("MmdFace");
            faceObject.transform.SetParent(root, false);
            Face = faceObject.AddComponent<SkinnedMeshRenderer>();
            _mesh = new Mesh { name = "Pro explicit MMD blendshape fixture", hideFlags = HideFlags.HideAndDontSave };
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.AddBlendShapeFrame("MmdFace", 100f, new[] { Vector3.forward, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
            _mesh.RecalculateBounds();
            Face.sharedMesh = _mesh;
            Face.updateWhenOffscreen = true;
            Animator = Avatar.AddComponent<Animator>();
            _generic = AvatarBuilder.BuildGenericAvatar(Avatar, "Root");
            _generic.hideFlags = HideFlags.HideAndDontSave;
            Animator.avatar = _generic;
            Descriptor = Avatar.AddComponent<VRCAvatarDescriptor>();
            Descriptor.ViewPosition = Vector3.up * 1.6f;
            Descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
            Descriptor.VisemeSkinnedMesh = Face;
            Descriptor.VisemeBlendShapes = new[] { "MmdFace" };
            Descriptor.baseAnimationLayers = new[]
            {
                CustomLayer(VRCAvatarDescriptor.AnimLayerType.Base),
                CustomLayer(VRCAvatarDescriptor.AnimLayerType.Additive),
                CustomLayer(VRCAvatarDescriptor.AnimLayerType.Gesture),
                CustomLayer(VRCAvatarDescriptor.AnimLayerType.Action),
                CustomLayer(VRCAvatarDescriptor.AnimLayerType.FX, 3)
            };
            Descriptor.specialAnimationLayers = new[]
            {
                CustomLayer(VRCAvatarDescriptor.AnimLayerType.Sitting),
                CustomLayer(VRCAvatarDescriptor.AnimLayerType.TPose),
                CustomLayer(VRCAvatarDescriptor.AnimLayerType.IKPose)
            };
            Clip = new AnimationClip { name = "Pro explicitly authored MMD motion", hideFlags = HideFlags.HideAndDontSave };
            Clip.SetCurve("Root/MmdMotion", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
            Clip.SetCurve("Root/MmdFace", typeof(SkinnedMeshRenderer), "blendShape.MmdFace", AnimationCurve.Linear(0f, 10f, 1f, 90f));
            EmptyClip = new AnimationClip { name = "Pro explicitly empty MMD motion", hideFlags = HideFlags.HideAndDontSave };
            LegacyClip = new AnimationClip { name = "Pro rejected legacy MMD motion", legacy = true, hideFlags = HideFlags.HideAndDontSave };
            WarmClip(Clip, Animator);
            Manager = new GameObject("Pro MMD fixture manager").AddComponent<GestureManager>();
            Manager.settings = new ModuleSettings();
            Module = new ModuleVrc3(Descriptor);
            Manager.SetModule(Module);
            check(Module.Active && !Module.IsInvalid() && Graph(Module).IsValid(),
                "The MMD fixture initializes a real Generic avatar with independent, non-writing controller states");
        }

        private VRCAvatarDescriptor.CustomAnimLayer CustomLayer(VRCAvatarDescriptor.AnimLayerType type, int count = 1)
        {
            var controller = new AnimatorController { name = "Pro MMD fixture " + type, hideFlags = HideFlags.HideAndDontSave };
            Controllers.Add(controller);
            for (var index = 0; index < count; index++)
            {
                controller.AddLayer("Fixture " + index);
                var stateMachine = controller.layers[index].stateMachine;
                var state = stateMachine.AddState("No authored animation");
                state.writeDefaultValues = false;
                stateMachine.defaultState = state;
            }
            return new VRCAvatarDescriptor.CustomAnimLayer { type = type, isDefault = false, animatorController = controller };
        }

        public void Dispose()
        {
            if (Manager && ReferenceEquals(Manager.Module, Module)) Manager.UnlinkModule();
            if (Manager) UnityEngine.Object.DestroyImmediate(Manager.gameObject);
            if (Avatar)
            {
                GestureManager.ControlledAvatars.Remove(Avatar);
                UnityEngine.Object.DestroyImmediate(Avatar);
            }
            foreach (var controller in Controllers)
            {
                foreach (var layer in controller.layers)
                {
                    foreach (var state in layer.stateMachine.states) UnityEngine.Object.DestroyImmediate(state.state);
                    UnityEngine.Object.DestroyImmediate(layer.stateMachine);
                }
                UnityEngine.Object.DestroyImmediate(controller);
            }
            if (_generic) UnityEngine.Object.DestroyImmediate(_generic);
            if (_mesh) UnityEngine.Object.DestroyImmediate(_mesh);
            if (Clip) UnityEngine.Object.DestroyImmediate(Clip);
            if (EmptyClip) UnityEngine.Object.DestroyImmediate(EmptyClip);
            if (LegacyClip) UnityEngine.Object.DestroyImmediate(LegacyClip);
        }
    }
}
