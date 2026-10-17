using System;
using System.IO;
using System.Text;
using RealGlass.Water;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The FFT waves (RealGlass/WaterWaves through WaterWaves.cs, as the mod draws them) against the CPU's
/// reference (WaterSpectrum.Fields: the same spectrum, the same steps in double precision), in numbers:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod WaterWavesTest.Run [-force-glcore]
/// writes waves-test-output.txt: per cascade and field the worst difference as a share of that field's
/// root-mean-square, then the composed ripple texture byte by byte against the CPU's composition (its
/// bilinear upsampling, the choppy slopes, the encoding), and its decoded root-mean-square. It is also
/// the probe of the engine calls the mod had never made: a Texture2D RGBAFloat filled by SetPixels, and
/// ARGBFloat render targets read back by ReadPixels into RGBAFloat.
/// </summary>
public static class WaterWavesTest
{
    public static void Run()
    {
        var output = new StringBuilder();
        string dir = Directory.GetCurrentDirectory();
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            output.AppendLine("graphics " + SystemInfo.graphicsDeviceType + " (" + SystemInfo.graphicsDeviceName + "), RGBAFloat textures " +
                              SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat) + ", ARGBFloat targets " +
                              SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat));
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterWaves.shader");
            output.AppendLine("shader " + (shader != null ? shader.name + " supported=" + shader.isSupported : "MISSING"));

            const float hour = 0.3172f, rms = 0.26f, chop = 1.0f;
            var spectrum = new WaterSpectrum(7.0, 50000.0, 30.0, 1u);
            output.AppendLine("spectrum: " + spectrum.WaveCount + " waves, peak " + spectrum.PeakWavelength.ToString("F1") + " m");
            Shader.SetGlobalVector("_SimulationTime2", new Vector4(hour * 3600f, hour, 0f, 0f));

            Debug.Log("WAVES TEST: new WaterWaves");
            var waves = new WaterWaves(shader);
            Debug.Log("WAVES TEST: Upload (Texture2D RGBAFloat SetPixels)");
            waves.Upload(spectrum);
            int size = WaterSpectrum.TextureTexels;
            var target = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                useMipMap = true,
                autoGenerateMips = true,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
            };
            target.Create();
            Debug.Log("WAVES TEST: Draw");
            waves.Draw(target, rms, chop);

            int n = WaterSpectrum.Size;
            var cpu = new double[WaterSpectrum.CascadeCount][][];
            bool ok = true;
            string[] names = { "dh/dx", "dh/dz", "dDx/dx", "dDz/dz", "dDx/dz", "h" };
            for (int c = 0; c < WaterSpectrum.CascadeCount; c++)
            {
                double[] hx, hz, dxx, dzz, dxz, h;
                spectrum.Fields(c, hour, out hx, out hz, out dxx, out dzz, out dxz, out h);
                cpu[c] = new[] { hx, hz, dxx, dzz, dxz, h };
                Debug.Log("WAVES TEST: ReadPixels (ARGBFloat -> RGBAFloat), cascade " + c);
                Color[] a = ReadFloat(waves.FieldsA(c), n);
                Color[] b = ReadFloat(waves.FieldsB(c), n);
                for (int f = 0; f < 6; f++)
                {
                    double sum = 0.0, worst = 0.0;
                    for (int t = 0; t < n * n; t++)
                        sum += cpu[c][f][t] * cpu[c][f][t];
                    double fieldRms = Math.Sqrt(sum / (n * n));
                    for (int t = 0; t < n * n; t++)
                    {
                        float g = f < 4 ? a[t][f] : b[t][f - 4];
                        worst = Math.Max(worst, Math.Abs(g - cpu[c][f][t]));
                    }
                    double share = worst / Math.Max(fieldRms, 1e-12);
                    bool pass = share < 2e-3;
                    ok &= pass;
                    output.AppendLine((pass ? "ok    " : "FAIL  ") + "cascade " + c + " " + names[f] + ": rms " + fieldRms.ToString("E3") +
                                      ", worst GPU - CPU " + (100.0 * share).ToString("F4") + "% of it");
                }
            }

            // The composed texture against the CPU's: cascade 0 bilinear (the patch repeating), cascade 1
            // the texel under, x the strength, the choppy slopes, the encoding.
            Color32[] px = ReadBytes(target, size);
            int off = 0, offTwo = 0;
            double decodedSum = 0.0, expectedSum = 0.0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double[] v = new double[6];
                    double u = (x + 0.5) / size, w = (y + 0.5) / size;
                    double p = u * n - 0.5, q = w * n - 0.5;
                    int x0 = (int)Math.Floor(p), y0 = (int)Math.Floor(q);
                    double fx = p - x0, fy = q - y0;
                    int xa = (x0 + n) % n, xb = (x0 + 1 + n) % n, ya = (y0 + n) % n, yb = (y0 + 1 + n) % n;
                    int i1 = x % n, j1 = y % n;
                    for (int f = 0; f < 6; f++)
                    {
                        double[] g = cpu[0][f];
                        double bottom = g[ya * n + xa] + fx * (g[ya * n + xb] - g[ya * n + xa]);
                        double top = g[yb * n + xa] + fx * (g[yb * n + xb] - g[yb * n + xa]);
                        v[f] = (bottom + fy * (top - bottom) + cpu[1][f][j1 * n + i1]) * rms;
                    }
                    double sx, sz;
                    WaterSpectrum.ChoppySlope(v[0], v[1], v[2], v[3], v[4], chop, out sx, out sz);
                    double mx = Math.Max(-1.0, Math.Min(1.0, -2.0 * sx)), my = Math.Max(-1.0, Math.Min(1.0, 2.0 * sz));
                    int ex = (int)Math.Round((0.5 + 0.5 * mx) * 255.0), ey = (int)Math.Round((0.5 + 0.5 * my) * 255.0);
                    Color32 got = px[y * size + x];
                    int d = Math.Max(Math.Abs(got.a - ex), Math.Abs(got.g - ey));
                    if (d > 1) off++;
                    if (d > 2) offTwo++;
                    double ga = got.a / 255.0 * 2.0 - 1.0, gb = got.g / 255.0 * 2.0 - 1.0;
                    decodedSum += ga * ga + gb * gb;
                    expectedSum += mx * mx + my * my;
                }
            }
            double decoded = Math.Sqrt(decodedSum / (size * size)), expected = Math.Sqrt(expectedSum / (size * size));
            bool composeOk = off < size * size / 1000 && offTwo == 0;
            ok &= composeOk;
            output.AppendLine((composeOk ? "ok    " : "FAIL  ") + "composed texture: " + off + " texels more than 1/255 off the CPU's, " + offTwo + " more than 2/255");
            bool rmsOk = Math.Abs(decoded / expected - 1.0) < 0.01;
            ok &= rmsOk;
            output.AppendLine((rmsOk ? "ok    " : "FAIL  ") + "decoded root-mean-square " + decoded.ToString("F4") + " (CPU " + expected.ToString("F4") +
                              "; without the clamp and the chop 2 x " + rms + " = " + (2f * rms).ToString("F2") + ")");
            // The main thread's cost of a frame's waves (the GPU's is not measured here): 200 draws.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < 200; f++)
            {
                Shader.SetGlobalVector("_SimulationTime2", new Vector4(0f, hour + f * 1e-4f, 0f, 0f));
                waves.Draw(target, rms, chop);
            }
            GL.Flush();
            output.AppendLine("cost: " + (watch.Elapsed.TotalMilliseconds / 200.0).ToString("F3") + " ms of the main thread a frame");
            output.AppendLine(ok ? "ALL OK" : "FAILED");
            waves.Destroy();
            target.Release();
        }
        catch (Exception e)
        {
            output.AppendLine("EXCEPTION " + e);
        }

        File.WriteAllText(Path.Combine(dir, "waves-test-output.txt"), output.ToString());
    }

    private static Color[] ReadFloat(RenderTexture rt, int n)
    {
        var read = new Texture2D(n, n, TextureFormat.RGBAFloat, false, true);
        RenderTexture before = RenderTexture.active;
        RenderTexture.active = rt;
        read.ReadPixels(new Rect(0, 0, n, n), 0, 0, false);
        RenderTexture.active = before;
        Color[] px = read.GetPixels();
        UnityEngine.Object.DestroyImmediate(read);
        return px;
    }

    private static Color32[] ReadBytes(RenderTexture rt, int n)
    {
        var read = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
        RenderTexture before = RenderTexture.active;
        RenderTexture.active = rt;
        read.ReadPixels(new Rect(0, 0, n, n), 0, 0, false);
        RenderTexture.active = before;
        Color32[] px = read.GetPixels32();
        UnityEngine.Object.DestroyImmediate(read);
        return px;
    }
}
