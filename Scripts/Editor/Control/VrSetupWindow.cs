using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    public sealed class VrSetupWindow : EditorWindow
    {
        private static AddAndRemoveRequest _request;
        private string _status;

        public static void ShowSetup()
        {
            var window = GetWindow<VrSetupWindow>();
            window.titleContent = new GUIContent(EditorLocalization.Tr("VR Setup"));
            window.minSize = new Vector2(420f, 240f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(EditorLocalization.Tr("Connect your headset and trackers to SteamVR. Install the original OpenVR XR Plugin 1.2.1 and the optional Pro SteamVR adapter together. Project XR settings are not changed when simulation starts."), MessageType.Info);
            EditorGUILayout.LabelField(EditorLocalization.Tr("SteamVR Adapter"), EditorLocalization.Tr(
                VrInputProviders.Providers.Count > 0 ? "Installed" : "Not Installed"));
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || _request != null))
            {
                if (GUILayout.Button(EditorLocalization.Tr("Install VR Packages..."))) Install();
                using (new EditorGUI.DisabledScope(VrInputProviders.Providers.Count == 0))
                    if (GUILayout.Button(EditorLocalization.Tr("Use Manual XR Startup")))
                        foreach (var provider in VrInputProviders.Providers) provider.Configure?.Invoke();
            }
            EditorGUILayout.HelpBox(EditorLocalization.Tr("Manual XR startup disables automatic XR initialization for this project. Pro starts and releases its own loader for each VR session."), MessageType.Info);
            if (_request != null) EditorGUILayout.LabelField(EditorLocalization.Tr("Installing VR packages..."));
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
            EditorGUILayout.HelpBox(EditorLocalization.Tr("Use the original dependency archive supplied with SteamVR Unity Plugin 2.8.0. The adapter is separate from the main package; PC simulation does not require it."), MessageType.Info);
        }

        private void Install()
        {
            var dependency = EditorUtility.OpenFilePanel(EditorLocalization.Tr("Select OpenVR XR Plugin 1.2.1"), string.Empty, "tgz");
            if (string.IsNullOrEmpty(dependency)) return;
            if (!Path.GetFileName(dependency).Contains("1.2.1"))
            { _status = EditorLocalization.Tr("Select the original OpenVR XR Plugin 1.2.1 archive."); return; }
            var adapter = EditorUtility.OpenFilePanel(EditorLocalization.Tr("Select Pro SteamVR Adapter"), Path.GetDirectoryName(dependency), "tgz");
            if (string.IsNullOrEmpty(adapter)) return;
            try
            {
                _request = Client.AddAndRemove(new[] { "file:" + dependency.Replace('\\', '/'), "file:" + adapter.Replace('\\', '/'), "com.unity.inputsystem@1.6.1" });
                EditorApplication.update += Poll;
            }
            catch (Exception exception) { _status = exception.Message; _request = null; }
        }

        private void Poll()
        {
            if (_request == null || !_request.IsCompleted) return;
            _status = _request.Status == StatusCode.Success ? EditorLocalization.Tr("VR packages installed. Enter Play Mode to start VR simulation.") : _request.Error?.message;
            _request = null;
            EditorApplication.update -= Poll;
            Repaint();
        }

        private void OnEnable() { if (_request != null) EditorApplication.update += Poll; }
        private void OnDisable() => EditorApplication.update -= Poll;
    }
}
