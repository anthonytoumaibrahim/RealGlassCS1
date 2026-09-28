namespace RealGlass.Glass
{
    /// <summary>
    /// Everything the player sets about the glass, in the settings' own units (a percent row's
    /// 47% is 0.47). PURE: no engine or game types, so the GPU preview (UnityProject, through
    /// tools/preview/sync-sim.ps1) fills the same struct and draws with the same uniforms.
    /// GlassController fills it from Settings; the preview from its command line.
    /// </summary>
    public struct GlassParams
    {
        // Glass
        public bool Enabled;
        public float TintR, TintG, TintB;   // the chosen glass colour, sRGB 0..1
        public float OwnColour;             // 0..1: how much of the building's own painted colour is kept
        public float Reflectivity;          // 0..0.6: reflectance looking straight at it (F0)
        public float Glancing;              // 0..1: reflectance edge-on
        public float Clarity;               // 0..1: visible light let through
        public float Strength;              // 0..1: our glass over the game's
        public float Threshold;             // 0.4..0.95: what counts as glass (the asset's shininess)
        public bool NightWindows;           // windows lit at night count as glass
        public bool ShowMask;
        public float EdgeSilver;            // 0..1: how far the reflection's colour turns silver edge-on (1 = as physics has it)

        // Reflections
        public bool Trace;                  // screen-space reflections
        public bool Finished;               // reflect last frame's finished picture (fog, clouds, lamps), reprojected
        public bool Neighbours;             // rays that miss the screen walk the city's height map (CityMap)
        public int Quality;                 // 0 Low .. 3 Ultra
        public float ReflectionBrightness;  // 0..3
        public int SkySource;               // 0 live sky, 1 the game's environment map
        public bool SkyClouds;              // the live sky carries the clouds (Volumetric Weather's volume)
        public int OffscreenFill;           // 0 mirrored screen, 1 city colour, 2 sky only
        public float SunGlint;              // 0..3
        public bool SteadyGlints;           // specular anti-aliasing of the bent panes
        public float Smoothness;            // 0..1
        public float EdgeFade;              // 0..0.5

        // Panes
        public float PaneWidth;             // metres
        public float FloorHeight;           // metres: a storey (a pane's height on its own grid; the mirrored neighbours' floors)
        public float Pillow;                // 0..3
        public float Tilt;                  // 0..3
        public float RollerWave;            // 0..3
        public bool PaneWeather;
        public bool PanesFollowWindows;     // close up, the panes are the windows the asset painted

        /// <summary>The defaults the mod ships (Settings.Defaults); tools/test-params.ps1 checks they agree.</summary>
        public static GlassParams Default()
        {
            GlassParams p;
            p.Enabled = true;
            p.TintR = 0x2E / 255f;
            p.TintG = 0x6A / 255f;
            p.TintB = 0x80 / 255f;
            p.OwnColour = 0.30f;
            p.Reflectivity = 0.30f;
            p.Glancing = 0.80f;
            p.Clarity = 0.24f;
            p.Strength = 1.00f;
            p.Threshold = 0.70f;
            p.NightWindows = true;
            p.ShowMask = false;
            p.EdgeSilver = 1.00f;

            p.Trace = true;
            p.Finished = true;
            p.Neighbours = true;
            p.Quality = 3;
            p.ReflectionBrightness = 0.50f;
            p.SkySource = 0;
            p.SkyClouds = true;
            p.OffscreenFill = 0;
            p.SunGlint = 3.00f;
            p.SteadyGlints = true;
            p.Smoothness = 1.00f;
            p.EdgeFade = 0.50f;

            p.PaneWidth = 1.4f;
            p.FloorHeight = 3.8f;
            p.Pillow = 3.00f;
            p.Tilt = 3.00f;
            p.RollerWave = 3.00f;
            p.PaneWeather = true;
            p.PanesFollowWindows = true;
            return p;
        }
    }

    /// <summary>
    /// What the scene gives the glass this frame: the sun, the sky's light, where the ground is.
    /// The mod reads the game (DayNightProperties, RenderSettings, TerrainManager, WeatherManager);
    /// the preview its own scene.
    /// </summary>
    public struct GlassFrame
    {
        public float SunX, SunY, SunZ;           // towards the sun, world, unit
        public float SunR, SunG, SunB;           // the sun's radiance (linear colour x intensity)
        public float AmbSkyR, AmbSkyG, AmbSkyB;  // light from the sky (linear)
        public float AmbGroundR, AmbGroundG, AmbGroundB;
        public float GroundY;                    // world height of the ground around the view
        public float Temperature;                // degrees C (the game's weather)
        public int ScreenHeight;                 // pixels
        public int CubeMips;                     // mip levels of the sky cube
        public int ColorMapWidth;                // the game's object colour map, whose texel a building's
        public int ColorMapHeight;               // colour index names (RenderManager.OBJECT_COLORMAP_*)
    }
}
