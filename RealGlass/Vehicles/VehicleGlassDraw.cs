using System;
using System.Collections.Generic;
using RealGlass.Glass;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealGlass.Vehicles
{
    /// <summary>
    /// The glass of every vehicle the game draws in full: its windows (the game's own rule, in
    /// VehicleGlass.shader), the lenses of its lamps, a dark inside, and the clear coat over its paint.
    /// VehicleGlassPatches hands over each of the game's near draws of a vehicle -- the very mesh,
    /// body matrix, material and layer it drew with -- right after the game made it; they are gathered per
    /// model (mesh + material) and drawn once the game has drawn every vehicle (VehicleManager's
    /// EndRenderingImpl), every copy of a model in ONE Graphics.DrawMeshInstanced (Unity splits it to the
    /// shader's array: 500 on Direct3D 11, 125 on OpenGL and Metal). Where instancing is missing, one
    /// DrawMesh a vehicle. Only the near tier: a vehicle's LOD is a few pixels, its glass fewer.
    ///
    /// Memory follows the models, not the vehicles: a model's arrays start at a few copies and double as
    /// its copies in view need, up to the shader's array (a model with more is drawn at every 500 and
    /// goes on); how many models are remembered is capped (<see cref="MaxModels"/>). Measured in the game
    /// (2026-10-26, a highway, ~4,000 vehicle meshes a second through the hook, the glass switched off and
    /// on in turns): the process's memory did not grow with the glass on.
    ///
    /// Only ever called on the main thread, from inside the game's rendering. Every entry point is in a
    /// try: an exception here would come back for every vehicle every frame, so one is logged and the
    /// vehicles' glass stays off for the rest of the city (<see cref="Fail"/>); the game draws on.
    /// </summary>
    public static class VehicleGlassDraw
    {
        /// <summary>Set by GlassController each frame: the glass is on, the Vehicles tab's switch is, the material exists.</summary>
        public static bool Active;

        /// <summary>A draw threw: off until the next city (<see cref="Reset"/>).</summary>
        public static bool Failed;

        /// <summary>RealGlass/VehicleGlass, with instancing enabled (GlassController makes it).</summary>
        public static Material Glass;

        // Per second, for the detail log line.
        public static int Vehicles, Draws;

        /// <summary>The most instances one DrawMeshInstanced is given: Direct3D 11's array (UNITY_MAX_INSTANCE_COUNT, Unity 5.6).</summary>
        private const int MaxInstances = 500;

        /// <summary>A model's arrays start this long and double as its copies in view need, up to <see cref="MaxInstances"/>.</summary>
        private const int FirstInstances = 8;

        /// <summary>
        /// The most models (mesh + material) and materials remembered at once. The game's own vehicle
        /// assets, a heavy Workshop collection included, stay far below it (one material per prefab, plus
        /// its sub-meshes' and the underground one the game makes); it is reached only by a mod handing the
        /// game a NEW material or mesh per vehicle per draw -- then the ones not in view are forgotten and
        /// read again when they come back (<see cref="Forget"/>), and memory stays bounded.
        /// </summary>
        private const int MaxModels = 8192;

        /// <summary>What a vehicle material gives its glass: the game's shader kind, its maps, or nothing.</summary>
        private sealed class Source
        {
            public bool Ok;
            public float ShaderKind;
            public Texture Main, Xys, Aci;
            public Vector4 St, Floor;
        }

        /// <summary>One model's vehicles this frame.</summary>
        private sealed class Batch
        {
            public long Key;
            public Mesh Mesh;
            public Material Material;
            public Source Source;
            public int Layer;
            // As long as this model has had copies in view at once (FirstInstances .. MaxInstances).
            public Matrix4x4[] Matrices = new Matrix4x4[FirstInstances];
            public float[] Seeds = new float[FirstInstances];
            public int Count;
        }

        private static readonly Dictionary<Material, Source> Sources = new Dictionary<Material, Source>();
        private static readonly Dictionary<long, Batch> Batches = new Dictionary<long, Batch>();
        private static readonly List<Batch> Pending = new List<Batch>();
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        private static readonly Dictionary<string, int> ShaderCounts = new Dictionary<string, int>();
        /// <summary>The seeds handed to the block, always the shader's full array (see <see cref="DrawBatch"/>).</summary>
        private static readonly float[] SeedsFull = new float[MaxInstances];
        private static int _frame = -1;
        private static bool _instanced;
        private static bool _loggedMode;
        private static bool _loggedForget;

        private static int _idMain, _idXys, _idAci, _idFloor, _idShader, _idSt, _idSeed;
        private static bool _ids;

        private static void Ids()
        {
            if (_ids)
                return;
            _idMain = Shader.PropertyToID("_MainTex");
            _idXys = Shader.PropertyToID("_XYSMap");
            _idAci = Shader.PropertyToID("_ACIMap");
            _idFloor = Shader.PropertyToID("_FloorParams");
            _idShader = Shader.PropertyToID("_RGVehicleShader");
            _idSt = Shader.PropertyToID("_RGVehicleST");
            _idSeed = Shader.PropertyToID("_RGVehicleSeed");
            _ids = true;
        }

        /// <summary>A new city: every cached material and model forgotten (the old city's are gone).</summary>
        public static void Reset()
        {
            Active = false;
            Failed = false;
            Sources.Clear();
            Batches.Clear();
            Pending.Clear();
            ShaderCounts.Clear();
            _frame = -1;
            _loggedMode = false;
            _loggedForget = false;
            Vehicles = Draws = 0;
        }

        /// <summary>
        /// More models or materials than <see cref="MaxModels"/>: the ones not in view this frame are
        /// forgotten (their arrays with them); a model coming back is read again from its material. The
        /// models in view this frame (pending, each with a copy noted) stay. Once per city in the log.
        /// </summary>
        private static void Forget()
        {
            Sources.Clear();
            Batches.Clear();
            for (int i = 0; i < Pending.Count; i++)
                Batches[Pending[i].Key] = Pending[i];
            if (_loggedForget)
                return;
            _loggedForget = true;
            Log.Msg("vehicles: over " + MaxModels + " vehicle models or materials seen in this city: the ones not in view are forgotten and read " +
                    "again when they come back (a mod may be making a material per vehicle)");
        }

        /// <summary>This model has more copies in view at once than its arrays hold: twice as long, up to the shader's array.</summary>
        private static void Grow(Batch b)
        {
            int size = Math.Min(b.Matrices.Length * 2, MaxInstances);
            var matrices = new Matrix4x4[size];
            Array.Copy(b.Matrices, matrices, b.Count);
            b.Matrices = matrices;
            var seeds = new float[size];
            Array.Copy(b.Seeds, seeds, b.Count);
            b.Seeds = seeds;
        }

        /// <summary>
        /// The game's vehicle shaders this draw follows -- the ones whose final pass takes the shine over
        /// 0.8 as a window (their disassembly, 2026-10-23) and whose vertex program moves nothing but the
        /// tyres: 0 Default (cars, buses, vans, trucks), 1 Train (its own lit windows at night), 2 Aircraft
        /// and Ship (windows only). -1 for every other (Rotors, Helicopter, Underground, Billboard, the
        /// race shaders...).
        /// </summary>
        public static float ShaderKindOf(Shader shader)
        {
            if (shader == null)
                return -1f;
            switch (shader.name)
            {
                case "Custom/Vehicles/Vehicle/Default": return 0f;
                case "Custom/Vehicles/Vehicle/Train": return 1f;
                case "Custom/Vehicles/Vehicle/Aircraft":
                case "Custom/Vehicles/Vehicle/Ship": return 2f;
                default: return -1f;
            }
        }

        private static Source SourceFor(Material material)
        {
            Source s;
            if (Sources.TryGetValue(material, out s))
                return s;

            s = new Source();
            Shader shader = material.shader;
            string shaderName = shader != null ? shader.name : "(none)";
            int seen;
            ShaderCounts.TryGetValue(shaderName, out seen);
            ShaderCounts[shaderName] = seen + 1;

            s.ShaderKind = ShaderKindOf(shader);
            if (s.ShaderKind >= 0f && material.HasProperty(_idXys) && material.HasProperty(_idAci))
            {
                s.Xys = material.GetTexture(_idXys);
                s.Aci = material.GetTexture(_idAci);
                // The albedo: the coat's livery test reads it (off today); white where it is missing.
                s.Main = material.HasProperty(_idMain) ? material.GetTexture(_idMain) : null;
                Vector2 scale = material.HasProperty(_idMain) ? material.mainTextureScale : Vector2.one;
                Vector2 offset = material.HasProperty(_idMain) ? material.mainTextureOffset : Vector2.zero;
                s.St = new Vector4(scale.x, scale.y, offset.x, offset.y);
                s.Floor = material.HasProperty(_idFloor) ? material.GetVector(_idFloor) : new Vector4(0.5f, 2f, 2.5f, 5f);
                s.Ok = s.Xys != null && s.Aci != null;
            }

            if (Sources.Count >= MaxModels)
                Forget();
            Sources[material] = s;
            return s;
        }

        /// <summary>A moving vehicle the game just drew (Vehicle.RenderInstance): its id from the InstanceID it drew with.</summary>
        public static void NoteMoving(Mesh mesh, Matrix4x4 matrix, Material material, int layer, InstanceID id)
        {
            if (!Active)
                return;
            Note(mesh, matrix, material, layer, id.RawData);
        }

        /// <summary>A parked vehicle the game just drew (VehicleParked.RenderInstance): its id as the game's InstanceID of a parked vehicle.</summary>
        public static void NoteParked(Mesh mesh, Matrix4x4 matrix, Material material, int layer, ushort parkedId)
        {
            if (!Active)
                return;
            Note(mesh, matrix, material, layer, new InstanceID { ParkedVehicle = parkedId }.RawData);
        }

        /// <summary>
        /// A vehicle as the game drew it, with its id (InstanceID.RawData): the id gives it its own random
        /// number (whether its windows are tinted, and how dark), the same every frame, so a car keeps its
        /// glass as it drives.
        /// </summary>
        private static void Note(Mesh mesh, Matrix4x4 matrix, Material material, int layer, uint id)
        {
            try
            {
                if (mesh == null || material == null || Glass == null)
                    return;
                Ids();
                int frame = Time.frameCount;
                if (frame != _frame)
                {
                    // Vehicles drawn outside a VehicleManager frame (an editor's preview) and never
                    // flushed: dropped, not drawn a frame late.
                    for (int i = 0; i < Pending.Count; i++)
                        Pending[i].Count = 0;
                    Pending.Clear();
                    _frame = frame;
                }

                Source source = SourceFor(material);
                if (!source.Ok)
                    return;

                long key = ((long)mesh.GetInstanceID() << 32) ^ (uint)material.GetInstanceID();
                Batch b;
                if (!Batches.TryGetValue(key, out b) || b.Mesh != mesh || b.Material != material)
                {
                    if (Batches.Count >= MaxModels)
                        Forget();
                    b = new Batch { Key = key, Mesh = mesh, Material = material, Source = source };
                    Batches[key] = b;
                }
                if (b.Count == 0)
                {
                    b.Layer = layer;
                    Pending.Add(b);
                }
                else if (b.Count == MaxInstances)
                {
                    DrawBatch(b);   // a full array drawn now; the model stays pending for the rest
                }
                else if (b.Count == b.Matrices.Length)
                {
                    Grow(b);
                }

                b.Matrices[b.Count] = matrix;
                b.Seeds[b.Count] = VehicleGlassLook.Seed(id);
                b.Count++;
                Vehicles++;
            }
            catch (Exception e)
            {
                Fail("noting a vehicle", e);
            }
        }

        /// <summary>Every model gathered this frame drawn (after VehicleManager's EndRenderingImpl).</summary>
        public static void Flush()
        {
            if (Pending.Count == 0)
                return;
            try
            {
                if (Active)
                {
                    for (int i = 0; i < Pending.Count; i++)
                    {
                        if (Pending[i].Count > 0)
                            DrawBatch(Pending[i]);
                    }
                }
                for (int i = 0; i < Pending.Count; i++)
                    Pending[i].Count = 0;
                Pending.Clear();
            }
            catch (Exception e)
            {
                Pending.Clear();
                Fail("drawing the vehicles' glass", e);
            }
        }

        private static void DrawBatch(Batch b)
        {
            if (!_loggedMode)
            {
                _loggedMode = true;
                _instanced = SystemInfo.supportsInstancing && Glass.enableInstancing;
                Log.Msg("vehicles: glass drawn " + (_instanced ? "with GPU instancing, one draw per model in view"
                    : "one draw per vehicle (no GPU instancing here)"));
            }

            Block.Clear();
            if (b.Source.Main != null)
                Block.SetTexture(_idMain, b.Source.Main);
            Block.SetTexture(_idXys, b.Source.Xys);
            Block.SetTexture(_idAci, b.Source.Aci);
            Block.SetVector(_idFloor, b.Source.Floor);
            Block.SetFloat(_idShader, b.Source.ShaderKind);
            Block.SetVector(_idSt, b.Source.St);
            if (_instanced)
            {
                // The seeds' array keeps the length it is first given in a block: always the full one,
                // this model's seeds copied in (what lies past its count is never read).
                Array.Copy(b.Seeds, SeedsFull, b.Count);
                Block.SetFloatArray(_idSeed, SeedsFull);
                Graphics.DrawMeshInstanced(b.Mesh, 0, Glass, b.Matrices, b.Count, Block, ShadowCastingMode.Off, false, b.Layer, null);
                Draws++;
            }
            else
            {
                for (int i = 0; i < b.Count; i++)
                {
                    Block.SetFloat(_idSeed, b.Seeds[i]);
                    Graphics.DrawMesh(b.Mesh, b.Matrices[i], Glass, b.Layer, null, 0, Block, false, false);
                    Draws++;
                }
            }
            b.Count = 0;
        }

        /// <summary>The game's vehicle shaders seen so far and how many materials of each, for the log.</summary>
        public static string DescribeShaders()
        {
            if (ShaderCounts.Count == 0)
                return "none yet";
            string s = "";
            foreach (KeyValuePair<string, int> pair in ShaderCounts)
                s += (s.Length > 0 ? ", " : "") + pair.Key.Replace("Custom/Vehicles/Vehicle/", "") + " x" + pair.Value;
            return s;
        }

        public static void Fail(string where, Exception e)
        {
            Active = false;
            if (Failed)
                return;

            Failed = true;
            ErrorNotice.Report(ErrorNotice.Vehicles);
            try
            {
                Log.Error("vehicles: " + where + " failed; the vehicles' glass is off until the next city is loaded", e);
            }
            catch
            {
                // the log itself failed: nothing more to do, the vehicles' glass is off
            }
        }
    }
}
