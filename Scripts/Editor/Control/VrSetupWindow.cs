using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;
using VRCLearn.GestureManagerPro.Localization;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    public sealed class VrSetupWindow : EditorWindow
    {
        [MenuItem("Tools/Gesture Manager Pro - VR Setup", false, -41)]
        public static void ShowSetup()
        {
            var window = GetWindow<VrSetupWindow>();
            window.titleContent = new GUIContent(EditorLocalization.Tr("VR Setup"));
            window.minSize = new Vector2(440f, 300f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(EditorLocalization.Tr("GMP includes the SteamVR adapter. Install VR support to download Valve's original OpenVR XR Plugin 1.2.1 and install its Unity dependencies. PC simulation does not require these packages."), MessageType.Info);
            EditorGUILayout.LabelField(EditorLocalization.Tr("SteamVR Support"), EditorLocalization.Tr(
                VrInputProviders.Providers.Count > 0 ? "Installed" : "Not Installed"));
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || VrDependencyInstaller.IsBusy))
            {
                if (GUILayout.Button(EditorLocalization.Tr("Install SteamVR Support"))) VrDependencyInstaller.Install();
                if (GUILayout.Button(EditorLocalization.Tr("Install from Local Archive...")))
                {
                    var path = EditorUtility.OpenFilePanel(EditorLocalization.Tr("Select OpenVR XR Plugin 1.2.1"), string.Empty, "tgz");
                    if (!string.IsNullOrEmpty(path)) VrDependencyInstaller.InstallFromArchive(path);
                }
                using (new EditorGUI.DisabledScope(VrInputProviders.Providers.Count == 0))
                    if (GUILayout.Button(EditorLocalization.Tr("Use Manual XR Startup")))
                        foreach (var provider in VrInputProviders.Providers) provider.Configure?.Invoke();
            }
            EditorGUILayout.HelpBox(EditorLocalization.Tr("Manual XR startup disables automatic XR initialization for this project. Pro starts and releases its own loader for each VR session."), MessageType.Info);
            var status = VrDependencyInstaller.Status;
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, VrDependencyInstaller.HasError ? MessageType.Error : MessageType.Info);
            if (VrDependencyInstaller.IsDownloading)
            {
                var rect = EditorGUILayout.GetControlRect();
                EditorGUI.ProgressBar(rect, VrDependencyInstaller.DownloadProgress, EditorLocalization.Tr("Downloading OpenVR XR Plugin 1.2.1..."));
            }
            EditorGUILayout.HelpBox(EditorLocalization.Tr("The 19.52 MB Valve archive is verified with SHA-256 and kept in LocalPackages~. Input System 1.6.1 and XR Management 4.4.0 are installed when needed; compatible installed versions are reused. XR project settings change only when you click Use Manual XR Startup."), MessageType.Info);
        }

        private void OnEnable() => VrDependencyInstaller.Changed += Repaint;
        private void OnDisable() => VrDependencyInstaller.Changed -= Repaint;
    }

    // Package operations belong to the editor session, not an individual setup window.
    [InitializeOnLoad]
    internal static class VrDependencyInstaller
    {
        internal const string OpenVrUrl = "https://raw.githubusercontent.com/ValveSoftware/steamvr_unity_plugin/2.8.0/Assets/SteamVR/OpenVRUnityXRPackage/Editor/com.valvesoftware.unity.openvr-1.2.1.tgz";
        internal const string OpenVrSha256 = "0cea29792b0b5aec5d366be994c17eda49b97c33a6728cf09c21fbfd94a2a7e6";
        internal const long OpenVrBytes = 19523367;
        private const string SessionPrefix = "com.vrclearn.gesture-manager-pro.vr-install.";
        private const string OpenVrPackage = "com.valvesoftware.unity.openvr";
        private const string InputPackage = "com.unity.inputsystem";
        private const string XrPackage = "com.unity.xr.management";
        private enum Stage { Idle, Downloading, Installing, Checking, Waiting }
        private static UnityWebRequest _download;
        private static AddAndRemoveRequest _install;
        private static ListRequest _check;
        private static bool _resumePending;
        internal static event Action Changed;

        static VrDependencyInstaller()
        {
            EditorApplication.update += Poll;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            _resumePending = IsBusy;
        }

        private static Stage CurrentStage
        {
            get => (Stage)SessionState.GetInt(SessionPrefix + "stage", 0);
            set => SessionState.SetInt(SessionPrefix + "stage", (int)value);
        }
        internal static bool IsBusy => CurrentStage != Stage.Idle;
        internal static bool IsDownloading => CurrentStage == Stage.Downloading;
        internal static bool HasError => SessionState.GetBool(SessionPrefix + "error", false);
        internal static float DownloadProgress => _download == null ? 0f : Mathf.Clamp01(_download.downloadProgress);
        internal static string ArchivePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../LocalPackages~/com.valvesoftware.unity.openvr-1.2.1.tgz"));
        private static string PartialPath => ArchivePath + ".download";
        internal static string Status
        {
            get
            {
                var key = SessionState.GetString(SessionPrefix + "status", string.Empty);
                var detail = SessionState.GetString(SessionPrefix + "detail", string.Empty);
                return string.IsNullOrEmpty(detail) ? EditorLocalization.Tr(key) : string.Format(EditorLocalization.Tr(key), detail);
            }
        }

        internal static void Install()
        {
            if (!CanInstall()) return;
            try
            {
                PackagesToInstall(ArchivePath, PackageInfo.GetAllRegisteredPackages());
                if (ValidateArchive(ArchivePath)) BeginInstall();
                else BeginDownload();
            }
            catch (Exception exception) { Fail(exception.Message); }
        }

        internal static void InstallFromArchive(string path)
        {
            if (!CanInstall()) return;
            try
            {
                PackagesToInstall(ArchivePath, PackageInfo.GetAllRegisteredPackages());
                if (!ValidateArchive(path))
                {
                    Fail(EditorLocalization.Tr("Select the original, unmodified OpenVR XR Plugin 1.2.1 archive from SteamVR Unity Plugin 2.8.0. Its SHA-256 checksum must match the official archive."));
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(ArchivePath));
                if (!string.Equals(Path.GetFullPath(path), ArchivePath, StringComparison.OrdinalIgnoreCase)) File.Copy(path, ArchivePath, true);
                BeginInstall();
            }
            catch (Exception exception) { Fail(exception.Message); }
        }

        private static bool CanInstall()
        {
            if (IsBusy) return false;
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                Fail(EditorLocalization.Tr("SteamVR support requires the Windows Editor."));
                return false;
            }
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating) return true;
            Fail(EditorLocalization.Tr("Exit Play Mode and wait for compilation and importing to finish before installing VR support."));
            return false;
        }

        private static void BeginDownload()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ArchivePath));
            if (File.Exists(PartialPath)) File.Delete(PartialPath);
            CurrentStage = Stage.Downloading;
            SetStatus("Downloading OpenVR XR Plugin 1.2.1...");
            _download = new UnityWebRequest(OpenVrUrl, UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerFile(PartialPath) { removeFileOnAbort = true }, null);
            _download.timeout = 180;
            _download.SendWebRequest();
        }

        private static void BeginInstall()
        {
            if (!CanSubmitPackageRequest()) { WaitForEditor(false); return; }
            var packages = PackagesToInstall(ArchivePath, PackageInfo.GetAllRegisteredPackages());
            CurrentStage = Stage.Installing;
            SetStatus("Installing VR dependencies...");
            _install = Client.AddAndRemove(packages);
        }

        private static bool CanSubmitPackageRequest() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        private static void WaitForEditor(bool verifyOnly)
        {
            SessionState.SetBool(SessionPrefix + "verify-only", verifyOnly);
            CurrentStage = Stage.Waiting;
            SetStatus("VR setup is waiting. Exit Play Mode and let compilation and importing finish to continue installation.");
        }

        private static void BeginCheck()
        {
            if (!CanSubmitPackageRequest()) { WaitForEditor(true); return; }
            CurrentStage = Stage.Checking;
            SetStatus("Checking installed VR dependencies...");
            _check = Client.List(true, true);
        }

        private static void ContinueWaiting()
        {
            if (!CanSubmitPackageRequest()) return;
            if (SessionState.GetBool(SessionPrefix + "verify-only", false)) BeginCheck();
            else BeginInstall();
        }

        // These checks can run without installing packages or changing project settings.
        internal static string[] PackagesToInstall(string archivePath, IEnumerable<PackageInfo> installed)
        {
            var packages = installed.ToArray();
            var additions = new List<string> { "file:" + Path.GetFullPath(archivePath).Replace('\\', '/') };
            var input = packages.FirstOrDefault(package => package.name == InputPackage);
            if (!AtLeast(input, new Version(1, 6, 1))) additions.Add(InputPackage + "@1.6.1");
            var xr = packages.FirstOrDefault(package => package.name == XrPackage);
            if (AtLeast(xr, new Version(5, 0, 0)))
                throw new InvalidOperationException(EditorLocalization.Tr("XR Management 5.x or newer is not supported. Use a Unity 2022.3 project with XR Management 4.x."));
            if (!AtLeast(xr, new Version(4, 3, 3))) additions.Add(XrPackage + "@4.4.0");
            return additions.ToArray();
        }

        internal static bool ValidateArchive(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != OpenVrBytes) return false;
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
                return string.Equals(BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty), OpenVrSha256, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AtLeast(PackageInfo package, Version minimum)
        {
            Version version;
            return package != null && Version.TryParse(package.version.Split('-', '+')[0], out version) && version >= minimum;
        }

        private static bool DependenciesInstalled(IEnumerable<PackageInfo> installed)
        {
            var packages = installed.ToArray();
            var xr = packages.FirstOrDefault(package => package.name == XrPackage);
            return packages.Any(package => package.name == OpenVrPackage && package.version == "1.2.1") &&
                   AtLeast(packages.FirstOrDefault(package => package.name == InputPackage), new Version(1, 6, 1)) &&
                   AtLeast(xr, new Version(4, 3, 3)) && !AtLeast(xr, new Version(5, 0, 0));
        }

        private static void Resume()
        {
            if (!IsBusy) return;
            try
            {
                if (CurrentStage == Stage.Downloading)
                {
                    if (ValidateArchive(ArchivePath)) BeginInstall();
                    else BeginDownload();
                }
                else if (CurrentStage == Stage.Waiting) ContinueWaiting();
                else BeginCheck();
            }
            catch (Exception exception) { Fail(exception.Message); }
        }

        private static void Poll()
        {
            try
            {
                if (_resumePending)
                {
                    _resumePending = false;
                    Resume();
                    return;
                }
                if (CurrentStage == Stage.Waiting) ContinueWaiting();
                if (_download != null)
                {
                    if (!_download.isDone) { Changed?.Invoke(); return; }
                    var error = _download.result == UnityWebRequest.Result.Success ? null : _download.error;
                    _download.Dispose();
                    _download = null;
                    if (error != null) { DeletePartial(); Fail(error); return; }
                    if (!ValidateArchive(PartialPath))
                    {
                        DeletePartial();
                        Fail(EditorLocalization.Tr("The downloaded OpenVR archive failed its SHA-256 verification. Retry or select the original local archive."));
                        return;
                    }
                    if (File.Exists(ArchivePath)) File.Delete(ArchivePath);
                    File.Move(PartialPath, ArchivePath);
                    BeginInstall();
                }
                if (_install != null && _install.IsCompleted)
                {
                    var request = _install;
                    _install = null;
                    if (request.Status == StatusCode.Success)
                        BeginCheck();
                    else Fail(request.Error?.message ?? EditorLocalization.Tr("VR dependencies are incomplete. Retry installation after Unity finishes resolving packages."));
                }
                if (_check != null && _check.IsCompleted)
                {
                    var request = _check;
                    _check = null;
                    if (request.Status == StatusCode.Success && DependenciesInstalled(request.Result)) Succeed();
                    else Fail(request.Error?.message ?? EditorLocalization.Tr("VR dependencies are incomplete. Retry installation after Unity finishes resolving packages."));
                }
            }
            catch (Exception exception) { Fail(exception.Message); }
        }

        private static void Succeed()
        {
            CurrentStage = Stage.Idle;
            SetStatus("VR support installed. Click Use Manual XR Startup, then connect to SteamVR and enter Game Simulation with VR input.");
        }

        private static void Fail(string detail)
        {
            _download?.Abort();
            _download?.Dispose();
            _download = null;
            _install = null;
            _check = null;
            CurrentStage = Stage.Idle;
            SetStatus("VR dependency installation failed.\n{0}", detail, true);
        }

        private static void SetStatus(string key, string detail = "", bool error = false)
        {
            SessionState.SetString(SessionPrefix + "status", key);
            SessionState.SetString(SessionPrefix + "detail", detail);
            SessionState.SetBool(SessionPrefix + "error", error);
            Changed?.Invoke();
        }

        private static void DeletePartial()
        {
            if (File.Exists(PartialPath)) File.Delete(PartialPath);
        }

        private static void BeforeAssemblyReload()
        {
            _download?.Abort();
            _download?.Dispose();
            _download = null;
            // The next domain verifies UPM results or resumes the download using SessionState.
        }
    }
}
