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
        public int Preset;                  // the chosen kind of glass (GlassPresets value; 0 = Custom)
        public float EdgeSilver;            // 0..1: how far the reflection's colour turns silver edge-on (1 = as physics has it)

        // Skyline
        public bool Mix;                    // every building its own kind of glass
        public int MixMask;                 // the kinds in the mix: bit (value - 1) of each GlassPresets value

        // Reflections
        public bool Trace;                  // screen-space reflections
        public int Quality;                 // 0 Low .. 3 Ultra
        public float ReflectionBrightness;  // 0..3
        public int SkySource;               // 0 live sky, 1 the game's environment map
        public int OffscreenFill;           // 0 mirrored screen, 1 city colour, 2 sky only
        public float SunGlint;              // 0..3
        public float Smoothness;            // 0..1
        public float EdgeFade;              // 0..0.5

        // Panes
        public float PaneWidth;             // metres
        public float Pillow;                // 0..3
        public float Tilt;                  // 0..3
        public float RollerWave;            // 0..3
        public bool PaneWeather;

        // Interiors
        public bool Rooms;
        public float FloorHeight;           // metres
        public float RoomWidth;             // metres
        public float RoomDepth;             // metres
        public float InteriorBrightness;    // 0..3
        public float Blinds;                // 0..1
        public float Furniture;             // 0..1
        public float NightRooms;            // 0..3
        public float NightWarmth;           // 0..1
        public float LitShare;              // 0..1: of the windows the game lights, the share that are lit rooms
        public float FloorBand;             // metres: the slab band between storeys on plain-glass towers

        /// <summary>The defaults the mod ships (Settings.Defaults); tools/test-params.ps1 checks they agree.</summary>
        public static GlassParams Default()
        {
            GlassParams p;
            p.Enabled = true;
            p.TintR = 0x2E / 255f;
            p.TintG = 0x6A / 255f;
            p.TintB = 0x80 / 255f;
            p.OwnColour = 0.40f;
            p.Reflectivity = 0.20f;
            p.Glancing = 0.70f;
            p.Clarity = 0.24f;
            p.Strength = 1.00f;
            p.Threshold = 0.70f;
            p.NightWindows = true;
            p.ShowMask = false;
            p.Preset = 10;                  // Reflective blue
            p.EdgeSilver = 0.50f;

            p.Mix = true;
            // Reflective blue, ocean, aqua and sky blue, green, grey, clear low-E and silver: the
            // glass of most towers. Bronze (value 6) and gold (value 9) are left to the player.
            p.MixMask = (1 << 9) | (1 << 2) | (1 << 1) | (1 << 3) | (1 << 4) | (1 << 6) | (1 << 0) | (1 << 7);

            p.Trace = true;
            p.Quality = 2;
            p.ReflectionBrightness = 1.00f;
            p.SkySource = 0;
            p.OffscreenFill = 0;
            p.SunGlint = 1.00f;
            p.Smoothness = 0.92f;
            p.EdgeFade = 0.12f;

            p.PaneWidth = 1.5f;
            p.Pillow = 1.00f;
            p.Tilt = 0.60f;
            p.RollerWave = 0.60f;
            p.PaneWeather = true;

            p.Rooms = true;
            p.FloorHeight = 3.8f;
            p.RoomWidth = 12f;
            p.RoomDepth = 12f;
            p.InteriorBrightness = 1.00f;
            p.Blinds = 0.35f;
            p.Furniture = 0.60f;
            p.NightRooms = 1.00f;
            p.NightWarmth = 0.45f;
            p.LitShare = 0.80f;
            p.FloorBand = 1.0f;
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
