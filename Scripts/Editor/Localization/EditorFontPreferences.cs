using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace VRCLearn.GestureManagerPro.Localization
{
    [InitializeOnLoad]
    internal static class EditorFontPreferences
    {
        private const string PreferencesResource = "GestureManagerPro/EditorFontPreferences";
        private static readonly Dictionary<ProLanguage, FontAsset[]> Fonts = new Dictionary<ProLanguage, FontAsset[]>();
        private static readonly HashSet<VisualElement> Roots = new HashSet<VisualElement>();

        static EditorFontPreferences()
        {
            EditorLocalization.Changed += Refresh;
            EditorApplication.update += Refresh;
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorApplication.quitting += Release;
        }

        internal static void Apply(VisualElement root)
        {
            root.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            root.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            root.RegisterCallback<AttachToPanelEvent>(OnAttach);
            root.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            if (root.panel != null) Roots.Add(root);
            SetFont(root, GetFonts());
        }

        private static void OnAttach(AttachToPanelEvent evt)
        {
            var root = (VisualElement)evt.currentTarget;
            Roots.Add(root);
            SetFont(root, GetFonts());
        }

        private static void OnDetach(DetachFromPanelEvent evt) => Roots.Remove((VisualElement)evt.currentTarget);

        private static void Refresh()
        {
            if (Roots.Count == 0) return;
            var fonts = GetFonts();
            foreach (var root in Roots) SetFont(root, fonts);
        }

        private static void SetFont(VisualElement root, FontAsset[] fonts)
        {
            var font = fonts.FirstOrDefault();
            if (root.style.unityFontDefinition.value.fontAsset == font) return;
            root.style.unityFontDefinition = font
                ? new StyleFontDefinition(FontDefinition.FromSDFFont(font))
                : new StyleFontDefinition(StyleKeyword.Null);
        }

        private static FontAsset[] GetFonts()
        {
            var language = EditorLocalization.EffectiveLanguage;
            if (!Fonts.TryGetValue(language, out var fonts) || !fonts.All(IsAlive))
            {
                if (fonts != null) DestroyFonts(fonts);
                fonts = LoadFonts(language);
                Fonts[language] = fonts;
            }
            // TextCore may add atlas pages after creation. Keep those alive across Play Mode as well.
            foreach (var font in fonts) PreserveResources(font);
            return fonts;
        }

        private static FontAsset[] LoadFonts(ProLanguage selectedLanguage)
        {
            var preferences = Resources.Load<UnityEngine.TextAsset>(PreferencesResource);
            if (!preferences) return Array.Empty<FontAsset>();
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fonts = new List<FontAsset>();
            var language = EditorLocalization.LanguageCode(selectedLanguage);
            foreach (var entry in preferences.text.Split('\n'))
            {
                var line = entry.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var separator = line.IndexOf('=');
                if (separator < 0) continue;
                var prefix = line.Substring(0, separator).Trim();
                var family = line.Substring(separator + 1).Trim();
                if (!language.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    !installed.Contains(family) || !selected.Add(family)) continue;
                var font = FontAsset.CreateFontAsset(family, "");
                if (!font) continue;
                font.name = "GestureManagerPro.UIFont." + language + "." + family;
                font.ReadFontAssetDefinition();
                PreserveResources(font);
                fonts.Add(font);
            }
            // Fresh regular faces have no alternate weight assets: bold uses the same glyphs with synthetic weight.
            if (fonts.Count > 0) fonts[0].fallbackFontAssetTable = fonts.Skip(1).ToList();
            return fonts.ToArray();
        }

        private static bool IsAlive(FontAsset font)
        {
            if (!font || !font.material) return false;
            var textures = font.atlasTextures;
            var count = font.atlasTextureCount;
            if (textures == null || count <= 0 || count > textures.Length) return false;
            for (var index = 0; index < count; index++)
                if (!textures[index]) return false;
            return true;
        }

        private static void PreserveResources(FontAsset font)
        {
            Preserve(font);
            Preserve(font.material);
            var textures = font.atlasTextures;
            for (var index = 0; index < font.atlasTextureCount; index++) Preserve(textures[index]);
        }

        private static void Preserve(UnityEngine.Object resource)
        {
            if (resource.hideFlags != HideFlags.HideAndDontSave) resource.hideFlags = HideFlags.HideAndDontSave;
        }

        private static void DestroyFonts(IEnumerable<FontAsset> fonts)
        {
            foreach (var font in fonts)
                if (font) UnityEngine.Object.DestroyImmediate(font);
        }

        private static void Release()
        {
            foreach (var root in Roots) root.style.unityFontDefinition = StyleKeyword.Null;
            foreach (var fonts in Fonts.Values) DestroyFonts(fonts);
            Fonts.Clear();
        }
    }
}
