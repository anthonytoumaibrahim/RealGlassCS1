using UnityEngine;
using UnityEngine.Rendering;

namespace RealGlass.Water
{
    /// <summary>
    /// Eye depth of the actual water meshes, independently of the scene depth (which contains the
    /// seabed). Recorded with each patch's property block, before the game reuses that block.
    /// Cleared before a frame's first draw so missing patches never leave old water behind; every
    /// render of the camera in that frame keeps the frame's draws, as the game's own draws do.
    /// </summary>
    /// <remarks>
    /// The scene depth it tests against -- and the city reflection traces through -- is its own copy of
    /// the camera's depth texture, taken at AfterFinalPass, before the forward water is drawn, and
    /// bound in place of _CameraDepthTexture. On Metal the depth texture read later in the frame holds
    /// the game's water itself, pulled toward the camera by its Offset, and every water pixel failed
    /// the test (the author's M1, 2026-10-26: no city in the water); on Direct3D 11 the depth texture is
    /// made once, so the copy holds exactly what it holds.
    /// </remarks>
    public sealed class WaterSurfaceMask
    {
        private const int PassSceneDepth = 1;
        private static readonly int IdDepth = Shader.PropertyToID("_CameraDepthTexture");

        private readonly Material _material;
        private readonly Material _copy;
        private readonly CommandBuffer _buffer;
        private readonly CommandBuffer _sceneBuffer;
        private Camera _camera;
        private RenderTexture _texture;
        private RenderTexture _scene;
        private bool _consumed;
        private int _frame = -1;

        public WaterSurfaceMask(Shader shader)
        {
            _material = new Material(shader) { name = "RealGlass water surface depth", hideFlags = HideFlags.HideAndDontSave };
            // The copy reads the camera's own depth texture: a material of its own, not given the copy.
            _copy = new Material(shader) { name = "RealGlass scene depth before the water", hideFlags = HideFlags.HideAndDontSave };
            _buffer = new CommandBuffer { name = "RealGlass water surface depth" };
            _sceneBuffer = new CommandBuffer { name = "RealGlass scene depth before the water" };
        }

        public RenderTexture Texture { get { return _texture; } }

        /// <summary>The camera's raw depth before the forward water; bind it in place of _CameraDepthTexture.</summary>
        public RenderTexture SceneDepth { get { return _scene; } }

        // Attach before the reflection buffer at the same event: the scene depth is ready and the
        // surface depth is produced before tracing. Its own depth attachment selects the nearest
        // water patch without writing into the camera's depth buffer.
        public void Attach(Camera camera)
        {
            if (_camera != camera)
            {
                Detach();
                _camera = camera;
                _camera.AddCommandBuffer(CameraEvent.AfterFinalPass, _sceneBuffer);
                _camera.AddCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _buffer);
                Camera.onPreCull += BeforeRender;
                Camera.onPostRender += AfterRender;
            }
            if (_texture != null && _texture.width == camera.pixelWidth && _texture.height == camera.pixelHeight)
                return;

            _buffer.Clear();
            _sceneBuffer.Clear();
            Free();
            int width = Mathf.Max(1, camera.pixelWidth), height = Mathf.Max(1, camera.pixelHeight);
            _texture = Target("RealGlassWaterSurfaceDepth", width, height, 24);
            _scene = Target("RealGlassWaterSceneDepth", width, height, 0);
            if (!_texture.Create() || !_scene.Create())
                throw new System.InvalidOperationException("Could not allocate the water surface depth textures.");
            _sceneBuffer.Blit(BuiltinRenderTextureType.CameraTarget, _scene, _copy, PassSceneDepth);
            _material.SetTexture(IdDepth, _scene);
            Clear();
        }

        private static RenderTexture Target(string name, int width, int height, int depth)
        {
            return new RenderTexture(width, height, depth, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        public void Draw(Mesh mesh, Matrix4x4 matrix, int layer, Camera camera, int submesh, MaterialPropertyBlock properties)
        {
            if (_camera == null || (camera != null && camera != _camera) || (_camera.cullingMask & (1 << layer)) == 0)
                return;
            if (_consumed || _frame != Time.frameCount)
                Clear();
            _buffer.DrawMesh(mesh, matrix, _material, submesh, 0, properties);
        }

        public void SetMapping(Material water)
        {
            if (water == null || !water.HasProperty("_MainTex"))
                return;
            _material.SetTextureScale("_MainTex", water.GetTextureScale("_MainTex"));
            _material.SetTextureOffset("_MainTex", water.GetTextureOffset("_MainTex"));
        }

        private void AfterRender(Camera camera)
        {
            if (camera == _camera)
                _consumed = true;
        }

        private void BeforeRender(Camera camera)
        {
            // Not after a render earlier this frame: a mod that renders the game's camera by hand can
            // render it twice a frame (Lumina's dynamic resolution beside the old Dynamic Resolution
            // mod), and the game's water, drawn for every camera, is in both renders.
            if (camera == _camera && _frame != Time.frameCount)
                Clear();
        }

        private void Clear()
        {
            _buffer.Clear();
            _consumed = false;
            _frame = Time.frameCount;
            if (_texture == null)
                return;
            _buffer.SetRenderTarget(_texture);
            _buffer.ClearRenderTarget(true, true, Color.clear);
        }

        public void Detach()
        {
            Camera.onPostRender -= AfterRender;
            Camera.onPreCull -= BeforeRender;
            if (_camera != null)
            {
                _camera.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _buffer);
                _camera.RemoveCommandBuffer(CameraEvent.AfterFinalPass, _sceneBuffer);
            }
            _camera = null;
            Clear();
        }

        private void Free()
        {
            if (_texture != null)
            {
                _texture.Release();
                Dispose(_texture);
                _texture = null;
            }
            if (_scene != null)
            {
                _scene.Release();
                Dispose(_scene);
                _scene = null;
            }
        }

        public void Destroy()
        {
            Detach();
            _buffer.Release();
            _sceneBuffer.Release();
            Free();
            Dispose(_material);
            Dispose(_copy);
        }

        private static void Dispose(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
