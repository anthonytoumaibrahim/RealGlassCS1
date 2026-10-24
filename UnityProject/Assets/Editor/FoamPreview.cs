using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RealGlass.Water;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Pictures of the water's foam (RealGlass/WaterFoam) on this machine's GPU -- tools\preview\
/// foam-render.ps1 writes foam-preview-input.txt (the mod's numbers, read from the DLL, and the shots)
/// and runs
///   Unity.exe -batchmode -quit -projectPath . -executeMethod FoamPreview.Render
/// A coast, 4 km square: the sea to the west at 40 m (the game's heights are 0..1024 m), a beach
/// whose waterline wanders, a town of towers on the land, and a river running down to the sea through
/// a rapid. Its water is the GAME'S DATA: the surface heights in two simulation frames, the terrain's
/// heights, and the surface maps (normal, flow, depth) at the game's resolutions and in its
/// encodings, which both the stand-in water (Editor/PreviewGameWater.shader) and the foam read
/// through WaterGame.cginc, as in the game. "tsunami" views add a 3 m bulge running at the shore,
/// "tsunamiwall" views a 20 m wall (seen from below its crest and from a city camera).
/// The camera in legacy deferred HDR like the game's; the city reflection on the sea as the mod
/// records it. Each shot is one PNG (tonemapped, sRGB).
/// </summary>
public static class FoamPreview
{
    private const float SeaLevel = 40f;
    private const float Half = 2048f;
    private const int WaterTexels = 256;
    private const int TerrainTexels = 1024;
    private const int SurfaceTexels = 128;
    private const float HeightScale = 1024f;
    private const float RiverHalf = 32f;

    private sealed class Shot
    {
        public string Name;
        public string View;
        public bool Foam;
        public bool Sources;
        // The foam drawn (the layered-foam experiment): 0 none, 1 RealGlass/WaterFoam, 2 its sources,
        // 3 today's foam (Editor/PreviewFoamToday.shader, frozen at 2b509fc), 4 the layers' view.
        public int Mode;
        public float Seconds;
        public float Strength;
        // The sky on the water: 0 the mod's cube until 2026-10-08 (the stand-in's own, x SkyGain); 1 the
        // sky layer (RealGlass/WaterSky) at the Sky slider SkyGain, the stand-in reflecting none.
        public int SkyMode;
        public float SkyGain = 1f;
        // The water's colour (sRGB, the stand-in's _BodyColour); null: the preview's own.
        public Color? Body;
        // The ripples: 0 the 32 waves (RealGlass/WaterRipples), 1 the FFT waves (RealGlass/WaterWaves).
        public int Waves;
        // The sun's glitter on the sky layer (1 = real water; 0 none, the game's own highlight kept).
        public float Glitter;
        // Its size: every facet's slope x this (1 = the waves' own).
        public float GlitterSize = 1f;
    }

    /// <summary>A run of frames for the flicker under temporal anti-aliasing (as WaterPreview's).</summary>
    private sealed class Seq
    {
        public string Name;
        public string View;
        public float Seconds;
        public float Strength;
    }

    /// <summary>What a frame needs: everything the shots set up.</summary>
    private sealed class Scene
    {
        public Coast Coast;
        public Camera Camera;
        public GameObject CameraObject;
        public RenderTexture Target;
        public Light Sun;
        public Material Skybox, Water, Foam, Reflection, Surface, Sky;
        public Camera Capture;
        public RenderTexture SkyCube;
        public List<Tower> Towers;
        public List<Renderer> FoamRenderers, SkyRenderers;
        public Color BodyColour;
        public Vector4 SkyInfo;
        public Vector4[] Rows;
        public float RippleRms;
        public RenderTexture Ripples;
        public float? Tsunami;
        public List<string> Log;

        // The ripples' generators: the 32 waves' material, the FFT waves (null without a "spectrum"
        // line), which one this shot uses, and the FFT's choppiness.
        public Material Generator;
        public WaterWaves Waves;
        public int WaveMode;
        public float Chop;

        // The layered foam: today's foam for the comparison (Editor/PreviewFoamToday.shader).
        public Material FoamToday;

        /// <summary>The data, light and camera for a view (true at night).</summary>
        public bool Set(string view, float strength)
        {
            float wantTsunami = view.StartsWith("tsunamiwall", StringComparison.Ordinal) ? Coast.Wall
                : view.StartsWith("tsunami", StringComparison.Ordinal) ? 3f : 0f;
            if (Tsunami != wantTsunami)
            {
                Coast.Tsunami = wantTsunami;
                Coast.Bind(FoamToday != null ? new[] { Water, Foam, FoamToday, Surface, Sky } : new[] { Water, Foam, Surface, Sky }, Log);
                Tsunami = wantTsunami;
            }

            Reflection.SetVector("_RGWaterGeom", new Vector4(SeaLevel, strength, 0f, 0f));
            return SetView(view, CameraObject, Sun, Skybox, Capture, SkyCube, Water, Towers);
        }

        /// <summary>
        /// The sky on the water for a shot: the old cube (the stand-in's, x gain) or the sky layer at the
        /// Sky slider gain; the city reflection takes the place of that sky (skyShare = gain, as the mod
        /// sets it), the stand-in's colour.
        /// </summary>
        public void SkyFor(Shot shot, float strength)
        {
            bool layer = shot.SkyMode == 1;
            Water.SetFloat("_SkyGain", layer ? 0f : shot.SkyGain);
            foreach (Renderer r in SkyRenderers)
                r.enabled = layer;
            Shader.SetGlobalVector("_RGWaterSky", new Vector4(shot.SkyGain, SkyInfo.y, SkyInfo.z, 0f));
            Reflection.SetVector("_RGWaterGeom", new Vector4(SeaLevel, strength, shot.SkyGain, 1f));
            Water.SetColor("_BodyColour", shot.Body.HasValue ? shot.Body.Value : BodyColour);
            // The bubbles take the water's own colour, linear (the mod: the colour it draws the water with).
            Color bodyLinear = (shot.Body.HasValue ? shot.Body.Value : BodyColour).linear;
            Foam.SetVector("_RGFoamWater", new Vector4(bodyLinear.r, bodyLinear.g, bodyLinear.b, 0f));

            // The ripples' generator, the sky layer's averaging (the FFT waves' table, at the ripples'
            // root-mean-square slope, or the 32 waves' own), and the sun's glitter: the game's highlight
            // off while it draws (the mod sets the material's _SpecColor to black).
            WaveMode = shot.Waves == 1 && Waves != null ? 1 : 0;
            float rms = 0.5f * RippleRms;
            var table = new Vector4[WaterSpectrum.TableLevels];
            Vector4 tail = Vector4.zero;
            if (WaveMode == 1)
            {
                WaterSpectrum s = Waves.Spectrum;
                float ms = rms * rms;
                for (int e = 0; e < table.Length; e++)
                    table[e] = new Vector4((float)s.Table[4 * e] * ms, (float)s.Table[4 * e + 1] * ms, (float)s.Table[4 * e + 2] * ms, (float)s.Table[4 * e + 3]);
                tail = new Vector4((float)s.TailXX * ms, (float)s.TailZZ * ms, (float)s.TailXZ * ms, 1f);
            }
            Shader.SetGlobalVectorArray("_RGWaterSkyWaves", table);
            Shader.SetGlobalVector("_RGWaterSkyTail", tail);
            Vector3 toSun = -Sun.transform.forward;
            Shader.SetGlobalVector("_RGWaterSun", new Vector4(toSun.x, toSun.y, toSun.z, layer ? shot.Glitter : 0f));
            Color light = Sun.color.linear * Sun.intensity;
            Shader.SetGlobalVector("_RGWaterSunColour", new Vector4(light.r, light.g, light.b, shot.GlitterSize));
            Water.SetFloat("_SpecGain", layer && shot.Glitter > 0f ? 0f : 0.6f);
        }

