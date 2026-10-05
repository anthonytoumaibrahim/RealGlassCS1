using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Pictures of the Water tab's city reflection (RealGlass/CityReflection) on this machine's GPU, for
/// the look -- tools\preview\water-render.ps1 writes water-preview-input.txt (the mod's numbers: the
/// reflectance table, the waves' rows, the streaks' sigma, the shots) and runs
///   Unity.exe -batchmode -quit -projectPath . -executeMethod WaterPreview.Render
/// A harbour: a plane of water at y = 0 and towers of window bays standing past it; the camera in
/// legacy deferred HDR like the game's, the reflection drawn by one command buffer at
/// AfterImageEffectsOpaque exactly as WaterCityReflection.Record records it. By day the towers are
/// lit by a sun, by night only their lit windows glow. Each shot is one PNG (tonemapped, sRGB).
/// </summary>
public static class WaterPreview
{
    private sealed class Shot
    {
        public string Name;
        public string View;
        public float Ripples;
        public float Seconds;
        public float Strength;
    }

    /// <summary>
    /// A run of frames for the flicker: the camera's projection jittered as temporal anti-aliasing
    /// jitters it, the game's clocks running (or frozen), each frame resolved by PreviewTAA.shader.
    /// CityGain / Footprint: the reflection's _RGWaterWave. FadeStart / FadeEnd above 0: the ripple
    /// texture's mip levels drawn one by one, each holding only the waves of FadeStart..FadeEnd texels
    /// or more; 0: the base level drawn, the GPU averaging its mips (as the mod does today).
    /// </summary>
    private sealed class Seq
    {
        public string Name;
        public string View;
        public float Ripples;
        public float Seconds;
        public float Strength;
        public bool Frozen;
        public float CityGain;
        public float Footprint;
        public float FadeStart;
        public float FadeEnd;
        public float Jitter;
        public float Cone;
    }

