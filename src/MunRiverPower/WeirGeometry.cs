using System.Collections.Generic;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace MunRiverPower
{
    internal struct WeirMeasurement
    {
        internal float Length;
        internal float Head;
        internal double NativeCapacity;
        internal List<Entity> Edges;
    }
    internal static class WeirGeometry
    {
        internal static WeirMeasurement Measure(EntityManager manager, Entity building, ref WaterSurfaceData<SurfaceWater> water, ref TerrainHeightData terrain, float nativeCapacityFactor)
        {
            var result = new WeirMeasurement { Edges = new List<Entity>() };
            if (!water.isCreated || !terrain.isCreated || !manager.HasBuffer<Game.Net.SubNet>(building)) return result;
            var area = 0.0;
            var seen = new HashSet<Entity>();
            foreach (var item in manager.GetBuffer<Game.Net.SubNet>(building, true))
            {
                var edge = item.m_SubNet;
                if (!seen.Add(edge) || !manager.Exists(edge) || !manager.HasComponent<Game.Net.Curve>(edge) || !manager.HasComponent<Game.Net.Composition>(edge) || !manager.HasComponent<PrefabRef>(edge)) continue;
                var prefab = manager.GetComponentData<PrefabRef>(edge).m_Prefab;
                if (!manager.HasComponent<PlaceableNetData>(prefab)) continue;
                var placement = manager.GetComponentData<PlaceableNetData>(prefab);
                if (((int)placement.m_PlacementFlags & 192) == 0) continue;
                var compositionEntity = manager.GetComponentData<Game.Net.Composition>(edge).m_Edge;
                if (!manager.HasComponent<NetCompositionData>(compositionEntity) || !manager.HasComponent<TerrainComposition>(compositionEntity)) continue;
                var composition = manager.GetComponentData<NetCompositionData>(compositionEntity);
                var ground = manager.GetComponentData<TerrainComposition>(compositionEntity);
                var curve = manager.GetComponentData<Game.Net.Curve>(edge);
                if (!math.isfinite(curve.m_Length) || curve.m_Length <= 0) continue;
                result.Length += curve.m_Length;
                result.Edges.Add(edge);
                // Same structural-height sampling as WaterPoweredInitializeSystem; this
                // reconstructs the default dam's capacity without using our normalized cache.
                var samples = math.clamp((int)math.round(curve.m_Length * water.scale.x), 1, 4096);
                var sum = 0.0;
                for (var i = 0; i < samples; i++)
                {
                    var p = Colossal.Mathematics.MathUtils.Position(curve.m_Bezier, (i + 0.5f) / samples);
                    p.y += composition.m_SurfaceHeight.min + ground.m_MaxHeightOffset.y;
                    sum += math.max(0, p.y - TerrainUtils.SampleHeight(ref terrain, p));
                }
                area += sum * curve.m_Length / samples;
                if (((int)composition.m_Flags.m_General & 26624) != 0) continue;
                for (var i = 0; i < 5; i++)
                {
                    var t = (i + 0.5f) / 5;
                    var p = Colossal.Mathematics.MathUtils.Position(curve.m_Bezier, t);
                    var tangent = Colossal.Mathematics.MathUtils.Tangent(curve.m_Bezier, t);
                    var normal = math.normalizesafe(new float2(tangent.z, -tangent.x));
                    if (((int)placement.m_PlacementFlags & 64) != 0) normal = -normal;
                    var upper = p; var lower = p;
                    upper.xz -= normal * composition.m_Width * 0.5f;
                    lower.xz += normal * composition.m_Width * 0.5f;
                    var h1 = WaterUtils.SampleHeight(ref water, ref terrain, upper, out float d1);
                    var h2 = WaterUtils.SampleHeight(ref water, ref terrain, lower, out float d2);
                    if (d1 > 0.05f && d2 > 0.05f && math.isfinite(h1) && math.isfinite(h2)) result.Head = math.max(result.Head, h1 - h2);
                }
            }
            result.NativeCapacity = System.Math.Min(area, 1000000) * nativeCapacityFactor;
            return result;
        }
    }
}
