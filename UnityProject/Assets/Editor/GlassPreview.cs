using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using RealGlass.Glass;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>
/// Renders a block of towers with the REAL glass shader on this machine's GPU, inside the editor
/// that builds the bundles, through the same Graphics.DrawMesh + MaterialPropertyBlock path the
/// mod uses, with the mod's own GlassUniforms, GlassScreen (the copy after the deferred final
/// pass) and GlassSky (the live cube) -- copied into Assets/Sim by tools/preview/sync-sim.ps1 --
/// and reads the pixels back. tools/preview/gpu-render.ps1 drives it:
///   Unity.exe -batchmode -quit -projectPath UnityProject -executeMethod GlassPreview.Render
/// (no -nographics: it needs a Direct3D 11 device).
///
/// The scene is the game's: a LEGACY DEFERRED camera with HDR in linear colour space, fov 45
/// (the game's), a sun that casts shadows, a trilight ambient, the procedural skybox standing in
/// for the game's scattering sky, towers painted the way Cities: Skylines assets are (a diffuse,
/// an _XYSMap whose z is 1 - specular, an _ACIMap whose b marks the windows lit at night), drawn by
/// a stand-in of the game's building shader (Assets/Editor/PreviewBuilding.shader), and ground
/// with roads.
///
/// Parameters come in as environment variables (executeMethod takes none):
///   RG_OUT        directory for the frames (rg-[row]-[col].png)
///   RG_W, RG_H    frame size
///   RG_VIEWS      "aerial,steep,street,low,close,night,nightclose,nightstreet"  one row each
///   RG_VARIANTS   "vanilla|glass|glass:Trace=0;Neighbours=0"  one column each; "glass:" takes
///                 GlassParams field overrides (bool 0/1, numbers in the settings' units), and
///                 the preview's own: Debug=n (the shader's check views), Level=n (view 9's
///                 level), Tier=near|lod|group, Budget=0..3 (the GlassQuality tier), HiZ=0 (the
///                 tier without the depth pyramid), Q.Field=n (one GlassQuality field)
///   RG_SUN        the sun's intensity by day (1.3)
///   RG_EXPOSURE   the picture's exposure (0.6 by day; night x6)
/// Every frame logs "RG FRAME row=.. col=.. view=.. variant=.. file=.. | ..." with its mean colour.
/// </summary>
public static class GlassPreview
{
    private const string GlassShaderPath = "Assets/RealGlass/Glass.shader";
    private const string UtilShaderPath = "Assets/RealGlass/GlassUtil.shader";
    private const string StandInPath = "Assets/Editor/PreviewBuilding.shader";
    private const string CloudsPath = "Assets/Editor/PreviewClouds.shader";

    /// <summary>The variant's "Debug=n": the shader's check view (0 = the picture); "Level=n": the level view 9 shows.</summary>
    private static float _debug, _debugLevel;

    /// <summary>The game's object colour map (RenderManager.OBJECT_COLORMAP_WIDTH x HEIGHT in the game: 512 x 256).</summary>
    private const int ColorMapWidth = 512, ColorMapHeight = 256;

    private sealed class Building
    {
        public Mesh Mesh;
        public Matrix4x4 Matrix;
        public Texture2D Main, Xys, Aci;
        public string Kind;

        /// <summary>Its colour index, as RenderManager.GetColorLocation(id) makes it: its texel in the colour map.</summary>
        public Vector4 ObjectIndex;

        /// <summary>The middle tier's test: 8 copies of this building's LOD mesh (uv in the atlas), as BuildingInfoBase.InitMeshData makes them.</summary>
        public Mesh Combined8;
    }

    private sealed class Scene
    {
        public Camera Camera;
        public RenderTexture Target;
        public Texture2D Readback;
        public int Width, Height;
        public Light Sun;
        public Light Moon;
        public Material Sky;
        public Material StandIn;
        public Material Glass;
        public Material Util;
        public MaterialPropertyBlock Block;
        public List<Building> Buildings = new List<Building>();
        public Mesh Ground;
        public Texture2D GroundMain, GroundXys, GroundAci;
        public GlassScreen Screen = new GlassScreen();
        public GlassSky SkyCube = new GlassSky();

        // The middle and far tiers' tests: the LOD atlases, the materials, and the whole city
        // baked into one world-space mesh (what RenderGroup draws).
        public Texture2D AtlasMain, AtlasXys, AtlasAci;
        public Material GlassLod, GlassGroup;
        public Mesh Baked;

        /// <summary>The stand-in for Volumetric Weather's cloud volume (PreviewClouds.shader), found by its name.</summary>
        public GameObject Clouds;

        /// <summary>The city from above (the mod's CityMap, same file): every tower as its box.</summary>
        public CityMap City = new CityMap();

        /// <summary>Each building's own glass (the mod's AssetTextures, same file): what "A.*" sets.</summary>
        public AssetTextures Assets = new AssetTextures();

        /// <summary>The id of the middle block's first full-glass tower: the building "A.*" gives its own glass.</summary>
        public int FullGlassId;

        /// <summary>The rooms behind the glass (the mod's RoomAtlas, same file, the mod's own pictures).</summary>
        public RoomAtlas Rooms = new RoomAtlas();
    }

    /// <summary>The variant's "Hour=h": the time of day the rooms' lights follow (21:00 by night, 13:00 by day).</summary>
    private static float? _hour;

    /// <summary>The kind of rooms a stand-in has (Interiors.Kind*): low punched-window blocks homes and shops, towers offices and flats.</summary>
    private static int RoomKindOf(Building b, int id)
    {
        switch (b.Kind)
        {
            case "punched": return id % 3 == 0 ? Interiors.KindShopLow : Interiors.KindHome;
            case "fins": return Interiors.KindHome;
            case "glassbox": return id % 2 == 0 ? Interiors.KindHotel : Interiors.KindOffice;
            default: return Interiors.KindOffice;
        }
    }

    /// <summary>The variant's "A.Field=value": the full-glass tower's own settings (its asset's, in the mod).</summary>
    private sealed class AssetOverride
    {
        public float? Strength, Detail, Reflectivity, Clarity, Own, Threshold;
        public bool? Night;
        public Color? Tint;

        public bool HasGlass
        {
            get { return Reflectivity.HasValue || Clarity.HasValue || Own.HasValue || Tint.HasValue || Threshold.HasValue || Night.HasValue; }
        }
    }

    private static AssetOverride _asset;

    /// <summary>The variant's "Tier=near|lod|group": which of the game's three paths the glass is drawn through.</summary>
    private static string _tier = "near";

    /// <summary>The variant's "Budget=0..3": the GlassQuality tier (the mod runs Ultra).</summary>
    private static int _quality = GlassQuality.Ultra;

    /// <summary>The variant's "HiZ=0": the tier's budget without the hierarchical depth (the plain trace).</summary>
    private static bool _hizOff;

    /// <summary>The variant's "Q.Field=n": the tier's budget with that GlassQuality field changed (Q.PaneSearchSteps=0...).</summary>
    private static readonly List<KeyValuePair<string, int>> _budgetOverrides = new List<KeyValuePair<string, int>>();


