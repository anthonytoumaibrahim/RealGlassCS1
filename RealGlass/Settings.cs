using System;
using ColossalFramework;
using UnityEngine;

namespace RealGlass
{
    /// <summary>
    /// Persisted options, kept in RealGlass.xml next to the log (see <see cref="SettingsXml"/>),
    /// where a player can read and edit them.
    /// </summary>
    /// <remarks>
    /// Every default is a named constant in <see cref="Defaults"/> and nowhere else: the
    /// constructors below read them, the renderer's fallbacks read them, and
    /// <see cref="UI.SettingsCatalog"/> carries the same constant on every row, which is what lets
    /// "Reset all settings to defaults" exist at all. A setting does NOT remember its default --
    /// the constructor's value is only what it holds until the file is read -- so the catalog is
    /// the only thing that can put a default back.
    ///
    /// Editing a default does not change a machine that already has RealGlass.xml: the default is
    /// only used when the setting's line is absent. Use the Reset button (or delete that file with
    /// the game closed) to see a first run.
    ///
    /// The first argument of each constructor is the setting's NAME in the file. When what a
    /// setting means changes, give it a new name: a saved value of a slider that meant something
    /// else is garbage, and the file drops a name nothing reads on its next save. Percent rows are
    /// stored as fractions (47% is 0.47f) and written as the slider shows them (47).
    /// </remarks>
    public static class Settings
    {
        /// <summary>
        /// The key's home in the game's settings system: registered with dontSave, so it is never
        /// written and there is no file to load. The key stays a SavedInputKey because Unified UI
        /// takes one; RealGlass.xml is where it is kept. NO DOT in this name: the game's
        /// PathUtils.AddExtension CHANGES an extension that is already there, and the name is read
        /// back from the path without one (Volumetric Weather's first "X.InMemory" became X.cgs).
        /// </summary>
        private const string InMemory = "RealGlassHotkey";

        // ---- the mod itself (options page) ---------------------------------------------------

        /// <summary>
        /// 0: the game's own language. Otherwise one of <see cref="Localization.LanguageCodes"/>,
        /// counted from 1 -- an append-only table, so a saved choice survives new languages.
        /// </summary>
        public static IntSetting Language { get; private set; }

        /// <summary>Opens and closes the in-game panel. F7 by default.</summary>
        public static SavedInputKey PanelKey { get; private set; }

        /// <summary>
        /// On (the default): while <see cref="PanelKey"/> is also the game's own "debug output"
        /// key (F7 out of the box), pressing it in a city opens only this mod's panel, not the
        /// game's debug panel too (<see cref="UI.DebugKeyPatch"/>).
        /// </summary>
        public static BoolSetting QuietDebugKey { get; private set; }

        /// <summary>Puts the panel's button in Unified UI's bar (when that is there), else on the HUD.</summary>
        public static BoolSetting UseUnifiedUI { get; private set; }

        /// <summary>
        /// Writes every periodic line to the log. Off for players: each Log call is an
        /// open-write-close plus a Unity stack trace, which is fine for a ten-minute test session
        /// and wrong for a four-hour one.
        /// </summary>
        public static BoolSetting DetailedLogging { get; private set; }

        /// <summary>
        /// Where the mod's own button sits when it is not in Unified UI: its top-left corner, in
        /// UI units from the top left of the screen. The player drags it there.
        /// </summary>
        public static FloatSetting HudButtonX { get; private set; }
        public static FloatSetting HudButtonY { get; private set; }

        // ---- F7 -> Glass ---------------------------------------------------------------------

        /// <summary>The master switch: off, nothing of the glass is drawn.</summary>
        public static BoolSetting GlassEnabled { get; private set; }

        /// <summary>
        /// Which real glass (<see cref="GlassPresets"/>): picking one writes GlassColor,
        /// GlassReflectivity and GlassClarity; moving any of those three by hand makes it
        /// <see cref="GlassPresets.Custom"/>.
        /// </summary>
        public static IntSetting GlassPreset { get; private set; }

        /// <summary>0..1: how much of the building's own painted glass colour is kept (1 = all of it).</summary>
        public static FloatSetting GlassOwnColour { get; private set; }

        /// <summary>The glass's tint (sRGB), where the building's own colour is not kept.</summary>
        public static ColorSetting GlassColor { get; private set; }

        /// <summary>0..0.6: reflectance looking straight at the glass (Fresnel's F0).</summary>
        public static FloatSetting GlassReflectivity { get; private set; }

