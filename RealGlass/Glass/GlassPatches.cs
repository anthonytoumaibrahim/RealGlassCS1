using System;
using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace RealGlass.Glass
{
    // The game's three building paths (IL, 2026-09-28) and the asset editor's (2026-09-29). Each
    // postfix repeats the game's own choice -- near or LOD, which mesh, which matrix -- and draws
    // the same thing again with the glass. Postfixes only: the game's draw is never skipped or
    // changed. The argument types are spelled out so an overload added by a game update cannot
    // make PatchAll fail for every patch in the mod. Parameter names must match the game's: that
    // is how Harmony binds them. Never take __args (CitiesHarmony's Harmony 2.0.4 writes it back
    // over ref arguments).
    //
    // A postfix reads each argument as the ORIGINAL LEFT IT, not as the caller passed it: Harmony
    // copies the original's IL into the patched method, so its starg writes the very slot the
    // postfix loads (probed with Harmony 2.0.4 on Unity 5.6's Mono, 2026-09-28). Of the methods
    // below, only the sub-mesh RenderMesh reassigns an argument.
    //
    // Every patch body is inside a try: these run in the middle of the game's own drawing, and an
    // exception there would come back every frame for every building. One is caught, logged once,
    // and the glass is off for the rest of the city (GlassDraw.Fail); the game draws on.

    /// <summary>
    /// A near building: when CheckRenderDistance(instance.m_position, info.m_minLodDistance) holds,
    /// the game drew info.m_mesh at instance.m_dataMatrix1 with info.m_material, the building's
    /// state vector (m_dataVector0) in BuildingManager.m_materialBlock; else it queued the building
    /// for Building.RenderLod. m_overrideMainRenderer buildings draw nothing here.
    /// </summary>
    [HarmonyPatch(typeof(BuildingAI), nameof(BuildingAI.RenderMesh), new[]
    {
        typeof(RenderManager.CameraInfo), typeof(ushort), typeof(Building), typeof(BuildingInfo), typeof(RenderManager.Instance),
    }, new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Ref })]
    public static class RenderMeshPatch
    {
        public static void Postfix(RenderManager.CameraInfo cameraInfo, BuildingInfo info, ref RenderManager.Instance instance)
        {
            if (!GlassDraw.Active)
                return;

            try
            {
                if (info == null || info.m_mesh == null || info.m_overrideMainRenderer != null)
                    return;
                if (!cameraInfo.CheckRenderDistance(instance.m_position, info.m_minLodDistance))
                    return;

                GlassDraw.DrawNear(info.m_mesh, instance.m_dataMatrix1, info.m_material, info.m_prefabDataLayer, instance.m_dataVector0,
                    instance.m_dataVector3);
            }
            catch (Exception e)
            {
                GlassDraw.Fail("a near building's glass", e);
            }
        }
    }

    /// <summary>
    /// A near sub-mesh: the game drew subInfo.m_mesh with subInfo.m_material at
    /// instance.m_dataMatrix1 x matrix, its height put on the ground when followTerrain -- and it
    /// works that out IN its matrix argument (starg) before drawing, so the matrix this postfix
    /// reads is already the one the game drew with. Multiplying it by m_dataMatrix1 again (as the
    /// first version did) drew the glass of every sub-mesh a second building transform away: lone
    /// blocks of glass across the city, which moved twice as far as their building did.
    /// </summary>
    [HarmonyPatch(typeof(BuildingAI), nameof(BuildingAI.RenderMesh), new[]
    {
        typeof(RenderManager.CameraInfo), typeof(Building), typeof(BuildingInfo), typeof(BuildingInfoBase), typeof(Matrix4x4),
        typeof(RenderManager.Instance), typeof(bool),
    }, new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    public static class RenderSubMeshPatch
    {
        public static void Postfix(RenderManager.CameraInfo cameraInfo, BuildingInfo info, BuildingInfoBase subInfo, Matrix4x4 matrix,
                                   ref RenderManager.Instance instance)
        {
            if (!GlassDraw.Active)
                return;

            try
            {
                if (subInfo == null || subInfo.m_mesh == null || info == null)
                    return;
                if (!cameraInfo.CheckRenderDistance(instance.m_position, subInfo.m_minLodDistance))
                    return;

                GlassDraw.DrawNear(subInfo.m_mesh, matrix, subInfo.m_material, info.m_prefabDataLayer, instance.m_dataVector0,
                    instance.m_dataVector3);
            }
            catch (Exception e)
            {
                GlassDraw.Fail("a near sub-mesh's glass", e);
            }
        }
    }

    /// <summary>
    /// The middle distance: Building.RenderLod draws up to 8 queued instances of one asset's LOD
    /// mesh and sets m_lodCount back to 0 -- so the prefix notes the count, and the postfix draws
    /// again with the arrays it left in BuildingManager.m_materialBlock.
    /// </summary>
    /// <remarks>
    /// The game's RenderLod ends by setting m_lodCount to 0 on every path (IL, 2026-10-14). If it is
    /// still set, the original did not run: another mod's prefix skipped it (Harmony runs the
    /// postfixes anyway), and the block holds the arrays of whichever asset was drawn before -- the
    /// glass would land on that asset's positions. Then nothing is drawn.
    /// </remarks>
    [HarmonyPatch(typeof(Building), nameof(Building.RenderLod), new[] { typeof(RenderManager.CameraInfo), typeof(BuildingInfoBase) })]
    public static class RenderLodPatch
    {
        public static void Prefix(BuildingInfoBase info, out int __state)
        {
            // A prefix that threw would keep the game's own draw from running: never.
            try
            {
                __state = info != null ? info.m_lodCount : 0;
            }
            catch
            {
                __state = 0;
            }
        }

        public static void Postfix(BuildingInfoBase info, int __state)
        {
            if (!GlassDraw.Active || __state <= 0)
                return;

            try
            {
                if (info == null || info.m_lodCount != 0)
                {
                    GlassDraw.NoteLodSkipped(info);
                    return;
                }

                GlassDraw.DrawLod(info, __state, Singleton<BuildingManager>.instance.m_materialBlock);
            }
            catch (Exception e)
            {
                GlassDraw.Fail("the middle distance's glass", e);
            }
        }
    }

    /// <summary>The far city: a 384 m block's baked mesh of one layer (a chain of meshes).</summary>
    [HarmonyPatch(typeof(RenderGroup), "RenderMesh", new[] { typeof(RenderGroup.MeshLayer) })]
    public static class GroupRenderMeshPatch
    {
        public static void Postfix(RenderGroup __instance, RenderGroup.MeshLayer layer)
        {
            if (!GlassDraw.Active || GlassDraw.Tiers < 2 || layer == null)
                return;

            try
            {
                for (RenderGroup.MeshWrapper w = layer.m_mesh; w != null; w = w.m_next)
                {
                    if (w.m_mesh != null)
                        GlassDraw.DrawGroup(w.m_mesh, __instance.m_matrix, layer.m_layer, layer.m_renderMaterial);
                }
            }
            catch (Exception e)
            {
                GlassDraw.Fail("the far city's glass", e);
            }
        }
    }

    /// <summary>
    /// The asset editor's building. It is not a building instance: ToolManager.EndRenderingImpl
    /// draws the edited prefab through BuildingInfo.RenderMesh -> this method, at a fixed
    /// TRS((0, 60, 0), identity, one) with _BuildingState (0, 1000, 0, 0) and _ObjectIndex =
    /// RenderManager.GetColorLocation(0). Near when m_ForceLowLod (the editor's LOD view) is off
    /// and CheckRenderDistance((0, 60, 0), m_minLodDistance) holds; else the asset's OWN m_lodMesh
    /// with m_lodMaterial, one mesh with the LOD's own textures, no atlas: the near glass draws
    /// it. The sub-meshes the editor shows (IsSubMeshRendered) the same way, at that matrix x
    /// m_matrix, on the main asset's layer. Sub-BUILDINGS are real buildings: the patches above.
    /// </summary>
    [HarmonyPatch(typeof(BuildingDecoration), nameof(BuildingDecoration.RenderBuildingMesh), new[]
    {
        typeof(RenderManager.CameraInfo), typeof(BuildingInfo),
    })]
    public static class EditorRenderMeshPatch
    {
        private static readonly Vector3 Position = new Vector3(0f, 60f, 0f);
        private static readonly Vector4 State = new Vector4(0f, 1000f, 0f, 0f);

        public static void Postfix(RenderManager.CameraInfo cameraInfo, BuildingInfo info)
        {
            if (!GlassDraw.Active || cameraInfo == null || info == null)
                return;

            try
            {
                Matrix4x4 matrix = Matrix4x4.TRS(Position, Quaternion.identity, Vector3.one);
                Vector4 objectIndex = RenderManager.GetColorLocation(0);
                Draw(cameraInfo, info, matrix, info.m_prefabDataLayer, objectIndex);

                BuildingInfo.MeshInfo[] subMeshes = info.m_subMeshes;
                if (subMeshes == null)
                    return;

                for (int i = 0; i < subMeshes.Length; i++)
                {
                    BuildingInfo.MeshInfo sub = subMeshes[i];
                    if (sub == null || sub.m_subInfo == null || !BuildingDecoration.IsSubMeshRendered(sub.m_subInfo))
                        continue;

                    Draw(cameraInfo, sub.m_subInfo, matrix * sub.m_matrix, info.m_prefabDataLayer, objectIndex);
                }
            }
            catch (Exception e)
            {
                GlassDraw.Fail("the asset editor's glass", e);
            }
        }

        private static void Draw(RenderManager.CameraInfo cameraInfo, BuildingInfoBase mesh, Matrix4x4 matrix, int layer, Vector4 objectIndex)
        {
            if (!BuildingDecoration.m_ForceLowLod && cameraInfo.CheckRenderDistance(Position, mesh.m_minLodDistance))
                GlassDraw.DrawNear(mesh.m_mesh, matrix, mesh.m_material, layer, State, objectIndex);
            else if (GlassDraw.Tiers >= 1)
                GlassDraw.DrawNear(mesh.m_lodMesh, matrix, mesh.m_lodMaterial, layer, State, objectIndex);
        }
    }

    /// <summary>The farthest city: a 1920 m block's baked mesh of one layer.</summary>
    [HarmonyPatch(typeof(MegaRenderGroup), "RenderMesh", new[] { typeof(MegaRenderGroup.MeshLayer) })]
    public static class MegaGroupRenderMeshPatch
    {
        public static void Postfix(MegaRenderGroup __instance, MegaRenderGroup.MeshLayer layer)
        {
            if (!GlassDraw.Active || GlassDraw.Tiers < 2 || layer == null || layer.m_mesh == null)
                return;

            try
            {
                GlassDraw.DrawGroup(layer.m_mesh, __instance.m_matrix, layer.m_layer, layer.m_renderMaterial);
            }
            catch (Exception e)
            {
                GlassDraw.Fail("the farthest city's glass", e);
            }
        }
    }
}
