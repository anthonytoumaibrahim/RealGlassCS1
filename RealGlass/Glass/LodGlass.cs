using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealGlass.Glass
{
    /// <summary>
    /// One LOD of a building asset (its main building's, or a sub-mesh's) and its model: where the LOD
    /// sits in the game's shared LOD atlas, both meshes, and what <see cref="LodGlass"/> found and
    /// decided. The geometry and the read-back pictures are let go once the LOD has been compared.
    /// </summary>
    public sealed class LodGlassEntry
    {
        /// <summary>The building asset it belongs to (BuildingInfo.name): whose settings it follows.</summary>
        public string Asset;

        /// <summary>This LOD's own name (the asset's, or "asset / sub-mesh"), for the log.</summary>
        public string Name;

        /// <summary>Its rectangle in the LOD atlas, in uv: x, y, width, height (the LOD mesh's uv3 / uv4).</summary>
        public Vector4 Rect;

        /// <summary>The model's shine map (its material's _XYSMap), that material's _MainTex_ST, and whether the map clamps.</summary>
        public Texture ModelXys;
        public Vector4 ModelSt;
        public bool ModelClamp;

        /// <summary>
        /// Both meshes in the asset's own space (the game places the LOD with the model's matrix):
        /// positions x, y, z; uv u, v (the LOD's in its own texture, 0..1); triangles.
        /// </summary>
        public float[] ModelPos, ModelUv, LodPos, LodUv;
        public int[] ModelTri, LodTri;

        /// <summary>The grid over the LOD's own texture the model's glass is carried onto (the mask's texels there).</summary>
        public int SideX, SideY;

        /// <summary>
        /// Read back from the GPU, <see cref="LodGlass.Tile"/> x Tile, shine 0..255: the model's over its
        /// whole shine map, and the LOD's own over its texture (its rectangle of the atlas).
        /// </summary>
        public byte[] ModelShine, LodShine;

        // ---- compared (LodGlass.Transfer, on a worker thread) ----
        public bool Transferred;

        /// <summary>Why it could not be compared; "" when it was.</summary>
        public string Problem = "";

        /// <summary>The grid's texels the LOD's mesh covers, and of those, the ones that met the model.</summary>
        public int OnLod, Met;

        /// <summary>How far the LOD's surface is from the model's at the texels that met it (the median), metres.</summary>
        public float Gap;

        /// <summary>
        /// Where they met: the LOD's own shine against the model's, 32 x 32 bins, [lod x 32 + model]. The
        /// shares and their agreement at any shininess needed come from it (no new comparison).
        /// </summary>
        public int[] Joint;

        /// <summary>SideX x SideY, 0..255: the shine to draw on the LOD -- the model's where the LOD met it,
        /// the LOD's own elsewhere.</summary>
        public byte[] Grid;

        // ---- decided (LodGlass.DecideAll) ----
        public int Mode;
        public float Threshold;
        public int Verdict;
        public float Reach, ModelShare, LodShare, Agreement;
    }

    /// <summary>
    /// The model's glass on its LOD, where the LOD has none or has it elsewhere. Every LOD texel is a
    /// point on the LOD's surface; the model is met there, along the LOD's normal, and its shine read
    /// where it was met -- the model's glass carried onto the LOD by the building's geometry, not by
    /// texture coordinates (Cignoni, Montani, Rocchini and Scopigno, "A general method for preserving
    /// attribute values on simplified meshes", IEEE Visualization 1998; the game's own asset importer
    /// bakes an LOD's textures from its model this way when the author gives none). The same points
    /// say whether the LOD's own glass agrees with its model's (the Jaccard index of the two). Engine
    /// types only, C# 4: the GPU preview runs this same file.
    /// </summary>
    /// <remarks>
    /// <para>READ BACK (GlassUtil pass 6, the GPU): each model's shine map and its LOD's own texture,
    /// both at <see cref="Tile"/> x Tile, into tiles of one target read back at once; a tile of known
    /// pattern says which way the rows came back.</para>
    /// <para>COMPARE (<see cref="Transfer"/>, pure, a worker thread): the model's triangles in a
    /// uniform grid (Fujimoto, Tanaka and Iwata, "ARTS: Accelerated Ray-Tracing System", IEEE CG&amp;A
    /// 1986); each LOD texel's line along the LOD's normal met with them (Moller and Trumbore, "Fast,
    /// minimum storage ray-triangle intersection", JGT 1997), the nearest within <see cref="ReachMetres"/>
    /// on a face turned the same way; the model's shine there read from a mip of its picture as wide
    /// as the texel (Williams 1983).</para>
    /// <para>DECIDE (<see cref="Decide"/>): the LOD keeps its own glass where it agrees with its model's,
    /// takes the model's where it does not -- none of its own, or glass elsewhere -- unless its shape
    /// is not its model's.</para>
    /// <para>MASK (<see cref="BuildMask"/>, pure; <see cref="Upload"/>): the shine to draw, laid out as
    /// the atlas (one byte a texel, 1 + 254 x shine where an LOD takes its model's, 0 where it keeps its
    /// own), mipmapped on the CPU so a coarse level never mixes the two; Glass.shader reads it on the
    /// middle and far tiers.</para>
    /// </remarks>
    public sealed class LodGlass
    {
        /// <summary>The asset's own say (its building window): its own setting, the model's glass, the LOD's own.</summary>
        public const int ModeAuto = 0, ModeModel = 1, ModeOwn = 2;

        public const int VerdictNotMeasured = 0;
        public const int VerdictAgrees = 1;          // its glass agrees with its model's: its own kept
        public const int VerdictNoGlass = 2;         // neither has glass
        public const int VerdictBorrowed = 3;        // it has none, or has it elsewhere: the model's
        public const int VerdictShapeDiffers = 4;    // its shape is not its model's: its own kept
        public const int VerdictForced = 5;          // the asset asks for the model's
        public const int VerdictKeptOwn = 6;         // the asset asks for its own
        public const int VerdictOff = 7;             // the switch is off
        public const int VerdictFailed = 8;          // could not be compared

        /// <summary>The model is looked for this far either side of the LOD's surface, metres.</summary>
        public const float ReachMetres = 3f;

        /// <summary>A face of the model counts only if it is turned within 60 degrees of the LOD's (cos).</summary>
        public const float MinFacing = 0.5f;

        /// <summary>Its shape is its model's: at least this share of its texels met the model.</summary>
        public const float MinReach = 0.6f;

        /// <summary>Its glass agrees with its model's: the two glasses' Jaccard index at least this.</summary>
        public const float MinAgreement = 0.6f;

        /// <summary>Less glass than this, on both: neither has any worth a word.</summary>
        public const float MinShare = 0.01f;

        /// <summary>The read-back pictures' side, and the grid's largest side over an LOD's texture.</summary>
        public const int Tile = 256, MaxSide = 128, MinSide = 8;

        /// <summary>The joint histogram's bins a side.</summary>
        public const int Bins = 32;

        /// <summary>The mask's largest side (one byte a texel: 16 MB and its mips at 4096).</summary>
        public const int MaxMaskSize = 4096;

        public const int PassSample = 6;

        /// <summary>A read-back batch: 8 x 2 tiles (2048 x 512), tile 0 the marker.</summary>
        public const int TilesPerRow = 8, TileRows = 2, PerBatch = TilesPerRow * TileRows - 1;

        private static readonly Color32 MarkLow = new Color32(0, 255, 0, 255), MarkHigh = new Color32(255, 0, 255, 0);

        private RenderTexture _target;
        private Texture2D _read, _mask, _none;
        private Mesh _quad;
        private bool _ids;
        private int _idTarget, _idRect, _idModel, _idInfo, _idAtlasXys, _idMask;

        /// <summary>The read-back rows ran the other way from texture space (logged once).</summary>
        public bool Flipped { get; private set; }

        /// <summary>Why the last call failed; "" when it did not.</summary>
        public string Problem { get; private set; }

        public int MaskWidth { get; private set; }
        public int MaskHeight { get; private set; }

        /// <summary>The mask itself (null while no LOD takes its model's glass): for the editor's test.</summary>
        public Texture Mask
        {
            get { return _mask; }
        }

        public LodGlass()
        {
            Problem = "";
        }

        private void Ids()
        {
            if (_ids)
                return;
            _idTarget = Shader.PropertyToID("_RGLodTarget");
            _idRect = Shader.PropertyToID("_RGLodRect");
            _idModel = Shader.PropertyToID("_RGLodModel");
            _idInfo = Shader.PropertyToID("_RGLodInfo");
            _idAtlasXys = Shader.PropertyToID("_RGLodAtlasXys");
            _idMask = Shader.PropertyToID("_RGLodGlass");
            _ids = true;
        }

        // ---------------------------------------------------------------------------------------
        // Read back (the GPU, main thread).
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Entries start .. start + count - 1 (at most <see cref="PerBatch"/>): each one's model shine and
        /// LOD's own shine read back into its ModelShine / LodShine. False, with <see cref="Problem"/>,
        /// when it could not.
        /// </summary>
        public bool ReadBack(List<LodGlassEntry> entries, int start, int count, Texture atlasXys, Material util)
        {
            Ids();
            Problem = "";
            if (count <= 0)
                return true;
            if (util == null || atlasXys == null)
            {
                Problem = util == null ? "no GlassUtil shader" : "no LOD atlas";
                return false;
            }
            count = Math.Min(count, PerBatch);

            int width = TilesPerRow * Tile, height = TileRows * Tile;
            if (_quad == null)
                _quad = GlassScreen.ClipQuad();
            if (_target == null)
            {
                _target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                _target.name = "RealGlass LOD glass read-back";
                _target.hideFlags = HideFlags.HideAndDontSave;
                _target.filterMode = FilterMode.Point;
                _target.Create();
            }
            if (_read == null)
            {
                _read = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                _read.hideFlags = HideFlags.HideAndDontSave;
            }

            util.SetTexture(_idAtlasXys, atlasXys);
            RenderTexture before = RenderTexture.active;
            var cb = new CommandBuffer();
            cb.name = "RealGlass LOD glass: read-back";
            try
            {
                cb.SetRenderTarget(_target);
                cb.ClearRenderTarget(false, true, new Color(0f, 0f, 0f, 0f));

                var marker = new MaterialPropertyBlock();
                marker.SetVector(_idTarget, TileRect(0, width, height));
                marker.SetVector(_idInfo, new Vector4(1f, 0f, 0f, 0f));
                cb.DrawMesh(_quad, Matrix4x4.identity, util, 0, PassSample, marker);

                for (int i = 0; i < count; i++)
                {
                    LodGlassEntry e = entries[start + i];
                    var block = new MaterialPropertyBlock();
                    block.SetVector(_idTarget, TileRect(i + 1, width, height));
                    block.SetVector(_idRect, e.Rect);
                    // A missing shine map reads as none (z = 1), as the game reads one.
                    block.SetTexture(_idModel, e.ModelXys != null ? e.ModelXys : (Texture)Texture2D.whiteTexture);
                    block.SetVector(_idInfo, new Vector4(0f, MipFor(e.ModelXys != null ? Math.Max(e.ModelXys.width, e.ModelXys.height) : 1),
                                                         MipFor(Math.Max(e.Rect.z * atlasXys.width, e.Rect.w * atlasXys.height)), 0f));
                    cb.DrawMesh(_quad, Matrix4x4.identity, util, 0, PassSample, block);
                }

                Graphics.ExecuteCommandBuffer(cb);
                RenderTexture.active = _target;
                _read.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                Color32[] pixels = _read.GetPixels32();

                bool flipped;
                if (!FindMarker(pixels, width, height, out flipped))
                {
                    Problem = "the read-back's marker tile was not where it was drawn";
                    return false;
                }
                Flipped = flipped;
                for (int i = 0; i < count; i++)
                {
                    int tile = i + 1;
                    TakeTile(entries[start + i], pixels, width, height, (tile % TilesPerRow) * Tile, (tile / TilesPerRow) * Tile, flipped);
                }
                return true;
            }
            finally
            {
                RenderTexture.active = before;
                cb.Release();
            }
        }

        /// <summary>The mip of a picture size pixels wide that is <see cref="Tile"/> wide: each read-back texel is the average of what it covers.</summary>
        public static float MipFor(double size)
        {
            return size > Tile ? (float)(Math.Log(size / Tile) / Math.Log(2.0)) : 0f;
        }

        /// <summary>Tile k of a target, in texture space (x, y, width, height).</summary>
        private static Vector4 TileRect(int k, int width, int height)
        {
            return new Vector4((float)(k % TilesPerRow) * Tile / width, (float)(k / TilesPerRow) * Tile / height,
                               (float)Tile / width, (float)Tile / height);
        }

        /// <summary>
        /// Tile 0 holds MarkLow in its lower half (v &lt; 0.5) and MarkHigh above. Read-back row 0 is
        /// texture row 0, or the target's last (flipped). False when it is neither.
        /// </summary>
        public static bool FindMarker(Color32[] pixels, int width, int height, out bool flipped)
        {
            flipped = false;
            if (pixels == null || pixels.Length < width * height || height < Tile || width < Tile)
                return false;

            if (Same(pixels[1], MarkLow) && Same(pixels[(Tile / 2 - 1) * width + 1], MarkLow) &&
                Same(pixels[(Tile / 2) * width + 1], MarkHigh) && Same(pixels[(Tile - 1) * width + 1], MarkHigh))
            {
                return true;
            }

            if (Same(pixels[(height - 1) * width + 1], MarkLow) && Same(pixels[(height - Tile / 2) * width + 1], MarkLow) &&
                Same(pixels[(height - Tile / 2 - 1) * width + 1], MarkHigh) && Same(pixels[(height - Tile) * width + 1], MarkHigh))
            {
                flipped = true;
                return true;
            }

            return false;
        }

        private static bool Same(Color32 a, Color32 b)
        {
            return Math.Abs(a.r - b.r) <= 2 && Math.Abs(a.g - b.g) <= 2 && Math.Abs(a.b - b.b) <= 2 && Math.Abs(a.a - b.a) <= 2;
        }

        /// <summary>One entry's tile (texture rows y0.., columns x0..): r the model's shine, g the LOD's own.</summary>
        public static void TakeTile(LodGlassEntry e, Color32[] pixels, int width, int height, int x0, int y0, bool flipped)
        {
            var model = new byte[Tile * Tile];
            var lod = new byte[Tile * Tile];
            for (int r = 0; r < Tile; r++)
            {
                int row = flipped ? height - 1 - (y0 + r) : y0 + r;
                for (int c = 0; c < Tile; c++)
                {
                    Color32 p = pixels[row * width + x0 + c];
                    model[r * Tile + c] = p.r;
                    lod[r * Tile + c] = p.g;
                }
            }
            e.ModelShine = model;
            e.LodShine = lod;
        }

        // ---------------------------------------------------------------------------------------
        // Compare (pure: a worker thread; tools/test-settings.ps1 runs it).
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The grid over the LOD's own texture for an atlas rectangle drawn maskW x maskH: the mask's
        /// texels there, between <see cref="MinSide"/> and <see cref="MaxSide"/> a side.
        /// </summary>
        public static void SideFor(LodGlassEntry e, int maskW, int maskH)
        {
            e.SideX = Math.Max(MinSide, Math.Min(MaxSide, (int)Math.Round(e.Rect.z * maskW)));
            e.SideY = Math.Max(MinSide, Math.Min(MaxSide, (int)Math.Round(e.Rect.w * maskH)));
        }

        /// <summary>
        /// The LOD met with its model, texel by texel of its grid: <see cref="LodGlassEntry.Grid"/>, the
        /// counts and <see cref="LodGlassEntry.Joint"/>. Never throws: a problem is written to Problem
        /// and the entry stays untransferred. The geometry and pictures are let go either way.
        /// </summary>
        public static void Transfer(LodGlassEntry e)
        {
            try
            {
                string problem = Check(e);
                if (problem.Length > 0)
                {
                    e.Problem = problem;
                    return;
                }
                TransferChecked(e);
                e.Transferred = true;
            }
            catch (Exception ex)
            {
                e.Problem = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                e.ModelPos = e.ModelUv = e.LodPos = e.LodUv = null;
                e.ModelTri = e.LodTri = null;
                e.ModelShine = e.LodShine = null;
            }
        }

        private static string Check(LodGlassEntry e)
        {
            if (e.ModelPos == null || e.ModelPos.Length < 9 || e.ModelTri == null || e.ModelTri.Length < 3)
                return "the model's mesh could not be read";
            if (e.ModelUv == null || e.ModelUv.Length / 2 < e.ModelPos.Length / 3)
                return "the model's mesh has no texture coordinates";
            if (e.LodPos == null || e.LodPos.Length < 9 || e.LodTri == null || e.LodTri.Length < 3 ||
                e.LodUv == null || e.LodUv.Length / 2 < e.LodPos.Length / 3)
                return "the LOD's mesh could not be read";
            if (e.ModelShine == null || e.ModelShine.Length != Tile * Tile || e.LodShine == null || e.LodShine.Length != Tile * Tile)
                return "the pictures were not read back";
            if (e.SideX < 1 || e.SideY < 1)
                return "no grid";
            return "";
        }

        private static void TransferChecked(LodGlassEntry e)
        {
            int sx = e.SideX, sy = e.SideY, n = sx * sy;
            var grid = new MeshGrid(e.ModelPos, e.ModelTri);

            // Per model triangle: its texture coordinates per metre (the read-back's texels per metre
            // follow), for the mip a texel's footprint needs.
            float stx = Math.Abs(e.ModelSt.x), sty = Math.Abs(e.ModelSt.y);
            int mt = e.ModelTri.Length / 3;
            var uvPerMetre = new float[mt];
            for (int t = 0; t < mt; t++)
            {
                int i0 = e.ModelTri[3 * t], i1 = e.ModelTri[3 * t + 1], i2 = e.ModelTri[3 * t + 2];
                double area = TriangleArea(e.ModelPos, i0, i1, i2);
                double du1 = (e.ModelUv[2 * i1] - e.ModelUv[2 * i0]) * stx, dv1 = (e.ModelUv[2 * i1 + 1] - e.ModelUv[2 * i0 + 1]) * sty;
                double du2 = (e.ModelUv[2 * i2] - e.ModelUv[2 * i0]) * stx, dv2 = (e.ModelUv[2 * i2 + 1] - e.ModelUv[2 * i0 + 1]) * sty;
                double uvArea = Math.Abs(du1 * dv2 - du2 * dv1) * 0.5;
                uvPerMetre[t] = area > 1e-9 ? (float)Math.Sqrt(uvArea / area) : 0f;
            }

            Mips model = new Mips(e.ModelShine, Tile);
            var sum = new float[n];
            var hits = new int[n];
            var on = new bool[n];
            var gaps = new List<float>();
            float[] P = e.LodPos, U = e.LodUv;
            int[] T = e.LodTri;

            for (int t = 0; t + 2 < T.Length; t += 3)
            {
                int a = T[t], b = T[t + 1], c = T[t + 2];
                if (a < 0 || b < 0 || c < 0 || 3 * a + 2 >= P.Length || 3 * b + 2 >= P.Length || 3 * c + 2 >= P.Length ||
                    2 * a + 1 >= U.Length || 2 * b + 1 >= U.Length || 2 * c + 1 >= U.Length)
                {
                    continue;
                }

                // Its plane and its normal.
                float ax = P[3 * a], ay = P[3 * a + 1], az = P[3 * a + 2];
                float e1x = P[3 * b] - ax, e1y = P[3 * b + 1] - ay, e1z = P[3 * b + 2] - az;
                float e2x = P[3 * c] - ax, e2y = P[3 * c + 1] - ay, e2z = P[3 * c + 2] - az;
                float nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (!(len > 1e-8f))
                    continue;
                float area3 = len * 0.5f;
                nx /= len;
                ny /= len;
                nz /= len;

                // Its texels: those whose centre it covers.
                float u0 = U[2 * a] * sx, v0 = U[2 * a + 1] * sy, u1 = U[2 * b] * sx, v1 = U[2 * b + 1] * sy, u2 = U[2 * c] * sx, v2 = U[2 * c + 1] * sy;
                float den = (v1 - v2) * (u0 - u2) + (u2 - u1) * (v0 - v2);
                if (!(Math.Abs(den) > 1e-9f) || float.IsNaN(den) || float.IsInfinity(den))
                    continue;
                float metresPerTexel = (float)Math.Sqrt(area3 / (Math.Abs(den) * 0.5f));
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(u0, Math.Min(u1, u2)) - 0.5f));
                int x1 = Math.Min(sx - 1, (int)Math.Ceiling(Math.Max(u0, Math.Max(u1, u2)) - 0.5f));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(v0, Math.Min(v1, v2)) - 0.5f));
                int y1 = Math.Min(sy - 1, (int)Math.Ceiling(Math.Max(v0, Math.Max(v1, v2)) - 0.5f));
                const float eps = -1e-4f;
                for (int y = y0; y <= y1; y++)
                {
                    float py = y + 0.5f;
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f;
                        float w0 = ((v1 - v2) * (px - u2) + (u2 - u1) * (py - v2)) / den;
                        float w1 = ((v2 - v0) * (px - u2) + (u0 - u2) * (py - v2)) / den;
                        float w2 = 1f - w0 - w1;
                        if (w0 < eps || w1 < eps || w2 < eps)
                            continue;

                        int k = y * sx + x;
                        on[k] = true;
                        float wx = ax + w1 * e1x + w2 * e2x, wy = ay + w1 * e1y + w2 * e2y, wz = az + w1 * e1z + w2 * e2z;
                        float s, b1, b2;
                        int tri;
                        if (!grid.Meet(wx, wy, wz, nx, ny, nz, ReachMetres, MinFacing, out s, out tri, out b1, out b2))
                            continue;

                        int m0 = e.ModelTri[3 * tri], m1 = e.ModelTri[3 * tri + 1], m2 = e.ModelTri[3 * tri + 2];
                        float b0 = 1f - b1 - b2;
                        float mu = (b0 * e.ModelUv[2 * m0] + b1 * e.ModelUv[2 * m1] + b2 * e.ModelUv[2 * m2]) * e.ModelSt.x + e.ModelSt.z;
                        float mv = (b0 * e.ModelUv[2 * m0 + 1] + b1 * e.ModelUv[2 * m1 + 1] + b2 * e.ModelUv[2 * m2 + 1]) * e.ModelSt.y + e.ModelSt.w;
                        float footprint = metresPerTexel * uvPerMetre[tri] * Tile;
                        float level = footprint > 1f ? (float)(Math.Log(footprint) / Math.Log(2.0)) : 0f;
                        sum[k] += model.Sample(mu, mv, level, e.ModelClamp);
                        hits[k]++;
                        if ((k & 7) == 0)
                            gaps.Add(Math.Abs(s));
                    }
                }
            }

            // The shine to draw, and the comparison where the two met.
            var joint = new int[Bins * Bins];
            var value = new float[n];
            var set = new bool[n];
            int onLod = 0, met = 0;
            for (int y = 0; y < sy; y++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int k = y * sx + x;
                    if (!on[k])
                        continue;
                    onLod++;
                    float own = Sample(e.LodShine, Tile, (x + 0.5f) / sx, (y + 0.5f) / sy);
                    if (hits[k] > 0)
                    {
                        met++;
                        float m = sum[k] / hits[k];
                        joint[Bin(own) * Bins + Bin(m)]++;
                        value[k] = m;
                    }
                    else
                    {
                        value[k] = own;
                    }
                    set[k] = true;
                }
            }

            // Round each island, the shine of its edge carried a few texels out (a mip or a filtered
            // read there would otherwise take in the unused texture between islands); further out
            // the LOD's own.
            Dilate(value, set, sx, sy, 3);
            var bytes = new byte[n];
            for (int y = 0; y < sy; y++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int k = y * sx + x;
                    float v = set[k] ? value[k] : Sample(e.LodShine, Tile, (x + 0.5f) / sx, (y + 0.5f) / sy);
                    bytes[k] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f)));
                }
            }

            gaps.Sort();
            e.OnLod = onLod;
            e.Met = met;
            e.Gap = gaps.Count > 0 ? gaps[gaps.Count / 2] : 0f;
            e.Joint = joint;
            e.Grid = bytes;
        }

        private static double TriangleArea(float[] p, int i0, int i1, int i2)
        {
            double e1x = p[3 * i1] - p[3 * i0], e1y = p[3 * i1 + 1] - p[3 * i0 + 1], e1z = p[3 * i1 + 2] - p[3 * i0 + 2];
            double e2x = p[3 * i2] - p[3 * i0], e2y = p[3 * i2 + 1] - p[3 * i0 + 1], e2z = p[3 * i2 + 2] - p[3 * i0 + 2];
            double cx = e1y * e2z - e1z * e2y, cy = e1z * e2x - e1x * e2z, cz = e1x * e2y - e1y * e2x;
            return 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
        }

        private static int Bin(float v)
        {
            return Math.Max(0, Math.Min(Bins - 1, (int)(v * Bins)));
        }

        /// <summary>A picture side x side (bytes, 0..255) read bilinearly at (u, v), clamped: 0..1.</summary>
        public static float Sample(byte[] picture, int side, float u, float v)
        {
            float x = Math.Max(0f, Math.Min(side - 1f, u * side - 0.5f));
            float y = Math.Max(0f, Math.Min(side - 1f, v * side - 0.5f));
            int xi = Math.Min(side - 2, (int)x), yi = Math.Min(side - 2, (int)y);
            if (side < 2)
                return picture[0] / 255f;
            float fx = x - xi, fy = y - yi;
            float a = picture[yi * side + xi] + (picture[yi * side + xi + 1] - picture[yi * side + xi]) * fx;
            float b = picture[(yi + 1) * side + xi] + (picture[(yi + 1) * side + xi + 1] - picture[(yi + 1) * side + xi]) * fx;
            return (a + (b - a) * fy) / 255f;
        }

        /// <summary>Unset texels take the mean of their set neighbours (8 round), passes times outwards.</summary>
        public static void Dilate(float[] value, bool[] set, int w, int h, int passes)
        {
            var next = new bool[set.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                bool any = false;
                Array.Copy(set, next, set.Length);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int k = y * w + x;
                        if (set[k])
                            continue;
                        float s = 0f;
                        int c = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int yy = y + dy;
                            if (yy < 0 || yy >= h)
                                continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx;
                                if (xx < 0 || xx >= w || !set[yy * w + xx])
                                    continue;
                                s += value[yy * w + xx];
                                c++;
                            }
                        }
                        if (c > 0)
                        {
                            value[k] = s / c;
                            next[k] = true;
                            any = true;
                        }
                    }
                }
                Array.Copy(next, set, set.Length);
                if (!any)
                    break;
            }
        }

        /// <summary>A read-back picture's mips (box filtered), read trilinearly; wrapped or clamped as its texture is.</summary>
        private sealed class Mips
        {
            private readonly List<float[]> _levels = new List<float[]>();
            private readonly List<int> _sides = new List<int>();

            public Mips(byte[] picture, int side)
            {
                var level = new float[side * side];
                for (int i = 0; i < level.Length; i++)
                    level[i] = picture[i] / 255f;
                _levels.Add(level);
                _sides.Add(side);
                while (side > 1)
                {
                    int half = side / 2;
                    var next = new float[half * half];
                    for (int y = 0; y < half; y++)
                    {
                        for (int x = 0; x < half; x++)
                        {
                            next[y * half + x] = 0.25f * (level[2 * y * side + 2 * x] + level[2 * y * side + 2 * x + 1] +
                                                          level[(2 * y + 1) * side + 2 * x] + level[(2 * y + 1) * side + 2 * x + 1]);
                        }
                    }
                    level = next;
                    side = half;
                    _levels.Add(level);
                    _sides.Add(side);
                }
            }

            public float Sample(float u, float v, float lod, bool clamp)
            {
                if (float.IsNaN(u) || float.IsNaN(v) || float.IsInfinity(u) || float.IsInfinity(v))
                    return 0f;
                if (clamp)
                {
                    u = Math.Max(0f, Math.Min(1f, u));
                    v = Math.Max(0f, Math.Min(1f, v));
                }
                else
                {
                    u -= (float)Math.Floor(u);
                    v -= (float)Math.Floor(v);
                }
                lod = Math.Max(0f, Math.Min(_levels.Count - 1, lod));
                int l0 = (int)lod;
                float f = lod - l0;
                float a = Bilinear(l0, u, v, clamp);
                return f > 0f && l0 + 1 < _levels.Count ? a + (Bilinear(l0 + 1, u, v, clamp) - a) * f : a;
            }

            private float Bilinear(int l, float u, float v, bool clamp)
            {
                float[] p = _levels[l];
                int side = _sides[l];
                float x = u * side - 0.5f, y = v * side - 0.5f;
                int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
                float fx = x - xi, fy = y - yi;
                float p00 = At(p, side, xi, yi, clamp), p10 = At(p, side, xi + 1, yi, clamp);
                float p01 = At(p, side, xi, yi + 1, clamp), p11 = At(p, side, xi + 1, yi + 1, clamp);
                float a = p00 + (p10 - p00) * fx, b = p01 + (p11 - p01) * fx;
                return a + (b - a) * fy;
            }

            private static float At(float[] p, int side, int x, int y, bool clamp)
            {
                if (clamp)
                {
                    x = Math.Max(0, Math.Min(side - 1, x));
                    y = Math.Max(0, Math.Min(side - 1, y));
                }
                else
                {
                    x = ((x % side) + side) % side;
                    y = ((y % side) + side) % side;
                }
                return p[y * side + x];
            }
        }

        /// <summary>
        /// A mesh's triangles in a uniform grid of cells, each cell listing the triangles whose bounds
        /// enter it (compressed rows: one array of starts, one of triangles); a short line is met with
        /// the triangles of the cells its bounds enter.
        /// </summary>
        public sealed class MeshGrid
        {
            private readonly float[] _p;
            private readonly int[] _t;
            private readonly float[] _n;
            private readonly float _minX, _minY, _minZ, _cell;
            private readonly int _nx, _ny, _nz;
            private readonly int[] _start, _items, _stamp;
            private int _now;

            /// <summary>The cells' side, metres, and how many cells there are (for the log and the tests).</summary>
            public float Cell { get { return _cell; } }
            public int Cells { get { return _nx * _ny * _nz; } }

            public MeshGrid(float[] positions, int[] triangles)
            {
                _p = positions;
                _t = triangles;
                int tris = triangles.Length / 3;
                _n = new float[tris * 3];
                _stamp = new int[tris];

                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
                for (int i = 0; i + 2 < positions.Length; i += 3)
                {
                    minX = Math.Min(minX, positions[i]); maxX = Math.Max(maxX, positions[i]);
                    minY = Math.Min(minY, positions[i + 1]); maxY = Math.Max(maxY, positions[i + 1]);
                    minZ = Math.Min(minZ, positions[i + 2]); maxZ = Math.Max(maxZ, positions[i + 2]);
                }
                float pad = ReachMetres + 1f;
                _minX = minX - pad;
                _minY = minY - pad;
                _minZ = minZ - pad;
                float ex = maxX - minX + 2f * pad, ey = maxY - minY + 2f * pad, ez = maxZ - minZ + 2f * pad;

                // Cells of a metre or more, no more than about 64 along the longest side.
                float cell = Math.Max(1f, Math.Max(ex, Math.Max(ey, ez)) / 64f);
                _cell = cell;
                _nx = Math.Max(1, (int)Math.Ceiling(ex / cell));
                _ny = Math.Max(1, (int)Math.Ceiling(ey / cell));
                _nz = Math.Max(1, (int)Math.Ceiling(ez / cell));

                var count = new int[_nx * _ny * _nz + 1];
                for (int pass = 0; pass < 2; pass++)
                {
                    int[] fill = pass == 1 ? new int[count.Length] : null;
                    for (int t = 0; t < tris; t++)
                    {
                        int i0 = triangles[3 * t], i1 = triangles[3 * t + 1], i2 = triangles[3 * t + 2];
                        if (i0 < 0 || i1 < 0 || i2 < 0 || 3 * i0 + 2 >= positions.Length || 3 * i1 + 2 >= positions.Length ||
                            3 * i2 + 2 >= positions.Length)
                        {
                            continue;
                        }
                        if (pass == 0)
                            Normal(t, i0, i1, i2);
                        int ax = Cx(Min3(positions[3 * i0], positions[3 * i1], positions[3 * i2]));
                        int bx = Cx(Max3(positions[3 * i0], positions[3 * i1], positions[3 * i2]));
                        int ay = Cy(Min3(positions[3 * i0 + 1], positions[3 * i1 + 1], positions[3 * i2 + 1]));
                        int by = Cy(Max3(positions[3 * i0 + 1], positions[3 * i1 + 1], positions[3 * i2 + 1]));
                        int az = Cz(Min3(positions[3 * i0 + 2], positions[3 * i1 + 2], positions[3 * i2 + 2]));
                        int bz = Cz(Max3(positions[3 * i0 + 2], positions[3 * i1 + 2], positions[3 * i2 + 2]));
                        for (int z = az; z <= bz; z++)
                        {
                            for (int y = ay; y <= by; y++)
                            {
                                for (int x = ax; x <= bx; x++)
                                {
                                    int cellIndex = (z * _ny + y) * _nx + x;
                                    if (pass == 0)
                                        count[cellIndex + 1]++;
                                    else
                                        _items[_start[cellIndex] + fill[cellIndex]++] = t;
                                }
                            }
                        }
                    }
                    if (pass == 0)
                    {
                        for (int i = 1; i < count.Length; i++)
                            count[i] += count[i - 1];
                        _start = count;
                        _items = new int[count[count.Length - 1]];
                    }
                }
            }

            private void Normal(int t, int i0, int i1, int i2)
            {
                float e1x = _p[3 * i1] - _p[3 * i0], e1y = _p[3 * i1 + 1] - _p[3 * i0 + 1], e1z = _p[3 * i1 + 2] - _p[3 * i0 + 2];
                float e2x = _p[3 * i2] - _p[3 * i0], e2y = _p[3 * i2 + 1] - _p[3 * i0 + 1], e2z = _p[3 * i2 + 2] - _p[3 * i0 + 2];
                float nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (l > 1e-12f)
                {
                    _n[3 * t] = nx / l;
                    _n[3 * t + 1] = ny / l;
                    _n[3 * t + 2] = nz / l;
                }
            }

            private static float Min3(float a, float b, float c) { return Math.Min(a, Math.Min(b, c)); }
            private static float Max3(float a, float b, float c) { return Math.Max(a, Math.Max(b, c)); }
            private int Cx(float v) { return Math.Max(0, Math.Min(_nx - 1, (int)((v - _minX) / _cell))); }
            private int Cy(float v) { return Math.Max(0, Math.Min(_ny - 1, (int)((v - _minY) / _cell))); }
            private int Cz(float v) { return Math.Max(0, Math.Min(_nz - 1, (int)((v - _minZ) / _cell))); }

            /// <summary>
            /// The line p + d s, |s| &lt;= reach (d a unit vector), met with the mesh: the nearest point on a
            /// face turned within acos(minFacing) of d (either side). Its s, its triangle and that
            /// triangle's barycentric weights of corners 1 and 2. Not thread-safe (one grid a thread).
            /// </summary>
            public bool Meet(float px, float py, float pz, float dx, float dy, float dz, float reach, float minFacing,
                             out float s, out int tri, out float b1, out float b2)
            {
                s = 0f;
                tri = -1;
                b1 = b2 = 0f;
                if (++_now == int.MaxValue)
                {
                    Array.Clear(_stamp, 0, _stamp.Length);
                    _now = 1;
                }

                int ax = Cx(Math.Min(px - dx * reach, px + dx * reach)), bx = Cx(Math.Max(px - dx * reach, px + dx * reach));
                int ay = Cy(Math.Min(py - dy * reach, py + dy * reach)), by = Cy(Math.Max(py - dy * reach, py + dy * reach));
                int az = Cz(Math.Min(pz - dz * reach, pz + dz * reach)), bz = Cz(Math.Max(pz - dz * reach, pz + dz * reach));
                float best = float.MaxValue;
                float[] P = _p;
                for (int z = az; z <= bz; z++)
                {
                    for (int y = ay; y <= by; y++)
                    {
                        for (int x = ax; x <= bx; x++)
                        {
                            int cell = (z * _ny + y) * _nx + x;
                            for (int j = _start[cell]; j < _start[cell + 1]; j++)
                            {
                                int t = _items[j];
                                if (_stamp[t] == _now)
                                    continue;
                                _stamp[t] = _now;
                                float facing = _n[3 * t] * dx + _n[3 * t + 1] * dy + _n[3 * t + 2] * dz;
                                if (Math.Abs(facing) < minFacing)
                                    continue;

                                // Moller-Trumbore.
                                int i0 = _t[3 * t], i1 = _t[3 * t + 1], i2 = _t[3 * t + 2];
                                float e1x = P[3 * i1] - P[3 * i0], e1y = P[3 * i1 + 1] - P[3 * i0 + 1], e1z = P[3 * i1 + 2] - P[3 * i0 + 2];
                                float e2x = P[3 * i2] - P[3 * i0], e2y = P[3 * i2 + 1] - P[3 * i0 + 1], e2z = P[3 * i2 + 2] - P[3 * i0 + 2];
                                float hx = dy * e2z - dz * e2y, hy = dz * e2x - dx * e2z, hz = dx * e2y - dy * e2x;
                                float det = e1x * hx + e1y * hy + e1z * hz;
                                if (Math.Abs(det) < 1e-12f)
                                    continue;
                                float inv = 1f / det;
                                float qx = px - P[3 * i0], qy = py - P[3 * i0 + 1], qz = pz - P[3 * i0 + 2];
                                float u = inv * (qx * hx + qy * hy + qz * hz);
                                if (u < -1e-4f || u > 1.0001f)
                                    continue;
                                float rx = qy * e1z - qz * e1y, ry = qz * e1x - qx * e1z, rz = qx * e1y - qy * e1x;
                                float v = inv * (dx * rx + dy * ry + dz * rz);
                                if (v < -1e-4f || u + v > 1.0001f)
                                    continue;
                                float d = inv * (e2x * rx + e2y * ry + e2z * rz);
                                float ad = Math.Abs(d);
                                if (ad > reach || ad >= best)
                                    continue;
                                best = ad;
                                s = d;
                                tri = t;
                                b1 = Math.Max(0f, Math.Min(1f, u));
                                b2 = Math.Max(0f, Math.Min(1f - b1, v));
                            }
                        }
                    }
                }
                return tri >= 0;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Decide (pure).
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The shares of glass where the LOD met its model, at a shininess needed: the LOD's own, the
        /// model's, and the two's Jaccard index (both / either; 1 when neither has any). Each bin
        /// counts as spread evenly over its width.
        /// </summary>
        public static void Shares(int[] joint, float threshold, out float lod, out float model, out float agreement)
        {
            lod = model = 0f;
            agreement = 1f;
            if (joint == null || joint.Length != Bins * Bins)
                return;

            var above = new double[Bins];
            double edge = Math.Max(0.0, Math.Min(Bins, threshold * Bins));
            for (int b = 0; b < Bins; b++)
                above[b] = Math.Max(0.0, Math.Min(1.0, b + 1 - edge));

            double total = 0.0, l = 0.0, m = 0.0, both = 0.0;
            for (int i = 0; i < Bins; i++)
            {
                for (int j = 0; j < Bins; j++)
                {
                    double c = joint[i * Bins + j];
                    if (c == 0.0)
                        continue;
                    total += c;
                    l += c * above[i];
                    m += c * above[j];
                    both += c * above[i] * above[j];
                }
            }
            if (total <= 0.0)
                return;
            lod = (float)(l / total);
            model = (float)(m / total);
            double either = l + m - both;
            agreement = either > 0.0 ? (float)(both / either) : 1f;
        }

        /// <summary>
        /// What one LOD gets. mode: the asset's say; on: the switch; reach: the share of its texels that
        /// met its model; lodShare, modelShare, agreement: <see cref="Shares"/>.
        /// </summary>
        public static int Decide(int mode, bool on, bool transferred, int met, float reach, float lodShare, float modelShare, float agreement)
        {
            if (!transferred)
                return VerdictFailed;
            if (met <= 0)
                return VerdictShapeDiffers;
            if (lodShare < MinShare && modelShare < MinShare)
                return VerdictNoGlass;
            if (mode == ModeOwn)
                return VerdictKeptOwn;
            if (mode == ModeModel)
                return VerdictForced;
            if (agreement >= MinAgreement)
                return VerdictAgrees;
            if (!on)
                return VerdictOff;
            if (!(reach >= MinReach))
                return VerdictShapeDiffers;
            return VerdictBorrowed;
        }

        public static bool Borrows(int verdict)
        {
            return verdict == VerdictBorrowed || verdict == VerdictForced;
        }

        /// <summary>Taken by an LOD with next to none of its own glass (else: it had glass elsewhere).</summary>
        public static bool HadNone(LodGlassEntry e)
        {
            return e.LodShare < 0.1f * e.ModelShare;
        }

        /// <summary>
        /// Every entry's verdict at its own Mode and Threshold (entries not yet compared: not measured).
        /// True when the set of LODs that take their model's glass changed (the mask is then made anew).
        /// </summary>
        public static bool DecideAll(List<LodGlassEntry> entries, bool on)
        {
            bool changed = false;
            for (int i = 0; i < entries.Count; i++)
            {
                LodGlassEntry e = entries[i];
                bool was = Borrows(e.Verdict);
                if (!e.Transferred && e.Problem.Length == 0)
                {
                    e.Verdict = VerdictNotMeasured;
                }
                else
                {
                    float lod, model, agreement;
                    Shares(e.Joint, e.Threshold, out lod, out model, out agreement);
                    e.LodShare = lod;
                    e.ModelShare = model;
                    e.Agreement = agreement;
                    e.Reach = e.OnLod > 0 ? (float)e.Met / e.OnLod : 0f;
                    e.Verdict = Decide(e.Mode, on, e.Transferred, e.Met, e.Reach, lod, model, agreement);
                }
                if (Borrows(e.Verdict) != was)
                    changed = true;
            }
            return changed;
        }

        // ---------------------------------------------------------------------------------------
        // The mask (pure), its upload and binding.
        // ---------------------------------------------------------------------------------------

        /// <summary>The mask's size for an atlas: halved until no side is over <see cref="MaxMaskSize"/>.</summary>
        public static void MaskSize(int atlasW, int atlasH, out int w, out int h)
        {
            w = Math.Max(1, atlasW);
            h = Math.Max(1, atlasH);
            while (w > MaxMaskSize || h > MaxMaskSize)
            {
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }
        }

        /// <summary>A mask byte for a shine 0..255: 1 + 254 x shine (0 is left for "the LOD's own").</summary>
        public static byte Encode(int shine)
        {
            return (byte)(1 + (Math.Max(0, Math.Min(255, shine)) * 254 + 127) / 255);
        }

        /// <summary>The mips a w x h texture has, down to 1 x 1.</summary>
        public static int MipCount(int w, int h)
        {
            int count = 1;
            while (w > 1 || h > 1)
            {
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
                count++;
            }
            return count;
        }

        /// <summary>
        /// The mask, every mip, one byte a texel, laid out as the atlas: each borrowing entry's grid
        /// drawn (bilinearly) over the texels its rectangle covers; 0 elsewhere. A coarser texel
        /// averages its texels that hold a shine when at least two of the four do, else 0: a mip never
        /// blends "the LOD's own" into a shine.
        /// </summary>
        public static byte[] BuildMask(List<LodGlassEntry> borrowing, int w, int h)
        {
            int total = 0;
            int lw = w, lh = h, mips = MipCount(w, h);
            for (int l = 0; l < mips; l++)
            {
                total += lw * lh;
                lw = Math.Max(1, lw / 2);
                lh = Math.Max(1, lh / 2);
            }
            var data = new byte[total];

            for (int i = 0; i < borrowing.Count; i++)
            {
                LodGlassEntry e = borrowing[i];
                if (e.Grid == null || e.SideX < 1 || e.SideY < 1 || !(e.Rect.z > 0f) || !(e.Rect.w > 0f))
                    continue;
                int x0 = Math.Max(0, (int)Math.Floor(e.Rect.x * w)), x1 = Math.Min(w - 1, (int)Math.Ceiling((e.Rect.x + e.Rect.z) * w) - 1);
                int y0 = Math.Max(0, (int)Math.Floor(e.Rect.y * h)), y1 = Math.Min(h - 1, (int)Math.Ceiling((e.Rect.y + e.Rect.w) * h) - 1);
                for (int y = y0; y <= y1; y++)
                {
                    float v = ((y + 0.5f) / h - e.Rect.y) / e.Rect.w;
                    for (int x = x0; x <= x1; x++)
                    {
                        float u = ((x + 0.5f) / w - e.Rect.x) / e.Rect.z;
                        float shine = SampleGrid(e.Grid, e.SideX, e.SideY, u, v);
                        data[y * w + x] = Encode((int)Math.Round(shine));
                    }
                }
            }

            // The mips.
            int src = 0, sw = w, sh = h;
            for (int l = 1; l < mips; l++)
            {
                int dw = Math.Max(1, sw / 2), dh = Math.Max(1, sh / 2);
                int dst = src + sw * sh;
                for (int y = 0; y < dh; y++)
                {
                    int ya = Math.Min(sh - 1, 2 * y), yb = Math.Min(sh - 1, 2 * y + 1);
                    for (int x = 0; x < dw; x++)
                    {
                        int xa = Math.Min(sw - 1, 2 * x), xb = Math.Min(sw - 1, 2 * x + 1);
                        int a = data[src + ya * sw + xa], b = data[src + ya * sw + xb], c = data[src + yb * sw + xa], d = data[src + yb * sw + xb];
                        int k = (a > 0 ? 1 : 0) + (b > 0 ? 1 : 0) + (c > 0 ? 1 : 0) + (d > 0 ? 1 : 0);
                        data[dst + y * dw + x] = k >= 2 ? (byte)((a + b + c + d + k / 2) / k) : (byte)0;
                    }
                }
                src = dst;
                sw = dw;
                sh = dh;
            }
            return data;
        }

        /// <summary>A grid (bytes) read bilinearly at (u, v), clamped: 0..255.</summary>
        public static float SampleGrid(byte[] grid, int sx, int sy, float u, float v)
        {
            float x = Math.Max(0f, Math.Min(sx - 1f, u * sx - 0.5f));
            float y = Math.Max(0f, Math.Min(sy - 1f, v * sy - 0.5f));
            int xi = Math.Min(Math.Max(0, sx - 2), (int)x), yi = Math.Min(Math.Max(0, sy - 2), (int)y);
            int xj = Math.Min(sx - 1, xi + 1), yj = Math.Min(sy - 1, yi + 1);
            float fx = x - xi, fy = y - yi;
            float a = grid[yi * sx + xi] + (grid[yi * sx + xj] - grid[yi * sx + xi]) * fx;
            float b = grid[yj * sx + xi] + (grid[yj * sx + xj] - grid[yj * sx + xi]) * fx;
            return a + (b - a) * fy;
        }

        /// <summary>
        /// The mask made from <see cref="BuildMask"/>'s bytes (w x h with every mip): one channel, read
        /// as alpha. Null data: no mask (nothing borrows). False, with <see cref="Problem"/>, when it
        /// could not be made.
        /// </summary>
        public bool Upload(byte[] data, int w, int h)
        {
            Problem = "";
            FreeMask();
            if (data == null)
                return true;

            try
            {
                bool alpha8 = SystemInfo.SupportsTextureFormat(TextureFormat.Alpha8);
                var mask = new Texture2D(w, h, alpha8 ? TextureFormat.Alpha8 : TextureFormat.RGBA32, true, true);
                mask.name = "RealGlass LOD glass";
                mask.hideFlags = HideFlags.HideAndDontSave;
                mask.filterMode = FilterMode.Trilinear;
                mask.wrapMode = TextureWrapMode.Clamp;
                if (alpha8)
                {
                    mask.LoadRawTextureData(data);
                }
                else
                {
                    // Four bytes a texel, the value in every channel.
                    var wide = new byte[data.Length * 4];
                    for (int i = 0; i < data.Length; i++)
                        wide[4 * i] = wide[4 * i + 1] = wide[4 * i + 2] = wide[4 * i + 3] = data[i];
                    mask.LoadRawTextureData(wide);
                }
                mask.Apply(false, true);
                _mask = mask;
                MaskWidth = w;
                MaskHeight = h;
                return true;
            }
            catch (Exception e)
            {
                FreeMask();
                Problem = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>The mask for the shader (_RGLodGlass): a texel of 0 everywhere while no LOD takes its model's glass.</summary>
        public void Bind()
        {
            Ids();
            if (_none == null)
            {
                _none = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                _none.name = "RealGlass LOD glass (none)";
                _none.hideFlags = HideFlags.HideAndDontSave;
                _none.SetPixels32(new[] { new Color32(0, 0, 0, 0) });
                _none.Apply(false, true);
            }
            Shader.SetGlobalTexture(_idMask, _mask != null ? (Texture)_mask : _none);
        }

        private void FreeMask()
        {
            if (_mask != null)
                UnityEngine.Object.DestroyImmediate(_mask);
            _mask = null;
            MaskWidth = MaskHeight = 0;
        }

        /// <summary>The read-back's target and picture, freed once every LOD has been read back.</summary>
        public void FreeReadBack()
        {
            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.DestroyImmediate(_target);
            }
            _target = null;
            if (_read != null)
                UnityEngine.Object.DestroyImmediate(_read);
            _read = null;
        }

        public void Destroy()
        {
            FreeReadBack();
            FreeMask();
            if (_quad != null)
                UnityEngine.Object.DestroyImmediate(_quad);
            _quad = null;
            if (_ids)
                Shader.SetGlobalTexture(_idMask, Texture2D.blackTexture);
            if (_none != null)
                UnityEngine.Object.DestroyImmediate(_none);
            _none = null;
        }
    }
}
