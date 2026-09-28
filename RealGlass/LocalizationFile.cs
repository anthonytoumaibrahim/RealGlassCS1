using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;

namespace RealGlass
{
    /// <summary>
    /// Reads one language file (Localization/en.xml and its translations). No game and no
    /// engine calls, so tools/test-localization.ps1 checks the real files offline.
    /// </summary>
    /// <remarks>
    /// The format, for translators:
    /// <code>
    /// &lt;Language code="de" name="Deutsch"&gt;
    ///   &lt;String key="FloorHeight.Label"&gt;Geschosshöhe&lt;/String&gt;
    /// &lt;/Language&gt;
    /// </code>
    /// A long text may be wrapped over several lines in the file: a line break there, with the
    /// spaces round it, reads as one space. A real line break is written \n. {0}, {1}... are
    /// filled in by the mod and must be kept.
    /// </remarks>
    public static class LocalizationFile
    {
        public const string RootName = "Language";
        public const string EntryName = "String";

        private static readonly Regex Wrap = new Regex(@"[ \t]*\r?\n[ \t]*", RegexOptions.Compiled);
        private static readonly Regex AroundBreak = new Regex(@" *\n *", RegexOptions.Compiled);

        /// <summary>
        /// Every text in the file by key. Throws <see cref="FormatException"/> (with the
        /// parser's line and position) when the text is not a language file.
        /// </summary>
        public static Dictionary<string, string> Parse(string text, out string code, out string name,
                                                        List<string> duplicates)
        {
            var document = new XmlDocument();
            document.XmlResolver = null;

            try
            {
                document.LoadXml(text ?? string.Empty);
            }
            catch (XmlException e)
            {
                throw new FormatException(e.Message, e);
            }

            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != RootName)
            {
                throw new FormatException("the outermost element is <" + (root == null ? "nothing" : root.Name) +
                                          ">, not <" + RootName + ">");
            }

            code = root.GetAttribute("code");
            name = root.GetAttribute("name");

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XmlNode node in root.ChildNodes)
            {
                var element = node as XmlElement;
                if (element == null || element.Name != EntryName)
                    continue;

                string key = element.GetAttribute("key");
                if (key.Length == 0)
                    continue;

                if (strings.ContainsKey(key) && duplicates != null)
                    duplicates.Add(key);

                strings[key] = Clean(element.InnerText);
            }

            return strings;
        }

        /// <summary>
        /// Wrapped lines joined with one space, trimmed, and \n turned into a line break with
        /// no space either side of it (a wrap straight after a \n would otherwise indent the
        /// next paragraph by one).
        /// </summary>
        public static string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            string joined = Wrap.Replace(raw.Trim(), " ");
            return AroundBreak.Replace(joined.Replace("\\n", "\n"), "\n");
        }
    }
}
