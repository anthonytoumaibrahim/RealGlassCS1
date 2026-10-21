using System;
using System.Diagnostics;
using System.Threading;
using ColossalFramework;
using UnityEngine;

namespace RealGlass.Water
{
    /// <summary>
    /// The water's moving ripples (docs: the water plan's step 1, the author's pick 2026-10-05: "start
    /// with 1 as a proof of concept"). The game shades its water with a ripple texture, _WaterNormal,
    /// that it moves only along the water's flow: an open sea or a still lake is frozen. Ours, drawn
    /// every frame by RealGlass/WaterRipples from <see cref="WaterOptics.Ripples"/>, is laid on the
    /// game's two water materials in its place, so the game's own shading -- its sky reflection, its
    /// sun highlight, its foam, its flow along rivers, its rain rings -- runs on moving ripples.
    /// </summary>
    /// <remarks>
    /// - STRENGTH: 100% is as rough as the texture the game had: it is measured once per texture
    ///   (copied into a 256 x 256 target and read back: the game decodes .w and .y x 2 - 1), and the
    ///   ripples' root-mean-square slope set to match (<see cref="WaterOptics.RippleSlope"/>).
    /// - Only the two water MATERIALS hold it (as the sky cube): the global the game set stays its own,
    ///   and is what is given back.
    /// - Redrawn only when the game's clock moved (paused: nothing to draw) or the strength changed.
    /// - OCEAN WAVES (2026-10-17, the Ocean waves row): the same texture drawn by <see cref="WaterWaves"/>
    ///   from a <see cref="WaterSpectrum"/> -- thousands of waves from a wind-sea spectrum, by an FFT --
    ///   at the same root-mean-square slope. The spectrum (~0.1 s of maths) is built on a worker
    ///   thread whenever the wind or the heading changes, and put on the GPU on the main thread; the
    ///   32 waves are drawn until the first one is ready, and wherever the FFT's float textures are
    ///   not supported.
    /// </remarks>
    public sealed class WaterRipples
    {
        /// <summary>The ocean's one realisation (the pictures': every city sees the same sea for a wind).</summary>
        public const uint OceanSeed = 1u;
        private static readonly int IdNormal = Shader.PropertyToID("_WaterNormal");
        private static readonly int IdWaves = Shader.PropertyToID("_RGRippleWaves");
        private static readonly int IdRipple = Shader.PropertyToID("_RGRipple");
        private static readonly int IdClock = Shader.PropertyToID("_SimulationTime2");

        private const int MeasureSize = 256;

        private readonly Material _material;
        private readonly RenderTexture _texture;
        private Material[] _water;
        private Texture[] _original;

        // The materials that showed the GLOBAL _WaterNormal when first seen: a release leaves a copy of
        // it on them (a material property cannot be taken away again in Unity 5.6), kept equal to the
        // global while ours is not held (FollowGlobal), as WaterSurface does (audit, 2026-10-11).
        private Material[] _followed;
        private bool[] _follow;

        private bool _held;
        private Texture _measured;
        private double _decodedRms = WaterOptics.DefaultRippleRms;
        private float _drawnHour = float.NaN;
        private double _drawnSlope = -1.0;
        private double _size = 1.0;
        private double _waveSize = 1.0;

        // The ocean waves: the FFT (null where it cannot run), the spectrum on the GPU, and the one a
        // worker thread is building or has built (under _lock).
        private readonly WaterWaves _waves;
        private WaterSpectrum _spectrum;
        private readonly object _lock = new object();
        private WaterSpectrum _ready;
        private long _readyMs;
        private string _buildError;
        private bool _building;
        private double _wantWind = double.NaN, _wantHeading = double.NaN;
        private double _haveWind = double.NaN, _haveHeading = double.NaN;
        private bool _uploaded;
        private int _drawnMode = -1;
        private float _drawnChop = float.NaN;
        private bool _oceanShown;
        private bool _weather;
        private bool _builtLogged;

        private WaterRipples(Shader shader, Shader waves)
        {
            _material = new Material(shader) { name = "RealGlass water ripples", hideFlags = HideFlags.HideAndDontSave };
            if (waves != null)
                _waves = new WaterWaves(waves);
            _material.SetVectorArray(IdWaves, Rows());
            _texture = new RenderTexture(WaterOptics.RippleTexels, WaterOptics.RippleTexels, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "RealGlassWaterRipples",
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = true,
                autoGenerateMips = true,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            _texture.Create();
        }

        /// <summary>Null when the shader is missing or unsupported here (logged by ShaderBundle): the game's ripples stay.</summary>
        public static WaterRipples Create()
        {
            Shader shader = ShaderBundle.Get(ShaderBundle.WaterRipples);
            if (shader == null || !shader.isSupported)
                return null;

            // The FFT reads and writes 32-bit floats (spectra, transforms): without them, the 32 waves.
            Shader waves = ShaderBundle.Get(ShaderBundle.WaterWaves);
            bool floats = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat) && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat);
            if (waves == null || !waves.isSupported || !floats)
            {
                Log.Msg("water: no ocean waves here (" + (waves == null ? "no shader" : !waves.isSupported ? "shader not supported" : "no 32-bit float textures") +
                        "): the ripples are the 32 waves");
                waves = null;
            }
            return new WaterRipples(shader, waves);
        }

