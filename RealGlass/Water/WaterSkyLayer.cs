using System;
using ColossalFramework;
using RealGlass.Glass;
using UnityEngine;

namespace RealGlass.Water
{
    /// <summary>
    /// The sky in the map's water, worked out per pixel (the author, 2026-10-08, after a Battlefield 6
    /// screenshot of Lake Mead: "that's EXACTLY how we want ours to look ... i have to turn down water
    /// brightness and sky reflection"). RealGlass/WaterSky is drawn on the game's own water meshes,
    /// right after each of the game's water draws (the foam's hook, <see cref="WaterFoamDrawPatch"/>),
    /// with the game's per-patch property block; the game's own water meanwhile reflects no sky (a black
    /// cube on its material: <see cref="WaterSurface"/>). The shader's header has the model.
    /// </summary>
    /// <remarks>
    /// - WHY: the game adds its cube along each ripple's reflected ray with no Fresnel of its own; our
    ///   cube carried real water's reflectance by that ray's height, which counts each ripple's tilt
    ///   twice (<see cref="WaterOptics.OldCubeReflectance"/>): rippled water showed about twice the sky
    ///   it should, the horizon's -- the author turned the Sky slider down to 30%, which also dimmed
    ///   the far water that should go bright.
    /// - THE SKY: the game's skybox as the player sees it, Volumetric Weather's clouds in it: the glass's
    ///   <see cref="GlassSky"/>, a cube of our own (<see cref="CubeSize"/> px, <see cref="FacesPerFrame"/>
    ///   faces a frame), bound as the global _RGWaterSkyCube. A published real sky (RealLight's, internal)
    ///   still takes over by its weight (WaterCommon.cginc).
    /// - Only on the OPAQUE water material: the transparent one is the info views' and the water tool's,
    ///   which keep our cube of the old kind. Off in info views and under the snapshot tool (an asset's
    ///   pictures ship to players without the mod), as the foam.
    /// - THE SUN'S GLITTER (2026-10-17, the Sun glitter row; the shader's header has the model): the
    ///   light the water is lit by (RenderManager.MainLight: the sun, the moon at night) off a sea of
    ///   facets, as rough as the ripples' averaged-away slopes -- the ocean's spectrum's table
    ///   (<see cref="WaterSpectrum.Table"/>) when the ripples are the ocean waves. While it draws, the
    ///   game's own highlight (its water material's _SpecColor) is black; given back when it stops.
    /// - SUN AND MOON APART (2026-10-21, after the release): the Sun glitter row was the moon's too. Each
    ///   now has its strength and its size (<see cref="Glints"/>); which one is the main light's: the game
    ///   makes the moon's light the main one before 6:00 and after 18:00 (DayNightProperties.
    ///   UpdateLighting, normalized time of day below 0.25 / above 0.75, IL 2026-10-21). The size scales
    ///   every slope the glitter sees, its brightest kept: 100% covered a third of a city camera's screen.
    /// </remarks>
    public sealed class WaterSkyLayer
    {
        /// <summary>The live sky's cube, texels a face: blurred by the ripples anyway.</summary>
        public const int CubeSize = 128;

        /// <summary>Faces of it rendered a frame (all six every three frames).</summary>
        public const int FacesPerFrame = 2;

        /// <summary>The global the live cube is bound to (the shader's _RGWaterSkyCube).</summary>
        public const string CubeGlobal = "_RGWaterSkyCube";

        private static readonly int IdSky = Shader.PropertyToID("_RGWaterSky");
        private static readonly int IdRipples = Shader.PropertyToID("_RGWaterSkyRipples");
        private static readonly int IdNormal = Shader.PropertyToID("_WaterNormal");
        private static readonly int IdMainTex = Shader.PropertyToID("_MainTex");
        private static readonly int IdWaves = Shader.PropertyToID("_RGWaterSkyWaves");
        private static readonly int IdTail = Shader.PropertyToID("_RGWaterSkyTail");
        private static readonly int IdSun = Shader.PropertyToID("_RGWaterSun");
        private static readonly int IdSunColour = Shader.PropertyToID("_RGWaterSunColour");
        private static readonly int IdSpecColor = Shader.PropertyToID("_SpecColor");