        /// <summary>0..1: the most the glass reflects at a glancing angle.</summary>
        public static FloatSetting GlassGlancing { get; private set; }

        /// <summary>0..1: how much light passes through: what is seen of the rooms behind.</summary>
        public static FloatSetting GlassClarity { get; private set; }

        /// <summary>0..1: the glass over the game's own look (0 = the game's windows, 1 = ours).</summary>
        public static FloatSetting GlassStrength { get; private set; }

        /// <summary>0.4..0.95: how shiny a part of an asset must be to count as glass.</summary>
        public static FloatSetting GlassThreshold { get; private set; }

        /// <summary>Windows the asset lights up at night count as glass too.</summary>
        public static BoolSetting GlassNightWindows { get; private set; }

        /// <summary>A check: paints what counts as glass magenta instead of drawing it.</summary>
        public static BoolSetting GlassShowMask { get; private set; }

        // ---- F7 -> Reflections ---------------------------------------------------------------

        /// <summary>Screen-space reflections: the glass reflects what is on screen.</summary>
        public static BoolSetting SsrEnabled { get; private set; }

        /// <summary>0 Low, 1 Medium, 2 High, 3 Ultra: how finely the reflections are traced.</summary>
        public static IntSetting SsrQuality { get; private set; }

        /// <summary>0..3: multiplier on everything reflected.</summary>
        public static FloatSetting ReflectionBrightness { get; private set; }

        /// <summary>0: the live sky (a small cube of the game's own sky, kept up to date). 1: the game's static environment map.</summary>
        public static IntSetting SkyReflection { get; private set; }

        /// <summary>
        /// What a reflection shows below the horizon when nothing on screen is hit: 0 the screen
        /// mirrored at the edge it left by ("smart invert"), 1 the city's average colour, 2 the sky.
        /// </summary>
        public static IntSetting OffscreenFill { get; private set; }

        /// <summary>0..3: multiplier on the sun's glint off the panes.</summary>
        public static FloatSetting SunGlint { get; private set; }

        /// <summary>0..1: how sharp the reflections are (1 = a perfect mirror).</summary>
        public static FloatSetting GlassSmoothness { get; private set; }

        /// <summary>0..0.5: how far in from the screen's edges screen reflections fade out.</summary>
        public static FloatSetting SsrEdgeFade { get; private set; }

        // ---- F7 -> Panes ---------------------------------------------------------------------

        /// <summary>Metres: the width of one pane of glass.</summary>
        public static FloatSetting PaneWidth { get; private set; }

        /// <summary>0..3: panes bowing like cushions (insulating glass under pressure changes).</summary>
        public static FloatSetting PanePillow { get; private set; }

        /// <summary>0..3: each pane set at a slightly different angle.</summary>
        public static FloatSetting PaneTilt { get; private set; }

        /// <summary>0..3: the ripple toughened glass takes from the furnace rollers.</summary>
        public static FloatSetting RollerWave { get; private set; }

        /// <summary>Panes bulge on hot days and sink on cold ones (the game's temperature).</summary>
        public static BoolSetting PaneWeather { get; private set; }

        // ---- F7 -> Interiors -----------------------------------------------------------------

        /// <summary>Rooms behind the glass (interior mapping).</summary>
        public static BoolSetting InteriorEnabled { get; private set; }

        /// <summary>Metres from one floor to the next.</summary>
        public static FloatSetting FloorHeight { get; private set; }

        /// <summary>Metres: the width of a room along the facade.</summary>
        public static FloatSetting RoomWidth { get; private set; }

        /// <summary>Metres: how deep a room goes behind the glass.</summary>
        public static FloatSetting RoomDepth { get; private set; }

        /// <summary>0..3: how bright the rooms are by day.</summary>
        public static FloatSetting InteriorBrightness { get; private set; }

        /// <summary>0..1: the share of windows with their blinds down.</summary>
        public static FloatSetting Blinds { get; private set; }

        /// <summary>0..1: how furnished the rooms are.</summary>
        public static FloatSetting Furniture { get; private set; }

        /// <summary>0..3: how bright the lit rooms are at night.</summary>
        public static FloatSetting NightRooms { get; private set; }

        /// <summary>0..1: the rooms' light at night, from cool office light (0) to warm home light (1).</summary>
        public static FloatSetting NightWarmth { get; private set; }

        // ---- F7 -> Rendering -----------------------------------------------------------------

        /// <summary>0: near buildings only. 1: near and middle distance. 2: the whole city.</summary>
        public static IntSetting GlassDistance { get; private set; }

