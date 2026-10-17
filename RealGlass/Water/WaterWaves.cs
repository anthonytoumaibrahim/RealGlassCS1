using UnityEngine;

namespace RealGlass.Water
{
    /// <summary>
    /// Draws <see cref="WaterSpectrum"/>'s waves into the ripple texture (RealGlass/WaterWaves; its header
    /// has the passes): per cascade the spectrum at the game's hour, then the inverse FFT along u and
    /// along v, then both cascades composed into the target. C# 4, UnityEngine only: the GPU preview
    /// draws its pictures with this same class.
    /// </summary>
    /// <remarks>
    /// - Per cascade: two field sets (A: the slopes and dDx/dx, dDz/dz; B: dDx/dz and the height), each
    ///   1 + 2 log2(N) passes through a shared scratch target (an even number of steps ends in the set's
    ///   own target). 2 x (2 + 32) + 1 = 69 passes of 256^2 (the compose pass at the target's size).
    /// - Float targets read point at texel centres (not every GPU filters 32-bit floats): ARGBFloat, no mips.
    /// </remarks>
    public sealed class WaterWaves
    {
        private static readonly int IdH0 = Shader.PropertyToID("_RGWaveH0");
        private static readonly int IdSpectrum = Shader.PropertyToID("_RGWaveSpectrum");
        private static readonly int IdFFT = Shader.PropertyToID("_RGWaveFFT");
        private static readonly int IdCompose = Shader.PropertyToID("_RGWaveCompose");
        private static readonly int[] IdA = { Shader.PropertyToID("_RGWaveA0"), Shader.PropertyToID("_RGWaveA1") };
        private static readonly int[] IdB = { Shader.PropertyToID("_RGWaveB0"), Shader.PropertyToID("_RGWaveB1") };

        private const int PassSpectrum = 0, PassFFT = 1, PassCompose = 2;

        private readonly Material _material;
        private readonly Texture2D[] _h0 = new Texture2D[WaterSpectrum.CascadeCount];
        private readonly RenderTexture[] _a = new RenderTexture[WaterSpectrum.CascadeCount];
        private readonly RenderTexture[] _b = new RenderTexture[WaterSpectrum.CascadeCount];
        private readonly RenderTexture _scratch;
        private WaterSpectrum _spectrum;

        public WaterWaves(Shader shader)
        {
            _material = new Material(shader) { name = "RealGlass water waves", hideFlags = HideFlags.HideAndDontSave };
            for (int c = 0; c < WaterSpectrum.CascadeCount; c++)
            {
                _a[c] = Target("RealGlassWavesA" + c);
                _b[c] = Target("RealGlassWavesB" + c);
            }
            _scratch = Target("RealGlassWavesScratch");
        }

        /// <summary>The spectrum drawn (null until <see cref="Upload"/>).</summary>
        public WaterSpectrum Spectrum
        {
            get { return _spectrum; }
        }

        /// <summary>Cascade c's fields after the FFT (A: dh/dx, dh/dz, dDx/dx, dDz/dz; B: dDx/dz, h): the tests read them.</summary>
        public RenderTexture FieldsA(int cascade)
        {
            return _a[cascade];
        }

        public RenderTexture FieldsB(int cascade)
        {
            return _b[cascade];
        }

        /// <summary>A spectrum's amplitudes onto the GPU (float textures, point).</summary>
        public void Upload(WaterSpectrum spectrum)
        {
            int n = WaterSpectrum.Size;
            for (int c = 0; c < WaterSpectrum.CascadeCount; c++)
            {
                if (_h0[c] == null)
                {
                    _h0[c] = new Texture2D(n, n, TextureFormat.RGBAFloat, false, true)
                    {
                        name = "RealGlassWavesH0_" + c,
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                }

                float[] h0 = spectrum.H0[c];
                var px = new Color[n * n];
                for (int t = 0; t < px.Length; t++)
                    px[t] = new Color(h0[4 * t], h0[4 * t + 1], h0[4 * t + 2], h0[4 * t + 3]);
                _h0[c].SetPixels(px);
                _h0[c].Apply(false);
            }
            _spectrum = spectrum;
        }

        /// <summary>
        /// This hour's waves (the game's _SimulationTime2, a global) into <paramref name="target"/> at a
        /// root-mean-square slope of <paramref name="rms"/> (both directions together), choppiness
        /// <paramref name="chop"/>.
        /// </summary>
        public void Draw(RenderTexture target, float rms, float chop)
        {
            if (_spectrum == null)
                return;

            int n = WaterSpectrum.Size;
            for (int c = 0; c < WaterSpectrum.CascadeCount; c++)
            {
                _material.SetTexture(IdH0, _h0[c]);
                for (int set = 0; set < 2; set++)
                {
                    RenderTexture into = set == 0 ? _a[c] : _b[c];
                    _material.SetVector(IdSpectrum, new Vector4(n, (float)WaterSpectrum.Tile(c), set, 0f));
                    Graphics.Blit(Texture2D.blackTexture, into, _material, PassSpectrum);
                    RenderTexture from = into, to = _scratch;
                    for (int axis = 0; axis < 2; axis++)
                    {
                        for (int span = 2; span <= n; span *= 2)
                        {
                            _material.SetVector(IdFFT, new Vector4(n, span, axis == 0 ? 1f : 0f, 0f));
                            Graphics.Blit(from, to, _material, PassFFT);
                            RenderTexture swap = from;
                            from = to;
                            to = swap;
                        }
                    }
                }
            }

            for (int c = 0; c < WaterSpectrum.CascadeCount; c++)
            {
                _material.SetTexture(IdA[c], _a[c]);
                _material.SetTexture(IdB[c], _b[c]);
            }
            _material.SetVector(IdCompose, new Vector4(rms, chop, n, WaterSpectrum.TileRatio));
            Graphics.Blit(Texture2D.blackTexture, target, _material, PassCompose);
        }

        private static RenderTexture Target(string name)
        {
            var rt = new RenderTexture(WaterSpectrum.Size, WaterSpectrum.Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        public void Destroy()
        {
            for (int c = 0; c < WaterSpectrum.CascadeCount; c++)
            {
                if (_h0[c] != null)
                    Object.DestroyImmediate(_h0[c]);
                _a[c].Release();
                _b[c].Release();
                Object.DestroyImmediate(_a[c]);
                Object.DestroyImmediate(_b[c]);
            }
            _scratch.Release();
            Object.DestroyImmediate(_scratch);
            Object.DestroyImmediate(_material);
        }
    }
}
