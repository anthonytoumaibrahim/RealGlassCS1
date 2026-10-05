using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Water tab's ripples (RealGlass/WaterRipples) against the mod's model, in numbers.
/// tools\test-reflection-shader.ps1 writes ripple-test-input.txt -- the waves' rows
/// (WaterOptics.Ripples), the root-mean-square slope, the game's hour clock, the texture's size, the
/// texels to report -- and runs
///   Unity.exe -batchmode -quit -projectPath . -executeMethod WaterRippleTest.Run
/// which draws the texture as the mod does (Graphics.Blit into an ARGB32 linear target with mips),
/// reads it back and writes each listed texel and the whole texture's decoded root-mean-square to
/// ripple-test-output.txt.
/// It also runs the mod's MEASUREMENT of the game's texture (WaterRipples.Measure) on a stand-in: a
/// normal-map-like pattern compressed to DXT5, copied by Graphics.Blit into a 256 x 256 ARGB32 linear
/// target and read back with ReadPixels -- the engine calls the mod makes, probed here first; the
/// decoded root-mean-square of the copy against the pattern's own.
/// </summary>
public static class WaterRippleTest
{
    public static void Run()
    {
        var output = new StringBuilder();
        string dir = Directory.GetCurrentDirectory();
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterRipples.shader");
            output.AppendLine("shader " + (shader != null ? shader.name + " supported=" + shader.isSupported : "MISSING"));

            var rows = new Vector4[64];
            float slope = 0f;
            int size = 256;
            var texels = new List<int[]>();
            foreach (string raw in File.ReadAllLines(Path.Combine(dir, "ripple-test-input.txt")))
            {
                string[] p = raw.Split(' ');
                if (p[0] == "ripple")
                    rows[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Vector4(F(p[2]), F(p[3]), F(p[4]), F(p[5]));
                else if (p[0] == "slope")
                    slope = F(p[1]);
                else if (p[0] == "simtime2")
                    Shader.SetGlobalVector("_SimulationTime2", new Vector4(F(p[1]), F(p[2]), F(p[3]), F(p[4])));
                else if (p[0] == "size")
                    size = int.Parse(p[1], CultureInfo.InvariantCulture);
                else if (p[0] == "texel")
                    texels.Add(new[] { int.Parse(p[1], CultureInfo.InvariantCulture), int.Parse(p[2], CultureInfo.InvariantCulture) });
            }

            RenderTexture rt = CityReflectionTest.GenerateRipples(rows, slope, size);
            Color32[] px = Read(rt, size);
            foreach (int[] t in texels)
            {
                Color32 c = px[t[1] * size + t[0]];
                output.AppendLine("texel " + t[0] + " " + t[1] + " " + c.r + " " + c.g + " " + c.b + " " + c.a);
            }

            double sumSq = 0.0;
            foreach (Color32 c in px)
            {
                double x = c.a / 255.0 * 2.0 - 1.0, y = c.g / 255.0 * 2.0 - 1.0;
                sumSq += x * x + y * y;
            }
            output.AppendLine("rms " + S(Math.Sqrt(sumSq / px.Length)));

            // The measurement, on a stand-in for the game's texture.
            Probe(output);
        }
        catch (Exception e)
        {
            output.AppendLine("EXCEPTION " + e);
        }

        File.WriteAllText(Path.Combine(dir, "ripple-test-output.txt"), output.ToString());
    }

    /// <summary>
    /// A 512 x 512 pattern with decoded (x, y) in .a and .g, compressed to DXT5 with mips, measured as
    /// WaterRipples.Measure measures: Blit into 256 x 256 ARGB32 linear, ReadPixels, decoded rms.
    /// </summary>
    private static void Probe(StringBuilder output)
    {
        const int n = 512;
        var source = new Texture2D(n, n, TextureFormat.RGBA32, true, true);
        var px = new Color32[n * n];
        double sumSq = 0.0;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                double a = 0.35 * Math.Sin(2.0 * Math.PI * (3 * x + 5 * y) / n) + 0.15 * Math.Cos(2.0 * Math.PI * (11 * x - 7 * y) / n);
                double b = 0.30 * Math.Cos(2.0 * Math.PI * (2 * x - 9 * y) / n);
                byte ea = (byte)Mathf.Clamp(Mathf.RoundToInt((float)((a * 0.5 + 0.5) * 255.0)), 0, 255);
                byte eb = (byte)Mathf.Clamp(Mathf.RoundToInt((float)((b * 0.5 + 0.5) * 255.0)), 0, 255);
                px[y * n + x] = new Color32(255, eb, 255, ea);
                double da = ea / 255.0 * 2.0 - 1.0, db = eb / 255.0 * 2.0 - 1.0;
                sumSq += da * da + db * db;
            }
        }
        source.SetPixels32(px);
        source.Apply(true);
        source.Compress(true);
        output.AppendLine("probe source " + source.format + " " + source.width + " x " + source.height + ", " + source.mipmapCount + " mips, rms " + S(Math.Sqrt(sumSq / px.Length)));

        // As WaterRipples.Measure.
        RenderTexture before = RenderTexture.active;
        var copy = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        copy.Create();
        Graphics.Blit(source, copy);
        RenderTexture.active = copy;
        var read = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
        read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0, false);
        Color32[] got = read.GetPixels32();
        RenderTexture.active = before;
        double sum = 0.0;
        foreach (Color32 c in got)
        {
            double x = c.a / 255.0 * 2.0 - 1.0, y = c.g / 255.0 * 2.0 - 1.0;
            sum += x * x + y * y;
        }
        output.AppendLine("probe measured " + S(Math.Sqrt(sum / got.Length)));
    }

    private static Color32[] Read(RenderTexture rt, int size)
    {
        RenderTexture before = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
        RenderTexture.active = before;
        return read.GetPixels32();
    }

    private static float F(string s)
    {
        return float.Parse(s, CultureInfo.InvariantCulture);
    }

    private static string S(double v)
    {
        return v.ToString("R", CultureInfo.InvariantCulture);
    }
}
