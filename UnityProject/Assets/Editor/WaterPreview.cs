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
        public float Waves;
        public float Seconds;
        public float Strength;
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
            var waves = new Vector4[32];
            var shots = new List<Shot>();
            string outDir = dir;
            int width = 960, height = 540;
            float streak = 0f, length = 1f, exposureDay = 1f, exposureNight = 1f;
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
                else if (p[0] == "wave")
                    waves[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Vector4(F(p[2]), F(p[3]), F(p[4]), F(p[5]));
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
                    shots.Add(new Shot { Name = p[1], View = p[2], Waves = F(p[3]), Seconds = F(p[4]), Strength = F(p[5]) });
            }

            Shader.SetGlobalVectorArray("_RGSeaTable", table);
            material.SetVectorArray("_RGWaterWaves", waves);
            material.SetTexture("_RGWaterTheme", Cube(Color.black));

            var city = BuildCity();

            var skybox = new Material(Shader.Find("Skybox/Procedural"));
            RenderSettings.skybox = skybox;
            var sunObject = new GameObject("preview sun");
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.None;
            sunObject.transform.rotation = Quaternion.LookRotation(new Vector3(0.55f, -0.45f, -0.7f).normalized);

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
            RenderTexture frame = Target(w, h, false), trace = Target(w, h, true), streaked = Target(w, h, false);
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

            foreach (Shot shot in shots)
            {
                bool night = shot.View.StartsWith("night", StringComparison.Ordinal);
                foreach (CityTower t in city)
                    t.Renderer.sharedMaterial = night ? t.Night : t.Day;
                sun.intensity = night ? 0f : 1.3f;
                sun.color = new Color(1f, 0.95f, 0.88f);
                skybox.SetFloat("_Exposure", night ? 0.04f : 1.1f);
                skybox.SetFloat("_AtmosphereThickness", 1f);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = night ? new Color(0.010f, 0.012f, 0.020f) : new Color(0.45f, 0.55f, 0.70f);
                RenderSettings.ambientEquatorColor = night ? new Color(0.008f, 0.009f, 0.012f) : new Color(0.40f, 0.42f, 0.45f);
                RenderSettings.ambientGroundColor = night ? new Color(0.004f, 0.004f, 0.005f) : new Color(0.20f, 0.20f, 0.20f);

                // The views: a camera low over the harbour, one higher up, the same low one by night.
                Vector3 eye;
                float pitch;
                if (shot.View.EndsWith("high", StringComparison.Ordinal))
                {
                    eye = new Vector3(-200f, 260f, 60f);
                    pitch = 18f;
                }
                else
                {
                    eye = new Vector3(0f, 45f, 40f);
                    pitch = 4f;
                }
                cameraObject.transform.position = eye;
                cameraObject.transform.rotation = Quaternion.Euler(pitch, 90f, 0f);

                material.SetVector("_RGWaterGeom", new Vector4(0f, shot.Strength, 0f, 0f));
                material.SetVector("_RGWaterWave", new Vector4(shot.Waves, 0f, 0f, 0f));
                material.SetVector("_RGWaterLight", new Vector4(1f, 1f, 1f, 0f));
                Shader.SetGlobalVector("_SimulationTime2", new Vector4(shot.Seconds, shot.Seconds / 3600f, 0f, 0f));

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

    /// <summary>
    /// Towers of 4 x 4 m window bays across the water, x 520..1150 m, z -480..520 m; each its own
    /// facade colour by day and its own pattern of lit windows by night (Self-Illumin: the window's
    /// colour x its illumination).
    /// </summary>
    private static List<CityTower> BuildCity()
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
        water.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse")) { color = new Color(0.035f, 0.07f, 0.09f) };
        water.transform.localScale = new Vector3(110f, 1f, 300f);
        water.transform.position = new Vector3(0f, 0f, 0f);

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
