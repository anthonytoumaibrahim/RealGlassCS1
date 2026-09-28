using System;
using System.Globalization;
using UnityEngine;

namespace RealGlass
{
    /// <summary>
    /// A colour as text: how it is written (#RRGGBB) and every spelling that is read back --
    /// by RealGlass.xml, the panel's colour field and its Paste button alike.
    /// </summary>
    /// <remarks>
    /// Pure: no engine calls (Color32 is a plain struct), so tools/test-settings.ps1 checks it on
    /// the built DLL. Read, in any case and with or without spaces:
    ///   #RRGGBB, RRGGBB, 0xRRGGBB, #RGB, and #RRGGBBAA / #RGBA (the alpha is ignored);
    ///   rgb(255, 128, 0), rgba(...), and a bare list "255, 128, 0" / "255 128 0" / "255;128;0";
    ///   0..1 lists when any number has a decimal point and none is above 1 ("1, 0.5, 0", and the
    ///   RGBA(1.000, 0.873, 0.647, 0.000) Unity writes in our own log); percentages ("100%, 50%, 0%");
    ///   a few colour names (white, pink, orange...).
    /// Numbers outside 0..255 are pulled into it; anything else is refused and the caller keeps
    /// the colour it had. A bare three-digit "fff" is refused on purpose: without its # it is too
    /// easily a number.
    /// </remarks>
    public static class ColorText
    {
        public static string Format(Color32 colour)
        {
            return "#" + colour.r.ToString("X2", CultureInfo.InvariantCulture)
                       + colour.g.ToString("X2", CultureInfo.InvariantCulture)
                       + colour.b.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>The same colour, alpha ignored.</summary>
        public static bool Same(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b;
        }

        public static bool TryParse(string text, out Color32 colour)
        {
            colour = new Color32(255, 255, 255, 255);
            if (text == null)
                return false;

            string s = text.Trim().Trim('"', '\'').Trim();
            if (s.Length == 0)
                return false;

            return TryName(s, out colour) || TryHex(s, out colour) || TryList(s, out colour);
        }

        private static bool TryHex(string s, out Color32 colour)
        {
            colour = default(Color32);

            bool prefixed = false;
            if (s.StartsWith("#", StringComparison.Ordinal))
            {
                s = s.Substring(1);
                prefixed = true;
            }
            else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(2);
                prefixed = true;
            }

            s = s.Trim();
            for (int i = 0; i < s.Length; i++)
            {
                if (!Uri.IsHexDigit(s[i]))
                    return false;
            }

            if (s.Length == 6 || s.Length == 8)
            {
                colour = new Color32(Hex(s, 0, 2), Hex(s, 2, 2), Hex(s, 4, 2), 255);
                return true;
            }

            if (prefixed && (s.Length == 3 || s.Length == 4))
            {
                colour = new Color32((byte)(Hex(s, 0, 1) * 17), (byte)(Hex(s, 1, 1) * 17), (byte)(Hex(s, 2, 1) * 17), 255);
                return true;
            }

            return false;
        }

        private static byte Hex(string s, int start, int length)
        {
            return byte.Parse(s.Substring(start, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static bool TryList(string s, out Color32 colour)
        {
            colour = default(Color32);

            int open = s.IndexOf('(');
            if (open >= 0)
            {
                string function = s.Substring(0, open).Trim().ToLowerInvariant();
                if ((function != "rgb" && function != "rgba") || !s.EndsWith(")", StringComparison.Ordinal))
                    return false;

                s = s.Substring(open + 1, s.Length - open - 2);
            }

            string[] parts = s.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 && parts.Length != 4)
                return false;

            // Only r, g and b; a fourth number is an alpha, which a glass colour has no use for.
            double[] values = new double[3];
            bool[] percent = new bool[3];
            bool anyDecimal = false;
            bool allUpToOne = true;

            for (int i = 0; i < 3; i++)
            {
                string part = parts[i];
                if (part.EndsWith("%", StringComparison.Ordinal))
                {
                    percent[i] = true;
                    part = part.Substring(0, part.Length - 1);
                }

                double value;
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    || double.IsNaN(value) || double.IsInfinity(value))
                    return false;

                values[i] = value;
                if (!percent[i])
                {
                    anyDecimal |= part.IndexOf('.') >= 0;
                    allUpToOne &= value <= 1.0;
                }
            }

            bool unit = anyDecimal && allUpToOne;
            byte[] channels = new byte[3];
            for (int i = 0; i < 3; i++)
            {
                // "/ 100 * 255", not "* 2.55": 50 * 2.55 is 127.4999..., which rounds the wrong way.
                double channel = percent[i] ? values[i] / 100.0 * 255.0 : unit ? values[i] * 255.0 : values[i];
                channels[i] = (byte)Math.Round(Math.Max(0.0, Math.Min(255.0, channel)), MidpointRounding.AwayFromZero);
            }

            colour = new Color32(channels[0], channels[1], channels[2], 255);
            return true;
        }

        private static bool TryName(string s, out Color32 colour)
        {
            switch (s.ToLowerInvariant())
            {
                case "white":   colour = new Color32(255, 255, 255, 255); return true;
                case "black":   colour = new Color32(0, 0, 0, 255); return true;
                case "grey":
                case "gray":    colour = new Color32(128, 128, 128, 255); return true;
                case "red":     colour = new Color32(255, 0, 0, 255); return true;
                case "orange":  colour = new Color32(255, 165, 0, 255); return true;
                case "yellow":  colour = new Color32(255, 255, 0, 255); return true;
                case "gold":    colour = new Color32(255, 215, 0, 255); return true;
                case "green":   colour = new Color32(0, 128, 0, 255); return true;
                case "cyan":    colour = new Color32(0, 255, 255, 255); return true;
                case "blue":    colour = new Color32(0, 0, 255, 255); return true;
                case "purple":  colour = new Color32(128, 0, 128, 255); return true;
                case "violet":  colour = new Color32(238, 130, 238, 255); return true;
                case "magenta": colour = new Color32(255, 0, 255, 255); return true;
                case "pink":    colour = new Color32(255, 192, 203, 255); return true;
                case "salmon":  colour = new Color32(250, 128, 114, 255); return true;
                case "peach":   colour = new Color32(255, 218, 185, 255); return true;
                default:        colour = default(Color32); return false;
            }
        }
    }
}
