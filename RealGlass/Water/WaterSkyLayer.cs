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

        // Read by the draw hook on every one of the game's terrain draws: kept to plain fields.
        private static Material s_material;
        private static Material s_gameWater;
        private static bool s_active;
        private static bool s_failed;

        private readonly Material _material;
        private readonly GlassSky _sky;
        private bool _shown;
        private string _why = "";

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
        public bool Show(float skySlider, Texture ripples, double slopeVariance, double rippleSize, double waveSize, Vector3 eye)
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
                _sky.Update(true, CubeSize, FacesPerFrame, null, eye, true);
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

        /// <summary>No sky layer (nothing drawn: the hook passes the game's draws straight through).</summary>
        public void Hide(string why)
        {
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
