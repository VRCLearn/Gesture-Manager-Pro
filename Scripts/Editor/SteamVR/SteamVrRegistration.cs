using UnityEditor;
using UnityEngine.XR.Management;
using VRCLearn.GestureManagerPro.Control;

namespace VRCLearn.GestureManagerPro.SteamVR.Editor
{
    [InitializeOnLoad]
    internal static class SteamVrRegistration
    {
        static SteamVrRegistration()
        {
            VrInputProviders.Register("steamvr", "SteamVR", () => new SteamVrInputSource(), ConfigureSessionStartup);
        }

        private static void ConfigureSessionStartup()
        {
            // Invoked by the explicit VR Setup button, never by registration or Start.
            // The adapter owns its loader and does not replace the project's loader list.
            var settings = XRGeneralSettings.Instance;
            if (!settings || !settings.InitManagerOnStart) return;
            settings.InitManagerOnStart = false;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }
}
