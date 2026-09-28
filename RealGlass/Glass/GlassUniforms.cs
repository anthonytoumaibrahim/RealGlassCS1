using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// The one place that turns the player's settings and the frame into the glass shader's
    /// uniforms (GlassCommon.cginc's _RG* globals). Engine types only, no game types: the GPU
    /// preview runs this same file (tools/preview/sync-sim.ps1).
    /// </summary>
    public static class GlassUniforms
    {
        // The trace per quality at 1080 lines: [stride px, steps]. The stride grows with the
        // screen's height so a step covers the same share of the picture at 4K as at 1080p.
        private static readonly float[] Strides = { 12f, 8f, 5f, 3f };
        private static readonly float[] Steps = { 16f, 28f, 48f, 80f };

        /// <summary>Real curtain-wall numbers behind the 100% of each pane slider.</summary>
        public const float PillowAt100 = 0.004f;   // m of bow at a pane's middle (insulating units: a few mm)
        public const float TiltAt100 = 0.004f;     // rad, ~0.23 degrees of install tolerance
        public const float RollerAt100 = 0.00006f; // m, half the peak-to-valley of a roller wave
        public const float RollerLength = 0.35f;   // m, the furnace's roller pitch

        /// <summary>How much of the glass colour the reflection takes (a coating reflects tinted).</summary>
        public const float CoatTint = 0.6f;

        /// <summary>
        /// The inside of a building seen through the glass by day, as a share of the sky's light: dark
        /// -- indoor light is a few percent of daylight. 10% is where the author had the rooms by day
        /// (round 5); the rooms went in round 7, this is their average. At night there is nothing of
        /// ours behind the glass: the game's own window shows, lit as its asset and the game light it.
        /// </summary>
        public const float InsideByDay = 0.10f;

        private static int _glass, _tint, _mask, _refl, _trace, _fill, _pane, _pane2, _light,
                           _sunDir, _sunColor, _ambSky, _ambGround, _glass2, _colorMap, _features, _budget, _budget2;
        private static bool _ids;

        private static void Ids()
        {
            if (_ids)
                return;
            _glass = Shader.PropertyToID("_RGGlass");
            _tint = Shader.PropertyToID("_RGTint");
            _mask = Shader.PropertyToID("_RGMask");
            _refl = Shader.PropertyToID("_RGRefl");
            _trace = Shader.PropertyToID("_RGTrace");
            _fill = Shader.PropertyToID("_RGFill");
            _pane = Shader.PropertyToID("_RGPane");
            _pane2 = Shader.PropertyToID("_RGPane2");
            _light = Shader.PropertyToID("_RGLight");
            _sunDir = Shader.PropertyToID("_RGSunDir");
            _sunColor = Shader.PropertyToID("_RGSunColor");
            _ambSky = Shader.PropertyToID("_RGAmbSky");
            _ambGround = Shader.PropertyToID("_RGAmbGround");
            _glass2 = Shader.PropertyToID("_RGGlass2");
            _colorMap = Shader.PropertyToID("_RGColorMap");
            _features = Shader.PropertyToID("_RGFeatures");
            _budget = Shader.PropertyToID("_RGBudget");
            _budget2 = Shader.PropertyToID("_RGBudget2");
            _ids = true;
        }

        /// <summary>0 (night) .. 1 (day), from how high the sun stands: civil dusk to a few degrees up.</summary>
        public static float Daylight(float sunY)
        {
            return Smooth(-0.10f, 0.10f, sunY);
        }

        /// <summary>
        /// -1 all panes sunk .. +1 all bulging: sealed insulating units bulge when warm and sink when
        /// cold (their gas against the air). Neutral at 15 C, as far as it goes at +-20 C from it.
        /// </summary>
        public static float BowBias(float temperature)
        {
            return Mathf.Clamp((temperature - 15f) / 20f, -0.7f, 0.7f);
        }

        /// <summary>The glass tint as the shader wants it: linear, its brightest channel 1.</summary>
        public static Vector3 Tint(float r, float g, float b)
        {
            float lr = ToLinear(r), lg = ToLinear(g), lb = ToLinear(b);
            float max = Mathf.Max(lr, Mathf.Max(lg, lb));
            if (max < 0.002f)
                return new Vector3(1f, 1f, 1f);
            return new Vector3(lr / max, lg / max, lb / max);
        }

        /// <summary>sRGB to linear (IEC 61966-2-1), in managed code so the tests can run it.</summary>
        public static float ToLinear(float c)
        {
            c = Mathf.Clamp01(c);
            return c <= 0.04045f ? c / 12.92f : (float)System.Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// <summary>The trace's stride in pixels for this quality and screen height.</summary>
        public static float Stride(int quality, int screenHeight)
        {
            int q = Mathf.Clamp(quality, 0, Strides.Length - 1);
            return Mathf.Max(1f, Mathf.Round(Strides[q] * Mathf.Max(screenHeight, 360) / 1080f));
        }

        public static float StepCount(int quality)
        {
            return Steps[Mathf.Clamp(quality, 0, Steps.Length - 1)];
        }

        public static void Apply(GlassParams p, GlassFrame f, GlassQuality q)
        {
            Ids();
            // What the shader's loops may spend (GlassQuality): x cells of the city walk, y its
            // coarse blocks, w steps each way up and down to a painted pane's top and bottom;
            // budget2 x across to its sides (0: the panes' own grid), y of the Hi-Z trace (read only
            // while GlassScreen has built the pyramid: _RGHiZInfo.w).
            bool panes = p.PanesFollowWindows && q.PaneSearchSteps > 0 && q.WindowSearchSteps > 0;
            Shader.SetGlobalVector(_budget, new Vector4(q.CityWalkSteps, q.CityWalkBlocks, 0f, panes ? q.WindowSearchSteps : 0f));
            Shader.SetGlobalVector(_budget2, new Vector4(panes ? q.PaneSearchSteps : 0f, q.HiZSteps, 0f, 0f));
            Vector3 tint = Tint(p.TintR, p.TintG, p.TintB);
            float rough = Mathf.Clamp01(1f - p.Smoothness);
            float daylight = Daylight(f.SunY);
            float sunUp = Smooth(-0.02f, 0.06f, f.SunY);

            Shader.SetGlobalVector(_colorMap, new Vector4(Mathf.Max(f.ColorMapWidth, 1), Mathf.Max(f.ColorMapHeight, 1), 0f, 0f));
            Shader.SetGlobalVector(_glass2, new Vector4(p.EdgeSilver, 0f, 0f, 0f));
            // x the buildings round in the reflections (the map itself says whether it is there:
            // _RGCityMapST.w), w steady glints.
            Shader.SetGlobalVector(_features, new Vector4(p.Neighbours ? 1f : 0f, 0f, 0f, p.SteadyGlints ? 1f : 0f));

            Shader.SetGlobalVector(_glass, new Vector4(p.Reflectivity, p.Glancing, p.Clarity, p.Enabled ? p.Strength : 0f));
            Shader.SetGlobalVector(_tint, new Vector4(tint.x, tint.y, tint.z, p.OwnColour));
            Shader.SetGlobalVector(_mask, new Vector4(p.Threshold, p.NightWindows ? 1f : 0f, p.ShowMask ? 1f : 0f, CoatTint));
            Shader.SetGlobalVector(_refl, new Vector4(p.ReflectionBrightness, rough, p.SunGlint, Mathf.Max(p.EdgeFade, 0.001f)));
            Shader.SetGlobalVector(_trace, new Vector4(p.Trace ? 1f : 0f, StepCount(p.Quality), Stride(p.Quality, f.ScreenHeight), 1f));
            Shader.SetGlobalVector(_fill, new Vector4(p.OffscreenFill, f.GroundY, Mathf.Max(f.CubeMips - 1, 0), 0f));
            Shader.SetGlobalVector(_pane, new Vector4(Mathf.Max(p.PaneWidth, 0.2f), p.Pillow * PillowAt100, p.Tilt * TiltAt100, p.RollerWave * RollerAt100));
            Shader.SetGlobalVector(_pane2, new Vector4(p.PaneWeather ? BowBias(f.Temperature) : 0f, RollerLength, Mathf.Max(p.FloorHeight, 1f), 0f));
            Shader.SetGlobalVector(_light, new Vector4(daylight, InsideByDay, 0f, 0f));
            Shader.SetGlobalVector(_sunDir, new Vector4(f.SunX, f.SunY, f.SunZ, sunUp));
            Shader.SetGlobalVector(_sunColor, new Vector4(f.SunR, f.SunG, f.SunB, 0f));
            Shader.SetGlobalVector(_ambSky, new Vector4(f.AmbSkyR, f.AmbSkyG, f.AmbSkyB, 0f));
            Shader.SetGlobalVector(_ambGround, new Vector4(f.AmbGroundR, f.AmbGroundG, f.AmbGroundB, 0f));
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
