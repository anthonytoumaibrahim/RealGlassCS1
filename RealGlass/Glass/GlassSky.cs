using UnityEngine;
using UnityEngine.Rendering;

namespace RealGlass.Glass
{
    /// <summary>
    /// The sky the glass reflects when the screen has nothing: a small HDR cube of the game's OWN
    /// skybox (RenderSettings.skybox: its scattering sky, sun and all), rendered by a hidden camera
    /// that draws nothing else (culling mask 0, clear = skybox), a face or a few a frame -- the way
    /// Mirror's Edge Catalyst kept its reflection cubes in step with the time of day, and GTA V
    /// rendered its cubemap every frame -- with Volumetric Weather's clouds drawn into it when that
    /// mod draws them. Or the game's static environment map, if asked.
    /// Engine types only: the GPU preview runs this same file.
    /// </summary>
    public sealed class GlassSky
    {
        /// <summary>
        /// Volumetric Weather's clouds: a mesh renderer in the scene (a box round the camera, its
        /// raymarch material), found by name.
        /// </summary>
        public const string CloudVolumeName = "VolumetricCloudsVolume";

        private GameObject _holder;
        private Camera _camera;
        private RenderTexture _cube;
        private int _size;
        private int _nextFace;
        private int _facesDone;

        private CommandBuffer _clouds;
        private MaterialPropertyBlock _cloudBlock;
        private MeshRenderer _cloudRenderer;
        private MeshFilter _cloudFilter;
        private float _nextCloudSearch;

        /// <summary>Mip levels of the cube (0 when the game's map is used: its own count is unknown).</summary>
        public int Mips { get; private set; }

        /// <summary>Faces rendered since the cube was made (6 = complete).</summary>
        public int FacesDone { get { return _facesDone; } }

        /// <summary>True while the live sky carries the clouds.</summary>
        public bool DrawsClouds { get; private set; }

        public string Problem = "";

        /// <summary>
        /// The global the cube is bound to: the glass's "_RGSky"; the water's sky layer keeps its own
        /// cube under another name (Water/WaterSkyLayer.cs).
        /// </summary>
        public string GlobalName = "_RGSky";

        /// <summary>The live cube (null until the first update, or while the game's map is used).</summary>
        public Texture Cube { get { return _cube; } }

        /// <summary>
        /// live: our cube of the skybox at size x size per face, facesPerFrame faces refreshed now,
        /// with the clouds in it when clouds is set and there are clouds to draw; else gameMap (the
        /// game's _EnvironmentCubemap) is bound as it is.
        /// </summary>
        public void Update(bool live, int size, int facesPerFrame, Texture gameMap, Vector3 position, bool clouds)
        {
            if (!live || RenderSettings.skybox == null)
            {
                Problem = live ? "no skybox" : "";
                DrawsClouds = false;
                Release();
                Mips = gameMap is Cubemap ? ((Cubemap)gameMap).mipmapCount : 1;
                if (gameMap != null)
                    Shader.SetGlobalTexture(GlobalName, gameMap);
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
            DrawsClouds = UpdateClouds(clouds);

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
            Shader.SetGlobalTexture(GlobalName, _cube);
        }

        /// <summary>
        /// The clouds in the sky's faces: the sky camera draws Volumetric Weather's own cloud mesh,
        /// with its own material, from a command buffer right after the skybox -- the same clouds
        /// the player sees, from the same point -- with the material's scene-depth test off for
        /// this draw alone (_UseDepth = 0 in a property block: the main camera's depth texture
        /// means nothing to a face of the cube). Unreal's Sky Light captures its volumetric clouds
        /// into the reflection cube the same way. Found by name, looked for every 2 s while absent.
        /// </summary>
        private bool UpdateClouds(bool wanted)
        {
            if (wanted && _cloudRenderer == null && Time.realtimeSinceStartup >= _nextCloudSearch)
            {
                _nextCloudSearch = Time.realtimeSinceStartup + 2f;
                GameObject volume = GameObject.Find(CloudVolumeName);
                if (volume != null)
                {
                    _cloudRenderer = volume.GetComponent<MeshRenderer>();
                    _cloudFilter = volume.GetComponent<MeshFilter>();
                }
            }

            bool draw = wanted && _cloudRenderer != null && _cloudRenderer.enabled && _cloudFilter != null &&
                        _cloudFilter.sharedMesh != null && _cloudRenderer.sharedMaterial != null;
            if (!draw)
            {
                DropClouds();
                return false;
            }

            if (_clouds == null)
            {
                _clouds = new CommandBuffer();
                _clouds.name = "RealGlass sky clouds";
                _cloudBlock = new MaterialPropertyBlock();
                _camera.AddCommandBuffer(CameraEvent.AfterSkybox, _clouds);
            }

            _clouds.Clear();
            _cloudBlock.SetFloat("_UseDepth", 0f);
            _clouds.DrawMesh(_cloudFilter.sharedMesh, _cloudRenderer.transform.localToWorldMatrix, _cloudRenderer.sharedMaterial, 0, 0, _cloudBlock);
            return true;
        }

        private void DropClouds()
        {
            if (_clouds == null)
                return;

            if (_camera != null)
                _camera.RemoveCommandBuffer(CameraEvent.AfterSkybox, _clouds);
            _clouds.Release();
            _clouds = null;
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
            DropClouds();
            DrawsClouds = false;
            _cloudRenderer = null;
            _cloudFilter = null;
            if (_holder != null)
            {
                Object.DestroyImmediate(_holder);
                _holder = null;
                _camera = null;
            }
        }
    }
}
