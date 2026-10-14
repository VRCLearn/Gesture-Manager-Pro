using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;
using VRCLearn.GestureManagerPro.Diagnostics;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Modules;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

// Called by the Play Mode validation runner. This uses the actual initialization
// path and default controller resources, rather than manually populating Params.
public static class UnityProGraphChecks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly Dictionary<VRCAvatarDescriptor.AnimLayerType, string> Controllers =
        new Dictionary<VRCAvatarDescriptor.AnimLayerType, string>
        {
            { VRCAvatarDescriptor.AnimLayerType.Base, "GmgBaseLayer" },
            { VRCAvatarDescriptor.AnimLayerType.Additive, "GmgAdditiveLayer" },
            { VRCAvatarDescriptor.AnimLayerType.Gesture, "GmgGestureLayer" },
            { VRCAvatarDescriptor.AnimLayerType.Action, "GmgActionLayer" },
            { VRCAvatarDescriptor.AnimLayerType.FX, "GmgFxLayer" },
            { VRCAvatarDescriptor.AnimLayerType.Sitting, "GmgSittingLayer" },
            { VRCAvatarDescriptor.AnimLayerType.TPose, "GmgUtilityTPose" },
            { VRCAvatarDescriptor.AnimLayerType.IKPose, "GmgUtilityIKPose" }
        };

    public static void RunChecks(Action<bool, string> check)
    {
        check(EditorApplication.isPlaying && !EditorApplication.isPaused, "The real graph fixture runs in resumed Play Mode");
        ProValidationWindow host = null;
        GameObject avatar = null;
        GestureManager manager = null;
        Avatar genericAvatar = null;
        ControlledModeSession session = null;
        ModuleVrc3 module = null;
        try
        {
            host = ScriptableObject.CreateInstance<ProValidationWindow>();
            host.Show();
            var owner = typeof(EditorWindow).GetField("m_Parent", Any).GetValue(host) as ScriptableObject;
            typeof(GUIUtility).GetMethod("BeginContainerFromOwner", Any).Invoke(null, new object[] { owner });
            typeof(GUIUtility).GetMethod("BeginGUI", Any).Invoke(null, new object[] { 1, host.GetInstanceID(), 1 });
            typeof(EditorStyles).GetMethod("UpdateSkinCache", Any, null, new[] { typeof(int) }, null)
                .Invoke(null, new object[] { EditorGUIUtility.isProSkin ? 1 : 0 });

            avatar = new GameObject("Pro real graph validation avatar");
            var skeleton = new GameObject("Root");
            skeleton.transform.SetParent(avatar.transform, false);
            var animator = avatar.AddComponent<Animator>();
            genericAvatar = AvatarBuilder.BuildGenericAvatar(avatar, "Root");
            genericAvatar.hideFlags = HideFlags.HideAndDontSave;
            check(genericAvatar && genericAvatar.isValid && !genericAvatar.isHuman, "The fixture has a valid generic Avatar asset");
            animator.avatar = genericAvatar;
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = new Vector3(0f, 1.6f, 0f);
            descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
            descriptor.baseAnimationLayers = new[]
            {
                DefaultLayer(VRCAvatarDescriptor.AnimLayerType.Base),
                DefaultLayer(VRCAvatarDescriptor.AnimLayerType.Additive),
                DefaultLayer(VRCAvatarDescriptor.AnimLayerType.Gesture),
                DefaultLayer(VRCAvatarDescriptor.AnimLayerType.Action),
                DefaultLayer(VRCAvatarDescriptor.AnimLayerType.FX)
            };
            descriptor.specialAnimationLayers = new[]
            {
                DefaultLayer(VRCAvatarDescriptor.AnimLayerType.Sitting),
                DefaultLayer(VRCAvatarDescriptor.AnimLayerType.TPose),
                DefaultLayer(VRCAvatarDescriptor.AnimLayerType.IKPose)
            };
            avatar.transform.SetPositionAndRotation(new Vector3(10f, 2f, 3f), Quaternion.Euler(0f, 60f, 0f));
            var originalPosition = avatar.transform.position;
            var originalRotation = avatar.transform.rotation;
            manager = new GameObject("Pro real graph validation manager").AddComponent<GestureManager>();
            manager.settings = new ModuleSettings { vrMode = true };
            module = new ModuleVrc3(descriptor);
            check(module.IsValidDesc(), "A normal generic SDK3 descriptor is eligible for simulator initialization");
            manager.SetModule(module);
            check(ReferenceEquals(manager.Module, module) && module.Active && !module.IsInvalid(), "SetModule fully connects and initializes the simulator");
            check(GestureManager.ControlledAvatars.TryGetValue(avatar, out var connected) && ReferenceEquals(connected, module), "The actual Connect path registers avatar ownership");
            var previewIdentity = ModelStatisticsService.GetAvatarIdentity(avatar);
            var previews = ModelStatisticsService.History.Where(snapshot => snapshot.AvatarIdentity == previewIdentity &&
                snapshot.Source == ModelStatisticsSource.PlayModePreview).ToArray();
            check(previews.Any(snapshot => !snapshot.IsMobile) && previews.Any(snapshot => snapshot.IsMobile),
                "The actual simulator initialization captures PC and Mobile preview statistics before its changes");
            check(previews.All(snapshot => snapshot.BuildStatus == ModelStatisticsBuildStatus.NotApplicable && snapshot.BundleBytes < 0),
                "Real simulator previews remain distinct from confirmed SDK build results");
            check(module.Params.Count >= 30 && module.GetParam("Viseme") != null && module.GetParam("VelocityZ") != null, "Initialization creates the normal built-in parameter catalog");
            var graph = (PlayableGraph)typeof(ModuleVrc3).GetField("_playableGraph", Any).GetValue(module);
            check(graph.IsValid() && graph.IsPlaying() && graph.GetOutputCount() == 1, "Initialization creates a live avatar animation graph");
            var layers = (IDictionary)typeof(ModuleVrc3).GetField("_layers", Any).GetValue(module);
            check(layers.Count == Controllers.Count, "All eight default SDK3 animation layers are connected");
            foreach (DictionaryEntry entry in layers)
            {
                var playable = (AnimatorControllerPlayable)entry.Value.GetType().GetField("Playable", Any).GetValue(entry.Value);
                check(playable.IsValid(), "Default layer has a valid controller playable: " + entry.Key);
            }

            foreach (var entry in Controllers)
            {
                var controller = Resources.Load<AnimatorController>("GestureManagerPro/Vrc3/Controllers/" + entry.Value);
                check(controller && controller.layers.Length > 0, "Built-in controller resolves and contains states: " + entry.Key);
                var clips = controller.animationClips.Where(clip => clip).Distinct().ToArray();
                Debug.Log("[Pro real graph validation] " + entry.Key + " resolved clips: " + clips.Length);
                // Empty additive/FX controllers are valid default placeholders.
                // The remaining layers need their actual animation references.
                if (entry.Key != VRCAvatarDescriptor.AnimLayerType.Additive && entry.Key != VRCAvatarDescriptor.AnimLayerType.FX)
                    check(clips.Length > 0, "Built-in animation references resolve to real clips: " + entry.Key);
            }

            var originalVelocity = module.GetParam("VelocityZ").FloatValue();
            var originalVrMode = module.GetParam("VRMode").FloatValue();
            session = new ControlledModeSession();
            var input = new GraphMotionInput();
            check(session.Start(manager, input, 2f, 120f, false, false, false), "Controlled mode starts on the fully initialized graph: " + session.Error);
            input.Frame = new MotionInputFrame(Vector2.up, 1f);
            session.Motor.Step(0.1f);
            check(Vector3.Distance(avatar.transform.position, originalPosition) > 0.1f, "The real simulator graph accepts actual root movement");
            check(Mathf.Abs(module.GetParam("VelocityZ").FloatValue() - 2f) < 0.001f, "Motion publishes forward velocity through real playable subscriptions");
            check(Mathf.Abs(module.GetParam("AngularY").FloatValue() - 120f) < 0.01f, "Motion publishes turning through the fully initialized parameter catalog");
            check(module.GetParam("VRMode").FloatValue() == 0f && module.GetParam("TrackingType").FloatValue() == 1f, "The fully initialized generic fixture reports desktop tracking during control");
            graph.Evaluate(1f / 60f);
            check(graph.IsValid(), "The initialized graph remains valid after motion-driven evaluation");
            session.Stop();
            check(!manager.IsPlayerControlled && Vector3.Distance(avatar.transform.position, originalPosition) < 0.0001f &&
                Quaternion.Angle(avatar.transform.rotation, originalRotation) < 0.001f, "Stop restores the real initialized avatar and releases control");
            check(Mathf.Abs(module.GetParam("VelocityZ").FloatValue() - originalVelocity) < 0.001f &&
                Mathf.Abs(module.GetParam("VRMode").FloatValue() - originalVrMode) < 0.001f && manager.settings.vrMode,
                "Stop restores parameters and settings through the real simulator callbacks");
            manager.UnlinkModule();
            check(manager.Module == null && !module.Active && !GestureManager.ControlledAvatars.ContainsKey(avatar), "The actual Disconnect path releases the initialized graph and avatar registration");
            check(!graph.IsValid(), "Disconnect destroys the real avatar animation graph");
        }
        finally
        {
            session?.Dispose();
            if (manager)
            {
                if (ReferenceEquals(manager.Module, module) && module != null && module.GetParam("EyeHeightAsMeters") != null)
                    manager.UnlinkModule();
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
                GestureManager.ControlledAvatars.Remove(avatar);
                UnityEngine.Object.DestroyImmediate(avatar);
            }
            if (genericAvatar) UnityEngine.Object.DestroyImmediate(genericAvatar);
            if (host) UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static VRCAvatarDescriptor.CustomAnimLayer DefaultLayer(VRCAvatarDescriptor.AnimLayerType type) =>
        new VRCAvatarDescriptor.CustomAnimLayer { type = type, isDefault = true };

    private sealed class GraphMotionInput : IMotionInputSource
    {
        public MotionInputFrame Frame;
        public MotionInputFrame Sample() => Frame;
        public void Reset() => Frame = default;
    }
}
