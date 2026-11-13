using System;
using System.Reflection;
using Game.Simulation;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MunRiverPower
{
    internal struct SpillwayTransfer { internal SpillwayReading Reading; internal float Depth; }
    /// <summary>
    /// Use the SAME native additive kernel for both sides. Native fresh-water source
    /// and pump modes use different kernels and cannot be assumed to conserve volume.
    /// No persistent source entities, sea-level edits or pollutant sentinel are used.
    /// Private shader bindings are validated for the installed game; fail closed if changed.
    /// </summary>
    internal sealed class SpillwayWaterAdapter
    {
        private object simulation;
        private ComputeShader shader;
        private bool hasSourceSpeed;
        private int kernel, resultId, activeId, positionId, radiusId, amountId, pollutedId, cellId, timestepId, speedId;
        private readonly FieldInfo readyFrame = typeof(WaterSystem).GetField("m_LastReadyFrame", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        internal uint ReadbackFrame(WaterSystem water) => readyFrame == null ? 0 : (uint)readyFrame.GetValue(water);
        private static object Field(object value, string name)
        {
            var field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(value.GetType().FullName, name);
            return field.GetValue(value);
        }
        internal bool Bind(WaterSystem water)
        {
            var current = water.WaterSimulation;
            if (current == null || readyFrame == null) return false;
            var typeName = current.GetType().FullName;
            if (typeName != "Game.Simulation.WaterSimulation" && typeName != "Game.Simulation.WaterSimulationLegacy") return false;
            var currentShader = Field(current, "m_UpdateShader") as ComputeShader;
            if (currentShader == null) return false;
            if (ReferenceEquals(current, simulation) && shader == currentShader) return true;
            kernel = (int)Field(current, "m_AddKernel");
            resultId = (int)Field(current, "m_ID_Result"); activeId = (int)Field(current, "m_ID_Active");
            positionId = (int)Field(current, "m_ID_AddPosition"); radiusId = (int)Field(current, "m_ID_AddRadius");
            amountId = (int)Field(current, "m_ID_AddAmount"); pollutedId = (int)Field(current, "m_ID_AddPolluted");
            cellId = (int)Field(current, "m_ID_CellSize"); timestepId = (int)Field(current, "m_ID_Timestep");
            hasSourceSpeed = typeName == "Game.Simulation.WaterSimulation";
            if (hasSourceSpeed) speedId = (int)Field(current, "m_ID_WaterSourceSpeed");
            currentShader.GetKernelThreadGroupSizes(kernel, out var x, out var y, out var z);
            if (x == 0 || y == 0 || z != 1) return false;
            simulation = current; shader = currentShader;
            return true;
        }
        internal bool Append(CommandBuffer commands, WaterSystem water, float3 origin, SpillwayTransfer transfer)
        {
            if (!Bind(water) || water.WaterTexture == null) return false;
            var helper = Field(water, "m_waterSimActiveTilesHelper");
            if (helper == null) return false;
            var getBuffer = helper.GetType().GetMethod("GetActiveBuffer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getBuffer == null || getBuffer.ReturnType != typeof(ComputeBuffer)) return false;
            var buffer = getBuffer.Invoke(helper, null) as ComputeBuffer;
            if (buffer == null) return false;
            var cell = water.CellSize;
            var a = transfer.Reading.Intake - origin;
            var b = transfer.Reading.Outlet - origin;
            var radius = transfer.Reading.Radius;
            var extent = new float2(water.WaterTexture.width, water.WaterTexture.height) * cell;
            if (cell <= 0 || !math.isfinite(transfer.Depth) || transfer.Depth <= 0 ||
                !math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) ||
                math.any(a.xz - radius < 0) || math.any(b.xz - radius < 0) ||
                math.any(a.xz + radius >= extent) || math.any(b.xz + radius >= extent)) return false;
            shader.GetKernelThreadGroupSizes(kernel, out var tx, out var ty, out _);
            var cells = 2 * (int)math.ceil(transfer.Reading.Radius / cell) + 1;
            var gx = (int)math.ceil(cells / (float)tx); var gy = (int)math.ceil(cells / (float)ty);
            commands.SetComputeFloatParam(shader, cellId, cell);
            commands.SetComputeFloatParam(shader, timestepId, 1);
            if (hasSourceSpeed) commands.SetComputeFloatParam(shader, speedId, 1);
            commands.SetComputeTextureParam(shader, kernel, resultId, water.WaterTexture);
            commands.SetComputeBufferParam(shader, kernel, activeId, buffer);
            commands.SetComputeFloatParam(shader, radiusId, transfer.Reading.Radius);
            commands.SetComputeFloatParam(shader, pollutedId, 0);
            // Snapped centers and identical radii give the same discrete kernel weights.
            commands.SetComputeVectorParam(shader, positionId, new Vector4(a.x, -transfer.Depth, a.z, 0));
            commands.SetComputeFloatParam(shader, amountId, -transfer.Depth);
            commands.DispatchCompute(shader, kernel, gx, gy, 1);
            commands.SetComputeVectorParam(shader, positionId, new Vector4(b.x, transfer.Depth, b.z, 0));
            commands.SetComputeFloatParam(shader, amountId, transfer.Depth);
            commands.DispatchCompute(shader, kernel, gx, gy, 1);
            return true;
        }
    }
}