        // Read by the draw hook on every one of the game's terrain draws: kept to plain fields.
        private static Material s_material;
        private static Material s_gameWater;
        private static bool s_active;
        private static bool s_failed;

        private readonly Material _material;
        private readonly GlassSky _sky;
        private bool _shown;
        private string _why = "";

        // The ocean table on the material (null: none), and the game's highlight held off: on which
        // material, and the colour to give back (null: it had none of its own, the global's then).
        private readonly Vector4[] _table = new Vector4[WaterSpectrum.TableLevels];
        private WaterSpectrum _tableSpectrum;
        private double _tableSlope = -1.0;
        private Material _specHeld;
        private Color? _specOwn;

        // Whose glitter was drawn last frame: 0 none, 1 the sun's, 2 the moon's (for the log).
        private int _glitterFrom;

        /// <summary>The Water tab's glitter rows: each light's strength (1 = real water, 0 = none) and size (1 = the waves' own spread).</summary>
        public struct Glints
        {
            public readonly float Sun, SunSize, Moon, MoonSize;

            public Glints(float sun, float sunSize, float moon, float moonSize)
            {
                Sun = sun;
                SunSize = sunSize;
                Moon = moon;
                MoonSize = moonSize;
            }
        }

        private WaterSkyLayer(Shader shader)
        {
            _material = new Material(shader) { name = "RealGlass water sky", hideFlags = HideFlags.HideAndDontSave };
            _sky = new GlassSky { GlobalName = CubeGlobal };
        }

        /// <summary>Null when the shader is missing or unsupported here (logged by ShaderBundle): the water keeps our cube.</summary>
        public static WaterSkyLayer Create()
        {
            Shader shader = ShaderBundle.Get(ShaderBundle.WaterSky);
            return shader != null && shader.isSupported ? new WaterSkyLayer(shader) : null;
        }

        /// <summary>
        /// From the shared hook (<see cref="WaterFoam.DrawMesh"/>), after the game's own draw: for the
        /// opaque water, the sky on the same mesh, matrix, layer and property block.
        /// </summary>
        public static void Draw(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex,
                                MaterialPropertyBlock properties)
        {
            if (!s_active || (object)material != (object)s_gameWater || (object)material == null)
                return;

            try
            {
                Graphics.DrawMesh(mesh, matrix, s_material, layer, camera, submeshIndex, properties, false, false);
            }
            catch (Exception e)
            {
                s_active = false;
                if (!s_failed)
                    Log.Error("water: the sky layer could not be drawn; the water keeps our cube until the city is reloaded", e);
                s_failed = true;
            }
        }

        /// <summary>The live cube (null until the first frame shown).</summary>
        public Texture Cube
        {
            get { return _sky.Cube; }
        }

