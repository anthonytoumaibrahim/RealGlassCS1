using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// The glass on Procedural Objects' buildings (Workshop 1094334744). A building converted there
    /// keeps a COPY of its asset's material (Object.Instantiate(info.m_material): the specular map, our
    /// glass mask, with it) and of its main mesh, and Procedural Objects draws it itself every frame
    /// (ProceduralObjectsLogic.Update: Graphics.DrawMesh on layer 0, no property block, the one mesh
    /// at every distance) -- by none of the game's building paths our patches follow, so it kept the
    /// game's own glass beside ours. Each is drawn again here, with the glass, where and when
    /// Procedural Objects draws it (<see cref="ProceduralAccess"/>): its asset's own glass and rooms,
    /// through a texel of _RGAssets of its own past the buildings' (an id no building has, under
    /// 65536 for the city map); in the city map; and found by the building picker by its shape.
    /// </summary>
    /// <remarks>
    /// Nothing of Procedural Objects is patched or changed: without it nothing runs; not as expected,
    /// one line and its buildings keep the game's glass. Not drawn: a building given a texture of its
    /// own or a text -- the glass mask is the asset's painting, which the new texture is not (most are
    /// converted only to be reshaped: taller, slimmer). Main thread only.
    /// </remarks>
    public static class ProceduralBuildings
    {
        /// <summary>One building of Procedural Objects.</summary>
        public sealed class Entry
        {
            public readonly object Object;

            /// <summary>Its texel of _RGAssets and its id in the city map; -1 when there was no place left.</summary>
            public readonly int Index;

            /// <summary>Its _ObjectIndex: the texel at Index, as the game's colour index names a building's.</summary>
            public readonly Vector4 ColourIndex;

            public BuildingInfo Info;

            /// <summary>Drawn with the glass: the asset's own texture on a building shader the glass follows.</summary>
            public bool Glass;

            /// <summary>Its material's _BuildingState: what the game's shader reads for it, with no block.</summary>
            public Vector4 State;

            internal int Stamp;

            public Entry(object o, int index)
            {
                Object = o;
                Index = index;
                int w = RenderManager.OBJECT_COLORMAP_WIDTH, h = RenderManager.OBJECT_COLORMAP_HEIGHT;
                ColourIndex = index >= 0 ? new Vector4((index % w + 0.5f) / w, (index / w + 0.5f) / h, 1f, 0f) : Vector4.zero;
            }
        }

        /// <summary>Seconds between two reads of the whole list while its length stays the same.</summary>
        private const float ScanEvery = 2f;

        /// <summary>The building shader's own default _BuildingState (resources.assets, 2026-10-21): nothing squashed or stretched.</summary>
        private static readonly Vector4 DefaultState = new Vector4(0f, 1000f, 0f, 1f);

        private static readonly List<Entry> List = new List<Entry>();
        private static readonly Dictionary<object, Entry> ByObject = new Dictionary<object, Entry>(new SameObject());
        private static readonly List<object> Gone = new List<object>();
        private static readonly List<Candidate> Candidates = new List<Candidate>();
        private static readonly Comparison<Candidate> ByEnter = (a, b) => a.Enter.CompareTo(b.Enter);
        private static readonly Dictionary<Mesh, Shape> Shapes = new Dictionary<Mesh, Shape>();

        private static ProceduralAccess _access;
        private static bool _looked;
        private static string _problem;
        private static bool _failed, _pickFailed;
        private static int _frame = -1;
        private static int _seenCount = -1;
        private static float _nextScan;
        private static int _stamp;
        private static int _first, _places;
        private static bool[] _taken;
        private static bool _loggedPresence, _loggedFirst;
        private static string _loggedSummary = "";
        private static int _stateId = -1;

        // The last pick, kept while neither the ray nor the list moved.
        private static Ray _lastRay;
        private static int _lastStamp = -1;
        private static Entry _lastHit;
        private static float _lastDistance;

        /// <summary>The buildings found at the last read of Procedural Objects' list (the same objects stay the same entries).</summary>
        public static List<Entry> Entries
        {
            get { return List; }
        }

        /// <summary>
        /// Keeps the list current: read whole when its length changes (an object made, deleted,
        /// pasted, undone) and every couple of seconds (a texture or a text given). Once a frame at
        /// most; called by the glass each frame and by the building picker while it is on.
        /// </summary>
        public static void Refresh()
        {
            if (_failed || Time.frameCount == _frame)
                return;

            _frame = Time.frameCount;
            try
            {
                if (!Find())
                    return;

                object logic = _access.Logic();
                IList objects = logic != null ? _access.Objects(logic) : null;
                if (objects == null)
                {
                    if (List.Count > 0)
                        Forget();
                    return;
                }

                int count = objects.Count;
                float now = Time.realtimeSinceStartup;
                if (count == _seenCount && now < _nextScan)
                    return;

                _seenCount = count;
                _nextScan = now + ScanEvery;
                Scan(objects);
            }
            catch (Exception e)
            {
                Fail("reading its objects", e);
            }
        }

        /// <summary>Procedural Objects among the loaded assemblies, read the first time (one line either way).</summary>
        private static bool Find()
        {
            if (!_looked)
            {
                _looked = true;
                Type logic = ProceduralAccess.FindLogicType();
                if (logic != null)
                    _access = ProceduralAccess.Create(logic, out _problem);
                else
                    _problem = "not installed";
            }

            if (!_loggedPresence)
            {
                _loggedPresence = true;
                if (_access != null)
                {
                    Log.Msg("procedural objects: " + (_access.Version ?? "a version without a number") +
                            " is here: its buildings get the glass, read from it without changing it");
                }
                else if (_problem == "not installed")
                {
                    Log.Msg("procedural objects: not installed");
                }
                else
                {
                    Log.Warn("procedural objects: installed, but not as this mod knows it (" + _problem +
                             "): its buildings keep the game's glass");
                }
            }

            return _access != null;
        }

        private static void Scan(IList objects)
        {
            Places();
            if (_stateId < 0)
                _stateId = Shader.PropertyToID("_BuildingState");

            _stamp++;
            List.Clear();
            int all = objects.Count, glass = 0, own = 0, otherShader = 0, noAsset = 0, noPlace = 0;
            for (int i = 0; i < all; i++)
            {
                object o = objects[i];
                if (o == null || _access.Kind(o) != ProceduralAccess.BuildingKind)
                    continue;

                Entry e;
                if (!ByObject.TryGetValue(o, out e))
                {
                    e = new Entry(o, TakePlace(_access.Id(o)));
                    ByObject[o] = e;
                }

                e.Stamp = _stamp;
                e.Info = _access.Asset(o);
                Material material = _access.Material(o);
                e.State = material != null && material.HasProperty(_stateId) ? material.GetVector(_stateId) : DefaultState;
                e.Glass = false;
                if (e.Info == null || e.Info.m_material == null || material == null)
                    noAsset++;
                else if (!GlassDraw.IsBuildingShader(material.shader))
                    otherShader++;
                else if (material.mainTexture != e.Info.m_material.mainTexture)
                    own++;
                else if (e.Index < 0)
                    noPlace++;
                else
                {
                    e.Glass = true;
                    glass++;
                }

                List.Add(e);
            }

            Gone.Clear();
            foreach (KeyValuePair<object, Entry> kv in ByObject)
            {
                if (kv.Value.Stamp != _stamp)
                    Gone.Add(kv.Key);
            }
            for (int i = 0; i < Gone.Count; i++)
            {
                FreePlace(ByObject[Gone[i]].Index);
                ByObject.Remove(Gone[i]);
            }
            Gone.Clear();

            string summary = List.Count + " buildings among its " + all + " objects, " + glass + " with the glass" +
                             (own > 0 ? "; " + own + " with a texture or text of their own (no glass)" : "") +
                             (otherShader > 0 ? "; " + otherShader + " on a shader the glass does not follow" : "") +
                             (noAsset > 0 ? "; " + noAsset + " without their asset" : "") +
                             (noPlace > 0 ? "; " + noPlace + " past the " + _places + " places for them (no glass)" : "");
            if (summary == _loggedSummary)
                return;

            _loggedSummary = summary;
            if (!_loggedFirst)
            {
                _loggedFirst = true;
                Log.Msg("procedural objects: " + summary);
            }
            else if (Log.Detailed)
            {
                Log.Detail("procedural objects: " + summary);
            }
        }

        /// <summary>
        /// Every building with the glass that Procedural Objects draws this frame, drawn again with the
        /// glass (GlassDraw.DrawProcedural). After GlassDraw.Active is set for the frame.
        /// </summary>
        public static void Draw()
        {
            if (_failed || List.Count == 0 || !GlassDraw.Active)
                return;

            try
            {
                object options = _access.Options();
                if (options == null)
                    return;

                bool night = Singleton<SimulationManager>.instance.m_isNightTime;
                TerrainManager terrain = Singleton<TerrainManager>.instance;
                for (int i = 0; i < List.Count; i++)
                {
                    Entry e = List[i];
                    if (!e.Glass || !_access.Drawn(e.Object, options, night))
                        continue;

                    Mesh mesh = _access.Mesh(e.Object);
                    if (mesh == null)
                        continue;

                    Vector3 at = _access.Position(e.Object);
                    Matrix4x4 matrix = Matrix4x4.TRS(at, _access.Rotation(e.Object), Vector3.one);
                    GlassDraw.DrawProcedural(mesh, matrix, e.Info.m_material, e.State, e.ColourIndex, terrain.SampleDetailHeight(at));
                }
            }
            catch (Exception ex)
            {
                Fail("drawing its buildings", ex);
            }
        }

        /// <summary>
        /// Every building Procedural Objects draws now, into the city map being built (CityHeights,
        /// before it finishes a map): with its asset's height grid while its mesh keeps the asset's
        /// size, else as its mesh's box. margin: metres round the map a building may stand and still
        /// reach in, besides its own size.
        /// </summary>
        public static void AddToMap(CityMap map, float margin)
        {
            if (_failed || List.Count == 0)
                return;

            try
            {
                object options = _access.Options();
                if (options == null)
                    return;

                bool night = Singleton<SimulationManager>.instance.m_isNightTime;
                for (int i = 0; i < List.Count; i++)
                {
                    Entry e = List[i];
                    if (e.Index < 0 || e.Info == null || !_access.Drawn(e.Object, options, night))
                        continue;

                    Mesh mesh = _access.Mesh(e.Object);
                    if (mesh == null)
                        continue;

                    Vector3 at = _access.Position(e.Object);
                    Bounds bounds = mesh.bounds;
                    Vector3 min = bounds.min, max = bounds.max;
                    float reach = Mathf.Sqrt(Mathf.Max(min.x * min.x, max.x * max.x) + Mathf.Max(min.z * min.z, max.z * max.z));
                    if (!map.Near(at.x, at.z, margin + reach))
                        continue;

                    BuildingInfoGen gen = e.Info.m_generatedInfo;
                    float[] heights = gen != null && Close(min, gen.m_min) && Close(max, gen.m_max) ? gen.m_heights : null;
                    map.AddBuilding(at, _access.Rotation(e.Object), min, max, heights, e.Index);
                }
            }
            catch (Exception ex)
            {
                Fail("putting its buildings in the city map", ex);
            }
        }

        private static bool Close(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.05f && Mathf.Abs(a.y - b.y) < 0.05f && Mathf.Abs(a.z - b.z) < 0.05f;
        }

        /// <summary>
        /// The nearest building with the glass under the ray, by its shape (its box, then its
        /// triangles); null when none within length. distance: along the ray, metres.
        /// </summary>
        public static Entry Raycast(Ray ray, float length, out float distance)
        {
            distance = length;
            if (_failed || _pickFailed || List.Count == 0)
                return null;

            if (_lastStamp == _stamp && ray.origin == _lastRay.origin && ray.direction == _lastRay.direction)
            {
                distance = _lastHit != null ? _lastDistance : length;
                return _lastHit;
            }

            Entry best = null;
            try
            {
                object options = _access.Options();
                if (options == null)
                    return null;

                bool night = Singleton<SimulationManager>.instance.m_isNightTime;
                Candidates.Clear();
                for (int i = 0; i < List.Count; i++)
                {
                    Entry e = List[i];
                    if (!e.Glass || !_access.Drawn(e.Object, options, night))
                        continue;

                    Mesh mesh = _access.Mesh(e.Object);
                    if (mesh == null)
                        continue;

                    // The ray in the mesh's own space (a rotation keeps the metres).
                    Quaternion back = Quaternion.Inverse(_access.Rotation(e.Object));
                    Vector3 o = back * (ray.origin - _access.Position(e.Object));
                    Vector3 d = back * ray.direction;
                    Bounds bounds = mesh.bounds;
                    float enter = ProceduralPick.RayBox(o, d, bounds.min, bounds.max);
                    if (enter >= 0f && enter < length)
                        Candidates.Add(new Candidate { Entry = e, Mesh = mesh, Origin = o, Direction = d, Enter = enter });
                }

                Candidates.Sort(ByEnter);
                for (int i = 0; i < Candidates.Count; i++)
                {
                    Candidate c = Candidates[i];
                    if (c.Enter >= distance)
                        break;  // every box further on starts behind what was already met

                    Shape shape = ShapeOf(c.Mesh);
                    float t = ProceduralPick.RayTriangles(c.Origin, c.Direction, shape.Vertices, shape.Triangles, distance);
                    if (t >= 0f)
                    {
                        distance = t;
                        best = c.Entry;
                    }
                }
                Candidates.Clear();
            }
            catch (Exception ex)
            {
                // The picker goes on with the city's own buildings; the glass is not touched.
                _pickFailed = true;
                Log.Error("procedural objects: picking its buildings failed; the picker finds the game's buildings only", ex);
                return null;
            }

            _lastRay = ray;
            _lastStamp = _stamp;
            _lastHit = best;
            _lastDistance = distance;
            return best;
        }

        /// <summary>The footprint of a building, as the game outlines one (OverlayEffect.DrawQuad), and its height span.</summary>
        public static bool Footprint(Entry e, out Quad3 quad, out float minY, out float maxY)
        {
            quad = default(Quad3);
            minY = maxY = 0f;
            Mesh mesh = e != null && _access != null ? _access.Mesh(e.Object) : null;
            if (mesh == null)
                return false;

            Vector3 at = _access.Position(e.Object);
            Quaternion turn = _access.Rotation(e.Object);
            Bounds b = mesh.bounds;
            quad.a = at + turn * new Vector3(b.min.x, b.min.y, b.min.z);
            quad.b = at + turn * new Vector3(b.min.x, b.min.y, b.max.z);
            quad.c = at + turn * new Vector3(b.max.x, b.min.y, b.max.z);
            quad.d = at + turn * new Vector3(b.max.x, b.min.y, b.min.z);
            minY = at.y + b.min.y;
            maxY = at.y + b.max.y;
            return true;
        }

        /// <summary>Procedural Objects' number for the object (for the log).</summary>
        public static int IdOf(Entry e)
        {
            return e != null && _access != null ? _access.Id(e.Object) : -1;
        }

        /// <summary>Its buildings of this asset that have the glass (the building window's count).</summary>
        public static int CountCopies(BuildingInfo info)
        {
            int count = 0;
            for (int i = 0; i < List.Count; i++)
            {
                if (List[i].Glass && List[i].Info == info)
                    count++;
            }
            return count;
        }

        /// <summary>The meshes' triangles read for picking: let go when the picker is.</summary>
        public static void ForgetShapes()
        {
            Shapes.Clear();
            _lastStamp = -1;
            _lastHit = null;
        }

        private static Shape ShapeOf(Mesh mesh)
        {
            Shape shape;
            if (Shapes.TryGetValue(mesh, out shape))
                return shape;

            if (Shapes.Count >= 32)
                Shapes.Clear();
            shape = new Shape { Vertices = mesh.vertices, Triangles = mesh.triangles };
            Shapes[mesh] = shape;
            return shape;
        }

        /// <summary>The places past the buildings' ids: from the building buffer's end to 65535 (the city map's ids are 16 bits).</summary>
        private static void Places()
        {
            if (_taken != null)
                return;

            _first = Singleton<BuildingManager>.instance.m_buildings.m_buffer.Length;
            int end = Mathf.Min(65536, RenderManager.OBJECT_COLORMAP_WIDTH * RenderManager.OBJECT_COLORMAP_HEIGHT);
            _places = Mathf.Max(0, end - _first);
            _taken = new bool[_places];
        }

        /// <summary>A place for an object: its own number's where free (the same place every time the city loads), else the next free.</summary>
        private static int TakePlace(int id)
        {
            if (_places <= 0)
                return -1;

            int start = (id % _places + _places) % _places;
            for (int k = 0; k < _places; k++)
            {
                int place = (start + k) % _places;
                if (!_taken[place])
                {
                    _taken[place] = true;
                    return _first + place;
                }
            }
            return -1;
        }

        private static void FreePlace(int index)
        {
            int place = index - _first;
            if (_taken != null && place >= 0 && place < _places)
                _taken[place] = false;
        }

        private static void Forget()
        {
            List.Clear();
            ByObject.Clear();
            if (_taken != null)
                Array.Clear(_taken, 0, _taken.Length);
            _seenCount = -1;
            ForgetShapes();
        }

        private static void Fail(string what, Exception e)
        {
            _failed = true;
            Forget();
            Log.Error("procedural objects: " + what + " failed; its buildings keep the game's glass in this city", e);
        }

        /// <summary>The city is going: nothing kept of its objects (what was read of the mod stays).</summary>
        public static void Reset()
        {
            Forget();
            _taken = null;
            _failed = _pickFailed = false;
            _frame = -1;
            _loggedPresence = _loggedFirst = false;
            _loggedSummary = "";
        }

        private struct Candidate
        {
            public Entry Entry;
            public Mesh Mesh;
            public Vector3 Origin, Direction;
            public float Enter;
        }

        private sealed class Shape
        {
            public Vector3[] Vertices;
            public int[] Triangles;
        }

        /// <summary>Procedural Objects' objects by reference: whatever Equals they may have.</summary>
        private sealed class SameObject : IEqualityComparer<object>
        {
            bool IEqualityComparer<object>.Equals(object a, object b)
            {
                return ReferenceEquals(a, b);
            }

            public int GetHashCode(object o)
            {
                return RuntimeHelpers.GetHashCode(o);
            }
        }
    }
}
