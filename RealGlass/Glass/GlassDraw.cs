using System.Collections.Generic;
using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// The second draw of every building, right after the game's own (GlassPatches calls in from
    /// the game's three building paths), with the glass material and the same mesh, matrix and
    /// data. Only ever called on the main thread, from inside the game's rendering.
    /// </summary>
    public static class GlassDraw
    {
        /// <summary>0 near buildings only, 1 and the middle distance, 2 the whole city (GlassDistance).</summary>
        public static int Tiers = 2;

        /// <summary>Set by GlassController each frame: glass is on, the materials exist, no info view is open.</summary>
        public static bool Active;

        /// <summary>
        /// A patch threw (<see cref="Fail"/>): the glass stays off for the rest of this city, so the
        /// game's own drawing never meets the same error again. Cleared with the next city (Reset).
        /// </summary>
        public static bool Failed;

        public static Material Near, Lod, Group;

        // Per second, for the detail log line.
        public static int NearDraws, LodDraws, GroupDraws, NearSkipped;

        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        private static readonly Dictionary<Material, Maps> ByMaterial = new Dictionary<Material, Maps>();
        private static readonly Dictionary<BuildingInfoBase, bool> LodOk = new Dictionary<BuildingInfoBase, bool>();
        private static readonly Dictionary<int, bool> GroupLayerOk = new Dictionary<int, bool>();
        private static readonly Dictionary<string, int> ShaderCounts = new Dictionary<string, int>();

        private static int _idMain, _idXys, _idAci, _idState, _idMainSt, _idObjectIndex;
        private static bool _ids;

        /// <summary>What a building material gives the glass: its three maps, or nothing (not glass-capable).</summary>
        private sealed class Maps
        {
            public bool Ok;
            public Texture Main, Xys, Aci;
            public Vector4 MainSt;
        }

        private static void Ids()
        {
            if (_ids)
                return;
            _idMain = Shader.PropertyToID("_MainTex");
            _idXys = Shader.PropertyToID("_XYSMap");
            _idAci = Shader.PropertyToID("_ACIMap");
            _idState = Shader.PropertyToID("_BuildingState");
            _idMainSt = Shader.PropertyToID("_MainTex_ST");
            _idObjectIndex = Shader.PropertyToID("_ObjectIndex");
            _ids = true;
        }

        /// <summary>
        /// The game's building shaders the glass follows: Default and the variants that draw a
        /// building the same way. Everything else (fences, wind turbines, animated signs, the
        /// Rotors transparency...) moves or cuts its vertices in ways this draw does not repeat.
        /// </summary>
        public static bool IsBuildingShader(Shader shader)
        {
            if (shader == null)
                return false;
            string name = shader.name;
            return name == "Custom/Buildings/Building/Default" || name == "Custom/Buildings/Building/NoBase"
                || name == "Custom/Buildings/Building/Floating" || name == "Custom/Buildings/Building/Basement";
        }

        private static Maps MapsFor(Material material)
        {
            Maps maps;
            if (ByMaterial.TryGetValue(material, out maps))
                return maps;

            Ids();
            maps = new Maps();
            string shaderName = material.shader != null ? material.shader.name : "(none)";
            int count;
            ShaderCounts.TryGetValue(shaderName, out count);
            ShaderCounts[shaderName] = count + 1;
            if (IsBuildingShader(material.shader) && material.HasProperty(_idXys) && material.HasProperty(_idAci))
            {
                maps.Main = material.GetTexture(_idMain);
                maps.Xys = material.GetTexture(_idXys);
                maps.Aci = material.GetTexture(_idAci);
                Vector2 scale = material.mainTextureScale, offset = material.mainTextureOffset;
                maps.MainSt = new Vector4(scale.x, scale.y, offset.x, offset.y);
                maps.Ok = maps.Main != null && maps.Xys != null && maps.Aci != null;
            }

            ByMaterial[material] = maps;
            return maps;
        }

        /// <summary>
        /// One near building or sub-mesh: the game just drew mesh with material at matrix. state is
        /// its _BuildingState (m_dataVector0), objectIndex its _ObjectIndex (m_dataVector3: where it
        /// is in the game's object colour map -- which building it is, for its kind of glass and
        /// its lamps).
        /// </summary>
        public static void DrawNear(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Vector4 state, Vector4 objectIndex)
        {
            if (!Active || mesh == null || material == null || Near == null)
                return;

            Maps maps = MapsFor(material);
            if (!maps.Ok)
            {
                NearSkipped++;
                return;
            }

            Block.Clear();
            Block.SetTexture(_idMain, maps.Main);
            Block.SetTexture(_idXys, maps.Xys);
            Block.SetTexture(_idAci, maps.Aci);
            Block.SetVector(_idMainSt, maps.MainSt);
            Block.SetVector(_idState, state);
            Block.SetVector(_idObjectIndex, objectIndex);
            Graphics.DrawMesh(mesh, matrix, Near, layer, null, 0, Block, false, false);
            NearDraws++;
        }

        /// <summary>
        /// The middle distance: Building.RenderLod just drew up to 8 instances of info's combined
        /// LOD mesh, placed by the arrays it left in BuildingManager's property block. The same
        /// block (copied by DrawMesh at the call) draws them again with the glass.
        /// </summary>
        public static void DrawLod(BuildingInfoBase info, int count, MaterialPropertyBlock gameBlock)
        {
            if (!Active || Tiers < 1 || count <= 0 || Lod == null || info == null)
                return;

            Mesh mesh = count <= 1 ? info.m_lodMeshCombined1 : (count <= 4 ? info.m_lodMeshCombined4 : info.m_lodMeshCombined8);
            if (mesh == null || info.m_lodMaterialCombined == null)
                return;

            bool ok;
            if (!LodOk.TryGetValue(info, out ok))
            {
                ok = IsBuildingShader(info.m_lodMaterialCombined.shader);
                LodOk[info] = ok;
            }

            if (!ok)
                return;

            Graphics.DrawMesh(mesh, Matrix4x4.identity, Lod, info.m_prefabDataLayer, null, 0, gameBlock, false, false);
            LodDraws++;
        }

        /// <summary>The far city: one baked block (a group mesh) of a building layer.</summary>
        public static void DrawGroup(Mesh mesh, Matrix4x4 matrix, int layer, Material renderMaterial)
        {
            if (!Active || Tiers < 2 || mesh == null || Group == null)
                return;

            bool ok;
            if (!GroupLayerOk.TryGetValue(layer, out ok))
            {
                ok = GlassController.IsBuildingLayer(layer) && renderMaterial != null && renderMaterial.HasProperty("_XYSMap");
                GroupLayerOk[layer] = ok;
                Log.Msg("glass: group layer " + layer + " (" + LayerMask.LayerToName(layer) + ") material '" +
                        (renderMaterial != null ? renderMaterial.name : "none") + "' shader '" +
                        (renderMaterial != null && renderMaterial.shader != null ? renderMaterial.shader.name : "none") +
                        "': " + (ok ? "glass drawn" : "not a building layer"));
            }

            if (!ok)
                return;

            Graphics.DrawMesh(mesh, matrix, Group, layer, null, 0, null, false, false);
            GroupDraws++;
        }

        /// <summary>The building materials seen so far, by shader: which ones the glass follows.</summary>
        public static string DescribeShaders()
        {
            string s = "";
            foreach (KeyValuePair<string, int> kv in ShaderCounts)
                s += (s.Length > 0 ? ", " : "") + kv.Key + " x" + kv.Value;
            return s.Length > 0 ? s : "none yet";
        }

        /// <summary>
        /// A patch caught an exception inside the game's rendering: the glass stops at once (one
        /// log line, not one per building per frame) and stays off for this city -- whatever it
        /// was, the game's buildings must go on being drawn.
        /// </summary>
        public static void Fail(string where, System.Exception e)
        {
            Active = false;
            if (Failed)
                return;

            Failed = true;
            try
            {
                Log.Error("glass: " + where + " failed; the glass is off until the next city is loaded", e);
            }
            catch
            {
                // the log itself failed: nothing more to do, the glass is off
            }
        }

        /// <summary>Forget every cached material and layer (a city unloads, or the atlas changed).</summary>
        public static void Reset()
        {
            ByMaterial.Clear();
            LodOk.Clear();
            GroupLayerOk.Clear();
            ShaderCounts.Clear();
            NearDraws = LodDraws = GroupDraws = NearSkipped = 0;
            Active = false;
            Failed = false;
        }
    }
}