        /// <summary>
        /// The sky on the water this frame; false when it cannot be (the hook missing, the sky not
        /// rendered, a failed draw): the caller then keeps our cube on the water instead.
        /// </summary>
        /// <param name="skySlider">The Sky slider (1 = real water).</param>
        /// <param name="ripples">The ripple texture the water is shaded with (ours or the game's).</param>
        /// <param name="slopeVariance">The ripples' mean square slope as the game shades them, both directions.</param>
        /// <param name="rippleSize">The Ripple size slider (1 = 100%; the game's own ripples: 1).</param>
        /// <param name="waveSize">The Wave size slider (likewise).</param>
        /// <param name="eye">Where the sky's cube is rendered from (the camera).</param>
        /// <param name="ocean">The ocean waves' spectrum the ripples hold (null: the 32 waves or the game's).</param>
        /// <param name="slope">The ripples' root-mean-square slope (the texture's units: the ocean table's scale).</param>
        /// <param name="glints">The glitter rows, the sun's and the moon's (a strength of 0 = none, the game's own highlight kept).</param>
        public bool Show(float skySlider, Texture ripples, double slopeVariance, double rippleSize, double waveSize, Vector3 eye,
                         WaterSpectrum ocean, double slope, Glints glints)
        {
            string why = null;
            Material water = Singleton<TerrainManager>.exists ? Singleton<TerrainManager>.instance.m_waterMaterial : null;
            if (s_failed)
                why = "a draw failed";
            else if (!WaterFoam.Hooked)
                why = "the game's water draws are not hooked (" + WaterFoam.Hooks() + ")";
            else if (water == null)
                why = "no water material";
            else if (ripples == null)
                why = "no ripple texture";
            else
            {
                // The game's sky with the volumetric clouds, as it always was: not the glass's dusk
                // or distant clouds.
                _sky.Update(true, CubeSize, FacesPerFrame, null, eye, true, false, null, 1f);
                if (_sky.Problem.Length > 0 || _sky.Cube == null)
                    why = "the live sky: " + (_sky.Problem.Length > 0 ? _sky.Problem : "no cube");
            }

            if (why != null)
            {
                Hide(why);
                return false;
            }

            if (water.HasProperty(IdMainTex))
            {
                _material.SetTextureScale(IdMainTex, water.GetTextureScale(IdMainTex));
                _material.SetTextureOffset(IdMainTex, water.GetTextureOffset(IdMainTex));
            }

            double start, span;
            WaterOptics.SkyRippleMips(rippleSize, waveSize, out start, out span);
            _material.SetTexture(IdNormal, ripples);
            _material.SetVector(IdSky, new Vector4(Mathf.Max(0f, skySlider), CubeSize, Mathf.Max(0, _sky.Mips - 1), 0f));
            _material.SetVector(IdRipples, new Vector4((float)start, (float)span, Mathf.Max(1, ripples.width), (float)Math.Max(0.0, slopeVariance)));
            Ocean(ocean, slope);
            Glitter(water, glints);

            s_material = _material;
            s_gameWater = water;
            s_active = true;
            if (!_shown)
            {
                Log.Msg("water: the sky drawn on the water per pixel (Fresnel on each ripple; the live sky " + CubeSize + " px, " + FacesPerFrame +
                        " faces a frame" + (_sky.DrawsClouds ? ", with Volumetric Weather's clouds" : "") + "; ripples averaged away from mip " +
                        start.ToString("F2") + " over " + span.ToString("F2") + ", mean square slope " + slopeVariance.ToString("F4") +
                        "); the game's own water reflects no sky meanwhile");
            }
            _shown = true;
            _why = "";
            return true;
        }

        /// <summary>
        /// The ocean's averaging on the layer: its table and tail at the ripples' mean-square slope
        /// (_RGWaterSkyTail.w = 1), or none (w = 0: the 32 waves' own averaging). Set when either changes.
        /// </summary>
        private void Ocean(WaterSpectrum ocean, double slope)
        {
            if (ocean == _tableSpectrum && Math.Abs(slope - _tableSlope) < 1e-6)
                return;

            _tableSpectrum = ocean;
            _tableSlope = slope;
            float ms = (float)(slope * slope);
            Vector4 tail = Vector4.zero;
            for (int e = 0; e < _table.Length; e++)
            {
                _table[e] = ocean == null ? Vector4.zero
                    : new Vector4((float)ocean.Table[4 * e] * ms, (float)ocean.Table[4 * e + 1] * ms, (float)ocean.Table[4 * e + 2] * ms, (float)ocean.Table[4 * e + 3]);
            }
            if (ocean != null)
                tail = new Vector4((float)ocean.TailXX * ms, (float)ocean.TailZZ * ms, (float)ocean.TailXZ * ms, 1f);
            _material.SetVectorArray(IdWaves, _table);
            _material.SetVector(IdTail, tail);
        }

