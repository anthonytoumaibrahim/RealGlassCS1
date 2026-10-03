using System;

namespace RealGlass.Water
{
    /// <summary>
    /// The maths of the map's water (the author, 2026-10-03: "the water shader itself -- i.e. ability
    /// to control brightness, SSR, and by default it should be off and have its own tab"; ported from
    /// RealLight's Sea.cs and CityReflection.cs, which it shares the model with). Pure: the shaders
    /// (RealGlass/WaterCube, RealGlass/CityReflection) read the same table, the tests check the numbers
    /// (tools\test-settings.ps1).
    /// </summary>
    /// <remarks>
    /// - THE REFLECTANCE: a rough sea of Cox &amp; Munk's (1954) Gaussian wave slopes, mean square slope
    ///   0.003 + 0.00512 U for a wind of U m/s; rho, the sea-surface reflectance factor (Mobley 1999,
    ///   "Estimation of the remote-sensing reflectance from above-surface measurements"): Schlick's
    ///   Fresnel for water (n 1.333, F0 0.0204) averaged over the facets the eye sees, each by its
    ///   projected area towards the eye. 2% looking down, about half toward the horizon.
    /// - THE CITY'S STREAKS: a facet tilted by a in the plane of the view turns the reflected ray by
    ///   2a in height, so the waves stretch a reflection up and down by <see cref="Spread"/>.
    /// </remarks>
    public static class WaterOptics
    {
        public const double WaterIndex = 1.333;

        /// <summary>Fresnel at normal incidence, air to water: ((n - 1) / (n + 1))^2 = 0.0204.</summary>
        public static readonly double F0 = Math.Pow((WaterIndex - 1.0) / (WaterIndex + 1.0), 2.0);

        /// <summary>Entries in the reflectance table, over x = sqrt(mu) from 0 to 1 (the shaders' _RGSeaTable[33]).</summary>
        public const int TableSize = 33;

        /// <summary>The wind the sky's reflectance is worked out for, m/s (a moderate breeze, Beaufort 3; RealLight's).</summary>
        public const double SkyWind = 5.0;

        /// <summary>The wind for the city's streaks, m/s (a light breeze, Beaufort 2: the sheltered water a city stands on).</summary>
        public const double CityWind = 2.0;

        /// <summary>The game's water shader adds the cube x this for clean water (Custom/Water/Default, disassembled by RealLight).</summary>
        public const double GameReflectivity = 0.5;

        /// <summary>How far a reflected ray is followed, metres (the far plane's 0.9 when nearer).</summary>
        public const double MaxDistance = 8000.0;

        /// <summary>The least depth behind a surface that still counts as meeting it, metres.</summary>
        public const double Thickness = 30.0;

        /// <summary>The share of the screen, at each edge, over which the city's reflection fades out.</summary>
        public const double EdgeFade = 0.05;

        /// <summary>The Blur slider's sigma at 100%, a share of the screen's height (5 px at 1080).</summary>
        public const double BlurBase = 0.005;

        /// <summary>Cox &amp; Munk's mean square slope of a clean sea for a wind of u m/s (both directions together).</summary>
        public static double SlopeVariance(double windSpeed)
        {
            return 0.003 + 0.00512 * Math.Max(0.0, windSpeed);
        }

        /// <summary>Schlick's Fresnel for water.</summary>
        public static double Fresnel(double cosTheta)
        {
            double c = 1.0 - Math.Max(0.0, Math.Min(1.0, cosTheta));
            double c2 = c * c;
            return F0 + (1.0 - F0) * c2 * c2 * c;
        }

