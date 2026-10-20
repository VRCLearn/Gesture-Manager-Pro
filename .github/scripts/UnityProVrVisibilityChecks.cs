using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Modules;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Pose = UnityEngine.Pose;

// Exercises the production SDK head-chop path and local/observer camera callbacks on a real Humanoid.
// Optional GPU images prove head-only clipping; a physical headset is not simulated by these checks.
public static class UnityProVrVisibilityChecks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static void Run(Action<bool, string> check)
    {
        ProValidationWindow host = null;
        Animator avatar = null;
        Avatar human = null;
        GestureManager manager = null;
        ModuleVrc3 module = null;
        GameObject owner = null;
        Camera local = null, observer = null, unrelated = null;
        var materials = new List<Material>();
        try
        {
            host = ScriptableObject.CreateInstance<ProValidationWindow>();
            host.Show();
            var windowOwner = typeof(EditorWindow).GetField("m_Parent", Any).GetValue(host) as ScriptableObject;
            typeof(GUIUtility).GetMethod("BeginContainerFromOwner", Any).Invoke(null, new object[] { windowOwner });
            typeof(GUIUtility).GetMethod("BeginGUI", Any).Invoke(null, new object[] { 1, host.GetInstanceID(), 1 });
            typeof(EditorStyles).GetMethod("UpdateSkinCache", Any, null, new[] { typeof(int) }, null)
                .Invoke(null, new object[] { EditorGUIUtility.isProSkin ? 1 : 0 });

            avatar = UnityProTrackingChecks.CreateHumanoid(out human, out var bones);
            check(human && human.isHuman && human.isValid, "The VR visibility fixture builds a real valid Humanoid Avatar");
            if (!human || !human.isHuman || !human.isValid) return;
            var descriptor = avatar.gameObject.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = bones[HumanBodyBones.Head].position + new Vector3(0f, 0.04f, 0.06f);
            descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
            descriptor.baseAnimationLayers = new[] { Layer(VRCAvatarDescriptor.AnimLayerType.Base),
                Layer(VRCAvatarDescriptor.AnimLayerType.Additive), Layer(VRCAvatarDescriptor.AnimLayerType.Gesture),
                Layer(VRCAvatarDescriptor.AnimLayerType.Action), Layer(VRCAvatarDescriptor.AnimLayerType.FX) };
            descriptor.specialAnimationLayers = new[] { Layer(VRCAvatarDescriptor.AnimLayerType.Sitting),
                Layer(VRCAvatarDescriptor.AnimLayerType.TPose), Layer(VRCAvatarDescriptor.AnimLayerType.IKPose) };
            manager = new GameObject("Pro VR visibility manager").AddComponent<GestureManager>();
            manager.settings = new ModuleSettings { vrMode = true };
            module = new ModuleVrc3(descriptor);
            manager.SetModule(module);
            module.GetParam("VRMode").Set(module, true);
            module.SetGlobalHeadChop(false);
            check(module.Active && !module.IsInvalid(), "Visibility checks use the initialized simulator and its generated SDK head-chop component");

            var head = bones[HumanBodyBones.Head];
            head.localScale = new Vector3(0.8f, 1.2f, 0.9f);
            var headPosition = head.localPosition;
            var headScale = head.localScale;
            var body = bones[HumanBodyBones.Chest];
            var leftArm = bones[HumanBodyBones.LeftUpperArm];
            var rightArm = bones[HumanBodyBones.RightUpperArm];
            var bodyScale = body.localScale;
            var leftScale = leftArm.localScale;
            var rightScale = rightArm.localScale;
            local = MakeCamera("Pro local VR visibility camera");
            observer = MakeCamera("Pro observer VR visibility camera");
            unrelated = MakeCamera("Pro unmatched VR visibility camera");
            owner = new GameObject("Pro VR visibility callbacks");
            var visibility = owner.AddComponent<VrHeadVisibility>();
            visibility.LocalCamera = local;
            visibility.Apply = module.BeginLocalHeadRender;
            visibility.Restore = module.EndLocalHeadRender;

            visibility.Begin(local);
            check(Clipped(head, headScale), "The local camera invokes the real SDK head-chop rule only for the head");
            check(body.localScale == bodyScale && leftArm.localScale == leftScale && rightArm.localScale == rightScale,
                "Head clipping preserves chest and both upper-arm transforms instead of hiding the whole avatar");
            visibility.Begin(local);
            check(Clipped(head, headScale), "Duplicate built-in and SRP begin callbacks retain one local head-clipping scope");
            visibility.Begin(observer);
            check(Restored(head, headPosition, headScale), "A nested observer camera sees the full original head position and scale");
            visibility.Begin(local);
            check(Clipped(head, headScale), "A local camera nested inside an observer render reapplies head clipping");
            visibility.End(local);
            check(Restored(head, headPosition, headScale), "Ending a nested local camera restores the enclosing observer's complete head");
            visibility.End(observer);
            check(Clipped(head, headScale), "Ending the observer camera resumes the enclosing local camera's head clipping");
            visibility.End(local);
            check(Restored(head, headPosition, headScale), "Ending the outer local render restores nonuniform head scale and local position exactly");

            visibility.Begin(local);
            visibility.End(unrelated);
            check(Restored(head, headPosition, headScale), "An unmatched end callback fails closed by restoring the head and clearing stale render scopes");
            visibility.Begin(local);
            visibility.enabled = false;
            check(Restored(head, headPosition, headScale), "Disabling the visibility component during rendering restores the interrupted head clip");
            visibility.enabled = true;
            visibility.Begin(local);
            owner.SetActive(false);
            check(Restored(head, headPosition, headScale), "Deactivating the XR camera owner during rendering releases its head clip");
            owner.SetActive(true);

            // Module.LateUpdate resets leftover head factors even when a renderer exception omitted End.
            visibility.Begin(local);
            module.EndLocalHeadRender();
            visibility.Begin(local);
            check(Clipped(head, headScale), "A new local begin reapplies clipping after an interrupted render's factors were restored");
            visibility.End(local);

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Debug.Log("[Pro VR visibility] SKIP GPU images: no graphics device; transform and lifecycle checks still run.");
            else
            {
                var shader = Shader.Find("Unlit/Color");
                check(shader, "The VR visibility GPU fixture resolves its real unlit shader");
                var headMaterial = new Material(shader) { color = new Color(1f, 0.02f, 0.02f) };
                var bodyMaterial = new Material(shader) { color = new Color(0.02f, 0.15f, 1f) };
                var armMaterial = new Material(shader) { color = new Color(0.02f, 1f, 0.02f) };
                materials.AddRange(new[] { headMaterial, bodyMaterial, armMaterial });
                var headMesh = Primitive(head, new Vector3(0.28f, 0.28f, 0.28f), headMaterial);
                var bodyMesh = Primitive(body, new Vector3(0.45f, 0.5f, 0.25f), bodyMaterial);
                var leftMesh = Primitive(leftArm, new Vector3(0.26f, 0.1f, 0.15f), armMaterial);
                var rightMesh = Primitive(rightArm, new Vector3(0.26f, 0.1f, 0.15f), armMaterial);
                avatar.transform.position = new Vector3(2400f, 0f, 2400f);
                var target = avatar.transform.position + Vector3.up * 1.25f;
                foreach (var camera in new[] { local, observer })
                {
                    camera.transform.position = target - Vector3.forward * 3.5f;
                    camera.transform.LookAt(target);
                    camera.fieldOfView = 32f;
                }
                var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../ValidationOutput~/GestureManagerProValidation/VRVisibility"));
                Directory.CreateDirectory(output);
                var localPixels = Render(local, Path.Combine(output, "local-head-clipped.png"));
                check(localPixels.Head < 5 && localPixels.Body > 100 && localPixels.Arms > 60,
                    "A real local camera image clips the red head while preserving the blue body and green arms");
                check(Restored(head, headPosition, headScale) && new[] { headMesh, bodyMesh, leftMesh, rightMesh }.All(renderer =>
                    renderer.enabled && !renderer.forceRenderingOff), "The local GPU render restores the head and leaves every avatar renderer enabled");
                var observerPixels = Render(observer, Path.Combine(output, "observer-full-body.png"));
                check(observerPixels.Head > 40 && observerPixels.Body > 100 && observerPixels.Arms > 60,
                    "An independent observer camera image retains the full head, body and both arms");
                Debug.Log("[Pro VR visibility] Rendered local and observer images to " + output);
            }

            visibility.enabled = false;
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                CheckMenuStop(check, manager, module, avatar, bones, descriptor, headPosition, headScale);
            else Debug.Log("[Pro VR visibility] SKIP session callbacks outside resumed Play Mode.");
        }
        finally
        {
            if (owner) UnityEngine.Object.DestroyImmediate(owner);
            if (local) UnityEngine.Object.DestroyImmediate(local.gameObject);
            if (observer) UnityEngine.Object.DestroyImmediate(observer.gameObject);
            if (unrelated) UnityEngine.Object.DestroyImmediate(unrelated.gameObject);
            if (manager)
            {
                if (ReferenceEquals(manager.Module, module) && module != null && module.GetParam("EyeHeightAsMeters") != null) manager.UnlinkModule();
                else manager.Module = null;
                UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }
            if (avatar)
            {
                GestureManager.ControlledAvatars.Remove(avatar.gameObject);
                UnityEngine.Object.DestroyImmediate(avatar.gameObject);
            }
            if (human) UnityEngine.Object.DestroyImmediate(human);
            foreach (var material in materials) if (material) UnityEngine.Object.DestroyImmediate(material);
            if (host) UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CheckMenuStop(Action<bool, string> check, GestureManager manager, ModuleVrc3 module,
        Animator avatar, Dictionary<HumanBodyBones, Transform> bones, VRCAvatarDescriptor descriptor,
        Vector3 originalHeadPosition, Vector3 originalHeadScale)
    {
        using (var motion = new ControlledModeSession())
        using (var session = new VrSimulationSession(motion))
        {
            var devices = new List<TrackedDevice>();
            for (var role = TrackingRole.Head; role <= TrackingRole.RightHand; role++)
            {
                var bone = bones[TrackingCalibration.BoneFor(role)];
                var position = role == TrackingRole.Head ? descriptor.transform.TransformPoint(descriptor.ViewPosition) : bone.position;
                devices.Add(new TrackedDevice("visibility:" + role, role, new Pose(position, bone.rotation), true, 1d));
            }
            var source = new Input { Tracking = new TrackingFrame(devices, 1d) };
            check(session.Start(manager, source, new VrSimulationSettings { UserHeight = 0f, UserArmSpan = 0f }, collisions: false),
                "The real VR session starts for menu callback and interrupted-render teardown checks: " + session.Error);
            if (!session.Running) return;
            var menu = (VrExpressionMenuView)typeof(VrSimulationSession).GetField("_menu", Any).GetValue(session);
            menu.OpenSystemMenu();
            var headView = new Pose(session.Camera.transform.position, session.Camera.transform.rotation);
            var pauseAxis = UnityProVrMenuOscChecks.SystemAxis(menu, headView, "Pause");
            source.Actions = new VrInputFrame(pauseAxis, selectPressed: true);
            session.Tick();
            source.Actions = new VrInputFrame(pauseAxis, selectReleased: true, pausePressed: true);
            session.Tick();
            check(session.Running && session.Paused,
                "A real session menu and hardware Pause event in one frame pause once instead of immediately resuming");
            source.Actions = default;
            session.Tick();
            check(session.Paused, "A neutral frame after Pause preserves the session's paused state");
            menu.OpenSystemMenu();
            pauseAxis = UnityProVrMenuOscChecks.SystemAxis(menu, headView, "Resume");
            source.Actions = new VrInputFrame(pauseAxis, selectPressed: true);
            session.Tick();
            source.Actions = new VrInputFrame(pauseAxis, selectReleased: true);
            session.Tick();
            check(session.Running && !session.Paused, "The paused headset system menu can resume the actual session");

            menu.OpenSystemMenu();
            var exitAxis = UnityProVrMenuOscChecks.SystemAxis(menu, headView, "Exit VR");
            source.Actions = new VrInputFrame(exitAxis, selectPressed: true);
            session.Tick();
            var localCamera = session.Camera;
            var visibility = localCamera.GetComponent<VrHeadVisibility>();
            var head = bones[HumanBodyBones.Head];
            head.localPosition += new Vector3(0.025f, 0.01f, 0f);
            visibility.Begin(localCamera);
            source.Actions = new VrInputFrame(exitAxis, selectReleased: true);
            session.Tick();
            check(!session.Running && !motion.Running && !localCamera && source.Disposals == 1 && !menu.IsOpen,
                "Exiting through the headset menu disposes the view inside its callback and tears down the real session exactly once");
            check(Restored(head, originalHeadPosition, originalHeadScale),
                "Exit during an active local head clip restores the original solver head pose after releasing render factors");
        }
    }

    private static bool Clipped(Transform head, Vector3 originalScale) => head.localScale.sqrMagnitude < originalScale.sqrMagnitude * 0.001f;
    private static bool Restored(Transform head, Vector3 position, Vector3 scale) =>
        Vector3.Distance(head.localPosition, position) < 0.00001f && Vector3.Distance(head.localScale, scale) < 0.00001f;
    private static VRCAvatarDescriptor.CustomAnimLayer Layer(VRCAvatarDescriptor.AnimLayerType type) =>
        new VRCAvatarDescriptor.CustomAnimLayer { type = type, isDefault = true };

    private static Camera MakeCamera(string name)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.enabled = false;
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        return camera;
    }

    private static Renderer Primitive(Transform bone, Vector3 scale, Material material)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.transform.SetParent(bone, false);
        item.transform.localPosition = Vector3.zero;
        item.transform.localRotation = Quaternion.identity;
        item.transform.localScale = scale;
        UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
        var renderer = item.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    private static PixelCounts Render(Camera camera, string file)
    {
        var target = new RenderTexture(640, 480, 24);
        var image = new Texture2D(640, 480, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(file, image.EncodeToPNG());
            var counts = new PixelCounts();
            foreach (var pixel in image.GetPixels())
            {
                if (pixel.r > 0.6f && pixel.g < 0.3f && pixel.b < 0.3f) counts.Head++;
                if (pixel.b > 0.6f && pixel.r < 0.3f && pixel.g < 0.5f) counts.Body++;
                if (pixel.g > 0.6f && pixel.r < 0.3f && pixel.b < 0.3f) counts.Arms++;
            }
            return counts;
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    private struct PixelCounts { internal int Head, Body, Arms; }

    private sealed class Input : IVrInputSource
    {
        public TrackingFrame Tracking { get; set; }
        public VrInputFrame Actions { get; set; }
        public int Disposals;
        public bool Running { get; private set; }
        public bool InputAvailable => true;
        public string Status => "Injected visibility input";
        public bool Start(out string error) { error = null; Running = true; return true; }
        public void Poll() { }
        public MotionInputFrame Sample() => default;
        public void Reset() { }
        public void OpenBindings() { }
        public void Dispose() { Disposals++; Running = false; }
    }
}
