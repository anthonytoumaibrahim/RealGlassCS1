using ColossalFramework;
using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// What each building's lamps are, where the glass shader can read it at every distance: a
    /// texture laid out like the game's object colour map (RenderManager.OBJECT_COLORMAP_WIDTH x
    /// HEIGHT, texel = building id), found through the building's colour index, which every tier of
    /// the game's drawing carries -- near: _ObjectIndex, middle: _ObjectIndices, far: the baked
    /// second uv (BuildingAI.PopulateGroupData) -- and which the game points at the PARENT of a
    /// sub-building (BuildingAI.GetObjectColorIndex), so a building complex is one building.
    /// r: the lamps' warmth, 0 cool (offices, about 4500 K) .. 1 warm (homes, about 2900 K);
    /// a: 255 where a building stands.
    /// </summary>
    public sealed class BuildingLights
    {
        /// <summary>Buildings looked at a frame: all of them in about a second.</summary>
        private const int PerFrame = 1024;

        /// <summary>Frames between two uploads while anything changed.</summary>
        private const int UploadEvery = 30;

        public const string TextureName = "_RGBuildings";

        private Texture2D _texture;
        private Color32[] _pixels;
        private int _width, _height;
        private int _next;
        private bool _dirty;
        private int _sinceUpload;

        public int Width
        {
            get { return _width; }
        }

        public int Height
        {
            get { return _height; }
        }

        /// <summary>Buildings standing at the last full pass, for the log.</summary>
        public int Standing { get; private set; }

        private int _counting;

        public bool IsReady
        {
            get { return _texture != null; }
        }

        /// <summary>Makes the texture and reads every building at once (a city has just loaded).</summary>
        public void Start()
        {
            if (_texture != null)
                return;

            _width = RenderManager.OBJECT_COLORMAP_WIDTH;
            _height = RenderManager.OBJECT_COLORMAP_HEIGHT;
            _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false, true);
            _texture.name = "RealGlass building lights";
            _texture.filterMode = FilterMode.Point;
            _texture.wrapMode = TextureWrapMode.Clamp;
            _texture.hideFlags = HideFlags.HideAndDontSave;
            _pixels = new Color32[_width * _height];

            int count = Singleton<BuildingManager>.instance.m_buildings.m_buffer.Length;
            _counting = 0;
            Scan(0, count);
            Standing = _counting;
            _next = 0;
            Upload();
            Log.Msg("glass: building lights " + _width + "x" + _height + ", " + Standing + " buildings");
        }

        /// <summary>A slice of the city a frame; the texture follows within about a second.</summary>
        public void Update()
        {
            if (_texture == null)
                return;

            int count = Singleton<BuildingManager>.instance.m_buildings.m_buffer.Length;
            if (_next == 0)
                _counting = 0;

            Scan(_next, PerFrame);
            _next += PerFrame;
            if (_next >= count)
            {
                _next = 0;
                Standing = _counting;
            }

            _sinceUpload++;
            if (_dirty && _sinceUpload >= UploadEvery)
                Upload();

            Shader.SetGlobalTexture(TextureName, _texture);
        }

        private void Scan(int from, int count)
        {
            Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            int end = Mathf.Min(Mathf.Min(from + count, buffer.Length), _pixels.Length);
            for (int id = from; id < end; id++)
            {
                // The texel of building id is (id % width, id / width): index id in the array.
                Color32 c = LampsOf(ref buffer[id]);
                if (c.a != 0)
                    _counting++;

                Color32 old = _pixels[id];
                if (old.r != c.r || old.a != c.a)
                {
                    _pixels[id] = c;
                    _dirty = true;
                }
            }
        }

        /// <summary>
        /// By what the building is: homes warm (2700-3000 K), shops and restaurants between,
        /// offices and industry cool (4000-5000 K) -- the usual lamps of each.
        /// </summary>
        private static Color32 LampsOf(ref Building building)
        {
            if ((building.m_flags & Building.Flags.Created) == 0)
                return new Color32(0, 0, 0, 0);

            BuildingInfo info = building.Info;
            if (info == null || info.m_class == null)
                return new Color32(0, 0, 0, 0);

            float warmth;
            switch (info.m_class.m_service)
            {
                case ItemClass.Service.Residential:
                    warmth = 0.9f;
                    break;
                case ItemClass.Service.Commercial:
                    warmth = 0.65f;
                    break;
                case ItemClass.Service.Office:
                    warmth = 0.2f;
                    break;
                case ItemClass.Service.Industrial:
                case ItemClass.Service.PlayerIndustry:
                    warmth = 0.1f;
                    break;
                default:
                    warmth = 0.45f;
                    break;
            }

            return new Color32((byte)Mathf.RoundToInt(warmth * 255f), 0, 0, 255);
        }

        private void Upload()
        {
            _texture.SetPixels32(_pixels);
            _texture.Apply(false, false);
            Shader.SetGlobalTexture(TextureName, _texture);
            _dirty = false;
            _sinceUpload = 0;
        }

        public void Destroy()
        {
            if (_texture != null)
            {
                Object.Destroy(_texture);
                _texture = null;
            }

            _pixels = null;
        }
    }
}
