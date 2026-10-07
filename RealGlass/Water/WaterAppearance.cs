using UnityEngine;

namespace RealGlass.Water
{
    /// <summary>Daylight art direction, separate from the reflection optics and simulation.</summary>
    public static class WaterAppearance
    {
        public static float DayAmount(float sunY, float setting)
        {
            return (float)WaterOptics.Daylight(sunY) * Mathf.Clamp01(setting);
        }

        /// <summary>
        /// Clean water in linear light. A brighter blue with some of the map theme retained; the
        /// brightness slider still scales the result. At night/zero amount this is the original colour.
        /// Pollution is shaded separately by the game and is never recoloured here.
        /// </summary>
        public static Color Colour(Color theme, float brightness, float amount)
        {
            float y = Mathf.Max(0f, 0.2126f * theme.r + 0.7152f * theme.g + 0.0722f * theme.b);
            float level = Mathf.Max(y, 0.025f) * 1.6f;
            var blue = new Color(0.2353f * level, 1.1178f * level, 2.0591f * level, theme.a);
            Color colour = Color.Lerp(theme, blue, 0.7f * Mathf.Clamp01(amount)) * Mathf.Max(0f, brightness);
            colour.a = theme.a;
            return colour;
        }
    }
}