    public static void Render()
    {
        string dir = Directory.GetCurrentDirectory();
        var log = new List<string>();
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/CityReflection.shader");
            log.Add("shader " + (shader != null ? shader.name + " supported=" + shader.isSupported : "MISSING"));
            var material = new Material(shader);

            var table = new Vector4[33];
            var rows = new Vector4[64];
            var shots = new List<Shot>();
            var seqs = new List<Seq>();
            string outDir = dir;
            int width = 960, height = 540;
            float streak = 0f, length = 1f, exposureDay = 1f, exposureNight = 1f, rippleRms = 0.3f, cityGain = 0f;
            var taa = new float[] { 64f, 32f, 60f, 1f, 0.94f, 0.15f };
            foreach (string raw in File.ReadAllLines(Path.Combine(dir, "water-preview-input.txt")))
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
                    table[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Vector4(F(p[2]), 0f, 0f, 0f);
                else if (p[0] == "ripple")
                    rows[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Vector4(F(p[2]), F(p[3]), F(p[4]), F(p[5]));
                else if (p[0] == "ripplerms")
                    rippleRms = F(p[1]);
                else if (p[0] == "citygain")
                    cityGain = F(p[1]);
                else if (p[0] == "streak")
                    streak = F(p[1]);
                else if (p[0] == "length")
                    length = F(p[1]);
                else if (p[0] == "exposure")
                {
                    exposureDay = F(p[1]);
                    exposureNight = F(p[2]);
                }
                else if (p[0] == "shot")
                    shots.Add(new Shot { Name = p[1], View = p[2], Ripples = F(p[3]), Seconds = F(p[4]), Strength = F(p[5]) });
                else if (p[0] == "taa")
                {
                    // frames, warm-up frames, frames a second, jitter spread, stationary blending, sharpen
                    for (int k = 0; k < taa.Length; k++)
                        taa[k] = F(p[k + 1]);
                }
                else if (p[0] == "seq")
                    seqs.Add(new Seq
                    {
                        Name = p[1], View = p[2], Ripples = F(p[3]), Seconds = F(p[4]), Strength = F(p[5]), Frozen = p[6] == "1",
                        CityGain = F(p[7]), Footprint = F(p[8]), FadeStart = F(p[9]), FadeEnd = F(p[10]), Jitter = F(p[11]), Cone = p.Length > 12 ? F(p[12]) : 0f,
                    });
            }

            Shader.SetGlobalVectorArray("_RGSeaTable", table);
            material.SetTexture("_RGWaterTheme", Cube(Color.black));

            // The game's water, stood in for (PreviewWater.shader), with our ripples on it.
            var water = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/PreviewWater.shader"));
            water.SetColor("_BodyColour", new Color(0.03f, 0.06f, 0.08f, 1f));
            var city = BuildCity(water);

            var skybox = new Material(Shader.Find("Skybox/Procedural"));
            RenderSettings.skybox = skybox;
            var sunObject = new GameObject("preview sun");
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;

            // The sky the water reflects: the skybox in a cube, as the mod's own cube holds it.
            var skyCube = new RenderTexture(128, 128, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
            };
            skyCube.Create();
            var captureObject = new GameObject("preview sky capture");
            Camera capture = captureObject.AddComponent<Camera>();
            capture.enabled = false;
            capture.clearFlags = CameraClearFlags.Skybox;
            capture.cullingMask = 0;
            capture.allowHDR = true;
            capture.renderingPath = RenderingPath.Forward;
            water.SetTexture("_Sky", skyCube);
            RenderTexture ripples = null;

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

            // As WaterCityReflection.Record.
            var mesh = UnitCube();
            int w = Mathf.Max(1, width / 2), h = Mathf.Max(1, height / 2);
            RenderTexture frame = Target(w, h, true), trace = Target(w, h, true), streaked = Target(w, h, false);
            material.SetTexture("_RGWaterFrame", frame);
            var buffer = new CommandBuffer { name = "RealGlass water reflection (preview)" };
            buffer.Blit(BuiltinRenderTextureType.CameraTarget, frame);
            buffer.SetRenderTarget(trace);
            buffer.ClearRenderTarget(false, true, Color.clear);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            buffer.Blit(trace, streaked, material, 1);
            buffer.SetGlobalTexture("_RGWaterStreaked", streaked);
            buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 2);
            camera.AddCommandBuffer(CameraEvent.AfterImageEffectsOpaque, buffer);

            material.SetVector("_RGWaterMarch", new Vector4(8000f, 30f, 0.05f, Mathf.Max(1f, length)));
            material.SetVector("_RGWaterStreak", new Vector4(streak, 1f / h, 0f, 0f));

            var stage = new Stage
            {
                City = city, SunObject = sunObject, Sun = sun, Skybox = skybox, Capture = capture, SkyCube = skyCube, Water = water,
                CameraObject = cameraObject,
            };
            if (seqs.Count > 0)
                Sequences(seqs, stage, camera, target, trace, material, rows, rippleRms, taa, exposureDay, exposureNight, outDir, log);

            foreach (Shot shot in shots)
            {
                bool night = stage.Set(shot.View);

                // The game's clocks at this shot's time, then the ripples drawn for it (as WaterRipples:
                // root-mean-square slope = half the game texture's decoded rms x the strength).
                Shader.SetGlobalVector("_SimulationTime", new Vector4(shot.Seconds % 60f, (shot.Seconds % 60f) / 60f, 0f, 0f));
                Shader.SetGlobalVector("_SimulationTime2", new Vector4(shot.Seconds, shot.Seconds / 3600f, 0f, 0f));
                if (ripples != null)
                    ripples.Release();
                ripples = CityReflectionTest.GenerateRipples(rows, 0.5f * rippleRms * shot.Ripples, 1024);
                water.SetTexture("_WaterNormal", ripples);
                material.SetTexture("_RGRipples", ripples);

                material.SetVector("_RGWaterGeom", new Vector4(0f, shot.Strength, 0f, 0f));
                material.SetVector("_RGWaterWave", new Vector4(cityGain, 0f, 0f, 0f));
                material.SetVector("_RGWaterLight", new Vector4(1f, 1f, 1f, 0f));

                camera.Render();
                RenderTexture.active = target;
                var read = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = null;

                float exposure = night ? exposureNight : exposureDay;
                Color[] px = read.GetPixels();
                var png = new Texture2D(width, height, TextureFormat.RGB24, false);
                var o = new Color[px.Length];
                for (int i = 0; i < px.Length; i++)
                    o[i] = new Color(Tone(px[i].r * exposure), Tone(px[i].g * exposure), Tone(px[i].b * exposure), 1f);
                png.SetPixels(o);
                png.Apply(false);
                File.WriteAllBytes(Path.Combine(outDir, shot.Name + ".png"), png.EncodeToPNG());
                log.Add("shot " + shot.Name + " ok");
            }
        }
        catch (Exception e)
        {
            log.Add("EXCEPTION " + e);
        }

