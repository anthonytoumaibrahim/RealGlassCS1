using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Random = System.Random;

/// <summary>
/// Renders the pictured rooms the glass shows behind its windows: twelve rooms modelled here from
/// simple shapes (open-plan offices, a meeting room, small offices, living rooms, a bedroom, a
/// kitchen, a shop, a restaurant, a lobby, an empty floor), each lit twice -- by day from its
/// window, by night by its own lamps -- and captured as a cube from its centre, the six faces in
/// one row of Assets/RealGlass/Rooms/RoomAtlas.png. The glass shader finds, for the point its ray
/// meets on a room's walls, floor or ceiling, the direction from the room's centre and reads that
/// face: interior mapping (van Dongen 2008) with the room's contents painted onto its surfaces
/// from the centre, the way interior cubemaps are (Unreal's InteriorCubemap node; Marvel's
/// Spider-Man's rooms, whose "desks are in fact flat textures on the walls", van Dongen 2018).
/// Everything here is made for this mod: no outside models or textures.
///
/// Run headless with a GPU (no -nographics): tools/bake-rooms.ps1.
///   Unity.exe -batchmode -quit -projectPath UnityProject -executeMethod RoomBaker.Bake
///
/// The face convention is the shader's (GlassCommon.cginc, RoomFace): face f is seen by a camera
/// at the centre looking along Faces[f].Forward with Faces[f].Up up; u runs along the camera's
/// right, v along its up. Rows: room k by day is row 2k, by night 2k + 1, counted from the bottom.
/// Each capture is scaled so the five faces a window can show average a fixed luminance (DayMean,
/// NightMean): the shader divides by the same numbers.
/// </summary>
public static class RoomBaker
{
    public const string AtlasPath = "Assets/RealGlass/Rooms/RoomAtlas.png";

    /// <summary>
    /// A face is Tile x Tile pixels; the atlas is AtlasW x AtlasH (powers of two, so it compresses
    /// to DXT1 everywhere), PerRow tiles a row: capture c = room x 2 + (night ? 1 : 0), face f is
    /// tile t = c x 6 + f at (t % PerRow, t / PerRow), rows counted from the bottom.
    /// </summary>
    public const int Tile = 112, AtlasW = 1024, AtlasH = 2048, PerRow = 9;
    private const int Render = Tile * 4;
    private const int Rooms = 12;

    /// <summary>The modelled room: 12 m along the facade, 3.4 m floor to ceiling, 12 m deep. The window is the z = 0 wall.</summary>
    private const float W = 12f, H = 3.4f, D = 12f;

    /// <summary>What each capture averages over faces 0 and 2..5 (the window's own face excluded), linear.</summary>
    public const float DayMean = 0.25f, NightMean = 0.30f;

    private struct Face
    {
        public Vector3 Forward, Up;

        public Face(Vector3 forward, Vector3 up)
        {
            Forward = forward;
            Up = up;
        }
    }

