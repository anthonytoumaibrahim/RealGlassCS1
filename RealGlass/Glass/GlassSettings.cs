using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>The player's settings as the glass wants them (GlassParams), read once a frame.</summary>
    public static class GlassSettings
    {
        public static GlassParams Read()
        {
            GlassParams p = GlassParams.Default();
            if (!Settings.IsInitialised)
                return p;

            p.Enabled = Settings.GlassEnabled.value;
            Color32 c = Settings.GlassColor.value;
            p.TintR = c.r / 255f;
            p.TintG = c.g / 255f;
            p.TintB = c.b / 255f;
            p.OwnColour = Settings.GlassOwnColour.value;
            p.PaintedDetail = Settings.GlassPaintedDetail.value;
            p.Reflectivity = Settings.GlassReflectivity.value;
            p.Glancing = Settings.GlassGlancing.value;
            p.Clarity = Settings.GlassClarity.value;
            p.Strength = Settings.GlassStrength.value;
            p.Threshold = Settings.GlassThreshold.value;
            p.NightWindows = Settings.GlassNightWindows.value;
            p.ShowMask = Settings.GlassShowMask.value;
            p.EdgeSilver = Settings.GlassEdgeSilver.value;

            p.Trace = Settings.SsrEnabled.value;
            p.Finished = Settings.ReflectFinished.value;
            p.Neighbours = Settings.ReflectNeighbours.value;
            p.Quality = Settings.SsrQuality.value;
            p.ReflectionBrightness = Settings.ReflectionBrightness.value;
            p.SkySource = Settings.SkyReflection.value;
            p.SkyClouds = Settings.SkyClouds.value;
            p.OffscreenFill = Settings.OffscreenFill.value;
            p.SunGlint = Settings.SunGlint.value;
            p.SteadyGlints = Settings.SteadyGlints.value;
            p.Smoothness = Settings.GlassSmoothness.value;
            p.EdgeFade = Settings.SsrEdgeFade.value;

            p.PaneWidth = Settings.PaneWidth.value;
            p.FloorHeight = Settings.FloorHeight.value;
            p.Pillow = Settings.PanePillow.value;
            p.Tilt = Settings.PaneTilt.value;
            p.RollerWave = Settings.RollerWave.value;
            p.PaneWeather = Settings.PaneWeather.value;
            p.PanesFollowWindows = Settings.PanesFollowWindows.value;

            p.Interiors = Settings.InteriorsEnabled.value;
            p.InteriorsByDay = Settings.InteriorsByDay.value;
            p.InteriorsDepth = Settings.InteriorsDepth.value;
            p.InteriorsMatchLods = Settings.InteriorsMatchLods.value;
            p.InteriorsLit = Settings.InteriorsLit.value;
            p.InteriorsLateNight = Settings.InteriorsLateNight.value;
            p.InteriorsSwitching = Settings.InteriorsSwitching.value;
            p.InteriorsBrightness = Settings.InteriorsBrightness.value;
            p.InteriorsColoured = Settings.InteriorsColoured.value;
            p.InteriorsCurtains = Settings.InteriorsCurtains.value;
            return p;
        }
    }
}
