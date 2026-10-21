using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RealGlass.Glass;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The model's glass on its LOD (LodGlass.cs, as the mod runs it) on the GPU, in numbers:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod LodGlassTest.Run [-force-glcore]
/// writes lod-glass-test-output.txt. The read-back (GlassUtil pass 6): a model's shine map and two
/// LODs' rectangles of an atlas read back into their pictures, against the textures' own texels,
/// whichever way this API's rows come back. Then the mask: a tower whose LOD is laid out unlike its
/// model compared on the CPU, its mask made (BuildMask) and uploaded (one byte a texel, every mip
/// given -- the engine call the mod had never made: an Alpha8 texture with a mip chain by
/// LoadRawTextureData), then read back where the shader will read it: its first level byte for byte,
/// its second against the CPU's mip.
/// </summary>
public static class LodGlassTest
{
    private const int AtlasSize = 1024;

    public static void Run()
    {
        var output = new StringBuilder();
        string dir = Directory.GetCurrentDirectory();
        var glass = new LodGlass();
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            output.AppendLine("graphics " + SystemInfo.graphicsDeviceType + " (" + SystemInfo.graphicsDeviceName + "), Alpha8 textures " +
                              SystemInfo.SupportsTextureFormat(TextureFormat.Alpha8));
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/GlassUtil.shader");
            var util = new Material(shader);

            // ---- the read-back ----
            // A model's shine map 256 x 256 (mip 0 is the tile), an atlas 1024 x 1024 with an LOD of
            // 256 x 256 (mip 0) and one of 128 x 128 (read up to the tile).
            var modelShine = new byte[256 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                    modelShine[y * 256 + x] = (byte)(((x / 16 + y / 32) % 2) * 200 + (x * 7 + y * 3) % 50);
            Texture2D model = Xys(256, 256, modelShine);

            var atlasShine = new byte[AtlasSize * AtlasSize];
            for (int y = 0; y < AtlasSize; y++)
                for (int x = 0; x < AtlasSize; x++)
                    atlasShine[y * AtlasSize + x] = (byte)((x * 13 + y * 29) % 256);
            Texture2D atlas = Xys(AtlasSize, AtlasSize, atlasShine);

            var a = new LodGlassEntry { Asset = "a", Name = "a", ModelXys = model, Rect = new Vector4(256f / AtlasSize, 512f / AtlasSize, 256f / AtlasSize, 256f / AtlasSize) };
            var b = new LodGlassEntry { Asset = "b", Name = "b", ModelXys = null, Rect = new Vector4(0f, 0f, 128f / AtlasSize, 128f / AtlasSize) };
            var entries = new List<LodGlassEntry> { a, b };
            Debug.Log("LOD GLASS TEST: ReadBack");
            bool read = glass.ReadBack(entries, 0, 2, atlas, util);
            Line(output, read, "read back (" + (glass.Flipped ? "rows bottom up" : "rows as drawn") + ")", glass.Problem);
            if (read)
            {
                int worstModel = 0, worstLod = 0, worstSmall = 0, worstNone = 0;
                for (int y = 0; y < 256; y++)
                {
                    for (int x = 0; x < 256; x++)
                    {
                        int k = y * 256 + x;
                        worstModel = Math.Max(worstModel, Math.Abs(a.ModelShine[k] - modelShine[k]));
                        worstLod = Math.Max(worstLod, Math.Abs(a.LodShine[k] - atlasShine[(512 + y) * AtlasSize + 256 + x]));
                        worstNone = Math.Max(worstNone, b.ModelShine[k]);
                        float sx, sy;
                        // The 128 rectangle read at the tile's texel centres: bilinear between its texels
                        // (the tile's last row and column reach half a texel past the rectangle, into
                        // the atlas beyond it, as any filtered read at an island's edge does).
                        if (x == 255 || y == 255)
                            continue;
                        sx = Mathf.Clamp((x + 0.5f) / 2f - 0.5f, 0f, 127f);
                        sy = Mathf.Clamp((y + 0.5f) / 2f - 0.5f, 0f, 127f);
                        int x0 = Mathf.Min(126, (int)sx), y0 = Mathf.Min(126, (int)sy);
                        float fx = sx - x0, fy = sy - y0;
                        float s00 = atlasShine[y0 * AtlasSize + x0], s10 = atlasShine[y0 * AtlasSize + x0 + 1];
                        float s01 = atlasShine[(y0 + 1) * AtlasSize + x0], s11 = atlasShine[(y0 + 1) * AtlasSize + x0 + 1];
                        float want = (s00 + (s10 - s00) * fx) + ((s01 + (s11 - s01) * fx) - (s00 + (s10 - s00) * fx)) * fy;
                        worstSmall = Math.Max(worstSmall, (int)Math.Abs(b.LodShine[k] - want));
                    }
                }
                Line(output, worstModel <= 1, "the model's shine map, texel for texel", "worst " + worstModel + "/255");
                Line(output, worstLod <= 1, "an LOD's rectangle of the atlas, texel for texel", "worst " + worstLod + "/255");
                Line(output, worstSmall <= 3, "a smaller LOD's rectangle, read up to the tile (bilinear)", "worst " + worstSmall + "/255");
                Line(output, worstNone == 0, "a model with no shine map: no shine", "highest " + worstNone + "/255");
            }

            // ---- the mask ----
            // A tower whose LOD is laid out turned (its sides stacked in v) and has no glass; its model's
            // glass: side 0 and the left half of the others.
            LodGlassEntry tower = Tower();
            LodGlass.Transfer(tower);
            Line(output, tower.Transferred && tower.Met == tower.OnLod && tower.OnLod > 0, "the tower compared", tower.Met + " of " + tower.OnLod +
                 " met" + (tower.Problem.Length > 0 ? ", " + tower.Problem : ""));
            const int maskSize = 256;
            byte[] data = LodGlass.BuildMask(new List<LodGlassEntry> { tower }, maskSize, maskSize);
            Debug.Log("LOD GLASS TEST: Upload (Alpha8, " + LodGlass.MipCount(maskSize, maskSize) + " mips by LoadRawTextureData)");
            bool uploaded = glass.Upload(data, maskSize, maskSize);
            Line(output, uploaded && glass.Mask != null && glass.Mask.width == maskSize, "the mask uploaded (" + LodGlass.MipCount(maskSize, maskSize) + " mips, " +
                 data.Length + " bytes)", glass.Problem);
            if (uploaded && glass.Mask != null)
            {
                glass.Mask.filterMode = FilterMode.Point;
                byte[] level0 = ReadAlpha(glass.Mask, maskSize);
                int worst0 = 0, inRect = 0;
                for (int i = 0; i < maskSize * maskSize; i++)
                {
                    worst0 = Math.Max(worst0, Math.Abs(level0[i] - data[i]));
                    if (data[i] > 0)
                        inRect++;
                }
                Line(output, worst0 == 0 && inRect == 64 * 64, "its first level where the shader reads it, byte for byte (the LOD's 64 x 64 texels)",
                     "worst " + worst0 + "/255, " + inRect + " texels hold a shine");
                byte[] level1 = ReadAlpha(glass.Mask, maskSize / 2);
                int worst1 = 0;
                for (int i = 0; i < level1.Length; i++)
                    worst1 = Math.Max(worst1, Math.Abs(level1[i] - data[maskSize * maskSize + i]));
                Line(output, worst1 == 0, "its second level, the CPU's mip", "worst " + worst1 + "/255");

                // The mask in the tower's rectangle against what its model's glass says there.
                int rx = maskSize / 4, ry = maskSize / 2, right = 0, judged = 0;
                for (int y = 0; y < 64; y++)
                {
                    float along = (y + 0.5f) / 64f, t = along * 4f - (int)(along * 4f);
                    if (Math.Min(t, Math.Min(Math.Abs(t - 0.5f), 1f - t)) * 16f < 1.5f)
                        continue;
                    for (int x = 0; x < 64; x++)
                    {
                        judged++;
                        int m = level0[(ry + y) * maskSize + rx + x];
                        if (Glass(along) ? m > 200 : (m >= 1 && m < 55))
                            right++;
                    }
                }
                Line(output, right == judged, "the tower's LOD shows its model's glass in its own layout", right + " of " + judged + " texels");
            }

            glass.Bind();
            Line(output, Shader.GetGlobalTexture("_RGLodGlass") == glass.Mask, "bound for the shader", "");
            Debug.Log("LOD GLASS TEST: Upload(null)");
            glass.Upload(null, 0, 0);
            glass.Bind();
            Texture none = Shader.GetGlobalTexture("_RGLodGlass");
            Line(output, none != null && none.width == 1, "with nothing to show, a texel of nothing bound", none != null ? none.name : "null");
        }
        catch (Exception e)
        {
            output.AppendLine("EXCEPTION " + e);
        }
        finally
        {
            glass.Destroy();
        }

        File.WriteAllText(Path.Combine(dir, "lod-glass-test-output.txt"), output.ToString());
        EditorApplication.Exit(0);
    }

