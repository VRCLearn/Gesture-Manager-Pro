using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace VRCLearn.GestureManagerPro.Localization
{
    internal sealed class TranslationCatalog
    {
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)(?:,-?\d+)?(?::[^{}]+)?\}", RegexOptions.CultureInvariant);
        private readonly Dictionary<ProLanguage, Dictionary<string, string>> _translations = new Dictionary<ProLanguage, Dictionary<string, string>>();
        private readonly Dictionary<string, string> _english = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Template> _templates = new Dictionary<string, Template>(StringComparer.Ordinal);
        private readonly ConditionalWeakTable<string, SourceText> _localizedSources = new ConditionalWeakTable<string, SourceText>();

        internal void Register(ProLanguage language, params IReadOnlyDictionary<string, string>[] tables)
        {
            if (language == ProLanguage.Automatic || !Enum.IsDefined(typeof(ProLanguage), language))
                throw new ArgumentOutOfRangeException(nameof(language));
            if (!_translations.TryGetValue(language, out var translations))
                _translations.Add(language, translations = new Dictionary<string, string>(StringComparer.Ordinal));
            foreach (var table in tables)
            foreach (var pair in table)
            {
                if (translations.TryGetValue(pair.Key, out var previous))
                {
                    if (previous != pair.Value) throw new ArgumentException("Conflicting translation: " + pair.Key);
                    continue;
                }
                // Equal captions from different keys keep distinct string identities.
                var source = new SourceText(pair.Key);
                translations.Add(pair.Key, Remember(new string(pair.Value.ToCharArray()), source));
                if (!_english.ContainsKey(pair.Key)) _english.Add(pair.Key, Remember(new string(pair.Key.ToCharArray()), source));
                if (Placeholder.IsMatch(pair.Key) && !_templates.ContainsKey(pair.Key)) _templates.Add(pair.Key, new Template(pair.Key));
            }
        }

        internal bool TryGetSource(string text, out string source)
        {
            source = null;
            if (text == null || !_localizedSources.TryGetValue(text, out var exact)) return false;
            // Export the original wording without discarding parameter boundaries.
            // Clone it so an interned string equal to a user name never gets tagged.
            source = Remember(new string(Original(exact).ToCharArray()), exact);
            return true;
        }

        internal string Format(string source, ProLanguage language, params object[] args)
        {
            var key = SourceOf(source);
            var values = args.Select(arg => arg is string text && _localizedSources.TryGetValue(text, out var caption) ? caption : arg).ToArray();
            return Render(new SourceText(key, values), language);
        }

        internal bool Contains(string source) => source != null && _english.ContainsKey(source);
        internal bool CanTranslate(string text) => Contains(text) || text != null &&
            (_localizedSources.TryGetValue(text, out _) || _templates.Values.Any(template => template.English.IsMatch(text)));
        internal string SourceOf(string text) => text != null && _localizedSources.TryGetValue(text, out var exact) ? Original(exact) : text;

        internal string Translate(string text, ProLanguage language)
        {
            if (text == null) return null;
            if (_localizedSources.TryGetValue(text, out var exact)) return Render(exact, language);
            if (_english.ContainsKey(text)) return Render(new SourceText(text), language);
            // Runtime diagnostics can arrive as English exception messages. Only
            // source-language templates may parse those untagged strings.
            foreach (var template in _templates.Values)
            {
                var match = template.English.Match(text);
                if (!match.Success) continue;
                var args = new object[template.ArgumentCount];
                for (var i = 0; i < args.Length; i++) args[i] = match.Groups["p" + i].Value;
                return Render(new SourceText(template.Source, args), language);
            }
            return text;
        }

        private string Render(SourceText source, ProLanguage language)
        {
            var format = _translations.TryGetValue(language, out var table) && table.TryGetValue(source.Key, out var translated)
                ? translated : _english.TryGetValue(source.Key, out var english) ? english : source.Key;
            if (source.Arguments == null) return format;
            var values = source.Arguments.Select(arg => arg is SourceText caption ? Render(caption, language) : arg).ToArray();
            if (_templates.TryGetValue(source.Key, out var template))
                foreach (var index in template.SemanticArguments)
                    if (index < values.Length && values[index] is string text) values[index] = Translate(text, language);
            return Remember(string.Format(CultureInfo.CurrentCulture, format, values), source);
        }

        private static string Original(SourceText source)
        {
            if (source.Arguments == null) return source.Key;
            var args = source.Arguments.Select(arg => arg is SourceText caption ? Original(caption) : arg).ToArray();
            return string.Format(CultureInfo.CurrentCulture, source.Key, args);
        }

        private string Remember(string text, SourceText source)
        {
            if (!_localizedSources.TryGetValue(text, out _)) _localizedSources.Add(text, source);
            return text;
        }

        private sealed class SourceText
        {
            internal readonly string Key;
            internal readonly object[] Arguments;
            internal SourceText(string key, object[] arguments = null) { Key = key; Arguments = arguments; }
        }

        private sealed class Template
        {
            internal readonly string Source;
            internal readonly Regex English;
            internal readonly int ArgumentCount;
            internal readonly int[] SemanticArguments;

            internal Template(string source)
            {
                Source = source;
                ArgumentCount = Placeholder.Matches(source).Cast<Match>().Max(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)) + 1;
                SemanticArguments = source.Contains("{0}-Mode") || source == "Send Custom {0}" ||
                    source == "OVRLipSync could not {0}: native error {1}." || source == "Audio processing failed: {0}"
                    ? new[] { 0 } : source == "Avatar: {0}\nLip sync mode: {1}" ? new[] { 1 } : Array.Empty<int>();
                var pattern = new StringBuilder(@"\A");
                var offset = 0;
                foreach (Match match in Placeholder.Matches(source))
                {
                    pattern.Append(Regex.Escape(source.Substring(offset, match.Index - offset)));
                    pattern.Append("(?<p").Append(match.Groups[1].Value).Append(">.*?)");
                    offset = match.Index + match.Length;
                }
                pattern.Append(Regex.Escape(source.Substring(offset))).Append(@"\z");
                English = new Regex(pattern.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            }
        }
    }
}
