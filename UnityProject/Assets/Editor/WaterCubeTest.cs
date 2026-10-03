using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The water's sky cube (RealGlass/WaterCube) drawn the way WaterSurface does it -- the skybox of a
/// hidden forward camera with no layers, Camera.RenderToCubemap into a 64 px ARGBHalf cube render
/// texture -- and the centre of every face read back (Graphics.CopyTexture to a 2D render texture).
/// (RealLight's test, ported 2026-10-03.) tools\test-reflection-shader.ps1 writes cube-test-input.txt:
/// the table, the cube's uniforms, a theme's cube and a published real sky (one colour per face
/// each, so a face read back names its direction) and that sky's weight; it checks each face against
/// rho x gain x lerp(theme x light, real sky, weight) / light.
///   Unity.exe -batchmode -quit -projectPath . -executeMethod WaterCubeTest.Run
/// </summary>
public static class WaterCubeTest
{
    private static readonly CubemapFace[] Faces =
    {
        CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ,
    };

    public static void Run()
    {
        var output = new StringBuilder();
        string dir = Directory.GetCurrentDirectory();
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/RealGlass/WaterCube.shader");
            output.AppendLine("shader " + (shader != null ? shader.name + " supported=" + shader.isSupported : "MISSING"));
            var material = new Material(shader);
            var table = new Vector4[33];
            var theme = new Color[6];
            var real = new Color[6];
            float weight = 0f;
            bool publish = false;
            foreach (string raw in File.ReadAllLines(Path.Combine(dir, "cube-test-input.txt")))
            {
                string[] p = raw.Split(' ');
                if (p[0] == "table")
                    table[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Vector4(F(p[2]), 0f, 0f, 0f);
                else if (p[0] == "theme")
                    theme[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Color(F(p[2]), F(p[3]), F(p[4]), 1f);
                else if (p[0] == "real")
                {
                    real[int.Parse(p[1], CultureInfo.InvariantCulture)] = new Color(F(p[2]), F(p[3]), F(p[4]), 1f);
                    publish = true;
                }
                else if (p[0] == "weight")
                    weight = F(p[1]);
                else if (p.Length == 5 && p[0].StartsWith("_RGWater"))
                    material.SetVector(p[0], new Vector4(F(p[1]), F(p[2]), F(p[3]), F(p[4])));
            }

            Shader.SetGlobalVectorArray("_RGSeaTable", table);
            material.SetTexture("_RGWaterTheme", Cube(theme));
            if (publish)
            {
                Shader.SetGlobalTexture("_RLSkyLightCube", Cube(real));
                Shader.SetGlobalFloat("_RLSkyLightCubeWeight", weight);
            }

            var cube = new RenderTexture(64, 64, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
            };
            cube.Create();

            var cameraObject = new GameObject("water sky camera");
            Camera capture = cameraObject.AddComponent<Camera>();
            capture.enabled = false;
            capture.clearFlags = CameraClearFlags.Skybox;
            capture.cullingMask = 0;
            capture.allowHDR = true;
            capture.allowMSAA = false;
            capture.renderingPath = RenderingPath.Forward;
            capture.nearClipPlane = 0.3f;
            capture.farClipPlane = 1000f;
            cameraObject.AddComponent<Skybox>().material = material;
            bool rendered = capture.RenderToCubemap(cube);
            output.AppendLine("rendered " + rendered);

            var face = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            face.Create();
            var read = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            foreach (CubemapFace f in Faces)
            {
                Graphics.CopyTexture(cube, (int)f, 0, face, 0, 0);
                RenderTexture.active = face;
                read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                RenderTexture.active = null;
                // The four centre texels' mean: the face's centre lies between them.
                Color c = (read.GetPixel(31, 31) + read.GetPixel(32, 31) + read.GetPixel(31, 32) + read.GetPixel(32, 32)) * 0.25f;
                output.AppendLine("face " + (int)f + " " + S(c.r) + " " + S(c.g) + " " + S(c.b));
            }
        }
        catch (Exception e)
        {
            output.AppendLine("EXCEPTION " + e);
        }

        File.WriteAllText(Path.Combine(dir, "cube-test-output.txt"), output.ToString());
    }

    /// <summary>A float cube, one colour per face (linear: read back exactly).</summary>
    private static Cubemap Cube(Color[] colours)
    {
        var cube = new Cubemap(8, TextureFormat.RGBAFloat, false);
        var pixels = new Color[64];
        for (int f = 0; f < 6; f++)
        {
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = colours[f];
            cube.SetPixels(pixels, Faces[f]);
        }

        cube.Apply(false);
        return cube;
    }

    private static float F(string s)
    {
        return float.Parse(s, CultureInfo.InvariantCulture);
    }

    private static string S(float v)
    {
        return v.ToString("R", CultureInfo.InvariantCulture);
    }
}
