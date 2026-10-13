using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace VRCLearn.GestureManagerPro.Localization
{
    internal sealed class TranslationCatalog
    {
        private readonly Dictionary<string, string> _chinese = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _english = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _sources = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<Template> _templates = new List<Template>();
        private readonly ConditionalWeakTable<string, SourceText> _localizedSources = new ConditionalWeakTable<string, SourceText>();
        private readonly HashSet<string> _ambiguous = new HashSet<string>(StringComparer.Ordinal);

        internal TranslationCatalog(params IReadOnlyDictionary<string, string>[] tables) => Register(tables);

        internal void Register(params IReadOnlyDictionary<string, string>[] tables)
        {
            foreach (var table in tables)
            foreach (var pair in table)
            {
                if (_chinese.TryGetValue(pair.Key, out var previous) && previous != pair.Value)
                    throw new ArgumentException("Conflicting translation: " + pair.Key);
                if (!_chinese.ContainsKey(pair.Key))
                {
                    // Equal translations can come from different source keys. Keep
                    // distinct string objects so cached controls retain the exact key.
                    var value = new string(pair.Value.ToCharArray());
                    _chinese.Add(pair.Key, value);
                    _localizedSources.Add(value, new SourceText(pair.Key));
                    var english = new string(pair.Key.ToCharArray());
                    _english.Add(pair.Key, english);
                    _localizedSources.Add(english, new SourceText(pair.Key));
                    if (Regex.IsMatch(pair.Key, @"\{\d+(?::[^{}]+)?\}")) _templates.Add(new Template(pair.Key, value));
                }
                if (_sources.TryGetValue(pair.Value, out var existing) && existing != pair.Key) _ambiguous.Add(pair.Value);
                else if (!_sources.ContainsKey(pair.Value)) _sources.Add(pair.Value, pair.Key);
            }
            foreach (var value in _ambiguous) _sources.Remove(value);
        }

        internal bool TryGetSource(string text, out string source)
        {
            source = null;
            if (text == null || !_localizedSources.TryGetValue(text, out var exact)) return false;
            source = exact.Value;
            return true;
        }

        internal string Format(string source, bool chinese, params object[] args)
        {
            var canonical = args.Select(arg => arg is string text && TryGetSource(text, out var original) ? original : arg).ToArray();
            var result = string.Format(CultureInfo.CurrentCulture, Translate(source, chinese), args);
            if (!_localizedSources.TryGetValue(result, out _))
                _localizedSources.Add(result, new SourceText(string.Format(CultureInfo.CurrentCulture, source, canonical)));
            return result;
        }

        internal bool Contains(string source) => source != null && _chinese.ContainsKey(source);
        internal bool CanTranslate(string text) => Contains(text) || text != null && _templates.Any(template => template.English.IsMatch(text) || template.Chinese.IsMatch(text));

        internal string SourceOf(string text)
        {
            if (text == null) return null;
            if (_localizedSources.TryGetValue(text, out var exact)) return exact.Value;
            if (_chinese.ContainsKey(text)) return text;
            if (_sources.TryGetValue(text, out var source)) return source;
            foreach (var template in _templates)
            {
                var match = template.Chinese.Match(text);
                if (!match.Success) continue;
                var args = Arguments(template, match);
                if (template.SemanticFirstArgument) args[0] = SourceOf((string)args[0]);
                return string.Format(CultureInfo.CurrentCulture, template.Source, args);
            }
            return text;
        }

        internal string Translate(string text, bool chinese)
        {
            if (text == null) return null;
            var source = SourceOf(text);
            if (_chinese.TryGetValue(source, out var translated)) return chinese ? translated : _english[source];
            foreach (var template in _templates)
            {
                var match = template.English.Match(source);
                if (!match.Success) match = template.Chinese.Match(text);
                if (!match.Success) continue;
                var args = Arguments(template, match);
                var canonical = (object[])args.Clone();
                if (template.SemanticFirstArgument || template.Source == "OVRLipSync could not {0}: native error {1}.")
                    canonical[0] = SourceOf((string)canonical[0]);
                if (template.SemanticFirstArgument) args[0] = Translate((string)args[0], chinese);
                if (template.Source == "OVRLipSync could not {0}: native error {1}.")
                    args[0] = Translate((string)args[0], chinese);
                if (template.Source == "Audio processing failed: {0}")
                    args[0] = Translate((string)args[0], chinese);
                var result = string.Format(CultureInfo.CurrentCulture, chinese ? template.Translation : template.Source, args);
                if (!_localizedSources.TryGetValue(result, out _))
                    _localizedSources.Add(result, new SourceText(string.Format(CultureInfo.CurrentCulture, template.Source, canonical)));
                return result;
            }
            return text;
        }

        private static object[] Arguments(Template template, Match match)
        {
            var args = new object[template.ArgumentCount];
            for (var i = 0; i < args.Length; i++) args[i] = match.Groups["p" + i].Value;
            return args;
        }

        private sealed class SourceText
        {
            internal readonly string Value;
            internal SourceText(string value) { Value = value; }
        }

        private sealed class Template
        {
            internal readonly string Source, Translation;
            internal readonly Regex English, Chinese;
            internal readonly int ArgumentCount;
            internal readonly bool SemanticFirstArgument;

            internal Template(string source, string translation)
            {
                Source = source;
                Translation = translation;
                ArgumentCount = Regex.Matches(source, @"\{(\d+)(?::[^{}]+)?\}").Cast<Match>().Max(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)) + 1;
                SemanticFirstArgument = source.Contains("{0}-Mode") || source == "Send Custom {0}";
                English = Pattern(source);
                Chinese = Pattern(translation);
            }

            private static Regex Pattern(string format)
            {
                var pattern = Regex.Replace(Regex.Escape(format), @"\\\{(\d+)(?::[^}]+)?\}", match => "(?<p" + match.Groups[1].Value + ">.*?)");
                return new Regex("\\A" + pattern + "\\z", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            }
        }
    }
}
