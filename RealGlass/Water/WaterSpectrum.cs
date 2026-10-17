using System;

namespace RealGlass.Water
{
    /// <summary>
    /// The ripples as an ocean's waves (the author, 2026-10-17, after web-ocean, an open-source WebGPU
    /// ocean: "can we make it like this?"): thousands of waves from a measured wind-sea spectrum, summed
    /// by an inverse FFT on the GPU (RealGlass/WaterWaves, <see cref="WaterWaves"/>) into the same
    /// texture the 32 waves of <see cref="WaterOptics.Ripples"/> are drawn into, which the game shades
    /// its water with. Pure, C# 4 (the GPU preview compiles it): the spectrum, one fixed realisation of
    /// it, and what the sky layer needs to know of it (how much slope a pixel averages away).
    /// </summary>
    /// <remarks>
    /// - THE MODEL (Tessendorf 2001, "Simulating Ocean Water"): the surface is a sum of waves on a grid of
    ///   wave vectors k that tile a square patch; each its random complex amplitude h0(k) drawn from the
    ///   spectrum, moving at deep water's speed omega^2 = g k, in WHOLE cycles per hour so that the
    ///   game's hour clock (_SimulationTime2.y) wraps with no jump (Tessendorf's looping by a base
    ///   frequency). h(k, t) = h0(k) e^(i w t) + conj(h0(-k)) e^(-i w t): a real surface.
    /// - THE SPECTRUM (as Horvath 2015, "Empirical directional wave spectra for computer graphics",
    ///   DigiPro, gathers them): JONSWAP (Hasselmann et al. 1973) for a fetch-limited wind sea -- its
    ///   peak and level from the wind speed and the fetch -- spread over directions by Donelan, Hamilton
    ///   &amp; Hui (1985), sech^2 about the wind, narrowest at the peak. Above the peak it falls as
    ///   omega^-5: every octave of wavelength carries the same slope (Phillips 1958), as the 32 waves did.
    /// - CHOPPY CRESTS (Tessendorf): each point also moves sideways by D = sum i (k / |k|) h(k), toward
    ///   the crests; a normal map cannot move points, so the slopes are those of the moved surface,
    ///   J^-T grad h (J = I + lambda grad D), shown where the point started -- crests sharper, troughs
    ///   wider. J's determinant (below 0: the wave folds) is kept for whitecaps.
    /// - TWO CASCADES of <see cref="Size"/>^2, both tiling the game's ripple texture (164 m): cascade 0
    ///   over the whole of it (0.64 m texels), the waves at least <see cref="SplitTexels"/> of its texels
    ///   long (5.1 m and up: its bilinear upsampling keeps them within 5%); cascade 1 over a quarter of
    ///   it, repeated 4 x 4 (0.16 m texels: the texture's own), the shorter ones down to 2 texels (0.32 m).
    /// - THE STRENGTH stays the Ripple strength slider's: the realisation is scaled to a unit mean-square
    ///   slope here, and the shader multiplies by the root-mean-square the slider asks for
    ///   (<see cref="WaterOptics.RippleSlope"/>), so the waves change shape, not roughness.
    /// - WHAT A PIXEL AVERAGES AWAY (<see cref="Table"/>): the slopes of the waves the texture's mip L
    ///   has averaged (a box of 2^L texels: each wave keeps sinc(kx W / 2) sinc(kz W / 2) of itself), as
    ///   a covariance -- the sky layer's roughness for the sun's glitter and the sky (Bruneton, Neyret &amp;
    ///   Holzschuch 2010: what filtering takes from the normals is roughness). Plus the waves shorter
    ///   than the texture holds (<see cref="TailXX"/>...: the spectrum carried on down to the
    ///   gravity-capillary limit, 1.7 cm), which no pixel ever resolves.
    /// </remarks>
    public sealed class WaterSpectrum
    {
        /// <summary>Texels a side of each cascade's FFT.</summary>
        public const int Size = 256;

        public const int CascadeCount = 2;

        /// <summary>Cascade 1's patch is cascade 0's divided by this (it repeats this many times each way).</summary>
        public const int TileRatio = 4;

        /// <summary>Cascade 0 holds the waves at least this many of its texels long; cascade 1 the rest.</summary>
        public const double SplitTexels = 8.0;

