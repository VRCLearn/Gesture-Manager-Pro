using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Control;

/// <summary>Optional fixture checks. Invoke with the official OpenVR and Unity XR dependencies installed.</summary>
public static class UnityProSteamVrChecks
{
    public static void Run(Action<bool, string> check)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("VRCLearn.GestureManagerPro.SteamVR.Editor.SteamVrInputSource"))
            .FirstOrDefault(candidate => candidate != null);
        check(type != null, "The optional SteamVR assembly compiles against the original OpenVR XR dependency");
        if (type == null) return;
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var manifestPath = (string)type.GetMethod("ResolveActionManifest", flags).Invoke(null, null);
        check(Path.IsPathRooted(manifestPath) && File.Exists(manifestPath) &&
            File.Exists(Path.Combine(Path.GetDirectoryName(manifestPath), "bindings_oculus_touch.json")),
            "The bundled action manifest and adjacent bindings resolve from the main GMP installation");
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(type.Assembly);
        check(package == null
                ? AssetDatabase.GetAssetPath(Resources.Load<TextAsset>("GestureManagerPro/SteamVR/actions")).StartsWith("Assets/", StringComparison.Ordinal)
                : package.name == "com.vrclearn.gesture-manager-pro",
            "The SteamVR adapter and resources belong to the main GMP installation");
        var convert = type.GetMethod("ConvertPose", flags);
        var parse = type.GetMethod("ParseTrackerRole", flags);
        var matrixType = convert.GetParameters()[0].ParameterType;
        var matrix = Activator.CreateInstance(matrixType);
        Action<string, float> set = (field, value) => matrixType.GetField(field).SetValue(matrix, value);
        set("m0", 1f); set("m5", 1f); set("m10", 1f);
        set("m3", 1f); set("m7", 2f); set("m11", 3f);
        var pose = (Pose)convert.Invoke(null, new[] { matrix });
        check(Vector3.Distance(pose.position, new Vector3(1f, 2f, -3f)) < 0.00001f &&
            Quaternion.Angle(pose.rotation, Quaternion.identity) < 0.001f,
            "OpenVR translation reflects Z and preserves metres and identity orientation");
        set("m0", 0f); set("m2", 1f); set("m8", -1f); set("m10", 0f);
        pose = (Pose)convert.Invoke(null, new[] { matrix });
        check(Vector3.Distance(pose.rotation * Vector3.forward, Vector3.left) < 0.00001f,
            "OpenVR yaw converts its handedness rather than mirroring only position");
        matrix = Activator.CreateInstance(matrixType);
        pose = (Pose)convert.Invoke(null, new[] { matrix });
        check(!new TrackedDevice("invalid", TrackingRole.Head, pose, true, 1d).Valid,
            "An invalid zero tracking matrix cannot become a valid identity pose");

        var expected = new[] { TrackingRole.Hip, TrackingRole.LeftFoot, TrackingRole.RightFoot, TrackingRole.Chest,
            TrackingRole.LeftKnee, TrackingRole.RightKnee, TrackingRole.LeftElbow, TrackingRole.RightElbow,
            TrackingRole.LeftElbow, TrackingRole.RightElbow, TrackingRole.Unassigned };
        var roles = new[] { "Waist", "LeftFoot", "RightFoot", "Chest", "LeftKnee", "RightKnee", "LeftElbow", "RightElbow",
            "LeftShoulder", "RightShoulder", "Unknown" };
        var mapping = true;
        for (var index = 0; index < roles.Length; index++)
            mapping &= (TrackingRole)parse.Invoke(null, new object[] { "TrackerRole_" + roles[index] }) == expected[index];
        check(mapping, "SteamVR role mapping covers all eight supported body targets and preserves unknown roles");

        var gestureWeight = type.GetMethod("GestureWeight", flags);
        Func<int, float, float> weight = (gesture, trigger) => (float)gestureWeight.Invoke(null, new object[] { gesture, trigger });
        check(Mathf.Abs(weight(1, 0.2f) - 0.2f) < 0.00001f && Mathf.Abs(weight(1, 0.8f) - 0.8f) < 0.00001f &&
            weight(1, 0f) == 0f && weight(1, 1f) == 1f,
            "Fist gesture weight follows each hand's analog index trigger, including an unsqueezed trigger");
        var nonFist = true;
        foreach (var gesture in new[] { 0, 2, 3, 4, 5, 6, 7 })
            nonFist &= weight(gesture, 0f) == 1f && weight(gesture, 0.4f) == 1f;
        check(nonFist, "Non-fist gestures retain the existing full-weight callback behavior");
        check(weight(1, -0.5f) == 0f && weight(1, 1.5f) == 1f,
            "Analog Fist weights remain inside VRChat's zero-to-one parameter range");

        if (!Application.isPlaying)
        {
            EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>("Unity.XR.OpenVR.Settings", out var before);
            var source = (IVrInputSource)Activator.CreateInstance(type);
            var createActions = type.GetMethod("CreateActions", BindingFlags.Instance | BindingFlags.NonPublic);
            var analogFrame = (VrInputFrame)createActions.Invoke(source, new object[] { 0.2f, 0.8f, 0.9f, 0.1f, 1, 1 });
            check(Mathf.Abs(analogFrame.GestureLeftWeight - 0.2f) < 0.00001f &&
                Mathf.Abs(analogFrame.GestureRightWeight - 0.8f) < 0.00001f &&
                Mathf.Abs(analogFrame.LeftGrip - 0.9f) < 0.00001f && Mathf.Abs(analogFrame.RightGrip - 0.1f) < 0.00001f,
                "Production action frames use independent per-hand trigger weights rather than either grip value");
            CheckMixedTrackingAndControlLoss(type, source, createActions, check);
            check(!source.Start(out var error) && !string.IsNullOrEmpty(error) && !source.Running,
                "Starting outside Play Mode fails before owning or initializing XR resources");
            source.Reset(); source.Poll(); source.OpenBindings();
            check(!source.InputAvailable && source.Tracking.Devices.Count == 0 && source.Sample().Move == Vector2.zero &&
                !source.Actions.SelectPressed && !source.Actions.MenuPressed,
                "An unstarted source supplies no motion, tracking or action events");
            source.Dispose(); source.Dispose();
            check(!source.Start(out error) && !string.IsNullOrEmpty(error), "Disposed SteamVR sources reject restart and support idempotent disposal");
            EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>("Unity.XR.OpenVR.Settings", out var after);
            check(before == after, "Validation failures and disposal preserve the project's existing XR configuration");
            CheckSessionPreparation(type, check);
            CheckOwnedLoaderConfiguration(type, check);
            CheckTrackingSpaceEvents(type, check);
        }
    }

    private static void CheckSessionPreparation(Type sourceType, Action<bool, string> check)
    {
        const string key = "com.unity.xr.management.loader_settings";
        Func<string, Type> findType = name => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(candidate => candidate != null);
        var settingsType = findType("UnityEngine.XR.Management.XRGeneralSettings");
        var managerType = findType("UnityEngine.XR.Management.XRManagerSettings");
        var containerType = findType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
        var loaderType = sourceType.Assembly.GetType("VRCLearn.GestureManagerPro.SteamVR.Editor.SteamVrOwnedLoader").BaseType;
        var instance = settingsType.GetProperty("Instance");
        var initOnStart = settingsType.GetProperty("InitManagerOnStart");
        var setSettings = containerType.GetMethod("SetSettingsForBuildTarget");
        EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>(key, out var previous);
        var previousInstance = instance.GetValue(null);
        var container = ScriptableObject.CreateInstance(containerType);
        var standalone = ScriptableObject.CreateInstance(settingsType);
        var android = ScriptableObject.CreateInstance(settingsType);
        var manager = ScriptableObject.CreateInstance(managerType);
        var loader = ScriptableObject.CreateInstance(loaderType);
        var unrelated = new AnimationClip { frameRate = 60f };
        var assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ProSteamVrPreparationRegression.asset");
        var unrelatedPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ProSteamVrUnrelatedRegression.anim");
        try
        {
            AssetDatabase.CreateAsset(container, assetPath);
            foreach (var asset in new[] { standalone, android, manager, loader }) AssetDatabase.AddObjectToAsset(asset, container);
            settingsType.GetProperty("AssignedSettings").SetValue(standalone, manager);
            check((bool)managerType.GetMethod("TryAddLoader").Invoke(manager, new object[] { loader, -1 }),
                "The session preparation fixture includes a persisted project loader");
            setSettings.Invoke(container, new object[] { BuildTargetGroup.Standalone, standalone });
            setSettings.Invoke(container, new object[] { BuildTargetGroup.Android, android });
            EditorUtility.SetDirty(container);
            AssetDatabase.SaveAssetIfDirty(container);
            EditorBuildSettings.AddConfigObject(key, container, true);
            AssetDatabase.CreateAsset(unrelated, unrelatedPath);
            var unrelatedBytes = File.ReadAllBytes(unrelatedPath);
            unrelated.frameRate = 72f;
            EditorUtility.SetDirty(unrelated);

            var provider = VrInputProviders.Providers.First(candidate => candidate.Id == "steamvr");
            var count = VrInputProviders.Providers.Count;
            var androidJson = EditorJsonUtility.ToJson(android);
            var managerJson = EditorJsonUtility.ToJson(manager);
            var before = File.ReadAllBytes(assetPath);
            check(provider.Configure != null && provider.IsConfigured != null && !provider.IsConfigured() &&
                File.ReadAllBytes(assetPath).SequenceEqual(before),
                "SteamVR reports automatic standalone XR startup without changing or saving project settings");
            provider.Configure();
            var prepared = File.ReadAllBytes(assetPath);
            check(provider.IsConfigured() && !(bool)initOnStart.GetValue(standalone) &&
                !EditorUtility.IsDirty(standalone) && !prepared.SequenceEqual(before),
                "Explicit preparation disables and persists only standalone XR automatic startup");
            check((bool)initOnStart.GetValue(android) && EditorJsonUtility.ToJson(android) == androidJson &&
                EditorJsonUtility.ToJson(manager) == managerJson && VrInputProviders.Providers.Count == count,
                "Preparing SteamVR preserves Android XR settings, existing project loaders and provider registration");
            check(EditorUtility.IsDirty(unrelated) && Mathf.Approximately(unrelated.frameRate, 72f) &&
                File.ReadAllBytes(unrelatedPath).SequenceEqual(unrelatedBytes),
                "VR preparation leaves unrelated unsaved assets dirty and does not save them");
            provider.Configure();
            check(provider.IsConfigured() && File.ReadAllBytes(assetPath).SequenceEqual(prepared) &&
                !EditorUtility.IsDirty(standalone),
                "Repeated VR preparation is idempotent and does not rewrite an already prepared XR asset");
            setSettings.Invoke(container, new object[] { BuildTargetGroup.Standalone, null });
            provider.Configure();
            check(provider.IsConfigured() && File.ReadAllBytes(assetPath).SequenceEqual(prepared) &&
                EditorJsonUtility.ToJson(android) == androidJson,
                "A project without standalone XR settings is already ready for the owned loader and needs no new settings asset");
        }
        finally
        {
            EditorBuildSettings.RemoveConfigObject(key);
            if (previous) EditorBuildSettings.AddConfigObject(key, previous, true);
            instance.SetValue(null, previousInstance);
            AssetDatabase.DeleteAsset(unrelatedPath);
            AssetDatabase.DeleteAsset(assetPath);
            foreach (var asset in new UnityEngine.Object[] { unrelated, container, standalone, android, manager, loader })
                if (asset) UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    private static void CheckOwnedLoaderConfiguration(Type sourceType, Action<bool, string> check)
    {
        const string key = "Unity.XR.OpenVR.Settings";
        var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
        var loaderType = sourceType.Assembly.GetType("VRCLearn.GestureManagerPro.SteamVR.Editor.SteamVrOwnedLoader");
        var dependencyType = loaderType.BaseType;
        var settingsType = dependencyType.Assembly.GetType("Unity.XR.OpenVR.OpenVRSettings");
        var originalLayout = dependencyType.GetNestedType("UserDefinedSettings", BindingFlags.NonPublic);
        var layout = loaderType.GetNestedType("NativeSettings", BindingFlags.NonPublic);
        var originalFields = originalLayout.GetFields();
        var fields = layout.GetFields();
        var sameLayout = Marshal.SizeOf(originalLayout) == Marshal.SizeOf(layout) && originalFields.Length == fields.Length;
        foreach (var field in originalFields)
        {
            var candidate = layout.GetField(field.Name);
            sameLayout &= candidate != null && candidate.FieldType == field.FieldType &&
                Marshal.OffsetOf(originalLayout, field.Name) == Marshal.OffsetOf(layout, field.Name);
        }
        check(sameLayout, "The owned loader's configuration ABI exactly matches the original OpenVR XR 1.2.1 struct layout");

        EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>(key, out var previous);
        var assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ProSteamVrConfigRegression.asset");
        var settings = ScriptableObject.CreateInstance(settingsType);
        var loader = ScriptableObject.CreateInstance(loaderType);
        var source = (IVrInputSource)Activator.CreateInstance(sourceType);
        var configure = loaderType.GetMethod("Configure", flags);
        var create = loaderType.GetMethod("CreateConfiguration", flags);
        var manifest = (string)sourceType.GetMethod("ResolveActionManifest", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        try
        {
            settingsType.GetField("EditorAppKey").SetValue(settings, "application.pro.fixture.original");
            settingsType.GetField("ActionManifestFileRelativeFilePath").SetValue(settings, "Keep/Project/Manifest.json");
            settingsType.GetField("StereoRenderingMode").SetValue(settings,
                Enum.ToObject(settingsType.GetField("StereoRenderingMode").FieldType, 0));
            AssetDatabase.CreateAsset(settings, assetPath);
            EditorBuildSettings.AddConfigObject(key, settings, true);
            var beforeJson = EditorJsonUtility.ToJson(settings);
            var beforeBytes = File.ReadAllBytes(assetPath);
            var beforeDirty = EditorUtility.IsDirty(settings);
            var config = create.Invoke(null, new object[] { manifest });
            check((string)layout.GetField("editorAppKey").GetValue(config) == "application.vrclearn.gesturemanagerpro.steamvr" &&
                (string)layout.GetField("actionManifestPath").GetValue(config) == manifest &&
                (ushort)layout.GetField("initializationType").GetValue(config) == 1 &&
                (ushort)layout.GetField("stereoRenderingMode").GetValue(config) == 0,
                "Production configuration isolates the Pro app key and absolute manifest while retaining project stereo preferences");

            SteamVrConfigSaveProbe.SaveCalls = 0;
            SteamVrConfigSaveProbe.Watching = true;
            try
            {
                // Execute the real native configuration export used by startup, but
                // do not create XR subsystems or require a connected SteamVR headset.
                configure.Invoke(loader, new object[] { manifest });
                EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>(key, out var after);
                check(after == settings && EditorJsonUtility.ToJson(settings) == beforeJson &&
                    EditorUtility.IsDirty(settings) == beforeDirty && File.ReadAllBytes(assetPath).SequenceEqual(beforeBytes) &&
                    SteamVrConfigSaveProbe.SaveCalls == 0,
                    "Executing native startup configuration neither replaces the persisted object nor saves or changes its app key/manifest");
            }
            finally { SteamVrConfigSaveProbe.Watching = false; }

            EditorBuildSettings.RemoveConfigObject(key);
            SteamVrConfigSaveProbe.Watching = true;
            try
            {
                configure.Invoke(loader, new object[] { manifest });
                EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>(key, out var absent);
                check(absent == null && SteamVrConfigSaveProbe.SaveCalls == 0,
                    "Native startup configuration also succeeds with no registered OpenVR settings and creates no project config asset");
            }
            finally { SteamVrConfigSaveProbe.Watching = false; }

            sourceType.GetField("_loader", flags).SetValue(source, loader);
            var invalid = Path.Combine(Path.GetDirectoryName(manifest), "missing-fixture-manifest.json");
            var rejected = false;
            try { sourceType.GetMethod("InitializeOwnedLoader", flags).Invoke(source, new object[] { invalid }); }
            catch (TargetInvocationException exception) { rejected = exception.InnerException is FileNotFoundException; }
            var cleanup = sourceType.GetMethod("StopOwnedLoader", flags).Invoke(source, null);
            check(rejected && cleanup == null && sourceType.GetField("_loader", flags).GetValue(source) == null &&
                !(bool)sourceType.GetField("_initialized", flags).GetValue(source) && !loader && !source.Running,
                "A startup configuration failure destroys its partially owned loader and clears initialization ownership");
        }
        finally
        {
            SteamVrConfigSaveProbe.Watching = false;
            source.Dispose();
            if (loader) UnityEngine.Object.DestroyImmediate(loader);
            EditorBuildSettings.RemoveConfigObject(key);
            if (previous) EditorBuildSettings.AddConfigObject(key, previous, true);
            AssetDatabase.DeleteAsset(assetPath);
            if (settings) UnityEngine.Object.DestroyImmediate(settings);
        }
    }

    private static void CheckMixedTrackingAndControlLoss(Type type, IVrInputSource source, MethodInfo createActions, Action<bool, string> check)
    {
        var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
        var headOnly = new TrackingFrame(new[] {
            new TrackedDevice("native-head", TrackingRole.Head, new Pose(Vector3.up, Quaternion.identity), true, 1d)
        }, 1d, "steamvr-standing:0");
        var access = type.GetMethod("HasInputAccess", flags);
        check((bool)access.Invoke(null, new object[] { true, headOnly }) &&
            !(bool)access.Invoke(null, new object[] { false, headOnly }) &&
            !(bool)access.Invoke(null, new object[] { true, TrackingFrame.Empty }),
            "Native HMD input remains available for OSC hands while dashboard focus or lost HMD tracking still blocks input");
        type.GetProperty("Tracking").GetSetMethod(true).Invoke(source, new object[] { headOnly });
        var gesture = type.GetMethod("Gesture", flags);
        check((int)gesture.Invoke(source, new object[] { "left", 1f, 1f }) == 0 &&
            (int)gesture.Invoke(source, new object[] { "right", 1f, 1f }) == 0,
            "An absent native hand does not invent an open or fist controller gesture for OSC hands");

        var storeButton = type.GetMethod("StoreButton", flags);
        var digitalType = storeButton.GetParameters()[1].ParameterType;
        var digital = Activator.CreateInstance(digitalType);
        digitalType.GetField("bActive").SetValue(digital, true);
        digitalType.GetField("bState").SetValue(digital, true);
        digitalType.GetField("bChanged").SetValue(digital, true);
        storeButton.Invoke(source, new[] { (object)"select", digital, true });
        Func<VrInputFrame> read = () => (VrInputFrame)createActions.Invoke(source, new object[] { 0f, 0f, 0f, 0f, 0, 0 });
        check(read().SelectPressed, "A valid originating controller can press select");
        storeButton.Invoke(source, new[] { (object)"select", digital, false });
        check(!read().SelectPressed && read().SelectReleased,
            "Loss of the select action's tracked origin produces a release rather than a stuck press");
        storeButton.Invoke(source, new[] { (object)"select", digital, false });
        check(!read().SelectReleased, "Controller loss emits the synthetic release only once");

        var storeAxis = type.GetMethod("StoreAxis", flags);
        var analogType = storeAxis.GetParameters()[1].ParameterType;
        var analog = Activator.CreateInstance(analogType);
        analogType.GetField("bActive").SetValue(analog, true);
        analogType.GetField("x").SetValue(analog, 0.75f);
        var axis = type.GetMethod("Axis", flags);
        storeAxis.Invoke(source, new[] { (object)"left_trigger", analog, true });
        check(Mathf.Abs(((Vector2)axis.Invoke(source, new object[] { "left_trigger" })).x - 0.75f) < 0.00001f,
            "Analog controller state is retained while its originating device is tracked");
        storeAxis.Invoke(source, new[] { (object)"left_trigger", analog, false });
        check((Vector2)axis.Invoke(source, new object[] { "left_trigger" }) == Vector2.zero,
            "An invalid originating controller neutralizes its analog values independently of the HMD");
        source.Reset();
        type.GetProperty("Tracking").GetSetMethod(true).Invoke(source, new object[] { TrackingFrame.Empty });
    }

    private static void CheckTrackingSpaceEvents(Type sourceType, Action<bool, string> check)
    {
        var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
        var pumpType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("Unity.XR.OpenVR.OpenVREvents"))
            .First(candidate => candidate != null);
        var instanceField = pumpType.GetField("instance", flags);
        var enabledField = pumpType.GetField("enabled", flags);
        var previousPump = instanceField.GetValue(null);
        var previousEnabled = enabledField.GetValue(null);
        var source = (IVrInputSource)Activator.CreateInstance(sourceType);
        try
        {
            // A managed event-pump fixture: no native initialization or polling. Preserve
            // the original pump so these Editor checks cannot replace a later session's pump.
            pumpType.GetMethod("Initialize").Invoke(null, new object[] { false });
            var pump = instanceField.GetValue(null);
            var events = (Array)pumpType.GetField("events", flags).GetValue(pump);
            var subscribe = sourceType.GetMethod("SubscribeSpaceEvents", flags);
            var id = sourceType.GetProperty("TrackingSpaceId", flags);
            var revision = sourceType.GetField("_spaceRevision", flags);
            Action<int> emit = eventId => {
                var callback = events.GetValue(eventId);
                var invoke = callback.GetType().GetMethods().First(method => method.Name == "Invoke" && method.GetParameters().Length == 1);
                var argumentType = invoke.GetParameters()[0].ParameterType;
                var argument = Activator.CreateInstance(argumentType);
                argumentType.GetField("eventType").SetValue(argument, (uint)eventId);
                invoke.Invoke(callback, new[] { argument });
            };
            subscribe.Invoke(source, null);
            subscribe.Invoke(source, null);
            var initial = (string)id.GetValue(source);
            emit(808);
            check((ulong)revision.GetValue(source) == 1UL && (string)id.GetValue(source) != initial,
                "Standing-origin resets change the production tracking-space revision with exactly one subscription");
            emit(801); emit(807);
            check((ulong)revision.GetValue(source) == 3UL && source.Tracking.Devices.Count == 0 && !source.InputAvailable,
                "Universe changes and committed room setup invalidate old poses and request a fresh calibration space");
            emit(804);
            check((ulong)revision.GetValue(source) == 3UL, "A seated-origin reset does not invalidate the standing tracking space");

            sourceType.GetMethod("StopOwnedLoader", flags).Invoke(source, null);
            var subscriptions = (System.Collections.IList)sourceType.GetField("_spaceEventSubscriptions", flags).GetValue(source);
            var eventType = subscriptions.GetType().GetGenericArguments()[0];
            // Re-enable only the callback's membership guard after teardown so a leaked
            // UnityEvent listener cannot be hidden by clearing our bookkeeping list.
            subscriptions.Add(Enum.ToObject(eventType, 808));
            emit(808);
            check((ulong)revision.GetValue(source) == 3UL,
                "Startup unwind removes listeners before loader teardown even when the source is not disposed");
            subscriptions.Clear();
            subscribe.Invoke(source, null);
            source.Dispose();
            sourceType.GetField("_disposed", flags).SetValue(source, false);
            subscriptions.Add(Enum.ToObject(eventType, 801));
            emit(801);
            check((ulong)revision.GetValue(source) == 3UL,
                "Dispose physically removes event listeners rather than relying only on a disposed guard");
            subscriptions.Clear();
        }
        finally
        {
            source.Dispose();
            instanceField.SetValue(null, previousPump);
            enabledField.SetValue(null, previousEnabled);
        }
    }
}

public sealed class SteamVrConfigSaveProbe : AssetModificationProcessor
{
    internal static bool Watching;
    internal static int SaveCalls;

    private static string[] OnWillSaveAssets(string[] paths)
    {
        if (Watching) SaveCalls++;
        return paths;
    }
}
