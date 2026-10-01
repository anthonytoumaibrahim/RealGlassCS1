using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;

namespace RealGlass.PerAsset
{
    /// <summary>
    /// The text of RealGlassAssets.xml: every building asset that has glass of its own, one element
    /// each, holding only what it overrides -- under the names those settings have in
    /// RealGlass.xml, written the way the sliders show them (a percentage as 47). Knows nothing
    /// about the game and makes no engine call: tools/test-settings.ps1 runs it on the built DLL.
    /// </summary>
    /// <remarks>
    /// One file for every city, like BOB's: an asset is the same everywhere, so its glass is too.
    /// Nothing of it goes into a save. Read and written as RealGlass.xml is (SettingsXmlFormat's
    /// numbers and comments, ColorText's colours, INVARIANT culture throughout).
    /// </remarks>
    public static class AssetFileFormat
    {
        public const string RootName = "RealGlassAssets";
        public const string AssetElement = "Asset";

        /// <summary>Goes up when a name or a unit in the file changes meaning.</summary>
        public const int Version = 1;

        // The global settings' names (Settings.cs): the asset's own value of each.
        public const string StrengthName = "GlassStrength";
        public const string DetailName = "GlassPaintedDetail";
        public const string PresetName = "GlassPreset";
        public const string ColourName = "GlassColor";
        public const string ReflectivityName = "GlassReflectivity";
        public const string ClarityName = "GlassClarity";
        public const string OwnColourName = "GlassOwnColour";
        public const string ThresholdName = "GlassThreshold";
        public const string NightWindowsName = "GlassNightWindows";

        /// <summary>The sliders' ranges, as the file writes them: the global rows' (UI/SettingsCatalog.cs).</summary>
        public const float StrengthMax = 100f, DetailMax = 100f, ReflectivityMax = 60f, ClarityMax = 100f, OwnColourMax = 100f;

        /// <summary>
        /// Shininess needed, per asset: lower than the global row's 40 (an asset's glass may be
        /// painted dull, and the walls it pulls in are that asset's alone) -- the Asset tab's slider.
        /// </summary>
        public const float ThresholdMin = 10f, ThresholdMax = 95f;

        private const string NewLine = "\r\n";

        public static string Write(IList<AssetRecord> records, string header)
        {
            var sorted = new List<AssetRecord>(records);
            sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            var text = new StringBuilder(4096);
            text.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>").Append(NewLine);

            if (!string.IsNullOrEmpty(header))
            {
                text.Append("<!--").Append(NewLine);
                foreach (string line in header.Split('\n'))
                    text.Append(("  " + SettingsXmlFormat.CommentSafe(line)).TrimEnd()).Append(NewLine);
                text.Append("-->").Append(NewLine);
            }

            text.Append('<').Append(RootName).Append(" version=\"")
                .Append(Version.ToString(CultureInfo.InvariantCulture)).Append("\">").Append(NewLine);

            foreach (AssetRecord record in sorted)
            {
                if (record == null || string.IsNullOrEmpty(record.Name) || record.IsEmpty)
                    continue;

                text.Append(NewLine).Append("  <").Append(AssetElement).Append(" name=\"").Append(Attribute(record.Name)).Append('"');
                if (!string.IsNullOrEmpty(record.Title))
                    text.Append(" title=\"").Append(Attribute(record.Title)).Append('"');
                text.Append('>').Append(NewLine);

                if (record.Strength.HasValue)
                    Element(text, StrengthName, SettingsXmlFormat.FormatNumber(record.Strength.Value * 100f));
                if (record.PaintedDetail.HasValue)
                    Element(text, DetailName, SettingsXmlFormat.FormatNumber(record.PaintedDetail.Value * 100f));
                if (record.Preset.HasValue)
                    Element(text, PresetName, record.Preset.Value.ToString(CultureInfo.InvariantCulture));
                if (record.Colour.HasValue)
                    Element(text, ColourName, ColorText.Format(record.Colour.Value));
                if (record.Reflectivity.HasValue)
                    Element(text, ReflectivityName, SettingsXmlFormat.FormatNumber(record.Reflectivity.Value * 100f));
                if (record.Clarity.HasValue)
                    Element(text, ClarityName, SettingsXmlFormat.FormatNumber(record.Clarity.Value * 100f));
                if (record.OwnColour.HasValue)
                    Element(text, OwnColourName, SettingsXmlFormat.FormatNumber(record.OwnColour.Value * 100f));
                if (record.Threshold.HasValue)
                    Element(text, ThresholdName, SettingsXmlFormat.FormatNumber(record.Threshold.Value * 100f));
                if (record.NightWindows.HasValue)
                    Element(text, NightWindowsName, SettingsXmlFormat.FormatBool(record.NightWindows.Value));

                text.Append("  </").Append(AssetElement).Append('>').Append(NewLine);
            }

            text.Append(NewLine).Append("</").Append(RootName).Append('>').Append(NewLine);
            return text.ToString();
        }

        private static void Element(StringBuilder text, string name, string value)
        {
            text.Append("    <").Append(name).Append('>').Append(value).Append("</").Append(name).Append('>').Append(NewLine);
        }