    // 0 +z the back wall, 1 -z the window wall, 2 +x, 3 -x, 4 +y the ceiling, 5 -y the floor.
    private static readonly Face[] Faces =
    {
        new Face(new Vector3(0f, 0f, 1f), Vector3.up),
        new Face(new Vector3(0f, 0f, -1f), Vector3.up),
        new Face(new Vector3(1f, 0f, 0f), Vector3.up),
        new Face(new Vector3(-1f, 0f, 0f), Vector3.up),
        new Face(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f)),
        new Face(new Vector3(0f, -1f, 0f), new Vector3(0f, 0f, 1f)),
    };

    // The camera's right for each face, as the shader has it: u = dot(d, Right) / dot(d, Forward).
    private static readonly Vector3[] Rights =
    {
        new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, -1f),
        new Vector3(0f, 0f, 1f), new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
    };

    private static readonly string[] Names =
    {
        "open-plan office", "office with desk clusters", "meeting room", "small offices", "living room",
        "living and dining room", "bedroom", "kitchen", "shop", "restaurant", "lobby", "empty floor",
    };

    private static Transform _root;
    private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
    private static Shader _standard;

    public static void Bake()
    {
        try
        {
            BakeAll();
            Debug.Log("ROOM BAKE OK");
        }
        catch (Exception e)
        {
            Debug.LogError("ROOM BAKE FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    private static void BakeAll()
    {
        if (PlayerSettings.colorSpace != ColorSpace.Linear)
            PlayerSettings.colorSpace = ColorSpace.Linear;
        _standard = Shader.Find("Standard");
        if (_standard == null)
            throw new Exception("the Standard shader is not found");

        QualitySettings.pixelLightCount = 48;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowDistance = 40f;
        QualitySettings.shadowCascades = 1;
        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Flat;

        GameObject camObject = new GameObject("RoomBakerCamera");
        Camera cam = camObject.AddComponent<Camera>();
        cam.renderingPath = RenderingPath.Forward;
        cam.allowHDR = true;
        cam.allowMSAA = true;
        cam.fieldOfView = 90f;
        cam.aspect = 1f;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 60f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        var msaa = new RenderTexture(Render, Render, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        msaa.antiAliasing = 8;
        msaa.Create();
        var resolved = new RenderTexture(Render, Render, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        resolved.Create();
        cam.targetTexture = msaa;
        var readback = new Texture2D(Render, Render, TextureFormat.RGBAFloat, false, true);

        // The shader's face convention, checked against the camera Unity builds.
        for (int f = 0; f < Faces.Length; f++)
        {
            cam.transform.rotation = Quaternion.LookRotation(Faces[f].Forward, Faces[f].Up);
            if (Vector3.Distance(cam.transform.right, Rights[f]) > 1e-3f || Vector3.Distance(cam.transform.forward, Faces[f].Forward) > 1e-3f)
                throw new Exception("face " + f + ": the camera's right is " + cam.transform.right + ", the shader expects " + Rights[f]);
        }

        if ((Rooms * 2 * 6 + PerRow - 1) / PerRow * Tile > AtlasH || PerRow * Tile > AtlasW)
            throw new Exception("the rooms do not fit the atlas");
        var atlas = new Color32[AtlasW * AtlasH];
        for (int i = 0; i < atlas.Length; i++)
            atlas[i] = new Color32(40, 40, 40, 255);

        for (int k = 0; k < Rooms; k++)
        {
            for (int night = 0; night < 2; night++)
            {
                BuildRoom(k, night == 1);
                cam.transform.position = new Vector3(W * 0.5f, H * 0.5f, D * 0.5f);

                var faces = new Color[6][];
                for (int f = 0; f < 6; f++)
                {
                    cam.transform.rotation = Quaternion.LookRotation(Faces[f].Forward, Faces[f].Up);
                    cam.Render();
                    Graphics.Blit(msaa, resolved);
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = resolved;
                    readback.ReadPixels(new Rect(0, 0, Render, Render), 0, 0, false);
                    readback.Apply(false);
                    RenderTexture.active = previous;
                    faces[f] = Downsample(readback.GetPixels(), Render, Render / Tile);
                }

                // The same average for every capture: faces 0 and 2..5 (a window never shows face 1
                // straight on, only its floor and ceiling edges).
                double sum = 0.0;
                int n = 0;
                for (int f = 0; f < 6; f++)
                {
                    if (f == 1)
                        continue;
                    foreach (Color c in faces[f])
                    {
                        sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                        n++;
                    }
                }

                float mean = (float)(sum / Math.Max(n, 1));
                float scale = (night == 1 ? NightMean : DayMean) / Mathf.Max(mean, 1e-5f);
                int capture = k * 2 + night;
                for (int f = 0; f < 6; f++)
                {
                    int t = capture * 6 + f;
                    int ox = (t % PerRow) * Tile, oy = (t / PerRow) * Tile;
                    for (int y = 0; y < Tile; y++)
                    {
                        for (int x = 0; x < Tile; x++)
                        {
                            Color c = faces[f][y * Tile + x] * scale;
                            c = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f).gamma;
                            atlas[(oy + y) * AtlasW + ox + x] = c;
                        }
                    }
                }

                Debug.Log("ROOM " + k + " " + Names[k] + (night == 1 ? " by night" : " by day") + ": mean " + mean.ToString("F4") + " x " + scale.ToString("F2"));
                ClearRoom();
            }
        }

        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(camObject);
        UnityEngine.Object.DestroyImmediate(msaa);
        UnityEngine.Object.DestroyImmediate(resolved);

        var png = new Texture2D(AtlasW, AtlasH, TextureFormat.RGB24, false, false);
        png.SetPixels32(atlas);
        png.Apply(false);
        Directory.CreateDirectory(Path.GetDirectoryName(AtlasPath));
        File.WriteAllBytes(AtlasPath, png.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(png);

        AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 1;
        importer.isReadable = false;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Standalone",
            overridden = true,
            maxTextureSize = 4096,
            format = TextureImporterFormat.DXT1,
            textureCompression = TextureImporterCompression.CompressedHQ,
        });
        importer.SaveAndReimport();

        Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        Debug.Log("ROOM ATLAS " + AtlasPath + ": " + (imported != null ? imported.width + "x" + imported.height + " " + imported.format + ", " + imported.mipmapCount + " mips" : "NOT IMPORTED"));
        if (imported == null)
            throw new Exception("the atlas did not import");
    }

    private static Color[] Downsample(Color[] src, int size, int factor)
    {
        int n = size / factor;
        var dst = new Color[n * n];
        float w = 1f / (factor * factor);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                Color c = Color.clear;
                for (int j = 0; j < factor; j++)
                    for (int i = 0; i < factor; i++)
                        c += src[(y * factor + j) * size + x * factor + i];
                dst[y * n + x] = c * w;
            }
        }
        return dst;
    }

    // -----------------------------------------------------------------------------------------
    // Building blocks.
    // -----------------------------------------------------------------------------------------

    private static Material Mat(Color albedo, float gloss, Color emission)
    {
        string key = albedo.ToString("F3") + gloss.ToString("F2") + emission.ToString("F3");
        Material m;
        if (Materials.TryGetValue(key, out m))
            return m;
        m = new Material(_standard);
        m.SetColor("_Color", albedo);
        m.SetFloat("_Glossiness", gloss);
        m.SetFloat("_Metallic", 0f);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        Materials[key] = m;
        return m;
    }

    private static Material Mat(Color albedo, float gloss)
    {
        return Mat(albedo, gloss, Color.black);
    }

    private static Material Glow(Color colour, float strength)
    {
        return Mat(colour * 0.8f, 0.1f, colour * strength);
    }

    private static GameObject Shape(PrimitiveType type, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(_root, false);
        MeshRenderer r = g.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.On;
        r.receiveShadows = true;
        return g;
    }

    /// <summary>An axis-aligned box from corner a to corner b.</summary>
    private static GameObject Box(float x0, float y0, float z0, float x1, float y1, float z1, Material m)
    {
        GameObject g = Shape(PrimitiveType.Cube, m);
        g.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        g.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
        return g;
    }

    /// <summary>A box centred on (x, z), standing on y0.</summary>
    private static GameObject Block(float x, float z, float sx, float sz, float y0, float y1, Material m)
    {
        return Box(x - sx * 0.5f, y0, z - sz * 0.5f, x + sx * 0.5f, y1, z + sz * 0.5f, m);
    }

    private static GameObject Cylinder(float x, float z, float radius, float y0, float y1, Material m)
    {
        GameObject g = Shape(PrimitiveType.Cylinder, m);
        g.transform.localPosition = new Vector3(x, (y0 + y1) * 0.5f, z);
        g.transform.localScale = new Vector3(radius * 2f, (y1 - y0) * 0.5f, radius * 2f);
        return g;
    }

    private static GameObject Ball(float x, float y, float z, float radius, Material m)
    {
        GameObject g = Shape(PrimitiveType.Sphere, m);
        g.transform.localPosition = new Vector3(x, y, z);
        g.transform.localScale = Vector3.one * radius * 2f;
        return g;
    }

    private static void Lamp(float x, float y, float z, float range, float intensity, bool shadows)
    {
        var g = new GameObject("lamp");
        g.transform.SetParent(_root, false);
        g.transform.localPosition = new Vector3(x, y, z);
        Light l = g.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = Color.white;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        l.shadowStrength = 0.85f;
        l.renderMode = LightRenderMode.ForcePixel;
    }

    private static void ClearRoom()
    {
        if (_root != null)
            UnityEngine.Object.DestroyImmediate(_root.gameObject);
        _root = null;
    }

    private static Color Rgb(float r, float g, float b)
    {
        return new Color(r, g, b, 1f);
    }

    private static Color Pick(Random rnd, Color[] from)
    {
        return from[rnd.Next(from.Length)];
    }

    private static readonly Color[] Clothes =
    {
        Rgb(0.10f, 0.12f, 0.18f), Rgb(0.55f, 0.55f, 0.58f), Rgb(0.60f, 0.18f, 0.15f), Rgb(0.15f, 0.30f, 0.50f),
        Rgb(0.85f, 0.85f, 0.82f), Rgb(0.20f, 0.35f, 0.25f), Rgb(0.65f, 0.55f, 0.35f), Rgb(0.08f, 0.08f, 0.08f),
    };

    private static readonly Color[] Skin =
    {
        Rgb(0.80f, 0.62f, 0.50f), Rgb(0.62f, 0.44f, 0.32f), Rgb(0.42f, 0.28f, 0.20f), Rgb(0.90f, 0.72f, 0.60f),
    };

    private static readonly Color[] Bright =
    {
        Rgb(0.75f, 0.20f, 0.18f), Rgb(0.20f, 0.45f, 0.75f), Rgb(0.90f, 0.72f, 0.20f), Rgb(0.25f, 0.60f, 0.35f),
        Rgb(0.85f, 0.85f, 0.80f), Rgb(0.55f, 0.30f, 0.60f), Rgb(0.90f, 0.50f, 0.20f), Rgb(0.15f, 0.15f, 0.18f),
    };

    /// <summary>A person, facing +z or -z: standing, or seated on something 0.45 m high.</summary>
    private static void Person(Random rnd, float x, float z, bool seated, float facing)
    {
        Material cloth = Mat(Pick(rnd, Clothes), 0.1f);
        Material legs = Mat(Pick(rnd, Clothes) * 0.6f, 0.1f);
        Material skin = Mat(Pick(rnd, Skin), 0.3f);
        Material hair = Mat(Rgb(0.08f, 0.06f, 0.05f) + Rgb(0.3f, 0.2f, 0.1f) * (float)rnd.NextDouble(), 0.2f);
        if (seated)
        {
            Block(x, z + 0.18f * facing, 0.34f, 0.44f, 0.45f, 0.58f, legs);
            Block(x, z + 0.42f * facing, 0.30f, 0.12f, 0.02f, 0.5f, legs);
            GameObject body = Shape(PrimitiveType.Capsule, cloth);
            body.transform.localPosition = new Vector3(x, 0.92f, z - 0.02f * facing);
            body.transform.localScale = new Vector3(0.40f, 0.36f, 0.26f);
            Ball(x, 1.36f, z, 0.11f, skin);
            Ball(x, 1.40f, z - 0.02f * facing, 0.105f, hair);
        }
        else
        {
            Block(x, z, 0.32f, 0.2f, 0f, 0.86f, legs);
            GameObject body = Shape(PrimitiveType.Capsule, cloth);
            body.transform.localPosition = new Vector3(x, 1.20f, z);
            body.transform.localScale = new Vector3(0.42f, 0.38f, 0.26f);
            Ball(x, 1.64f, z, 0.11f, skin);
            Ball(x, 1.68f, z - 0.02f * facing, 0.105f, hair);
        }
    }

    private static void Plant(Random rnd, float x, float z, float size)
    {
        Cylinder(x, z, 0.22f * size, 0f, 0.42f * size, Mat(Rgb(0.82f, 0.80f, 0.76f), 0.3f));
        Material leaves = Mat(Rgb(0.10f, 0.28f + 0.1f * (float)rnd.NextDouble(), 0.08f), 0.15f);
        Ball(x, 0.75f * size, z, 0.38f * size, leaves);
        Ball(x + 0.18f * size, 1.05f * size, z - 0.1f * size, 0.30f * size, leaves);
        Ball(x - 0.15f * size, 1.20f * size, z + 0.08f * size, 0.26f * size, leaves);
    }

    private static void Chair(float x, float z, float facing, Material m)
    {
        Block(x, z, 0.48f, 0.46f, 0.42f, 0.48f, m);
        Block(x, z - 0.22f * facing, 0.46f, 0.06f, 0.48f, 1.0f, m);
        Cylinder(x, z, 0.03f, 0f, 0.42f, Mat(Rgb(0.1f, 0.1f, 0.1f), 0.5f));
        Block(x, z, 0.5f, 0.5f, 0.02f, 0.05f, Mat(Rgb(0.1f, 0.1f, 0.1f), 0.5f));
    }

    /// <summary>A bookcase against the back wall (z = D), from x0 to x1, 0.4 m deep.</summary>
    private static void ShelfOnBack(Random rnd, float x0, float x1, float height, Material wood)
    {
        float z0 = D - 0.4f;
        Box(x0, 0f, D - 0.03f, x1, height, D, wood);
        Box(x0, 0f, z0, x0 + 0.03f, height, D, wood);
        Box(x1 - 0.03f, 0f, z0, x1, height, D, wood);
        for (float y = 0.02f; y < height; y += 0.38f)
        {
            Box(x0, y, z0, x1, y + 0.03f, D, wood);
            if (y + 0.4f >= height)
                continue;
            float x = x0 + 0.04f;
            while (x < x1 - 0.08f)
            {
                float w = 0.03f + 0.05f * (float)rnd.NextDouble();
                float h = 0.20f + 0.12f * (float)rnd.NextDouble();
                Box(x, y + 0.03f, z0 + 0.06f, Mathf.Min(x + w, x1 - 0.04f), y + 0.03f + h, D - 0.04f, Mat(Pick(rnd, Bright) * (0.6f + 0.4f * (float)rnd.NextDouble()), 0.2f));
                x += w + 0.005f + (rnd.NextDouble() < 0.08 ? 0.2f : 0f);
            }
        }
    }

    /// <summary>A bookcase against a side wall (left: x = 0, else x = W), from z0 to z1, 0.4 m deep.</summary>
    private static void ShelfOnSide(Random rnd, float z0, float z1, bool left, float height, Material wood)
    {
        float wall = left ? 0f : W, front = left ? 0.4f : W - 0.4f;
        float a = Mathf.Min(wall, front), b = Mathf.Max(wall, front);
        Box(left ? 0f : W - 0.03f, 0f, z0, left ? 0.03f : W, height, z1, wood);
        for (float y = 0.02f; y < height; y += 0.38f)
        {
            Box(a, y, z0, b, y + 0.03f, z1, wood);
            if (y + 0.4f >= height)
                continue;
            float z = z0 + 0.04f;
            while (z < z1 - 0.08f)
            {
                float w = 0.03f + 0.05f * (float)rnd.NextDouble();
                float h = 0.20f + 0.12f * (float)rnd.NextDouble();
                Box(a + 0.04f, y + 0.03f, z, b - 0.06f, y + 0.03f + h, Mathf.Min(z + w, z1 - 0.04f), Mat(Pick(rnd, Bright) * (0.6f + 0.4f * (float)rnd.NextDouble()), 0.2f));
                z += w + 0.005f + (rnd.NextDouble() < 0.08 ? 0.2f : 0f);
            }
        }
    }

    private static void Picture(Random rnd, float x, float y, float z, float w, float h, bool onBackWall)
    {
        Material frame = Mat(Rgb(0.1f, 0.1f, 0.1f), 0.4f);
        Material art = Mat(Pick(rnd, Bright), 0.3f);
        if (onBackWall)
        {
            Box(x - w * 0.5f, y - h * 0.5f, z - 0.03f, x + w * 0.5f, y + h * 0.5f, z, frame);
            Box(x - w * 0.45f, y - h * 0.45f, z - 0.04f, x + w * 0.45f, y + h * 0.45f, z - 0.03f, art);
        }
        else
        {
            float side = z;   // x of the wall here: the caller passes the wall's x as z
            float dir = side < 1f ? 1f : -1f;
            Box(side, y - h * 0.5f, x - w * 0.5f, side + 0.03f * dir, y + h * 0.5f, x + w * 0.5f, frame);
            Box(side + 0.03f * dir, y - h * 0.45f, x - w * 0.45f, side + 0.04f * dir, y + h * 0.45f, x + w * 0.45f, art);
        }
    }

    /// <summary>
    /// The room's shell: floor, ceiling, walls, and the window wall -- glass from floor to ceiling
    /// between mullions, the sky's light by day, dark by night.
    /// </summary>
    private static void Shell(Material floor, Material walls, Material back, Material ceiling, bool night)
    {
        Box(-0.2f, -0.2f, -0.2f, W + 0.2f, 0f, D + 0.2f, floor);
        Box(-0.2f, H, -0.2f, W + 0.2f, H + 0.2f, D + 0.2f, ceiling);
        Box(-0.2f, 0f, 0f, 0f, H, D, walls);
        Box(W, 0f, 0f, W + 0.2f, H, D, walls);
        Box(0f, 0f, D, W, H, D + 0.2f, back);
        Material glass = night ? Mat(Rgb(0.01f, 0.012f, 0.02f), 0.9f) : Glow(Rgb(0.80f, 0.86f, 0.95f), 1.6f);
        Box(0f, 0f, -0.25f, W, H, -0.2f, glass);
        Material mullion = Mat(Rgb(0.22f, 0.23f, 0.25f), 0.4f);
        for (float x = 0f; x <= W + 0.01f; x += 1.5f)
            Box(x - 0.04f, 0f, -0.2f, x + 0.04f, H, -0.05f, mullion);
        Box(0f, 0f, -0.2f, W, 0.08f, -0.02f, mullion);
    }

    /// <summary>Daylight through the window: soft lights along it, strong at the glass and falling off to the back.</summary>
    private static void Daylight()
    {
        RenderSettings.ambientLight = Rgb(0.20f, 0.22f, 0.25f);
        for (float x = 1.5f; x < W; x += 3f)
            Lamp(x, H * 0.72f, 0.6f, 14f, 1.1f, x > 4f && x < 8f);
        Lamp(W * 0.5f, H * 0.5f, -1.5f, 18f, 0.8f, false);
    }

    /// <summary>By night: the room's own lamps, and the light they bounce round it (a flat ambient: a lit room is never pitch dark in its corners).</summary>
    private static void Dark()
    {
        RenderSettings.ambientLight = Rgb(0.05f, 0.05f, 0.05f);
    }

    /// <summary>A grid of office ceiling panels, 0.6 x 1.2 m every 2.4 x 1.8 m, and their light at night.</summary>
    private static void CeilingPanels(bool night, float strength)
    {
        Material panel = night ? Glow(Rgb(1f, 1f, 1f), 2.0f * strength) : Mat(Rgb(0.92f, 0.92f, 0.90f), 0.2f);
        for (float x = 1.2f; x < W; x += 2.4f)
        {
            for (float z = 0.9f; z < D; z += 1.8f)
            {
                Box(x - 0.3f, H - 0.02f, z - 0.6f, x + 0.3f, H, z + 0.6f, panel);
                if (night && ((int)(z / 1.8f) % 2 == 0))
                    Lamp(x, H - 0.25f, z + 0.9f, 5.5f, 1.5f * strength, false);
            }
        }
    }

    private static void Downlights(bool night, float spacing, float strength)
    {
        Material light = night ? Glow(Rgb(1f, 1f, 1f), 4f * strength) : Mat(Rgb(0.9f, 0.9f, 0.9f), 0.5f);
        for (float x = spacing * 0.5f; x < W; x += spacing)
        {
            for (float z = spacing * 0.5f; z < D; z += spacing)
            {
                Cylinder(x, z, 0.1f, H - 0.02f, H, light);
                if (night)
                    Lamp(x, H - 0.3f, z, spacing * 1.6f, 0.75f * strength, false);
            }
        }
    }

    private static void Pendant(float x, float z, float y, bool night, float strength)
    {
        Cylinder(x, z, 0.01f, y + 0.2f, H, Mat(Rgb(0.1f, 0.1f, 0.1f), 0.5f));
        Material shade = night ? Glow(Rgb(1f, 0.98f, 0.95f), 3f * strength) : Mat(Rgb(0.85f, 0.83f, 0.78f), 0.4f);
        Cylinder(x, z, 0.22f, y, y + 0.2f, shade);
        if (night)
            Lamp(x, y - 0.1f, z, 4.5f, 1.2f * strength, true);
    }

    private static void FloorLamp(float x, float z, bool night)
    {
        Cylinder(x, z, 0.02f, 0f, 1.5f, Mat(Rgb(0.15f, 0.15f, 0.15f), 0.6f));
        Material shade = night ? Glow(Rgb(1f, 0.95f, 0.85f), 2.5f) : Mat(Rgb(0.88f, 0.84f, 0.75f), 0.2f);
        Cylinder(x, z, 0.22f, 1.45f, 1.75f, shade);
        if (night)
            Lamp(x, 1.55f, z, 5f, 1.4f, true);
    }

    private static void TableLamp(float x, float y, float z, bool night)
    {
        TableLamp(x, y, z, night, 1f);
    }

    private static void TableLamp(float x, float y, float z, bool night, float power)
    {
        Cylinder(x, z, 0.06f, y, y + 0.3f, Mat(Rgb(0.7f, 0.65f, 0.55f), 0.4f));
        Material shade = night ? Glow(Rgb(1f, 0.95f, 0.85f), 2.5f) : Mat(Rgb(0.9f, 0.86f, 0.78f), 0.2f);
        Cylinder(x, z, 0.14f, y + 0.3f, y + 0.5f, shade);
        if (night)
            Lamp(x, y + 0.42f, z, 3.2f + 1.5f * (power - 1f), power, power > 1.5f);
    }

    private static void Monitor(float x, float z, float facing, bool night)
    {
        Material screen = night ? Mat(Rgb(0.05f, 0.06f, 0.08f), 0.8f, Rgb(0.10f, 0.14f, 0.20f)) : Mat(Rgb(0.04f, 0.04f, 0.05f), 0.8f);
        Block(x, z, 0.56f, 0.03f, 0.95f, 1.30f, screen);
        Block(x, z + 0.06f * facing, 0.08f, 0.04f, 0.75f, 0.95f, Mat(Rgb(0.2f, 0.2f, 0.22f), 0.5f));
    }

    // -----------------------------------------------------------------------------------------
    // The rooms.
    // -----------------------------------------------------------------------------------------

    private static void BuildRoom(int k, bool night)
    {
        ClearRoom();
        _root = new GameObject("room " + k).transform;
        var rnd = new Random(1000 + k);
        if (night)
            Dark();
        else
            Daylight();

        switch (k)
        {
            case 0: OpenPlan(rnd, night); break;
            case 1: Clusters(rnd, night); break;
            case 2: Meeting(rnd, night); break;
            case 3: SmallOffices(rnd, night); break;
            case 4: Living(rnd, night, Rgb(0.30f, 0.36f, 0.45f), Rgb(0.55f, 0.20f, 0.18f)); break;
            case 5: LivingDining(rnd, night); break;
            case 6: Bedroom(rnd, night); break;
            case 7: Kitchen(rnd, night); break;
            case 8: Shop(rnd, night); break;
            case 9: Restaurant(rnd, night); break;
            case 10: Lobby(rnd, night); break;
            default: Empty(rnd, night); break;
        }
    }

    private static void OpenPlan(Random rnd, bool night)
    {
        Material carpet = Mat(Rgb(0.26f, 0.27f, 0.29f), 0.05f);
        Material wall = Mat(Rgb(0.78f, 0.78f, 0.76f), 0.1f);
        Shell(carpet, wall, Mat(Rgb(0.30f, 0.42f, 0.52f), 0.1f), Mat(Rgb(0.86f, 0.86f, 0.85f), 0.05f), night);
        CeilingPanels(night, 1f);
        Material top = Mat(Rgb(0.86f, 0.86f, 0.84f), 0.35f);
        Material leg = Mat(Rgb(0.3f, 0.3f, 0.32f), 0.5f);
        Material seat = Mat(Rgb(0.12f, 0.12f, 0.14f), 0.2f);
        foreach (float zc in new[] { 2.6f, 9.4f })
        {
            for (float x = 1.2f; x < W - 0.8f; x += 1.7f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float z = zc + side * 0.42f;
                    Block(x, z, 1.6f, 0.8f, 0.72f, 0.75f, top);
                    Block(x - 0.75f, z, 0.04f, 0.7f, 0f, 0.72f, leg);
                    Block(x + 0.75f, z, 0.04f, 0.7f, 0f, 0.72f, leg);
                    Monitor(x, zc + side * 0.05f, -side, night);
                    float cz = z + side * 0.65f;
                    Chair(x, cz, -side, seat);
                    if (rnd.NextDouble() < (night ? 0.2 : 0.35))
                        Person(rnd, x, cz, true, -side);
                }
            }
        }

        Box(1f, 0f, D - 0.05f, 2.1f, 2.2f, D, Mat(Rgb(0.35f, 0.25f, 0.18f), 0.3f));
        Box(4f, 1.0f, D - 0.03f, 7f, 2.1f, D, Mat(Rgb(0.95f, 0.95f, 0.95f), 0.6f));
        Plant(rnd, 0.6f, 6f, 1.2f);
        Plant(rnd, W - 0.6f, 6f, 1.2f);
        Plant(rnd, W - 0.7f, D - 0.7f, 1.4f);
        Person(rnd, 8.5f, 6.4f, false, 1f);
    }

    private static void Clusters(Random rnd, bool night)
    {
        Material carpet = Mat(Rgb(0.20f, 0.24f, 0.30f), 0.05f);
        Material wall = Mat(Rgb(0.70f, 0.70f, 0.68f), 0.1f);
        Shell(carpet, wall, wall, Mat(Rgb(0.80f, 0.80f, 0.80f), 0.05f), night);
        Downlights(night, 2f, 1f);
        Material wood = Mat(Rgb(0.55f, 0.40f, 0.26f), 0.35f);
        Material screen = Mat(Rgb(0.20f, 0.42f, 0.36f), 0.05f);
        Material seat = Mat(Rgb(0.45f, 0.12f, 0.10f), 0.2f);
        for (float x = 2.2f; x < W; x += 3.8f)
        {
            foreach (float z in new[] { 3.0f, 8.8f })
            {
                Block(x, z, 3.2f, 1.6f, 0.72f, 0.75f, wood);
                Block(x, z, 3.2f, 0.04f, 0.75f, 1.15f, screen);
                Block(x, z, 0.04f, 1.6f, 0.75f, 1.15f, screen);
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Monitor(x + sx * 0.8f, z + sz * 0.3f, sz, night);
                        Chair(x + sx * 0.8f, z + sz * 1.1f, -sz, seat);
                        if (rnd.NextDouble() < (night ? 0.2 : 0.4))
                            Person(rnd, x + sx * 0.8f, z + sz * 1.1f, true, -sz);
                    }
                }
            }
        }

        Material cabinet = Mat(Rgb(0.55f, 0.56f, 0.58f), 0.3f);
        Box(0.2f, 0f, D - 0.6f, 7.5f, 1.2f, D, cabinet);
        Box(8.2f, 0f, D - 0.65f, W - 0.2f, 0.9f, D, Mat(Rgb(0.85f, 0.85f, 0.83f), 0.5f));
        Box(8.2f, 1.5f, D - 0.35f, W - 0.2f, 2.2f, D, Mat(Rgb(0.85f, 0.85f, 0.83f), 0.5f));
        Plant(rnd, 0.6f, 0.8f, 1.1f);
        Plant(rnd, 7.8f, D - 0.8f, 1.3f);
        Person(rnd, 9.5f, 10.6f, false, 1f);
    }

    private static void Meeting(Random rnd, bool night)
    {
        Material floor = Mat(Rgb(0.34f, 0.27f, 0.20f), 0.3f);
        Material wall = Mat(Rgb(0.74f, 0.72f, 0.68f), 0.1f);
        Shell(floor, wall, Mat(Rgb(0.24f, 0.26f, 0.30f), 0.1f), Mat(Rgb(0.84f, 0.84f, 0.83f), 0.05f), night);
        Material table = Mat(Rgb(0.22f, 0.16f, 0.12f), 0.5f);
        const float tz = 8.6f;
        Block(6f, tz, 7f, 1.6f, 0.72f, 0.76f, table);
        Block(3.5f, tz, 0.2f, 0.8f, 0f, 0.72f, table);
        Block(8.5f, tz, 0.2f, 0.8f, 0f, 0.72f, table);
        Material seat = Mat(Rgb(0.10f, 0.10f, 0.11f), 0.3f);
        for (float x = 3.3f; x < 9f; x += 1.1f)
        {
            Chair(x, tz - 1.1f, 1f, seat);
            Chair(x, tz + 1.1f, -1f, seat);
            if (rnd.NextDouble() < 0.5)
                Person(rnd, x, tz - 1.1f, true, 1f);
            if (rnd.NextDouble() < 0.5)
                Person(rnd, x, tz + 1.1f, true, -1f);
        }

        Material screen = night ? Mat(Rgb(0.05f, 0.06f, 0.08f), 0.8f, Rgb(0.25f, 0.35f, 0.55f)) : Mat(Rgb(0.03f, 0.03f, 0.04f), 0.8f);
        Box(4.2f, 1.1f, D - 0.06f, 7.8f, 3.1f, D, screen);
        Box(W - 0.03f, 1.0f, 3f, W, 2.2f, 7f, Mat(Rgb(0.95f, 0.95f, 0.95f), 0.6f));
        Box(0.4f, 0f, D - 0.5f, 3.0f, 0.8f, D, table);
        for (float x = 4f; x <= 8f; x += 2f)
            Pendant(x, tz, 2.1f, night, 1f);
        Downlights(night, 3f, 0.5f);
        Plant(rnd, 0.6f, 0.8f, 1.2f);
        Plant(rnd, W - 0.6f, D - 0.7f, 1.2f);
        Picture(rnd, 6f, 2f, 0f, 2.4f, 1.2f, false);
    }

    private static void SmallOffices(Random rnd, bool night)
    {
        Material carpet = Mat(Rgb(0.30f, 0.28f, 0.26f), 0.05f);
        Material wall = Mat(Rgb(0.80f, 0.79f, 0.76f), 0.1f);
        Shell(carpet, wall, wall, Mat(Rgb(0.85f, 0.85f, 0.84f), 0.05f), night);
        CeilingPanels(night, 0.8f);
        Material partition = Mat(Rgb(0.72f, 0.72f, 0.70f), 0.1f);
        Material wood = Mat(Rgb(0.45f, 0.32f, 0.22f), 0.4f);
        Material seat = Mat(Rgb(0.12f, 0.12f, 0.12f), 0.3f);
        foreach (float px in new[] { 4f, 8f })
            Box(px - 0.05f, 0f, 2.5f, px + 0.05f, H, D, partition);
        for (int cell = 0; cell < 3; cell++)
        {
            float cx = cell * 4f + 2f;
            Block(cx, 3.4f, 1.8f, 0.8f, 0.72f, 0.76f, wood);
            Block(cx, 3.4f, 1.7f, 0.05f, 0f, 0.72f, wood);
            Monitor(cx, 3.7f, -1f, night);
            Chair(cx, 4.3f, -1f, seat);
            if (rnd.NextDouble() < 0.6)
                Person(rnd, cx, 4.3f, true, -1f);
            ShelfOnBack(rnd, cx - 1.6f, cx + 1.6f, 2.2f, wood);
            Block(cx + 1.2f, 9.5f, 0.8f, 0.8f, 0f, 0.45f, Mat(Pick(rnd, Clothes), 0.1f));
            TableLamp(cx - 0.6f, 0.76f, 3.2f, night);
            Picture(rnd, 7.5f, 1.8f, px(cell), 0.8f, 0.6f, false);
        }
        Plant(rnd, 0.6f, 1.2f, 1.1f);
        Plant(rnd, W - 0.6f, 1.2f, 1.1f);
    }

    private static float px(int cell)
    {
        return cell == 0 ? 0.01f : (cell == 1 ? 4.06f : 8.06f);
    }

    private static void Living(Random rnd, bool night, Color sofa, Color rug)
    {
        Material floor = Mat(Rgb(0.44f, 0.31f, 0.20f), 0.35f);
        Material wall = Mat(Rgb(0.82f, 0.78f, 0.70f), 0.1f);
        Shell(floor, wall, Mat(Rgb(0.60f, 0.52f, 0.42f), 0.1f), Mat(Rgb(0.88f, 0.86f, 0.82f), 0.05f), night);
        Box(2.5f, 0f, 3.5f, 8.5f, 0.015f, 8.5f, Mat(rug, 0.05f));
        Material fabric = Mat(sofa, 0.05f);
        Box(2.8f, 0.2f, 7.6f, 8.2f, 0.45f, 8.6f, fabric);
        Box(2.8f, 0.45f, 8.3f, 8.2f, 0.9f, 8.6f, fabric);
        Box(2.8f, 0.2f, 5.2f, 3.8f, 0.45f, 8.6f, fabric);
        Box(2.8f, 0.45f, 5.2f, 3.1f, 0.9f, 8.6f, fabric);
        Material wood = Mat(Rgb(0.30f, 0.20f, 0.13f), 0.4f);
        Block(5.8f, 5.8f, 1.6f, 0.9f, 0.3f, 0.4f, wood);
        Block(6f, D - 0.3f, 3.2f, 0.5f, 0f, 0.5f, wood);
        Material tv = night ? Mat(Rgb(0.03f, 0.03f, 0.04f), 0.9f, Rgb(0.18f, 0.24f, 0.35f)) : Mat(Rgb(0.02f, 0.02f, 0.03f), 0.9f);
        Box(4.6f, 0.9f, D - 0.1f, 7.4f, 2.4f, D - 0.05f, tv);
        ShelfOnSide(rnd, 2f, 5f, true, 2.4f, wood);
        ShelfOnBack(rnd, 9.5f, W - 0.05f, 2.2f, wood);
        FloorLamp(8.8f, 8.4f, night);
        TableLamp(1.2f, 0.5f, 10.8f, night);
        Block(1.2f, 10.8f, 0.6f, 0.6f, 0f, 0.5f, wood);
        Pendant(7.5f, 7.5f, 2.5f, night, 0.8f);
        Plant(rnd, W - 0.7f, 1.2f, 1.3f);
        Plant(rnd, 0.7f, D - 0.8f, 1.0f);
        Picture(rnd, 2.5f, 1.9f, D, 1.0f, 0.8f, true);
        Picture(rnd, 9.5f, 1.8f, W - 0.01f, 1.2f, 0.9f, false);
        Box(0.1f, 0f, 0.05f, 0.9f, H, 0.25f, Mat(Rgb(0.75f, 0.70f, 0.60f), 0.05f));
        Box(W - 0.9f, 0f, 0.05f, W - 0.1f, H, 0.25f, Mat(Rgb(0.75f, 0.70f, 0.60f), 0.05f));
        if (rnd.NextDouble() < 0.8)
            Person(rnd, 5.5f, 8.1f, true, -1f);
    }

    private static void LivingDining(Random rnd, bool night)
    {
        Material floor = Mat(Rgb(0.58f, 0.55f, 0.50f), 0.3f);
        Material wall = Mat(Rgb(0.86f, 0.86f, 0.84f), 0.1f);
        Shell(floor, wall, Mat(Rgb(0.35f, 0.45f, 0.40f), 0.1f), Mat(Rgb(0.90f, 0.90f, 0.88f), 0.05f), night);
        Box(1f, 0f, 4f, 5.5f, 0.015f, 9f, Mat(Rgb(0.20f, 0.30f, 0.45f), 0.05f));
        Material fabric = Mat(Rgb(0.70f, 0.66f, 0.58f), 0.05f);
        Box(1.2f, 0.2f, 8.2f, 5.2f, 0.45f, 9.1f, fabric);
        Box(1.2f, 0.45f, 8.8f, 5.2f, 0.9f, 9.1f, fabric);
        Material chairFabric = Mat(Rgb(0.25f, 0.45f, 0.30f), 0.05f);
        Block(1.8f, 5.2f, 0.8f, 0.8f, 0.2f, 0.45f, chairFabric);
        Block(1.8f, 4.85f, 0.8f, 0.12f, 0.45f, 0.9f, chairFabric);
        Material wood = Mat(Rgb(0.50f, 0.36f, 0.24f), 0.4f);
        Block(3.2f, 6.6f, 1.2f, 0.7f, 0.3f, 0.4f, wood);
        Block(8.8f, 6f, 2.2f, 1.1f, 0.72f, 0.76f, wood);
        for (int i = -1; i <= 1; i += 2)
        {
            Chair(8.2f, 6f + i * 0.85f, -i, wood);
            Chair(9.4f, 6f + i * 0.85f, -i, wood);
        }
        if (rnd.NextDouble() < 0.7)
            Person(rnd, 8.2f, 5.15f, true, 1f);
        Pendant(8.8f, 6f, 2.2f, night, 1.4f);
        Pendant(3.2f, 6.6f, 2.5f, night, 1.2f);
        ShelfOnBack(rnd, 7f, W - 0.2f, 2.4f, wood);
        FloorLamp(0.6f, 8.6f, night);
        TableLamp(W - 0.6f, 0.9f, 1.5f, night, 2f);
        Block(W - 0.6f, 1.5f, 0.6f, 1.6f, 0f, 0.9f, wood);
        Plant(rnd, 6.2f, D - 0.7f, 1.4f);
        Plant(rnd, 0.7f, 0.9f, 1.0f);
        Picture(rnd, 3.2f, 2f, D, 1.6f, 1.0f, true);
        if (rnd.NextDouble() < 0.6)
            Person(rnd, 3.2f, 8.4f, true, -1f);
    }

    private static void Bedroom(Random rnd, bool night)
    {
        Material floor = Mat(Rgb(0.40f, 0.30f, 0.22f), 0.3f);
        Material wall = Mat(Rgb(0.78f, 0.74f, 0.70f), 0.1f);
        Shell(floor, wall, Mat(Rgb(0.42f, 0.46f, 0.55f), 0.1f), Mat(Rgb(0.88f, 0.86f, 0.84f), 0.05f), night);
        Box(3.5f, 0f, 5.5f, 8.5f, 0.015f, 10.5f, Mat(Rgb(0.65f, 0.60f, 0.55f), 0.05f));
        Material frame = Mat(Rgb(0.30f, 0.22f, 0.16f), 0.4f);
        Box(4.6f, 0f, 8.4f, 7.4f, 0.35f, D - 0.1f, frame);
        Box(4.7f, 0.35f, 8.5f, 7.3f, 0.6f, D - 0.2f, Mat(Rgb(0.90f, 0.90f, 0.88f), 0.1f));
        Box(4.65f, 0.6f, 8.4f, 7.35f, 0.66f, 10.9f, Mat(Pick(rnd, Bright) * 0.8f, 0.05f));
        Box(4.6f, 0f, D - 0.12f, 7.4f, 1.3f, D - 0.02f, frame);
        Block(5.3f, 11.3f, 0.9f, 0.4f, 0.6f, 0.78f, Mat(Rgb(0.95f, 0.95f, 0.93f), 0.1f));
        Block(6.7f, 11.3f, 0.9f, 0.4f, 0.6f, 0.78f, Mat(Rgb(0.95f, 0.95f, 0.93f), 0.1f));
        Material wood = Mat(Rgb(0.52f, 0.38f, 0.26f), 0.4f);
        Block(3.9f, 11.4f, 0.6f, 0.5f, 0f, 0.55f, wood);
        Block(8.1f, 11.4f, 0.6f, 0.5f, 0f, 0.55f, wood);
        TableLamp(3.9f, 0.55f, 11.4f, night, 3f);
        TableLamp(8.1f, 0.55f, 11.4f, night, 3f);
        Box(W - 0.65f, 0f, 3f, W, 2.4f, 8f, Mat(Rgb(0.85f, 0.83f, 0.78f), 0.3f));
        Box(0f, 0f, 7f, 0.5f, 0.85f, 9f, wood);
        Block(1.5f, 2f, 1.2f, 0.6f, 0.72f, 0.76f, wood);
        Chair(1.5f, 2.6f, -1f, wood);
        TableLamp(1.1f, 0.76f, 1.9f, night, 2f);
        Picture(rnd, 6f, 2.2f, D - 0.02f, 1.6f, 0.7f, true);
        Pendant(6f, 8.5f, 2.8f, night, 1.2f);
        Plant(rnd, 0.7f, D - 0.7f, 1.2f);
        Box(0.1f, 0f, 0.05f, 1.2f, H, 0.25f, Mat(Rgb(0.70f, 0.72f, 0.80f), 0.05f));
        Box(W - 1.2f, 0f, 0.05f, W - 0.1f, H, 0.25f, Mat(Rgb(0.70f, 0.72f, 0.80f), 0.05f));
    }

    private static void Kitchen(Random rnd, bool night)
    {
        Material floor = Mat(Rgb(0.62f, 0.60f, 0.56f), 0.4f);
        Material wall = Mat(Rgb(0.88f, 0.87f, 0.84f), 0.1f);
        Shell(floor, wall, wall, Mat(Rgb(0.90f, 0.90f, 0.88f), 0.05f), night);
        Material unit = Mat(Rgb(0.20f, 0.30f, 0.34f), 0.4f);
        Material worktop = Mat(Rgb(0.85f, 0.84f, 0.80f), 0.6f);
        Box(0.5f, 0f, D - 0.65f, 9.5f, 0.88f, D, unit);
        Box(0.5f, 0.88f, D - 0.68f, 9.5f, 0.92f, D, worktop);
        Box(0.5f, 1.5f, D - 0.35f, 9.5f, 2.3f, D, unit);
        Box(9.6f, 0f, D - 0.7f, 10.6f, 2.0f, D, Mat(Rgb(0.75f, 0.76f, 0.78f), 0.7f));
        Block(4.5f, 8.8f, 3f, 1f, 0f, 0.9f, unit);
        Block(4.5f, 8.8f, 3.2f, 1.1f, 0.9f, 0.94f, worktop);
        Material wood = Mat(Rgb(0.55f, 0.40f, 0.27f), 0.4f);
        Block(7.5f, 4.5f, 2.4f, 1f, 0.72f, 0.76f, wood);
        for (int i = -1; i <= 1; i++)
        {
            Chair(7.5f + i * 0.8f, 3.7f, 1f, wood);
            Chair(7.5f + i * 0.8f, 5.3f, -1f, wood);
        }
        if (rnd.NextDouble() < 0.7)
            Person(rnd, 6.7f, 3.7f, true, 1f);
        if (rnd.NextDouble() < 0.6)
            Person(rnd, 3.6f, 10.1f, false, 1f);
        Pendant(3.8f, 8.8f, 2.2f, night, 0.8f);
        Pendant(5.2f, 8.8f, 2.2f, night, 0.8f);
        Pendant(7.5f, 4.5f, 2.2f, night, 1f);
        for (float x = 1f; x < 9.5f; x += 0.9f)
            Block(x, D - 0.2f, 0.2f, 0.1f, 0.95f, 1.15f + 0.1f * (float)rnd.NextDouble(), Mat(Pick(rnd, Bright), 0.4f));
        Plant(rnd, 0.7f, 1f, 1.2f);
        Picture(rnd, 2.5f, 1.8f, 0.01f, 1.2f, 0.9f, false);
    }

    private static void Shop(Random rnd, bool night)
    {
        Material floor = Mat(Rgb(0.66f, 0.65f, 0.62f), 0.5f);
        Material wall = Mat(Rgb(0.90f, 0.90f, 0.90f), 0.1f);
        Shell(floor, wall, wall, Mat(Rgb(0.92f, 0.92f, 0.92f), 0.05f), night);
        CeilingPanels(true, night ? 0.8f : 0.35f);
        Material rack = Mat(Rgb(0.80f, 0.80f, 0.82f), 0.5f);
        // Shelves round the walls and in rows, full of goods.
        ShelfOfGoods(rnd, 0.2f, W - 0.2f, D - 0.6f, D, 2.2f, rack);
        for (float x = 3f; x < W - 1f; x += 6f)
            GondolaOfGoods(rnd, x, 4.5f, 9.5f, rack);
        if (!night)
            RenderSettings.ambientLight = Rgb(0.14f, 0.15f, 0.17f);
        Block(W - 1.8f, 1.6f, 2.4f, 0.8f, 0f, 1.0f, Mat(Rgb(0.25f, 0.25f, 0.28f), 0.5f));
        Person(rnd, W - 1.8f, 2.4f, false, -1f);
        Person(rnd, 4.2f, 6.2f, false, 1f);
        Person(rnd, 7.8f, 8.1f, false, -1f);
        Plant(rnd, 0.7f, 1f, 1.2f);
    }

    private static void ShelfOfGoods(Random rnd, float x0, float x1, float z0, float z1, float height, Material rack)
    {
        Box(x0, 0f, z1 - 0.03f, x1, height, z1, rack);
        for (float y = 0.1f; y < height - 0.2f; y += 0.45f)
        {
            Box(x0, y, z0, x1, y + 0.03f, z1, rack);
            float x = x0;
            while (x < x1 - 0.2f)
            {
                float w = 0.12f + 0.18f * (float)rnd.NextDouble();
                Box(x, y + 0.03f, z0 + 0.05f, Mathf.Min(x + w, x1), y + 0.18f + 0.18f * (float)rnd.NextDouble(), z1 - 0.05f, Mat(Pick(rnd, Bright), 0.3f));
                x += w + 0.02f;
            }
        }
    }

    private static void GondolaOfGoods(Random rnd, float x, float z0, float z1, Material rack)
    {
        Box(x - 0.05f, 0f, z0, x + 0.05f, 1.5f, z1, rack);
        for (float y = 0.1f; y < 1.4f; y += 0.42f)
        {
            Box(x - 0.45f, y, z0, x + 0.45f, y + 0.03f, z1, rack);
            float z = z0;
            while (z < z1 - 0.2f)
            {
                float w = 0.12f + 0.2f * (float)rnd.NextDouble();
                Material goods = Mat(Pick(rnd, Bright), 0.3f);
                Box(x - 0.42f, y + 0.03f, z, x - 0.08f, y + 0.2f + 0.15f * (float)rnd.NextDouble(), Mathf.Min(z + w, z1), goods);
                Box(x + 0.08f, y + 0.03f, z, x + 0.42f, y + 0.2f + 0.15f * (float)rnd.NextDouble(), Mathf.Min(z + w, z1), goods);
                z += w + 0.02f;
            }
        }
    }

    private static void Restaurant(Random rnd, bool night)
    {
        Material floor = Mat(Rgb(0.36f, 0.25f, 0.17f), 0.4f);
        Material wall = Mat(Rgb(0.70f, 0.55f, 0.42f), 0.1f);
        Shell(floor, wall, Mat(Rgb(0.42f, 0.30f, 0.24f), 0.1f), Mat(Rgb(0.50f, 0.44f, 0.38f), 0.05f), night);
        Downlights(night, 3f, 0.6f);
        Material bar = Mat(Rgb(0.20f, 0.13f, 0.09f), 0.5f);
        Box(1f, 0f, D - 1.6f, W - 1f, 1.1f, D - 1.0f, bar);
        Box(1f, 1.1f, D - 1.7f, W - 1f, 1.15f, D - 0.9f, Mat(Rgb(0.15f, 0.15f, 0.15f), 0.8f));
        for (float y = 1.2f; y < 2.8f; y += 0.45f)
        {
            Box(1.5f, y, D - 0.35f, W - 1.5f, y + 0.03f, D, bar);
            for (float x = 1.6f; x < W - 1.6f; x += 0.14f)
                Block(x, D - 0.18f, 0.08f, 0.08f, y + 0.03f, y + 0.3f, Mat(Pick(rnd, Bright) * 0.7f, 0.8f));
        }
        Material table = Mat(Rgb(0.15f, 0.10f, 0.07f), 0.5f);
        Material seat = Mat(Rgb(0.45f, 0.15f, 0.12f), 0.2f);
        foreach (float x in new[] { 2.8f, 9.2f })
        {
            foreach (float z in new[] { 2.2f, 5.4f, 8.4f })
            {
                Cylinder(x, z, 0.45f, 0.72f, 0.76f, table);
                Cylinder(x, z, 0.05f, 0f, 0.72f, table);
                Chair(x, z - 0.75f, 1f, seat);
                Chair(x, z + 0.75f, -1f, seat);
                if (rnd.NextDouble() < 0.6)
                    Person(rnd, x, z - 0.75f, true, 1f);
                if (rnd.NextDouble() < 0.6)
                    Person(rnd, x, z + 0.75f, true, -1f);
                Pendant(x, z, 1.8f, night, 1.3f);
            }
        }
        for (float x = 2f; x < W - 1.5f; x += 1.2f)
            Cylinder(x, D - 2.0f, 0.18f, 0f, 0.75f, seat);
        Person(rnd, 6f, D - 0.6f, false, -1f);
        Plant(rnd, 0.7f, 0.9f, 1.3f);
        Plant(rnd, W - 0.7f, 0.9f, 1.3f);
    }

    private static void Lobby(Random rnd, bool night)
    {
        Material floor = Mat(Rgb(0.78f, 0.76f, 0.72f), 0.7f);
        Material wall = Mat(Rgb(0.70f, 0.66f, 0.60f), 0.2f);
        Shell(floor, wall, Mat(Rgb(0.40f, 0.36f, 0.32f), 0.3f), Mat(Rgb(0.85f, 0.85f, 0.84f), 0.05f), night);
        Downlights(night, 2.4f, 1f);
        Material steel = Mat(Rgb(0.60f, 0.62f, 0.65f), 0.85f);
        for (float x = 3f; x < W - 2f; x += 2.2f)
            Box(x, 0f, D - 0.1f, x + 1.4f, 2.4f, D - 0.05f, steel);
        Material desk = Mat(Rgb(0.90f, 0.88f, 0.84f), 0.6f);
        Block(6f, 8.5f, 4f, 0.9f, 0f, 1.1f, desk);
        Person(rnd, 6.3f, 9.3f, false, -1f);
        Material fabric = Mat(Rgb(0.18f, 0.20f, 0.24f), 0.1f);
        foreach (float z in new[] { 2.2f, 4.4f })
        {
            Block(2.5f, z, 2.2f, 0.8f, 0.2f, 0.45f, fabric);
            Block(2.5f, z + (z < 3f ? -0.35f : 0.35f), 2.2f, 0.15f, 0.45f, 0.85f, fabric);
        }
        Block(2.5f, 3.3f, 1.2f, 0.7f, 0.3f, 0.42f, Mat(Rgb(0.2f, 0.15f, 0.1f), 0.5f));
        Box(W - 0.05f, 0.8f, 3f, W, 3f, 7.5f, Mat(Pick(rnd, Bright), 0.3f));
        Plant(rnd, 0.8f, D - 0.8f, 1.7f);
        Plant(rnd, W - 0.8f, D - 0.8f, 1.7f);
        Plant(rnd, 0.8f, 0.8f, 1.5f);
        Person(rnd, 8.5f, 4.5f, false, 1f);
        Person(rnd, 9.1f, 4.8f, false, -1f);
    }

    private static void Empty(Random rnd, bool night)
    {
        Material concrete = Mat(Rgb(0.46f, 0.45f, 0.43f), 0.2f);
        Material board = Mat(Rgb(0.72f, 0.72f, 0.70f), 0.05f);
        Shell(concrete, board, board, concrete, night);
        Material duct = Mat(Rgb(0.62f, 0.63f, 0.65f), 0.7f);
        foreach (float z in new[] { 3f, 9f })
        {
            GameObject pipe = Shape(PrimitiveType.Cylinder, duct);
            pipe.transform.localPosition = new Vector3(W * 0.5f, H - 0.35f, z);
            pipe.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            pipe.transform.localScale = new Vector3(0.45f, W * 0.5f, 0.45f);
        }
        foreach (float x in new[] { 3f, 9f })
            foreach (float z in new[] { 4f, 9.5f })
                Block(x, z, 0.5f, 0.5f, 0f, H, concrete);
        Material cardboard = Mat(Rgb(0.60f, 0.45f, 0.28f), 0.1f);
        for (int i = 0; i < 7; i++)
        {
            float x = 1f + (float)rnd.NextDouble() * 10f, z = 7f + (float)rnd.NextDouble() * 4f;
            Block(x, z, 0.6f, 0.5f, 0f, 0.4f + 0.3f * (float)rnd.NextDouble(), cardboard);
        }
        if (night)
        {
            Ball(4f, 2.6f, 6f, 0.08f, Glow(Rgb(1f, 1f, 1f), 6f));
            Lamp(4f, 2.5f, 6f, 9f, 1.8f, true);
            Ball(9f, 2.6f, 10f, 0.08f, Glow(Rgb(1f, 1f, 1f), 6f));
            Lamp(9f, 2.5f, 10f, 7f, 1.2f, true);
        }
        Person(rnd, 7.5f, 7.5f, false, 1f);
    }
}
