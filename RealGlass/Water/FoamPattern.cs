using System;

namespace RealGlass.Water
{
    /// <summary>
    /// The foam's pattern: one tileable texture, two channels the foam shader thresholds by how much
    /// foam a point has (RealGlass/WaterFoam). Pure and deterministic: the GPU preview runs this same
    /// file (tools/preview/sync-sim.ps1), and the mod builds the texture from it once.
    /// </summary>
    /// <remarks>
    /// - R, the LACE: sea foam seen close is a network of white bubble walls round dark holes. The
    ///   cells of Worley's cellular texture ("A Cellular Texture Basis Function", SIGGRAPH 1996), the
    ///   foam on their borders (where the nearest feature point is hardly nearer than the next: small
    ///   F2 - F1), two scales of cells.
    /// - G, the PATCHES: the foam's larger structure (streaks and clumps tens of metres across), a
    ///   periodic value-noise fractal sum (Perlin 1985's fBm, on a lattice).
    /// - Each channel is HISTOGRAM-EQUALISED (every value by its rank), so it is uniform on 0..1: the
    ///   share of a channel above 1 - a is exactly a. The shader mixes the two as 0.6 G + 0.4 R, whose
    ///   distribution is then known (a trapezoid), so a point given an amount a of foam shows exactly
    ///   that share of white wherever the pattern is resolved, and a far away where it is averaged.
    /// - Both tile: the cells and the lattice wrap at the texture's edge.
    /// </remarks>
    public static class FoamPattern
    {
        /// <summary>Texels along a side.</summary>
        public const int Size = 256;

        /// <summary>The lace's repeat on the water (m): cells of 2.5 m and 1 m.</summary>
        public const double LaceTile = 40.0;

        /// <summary>The patches' repeat on the water (m): features of about 20-90 m.</summary>
        public const double PatchTile = 360.0;

        /// <summary>The lace's cells across a tile, the coarse scale and the fine (whole numbers: it tiles).</summary>
        public const int LaceCells = 16;

        public const int LaceCellsFine = 40;

        /// <summary>The patches' lattice: the coarsest octave's cells across a tile, and how many octaves.</summary>
        public const int PatchPeriod = 4;

        public const int PatchOctaves = 5;

        /// <summary>A channel's features, in texels (what the shader's filtering measures a pixel's footprint against).</summary>
        public const double LaceFeature = (double)Size / LaceCells;

        public const double PatchFeature = (double)Size / (PatchPeriod * 4);

        private const uint Seed = 0x5EAF0A11u;

        /// <summary>The texture's texels, RGBA32 row by row (r lace, g patches, b 128, a 255).</summary>
        public static byte[] Generate()
        {
            int n = Size * Size;
            double[] lace = new double[n];
            double[] patch = new double[n];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    double u = (x + 0.5) / Size, v = (y + 0.5) / Size;
                    lace[y * Size + x] = 0.6 * Lace(u, v, LaceCells, 1u) + 0.4 * Lace(u, v, LaceCellsFine, 2u);
                    patch[y * Size + x] = Fbm(u, v);
                }
            }

            byte[] r = Equalise(lace);
            byte[] g = Equalise(patch);
            var rgba = new byte[n * 4];
            for (int i = 0; i < n; i++)
            {
                rgba[4 * i] = r[i];
                rgba[4 * i + 1] = g[i];
                rgba[4 * i + 2] = 128;
                rgba[4 * i + 3] = 255;
            }

            return rgba;
        }

        /// <summary>
        /// The lace at (u, v) of a tile of <paramref name="cells"/> x <paramref name="cells"/> Worley
        /// cells (one jittered feature point each): 1 on a cell's border, falling to 0 a third of a cell in.
        /// </summary>
        public static double Lace(double u, double v, int cells, uint salt)
        {
            double px = u * cells, py = v * cells;
            int cx = (int)Math.Floor(px), cy = (int)Math.Floor(py);
            double f1 = double.MaxValue, f2 = double.MaxValue;
            for (int j = -1; j <= 1; j++)
            {
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                    double fx = gx + Unit(wx, wy, salt, 0u), fy = gy + Unit(wx, wy, salt, 1u);
                    double d = Math.Sqrt((fx - px) * (fx - px) + (fy - py) * (fy - py));
                    if (d < f1)
                    {
                        f2 = f1;
                        f1 = d;
                    }
                    else if (d < f2)
                        f2 = d;
                }
            }

            double edge = (f2 - f1) / 0.35;
            return 1.0 - Math.Min(1.0, edge);
        }

        /// <summary>The patches at (u, v): <see cref="PatchOctaves"/> octaves of periodic value noise, each half the last.</summary>
        public static double Fbm(double u, double v)
        {
            double sum = 0.0, amplitude = 1.0;
            int period = PatchPeriod;
            for (int o = 0; o < PatchOctaves; o++)
            {
                sum += amplitude * ValueNoise(u * period, v * period, period, (uint)(10 + o));
                amplitude *= 0.5;
                period *= 2;
            }

            return sum;
        }

        /// <summary>Value noise on a lattice wrapping at <paramref name="period"/>, quintic between the lattice values.</summary>
        private static double ValueNoise(double x, double y, int period, uint salt)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            double sx = fx * fx * fx * (fx * (fx * 6.0 - 15.0) + 10.0);
            double sy = fy * fy * fy * (fy * (fy * 6.0 - 15.0) + 10.0);
            double a = Lattice(x0, y0, period, salt), b = Lattice(x0 + 1, y0, period, salt);
            double c = Lattice(x0, y0 + 1, period, salt), d = Lattice(x0 + 1, y0 + 1, period, salt);
            double bottom = a + sx * (b - a), top = c + sx * (d - c);
            return bottom + sy * (top - bottom);
        }

        private static double Lattice(int x, int y, int period, uint salt)
        {
            int wx = ((x % period) + period) % period, wy = ((y % period) + period) % period;
            return Unit(wx, wy, salt, 7u);
        }

        /// <summary>A number in 0..1 from a cell and a salt (Wellons's "lowbias32" integer hash).</summary>
        private static double Unit(int x, int y, uint salt, uint k)
        {
            unchecked
            {
                uint h = Seed ^ ((uint)x * 0x9E3779B1u) ^ ((uint)y * 0x85EBCA77u) ^ (salt * 0xC2B2AE3Du) ^ (k * 0x27D4EB2Fu);
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h / 4294967296.0;
            }
        }

        /// <summary>Every value by its rank, to 0..255: a uniform channel (ties in index order).</summary>
        public static byte[] Equalise(double[] values)
        {
            int n = values.Length;
            var order = new int[n];
            var keys = new double[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                keys[i] = values[i] + i * 1e-12;
            }

            Array.Sort(keys, order);
            var result = new byte[n];
            for (int rank = 0; rank < n; rank++)
                result[order[rank]] = (byte)Math.Min(255, (int)((rank + 0.5) * 256.0 / n));
            return result;
        }
    }
}
