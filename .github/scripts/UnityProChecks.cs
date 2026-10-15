using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Control.Editor;
using VRCLearn.GestureManagerPro.Editor;
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Params;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.RadialSlices;
using VRCLearn.GestureManagerPro.Extensions.Editor;
using VRCLearn.GestureManagerPro.Localization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

// Copy to an isolated Unity 2022.3 project's Assets/Editor directory, install the
// Pro package plus its declared VRChat SDK dependency, and call UnityProChecks.Run.
// This runner does not require the original Gesture Manager or the extension.
public static class UnityProChecks
{
    private static int _assertions;
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public static void Run()
    {
        _assertions = 0;
        ProValidationWindow host = null;
        var exitCode = 1;
        try
        {
            // Upstream visual helpers use GUI.skin during construction. A real
            // window owner initializes the same GUI context in batch mode.
            host = ScriptableObject.CreateInstance<ProValidationWindow>();
            host.Show();
            var owner = typeof(EditorWindow).GetField("m_Parent", Any).GetValue(host) as ScriptableObject;
            typeof(GUIUtility).GetMethod("BeginContainerFromOwner", Any).Invoke(null, new object[] { owner });
            typeof(GUIUtility).GetMethod("BeginGUI", Any).Invoke(null, new object[] { 1, host.GetInstanceID(), 1 });
            typeof(EditorStyles).GetMethod("UpdateSkinCache", Any, null, new[] { typeof(int) }, null)
                .Invoke(null, new object[] { EditorGUIUtility.isProSkin ? 1 : 0 });
            ExecuteChecks();
            Debug.Log("[Pro Unity validation] PASS: " + _assertions + " assertions. Package resources, native DLL, localization/cache switching, avatar metadata preservation, and injected motion input.");
            exitCode = 0;
        }
        catch (Exception exception)
        {
            Debug.LogError("[Pro Unity validation] FAIL after " + _assertions + " assertions: " + exception);
        }
        finally
        {
            if (host) UnityEngine.Object.DestroyImmediate(host);
        }
        EditorApplication.Exit(exitCode);
    }

    private static void ExecuteChecks()
    {
        var preferenceKey = "com.vrclearn.gesture-manager-pro.language";
        var hadPreference = EditorPrefs.HasKey(preferenceKey);
        var preferenceValue = EditorPrefs.GetInt(preferenceKey, 0);
        var language = EditorLocalization.Language;
        GameObject avatar = null;
        GameSimulationTool simulationTool = null;
        GestureManager manager = null;
        UnityEditor.Editor inspector = null;
        VRCExpressionsMenu expressionMenu = null;
        IDisposable analyzer = null;
        Action onLanguageChange = null;
        try
        {
            var core = typeof(GestureManager).Assembly;
            var simulator = typeof(ModuleVrc3).Assembly;
            var localization = typeof(EditorLocalization).Assembly;
            var audioTools = typeof(MicrophoneLipSync).Assembly;
            foreach (var assembly in new[] { core, simulator, localization, audioTools }.Distinct())
            {
                Check(assembly.GetName().Name.StartsWith("VRCLearn.GestureManagerPro", StringComparison.Ordinal), "Standalone assembly name: " + assembly.GetName().Name);
                Check(!assembly.GetReferencedAssemblies().Any(name => name.Name.Contains("blackstartx") || name.Name.Contains("Harmony") || name.Name.Contains("Cqmhv")), "No original Gesture Manager or Harmony assembly dependency: " + assembly.GetName().Name);
                Check(!assembly.GetTypes().Any(type => type.Name == "GestureManagerLocalizationPatch"), "No interface localization patch: " + assembly.GetName().Name);
            }
            Check(core != simulator && simulator != localization && localization != audioTools, "Core, simulator, localization, and audio tools use separate assemblies");
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "vrchat.blackstartx.gesture-manager"), "Fixture does not contain the original Gesture Manager");
            CheckToolsMenu(typeof(GestureManagerEditor), "AddNewEmulator", "Tools/Gesture Manager Pro");
            Check(new[] { typeof(GameSimulationTool), typeof(MicrophoneLipSync), typeof(VRCLearn.GestureManagerPro.Diagnostics.ModelStatisticsWindow) }
                .All(type => !type.GetMethods(Any).Any(method => method.IsDefined(typeof(MenuItem), false))),
                "Feature tools do not add standalone Tools menu entries");
            Check(!localization.GetTypes().SelectMany(type => type.GetMethods(Any))
                .Any(method => method.IsDefined(typeof(SettingsProviderAttribute), false)),
                "Language selection does not register a standalone Unity Preferences page");

