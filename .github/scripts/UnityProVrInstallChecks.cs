using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;

// Opt-in real download/UPM check. Run only in a disposable PC fixture with no OpenVR archive.
public static class UnityProVrInstallChecks
{
    private const string Key = "GmpValidation.RealVrInstall.";
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static UnityEditor.PackageManager.Requests.AddAndRemoveRequest _removal;
    private static UnityEditor.PackageManager.Requests.ListRequest _removalCheck;
    private static Type Installer => typeof(VrSetupWindow).Assembly.GetType("VRCLearn.GestureManagerPro.Control.Editor.VrDependencyInstaller", true);
    private static bool Flag(string name) => (bool)Installer.GetProperty(name, Static).GetValue(null);

    public static void Run()
    {
        SessionState.SetBool(Key + "active", true);
        SessionState.SetBool(Key + "removing", false);
        SessionState.SetBool(Key + "begun", false);
        SessionState.SetBool(Key + "reloaded", false);
        SessionState.SetBool(Key + "waited-for-edit-mode", false);
        SessionState.SetInt(Key + "input-before", (int)Installer.GetProperty("InputHandling", Static).GetValue(null));
        SessionState.SetBool(Key + "restart-before", Flag("RestartRequired"));
        SessionState.SetString(Key + "deadline", DateTime.UtcNow.AddMinutes(6).Ticks.ToString());
        var scene = SceneManager.GetActiveScene();
        SessionState.SetString(Key + "scene", scene.path);
        SessionState.SetBool(Key + "dirty", scene.isDirty);
        SessionState.SetString(Key + "roots", string.Join("\n", scene.GetRootGameObjects().Select(root => root.name)));
        foreach (var path in PreservedSettings)
            SessionState.SetString(Key + path, SettingsSnapshot(path));
        Resume();
    }

    public static void RemoveDependencies()
    {
        SessionState.SetBool(Key + "active", true);
        SessionState.SetBool(Key + "removing", true);
        SessionState.SetString(Key + "deadline", DateTime.UtcNow.AddMinutes(4).Ticks.ToString());
        _removal = UnityEditor.PackageManager.Client.AddAndRemove(null,
            new[] { "com.valvesoftware.unity.openvr", "com.unity.inputsystem" });
        Resume();
    }