        /// <summary>The ocean's spectrum the texture holds now (null: the 32 waves): the sky layer's averaging.</summary>
        public WaterSpectrum Ocean
        {
            get { return _drawnMode == 1 ? _spectrum : null; }
        }

        /// <summary>The ripple texture (the city reflection reads it as the game does).</summary>
        public Texture Texture
        {
            get { return _texture; }
        }

        /// <summary>The decoded root-mean-square of the game's own texture (or <see cref="WaterOptics.DefaultRippleRms"/>).</summary>
        public double DecodedRms
        {
            get { return _decodedRms; }
        }

        public bool Held
        {
            get { return _held; }
        }

        /// <summary>The generator's _RGRippleWaves: <see cref="WaterOptics.Ripples"/> as its two rows a wave.</summary>
        public static Vector4[] Rows()
        {
            return Rows(1.0);
        }

        /// <summary>The same at the Ripple size slider's <paramref name="size"/> (1 = 100%).</summary>
        public static Vector4[] Rows(double size)
        {
            return Rows(size, 1.0);
        }

        /// <summary>The same with the Wave size slider's <paramref name="waveSize"/> (1 = 100%).</summary>
        public static Vector4[] Rows(double size, double waveSize)
        {
            double[] w = WaterOptics.Ripples(size, waveSize);
            int n = WaterOptics.RippleCount;
            var rows = new Vector4[2 * n];
            for (int i = 0; i < n; i++)
            {
                int at = i * WaterOptics.WaveStride;
                rows[i] = new Vector4((float)w[at], (float)w[at + 1], (float)w[at + 2], (float)w[at + 3]);
                rows[n + i] = new Vector4((float)w[at + 4], (float)w[at + 5], (float)w[at + 6], (float)w[at + 7]);
            }

            return rows;
        }

        /// <summary>
        /// This frame's ripples at <paramref name="strength"/> (1 = as rough as the game's own texture):
        /// with <paramref name="ocean"/>, the ocean's waves for a <paramref name="wind"/> m/s wind toward
        /// <paramref name="heading"/> (degrees from +x toward +z) at choppiness <paramref name="chop"/>,
        /// once their spectrum is built; else (and meanwhile) the 32 waves at the size sliders.
        /// <paramref name="weather"/>: the wind is Dynamic waves' (its rebuilds are logged in detail only, after the first).
        /// </summary>
        public void Draw(float strength, float size, float waveSize, bool ocean, float wind, double heading, float chop, bool weather)
        {
            _weather = weather;
            Measure();
            double slope = WaterOptics.RippleSlope(_decodedRms, strength);
            Vector4 clock = Shader.GetGlobalVector(IdClock);
            if (ocean && _waves != null)
            {
                Request(wind, heading);
                TakeReady();
                if (_spectrum != null)
                {
                    if (_drawnMode == 1 && !_uploaded && clock.y == _drawnHour && Math.Abs(slope - _drawnSlope) < 1e-6 && chop == _drawnChop)
                        return;

                    _waves.Draw(_texture, (float)slope, chop);
                    if (!_oceanShown)
                        Log.Msg("water: the ripples are ocean waves (root-mean-square slope " + slope.ToString("F3") + ", choppiness " + (chop * 100f).ToString("F0") + "%)");
                    _oceanShown = true;
                    _uploaded = false;
                    _drawnMode = 1;
                    _drawnHour = clock.y;
                    _drawnSlope = slope;
                    _drawnChop = chop;
                    return;
                }
            }
            else if (_oceanShown)
            {
                _oceanShown = false;
                Log.Msg("water: the ripples are the 32 waves again");
            }

            if (Math.Abs(size - _size) > 1e-4 || Math.Abs(waveSize - _waveSize) > 1e-4)
            {
                _size = size;
                _waveSize = waveSize;
                _material.SetVectorArray(IdWaves, Rows(size, waveSize));
                _drawnHour = float.NaN;
                Log.Msg("water: ripple size " + (size * 100f).ToString("F0") + "%, wave size " + (waveSize * 100f).ToString("F0") + "%");
            }

            if (_drawnMode == 0 && clock.y == _drawnHour && Math.Abs(slope - _drawnSlope) < 1e-6)
                return;
            _drawnMode = 0;

            _material.SetVector(IdRipple, new Vector4((float)slope, 0f, 0f, 0f));
            Graphics.Blit(Texture2D.blackTexture, _texture, _material, 0);
            if (_drawnSlope < 0.0)
                Log.Msg("water: moving ripples drawn (" + WaterOptics.RippleCount + " waves, " + WaterOptics.RippleLongest.ToString("F0") + " m to " +
                        WaterOptics.RippleShortest.ToString("F1") + " m, " + WaterOptics.RippleTexels + " px over " + WaterOptics.RippleTile.ToString("F0") +
                        " m; root-mean-square slope " + slope.ToString("F3") + ")");
            _drawnHour = clock.y;
            _drawnSlope = slope;
        }