        /// <summary>Entries of <see cref="Table"/>: mip -1 (nothing averaged) to mip 10 (the 1024 texture's last).</summary>
        public const int TableLevels = 12;

        /// <summary>The ripple texture the cascades are composed into, texels a side.</summary>
        public const int TextureTexels = WaterOptics.RippleTexels;

        /// <summary>JONSWAP's peak enhancement (Hasselmann et al. 1973's mean).</summary>
        public const double Gamma = 3.3;

        /// <summary>The shortest wave of the unresolved tail, m: gravity-capillary waves' least phase speed (Elfouhaily et al. 1997, k_m = 370 rad/m).</summary>
        public const double CapillaryWavelength = 0.017;

        /// <summary>
        /// The open water upwind, m: a coastal sea's (the pictures' and the mod's; the Wind speed row sets
        /// the waves' length over it). JONSWAP is fetch-limited: the same wind over a lake raises shorter
        /// waves, which the wind row can stand in for.
        /// </summary>
        public const double SeaFetch = 50000.0;

        /// <summary>The waves' heading when the game has no wind (degrees from +x toward +z): the 32 waves'.</summary>
        public const double DefaultHeading = WaterOptics.WaveHeading;

        /// <summary>
        /// The way the game's wind blows, as a heading (degrees from +x toward +z), from
        /// WeatherManager.m_windDirection (degrees; IL 2026-10-17): the game publishes (sin a, 0, cos a)
        /// as _WindDirection and turns its WindZone to a + 180 about up, so the wind blows toward
        /// -(sin a, cos a) -- and the waves run with it.
        /// </summary>
        public static double HeadingFromGameWind(double windDirection)
        {
            double a = windDirection * Math.PI / 180.0;
            double heading = Math.Atan2(-Math.Cos(a), -Math.Sin(a)) * 180.0 / Math.PI;
            return heading < 0.0 ? heading + 360.0 : heading;
        }

        /// <summary>The wind (m/s at 10 m), the fetch (m) and the way the wind blows (degrees from +x toward +z).</summary>
        public readonly double WindSpeed, Fetch, Heading;

        /// <summary>
        /// Per cascade, <see cref="Size"/>^2 x 4 floats, row j (along z) by column i (along x): h0(k)'s real
        /// and imaginary parts at a unit mean-square slope, its whole cycles per hour, 0. Texel (i, j) is
        /// the wave numbers m = i (i - Size from Size / 2 on) across the patch along x, n likewise along z:
        /// the FFT's own order, k = 0 at texel 0.
        /// </summary>
        public readonly float[][] H0;

        /// <summary>
        /// <see cref="TableLevels"/> x 4: per mip L = entry - 1 of the ripple texture, the slopes it averages
        /// away (xx, zz, xz) at a unit mean-square slope, and their share of the texture's slopes.
        /// </summary>
        public readonly double[] Table;

        /// <summary>The waves too short for the texture: their slopes' covariance at the same scale.</summary>
        public readonly double TailXX, TailZZ, TailXZ;

        /// <summary>The realisation's mean-square slope before scaling (m^2 / m^2: for the log).</summary>
        public readonly double RawMeanSquare;

        /// <summary>The spectrum's peak wavelength, m (for the log).</summary>
        public readonly double PeakWavelength;

        /// <summary>The waves held, both cascades.</summary>
        public readonly int WaveCount;