        /// <summary>
        /// The rough sea's reflectance of the sky by mu = the sine of how far below the level one
        /// looks: entry i at x = i / 32, mu = x^2 (finer near grazing).
        /// </summary>
        public static double[] BuildTable(double slopeVariance)
        {
            var rho = new double[TableSize];
            const int n = 64;
            const double reach = 4.0;
            double perAxis = Math.Sqrt(slopeVariance / 2.0);
            double step = 2.0 * reach * perAxis / (n - 1);

            for (int i = 0; i < TableSize; i++)
            {
                double x = i / (double)(TableSize - 1);
                double mu = Math.Max(1e-4, x * x);
                double across = Math.Sqrt(1.0 - mu * mu);

                // Towards the eye from the surface: back along +x, up by mu.
                double ex = -across, ey = mu;
                double sumW = 0.0, sumWf = 0.0;
                for (int a = 0; a < n; a++)
                {
                    double zx = -reach * perAxis + a * step;
                    for (int b = 0; b < n; b++)
                    {
                        double zz = -reach * perAxis + b * step;
                        double weight = Math.Exp(-(zx * zx + zz * zz) / slopeVariance);
                        double len = Math.Sqrt(1.0 + zx * zx + zz * zz);
                        double nx = -zx / len, ny = 1.0 / len;
                        double cosI = nx * ex + ny * ey;
                        if (cosI <= 0.0)
                            continue;

                        double w = weight * cosI / ny;
                        sumW += w;
                        sumWf += w * Fresnel(cosI);
                    }
                }

                rho[i] = sumW > 0.0 ? sumWf / sumW : 1.0;
            }

            return rho;
        }

        /// <summary>The table at mu, linearly between its entries (as the shaders read it).</summary>
        public static double Lookup(double[] table, double mu)
        {
            double x = Math.Sqrt(Math.Max(0.0, Math.Min(1.0, mu))) * (TableSize - 1);
            int i = Math.Min(TableSize - 2, (int)Math.Floor(x));
            double f = x - i;
            return table[i] + (table[i + 1] - table[i]) * f;
        }

        /// <summary>
        /// The gain the cube carries: the theme's sky x rho x this, lit by the game's water shader
        /// (x 0.5 x its light), comes back as the theme's sky x its light x rho x the slider -- the
        /// game's own lighting kept, the reflectance real water's.
        /// </summary>
        public static double CubeGain(double skySlider)
        {
            return Math.Max(0.0, skySlider) / GameReflectivity;
        }

        /// <summary>
        /// The light the game's water lights its cube with, per channel, made safe to divide by: a
        /// published real sky (light already) is divided by it in the cube, so that the game's
        /// lighting gives it back. Each channel at least 5% of the light's luminance (and 1e-3):
        /// a deep-red dusk does not blow the blue up. Returned in place.
        /// </summary>
        public static void FloorLight(ref double r, ref double g, ref double b)
        {
            double floor = Math.Max(1e-3, (0.2126 * r + 0.7152 * g + 0.0722 * b) * 0.05);
            r = Math.Max(floor, r);
            g = Math.Max(floor, g);
            b = Math.Max(floor, b);
        }

        /// <summary>One standard deviation of the reflected rays' height about the mirror ray, radians: 2 x the slope's in one plane.</summary>
        public static double Spread(double slopeVariance)
        {
            return 2.0 * Math.Sqrt(slopeVariance / 2.0);
        }

        /// <summary>
        /// The city reflection's Gaussian sigma in uv: across and up/down. blur: the Blur slider (1 =
        /// <see cref="BlurBase"/> of the height, round); streaks: the Streaks slider (1 = the waves'
        /// <see cref="Spread"/> as an angle on the screen, up and down only, the two added as
        /// variances); fovY in degrees; aspect = width / height.
        /// </summary>
        public static void BlurSigmas(double blur, double streaks, double spread, double fovY, double aspect, out double across, out double up)
        {
            double round = Math.Max(0.0, blur) * BlurBase;
            double perRadian = 1.0 / (2.0 * Math.Tan(fovY * Math.PI / 360.0));
            double streak = Math.Max(0.0, streaks) * spread * perRadian;
            across = round / Math.Max(aspect, 1e-3);
            up = Math.Sqrt(round * round + streak * streak);
        }

        /// <summary>By day 1, by night 0, smooth through dusk: the glass's daylight (GlassUniforms), from the sun's height (y of the unit vector to it).</summary>
        public static double Daylight(double sunY)
        {
            double t = Math.Max(0.0, Math.Min(1.0, (sunY + 0.10) / 0.20));
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
