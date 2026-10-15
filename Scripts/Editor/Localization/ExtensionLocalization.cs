using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements;
using VRCLearn.GestureManagerPro.Library.VisualElements;

namespace VRCLearn.GestureManagerPro.Localization
{
    public enum ProLanguage { Automatic, English, SimplifiedChinese }

    public static class EditorLocalization
    {
        private const string PreferenceKey = "com.vrclearn.gesture-manager-pro.language";
        private static readonly TranslationCatalog Catalog = new TranslationCatalog(GestureManagerChinese.Entries, LipSyncChinese.Entries, ModelStatisticsChinese.Entries);
        private static ProLanguage _language = ReadLanguage();
        private static readonly List<CaptionBinding> Captions = new List<CaptionBinding>();

        public static event Action Changed;

        public static ProLanguage Language
        {
            get => _language;
            set
            {
                if (!Enum.IsDefined(typeof(ProLanguage), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_language == value) return;
                _language = value;
                EditorPrefs.SetInt(PreferenceKey, (int)value);
                RefreshCaptions();
                Changed?.Invoke();
                InternalEditorUtility.RepaintAllViews();
            }
        }

        public static string Tr(string source) => Catalog.Translate(source, IsChinese);
        public static string Format(string source, params object[] args) => Catalog.Format(source, IsChinese, args);
        public static bool TryGetSource(string text, out string source) => Catalog.TryGetSource(text, out source);

        public static void ApplyFontPreferences(VisualElement root) => EditorFontPreferences.Apply(root);

        public static void RegisterTranslations(IReadOnlyDictionary<string, string> translations)
        {
            if (translations == null) throw new ArgumentNullException(nameof(translations));
            Catalog.Register(translations);
        }

        public static T Bind<T>(T element, string text) where T : TextElement
        {
            if (TryGetSource(text, out var source)) Captions.Add(new CaptionBinding(element, source));
            PruneCaptions();
            element.text = text;
            return element;
        }

        public static GmgTmpRichTextElement Bind(GmgTmpRichTextElement element, string text)
        {
            if (TryGetSource(text, out var source)) Captions.Add(new CaptionBinding(element, source));
            PruneCaptions();
            element.Text = text;
            return element;
        }
        internal static string SourceOf(string text) => Catalog.SourceOf(text);
        internal static bool HasTranslation(string source) => Catalog.Contains(source);
        internal static bool CanTranslate(string source) => Catalog.CanTranslate(source);

        internal static bool IsChinese => Language == ProLanguage.SimplifiedChinese ||
            Language == ProLanguage.Automatic && IsSimplifiedChinese(CultureInfo.CurrentUICulture.Name);

        internal static bool IsSimplifiedChinese(string culture) =>
            culture.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
            culture.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            culture.StartsWith("zh-SG", StringComparison.OrdinalIgnoreCase) ||
            culture.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase);

        private static ProLanguage ReadLanguage()
        {
            var value = EditorPrefs.GetInt(PreferenceKey, 0);
            return Enum.IsDefined(typeof(ProLanguage), value) ? (ProLanguage)value : ProLanguage.Automatic;
        }

        private static void RefreshCaptions()
        {
            for (var i = Captions.Count - 1; i >= 0; i--)
                if (!Captions[i].Refresh()) Captions.RemoveAt(i);
        }

        private static void PruneCaptions()
        {
            if (Captions.Count > 0 && Captions.Count % 64 == 0) Captions.RemoveAll(binding => !binding.IsAlive);
        }

        // Weak references keep cached radial labels switchable without retaining
        // discarded inspectors, menus, or user-provided expression controls.
        private sealed class CaptionBinding
        {
            private readonly WeakReference<VisualElement> _element;
            private readonly string _source;

            internal CaptionBinding(VisualElement element, string source)
            {
                _element = new WeakReference<VisualElement>(element);
                _source = source;
            }

            internal bool IsAlive => _element.TryGetTarget(out _);

            internal bool Refresh()
            {
                if (!_element.TryGetTarget(out var element)) return false;
                if (element is TextElement label) label.text = Tr(_source);
                else if (element is GmgTmpRichTextElement rich) rich.Text = Tr(_source);
                return true;
            }
        }
    }
}
