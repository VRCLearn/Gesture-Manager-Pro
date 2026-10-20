using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;
using VRCLearn.GestureManagerPro.Editor.Data;
using VRCLearn.GestureManagerPro.Modules;

// Runs without SteamVR hardware or an initialized avatar.
public static class UnityProVrSetupChecks
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    public static void Run(Action<bool, string> check)
    {
        var installer = typeof(VrSetupWindow).Assembly.GetType("VRCLearn.GestureManagerPro.Control.Editor.VrDependencyInstaller", true);
        var manifest = Path.GetFullPath("Packages/manifest.json");
        var originalManifest = File.ReadAllBytes(manifest);
        var originalInput = InputHandling;
        var prefix = (string)installer.GetField("SessionPrefix", Static).GetRawConstantValue();
        var restartKey = prefix + "restart-required";
        var restartBefore = SessionState.GetBool(restartKey, false);
        var stageBefore = SessionState.GetInt(prefix + "stage", 0);
        var errorBefore = SessionState.GetBool(prefix + "error", false);
        var statusBefore = SessionState.GetString(prefix + "status", string.Empty);
        var detailBefore = SessionState.GetString(prefix + "detail", string.Empty);
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects().Select(root => root.GetInstanceID()).ToArray();
        var wasDirty = scene.isDirty;
        EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>("Unity.XR.OpenVR.Settings", out var xrBefore);
        var xrJsonBefore = xrBefore ? EditorJsonUtility.ToJson(xrBefore) : null;
        var oldWindows = Resources.FindObjectsOfTypeAll<VrSetupWindow>();
        try
        {
            check(!typeof(VrSetupWindow).GetMethods(Static).Any(method => method.IsDefined(typeof(MenuItem), false)),
                "VR setup does not register a standalone Unity Tools menu item");
            DrawComponentSettings(check);
            check(Resources.FindObjectsOfTypeAll<VrSetupWindow>().SequenceEqual(oldWindows),
                "Drawing VR setup in the component settings does not open a separate window");
            check(!EditorApplication.isPlaying && scene.isDirty == wasDirty &&
                roots.SequenceEqual(scene.GetRootGameObjects().Select(root => root.GetInstanceID())),
                "Drawing component VR settings neither enters Play Mode nor changes the scene");
            EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>("Unity.XR.OpenVR.Settings", out var xrAfter);
            check(xrBefore == xrAfter && (xrAfter ? EditorJsonUtility.ToJson(xrAfter) : null) == xrJsonBefore &&
                File.ReadAllBytes(manifest).SequenceEqual(originalManifest) && InputHandling == originalInput &&
                SessionState.GetBool(restartKey, false) == restartBefore,
                "Drawing component VR settings preserves XR, input handling, restart state and installed dependencies");
            VrSetupWindow.ShowSetup();
            VrSetupWindow.ShowSetup();
            check(Resources.FindObjectsOfTypeAll<VrSetupWindow>().Length == Math.Max(1, oldWindows.Length),
                "The existing simulation shortcut can still reuse the setup window");

            var invalid = Path.GetFullPath("Temp/ProInvalidOpenVr.tgz");
            Directory.CreateDirectory(Path.GetDirectoryName(invalid));
            File.WriteAllText(invalid, "This is not the original Valve archive.");
            try
            {
                check(!(bool)installer.GetMethod("ValidateArchive", Static).Invoke(null, new object[] { invalid }),
                    "A renamed or invalid OpenVR archive is rejected before installation");
                installer.GetMethod("InstallFromArchive", Static).Invoke(null, new object[] { invalid });
                check((bool)installer.GetProperty("HasError", Static).GetValue(null) &&
                    !(bool)installer.GetProperty("IsBusy", Static).GetValue(null) &&
                    File.ReadAllBytes(manifest).SequenceEqual(originalManifest) && InputHandling == originalInput &&
                    SessionState.GetBool(restartKey, false) == restartBefore,
                    "A rejected archive leaves packages, input handling and restart state unchanged and allows a retry");
            }
            finally { File.Delete(invalid); }

            var official = Argument("-gmpOpenVrArchive");
            if (!string.IsNullOrEmpty(official))
            {
                var validate = installer.GetMethod("ValidateArchive", Static);
                check((bool)validate.Invoke(null, new object[] { official }), "The original Valve archive passes its pinned SHA-256 verification");
                var corrupted = Path.GetFullPath("Temp/ProCorruptOpenVr.tgz");
                File.Copy(official, corrupted, true);
                try
                {
                    using (var stream = new FileStream(corrupted, FileMode.Open, FileAccess.ReadWrite))
                    {
                        var first = stream.ReadByte();
                        stream.Position = 0;
                        stream.WriteByte((byte)(first ^ 1));
                    }
                    check(!(bool)validate.Invoke(null, new object[] { corrupted }),
                        "SHA-256 verification rejects a modified archive even when its file size matches");
                }
                finally { File.Delete(corrupted); }
            }
            var planned = (string[])installer.GetMethod("PackagesToInstall", Static).Invoke(null,
                new object[] { Path.GetFullPath("Temp/OpenVr.tgz"), new UnityEditor.PackageManager.PackageInfo[0] });
            check(planned.Length == 3 && planned.Any(value => value == "com.unity.inputsystem@1.6.1") &&
                planned.Any(value => value == "com.unity.xr.management@4.4.0") &&
                !planned.Any(value => value.Contains("gesture-manager-pro.steamvr")),
                "VR support installation requests only Valve's archive and Unity dependencies");
            CheckDependencyReadiness(installer, check);
            CheckExplicitProjectSetup(installer, check);
        }
        finally
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<VrSetupWindow>())
                if (!oldWindows.Contains(window)) window.Close();
            InputHandling = originalInput;
            SessionState.SetBool(restartKey, restartBefore);
            SessionState.SetInt(prefix + "stage", stageBefore);
            SessionState.SetBool(prefix + "error", errorBefore);
            SessionState.SetString(prefix + "status", statusBefore);
            SessionState.SetString(prefix + "detail", detailBefore);
        }
    }

    private static void CheckDependencyReadiness(Type installer, Action<bool, string> check)
    {
        var registered = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
        var ready = (bool)installer.GetProperty("DependenciesReady", Static).GetValue(null);
        var completeGraph = installer.GetMethod("DependenciesInstalled", Static);
        if (VrInputProviders.Providers.Any(provider => provider.Id == "steamvr"))
            check(ready, "The VR fixture reports ready only with the complete OpenVR, XR and Input System dependency graph");
        if (!registered.Any(package => package.name == "com.valvesoftware.unity.openvr"))
            check(!ready, "A PC-only fixture does not report VR dependencies ready");
        foreach (var dependency in new[] { "com.unity.inputsystem", "com.unity.xr.management", "com.valvesoftware.unity.openvr" })
        {
            var incomplete = registered.Where(package => package.name != dependency).ToArray();
            check(!(bool)completeGraph.Invoke(null, new object[] { incomplete }),
                "VR setup requires installation instead of configuration when missing " + dependency);
        }
    }

    private static int InputHandling
    {
        get
        {
            var target = Resources.FindObjectsOfTypeAll<PlayerSettings>().FirstOrDefault() ??
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").OfType<PlayerSettings>().First();
            using (var settings = new SerializedObject(target))
                return settings.FindProperty("activeInputHandler").intValue;
        }
        set
        {
            var target = Resources.FindObjectsOfTypeAll<PlayerSettings>().FirstOrDefault() ??
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").OfType<PlayerSettings>().First();
            using (var settings = new SerializedObject(target))
            {
                settings.FindProperty("activeInputHandler").intValue = value;
                settings.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(target);
            }
        }
    }

    private static void CheckExplicitProjectSetup(Type installer, Action<bool, string> check)
    {
        var prefix = (string)installer.GetField("SessionPrefix", Static).GetRawConstantValue();
        var originalInput = InputHandling;
        var stage = SessionState.GetInt(prefix + "stage", 0);
        var error = SessionState.GetBool(prefix + "error", false);
        var restart = SessionState.GetBool(prefix + "restart-required", false);
        var status = SessionState.GetString(prefix + "status", string.Empty);
        var detail = SessionState.GetString(prefix + "detail", string.Empty);
        var providers = (List<VrInputProviders.Provider>)typeof(VrInputProviders).GetField("Registered", Static).GetValue(null);
        var originalProviders = providers.ToArray();
        var configureCalls = 0;
        var createCalls = 0;
        var configured = false;
        Func<string, bool> flag = name => (bool)installer.GetProperty(name, Static).GetValue(null);
        try
        {
            providers.Clear();
            InputHandling = 0;
            SessionState.SetBool(prefix + "restart-required", false);
            check(!flag("NeedsProjectSetup"), "A PC-only project does not require VR project setup");
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                installer.GetMethod("PrepareProject", Static).Invoke(null, null);
                check(flag("HasError") && InputHandling == 0 && !flag("RestartRequired"),
                    "Unsupported editors reject VR setup while preserving PC input and restart state");
                return;
            }
            providers.Add(new VrInputProviders.Provider("validation", "Validation",
                () => { createCalls++; return null; },
                () => { configureCalls++; configured = true; }, () => configured));
            check(flag("NeedsProjectSetup"), "Installed VR support exposes setup when XR or Both input handling is missing");
            installer.GetMethod("PrepareProject", Static).Invoke(null, null);
            check(configured && configureCalls == 1 && InputHandling == 2 && !flag("NeedsProjectSetup") &&
                !flag("HasError") && !flag("IsBusy"),
                "One explicit setup action configures the provider and enables Both input handling");
            check(flag("RestartRequired") && createCalls == 0,
                "Changing input handling requests an explicit restart without creating or starting a VR source");

            SessionState.SetBool(prefix + "restart-required", false);
            installer.GetMethod("PrepareProject", Static).Invoke(null, null);
            check(InputHandling == 2 && !flag("RestartRequired") && !flag("NeedsProjectSetup") && createCalls == 0,
                "Repeating setup with Both already enabled does not request another restart or start XR");

            InputHandling = 1;
            check(flag("NeedsProjectSetup"), "Input System-only mode still needs Both to preserve PC keyboard and mouse input");
            installer.GetMethod("ConfigureInputHandling", Static).Invoke(null, null);
            check(InputHandling == 2 && flag("RestartRequired"),
                "Explicit setup changes Input System-only mode to Both and records the required restart");
            configured = false;
            check(flag("NeedsProjectSetup"), "XR configuration is still required when Both input handling is already enabled");
        }
        finally
        {
            providers.Clear();
            providers.AddRange(originalProviders);
            InputHandling = originalInput;
            SessionState.SetInt(prefix + "stage", stage);
            SessionState.SetBool(prefix + "error", error);
            SessionState.SetBool(prefix + "restart-required", restart);
            SessionState.SetString(prefix + "status", status);
            SessionState.SetString(prefix + "detail", detail);
        }
        check(InputHandling == originalInput && providers.SequenceEqual(originalProviders) &&
            SessionState.GetBool(prefix + "restart-required", false) == restart,
            "Project setup validation restores input handling, provider registration and pending restart state");
    }

    // Called inside the same real GUI owner context as the other Editor checks.
    // The preview scene provides an uninitialized component without touching the user's scene.
    private static void DrawComponentSettings(Action<bool, string> check)
    {
        var callbacks = typeof(GestureManagerSettings).GetField("AdditionalSettingsGui", Static);
        var register = typeof(VrSetupWindow).GetMethod("RegisterSettings", Static);
        var draw = typeof(VrSetupWindow).GetMethod("DrawInspectorSettings", Static);
        Func<int> registrations = () => ((Action)callbacks.GetValue(null))?.GetInvocationList()
            .Count(callback => callback.Method == draw) ?? 0;
        check(registrations() == 1, "Component settings register the bundled VR section in Edit Mode");
        register.Invoke(null, null);
        register.Invoke(null, null);
        check(registrations() == 1, "Repeated initialization does not duplicate the component VR section");

        var preview = EditorSceneManager.NewPreviewScene();
        GameObject temporary = null;
        var originalEvent = Event.current;
        var foldout = typeof(VrSetupWindow).GetField("_showSettings", Static);
        var wasOpen = (bool)foldout.GetValue(null);
        var invoked = 0;
        Action observer = () => invoked++;
        try
        {
            temporary = new GameObject("Pro VR settings validation") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(temporary, preview);
            var manager = temporary.AddComponent<GestureManager>();
            manager.settings = new ModuleSettings();
            foldout.SetValue(null, true);
            GestureManagerSettings.AdditionalSettingsGui += observer;
            Event.current = new Event { type = EventType.Layout };
            GestureManagerSettings.SettingGui(manager);
            check(invoked == 1 && manager.Module == null && !EditorApplication.isPlaying,
                "Actual component settings dispatch the expanded VR section before Play Mode without an avatar");
        }
        finally
        {
            GestureManagerSettings.AdditionalSettingsGui -= observer;
            foldout.SetValue(null, wasOpen);
            Event.current = originalEvent;
            if (temporary) UnityEngine.Object.DestroyImmediate(temporary);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static string Argument(string name)
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
}