        public WaterSpectrum(double windSpeed, double fetch, double heading, uint seed)
        {
            WindSpeed = Math.Max(0.5, windSpeed);
            Fetch = Math.Max(100.0, fetch);
            Heading = heading;
            PeakWavelength = 2.0 * Math.PI * WaterOptics.Gravity / Math.Pow(PeakOmega(WindSpeed, Fetch), 2.0);

            H0 = new float[CascadeCount][];
            var rng = new SplitMix(seed);
            double[] weight = new double[CascadeCount * Size * Size];
            int count = 0;
            for (int c = 0; c < CascadeCount; c++)
            {
                double tile = Tile(c);
                double dk = 2.0 * Math.PI / tile;
                float[] h0 = new float[Size * Size * 4];
                for (int j = 0; j < Size; j++)
                {
                    for (int i = 0; i < Size; i++)
                    {
                        // Every texel draws its two numbers, held or not: the sequence never shifts.
                        double g1 = rng.Gaussian(), g2 = rng.Gaussian();
                        int m = Wave(i), n = Wave(j);
                        double kx = m * dk, kz = n * dk;
                        double k = Math.Sqrt(kx * kx + kz * kz);
                        int at = 4 * (j * Size + i);
                        h0[at + 2] = (float)CyclesPerHour(k);
                        if (!Holds(c, m, n))
                            continue;

                        // E|h0|^2 = S(k) dk^2 / 2: the k and -k terms of a real surface carry S dk^2 together.
                        double amplitude = Math.Sqrt(Density(kx, kz) * dk * dk / 2.0) / Math.Sqrt(2.0);
                        h0[at] = (float)(g1 * amplitude);
                        h0[at + 1] = (float)(g2 * amplitude);
                        count++;
                    }
                }
                H0[c] = h0;
            }
            WaveCount = count;

            // The realisation's slopes, as the composed texture holds them (cascade 0 through its bilinear
            // upsampling), averaged over the hour: 2 |h0|^2 k k^T for each wave.
            double xx, zz, xz;
            Covariance(-1, out xx, out zz, out xz);
            RawMeanSquare = xx + zz;
            double unit = RawMeanSquare > 0.0 ? 1.0 / RawMeanSquare : 0.0;
            double scale = Math.Sqrt(unit);
            for (int c = 0; c < CascadeCount; c++)
            {
                float[] h0 = H0[c];
                for (int t = 0; t < Size * Size; t++)
                {
                    h0[4 * t] = (float)(h0[4 * t] * scale);
                    h0[4 * t + 1] = (float)(h0[4 * t + 1] * scale);
                }
            }

            // From the scaled amplitudes: already at a unit mean-square slope.
            Table = new double[TableLevels * 4];
            for (int e = 1; e < TableLevels; e++)
            {
                double ax, az, axz;
                Covariance(e - 1, out ax, out az, out axz);
                Table[4 * e] = ax;
                Table[4 * e + 1] = az;
                Table[4 * e + 2] = axz;
                Table[4 * e + 3] = Math.Max(0.0, Math.Min(1.0, ax + az));
            }

            double tx, tz, txz;
            Tail(out tx, out tz, out txz);
            TailXX = tx * unit;
            TailZZ = tz * unit;
            TailXZ = txz * unit;
        }

        /// <summary>Cascade c's patch, m.</summary>
        public static double Tile(int cascade)
        {
            return cascade == 0 ? WaterOptics.RippleTile : WaterOptics.RippleTile / TileRatio;
        }

        /// <summary>Texel index -> its wave number in the FFT's order (0..Size/2-1, then -Size/2..-1).</summary>
        public static int Wave(int index)
        {
            return index < Size / 2 ? index : index - Size;
        }

        /// <summary>
        /// Whether cascade c holds the wave (m, n): never k = 0 or the Nyquist row and column (no -k twin
        /// there); cascade 0 the long waves, |(m, n)| up to Size / SplitTexels; cascade 1 the rest of the
        /// circle under its Nyquist frequency.
        /// </summary>
        public static bool Holds(int cascade, int m, int n)
        {
            if ((m == 0 && n == 0) || Math.Abs(m) >= Size / 2 || Math.Abs(n) >= Size / 2)
                return false;
            double r = Math.Sqrt((double)m * m + (double)n * n);
            double split = Size / SplitTexels;
            return cascade == 0 ? r <= split : r * TileRatio > split && r < Size / 2;
        }

        /// <summary>Deep water's frequency for k rad/m, in whole cycles per hour (at least 1).</summary>
        public static double CyclesPerHour(double k)
        {
            return Math.Max(1.0, Math.Round(Math.Sqrt(WaterOptics.Gravity * Math.Max(0.0, k)) * 3600.0 / (2.0 * Math.PI)));
        }

        /// <summary>JONSWAP's peak frequency, rad/s, for a wind of u m/s over a fetch of f m.</summary>
        public static double PeakOmega(double u, double f)
        {
            double g = WaterOptics.Gravity;
            return 22.0 * Math.Pow(g * g / (u * f), 1.0 / 3.0);
        }