    private static readonly string[] PreservedSettings = {
        "ProjectSettings/XRPackageSettings.asset",
        "ProjectSettings/XRSettings.asset",
        "Packages/com.vrchat.base/Editor/XR/XRGeneralSettings.asset"
    };

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(Key + "active", false)) return;
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        AssemblyReloadEvents.beforeAssemblyReload -= Reloading;
        AssemblyReloadEvents.beforeAssemblyReload += Reloading;
    }

    private static void Reloading() => SessionState.SetBool(Key + "reloaded", true);

    private static void Poll()
    {
        if (!SessionState.GetBool(Key + "active", false)) return;
        try
        {
            if (DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Key + "deadline", "0")))
                throw new TimeoutException("Real VR installation did not finish within six minutes.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (SessionState.GetBool(Key + "removing", false))
            {
                PollRemoval();
                return;
            }
            if (!SessionState.GetBool(Key + "begun", false))
            {
                var archive = (string)Installer.GetProperty("ArchivePath", Static).GetValue(null);
                Require(!File.Exists(archive), "Use a fresh PC fixture so the official archive is really downloaded.");
                Require(!Flag("IsBusy"), "No previous installer operation is active.");
                VrSetupWindow.ShowSetup();
                Require(Resources.FindObjectsOfTypeAll<VrSetupWindow>().Length == 1,
                    "The existing simulation shortcut opens a single setup view in Edit Mode.");
                SessionState.SetBool(Key + "begun", true);
                Installer.GetMethod("Install", Static).Invoke(null, null);
                Require(Flag("IsDownloading"), "The one-click action starts downloading the official Valve archive.");
                foreach (var window in Resources.FindObjectsOfTypeAll<VrSetupWindow>()) window.Close();
                Debug.Log("[Pro VR install validation] Download started; setup window closed while the request is active.");
                EditorApplication.EnterPlaymode();
                return;
            }
            var stage = Installer.GetProperty("CurrentStage", Static).GetValue(null).ToString();
            if (stage == "Waiting" && EditorApplication.isPlaying)
            {
                Require(!UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .Any(package => package.name == "com.valvesoftware.unity.openvr"),
                    "Download completion in Play Mode must not submit a UPM installation.");
                SessionState.SetBool(Key + "waited-for-edit-mode", true);
                Debug.Log("[Pro VR install validation] Download completed in Play Mode; installation waited for Edit Mode.");
                EditorApplication.ExitPlaymode();
                return;
            }
            if (Flag("IsBusy")) return;
            Require(!Flag("HasError"), (string)Installer.GetProperty("Status", Static).GetValue(null));
            if (VrInputProviders.Providers.Count == 0) return;
            var archivePath = (string)Installer.GetProperty("ArchivePath", Static).GetValue(null);
            Require((bool)Installer.GetMethod("ValidateArchive", Static).Invoke(null, new object[] { archivePath }),
                "The downloaded official archive passes SHA-256 verification.");
            var installed = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            Require(installed.Any(package => package.name == "com.valvesoftware.unity.openvr" && package.version == "1.2.1"),
                "UPM registered the original OpenVR 1.2.1 package.");
            Require(!installed.Any(package => package.name == "com.vrclearn.gesture-manager-pro.steamvr"),
                "No companion adapter package was installed.");
            Require(SessionState.GetBool(Key + "reloaded", false), "The installer survived the package compilation/domain reload.");
            Require(SessionState.GetBool(Key + "waited-for-edit-mode", false),
                "The installer deferred package changes until the fixture returned to Edit Mode.");
            Require(Resources.FindObjectsOfTypeAll<VrSetupWindow>().Length == 0,
                "Installation completed with the setup window closed.");
            Require((int)Installer.GetProperty("InputHandling", Static).GetValue(null) == 2,
                "The one-click installation enabled Both input handling for VR and PC input.");
            Require(Flag("RestartRequired") == (SessionState.GetInt(Key + "input-before", 0) != 2 ||
                SessionState.GetBool(Key + "restart-before", false)),
                "The input restart requirement survives domain reload and is added only when input handling changes.");
            Require(!(bool)Installer.GetProperty("NeedsProjectSetup", Static).GetValue(null) &&
                VrInputProviders.Providers.All(provider => provider.IsConfigured == null || provider.IsConfigured()),
                "The one-click installation completed XR project setup without a second configuration button.");
            var standalone = StandaloneXrSettings();
            Require(standalone && !new SerializedObject(standalone).FindProperty("m_InitManagerOnStart").boolValue,
                "Standalone XR automatic startup is disabled so GMP can own each requested VR session.");
            var scene = SceneManager.GetActiveScene();
            Require(scene.path == SessionState.GetString(Key + "scene", "") &&
                scene.isDirty == SessionState.GetBool(Key + "dirty", false) &&
                string.Join("\n", scene.GetRootGameObjects().Select(root => root.name)) == SessionState.GetString(Key + "roots", ""),
                "Installation preserved the current scene and its objects.");
            foreach (var path in PreservedSettings)
                Require(SettingsSnapshot(path) == SessionState.GetString(Key + path, ""),
                    "Installation preserved XR loader lists, other platforms and unrelated settings: " + path);
            Finish(0, "PASS: official download, hash verification, UPM install, closed-window ownership, Play Mode deferral, domain-reload recovery, bundled provider registration, automatic VR project setup and scene/unrelated-XR preservation.");
        }
        catch (Exception exception) { Finish(1, "FAIL: " + exception); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void PollRemoval()
    {
        if (_removal != null)
        {
            if (!_removal.IsCompleted) return;
            Require(_removal.Status == UnityEditor.PackageManager.StatusCode.Success, _removal.Error?.message);
            _removal = null;
        }
        if (_removalCheck == null)
        {
            _removalCheck = UnityEditor.PackageManager.Client.List(true, true);
            return;
        }
        if (!_removalCheck.IsCompleted) return;
        Require(_removalCheck.Status == UnityEditor.PackageManager.StatusCode.Success, _removalCheck.Error?.message);
        var stillInstalled = _removalCheck.Result.Any(package => package.name == "com.valvesoftware.unity.openvr" ||
            package.name == "com.unity.inputsystem");
        _removalCheck = null;
        if (stillInstalled) return;
        Require(VrInputProviders.Providers.Count == 0, "Removing the optional dependencies disables only the bundled SteamVR provider.");
        SessionState.SetBool(Key + "active", false);
        EditorApplication.update -= Poll;
        AssemblyReloadEvents.beforeAssemblyReload -= Reloading;
        Debug.Log("[Pro VR install validation] PASS: optional dependencies removed; running main-package PC regression.");
        UnityProChecks.Run();
    }

    private static string SettingsSnapshot(string path)
    {
        if (!File.Exists(path)) return "<absent>";
        if (!path.EndsWith("XRGeneralSettings.asset", StringComparison.Ordinal)) return File.ReadAllText(path);
        // Unity/SDK reserialization can regenerate internal file IDs. Compare the
        // platform mapping, startup flags and loader identities, rather than those IDs.
        var records = new List<string>();
        var standalone = StandaloneXrSettings();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).Where(value => value))
        {
            var serialized = new SerializedObject(asset);
            var fields = new List<string> { asset.GetType().FullName, asset.name };
            foreach (var name in new[] { "m_InitManagerOnStart", "m_AutomaticLoading", "m_AutomaticRunning",
                "m_LoaderManagerInstance", "m_Loaders", "Keys", "Values" })
            {
                var property = serialized.FindProperty(name);
                if (property == null) continue;
                // The explicit Enable VR action deliberately takes ownership of
                // Standalone startup. Other platform flags and all loaders are retained.
                if (name == "m_InitManagerOnStart" && asset == standalone) continue;
                if (property.isArray && property.propertyType != SerializedPropertyType.String)
                    fields.Add(name + "=" + string.Join(",", Enumerable.Range(0, property.arraySize)
                        .Select(index => PropertyValue(property.GetArrayElementAtIndex(index)))));
                else fields.Add(name + "=" + PropertyValue(property));
            }
            records.Add(string.Join("|", fields));
        }
        Require(records.Count > 0, "XR settings assets were loaded for configuration comparison.");
        return string.Join("\n", records.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static UnityEngine.Object StandaloneXrSettings()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget"))
            .FirstOrDefault(candidate => candidate != null);
        return type?.GetMethod("XRGeneralSettingsForBuildTarget", Static, null,
            new[] { typeof(BuildTargetGroup) }, null)?.Invoke(null, new object[] { BuildTargetGroup.Standalone }) as UnityEngine.Object;
    }

    private static string PropertyValue(SerializedProperty property)
    {
        if (property.propertyType == SerializedPropertyType.Boolean) return property.boolValue.ToString();
        if (property.propertyType == SerializedPropertyType.ObjectReference)
        {
            var value = property.objectReferenceValue;
            return value ? AssetDatabase.GetAssetPath(value) + ":" + value.name + ":" + value.GetType().FullName : "null";
        }
        return property.intValue.ToString();
    }

    private static void Finish(int result, string message)
    {
        SessionState.SetBool(Key + "active", false);
        EditorApplication.update -= Poll;
        AssemblyReloadEvents.beforeAssemblyReload -= Reloading;
        if (result == 0) Debug.Log("[Pro VR install validation] " + message);
        else Debug.LogError("[Pro VR install validation] " + message);
        EditorApplication.Exit(result);
    }
}