        File.WriteAllLines(Path.Combine(dir, "water-preview-output.txt"), log.ToArray());
    }

    private sealed class CityTower
    {
        public Renderer Renderer;
        public Material Day, Night;
    }

    /// <summary>The harbour's light, sky and camera for a view.</summary>
    private sealed class Stage
    {
        public List<CityTower> City;
        public GameObject SunObject;
        public Light Sun;
        public Material Skybox;
        public Camera Capture;
        public RenderTexture SkyCube;
        public Material Water;
        public GameObject CameraObject;

        /// <summary>True at night.</summary>
        public bool Set(string view)
        {
            bool night = view.StartsWith("night", StringComparison.Ordinal);
            bool sunset = view.StartsWith("sunset", StringComparison.Ordinal);
            foreach (CityTower t in City)
                t.Renderer.sharedMaterial = night ? t.Night : t.Day;

            // Day: the sun high behind and to the left; sunset: low, ahead of the camera (its glitter
            // on the water between camera and towers); night: none.
            Vector3 toSun = sunset ? new Vector3(0.979f, 0.105f, -0.173f).normalized : new Vector3(-0.55f, 0.45f, 0.7f).normalized;
            SunObject.transform.rotation = Quaternion.LookRotation(-toSun);
            Sun.intensity = night ? 0f : sunset ? 1.6f : 1.3f;
            Sun.color = sunset ? new Color(1f, 0.62f, 0.38f) : new Color(1f, 0.95f, 0.88f);
            Skybox.SetFloat("_Exposure", night ? 0.04f : 1.1f);
            Skybox.SetFloat("_AtmosphereThickness", 1f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            Color ambient = night ? new Color(0.010f, 0.012f, 0.020f) : sunset ? new Color(0.30f, 0.30f, 0.38f) : new Color(0.45f, 0.55f, 0.70f);
            RenderSettings.ambientSkyColor = ambient;
            RenderSettings.ambientEquatorColor = night ? new Color(0.008f, 0.009f, 0.012f) : new Color(0.40f, 0.42f, 0.45f);
            RenderSettings.ambientGroundColor = night ? new Color(0.004f, 0.004f, 0.005f) : new Color(0.20f, 0.20f, 0.20f);
            Capture.RenderToCubemap(SkyCube);
            Water.SetVector("_PreviewSun", new Vector4(toSun.x, toSun.y, toSun.z, Sun.intensity));
            Water.SetColor("_PreviewSunColour", Sun.color.linear);
            Water.SetColor("_PreviewAmbient", ambient.linear);

            // The views: a camera low over the harbour, one close over the water, one higher up;
            // sunset and night from the low one (nightclose: night from the close one).
            Vector3 eye;
            float pitch;
            if (view.EndsWith("high", StringComparison.Ordinal))
            {
                eye = new Vector3(-200f, 260f, 60f);
                pitch = 18f;
            }
            else if (view.EndsWith("close", StringComparison.Ordinal))
            {
                eye = new Vector3(0f, 14f, 40f);
                pitch = 22f;
            }
            else
            {
                eye = new Vector3(0f, 45f, 40f);
                pitch = 4f;
            }
            CameraObject.transform.position = eye;
            CameraObject.transform.rotation = Quaternion.Euler(pitch, 90f, 0f);
            return night;
        }
    }

    /// <summary>
    /// The flicker: each run's frames rendered with the projection jittered (Halton 2, 3 over 8
    /// frames, as Post-processing Stack v1's temporal anti-aliasing) and the clocks running, resolved
    /// by PreviewTAA.shader; after the warm-up, each water pixel's mean |second difference in time|
    /// of its displayed brightness (0..1) -- steady motion has none, a pixel that jumps back and forth
    /// a lot. Logged per run as the share of the water over 2/255 and over 8/255, with TAA and without
    /// (the raw frames); the last resolved frame and the flicker map written as PNGs.
    /// </summary>
    private static void Sequences(List<Seq> seqs, Stage stage, Camera camera, RenderTexture target, RenderTexture trace, Material material,
                                  Vector4[] rows, float rippleRms, float[] taa, float exposureDay, float exposureNight, string outDir, List<string> log)
    {
        int frames = (int)taa[0], warm = (int)taa[1];
        float fps = taa[2], spread = taa[3], blend = taa[4], sharpen = taa[5];
        int width = target.width, height = target.height, n = width * height;
        var resolve = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/PreviewTAA.shader"));
        var generator = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterRipples.shader"));
        generator.SetVectorArray("_RGRippleWaves", rows);
        RenderTexture boxRipples = RippleTarget(1024, true), bandRipples = RippleTarget(1024, false);
        RenderTexture resolved = Target(width, height, false), history = Target(width, height, false);
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
        log.Add("taa: " + frames + " frames (" + warm + " to warm up) at " + fps + " a second; jitter " + spread + ", blending " + blend + ", sharpen " + sharpen);

        foreach (Seq s in seqs)
        {
            bool night = stage.Set(s.View);
            float exposure = night ? exposureNight : exposureDay;
            material.SetVector("_RGWaterGeom", new Vector4(0f, s.Strength, 0f, 0f));
            material.SetVector("_RGWaterWave", new Vector4(s.CityGain, s.Footprint, s.Cone, 0f));
            material.SetVector("_RGWaterLight", new Vector4(1f, 1f, 1f, 0f));
            RenderTexture ripples = s.FadeEnd > 0f ? bandRipples : boxRipples;
            stage.Water.SetTexture("_WaterNormal", ripples);
            material.SetTexture("_RGRipples", ripples);
            Array.Clear(flicker, 0, n);
            Array.Clear(flickerRaw, 0, n);
            bool[] mask = null;
            int measured = 0;

            for (int f = 0; f < frames; f++)
            {
                float t = s.Seconds + (s.Frozen ? 0f : f / fps);
                Shader.SetGlobalVector("_SimulationTime", new Vector4(t % 60f, (t % 60f) / 60f, 0f, 0f));
                Shader.SetGlobalVector("_SimulationTime2", new Vector4(t, t / 3600f, 0f, 0f));
                DrawRipples(generator, ripples, 0.5f * rippleRms * s.Ripples, s.FadeStart, s.FadeEnd);

                int i = f % 8;
                float jx = (Halton(i + 1, 2) - 0.5f) * spread * s.Jitter, jy = (Halton(i + 1, 3) - 0.5f) * spread * s.Jitter;
                camera.ResetProjectionMatrix();
                Matrix4x4 projection = camera.projectionMatrix;
                projection.m02 += 2f * jx / width;
                projection.m12 += 2f * jy / height;
                camera.projectionMatrix = projection;
                camera.Render();

                resolve.SetTexture("_HistoryTex", history);
                resolve.SetVector("_Jitter", new Vector4(jx / width, jy / height, 0f, 0f));
                resolve.SetVector("_TAA", new Vector4(sharpen, blend, f == 0 ? 1f : 0f, 0f));
                Graphics.Blit(target, resolved, resolve, 0);
                Graphics.Blit(resolved, history);

                if (mask == null)
                    mask = WaterMask(trace, width, height);
                if (f >= warm - 2)
                {
                    Brightness(resolved, read, lum[f % 3], exposure);
                    Brightness(target, read, raw[f % 3], exposure);
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
            camera.ResetProjectionMatrix();

            int water = 0, over2 = 0, over8 = 0, rawOver2 = 0, rawOver8 = 0;
            double sum = 0.0, rawSum = 0.0, bright = 0.0;
            float[] last = lum[(frames - 1) % 3];
            for (int p = 0; p < n; p++)
            {
                if (!mask[p])
                    continue;
                water++;
                double v = flicker[p] / Math.Max(1, measured), rv = flickerRaw[p] / Math.Max(1, measured);
                sum += v;
                rawSum += rv;
                bright += last[p];
                if (v > 2.0 / 255.0) over2++;
                if (v > 8.0 / 255.0) over8++;
                if (rv > 2.0 / 255.0) rawOver2++;
                if (rv > 8.0 / 255.0) rawOver8++;
            }
            double w = Math.Max(1, water);
            log.Add(string.Format(CultureInfo.InvariantCulture,
                "flicker {0}: water {1:F1}% of the frame, brightness {2:F3} | TAA: mean {3:F2}/255, over 2/255 {4:F2}%, over 8/255 {5:F2}% | no TAA: mean {6:F2}/255, over 2/255 {7:F2}%, over 8/255 {8:F2}%",
                s.Name, 100.0 * water / n, bright / w, 255.0 * sum / w, 100.0 * over2 / w, 100.0 * over8 / w, 255.0 * rawSum / w, 100.0 * rawOver2 / w, 100.0 * rawOver8 / w));

            // The last resolved frame, and the flicker map: grey = the mean second difference x 25 on the
            // water (red over 8/255), the frame at a third elsewhere.
            RenderTexture.active = resolved;
            read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = null;
            Color[] px = read.GetPixels();
            var picture = new Color[n];
            var map = new Color[n];
            for (int p = 0; p < n; p++)
            {
                Color c = new Color(Tone(px[p].r * exposure), Tone(px[p].g * exposure), Tone(px[p].b * exposure), 1f);
                picture[p] = c;
                float v = (float)(flicker[p] / Math.Max(1, measured));
                map[p] = !mask[p] ? c * 0.33f : v > 8f / 255f ? new Color(1f, 0.15f, 0.1f, 1f) : new Color(Mathf.Min(1f, 25f * v), Mathf.Min(1f, 25f * v), Mathf.Min(1f, 25f * v), 1f);
                map[p].a = 1f;
            }
            WritePng(Path.Combine(outDir, s.Name + ".png"), picture, width, height);
            WritePng(Path.Combine(outDir, s.Name + "-flicker.png"), map, width, height);
        }
    }

    private static void WritePng(string path, Color[] px, int width, int height)
    {
        var png = new Texture2D(width, height, TextureFormat.RGB24, false);
        png.SetPixels(px);
        png.Apply(false);
        File.WriteAllBytes(path, png.EncodeToPNG());
    }

    /// <summary>Each pixel's displayed brightness (0..1): its luminance x the exposure, tonemapped as the PNGs.</summary>
    private static void Brightness(RenderTexture rt, Texture2D read, float[] into, float exposure)
    {
        RenderTexture.active = rt;
        read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = null;
        Color[] px = read.GetPixels();
        for (int p = 0; p < px.Length; p++)
            into[p] = Tone((0.2126f * px[p].r + 0.7152f * px[p].g + 0.0722f * px[p].b) * exposure);
    }

    /// <summary>The water: the trace's alpha (1 on the map's water), at the full size.</summary>
    private static bool[] WaterMask(RenderTexture trace, int width, int height)
    {
        int w = trace.width, h = trace.height;
        var read = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
        RenderTexture.active = trace;
        read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        RenderTexture.active = null;
        Color[] px = read.GetPixels();
        var mask = new bool[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                mask[y * width + x] = px[Mathf.Min(h - 1, y / 2) * w + Mathf.Min(w - 1, x / 2)].a > 0.5f;
        return mask;
    }

    private static RenderTexture RippleTarget(int size, bool autoMips)
    {
        var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            useMipMap = true,
            autoGenerateMips = autoMips,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
        };
        rt.Create();
        return rt;
    }

    /// <summary>
    /// The ripples for this frame: fadeEnd 0, the base level (Blit; the GPU averages the mips, as the
    /// mod today); else every mip level drawn by the generator (Graphics.SetRenderTarget(rt, level) and
    /// a quad as Blit draws it), each holding only the waves of fadeStart..fadeEnd texels or more there.
    /// </summary>
    public static void DrawRipples(Material generator, RenderTexture rt, float slope, float fadeStart, float fadeEnd)
    {
        if (fadeEnd <= 0f)
        {
            generator.SetVector("_RGRipple", new Vector4(slope, 0f, 0f, 0f));
            Graphics.Blit(Texture2D.blackTexture, rt, generator, 0);
            return;
        }

        // The base level holds every wave: the GPU reads it where the ripples are magnified, every
        // wave several pixels long.
        RenderTexture before = RenderTexture.active;
        int level = 0;
        for (int size = rt.width; size >= 1; size >>= 1, level++)
        {
            generator.SetVector("_RGRipple", new Vector4(slope, level == 0 ? 0f : size, fadeStart, fadeEnd));
            Graphics.SetRenderTarget(rt, level);
            GL.PushMatrix();
            GL.LoadOrtho();
            generator.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0f, 0f);
            GL.Vertex3(0f, 0f, 0f);
            GL.TexCoord2(1f, 0f);
            GL.Vertex3(1f, 0f, 0f);
            GL.TexCoord2(1f, 1f);
            GL.Vertex3(1f, 1f, 0f);
            GL.TexCoord2(0f, 1f);
            GL.Vertex3(0f, 1f, 0f);
            GL.End();
            GL.PopMatrix();
        }
        RenderTexture.active = before;
    }

    private static float Halton(int index, int radix)
    {
        float result = 0f, fraction = 1f / radix;
        while (index > 0)
        {
            result += (index % radix) * fraction;
            index /= radix;
            fraction /= radix;
        }
        return result;
    }

    /// <summary>
    /// Towers of 4 x 4 m window bays across the water, x 520..1150 m, z -480..520 m; each its own
    /// facade colour by day and its own pattern of lit windows by night (Self-Illumin: the window's
    /// colour x its illumination).
    /// </summary>
    private static List<CityTower> BuildCity(Material waterMaterial)
    {
        var towers = new List<CityTower>();
        var random = new System.Random(7);
        Texture2D day = Facade(false, random), night = Facade(true, random);
        Texture2D dayIllum = Illum(day), nightIllum = Illum(night);
        Color[] facades = { new Color(0.62f, 0.58f, 0.52f), new Color(0.45f, 0.50f, 0.56f), new Color(0.70f, 0.66f, 0.60f), new Color(0.38f, 0.36f, 0.34f), new Color(0.55f, 0.60f, 0.62f) };

        // The front row at the water's edge (x 520..), taller ones behind.
        float[,] spec =
        {
            // x, z, width, height
            { 540f, -420f, 44f, 90f }, { 560f, -300f, 36f, 140f }, { 545f, -180f, 50f, 70f }, { 570f, -60f, 40f, 180f },
            { 550f, 60f, 48f, 110f }, { 565f, 180f, 38f, 160f }, { 545f, 300f, 46f, 95f }, { 560f, 420f, 40f, 130f },
            { 700f, -360f, 52f, 230f }, { 720f, -120f, 46f, 260f }, { 690f, 120f, 56f, 210f }, { 730f, 340f, 44f, 250f },
            { 900f, -250f, 60f, 300f }, { 880f, 30f, 50f, 280f }, { 920f, 260f, 58f, 240f }, { 1100f, -80f, 70f, 330f },
        };
        for (int i = 0; i < spec.GetLength(0); i++)
        {
            float x = spec[i, 0], z = spec[i, 1], width = spec[i, 2], height = spec[i, 3];
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.localScale = new Vector3(width, height, width);
            box.transform.position = new Vector3(x, height / 2f, z);
            // One texture repeat = 8 x 8 bays = 32 m.
            var scale = new Vector2(width / 32f, height / 32f);
            var offset = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
            var dayMaterial = new Material(Shader.Find("Legacy Shaders/Self-Illumin/Diffuse"))
            {
                color = facades[i % facades.Length], mainTexture = day, mainTextureScale = scale, mainTextureOffset = offset,
            };
            dayMaterial.SetTexture("_Illum", dayIllum);
            dayMaterial.SetTextureScale("_Illum", scale);
            dayMaterial.SetTextureOffset("_Illum", offset);
            var nightMaterial = new Material(Shader.Find("Legacy Shaders/Self-Illumin/Diffuse"))
            {
                color = Color.white, mainTexture = night, mainTextureScale = scale, mainTextureOffset = offset,
            };
            nightMaterial.SetTexture("_Illum", nightIllum);
            nightMaterial.SetTextureScale("_Illum", scale);
            nightMaterial.SetTextureOffset("_Illum", offset);
            Renderer renderer = box.GetComponent<Renderer>();
            renderer.sharedMaterial = dayMaterial;
            towers.Add(new CityTower { Renderer = renderer, Day = dayMaterial, Night = nightMaterial });
        }

        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.GetComponent<Renderer>().sharedMaterial = waterMaterial;
        water.transform.localScale = new Vector3(110f, 1f, 300f);
        water.transform.position = new Vector3(0f, 0f, 0f);

        // The stand-in is forward-only, so it is not in the camera's depth texture, where the game's
        // water is (the city reflection finds the water there). A deferred plane 5 cm under it puts the
        // water's depth in; the stand-in, drawn later and nearer, covers it.
        GameObject depth = GameObject.CreatePrimitive(PrimitiveType.Plane);
        depth.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse")) { color = new Color(0.035f, 0.07f, 0.09f) };
        depth.transform.localScale = new Vector3(110f, 1f, 300f);
        depth.transform.position = new Vector3(0f, -0.05f, 0f);

        // The quay the towers stand on, a little above the water.
        GameObject quay = GameObject.CreatePrimitive(PrimitiveType.Cube);
        quay.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse")) { color = new Color(0.30f, 0.29f, 0.27f) };
        quay.transform.localScale = new Vector3(2000f, 4f, 3000f);
        quay.transform.position = new Vector3(520f + 1000f, 1f, 0f);
        return towers;
    }

    /// <summary>8 x 8 bays of 16 px (4 m): a window 3 x 2.6 m in each. By day dark glass in light walls (alpha 0: nothing glows); by night dark walls, about half the windows lit warm or cool (alpha 1 where lit).</summary>
    private static Texture2D Facade(bool night, System.Random random)
    {
        const int bay = 16, bays = 8, size = bay * bays;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        var px = new Color[size * size];
        for (int by = 0; by < bays; by++)
            for (int bx = 0; bx < bays; bx++)
            {
                bool lit = random.NextDouble() < 0.5;
                Color window = !night ? new Color(0.10f, 0.12f, 0.15f, 0f)
                    : lit ? (random.NextDouble() < 0.7 ? new Color(1f, 0.78f, 0.45f, 1f) : new Color(0.75f, 0.85f, 1f, 1f)) * (0.6f + 0.4f * (float)random.NextDouble())
                    : new Color(0.02f, 0.02f, 0.025f, 0f);
                if (night)
                    window.a = lit ? 1f : 0f;
                Color wall = night ? new Color(0.05f, 0.05f, 0.05f, 0f) : new Color(1f, 1f, 1f, 0f);
                for (int y = 0; y < bay; y++)
                    for (int x = 0; x < bay; x++)
                    {
                        bool glass = x >= 2 && x < 14 && y >= 3 && y < 13;
                        px[(by * bay + y) * size + bx * bay + x] = glass ? window : wall;
                    }
            }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    /// <summary>The illumination map Self-Illumin reads (its alpha): the facade's own alpha, x 8 for a lit window's glow.</summary>
    private static Texture2D Illum(Texture2D facade)
    {
        var t = new Texture2D(facade.width, facade.height, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        Color[] px = facade.GetPixels();
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color(1f, 1f, 1f, px[i].a);
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    private static float Tone(float x)
    {
        float c = 1f - Mathf.Exp(-Mathf.Max(0f, x));
        return Mathf.Pow(c, 1f / 2.2f);
    }

    private static Mesh UnitCube()
    {
        var mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1),
            new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1),
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        return mesh;
    }

    private static Cubemap Cube(Color colour)
    {
        var cube = new Cubemap(8, TextureFormat.RGBAFloat, false);
        var face = new Color[64];
        for (int i = 0; i < face.Length; i++)
            face[i] = colour;
        foreach (CubemapFace f in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
            cube.SetPixels(face, f);
        cube.Apply(false);
        return cube;
    }

    private static RenderTexture Target(int width, int height, bool mips)
    {
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            useMipMap = mips,
            autoGenerateMips = mips,
            filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        rt.Create();
        return rt;
    }

    private static float F(string s)
    {
        return float.Parse(s, CultureInfo.InvariantCulture);
    }
}
