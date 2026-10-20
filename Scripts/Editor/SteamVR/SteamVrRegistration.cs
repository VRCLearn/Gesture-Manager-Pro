using UnityEditor;
using UnityEditor.XR.Management;
using VRCLearn.GestureManagerPro.Control;

namespace VRCLearn.GestureManagerPro.SteamVR.Editor
{
    [InitializeOnLoad]
    internal static class SteamVrRegistration
    {
        static SteamVrRegistration()
        {
            VrInputProviders.Register("steamvr", "SteamVR", () => new SteamVrInputSource(), ConfigureSessionStartup, IsSessionStartupConfigured);
        }

        private static bool IsSessionStartupConfigured()
        {
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            return !settings || !settings.InitManagerOnStart;
        }

        private static void ConfigureSessionStartup()
        {
            // Invoked by explicit VR preparation, never by registration or Start.
            // The adapter owns its loader and does not replace the project's loader list.
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!settings || !settings.InitManagerOnStart) return;
            settings.InitManagerOnStart = false;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }
    }
}
