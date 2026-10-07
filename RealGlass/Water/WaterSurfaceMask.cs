using UnityEngine;
using UnityEngine.Rendering;

namespace RealGlass.Water
{
    /// <summary>
    /// Eye depth of the actual water meshes, independently of the scene depth (which contains the
    /// seabed). Recorded with each patch's property block, before the game reuses that block.
    /// Clear before recording the next render so missing patches never leave old water behind.
    /// </summary>
    public sealed class WaterSurfaceMask
    {
        private readonly Material _material;
        private readonly CommandBuffer _buffer;
        private Camera _camera;
        private RenderTexture _texture;
        private bool _consumed;
        private int _frame = -1;

        public WaterSurfaceMask(Shader shader)
        {
            _material = new Material(shader) { name = "RealGlass water surface depth", hideFlags = HideFlags.HideAndDontSave };
            _buffer = new CommandBuffer { name = "RealGlass water surface depth" };
        }

        public RenderTexture Texture { get { return _texture; } }

        // Attach before the reflection buffer at the same event: the scene depth is ready and the
        // surface depth is produced before tracing. Its own depth attachment selects the nearest
        // water patch without writing into the camera's depth buffer.
        public void Attach(Camera camera)
        {
            if (_camera != camera)
            {
                Detach();
                _camera = camera;
                _camera.AddCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _buffer);
                Camera.onPreCull += BeforeRender;
                Camera.onPostRender += AfterRender;
            }
            if (_texture != null && _texture.width == camera.pixelWidth && _texture.height == camera.pixelHeight)
                return;

            _buffer.Clear();
            Free();
            _texture = new RenderTexture(Mathf.Max(1, camera.pixelWidth), Mathf.Max(1, camera.pixelHeight), 24,
                                         RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                name = "RealGlassWaterSurfaceDepth",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            if (!_texture.Create())
                throw new System.InvalidOperationException("Could not allocate the water surface depth texture.");
            Clear();
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
            if (camera == _camera && (_consumed || _frame != Time.frameCount))
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
                _camera.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _buffer);
            _camera = null;
            Clear();
        }

        private void Free()
        {
            if (_texture == null)
                return;
            _texture.Release();
            Dispose(_texture);
            _texture = null;
        }

        public void Destroy()
        {
            Detach();
            _buffer.Release();
            Free();
            Dispose(_material);
        }

        private static void Dispose(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