        /// <summary>0: 64 px, 1: 128 px, 2: 256 px a face of the live sky cube. See <see cref="SkyCubePixels"/>.</summary>
        public static IntSetting SkyCubeSize { get; private set; }

        /// <summary>1..6: faces of the live sky cube rendered a frame (6 = the whole cube every frame).</summary>
        public static IntSetting SkyCubeRate { get; private set; }

        /// <summary>
        /// Every default, in one place. A new one reaches only new installs and a Reset: a player's
        /// file already lists every row.
        /// </summary>
        public static class Defaults
        {
            // ---- housekeeping ----

            /// <summary>The game's own language.</summary>
            public const int Language = 0;
            public const bool QuietDebugKey = true;
            public const bool UseUnifiedUI = true;
            public const bool DetailedLogging = false;

            /// <summary>Under the sister mods' buttons: Volumetric Weather (10, 60), Volumetric Explosions (10, 100), RealFlag (10, 140).</summary>
            public const float HudButtonX = 10f;
            public const float HudButtonY = 180f;

            // ---- Glass ----
            public const bool GlassEnabled = true;
            public const int GlassPreset = GlassPresets.ReflectiveBlue;
            public const float GlassOwnColour = 0.40f;

            /// <summary>The Reflective blue preset's colour (#2E6A80): a static, because a Color32 cannot be a const.</summary>
            public static readonly Color32 GlassColor = new Color32(0x2E, 0x6A, 0x80, 255);
            public const float GlassReflectivity = 0.20f;
            public const float GlassGlancing = 0.70f;
            public const float GlassClarity = 0.24f;
            public const float GlassStrength = 1f;
            public const float GlassThreshold = 0.70f;
            public const bool GlassNightWindows = true;
            public const bool GlassShowMask = false;

            // ---- Reflections ----
            public const bool SsrEnabled = true;
            public const int SsrQuality = 2;          // High
            public const float ReflectionBrightness = 1f;
            public const int SkyReflection = 0;       // the live sky
            public const int OffscreenFill = 0;       // the mirrored screen
            public const float SunGlint = 1f;
            public const float GlassSmoothness = 0.92f;
            public const float SsrEdgeFade = 0.12f;

            // ---- Panes ----
            public const float PaneWidth = 1.5f;
            public const float PanePillow = 1f;
            public const float PaneTilt = 0.6f;
            public const float RollerWave = 0.6f;
            public const bool PaneWeather = true;

            // ---- Interiors ----
            public const bool InteriorEnabled = true;
            public const float FloorHeight = 3.8f;
            public const float RoomWidth = 12f;   // open-plan office floors, not cells
            public const float RoomDepth = 12f;
            public const float InteriorBrightness = 1f;
            public const float Blinds = 0.35f;
            public const float Furniture = 0.6f;
            public const float NightRooms = 1f;
            public const float NightWarmth = 0.45f;

            // ---- Rendering ----
            public const int GlassDistance = 2;       // the whole city
            public const int SkyCubeSize = 1;         // 128 px
            public const int SkyCubeRate = 1;
        }

        private static bool _initialised;

        /// <summary>True once every setting exists (the file is read straight after).</summary>
        public static bool IsInitialised
        {
            get { return _initialised; }
        }

