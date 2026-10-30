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
    // The game's frames, counted here: in the editor every render falls in one Time.frameCount.
    private static int _frame;

    public static void Run()
    {
        WaterSurfaceMask mask = null;
        WaterSurfaceMask.Frame = () => _frame;
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

            // The passes after the water (the game's fog, the clouds) read its surface in the depth
            // texture; the next render's copy before the water reads the camera's own again.
            if (WaterSurfaceMask.MergesDepth)
            {
                var readGlobal = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterSurfaceMask.shader"));
                var after = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
                after.Create();
                var probe = new CommandBuffer { name = "the depth texture as the passes after the water read it" };
                probe.Blit(BuiltinRenderTextureType.CameraTarget, after, readGlobal, 1);
                camera.AddCommandBuffer(CameraEvent.AfterImageEffectsOpaque, probe);
                Vector3 sea = new Vector3(-55f, 40f, 15f);
                for (int render = 1; render <= 2; render++)
                {
                    Record(mask, mesh, left, right, block);
                    camera.Render();
                    Color[] surface = Read(mask.Texture), merged = Read(after), scene = Read(mask.SceneDepth);
                    float seaEye = Pixel(camera, surface, sea).r, mergedEye = Eye(camera, Pixel(camera, merged, sea).r);
                    Check("render " + render + ": the water's surface in the depth texture after it", seaEye > 0f && Mathf.Abs(mergedEye - seaEye) < 0.001f * seaEye,
                          mergedEye.ToString("F3") + " m, the surface " + seaEye.ToString("F3") + " m");
                    float blockerEye = Eye(camera, Pixel(camera, scene, blocker.transform.position).r);
                    float mergedBlocker = Eye(camera, Pixel(camera, merged, blocker.transform.position).r);
                    Check("render " + render + ": no water in front: the scene's depth", Mathf.Abs(mergedBlocker - blockerEye) < 0.001f * blockerEye,
                          mergedBlocker.ToString("F3") + " m, the scene " + blockerEye.ToString("F3") + " m");
                    float bed = Eye(camera, Pixel(camera, scene, sea).r);
                    Check("render " + render + ": the scene depth before the water has no water in it", bed > seaEye + 10f,
                          "the bed " + bed.ToString("F1") + " m, the surface " + seaEye.ToString("F1") + " m");
                }
                camera.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, probe);
                probe.Release();
                after.Release();
            }

            ground.transform.rotation = Quaternion.Euler(9f, 0f, -5f);
            Record(mask, mesh, left, right, block);
            camera.Render();
            Color[] sloped = Read(mask.Texture);
            float difference = 0f;
            for (int i = 0; i < flat.Length; i++)
                difference = Mathf.Max(difference, Mathf.Abs(flat[i].r - sloped[i].r));
            Check("sloping seabed leaves water depth unchanged", difference < 0.001f, difference.ToString("G6"));

            _frame++;
            Bind(block, 120f, true);
            mask.Draw(mesh, right, 0, null, 0, block);
            Bind(block, 40f, true);
            mask.Draw(mesh, right, 0, null, 0, block);
            camera.Render();
            Height(camera, Read(mask.Texture), new Vector3(55f, 120f, 15f), 120f, "nearest overlapping surface wins");

            // No simulation depth despite a height map below the eye: must not infer a lake.
            _frame++;
            Bind(block, 40f, false);
            mask.Draw(mesh, left, 0, null, 0, block);
            camera.Render();
            Check("dry simulation cells are rejected", Maximum(Read(mask.Texture)) == 0f);

            // Every render of a frame keeps the frame's draws (a camera rendered twice a frame by
            // hand); the next frame's render, with none, has no water.
            Record(mask, mesh, left, right, block);
            camera.Render();
            camera.Render();
            Height(camera, Read(mask.Texture), new Vector3(55f, 120f, 15f), 120f, "a second render of the same frame keeps its water");
            _frame++;
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

            // On Metal the depth texture holds the game's water by the time of the capture, nearer than
            // the surface (the author's M1, 2026-10-26: no water captured, no city in it): a depth
            // texture changed after the final pass must not reach the capture. Run before the capture,
            // at its own event.
            var spoil = new CommandBuffer { name = "the depth texture nearer than everything after the final pass" };
            spoil.SetGlobalTexture("_CameraDepthTexture", SystemInfo.usesReversedZBuffer ? Texture2D.whiteTexture : Texture2D.blackTexture);
            CameraEvent spoiled = WaterSurfaceMask.MergesDepth ? CameraEvent.BeforeImageEffectsOpaque : CameraEvent.AfterImageEffectsOpaque;
            mask.Detach();
            camera.AddCommandBuffer(spoiled, spoil);
            mask.Attach(camera);
            Record(mask, mesh, left, right, block);
            camera.Render();
            Height(camera, Read(mask.Texture), new Vector3(-55f, 40f, 15f), 40f, "sea surface with the depth texture changed after the final pass");
            camera.RemoveCommandBuffer(spoiled, spoil);
            spoil.Release();

            // From under the surface the game draws no water (it culls the underside): nothing recorded,
            // so nothing of it reaches the depth texture either.
            camera.transform.position = new Vector3(-55f, 20f, -60f);
            camera.transform.LookAt(new Vector3(-55f, 60f, 60f));
            Record(mask, mesh, left, right, block);
            camera.Render();
            Check("no surface recorded from under it", Maximum(Read(mask.Texture)) == 0f);

            // A camera that does not draw this layer must not acquire that layer's water.
            camera.cullingMask = 0;
            Record(mask, mesh, left, right, block);
            camera.Render();
            Check("camera layer filtering", Maximum(Read(mask.Texture)) == 0f);
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
            WaterSurfaceMask.Frame = () => Time.frameCount;
        }
        Results.Add(_failed == 0 ? "WATER SURFACE TEST OK" : "WATER SURFACE TEST FAILED: " + _failed);
        File.WriteAllLines("water-surface-test-output.txt", Results.ToArray());
        EditorApplication.Exit(_failed == 0 ? 0 : 1);
    }

    // A new frame's draws.
    private static void Record(WaterSurfaceMask mask, Mesh mesh, Matrix4x4 left, Matrix4x4 right, MaterialPropertyBlock block)
    {
        _frame++;
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

    // A raw depth as LinearEyeDepth reads it (_ZBufferParams as Unity sets them, either convention).
    private static float Eye(Camera camera, float raw)
    {
        float n = camera.nearClipPlane, f = camera.farClipPlane;
        float x = SystemInfo.usesReversedZBuffer ? -1f + f / n : 1f - f / n;
        float y = SystemInfo.usesReversedZBuffer ? 1f : f / n;
        return 1f / (x / f * raw + y / f);
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