    private static void Line(StringBuilder output, bool ok, string what, string detail)
    {
        output.AppendLine((ok ? "ok    " : "FAIL  ") + what + "  ->  " + detail);
    }

    /// <summary>A shine map as the game holds one: xys, z = 1 - shine; linear, mipmapped.</summary>
    private static Texture2D Xys(int w, int h, byte[] shine)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
        t.filterMode = FilterMode.Trilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color32(128, 128, (byte)(255 - shine[i]), 255);
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }

    /// <summary>A texture's alpha read at size x size (its level of that size, point filtered).</summary>
    private static byte[] ReadAlpha(Texture texture, int size)
    {
        var copy = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        copy.Create();
        Graphics.Blit(texture, copy);
        RenderTexture before = RenderTexture.active;
        RenderTexture.active = copy;
        var read = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
        RenderTexture.active = before;
        Color32[] px = read.GetPixels32();
        copy.Release();
        UnityEngine.Object.DestroyImmediate(copy);
        UnityEngine.Object.DestroyImmediate(read);
        var a = new byte[px.Length];
        for (int i = 0; i < px.Length; i++)
            a[i] = px[i].a;
        return a;
    }

    // ---- a tower 10 x 10 x 30 m (as tools\test-settings.ps1's) ----

    private static bool Glass(float along)
    {
        int k = Math.Min(3, (int)(along * 4f));
        return k == 0 || along * 4f - k < 0.5f;
    }

    private static LodGlassEntry Tower()
    {
        var pos = new float[16 * 3];
        var modelUv = new float[16 * 2];
        var lodUv = new float[16 * 2];
        var tri = new int[24];
        for (int k = 0; k < 4; k++)
        {
            for (int c = 0; c < 4; c++)
            {
                float t = c == 1 || c == 2 ? 1f : 0f, up = c >= 2 ? 1f : 0f, x, z;
                switch (k)
                {
                    case 0: x = -5f + 10f * t; z = 5f; break;
                    case 1: x = 5f; z = 5f - 10f * t; break;
                    case 2: x = 5f - 10f * t; z = -5f; break;
                    default: x = -5f; z = -5f + 10f * t; break;
                }
                int i = 4 * k + c;
                pos[3 * i] = x;
                pos[3 * i + 1] = 30f * up;
                pos[3 * i + 2] = z;
                float along = (k + t) / 4f;
                modelUv[2 * i] = along;
                modelUv[2 * i + 1] = up;
                lodUv[2 * i] = up;
                lodUv[2 * i + 1] = along;
            }
            int b = 4 * k;
            tri[6 * k] = b; tri[6 * k + 1] = b + 1; tri[6 * k + 2] = b + 2;
            tri[6 * k + 3] = b; tri[6 * k + 4] = b + 2; tri[6 * k + 5] = b + 3;
        }

        var model = new byte[256 * 256];
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
                model[y * 256 + x] = Glass((x + 0.5f) / 256f) ? (byte)255 : (byte)0;

        var e = new LodGlassEntry
        {
            Asset = "tower",
            Name = "tower",
            Rect = new Vector4(0.25f, 0.5f, 0.25f, 0.25f),
            ModelSt = new Vector4(1f, 1f, 0f, 0f),
            ModelPos = pos,
            ModelUv = modelUv,
            ModelTri = tri,
            LodPos = (float[])pos.Clone(),
            LodUv = lodUv,
            LodTri = (int[])tri.Clone(),
            ModelShine = model,
            LodShine = new byte[256 * 256],
            SideX = 64,
            SideY = 64,
        };
        return e;
    }
}