            Check(Resources.Load<Texture2D>("GestureManagerPro/Vrc3/BSX_GM_Default"), "Pro radial icon loads from scoped resource path");
            Check(Resources.Load<GameObject>("GestureManagerPro/Vrc3/Meshes/CullingDiamond"), "Pro culling prefab loads from scoped resource path");
            Check(Resources.Load<RuntimeAnimatorController>("GestureManagerPro/Vrc3/Controllers/GmgUtilityIKPose"), "Pro controller loads from scoped resource path");
            CheckBuiltInControllers();
            var packagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.vrclearn.gesture-manager-pro/GestureManagerPro.prefab");
            Check(packagePrefab && packagePrefab.GetComponent<GestureManager>(), "Package prefab points to the Pro simulator component");

            EditorLocalization.Language = ProLanguage.English;
            var changes = 0;
            onLanguageChange = () => changes++;
            EditorLocalization.Changed += onLanguageChange;
            EditorLocalization.Language = ProLanguage.SimplifiedChinese;
            Check(changes == 1, "Language change emits one notification");
            EditorLocalization.Language = ProLanguage.SimplifiedChinese;
            Check(changes == 1, "Assigning the current language does not emit another notification");
            Check(EditorLocalization.Tr("Left Hand") == "左手", "Chinese inspector label");
            Check(EditorLocalization.Tr("unknown-test-key") == "unknown-test-key", "Missing text falls back to source");
            Check(EditorLocalization.Tr(null) == null, "Null caption remains null");
            Check(EditorLocalization.Tr("Exit Edit-Mode") == "退出编辑模式", "Mode template is translated");
            Check(EditorLocalization.Format("Avatar: {0}\nLip sync mode: {1}", "Tools", "Mode").Contains("Tools"), "Formatted avatar metadata remains exact");
            Check(EditorLocalization.Tr("OVRLipSync could not process audio: native error -1.").Contains("-1"), "Native diagnostic retains error code");

            manager = new GameObject("Pro validation manager").AddComponent<GestureManager>();
            manager.settings = new VRCLearn.GestureManagerPro.Modules.ModuleSettings();
            simulationTool = new GameSimulationTool(manager);
            Check(simulationTool is GmgDynamicFunction && !typeof(GmgDynamicFunction).IsAssignableFrom(typeof(MicrophoneLipSync)),
                "Game simulation is a standard tool and microphone lip sync has no independent tool entry");
            Check(ToolCaption(simulationTool) == "游戏模拟" && EditorLocalization.Tr("Microphone Lip Sync") == "麦克风口型",
                "The simulation header and embedded microphone setting render in Chinese");
            Check((VRCLearn.GestureManagerPro.Control.SimulationViewMode)typeof(GameSimulationTool).GetField("_viewMode", Any).GetValue(simulationTool) == VRCLearn.GestureManagerPro.Control.SimulationViewMode.FirstPerson,
                "Inline game simulation keeps the first-person default");
            var microphone = (MicrophoneLipSync)typeof(GameSimulationTool).GetField("_lipSync", Any).GetValue(simulationTool);
            Check(microphone.Enabled && !microphone.Running, "Microphone lip sync is enabled by default but does not record before game simulation starts");
            Check(!typeof(MicrophoneLipSync).GetMethods(Any).Any(method => method.IsDefined(typeof(InitializeOnLoadMethodAttribute), false)),
                "Microphone integration does not register an independent tool factory");
            var factories = (List<Func<GestureManager, GmgDynamicFunction>>)typeof(AvatarTools).GetField("ToolFactories", Any).GetValue(null);
            Check(factories.Count == 1, "Only game simulation registers a feature tool factory");
            foreach (var factory in factories.ToArray()) AvatarTools.RegisterTool(factory);
            Check(factories.Count == 1, "Repeating a tool registration does not duplicate its entry");
            foreach (var factory in factories)
            {
                using (var tool = factory(manager))
                    Check(tool.GetType().GetField("_manager", Any).GetValue(tool) == manager,
                        "Tool creation binds to the originating manager: " + tool.GetType().Name);
            }

