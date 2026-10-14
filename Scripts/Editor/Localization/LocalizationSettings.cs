using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Localization
{
    internal static class LocalizationSettings
    {
        [MenuItem("Tools/Language Settings")]
        private static void Open() => SettingsService.OpenUserPreferences("Preferences/Gesture Manager Pro");

        [SettingsProvider]
        private static SettingsProvider Create() => new SettingsProvider("Preferences/Gesture Manager Pro", SettingsScope.User)
        {
            label = "Gesture Manager Pro",
            keywords = new HashSet<string> { "Language", "Chinese", "Gesture Manager", "语言", "中文" },
            guiHandler = _ => Draw()
        };

        private static void Draw()
        {
            GUILayout.Label(EditorLocalization.Tr("Interface language"), EditorStyles.boldLabel);
            var options = new[] { EditorLocalization.Tr("Automatic"), "English", "简体中文" };
            var selected = EditorGUILayout.Popup(EditorLocalization.Tr("Language"), (int)EditorLocalization.Language, options);
            EditorLocalization.Language = (ProLanguage)selected;
            EditorGUILayout.HelpBox(EditorLocalization.Tr("Automatic follows the operating system language. Missing translations fall back to English. Avatar menu names and parameter identifiers are preserved."), MessageType.Info);
        }
    }

    internal static class SettingsChinese
    {
        internal static readonly IReadOnlyDictionary<string, string> Entries = new Dictionary<string, string>
        {
            ["Interface language"] = "界面语言",
            ["Automatic follows the operating system language. Missing translations fall back to English. Avatar menu names and parameter identifiers are preserved."] = "自动模式跟随操作系统语言。缺失的翻译回退为英语。模型菜单名称和参数标识符保持原样。"
        };
    }
}
