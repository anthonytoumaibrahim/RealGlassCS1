using System.Collections.Generic;
using ColossalFramework;
using RealGlass.PerAsset;
using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// Puts each building asset's own glass (RealGlassAssets.xml, <see cref="AssetFile"/>) on every
    /// copy of it in the city: the building's texel of <see cref="AssetTextures"/>, at the id its
    /// colour index names -- a building complex is its main building, as the game's colour index
    /// has it (BuildingAI.GetObjectColorIndex), so a complex follows its main building's asset.
    /// </summary>
    /// <remarks>
    /// After any change of an asset's glass (or of a global setting an asset's glass falls back on)
    /// a pass over the whole building buffer, a slice a frame; then a slow watch for buildings that
    /// went up or levelled up into another asset. Nothing at all while no asset has glass of its
    /// own. The asset being edited in the panel is put on its buildings at once
    /// (<see cref="RefreshAsset"/>).
    /// </remarks>
    internal sealed class AssetMap
    {
        /// <summary>Building slots looked at a frame during a pass: the whole buffer (49,152) in a dozen frames.</summary>
        private const int PassPerFrame = 4096;

        /// <summary>Afterwards: a new or levelled-up building takes its asset's glass within a couple of seconds.</summary>
        private const int WatchPerFrame = 512;

        /// <summary>Frames between two uploads while a pass runs.</summary>
        private const int UploadEvery = 10;

        private static readonly Color32 AllGlobal = new Color32(AssetTextures.Global, AssetTextures.Global, AssetTextures.Global, AssetTextures.Global);

        private readonly AssetTextures _textures = new AssetTextures();
        private readonly Dictionary<BuildingInfo, Color32> _texelOf = new Dictionary<BuildingInfo, Color32>();
        private readonly Dictionary<ulong, int> _slotOf = new Dictionary<ulong, int>();
        private int _seenVersion = -1;
        private ulong _seenGlobal = ulong.MaxValue;
        private int _next = 1;
        private int _passLeft;
        private int _sinceUpload;
        private bool _warnedSlots;

        /// <summary>For the log: the assets with glass of their own that are loaded, and the glasses they make.</summary>
        public int LoadedAssets { get; private set; }

        public int GlassCount
        {
            get { return _slotOf.Count; }
        }

        /// <summary>Every frame while the glass is on: keeps the textures current and bound.</summary>
        public void Update(GlassParams global)
        {
            if (!_textures.IsCreated)
                _textures.Create(RenderManager.OBJECT_COLORMAP_WIDTH, RenderManager.OBJECT_COLORMAP_HEIGHT);

            ulong globalKey = GlassKey(GlassUniforms.Tint(global.TintR, global.TintG, global.TintB), global.OwnColour,
                                       global.Reflectivity, global.Clarity, global.Threshold, global.NightWindows);
            if (AssetFile.Version != _seenVersion || globalKey != _seenGlobal)
            {
                bool first = _seenVersion < 0;
                _seenVersion = AssetFile.Version;
                _seenGlobal = globalKey;
                Resolve(global);
                if (AssetFile.Count > 0 || !first)
                    _passLeft = Singleton<BuildingManager>.instance.m_buildings.m_buffer.Length;
                if (first && AssetFile.Count > 0)
                {
                    Log.Msg("assets: " + LoadedAssets + " of the " + AssetFile.Count + " building assets with glass of their own are loaded" +
                            " (" + _slotOf.Count + " glasses of their own)");
                }
            }

            int budget = _passLeft > 0 ? PassPerFrame : (_texelOf.Count > 0 ? WatchPerFrame : 0);
            if (budget > 0)
                Scan(budget);

            if (_textures.Pending && (_passLeft <= 0 || ++_sinceUpload >= UploadEvery))
            {
                _textures.Upload();
                _sinceUpload = 0;
            }

            _textures.Bind();
        }

        /// <summary>
        /// Puts this asset's glass on every copy of it now, in one go: the panel is changing it and
        /// the player is looking at it.
        /// </summary>
        public void RefreshAsset(BuildingInfo info, GlassParams global)
        {
            if (info == null || !_textures.IsCreated)
                return;

            if (AssetFile.Version != _seenVersion)
            {
                _seenVersion = AssetFile.Version;
                Resolve(global);
                _passLeft = Singleton<BuildingManager>.instance.m_buildings.m_buffer.Length;
            }

            Color32 texel;
            if (!_texelOf.TryGetValue(info, out texel))
                texel = AllGlobal;

            Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            for (int i = 1; i < buffer.Length; i++)
            {
                if ((buffer[i].m_flags & (Building.Flags.Created | Building.Flags.Deleted)) == Building.Flags.Created &&
                    buffer[i].m_parentBuilding == 0 && buffer[i].Info == info)
                {
                    _textures.Set(i, texel);
                }
            }

            _textures.Upload();
            _sinceUpload = 0;
        }

        private void Scan(int budget)
        {
            Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            int n = buffer.Length;
            for (int k = 0; k < budget; k++)
            {
                int i = _next;
                _next = _next + 1 < n ? _next + 1 : 1;

                // Only main buildings: a sub-building's draws read its main building's texel.
                Color32 texel = AllGlobal;
                if ((buffer[i].m_flags & (Building.Flags.Created | Building.Flags.Deleted)) == Building.Flags.Created &&
                    buffer[i].m_parentBuilding == 0)
                {
                    BuildingInfo info = buffer[i].Info;
                    if (info == null || !_texelOf.TryGetValue(info, out texel))
                        texel = AllGlobal;
                }

                _textures.Set(i, texel);
            }

            if (_passLeft > 0)
                _passLeft -= budget;
        }

        /// <summary>Each asset's texel, from its own settings and, where it has none, the global ones.</summary>
        private void Resolve(GlassParams global)
        {
            _texelOf.Clear();
            _slotOf.Clear();
            LoadedAssets = 0;
            int slots = 0;

            foreach (AssetRecord record in AssetFile.All)
            {
                BuildingInfo info = PrefabCollection<BuildingInfo>.FindLoaded(record.Name);
                if (info == null)
                    continue;
                LoadedAssets++;

                byte slot = AssetTextures.Global;
                if (record.HasGlass)
                {
                    Vector3 tint;
                    float own, reflectivity, clarity, threshold;
                    bool nightWindows;
                    Glass(record, global, out tint, out own, out reflectivity, out clarity, out threshold, out nightWindows);
                    ulong key = GlassKey(tint, own, reflectivity, clarity, threshold, nightWindows);
                    int found;
                    if (!_slotOf.TryGetValue(key, out found))
                    {
                        if (slots < AssetTextures.GlassSlots)
                        {
                            found = slots++;
                            _slotOf[key] = found;
                            _textures.SetGlass(found, tint, own, reflectivity, clarity, threshold, nightWindows);
                        }
                        else
                        {
                            found = AssetTextures.Global;
                            if (!_warnedSlots)
                            {
                                _warnedSlots = true;
                                Log.Warn("assets: more than " + AssetTextures.GlassSlots + " different glasses of their own; the rest show the global glass");
                            }
                        }
                    }
                    slot = (byte)found;
                }

                _texelOf[info] = new Color32(
                    record.Strength.HasValue ? AssetTextures.Encode(record.Strength.Value) : AssetTextures.Global,
                    record.PaintedDetail.HasValue ? AssetTextures.Encode(record.PaintedDetail.Value) : AssetTextures.Global,
                    slot, AssetTextures.Global);
            }
        }

        /// <summary>
        /// The asset's glass in full: each number its own, else its kind of glass's (when it chose
        /// one), else the global one; what counts as its glass, its own else the global.
        /// </summary>
        public static void Glass(AssetRecord record, GlassParams global, out Vector3 tint, out float own, out float reflectivity,
                                 out float clarity, out float threshold, out bool nightWindows)
        {
            threshold = record.Threshold ?? global.Threshold;
            nightWindows = record.NightWindows ?? global.NightWindows;

            GlassPreset preset = default(GlassPreset);
            bool hasPreset = record.Preset.HasValue && GlassPresets.TryGet(record.Preset.Value, out preset);

            float r, g, b;
            if (record.Colour.HasValue)
            {
                r = record.Colour.Value.r / 255f;
                g = record.Colour.Value.g / 255f;
                b = record.Colour.Value.b / 255f;
            }
            else if (hasPreset)
            {
                r = preset.Colour.r / 255f;
                g = preset.Colour.g / 255f;
                b = preset.Colour.b / 255f;
            }
            else
            {
                r = global.TintR;
                g = global.TintG;
                b = global.TintB;
            }

            tint = GlassUniforms.Tint(r, g, b);
            own = record.OwnColour ?? global.OwnColour;
            reflectivity = record.Reflectivity ?? (hasPreset ? preset.Reflectivity : global.Reflectivity);
            clarity = record.Clarity ?? (hasPreset ? preset.Clarity : global.Clarity);
        }

        /// <summary>A glass as the texture holds it (8 bits a number, all eight in 64): equal keys, one slot.</summary>
        private static ulong GlassKey(Vector3 tint, float own, float reflectivity, float clarity, float threshold, bool nightWindows)
        {
            return Byte(tint.x) | Byte(tint.y) << 8 | Byte(tint.z) << 16 | Byte(own) << 24 |
                   Byte(reflectivity) << 32 | Byte(clarity) << 40 | Byte(threshold) << 48 | (nightWindows ? 255UL : 0UL) << 56;
        }

        private static ulong Byte(float value)
        {
            return (ulong)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255f), 0, 255);
        }

        /// <summary>The copies of an asset standing in the city (main buildings only).</summary>
        public static int CountCopies(BuildingInfo info)
        {
            if (info == null || !Singleton<BuildingManager>.exists)
                return 0;

            int count = 0;
            Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            for (int i = 1; i < buffer.Length; i++)
            {
                if ((buffer[i].m_flags & (Building.Flags.Created | Building.Flags.Deleted)) == Building.Flags.Created &&
                    buffer[i].m_parentBuilding == 0 && buffer[i].Info == info)
                {
                    count++;
                }
            }

            return count;
        }

        public void Destroy()
        {
            _textures.Destroy();
            _texelOf.Clear();
            _slotOf.Clear();
            _seenVersion = -1;
        }
    }
}
