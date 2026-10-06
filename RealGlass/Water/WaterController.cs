using System;
using ColossalFramework;
using UnityEngine;

namespace RealGlass.Water
{
    /// <summary>
    /// The F8 panel's Water tab, for one city (on ModController's GameObject, beside the glass;
    /// independent of the glass's switch): the map's water's brightness, its sky with real water's
    /// reflectance (<see cref="WaterSurface"/>) and the city in it (<see cref="WaterCityReflection"/>).
    /// Off by default (the author, 2026-10-03). Off means nothing is held: the theme's water, and
    /// _RGWaterHeld = 0 so RealLight's copy of these controls may take it.
    /// </summary>
    public sealed class WaterController : MonoBehaviour
    {
        private static readonly int IdTable = Shader.PropertyToID("_RGSeaTable");

        private static readonly int IdGameRipples = Shader.PropertyToID("_WaterNormal");

        private WaterSurface _surface;
        private WaterCityReflection _city;
        private WaterRipples _ripples;
        private WaterFoam _foam;
        private Vector4[] _table;
        private readonly double _spread = WaterOptics.Spread(WaterOptics.SlopeVariance(WaterOptics.CityWind));
        private bool _failed;
        private string _cityState = "";

        private void Start()
        {
            try
            {
                double[] rho = WaterOptics.BuildTable(WaterOptics.SlopeVariance(WaterOptics.SkyWind));
                _table = new Vector4[WaterOptics.TableSize];
                for (int i = 0; i < _table.Length; i++)
                    _table[i] = new Vector4((float)rho[i], 0f, 0f, 0f);

                _surface = WaterSurface.Create();
                _city = WaterCityReflection.Create();
                _ripples = WaterRipples.Create();
                _foam = WaterFoam.Create();
                Log.Msg("water: ready (cube " + (_surface != null ? "yes" : "NO SHADER") + ", city reflection " + (_city != null ? "yes" : "NO SHADER") +
                        ", ripples " + (_ripples != null ? "yes" : "NO SHADER") + ", foam " + (_foam != null ? "yes" : "NO SHADER") +
                        "; reflectance " + (rho[WaterOptics.TableSize - 1] * 100.0).ToString("F1") + "% looking down, " + (rho[4] * 100.0).ToString("F0") +
                        "% at 1 deg); " + Describe());
            }
            catch (Exception e)
            {
                Log.Error("water: could not start", e);
                _failed = true;
            }
        }

        /// <summary>The Water tab's settings, for the log.</summary>
        public static string Describe()
        {
            if (!On(Settings.WaterEnabled))
                return "water controls off";
            return "water controls ON: brightness " + Pct(Settings.WaterBrightness) +
                   ", moving ripples " + (On(Settings.WaterRipples) ? Pct(Settings.WaterRippleStrength) : "off") +
                   ", foam " + (On(Settings.WaterFoam) ? Pct(Settings.WaterFoamAmount) + " (shores " + Pct(Settings.WaterFoamShore) + ", fast water " +
                   Pct(Settings.WaterFoamRapids) + ", wave fronts " + Pct(Settings.WaterFoamFronts) + ")" : "off") +
                   ", real sky " + (On(Settings.WaterRealSky) ? Pct(Settings.WaterSky) : "off") +
                   ", city " + (On(Settings.WaterCity) ? Pct(Settings.WaterCityDay) + " day / " + Pct(Settings.WaterCityNight) + " night, waves " +
                   Pct(Settings.WaterCityWaves) + ", streaks " + Pct(Settings.WaterCityStreaks) + ", length " + Pct(Settings.WaterCityLength) : "off");
        }

        private static bool On(BoolSetting s)
        {
            return s != null && s.value;
        }

        private static float Value(FloatSetting s, float fallback)
        {
            return s != null ? s.value : fallback;
        }

