using System;
using System.Collections.Generic;
using System.IO;
using RealGlass.Water;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>GPU regression for real water coverage, patch data capture and seabed independence.</summary>
public static class WaterSurfaceTest
{
    private static readonly List<string> Results = new List<string>();
    private static int _failed;

    public static void Run()
    {
        WaterSurfaceMask mask = null;
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var camera = new GameObject("surface regression camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.renderingPath = RenderingPath.DeferredLighting;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 2000f;
            camera.transform.position = new Vector3(0f, 200f, -230f);
            camera.transform.LookAt(new Vector3(0f, 55f, 100f));
            camera.targetTexture = new RenderTexture(641, 385, 24, RenderTextureFormat.ARGBFloat);
            camera.targetTexture.Create();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.position = new Vector3(0f, -80f, 100f);
            ground.transform.localScale = new Vector3(100f, 1f, 100f);
            ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.transform.position = new Vector3(-55f, 70f, 110f);
            blocker.transform.localScale = new Vector3(28f, 60f, 28f);
            blocker.GetComponent<Renderer>().sharedMaterial = ground.GetComponent<Renderer>().sharedMaterial;

            // Densely enough tessellated to reproduce the four game swells.
            Mesh mesh = Grid();
            Matrix4x4 left = Matrix4x4.TRS(new Vector3(-55f, 0f, 100f), Quaternion.identity, Vector3.one);
            Matrix4x4 right = Matrix4x4.TRS(new Vector3(55f, 0f, 100f), Quaternion.identity, Vector3.one);
            Shader.SetGlobalVector("_SimulationTime", Vector4.zero);
            var block = new MaterialPropertyBlock();
            mask = new WaterSurfaceMask(AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterSurfaceMask.shader"));
            mask.Attach(camera);

            Record(mask, mesh, left, right, block);
            camera.Render();
            Color[] flat = Read(mask.Texture);
            Height(camera, flat, new Vector3(-55f, 40f, 15f), 40f, "sea surface");
            Height(camera, flat, new Vector3(55f, 120f, 15f), 120f, "raised lake / shared property block snapshot");
            Check("dry channel below sea level has no water", Pixel(camera, flat, new Vector3(0f, 40f, 20f)).r == 0f);
            Check("foreground building hides water", Pixel(camera, flat, blocker.transform.position).r == 0f);

            ground.transform.rotation = Quaternion.Euler(9f, 0f, -5f);
            Record(mask, mesh, left, right, block);
            camera.Render();
            Color[] sloped = Read(mask.Texture);
            float difference = 0f;
            for (int i = 0; i < flat.Length; i++)
                difference = Mathf.Max(difference, Mathf.Abs(flat[i].r - sloped[i].r));
            Check("sloping seabed leaves water depth unchanged", difference < 0.001f, difference.ToString("G6"));

            Bind(block, 120f, true);
            mask.Draw(mesh, right, 0, null, 0, block);
            Bind(block, 40f, true);
            mask.Draw(mesh, right, 0, null, 0, block);
            camera.Render();
            Height(camera, Read(mask.Texture), new Vector3(55f, 120f, 15f), 120f, "nearest overlapping surface wins");

            // No simulation depth despite a height map below the eye: must not infer a lake.
            Bind(block, 40f, false);
            mask.Draw(mesh, left, 0, null, 0, block);
            camera.Render();
            Check("dry simulation cells are rejected", Maximum(Read(mask.Texture)) == 0f);

            // A completed frame's draws are cleared before the next capture/render.
            Record(mask, mesh, left, right, block);
            camera.Render();
            camera.Render();
            Check("no stale water after an empty frame", Maximum(Read(mask.Texture)) == 0f);

            mask.Detach();
            mask.Attach(camera);
            Record(mask, mesh, left, right, block);
            camera.Render();
            Height(camera, Read(mask.Texture), new Vector3(55f, 120f, 15f), 120f, "reattach");
            camera.targetTexture = new RenderTexture(377, 239, 24, RenderTextureFormat.ARGBFloat);
            camera.targetTexture.Create();
            mask.Attach(camera);
            Record(mask, mesh, left, right, block);
            camera.Render();
            Check("resize follows camera", mask.Texture.width == 377 && mask.Texture.height == 239);
            Height(camera, Read(mask.Texture), new Vector3(-55f, 40f, 15f), 40f, "resized sea surface");

            // A camera that does not draw this layer must not acquire that layer's water.
            camera.cullingMask = 0;
            Record(mask, mesh, left, right, block);
            camera.Render();
            Check("camera layer filtering", Maximum(Read(mask.Texture)) == 0f);
            Daylight();
        }
        catch (Exception e)
        {
            _failed++;
            Results.Add("EXCEPTION " + e);
        }
        finally
        {
            if (mask != null)
                mask.Destroy();
        }
        Results.Add(_failed == 0 ? "WATER SURFACE TEST OK" : "WATER SURFACE TEST FAILED: " + _failed);
        File.WriteAllLines("water-surface-test-output.txt", Results.ToArray());
        EditorApplication.Exit(_failed == 0 ? 0 : 1);
    }

    private static void Daylight()
    {
        var theme = new Color(0.03f, 0.06f, 0.08f, 0.6f);
        Color night = WaterAppearance.Colour(theme, 1.3f, WaterAppearance.DayAmount(-0.3f, 1f));
        Check("night colour preserved", Mathf.Abs(night.r - theme.r * 1.3f) < 1e-6f &&
            Mathf.Abs(night.g - theme.g * 1.3f) < 1e-6f && Mathf.Abs(night.b - theme.b * 1.3f) < 1e-6f && night.a == theme.a);
        Color day = WaterAppearance.Colour(theme, 1f, WaterAppearance.DayAmount(0.5f, 1f));
        Check("day is brighter and bluer", day.grayscale > theme.grayscale && day.b / day.r > theme.b / theme.r);
        Color off = WaterAppearance.Colour(theme, 1f, WaterAppearance.DayAmount(0.5f, 0f));
        Check("zero daylight control restores theme", off == theme);
        Check("dusk transitions continuously", Mathf.Abs(WaterAppearance.DayAmount(-0.001f, 1f) - WaterAppearance.DayAmount(0.001f, 1f)) < 0.02f);
        double[] waves = WaterOptics.Ripples();
        var rows = new Vector4[64];
        for (int i = 0; i < 32; i++)
        {
            int p = i * 8;
            rows[i] = new Vector4((float)waves[p], (float)waves[p + 1], (float)waves[p + 2], (float)waves[p + 3]);
            rows[i + 32] = new Vector4((float)waves[p + 4], (float)waves[p + 5], (float)waves[p + 6], 0f);
        }
        Shader.SetGlobalVector("_SimulationTime2", new Vector4(443f, 443f / 3600f, 0f, 0f));
        Color[] original = Read(CityReflectionTest.GenerateRipples(rows, 0.10f, 256));
        Color[] daylight = Read(CityReflectionTest.GenerateRipples(rows, 0.10f, 256, 1f));
        Color[] nightWaves = Read(CityReflectionTest.GenerateRipples(rows, 0.10f, 256, WaterAppearance.DayAmount(-0.3f, 1f)));
        double before = 0.0, after = 0.0;
        float difference = 0f;
        for (int i = 0; i < original.Length; i++)
        {
            before += Math.Pow(original[i].r - 0.5f, 2) + Math.Pow(original[i].g - 0.5f, 2);
            after += Math.Pow(daylight[i].r - 0.5f, 2) + Math.Pow(daylight[i].g - 0.5f, 2);
            difference = Mathf.Max(difference, Mathf.Abs(original[i].r - nightWaves[i].r), Mathf.Abs(original[i].g - nightWaves[i].g));
        }
        Check("daylight waves gain visible slope detail", after > before * 1.35 && after < before * 2.5, Math.Sqrt(after / before).ToString("F3") + "x RMS slope");
        Check("night waves unchanged on GPU", difference == 0f);
    }

    private static void Record(WaterSurfaceMask mask, Mesh mesh, Matrix4x4 left, Matrix4x4 right, MaterialPropertyBlock block)
    {
        Bind(block, 40f, true);
        mask.Draw(mesh, left, 0, null, 0, block);
        Bind(block, 120f, true);
        mask.Draw(mesh, right, 0, null, 0, block);
        // TerrainManager reuses its block after every draw; recorded draws must keep their own data.
        Bind(block, 900f, false);
    }

    private static void Bind(MaterialPropertyBlock block, float height, bool wet)
    {
        // Float data isolates placement from 16-bit quantisation. The exact game's decoder is used.
        block.SetTexture("_MainTex", Solid(new Color(1f, 0f, 1f, 0f)));
        block.SetVector("_HeightMapping", new Vector4(0.25f, height / 0.996094f, 0.25f, 0f));
        block.SetTexture("_TerrainHeight", Solid(Color.clear));
        block.SetVector("_TerrainMapping", new Vector4(0.25f, 1024f, 0.25f, 0f));
        block.SetVector("_SurfaceMapping", new Vector4(0.5f, 0.5f, 0.001f, 0f));
        block.SetVector("_TerrainStatus", new Vector4(0f, 0.5f, 0f, 0f));
        Texture2D a = Solid(new Color(0.5f, 0f, 0.5f, 1f));
        Texture2D b = Solid(new Color(0.5f, wet ? 1f : 0f, 0.5f, 1f));
        block.SetTexture("_SurfaceAOld", a);
        block.SetTexture("_SurfaceANew", a);
        block.SetTexture("_SurfaceBOld", b);
        block.SetTexture("_SurfaceBNew", b);
    }

    private static Texture2D Solid(Color c)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        texture.SetPixels(pixels);
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    private static Mesh Grid()
    {
        const int nx = 16, nz = 48;
        var vertices = new Vector3[(nx + 1) * (nz + 1)];
        var indices = new int[nx * nz * 6];
        for (int z = 0; z <= nz; z++)
        for (int x = 0; x <= nx; x++)
            vertices[z * (nx + 1) + x] = new Vector3(-40f + 80f * x / nx, 0f, -140f + 280f * z / nz);
        int k = 0;
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            int a = z * (nx + 1) + x, b = a + nx + 1;
            indices[k++] = a; indices[k++] = b; indices[k++] = a + 1;
            indices[k++] = a + 1; indices[k++] = b; indices[k++] = b + 1;
        }
        return new Mesh { vertices = vertices, triangles = indices };
    }