        /// <summary>
        /// The glitter this frame: the light the water is lit by (RenderManager.MainLight, the moon at
        /// night; none when it is off) at its own row's strength and size, and the game's own highlight
        /// off on its material meanwhile.
        /// </summary>
        private void Glitter(Material water, Glints glints)
        {
            Light light = Singleton<RenderManager>.exists ? Singleton<RenderManager>.instance.MainLight : null;
            DayNightProperties dn = DayNightProperties.instance;
            bool moon = light != null && dn != null && light == dn.moonLightSource;
            float strength = moon ? glints.Moon : glints.Sun;
            float size = Mathf.Max(0.05f, moon ? glints.MoonSize : glints.SunSize);
            bool on = strength > 0f && light != null;
            Vector3 toLight = light != null ? -light.transform.forward : Vector3.down;
            Color colour = light != null && light.enabled ? light.color.linear * light.intensity : Color.black;
            _material.SetVector(IdSun, new Vector4(toLight.x, toLight.y, toLight.z, on ? strength : 0f));
            _material.SetVector(IdSunColour, new Vector4(colour.r, colour.g, colour.b, size));

            if (on)
                HoldSpec(water);
            else
                ReleaseSpec();
            int from = on ? (moon ? 2 : 1) : 0;
            if (from != _glitterFrom)
            {
                string whose = moon ? "the moon's" : "the sun's";
                Log.Msg("water: " + (on ? whose + " glitter on (" + (strength * 100f).ToString("F0") + "%, size " + (size * 100f).ToString("F0") + "%)"
                                        : "the glitter off" + (light == null ? " (no main light)" : " (" + whose + " at 0%)")));
            }
            _glitterFrom = from;
        }

        /// <summary>The game's own highlight off on its opaque water material (its _SpecColor black), its own colour kept.</summary>
        private void HoldSpec(Material water)
        {
            if (_specHeld != null && _specHeld != water)
                ReleaseSpec();
            if (_specHeld == null)
            {
                _specHeld = water;
                _specOwn = water.HasProperty(IdSpecColor) ? water.GetColor(IdSpecColor) : (Color?)null;
                Log.Msg("water: the game's own sun highlight on the water off while the glitter draws (its _SpecColor " +
                        (_specOwn.HasValue ? ColorText.Format((Color32)_specOwn.Value) + " x " + _specOwn.Value.a.ToString("F2") : "none of the material's: the global " +
                         ColorText.Format((Color32)Shader.GetGlobalColor(IdSpecColor))) + ")");
            }
            if (water.GetColor(IdSpecColor) != Color.black)
                water.SetColor(IdSpecColor, Color.black);
        }

        /// <summary>The game's highlight back: the material's own colour, or the global's where it had none.</summary>
        private void ReleaseSpec()
        {
            if (_specHeld == null)
                return;
            _specHeld.SetColor(IdSpecColor, _specOwn.HasValue ? _specOwn.Value : Shader.GetGlobalColor(IdSpecColor));
            _specHeld = null;
            _specOwn = null;
            Log.Msg("water: the game's own sun highlight on the water again");
        }

        /// <summary>No sky layer (nothing drawn: the hook passes the game's draws straight through).</summary>
        public void Hide(string why)
        {
            ReleaseSpec();
            _glitterFrom = 0;
            s_active = false;
            if (_shown || (why != _why && why.Length > 0 && why != "off"))
                Log.Msg("water: the sky layer off (" + why + ")");
            _shown = false;
            _why = why;
        }

        public void Destroy()
        {
            Hide("off");
            _sky.Destroy();
            s_material = null;
            s_gameWater = null;
            UnityEngine.Object.Destroy(_material);
        }
    }
}
