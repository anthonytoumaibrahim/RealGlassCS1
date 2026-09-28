using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace RealGlass.Glass
{
    // The game's three building paths (IL, 2026-09-28). Each postfix repeats the game's own
    // choice -- near or LOD, which mesh, which matrix -- and draws the same thing again with the
    // glass. Postfixes only: the game's draw is never skipped or changed. The argument types are
    // spelled out so an overload added by a game update cannot make PatchAll fail for every patch
    // in the mod. Parameter names must match the game's: that is how Harmony binds them. Never
    // take __args (CitiesHarmony's Harmony 2.0.4 writes it back over ref arguments).

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
            if (!GlassDraw.Active || info == null || info.m_mesh == null || info.m_overrideMainRenderer != null)
                return;
            if (!cameraInfo.CheckRenderDistance(instance.m_position, info.m_minLodDistance))
                return;

            GlassDraw.DrawNear(info.m_mesh, instance.m_dataMatrix1, info.m_material, info.m_prefabDataLayer, instance.m_dataVector0);
        }
    }

    /// <summary>
    /// A near sub-building: the game drew subInfo.m_mesh with subInfo.m_material at
    /// instance.m_dataMatrix1 x matrix, its height put on the ground when followTerrain.
    /// </summary>
    [HarmonyPatch(typeof(BuildingAI), nameof(BuildingAI.RenderMesh), new[]
    {
        typeof(RenderManager.CameraInfo), typeof(Building), typeof(BuildingInfo), typeof(BuildingInfoBase), typeof(Matrix4x4),
        typeof(RenderManager.Instance), typeof(bool),
    }, new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    public static class RenderSubMeshPatch
    {
        public static void Postfix(RenderManager.CameraInfo cameraInfo, BuildingInfo info, BuildingInfoBase subInfo, Matrix4x4 matrix,
                                   ref RenderManager.Instance instance, bool followTerrain)
        {
            if (!GlassDraw.Active || subInfo == null || subInfo.m_mesh == null || info == null)
                return;
            if (!cameraInfo.CheckRenderDistance(instance.m_position, subInfo.m_minLodDistance))
                return;

            Matrix4x4 m = instance.m_dataMatrix1 * matrix;
            if (followTerrain)
                m.m13 = Singleton<TerrainManager>.instance.SampleDetailHeight(new Vector3(m.m03, m.m13, m.m23));
            GlassDraw.DrawNear(subInfo.m_mesh, m, subInfo.m_material, info.m_prefabDataLayer, instance.m_dataVector0);
        }
    }

    /// <summary>
    /// The middle distance: Building.RenderLod draws up to 8 queued instances of one asset's LOD
    /// mesh and sets m_lodCount back to 0 -- so the prefix notes the count, and the postfix draws
    /// again with the arrays it left in BuildingManager.m_materialBlock.
    /// </summary>
    [HarmonyPatch(typeof(Building), nameof(Building.RenderLod), new[] { typeof(RenderManager.CameraInfo), typeof(BuildingInfoBase) })]
    public static class RenderLodPatch
    {
        public static void Prefix(BuildingInfoBase info, out int __state)
        {
            __state = info != null ? info.m_lodCount : 0;
        }

        public static void Postfix(BuildingInfoBase info, int __state)
        {
            if (!GlassDraw.Active || __state <= 0)
                return;

            GlassDraw.DrawLod(info, __state, Singleton<BuildingManager>.instance.m_materialBlock);
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

            for (RenderGroup.MeshWrapper w = layer.m_mesh; w != null; w = w.m_next)
            {
                if (w.m_mesh != null)
                    GlassDraw.DrawGroup(w.m_mesh, __instance.m_matrix, layer.m_layer, layer.m_renderMaterial);
            }
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

            GlassDraw.DrawGroup(layer.m_mesh, __instance.m_matrix, layer.m_layer, layer.m_renderMaterial);
        }
    }
}
