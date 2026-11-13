using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace MunRiverPower
{
    internal struct SpillwayReading
    {
        internal bool Valid;
        internal float Freeboard, Crest, Upstream, Downstream, MinimumDepth, Radius;
        internal float3 Intake, Outlet;
    }
    internal static class SpillwayProbe
    {
        internal static SpillwayReading Measure(EntityManager manager, Entity building, ref WaterSurfaceData<SurfaceWater> water, ref TerrainHeightData terrain, float cellSize, float3 origin)
        {
            var result = new SpillwayReading();
            if (!water.isCreated || !terrain.isCreated || cellSize <= 0 || !manager.HasBuffer<Game.Net.SubNet>(building)) return result;
            var radius = math.max(8, cellSize * 2);
            foreach (var subnet in manager.GetBuffer<Game.Net.SubNet>(building, true))
            {
                var edge = subnet.m_SubNet;
                if (!manager.Exists(edge) || !manager.HasComponent<Game.Net.Curve>(edge) || !manager.HasComponent<Game.Net.Composition>(edge) || !manager.HasComponent<PrefabRef>(edge)) continue;
                var prefab = manager.GetComponentData<PrefabRef>(edge).m_Prefab;
                if (!manager.HasComponent<PlaceableNetData>(prefab)) continue;
                var placement = manager.GetComponentData<PlaceableNetData>(prefab);
                if (((int)placement.m_PlacementFlags & 192) == 0) continue;
                var compositionEntity = manager.GetComponentData<Game.Net.Composition>(edge).m_Edge;
                if (!manager.HasComponent<NetCompositionData>(compositionEntity) || !manager.HasComponent<TerrainComposition>(compositionEntity)) continue;
                var composition = manager.GetComponentData<NetCompositionData>(compositionEntity);
                if (((int)composition.m_Flags.m_General & 26624) != 0) continue;
                var ground = manager.GetComponentData<TerrainComposition>(compositionEntity);
                var curve = manager.GetComponentData<Game.Net.Curve>(edge);
                for (var i = 0; i < 5; i++)
                {
                    var t = (i + 0.5f) / 5;
                    var p = Colossal.Mathematics.MathUtils.Position(curve.m_Bezier, t);
                    var tangent = Colossal.Mathematics.MathUtils.Tangent(curve.m_Bezier, t);
                    var normal = math.normalizesafe(new float2(tangent.z, -tangent.x));
                    if (((int)placement.m_PlacementFlags & 64) != 0) normal = -normal;
                    // Outside the wall and far enough apart that the two footprints cannot overlap.
                    var intake = p; var outlet = p;
                    var offset = composition.m_Width * 0.5f + radius * 2;
                    intake.xz -= normal * offset; outlet.xz += normal * offset;
                    intake.xz = origin.xz + (math.round((intake.xz - origin.xz) / cellSize) + 0.5f) * cellSize;
                    outlet.xz = origin.xz + (math.round((outlet.xz - origin.xz) / cellSize) + 0.5f) * cellSize;
                    var upper = WaterUtils.SampleHeight(ref water, ref terrain, intake, out float upperDepth);
                    var lower = WaterUtils.SampleHeight(ref water, ref terrain, outlet, out float lowerDepth);
                    if (upperDepth <= 0.25f || lowerDepth <= 0.25f || !math.isfinite(upper) || !math.isfinite(lower)) continue;
                    // Same structural reference used by the shipped hydro initializer.
                    // This is a simulation crest estimate, not the top of lamp posts/parapets.
                    var crest = p.y + composition.m_SurfaceHeight.min + ground.m_MaxHeightOffset.y;
                    var freeboard = crest - upper;
                    if (!math.isfinite(crest) || (result.Valid && freeboard >= result.Freeboard)) continue;
                    result = new SpillwayReading { Valid = true, Freeboard = freeboard, Crest = crest, Upstream = upper, Downstream = lower, MinimumDepth = math.min(upperDepth, lowerDepth), Radius = radius, Intake = intake, Outlet = outlet };
                }
            }
            return result;
        }
        internal static bool WetFootprints(ref WaterSurfaceData<SurfaceWater> water, ref TerrainHeightData terrain, SpillwayReading reading, float cellSize, out float minimum)
        {
            minimum = float.MaxValue;
            var steps = (int)math.ceil(reading.Radius / cellSize);
            for (var x = -steps; x <= steps; x++)
                for (var z = -steps; z <= steps; z++)
                {
                    var offset = new float2(x, z) * cellSize;
                    if (math.lengthsq(offset) > reading.Radius * reading.Radius) continue;
                    var a = reading.Intake; var b = reading.Outlet;
                    a.xz += offset; b.xz += offset;
                    WaterUtils.SampleHeight(ref water, ref terrain, a, out float d1);
                    WaterUtils.SampleHeight(ref water, ref terrain, b, out float d2);
                    if (!math.isfinite(d1) || !math.isfinite(d2) || d1 < 0.25f || d2 < 0.25f) return false;
                    minimum = math.min(minimum, math.min(d1, d2));
                }
            return minimum < float.MaxValue;
        }
    }
}
