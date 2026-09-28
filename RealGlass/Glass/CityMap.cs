using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// The city from above, as roof heights: the ground round the view in Size x Size cells of
    /// Texel metres, each holding the highest roof over it and whose it is. The glass shader walks
    /// it along a reflected ray, cell by cell (Amanatides and Woo, "A Fast Voxel Traversal Algorithm
    /// for Ray Tracing", Eurographics 1987), wherever the screen has nothing for that ray: the lower
    /// floors of a street mirror the towers across it instead of open sky. It is the kind of map
    /// CRYENGINE's height-map ambient occlusion draws of the world round the camera, and Unreal's
    /// heightfield sky occlusion reads for reflections. Filled on the CPU a building at a time (the
    /// game's own per-prefab height grids; the preview's boxes), then uploaded whole, with a second
    /// map Coarse times coarser holding each block's highest roof, for the far part of a ray.
    /// Engine types only: the GPU preview runs this same file (tools/preview/sync-sim.ps1).
    /// </summary>
    /// <remarks>
    /// A cell is RGBA32, linear, point-sampled: r and g the roof's world height in steps of
    /// <see cref="HeightStep"/> (16 bits: 0 .. 2048 m), b and a the building's id (16 bits, 0 for
    /// none) -- the id its colour index names, so a sub-building is its parent. Texture2D RGBA32,
    /// SetPixels32 and Apply are the calls BuildingLights already makes in the game.
    /// </remarks>
    public sealed class CityMap
    {
        /// <summary>Cells a side: 512 x 4 m, two kilometres round the view.</summary>
        public const int Size = 512;

        /// <summary>Metres a cell.</summary>
        public const float Texel = 4f;

        /// <summary>A coarse cell is Coarse x Coarse cells (32 m): the highest roof in each.</summary>
        public const int Coarse = 8;

        /// <summary>Metres per step of the 16-bit height.</summary>
        public const float HeightStep = 1f / 32f;

        /// <summary>The game's height grid of a building (BuildingInfoGen.m_heights): 17 x 17 points over its mesh's bounds.</summary>
        public const int GridPoints = 17;

        private Texture2D _fine, _coarse;
        private readonly Color32[] _cells = new Color32[Size * Size];
        private readonly Color32[] _blocks = new Color32[(Size / Coarse) * (Size / Coarse)];
        private float _buildX, _buildZ;
        private float _liveX, _liveZ;
        private float _buildTop, _liveTop;
        private int _buildCount;
        private bool _live;

        private static int _mapId, _coarseId, _stId, _infoId;
        private static bool _ids;

        /// <summary>A map is on the GPU.</summary>
        public bool Live
        {
            get { return _live; }
        }

        /// <summary>Buildings drawn into the map on the GPU.</summary>
        public int LiveCount { get; private set; }

        /// <summary>The highest roof in the map on the GPU, world y.</summary>
        public float LiveTop
        {
            get { return _liveTop; }
        }

        /// <summary>The centre of the map on the GPU (world x, z).</summary>
        public Vector2 LiveCentre
        {
            get { return new Vector2(_liveX + Size * Texel * 0.5f, _liveZ + Size * Texel * 0.5f); }
        }

        private static void Ids()
        {
            if (_ids)
                return;
            _mapId = Shader.PropertyToID("_RGCityMap");
            _coarseId = Shader.PropertyToID("_RGCityCoarse");
            _stId = Shader.PropertyToID("_RGCityMapST");
            _infoId = Shader.PropertyToID("_RGCityMapInfo");
            _ids = true;
        }

        /// <summary>
        /// Starts a new map round this point (snapped to the coarse grid, so two maps of the same
        /// place are the same map): every cell empty until the buildings are added.
        /// </summary>
        public void Begin(float centreX, float centreZ)
        {
            float snap = Texel * Coarse;
            float half = Size * Texel * 0.5f;
            _buildX = Mathf.Round(centreX / snap) * snap - half;
            _buildZ = Mathf.Round(centreZ / snap) * snap - half;
            System.Array.Clear(_cells, 0, _cells.Length);
            _buildTop = 0f;
            _buildCount = 0;
        }

        /// <summary>Whether a point is within the map being built, with a margin (metres) for a building's size.</summary>
        public bool Near(float x, float z, float margin)
        {
            float extent = Size * Texel;
            return x > _buildX - margin && x < _buildX + extent + margin && z > _buildZ - margin && z < _buildZ + extent + margin;
        }

        /// <summary>
        /// Draws one building into the map being built: its mesh's bounds (min, max, mesh space) at
        /// its mesh position and rotation, with its height grid when it has one (heights[j * 17 + i]:
        /// the highest point of the mesh above x = min.x + i (max.x - min.x) / 16, z likewise, as the
        /// game fills it), else its bounds as a box. Each cell keeps the highest roof over its centre.
        /// </summary>
        public void AddBuilding(Vector3 position, Quaternion rotation, Vector3 min, Vector3 max, float[] heights, int id)
        {
            if (id <= 0 || id > 65535 || max.x <= min.x || max.z <= min.z || max.y < 1f)
                return;

            Vector3 ax = rotation * Vector3.right;
            Vector3 az = rotation * Vector3.forward;

            // The footprint's corners, and the cells round them.
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            for (int c = 0; c < 4; c++)
            {
                float lx = (c & 1) == 0 ? min.x : max.x;
                float lz = (c & 2) == 0 ? min.z : max.z;
                float wx = position.x + ax.x * lx + az.x * lz;
                float wz = position.z + ax.z * lx + az.z * lz;
                x0 = Mathf.Min(x0, wx);
                x1 = Mathf.Max(x1, wx);
                z0 = Mathf.Min(z0, wz);
                z1 = Mathf.Max(z1, wz);
            }

            int i0 = Mathf.Max(0, Mathf.FloorToInt((x0 - _buildX) / Texel));
            int i1 = Mathf.Min(Size - 1, Mathf.FloorToInt((x1 - _buildX) / Texel));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((z0 - _buildZ) / Texel));
            int j1 = Mathf.Min(Size - 1, Mathf.FloorToInt((z1 - _buildZ) / Texel));
            if (i0 > i1 || j0 > j1)
                return;

            bool grid = heights != null && heights.Length >= GridPoints * GridPoints;
            float gx = (GridPoints - 1) / (max.x - min.x);
            float gz = (GridPoints - 1) / (max.z - min.z);
            byte idLow = (byte)(id & 255), idHigh = (byte)(id >> 8);
            bool drawn = false;

            for (int j = j0; j <= j1; j++)
            {
                float dz = _buildZ + (j + 0.5f) * Texel - position.z;
                for (int i = i0; i <= i1; i++)
                {
                    float dx = _buildX + (i + 0.5f) * Texel - position.x;
                    float lx = dx * ax.x + dz * ax.z;
                    float lz = dx * az.x + dz * az.z;
                    if (lx < min.x || lx > max.x || lz < min.z || lz > max.z)
                        continue;

                    float h;
                    if (grid)
                    {
                        // The highest of the four grid points round the cell's centre: a roof is
                        // never missed between them.
                        int a = Mathf.Clamp((int)((lx - min.x) * gx), 0, GridPoints - 1);
                        int b = Mathf.Clamp((int)((lz - min.z) * gz), 0, GridPoints - 1);
                        int a1 = Mathf.Min(a + 1, GridPoints - 1), b1 = Mathf.Min(b + 1, GridPoints - 1);
                        h = Mathf.Max(Mathf.Max(heights[b * GridPoints + a], heights[b * GridPoints + a1]),
                                      Mathf.Max(heights[b1 * GridPoints + a], heights[b1 * GridPoints + a1]));
                    }
                    else
                    {
                        h = max.y;
                    }

                    if (h < 1f)
                        continue;   // an open corner of an L-shaped block: nothing stands here

                    float top = position.y + h;
                    int code = Mathf.Clamp(Mathf.RoundToInt(top / HeightStep), 1, 65535);
                    int k = j * Size + i;
                    Color32 old = _cells[k];
                    if (code <= ((old.r << 8) | old.g))
                        continue;

                    _cells[k] = new Color32((byte)(code >> 8), (byte)(code & 255), idLow, idHigh);
                    if (top > _buildTop)
                        _buildTop = top;
                    drawn = true;
                }
            }

            if (drawn)
                _buildCount++;
        }

        /// <summary>Each block's highest roof into the coarse map; both maps to the GPU; the new map live.</summary>
        public void Finish()
        {
            int n = Size / Coarse;
            for (int bj = 0; bj < n; bj++)
            {
                for (int bi = 0; bi < n; bi++)
                {
                    Color32 best = new Color32(0, 0, 0, 0);
                    int bestCode = 0;
                    for (int j = bj * Coarse; j < (bj + 1) * Coarse; j++)
                    {
                        int row = j * Size;
                        for (int i = bi * Coarse; i < (bi + 1) * Coarse; i++)
                        {
                            Color32 c = _cells[row + i];
                            int code = (c.r << 8) | c.g;
                            if (code > bestCode)
                            {
                                bestCode = code;
                                best = c;
                            }
                        }
                    }

                    _blocks[bj * n + bi] = best;
                }
            }

            if (_fine == null)
            {
                _fine = NewTexture(Size, "RealGlass city heights");
                _coarse = NewTexture(n, "RealGlass city heights, coarse");
            }

            _fine.SetPixels32(_cells);
            _fine.Apply(false, false);
            _coarse.SetPixels32(_blocks);
            _coarse.Apply(false, false);
            _liveX = _buildX;
            _liveZ = _buildZ;
            _liveTop = _buildTop;
            LiveCount = _buildCount;
            _live = true;
        }

        private static Texture2D NewTexture(int size, string name)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            t.name = name;
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        /// <summary>The map to the shader (_RGCityMapST.w = 0 turns the walk off).</summary>
        public void Bind(bool on)
        {
            Ids();
            bool use = on && _live && _fine != null;
            if (_fine != null)
            {
                Shader.SetGlobalTexture(_mapId, _fine);
                Shader.SetGlobalTexture(_coarseId, _coarse);
            }

            Shader.SetGlobalVector(_stId, new Vector4(_liveX, _liveZ, Texel, use ? 1f : 0f));
            Shader.SetGlobalVector(_infoId, new Vector4(Size, Coarse, _liveTop, 0f));
        }

        public void Destroy()
        {
            if (_fine != null)
                Object.DestroyImmediate(_fine);
            if (_coarse != null)
                Object.DestroyImmediate(_coarse);
            _fine = null;
            _coarse = null;
            _live = false;
        }
    }
}
