using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using UnityEngine;

namespace RealGlass
{
    /// <summary>
    /// The text of RealGlass.xml: writing it, reading it back, and the number, switch
    /// and key formats in it. Knows nothing about the catalog or the game, and makes no engine
    /// call (KeyCode is a plain enum), so tools/test-settings.ps1 runs it against the built DLL
    /// with no game.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than with XmlSerializer, which would need a class with one field
    /// per setting: a third list of every setting next to Settings and the catalog, and the
    /// one that drifts. This way the catalog is the list, and every value can carry a comment
    /// with its label, its range and its default.
    ///
    /// Everything is INVARIANT culture. A player whose Windows writes "0,5" still gets "0.5" in
    /// the file, and a "0,5" typed by hand is refused rather than read as 5.
    /// </remarks>
    public static class SettingsXmlFormat
    {
        public const string RootName = "RealGlass";

        /// <summary>Goes up when a name or a unit in the file changes meaning.</summary>
        public const int Version = 1;

        private const string NewLine = "\r\n";

        // SavedInputKey's encoding, read from ColossalManaged's IL (Encode, get_Key/Control/...).
        private const int KeyMask = 0x0FFFFFFF;
        private const int ControlBit = 0x40000000;
        private const int ShiftBit = 0x20000000;
        private const int AltBit = 0x10000000;

        public sealed class Entry
        {
            /// <summary>A heading written before the first entry of each new section.</summary>
            public string Section;

            /// <summary>Non-null: a smaller heading written just before this entry.</summary>
            public string Group;

            public string Name;
            public string Value;
            public string Comment;
        }

        public static string Write(IList<Entry> entries, string header)
        {
            var text = new StringBuilder(16384);
            text.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>").Append(NewLine);

            if (!string.IsNullOrEmpty(header))
            {
                text.Append("<!--").Append(NewLine);
                foreach (string line in header.Split('\n'))
                    text.Append(("  " + CommentSafe(line)).TrimEnd()).Append(NewLine);
                text.Append("-->").Append(NewLine);
            }

            text.Append('<').Append(RootName).Append(" version=\"")
                .Append(Version.ToString(CultureInfo.InvariantCulture)).Append("\">").Append(NewLine);

            string section = null;
            foreach (Entry entry in entries)
            {
                if (entry.Section != section)
                {
                    section = entry.Section;
                    text.Append(NewLine).Append("  <!-- ========== ").Append(CommentSafe(section))
                        .Append(" ========== -->").Append(NewLine);
                }

                if (!string.IsNullOrEmpty(entry.Group))
                    text.Append(NewLine).Append("  <!-- [").Append(CommentSafe(entry.Group)).Append("] -->").Append(NewLine);

                text.Append(NewLine);
                if (!string.IsNullOrEmpty(entry.Comment))
                    text.Append("  <!-- ").Append(CommentSafe(entry.Comment)).Append(" -->").Append(NewLine);

                text.Append("  <").Append(entry.Name).Append('>')
                    .Append(Escape(entry.Value))
                    .Append("</").Append(entry.Name).Append('>').Append(NewLine);
            }

            text.Append(NewLine).Append("</").Append(RootName).Append('>').Append(NewLine);
            return text.ToString();
        }

        /// <summary>
        /// Every value in the file by name, names matched without regard to case. An element
        /// that holds other elements is looked into rather than read, so wrapping lines in a
        /// group of your own does no harm. Throws <see cref="FormatException"/>, with the
        /// parser's line and position, when the text is not a settings file at all.
        /// </summary>
        public static Dictionary<string, string> Parse(string text, out int version, List<string> duplicates)
        {
            version = 0;

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

            int.TryParse(root.GetAttribute("version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out version);

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Collect(root, values, duplicates);
            return values;
        }

        private static void Collect(XmlElement parent, Dictionary<string, string> values, List<string> duplicates)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                var element = node as XmlElement;
                if (element == null)
                    continue;

                if (HasChildElements(element))
                {
                    Collect(element, values, duplicates);
                    continue;
                }

                if (values.ContainsKey(element.Name) && duplicates != null)
                    duplicates.Add(element.Name);

                // The last one wins, as it would for anyone reading the file top to bottom.
                values[element.Name] = element.InnerText.Trim();
            }
        }

