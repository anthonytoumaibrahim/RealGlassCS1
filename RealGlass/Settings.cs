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

        /// <summary>Opens and closes the in-game panel. F8 by default.</summary>
        public static SavedInputKey PanelKey { get; private set; }

        /// <summary>
        /// Turns the building picker on and off (PerAsset.AssetPicker): click a building, and the
        /// building window gives its asset glass of its own. Alt+G by default.
        /// </summary>
        public static SavedInputKey PickerKey { get; private set; }

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

        /// <summary>Hidden: the foam's diagnostic view (0 none, 1 its sources, 2 / 3 where it draws: WaterFoam.shader).</summary>
        public static FloatSetting WaterFoamView { get; private set; }
        public static FloatSetting HudButtonY { get; private set; }

        // ---- F8 -> Glass ---------------------------------------------------------------------

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

        /// <summary>
        /// 0..1: how much of the lines an asset painted on its glass stay -- floor bands, mullions,
        /// frames: where its colour map stands out from the storey round it. A full-glass tower
        /// (its whole facade marked shiny) keeps its floors.
        /// </summary>
        public static FloatSetting GlassPaintedDetail { get; private set; }

        /// <summary>The glass's tint (sRGB), where the building's own colour is not kept.</summary>
        public static ColorSetting GlassColor { get; private set; }

        /// <summary>0..0.6: reflectance looking straight at the glass (Fresnel's F0).</summary>
        public static FloatSetting GlassReflectivity { get; private set; }

        /// <summary>0..1: the most the glass reflects at a glancing angle.</summary>
        public static FloatSetting GlassGlancing { get; private set; }

        /// <summary>
        /// 0..1: how far a coated pane's reflection loses its colour edge-on. Physics says all the
        /// way (1): the Fresnel rise is the same for every colour. 0 keeps the tint at every angle.
        /// </summary>
        public static FloatSetting GlassEdgeSilver { get; private set; }

        /// <summary>0..1: how much light passes through: how dark the glass looks by day.</summary>
        public static FloatSetting GlassClarity { get; private set; }

        /// <summary>0..1: the glass over the game's own look (0 = the game's windows, 1 = ours).</summary>
        public static FloatSetting GlassStrength { get; private set; }

        /// <summary>0.4..0.95: how shiny a part of an asset must be to count as glass.</summary>
        public static FloatSetting GlassThreshold { get; private set; }

        /// <summary>Windows the asset lights up at night count as glass too.</summary>
        public static BoolSetting GlassNightWindows { get; private set; }

        /// <summary>A check: paints what counts as glass magenta instead of drawing it.</summary>
        public static BoolSetting GlassShowMask { get; private set; }

        // ---- F8 -> Reflections ---------------------------------------------------------------

        /// <summary>Screen-space reflections: the glass reflects what is on screen.</summary>
        public static BoolSetting SsrEnabled { get; private set; }

        /// <summary>
        /// The glass reflects the FINISHED picture (last frame's, after the fog, the clouds, the
        /// lamps' glow and the other glass), put back where it was: not the copy taken before them.
        /// </summary>
        public static BoolSetting ReflectFinished { get; private set; }

        /// <summary>
        /// The glass mirrors the buildings around it where the screen cannot show them: a map of
        /// the city's roof heights round the view, walked by every reflected ray that misses the
        /// screen (Glass/CityMap.cs) -- the towers across a street instead of open sky.
        /// </summary>
        public static BoolSetting ReflectNeighbours { get; private set; }

        /// <summary>0 Low, 1 Medium, 2 High, 3 Ultra: how finely the reflections are traced.</summary>
        public static IntSetting SsrQuality { get; private set; }

        /// <summary>0..3: multiplier on everything reflected.</summary>
        public static FloatSetting ReflectionBrightness { get; private set; }

        /// <summary>0: the live sky (a small cube of the game's own sky, kept up to date). 1: the game's static environment map.</summary>
        public static IntSetting SkyReflection { get; private set; }

        /// <summary>The live sky carries the clouds in the sky (Volumetric Weather's, when it draws them).</summary>
        public static BoolSetting SkyClouds { get; private set; }

        /// <summary>
        /// What a reflection shows below the horizon when nothing on screen is hit: 0 the screen
        /// mirrored at the edge it left by ("smart invert"), 1 the city's average colour, 2 the sky.
        /// </summary>
        public static IntSetting OffscreenFill { get; private set; }

        /// <summary>0..3: multiplier on the sun's glint off the panes.</summary>
        public static FloatSetting SunGlint { get; private set; }

        /// <summary>
        /// Specular anti-aliasing: where the panes' bend changes within a pixel, the glint and the
        /// reflection widen to match instead of flickering (Kaplanyan et al. 2016).
        /// </summary>
        public static BoolSetting SteadyGlints { get; private set; }

        /// <summary>0..1: how sharp the reflections are (1 = a perfect mirror).</summary>
        public static FloatSetting GlassSmoothness { get; private set; }

        /// <summary>0..0.5: how far in from the screen's edges screen reflections fade out.</summary>
        public static FloatSetting SsrEdgeFade { get; private set; }

        // ---- F8 -> Panes ---------------------------------------------------------------------

        /// <summary>Metres: the width of one pane of glass.</summary>
        public static FloatSetting PaneWidth { get; private set; }

        /// <summary>
        /// Metres from one floor to the next: a pane's height where the building painted no window
        /// of its own, and the floors of the buildings mirrored where the screen cannot show them.
        /// </summary>
        public static FloatSetting FloorHeight { get; private set; }

        /// <summary>0..3: panes bowing like cushions (insulating glass under pressure changes).</summary>
        public static FloatSetting PanePillow { get; private set; }

        /// <summary>0..3: each pane set at a slightly different angle.</summary>
        public static FloatSetting PaneTilt { get; private set; }

        /// <summary>0..3: the ripple toughened glass takes from the furnace rollers.</summary>
        public static FloatSetting RollerWave { get; private set; }

        /// <summary>Panes bulge on hot days and sink on cold ones (the game's temperature).</summary>
        public static BoolSetting PaneWeather { get; private set; }

        /// <summary>
        /// Close up, each pane is a window the asset painted (found between its painted mullions
        /// and transoms), split into panes about the pane width wide: not a grid of its own.
        /// </summary>
        public static BoolSetting PanesFollowWindows { get; private set; }

        // ---- F8 -> Interiors (Glass/Interiors.cs; off by default, the author 2026-10-07) --------
        // Off: behind the glass by day a dark inside (GlassUniforms), by night the game's own window
        // (round 7). On: rooms behind every building's windows, OURS at night.

        /// <summary>Rooms behind the windows of every building (an asset can say otherwise, Alt+G).</summary>
        public static BoolSetting InteriorsEnabled { get; private set; }

        /// <summary>The rooms by day too; off, they show from dusk (the author: "by default interiors only show up at night").</summary>
        public static BoolSetting InteriorsByDay { get; private set; }

        /// <summary>0.05..0.9: the share of rooms lit at the busiest hour.</summary>
        public static FloatSetting InteriorsLit { get; private set; }

        /// <summary>The share follows the hour: homes empty out after midnight, offices keep a few floors.</summary>
        public static BoolSetting InteriorsLateNight { get; private set; }

        /// <summary>0..3: how often a room's light changes (1 = about every Interiors.HoursPerChange game hours; 0 never).</summary>
        public static FloatSetting InteriorsSwitching { get; private set; }

        /// <summary>0.25..3: a lit room's light (1 = about the game's own lit window).</summary>
        public static FloatSetting InteriorsBrightness { get; private set; }

        /// <summary>0..0.3: the share of rooms lit by coloured lights (LED, RGB).</summary>
        public static FloatSetting InteriorsColoured { get; private set; }

        /// <summary>0..1: the share of windows with curtains or blinds.</summary>
        public static FloatSetting InteriorsCurtains { get; private set; }

        // ---- Options page -> Rendering (the F8 panel's tab until 2026-10-04) ---------------

        /// <summary>0: near buildings only. 1: near and middle distance. 2: the whole city.</summary>
        public static IntSetting GlassDistance { get; private set; }

        /// <summary>0: 64 px, 1: 128 px, 2: 256 px a face of the live sky cube. See <see cref="SkyCubePixels"/>.</summary>
        public static IntSetting SkyCubeSize { get; private set; }

        /// <summary>1..6: faces of the live sky cube rendered a frame (6 = the whole cube every frame).</summary>
        public static IntSetting SkyCubeRate { get; private set; }

        // ---- F8 -> Water (Water/WaterController.cs; off by default, the author 2026-10-03) ------

        /// <summary>The Water tab's switch: off, the map's water is the game's and the theme's.</summary>
        public static BoolSetting WaterEnabled { get; private set; }

        /// <summary>0.25..4: the water's own colour x this (1 = the map theme's).</summary>
        public static FloatSetting WaterBrightness { get; private set; }

        /// <summary>0..1: brighter blue clean water and stronger long-wave detail by day, fading out at night.</summary>
        public static FloatSetting WaterDayLook { get; private set; }

        /// <summary>The water reflects the theme's sky with real water's reflectance, not the same share at every angle.</summary>
        public static BoolSetting WaterRealSky { get; private set; }

        /// <summary>0..3: that reflection x this (1 = real water).</summary>
        public static FloatSetting WaterSky { get; private set; }

        /// <summary>The city on screen is reflected in the water (screen-space), day and night.</summary>
        public static BoolSetting WaterCity { get; private set; }

        /// <summary>0..3: the city's reflection by day (1 = real water).</summary>
        public static FloatSetting WaterCityDay { get; private set; }

        /// <summary>0..3: the city's reflection by night (1 = real water).</summary>
        public static FloatSetting WaterCityNight { get; private set; }

        /// <summary>0..3: the ripples bend the city's reflection (1 = today's look: WaterOptics.CityRippleGain).</summary>
        public static FloatSetting WaterCityWaves { get; private set; }

        /// <summary>Our moving ripples on the water in place of the game's frozen ones (Water/WaterRipples.cs).</summary>
        public static BoolSetting WaterRipples { get; private set; }

        /// <summary>0..3: the ripples' roughness (1 = the game's own ripple texture's, measured).</summary>
        public static FloatSetting WaterRippleStrength { get; private set; }

        /// <summary>0.5..3: the ripples' lengths (1 = 16 m down to 0.5 m).</summary>
        public static FloatSetting WaterRippleSize { get; private set; }

        /// <summary>0.05..1: the longest wave's share of its length (1 = 16 m at 100% ripple size).</summary>
        public static FloatSetting WaterWaveSize { get; private set; }

        /// <summary>Foam on the water: shores, rapids, wave fronts (Water/WaterFoam.cs).</summary>
        public static BoolSetting WaterFoam { get; private set; }

        /// <summary>0..3: all the foam x this (1 = the model's: Water/FoamOptics.cs).</summary>
        public static FloatSetting WaterFoamAmount { get; private set; }

        /// <summary>0..2: the foam along the sea's shores x this.</summary>
        public static FloatSetting WaterFoamShore { get; private set; }

        /// <summary>0..2: the foam on fast water (rapids, weirs, outflows) x this.</summary>
        public static FloatSetting WaterFoamRapids { get; private set; }

        /// <summary>0..2: the foam on rising wave fronts (tsunamis, floods, dam breaks) x this.</summary>
        public static FloatSetting WaterFoamFronts { get; private set; }

        /// <summary>0..0.1: the waves smear the city's reflection up and down (1 would be a light breeze's).</summary>
        public static FloatSetting WaterCityStreaks { get; private set; }

        /// <summary>1..3: the city's reflection drawn this many times as long as the city is tall (1 = a mirror).</summary>
        public static FloatSetting WaterCityLength { get; private set; }

        /// <summary>
        /// Every default, in one place. A new one reaches only new installs and a Reset: a player's
        /// file already lists every row.
        /// </summary>
        public static class Defaults
        {
            // ---- housekeeping ----

            /// <summary>The game's own language.</summary>
            public const int Language = 0;
            public const bool UseUnifiedUI = true;
            public const bool DetailedLogging = false;

            /// <summary>Under the sister mods' buttons: Volumetric Weather (10, 60), Volumetric Explosions (10, 100), RealFlag (10, 140).</summary>
            public const float HudButtonX = 10f;
            public const float HudButtonY = 180f;

            public const float WaterFoamView = 0f;

            // ---- Glass ----
            public const bool GlassEnabled = true;

            /// <summary>
            /// Reflective blue (the author's pick, 2026-10-04; Silver reflective from 2026-09-29).
            /// The colour, reflectivity and clarity below ARE the preset's (tools/test-settings.ps1).
            /// </summary>
            public const int GlassPreset = GlassPresets.ReflectiveBlue;
            public const float GlassOwnColour = 0.30f;

            /// <summary>All of them: the asset's own floors are what a player expects to see (2026-10-01).</summary>
            public const float GlassPaintedDetail = 1f;

            /// <summary>The Reflective blue preset's colour (#2E6A80): a static, because a Color32 cannot be a const.</summary>
            public static readonly Color32 GlassColor = new Color32(0x2E, 0x6A, 0x80, 255);
            public const float GlassReflectivity = 0.20f;
            public const float GlassGlancing = 0.80f;
            public const float GlassEdgeSilver = 1f;
            public const float GlassClarity = 0.24f;
            /// <summary>85%: a little of the game's own windows shows through (the author's, 2026-10-07).</summary>
            public const float GlassStrength = 0.85f;
            public const float GlassThreshold = 0.70f;
            public const bool GlassNightWindows = true;
            public const bool GlassShowMask = false;

            // ---- Reflections ----
            public const bool SsrEnabled = true;
            public const bool ReflectFinished = true;
            public const bool ReflectNeighbours = true;
            public const int SsrQuality = 3;          // Ultra
            public const float ReflectionBrightness = 0.55f;
            public const int SkyReflection = 0;       // the live sky, with the clouds
            public const bool SkyClouds = true;
            public const int OffscreenFill = 0;       // the mirrored screen
            public const float SunGlint = 3f;
            public const bool SteadyGlints = true;
            public const float GlassSmoothness = 1f;
            public const float SsrEdgeFade = 0.50f;

            // ---- Panes ---- (the author's: every bend at half its slider)
            public const float PaneWidth = 1.4f;
            public const float FloorHeight = 3.8f;    // an office storey
            public const float PanePillow = 1.5f;
            public const float PaneTilt = 1.5f;
            public const float RollerWave = 1.5f;
            public const bool PaneWeather = true;
            public const bool PanesFollowWindows = true;

            // ---- Interiors ---- (off: "by default it's off for now", the author 2026-10-07)
            public const bool InteriorsEnabled = false;
            public const bool InteriorsByDay = false;

            /// <summary>Under half at the busiest hour: "you'll likely never find ALL interiors lit up".</summary>
            public const float InteriorsLit = 0.45f;
            public const bool InteriorsLateNight = true;
            public const float InteriorsSwitching = 1f;
            public const float InteriorsBrightness = 1f;

            /// <summary>"orange/white/led/rgb even but less common": a few rooms in twenty-five.</summary>
            public const float InteriorsColoured = 0.04f;
            public const float InteriorsCurtains = 0.70f;

            // ---- Rendering ---- (the author's: the largest, livest sky)
            public const int GlassDistance = 2;       // the whole city
            public const int SkyCubeSize = 2;         // 256 px
            public const int SkyCubeRate = 6;

            // ---- Water ---- (off; inside it, the author's own settings, 2026-10-07)
            public const bool WaterEnabled = false;
            public const float WaterBrightness = 1f;
            public const float WaterDayLook = 1f;
            public const bool WaterRealSky = true;
            public const float WaterSky = 0.80f;
            public const bool WaterCity = true;
            public const float WaterCityDay = 1f;

            /// <summary>300%: the lit city carries the water at night (the author's, 2026-10-07).</summary>
            public const float WaterCityNight = 3f;

            /// <summary>The waves bend the city's reflection; replaced the blur (the author, 2026-10-05: "the water doesn't get blurry").</summary>
            public const float WaterCityWaves = 1f;

            /// <summary>None: the waves bend the reflection, no smear (the author's, 2026-10-07; 1% from 2026-10-04).</summary>
            public const float WaterCityStreaks = 0f;

            /// <summary>130%: the city's reflection a little longer than a mirror's (the author's, 2026-10-07).</summary>
            public const float WaterCityLength = 1.3f;

            /// <summary>Moving ripples on (the author, 2026-10-05: step 1 of the water plan, "a proof of concept").</summary>
            public const bool WaterRipples = true;

            /// <summary>As rough as the game's own ripples.</summary>
            public const float WaterRippleStrength = 1f;

            /// <summary>16 m down to 0.5 m.</summary>
            public const float WaterRippleSize = 1f;

            /// <summary>The longest waves at full length.</summary>
            public const float WaterWaveSize = 1f;

            /// <summary>Foam on (the author, 2026-10-06: "One thing I'd actually like is the foam").</summary>
            public const bool WaterFoam = true;

            /// <summary>30% of the model's foam, hardly any on fast water (the author's, 2026-10-07).</summary>
            public const float WaterFoamAmount = 0.30f;
            public const float WaterFoamShore = 1f;
            public const float WaterFoamRapids = 0.05f;
            public const float WaterFoamFronts = 1f;
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
                GlassPaintedDetail = new FloatSetting("GlassPaintedDetail", Defaults.GlassPaintedDetail);
                GlassColor = new ColorSetting("GlassColor", Defaults.GlassColor);
                GlassReflectivity = new FloatSetting("GlassReflectivity", Defaults.GlassReflectivity);
                GlassGlancing = new FloatSetting("GlassGlancing", Defaults.GlassGlancing);
                GlassEdgeSilver = new FloatSetting("GlassEdgeSilver", Defaults.GlassEdgeSilver);
                GlassClarity = new FloatSetting("GlassClarity", Defaults.GlassClarity);
                GlassStrength = new FloatSetting("GlassStrength", Defaults.GlassStrength);
                GlassThreshold = new FloatSetting("GlassThreshold", Defaults.GlassThreshold);
                GlassNightWindows = new BoolSetting("GlassNightWindows", Defaults.GlassNightWindows);
                GlassShowMask = new BoolSetting("GlassShowMask", Defaults.GlassShowMask);


                SsrEnabled = new BoolSetting("SsrEnabled", Defaults.SsrEnabled);
                ReflectFinished = new BoolSetting("ReflectFinished", Defaults.ReflectFinished);
                ReflectNeighbours = new BoolSetting("ReflectNeighbours", Defaults.ReflectNeighbours);
                SsrQuality = new IntSetting("SsrQuality", Defaults.SsrQuality);
                ReflectionBrightness = new FloatSetting("ReflectionBrightness", Defaults.ReflectionBrightness);
                SkyReflection = new IntSetting("SkyReflection", Defaults.SkyReflection);
                SkyClouds = new BoolSetting("SkyClouds", Defaults.SkyClouds);
                OffscreenFill = new IntSetting("OffscreenFill", Defaults.OffscreenFill);
                SunGlint = new FloatSetting("SunGlint", Defaults.SunGlint);
                SteadyGlints = new BoolSetting("SteadyGlints", Defaults.SteadyGlints);
                GlassSmoothness = new FloatSetting("GlassSmoothness", Defaults.GlassSmoothness);
                SsrEdgeFade = new FloatSetting("SsrEdgeFade", Defaults.SsrEdgeFade);

                PaneWidth = new FloatSetting("PaneWidth", Defaults.PaneWidth);
                FloorHeight = new FloatSetting("FloorHeight", Defaults.FloorHeight);
                PanePillow = new FloatSetting("PanePillow", Defaults.PanePillow);
                PaneTilt = new FloatSetting("PaneTilt", Defaults.PaneTilt);
                RollerWave = new FloatSetting("RollerWave", Defaults.RollerWave);
                PaneWeather = new BoolSetting("PaneWeather", Defaults.PaneWeather);
                PanesFollowWindows = new BoolSetting("PanesFollowWindows", Defaults.PanesFollowWindows);

                InteriorsEnabled = new BoolSetting("InteriorsEnabled", Defaults.InteriorsEnabled);
                InteriorsByDay = new BoolSetting("InteriorsByDay", Defaults.InteriorsByDay);
                InteriorsLit = new FloatSetting("InteriorsLit", Defaults.InteriorsLit);
                InteriorsLateNight = new BoolSetting("InteriorsLateNight", Defaults.InteriorsLateNight);
                InteriorsSwitching = new FloatSetting("InteriorsSwitching", Defaults.InteriorsSwitching);
                InteriorsBrightness = new FloatSetting("InteriorsBrightness", Defaults.InteriorsBrightness);
                InteriorsColoured = new FloatSetting("InteriorsColoured", Defaults.InteriorsColoured);
                InteriorsCurtains = new FloatSetting("InteriorsCurtains", Defaults.InteriorsCurtains);

                WaterEnabled = new BoolSetting("WaterEnabled", Defaults.WaterEnabled);
                WaterBrightness = new FloatSetting("WaterBrightness", Defaults.WaterBrightness);
                WaterDayLook = new FloatSetting("WaterDayLook", Defaults.WaterDayLook);
                WaterRipples = new BoolSetting("WaterRipples", Defaults.WaterRipples);
                WaterRippleStrength = new FloatSetting("WaterRippleStrength", Defaults.WaterRippleStrength);
                WaterRippleSize = new FloatSetting("WaterRippleSize", Defaults.WaterRippleSize);
                WaterWaveSize = new FloatSetting("WaterWaveSize", Defaults.WaterWaveSize);
                WaterFoam = new BoolSetting("WaterFoam", Defaults.WaterFoam);
                WaterFoamAmount = new FloatSetting("WaterFoamAmount", Defaults.WaterFoamAmount);
                WaterFoamShore = new FloatSetting("WaterFoamShore", Defaults.WaterFoamShore);
                WaterFoamRapids = new FloatSetting("WaterFoamRapids", Defaults.WaterFoamRapids);
                WaterFoamFronts = new FloatSetting("WaterFoamFronts", Defaults.WaterFoamFronts);
                WaterRealSky = new BoolSetting("WaterRealSky", Defaults.WaterRealSky);
                WaterSky = new FloatSetting("WaterSky", Defaults.WaterSky);
                WaterCity = new BoolSetting("WaterCity", Defaults.WaterCity);
                WaterCityDay = new FloatSetting("WaterCityDay", Defaults.WaterCityDay);
                WaterCityNight = new FloatSetting("WaterCityNight", Defaults.WaterCityNight);
                WaterCityWaves = new FloatSetting("WaterCityWaves", Defaults.WaterCityWaves);
                WaterCityStreaks = new FloatSetting("WaterCityStreaks", Defaults.WaterCityStreaks);
                WaterCityLength = new FloatSetting("WaterCityLength", Defaults.WaterCityLength);

                Language = new IntSetting("Language", Defaults.Language);
                PanelKey = new SavedInputKey("PanelKey", InMemory, DefaultPanelKey, false);
                PickerKey = new SavedInputKey("PickerKey", InMemory, DefaultPickerKey, false);
                UseUnifiedUI = new BoolSetting("UseUnifiedUI", Defaults.UseUnifiedUI);

                GlassDistance = new IntSetting("GlassDistance", Defaults.GlassDistance);
                SkyCubeSize = new IntSetting("SkyCubeSize", Defaults.SkyCubeSize);
                SkyCubeRate = new IntSetting("SkyCubeRate", Defaults.SkyCubeRate);

                DetailedLogging = new BoolSetting("DetailedLogging", Defaults.DetailedLogging);

                HudButtonX = new FloatSetting("HudButtonX", Defaults.HudButtonX);
                HudButtonY = new FloatSetting("HudButtonY", Defaults.HudButtonY);
                WaterFoamView = new FloatSetting("WaterFoamView", Defaults.WaterFoamView);

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

            // Each building asset's own glass (RealGlassAssets.xml): after the settings, whose
            // global values every asset falls back on.
            PerAsset.AssetFile.Load();
        }

        /// <summary>
        /// F8 (asked for, 2026-09-29): F7 is the game's own debug-output key
        /// (DefaultSettings.debugOuput), and the game binds no other F key but F1 (quick save) and
        /// F12 (screenshots); the sister mods have F3, F4 and F6. A property because
        /// SavedInputKey.Encode is a method, so it cannot be a const.
        /// </summary>
        public static int DefaultPanelKey
        {
            get { return SavedInputKey.Encode(KeyCode.F8, false, false, false); }
        }

        /// <summary>
        /// Alt+G (asked for, 2026-10-01, as BOB's Alt+B): the game binds nothing to it (no default of
        /// DefaultSettings uses Alt at all), and of the 141 mod assemblies installed here only Network
        /// Anarchy does -- its editor grid, while its road tools are on -- so the key is not taken while
        /// a road tool is (ModController). Ctrl+Alt+G and Alt+Shift+G are bound by no one.
        /// </summary>
        public static int DefaultPickerKey
        {
            get { return SavedInputKey.Encode(KeyCode.G, false, false, true); }
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