        /// <summary>To a tenth where there is one: "0.5%", "100%".</summary>
        private static string Pct(FloatSetting s)
        {
            return s != null ? (Math.Round(s.value * 1000.0) / 10.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%" : "?";
        }

        private void LateUpdate()
        {
            if (_failed)
                return;

            try
            {
                Frame();
            }
            catch (Exception e)
            {
                Log.Error("water: frame failed; the water is given back and stays the game's until the city is reloaded", e);
                _failed = true;
                Release();
            }
        }

        private void Frame()
        {
            if (!On(Settings.WaterEnabled))
            {
                Release();
                return;
            }

            Shader.SetGlobalVectorArray(IdTable, _table);

            Vector3 toSun;
            Color light = WaterLight(out toSun);

            if (_surface != null)
            {
                _surface.HoldColour(Value(Settings.WaterBrightness, Settings.Defaults.WaterBrightness));
                if (On(Settings.WaterRealSky))
                    _surface.HoldCube(Value(Settings.WaterSky, Settings.Defaults.WaterSky), light);
                else
                    _surface.ReleaseCube();
            }

            RipplesFrame();
            FoamFrame();
            CityFrame(light, toSun);
        }

        /// <summary>The foam on the game's water this frame, or none (off, an info view, the snapshot tool, no sea level).</summary>
        private void FoamFrame()
        {
            if (_foam == null)
                return;

            float level = float.NaN;
            if (Singleton<TerrainManager>.exists && Singleton<TerrainManager>.instance.WaterSimulation != null)
                level = Singleton<TerrainManager>.instance.WaterSimulation.m_currentSeaLevel;
            bool show = On(Settings.WaterFoam) && !float.IsNaN(level) && !float.IsInfinity(level) &&
                        !(Singleton<InfoManager>.exists && Singleton<InfoManager>.instance.CurrentMode != InfoManager.InfoMode.None) &&
                        !(ToolsModifierControl.toolController != null && ToolsModifierControl.toolController.CurrentTool is SnapshotTool);
            if (!show)
            {
                _foam.Hide();
                return;
            }

            _foam.Show(Value(Settings.WaterFoamAmount, Settings.Defaults.WaterFoamAmount), Value(Settings.WaterFoamShore, Settings.Defaults.WaterFoamShore),
                       Value(Settings.WaterFoamRapids, Settings.Defaults.WaterFoamRapids), Value(Settings.WaterFoamFronts, Settings.Defaults.WaterFoamFronts), level,
                       Value(Settings.WaterFoamView, 0f));
        }

        /// <summary>
        /// Our moving ripples on the water (drawn this frame), or the game's own again. The game's
        /// texture is measured either way: the city reflection's gain is set against it.
        /// </summary>
        private void RipplesFrame()
        {
            if (_ripples == null)
                return;

            if (On(Settings.WaterRipples))
            {
                _ripples.Draw(Value(Settings.WaterRippleStrength, Settings.Defaults.WaterRippleStrength));
                _ripples.Hold();
            }
            else
            {
                _ripples.Release();
                _ripples.Measure();
            }
        }

        /// <summary>The ripple texture the water is shaded with now: ours while held, else the game's own.</summary>
        private Texture ShadedRipples()
        {
            if (_ripples != null && _ripples.Held)
                return _ripples.Texture;
            return Shader.GetGlobalTexture(IdGameRipples);
        }

        /// <summary>
        /// The light the game's water lights its cube with (linear; the sun as the glass reads it,
        /// GlassController.ReadFrame): the sun on the level + the sky's ambient, each channel floored
        /// (<see cref="WaterOptics.FloorLight"/>).
        /// </summary>
        private static Color WaterLight(out Vector3 toSun)
        {
            toSun = Vector3.down;
            Color sun = Color.black;
            DayNightProperties dn = DayNightProperties.instance;
            if (dn != null && dn.m_SunLight != null)
            {
                toSun = -dn.m_SunLight.forward;
                Light light = dn.m_SunLight.GetComponent<Light>();
                if (light != null && light.enabled)
                    sun = light.color.linear * light.intensity;
            }

            Color lit = sun * Mathf.Max(0f, toSun.y) + RenderSettings.ambientSkyColor.linear;
            double r = lit.r, g = lit.g, b = lit.b;
            WaterOptics.FloorLight(ref r, ref g, ref b);
            return new Color((float)r, (float)g, (float)b, 1f);
        }

        private void CityFrame(Color lit, Vector3 toSun)
        {
            if (_city == null)
                return;

            Camera camera = Camera.main;
            string why = null;
            float level = float.NaN;
            if (!On(Settings.WaterCity))
                why = "switched off";
            else if (camera == null)
                why = "no camera";
            else if (Singleton<InfoManager>.exists && Singleton<InfoManager>.instance.CurrentMode != InfoManager.InfoMode.None)
                why = "an info view is open";
            else if (ToolsModifierControl.toolController != null && ToolsModifierControl.toolController.CurrentTool is SnapshotTool)
                why = "the snapshot tool";
            else
            {
                TerrainManager terrain = Singleton<TerrainManager>.exists ? Singleton<TerrainManager>.instance : null;
                if (terrain != null && terrain.WaterSimulation != null)
                    level = terrain.WaterSimulation.m_currentSeaLevel;
                if (float.IsNaN(level) || float.IsInfinity(level))
                    why = "no sea level";
                else if (camera.transform.position.y - level <= 1f)
                    why = "the camera is at the water";
            }

            if (why != null)
            {
                if (_city.Attached)
                    _city.Detach();
                CityState("off (" + why + ")");
                return;
            }

            float day = (float)WaterOptics.Daylight(toSun.y);
            float dayStrength = Value(Settings.WaterCityDay, Settings.Defaults.WaterCityDay);
            float nightStrength = Value(Settings.WaterCityNight, Settings.Defaults.WaterCityNight);
            float strength = nightStrength + (dayStrength - nightStrength) * day;
            float skyShare = _surface != null && _surface.CubeHeld ? Value(Settings.WaterSky, Settings.Defaults.WaterSky) : 0f;

            double up = WaterOptics.StreakSigma(Value(Settings.WaterCityStreaks, Settings.Defaults.WaterCityStreaks), _spread, camera.fieldOfView);
            double rms = _ripples != null ? _ripples.DecodedRms : WaterOptics.DefaultRippleRms;
            double gain = WaterOptics.CityRippleGain(Value(Settings.WaterCityWaves, Settings.Defaults.WaterCityWaves), rms);
            _city.SetUniforms(level, strength, skyShare, WaterSurface.ThemeCube(), lit, up, ShadedRipples(), gain,
                              Value(Settings.WaterCityLength, Settings.Defaults.WaterCityLength), camera.farClipPlane);
            _city.Attach(camera);
            CityState("on (sea level " + level.ToString("F1") + " m)");
        }

        /// <summary>One line whenever the city reflection's state changes.</summary>
        private void CityState(string state)
        {
            if (state == _cityState)
                return;
            // The sea level moves with the tide of a tsunami: not a state of its own.
            if (state.StartsWith("on", StringComparison.Ordinal) && _cityState.StartsWith("on", StringComparison.Ordinal))
                return;
            _cityState = state;
            Log.Msg("water: city reflection " + state);
        }

        private void Release()
        {
            if (_city != null && _city.Attached)
                _city.Detach();
            if (_ripples != null)
                _ripples.Release();
            if (_foam != null)
                _foam.Hide();
            if (_surface != null)
                _surface.GiveBack();
            else
                Shader.SetGlobalFloat("_RGWaterHeld", 0f);
            _cityState = "";
        }

        private void OnDestroy()
        {
            try
            {
                if (_city != null)
                    _city.Destroy();
                if (_ripples != null)
                    _ripples.Destroy();
                if (_foam != null)
                    _foam.Destroy();
                if (_surface != null)
                    _surface.Destroy();
                Shader.SetGlobalFloat("_RGWaterHeld", 0f);
            }
            catch (Exception e)
            {
                Log.Error("water: could not give the water back", e);
            }

            _city = null;
            _ripples = null;
            _foam = null;
            _surface = null;
        }
    }
}
