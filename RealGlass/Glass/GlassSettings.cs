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
            p.Reflectivity = Settings.GlassReflectivity.value;
            p.Glancing = Settings.GlassGlancing.value;
            p.Clarity = Settings.GlassClarity.value;
            p.Strength = Settings.GlassStrength.value;
            p.Threshold = Settings.GlassThreshold.value;
            p.NightWindows = Settings.GlassNightWindows.value;
            p.ShowMask = Settings.GlassShowMask.value;

            p.Trace = Settings.SsrEnabled.value;
            p.Quality = Settings.SsrQuality.value;
            p.ReflectionBrightness = Settings.ReflectionBrightness.value;
            p.SkySource = Settings.SkyReflection.value;
            p.OffscreenFill = Settings.OffscreenFill.value;
            p.SunGlint = Settings.SunGlint.value;
            p.Smoothness = Settings.GlassSmoothness.value;
            p.EdgeFade = Settings.SsrEdgeFade.value;

            p.PaneWidth = Settings.PaneWidth.value;
            p.Pillow = Settings.PanePillow.value;
            p.Tilt = Settings.PaneTilt.value;
            p.RollerWave = Settings.RollerWave.value;
            p.PaneWeather = Settings.PaneWeather.value;

            p.Rooms = Settings.InteriorEnabled.value;
            p.FloorHeight = Settings.FloorHeight.value;
            p.RoomWidth = Settings.RoomWidth.value;
            p.RoomDepth = Settings.RoomDepth.value;
            p.InteriorBrightness = Settings.InteriorBrightness.value;
            p.Blinds = Settings.Blinds.value;
            p.Furniture = Settings.Furniture.value;
            p.NightRooms = Settings.NightRooms.value;
            p.NightWarmth = Settings.NightWarmth.value;
            return p;
        }
    }
}
