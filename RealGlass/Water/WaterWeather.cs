using System;

namespace RealGlass.Water
{
    /// <summary>
    /// Dynamic waves (the author, 2026-10-21: "Dynamic Waves for the water, which reacts to the game
    /// itself. Dynamically it could start from a high value though because for seas it's always likely
    /// that there's at least some waves"; then "I'd like to do without additional sliders. But make
    /// sure that if Dynamic Waves are off, the user can change them just as it is today"): the ocean
    /// waves' wind follows the game's weather instead of the Wind speed row. Pure: the tests check it.
    /// </summary>
    /// <remarks>
    /// - THE GAME HAS NO WIND SPEED, only a factor (IL, 2026-10-21): WeatherManager.GetWindSpeedFactor =
    ///   1 + rain / 2 - fog / 2 (published as _WindDirection.w), and SampleWindSpeed clamps it to 1 in
    ///   the open. Its rain is 0, or 0.25..1 when a spell is rolled, moving 0.0002 a simulation frame
    ///   (none to full in about 2 game hours); lightning above 0.8. Volumetric Weather reads the same
    ///   m_currentRain and never writes it, so its storms are these storms: nothing of it is needed.
    /// - THE WIND (<see cref="Target"/>): <see cref="CalmWind"/> with no rain -- the Wind row's default,
    ///   the sea of a dry day as the mod has always drawn it, never a mirror -- rising with the rain,
    ///   as the game's own factor does, to <see cref="StormWind"/> in its heaviest rain. Fog is left out
    ///   (Volumetric Weather drops it too: fog mods park it at a constant, and the game's half would
    ///   halve the calm sea for good).
    /// - THE ROUGHNESS (<see cref="RoughnessGain"/>): the ripples are drawn at the Ripple strength row's
    ///   slope whatever the wind (it sets their lengths only); a sea's mean square slope grows with the
    ///   wind by Cox &amp; Munk's (1954) clean-surface fit, so the strength is multiplied by the root of
    ///   its rise over the calm sea's. A dry day is exactly today's sea.
    /// - THE LAG (<see cref="Follow"/>): a sea takes hours to rise to a new wind and to fall back. The
    ///   Shore Protection Manual (U.S. Army Corps of Engineers, 1984, ch. 3) gives the duration a wind
    ///   needs to raise its fetch-limited sea, t = 68.8 F^(2/3) / (g^(1/3) U^(1/3)): about 6 game hours
    ///   for the calm wind over the sea's 50 km, 5 for the storm's. The sea's wind moves toward the
    ///   target exponentially, 95% of the way in that duration (the target's); the same rule run back
    ///   down to the calm wind is a stand-in (the Manual gives no decay), and the slower of the two.
    /// - THE CLOCK: simulation frames at the vanilla day's <see cref="FramesPerDay"/>, as the rain moves
    ///   per frame: a mod's longer day (Real Time rewrites DAYTIME_FRAMES) stretches neither. Paused,
    ///   the sea holds.
    /// - THE SPECTRUM is rebuilt at <see cref="RebuildStep"/> steps of the sea's wind (one seed: a step
    ///   changes the sea's slopes by about 2% at the calm wind, 1% at the storm's, measured 2026-10-21);
    ///   the roughness follows the sea's wind smoothly.
    /// </remarks>
    public static class WaterWeather
    {
        /// <summary>The sea's wind with no rain, m/s: the Wind row's default (a moderate breeze, Beaufort 4).</summary>
        public const double CalmWind = 7.0;

        /// <summary>The sea's wind in the game's heaviest rain, m/s: a near gale (Beaufort 7, 13.9-17.1 m/s).</summary>
        public const double StormWind = 15.0;

        /// <summary>The steps of the sea's wind the spectrum is rebuilt at, m/s.</summary>
        public const double RebuildStep = 0.25;

        /// <summary>The vanilla game's day in simulation frames (SimulationManager's cctor: DAYTIME_FRAMES = 65536).</summary>
        public const double FramesPerDay = 65536.0;

        /// <summary>A step longer than this (game hours) is no step: the sea is put on the weather at once.</summary>
        public const double MaxStepHours = 24.0;

        /// <summary>The wind the weather raises the sea with, m/s, for the game's rain (0..1).</summary>
        public static double Target(double rain)
        {
            double r = rain > 0.0 ? Math.Min(1.0, rain) : 0.0;
            return CalmWind + (StormWind - CalmWind) * r;
        }

        /// <summary>The Ripple strength's multiplier for a sea raised by <paramref name="wind"/> m/s: 1 at the calm wind.</summary>
        public static double RoughnessGain(double wind)
        {
            return Math.Sqrt(WaterOptics.SlopeVariance(wind) / WaterOptics.SlopeVariance(CalmWind));
        }

        /// <summary>The Shore Protection Manual's duration a wind of u m/s needs to raise its sea over a fetch of f m, game hours.</summary>
        public static double DevelopmentHours(double wind, double fetch)
        {
            double u = Math.Max(0.5, wind);
            double f = Math.Max(100.0, fetch);
            return 68.8 * Math.Pow(f, 2.0 / 3.0) / (Math.Pow(WaterOptics.Gravity, 1.0 / 3.0) * Math.Pow(u, 1.0 / 3.0)) / 3600.0;
        }

        /// <summary>The sea's wind <paramref name="hours"/> game hours on, from <paramref name="sea"/> toward <paramref name="target"/>.</summary>
        public static double Follow(double sea, double target, double hours)
        {
            if (!(hours > 0.0))
                return sea;
            double tau = DevelopmentHours(target, WaterSpectrum.SeaFetch) / 3.0;
            return target + (sea - target) * Math.Exp(-hours / tau);
        }

        /// <summary>The wind the spectrum is built for: the sea's at <see cref="RebuildStep"/>.</summary>
        public static double Quantize(double wind)
        {
            return Math.Round(wind / RebuildStep) * RebuildStep;
        }
    }

    /// <summary>One city's sea under the weather: its wind, followed frame by frame (<see cref="WaterWeather"/>).</summary>
    public sealed class WaterSea
    {
        private double _frame = double.NaN;

        /// <summary>The sea's wind now, m/s (NaN until the first <see cref="Advance"/>).</summary>
        public double Wind { get; private set; } = double.NaN;

        /// <summary>The wind the weather is raising it toward, m/s.</summary>
        public double Target { get; private set; } = double.NaN;

        /// <summary>Whether it has been advanced since the last <see cref="Reset"/>.</summary>
        public bool Started
        {
            get { return !double.IsNaN(_frame); }
        }

        /// <summary>The next <see cref="Advance"/> puts the sea on its weather at once (a city's start, the switch turned on).</summary>
        public void Reset()
        {
            _frame = double.NaN;
            Wind = double.NaN;
            Target = double.NaN;
        }

        /// <summary>The sea at simulation frame <paramref name="frame"/> (fractional) under <paramref name="rain"/> (0..1).</summary>
        public void Advance(double rain, double frame)
        {
            Target = WaterWeather.Target(double.IsNaN(rain) ? 0.0 : rain);
            double hours = (frame - _frame) * 24.0 / WaterWeather.FramesPerDay;
            if (!Started || double.IsNaN(Wind) || !(hours >= 0.0) || hours > WaterWeather.MaxStepHours)
                Wind = Target;
            else
                Wind = WaterWeather.Follow(Wind, Target, hours);
            _frame = frame;
        }
    }
}