        /// <summary>
        /// Every asset in the text, the last one winning where an asset is written twice. NEVER
        /// throws, whatever the text: a player edits this file by hand.
        /// </summary>
        /// <param name="error">
        /// Null when the whole file was read. Otherwise why it could not be (the parser's line and
        /// position), and the assets returned are the ones that could be SALVAGED one by one -- a
        /// typo in one asset loses that asset only. An empty file is read in full: no assets.
        /// </param>
        /// <remarks>
        /// A value that cannot be taken as written is reported in <paramref name="problems"/> and
        /// skipped, or pulled into its range. No DTD, no entities beyond XML's own five, no external
        /// anything (<see cref="SafeXml"/>).
        /// </remarks>
        public static List<AssetRecord> Parse(string text, out int version, List<string> problems, out string error)
        {
            version = 0;
            error = null;
            var found = new Found();

            try
            {
                if (text == null || text.Trim().Length == 0)
                    return found.Records();

                XmlElement root;
                if (!SafeXml.TryLoad(text, out root, out error))
                {
                    Salvage(text, found, problems);
                    return found.Records();
                }

                if (root.Name != RootName)
                {
                    error = "the outermost element is <" + root.Name + ">, not <" + RootName + ">";
                    Salvage(text, found, problems);
                    return found.Records();
                }

                int.TryParse(root.GetAttribute("version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
                foreach (XmlNode node in root.ChildNodes)
                {
                    var element = node as XmlElement;
                    if (element == null)
                        continue;

                    if (!string.Equals(element.Name, AssetElement, StringComparison.OrdinalIgnoreCase))
                    {
                        Problem(problems, "<" + element.Name + "> ignored: only <" + AssetElement + "> elements are read");
                        continue;
                    }

                    ReadAsset(element, found, problems);
                }
            }
            catch (Exception e)
            {
                // Nothing above should throw; if something does, the file is unreadable, not the mod broken.
                if (error == null)
                    error = e.GetType().Name + ": " + e.Message;
            }

            return found.Records();
        }

        /// <summary>The assets read so far, in the file's order, the last of a name winning.</summary>
        private sealed class Found
        {
            private readonly Dictionary<string, AssetRecord> _byName = new Dictionary<string, AssetRecord>(StringComparer.Ordinal);
            private readonly List<string> _order = new List<string>();

            public void Add(AssetRecord record, List<string> problems)
            {
                if (_byName.ContainsKey(record.Name))
                    Problem(problems, record.Name + ": written twice, the last one taken");
                else
                    _order.Add(record.Name);
                _byName[record.Name] = record;
            }

            public List<AssetRecord> Records()
            {
                var records = new List<AssetRecord>(_order.Count);
                foreach (string name in _order)
                    records.Add(_byName[name]);
                return records;
            }
        }

        private static void ReadAsset(XmlElement element, Found found, List<string> problems)
        {
            string name = element.GetAttribute("name").Trim();
            if (name.Length == 0)
            {
                Problem(problems, "an <" + AssetElement + "> without a name ignored");
                return;
            }

            AssetRecord record = new AssetRecord(name);
            string title = element.GetAttribute("title").Trim();
            record.Title = title.Length > 0 ? title : null;
            foreach (XmlNode child in element.ChildNodes)
            {
                var setting = child as XmlElement;
                if (setting != null)
                    Read(record, setting.Name, setting.InnerText.Trim(), problems);
            }

            found.Add(record, problems);
        }

        /// <summary>
        /// A file that is not well-formed, read asset by asset: each &lt;Asset ...&gt; up to its
        /// &lt;/Asset&gt; (or the next &lt;Asset, or the end) is parsed on its own, closed for it
        /// where its end tag is missing. What still cannot be read is said, with its line.
        /// </summary>
        private static void Salvage(string text, Found found, List<string> problems)
        {
            MatchCollection starts = Regex.Matches(text, @"<\s*" + AssetElement + @"\b", RegexOptions.IgnoreCase);
            for (int i = 0; i < starts.Count; i++)
            {
                int start = starts[i].Index;
                int end = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
                string piece = text.Substring(start, end - start);

                Match close = Regex.Match(piece, @"</\s*" + AssetElement + @"\s*>", RegexOptions.IgnoreCase);
                bool closed = close.Success;
                if (closed)
                {
                    piece = piece.Substring(0, close.Index + close.Length);
                }
                else
                {
                    // The last asset of a file cut short: whatever closes the root is not its.
                    Match rootClose = Regex.Match(piece, @"</\s*" + RootName + @"\s*>", RegexOptions.IgnoreCase);
                    if (rootClose.Success)
                        piece = piece.Substring(0, rootClose.Index);
                }

                XmlElement element;
                string why;
                if (!SafeXml.TryLoad(piece, out element, out why) && (closed || !SafeXml.TryLoad(piece + "</" + AssetElement + ">", out element, out why)))
                {
                    Problem(problems, "the asset at line " + LineOf(text, start) + " could not be read (" + why + ")");
                    continue;
                }

                if (!string.Equals(element.Name, AssetElement, StringComparison.OrdinalIgnoreCase))
                    continue;

                ReadAsset(element, found, problems);
            }
        }

        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
            {
                if (text[i] == '\n')
                    line++;
            }
            return line;
        }

        private static void Read(AssetRecord record, string name, string value, List<string> problems)
        {
            string where = record.Name + ": " + name + " '" + value + "' ";
            if (Is(name, StrengthName))
                record.Strength = Percent(value, StrengthMax, where, problems);
            else if (Is(name, DetailName))
                record.PaintedDetail = Percent(value, DetailMax, where, problems);
            else if (Is(name, ReflectivityName))
                record.Reflectivity = Percent(value, ReflectivityMax, where, problems);
            else if (Is(name, ClarityName))
                record.Clarity = Percent(value, ClarityMax, where, problems);
            else if (Is(name, OwnColourName))
                record.OwnColour = Percent(value, OwnColourMax, where, problems);
            else if (Is(name, ThresholdName))
                record.Threshold = Percent(value, ThresholdMin, ThresholdMax, where, problems);
            else if (Is(name, NightWindowsName))
            {
                bool on;
                if (SettingsXmlFormat.TryParseBool(value, out on))
                    record.NightWindows = on;
                else
                    Problem(problems, where + "ignored: not true or false");
            }
            else if (Is(name, ColourName))
            {
                Color32 colour;
                if (ColorText.TryParse(value, out colour))
                    record.Colour = colour;
                else
                    Problem(problems, where + "ignored: not a colour (#RRGGBB, or r, g, b)");
            }
            else if (Is(name, PresetName))
            {
                int preset;
                if (TryPreset(value, out preset))
                    record.Preset = preset;
                else
                    Problem(problems, where + "ignored: not a kind of glass (0 Custom, or 1 to " + GlassPresets.Count + ")");
            }
            else
            {
                Problem(problems, record.Name + ": <" + name + "> ignored: an asset can have " + StrengthName + ", " + DetailName +
                                  ", " + PresetName + ", " + ColourName + ", " + ReflectivityName + ", " + ClarityName + ", " +
                                  OwnColourName + ", " + ThresholdName + " and " + NightWindowsName);
            }
        }

        /// <summary>A kind of glass by its number, or by its English name ("Silver reflective").</summary>
        public static bool TryPreset(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
                return false;

            // A whole number, however written ("5", "5.0").
            double number;
            if (SettingsXmlFormat.TryParseNumber(text, out number))
            {
                if (number != Math.Floor(number) || number < int.MinValue || number > int.MaxValue)
                    return false;

                int whole = (int)number;
                GlassPreset preset;
                if (whole == GlassPresets.Custom || GlassPresets.TryGet(whole, out preset))
                {
                    value = whole;
                    return true;
                }
                return false;
            }

            if (string.Equals(text.Trim(), "Custom", StringComparison.OrdinalIgnoreCase))
            {
                value = GlassPresets.Custom;
                return true;
            }

            for (int i = 0; i < GlassPresets.Count; i++)
            {
                GlassPreset preset = GlassPresets.At(i);
                if (string.Equals(text.Trim(), preset.LogName, StringComparison.OrdinalIgnoreCase))
                {
                    value = preset.Value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>A percentage as the slider shows it (0..max), stored as a fraction; null when unreadable.</summary>
        private static float? Percent(string value, float max, string where, List<string> problems)
        {
            return Percent(value, 0f, max, where, problems);
        }

        /// <summary>A percentage as the slider shows it (min..max), stored as a fraction; null when unreadable.</summary>
        private static float? Percent(string value, float min, float max, string where, List<string> problems)
        {
            double number;
            if (!SettingsXmlFormat.TryParseNumber(value, out number))
            {
                Problem(problems, where + "ignored: not a number (decimals take a dot: 0.5)");
                return null;
            }

            if (number < min || number > max)
            {
                double clamped = Math.Max(min, Math.Min(max, number));
                Problem(problems, where + "pulled into its range " + SettingsXmlFormat.FormatNumber(min) + " to " +
                                  SettingsXmlFormat.FormatNumber(max) + ": " + SettingsXmlFormat.FormatNumber(clamped));
                number = clamped;
            }

            return (float)(number / 100.0);
        }

        private static bool Is(string name, string wanted)
        {
            return string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase);
        }

        private static void Problem(List<string> problems, string text)
        {
            if (problems != null)
                problems.Add(text);
        }

        /// <summary>
        /// An attribute's text, escaped -- and without the characters XML 1.0 forbids outright (a
        /// control character in a title would make a file this mod then cannot read back).
        /// </summary>
        private static string Attribute(string text)
        {
            var clean = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n' || c == '\t')
                {
                    clean.Append(' ');
                }
                else if (char.IsSurrogate(c))
                {
                    // Only a whole pair.
                    if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        clean.Append(c).Append(text[i + 1]);
                        i++;
                    }
                }
                else if (c >= 0x20 && c != '￾' && c != '￿')
                {
                    clean.Append(c);
                }
            }

            return clean.ToString().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
