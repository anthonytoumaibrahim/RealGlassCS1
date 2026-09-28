using UnityEngine;
using UnityEngine.Rendering;

namespace RealGlass.Glass
{
    /// <summary>
    /// The picture the glass reflects: a command buffer on the camera, right after the deferred
    /// final pass (before any glass is drawn, before the fog), copies the lit opaque picture into
    /// _RGScene (mipmapped: rough glass reads a blurrier level) and averages its lower half into the
    /// 1x1 _RGCity. Both hold finite light only (GlassUtil says why), and the city colour is blended
    /// by the shader from a copy of last frame's (_cityPrev), so nothing bad can stay in it.
    /// Engine types only: the GPU preview runs this same file.
    /// </summary>
    public sealed class GlassScreen
    {
        /// <summary>After the deferred final pass: lit, opaque, not yet fogged.</summary>
        public const CameraEvent When = CameraEvent.AfterFinalPass;

        /// <summary>This frame's share of the city colour (the rest is last frame's): ~0.3 s to settle.</summary>
        public const float CityBlend = 0.1f;

        // GlassUtil's passes.
        private const int PassCity = 0, PassCopy = 1;

        private Camera _camera;
        private Material _util;
        private CommandBuffer _buffer;
        private RenderTexture _scene;
        private RenderTexture _city, _cityPrev;
        private int _width, _height;
        private bool _fresh;

        public RenderTexture Scene { get { return _scene; } }
        public RenderTexture City { get { return _city; } }
        public int Width { get { return _width; } }
        public int Height { get { return _height; } }

        /// <summary>Why the last Update failed ("" when it did not).</summary>
        public string Problem = "";

        /// <summary>
        /// Keeps the copy the camera's size and the buffer attached to it. Returns false when there
        /// is nothing to copy with (no camera, no util shader): the glass then reflects the sky only.
        /// </summary>
        public bool Update(Camera camera, Material util)
        {
            if (camera == null || util == null)
            {
                Problem = camera == null ? "no camera" : "no GlassUtil shader";
                Detach();
                return false;
            }

            int w = Mathf.Max(camera.pixelWidth, 16);
            int h = Mathf.Max(camera.pixelHeight, 16);
            if (camera != _camera || util != _util || w != _width || h != _height || _scene == null || !_scene.IsCreated())
            {
                Detach();
                _camera = camera;
                _util = util;
                _width = w;
                _height = h;
                Build();
            }

            Shader.SetGlobalTexture("_RGScene", _scene);
            Shader.SetGlobalTexture("_RGCity", _city);
            // A new target holds whatever its memory held last: its first frame takes this
            // frame's average alone.
            Shader.SetGlobalFloat("_RGCityBlend", _fresh ? 1f : CityBlend);
            _fresh = false;
            Problem = "";
            return true;
        }

        private void Build()
        {
            _scene = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            _scene.name = "RealGlass scene";
            _scene.useMipMap = true;
            _scene.autoGenerateMips = true;
            _scene.filterMode = FilterMode.Trilinear;
            _scene.wrapMode = TextureWrapMode.Clamp;
            _scene.Create();

            _city = NewCity("RealGlass city colour");
            _cityPrev = NewCity("RealGlass city colour, last frame");
            _util.SetTexture("_RGCityPrev", _cityPrev);

            _buffer = new CommandBuffer();
            _buffer.name = "RealGlass screen copy";
            _buffer.Blit(BuiltinRenderTextureType.CameraTarget, _scene, _util, PassCopy);
            _buffer.Blit(_city, _cityPrev);
            _buffer.Blit(_scene, _city, _util, PassCity);
            _camera.AddCommandBuffer(When, _buffer);
            _fresh = true;
        }

        private static RenderTexture NewCity(string name)
        {
            var rt = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rt.name = name;
            rt.Create();
            return rt;
        }

        /// <summary>Takes the buffer off the camera and frees the targets (unload, resize, off).</summary>
        public void Detach()
        {
            if (_buffer != null)
            {
                if (_camera != null)
                    _camera.RemoveCommandBuffer(When, _buffer);
                _buffer.Release();
                _buffer = null;
            }

            Free(ref _scene);
            Free(ref _city);
            Free(ref _cityPrev);
            _camera = null;
            _width = _height = 0;
        }

        private static void Free(ref RenderTexture rt)
        {
            if (rt == null)
                return;
            rt.Release();
            Object.DestroyImmediate(rt);
            rt = null;
        }
    }
}