        /// <summary>The fetch-limited JONSWAP spectrum S(omega), m^2 s.</summary>
        public static double Jonswap(double omega, double u, double f)
        {
            if (omega <= 0.0)
                return 0.0;
            double g = WaterOptics.Gravity;
            double alpha = 0.076 * Math.Pow(u * u / (f * g), 0.22);
            double peak = PeakOmega(u, f);
            double sigma = omega <= peak ? 0.07 : 0.09;
            double r = Math.Exp(-(omega - peak) * (omega - peak) / (2.0 * sigma * sigma * peak * peak));
            return alpha * g * g / Math.Pow(omega, 5.0) * Math.Exp(-1.25 * Math.Pow(peak / omega, 4.0)) * Math.Pow(Gamma, r);
        }

        /// <summary>Donelan, Hamilton &amp; Hui's spreading at omega / omega_p = ratio, theta (rad) off the wind: unit over -pi..pi.</summary>
        public static double Spreading(double ratio, double theta)
        {
            double beta;
            if (ratio < 0.95)
                beta = 2.61 * Math.Pow(ratio, 1.3);
            else if (ratio < 1.6)
                beta = 2.28 * Math.Pow(ratio, -0.65);
            else
                beta = Math.Pow(10.0, -0.4 + 0.8393 * Math.Exp(-0.567 * Math.Log(ratio * ratio)));
            double sech = 1.0 / Math.Cosh(beta * theta);
            return beta / (2.0 * Math.Tanh(beta * Math.PI)) * sech * sech;
        }

        /// <summary>
        /// The directional wave-number spectrum S(kx, kz), m^4: S(omega) D(omega, theta) (d omega / dk) / k,
        /// with deep water's d omega / dk = g / (2 omega).
        /// </summary>
        public double Density(double kx, double kz)
        {
            double k = Math.Sqrt(kx * kx + kz * kz);
            if (k <= 0.0)
                return 0.0;
            double omega = Math.Sqrt(WaterOptics.Gravity * k);
            double theta = Math.Atan2(kz, kx) - Heading * Math.PI / 180.0;
            theta = Math.Atan2(Math.Sin(theta), Math.Cos(theta));
            double peak = PeakOmega(WindSpeed, Fetch);
            return Jonswap(omega, WindSpeed, Fetch) * Spreading(omega / peak, theta) * (WaterOptics.Gravity / (2.0 * omega)) / k;
        }

        /// <summary>sin(x) / x.</summary>
        public static double Sinc(double x)
        {
            return Math.Abs(x) < 1e-8 ? 1.0 : Math.Sin(x) / x;
        }

        /// <summary>
        /// The gain cascade 0's wave (kx, kz) keeps through the compose pass's bilinear upsampling: the
        /// triangle kernel's sinc^2 per axis over its texel.
        /// </summary>
        public static double UpsampleGain(int cascade, double kx, double kz)
        {
            if (cascade != 0)
                return 1.0;
            double d = Tile(0) / Size;
            double sx = Sinc(kx * d / 2.0), sz = Sinc(kz * d / 2.0);
            return sx * sx * sz * sz;
        }

        /// <summary>
        /// The covariance of the composed texture's slopes (level -1: all of them) or of those its mip
        /// <paramref name="level"/> averages away (a box of 2^level texels), at the amplitudes held now.
        /// </summary>
        private void Covariance(int level, out double xx, out double zz, out double xz)
        {
            xx = zz = xz = 0.0;
            double width = level < 0 ? 0.0 : WaterOptics.RippleTile / TextureTexels * Math.Pow(2.0, level);
            for (int c = 0; c < CascadeCount; c++)
            {
                double dk = 2.0 * Math.PI / Tile(c);
                float[] h0 = H0[c];
                for (int j = 0; j < Size; j++)
                {
                    for (int i = 0; i < Size; i++)
                    {
                        int at = 4 * (j * Size + i);
                        double re = h0[at], im = h0[at + 1];
                        double power = re * re + im * im;
                        if (power <= 0.0)
                            continue;
                        double kx = Wave(i) * dk, kz = Wave(j) * dk;
                        double gain = UpsampleGain(c, kx, kz);
                        double kept = 1.0;
                        if (level >= 0)
                        {
                            double a = Sinc(kx * width / 2.0) * Sinc(kz * width / 2.0);
                            kept = a * a;
                        }
                        double w = 2.0 * power * gain * gain * (level < 0 ? 1.0 : 1.0 - kept);
                        xx += w * kx * kx;
                        zz += w * kz * kz;
                        xz += w * kx * kz;
                    }
                }
            }
        }

