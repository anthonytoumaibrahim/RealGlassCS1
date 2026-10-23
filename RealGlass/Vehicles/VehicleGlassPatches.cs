using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RealGlass.Vehicles
{
    // The vehicles' glass hooks (IL, 2026-10-23). The game draws a vehicle near enough to be seen in full
    // with Graphics.DrawMesh(mesh, body matrix, material, layer, null, 0, VehicleManager.m_materialBlock)
    // -- the 7-argument overload -- in the static Vehicle.RenderInstance (a moving vehicle: its main mesh
    // and each sub-mesh its flags and variation allow; underground ones with m_undergroundMaterial) and in
    // VehicleParked.RenderInstance. The body matrix (VehicleAI.CalculateBodyMatrix: its sway included) is
    // a local there, gone by the time a postfix runs: so a transpiler, which after each of those calls
    // hands the same mesh, matrix, material and layer to VehicleGlassDraw, with the vehicle's id.
    //
    // The game's call is never removed, replaced or moved: its seven arguments are copied to locals and
    // put back before it, and ours runs after it. Another mod's transpiler that looks for the call (his own
    // FrameDoctor swaps the same kind of call in PropInstance.RenderInstance) still finds it. In their own
    // namespace: a hook that cannot be applied takes the vehicles' glass with it and nothing else
    // (Patcher: only RealGlass.Glass's failures stop the buildings' glass).

    /// <summary>The shared transpiler: after each of the game's 7-argument DrawMesh calls, our note with the same draw.</summary>
    internal static class VehicleDrawHook
    {
        internal static readonly MethodInfo GameDraw = AccessTools.Method(typeof(Graphics), nameof(Graphics.DrawMesh), new[]
        {
            typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(int), typeof(Camera), typeof(int), typeof(MaterialPropertyBlock),
        });

        /// <param name="loadId">Loads the vehicle's id for <paramref name="note"/>'s last parameter (an argument of the original).</param>
        internal static IEnumerable<CodeInstruction> AfterEachDraw(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodInfo note,
                                                                  Func<CodeInstruction> loadId, string where)
        {
            var result = new List<CodeInstruction>();
            LocalBuilder mesh = null, matrix = null, material = null, layer = null, camera = null, submesh = null, block = null;
            int hooked = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                bool isDraw = GameDraw != null && note != null && (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                              instruction.operand is MethodInfo called && called == GameDraw;
                if (!isDraw)
                {
                    result.Add(instruction);
                    continue;
                }

                if (mesh == null)
                {
                    mesh = il.DeclareLocal(typeof(Mesh));
                    matrix = il.DeclareLocal(typeof(Matrix4x4));
                    material = il.DeclareLocal(typeof(Material));
                    layer = il.DeclareLocal(typeof(int));
                    camera = il.DeclareLocal(typeof(Camera));
                    submesh = il.DeclareLocal(typeof(int));
                    block = il.DeclareLocal(typeof(MaterialPropertyBlock));
                }

                // The seven arguments are on the stack, the last on top: copy each into its local (the
                // first store takes over any label on the call, with the stack exactly as the call had
                // it), load them back, let the game's own call take them.
                var first = new CodeInstruction(OpCodes.Stloc, block);
                first.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                result.Add(first);
                result.Add(new CodeInstruction(OpCodes.Stloc, submesh));
                result.Add(new CodeInstruction(OpCodes.Stloc, camera));
                result.Add(new CodeInstruction(OpCodes.Stloc, layer));
                result.Add(new CodeInstruction(OpCodes.Stloc, material));
                result.Add(new CodeInstruction(OpCodes.Stloc, matrix));
                result.Add(new CodeInstruction(OpCodes.Stloc, mesh));
                result.Add(new CodeInstruction(OpCodes.Ldloc, mesh));
                result.Add(new CodeInstruction(OpCodes.Ldloc, matrix));
                result.Add(new CodeInstruction(OpCodes.Ldloc, material));
                result.Add(new CodeInstruction(OpCodes.Ldloc, layer));
                result.Add(new CodeInstruction(OpCodes.Ldloc, camera));
                result.Add(new CodeInstruction(OpCodes.Ldloc, submesh));
                result.Add(new CodeInstruction(OpCodes.Ldloc, block));
                result.Add(instruction);

                // Then ours, with the draw the game just made.
                result.Add(new CodeInstruction(OpCodes.Ldloc, mesh));
                result.Add(new CodeInstruction(OpCodes.Ldloc, matrix));
                result.Add(new CodeInstruction(OpCodes.Ldloc, material));
                result.Add(new CodeInstruction(OpCodes.Ldloc, layer));
                result.Add(loadId());
                result.Add(new CodeInstruction(OpCodes.Call, note));
                hooked++;
            }

            Log.Msg("vehicles: the glass's hook in " + where + ": after " + hooked + " of the game's draws (the game's own calls unchanged)" +
                    (hooked == 0 ? " -- NONE FOUND: no glass on those vehicles" : ""));
            return result;
        }
    }

    /// <summary>A moving vehicle: the static Vehicle.RenderInstance, whose InstanceID (argument 13) names it.</summary>
    [HarmonyPatch]
    public static class VehicleRenderInstancePatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Vehicle), nameof(Vehicle.RenderInstance), new[]
            {
                typeof(RenderManager.CameraInfo), typeof(VehicleInfo), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Vector4),
                typeof(Vector4), typeof(Vector3), typeof(float), typeof(Color), typeof(Vehicle.Flags), typeof(Vehicle.Flags2), typeof(int),
                typeof(InstanceID), typeof(bool), typeof(bool),
            });
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            return VehicleDrawHook.AfterEachDraw(instructions, il, AccessTools.Method(typeof(VehicleGlassDraw), nameof(VehicleGlassDraw.NoteMoving)),
                () => new CodeInstruction(OpCodes.Ldarg_S, (byte)13), "Vehicle.RenderInstance");
        }
    }

    /// <summary>A parked vehicle: VehicleParked.RenderInstance(cameraInfo, parkedVehicleID), an instance method of the struct (argument 0 is it).</summary>
    [HarmonyPatch(typeof(VehicleParked), nameof(VehicleParked.RenderInstance), new[] { typeof(RenderManager.CameraInfo), typeof(ushort) })]
    public static class VehicleParkedRenderInstancePatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            return VehicleDrawHook.AfterEachDraw(instructions, il, AccessTools.Method(typeof(VehicleGlassDraw), nameof(VehicleGlassDraw.NoteParked)),
                () => new CodeInstruction(OpCodes.Ldarg_2), "VehicleParked.RenderInstance");
        }
    }

    /// <summary>The game has drawn every vehicle of the frame: their glass now, one draw per model.</summary>
    [HarmonyPatch(typeof(VehicleManager), "EndRenderingImpl", new[] { typeof(RenderManager.CameraInfo) })]
    public static class VehicleManagerEndRenderingPatch
    {
        public static void Postfix()
        {
            VehicleGlassDraw.Flush();
        }
    }
}
