using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using ColossalFramework.Globalization;

namespace RealGlass
{
    /// <summary>
    /// Every word a player reads, by key, in the game's language: Localization/&lt;code&gt;.xml,
    /// embedded in the DLL. English is the source; a key missing from another language falls
    /// back to it, and a key missing from English shows as the key (and is logged once).
    /// </summary>
    /// <remarks>
    /// The language is the player's choice on the options page (<see cref="Settings.Language"/>),
    /// by default the GAME's (LocaleManager.language: en, de, es, fr, ko, pl, pt, ru, zh). It is
    /// looked at on every lookup -- an int, or a property read and a string compare -- so
    /// anything built after it changes comes out in the new one. The in-game panel is rebuilt
    /// when it changes (the setting's AfterChange, and ModController on the game's own locale
    /// event); the options page is rebuilt by the game every time it opens.
    ///
    /// What stays English, always: the log (it is read to fix things) and the element names in
    /// RealGlass.xml (they are keys, not words).
    ///
    /// Adding a language: copy Localization/en.xml to the game's code for it, translate the
    /// text between the tags, rebuild. The csproj embeds every file in that folder.
    /// </remarks>
    public static class Localization
    {
        public const string SourceLanguage = "en";

        /// <summary>
        /// What <see cref="Settings.Language"/> counts, from 1 (0 is "the game's"). APPEND ONLY:
        /// the saved number must keep meaning the same language. The game's own nine first; a
        /// language the game does not have (a community locale) goes on the end.
        /// </summary>
        public static readonly string[] LanguageCodes = { "en", "de", "es", "fr", "ko", "pl", "pt", "ru", "zh" };

        private const string ResourcePrefix = "Localization.";
        private const string ResourceSuffix = ".xml";

        private static readonly Dictionary<string, Dictionary<string, string>> Loaded =
            new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Each file's own name for its language ("English", "Deutsch"), for the dropdown.</summary>
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>();

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static Dictionary<string, string> _english;
        private static Dictionary<string, string> _current;
        private static string _currentCode;

        /// <summary>The text for <paramref name="key"/> in the game's language.</summary>
        public static string Get(string key)
        {
            return Lookup(key, Current());
        }

        /// <summary>The same, with {0}, {1}... filled in.</summary>
        public static string Get(string key, params object[] args)
        {
            return Format(key, Get(key), args);
        }

        /// <summary>True if the English file has this key: the startup self-check.</summary>
        public static bool HasEnglish(string key)
        {
            return English().ContainsKey(key);
        }

        /// <summary>The language code in use, for the log.</summary>
        public static string CurrentCode
        {
            get
            {
                Current();
                return _currentCode;
            }
        }

        /// <summary>
        /// The values the Language dropdown offers: 0 (the game's), then every language that has
        /// a file, by its place in <see cref="LanguageCodes"/>.
        /// </summary>
        public static int[] LanguageChoices()
        {
            var values = new List<int> { 0 };
            List<string> available = Available();

            for (int i = 0; i < LanguageCodes.Length; i++)
            {
                if (available.Contains(LanguageCodes[i]))
                    values.Add(i + 1);
            }

            return values.ToArray();
        }

        /// <summary>A Language dropdown entry: "Same as the game", or the language's name for itself.</summary>
        public static string LanguageName(int value)
        {
            if (value <= 0 || value > LanguageCodes.Length)
                return Get("Language.Game");

            string code = LanguageCodes[value - 1];
            Table(code);

            string name;
            return Names.TryGetValue(code, out name) && !string.IsNullOrEmpty(name) ? name : code;
        }

        private static Dictionary<string, string> Current()
        {
            bool chosen;
            string code = ChosenLanguage(out chosen);
            if (code == _currentCode && _current != null)
                return _current;

            _currentCode = code;
            _current = Table(code) ?? English();

            Log.Msg("localization: " + (chosen ? "chosen" : "game") + " language '" + code + "' -> " +
                    (ReferenceEquals(_current, English()) && code != SourceLanguage
                        ? "no translation, using English"
                        : "using '" + code + "'") +
                    " (" + _current.Count + " texts; available: " + string.Join(", ", Available().ToArray()) + ")");

            return _current;
        }

        /// <summary>The player's choice on the options page, or the game's language.</summary>
        private static string ChosenLanguage(out bool chosen)
        {
            int value = Settings.Language != null ? Settings.Language.value : 0;
            chosen = value > 0 && value <= LanguageCodes.Length;

            return chosen ? LanguageCodes[value - 1] : GameLanguage();
        }

        private static string Lookup(string key, Dictionary<string, string> table)
        {
            string text;
            if (table.TryGetValue(key, out text))
                return text;

            if (!ReferenceEquals(table, English()) && English().TryGetValue(key, out text))
                return text;

            if (Reported.Add(key))
                Log.Warn("localization: no text for '" + key + "' in any language; showing the key");

            return key;
        }

        private static string Format(string key, string text, object[] args)
        {
            if (args == null || args.Length == 0)
                return text;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, text, args);
            }
            catch (FormatException)
            {
                // A translation with a broken {0}: say so once, and fall back to English.
                if (Reported.Add("format:" + key))
                    Log.Warn("localization: the text for '" + key + "' has a broken {n} placeholder: " + text);

                string english;
                if (English().TryGetValue(key, out english) && english != text)
                {
                    try
                    {
                        return string.Format(CultureInfo.InvariantCulture, english, args);
                    }
                    catch (FormatException)
                    {
                    }
                }

                return text;
            }
        }

        /// <summary>
        /// "de", from the game's "de" (or "de-DE" / "de_DE", should a locale mod say so). English
        /// if the game cannot be asked -- at the very start, or outside the game.
        /// </summary>
        private static string GameLanguage()
        {
            try
            {
                if (!LocaleManager.exists)
                    return SourceLanguage;

                string language = LocaleManager.instance.language;
                if (string.IsNullOrEmpty(language))
                    return SourceLanguage;

                int cut = language.IndexOfAny(new[] { '-', '_' });
                return (cut > 0 ? language.Substring(0, cut) : language).ToLowerInvariant();
            }
            catch (Exception)
            {
                return SourceLanguage;
            }
        }

        private static Dictionary<string, string> English()
        {
            if (_english == null)
                _english = Table(SourceLanguage) ?? new Dictionary<string, string>();

            return _english;
        }

        /// <summary>One language, read from the DLL once. Null if there is no such file.</summary>
        private static Dictionary<string, string> Table(string code)
        {
            Dictionary<string, string> table;
            if (Loaded.TryGetValue(code, out table))
                return table;

            table = null;
            try
            {
                Assembly assembly = typeof(Localization).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(ResourcePrefix + code + ResourceSuffix))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            string fileCode, fileName;
                            var duplicates = new List<string>();
                            table = LocalizationFile.Parse(reader.ReadToEnd(), out fileCode, out fileName, duplicates);
                            Names[code] = fileName;

                            if (duplicates.Count > 0)
                            {
                                Log.Warn("localization: " + code + ".xml has keys written twice (the last one is used): " +
                                         string.Join(", ", duplicates.ToArray()));
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("localization: could not read " + code + ".xml", e);
                table = null;
            }

            Loaded[code] = table;
            return table;
        }

        private static List<string> Available()
        {
            var codes = new List<string>();
            foreach (string resource in typeof(Localization).Assembly.GetManifestResourceNames())
            {
                if (resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                    resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                {
                    codes.Add(resource.Substring(ResourcePrefix.Length,
                        resource.Length - ResourcePrefix.Length - ResourceSuffix.Length));
                }
            }

            return codes;
        }
    }
}
