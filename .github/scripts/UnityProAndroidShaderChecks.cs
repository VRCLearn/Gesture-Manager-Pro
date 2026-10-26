using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Modules;

// Run joins the Editor checks, RunPlay joins Play checks, and RunModule uses the
// existing initialized graph fixture. RunBatch is a focused graphics invocation
// without -quit; it enters Play Mode and records the actual build target.
[InitializeOnLoad]
public static class UnityProAndroidShaderChecks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string TypeName = "VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools.AndroidShaderPreview";
    private const string PendingKey = "VRCLearn.GestureManagerPro.Validation.AndroidShaderPending";
    private const string DeadlineKey = "VRCLearn.GestureManagerPro.Validation.AndroidShaderDeadline";
    private const string FirstUpdateKey = "VRCLearn.GestureManagerPro.Validation.AndroidShaderFirstUpdate";
    private const string AllowedShader = "VRChat/Mobile/Toon Lit";
    private const string OutlineShader = "VRChat/Mobile/Toon Standard (Outline)";
    private const string MobileToonShader = "VRChat/Mobile/Toon Standard";
    private const string ErrorShader = "Hidden/InternalErrorShader";
    private static int _assertions;
    private static readonly Dictionary<ModuleVrc3, object> ModuleOwners = new Dictionary<ModuleVrc3, object>();

    static UnityProAndroidShaderChecks()
    {
        if (SessionState.GetBool(PendingKey, false)) EditorApplication.update += AwaitPlayMode;
    }

    public static void Run(Action<bool, string> check)
    {
        using (var fixture = new Fixture(check))
        {
            var android = false;
            var roots = new List<GameObject> { fixture.Root, fixture.Root, fixture.Illegal.gameObject, null };
            using (var preview = new Preview(() => roots, () => android))
            {
                check(preview.Enabled, "Android shader preview is enabled by default for its current simulator owner");
                preview.Refresh();
                check(preview.Blocked == 1 && preview.Outlines == 1 && preview.BlockedMaterials.SequenceEqual(new[] { fixture.IllegalSource }),
                    "Shader status counts unique original materials using the SDK allowlist, including the supported Outline exception");
                check(!preview.Active && fixture.IsRestored(), "A non-Android target scans shader status without changing renderer materials");
                preview.Begin(fixture.Camera);
                check(fixture.IsRestored(), "The platform guard prevents an injected desktop target from applying material overrides");
                preview.End(fixture.Camera);
                android = true;
                preview.Refresh();
                check(preview.Active == Application.isPlaying,
                    "An injected Android target requires Play Mode before preview becomes active");
                if (!Application.isPlaying)
                {
                    preview.Begin(fixture.Camera);
                    check(fixture.IsRestored(), "Edit Mode never installs Android preview materials even on an Android target");
                    preview.End(fixture.Camera);
                }
                preview.Enabled = false;
                check(!preview.Active && fixture.IsRestored(), "Disabling the preview keeps shader status separate from render ownership");
                preview.Refresh();
                check(preview.Blocked == 1 && preview.Outlines == 1, "Disabled preview still reports unsupported materials without changing them");
            }
            using (var preview = new Preview(() => new[] { fixture.Root }))
            {
                var expected = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android && Application.isPlaying;
                check(preview.Active == expected, "The default target guard follows the actual Unity Android build target: " + EditorUserBuildSettings.activeBuildTarget);
                preview.Begin(fixture.Camera);
                check(expected ? fixture.IsFiltered() : fixture.IsRestored(), "The actual build target controls material filtering without an injected target predicate");
                preview.End(fixture.Camera);
                check(fixture.IsRestored(), "The default target guard restores source slots after a render");
            }
            fixture.CheckSources(check);
        }
    }

    public static void RunPlay(Action<bool, string> check)
    {
        check(Application.isPlaying && !EditorApplication.isPaused, "Android rendering checks execute in resumed Play Mode");
        Run(check);
        CheckRenderOwnership(check);
        CheckAnimatedMaterials(check);
        CheckGpu(check);
    }

    private static void CheckRenderOwnership(Action<bool, string> check)
    {
        using (var fixture = new Fixture(check))
        {
            var android = true;
            var roots = new List<GameObject> { fixture.Root };
            using (var preview = new Preview(() => roots, () => android))
            {
                preview.Begin(fixture.Camera);
                check(fixture.IsFiltered(), "Android camera entry substitutes only illegal and Outline material slots");
                var error = fixture.Illegal.sharedMaterial;
                var outline = fixture.Outline.sharedMaterial;
                check(error && outline && error != fixture.IllegalSource && outline != fixture.OutlineSource &&
                    (error.hideFlags & HideFlags.DontSave) != 0 && (outline.hideFlags & HideFlags.DontSave) != 0,
                    "Preview materials are owned temporary objects rather than edited source materials");
                preview.Refresh();
                check(preview.Blocked == 1 && preview.Outlines == 1 && preview.BlockedMaterials.Single() == fixture.IllegalSource && fixture.IsFiltered(),
                    "Refreshing status during rendering resolves temporary proxies back to original materials");
                preview.Begin(fixture.SecondCamera);
                preview.End(fixture.SecondCamera);
                check(fixture.IsFiltered(), "Ending an inner camera leaves the outer camera's material overrides in place");
                preview.End(fixture.Camera);
                check(fixture.IsRestored(), "The outer camera restores every original material reference");
                fixture.CheckBlocks(check);
                fixture.OutlineSource.enableInstancing = false;
                fixture.OutlineSource.doubleSidedGI = false;
                preview.Begin(fixture.Camera);
                check(!fixture.Outline.sharedMaterial.enableInstancing && !fixture.Outline.sharedMaterial.doubleSidedGI,
                    "Reused Outline previews pick up the source material's current instancing and GI flags");
                preview.End(fixture.Camera);
                fixture.OutlineSource.enableInstancing = true;
                fixture.OutlineSource.doubleSidedGI = true;
                preview.Begin(fixture.Camera);
                check(fixture.Outline.sharedMaterial.enableInstancing && fixture.Outline.sharedMaterial.doubleSidedGI,
                    "The next render synchronizes changed Outline flags without replacing or editing the source asset");
                preview.End(fixture.Camera);

                preview.Begin(fixture.Camera);
                preview.Restore();
                check(fixture.IsRestored(), "Restore releases an interrupted camera render immediately");
                preview.End(fixture.Camera);
                check(fixture.IsRestored(), "A late camera callback cannot reapply an interrupted override");
                preview.Begin(fixture.Camera);
                preview.Enabled = false;
                check(!preview.Active && fixture.IsRestored(), "Disabling during rendering restores the source slots immediately");
                preview.End(fixture.Camera);
                preview.Enabled = true;
                preview.Begin(fixture.Camera);
                android = false;
                preview.Begin(fixture.SecondCamera);
                check(!preview.Active && fixture.IsRestored(), "A target change releases overrides before another camera can use them");
                preview.End(fixture.SecondCamera);
                preview.End(fixture.Camera);
                android = true;

                var late = fixture.AddRenderer("Late renderer", fixture.IllegalSource);
                preview.Begin(fixture.Camera);
                check(late.sharedMaterial && late.sharedMaterial.shader.name == ErrorShader,
                    "A renderer added after service construction is filtered at the next render");
                UnityEngine.Object.DestroyImmediate(late.gameObject);
                preview.End(fixture.Camera);
                check(fixture.IsRestored(), "Destroying a renderer inside a render does not interrupt restoration of surviving renderers");
                roots.Clear();
                preview.Begin(fixture.Camera);
                check(fixture.IsRestored(), "Render targets removed from the owner are no longer overridden");
                preview.End(fixture.Camera);
                roots.Add(fixture.Root);

                var paused = EditorApplication.isPaused;
                try
                {
                    preview.Begin(fixture.Camera);
                    EditorApplication.isPaused = true;
                    preview.Begin(fixture.SecondCamera);
                    check(preview.Active && fixture.IsFiltered(), "Paused Play Mode retains Android restrictions when a camera renders the stationary avatar");
                    preview.End(fixture.SecondCamera);
                    preview.End(fixture.Camera);
                    check(fixture.IsRestored(), "Paused camera rendering still restores source materials after its final callback");
                }
                finally { EditorApplication.isPaused = paused; preview.Restore(); }
                preview.Begin(fixture.Camera);
                check(fixture.IsFiltered(), "The same owner resumes normal filtering after interruption and pause");
                error = fixture.Illegal.sharedMaterial;
                outline = fixture.Outline.sharedMaterial;
                Call(preview.Value, "OnPlayModeChanged", PlayModeStateChange.ExitingPlayMode);
                check(fixture.IsRestored() && !error && !outline && preview.Blocked == 0 && preview.Outlines == 0 && Callbacks(preview.Value),
                    "The Play exit callback releases all owned materials and status while retaining a reusable owner subscription");
                preview.Begin(fixture.Camera);
                error = fixture.Illegal.sharedMaterial;
                outline = fixture.Outline.sharedMaterial;
                check(fixture.IsFiltered(), "An owner retained across Play sessions recreates valid temporary materials after Play exit cleanup");
                preview.Dispose();
                check(!preview.Active && fixture.IsRestored() && !error && !outline && !Callbacks(preview.Value),
                    "Disposing an owner during rendering restores slots, destroys its temporary materials and unregisters camera callbacks");
                preview.End(fixture.Camera);
                preview.Enabled = true;
                preview.Begin(fixture.SecondCamera);
                check(!preview.Active && fixture.IsRestored(), "Disposed previews ignore late callbacks and cannot be reactivated");
                preview.End(fixture.SecondCamera);
            }
            fixture.CheckBlocks(check);
            fixture.CheckSources(check);
        }
    }

    private static void CheckAnimatedMaterials(Action<bool, string> check)
    {
        using (var fixture = new Fixture(check))
        using (var preview = new Preview(() => new[] { fixture.Root }, () => true))
        {
            var clip = new AnimationClip { name = "Pro authored material swap", hideFlags = HideFlags.HideAndDontSave };
            var properties = new AnimationClip { name = "Pro authored shader property animation", hideFlags = HideFlags.HideAndDontSave };
            var animator = fixture.Root.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var candidates = AnimationUtility.GetAnimatableBindings(fixture.Illegal.gameObject, fixture.Root);
            var materialBindings = candidates.Where(candidate => candidate.propertyName == "m_Materials.Array.data[0]").ToArray();
            check(materialBindings.Length == 1, "Unity exposes the real renderer's material-slot animation binding: " +
                string.Join(", ", candidates.Where(candidate => candidate.propertyName.Contains("Material")).Select(candidate => candidate.type.Name + ":" + candidate.propertyName)));
            var binding = materialBindings[0];
            ClipSampler sampler = null;
            var keys = new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = fixture.AllowedSource },
                new ObjectReferenceKeyframe { time = 1f, value = fixture.IllegalSource }
            };
            try
            {
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
                foreach (var channel in new[] { "r", "g", "b" })
                    AnimationUtility.SetEditorCurve(properties,
                        EditorCurveBinding.FloatCurve(fixture.Illegal.gameObject.name, typeof(MeshRenderer), "material._Color." + channel),
                        AnimationCurve.Constant(0f, 1f, channel == "r" ? 0.9f : 0.05f));
                sampler = new ClipSampler(animator, clip, properties);
                sampler.Sample(0, 0d);
                AnimationDiagnostic(fixture, clip, properties, binding, "supported-slot");
                check(fixture.Illegal.sharedMaterial == fixture.AllowedSource,
                    "A real authored animation can select a supported material slot; actual=" + MaterialName(fixture.Illegal.sharedMaterial));
                preview.Begin(fixture.Camera);
                check(fixture.Illegal.sharedMaterial == fixture.AllowedSource && preview.Blocked == 0,
                    "Supported materials selected by animation stay as their original references");
                preview.End(fixture.Camera);
                sampler.Sample(0, 1d);
                AnimationDiagnostic(fixture, clip, properties, binding, "unsupported-slot");
                check(fixture.Illegal.sharedMaterial == fixture.IllegalSource,
                    "The authored animation subsequently selects its unsupported source material; actual=" + MaterialName(fixture.Illegal.sharedMaterial));
                // A shader-property animation owns its own animation property
                // block. The independent fixture covers preservation of user
                // renderer-wide and indexed property blocks during preview.
                fixture.Illegal.SetPropertyBlock(null);
                fixture.Illegal.SetPropertyBlock(null, 0);
                sampler.Sample(1, 0.5d);
                AnimationDiagnostic(fixture, clip, properties, binding, "shader-properties");
                var clipJson = SnapshotClip(clip);
                var propertiesJson = SnapshotClip(properties);
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    preview.Enabled = false;
                    var animated = fixture.Render(fixture.Illegal, "animated-properties-native.png");
                    check(Center(animated).Count(color => color.r > 0.65f && color.g < 0.3f && color.b < 0.3f) > 200,
                        "The real authored shader-property curves reach the GPU while the source material remains unchanged");
                    preview.Enabled = true;
                }
                preview.Begin(fixture.Camera);
                check(fixture.Illegal.sharedMaterial.shader.name == ErrorShader && preview.Blocked == 1,
                    "The next render filters an unsupported material selected by animation");
                preview.End(fixture.Camera);
                check(fixture.Illegal.sharedMaterial == fixture.IllegalSource,
                    "Render cleanup preserves the animation's current source material rather than restoring an obsolete slot");
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    check(Magenta(fixture.Render(fixture.Illegal, "animated-properties-preview.png")),
                        "Material property animation cannot replace the unsupported shader's pink preview output");
                    preview.Enabled = false;
                    var restored = fixture.Render(fixture.Illegal, "animated-properties-restored.png");
                    check(Center(restored).Count(color => color.r > 0.65f && color.g < 0.3f && color.b < 0.3f) > 200,
                        "Disabling preview restores the shader property animation's current GPU output");
                }
                var after = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                check(after.Length == keys.Length && after.Select(key => key.time).SequenceEqual(keys.Select(key => key.time)) &&
                    after.Select(key => key.value).SequenceEqual(keys.Select(key => key.value)),
                    "Shader preview leaves the authored animation's material keys unchanged");
                check(SnapshotClip(clip) == clipJson && SnapshotClip(properties) == propertiesJson,
                    "Shader preview preserves the complete compiled source clips and every authored shader-property curve");
                fixture.CheckSources(check);
            }
            finally { sampler?.Dispose(); UnityEngine.Object.DestroyImmediate(clip); UnityEngine.Object.DestroyImmediate(properties); }
        }
    }

    private static void CheckGpu(Action<bool, string> check)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Debug.Log("[Pro Android shaders] SKIP GPU pixels: no graphics device; ownership and target checks still run.");
            return;
        }
        using (var fixture = new Fixture(check))
        using (var preview = new Preview(() => new[] { fixture.Root }, () => true))
        {
            var previousMode = RenderSettings.ambientMode;
            var previousAmbient = RenderSettings.ambientLight;
            var previousIntensity = RenderSettings.ambientIntensity;
            var previousFog = RenderSettings.fog;
            Camera.CameraCallback inspect = null;
            Material expectedToon = null;
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white;
                RenderSettings.ambientIntensity = 1f;
                RenderSettings.fog = false;
                var lightObject = new GameObject("Pro Android shader directional light");
                lightObject.transform.SetParent(fixture.Root.transform, false);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1f;
                light.shadows = LightShadows.None;
                light.cullingMask = 1 << 30;
                var duringIllegal = string.Empty;
                var duringOutline = string.Empty;
                inspect = camera =>
                {
                    if (camera != fixture.Camera) return;
                    duringIllegal = fixture.Illegal.sharedMaterial.shader.name;
                    duringOutline = fixture.Outline.sharedMaterial.shader.name;
                };
                Camera.onPreRender += inspect;
                preview.Enabled = false;
                var nativeIllegal = fixture.Render(fixture.Illegal, "illegal-native.png");
                check(!Magenta(nativeIllegal), "The unsupported fixture shader renders its authored color before preview is enabled");
                var nativeAllowed = fixture.Render(fixture.Allowed, "allowed-native.png");
                check(Blue(nativeAllowed), "The SDK-allowed shader renders the renderer's blue texture property block");
                preview.Enabled = true;
                var blocked = fixture.Render(fixture.Illegal, "illegal-preview.png");
                check(Magenta(blocked) && duringIllegal == ErrorShader,
                    "A real camera render displays the unsupported material with Unity's pink error shader");
                check(fixture.IsRestored(), "A real camera's post-render callback restores original material references");
                var allowed = fixture.Render(fixture.Allowed, "allowed-preview.png");
                check(Blue(allowed) && Difference(nativeAllowed, allowed) < 0.02f,
                    "Allowed SDK shaders retain their real GPU output and per-renderer property block during preview");

                preview.Enabled = false;
                expectedToon = new Material(fixture.OutlineSource) { shader = Shader.Find(MobileToonShader), hideFlags = HideFlags.HideAndDontSave };
                fixture.Outline.sharedMaterial = expectedToon;
                var expected = fixture.Render(fixture.Outline, "outline-expected-mobile.png");
                fixture.Outline.sharedMaterial = fixture.OutlineSource;
                preview.Enabled = true;
                var actual = fixture.Render(fixture.Outline, "outline-preview.png");
                check(duringOutline == MobileToonShader && Center(actual).Any(color => color.maxColorComponent > 0.15f) && Difference(expected, actual) < 0.02f,
                    "Toon Standard Outline renders through the official non-outline mobile shader with the original material properties");
                check(fixture.IsRestored(), "Outline preview restores the source Outline shader after the camera finishes");
                fixture.CheckBlocks(check);
                fixture.CheckSources(check);
            }
            finally
            {
                if (inspect != null) Camera.onPreRender -= inspect;
                preview.Restore();
                fixture.Outline.sharedMaterial = fixture.OutlineSource;
                if (expectedToon) UnityEngine.Object.DestroyImmediate(expectedToon);
                RenderSettings.ambientMode = previousMode;
                RenderSettings.ambientLight = previousAmbient;
                RenderSettings.ambientIntensity = previousIntensity;
                RenderSettings.fog = previousFog;
            }
        }
    }

    public static void RunModule(ModuleVrc3 module, GestureManager manager, Action<bool, string> check)
    {
        check(Application.isPlaying && module.Active && ReferenceEquals(manager.Module, module),
            "Android shader ownership is checked on a real initialized simulator module");
        using (var fixture = new Fixture(check, module.Avatar.transform))
        {
            var preview = new Preview(Get(module, "AndroidShaders"));
            var enabled = preview.Enabled;
            var descriptorJson = EditorJsonUtility.ToJson(module.AvatarDescriptor, true);
            var settingsJson = JsonUtility.ToJson(manager.settings);
            var clip = new AnimationClip { name = "Pro Android shader dummy clip", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                preview.Enabled = true;
                preview.Refresh();
                ModuleOwners[module] = preview.Value;
                var android = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
                check(preview.Active == android, "The initialized module's default Android guard matches the actual active build target");
                check(Targets(module).Contains(module.Avatar) && preview.Blocked >= 1 && preview.Outlines >= 1,
                    "The simulator owner scans its real avatar rather than an unrelated scene renderer");
                preview.Begin(fixture.Camera);
                check(android ? fixture.IsFiltered() : fixture.IsRestored(), "The real module applies Android filtering only for an Android build");
                Call(module, "RestoreAndroidShaders");
                check(fixture.IsRestored(), "The module can release an interrupted camera's temporary materials before animation work");
                preview.End(fixture.Camera);

                Call(module, "SpawnClone", new ModuleSettings { isRemote = true });
                var clones = ((IEnumerable)Get(module, "_clones")).Cast<ModuleVrc3>().ToArray();
                check(clones.Length > 0, "The fixture creates an actual simulator remote clone");
                var clone = clones.Last();
                var remote = fixture.AddRenderer("Remote shader fixture", fixture.IllegalSource, clone.Avatar.transform);
                check(ReferenceEquals(Get(clone, "AndroidShaders"), preview.Value) && Targets(module).Contains(clone.Avatar),
                    "A remote clone shares its source owner's shader preview and participates in the live target set");
                preview.Begin(fixture.Camera);
                check(android ? remote.sharedMaterial.shader.name == ErrorShader : remote.sharedMaterial == fixture.IllegalSource,
                    "Remote clone materials obey the same actual-platform filtering guard as the local avatar");
                preview.End(fixture.Camera);
                check(remote.sharedMaterial == fixture.IllegalSource, "Camera cleanup restores a remote clone's original material reference");
                // The remote's independent animation graph belongs to this
                // fixture; release it after the remote rendering checks.
                Call(clone, "DestroyGraphs");

                preview.Begin(fixture.Camera);
                module.PlayCustomAnimation(clip);
                var dummy = Get(module, "DummyMode");
                var dummyAvatar = (GameObject)Get(dummy, "Avatar");
                var dummyRenderer = dummyAvatar.transform.Find(fixture.Root.name + "/" + fixture.Illegal.gameObject.name).GetComponent<Renderer>();
                check(module.IsDummyModeActive && Targets(module).Contains(dummyAvatar) && dummyRenderer.sharedMaterial == fixture.IllegalSource,
                    "Entering a real animation dummy restores source materials before cloning and includes the dummy in preview targets");
                preview.End(fixture.Camera);
                preview.Begin(fixture.Camera);
                check(android ? dummyRenderer.sharedMaterial.shader.name == ErrorShader : dummyRenderer.sharedMaterial == fixture.IllegalSource,
                    "The visible dummy avatar receives the same Android shader filtering as its source");
                preview.End(fixture.Camera);
                check(dummyRenderer.sharedMaterial == fixture.IllegalSource, "Dummy rendering restores the dummy's original source material slot");
                module.StopCustomAnimation();
                check(!module.IsDummyModeActive && module.Avatar.activeSelf && ReferenceEquals(Get(module, "AndroidShaders"), preview.Value),
                    "Leaving dummy mode keeps one source-owned shader preview and restores the normal avatar");
                preview.Begin(fixture.Camera);
                module.ResetAvatar();
                check(fixture.IsRestored() && ReferenceEquals(Get(module, "AndroidShaders"), preview.Value),
                    "Reset restores any interrupted render before rebuilding the avatar graph without duplicating the preview owner");
                preview.End(fixture.Camera);
                check(EditorJsonUtility.ToJson(module.AvatarDescriptor, true) == descriptorJson && JsonUtility.ToJson(manager.settings) == settingsJson,
                    "Android preview, remote rendering and dummy/reset transitions preserve avatar metadata and simulator settings");
                fixture.CheckSources(check);
            }
            finally
            {
                preview.Restore();
                if (module.IsDummyModeActive) module.StopCustomAnimation();
                foreach (var clone in ((IEnumerable)Get(module, "_clones")).Cast<ModuleVrc3>()) Call(clone, "DestroyGraphs");
                Call(module, "RemoveClones");
                preview.Enabled = enabled;
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }
    }

    public static void CheckUnlinked(ModuleVrc3 module, Action<bool, string> check)
    {
        if (!ModuleOwners.TryGetValue(module, out var owner)) throw new InvalidOperationException("The initialized module's Android owner was not recorded.");
        ModuleOwners.Remove(module);
        check(Get(module, "_androidShaders") == null && !new Preview(owner).Active && !Callbacks(owner),
            "Unlink clears the simulator's Android preview owner and unregisters its camera callbacks");
    }

    public static void RunBatch()
    {
        SessionState.SetBool(PendingKey, true);
        SessionState.SetBool(FirstUpdateKey, false);
        SessionState.SetString(DeadlineKey, DateTime.UtcNow.AddSeconds(120).Ticks.ToString());
        EditorApplication.update -= AwaitPlayMode;
        EditorApplication.update += AwaitPlayMode;
        if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
    }

    private static void AwaitPlayMode()
    {
        if (!SessionState.GetBool(PendingKey, false)) { EditorApplication.update -= AwaitPlayMode; return; }
        if (long.TryParse(SessionState.GetString(DeadlineKey, "0"), out var deadline) && DateTime.UtcNow.Ticks > deadline)
        {
            Finish(new TimeoutException("Android shader focused checks could not enter Play Mode within 120 seconds."));
            return;
        }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (!SessionState.GetBool(FirstUpdateKey, false)) { SessionState.SetBool(FirstUpdateKey, true); return; }
        EditorApplication.update -= AwaitPlayMode;
        _assertions = 0;
        try
        {
            Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Focused Android shader verification requires a real graphics device");
            RunPlay(Check);
            CheckFocusedModule(Check);
            Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void Finish(Exception exception)
    {
        EditorApplication.update -= AwaitPlayMode;
        SessionState.SetBool(PendingKey, false);
        SessionState.EraseString(DeadlineKey);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "focused.json"), JsonUtility.ToJson(new Result
        {
            passed = exception == null,
            assertions = _assertions,
            buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            androidTarget = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android,
            graphics = SystemInfo.graphicsDeviceType.ToString(),
            error = exception?.ToString()
        }, true), new UTF8Encoding(false));
        if (exception == null) Debug.Log("[Pro Android shaders] PASS: " + _assertions + " assertions; actual target " + EditorUserBuildSettings.activeBuildTarget + ". Images: " + Output);
        else Debug.LogError("[Pro Android shaders] FAIL after " + _assertions + " assertions: " + exception);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.Exit(exception == null ? 0 : 1);
    }

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../ValidationOutput~/AndroidShaders"));
    private static GameObject[] Targets(ModuleVrc3 module) => ((IEnumerable<GameObject>)Call(module, "ShaderPreviewAvatars")).Where(item => item).ToArray();
    private static bool Callbacks(object owner) =>
        (Camera.onPreCull?.GetInvocationList().Any(callback => ReferenceEquals(callback.Target, owner)) ?? false) ||
        (Camera.onPostRender?.GetInvocationList().Any(callback => ReferenceEquals(callback.Target, owner)) ?? false);

    private static void CheckFocusedModule(Action<bool, string> check)
    {
        ProValidationWindow host = null;
        GameObject avatar = null;
        Avatar generic = null;
        GestureManager manager = null;
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
            avatar = new GameObject("Pro focused Android module avatar");
            new GameObject("Root").transform.SetParent(avatar.transform, false);
            var animator = avatar.AddComponent<Animator>();
            generic = AvatarBuilder.BuildGenericAvatar(avatar, "Root");
            generic.hideFlags = HideFlags.HideAndDontSave;
            animator.avatar = generic;
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            avatar.AddComponent<VRC.Core.PipelineManager>();
            descriptor.ViewPosition = Vector3.up * 1.6f;
            descriptor.lipSync = VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
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
            manager = new GameObject("Pro focused Android module manager").AddComponent<GestureManager>();
            manager.settings = new ModuleSettings();
            module = new ModuleVrc3(descriptor);
            manager.SetModule(module);
            RunModule(module, manager, check);
            manager.UnlinkModule();
            CheckUnlinked(module, check);
        }
        finally
        {
            if (manager && ReferenceEquals(manager.Module, module)) manager.UnlinkModule();
            if (module != null)
            {
                ModuleOwners.Remove(module);
                Call(module, "StopVrcHooks");
            }
            if (manager) UnityEngine.Object.DestroyImmediate(manager.gameObject);
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            if (generic) UnityEngine.Object.DestroyImmediate(generic);
            if (host) UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static VRCAvatarDescriptor.CustomAnimLayer DefaultLayer(VRCAvatarDescriptor.AnimLayerType type) =>
        new VRCAvatarDescriptor.CustomAnimLayer { type = type, isDefault = true };
    private static object Get(object target, string name)
    {
        var type = target.GetType();
        return type.GetProperty(name, Any) is PropertyInfo property ? property.GetValue(target) : type.GetField(name, Any).GetValue(target);
    }
    private static object Call(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, Any).Invoke(target, arguments);
    private static bool Magenta(Color[] pixels) => Center(pixels).Count(color => color.r > 0.75f && color.g < 0.2f && color.b > 0.75f) > 200;
    private static bool Blue(Color[] pixels) => Center(pixels).Count(color => color.b > 0.25f && color.b > color.r * 2f && color.b > color.g * 1.5f) > 200;
    private static IEnumerable<Color> Center(Color[] pixels)
    {
        for (var y = 20; y < 44; y++) for (var x = 20; x < 44; x++) yield return pixels[y * 64 + x];
    }
    private static float Difference(Color[] first, Color[] second) => first.Zip(second, (a, b) =>
        Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))).Average();
    private static string SnapshotClip(AnimationClip clip)
    {
        // Establish Unity's editor curve caches before comparing the complete
        // compiled transient clip, as in the MMD source immutability checks.
        foreach (var binding in AnimationUtility.GetCurveBindings(clip)) AnimationUtility.GetEditorCurve(clip, binding);
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip)) AnimationUtility.GetObjectReferenceCurve(clip, binding);
        return EditorJsonUtility.ToJson(clip, true);
    }

    private static string MaterialName(Material material) => material ? material.name + "#" + material.GetInstanceID() + "/" + material.shader.name : "null";

    private static void AnimationDiagnostic(Fixture fixture, AnimationClip clip, AnimationClip properties, EditorCurveBinding binding, string stage)
    {
        Directory.CreateDirectory(Output);
        var text = new StringBuilder();
        text.AppendLine("Stage=" + stage + "; playing=" + Application.isPlaying + "; renderer=" + fixture.Illegal.GetType().FullName);
        text.AppendLine("Binding=" + binding.path + ";" + binding.type.FullName + ";" + binding.propertyName + "; PPtr=" + binding.isPPtrCurve);
        text.AppendLine("Bound object=" + AnimationUtility.GetAnimatedObject(fixture.Root, binding));
        text.AppendLine("Actual=" + MaterialName(fixture.Illegal.sharedMaterial));
        text.AppendLine("Allowed=" + MaterialName(fixture.AllowedSource) + "; Illegal=" + MaterialName(fixture.IllegalSource));
        foreach (var item in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            text.AppendLine("Object curve=" + item.path + ";" + item.type.FullName + ";" + item.propertyName);
            foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, item)) text.AppendLine("  time=" + key.time + "; value=" + key.value);
        }
        text.AppendLine("Clip=" + clip.legacy + "; length=" + clip.length + "; property clip length=" + properties.length);
        File.WriteAllText(Path.Combine(Output, "animation-" + stage + ".txt"), text.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Output, "animation-" + stage + ".clip.json"), SnapshotClip(clip), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Output, "animation-" + stage + ".properties.json"), SnapshotClip(properties), new UTF8Encoding(false));
    }

    private sealed class ClipSampler : IDisposable
    {
        private PlayableGraph _graph;
        private readonly AnimationPlayableOutput _output;
        private readonly AnimationClipPlayable[] _clips;

        internal ClipSampler(Animator animator, params AnimationClip[] clips)
        {
            _graph = PlayableGraph.Create("Pro real material-animation fixture");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _clips = clips.Select(clip =>
            {
                var playable = AnimationClipPlayable.Create(_graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                playable.SetSpeed(0d);
                return playable;
            }).ToArray();
            _output = AnimationPlayableOutput.Create(_graph, "Authored renderer material animation", animator);
            _graph.Play();
        }

        internal void Sample(int clip, double time)
        {
            _output.SetSourcePlayable(_clips[clip]);
            _clips[clip].SetTime(time);
            _graph.Evaluate(0f);
        }

        public void Dispose() { if (_graph.IsValid()) _graph.Destroy(); }
    }

    [Serializable]
    private sealed class Result
    {
        public bool passed;
        public int assertions;
        public string buildTarget;
        public bool androidTarget;
        public string graphics;
        public string error;
    }

    private sealed class Preview : IDisposable
    {
        internal readonly object Value;
        internal Preview(object value) { Value = value; }
        internal Preview(Func<IEnumerable<GameObject>> targets, Func<bool> android = null)
        {
            var type = typeof(ModuleVrc3).Assembly.GetType(TypeName, true);
            Value = Activator.CreateInstance(type, Any, null, new object[] { targets, android }, null);
        }
        internal bool Enabled
        {
            get => (bool)Get(Value, "Enabled");
            set => Value.GetType().GetProperty("Enabled", Any).SetValue(Value, value);
        }
        internal bool Active => (bool)Get(Value, "IsActive");
        internal int Blocked => (int)Get(Value, "BlockedMaterialCount");
        internal int Outlines => (int)Get(Value, "OutlineMaterialCount");
        internal Material[] BlockedMaterials => ((IEnumerable)Get(Value, "BlockedMaterials")).Cast<Material>().ToArray();
        internal void Refresh() => Call(Value, "RefreshStatus");
        internal void Begin(Camera camera) => Call(Value, "Begin", camera);
        internal void End(Camera camera) => Call(Value, "End", camera);
        internal void Restore() => Call(Value, "Restore");
        public void Dispose() => ((IDisposable)Value).Dispose();
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly GameObject Root;
        internal readonly Renderer Illegal;
        internal readonly Renderer Allowed;
        internal readonly Renderer Outline;
        internal readonly Camera Camera;
        internal readonly Camera SecondCamera;
        internal readonly Material IllegalSource;
        internal readonly Material AllowedSource;
        internal readonly Material OutlineSource;
        private readonly Texture2D _green;
        private readonly Texture2D _blue;
        private readonly string[] _sources;
        private readonly List<GameObject> _extraObjects = new List<GameObject>();

        internal Fixture(Action<bool, string> check, Transform parent = null)
        {
            var illegalShader = Shader.Find("Unlit/Color");
            var allowedShader = Shader.Find(AllowedShader);
            var outlineShader = Shader.Find(OutlineShader);
            check(illegalShader && allowedShader && outlineShader && Shader.Find(MobileToonShader) && Shader.Find(ErrorShader),
                "The Android shader fixture resolves real SDK mobile, Outline, unsupported and Unity error shaders");
            Root = new GameObject("Pro Android shader render fixture");
            if (parent) Root.transform.SetParent(parent, false);
            else Root.transform.position = new Vector3(5000f, 5000f, 5000f);
            _green = Texture(new Color(0.03f, 0.9f, 0.05f));
            _blue = Texture(new Color(0.04f, 0.06f, 0.95f));
            IllegalSource = new Material(illegalShader) { name = "Pro unsupported source", color = Color.cyan, hideFlags = HideFlags.HideAndDontSave };
            AllowedSource = new Material(allowedShader) { name = "Pro supported source", mainTexture = _green, hideFlags = HideFlags.HideAndDontSave };
            OutlineSource = new Material(outlineShader) { name = "Pro Outline source", mainTexture = _green, hideFlags = HideFlags.HideAndDontSave };
            OutlineSource.enableInstancing = true;
            OutlineSource.doubleSidedGI = true;
            if (OutlineSource.HasProperty("_Color")) OutlineSource.SetColor("_Color", Color.white);
            Illegal = AddRenderer("Illegal", IllegalSource);
            Allowed = AddRenderer("Allowed", AllowedSource);
            Outline = AddRenderer("Outline", OutlineSource);
            foreach (var renderer in new[] { Illegal, Allowed, Outline })
            {
                var block = new MaterialPropertyBlock();
                block.SetFloat("_ProShaderSentinel", 742.25f);
                block.SetVector("_ProShaderVector", new Vector4(1f, 2f, 3f, 4f));
                renderer.SetPropertyBlock(block);
                var indexed = new MaterialPropertyBlock();
                indexed.SetFloat("_ProShaderIndexed", 29.5f);
                indexed.SetTexture("_MainTex", _blue);
                renderer.SetPropertyBlock(indexed, 0);
            }
            Camera = CreateCamera("Pro Android shader camera");
            SecondCamera = CreateCamera("Pro Android nested camera");
            _sources = Sources();
        }

        internal Renderer AddRenderer(string name, Material material, Transform parent = null)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.layer = 30;
            item.transform.SetParent(parent ? parent : Root.transform, false);
            item.transform.localScale = Vector3.one * 1.2f;
            if (parent) _extraObjects.Add(item);
            var renderer = item.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private Camera CreateCamera(string name)
        {
            var item = new GameObject(name);
            item.transform.SetParent(Root.transform, false);
            item.transform.localPosition = new Vector3(0f, 0f, -5f);
            var camera = item.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1f;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10f;
            camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            return camera;
        }

        internal bool IsRestored() => Illegal.sharedMaterial == IllegalSource && Allowed.sharedMaterial == AllowedSource && Outline.sharedMaterial == OutlineSource;
        internal bool IsFiltered() => Illegal.sharedMaterial && Illegal.sharedMaterial.shader.name == ErrorShader &&
            Allowed.sharedMaterial == AllowedSource && Outline.sharedMaterial && Outline.sharedMaterial.shader.name == MobileToonShader;
        private string[] Sources() => new[] { IllegalSource, AllowedSource, OutlineSource }.Select(material => EditorJsonUtility.ToJson(material, true)).ToArray();
        internal void CheckSources(Action<bool, string> check) => check(Sources().SequenceEqual(_sources) &&
            IllegalSource.shader.name == "Unlit/Color" && AllowedSource.shader.name == AllowedShader && OutlineSource.shader.name == OutlineShader,
            "Shader preview preserves every source material's complete serialized properties and shader identity");
        internal void CheckBlocks(Action<bool, string> check)
        {
            foreach (var renderer in new[] { Illegal, Allowed, Outline })
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                var indexed = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(indexed, 0);
                check(block.GetFloat("_ProShaderSentinel") == 742.25f && block.GetVector("_ProShaderVector") == new Vector4(1f, 2f, 3f, 4f) &&
                    indexed.GetFloat("_ProShaderIndexed") == 29.5f && indexed.GetTexture("_MainTex") == _blue,
                    "Camera overrides preserve renderer-wide and per-material property blocks: " + renderer.name);
            }
        }

        internal Color[] Render(Renderer visible, string filename)
        {
            var renderers = new[] { Illegal, Allowed, Outline };
            var enabled = renderers.Select(renderer => renderer.enabled).ToArray();
            var previousTarget = Camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(64, 64, TextureFormat.RGB24, false);
            try
            {
                for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = renderers[index] == visible;
                Camera.targetTexture = target;
                Camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Output);
                File.WriteAllBytes(Path.Combine(Output, filename), image.EncodeToPNG());
                return image.GetPixels();
            }
            finally
            {
                for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = enabled[index];
                Camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static Texture2D Texture(Color color)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            return texture;
        }

        public void Dispose()
        {
            foreach (var item in _extraObjects) if (item) UnityEngine.Object.DestroyImmediate(item);
            if (Root) UnityEngine.Object.DestroyImmediate(Root);
            foreach (var material in new[] { IllegalSource, AllowedSource, OutlineSource }) if (material) UnityEngine.Object.DestroyImmediate(material);
            if (_green) UnityEngine.Object.DestroyImmediate(_green);
            if (_blue) UnityEngine.Object.DestroyImmediate(_blue);
        }
    }
}