    public static void Render()
    {
        try
        {
            RenderAll();
            Debug.Log("RG PREVIEW OK");
        }
        catch (Exception e)
        {
            Debug.LogError("RG PREVIEW FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    private static void RenderAll()
    {
        if (PlayerSettings.colorSpace != ColorSpace.Linear)
            PlayerSettings.colorSpace = ColorSpace.Linear;

        string outDir = Env("RG_OUT", "GlassPreview");
        Directory.CreateDirectory(outDir);
        int width = int.Parse(Env("RG_W", "480"), CultureInfo.InvariantCulture);
        int height = int.Parse(Env("RG_H", "270"), CultureInfo.InvariantCulture);
        string[] views = Env("RG_VIEWS", "aerial").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        string[] variants = Env("RG_VARIANTS", "vanilla|glass").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        float sunIntensity = ParseFloat(Env("RG_SUN", "1.3"));
        float exposure = ParseFloat(Env("RG_EXPOSURE", "0.6"));

        Shader glass = Load(GlassShaderPath);
        Shader util = Load(UtilShaderPath);
        Shader standIn = Load(StandInPath);
        Debug.Log("RG PREVIEW: glass supported=" + glass.isSupported + " util supported=" + util.isSupported +
                  " stand-in passes=" + new Material(standIn).passCount + " | device " + SystemInfo.graphicsDeviceType + " '" +
                  SystemInfo.graphicsDeviceName + "' | colour space " + QualitySettings.activeColorSpace);
        if (!glass.isSupported)
            throw new Exception("the glass shader is not supported here: it did not compile (see the log's Shader error lines)");

        Scene s = BuildScene(width, height, glass, util, standIn);
        Debug.Log("RG PREVIEW: " + s.Buildings.Count + " buildings");

        // The rooms' pictures, as the mod ships them (tools/interiors/build-rooms.ps1).
        string resources = Path.Combine(Path.GetDirectoryName(Application.dataPath), "../RealGlass/Resources");
        string nightFile = Path.Combine(resources, "rooms-night.bytes"), dayFile = Path.Combine(resources, "rooms-day.bytes");
        if (File.Exists(nightFile) && File.Exists(dayFile) && s.Rooms.Load(File.ReadAllBytes(nightFile), File.ReadAllBytes(dayFile)))
            Debug.Log("RG PREVIEW: " + s.Rooms.Tiles + " rooms");
        else
            Debug.Log("RG PREVIEW: no rooms (" + (File.Exists(nightFile) ? s.Rooms.Problem : "no " + nightFile) + ")");

        for (int row = 0; row < views.Length; row++)
        {
            string view = views[row].Trim().ToLowerInvariant();
            for (int col = 0; col < variants.Length; col++)
            {
                string variant = variants[col].Trim();
                string info;
                Color[] pixels = RenderView(s, view, variant, sunIntensity, out info);
                float exp = view.StartsWith("night") ? exposure * 6f : exposure;
                string file = Path.Combine(outDir, "rg-" + row + "-" + col + ".png");
                SavePng(pixels, width, height, exp, file);
                Debug.Log("RG FRAME row=" + row + " col=" + col + " view=" + view + " variant=" + variant + " file=" + file + " | " + info);
            }
        }

        s.Screen.Detach();
        s.SkyCube.Destroy();
        s.City.Destroy();
        s.Assets.Destroy();
        s.Camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(s.Camera.gameObject);
        UnityEngine.Object.DestroyImmediate(s.Target);
    }

    private static Shader Load(string path)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null)
            throw new Exception("shader not found at " + path);
        return shader;
    }

    // ---------------------------------------------------------------------------------------
    // The scene.
    // ---------------------------------------------------------------------------------------

    private static Scene BuildScene(int width, int height, Shader glass, Shader util, Shader standIn)
    {
        var s = new Scene { Width = width, Height = height };

        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowDistance = 2500f;
        QualitySettings.shadowCascades = 4;
        QualitySettings.pixelLightCount = 4;

        GameObject camObject = new GameObject("RGPreviewCamera");
        s.Camera = camObject.AddComponent<Camera>();
        s.Camera.allowHDR = true;
        s.Camera.allowMSAA = false;
        s.Camera.renderingPath = RenderingPath.DeferredLighting;
        s.Camera.fieldOfView = 45f;
        s.Camera.nearClipPlane = 1f;
        s.Camera.farClipPlane = 12000f;
        s.Camera.clearFlags = CameraClearFlags.Skybox;
        s.Camera.depthTextureMode = DepthTextureMode.Depth;
        s.Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        s.Target.antiAliasing = 1;
        s.Target.Create();
        s.Camera.targetTexture = s.Target;
        s.Readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);

        GameObject sunObject = new GameObject("RGPreviewSun");
        s.Sun = sunObject.AddComponent<Light>();
        s.Sun.type = LightType.Directional;
        s.Sun.shadows = LightShadows.Soft;
        s.Sun.shadowStrength = 0.9f;
        RenderSettings.sun = s.Sun;

        GameObject moonObject = new GameObject("RGPreviewMoon");
        s.Moon = moonObject.AddComponent<Light>();
        s.Moon.type = LightType.Directional;
        s.Moon.shadows = LightShadows.None;
        s.Moon.color = new Color(0.55f, 0.65f, 1f);
        s.Moon.intensity = 0f;

        s.Sky = new Material(Shader.Find("Skybox/Procedural"));
        RenderSettings.skybox = s.Sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;

        s.StandIn = new Material(standIn);
        s.Glass = new Material(glass);
        s.Glass.EnableKeyword("RG_NEAR");
        s.GlassLod = new Material(glass);
        s.GlassLod.EnableKeyword("RG_LOD");
        s.GlassGroup = new Material(glass);
        s.GlassGroup.EnableKeyword("RG_GROUP");
        s.Util = new Material(util);
        s.Block = new MaterialPropertyBlock();

        BuildCity(s);
        BuildLodTiers(s);
        BuildCityMap(s);
        s.Assets.Create(ColorMapWidth, ColorMapHeight);
        Debug.Log("RG PREVIEW: city heights " + s.City.LiveCount + " buildings, highest roof " + s.City.LiveTop.ToString("F0") + " m");

        // Volumetric Weather's cloud volume, as the mod finds it: a mesh renderer by that name, a
        // box round the camera with a Transparent+100 material and _UseDepth.
        s.Clouds = GameObject.CreatePrimitive(PrimitiveType.Cube);
        s.Clouds.name = GlassSky.CloudVolumeName;
        UnityEngine.Object.DestroyImmediate(s.Clouds.GetComponent<Collider>());
        s.Clouds.GetComponent<MeshRenderer>().sharedMaterial = new Material(Load(CloudsPath));
        s.Clouds.transform.localScale = Vector3.one * 9000f;
        return s;
    }

