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

        /// <summary>Offices with their ceiling lights on in daytime.</summary>
        public const float CeilingLightsByDay = 0.6f;

        /// <summary>How much of the glass colour the reflection takes (a coating reflects tinted).</summary>
        public const float CoatTint = 0.6f;

        /// <summary>
        /// The lamps' colour temperatures, coolest to warmest (warmth 0 .. 1): daylight-white
        /// office LEDs to incandescent-warm homes. <see cref="WhitePoint"/> reads as white.
        /// </summary>
        public static readonly float[] LampKelvin = { 5000f, 4400f, 3800f, 3200f, 2700f };

        /// <summary>The colour temperature seen as white: a city at night, mixed lamps.</summary>
        public const float WhitePoint = 4000f;

        /// <summary>
        /// The rooms at night are at full, with no settings: the author found anything less wrong
        /// (2026-09-28) and their three sliders went, fixed where he had them. A lit room's light is
        /// this many times the game's own lit window (the old slider's 600%), and every window the
        /// game lights is a lit room (the old share at 100%).
        /// </summary>
        public const float NightRooms = 6f;

        /// <summary>
        /// Added to every building's lamp warmth (0 offices .. 1 homes): the old Light colour slider
        /// at 100%, (1 - 0.45) x 1.5. Every lamp is at or near the warm end, and -- as the room kind
        /// reads the same warmth -- every building's storeys are mostly flats. That is the look the
        /// author chose.
        /// </summary>
        public const float LampShift = 0.825f;

        private static int _glass, _tint, _mask, _refl, _trace, _fill, _pane, _pane2, _room, _room2, _light,
                           _sunDir, _sunColor, _ambSky, _ambGround, _glass2, _colorMap, _lamps;
        private static bool _ids;

        // Filled in place: SetGlobalVectorArray keeps the size of its first call.
        private static readonly Vector4[] LampArray = new Vector4[5];

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
            _room = Shader.PropertyToID("_RGRoom");
            _room2 = Shader.PropertyToID("_RGRoom2");
            _light = Shader.PropertyToID("_RGLight");
            _sunDir = Shader.PropertyToID("_RGSunDir");
            _sunColor = Shader.PropertyToID("_RGSunColor");
            _ambSky = Shader.PropertyToID("_RGAmbSky");
            _ambGround = Shader.PropertyToID("_RGAmbGround");
            _glass2 = Shader.PropertyToID("_RGGlass2");
            _colorMap = Shader.PropertyToID("_RGColorMap");
            _lamps = Shader.PropertyToID("_RGLamps");
            _ids = true;
        }

        /// <summary>
        /// A lamp of this colour temperature as linear RGB, seen by an eye adapted to
        /// <see cref="WhitePoint"/> (von Kries in RGB), scaled to a luminance of 1. The chromaticity is
        /// Kim et al.'s cubic fit of the Planckian locus (2002), 1667 K .. 25000 K.
        /// </summary>
        public static Vector3 LampColour(float kelvin)
        {
            Vector3 c = Planck(kelvin), white = Planck(WhitePoint);
            c = new Vector3(c.x / white.x, c.y / white.y, c.z / white.z);
            float luma = 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;
            return luma > 1e-4f ? c / luma : new Vector3(1f, 1f, 1f);
        }

        /// <summary>A black body's colour at this temperature: linear sRGB (D65 primaries), Y = 1.</summary>
        public static Vector3 Planck(float kelvin)
        {
            double t = Mathf.Clamp(kelvin, 1667f, 25000f);
            double t2 = t * t, t3 = t2 * t;
            double x = t <= 4000.0
                ? -0.2661239e9 / t3 - 0.2343589e6 / t2 + 0.8776956e3 / t + 0.179910
                : -3.0258469e9 / t3 + 2.1070379e6 / t2 + 0.2226347e3 / t + 0.240390;
            double x2 = x * x, x3 = x2 * x;
            double y = t <= 2222.0
                ? -1.1063814 * x3 - 1.34811020 * x2 + 2.18555832 * x - 0.20219683
                : (t <= 4000.0
                    ? -0.9549476 * x3 - 1.37418593 * x2 + 2.09137015 * x - 0.16748867
                    : 3.0817580 * x3 - 5.87338670 * x2 + 3.75112997 * x - 0.37001483);
            double X = x / y, Y = 1.0, Z = (1.0 - x - y) / y;
            return new Vector3(
                (float)(3.2406 * X - 1.5372 * Y - 0.4986 * Z),
                (float)(-0.9689 * X + 1.8758 * Y + 0.0415 * Z),
                (float)(0.0557 * X - 0.2040 * Y + 1.0570 * Z));
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

        public static void Apply(GlassParams p, GlassFrame f)
        {
            Ids();
            Vector3 tint = Tint(p.TintR, p.TintG, p.TintB);
            float rough = Mathf.Clamp01(1f - p.Smoothness);
            float daylight = Daylight(f.SunY);
            float sunUp = Smooth(-0.02f, 0.06f, f.SunY);

            for (int i = 0; i < LampArray.Length; i++)
            {
                Vector3 lamp = LampColour(LampKelvin[i]);
                LampArray[i] = new Vector4(lamp.x, lamp.y, lamp.z, 0f);
            }

            Shader.SetGlobalVectorArray(_lamps, LampArray);
            Shader.SetGlobalVector(_colorMap, new Vector4(Mathf.Max(f.ColorMapWidth, 1), Mathf.Max(f.ColorMapHeight, 1), 0f, 0f));
            Shader.SetGlobalVector(_glass2, new Vector4(p.EdgeSilver, 0f, Mathf.Max(p.FloorBand, 0f), 0f));

            Shader.SetGlobalVector(_glass, new Vector4(p.Reflectivity, p.Glancing, p.Clarity, p.Enabled ? p.Strength : 0f));
            Shader.SetGlobalVector(_tint, new Vector4(tint.x, tint.y, tint.z, p.OwnColour));
            Shader.SetGlobalVector(_mask, new Vector4(p.Threshold, p.NightWindows ? 1f : 0f, p.ShowMask ? 1f : 0f, CoatTint));
            Shader.SetGlobalVector(_refl, new Vector4(p.ReflectionBrightness, rough, p.SunGlint, Mathf.Max(p.EdgeFade, 0.001f)));
            Shader.SetGlobalVector(_trace, new Vector4(p.Trace ? 1f : 0f, StepCount(p.Quality), Stride(p.Quality, f.ScreenHeight), 1f));
            Shader.SetGlobalVector(_fill, new Vector4(p.OffscreenFill, f.GroundY, Mathf.Max(f.CubeMips - 1, 0), 0f));
            Shader.SetGlobalVector(_pane, new Vector4(Mathf.Max(p.PaneWidth, 0.2f), p.Pillow * PillowAt100, p.Tilt * TiltAt100, p.RollerWave * RollerAt100));
            Shader.SetGlobalVector(_pane2, new Vector4(p.PaneWeather ? BowBias(f.Temperature) : 0f, RollerLength, 0f, 0f));
            Shader.SetGlobalVector(_room, new Vector4(Mathf.Max(p.FloorHeight, 1f), Mathf.Max(p.RoomWidth, 1f), Mathf.Max(p.RoomDepth, 1f), p.Rooms ? 1f : 0f));
            Shader.SetGlobalVector(_room2, new Vector4(p.InteriorBrightness, p.Blinds, p.Furniture, NightRooms));
            Shader.SetGlobalVector(_light, new Vector4(daylight, LampShift, CeilingLightsByDay, 0f));
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
