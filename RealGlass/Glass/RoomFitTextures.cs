using System;
using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// What the shader needs of <see cref="RoomFit"/> (Interiors.cginc, RoomFrameAt), in two float
    /// textures. _RGRoomPlaces: laid out as the game's object colour map, two texels a building side
    /// by side -- its model's origin in the world and its fit's row (0: none), then its model's x and
    /// z axes (world x and z of each): the same on every tier, so a room is where it was when the
    /// building changes model. _RGRoomFits: a row per measured asset -- the storey (0: none found),
    /// the ground storey's top, 1 when its glass holds no rooms, where its floors are; then each
    /// side's room width, where its walls stand, 1 when it is too narrow, 1 when it was found. A
    /// texel of zeros is always "as before the fit". Engine types only, C# 4: the GPU preview runs
    /// this same file.
    /// </summary>
    /// <remarks>
    /// Uploaded raw (LoadRawTextureData, four floats a texel): the CPU keeps its copy, so a building
    /// put up or moved is one texel changed and the texture sent again (at most every so often: the
    /// places are 4 MB in the game's 512 x 256 colour map).
    /// </remarks>
    public sealed class RoomFitTextures
    {
        /// <summary>A fit's texels: the asset's own, then one a side.</summary>
        public const int FitWidth = 1 + RoomFit.Sides;

        /// <summary>Rows of fits: assets measured in a city, at most (row 0 is none).</summary>
        public const int FitRows = 4096;

        private Texture2D _places, _fits;
        private float[] _placeData, _fitData;
        private byte[] _placeBytes, _fitBytes;
        private int _w, _h;
        private bool _ids;
        private int _idPlaces, _idFits, _idInfo;

        /// <summary>Changed since the last upload.</summary>
        public bool PlacesPending { get; private set; }
        public bool FitsPending { get; private set; }

        public bool IsCreated
        {
            get { return _places != null; }
        }

        /// <summary>Why it could not be made; "" when it was.</summary>
        public string Problem { get; private set; }

        public RoomFitTextures()
        {
            Problem = "";
        }

        /// <summary>Float textures can be read here (the rooms are then laid out as before the fit).</summary>
        public static bool Supported
        {
            get { return SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat); }
        }

        /// <summary>Both textures, every texel 0, for a colour map of w x h. False, with Problem, when they could not be made.</summary>
        public bool Create(int w, int h)
        {
            Destroy();
            Problem = "";
            if (!Supported)
            {
                Problem = "no float textures here";
                return false;
            }
            try
            {
                _w = w;
                _h = h;
                _placeData = new float[2 * w * h * 4];
                _fitData = new float[FitWidth * FitRows * 4];
                _placeBytes = new byte[_placeData.Length * 4];
                _fitBytes = new byte[_fitData.Length * 4];
                _places = Make(2 * w, h, "RealGlass room places");
                _fits = Make(FitWidth, FitRows, "RealGlass room fits");
                PlacesPending = FitsPending = true;
                Upload();
                return Problem.Length == 0;
            }
            catch (Exception e)
            {
                Problem = e.GetType().Name + ": " + e.Message;
                Destroy();
                return false;
            }
        }

        private static Texture2D Make(int w, int h, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
            t.name = name;
            t.hideFlags = HideFlags.HideAndDontSave;
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>
        /// Building (or colour-map texel) index: its model's origin and axes and its fit's row (0:
        /// none). False when nothing changed.
        /// </summary>
        public bool SetPlace(int index, Vector3 origin, Vector3 xAxis, Vector3 zAxis, int row)
        {
            if (_placeData == null || index < 0 || index >= _w * _h)
                return false;
            int x = index % _w, y = index / _w;
            int i = 4 * (y * 2 * _w + 2 * x);
            bool changed = Put(_placeData, i, origin.x, origin.y, origin.z, row);
            changed |= Put(_placeData, i + 4, xAxis.x, xAxis.z, zAxis.x, zAxis.z);
            if (changed)
                PlacesPending = true;
            return changed;
        }

        /// <summary>No building there (or one not placed yet): the rooms as before the fit.</summary>
        public bool ClearPlace(int index)
        {
            return SetPlace(index, Vector3.zero, Vector3.zero, Vector3.zero, 0);
        }

        /// <summary>Row (1..FitRows-1): one asset's fit. null clears it.</summary>
        public void SetFit(int row, RoomFitResult r)
        {
            if (_fitData == null || row <= 0 || row >= FitRows)
                return;
            int i = 4 * row * FitWidth;
            if (r == null || !r.Measured)
            {
                for (int k = 0; k < 4 * FitWidth; k++)
                    _fitData[i + k] = 0f;
            }
            else
            {
                Put(_fitData, i, r.Storey, r.GroundTop, r.Rooms ? 0f : 1f, r.FloorPhase);
                for (int k = 0; k < RoomFit.Sides; k++)
                {
                    RoomFitSide s = r.Sides[k];
                    Put(_fitData, i + 4 * (1 + k), s.Found ? s.Width : 0f, s.Found ? s.Phase : 0f, s.Narrow ? 1f : 0f, s.Found ? 1f : 0f);
                }
            }
            FitsPending = true;
        }

        private static bool Put(float[] data, int i, float a, float b, float c, float d)
        {
            if (data[i] == a && data[i + 1] == b && data[i + 2] == c && data[i + 3] == d)
                return false;
            data[i] = a;
            data[i + 1] = b;
            data[i + 2] = c;
            data[i + 3] = d;
            return true;
        }

        /// <summary>What changed sent to the GPU. False, with Problem, when it could not be.</summary>
        public bool Upload()
        {
            if (_places == null)
                return false;
            try
            {
                if (PlacesPending)
                {
                    Buffer.BlockCopy(_placeData, 0, _placeBytes, 0, _placeBytes.Length);
                    _places.LoadRawTextureData(_placeBytes);
                    _places.Apply(false, false);
                    PlacesPending = false;
                }
                if (FitsPending)
                {
                    Buffer.BlockCopy(_fitData, 0, _fitBytes, 0, _fitBytes.Length);
                    _fits.LoadRawTextureData(_fitBytes);
                    _fits.Apply(false, false);
                    FitsPending = false;
                }
                return true;
            }
            catch (Exception e)
            {
                Problem = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        private void Ids()
        {
            if (_ids)
                return;
            _idPlaces = Shader.PropertyToID("_RGRoomPlaces");
            _idFits = Shader.PropertyToID("_RGRoomFits");
            _idInfo = Shader.PropertyToID("_RGRoomFitInfo");
            _ids = true;
        }

        /// <summary>For the shader: both textures and _RGRoomFitInfo (x 1 while bound, y the rows, z the fit's width).</summary>
        public void Bind()
        {
            Ids();
            if (_places == null)
            {
                Unbind();
                return;
            }
            Shader.SetGlobalTexture(_idPlaces, _places);
            Shader.SetGlobalTexture(_idFits, _fits);
            Shader.SetGlobalVector(_idInfo, new Vector4(1f, FitRows, FitWidth, 0f));
        }

        /// <summary>The rooms laid out as before the fit (the shader reads nothing of it).</summary>
        public void Unbind()
        {
            Ids();
            Shader.SetGlobalVector(_idInfo, Vector4.zero);
        }

        public void Destroy()
        {
            if (_ids)
                Unbind();
            if (_places != null)
                UnityEngine.Object.DestroyImmediate(_places);
            if (_fits != null)
                UnityEngine.Object.DestroyImmediate(_fits);
            _places = _fits = null;
            _placeData = _fitData = null;
            _placeBytes = _fitBytes = null;
            PlacesPending = FitsPending = false;
        }
    }
}