    private static Color[] Read(RenderTexture texture)
    {
        // Unity 5.6 cannot reliably ReadPixels RFloat directly into RGBAFloat on every driver.
        var copy = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        Graphics.Blit(texture, copy);
        RenderTexture.active = copy;
        var read = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
        Color[] pixels = read.GetPixels();
        UnityEngine.Object.DestroyImmediate(read);
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(copy);
        return pixels;
    }

    private static Color Pixel(Camera camera, Color[] pixels, Vector3 world)
    {
        Vector3 v = camera.WorldToViewportPoint(world);
        int x = Mathf.Clamp((int)(v.x * camera.pixelWidth), 0, camera.pixelWidth - 1);
        int y = Mathf.Clamp((int)(v.y * camera.pixelHeight), 0, camera.pixelHeight - 1);
        return pixels[y * camera.pixelWidth + x];
    }

    private static void Height(Camera camera, Color[] pixels, Vector3 world, float expected, string label)
    {
        Vector3 v = camera.WorldToViewportPoint(world);
        v.x = (Mathf.Floor(v.x * camera.pixelWidth) + 0.5f) / camera.pixelWidth;
        v.y = (Mathf.Floor(v.y * camera.pixelHeight) + 0.5f) / camera.pixelHeight;
        Vector3 ray = camera.ViewportPointToRay(v).direction.normalized;
        float eye = Pixel(camera, pixels, world).r;
        float height = (camera.transform.position + ray * (eye / Vector3.Dot(ray, camera.transform.forward))).y;
        Check(label, eye > 0f && Mathf.Abs(height - expected) < 1f, height.ToString("F3") + " m");
    }

    private static float Maximum(Color[] pixels)
    {
        float max = 0f;
        foreach (Color pixel in pixels) max = Mathf.Max(max, pixel.r);
        return max;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        Results.Add((ok ? "ok " : "FAIL ") + name + " " + detail);
        if (!ok) _failed++;
    }
}
