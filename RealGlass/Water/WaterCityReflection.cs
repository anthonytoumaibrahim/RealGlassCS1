using System;
using ColossalFramework;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealGlass.Water
{
    /// <summary>
    /// The city in the map's water (RealGlass/CityReflection; ported from RealLight's
    /// CityReflectionOverlay, 2026-10-03): one command buffer on the main camera at
    /// CameraEvent.AfterImageEffectsOpaque -- a half-size copy of the frame (what the reflected rays
    /// meet), the reflection traced into a half-size buffer, Gaussian-blurred across and up/down, then
    /// added over the water. Day and night.
    /// </summary>
    /// <remarks>
    /// - WHERE: on the map's water at the sea's level only (WaterSimulation.m_currentSeaLevel): a pixel
    ///   whose depth lies at that plane or beyond it (the water or its bed). The surface is that flat
    ///   plane, so the mirror ray is exact.
    /// - THE RAY: followed through the camera's depth texture (McGuire &amp; Mara 2014), 64 steps
    ///   growing geometrically to <see cref="WaterOptics.MaxDistance"/>, the crossing refined by
    ///   bisection; only what stands above the level counts as a hit.
    /// - HOW MUCH: rho at the view's angle (<see cref="WaterOptics.BuildTable"/>) x the strength, by
    ///   day or by night: faint from above, strong across the water.
    /// - WHAT IT REPLACES: where the ray meets the city, the city takes the place of the sky the
    ///   water's cube shows: added = rho x strength x (city - skyShare x the theme's sky x its light),
    ///   skyShare the Sky slider while our cube is on the water, 0 otherwise.
    /// Three Unity 5.6 facts from RealLight's editor test: CommandBuffer.Blit binds its source only
    /// when the shader declares _MainTex in Properties; _MainTex_TexelSize read 0 (the texel size is a
    /// uniform); the blurred buffer must be bound with the buffer's own SetGlobalTexture.
    /// </remarks>
    public sealed class WaterCityReflection
    {
        private static readonly int IdFrame = Shader.PropertyToID("_RGWaterFrame");
        private static readonly int IdGeom = Shader.PropertyToID("_RGWaterGeom");
        private static readonly int IdMarch = Shader.PropertyToID("_RGWaterMarch");
        private static readonly int IdBlur = Shader.PropertyToID("_RGWaterBlur");
        private static readonly int IdBlurred = Shader.PropertyToID("_RGWaterBlurred");
        private static readonly int IdTheme = Shader.PropertyToID("_RGWaterTheme");
        private static readonly int IdLight = Shader.PropertyToID("_RGWaterLight");

        private readonly Material _material;
        private readonly Mesh _cube;
        private readonly CommandBuffer _buffer;
        private RenderTexture _frame, _trace, _across, _blurred;
        private Camera _camera;
        private bool _addedDepth;
        private bool _loggedWater;

        private WaterCityReflection(Shader shader)
        {
            _material = new Material(shader) { name = "RealGlass city reflection", hideFlags = HideFlags.HideAndDontSave };
            _cube = Cube("RealGlassWaterCube");
            _buffer = new CommandBuffer { name = "RealGlass water reflection" };
        }

        /// <summary>Null when the shader is missing or unsupported here (logged by ShaderBundle): the water reflects no city.</summary>
        public static WaterCityReflection Create()
        {
            Shader shader = ShaderBundle.Get(ShaderBundle.CityReflection);
            return shader != null && shader.isSupported ? new WaterCityReflection(shader) : null;
        }

        public bool Attached
        {
            get { return _camera != null; }
        }

        /// <summary>A unit cube round the origin, drawn round the camera (the vertex shader puts it at the far plane).</summary>
        private static Mesh Cube(string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1),
                new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1),
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2, // -z
                4, 5, 6, 4, 6, 7, // +z
                0, 1, 5, 0, 5, 4, // -y
                3, 7, 6, 3, 6, 2, // +y
                0, 4, 7, 0, 7, 3, // -x
                1, 2, 6, 1, 6, 5, // +x
            };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }

        private static int Half(int pixels)
        {
            return Mathf.Max(1, pixels / 2);
        }

        private static RenderTexture Target(string name, int width, int height, bool mips)
        {
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = mips,
                autoGenerateMips = mips,
                filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        private static void Free(ref RenderTexture rt)
        {
            if (rt == null)
                return;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            rt = null;
        }

        /// <summary>
        /// The buffer: a half-size copy of the frame; the trace into a half-size buffer with mips (pass
        /// 0); the Gaussian across (1) and up and down (2), each from its source's mips; onto the water (3).
        /// </summary>
        private void Record(int width, int height)
        {
            _buffer.Clear();
            Free(ref _frame);
            Free(ref _trace);
            Free(ref _across);
            Free(ref _blurred);

            int w = Half(width), h = Half(height);
            _frame = Target("RealGlassWaterFrame", w, h, false);
            _trace = Target("RealGlassWaterTrace", w, h, true);
            _across = Target("RealGlassWaterAcross", w, h, true);
            _blurred = Target("RealGlassWaterBlurred", w, h, false);
            _material.SetTexture(IdFrame, _frame);

            _buffer.Blit(BuiltinRenderTextureType.CameraTarget, _frame);
            _buffer.SetRenderTarget(_trace);
            _buffer.ClearRenderTarget(false, true, Color.clear);
            _buffer.DrawMesh(_cube, Matrix4x4.identity, _material, 0, 0);
            _buffer.Blit(_trace, _across, _material, 1);
            _buffer.Blit(_across, _blurred, _material, 2);
            _buffer.SetGlobalTexture(IdBlurred, _blurred);
            _buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            _buffer.DrawMesh(_cube, Matrix4x4.identity, _material, 0, 3);
        }

        /// <summary>On the camera (or kept there, its buffers re-made on a resize).</summary>
        public void Attach(Camera camera)
        {
            if (camera == null)
            {
                Detach();
                return;
            }

            if (camera == _camera)
            {
                // The glass gives the depth texture back when it goes off, also when it was ours first.
                if ((camera.depthTextureMode & DepthTextureMode.Depth) == 0)
                {
                    camera.depthTextureMode |= DepthTextureMode.Depth;
                    _addedDepth = true;
                }
                if (_frame == null || _frame.width != Half(camera.pixelWidth) || _frame.height != Half(camera.pixelHeight))
                    Record(camera.pixelWidth, camera.pixelHeight);
                return;
            }

            Detach();
            _addedDepth = (camera.depthTextureMode & DepthTextureMode.Depth) == 0;
            camera.depthTextureMode |= DepthTextureMode.Depth;
            Record(camera.pixelWidth, camera.pixelHeight);
            camera.AddCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _buffer);
            _camera = camera;
            Log.Msg("water: the city is reflected in the map's water on '" + camera.name + "' after the opaque image effects (" +
                    _frame.width + " x " + _frame.height + ": traced at half size, blurred, then onto the water" +
                    (_addedDepth ? "; depth texture asked for" : "") + ")");
            LogWater();
        }

        public void Detach()
        {
            if (_camera == null)
                return;

            try
            {
                _camera.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _buffer);
                // Given back only when nothing else of ours wants it: the glass asks for it too.
                if (_addedDepth && !GlassWantsDepth())
                    _camera.depthTextureMode &= ~DepthTextureMode.Depth;
            }
            catch (Exception)
            {
                // A camera destroyed with the city takes its buffers with it.
            }

            _camera = null;
            _addedDepth = false;
            Log.Msg("water: the city reflection is off");
        }

        private static bool GlassWantsDepth()
        {
            return Glass.GlassController.Instance != null && Glass.GlassController.Instance.enabled &&
                   Settings.GlassEnabled != null && Settings.GlassEnabled.value;
        }

        /// <summary>This frame's uniforms.</summary>
        /// <param name="level">The water's level, world metres.</param>
        /// <param name="strength">The city reflection's strength now (day and night blended).</param>
        /// <param name="skyShare">The Sky slider while our cube is on the water, else 0.</param>
        /// <param name="theme">The theme's cube (what the water shows of the sky).</param>
        /// <param name="light">The light the game's water lights the cube with (linear): the sun on the level + the sky's ambient.</param>
        public void SetUniforms(float level, float strength, float skyShare, Texture theme, Color light, double sigmaAcross, double sigmaUp, float farPlane)
        {
            float reach = Mathf.Min((float)WaterOptics.MaxDistance, 0.9f * farPlane);
            int themeSize = theme != null ? Mathf.Max(theme.width, 1) : 1;
            _material.SetVector(IdGeom, new Vector4(level, strength, theme != null ? skyShare : 0f, Mathf.Max(0f, Mathf.Log(themeSize / 64f, 2f))));
            _material.SetVector(IdMarch, new Vector4(reach, (float)WaterOptics.Thickness, (float)WaterOptics.EdgeFade, 0f));
            float texelX = _trace != null ? 1f / _trace.width : 0f, texelY = _trace != null ? 1f / _trace.height : 0f;
            _material.SetVector(IdBlur, new Vector4((float)sigmaAcross, (float)sigmaUp, texelX, texelY));
            if (theme != null)
                _material.SetTexture(IdTheme, theme);
            _material.SetVector(IdLight, new Vector4(light.r, light.g, light.b, 0f));
        }

        /// <summary>
        /// Once a city: the game's two water materials and their render queues -- the reflection is
        /// drawn after the opaque queue, so water drawn later (the transparent queue) would cover it.
        /// </summary>
        private void LogWater()
        {
            if (_loggedWater || !Singleton<TerrainManager>.exists)
                return;

            _loggedWater = true;
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            Log.Msg("water: the game's water -- transparent water " + terrain.TransparentWater +
                    "; default " + Describe(terrain.m_waterMaterial) + "; transparent " + Describe(terrain.m_waterTransparentMaterial));
        }

        private static string Describe(Material m)
        {
            if (m == null)
                return "none";
            Shader s = m.shader;
            return "'" + (s != null ? s.name : "no shader") + "' queue " + m.renderQueue +
                   (m.renderQueue > 2500 ? " (AFTER the city reflection: it covers it)" : "");
        }

        public void Destroy()
        {
            Detach();
            _buffer.Release();
            Free(ref _frame);
            Free(ref _trace);
            Free(ref _across);
            Free(ref _blurred);
            UnityEngine.Object.Destroy(_material);
            UnityEngine.Object.Destroy(_cube);
        }
    }
}
