using System;

namespace RealGlass.Water
{
    /// <summary>
    /// How far each point of the map lies from the sea's waterline, as a map the foam reads. The surf is
    /// a band measured in distance from the shore, and the shader's own measure of that distance -- the
    /// depth over the seabed's slope, exact on a plane beach -- runs away where a bay's floor flattens:
    /// the slope near 0, the "distance" hundreds of metres, so the band's edge and the bores' crests
    /// closed into rings round the flattest spot (the author's screenshot, 2026-10-25). The shader keeps
    /// its measure within a corridor round this distance (FoamOptics.ShoreCorridor): the seabed still
    /// bends the lines, but cannot tear them into rings.
    /// Pure, and synced into the preview (C# 4, no UnityEngine): the terrain's heights in, bytes out.
    /// </summary>
    /// <remarks>
    /// - THE SEA: where the terrain lies below the sea's level -- the lower of the game's two height sets,
    ///   bilinear between their points, as the water's own pixel program takes the terrain
    ///   (TerrainPatch.Refresh fills its height map from FinalHeights and BlockHeightTargets).
    /// - THE DISTANCE: Felzenszwalb and Huttenlocher's exact Euclidean distance transform (2012),
    ///   separable: down each column the distance to the nearest feature, then along each row the lower
    ///   envelope of parabolas. The features are the land's samples next to the sea (the coast's land
    ///   side), so one transform measures both ways: the sea's samples from the coast, less half a sample
    ///   (the waterline lies between), and the land's inland -- flooded land is not the surf.
    /// - THE MAP: <see cref="Factor"/> samples a terrain cell's side, a byte each: <see cref="Zero"/> + the
    ///   distance in <see cref="Step"/> m (the sea above, the land below), clamped to 0..255.
    /// </remarks>
    public sealed class ShoreDistance
    {
        /// <summary>Map samples a terrain cell's side (the game's cells are 16 m: 8 m samples).</summary>
        public const int Factor = 2;

        /// <summary>Metres a step of the map's byte: 254 m out to sea, 256 m inland.</summary>
        public const float Step = 2f;

        /// <summary>The byte at the waterline.</summary>
        public const int Zero = 128;

        private readonly int _points;
        private readonly int _side;
        private readonly float _spacing;
        private readonly int _cap;
        private readonly byte[] _map;
        private readonly ushort[] _columns;
        private readonly int[] _down;
        private readonly float[] _f;
        private readonly int[] _v;
        private readonly float[] _z;
        private readonly float[] _row;

        /// <summary>A map over a square of <paramref name="points"/> x points of terrain, <paramref name="cell"/> m apart.</summary>
        public ShoreDistance(int points, float cell)
        {
            if (points < 2 || cell <= 0f)
                throw new ArgumentException("a map needs two points a side and a cell");
            _points = points;
            _side = (points - 1) * Factor + 1;
            _spacing = cell / Factor;
            // Far enough that the byte is at its end either way, past the cap.
            _cap = (int)Math.Ceiling((Zero * Step) / _spacing) + 2;
            _map = new byte[_side * _side];
            _columns = new ushort[_side * _side];
            _down = new int[_side];
            _f = new float[_side];
            _v = new int[_side];
            _z = new float[_side + 1];
            _row = new float[_side];
        }

        /// <summary>Samples a side of the map.</summary>
        public int Side
        {
            get { return _side; }
        }

        /// <summary>Metres between samples.</summary>
        public float Spacing
        {
            get { return _spacing; }
        }

        /// <summary>The map, a byte a sample, row by row (rows along z, each along x), after <see cref="Build"/>.</summary>
        public byte[] Map
        {
            get { return _map; }
        }

        /// <summary>The sea's samples, and the coast's (land next to the sea), at the last <see cref="Build"/>.</summary>
        public int SeaSamples { get; private set; }

        public int CoastSamples { get; private set; }