        /// <summary>
        /// Constructs every setting with its default, then reads RealGlass.xml over them. Called
        /// from Mod.OnEnabled, and defensively from everything that needs a setting before that
        /// could have run.
        /// </summary>
        public static void Init()
        {
            if (_initialised)
                return;

            try
            {
                // Looked up first because a reloaded mod finds it already there, and adding a name
                // twice throws.
                if (GameSettings.FindSettingsFileByName(InMemory) == null)
                    GameSettings.AddSettingsFile(new SettingsFile { fileName = InMemory, dontSave = true });

                // In catalog order (UI/SettingsCatalog.cs), which is also the file's order.
                GlassEnabled = new BoolSetting("GlassEnabled", Defaults.GlassEnabled);
                GlassPreset = new IntSetting("GlassPreset", Defaults.GlassPreset);
                GlassOwnColour = new FloatSetting("GlassOwnColour", Defaults.GlassOwnColour);
                GlassColor = new ColorSetting("GlassColor", Defaults.GlassColor);
                GlassReflectivity = new FloatSetting("GlassReflectivity", Defaults.GlassReflectivity);
                GlassGlancing = new FloatSetting("GlassGlancing", Defaults.GlassGlancing);
                GlassClarity = new FloatSetting("GlassClarity", Defaults.GlassClarity);
                GlassStrength = new FloatSetting("GlassStrength", Defaults.GlassStrength);
                GlassThreshold = new FloatSetting("GlassThreshold", Defaults.GlassThreshold);
                GlassNightWindows = new BoolSetting("GlassNightWindows", Defaults.GlassNightWindows);
                GlassShowMask = new BoolSetting("GlassShowMask", Defaults.GlassShowMask);

                SsrEnabled = new BoolSetting("SsrEnabled", Defaults.SsrEnabled);
                SsrQuality = new IntSetting("SsrQuality", Defaults.SsrQuality);
                ReflectionBrightness = new FloatSetting("ReflectionBrightness", Defaults.ReflectionBrightness);
                SkyReflection = new IntSetting("SkyReflection", Defaults.SkyReflection);
                OffscreenFill = new IntSetting("OffscreenFill", Defaults.OffscreenFill);
                SunGlint = new FloatSetting("SunGlint", Defaults.SunGlint);
                GlassSmoothness = new FloatSetting("GlassSmoothness", Defaults.GlassSmoothness);
                SsrEdgeFade = new FloatSetting("SsrEdgeFade", Defaults.SsrEdgeFade);

                PaneWidth = new FloatSetting("PaneWidth", Defaults.PaneWidth);
                PanePillow = new FloatSetting("PanePillow", Defaults.PanePillow);
                PaneTilt = new FloatSetting("PaneTilt", Defaults.PaneTilt);
                RollerWave = new FloatSetting("RollerWave", Defaults.RollerWave);
                PaneWeather = new BoolSetting("PaneWeather", Defaults.PaneWeather);

                InteriorEnabled = new BoolSetting("InteriorEnabled", Defaults.InteriorEnabled);
                FloorHeight = new FloatSetting("FloorHeight", Defaults.FloorHeight);
                RoomWidth = new FloatSetting("RoomWidth", Defaults.RoomWidth);
                RoomDepth = new FloatSetting("RoomDepth", Defaults.RoomDepth);
                InteriorBrightness = new FloatSetting("InteriorBrightness", Defaults.InteriorBrightness);
                Blinds = new FloatSetting("Blinds", Defaults.Blinds);
                Furniture = new FloatSetting("Furniture", Defaults.Furniture);
                NightRooms = new FloatSetting("NightRooms", Defaults.NightRooms);
                NightWarmth = new FloatSetting("NightWarmth", Defaults.NightWarmth);

                GlassDistance = new IntSetting("GlassDistance", Defaults.GlassDistance);
                SkyCubeSize = new IntSetting("SkyCubeSize", Defaults.SkyCubeSize);
                SkyCubeRate = new IntSetting("SkyCubeRate", Defaults.SkyCubeRate);

                Language = new IntSetting("Language", Defaults.Language);
                PanelKey = new SavedInputKey("PanelKey", InMemory, DefaultPanelKey, false);
                QuietDebugKey = new BoolSetting("QuietDebugKey", Defaults.QuietDebugKey);
                UseUnifiedUI = new BoolSetting("UseUnifiedUI", Defaults.UseUnifiedUI);
                DetailedLogging = new BoolSetting("DetailedLogging", Defaults.DetailedLogging);

                HudButtonX = new FloatSetting("HudButtonX", Defaults.HudButtonX);
                HudButtonY = new FloatSetting("HudButtonY", Defaults.HudButtonY);

                // Before the file is read: reading it walks the catalog, and building the catalog
                // calls Init.
                _initialised = true;
            }
            catch (Exception e)
            {
                Debug.LogError("[RealGlass] Could not initialise settings.");
                Debug.LogException(e);
                return;
            }

            SettingsXml.Load();
        }

        /// <summary>F7 (asked for). A property because SavedInputKey.Encode is a method, so it cannot be a const.</summary>
        public static int DefaultPanelKey
        {
            get { return SavedInputKey.Encode(KeyCode.F7, false, false, false); }
        }

        /// <summary>The live sky cube's face size in pixels, from <see cref="SkyCubeSize"/>: 64, 128 or 256.</summary>
        public static int SkyCubePixels
        {
            get { return CubePixels(SkyCubeSize != null ? SkyCubeSize.value : Defaults.SkyCubeSize); }
        }

        /// <summary>A SkyCubeSize value as pixels. Pure, for the readout and the tests.</summary>
        public static int CubePixels(int sizeValue)
        {
            switch (sizeValue)
            {
                case 0: return 64;
                case 2: return 256;
                default: return 128;
            }
        }
    }
}
