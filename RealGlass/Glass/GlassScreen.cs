using UnityEngine;
using UnityEngine.Rendering;

namespace RealGlass.Glass
{
    /// <summary>
    /// The pictures the glass reads. A command buffer on the camera, right after the deferred final
    /// pass (before any glass is drawn, before the fog), copies the lit opaque picture into _RGScene
    /// (mipmapped: rough glass reads a blurrier level) -- the pixel UNDER each pane -- and averages
    /// its lower half into the 1x1 _RGCity. A second one, at the end of the frame (after the fog, the
    /// transparent things -- clouds, the lamps' glow -- and the glass itself), copies the FINISHED
    /// picture into _RGSceneFinished: what the next frame's glass reflects, read where each
    /// reflected point was last frame (_RGPrevVP, the camera's matrices as it rendered) -- the
    /// previous frame reprojected, as Killzone Shadow Fall's reflections (GDC 2014) did.
    /// All hold finite light only (GlassUtil says why), and the city colour is blended by the
    /// shader from a copy of last frame's (_cityPrev), so nothing bad can stay in it.
    /// Engine types only: the GPU preview runs this same file.
    /// </summary>
    public sealed class GlassScreen
    {
        /// <summary>After the deferred final pass: lit, opaque, not yet fogged.</summary>
        public const CameraEvent When = CameraEvent.AfterFinalPass;

        /// <summary>The end of the frame, before the image effects (tone mapping): still HDR, fogged, everything drawn.</summary>
        public const CameraEvent FinishedWhen = CameraEvent.BeforeImageEffects;

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

        private CommandBuffer _finishedBuffer;
        private RenderTexture _finished;
        private Matrix4x4 _lastViewProjection = Matrix4x4.identity;
        private int _renders;
        private Camera.CameraCallback _onPreRender;

        public RenderTexture Scene { get { return _scene; } }
        public RenderTexture City { get { return _city; } }
        public int Width { get { return _width; } }
        public int Height { get { return _height; } }

        /// <summary>True while the finished picture is copied and read (it exists after the camera's first render).</summary>
        public bool Finished { get { return _finished != null && _renders > 0; } }

        /// <summary>Why the last Update failed ("" when it did not).</summary>
        public string Problem = "";

        /// <summary>
        /// Keeps the copies the camera's size and the buffers attached to it. finished: also copy
        /// the finished picture for the reflections. Returns false when there is nothing to copy
        /// with (no camera, no util shader): the glass then reflects the sky only.
        /// </summary>
        public bool Update(Camera camera, Material util, bool finished)
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

            if (finished)
                AttachFinished();
            else
                DropFinished();

            Shader.SetGlobalTexture("_RGScene", _scene);
            Shader.SetGlobalTexture("_RGCity", _city);
            // A new target holds whatever its memory held last: its first frame takes this
            // frame's average alone.
            Shader.SetGlobalFloat("_RGCityBlend", _fresh ? 1f : CityBlend);
            _fresh = false;

            // Last frame's picture and the matrices it was drawn with; until the camera has rendered
            // once with the copy on, there is none and the shader reads _RGScene as before.
            Shader.SetGlobalTexture("_RGSceneFinished", Finished ? _finished : _scene);
            Shader.SetGlobalMatrix("_RGPrevVP", _lastViewProjection);
            Shader.SetGlobalVector("_RGFinished", new Vector4(Finished ? 1f : 0f, 0f, 0f, 0f));
            Problem = "";
            return true;
        }

        private void Build()
        {
            _scene = NewPicture("RealGlass scene");

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

            // The camera's matrices as it renders, for the next frame's reprojection. The plain
            // (OpenGL-convention) projection: ComputeScreenPos undoes Unity's render-texture flip,
            // so screen uv from it is what the copies are read with.
            _onPreRender = OnPreRender;
            Camera.onPreRender += _onPreRender;
        }

        private void OnPreRender(Camera camera)
        {
            if (camera != _camera)
                return;

            _lastViewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            if (_finished != null)
                _renders++;
        }

        private void AttachFinished()
        {
            if (_finishedBuffer != null)
                return;

            _finished = NewPicture("RealGlass finished picture");
            _renders = 0;
            _finishedBuffer = new CommandBuffer();
            _finishedBuffer.name = "RealGlass finished picture";
            _finishedBuffer.Blit(BuiltinRenderTextureType.CameraTarget, _finished, _util, PassCopy);
            _camera.AddCommandBuffer(FinishedWhen, _finishedBuffer);
        }

        private void DropFinished()
        {
            if (_finishedBuffer != null)
            {
                if (_camera != null)
                    _camera.RemoveCommandBuffer(FinishedWhen, _finishedBuffer);
                _finishedBuffer.Release();
                _finishedBuffer = null;
            }

            Free(ref _finished);
            _renders = 0;
        }

        private RenderTexture NewPicture(string name)
        {
            var rt = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rt.name = name;
            rt.useMipMap = true;
            rt.autoGenerateMips = true;
            rt.filterMode = FilterMode.Trilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.Create();
            return rt;
        }

        private static RenderTexture NewCity(string name)
        {
            var rt = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rt.name = name;
            rt.Create();
            return rt;
        }

        /// <summary>Takes the buffers off the camera and frees the targets (unload, resize, off).</summary>
        public void Detach()
        {
            DropFinished();
            if (_buffer != null)
            {
                if (_camera != null)
                    _camera.RemoveCommandBuffer(When, _buffer);
                _buffer.Release();
                _buffer = null;
            }

            if (_onPreRender != null)
            {
                Camera.onPreRender -= _onPreRender;
                _onPreRender = null;
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
