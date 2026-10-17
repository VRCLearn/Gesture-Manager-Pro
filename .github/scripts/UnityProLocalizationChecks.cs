using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;
using VRCLearn.GestureManagerPro.Editor.Data;
using VRCLearn.GestureManagerPro.Localization;

// Run inside the disposable fixture's existing editor validation assembly.
public static class UnityProLocalizationChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run(Action<bool, string> check)
    {
        var original = EditorLocalization.Language;
        var culture = CultureInfo.CurrentUICulture;
        var languages = new[] { ProLanguage.English, ProLanguage.Japanese, ProLanguage.Korean, ProLanguage.TraditionalChinese, ProLanguage.SimplifiedChinese };
        try
        {
            check((int)ProLanguage.Automatic == 0 && (int)ProLanguage.English == 1 && (int)ProLanguage.SimplifiedChinese == 2,
                "Existing persisted language values retain their meanings");
            var values = (ProLanguage[])typeof(GestureManagerSettings).GetField("LanguageValues", Any).GetValue(null);
            check(values.SequenceEqual(new[] { ProLanguage.Automatic }.Concat(languages)),
                "The settings selector lists Automatic followed by English, Japanese, Korean, Traditional and Simplified Chinese");
            var resolve = typeof(EditorLocalization).GetMethod("ResolveLanguage", Any);
            foreach (var pair in new[]
            {
                ("en-US", ProLanguage.English), ("ja-JP", ProLanguage.Japanese), ("ko-KR", ProLanguage.Korean),
                ("zh-TW", ProLanguage.TraditionalChinese), ("zh-HK", ProLanguage.TraditionalChinese), ("zh-MO", ProLanguage.TraditionalChinese),
                ("zh-Hant-CN", ProLanguage.TraditionalChinese), ("zh-Hans-HK", ProLanguage.SimplifiedChinese),
                ("zh-CN", ProLanguage.SimplifiedChinese), ("zh-SG", ProLanguage.SimplifiedChinese), ("fr-FR", ProLanguage.English)
            })
                check((ProLanguage)resolve.Invoke(null, new object[] { pair.Item1 }) == pair.Item2,
                    "Automatic locale resolves script/region correctly: " + pair.Item1);

            EditorLocalization.Language = ProLanguage.English;
            var tools = EditorLocalization.Bind(new Label(), EditorLocalization.Tr("Tools"));
            var exit = EditorLocalization.Bind(new Label(), EditorLocalization.Format("Exit {0}-Mode", EditorLocalization.Tr("Test")));
            const string userName = "Tools / 工具 / ツール / 도구 / 工具";
            var metadata = EditorLocalization.Bind(new Label(),
                EditorLocalization.Format("Avatar: {0}\nLip sync mode: {1}", userName, EditorLocalization.Tr("JawFlapBone")));
            var rawUser = EditorLocalization.Bind(new Label(), "Tools");
            var trickyNames = new[] { "A：B", "foo\nLip sync mode: bar", "Avatar: Tools\nLip sync mode: Game Simulation" };
            var trickyLabels = trickyNames.Select(name => EditorLocalization.Bind(new Label(),
                EditorLocalization.Format("Avatar: {0}\nLip sync mode: {1}", name, EditorLocalization.Tr("JawFlapBone")))).ToArray();
            var nestedError = EditorLocalization.Bind(new Label(), EditorLocalization.Format("Audio processing failed: {0}",
                EditorLocalization.Format("OVRLipSync could not {0}: native error {1}.", EditorLocalization.Tr("process audio"), -1)));
            foreach (var language in languages.Concat(languages.Reverse()))
            {
                EditorLocalization.Language = language;
                check(tools.text == EditorLocalization.Tr("Tools"), "Cached captions switch without recreation: " + language);
                check(exit.text == EditorLocalization.Format("Exit {0}-Mode", EditorLocalization.Tr("Test")),
                    "Mode templates reorder and translate their semantic argument: " + language);
                check(metadata.text == EditorLocalization.Format("Avatar: {0}\nLip sync mode: {1}", userName, EditorLocalization.Tr("JawFlapBone")),
                    "Metadata names stay exact while a semantic mode caption switches: " + language);
                check(rawUser.text == "Tools", "A raw user name matching a source key is not bound or translated: " + language);
                var literalCaption = new string(EditorLocalization.Tr("Tools").ToCharArray());
                check(!EditorLocalization.TryGetSource(literalCaption, out _), "An equal user string cannot acquire a caption identity: " + language);
                check(EditorLocalization.Format("Missing mappings: {0}", "sil, aa").Contains("sil, aa"),
                    "Viseme identifiers remain untouched inside localized diagnostics: " + language);
                check(EditorLocalization.Tr("IsOnFriendList: ").Contains("IsOnFriendsList"),
                    "The friend-state display uses the correct parameter identifier: " + language);
                for (var i = 0; i < trickyNames.Length; i++)
                    check(trickyLabels[i].text == EditorLocalization.Format("Avatar: {0}\nLip sync mode: {1}", trickyNames[i], EditorLocalization.Tr("JawFlapBone")),
                        "Structured templates preserve delimiters and newlines inside user names: " + language + " / " + i);
                check(nestedError.text == EditorLocalization.Format("Audio processing failed: {0}",
                    EditorLocalization.Format("OVRLipSync could not {0}: native error {1}.", EditorLocalization.Tr("process audio"), -1)),
                    "Nested diagnostic templates retain their argument identity across locales: " + language);
                check(EditorLocalization.Tr("A：B") == "A：B", "Unmarked text is never reverse-matched against a translated sentence: " + language);
            }
            foreach (var pair in new[] { ("ja-JP", ProLanguage.Japanese), ("ko-KR", ProLanguage.Korean), ("zh-TW", ProLanguage.TraditionalChinese), ("zh-CN", ProLanguage.SimplifiedChinese) })
            {
                EditorLocalization.Language = ProLanguage.English;
                CultureInfo.CurrentUICulture = new CultureInfo(pair.Item1);
                EditorLocalization.Language = ProLanguage.Automatic;
                check(EditorLocalization.EffectiveLanguage == pair.Item2 && tools.text == EditorLocalization.Tr("Tools"),
                    "Automatic language uses the same locale for new and cached text: " + pair.Item1);
            }
            EditorLocalization.Language = ProLanguage.SimplifiedChinese;
            check(EditorLocalization.Tr("Brightness") == "明度" && EditorLocalization.Tr("Value") != "明度",
                "The color wheel no longer reuses the parameter-value caption");
            check(EditorLocalization.Tr("Excellent") == "优秀" && EditorLocalization.Tr("Very Poor") == "极差",
                "SDK rating translations express ratings rather than frame-rate/load claims");
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
            EditorLocalization.Language = original;
        }
    }
}