    /// <summary>
    /// The mod's city height map (CityMap, the same file) for the preview's city: every building as
    /// its mesh's bounding box, at its matrix, under its id. The game gives each its prefab's height
    /// grid; a box is a box.
    /// </summary>
    private static void BuildCityMap(Scene s)
    {
        s.City.Begin(0f, 0f);
        for (int i = 0; i < s.Buildings.Count; i++)
        {
            Building b = s.Buildings[i];
            Vector3 position = b.Matrix.GetColumn(3);
            Quaternion rotation = Quaternion.LookRotation(b.Matrix.GetColumn(2), b.Matrix.GetColumn(1));
            Bounds bounds = b.Mesh.bounds;
            s.City.AddBuilding(position, rotation, bounds.min, bounds.max, null, i + 1);
        }
        s.City.Finish();
    }

    /// <summary>RenderManager.GetColorLocation: the texel centre of object id in the colour map.</summary>
    private static Vector4 ColorIndex(int id)
    {
        return new Vector4((id % ColorMapWidth + 0.5f) / ColorMapWidth, (id / ColorMapWidth + 0.5f) / ColorMapHeight, 1f, 0f);
    }

    /// <summary>
    /// The middle and far tiers as the game builds them: every building's textures packed into
    /// LOD atlases (BuildingManager.m_lodRgb/Xys/AciAtlas), its LOD mesh with uv remapped into its
    /// rectangle and the rectangle itself in uv3 (x, y) / uv4 (w, h), eight copies of it with
    /// Color32.a = k x 32 (BuildingInfoBase.InitMeshData), and the whole city baked into one
    /// world-space mesh (BuildingAI.PopulateGroupData: world normals and tangents).
    /// </summary>
    private static void BuildLodTiers(Scene s)
    {
        const int slotW = 64, slotH = 256, atlasW = 1024, atlasH = 2048, perRow = atlasW / slotW;
        var main = new Color[atlasW * atlasH];
        var xys = new Color[atlasW * atlasH];
        var aci = new Color[atlasW * atlasH];
        var baked = new List<Vector3>();
        var bakedN = new List<Vector3>();
        var bakedT = new List<Vector4>();
        var bakedUv = new List<Vector2>();
        var bakedUv2 = new List<Vector2>();
        var bakedUv3 = new List<Vector2>();
        var bakedUv4 = new List<Vector2>();
        var bakedTris = new List<int>();

        for (int i = 0; i < s.Buildings.Count; i++)
        {
            Building b = s.Buildings[i];
            int sx = (i % perRow) * slotW, sy = (i / perRow) * slotH;
            if (sy + slotH > atlasH)
                throw new Exception("the preview's LOD atlas is full");

            // Each atlas texel the AVERAGE of the facade texels it covers, as a baked LOD texture
            // is a downsampled one: a point sample of every fourth mullion would draw moire that no
            // game atlas has.
            Color[] srcMain = b.Main.GetPixels(0), srcXys = b.Xys.GetPixels(0), srcAci = b.Aci.GetPixels(0);
            int srcW = b.Main.width, srcH = b.Main.height;
            for (int y = 0; y < slotH; y++)
            {
                int y0 = y * srcH / slotH, y1 = Mathf.Max(y0 + 1, (y + 1) * srcH / slotH);
                for (int x = 0; x < slotW; x++)
                {
                    int x0 = x * srcW / slotW, x1 = Mathf.Max(x0 + 1, (x + 1) * srcW / slotW);
                    Color m = Color.clear, xy = Color.clear, ac = Color.clear;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        for (int xx = x0; xx < x1; xx++)
                        {
                            int j = yy * srcW + xx;
                            m += srcMain[j];
                            xy += srcXys[j];
                            ac += srcAci[j];
                        }
                    }
                    float n = (y1 - y0) * (x1 - x0);
                    int k = (sy + y) * atlasW + sx + x;
                    main[k] = m / n;
                    xys[k] = xy / n;
                    aci[k] = ac / n;
                }
            }

            var rect = new Rect((float)sx / atlasW, (float)sy / atlasH, (float)slotW / atlasW, (float)slotH / atlasH);
            Vector3[] v0 = b.Mesh.vertices;
            Vector3[] n0 = b.Mesh.normals;
            Vector4[] t0 = b.Mesh.tangents;
            Vector2[] uv0 = b.Mesh.uv;
            int[] tri0 = b.Mesh.triangles;

            // Eight copies, as the game's combined LOD mesh.
            var cv = new List<Vector3>();
            var cn = new List<Vector3>();
            var ct = new List<Vector4>();
            var cuv = new List<Vector2>();
            var cuv3 = new List<Vector2>();
            var cuv4 = new List<Vector2>();
            var cc = new List<Color32>();
            var ctri = new List<int>();
            for (int copy = 0; copy < 8; copy++)
            {
                int start = cv.Count;
                for (int j = 0; j < v0.Length; j++)
                {
                    cv.Add(v0[j]);
                    cn.Add(n0[j]);
                    ct.Add(t0[j]);
                    cuv.Add(new Vector2(rect.x + uv0[j].x * rect.width, rect.y + uv0[j].y * rect.height));
                    cuv3.Add(new Vector2(rect.x, rect.y));
                    cuv4.Add(new Vector2(rect.width, rect.height));
                    cc.Add(new Color32(255, 255, 255, (byte)(copy * 32)));
                }
                foreach (int t in tri0)
                    ctri.Add(start + t);
            }

            b.Combined8 = new Mesh();
            b.Combined8.SetVertices(cv);
            b.Combined8.SetNormals(cn);
            b.Combined8.SetTangents(ct);
            b.Combined8.SetUVs(0, cuv);
            b.Combined8.SetUVs(2, cuv3);
            b.Combined8.SetUVs(3, cuv4);
            b.Combined8.colors32 = cc.ToArray();
            b.Combined8.SetTriangles(ctri, 0);

            // Baked into the city block, in world space.
            int bs = baked.Count;
            for (int j = 0; j < v0.Length; j++)
            {
                baked.Add(b.Matrix.MultiplyPoint3x4(v0[j]));
                bakedN.Add(b.Matrix.MultiplyVector(n0[j]).normalized);
                Vector3 tw = b.Matrix.MultiplyVector(new Vector3(t0[j].x, t0[j].y, t0[j].z)).normalized;
                bakedT.Add(new Vector4(tw.x, tw.y, tw.z, t0[j].w));
                bakedUv.Add(new Vector2(rect.x + uv0[j].x * rect.width, rect.y + uv0[j].y * rect.height));
                bakedUv2.Add(new Vector2(b.ObjectIndex.x, b.ObjectIndex.y));   // PopulateGroupData: uvs2 = the colour index
                bakedUv3.Add(new Vector2(rect.x, rect.y));
                bakedUv4.Add(new Vector2(rect.width, rect.height));
            }
            foreach (int t in tri0)
                bakedTris.Add(bs + t);
        }

