using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using VRCLearn.GestureManagerPro.Diagnostics;
using VRCLearn.GestureManagerPro.Localization;

// Run only in the disposable fixture, alongside the other editor integration checks.
public static class UnityProFontChecks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public static void Run(Action<bool, string> check)
    {
        var language = EditorLocalization.Language;
        var culture = CultureInfo.CurrentUICulture;
        var service = typeof(EditorLocalization).Assembly.GetType("VRCLearn.GestureManagerPro.Localization.EditorFontPreferences", true);
        var refresh = service.GetMethod("Refresh", Any);
        var roots = (HashSet<VisualElement>)service.GetField("Roots", Any).GetValue(null);
        var originalRootCount = roots.Count;
        var windows = new List<EditorWindow>();
        try
        {
            EditorLocalization.Language = ProLanguage.SimplifiedChinese;
            windows.Add(ScriptableObject.CreateInstance<ModelStatisticsWindow>());
            windows.Add(ScriptableObject.CreateInstance<ProValidationWindow>());
            windows.Add(ScriptableObject.CreateInstance<ProValidationWindow>());
            foreach (var window in windows)
            {
                window.Show();
                if (window is ModelStatisticsWindow) window.GetType().GetMethod("CreateGUI").Invoke(window, null);
                else EditorLocalization.ApplyFontPreferences(window.rootVisualElement);
            }
            var root = windows[0].rootVisualElement;
            var chinese = root.style.unityFontDefinition.value.fontAsset;
            var chain = new[] { chinese }.Concat(chinese.fallbackFontAssetTable).ToArray();
            check(chinese && chinese.name.StartsWith("GestureManagerPro.UIFont.zh-Hans.", StringComparison.Ordinal) &&
                windows.All(window => window.rootVisualElement.style.unityFontDefinition.value.fontAsset == chinese),
                "Production statistics and two independent UI Toolkit roots share the selected language's owned font chain");
            check(chain.Any(font => font.faceInfo.familyName == "Microsoft YaHei UI") &&
                chain.All(font => font.fontWeightTable.All(pair => !pair.regularTypeface && !pair.italicTypeface)),
                "Simplified Chinese includes the configured OS face and uses synthetic styles without alternate weight assets");
            check(chain.All(font => font.hideFlags == HideFlags.HideAndDontSave &&
                font.material.hideFlags == HideFlags.HideAndDontSave &&
                font.atlasTextures.Take(font.atlasTextureCount).All(texture => texture && texture.hideFlags == HideFlags.HideAndDontSave)),
                "Dynamic font assets, materials and atlas pages are retained across editor scene and mode changes");
            check(roots.Count == originalRootCount + windows.Count,
                "Each attached window registers exactly one font update target");
            EditorLocalization.ApplyFontPreferences(root);
            check(roots.Count == originalRootCount + windows.Count && root.style.unityFontDefinition.value.fontAsset == chinese,
                "Repeated font setup reuses the cached chain and does not duplicate attachment subscriptions");

            EditorLocalization.Language = ProLanguage.English;
            var english = root.style.unityFontDefinition.value.fontAsset;
            check(english && english != chinese && windows.All(window =>
                window.rootVisualElement.style.unityFontDefinition.value.fontAsset == english),
                "Language changes update every attached window without changing the selected language");
            EditorLocalization.Language = ProLanguage.SimplifiedChinese;
            check(root.style.unityFontDefinition.value.fontAsset == chinese,
                "Switching back reuses the Chinese font chain rather than allocating per heading or window");
            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
            EditorLocalization.Language = ProLanguage.Automatic;
            check(root.style.unityFontDefinition.value.fontAsset == chinese,
                "Automatic font language follows the same culture policy as the displayed translations");

            var cjk = chain.First(font => font.faceInfo.familyName == "Microsoft YaHei UI");
            for (var offset = 0; offset < 512 && cjk.atlasTextureCount < 3; offset += 32)
                cjk.TryAddCharacters(new string(Enumerable.Range(0x4e00 + offset, 32).Select(value => (char)value).ToArray()), out _);
            var pages = cjk.atlasTextures.Take(cjk.atlasTextureCount).ToArray();
            check(cjk.atlasTextureCount >= 3 && cjk.atlasTextures.Length > cjk.atlasTextureCount &&
                cjk.atlasTextures.Skip(cjk.atlasTextureCount).Any(texture => !texture),
                "A real multi-page CJK font reserves unused null atlas slots beyond its active pages");
            var resourceIds = chain.SelectMany(font => new UnityEngine.Object[] { font, font.material }
                .Concat(font.atlasTextures.Take(font.atlasTextureCount).Cast<UnityEngine.Object>()))
                .Select(resource => resource.GetInstanceID()).ToArray();
            for (var update = 0; update < 200; update++) refresh.Invoke(null, null);
            check(windows.All(window => window.rootVisualElement.style.unityFontDefinition.value.fontAsset == chinese) &&
                chain.SelectMany(font => new UnityEngine.Object[] { font, font.material }
                    .Concat(font.atlasTextures.Take(font.atlasTextureCount).Cast<UnityEngine.Object>()))
                    .Select(resource => resource.GetInstanceID()).SequenceEqual(resourceIds),
                "Two hundred font updates preserve the chain, materials and active atlas pages despite reserved null slots");
            check(pages.All(texture => texture && texture.hideFlags == HideFlags.HideAndDontSave),
                "New active atlas pages are retained without touching unused capacity slots");

            // Simulate Unity invalidating a dynamic child resource without changing the bound FontAsset reference.
            UnityEngine.Object.DestroyImmediate(chain.Last().material);
            refresh.Invoke(null, null);
            var repaired = root.style.unityFontDefinition.value.fontAsset;
            check(!chinese && repaired && repaired != chinese && windows.All(window =>
                window.rootVisualElement.style.unityFontDefinition.value.fontAsset == repaired),
                "A damaged fallback resource rebuilds the complete chain and rebinds all attached windows");
            UnityEngine.Object.DestroyImmediate(repaired.atlasTextures[0]);
            refresh.Invoke(null, null);
            var atlasRepaired = root.style.unityFontDefinition.value.fontAsset;
            check(!repaired && atlasRepaired && windows.All(window =>
                window.rootVisualElement.style.unityFontDefinition.value.fontAsset == atlasRepaired),
                "A destroyed active atlas page still triggers complete-chain recovery");
            repaired = atlasRepaired;
            var last = windows.Last();
            windows.Remove(last);
            last.Close();
            check(roots.Count == originalRootCount + windows.Count && repaired && repaired.material,
                "Closing one window unregisters it while preserving the fonts used by other windows");
        }
        finally
        {
            foreach (var window in windows) if (window) window.Close();
            CultureInfo.CurrentUICulture = culture;
            EditorLocalization.Language = language;
        }
        check(roots.Count == originalRootCount, "Closed font-check windows leave no stale update targets");
    }
}
