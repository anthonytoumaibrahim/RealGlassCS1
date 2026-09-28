using System;
using ColossalFramework;
using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// The glass renderer for one city: created by ModController, destroyed with it. Every frame
    /// it reads the settings and the scene (the sun, the sky's light, the ground, the weather),
    /// keeps the screen copy (GlassScreen) and the sky cube (GlassSky) up, sets the shader's
    /// uniforms (GlassUniforms) and tells the draws (GlassDraw, called from GlassPatches inside the
    /// game's rendering) whether to run. Off means nothing runs: no copy, no cube, no draw.
    /// </summary>
    public sealed class GlassController : MonoBehaviour
    {
        /// <summary>The live controller, or null outside a city.</summary>
        public static GlassController Instance { get; private set; }

        private static bool _dirty = true;
        private static string _status = "";

        private readonly GlassScreen _screen = new GlassScreen();
        private readonly GlassSky _sky = new GlassSky();
        private Material _near, _lod, _group, _util;
        private Texture _atlasRgb, _atlasXys, _atlasAci;
        private int _buildingLayers;
        private bool _layersKnown;
        private bool _wasOn;
        private string _lastState = "";
        private float _nextDetail;
        private int _frames;
        private float _frameTime;
        private int _lastNear, _lastLod, _lastFar;
        private Camera _watched;
        private DepthTextureMode _originalDepthMode;
        private GlassParams _params;

        /// <summary>
        /// Called by the settings catalog after any glass setting changed (every glass row's
        /// AfterChange), and once after a Reset. Cheap: the controller re-reads next frame.
        /// </summary>
        public static void SettingsChanged()
        {
            _dirty = true;
        }

        /// <summary>One line for the panel's status row; "" when there is nothing to say.</summary>
        public static string Status
        {
            get { return _status; }
        }

        /// <summary>A layer any building asset draws on (its m_prefabDataLayer): where group meshes get glass.</summary>
        public static bool IsBuildingLayer(int layer)
        {
            GlassController c = Instance;
            if (c == null || layer < 0 || layer > 31)
                return false;
            if (!c._layersKnown)
                c.FindBuildingLayers();
            return (c._buildingLayers & (1 << layer)) != 0;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            try
            {
                Shader glass = ShaderBundle.Get(ShaderBundle.Glass);
                Shader util = ShaderBundle.Get(ShaderBundle.GlassUtil);
                if (glass == null || !glass.isSupported)
                {
                    Log.Warn("glass: the glass shader is " + (glass == null ? "missing" : "not supported here") + ": no glass");
                    enabled = false;
                    return;
                }

                _near = MakeMaterial(glass, "RG_NEAR");
                _lod = MakeMaterial(glass, "RG_LOD");
                _group = MakeMaterial(glass, "RG_GROUP");
                if (util != null && util.isSupported)
                    _util = new Material(util) { name = "RealGlass util", hideFlags = HideFlags.HideAndDontSave };
                else
                    Log.Warn("glass: the GlassUtil shader is " + (util == null ? "missing" : "not supported") + ": reflections of the sky only");

                GlassDraw.Reset();
                GlassDraw.Near = _near;
                GlassDraw.Lod = _lod;
                GlassDraw.Group = _group;
                FindBuildingLayers();
                Log.Msg("glass: renderer ready (shader '" + glass.name + "', util " + (_util != null ? "yes" : "no") +
                        "); building layers " + DescribeLayers() + "; " + DescribeSettings(GlassSettings.Read()));
            }
            catch (Exception e)
            {
                Log.Error("glass: could not start the renderer", e);
                enabled = false;
            }
        }

        private static Material MakeMaterial(Shader shader, string keyword)
        {
            var m = new Material(shader) { name = "RealGlass " + keyword, hideFlags = HideFlags.HideAndDontSave };
            m.EnableKeyword(keyword);
            return m;
        }

        private void FindBuildingLayers()
        {
            _buildingLayers = 0;
            int count = PrefabCollection<BuildingInfo>.LoadedCount();
            for (uint i = 0; i < count; i++)
            {
                BuildingInfo info = PrefabCollection<BuildingInfo>.GetLoaded(i);
                if (info != null && info.m_prefabDataLayer >= 0 && info.m_prefabDataLayer < 32)
                    _buildingLayers |= 1 << info.m_prefabDataLayer;
            }

            _layersKnown = count > 0;
        }

        private string DescribeLayers()
        {
            string s = "";
            for (int i = 0; i < 32; i++)
            {
                if ((_buildingLayers & (1 << i)) != 0)
                    s += (s.Length > 0 ? ", " : "") + i + " " + LayerMask.LayerToName(i);
            }
            return s.Length > 0 ? s : "none";
        }

        private void LateUpdate()
        {
            try
            {
                Frame();
            }
            catch (Exception e)
            {
                Log.Error("glass: frame failed; the glass is off until the next change", e);
                GlassDraw.Active = false;
                _screen.Detach();
                _dirty = false;
                enabled = false;
            }
        }

        private void Frame()
        {
            _frames++;
            _frameTime += Time.unscaledDeltaTime;
            if (_dirty)
            {
                _params = GlassSettings.Read();
                _dirty = false;
            }

            GlassParams p = _params;
            Camera camera = Camera.main;
            bool infoView = Singleton<InfoManager>.exists && Singleton<InfoManager>.instance.CurrentMode != InfoManager.InfoMode.None;
            bool on = p.Enabled && camera != null && _near != null && !infoView;

            string state = !p.Enabled ? "off" : (camera == null ? "no camera" : (infoView ? "an info view is open" : "on"));
            if (state != _lastState)
            {
                Log.Msg("glass: " + state);
                _lastState = state;
            }

            if (!on)
            {
                GlassDraw.Active = false;
                if (_wasOn)
                {
                    // Off means nothing runs: the copy leaves the camera, the cube is freed, the
                    // camera's depth texture is as the game had it.
                    _screen.Detach();
                    _sky.Release();
                    RestoreCamera();
                    _wasOn = false;
                }
                _status = Localization.Get(p.Enabled ? (infoView ? "Status.InfoView" : "Status.Waiting") : "Status.Off");
                return;
            }

            _wasOn = true;
            GlassDraw.Tiers = Settings.IsInitialised ? Settings.GlassDistance.value : 2;
            BindAtlases();
            WatchCamera(camera);

            GlassFrame f = ReadFrame(camera);
            int size = Settings.IsInitialised ? Settings.SkyCubePixels : 128;
            int rate = Settings.IsInitialised ? Settings.SkyCubeRate.value : 1;
            _sky.Update(p.SkySource == 0, size, rate, Shader.GetGlobalTexture("_EnvironmentCubemap"), camera.transform.position);
            f.CubeMips = _sky.Mips;
            GlassUniforms.Apply(p, f);
            bool copied = _screen.Update(camera, _util);
            if (!copied)
            {
                // Without the copy nothing on screen can be reflected: the trace and the mirrored
                // fill fall back to the city colour / sky (the shader reads what is bound).
                Shader.SetGlobalVector("_RGTrace", new Vector4(0f, 0f, 1f, 1f));
            }

            GlassDraw.Active = true;
            // Last frame's draws per tier (the counters only grow between the 5 s log lines).
            int near = GlassDraw.NearDraws - _lastNear, lod = GlassDraw.LodDraws - _lastLod, far = GlassDraw.GroupDraws - _lastFar;
            _status = Localization.Get("Status.On", Mathf.Max(near, 0), Mathf.Max(lod, 0), Mathf.Max(far, 0));
            _lastNear = GlassDraw.NearDraws;
            _lastLod = GlassDraw.LodDraws;
            _lastFar = GlassDraw.GroupDraws;

            if (Log.Detailed && Time.realtimeSinceStartup >= _nextDetail)
            {
                _nextDetail = Time.realtimeSinceStartup + 5f;
                float ms = _frames > 0 ? 1000f * _frameTime / _frames : 0f;
                Log.Detail("glass: 5 s: " + GlassDraw.NearDraws / 5 + " near, " + GlassDraw.LodDraws / 5 + " middle, " +
                           GlassDraw.GroupDraws / 5 + " far draws a second (" + GlassDraw.NearSkipped / 5 + " near skipped: not glass-capable); " +
                           "frame " + ms.ToString("F1") + " ms; copy " + (copied ? _screen.Width + "x" + _screen.Height : "OFF " + _screen.Problem) +
                           "; sky " + (p.SkySource == 0 ? _sky.FacesDone + "/6 faces " + _sky.Mips + " mips" + (_sky.Problem.Length > 0 ? " " + _sky.Problem : "") : "the game's map") +
                           "; daylight " + GlassUniforms.Daylight(f.SunY).ToString("F2") + ", sun (" + f.SunR.ToString("F2") + " " + f.SunG.ToString("F2") + " " + f.SunB.ToString("F2") +
                           "), ground " + f.GroundY.ToString("F0") + " m, " + f.Temperature.ToString("F0") + " C; shaders seen: " + GlassDraw.DescribeShaders());
                ResetCounters();
            }
            else if (!Log.Detailed && _frames >= 300)
            {
                ResetCounters();
            }
        }

        private void ResetCounters()
        {
            GlassDraw.NearDraws = GlassDraw.LodDraws = GlassDraw.GroupDraws = GlassDraw.NearSkipped = 0;
            _lastNear = _lastLod = _lastFar = 0;
            _frames = 0;
            _frameTime = 0f;
        }

        /// <summary>
        /// The trace reads _CameraDepthTexture: the camera must make one (it usually does; if not,
        /// ask for it, and give the camera back as it was on the way out). Logged once per camera:
        /// the copy is taken after the DEFERRED final pass, so another path is worth knowing about.
        /// </summary>
        private void WatchCamera(Camera camera)
        {
            if (camera == _watched)
                return;

            RestoreCamera();
            _watched = camera;
            _originalDepthMode = camera.depthTextureMode;
            if ((camera.depthTextureMode & DepthTextureMode.Depth) == 0)
                camera.depthTextureMode |= DepthTextureMode.Depth;
            Log.Msg("glass: camera '" + camera.name + "' " + camera.actualRenderingPath + ", HDR " + camera.allowHDR + ", depth texture " +
                    ((_originalDepthMode & DepthTextureMode.Depth) != 0 ? "already made" : "ASKED FOR (was " + _originalDepthMode + ")") +
                    ", " + camera.pixelWidth + "x" + camera.pixelHeight + ", fov " + camera.fieldOfView.ToString("F0") +
                    (camera.actualRenderingPath != RenderingPath.DeferredLighting ? " -- NOT the legacy deferred path the copy is timed for" : ""));
        }

        private void RestoreCamera()
        {
            if (_watched != null && (_originalDepthMode & DepthTextureMode.Depth) == 0)
                _watched.depthTextureMode = _originalDepthMode;
            _watched = null;
        }

        /// <summary>The middle and far tiers read the game's shared LOD atlases; they are made once per load.</summary>
        private void BindAtlases()
        {
            BuildingManager bm = Singleton<BuildingManager>.instance;
            if (bm.m_lodRgbAtlas == _atlasRgb && bm.m_lodXysAtlas == _atlasXys && bm.m_lodAciAtlas == _atlasAci)
                return;

            _atlasRgb = bm.m_lodRgbAtlas;
            _atlasXys = bm.m_lodXysAtlas;
            _atlasAci = bm.m_lodAciAtlas;
            foreach (Material m in new[] { _lod, _group })
            {
                m.SetTexture("_MainTex", _atlasRgb);
                m.SetTexture("_XYSMap", _atlasXys);
                m.SetTexture("_ACIMap", _atlasAci);
            }

            Log.Msg("glass: LOD atlases " + Describe(_atlasRgb) + " / " + Describe(_atlasXys) + " / " + Describe(_atlasAci));
        }

        private static string Describe(Texture t)
        {
            return t == null ? "none" : t.name + " " + t.width + "x" + t.height;
        }

        /// <summary>The scene as the glass needs it: the game's sun, its ambient, the ground, the weather.</summary>
        private GlassFrame ReadFrame(Camera camera)
        {
            GlassFrame f = new GlassFrame();
            Vector3 toSun = new Vector3(0f, -1f, 0f);
            Color sun = Color.black;
            DayNightProperties dn = DayNightProperties.instance;
            if (dn != null && dn.m_SunLight != null)
            {
                toSun = -dn.m_SunLight.forward;
                Light light = dn.m_SunLight.GetComponent<Light>();
                if (light != null && light.enabled)
                    sun = light.color.linear * light.intensity;
            }

            f.SunX = toSun.x;
            f.SunY = toSun.y;
            f.SunZ = toSun.z;
            f.SunR = sun.r;
            f.SunG = sun.g;
            f.SunB = sun.b;
            Color sky = RenderSettings.ambientSkyColor.linear, ground = RenderSettings.ambientGroundColor.linear;
            f.AmbSkyR = sky.r;
            f.AmbSkyG = sky.g;
            f.AmbSkyB = sky.b;
            f.AmbGroundR = ground.r;
            f.AmbGroundG = ground.g;
            f.AmbGroundB = ground.b;

            // The ground where the camera looks: what a far block's floors and the reflected
            // street are measured from.
            Vector3 look = camera.transform.position;
            CameraController cc = camera.GetComponent<CameraController>();
            if (cc != null)
                look = cc.m_currentPosition;
            f.GroundY = Singleton<TerrainManager>.exists ? Singleton<TerrainManager>.instance.SampleDetailHeight(look) : 0f;
            f.Temperature = Singleton<WeatherManager>.exists ? Singleton<WeatherManager>.instance.m_currentTemperature : 15f;
            f.ScreenHeight = camera.pixelHeight;
            return f;
        }

        /// <summary>The settings in one line: what the log's "glass:" lines are read against.</summary>
        public static string DescribeSettings(GlassParams p)
        {
            return "glass " + (p.Enabled ? "on" : "off") + ", reflect " + Pct(p.Reflectivity) + "..." + Pct(p.Glancing) + ", clarity " + Pct(p.Clarity) +
                   ", tint #" + Hex(p.TintR) + Hex(p.TintG) + Hex(p.TintB) + " own " + Pct(p.OwnColour) + ", strength " + Pct(p.Strength) +
                   ", glass above " + Pct(p.Threshold) + (p.NightWindows ? " + night windows" : "") + (p.ShowMask ? ", SHOWING THE MASK" : "") +
                   "; trace " + (p.Trace ? "q" + p.Quality : "off") + ", brightness " + Pct(p.ReflectionBrightness) + ", sky " + (p.SkySource == 0 ? "live" : "game map") +
                   ", fill " + p.OffscreenFill + ", glint " + Pct(p.SunGlint) + ", smooth " + Pct(p.Smoothness) +
                   "; panes " + p.PaneWidth.ToString("F1") + " m, pillow " + Pct(p.Pillow) + ", tilt " + Pct(p.Tilt) + ", roller " + Pct(p.RollerWave) +
                   (p.PaneWeather ? " (weather)" : "") + "; rooms " + (p.Rooms ? p.FloorHeight.ToString("F1") + " x " + p.RoomWidth.ToString("F0") + " x " + p.RoomDepth.ToString("F0") + " m" : "off") +
                   ", blinds " + Pct(p.Blinds) + ", furniture " + Pct(p.Furniture) + ", night " + Pct(p.NightRooms) + " warmth " + Pct(p.NightWarmth);
        }

        private static string Pct(float v)
        {
            return Mathf.RoundToInt(v * 100f) + "%";
        }

        private static string Hex(float c)
        {
            return Mathf.Clamp(Mathf.RoundToInt(c * 255f), 0, 255).ToString("X2");
        }

        private void OnDestroy()
        {
            GlassDraw.Active = false;
            GlassDraw.Near = GlassDraw.Lod = GlassDraw.Group = null;
            GlassDraw.Reset();
            _screen.Detach();
            _sky.Destroy();
            RestoreCamera();
            foreach (Material m in new[] { _near, _lod, _group, _util })
            {
                if (m != null)
                    Destroy(m);
            }

            _status = "";
            if (Instance == this)
                Instance = null;
            Log.Msg("glass: renderer stopped");
        }
    }
}