        /// <summary>
        /// The waves shorter than the texture holds (2 of its texels, 0.32 m) down to
        /// <see cref="CapillaryWavelength"/>: the spectrum's slope covariance, integrated in polar
        /// steps (raw: the realisation's units).
        /// </summary>
        private void Tail(out double xx, out double zz, out double xz)
        {
            xx = zz = xz = 0.0;
            double from = 2.0 * Math.PI / (2.0 * WaterOptics.RippleTile / TextureTexels);
            double to = 2.0 * Math.PI / CapillaryWavelength;
            const int steps = 160, turns = 180;
            double lnStep = Math.Log(to / from) / steps;
            double dTheta = 2.0 * Math.PI / turns;
            for (int s = 0; s < steps; s++)
            {
                double k = from * Math.Exp((s + 0.5) * lnStep);
                double dk = k * lnStep;
                for (int t = 0; t < turns; t++)
                {
                    double theta = (t + 0.5) * dTheta;
                    double kx = k * Math.Cos(theta), kz = k * Math.Sin(theta);
                    // S(k) k dk dtheta is the variance of heights; x k k^T for the slopes'.
                    double w = Density(kx, kz) * k * dk * dTheta;
                    xx += w * kx * kx;
                    zz += w * kz * kz;
                    xz += w * kx * kz;
                }
            }
        }

        /// <summary>
        /// The six fields of cascade c at the game's hour <paramref name="hour"/> (0..1), on the CPU, as the
        /// GPU makes them (the tests' reference): Size^2 each, row j by column i -- the slopes dh/dx and
        /// dh/dz, the sideways motion's dDx/dx, dDz/dz and dDx/dz, and the height, at a unit mean-square
        /// slope (before the strength).
        /// </summary>
        public void Fields(int cascade, double hour, out double[] hx, out double[] hz, out double[] dxx, out double[] dzz, out double[] dxz, out double[] h)
        {
            int count = Size * Size;
            // Two complex spectra per set, as the GPU packs them: A = (hx + i hz, dxx + i dzz), B = (dxz + i h, 0).
            var aRe = new double[count];
            var aIm = new double[count];
            var a2Re = new double[count];
            var a2Im = new double[count];
            var bRe = new double[count];
            var bIm = new double[count];
            double dk = 2.0 * Math.PI / Tile(cascade);
            float[] h0 = H0[cascade];
            for (int j = 0; j < Size; j++)
            {
                for (int i = 0; i < Size; i++)
                {
                    int t = j * Size + i;
                    int mirror = ((Size - j) % Size) * Size + (Size - i) % Size;
                    double re0 = h0[4 * t], im0 = h0[4 * t + 1];
                    double re1 = h0[4 * mirror], im1 = h0[4 * mirror + 1];
                    // The GPU's phase: 2 pi frac(cycles x hour), in float.
                    float cycles = h0[4 * t + 2];
                    float turn = cycles * (float)hour;
                    double phase = 2.0 * Math.PI * (turn - Math.Floor(turn));
                    double cs = Math.Cos(phase), sn = Math.Sin(phase);
                    double hr = (re0 * cs - im0 * sn) + (re1 * cs - im1 * sn);
                    double hi = (re0 * sn + im0 * cs) - (re1 * sn + im1 * cs);
                    double kx = Wave(i) * dk, kz = Wave(j) * dk;
                    double k = Math.Sqrt(kx * kx + kz * kz);
                    double nx = k > 0.0 ? kx / k : 0.0, nz = k > 0.0 ? kz / k : 0.0;
                    // i kx h + i (i kz h) = (-kx hi - kz hr) + i (kx hr - kz hi)
                    aRe[t] = -kx * hi - kz * hr;
                    aIm[t] = kx * hr - kz * hi;
                    // -kx nx h + i (-kz nz h)
                    double fx = -kx * nx, fz = -kz * nz, fxz = -kx * nz;
                    a2Re[t] = fx * hr - fz * hi;
                    a2Im[t] = fx * hi + fz * hr;
                    // -kx nz h + i h
                    bRe[t] = fxz * hr - hi;
                    bIm[t] = fxz * hi + hr;
                }
            }

            Inverse2D(aRe, aIm);
            Inverse2D(a2Re, a2Im);
            Inverse2D(bRe, bIm);
            hx = aRe;
            hz = aIm;
            dxx = a2Re;
            dzz = a2Im;
            dxz = bRe;
            h = bIm;
        }

