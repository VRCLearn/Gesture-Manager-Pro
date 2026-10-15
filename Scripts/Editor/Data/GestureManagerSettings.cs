using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Editor.Modules;
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Editor.Data
{
    public static class GestureManagerSettings
    {
        private static readonly ProLanguage[] LanguageValues = { ProLanguage.Automatic, ProLanguage.English, ProLanguage.Japanese, ProLanguage.SimplifiedChinese };
        private static readonly string[] LanguageOptions = { "", "English", "日本語", "简体中文" };
        private const string LocalFolder = "LocalAvatarData";

        private static IEnumerable<string> Users(string directory) => Directory.Exists(directory) ? Directory.GetDirectories(directory).Select(Path.GetFileName) : new string[] { };
        private static string UserPath(string folder, string user) => user == null ? null : PathCombine(VrcDirectory, folder, user);
        private static string UserID(int index, IReadOnlyList<string> users) => index < users.Count ? users[index] : null;
        public static string VrcDirectory => PathCombine(ModuleHelper.LocalLowPath, "VRChat", "VRChat");
        private static string[] Choose => new[] { EditorLocalization.Tr("[Unset]") }.Concat(Users(DataDirectory)).ToArray();
        public static string UserID(int index) => index < 1 ? null : UserID(index, Choose);
        public static string UserPath(int index) => UserPath(LocalFolder, UserID(index));
        private static string DataDirectory => PathCombine(VrcDirectory, LocalFolder);

        private static string PathCombine(string path1, string path2, string path3) => path1 == null ? null : Path.Combine(path1, path2, path3);
        private static string PathCombine(string path1, string path2) => path1 == null ? null : Path.Combine(path1, path2);

        private static bool _generalSettings;
        private static bool _simulationSettings;

        public static void SettingGui(GestureManager manager)
        {
            GUILayout.Label(EditorLocalization.Tr("Here you can customize the tool settings!"), GestureManagerStyles.MiddleStyle);
            GUILayout.Space(10);

            LanguageGui();
            using (new GUILayout.VerticalScope(GestureManagerStyles.EmoteError)) GeneralSettings(manager);
            using (new GUILayout.VerticalScope(GestureManagerStyles.EmoteError)) SimulationSettings(manager);
        }

        private static void LanguageGui()
        {
            LanguageOptions[0] = EditorLocalization.Tr("Automatic");
            var selected = System.Array.IndexOf(LanguageValues, EditorLocalization.Language);
            var language = LanguageValues[EditorGUILayout.Popup(EditorLocalization.Tr("Language"), selected, LanguageOptions)];
            if (language == EditorLocalization.Language) return;
            EditorLocalization.Language = language;
            GUIUtility.ExitGUI();
        }

        private static void GeneralSettings(GestureManager manager)
        {
            if (!GmgLayoutHelper.FoldoutSection(EditorLocalization.Tr("General Settings"), ref _generalSettings)) return;
            GUILayout.Label(EditorLocalization.Tr("What avatar should be picked on play?"), GestureManagerStyles.SettingsText);
            manager.settings.favourite = GmgLayoutHelper.ComponentField(EditorLocalization.Tr("Favourite Avatar: "), manager.settings.favourite, manager);
            GUILayout.Label(EditorLocalization.Tr("User Data"), GestureManagerStyles.ToolSubHeader);
            GUILayout.Label(EditorLocalization.Tr("The folder will be used to fetch local avatar information!"), GestureManagerStyles.SettingsText);
            manager.settings.userIndex = GmgLayoutHelper.Popup(EditorLocalization.Tr("User ID: "), manager.settings.userIndex, Choose, manager);
            GUILayout.Label(EditorLocalization.Tr("Editor Settings"), GestureManagerStyles.ToolSubHeader);
            GUILayout.Label(EditorLocalization.Tr("Easy toggles for Unity Editor settings!"), GestureManagerStyles.SettingsText);
            using (new GUILayout.HorizontalScope()) BlendShapeSettings();
        }

        private static void SimulationSettings(GestureManager manager)
        {
            if (!GmgLayoutHelper.FoldoutSection(EditorLocalization.Tr("Simulation Settings"), ref _simulationSettings)) return;
            GUILayout.Label(EditorLocalization.Tr("Do you wish to load the local stored parameters values?"), GestureManagerStyles.SettingsText);
            using (new GUILayout.HorizontalScope()) LoadParametersSettings(manager);
            GUILayout.Label(EditorLocalization.Tr("Initial Pose"), GestureManagerStyles.ToolSubHeader);
            GUILayout.Label(EditorLocalization.Tr("Set the initial pose of your avatar!"), GestureManagerStyles.SettingsText);
            manager.settings.initialPose = (VRCLearn.GestureManagerPro.Modules.Pose)GmgLayoutHelper.Popup(EditorLocalization.Tr("Initial Pose: "), (int)manager.settings.initialPose,
                new[] { EditorLocalization.Tr("None"), EditorLocalization.Tr("T Pose"), EditorLocalization.Tr("IK Pose") }, manager);
            GUILayout.Label(EditorLocalization.Tr("Default Parameters"), GestureManagerStyles.ToolSubHeader);
            GUILayout.Label(EditorLocalization.Tr("Set the initial states of the default parameters!"), GestureManagerStyles.SettingsText);
            using (new GUILayout.HorizontalScope()) DefaultParametersSettings(manager);
            manager.settings.isOnFriendsList = GmgLayoutHelper.Toggle(EditorLocalization.Tr("IsOnFriendList: "), manager.settings.isOnFriendsList, manager);
            GUILayout.Label(EditorLocalization.Tr("Avatar Culling"), GestureManagerStyles.ToolSubHeader);
            GUILayout.Label(EditorLocalization.Tr("Simulate VRChat Avatar Culling based on the camera distance."), GestureManagerStyles.SettingsText);
            using (new GUILayout.HorizontalScope()) AvatarCullingSettings(manager);
        }

        private static void AvatarCullingSettings(GestureManager manager, string label = null, float leftValue = 1f, float rightValue = 10f)
        {
            manager.settings.simulateCulling = GmgLayoutHelper.Toggle(EditorLocalization.Tr("Simulate Culling: "), manager.settings.simulateCulling, manager);
            using (new GmgLayoutHelper.GuiEnabled(manager.settings.simulateCulling)) manager.settings.cullingDistance = GmgLayoutHelper.Slider(label, manager.settings.cullingDistance, leftValue, rightValue, manager);
        }

        private static void BlendShapeSettings(string label = "Blend-Shapes Clamping: ")
        {
            PlayerSettings.legacyClampBlendShapeWeights = EditorGUILayout.Toggle(EditorLocalization.Tr(label), PlayerSettings.legacyClampBlendShapeWeights);
            if (PlayerSettings.legacyClampBlendShapeWeights) return;
            GUILayout.FlexibleSpace();
            using (new GmgLayoutHelper.GuiContent(Color.yellow)) GUILayout.Label(EditorLocalization.Tr("[This is recommended to be on]"));
        }

        private static void LoadParametersSettings(GestureManager manager)
        {
            using (new GmgLayoutHelper.GuiEnabled(manager.settings.userIndex != 0))
            {
                var buttonString = GUI.enabled ? EditorLocalization.Tr("Open cache folder") : EditorLocalization.Tr("No user selected!");

                if (!GUI.enabled) GmgLayoutHelper.Toggle(EditorLocalization.Tr("Load stored parameters: "), GUI.enabled, manager);
                else manager.settings.loadStored = GmgLayoutHelper.Toggle(EditorLocalization.Tr("Load stored parameters: "), manager.settings.loadStored, manager);

                if (GUILayout.Button(buttonString)) EditorUtility.RevealInFinder(UserPath(manager.settings.userIndex));
            }
        }

        private static void DefaultParametersSettings(GestureManager manager)
        {
            manager.settings.isRemote = !GmgLayoutHelper.Toggle(EditorLocalization.Tr("IsLocal: "), !manager.settings.isRemote, manager);
            GUILayout.FlexibleSpace();
            manager.settings.vrMode = GmgLayoutHelper.Toggle(EditorLocalization.Tr("VRMode: "), manager.settings.vrMode, manager);
        }
    }
}