        /// <summary>
        /// The map from the terrain's heights (<see cref="Side"/> along z of rows along x, in the game's
        /// units: <paramref name="unit"/> m each; <paramref name="heights2"/> the second set, or null) and
        /// the sea's level (m). Reuses its buffers: one build at a time.
        /// </summary>
        public void Build(ushort[] heights, ushort[] heights2, float unit, float seaLevel)
        {
            int n = _points, side = _side;
            if (heights == null || heights.Length < n * n || (heights2 != null && heights2.Length < n * n))
                throw new ArgumentException("the heights do not cover the map");
            float sea = seaLevel / unit;

            // The land, 1 (the sea 0), into the map for now: each set bilinear between its points, the lower.
            int seaCount = 0;
            for (int j = 0; j < side; j++)
            {
                int cz = Math.Min(j / Factor, n - 2);
                float fz = (j - cz * Factor) / (float)Factor;
                for (int i = 0; i < side; i++)
                {
                    int cx = Math.Min(i / Factor, n - 2);
                    float fx = (i - cx * Factor) / (float)Factor;
                    int at = cz * n + cx;
                    float h = Bilinear(heights, at, n, fx, fz);
                    if (heights2 != null)
                        h = Math.Min(h, Bilinear(heights2, at, n, fx, fz));
                    bool land = h >= sea;
                    _map[j * side + i] = land ? (byte)1 : (byte)0;
                    if (!land)
                        seaCount++;
                }
            }

            // Down the columns, all at once row by row: the samples from the last feature above (capped),
            // then from the next one below; kept as the square of the nearer.
            int coast = 0;
            for (int i = 0; i < side; i++)
                _down[i] = _cap;
            for (int j = 0; j < side; j++)
            {
                for (int i = 0; i < side; i++)
                {
                    if (Feature(i, j))
                    {
                        _down[i] = 0;
                        coast++;
                    }
                    else if (_down[i] < _cap)
                        _down[i]++;
                    _columns[j * side + i] = (ushort)_down[i];
                }
            }

            for (int i = 0; i < side; i++)
                _down[i] = _cap;
            for (int j = side - 1; j >= 0; j--)
            {
                for (int i = 0; i < side; i++)
                {
                    int k = j * side + i;
                    if (_columns[k] == 0)
                        _down[i] = 0;
                    else if (_down[i] < _cap)
                        _down[i]++;
                    int d = Math.Min(_columns[k], _down[i]);
                    _columns[k] = (ushort)(d * d);
                }
            }

            // Along the rows: the lower envelope of the parabolas (i - q)^2 + g(q); signed and quantised.
            float capSquared = (float)_cap * _cap;
            for (int j = 0; j < side; j++)
            {
                int row = j * side;
                for (int i = 0; i < side; i++)
                    _f[i] = _columns[row + i];
                Envelope(side);
                for (int i = 0; i < side; i++)
                {
                    float d = _row[i];
                    bool land = _map[row + i] != 0;
                    int b;
                    if (d >= capSquared)
                        b = land ? 0 : 255;
                    else
                    {
                        float r = (float)Math.Sqrt(d);
                        float metres = land ? -(r + 0.5f) * _spacing : (r - 0.5f) * _spacing;
                        b = Zero + (int)Math.Round(metres / Step);
                    }
                    _map[row + i] = (byte)Math.Max(0, Math.Min(255, b));
                }
            }

            SeaSamples = seaCount;
            CoastSamples = coast;
        }

        /// <summary>
        /// The map's uv for a world point, uv = xz x scale + offset (both axes), for a map whose terrain's
        /// first point lies at (origin, origin) m: each sample's centre at its texel's.
        /// </summary>
        public void Mapping(float origin, out float scale, out float offset)
        {
            scale = 1f / (_spacing * _side);
            offset = (-origin / _spacing + 0.5f) / _side;
        }

        /// <summary>The distance at a sample (m; negative inland), as the map holds it.</summary>
        public static float Metres(byte b)
        {
            return (b - Zero) * Step;
        }

        private static float Bilinear(ushort[] h, int at, int n, float fx, float fz)
        {
            float a = h[at] + fx * (h[at + 1] - h[at]);
            float b = h[at + n] + fx * (h[at + n + 1] - h[at + n]);
            return a + fz * (b - a);
        }

        /// <summary>Land with the sea next to it (one of its four neighbours).</summary>
        private bool Feature(int i, int j)
        {
            int side = _side, k = j * side + i;
            if (_map[k] == 0)
                return false;
            return (i > 0 && _map[k - 1] == 0) || (i < side - 1 && _map[k + 1] == 0) ||
                   (j > 0 && _map[k - side] == 0) || (j < side - 1 && _map[k + side] == 0);
        }

        /// <summary>
        /// Felzenszwalb and Huttenlocher's one-dimensional transform of _f into _row: min over q of
        /// (i - q)^2 + f(q). Every f is finite (capped), so no infinities meet in the arithmetic.
        /// </summary>
        private void Envelope(int n)
        {
            int k = 0;
            _v[0] = 0;
            _z[0] = float.NegativeInfinity;
            _z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                // Parabolas the new one hides are dropped (never the first: its z is -infinity).
                float s;
                while (true)
                {
                    int p = _v[k];
                    s = ((_f[q] + (float)q * q) - (_f[p] + (float)p * p)) / (2f * (q - p));
                    if (s > _z[k])
                        break;
                    k--;
                }

                k++;
                _v[k] = q;
                _z[k] = s;
                _z[k + 1] = float.PositiveInfinity;
            }

            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (_z[k + 1] < q)
                    k++;
                int p = _v[k];
                _row[q] = (float)(q - p) * (q - p) + _f[p];
            }
        }
    }
}
