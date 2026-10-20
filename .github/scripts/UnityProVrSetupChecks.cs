using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRCLearn.GestureManagerPro.Control.Editor;

// Runs without SteamVR hardware or an initialized avatar.
public static class UnityProVrSetupChecks
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    public static void Run(Action<bool, string> check)
    {
        var installer = typeof(VrSetupWindow).Assembly.GetType("VRCLearn.GestureManagerPro.Control.Editor.VrDependencyInstaller", true);
        var manifest = Path.GetFullPath("Packages/manifest.json");
        var originalManifest = File.ReadAllBytes(manifest);
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects().Select(root => root.GetInstanceID()).ToArray();
        var wasDirty = scene.isDirty;
        EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>("Unity.XR.OpenVR.Settings", out var xrBefore);
        var oldWindows = Resources.FindObjectsOfTypeAll<VrSetupWindow>();
        try
        {
            check(EditorApplication.ExecuteMenuItem("Tools/Gesture Manager Pro - VR Setup"),
                "The top-level VR setup menu opens in Edit Mode without an initialized avatar");
            check(EditorApplication.ExecuteMenuItem("Tools/Gesture Manager Pro - VR Setup") &&
                Resources.FindObjectsOfTypeAll<VrSetupWindow>().Length == Math.Max(1, oldWindows.Length),
                "Repeated setup menu actions reuse the existing setup window");
            check(!EditorApplication.isPlaying && scene.isDirty == wasDirty &&
                roots.SequenceEqual(scene.GetRootGameObjects().Select(root => root.GetInstanceID())),
                "Opening VR setup neither enters Play Mode nor changes the scene");
            EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>("Unity.XR.OpenVR.Settings", out var xrAfter);
            check(xrBefore == xrAfter && File.ReadAllBytes(manifest).SequenceEqual(originalManifest),
                "Opening VR setup preserves XR configuration and installed dependencies");

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
                    File.ReadAllBytes(manifest).SequenceEqual(originalManifest),
                    "A rejected archive leaves packages unchanged and allows a retry");
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
        }
        finally
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<VrSetupWindow>())
                if (!oldWindows.Contains(window)) window.Close();
        }
    }

    private static string Argument(string name)
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
}
