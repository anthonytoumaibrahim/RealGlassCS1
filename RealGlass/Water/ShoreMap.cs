using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace RealGlass.Water
{
    /// <summary>
    /// The sea's distance from its waterline over the whole map (<see cref="ShoreDistance"/>), for the
    /// foam's surf: built on a worker thread from the game's terrain heights and the sea's level, built
    /// again when either changes, and held as an Alpha8 texture (2161 x 2161, 8 m samples, 4.7 MB).
    /// </summary>
    /// <remarks>
    /// - WHEN: looked at every <see cref="CheckEvery"/> s while the foam shows -- the worker hashes both
    ///   height sets (FinalHeights, BlockHeightTargets: what the water's own shader takes the terrain
    ///   from) with the sea's level, and builds only when that changed. Nothing of the game is patched,
    ///   so every way the terrain changes (landscaping, roads, other mods) is seen. The arrays are read
    ///   while the simulation may be writing them: a map half old is mended at the next look.
    /// - Until the first map is up (and if one cannot be built) the shader measures the distance as it
    ///   did before, the depth over the seabed's slope alone (<see cref="Mapping"/> w = 0).
    /// </remarks>
    internal sealed class ShoreMap
    {
        private const float CheckEvery = 5f;

        // The game's terrain: TerrainManager.RAW_RESOLUTION + 1 points a side, RAW_CELL_SIZE apart, the
        // first at -8640 m; heights in 1/64 m (TerrainManager.SampleRawHeightSmooth).
        private const int Points = 1081;
        private const float Cell = 16f;
        private const float Origin = -8640f;
        private const float Unit = 1f / 64f;

        private readonly object _lock = new object();
        private Texture2D _texture;
        private Vector4 _mapping;
        private float _checkedAt = float.NegativeInfinity;
        private bool _logged;

        // Shared with the worker, under _lock.
        private ShoreDistance _distance;
        private bool _building;
        private bool _ready;
        private uint _builtHash;
        private bool _hasBuilt;
        private long _readyMs;
        private float _readySea;
        private string _error;
        private bool _warned;

        private sealed class Job
        {
            public ushort[] Heights, Heights2;
            public float SeaLevel;
        }

        /// <summary>The map (null until the first is up).</summary>
        public Texture Texture
        {
            get { return _texture; }
        }

        /// <summary>The shader's _RGShoreMapping: uv = xz x x + y, z m a step, w 1 once a map is up.</summary>
        public Vector4 Mapping
        {
            get { return _mapping; }
        }

        /// <summary>Each frame the foam shows: a finished map up to the GPU; every few seconds, a look for changes.</summary>
        public void Update(TerrainManager terrain, float seaLevel)
        {
            TakeReady();
            float now = Time.realtimeSinceStartup;
            if (now - _checkedAt < CheckEvery)
                return;
            _checkedAt = now;

            ushort[] heights = terrain.FinalHeights, heights2 = terrain.BlockHeightTargets;
            if (heights == null || heights.Length < Points * Points)
                return;
            if (heights2 != null && heights2.Length < Points * Points)
                heights2 = null;
            lock (_lock)
            {
                if (_building)
                    return;
                _building = true;
            }

            ThreadPool.QueueUserWorkItem(Build, new Job { Heights = heights, Heights2 = heights2, SeaLevel = seaLevel });
        }

        public void Destroy()
        {
            if (_texture != null)
                UnityEngine.Object.Destroy(_texture);
            _texture = null;
            _mapping = Vector4.zero;
        }

        /// <summary>The worker: hashes, and builds when the terrain or the sea moved. Never throws (a thread's exception would end the game).</summary>
        private void Build(object state)
        {
            var job = (Job)state;
            try
            {
                uint hash = Hash(job.Heights, job.Heights2, job.SeaLevel);
                ShoreDistance distance;
                lock (_lock)
                {
                    if (_hasBuilt && hash == _builtHash)
                    {
                        _building = false;
                        return;
                    }
                    if (_distance == null)
                        _distance = new ShoreDistance(Points, Cell);
                    distance = _distance;
                }

                Stopwatch watch = Stopwatch.StartNew();
                distance.Build(job.Heights, job.Heights2, Unit, job.SeaLevel);
                lock (_lock)
                {
                    _builtHash = hash;
                    _hasBuilt = true;
                    _ready = true;
                    _readyMs = watch.ElapsedMilliseconds;
                    _readySea = job.SeaLevel;
                    _building = false;
                }
            }
            catch (Exception e)
            {
                lock (_lock)
                {
                    _error = e.GetType().Name + ": " + e.Message;
                    _building = false;
                }
            }
        }

        /// <summary>A map the worker finished, onto the GPU (main thread), with its log line. The worker is idle while it is read.</summary>
        private void TakeReady()
        {
            ShoreDistance distance;
            string error;
            long ms;
            float sea;
            lock (_lock)
            {
                error = _error;
                _error = null;
                distance = _ready ? _distance : null;
                _ready = false;
                ms = _readyMs;
                sea = _readySea;
            }

            if (error != null && !_warned)
            {
                Log.Warn("water: the shore's distances could not be mapped (" + error + "); the surf measures them by the seabed's slope alone");
                _warned = true;
            }
            if (distance == null)
                return;

            int side = distance.Side;
            if (_texture == null)
            {
                _texture = new Texture2D(side, side, TextureFormat.Alpha8, false, true)
                {
                    name = "RealGlassShoreDistance",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }

            _texture.LoadRawTextureData(distance.Map);
            _texture.Apply(false);
            float scale, offset;
            distance.Mapping(Origin, out scale, out offset);
            _mapping = new Vector4(scale, offset, ShoreDistance.Step, 1f);

            // Built again whenever the terrain changes near or far: those in detail only.
            if (_logged && !Log.Detailed)
                return;
            string line = "water: the shore's distances mapped for the surf (" + side + " x " + side + " at " + distance.Spacing.ToString("F0") + " m; " +
                          (100.0 * distance.SeaSamples / ((double)side * side)).ToString("F1") + "% sea, " + distance.CoastSamples + " coast samples; sea level " +
                          sea.ToString("F1") + " m; " + ms + " ms on a worker thread)";
            if (_logged)
                Log.Detail(line);
            else
                Log.Msg(line);
            _logged = true;
        }

        /// <summary>FNV-1a over both height sets and the sea's level.</summary>
        private static uint Hash(ushort[] heights, ushort[] heights2, float seaLevel)
        {
            uint h = 2166136261u;
            h = (h ^ (uint)BitConverter.ToInt32(BitConverter.GetBytes(seaLevel), 0)) * 16777619u;
            for (int i = 0; i < heights.Length; i++)
                h = (h ^ heights[i]) * 16777619u;
            if (heights2 != null)
            {
                for (int i = 0; i < heights2.Length; i++)
                    h = (h ^ heights2[i]) * 16777619u;
            }
            return h;
        }
    }
}