        /// <summary>The inverse DFT of a Size x Size grid in place, unnormalised (sum of X e^(+i k x)): rows, then columns.</summary>
        public static void Inverse2D(double[] re, double[] im)
        {
            var rowRe = new double[Size];
            var rowIm = new double[Size];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int line = 0; line < Size; line++)
                {
                    for (int s = 0; s < Size; s++)
                    {
                        int at = pass == 0 ? line * Size + s : s * Size + line;
                        rowRe[s] = re[at];
                        rowIm[s] = im[at];
                    }
                    Inverse1D(rowRe, rowIm);
                    for (int s = 0; s < Size; s++)
                    {
                        int at = pass == 0 ? line * Size + s : s * Size + line;
                        re[at] = rowRe[s];
                        im[at] = rowIm[s];
                    }
                }
            }
        }

        /// <summary>
        /// One line's inverse DFT by the GPU's own steps (Stockham, radix 2, self-sorting): for span
        /// s = 2, 4 .. n, output j = even + e^(+2 pi i j / s) odd, even = input[floor(j / s) s / 2 +
        /// j mod (s / 2)], odd = the one n / 2 further on. In place (through a scratch line).
        /// </summary>
        public static void Inverse1D(double[] re, double[] im)
        {
            int n = re.Length;
            var outRe = new double[n];
            var outIm = new double[n];
            for (int span = 2; span <= n; span *= 2)
            {
                int half = span / 2;
                for (int j = 0; j < n; j++)
                {
                    int even = (j / span) * half + j % half;
                    int odd = even + n / 2;
                    double angle = 2.0 * Math.PI * j / span;
                    double c = Math.Cos(angle), s = Math.Sin(angle);
                    outRe[j] = re[even] + c * re[odd] - s * im[odd];
                    outIm[j] = im[even] + s * re[odd] + c * im[odd];
                }
                Array.Copy(outRe, re, n);
                Array.Copy(outIm, im, n);
            }
        }

        /// <summary>
        /// The slopes the compose pass writes, from the six fields at a point (strength applied):
        /// J^-T grad h with J = I + chop grad D, its determinant held at 0.25 or more (a fold is no
        /// steeper than that).
        /// </summary>
        public static void ChoppySlope(double hx, double hz, double dxx, double dzz, double dxz, double chop, out double sx, out double sz)
        {
            double jxx = 1.0 + chop * dxx, jzz = 1.0 + chop * dzz, jxz = chop * dxz;
            double det = Math.Max(0.25, jxx * jzz - jxz * jxz);
            sx = (jzz * hx - jxz * hz) / det;
            sz = (jxx * hz - jxz * hx) / det;
        }

        /// <summary>SplitMix64 (Steele, Lea &amp; Flood 2014): the same numbers on every runtime, unlike System.Random.</summary>
        private sealed class SplitMix
        {
            private ulong _state;
            private double _spare;
            private bool _hasSpare;

            public SplitMix(uint seed)
            {
                _state = 0x9E3779B97F4A7C15UL ^ seed;
            }

            private ulong Next()
            {
                ulong z = _state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }

            /// <summary>Uniform in (0, 1].</summary>
            private double Uniform()
            {
                return ((Next() >> 11) + 1.0) / 9007199254740992.0;
            }

            /// <summary>A standard normal number (Box-Muller, the second of each pair kept for the next call).</summary>
            public double Gaussian()
            {
                if (_hasSpare)
                {
                    _hasSpare = false;
                    return _spare;
                }
                double r = Math.Sqrt(-2.0 * Math.Log(Uniform()));
                double a = 2.0 * Math.PI * Uniform();
                _spare = r * Math.Sin(a);
                _hasSpare = true;
                return r * Math.Cos(a);
            }
        }
    }
}