        /// <summary>
        /// The ocean's spectrum for this wind and heading: nothing when it is the one held or being built;
        /// else a worker thread builds it (one at a time: a wind slider being dragged ends in its last value).
        /// </summary>
        private void Request(double wind, double heading)
        {
            lock (_lock)
            {
                _wantWind = wind;
                _wantHeading = heading;
                if (_building || (wind == _haveWind && heading == _haveHeading))
                    return;
                _building = true;
            }

            ThreadPool.QueueUserWorkItem(Build);
        }

        /// <summary>The worker: builds what is wanted, again while the wanted changed meanwhile. Never throws (a thread's exception would end the game).</summary>
        private void Build(object unused)
        {
            while (true)
            {
                double wind, heading;
                lock (_lock)
                {
                    wind = _wantWind;
                    heading = _wantHeading;
                }

                WaterSpectrum built = null;
                string error = null;
                long ms = 0;
                try
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    built = new WaterSpectrum(wind, WaterSpectrum.SeaFetch, heading, OceanSeed);
                    ms = watch.ElapsedMilliseconds;
                }
                catch (Exception e)
                {
                    error = e.GetType().Name + ": " + e.Message;
                }

                lock (_lock)
                {
                    _haveWind = wind;
                    _haveHeading = heading;
                    if (built != null)
                    {
                        _ready = built;
                        _readyMs = ms;
                    }
                    else
                        _buildError = error;
                    if (_wantWind == wind && _wantHeading == heading)
                    {
                        _building = false;
                        return;
                    }
                }
            }
        }

        /// <summary>A spectrum the worker finished onto the GPU (main thread), with its log line.</summary>
        private void TakeReady()
        {
            WaterSpectrum ready;
            long ms;
            string error;
            lock (_lock)
            {
                ready = _ready;
                _ready = null;
                ms = _readyMs;
                error = _buildError;
                _buildError = null;
            }

            if (error != null)
                Log.Warn("water: the ocean waves could not be built (" + error + "); the 32 waves until the wind changes");
            if (ready == null)
                return;

            _waves.Upload(ready);
            _spectrum = ready;
            _uploaded = true;
            // Dynamic waves rebuild it every quarter m/s the weather moves the sea: those in detail only.
            if (_weather && _builtLogged && !Log.Detailed)
                return;
            string line = "water: ocean waves built: " + ready.WaveCount + " waves from a " + ready.WindSpeed.ToString("F1") + " m/s wind over " +
                          (ready.Fetch / 1000.0).ToString("F0") + " km toward " + ready.Heading.ToString("F0") + " deg, the longest " +
                          ready.PeakWavelength.ToString("F0") + " m at the peak; the waves too short for the texture add " +
                          ((ready.TailXX + ready.TailZZ) * 100.0).ToString("F0") + "% to its slopes (" + ms + " ms on a worker thread)";
            if (_weather && _builtLogged)
                Log.Detail(line);
            else
                Log.Msg(line);
            _builtLogged = true;
        }

        /// <summary>
        /// The game's own ripple texture, measured once per texture: its decoded root-mean-square, what
        /// 100% matches. Our own texture is never measured (it is only ever on the materials).
        /// </summary>
        public void Measure()
        {
            Texture game = Shader.GetGlobalTexture(IdNormal);
            if (game == null || game == _measured || game == _texture)
                return;

            _measured = game;
            RenderTexture copy = null;
            Texture2D read = null;
            RenderTexture before = RenderTexture.active;
            try
            {
                copy = new RenderTexture(MeasureSize, MeasureSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
                copy.Create();
                Graphics.Blit(game, copy);
                RenderTexture.active = copy;
                read = new Texture2D(MeasureSize, MeasureSize, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                read.ReadPixels(new Rect(0, 0, MeasureSize, MeasureSize), 0, 0, false);
                Color32[] px = read.GetPixels32();
                var x = new byte[px.Length];
                var y = new byte[px.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    x[i] = px[i].a;
                    y[i] = px[i].g;
                }

                double mean, rms;
                WaterOptics.DecodedStats(x, y, out mean, out rms);
                _decodedRms = rms > 1e-3 ? rms : WaterOptics.DefaultRippleRms;
                var t2 = game as Texture2D;
                Log.Msg("water: the game's ripple texture '" + game.name + "' " + game.width + " x " + game.height +
                        (t2 != null ? " " + t2.format + ", " + t2.mipmapCount + " mips" : "") + ", " + game.wrapMode + ", " + game.filterMode +
                        ", aniso " + game.anisoLevel + ": decoded root-mean-square " + rms.ToString("F3") + " (mean " + mean.ToString("F3") +
                        ") -- 100% ripples match it");
            }
            catch (Exception e)
            {
                _decodedRms = WaterOptics.DefaultRippleRms;
                Log.Warn("water: could not measure the game's ripple texture (" + e.Message + "); 100% ripples take " +
                         WaterOptics.DefaultRippleRms.ToString("F2"));
            }
            finally
            {
                RenderTexture.active = before;
                if (copy != null)
                {
                    copy.Release();
                    UnityEngine.Object.Destroy(copy);
                }
                if (read != null)
                    UnityEngine.Object.Destroy(read);
            }
        }

        /// <summary>Ours on the two water materials (their own values kept to give back).</summary>
        public void Hold()
        {
            if (!Take())
                return;

            for (int i = 0; i < 2; i++)
            {
                if (_water[i] != null && _water[i].GetTexture(IdNormal) != _texture)
                    _water[i].SetTexture(IdNormal, _texture);
            }

            if (!_held)
                Log.Msg("water: the map's water shades with moving ripples (the game's texture kept for everything else)");
            _held = true;
        }

        /// <summary>The game's ripples on the water again.</summary>
        public void Release()
        {
            if (!_held)
                return;

            _held = false;
            if (_water != null)
            {
                Texture global = Shader.GetGlobalTexture(IdNormal);
                for (int i = 0; i < 2; i++)
                {
                    bool follow = _follow != null && _follow[i];
                    if (_water[i] != null)
                        _water[i].SetTexture(IdNormal, follow || _original[i] == null ? global : _original[i]);
                }
            }

            _water = null;
            Log.Msg("water: the map's water shades with the game's own ripples again");
        }

        /// <summary>
        /// Every frame while ours is not held: a material that showed the global when first seen is
        /// brought up to the global whenever the global changes (another mod's doing).
        /// </summary>
        public void FollowGlobal()
        {
            if (_held || _followed == null)
                return;

            Texture global = Shader.GetGlobalTexture(IdNormal);
            if (global == null)
                return;

            for (int i = 0; i < 2; i++)
            {
                Material m = _followed[i];
                if (m != null && _follow[i] && m.GetTexture(IdNormal) != global)
                    m.SetTexture(IdNormal, global);
            }
        }

        /// <summary>The terrain's two water materials, their own values kept (new materials: the old given back first).</summary>
        private bool Take()
        {
            if (!Singleton<TerrainManager>.exists)
                return false;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (_water != null && _water[0] == terrain.m_waterMaterial && _water[1] == terrain.m_waterTransparentMaterial)
                return true;

            Release();
            _water = new[] { terrain.m_waterMaterial, terrain.m_waterTransparentMaterial };
            _original = new Texture[2];
            for (int i = 0; i < 2; i++)
            {
                Material m = _water[i];
                if (m == null)
                    continue;
                Texture t = m.GetTexture(IdNormal);
                if (t != null && t != _texture)
                    _original[i] = t;
            }

            // Decided once per pair of materials: what they showed when FIRST seen (a later take finds
            // the copy a release left there).
            if (_followed == null || _followed[0] != _water[0] || _followed[1] != _water[1])
            {
                _followed = new[] { _water[0], _water[1] };
                _follow = new[] { _original[0] == null, _original[1] == null };
            }

            Log.Msg("water: the water materials' own ripple textures: " + Name(_original[0]) + " / " + Name(_original[1]) +
                    (_water[0] != null && _water[0].HasProperty(IdNormal) ? " (a material property)" : " (the global, not a material property)"));
            return true;
        }

        private static string Name(Texture t)
        {
            return t != null ? "'" + t.name + "'" : "none";
        }

        public void Destroy()
        {
            Release();
            if (_waves != null)
                _waves.Destroy();
            _texture.Release();
            UnityEngine.Object.Destroy(_texture);
            UnityEngine.Object.Destroy(_material);
        }
    }
}
