using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RealGlass.Water
{
    /// <summary>
    /// The foam's hook: in TerrainPatch.Render and TerrainPatch.RenderSubPatch (IL 2026-10-06: each
    /// patch's water is one Graphics.DrawMesh of a generic grid with the water material and the shared
    /// TerrainManager.m_materialBlock2, just filled for that patch), every call of the 9-argument
    /// Graphics.DrawMesh goes through <see cref="WaterFoam.DrawMesh"/> instead, which makes the game's
    /// call exactly as it was and then, for the water, draws the foam. The game's draw is never skipped
    /// or changed. A transpiler, not a postfix: the mesh, matrix and property block the game draws with
    /// are its locals, gone by the time a postfix runs. When the game's code changes and no call is
    /// found, nothing is replaced and the log says so (no foam; nothing else touched).
    /// </summary>
    [HarmonyPatch]
    public static class WaterFoamDrawPatch
    {
        private static readonly MethodInfo GameDraw = AccessTools.Method(typeof(Graphics), nameof(Graphics.DrawMesh), new[]
        {
            typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(int), typeof(Camera), typeof(int), typeof(MaterialPropertyBlock),
            typeof(bool), typeof(bool),
        });

        private static readonly MethodInfo OurDraw = AccessTools.Method(typeof(WaterFoam), nameof(WaterFoam.DrawMesh));

        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(TerrainPatch), nameof(TerrainPatch.Render));
            yield return AccessTools.Method(typeof(TerrainPatch), "RenderSubPatch");
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var result = new List<CodeInstruction>();
            int swapped = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (GameDraw != null && OurDraw != null && (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    instruction.operand is MethodInfo called && called == GameDraw)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = OurDraw;
                    swapped++;
                }

                result.Add(instruction);
            }

            WaterFoam.NoteHooks(original != null ? original.Name : "?", swapped);
            Log.Msg("water: the foam's hook in TerrainPatch." + (original != null ? original.Name : "?") + ": " + swapped +
                    " of the game's draws routed (the game's own call first, unchanged)" + (swapped == 0 ? " -- NONE FOUND: no foam there" : ""));
            return result;
        }
    }
}
