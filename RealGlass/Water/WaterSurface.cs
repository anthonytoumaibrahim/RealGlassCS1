using System;
using ColossalFramework;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealGlass.Water
{
    /// <summary>
    /// The game's water, on its two materials only (TerrainManager.m_waterMaterial and
    /// m_waterTransparentMaterial, both public; a material's value overrides the global the game and
    /// Theme Mixer set, and buildings keep the theme's): its own colour, and a cube that reflects the
    /// theme's sky the way real water does. Ported from RealLight's WaterReflection (2026-10-03).
    /// </summary>
    /// <remarks>
    /// What the game does (RealLight's disassembly, 2026-10-02): Custom/Water/Default adds the theme's
    /// _EnvironmentCubemap x 0.5 for clean water at EVERY angle -- no Fresnel, so a white cubemap gives
    /// white water -- lit by the sun and the ambient, over _WaterColorClean.
    /// - _WaterColorClean: the theme's (the global, read every frame) x the Brightness slider.
    /// - _EnvironmentCubemap: a small cube (RealGlass/WaterCube as a hidden camera's skybox,
    ///   Camera.RenderToCubemap) of the theme's own cube x rho(reflected ray's height, at least the
    ///   level) x <see cref="WaterOptics.CubeGain"/>: lit by the game as before, it comes back as the
    ///   theme's sky x real water's reflectance x the Sky slider. Re-drawn only when the slider or the
    ///   theme's cube changes (Cubemap Replacer, Theme Mixer).
    /// - While either is held the global _RGWaterHeld is 1 (else 0): RealLight then leaves the water
    ///   to us (its own copy of these controls).
    /// Given back: the materials' own values, or the theme's where they held none (or RealLight's).
    /// </remarks>
    public sealed class WaterSurface
    {
        public const int Size = 64;

        private static readonly int IdCube = Shader.PropertyToID("_EnvironmentCubemap");
        private static readonly int IdColour = Shader.PropertyToID("_WaterColorClean");
        private static readonly int IdHeld = Shader.PropertyToID("_RGWaterHeld");
        private static readonly int IdTheme = Shader.PropertyToID("_RGWaterTheme");
        private static readonly int IdGain = Shader.PropertyToID("_RGWaterGain");

        private readonly Material _material;
        private readonly RenderTexture _cube;
        private readonly GameObject _object;
        private readonly Camera _capture;
        private Material[] _water;
        private Texture[] _originalCube;
        private Color?[] _originalColour;
        private bool _cubeHeld, _colourHeld;
        private Texture _drawnTheme;
        private float _drawnGain = -1f;

        private WaterSurface(Shader shader)
        {
            _material = new Material(shader) { name = "RealGlass water cube", hideFlags = HideFlags.HideAndDontSave };
            _cube = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "RealGlassWaterSky",
                hideFlags = HideFlags.HideAndDontSave,
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            _cube.Create();

            _object = new GameObject("RealGlassWaterCubeCamera") { hideFlags = HideFlags.HideAndDontSave };
            _capture = _object.AddComponent<Camera>();
            _capture.enabled = false;
            _capture.clearFlags = CameraClearFlags.Skybox;
            _capture.cullingMask = 0;
            _capture.allowHDR = true;
            _capture.allowMSAA = false;
            _capture.renderingPath = RenderingPath.Forward;
            _capture.useOcclusionCulling = false;
            _capture.nearClipPlane = 0.3f;
            _capture.farClipPlane = 1000f;
            _object.AddComponent<Skybox>().material = _material;
        }

        /// <summary>Null when the shader is missing or unsupported here (logged by ShaderBundle): the water keeps the theme's cube.</summary>
        public static WaterSurface Create()
        {
            Shader shader = ShaderBundle.Get(ShaderBundle.WaterCube);
            return shader != null && shader.isSupported ? new WaterSurface(shader) : null;
        }

        public bool CubeHeld
        {
            get { return _cubeHeld; }
        }

        /// <summary>The theme's cube, as the game set it (the global: our cube is only ever on the two water materials).</summary>
        public static Texture ThemeCube()
        {
            return Shader.GetGlobalTexture(IdCube);
        }

        /// <summary>The cube of real water's reflectance on the water's materials, re-drawn when the slider or the theme's cube changed.</summary>
        public void HoldCube(float skySlider)
        {
            if (!Take())
                return;

            Texture theme = ThemeCube();
            float gain = (float)WaterOptics.CubeGain(skySlider);
            if (theme != _drawnTheme || Mathf.Abs(gain - _drawnGain) > 1e-4f || !_cubeHeld)
            {
                // The theme's cube read at the mip whose texels are this cube's (64 px): no aliasing.
                int themeSize = theme != null ? Mathf.Max(theme.width, Size) : Size;
                _material.SetTexture(IdTheme, theme);
                _material.SetVector(IdGain, new Vector4(gain, theme != null ? 1f : 0f, Mathf.Log(themeSize / (float)Size, 2f), 0f));
                _capture.RenderToCubemap(_cube);
                if (theme != _drawnTheme || !_cubeHeld)
                {
                    Log.Msg("water: the map's water reflects the theme's sky (" + (theme != null ? "'" + theme.name + "' " + theme.width + " px" : "none") +
                            ") with real water's reflectance, a " + Size + " px cube; buildings keep the theme's");
                }
                _drawnTheme = theme;
                _drawnGain = gain;
            }

            for (int i = 0; i < 2; i++)
            {
                if (_water[i] != null && _water[i].GetTexture(IdCube) != _cube)
                    _water[i].SetTexture(IdCube, _cube);
            }

            _cubeHeld = true;
            Shader.SetGlobalFloat(IdHeld, 1f);
        }

        /// <summary>The theme's water colour x the slider on the water's materials, every frame (the theme may change it).</summary>
        public void HoldColour(float brightness)
        {
            if (!Take())
                return;

            Color theme = Shader.GetGlobalColor(IdColour);
            Color linear = theme.linear;
            float k = Mathf.Max(0f, brightness);
            Color colour = new Color(linear.r * k, linear.g * k, linear.b * k).gamma;
            colour.a = theme.a;
            for (int i = 0; i < 2; i++)
            {
                if (_water[i] != null)
                    _water[i].SetColor(IdColour, colour);
            }

            if (!_colourHeld)
                Log.Msg("water: the water's colour is the theme's x " + Mathf.RoundToInt(k * 100f) + "%");
            _colourHeld = true;
            Shader.SetGlobalFloat(IdHeld, 1f);
        }

        /// <summary>The theme's cube on the water again.</summary>
        public void ReleaseCube()
        {
            if (!_cubeHeld)
                return;

            _cubeHeld = false;
            _drawnTheme = null;
            if (_water != null)
            {
                Texture theme = ThemeCube();
                for (int i = 0; i < 2; i++)
                {
                    if (_water[i] != null)
                        _water[i].SetTexture(IdCube, _originalCube[i] != null ? _originalCube[i] : theme);
                }
            }

            Log.Msg("water: the map's water reflects the theme's cubemap again");
            Held();
        }

        /// <summary>The theme's colour on the water again.</summary>
        public void ReleaseColour()
        {
            if (!_colourHeld)
                return;

            _colourHeld = false;
            if (_water != null)
            {
                Color theme = Shader.GetGlobalColor(IdColour);
                for (int i = 0; i < 2; i++)
                {
                    if (_water[i] != null)
                        _water[i].SetColor(IdColour, _originalColour[i].HasValue ? _originalColour[i].Value : theme);
                }
            }

            Log.Msg("water: the water's colour is the theme's again");
            Held();
        }

        public void GiveBack()
        {
            ReleaseCube();
            ReleaseColour();
            _water = null;
            Shader.SetGlobalFloat(IdHeld, 0f);
        }

        private void Held()
        {
            Shader.SetGlobalFloat(IdHeld, _cubeHeld || _colourHeld ? 1f : 0f);
        }

        /// <summary>
        /// The terrain's two water materials, their own values kept to give back (new materials: the
        /// old given back first). A value RealLight left there (it holds the water until it sees
        /// _RGWaterHeld, a frame later) is not the material's own: the theme's is given back instead.
        /// </summary>
        private bool Take()
        {
            if (!Singleton<TerrainManager>.exists)
                return false;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (_water != null && _water[0] == terrain.m_waterMaterial && _water[1] == terrain.m_waterTransparentMaterial)
                return true;

            GiveBack();
            _water = new[] { terrain.m_waterMaterial, terrain.m_waterTransparentMaterial };
            _originalCube = new Texture[2];
            _originalColour = new Color?[2];
            Color themeColour = Shader.GetGlobalColor(IdColour);
            for (int i = 0; i < 2; i++)
            {
                Material m = _water[i];
                if (m == null)
                    continue;
                if (m.HasProperty(IdCube))
                {
                    Texture t = m.GetTexture(IdCube);
                    if (t != null && !t.name.StartsWith("RealLight", StringComparison.Ordinal))
                        _originalCube[i] = t;
                }
                if (m.HasProperty(IdColour))
                {
                    Color c = m.GetColor(IdColour);
                    if (c == themeColour)
                        _originalColour[i] = c;
                }
            }

            return true;
        }

        public void Destroy()
        {
            GiveBack();
            UnityEngine.Object.Destroy(_object);
            _cube.Release();
            UnityEngine.Object.Destroy(_cube);
            UnityEngine.Object.Destroy(_material);
        }
    }
}