        /// <summary>The game's clocks at t (SimulationManager: x seconds into the minute, w the same as an angle), and the ripples for it.</summary>
        public void Clock(float t)
        {
            float minute = t % 60f;
            Shader.SetGlobalVector("_SimulationTime", new Vector4(minute, minute / 60f, 2f * Mathf.PI * minute, 2f * Mathf.PI * minute / 60f));
            Shader.SetGlobalVector("_SimulationTime2", new Vector4(t, t / 3600f, 0f, 0f));
            // As WaterRipples draws them: one ARGB32 linear texture with mips, anisotropic 4, redrawn.
            if (Ripples == null)
            {
                Ripples = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    useMipMap = true,
                    autoGenerateMips = true,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                };
                Ripples.Create();
            }
            if (WaveMode == 1)
                Waves.Draw(Ripples, 0.5f * RippleRms, Chop);
            else
            {
                Generator.SetVectorArray("_RGRippleWaves", Rows);
                Generator.SetVector("_RGRipple", new Vector4(0.5f * RippleRms, 0f, 0f, 0f));
                Graphics.Blit(Texture2D.blackTexture, Ripples, Generator, 0);
            }
            Water.SetTexture("_WaterNormal", Ripples);
            Sky.SetTexture("_WaterNormal", Ripples);
            Reflection.SetTexture("_RGRipples", Ripples);
        }

        public void ShowFoam(bool on, bool sources)
        {
            ShowFoam(on ? (sources ? 2 : 1) : 0);
        }