            avatar = new GameObject("Tools");
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            var module = new ModuleVrc3(descriptor);
            foreach (var key in new[] { "Grounded", "VelocityY", "Upright", "VelocityX", "VelocityZ", "AngularY", "State", "Tools" })
                module.Params.Add(key, new Vrc3Param(key, AnimatorControllerParameterType.Float));
            Check(module.Name == "Tools", "Avatar name matching a translated caption is preserved");
            Check(module.GetParam("Grounded") != null && module.GetParam("着地") == null, "Built-in parameter lookup key is preserved");
            Check(module.GetParam("State").Name == "State" && module.GetParam("Tools").Name == "Tools", "User parameter identifiers remain exact");

            var radial = new RadialMenu(module, false);
            radial.Set(null);
            manager.Module = module;
            typeof(RadialMenu).GetMethod("ToolMenuPrefab", Any).Invoke(radial, null);
            var toolButtons = ((RadialSliceBase[])typeof(RadialMenu).GetField("_buttons", Any).GetValue(radial)).OfType<RadialSliceTool>().ToArray();
            Check(toolButtons.Length == 4, "The radial Tools submenu contains game simulation and the three inherited tools, without an independent lip-sync entry");
            var toolsOwner = (AvatarTools)typeof(ModuleVrc3).GetField("AvatarTools", Any).GetValue(module);
            var registered = (List<GmgDynamicFunction>)typeof(AvatarTools).GetField("_registeredTools", Any).GetValue(toolsOwner);
            var getRegistered = typeof(AvatarTools).GetMethod("GetRegisteredTools", Any);
            Check(ReferenceEquals(registered, getRegistered.Invoke(toolsOwner, new object[] { module })),
                "The radial menu and Inspector share the same registered tool instances");
            foreach (var tool in registered)
            {
                Check(tool.GetType().GetField("_manager", Any).GetValue(tool) == manager,
                    "Radial-first creation binds to the manager controlling this module: " + tool.GetType().Name);
                Check(toolButtons.Any(button => ReferenceEquals(typeof(RadialSliceTool).GetField("_tool", Any).GetValue(button), tool)),
                    "The radial action targets the registered session: " + tool.GetType().Name);
                Check((Texture2D)tool.GetType().GetProperty("Icon", Any).GetValue(tool),
                    "Registered radial tool icon resolves: " + tool.GetType().Name);
            }
            var gameButton = toolButtons.Single(button => Caption(button) == "游戏模拟");
            toolsOwner.Unlink(module);
            manager.Module = null;
            typeof(RadialMenu).GetMethod("OptionLocomotionMenuPrefab", Any).Invoke(radial, null);
            var buttons = (RadialSliceBase[])typeof(RadialMenu).GetField("_buttons", Any).GetValue(radial);
            var grounded = buttons.Single(button => Caption(button) == EditorLocalization.Tr("Grounded"));
            var state = new RadialSliceButton(null, EditorLocalization.Tr("State"));
            var states = new RadialSliceButton(null, EditorLocalization.Tr("States"));
            Check(Caption(state) == EditorLocalization.Tr("State") && Caption(states) == EditorLocalization.Tr("States"), "Ambiguous state captions render in Chinese");

            expressionMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            foreach (var name in new[] { "Tools", "State", "States", "工具", "Avatar: Tools\nLip sync mode: Mode" })
                expressionMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = name, type = VRCExpressionsMenu.Control.ControlType.Button,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = "Tools" },
                    subParameters = new VRCExpressionsMenu.Control.Parameter[0]
                });
            var customControls = expressionMenu.controls.Select(control => new RadialSliceControl(radial, control)).ToArray();
            for (var i = 0; i < customControls.Length; i++)
                Check(Caption(customControls[i]) == expressionMenu.controls[i].name, "Custom expression name is preserved: " + expressionMenu.controls[i].name);
            var focusLabel = (TextElement)typeof(Vrc3FloatingMenu).GetProperty("TextElement", Any).GetValue(null);
            Check(focusLabel.text == EditorLocalization.Tr("The window is not focused!"), "Focus notice renders in Chinese");
            var toolbar = new GmgLayoutHelper.Toolbar();
            var cachedToolbar = toolbar.Contents(new[] { (EditorLocalization.Tr("Tools"), (Action)(() => { })) });
            Check(cachedToolbar[0].text == EditorLocalization.Tr("Tools"), "Toolbar initially renders in Chinese");

            EditorLocalization.Language = ProLanguage.English;
            Check(Caption(gameButton) == "Game Simulation",
                "Existing radial tool captions follow live language changes");
            Check(ToolCaption(simulationTool) == "Game Simulation" && EditorLocalization.Tr("Microphone Lip Sync") == "Microphone Lip Sync",
                "Existing tool headers follow live language changes without rebuilding a window");
            Check(Caption(grounded) == "Grounded", "Existing built-in radial control switches live to English");
            Check(Caption(state) == "State" && Caption(states) == "States", "Equal Chinese captions retain their distinct original source keys");
            Check(focusLabel.text == "The window is not focused!", "Existing focus notice switches live to English");
            var englishToolbar = toolbar.Contents(new[] { (EditorLocalization.Tr("Tools"), (Action)(() => { })) });
            Check(englishToolbar[0].text == "Tools", "Cached toolbar refreshes its label after language switch");
            for (var i = 0; i < customControls.Length; i++)
                Check(Caption(customControls[i]) == expressionMenu.controls[i].name, "Custom expression name survives language change: " + expressionMenu.controls[i].name);
            Check(avatar.name == "Tools" && module.Name == "Tools", "Live switching leaves avatar metadata unchanged");
            Check(module.GetGestureTextNameByIndex(1) == "[GESTURE] Fist", "Gesture animation resource identifier is preserved");
            EditorLocalization.Language = ProLanguage.SimplifiedChinese;
            Check(Caption(grounded) == EditorLocalization.Tr("Grounded"), "Existing radial control switches back to Chinese");
            Check(Caption(state) == EditorLocalization.Tr("State") && Caption(states) == EditorLocalization.Tr("States"), "Distinct sources survive a second language switch");

            inspector = UnityEditor.Editor.CreateEditor(manager);
            Check(inspector is GestureManagerEditor, "Pro component has its own custom inspector");
            Check(((GestureManagerEditor)inspector).CreateInspectorGUI() != null, "Pro custom inspector creates its visual root");

            UnityProMotionChecks.Run(Check);
            UnityProCameraChecks.Run(Check);
            UnityProStatisticsChecks.Run(Check);
            UnityProLocalizationChecks.Run(Check);
            UnityProFontChecks.Run(Check);

            Check(Application.platform == RuntimePlatform.WindowsEditor && IntPtr.Size == 8, "Native validation runs on supported Windows x64 Editor");
            var analyzerType = audioTools.GetType("VRCLearn.GestureManagerPro.Extensions.Editor.OvrLipSyncAnalyzer", true);
            analyzer = (IDisposable)Activator.CreateInstance(analyzerType, Any, null, new object[] { 48000, 1024 }, null);
            Check((string)analyzerType.GetMethod("Version", Any).Invoke(null, null) == "1.54.0", "Bundled renamed native DLL exports expected SDK version");
            var weights = new float[15];
            analyzerType.GetMethod("Process", Any).Invoke(analyzer, new object[] { new float[1024], weights });
            Check(weights.All(value => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1), "Native Enhanced analyzer returns valid viseme weights for silence");
            analyzer.Dispose();
            analyzer.Dispose();
            analyzer = null;
        }
        finally
        {
            analyzer?.Dispose();
            if (inspector) UnityEngine.Object.DestroyImmediate(inspector);
            if (manager) UnityEngine.Object.DestroyImmediate(manager.gameObject);
            simulationTool?.Dispose();
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            if (expressionMenu) UnityEngine.Object.DestroyImmediate(expressionMenu);
            if (onLanguageChange != null) EditorLocalization.Changed -= onLanguageChange;
            EditorLocalization.Language = language;
            if (hadPreference) EditorPrefs.SetInt(preferenceKey, preferenceValue);
            else EditorPrefs.DeleteKey(preferenceKey);
        }
    }

    private static string Caption(RadialSliceBase slice)
    {
        var textField = typeof(RadialSliceBase).GetField("Text", Any);
        var text = textField.GetValue(slice);
        return (string)textField.FieldType.GetField("_text", Any).GetValue(text);
    }

    private static void CheckToolsMenu(Type owner, string methodName, string path)
    {
        var method = owner.GetMethod(methodName, Any, null, Type.EmptyTypes, null);
        var entries = method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().ToArray();
        Check(entries.Length == 1 && entries[0].menuItem == path,
            owner.Name + " exposes one direct Tools action: " + path);
    }

    private static string ToolCaption(GmgDynamicFunction tool) =>
        (string)tool.GetType().GetProperty("Name", Any).GetValue(tool);

    private static void CheckBuiltInControllers()
    {
        var styles = typeof(ModuleVrc3).Assembly.GetType("VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.ModuleVrc3Styles", true);
        var data = styles.GetNestedType("Data", Any);
        var controllerOf = data.GetMethod("ControllerOf", Any);
        var restoreOf = data.GetMethod("RestoreOf", Any);
        foreach (var type in new[]
        {
            VRCAvatarDescriptor.AnimLayerType.Base, VRCAvatarDescriptor.AnimLayerType.Additive,
            VRCAvatarDescriptor.AnimLayerType.Gesture, VRCAvatarDescriptor.AnimLayerType.Action,
            VRCAvatarDescriptor.AnimLayerType.FX, VRCAvatarDescriptor.AnimLayerType.Sitting,
            VRCAvatarDescriptor.AnimLayerType.TPose, VRCAvatarDescriptor.AnimLayerType.IKPose
        })
        {
            var controller = (UnityEditor.Animations.AnimatorController)controllerOf.Invoke(null, new object[] { type });
            var restore = (TextAsset)restoreOf.Invoke(null, new object[] { type });
            Check(controller && restore, "Actual controller/restore lookup resolves for layer " + type);
            var path = AssetDatabase.GetAssetPath(controller);
            Check(path.StartsWith("Packages/com.vrclearn.gesture-manager-pro/", StringComparison.Ordinal), "Built-in layer controller belongs to the Pro package: " + type);
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            var physical = Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1));
            Check(File.ReadAllText(physical) == restore.text, "Controller/restore body integrity matches: " + type);
            Debug.Log("[Pro controller diagnostic] " + type + ": " + controller.animationClips.Length + " resolved clips; " + string.Join(", ", controller.animationClips.Select(clip => clip.name)));
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }
}

public sealed class ProValidationWindow : EditorWindow
{
}