        private static bool HasChildElements(XmlElement element)
        {
            foreach (XmlNode node in element.ChildNodes)
            {
                if (node is XmlElement)
                    return true;
            }

            return false;
        }

        // ---- values ---------------------------------------------------------------------------

        /// <summary>
        /// Up to four decimals and no trailing zeros: 47, 3.3, -0.4, 13500. Enough for every
        /// slider step in the mod, and it brings float noise (0.47f * 100 = 46.99999...) back
        /// to the number the slider shows.
        /// </summary>
        public static string FormatNumber(double value)
        {
            double rounded = Math.Round(value, 4);
            if (rounded == 0.0)
                rounded = 0.0; // never "-0"

            return rounded.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A number with a dot for decimals. A trailing % is allowed ("47%"); a comma is not,
        /// either way it is meant. NaN and infinities are refused: no slider can produce them,
        /// and a shader handed one draws black.
        /// </summary>
        public static bool TryParseNumber(string text, out double value)
        {
            value = 0.0;
            if (text == null)
                return false;

            string trimmed = text.Trim();
            if (trimmed.EndsWith("%"))
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;

            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        /// <summary>true / false, and the obvious other spellings: 1 / 0, yes / no, on / off.</summary>
        public static bool TryParseBool(string text, out bool value)
        {
            value = false;
            if (text == null)
                return false;

            switch (text.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                case "on":
                    value = true;
                    return true;

                case "false":
                case "0":
                case "no":
                case "off":
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>"F4", "Ctrl+Shift+C", "None": what a SavedInputKey holds, readable.</summary>
        public static string FormatKey(int encoded)
        {
            int code = encoded & KeyMask;
            if (code == 0)
                return "None";

            var text = new StringBuilder();
            if ((encoded & ControlBit) != 0) text.Append("Ctrl+");
            if ((encoded & ShiftBit) != 0) text.Append("Shift+");
            if ((encoded & AltBit) != 0) text.Append("Alt+");

            text.Append(Enum.IsDefined(typeof(KeyCode), code)
                ? Enum.GetName(typeof(KeyCode), code)
                : code.ToString(CultureInfo.InvariantCulture));

            return text.ToString();
        }

        /// <summary>
        /// The reverse of <see cref="FormatKey"/>. Modifiers in any order and any case (Ctrl or
        /// Control), then one of Unity's key names; a lone digit means that key on the top row.
        /// </summary>
        public static bool TryParseKey(string text, out int encoded)
        {
            encoded = 0;
            if (string.IsNullOrEmpty(text))
                return false;

            string[] parts = text.Split('+');
            int modifiers = 0;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ControlBit;
                        break;
                    case "shift":
                        modifiers |= ShiftBit;
                        break;
                    case "alt":
                        modifiers |= AltBit;
                        break;
                    default:
                        return false;
                }
            }

            string name = parts[parts.Length - 1].Trim();
            if (name.Length == 0)
                return false;

            if (string.Equals(name, "None", StringComparison.OrdinalIgnoreCase))
                return true;

            if (name.Length == 1 && char.IsDigit(name[0]))
                name = "Alpha" + name;

            // Enum.Parse takes a number as a VALUE ("8" would be Backspace) and a comma list as
            // flags; neither is a key name.
            if (char.IsDigit(name[0]) || name[0] == '-' || name.IndexOf(',') >= 0)
                return false;

            KeyCode code;
            try
            {
                code = (KeyCode)Enum.Parse(typeof(KeyCode), name, true);
            }
            catch (ArgumentException)
            {
                return false;
            }

            encoded = (int)code | modifiers;
            return true;
        }

        // ---- text -----------------------------------------------------------------------------

        /// <summary>
        /// An XML comment may not contain "--" or end in "-", and several labels and tooltips
        /// in the catalog use "--" as a dash.
        /// </summary>
        public static string CommentSafe(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string safe = text.Replace("\r", " ").Replace("\n", " ");
            while (safe.IndexOf("--", StringComparison.Ordinal) >= 0)
                safe = safe.Replace("--", "-");

            return safe.EndsWith("-") ? safe + " " : safe;
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