        /// <summary>The foam for a shot's mode (<see cref="Shot.Mode"/>).</summary>
        public void ShowFoam(int mode)
        {
            Material m = mode == 3 && FoamToday != null ? FoamToday : Foam;
            foreach (Renderer r in FoamRenderers)
            {
                r.enabled = mode != 0;
                r.sharedMaterial = m;
            }
            Foam.SetVector("_RGFoamDebug", new Vector4(mode == 2 ? 1f : mode == 4 ? 4f : 0f, 0f, 0f, 0f));
        }
    }

    public static void Render()
    {
        string dir = Directory.GetCurrentDirectory();
        var log = new List<string>();
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var table = new Vector4[33];
            var rows = new Vector4[64];
            var foam = new Vector4[6];
            var layers = new Vector4[10];
            var shots = new List<Shot>();
            var seqs = new List<Seq>();
            var taa = new float[] { 64f, 32f, 60f, 1f, 0.94f, 0.15f };
            string outDir = dir;
            int width = 960, height = 540;
            float streak = 0f, length = 1f, exposureDay = 1f, exposureNight = 1f, rippleRms = 0.3f, cityGain = 0f;
            var skyRipples = new Vector4(0.64f, 5f, 1024f, 0.0225f);
            var body = new Color(0.03f, 0.06f, 0.08f, 1f);
            // The FFT waves' spectrum (wind m/s, fetch m, heading degrees, seed, choppiness), if asked for.
            double[] waveSpec = null;
            foreach (string raw in File.ReadAllLines(Path.Combine(dir, "foam-preview-input.txt")))
            {
                string[] p = raw.Split(' ');
                if (p[0] == "out")
                    outDir = raw.Substring(4);
                else if (p[0] == "size")
                {
                    width = int.Parse(p[1], CultureInfo.InvariantCulture);
                    height = int.Parse(p[2], CultureInfo.InvariantCulture);
                }
                else if (p[0] == "table")
                    table[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Vector4(WaterPreview.F(p[2]), 0f, 0f, 0f);
                else if (p[0] == "ripple")
                    rows[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Vector4(WaterPreview.F(p[2]), WaterPreview.F(p[3]), WaterPreview.F(p[4]), WaterPreview.F(p[5]));
                else if (p[0] == "ripplerms")
                    rippleRms = WaterPreview.F(p[1]);
                else if (p[0] == "citygain")
                    cityGain = WaterPreview.F(p[1]);
                else if (p[0] == "streak")
                    streak = WaterPreview.F(p[1]);
                else if (p[0] == "length")
                    length = WaterPreview.F(p[1]);
                else if (p[0] == "exposure")
                {
                    exposureDay = WaterPreview.F(p[1]);
                    exposureNight = WaterPreview.F(p[2]);
                }
                else if (p[0] == "foam")
                    foam[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Vector4(WaterPreview.F(p[2]), WaterPreview.F(p[3]), WaterPreview.F(p[4]), WaterPreview.F(p[5]));
                else if (p[0] == "layer")
                    layers[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Vector4(WaterPreview.F(p[2]), WaterPreview.F(p[3]), WaterPreview.F(p[4]), WaterPreview.F(p[5]));
                else if (p[0] == "skyripples")
                    skyRipples = new Vector4(WaterPreview.F(p[1]), WaterPreview.F(p[2]), WaterPreview.F(p[3]), WaterPreview.F(p[4]));
                else if (p[0] == "body")
                    body = Hex(p[1]) ?? body;
                else if (p[0] == "spectrum")
                    waveSpec = new double[] { WaterPreview.F(p[1]), WaterPreview.F(p[2]), WaterPreview.F(p[3]), WaterPreview.F(p[4]), WaterPreview.F(p[5]) };
                else if (p[0] == "depthskew")
                    Shader.SetGlobalFloat("_PreviewDepthSkew", WaterPreview.F(p[1]));
                else if (p[0] == "shot")
                    shots.Add(new Shot
                    {
                        Name = p[1], View = p[2], Foam = p[3] != "0", Sources = p[3] == "2", Mode = int.Parse(p[3], CultureInfo.InvariantCulture),
                        Seconds = WaterPreview.F(p[4]), Strength = WaterPreview.F(p[5]),
                        SkyMode = p.Length > 6 ? int.Parse(p[6], CultureInfo.InvariantCulture) : 0,
                        SkyGain = p.Length > 7 ? WaterPreview.F(p[7]) : 1f,
                        Body = p.Length > 8 ? Hex(p[8]) : null,
                        Waves = p.Length > 9 ? int.Parse(p[9], CultureInfo.InvariantCulture) : 0,
                        Glitter = p.Length > 10 ? WaterPreview.F(p[10]) : 0f,
                        GlitterSize = p.Length > 11 ? WaterPreview.F(p[11]) : 1f,
                    });
                else if (p[0] == "taa")
                {
                    // frames, warm-up frames, frames a second, jitter spread, stationary blending, sharpen
                    for (int k = 0; k < taa.Length; k++)
                        taa[k] = WaterPreview.F(p[k + 1]);
                }
                else if (p[0] == "seq")
                    seqs.Add(new Seq { Name = p[1], View = p[2], Seconds = WaterPreview.F(p[3]), Strength = WaterPreview.F(p[4]) });
            }

            Shader.SetGlobalVectorArray("_RGSeaTable", table);

            // The foam, and the stand-in for the game's water, both on the same data.
            Shader foamShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterFoam.shader");
            log.Add("foam shader " + (foamShader != null ? foamShader.name + " supported=" + foamShader.isSupported : "MISSING"));
            var foamMaterial = new Material(foamShader);
            foamMaterial.SetTexture("_RGFoamPattern", Pattern());
            string[] names = { "_RGFoamScale", "_RGFoamFeature", "_RGFoamShore", "_RGFoamFast", "_RGFoamFront", "_RGFoamLook" };
            for (int i = 0; i < names.Length; i++)
                foamMaterial.SetVector(names[i], foam[i]);
            string[] layerNames = { "_RGFoamLayers", "_RGFoamSheet", "_RGFoamBubbles", "_RGFoamSurf", "_RGFoamScale2", "_RGFoamExtra", "_RGFoamCrest", "_RGFoamAbsorb", "_RGFoamOuter", "_RGFoamSteep" };
            for (int i = 0; i < layerNames.Length; i++)
                foamMaterial.SetVector(layerNames[i], layers[i]);

            // Today's foam, frozen, for the comparison column (the same six vectors and pattern: its two
            // channels are today's).
            Material foamToday = null;
            foreach (Shot s in shots)
            {
                if (s.Mode != 3 || foamToday != null)
                    continue;
                Shader todayShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/PreviewFoamToday.shader");
                log.Add("today's foam shader " + (todayShader != null ? todayShader.name + " supported=" + todayShader.isSupported : "MISSING"));
                foamToday = new Material(todayShader);
                foamToday.SetTexture("_RGFoamPattern", foamMaterial.GetTexture("_RGFoamPattern"));
                for (int i = 0; i < names.Length; i++)
                    foamToday.SetVector(names[i], foam[i]);
            }
            var water = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/PreviewGameWater.shader"));
            water.SetColor("_BodyColour", body);

            // The sky layer (RealGlass/WaterSky) on the same meshes, as WaterSkyLayer draws it.
            Shader skyShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterSky.shader");
            log.Add("sky shader " + (skyShader != null ? skyShader.name + " supported=" + skyShader.isSupported : "MISSING"));
            var skyMaterial = new Material(skyShader);

            var coast = new Coast();
            var waterRenderers = new List<Renderer>();
            var foamRenderers = new List<Renderer>();
            var skyRenderers = new List<Renderer>();
            BuildScene(water, foamMaterial, skyMaterial, waterRenderers, foamRenderers, skyRenderers);
            List<Tower> towers = BuildTowers();
            log.Add("scene: " + waterRenderers.Count + " water tiles, " + towers.Count + " towers");

            // Light, sky, the sky's cube for the water (as WaterPreview).
            var skybox = new Material(Shader.Find("Skybox/Procedural"));
            RenderSettings.skybox = skybox;
            var sunObject = new GameObject("preview sun");
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;
            var skyCube = new RenderTexture(128, 128, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
            };
            skyCube.Create();
            Shader.SetGlobalTexture("_RGWaterSkyCube", skyCube);
            Shader.SetGlobalVector("_RGWaterSkyRipples", skyRipples);
            var captureObject = new GameObject("preview sky capture");
            Camera capture = captureObject.AddComponent<Camera>();
            capture.enabled = false;
            capture.clearFlags = CameraClearFlags.Skybox;
            capture.cullingMask = 0;
            capture.allowHDR = true;
            capture.renderingPath = RenderingPath.Forward;
            water.SetTexture("_Sky", skyCube);

            var cameraObject = new GameObject("preview camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.renderingPath = RenderingPath.DeferredLighting;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 20000f;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            camera.targetTexture = target;

            // The city reflection on the sea, as WaterCityReflection.Record records it.
            var reflection = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/CityReflection.shader"));
            // The sky the city takes the place of: the live sky, light already (the mod's with the sky layer).
            reflection.SetTexture("_RGWaterTheme", skyCube);
            var cube = WaterPreview.UnitCube();
            int w = Mathf.Max(1, width / 2), h = Mathf.Max(1, height / 2);
            RenderTexture frame = WaterPreview.Target(w, h, true), trace = WaterPreview.Target(w, h, true), streaked = WaterPreview.Target(w, h, false);
            reflection.SetTexture("_RGWaterFrame", frame);
            var buffer = new CommandBuffer { name = "RealGlass water reflection (preview)" };
            var surface = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterSurfaceMask.shader"));
            var surfaceDepth = new RenderTexture(width, height, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            surfaceDepth.filterMode = FilterMode.Point;
            surfaceDepth.Create();
            reflection.SetTexture("_RGWaterSurfaceDepth", surfaceDepth);
            buffer.SetRenderTarget(surfaceDepth);
            buffer.ClearRenderTarget(true, true, Color.clear);
            foreach (Renderer renderer in waterRenderers)
                buffer.DrawMesh(renderer.GetComponent<MeshFilter>().sharedMesh, renderer.transform.localToWorldMatrix, surface, 0, 0);
            buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            buffer.Blit(BuiltinRenderTextureType.CameraTarget, frame);
            buffer.SetRenderTarget(trace);
            buffer.ClearRenderTarget(false, true, Color.clear);
            buffer.DrawMesh(cube, Matrix4x4.identity, reflection, 0, 0);
            buffer.Blit(trace, streaked, reflection, 1);
            buffer.SetGlobalTexture("_RGWaterStreaked", streaked);
            buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            buffer.DrawMesh(cube, Matrix4x4.identity, reflection, 0, 2);
            camera.AddCommandBuffer(CameraEvent.AfterImageEffectsOpaque, buffer);
            reflection.SetVector("_RGWaterMarch", new Vector4(0.9f * camera.farClipPlane, 30f, 0.05f, Mathf.Max(1f, length)));
            reflection.SetVector("_RGWaterStreak", new Vector4(streak, 1f / h, 0f, 0f));
            reflection.SetVector("_RGWaterWave", new Vector4(cityGain * 2f, 3f, 1f, 0f));
            reflection.SetVector("_RGWaterLight", new Vector4(1f, 1f, 1f, 0f));

            var scene = new Scene
            {
                Coast = coast, Camera = camera, CameraObject = cameraObject, Target = target, Sun = sun, Skybox = skybox, Water = water,
                Foam = foamMaterial, Reflection = reflection, Surface = surface, Sky = skyMaterial, Capture = capture, SkyCube = skyCube, Towers = towers,
                FoamRenderers = foamRenderers, SkyRenderers = skyRenderers, Rows = rows, RippleRms = rippleRms, Log = log, BodyColour = body,
                SkyInfo = new Vector4(1f, skyCube.width, 7f, 0f),
                Generator = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterRipples.shader")),
            };
            if (waveSpec != null)
            {
                Shader wavesShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterWaves.shader");
                log.Add("waves shader " + (wavesShader != null ? wavesShader.name + " supported=" + wavesShader.isSupported : "MISSING"));
                var spectrum = new WaterSpectrum(waveSpec[0], waveSpec[1], waveSpec[2], (uint)waveSpec[3]);
                scene.Waves = new WaterWaves(wavesShader);
                scene.Waves.Upload(spectrum);
                scene.Chop = (float)waveSpec[4];
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "spectrum: wind {0} m/s, fetch {1} km, heading {2} deg: {3} waves, peak {4:F1} m; choppiness {5}; tail {6:F3} of the texture's slopes",
                    waveSpec[0], waveSpec[1] / 1000.0, waveSpec[2], spectrum.WaveCount, spectrum.PeakWavelength, waveSpec[4], spectrum.TailXX + spectrum.TailZZ));
            }
            scene.FoamToday = foamToday;
            scene.SkyFor(new Shot(), 1f);
            if (seqs.Count > 0)
                Flicker(seqs, scene, taa, exposureDay, exposureNight, outDir);

            foreach (Shot shot in shots)
            {
                bool night = scene.Set(shot.View, shot.Strength);
                scene.SkyFor(shot, shot.Strength);
                scene.ShowFoam(shot.Mode);
                scene.Clock(shot.Seconds);

                camera.Render();
                RenderTexture.active = target;
                var read = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = null;

                float exposure = night ? exposureNight : exposureDay;
                Color[] px = read.GetPixels();
                var o = new Color[px.Length];
                for (int i = 0; i < px.Length; i++)
                    o[i] = new Color(WaterPreview.Tone(px[i].r * exposure), WaterPreview.Tone(px[i].g * exposure), WaterPreview.Tone(px[i].b * exposure), 1f);
                WaterPreview.WritePng(Path.Combine(outDir, shot.Name + ".png"), o, width, height);
                UnityEngine.Object.DestroyImmediate(read);
                log.Add("shot " + shot.Name + " ok");
            }
        }
        catch (Exception e)
        {
            log.Add("EXCEPTION " + e);
        }

        File.WriteAllLines(Path.Combine(dir, "foam-preview-output.txt"), log.ToArray());
    }

    /// <summary>
    /// The foam's flicker under temporal anti-aliasing: per run, the pixels the foam changes (a still
    /// with and without it, more than 2/255 apart), then the run twice -- with the foam and without --
    /// each frame's projection jittered (Halton 2, 3 over 8 frames, as Post-processing Stack v1) and the
    /// game's clocks running, resolved by PreviewTAA.shader; per pixel the mean |second difference in
    /// time| of its displayed brightness after the warm-up. Logged as the share of those pixels over
    /// 2/255 and over 8/255, with TAA and without; the last resolved frame and the flicker map as PNGs.
    /// </summary>
    private static void Flicker(List<Seq> seqs, Scene scene, float[] taa, float exposureDay, float exposureNight, string outDir)
    {
        int frames = (int)taa[0], warm = (int)taa[1];
        float fps = taa[2], spread = taa[3], blend = taa[4], sharpen = taa[5];
        RenderTexture target = scene.Target;
        int width = target.width, height = target.height, n = width * height;
        var resolve = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/PreviewTAA.shader"));
        RenderTexture resolved = WaterPreview.Target(width, height, false), history = WaterPreview.Target(width, height, false);
        var read = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        var lum = new float[3][];
        var raw = new float[3][];
        for (int k = 0; k < 3; k++)
        {
            lum[k] = new float[n];
            raw[k] = new float[n];
        }
        var flicker = new double[n];
        var flickerRaw = new double[n];
        scene.Log.Add("taa: " + frames + " frames (" + warm + " to warm up) at " + fps + " a second; jitter " + spread + ", blending " + blend + ", sharpen " + sharpen);

        foreach (Seq s in seqs)
        {
            bool night = scene.Set(s.View, s.Strength);
            float exposure = night ? exposureNight : exposureDay;

            // The foam's pixels: a still with it and one without.
            scene.Clock(s.Seconds);
            scene.Camera.ResetProjectionMatrix();
            var with = new float[n];
            var without = new float[n];
            scene.ShowFoam(true, false);
            scene.Camera.Render();
            WaterPreview.Brightness(target, read, with, exposure);
            scene.ShowFoam(false, false);
            scene.Camera.Render();
            WaterPreview.Brightness(target, read, without, exposure);
            var mask = new bool[n];
            int masked = 0;
            for (int p = 0; p < n; p++)
            {
                mask[p] = Math.Abs(with[p] - without[p]) > 2f / 255f;
                if (mask[p])
                    masked++;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                bool foam = pass == 0;
                scene.ShowFoam(foam, false);
                Array.Clear(flicker, 0, n);
                Array.Clear(flickerRaw, 0, n);
                int measured = 0;
                for (int f = 0; f < frames; f++)
                {
                    scene.Clock(s.Seconds + f / fps);
                    int i = f % 8;
                    float jx = (WaterPreview.Halton(i + 1, 2) - 0.5f) * spread, jy = (WaterPreview.Halton(i + 1, 3) - 0.5f) * spread;
                    scene.Camera.ResetProjectionMatrix();
                    Matrix4x4 projection = scene.Camera.projectionMatrix;
                    projection.m02 += 2f * jx / width;
                    projection.m12 += 2f * jy / height;
                    scene.Camera.projectionMatrix = projection;
                    scene.Camera.Render();

                    resolve.SetTexture("_HistoryTex", history);
                    resolve.SetVector("_Jitter", new Vector4(jx / width, jy / height, 0f, 0f));
                    resolve.SetVector("_TAA", new Vector4(sharpen, blend, f == 0 ? 1f : 0f, 0f));
                    Graphics.Blit(target, resolved, resolve, 0);
                    Graphics.Blit(resolved, history);

                    if (f >= warm - 2)
                    {
                        WaterPreview.Brightness(resolved, read, lum[f % 3], exposure);
                        WaterPreview.Brightness(target, read, raw[f % 3], exposure);
                        if (f >= warm)
                        {
                            float[] a = lum[f % 3], b = lum[(f + 2) % 3], c = lum[(f + 1) % 3];
                            float[] ra = raw[f % 3], rb = raw[(f + 2) % 3], rc = raw[(f + 1) % 3];
                            for (int p = 0; p < n; p++)
                            {
                                flicker[p] += Math.Abs(a[p] - 2f * b[p] + c[p]);
                                flickerRaw[p] += Math.Abs(ra[p] - 2f * rb[p] + rc[p]);
                            }
                            measured++;
                        }
                    }
                }
                scene.Camera.ResetProjectionMatrix();

                int over2 = 0, over8 = 0, rawOver2 = 0, rawOver8 = 0;
                double sum = 0.0, rawSum = 0.0;
                for (int p = 0; p < n; p++)
                {
                    if (!mask[p])
                        continue;
                    double v = flicker[p] / Math.Max(1, measured), rv = flickerRaw[p] / Math.Max(1, measured);
                    sum += v;
                    rawSum += rv;
                    if (v > 2.0 / 255.0) over2++;
                    if (v > 8.0 / 255.0) over8++;
                    if (rv > 2.0 / 255.0) rawOver2++;
                    if (rv > 8.0 / 255.0) rawOver8++;
                }
                double w = Math.Max(1, masked);
                scene.Log.Add(string.Format(CultureInfo.InvariantCulture,
                    "flicker {0} {1}: foam pixels {2:F1}% of the frame | TAA: mean {3:F2}/255, over 2/255 {4:F2}%, over 8/255 {5:F2}% | no TAA: mean {6:F2}/255, over 2/255 {7:F2}%, over 8/255 {8:F2}%",
                    s.Name, foam ? "with foam" : "without", 100.0 * masked / n, 255.0 * sum / w, 100.0 * over2 / w, 100.0 * over8 / w,
                    255.0 * rawSum / w, 100.0 * rawOver2 / w, 100.0 * rawOver8 / w));

                if (!foam)
                    continue;
                RenderTexture.active = resolved;
                read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = null;
                Color[] px = read.GetPixels();
                var picture = new Color[n];
                var map = new Color[n];
                for (int p = 0; p < n; p++)
                {
                    Color c = new Color(WaterPreview.Tone(px[p].r * exposure), WaterPreview.Tone(px[p].g * exposure), WaterPreview.Tone(px[p].b * exposure), 1f);
                    picture[p] = c;
                    float v = (float)(flicker[p] / Math.Max(1, measured));
                    map[p] = !mask[p] ? c * 0.33f : v > 8f / 255f ? new Color(1f, 0.15f, 0.1f, 1f) : new Color(Mathf.Min(1f, 25f * v), Mathf.Min(1f, 25f * v), Mathf.Min(1f, 25f * v), 1f);
                    map[p].a = 1f;
                }
                WaterPreview.WritePng(Path.Combine(outDir, s.Name + ".png"), picture, width, height);
                WaterPreview.WritePng(Path.Combine(outDir, s.Name + "-flicker.png"), map, width, height);
            }
        }
    }

    /// <summary>The foam's pattern texture, from the mod's own generator (FoamPattern.cs, synced).</summary>
    private static Texture2D Pattern()
    {
        byte[] rgba = FoamPattern.Generate();
        var t = new Texture2D(FoamPattern.Size, FoamPattern.Size, TextureFormat.RGBA32, true, true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
        };
        var px = new Color32[FoamPattern.Size * FoamPattern.Size];
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color32(rgba[4 * i], rgba[4 * i + 1], rgba[4 * i + 2], rgba[4 * i + 3]);
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }

    /// <summary>
    /// The coast's shape and water, as functions of world x, z. The sea at <see cref="SeaLevel"/> west
    /// of a waterline that wanders round x = 0, its bed rising from 14 m deep 500 m out to the beach
    /// (south of z = -450 steeply, 12 cm a metre: the game's coasts);
    /// the land rising to a plateau 6-10 m above the sea; the river in a 64 m channel along z = 210
    /// (meandering 40 m), its surface climbing 0.3% and 3 m at a rapid 380-440 m inland.
    /// </summary>
    private sealed class Coast
    {
        /// <summary>The tsunami's height over the sea (m; 0 none).</summary>
        public float Tsunami;

        /// <summary>
        /// The "tsunamiwall" views' tsunami (m): a wall higher than the camera, so that what lies behind
        /// its face -- its crest, its back, the sea beyond -- must not show through it.
        /// </summary>
        public const float Wall = 20f;

        /// <summary>
        /// The tsunami's front in the older frame, m from the waterline, and how far it runs a frame: a
        /// long wave's speed in 14 m of water, sqrt(g h) = 11.7 m/s, over a water frame (64 simulation
        /// frames: TerrainManager.HeightMap_sampleWaterHeightAndNormal, about a second at normal speed).
        /// </summary>
        public const float Front = -650f;

        public const float Step = 12.5f;

        public static float ShoreX(float z)
        {
            return 30f * Mathf.Sin(z / 140f) + 14f * Mathf.Sin(z / 53f + 1f);
        }

        public static float RiverZ(float x)
        {
            return 210f + 40f * Mathf.Sin(x / 260f);
        }

        public static float RiverSurface(float s)
        {
            s = Mathf.Max(s, 0f);
            return SeaLevel + 0.003f * s + 3f * Smooth(380f, 440f, s);
        }

        public static float Ground(float x, float z)
        {
            float s = x - ShoreX(z);
            float land;
            if (s < -500f)
                land = SeaLevel - 14f;
            else if (s < 0f)
            {
                // South of z = -450 the seabed drops as the game's coasts do (the author's city,
                // 2026-10-24: under 6% of its shore's water shallower than 2 m): 12 cm a metre.
                float gentle = SeaLevel - 14f + 14f * Mathf.Pow((s + 500f) / 500f, 1.6f);
                land = Mathf.Lerp(gentle, SeaLevel - Mathf.Min(14f, -0.12f * s), Smooth(-250f, -450f, z));
            }
            else
                land = SeaLevel + 4.5f * (1f - Mathf.Exp(-s / 60f)) + 4f * Smooth(250f, 600f, s);

            float dz = Mathf.Abs(z - RiverZ(x));
            if (s > -120f && dz < RiverHalf + 12f)
            {
                float across = Mathf.Clamp01(dz / RiverHalf);
                float bed = RiverSurface(s) - 0.3f - 4f * (1f - across * across);
                land = Mathf.Min(land, Mathf.Lerp(bed, land, Smooth(RiverHalf - 2f, RiverHalf + 12f, dz)));
            }

            return land;
        }

        /// <summary>The tsunami's height over the sea at s (m from the waterline) in a frame (0 the older).</summary>
        public float Eta(float s, int frame)
        {
            if (Tsunami <= 0f)
                return 0f;
            float front = Front + frame * Step;
            return Tsunami * (1f - Smooth(front - 40f, front + 10f, s)) * Smooth(front - 520f, front - 260f, s);
        }

        /// <summary>The water's surface at (x, z) in a frame (the ground itself where it is dry), and its flow (the game's units).</summary>
        public float Surface(float x, float z, int frame, out Vector2 flow)
        {
            float s = x - ShoreX(z);
            float ground = Ground(x, z);
            flow = Vector2.zero;
            float surface = ground;
            float eta = Eta(s, frame);
            float sea = SeaLevel + eta;
            if (ground < sea)
            {
                surface = sea;
                flow = new Vector2(Mathf.Min(0.45f * eta / 3f, 0.9f), 0f);
            }

            float dz = Mathf.Abs(z - RiverZ(x));
            if (s > -150f && dz < RiverHalf + 12f)
            {
                float across = Mathf.Clamp01(dz / RiverHalf);
                float river = RiverSurface(s);
                if (ground < river && river >= surface)
                {
                    surface = river;
                    float speed = (0.22f + 0.62f * Bump(370f, 450f, s)) * (1f - 0.7f * across * across);
                    float slope = (40f / 260f) * Mathf.Cos(x / 260f);
                    var along = new Vector2(-1f, -slope).normalized;
                    flow = along * speed;
                }
                else if (s < 0f && ground < sea)
                    flow += new Vector2(-0.25f * (1f + s / 150f) * (1f - across * across), 0f);
            }

            return surface;
        }

        /// <summary>The data as the game holds it, on every material drawn on the water's meshes.</summary>
        public void Bind(Material[] materials, List<string> log)
        {
            var heights = new Color32[WaterTexels * WaterTexels];
            for (int j = 0; j < WaterTexels; j++)
            {
                for (int i = 0; i < WaterTexels; i++)
                {
                    float x = -Half + (i + 0.5f) * (2f * Half / WaterTexels), z = -Half + (j + 0.5f) * (2f * Half / WaterTexels);
                    Vector2 flow;
                    float older = Surface(x, z, 0, out flow), newer = Surface(x, z, 1, out flow);
                    ushort a = Encode(older), b = Encode(newer);
                    heights[j * WaterTexels + i] = new Color32((byte)(a >> 8), (byte)(a & 255), (byte)(b >> 8), (byte)(b & 255));
                }
            }

            var ground = new Color32[TerrainTexels * TerrainTexels];
            for (int j = 0; j < TerrainTexels; j++)
            {
                for (int i = 0; i < TerrainTexels; i++)
                {
                    float x = -Half + (i + 0.5f) * (2f * Half / TerrainTexels), z = -Half + (j + 0.5f) * (2f * Half / TerrainTexels);
                    ushort g = Encode(Ground(x, z));
                    ground[j * TerrainTexels + i] = new Color32((byte)(g >> 8), (byte)(g & 255), (byte)(g >> 8), (byte)(g & 255));
                }
            }

            Texture2D heightMap = DataTexture(WaterTexels, heights, FilterMode.Point);
            Texture2D terrainMap = DataTexture(TerrainTexels, ground, FilterMode.Point);
            Texture2D[] surface = new Texture2D[4];
            for (int frame = 0; frame < 2; frame++)
            {
                Color32[] a, b;
                SurfaceMaps(frame, out a, out b);
                surface[frame] = DataTexture(SurfaceTexels, a, FilterMode.Bilinear);
                surface[2 + frame] = DataTexture(SurfaceTexels, b, FilterMode.Bilinear);
            }

            foreach (Material m in materials)
            {
                m.SetTexture("_MainTex", heightMap);
                m.SetTexture("_TerrainHeight", terrainMap);
                m.SetTexture("_SurfaceAOld", surface[0]);
                m.SetTexture("_SurfaceANew", surface[1]);
                m.SetTexture("_SurfaceBOld", surface[2]);
                m.SetTexture("_SurfaceBNew", surface[3]);
                m.SetVector("_HeightMapping", new Vector4(1f / WaterTexels, HeightScale, 1f / WaterTexels, 0f));
                m.SetVector("_TerrainMapping", new Vector4(1f / TerrainTexels, HeightScale, 1f / TerrainTexels, 0f));
                m.SetVector("_SurfaceMapping", new Vector4(0.5f, 0.5f, 1f / (2f * Half), 0f));
                m.SetVector("_TerrainStatus", new Vector4(0f, 0.5f, 0f, 0f));
            }

            log.Add(Tsunami > 0f ? "data: with a " + Tsunami + " m tsunami" : "data: without a tsunami");
        }

        /// <summary>
        /// The surface maps of a frame as WaterSimulation.FillSurfaceMap makes them: per 32 m texel a 1-2-1
        /// weighted 3 x 3 of points 16 m apart; A = (the surface's normal x, pollution 0, normal z),
        /// B = (flow x, depth / 16 m, flow z), each 128 + 127 v (the depth 255 v).
        /// </summary>
        private void SurfaceMaps(int frame, out Color32[] a, out Color32[] b)
        {
            a = new Color32[SurfaceTexels * SurfaceTexels];
            b = new Color32[SurfaceTexels * SurfaceTexels];
            float texel = 2f * Half / SurfaceTexels;
            for (int j = 0; j < SurfaceTexels; j++)
            {
                for (int i = 0; i < SurfaceTexels; i++)
                {
                    float cx = -Half + (i + 0.5f) * texel, cz = -Half + (j + 0.5f) * texel;
                    float sumW = 0f, depth = 0f, wetW = 0f;
                    Vector2 flow = Vector2.zero;
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        for (int di = -1; di <= 1; di++)
                        {
                            float wgt = (di == 0 ? 2f : 1f) * (dj == 0 ? 2f : 1f);
                            float x = cx + di * 16f, z = cz + dj * 16f;
                            Vector2 f;
                            float surface = Surface(x, z, frame, out f);
                            float d = surface - Ground(x, z);
                            sumW += wgt;
                            if (d > 0.01f)
                            {
                                depth += wgt * d;
                                flow += wgt * f;
                                wetW += wgt;
                            }
                        }
                    }

                    Vector2 dummy;
                    float hx0 = Surface(cx - 16f, cz, frame, out dummy), hx1 = Surface(cx + 16f, cz, frame, out dummy);
                    float hz0 = Surface(cx, cz - 16f, frame, out dummy), hz1 = Surface(cx, cz + 16f, frame, out dummy);
                    var n = new Vector3(-(hx1 - hx0) / 32f, 1f, -(hz1 - hz0) / 32f).normalized;
                    Vector2 meanFlow = wetW > 0f ? flow / wetW : Vector2.zero;
                    float meanDepth = depth / sumW;
                    int k = j * SurfaceTexels + i;
                    a[k] = new Color32(Byte(128f + 127f * n.x), 0, Byte(128f + 127f * n.z), 255);
                    b[k] = meanDepth > 0f
                        ? new Color32(Byte(128f + 127f * Mathf.Clamp(meanFlow.x, -1f, 1f)), Byte(255f * Mathf.Clamp01(meanDepth / 16f)), Byte(128f + 127f * Mathf.Clamp(meanFlow.y, -1f, 1f)), 255)
                        : new Color32(128, 0, 128, 255);
                }
            }
        }
    }

    private static ushort Encode(float height)
    {
        return (ushort)Mathf.Clamp(Mathf.RoundToInt(height / HeightScale * 65536f), 0, 65535);
    }

    private static byte Byte(float v)
    {
        return (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    private static float Bump(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return 4f * t * (1f - t);
    }

    private static Texture2D DataTexture(int size, Color32[] px, FilterMode filter)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = filter,
        };
        t.SetPixels32(px);
        t.Apply(false);
        return t;
    }

    /// <summary>
    /// The terrain (deferred, so it is in the depth texture as the game's is) and the water's meshes:
    /// 16 tiles of 128 x 128 quads, 8 m apart, over the 4 km square. The water's are flat: the vertex
    /// program puts each vertex at the simulated surface, as the game's does.
    /// </summary>
    private static void BuildScene(Material water, Material foam, Material sky, List<Renderer> waterRenderers, List<Renderer> foamRenderers,
                                   List<Renderer> skyRenderers)
    {
        var colours = new Color[TerrainTexels * TerrainTexels];
        for (int j = 0; j < TerrainTexels; j++)
        {
            for (int i = 0; i < TerrainTexels; i++)
            {
                float x = -Half + (i + 0.5f) * (2f * Half / TerrainTexels), z = -Half + (j + 0.5f) * (2f * Half / TerrainTexels);
                float y = Coast.Ground(x, z) - SeaLevel;
                Color sand = new Color(0.62f, 0.56f, 0.44f), wetSand = new Color(0.36f, 0.33f, 0.27f), grass = new Color(0.27f, 0.33f, 0.18f);
                Color bed = new Color(0.22f, 0.24f, 0.22f);
                Color c = y < -0.3f ? Color.Lerp(wetSand, bed, Mathf.Clamp01(-y / 6f)) : y < 2.5f ? Color.Lerp(wetSand, sand, Mathf.Clamp01((y + 0.3f) / 1.2f)) : Color.Lerp(sand, grass, Mathf.Clamp01((y - 2.5f) / 2f));
                colours[j * TerrainTexels + i] = c;
            }
        }

        var groundTexture = new Texture2D(TerrainTexels, TerrainTexels, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        groundTexture.SetPixels(colours);
        groundTexture.Apply(true);
        var groundMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = groundTexture };

        const int quads = 128;
        const float spacing = 8f;
        for (int tj = 0; tj < 4; tj++)
        {
            for (int ti = 0; ti < 4; ti++)
            {
                float x0 = -Half + ti * quads * spacing, z0 = -Half + tj * quads * spacing;
                Mesh groundMesh = Grid(x0, z0, quads, spacing, true);
                var g = new GameObject("ground " + ti + "," + tj);
                g.AddComponent<MeshFilter>().sharedMesh = groundMesh;
                g.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;

                Mesh waterMesh = Grid(x0, z0, quads, spacing, false);
                var wgo = new GameObject("water " + ti + "," + tj);
                wgo.AddComponent<MeshFilter>().sharedMesh = waterMesh;
                MeshRenderer wr = wgo.AddComponent<MeshRenderer>();
                wr.sharedMaterial = water;
                waterRenderers.Add(wr);

                var fgo = new GameObject("foam " + ti + "," + tj);
                fgo.AddComponent<MeshFilter>().sharedMesh = waterMesh;
                MeshRenderer fr = fgo.AddComponent<MeshRenderer>();
                fr.sharedMaterial = foam;
                fr.shadowCastingMode = ShadowCastingMode.Off;
                foamRenderers.Add(fr);

                var sgo = new GameObject("sky " + ti + "," + tj);
                sgo.AddComponent<MeshFilter>().sharedMesh = waterMesh;
                MeshRenderer sr = sgo.AddComponent<MeshRenderer>();
                sr.sharedMaterial = sky;
                sr.shadowCastingMode = ShadowCastingMode.Off;
                sr.receiveShadows = false;
                skyRenderers.Add(sr);
            }
        }
    }

    /// <summary>"#RRGGBB" as a colour (sRGB); null for "-" or anything unreadable.</summary>
    private static Color? Hex(string text)
    {
        Color c;
        return text != "-" && ColorUtility.TryParseHtmlString(text, out c) ? c : (Color?)null;
    }

    private static Mesh Grid(float x0, float z0, int quads, float spacing, bool ground)
    {
        int n = quads + 1;
        var vertices = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var uvs = new Vector2[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                float x = x0 + i * spacing, z = z0 + j * spacing;
                float y = ground ? Coast.Ground(x, z) : 0f;
                vertices[j * n + i] = new Vector3(x, y, z);
                if (ground)
                {
                    float dx = Coast.Ground(x + 2f, z) - Coast.Ground(x - 2f, z), dz = Coast.Ground(x, z + 2f) - Coast.Ground(x, z - 2f);
                    normals[j * n + i] = new Vector3(-dx / 4f, 1f, -dz / 4f).normalized;
                }
                else
                    normals[j * n + i] = Vector3.up;
                uvs[j * n + i] = new Vector2(x / (2f * Half) + 0.5f, z / (2f * Half) + 0.5f);
            }
        }

        var triangles = new int[quads * quads * 6];
        int t = 0;
        for (int j = 0; j < quads; j++)
        {
            for (int i = 0; i < quads; i++)
            {
                int a = j * n + i;
                triangles[t++] = a;
                triangles[t++] = a + n;
                triangles[t++] = a + 1;
                triangles[t++] = a + 1;
                triangles[t++] = a + n;
                triangles[t++] = a + n + 1;
            }
        }

        var mesh = new Mesh { vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
        float size = quads * spacing;
        // The water's vertices move to the surface in the vertex program: bounds that hold any height.
        mesh.bounds = new Bounds(new Vector3(x0 + size / 2f, 100f, z0 + size / 2f), new Vector3(size, 400f, size));
        return mesh;
    }

    private sealed class Tower
    {
        public Renderer Renderer;
        public Material Day, Night;
    }

    /// <summary>A town of window-bay towers on the land behind the beach (WaterPreview's facades).</summary>
    private static List<Tower> BuildTowers()
    {
        var towers = new List<Tower>();
        var random = new System.Random(11);
        Texture2D day = WaterPreview.Facade(false, random), night = WaterPreview.Facade(true, random);
        Texture2D dayIllum = WaterPreview.Illum(day), nightIllum = WaterPreview.Illum(night);
        Color[] facades = { new Color(0.62f, 0.58f, 0.52f), new Color(0.45f, 0.50f, 0.56f), new Color(0.70f, 0.66f, 0.60f), new Color(0.38f, 0.36f, 0.34f), new Color(0.55f, 0.60f, 0.62f) };
        float[,] spec =
        {
            // x, z, width, height
            { 330f, -420f, 40f, 70f }, { 360f, -260f, 34f, 110f }, { 340f, -90f, 44f, 60f }, { 350f, 40f, 36f, 140f },
            { 340f, 380f, 46f, 90f }, { 360f, 520f, 38f, 120f }, { 520f, -360f, 50f, 190f }, { 540f, -140f, 44f, 230f },
            { 560f, 420f, 52f, 200f }, { 760f, -260f, 56f, 280f }, { 740f, 40f, 48f, 250f }, { 780f, 520f, 54f, 220f },
            { 1000f, -120f, 64f, 320f }, { 1050f, 420f, 60f, 270f },
        };
        for (int i = 0; i < spec.GetLength(0); i++)
        {
            float x = spec[i, 0], z = spec[i, 1], width = spec[i, 2], height = spec[i, 3];
            float baseY = Coast.Ground(x, z) - 2f;
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.localScale = new Vector3(width, height, width);
            box.transform.position = new Vector3(x, baseY + height / 2f, z);
            var scale = new Vector2(width / 32f, height / 32f);
            var offset = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
            var dayMaterial = new Material(Shader.Find("Legacy Shaders/Self-Illumin/Diffuse")) { color = facades[i % facades.Length], mainTexture = day, mainTextureScale = scale, mainTextureOffset = offset };
            dayMaterial.SetTexture("_Illum", dayIllum);
            dayMaterial.SetTextureScale("_Illum", scale);
            dayMaterial.SetTextureOffset("_Illum", offset);
            var nightMaterial = new Material(Shader.Find("Legacy Shaders/Self-Illumin/Diffuse")) { color = Color.white, mainTexture = night, mainTextureScale = scale, mainTextureOffset = offset };
            nightMaterial.SetTexture("_Illum", nightIllum);
            nightMaterial.SetTextureScale("_Illum", scale);
            nightMaterial.SetTextureOffset("_Illum", offset);
            Renderer renderer = box.GetComponent<Renderer>();
            renderer.sharedMaterial = dayMaterial;
            towers.Add(new Tower { Renderer = renderer, Day = dayMaterial, Night = nightMaterial });
        }

        return towers;
    }

    /// <summary>The light, sky and camera of a view; true at night.</summary>
    private static bool SetView(string view, GameObject cameraObject, Light sun, Material skybox, Camera capture, RenderTexture skyCube, Material water, List<Tower> towers)
    {
        bool night = view.StartsWith("night", StringComparison.Ordinal);
        // "glitter" views: the sunset's sun, the camera far out at sea looking into it (web-ocean's);
        // "glitterday": a sun 30 deg up the same way, under a city camera's usual pitch.
        bool glitter = view.StartsWith("glitter", StringComparison.Ordinal);
        bool glitterDay = view == "glitterday";
        bool sunset = view.StartsWith("sunset", StringComparison.Ordinal) || (glitter && !glitterDay);
        foreach (Tower t in towers)
            t.Renderer.sharedMaterial = night ? t.Night : t.Day;

        // Day: the sun high behind the camera's left; sunset: low over the land ahead; night: a dim moon.
        Vector3 toSun = sunset ? new Vector3(0.979f, 0.105f, -0.173f).normalized : night ? new Vector3(-0.3f, 0.6f, 0.5f).normalized
            : glitterDay ? new Vector3(0.853f, 0.5f, -0.150f).normalized : new Vector3(-0.55f, 0.45f, 0.7f).normalized;
        sun.transform.rotation = Quaternion.LookRotation(-toSun);
        sun.intensity = night ? 0.12f : sunset ? 1.6f : 1.3f;
        sun.color = night ? new Color(0.6f, 0.7f, 1f) : sunset ? new Color(1f, 0.62f, 0.38f) : new Color(1f, 0.95f, 0.88f);
        skybox.SetFloat("_Exposure", night ? 0.04f : 1.1f);
        skybox.SetFloat("_AtmosphereThickness", 1f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        Color ambient = night ? new Color(0.010f, 0.012f, 0.020f) : sunset ? new Color(0.30f, 0.30f, 0.38f) : new Color(0.45f, 0.55f, 0.70f);
        RenderSettings.ambientSkyColor = ambient;
        RenderSettings.ambientEquatorColor = night ? new Color(0.008f, 0.009f, 0.012f) : new Color(0.40f, 0.42f, 0.45f);
        RenderSettings.ambientGroundColor = night ? new Color(0.004f, 0.004f, 0.005f) : new Color(0.20f, 0.20f, 0.20f);
        capture.RenderToCubemap(skyCube);
        water.SetVector("_PreviewSun", new Vector4(toSun.x, toSun.y, toSun.z, sun.intensity));
        water.SetColor("_PreviewSunColour", sun.color.linear);
        water.SetColor("_PreviewAmbient", ambient.linear);

        // The views: the coast from a city camera's usual height, close over the beach, high above,
        // over the river's rapid, and at sea before the tsunami's front (sunset and night: the coast).
        Vector3 eye, at;
        if (glitter)
        {
            // 30 m (or 300 m) over the sea at its far edge, facing the sun's way (10 deg south of
            // east), the horizon a little above the middle (or the sea filling most of the frame).
            bool high = view.EndsWith("300", StringComparison.Ordinal);
            float pitch = (glitterDay ? 35f : high ? 11f : 2.5f) * Mathf.Deg2Rad, yaw = -10f * Mathf.Deg2Rad;
            eye = new Vector3(-1950f, SeaLevel + (glitterDay ? 400f : high ? 300f : 30f), 150f);
            at = eye + new Vector3(Mathf.Cos(pitch) * Mathf.Cos(yaw), -Mathf.Sin(pitch), Mathf.Cos(pitch) * Mathf.Sin(yaw)) * 1000f;
        }
        else if (view == "tsunamiwall")
        {
            // 9 m over the sea, 100 m before the wall's face, looking out to sea: the face fills the
            // view's middle and hides all behind it.
            eye = new Vector3(-570f, SeaLevel + 9f, 20f);
            at = new Vector3(-800f, SeaLevel + 16f, 70f);
        }
        else if (view == "tsunamiwallcity")
        {
            // A city camera 70 m up, 400 m off: the face, its crest, the sea beyond over the crest.
            eye = new Vector3(-420f, SeaLevel + 70f, -160f);
            at = new Vector3(-760f, SeaLevel + 8f, 60f);
        }
        else if (view == "photo")
        {
            // The layered foam: his reference picture's view -- the surf seen from 14 m up, nearly
            // straight down over the shore band (the waterline at x = 12).
            eye = new Vector3(-6f, SeaLevel + 14f, 0f);
            at = new Vector3(-3.5f, SeaLevel, 3f);
        }
        else if (view == "beach")
        {
            // The surf from a city camera 90 m up, the beach across the frame.
            eye = new Vector3(-210f, SeaLevel + 90f, -130f);
            at = new Vector3(0f, SeaLevel, 30f);
        }
        else if (view == "steep")
        {
            // The steep coast (as the game's) from a city camera 90 m up; its waterline near x = 38.
            eye = new Vector3(-170f, SeaLevel + 90f, -760f);
            at = new Vector3(30f, SeaLevel, -580f);
        }
        else if (view == "steepclose")
        {
            // The steep coast from 30 m up, close.
            eye = new Vector3(-40f, SeaLevel + 30f, -650f);
            at = new Vector3(25f, SeaLevel, -590f);
        }
        else if (view == "steepdown")
        {
            // The steep coast from 120 m up, looking well down, out over the sea beyond the surf (his
            // picture of streaks of foam out past the big foam, 2026-10-24).
            eye = new Vector3(-60f, SeaLevel + 120f, -560f);
            at = new Vector3(-60f, SeaLevel, -640f);
        }
        else if (view == "steephigh")
        {
            // The steep coast from 300 m up, the usual city view of a shoreline.
            eye = new Vector3(-520f, SeaLevel + 300f, -980f);
            at = new Vector3(20f, SeaLevel, -560f);
        }
        else if (view == "tsunamiclose")
        {
            eye = new Vector3(-940f, 150f, -120f);
            at = new Vector3(-640f, SeaLevel, 40f);
        }
        else if (view.EndsWith("close", StringComparison.Ordinal))
        {
            eye = new Vector3(-110f, 75f, 40f);
            at = new Vector3(25f, SeaLevel, 95f);
        }
        else if (view.EndsWith("high", StringComparison.Ordinal))
        {
            eye = new Vector3(-1300f, 700f, -700f);
            at = new Vector3(100f, SeaLevel, 150f);
        }
        else if (view.EndsWith("river", StringComparison.Ordinal))
        {
            eye = new Vector3(190f, 110f, 60f);
            at = new Vector3(430f, SeaLevel + 3f, 215f);
        }
        else if (view.StartsWith("tsunami", StringComparison.Ordinal))
        {
            eye = new Vector3(-1250f, 180f, -450f);
            at = new Vector3(-560f, SeaLevel, 100f);
        }
        else
        {
            eye = new Vector3(-650f, 150f, -250f);
            at = new Vector3(0f, SeaLevel, 120f);
        }

        cameraObject.transform.position = eye;
        cameraObject.transform.rotation = Quaternion.LookRotation(at - eye);
        return night;
    }
}
