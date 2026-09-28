using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// The sky the glass reflects when the screen has nothing: a small HDR cube of the game's OWN
    /// skybox (RenderSettings.skybox: its scattering sky, sun and all), rendered by a hidden camera
    /// that draws nothing else (culling mask 0, clear = skybox), a face or a few a frame -- the way
    /// Mirror's Edge Catalyst kept its reflection cubes in step with the time of day, and GTA V
    /// rendered its cubemap every frame. Or the game's static environment map, if asked.
    /// Engine types only: the GPU preview runs this same file.
    /// </summary>
    public sealed class GlassSky
    {
        private GameObject _holder;
        private Camera _camera;
        private RenderTexture _cube;
        private int _size;
        private int _nextFace;
        private int _facesDone;

        /// <summary>Mip levels of the cube (0 when the game's map is used: its own count is unknown).</summary>
        public int Mips { get; private set; }

        /// <summary>Faces rendered since the cube was made (6 = complete).</summary>
        public int FacesDone { get { return _facesDone; } }

        public string Problem = "";

        /// <summary>
        /// live: our cube of the skybox at size x size per face, facesPerFrame faces refreshed now;
        /// else gameMap (the game's _EnvironmentCubemap) is bound as it is.
        /// </summary>
        public void Update(bool live, int size, int facesPerFrame, Texture gameMap, Vector3 position)
        {
            if (!live || RenderSettings.skybox == null)
            {
                Problem = live ? "no skybox" : "";
                Release();
                Mips = gameMap is Cubemap ? ((Cubemap)gameMap).mipmapCount : 1;
                if (gameMap != null)
                    Shader.SetGlobalTexture("_RGSky", gameMap);
                return;
            }

            size = Mathf.Clamp(Mathf.ClosestPowerOfTwo(size), 16, 512);
            if (_cube == null || !_cube.IsCreated() || _size != size)
            {
                Release();
                _size = size;
                _cube = new RenderTexture(size, size, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                _cube.name = "RealGlass sky";
                _cube.dimension = UnityEngine.Rendering.TextureDimension.Cube;
                _cube.useMipMap = true;
                _cube.autoGenerateMips = true;
                _cube.filterMode = FilterMode.Trilinear;
                _cube.Create();
                _nextFace = 0;
                _facesDone = 0;
                int mips = 1;
                for (int s = size; s > 1; s >>= 1)
                    mips++;
                Mips = mips;
            }

            if (_camera == null)
            {
                _holder = new GameObject("RealGlass sky camera");
                _holder.hideFlags = HideFlags.HideAndDontSave;
                _camera = _holder.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.cullingMask = 0;
                _camera.clearFlags = CameraClearFlags.Skybox;
                _camera.renderingPath = RenderingPath.Forward;
                _camera.allowHDR = true;
                _camera.allowMSAA = false;
                _camera.nearClipPlane = 1f;
                _camera.farClipPlane = 100000f;
            }

            _holder.transform.position = position;

            // A new cube renders all six faces at once; after that, a few a frame.
            int count = _facesDone < 6 ? 6 : Mathf.Clamp(facesPerFrame, 1, 6);
            int mask = 0;
            for (int i = 0; i < count; i++)
            {
                mask |= 1 << _nextFace;
                _nextFace = (_nextFace + 1) % 6;
            }

            if (!_camera.RenderToCubemap(_cube, mask))
            {
                Problem = "RenderToCubemap failed";
                return;
            }

            _facesDone = Mathf.Min(_facesDone + count, 6);
            Problem = "";
            Shader.SetGlobalTexture("_RGSky", _cube);
        }

        public void Release()
        {
            if (_cube != null)
            {
                _cube.Release();
                Object.DestroyImmediate(_cube);
                _cube = null;
            }

            _size = 0;
            _facesDone = 0;
        }

        public void Destroy()
        {
            Release();
            if (_holder != null)
            {
                Object.DestroyImmediate(_holder);
                _holder = null;
                _camera = null;
            }
        }
    }
}