        s.AtlasMain = Tex(atlasW, atlasH, main, false);
        s.AtlasXys = Tex(atlasW, atlasH, xys, true);
        s.AtlasAci = Tex(atlasW, atlasH, aci, true);
        foreach (Material m in new[] { s.GlassLod, s.GlassGroup })
        {
            m.SetTexture("_MainTex", s.AtlasMain);
            m.SetTexture("_XYSMap", s.AtlasXys);
            m.SetTexture("_ACIMap", s.AtlasAci);
        }

        s.Baked = new Mesh();
        s.Baked.SetVertices(baked);
        s.Baked.SetNormals(bakedN);
        s.Baked.SetTangents(bakedT);
        s.Baked.SetUVs(0, bakedUv);
        s.Baked.SetUVs(1, bakedUv2);
        s.Baked.SetUVs(2, bakedUv3);
        s.Baked.SetUVs(3, bakedUv4);
        var white = new Color32[baked.Count];
        for (int i = 0; i < white.Length; i++)
            white[i] = new Color32(255, 255, 255, 255);
        s.Baked.colors32 = white;
        s.Baked.SetTriangles(bakedTris, 0);
        s.Baked.RecalculateBounds();
    }

    /// <summary>
    /// A downtown on a 120 m grid (100 m blocks, 20 m streets): towers in the middle, lower blocks
    /// round them, one tower turned 30 degrees. Deterministic.
    /// </summary>
    private static void BuildCity(Scene s)
    {
        var rnd = new System.Random(20260928);
        Color[] glassColours =
        {
            new Color(0.25f, 0.36f, 0.45f), new Color(0.30f, 0.40f, 0.38f), new Color(0.17f, 0.20f, 0.23f),
            new Color(0.36f, 0.32f, 0.27f), new Color(0.34f, 0.43f, 0.52f), new Color(0.22f, 0.30f, 0.40f),
        };

        for (int bx = -3; bx <= 3; bx++)
        {
            for (int bz = -3; bz <= 3; bz++)
            {
                float cx = bx * 120f, cz = bz * 120f;
                float centre = Mathf.Sqrt(bx * bx + bz * bz);
                int count = centre < 1.6f ? 2 : 1 + rnd.Next(2);
                for (int k = 0; k < count; k++)
                {
                    bool tower = centre < 2.3f && rnd.NextDouble() < 0.85;
                    float w = tower ? 34f + (float)rnd.NextDouble() * 20f : 40f + (float)rnd.NextDouble() * 30f;
                    float h = tower ? 90f + (float)rnd.NextDouble() * (220f - centre * 50f) : 14f + (float)rnd.NextDouble() * 26f;
                    float ox = count == 1 ? 0f : (k == 0 ? -24f : 24f);
                    float oz = count == 1 ? 0f : (k == 0 ? -20f : 22f);
                    if (count > 1) w = Mathf.Min(w, 44f);
                    float turn = (bx == 1 && bz == 0 && k == 0) ? 30f : 0f;
                    string kind = tower ? (rnd.NextDouble() < 0.7 ? "curtain" : "fins") : "punched";
                    Color glassColour = glassColours[rnd.Next(glassColours.Length)];

                    // Off the random stream (the city stays the same): its id, whether a curtain
                    // wall is a plain glass box (no floors painted in, lit all alike).
                    int id = s.Buildings.Count + 1;
                    if (kind == "curtain" && Hash(id * 3.7f, 11.3f) < 0.45f)
                        kind = "glassbox";

                    // The middle block's two towers are FULL-GLASS assets: the whole facade marked
                    // shiny, its floors painted in its colour map only (what PaintedWall is for) --
                    // dark slab bands and light mullions; light floor lines over a painted sky.
                    if (bx == 0 && bz == 0)
                        kind = k == 0 ? "fullglass" : "fullglass2";
                    if (kind == "fullglass")
                        s.FullGlassId = id;

                    Building b = new Building();
                    b.Kind = kind;
                    b.ObjectIndex = ColorIndex(id);
                    b.Mesh = BoxMesh(w, h, w);
                    b.Matrix = Matrix4x4.TRS(new Vector3(cx + ox, 0f, cz + oz), Quaternion.Euler(0f, turn, 0f), Vector3.one);
                    MakeFacade(kind, w, h, glassColour, rnd.Next(), out b.Main, out b.Xys, out b.Aci);
                    s.Buildings.Add(b);
                }
            }
        }

        s.Ground = GroundMesh(4000f);
        MakeGround(out s.GroundMain, out s.GroundXys, out s.GroundAci);
    }

    // A box on its base (y = 0 .. h), the four facades mapped u 0..1 across, v 0..0.9 up; the
    // roof into v 0.9..1 -- one texture per building, the way an asset has one.
    private static Mesh BoxMesh(float w, float h, float d)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tans = new List<Vector4>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        float x = w * 0.5f, z = d * 0.5f;

        // Four sides: corners (a -> b) along the facade, outward normal n.
        AddQuad(verts, norms, tans, uvs, tris, new Vector3(-x, 0, -z), new Vector3(x, 0, -z), h, new Vector3(0, 0, -1), 0f, 0.9f);
        AddQuad(verts, norms, tans, uvs, tris, new Vector3(x, 0, -z), new Vector3(x, 0, z), h, new Vector3(1, 0, 0), 0f, 0.9f);
        AddQuad(verts, norms, tans, uvs, tris, new Vector3(x, 0, z), new Vector3(-x, 0, z), h, new Vector3(0, 0, 1), 0f, 0.9f);
        AddQuad(verts, norms, tans, uvs, tris, new Vector3(-x, 0, z), new Vector3(-x, 0, -z), h, new Vector3(-1, 0, 0), 0f, 0.9f);

        // Roof.
        int i0 = verts.Count;
        verts.Add(new Vector3(-x, h, -z)); verts.Add(new Vector3(-x, h, z)); verts.Add(new Vector3(x, h, z)); verts.Add(new Vector3(x, h, -z));
        for (int i = 0; i < 4; i++) { norms.Add(Vector3.up); tans.Add(new Vector4(1, 0, 0, 1)); }
        uvs.Add(new Vector2(0, 0.905f)); uvs.Add(new Vector2(0, 0.995f)); uvs.Add(new Vector2(1, 0.995f)); uvs.Add(new Vector2(1, 0.905f));
        tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });

        Mesh m = new Mesh();
        m.SetVertices(verts);
        m.SetNormals(norms);
        m.SetTangents(tans);
        m.SetUVs(0, uvs);
        var colours = new Color[verts.Count];
        for (int i = 0; i < colours.Length; i++)
            colours[i] = Color.white;
        m.colors = colours;
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    private static void AddQuad(List<Vector3> verts, List<Vector3> norms, List<Vector4> tans, List<Vector2> uvs, List<int> tris,
                                Vector3 a, Vector3 b, float h, Vector3 n, float v0, float v1)
    {
        int i0 = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(b + Vector3.up * h); verts.Add(a + Vector3.up * h);
        Vector3 t = (b - a).normalized;
        for (int i = 0; i < 4; i++) { norms.Add(n); tans.Add(new Vector4(t.x, t.y, t.z, 1)); }
        uvs.Add(new Vector2(0, v0)); uvs.Add(new Vector2(1, v0)); uvs.Add(new Vector2(1, v1)); uvs.Add(new Vector2(0, v1));
        // Clockwise seen from outside (Unity's front face).
        tris.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
    }

    private static Mesh GroundMesh(float size)
    {
        Mesh m = new Mesh();
        float e = size * 0.5f;
        m.vertices = new[] { new Vector3(-e, 0, -e), new Vector3(-e, 0, e), new Vector3(e, 0, e), new Vector3(e, 0, -e) };
        m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        m.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
        // One texture tile = one 120 m block and its street.
        float tiles = size / 120f;
        m.uv = new[] { new Vector2(-tiles * 0.5f + 0.5f, -tiles * 0.5f + 0.5f), new Vector2(-tiles * 0.5f + 0.5f, tiles * 0.5f + 0.5f),
                       new Vector2(tiles * 0.5f + 0.5f, tiles * 0.5f + 0.5f), new Vector2(tiles * 0.5f + 0.5f, -tiles * 0.5f + 0.5f) };
        m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        m.RecalculateBounds();
        return m;
    }

    // ---------------------------------------------------------------------------------------
    // Textures painted the way the game's assets are.
    // ---------------------------------------------------------------------------------------

    private static void MakeFacade(string kind, float w, float h, Color glassColour, int seed,
                                   out Texture2D main, out Texture2D xys, out Texture2D aci)
    {
        int tw = 256, th = 1024;
        var rnd = new System.Random(seed);
        var cMain = new Color[tw * th];
        var cXys = new Color[tw * th];
        var cAci = new Color[tw * th];
        float floorH = 3.8f;
        float colW = kind == "punched" ? 3.0f : 1.5f;
        Color concrete = new Color(0.60f, 0.57f, 0.52f);
        Color mullion = new Color(0.34f, 0.35f, 0.37f);
        float panelSeed = (float)rnd.NextDouble() * 100f;

        for (int y = 0; y < th; y++)
        {
            float v = (y + 0.5f) / th;
            for (int x = 0; x < tw; x++)
            {
                float u = (x + 0.5f) / tw;
                int i = y * tw + x;
                if (v >= 0.9f)
                {
                    float n = Noise(x * 0.3f, y * 0.3f, seed);
                    cMain[i] = concrete * (0.75f + 0.1f * n);
                    cXys[i] = new Color(0.5f, 0.5f, 0.95f, 1f);
                    cAci[i] = new Color(0f, 0f, 0f, 1f);
                    continue;
                }

                float um = u * w, vm = v / 0.9f * h;
                int floorIndex = Mathf.FloorToInt(vm / floorH);
                float inFloor = vm / floorH - floorIndex;
                int column = Mathf.FloorToInt(um / colW);
                float inCol = um / colW - column;
                float cell = Hash(column * 7.13f + panelSeed, floorIndex * 3.17f);
                float illum = 0.5f + 0.48f * Hash(column * 1.7f + 31f, floorIndex * 9.1f + panelSeed);

                Color c;
                float spec;
                float lit = 0f;
                if (kind == "fullglass")
                {
                    // Shiny all over (spec 0.92, a little less on the slab band: 0.84, still glass
                    // to the 70% threshold); the floors and the mullions are in the colour only.
                    bool slab = inFloor < 0.24f;
                    bool mull = inCol < 0.04f || inCol > 0.96f;
                    if (mull)
                        c = new Color(0.56f, 0.58f, 0.61f);
                    else if (slab)
                        c = glassColour * 0.42f;
                    else
                        c = glassColour * (0.9f + 0.1f * cell + 0.12f * inFloor);
                    spec = slab ? 0.84f : 0.92f;
                    lit = slab || mull ? 0f : illum;
                }
                else if (kind == "fullglass2")
                {
                    // A painted sky across the whole facade (lighter up high, soft clouds: what
                    // some asset painters put on glass), thin light floor lines, no mullions,
                    // shiny everywhere alike. The sky must stay glass; the lines must not.
                    float up = vm / h;
                    Color skyLow = new Color(0.22f, 0.27f, 0.32f), skyHigh = new Color(0.50f, 0.60f, 0.70f);
                    float cloud = Mathf.Clamp01((Noise(um * 0.08f, vm * 0.05f, seed) - 0.45f) * 2.5f);
                    c = Color.Lerp(skyLow, skyHigh, up) * (1f + 0.25f * cloud);
                    if (inFloor < 0.05f)
                        c = new Color(0.78f, 0.80f, 0.82f);
                    spec = 0.95f;
                    lit = inFloor < 0.05f ? 0f : 0.85f;
                }
                else if (kind == "punched")
                {
                    bool window = inCol > 0.2f && inCol < 0.8f && inFloor > 0.3f && inFloor < 0.86f && floorIndex > 0;
                    if (window)
                    {
                        c = new Color(0.20f, 0.27f, 0.34f) * (0.9f + 0.2f * cell);
                        spec = 0.86f;
                        lit = illum;
                    }
                    else
                    {
                        c = concrete * (0.92f + 0.12f * Noise(x * 0.5f, y * 0.5f, seed));
                        spec = 0.06f;
                    }
                }
                else
                {
                    // A glass box: thin mullions only, no spandrels painted in, one illumination
                    // value for all its glass (a whole tower lights at once, as such assets do).
                    bool box = kind == "glassbox";
                    bool fin = kind == "fins" && (column % 2 == 0) && inCol < 0.12f;
                    bool mull = box ? (inCol < 0.03f || inCol > 0.97f) : (inCol < 0.05f || inCol > 0.95f);
                    bool spandrel = !box && inFloor < 0.22f;
                    if (fin)
                    {
                        c = concrete * 1.05f;
                        spec = 0.1f;
                    }
                    else if (mull)
                    {
                        c = mullion;
                        spec = 0.45f;
                    }
                    else if (spandrel)
                    {
                        c = glassColour * 0.55f;
                        spec = 0.58f;
                    }
                    else if (box)
                    {
                        c = glassColour * (0.95f + 0.05f * cell);
                        spec = 0.9f;
                        lit = 0.85f;
                    }
                    else
                    {
                        // Asset painters shade glass: a little lighter at the top of each pane.
                        c = glassColour * (0.9f + 0.15f * cell + 0.12f * inFloor);
                        spec = 0.9f;
                        lit = illum;
                    }
                }

                cMain[i] = new Color(c.r, c.g, c.b, 1f);
                cXys[i] = new Color(0.5f, 0.5f, 1f - spec, 1f);
                cAci[i] = new Color(0f, 0f, lit, 1f);
            }
        }

        main = Tex(tw, th, cMain, false);
        xys = Tex(tw, th, cXys, true);
        aci = Tex(tw, th, cAci, true);
    }

    private static void MakeGround(out Texture2D main, out Texture2D xys, out Texture2D aci)
    {
        int n = 512;
        var cMain = new Color[n * n];
        var cXys = new Color[n * n];
        var cAci = new Color[n * n];
        Color asphalt = new Color(0.12f, 0.12f, 0.13f);
        Color walk = new Color(0.42f, 0.41f, 0.39f);
        Color paved = new Color(0.33f, 0.32f, 0.30f);
        Color grass = new Color(0.16f, 0.24f, 0.10f);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                // One tile = 120 m: a 20 m street on two edges (centred on the tile's border).
                float mx = (x + 0.5f) / n * 120f, my = (y + 0.5f) / n * 120f;
                float dx = Mathf.Min(mx, 120f - mx), dy = Mathf.Min(my, 120f - my);
                float d = Mathf.Min(dx, dy);
                Color c;
                if (d < 7f)
                {
                    c = asphalt * (0.9f + 0.2f * Noise(x * 0.8f, y * 0.8f, 5));
                    // Lane markings.
                    if ((d < 0.25f) && (Mathf.Repeat(dx < dy ? my : mx, 9f) < 4.5f))
                        c = new Color(0.7f, 0.7f, 0.65f);
                }
                else if (d < 10f)
                    c = walk;
                else
                {
                    float p = Noise(x * 0.05f, y * 0.05f, 9);
                    c = p > 0.55f ? grass * (0.8f + 0.4f * Noise(x * 0.4f, y * 0.4f, 3)) : paved * (0.9f + 0.2f * Noise(x * 0.6f, y * 0.6f, 4));
                }

                int i = y * n + x;
                cMain[i] = c;
                cXys[i] = new Color(0.5f, 0.5f, 1f, 1f);
                cAci[i] = new Color(0f, 0f, 0f, 1f);
            }
        }

        main = Tex(n, n, cMain, false);
        xys = Tex(n, n, cXys, true);
        aci = Tex(n, n, cAci, true);
        main.wrapMode = TextureWrapMode.Repeat;
        xys.wrapMode = TextureWrapMode.Repeat;
        aci.wrapMode = TextureWrapMode.Repeat;
    }

    private static Texture2D Tex(int w, int h, Color[] pixels, bool linear)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true, linear);
        t.SetPixels(pixels);
        t.Apply(true);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Trilinear;
        t.anisoLevel = 4;
        return t;
    }

    private static float Hash(float x, float y)
    {
        float h = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
        return h - Mathf.Floor(h);
    }

    private static float Noise(float x, float y, int seed)
    {
        float ix = Mathf.Floor(x), iy = Mathf.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(ix + seed, iy), b = Hash(ix + 1f + seed, iy), c = Hash(ix + seed, iy + 1f), d = Hash(ix + 1f + seed, iy + 1f);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // ---------------------------------------------------------------------------------------
    // One frame.
    // ---------------------------------------------------------------------------------------

    private static Color[] RenderView(Scene s, string view, string variant, float sunIntensity, out string info)
    {
        // The light of the moment.
        // "night" (the aerial camera) and "nightclose" (the close one) are both by night.
        bool night = view.StartsWith("night");
        float sunElev = view == "low" ? 7f : (night ? -12f : 42f);
        float sunYaw = view == "low" ? 200f : 140f;
        Vector3 toSun = Quaternion.Euler(-sunElev, sunYaw, 0f) * Vector3.forward;
        s.Sun.transform.rotation = Quaternion.LookRotation(-toSun);
        s.Sun.color = view == "low" ? new Color(1f, 0.72f, 0.45f) : new Color(1f, 0.96f, 0.9f);
        s.Sun.intensity = night ? 0f : (view == "low" ? sunIntensity * 0.8f : sunIntensity);
        s.Moon.transform.rotation = Quaternion.LookRotation(-(Quaternion.Euler(-35f, 20f, 0f) * Vector3.forward));
        s.Moon.intensity = night ? 0.06f : 0f;
        s.Sky.SetFloat("_Exposure", night ? 0.05f : (view == "low" ? 1.0f : 1.3f));
        s.Sky.SetFloat("_SunSize", 0.035f);
        s.Sky.SetFloat("_AtmosphereThickness", view == "low" ? 1.3f : 1.0f);
        RenderSettings.ambientSkyColor = night ? new Color(0.012f, 0.015f, 0.03f) : (view == "low" ? new Color(0.42f, 0.44f, 0.55f) : new Color(0.45f, 0.55f, 0.72f));
        RenderSettings.ambientEquatorColor = night ? new Color(0.01f, 0.011f, 0.02f) : new Color(0.38f, 0.42f, 0.45f);
        RenderSettings.ambientGroundColor = night ? new Color(0.005f, 0.005f, 0.006f) : new Color(0.2f, 0.2f, 0.17f);
        Shader.SetGlobalFloat("_PreviewNight", night ? 1f : 0f);
        Shader.SetGlobalFloat("_PreviewLitShare", 0.25f);

        // The camera, city-builder style.
        Vector3 target;
        float yaw, pitch, dist;
        switch (view)
        {
            case "steep": target = new Vector3(0f, 40f, 0f); yaw = 60f; pitch = 55f; dist = 1000f; break;
            case "street":
            case "nightstreet": target = new Vector3(20f, 140f, 30f); yaw = 20f; pitch = -28f; dist = 160f; break;
            case "close":
            case "nightclose": target = new Vector3(0f, 60f, 0f); yaw = 35f; pitch = 20f; dist = 260f; break;
            case "near":
            case "nightnear": target = new Vector3(0f, 50f, 0f); yaw = 35f; pitch = 12f; dist = 110f; break;
            // The middle block's other full-glass tower, from the street on its far side.
            case "near2":
            case "nightnear2": target = new Vector3(0f, 50f, 0f); yaw = 215f; pitch = 12f; dist = 110f; break;
            default: target = new Vector3(0f, 70f, 0f); yaw = 30f; pitch = 32f; dist = 650f; break;
        }
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 camPos = target - rot * Vector3.forward * dist;
        bool street = view == "street" || view == "nightstreet";
        if (street)
            camPos = new Vector3(-40f, 3f, -95f);
        s.Camera.transform.position = camPos;
        s.Camera.transform.rotation = street ? Quaternion.LookRotation(target - camPos) : rot;

        // The glass, as the mod sets it.
        bool drawGlass = variant.StartsWith("glass", StringComparison.OrdinalIgnoreCase);
        GlassParams p = GlassParams.Default();
        _debug = 0f;
        _debugLevel = 0f;
        _tier = "near";
        _quality = GlassQuality.Ultra;
        _hizOff = false;
        _budgetOverrides.Clear();
        _asset = new AssetOverride();
        _hour = null;
        if (drawGlass)
            p = Override(p, variant);

        // Every building's kind of rooms, as the mod's AssetMap writes it while there are rooms;
        // then the full-glass tower's own settings, as it writes an asset's.
        s.Assets.ClearAll();
        if (p.Interiors)
        {
            for (int i = 0; i < s.Buildings.Count; i++)
            {
                byte a = Interiors.Texel(RoomKindOf(s.Buildings[i], i + 1), Interiors.FollowGlobal);
                s.Assets.Set(i + 1, new Color32(AssetTextures.Global, AssetTextures.Global, AssetTextures.Global, a));
            }
        }
        if (_asset.Strength.HasValue || _asset.Detail.HasValue || _asset.HasGlass)
        {
            byte slot = AssetTextures.Global;
            if (_asset.HasGlass)
            {
                Color c = _asset.Tint ?? new Color(p.TintR, p.TintG, p.TintB);
                s.Assets.SetGlass(0, GlassUniforms.Tint(c.r, c.g, c.b), _asset.Own ?? p.OwnColour,
                    _asset.Reflectivity ?? p.Reflectivity, _asset.Clarity ?? p.Clarity, _asset.Threshold ?? p.Threshold,
                    _asset.Night ?? p.NightWindows);
                slot = 0;
            }
            s.Assets.Set(s.FullGlassId, new Color32(
                _asset.Strength.HasValue ? AssetTextures.Encode(_asset.Strength.Value) : AssetTextures.Global,
                _asset.Detail.HasValue ? AssetTextures.Encode(_asset.Detail.Value) : AssetTextures.Global,
                slot, p.Interiors ? Interiors.Texel(Interiors.KindOffice, Interiors.FollowGlobal) : AssetTextures.Global));
        }
        s.Assets.Upload();
        s.Assets.Bind();
        Shader.SetGlobalVector("_RGDebug", new Vector4(_debug, _debugLevel, 0f, 0f));
        GlassFrame f = new GlassFrame();
        f.SunX = toSun.x; f.SunY = toSun.y; f.SunZ = toSun.z;
        Color sunLin = s.Sun.color.linear * s.Sun.intensity;
        f.SunR = sunLin.r; f.SunG = sunLin.g; f.SunB = sunLin.b;
        Color sky = RenderSettings.ambientSkyColor.linear, ground = RenderSettings.ambientGroundColor.linear;
        f.AmbSkyR = sky.r; f.AmbSkyG = sky.g; f.AmbSkyB = sky.b;
        f.AmbGroundR = ground.r; f.AmbGroundG = ground.g; f.AmbGroundB = ground.b;
        f.GroundY = 0f;
        f.Temperature = view == "low" ? 8f : 24f;
        f.ScreenHeight = s.Height;
        f.ColorMapWidth = ColorMapWidth;
        f.ColorMapHeight = ColorMapHeight;
        f.Hour = _hour ?? (night ? 21f : 13f);
        f.Clock = 24f * 10f + f.Hour;

        // The clouds' stand-in round the camera, lit by the sun of the moment (VW follows the camera
        // the same way).
        s.Clouds.transform.position = camPos;
        Color cloudLight = night ? new Color(0.01f, 0.012f, 0.02f) : sunLin * 0.55f + sky;
        Shader.SetGlobalVector("_PreviewCloudLight", new Vector4(cloudLight.r, cloudLight.g, cloudLight.b, 0f));

        // No game map here: its stand-in is the live sky WITHOUT clouds (the game's map is a
        // static picture); the live sky proper carries them.
        s.SkyCube.Update(true, 128, 6, null, camPos, p.SkySource == 0 && p.SkyClouds);
        f.CubeMips = s.SkyCube.Mips;
        s.City.Bind(p.Neighbours);
        GlassQuality q = GlassQuality.ForTier(_quality);
        object boxedQ = q;
        foreach (KeyValuePair<string, int> o in _budgetOverrides)
        {
            FieldInfo qf = typeof(GlassQuality).GetField(o.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (qf == null)
                throw new Exception("GlassQuality has no field '" + o.Key + "'");
            qf.SetValue(boxedQ, o.Value);
        }
        q = (GlassQuality)boxedQ;
        GlassUniforms.Apply(p, f, q);
        Interiors.Apply(p, f, s.Rooms, p.Interiors && s.Rooms.Loaded);
        bool screenOk = false;

        // Two renders: the first leaves the finished picture and the camera's matrices for the
        // second, which reads them as the mod's glass reads last frame's.
        for (int pass = 0; pass < 2; pass++)
        {
            screenOk = s.Screen.Update(s.Camera, s.Util, p.Finished, _hizOff ? 0 : q.HiZLevels);
            DrawCity(s, drawGlass);
            s.Camera.Render();
        }

        Color[] pixels = ReadPixels(s);

        Color mean = Color.black;
        for (int i = 0; i < pixels.Length; i++)
            mean += pixels[i];
        mean /= pixels.Length;
        info = "mean " + Fmt(mean) + " | screen copy " + (screenOk ? s.Screen.Width + "x" + s.Screen.Height : "FAILED " + s.Screen.Problem) +
               " | sky cube " + (p.SkySource == 0 ? s.SkyCube.FacesDone + " faces, " + s.SkyCube.Mips + " mips" + (s.SkyCube.Problem.Length > 0 ? " " + s.SkyCube.Problem : "") : "game map") +
               " | stride " + GlassUniforms.Stride(p.Quality, s.Height).ToString("0", CultureInfo.InvariantCulture) + " x " +
               GlassUniforms.StepCount(p.Quality).ToString("0", CultureInfo.InvariantCulture) +
               " | budget " + q + " | hi-z " + s.Screen.HiZLevels + " levels";
        return pixels;
    }

    /// <summary>The game's draws, then ours, for the camera's next render.</summary>
    private static void DrawCity(Scene s, bool drawGlass)
    {
        s.Block.Clear();
        s.Block.SetTexture("_MainTex", s.GroundMain);
        s.Block.SetTexture("_XYSMap", s.GroundXys);
        s.Block.SetTexture("_ACIMap", s.GroundAci);
        Graphics.DrawMesh(s.Ground, Matrix4x4.identity, s.StandIn, 0, s.Camera, 0, s.Block, true, true);
        // The game's own unused LOD slots: 100 km behind the camera (Building.RenderLod).
        Matrix4x4 away = Matrix4x4.TRS(s.Camera.transform.forward * -100000f, Quaternion.identity, Vector3.one);
        var locations = new Matrix4x4[8];
        var states = new Vector4[8];
        var indices = new Vector4[8];
        foreach (Building b in s.Buildings)
        {
            s.Block.Clear();
            s.Block.SetTexture("_MainTex", b.Main);
            s.Block.SetTexture("_XYSMap", b.Xys);
            s.Block.SetTexture("_ACIMap", b.Aci);
            s.Block.SetVector("_ObjectIndex", b.ObjectIndex);
            Graphics.DrawMesh(b.Mesh, b.Matrix, s.StandIn, 0, s.Camera, 0, s.Block, true, true);
            if (!drawGlass)
                continue;

            if (_tier == "lod")
            {
                // The building in slot 5 of its combined mesh: tests the index (color.a x 8),
                // the location and state arrays and the atlas rectangle.
                for (int k = 0; k < 8; k++)
                {
                    locations[k] = away;
                    states[k] = Vector4.zero;
                    indices[k] = Vector4.zero;
                }
                locations[5] = b.Matrix;
                states[5] = new Vector4(0f, 100000f, 0f, 1f);
                indices[5] = b.ObjectIndex;
                var lodBlock = new MaterialPropertyBlock();
                lodBlock.SetMatrixArray("_BuildingLocations", locations);
                lodBlock.SetVectorArray("_BuildingStates", states);
                lodBlock.SetVectorArray("_ObjectIndices", indices);
                Vector3 c = b.Matrix.MultiplyPoint3x4(Vector3.zero);
                b.Combined8.bounds = new Bounds(c + Vector3.up * 150f, new Vector3(400f, 400f, 400f));
                Graphics.DrawMesh(b.Combined8, Matrix4x4.identity, s.GlassLod, 0, s.Camera, 0, lodBlock, false, false);
            }
            else if (_tier == "near")
            {
                s.Block.SetVector("_BuildingState", new Vector4(0f, 100000f, 0f, 1f));
                Graphics.DrawMesh(b.Mesh, b.Matrix, s.Glass, 0, s.Camera, 0, s.Block, false, false);
            }
        }

        if (drawGlass && _tier == "group")
            Graphics.DrawMesh(s.Baked, Matrix4x4.identity, s.GlassGroup, 0, s.Camera, 0, null, false, false);
    }

    /// <summary>"glass:Trace=0;Reflectivity=0.3" -> the defaults with those GlassParams fields set.</summary>
    private static GlassParams Override(GlassParams p, string variant)
    {
        int colon = variant.IndexOf(':');
        if (colon < 0)
            return p;
        object boxed = p;
        foreach (string pair in variant.Substring(colon + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = pair.Split('=');
            if (kv.Length != 2)
                continue;
            if (kv[0].Trim().Equals("Debug", StringComparison.OrdinalIgnoreCase))
            {
                // Not a setting: the shader's check views (GlassCommon.cginc, _RGDebug).
                _debug = float.Parse(kv[1].Trim(), CultureInfo.InvariantCulture);
                continue;
            }
            if (kv[0].Trim().Equals("Level", StringComparison.OrdinalIgnoreCase))
            {
                _debugLevel = float.Parse(kv[1].Trim(), CultureInfo.InvariantCulture);
                continue;
            }
            if (kv[0].Trim().Equals("HiZ", StringComparison.OrdinalIgnoreCase))
            {
                _hizOff = kv[1].Trim() == "0";
                continue;
            }
            if (kv[0].Trim().StartsWith("Q.", StringComparison.OrdinalIgnoreCase))
            {
                _budgetOverrides.Add(new KeyValuePair<string, int>(kv[0].Trim().Substring(2), int.Parse(kv[1].Trim(), CultureInfo.InvariantCulture)));
                continue;
            }
            if (kv[0].Trim().Equals("Tier", StringComparison.OrdinalIgnoreCase))
            {
                _tier = kv[1].Trim().ToLowerInvariant();
                continue;
            }
            if (kv[0].Trim().Equals("Hour", StringComparison.OrdinalIgnoreCase))
            {
                _hour = float.Parse(kv[1].Trim(), CultureInfo.InvariantCulture);
                continue;
            }
            if (kv[0].Trim().Equals("Budget", StringComparison.OrdinalIgnoreCase))
            {
                // Not a setting: the GlassQuality tier (GlassParams.Quality is the screen trace's).
                _quality = int.Parse(kv[1].Trim(), CultureInfo.InvariantCulture);
                continue;
            }
            if (kv[0].Trim().StartsWith("A.", StringComparison.OrdinalIgnoreCase))
            {
                // The full-glass tower's own settings (its asset's): A.Strength, A.Detail,
                // A.Reflectivity, A.Clarity, A.Own, A.Threshold (0..1), A.Night (0/1), A.Tint (#RRGGBB).
                string key = kv[0].Trim().Substring(2).ToLowerInvariant();
                string text = kv[1].Trim();
                if (key == "tint")
                {
                    Color tint;
                    if (!ColorUtility.TryParseHtmlString(text, out tint))
                        throw new Exception("A.Tint: not a colour: " + text);
                    _asset.Tint = tint;
                    continue;
                }
                if (key == "night")
                {
                    _asset.Night = text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                float number = float.Parse(text, CultureInfo.InvariantCulture);
                switch (key)
                {
                    case "strength": _asset.Strength = number; break;
                    case "detail": _asset.Detail = number; break;
                    case "reflectivity": _asset.Reflectivity = number; break;
                    case "clarity": _asset.Clarity = number; break;
                    case "own": _asset.Own = number; break;
                    case "threshold": _asset.Threshold = number; break;
                    default: throw new Exception("no asset setting 'A." + key + "'");
                }
                continue;
            }
            FieldInfo field = typeof(GlassParams).GetField(kv[0].Trim(), BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field == null)
                throw new Exception("GlassParams has no field '" + kv[0] + "'");
            string value = kv[1].Trim();
            if (field.FieldType == typeof(bool))
                field.SetValue(boxed, value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
            else if (field.FieldType == typeof(int))
                field.SetValue(boxed, int.Parse(value, CultureInfo.InvariantCulture));
            else
                field.SetValue(boxed, float.Parse(value, CultureInfo.InvariantCulture));
        }
        return (GlassParams)boxed;
    }

    private static Color[] ReadPixels(Scene s)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = s.Target;
        s.Readback.ReadPixels(new Rect(0, 0, s.Width, s.Height), 0, 0, false);
        s.Readback.Apply(false);
        RenderTexture.active = previous;
        return s.Readback.GetPixels();
    }

    /// <summary>Filmic (Hable 2010, the Uncharted 2 curve) over the exposure, then sRGB: a picture for the eye.</summary>
    private static void SavePng(Color[] pixels, int width, int height, float exposure, string file)
    {
        var png = new Texture2D(width, height, TextureFormat.RGB24, false, false);
        var output = new Color[pixels.Length];
        float white = 1f / Hable(11.2f);
        for (int i = 0; i < pixels.Length; i++)
        {
            Color p = pixels[i] * exposure * 2f;
            output[i] = new Color(Srgb(Hable(p.r) * white), Srgb(Hable(p.g) * white), Srgb(Hable(p.b) * white), 1f);
        }

        png.SetPixels(output);
        png.Apply(false);
        File.WriteAllBytes(file, png.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(png);
    }

    private static float Hable(float x)
    {
        const float A = 0.15f, B = 0.50f, C = 0.10f, D = 0.20f, E = 0.02f, F = 0.30f;
        return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
    }

    private static float Srgb(float c)
    {
        c = Mathf.Clamp01(c);
        return c <= 0.0031308f ? 12.92f * c : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
    }

    private static string Fmt(Color c)
    {
        return "(" + c.r.ToString("F3", CultureInfo.InvariantCulture) + " " + c.g.ToString("F3", CultureInfo.InvariantCulture) + " " +
               c.b.ToString("F3", CultureInfo.InvariantCulture) + ")";
    }

    private static string Env(string name, string fallback)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private static float ParseFloat(string text)
    {
        return float.Parse(text, CultureInfo.InvariantCulture);
    }
}
