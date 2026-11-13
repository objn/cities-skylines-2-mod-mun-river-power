using Game;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using BuildingWater = Game.Buildings.WaterPowered;

namespace MunRiverPower
{
    /// <summary>Rate turbines from the real span, then retain native hydraulic production.</summary>
    public partial class WeirRatedPowerSystem : GameSystemBase
    {
        private PrefabSystem prefabs;
        private WaterSystem water;
        private TerrainSystem terrain;
        private WeirEconomicProfilesSystem profiles;
        private WeirConstructionEconomySystem construction;
        private EntityQuery query;
        private bool logged;
        protected override void OnCreate()
        {
            base.OnCreate();
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            water = World.GetOrCreateSystemManaged<WaterSystem>();
            terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            profiles = World.GetOrCreateSystemManaged<WeirEconomicProfilesSystem>();
            construction = World.GetOrCreateSystemManaged<WeirConstructionEconomySystem>();
            query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<BuildingWater>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>(), ComponentType.ReadOnly<Game.Common.Deleted>(), ComponentType.ReadOnly<Game.Common.Created>() }
            });
        }
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 128;
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            DamSceneVisuals.Reset(); logged = false;
            base.OnGamePreload(purpose, mode);
        }
        protected override void OnUpdate()
        {
            if (WeirFactory.RegisteredProducer == null || !prefabs.TryGetEntity(WeirFactory.RegisteredProducer, out var modProducer) || !prefabs.TryGetEntity(WeirFactory.OriginalProducer, out var nativeProducer)) return;
            var nativeFactor = EntityManager.GetComponentData<WaterPoweredData>(nativeProducer).m_CapacityFactor;
            construction.BeginOwnedRefresh();
            using (var entities = query.ToEntityArray(Allocator.Temp))
            {
                var any = false;
                foreach (var entity in entities)
                    if (profiles.Owns(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab)) { any = true; break; }
                if (!any) return;
                var surface = water.GetSurfaceData(out var readDependency);
                readDependency.Complete();
                var heightData = terrain.GetHeightData();
                foreach (var entity in entities)
                {
                    var reference = EntityManager.GetComponentData<PrefabRef>(entity);
                    if (!profiles.Owns(reference.m_Prefab)) continue;
                    var measured = WeirGeometry.Measure(EntityManager, entity, ref surface, ref heightData, nativeFactor);
                    var head = measured.Head;
                    var installedMW = HydroSpecification.InstalledMW(measured.Length);
                    var ratio = HydroSpecification.PriceRatio(measured.Length, measured.NativeCapacity);
                    construction.SetOwnedRatio(entity, ratio);
                    foreach (var edge in measured.Edges)
                    {
                        construction.SetOwnedRatio(edge, ratio);
                    }
                    if (profiles.TryProfile(ratio, out var economicPrefab) && reference.m_Prefab != economicPrefab)
                    {
                        reference.m_Prefab = economicPrefab;
                        EntityManager.SetComponentData(entity, reference);
                    }
                    var efficiencyFactors = new float[128];
                    for (var i = 0; i < efficiencyFactors.Length; i++) efficiencyFactors[i] = 1;
                    if (EntityManager.HasBuffer<Game.Buildings.Efficiency>(entity))
                        foreach (var factor in EntityManager.GetBuffer<Game.Buildings.Efficiency>(entity, true))
                        {
                            // Same exclusions as PowerPlantAISystem's native factor array.
                            var index = (int)factor.m_Factor;
                            if (index >= 0 && index < efficiencyFactors.Length) efficiencyFactors[index] = factor.m_Efficiency;
                        }
                    var efficiency = HydroSpecification.OperationalEfficiency(efficiencyFactors);
                    var effectiveWaterData = EntityManager.GetComponentData<WaterPoweredData>(modProducer);
                    if (EntityManager.HasBuffer<Game.Buildings.InstalledUpgrade>(entity))
                        UpgradeUtils.CombineStats(EntityManager, ref effectiveWaterData, EntityManager.GetBuffer<Game.Buildings.InstalledUpgrade>(entity, true));
                    var state = EntityManager.GetComponentData<BuildingWater>(entity);
                    // These two cached fields are read by GetWaterCapacity only. Production
                    // still samples the actual SubNet curves, water velocity and water levels.
                    // This does not move or resize any geometry/terrain/network.
                    state.m_Length = (float)HydroSpecification.ReferenceLengthMetres;
                    state.m_Height = (float)HydroSpecification.CapacityHeight(efficiency, head, effectiveWaterData.m_CapacityFactor, installedMW);
                    EntityManager.SetComponentData(entity, state);
                    if (!logged)
                    {
                        logged = true;
                        Mod.Log.Info($"MODULAR POWER APPLIED: length={measured.Length:F2}m, turbines={HydroSpecification.InstalledTurbines(measured.Length)}, rating={installedMW} MW, defaultRating={measured.NativeCapacity / 10000:F2}MW, priceRatio={ratio:F6}, head={head:F2}m. Cut-in=3m; assumed hydraulic efficiency=90%.");
                    }
                }
            }
        }
        private float MaximumWetHead(Entity building, ref WaterSurfaceData<SurfaceWater> surface, ref TerrainHeightData terrainData)
        {
            if (!surface.isCreated || !terrainData.isCreated || surface.resolution.x < 2 || surface.resolution.z < 2) return 0;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(building)) return 0;
            var maximum = 0f;
            foreach (var subnet in EntityManager.GetBuffer<Game.Net.SubNet>(building, true))
            {
                var edge = subnet.m_SubNet;
                if (!EntityManager.Exists(edge) || !EntityManager.HasComponent<Game.Net.Curve>(edge) || !EntityManager.HasComponent<Game.Net.Composition>(edge) || !EntityManager.HasComponent<PrefabRef>(edge)) continue;
                var networkPrefab = EntityManager.GetComponentData<PrefabRef>(edge).m_Prefab;
                if (!EntityManager.HasComponent<PlaceableNetData>(networkPrefab)) continue;
                var placement = EntityManager.GetComponentData<PlaceableNetData>(networkPrefab);
                if (((int)placement.m_PlacementFlags & 192) == 0) continue;
                var compositionEntity = EntityManager.GetComponentData<Game.Net.Composition>(edge).m_Edge;
                if (!EntityManager.HasComponent<NetCompositionData>(compositionEntity)) continue;
                var composition = EntityManager.GetComponentData<NetCompositionData>(compositionEntity);
                if (((int)composition.m_Flags.m_General & 26624) != 0) continue;
                var curve = EntityManager.GetComponentData<Game.Net.Curve>(edge);
                // Five wet cross-sections per native edge; only the cut-in gate uses these
                // samples. Native AI retains its own full sampling for Q*H power production.
                for (var sample = 0; sample < 5; sample++)
                {
                    var t = (sample + 0.5f) / 5;
                    var position = Colossal.Mathematics.MathUtils.Position(curve.m_Bezier, t);
                    var tangent = Colossal.Mathematics.MathUtils.Tangent(curve.m_Bezier, t);
                    var normal = math.normalizesafe(new float2(tangent.z, -tangent.x));
                    if (((int)placement.m_PlacementFlags & 64) != 0) normal = -normal;
                    var upstream = position;
                    var downstream = position;
                    upstream.xz -= normal * composition.m_Width * 0.5f;
                    downstream.xz += normal * composition.m_Width * 0.5f;
                    var upper = WaterUtils.SampleHeight(ref surface, ref terrainData, upstream, out float upperDepth);
                    var lower = WaterUtils.SampleHeight(ref surface, ref terrainData, downstream, out float lowerDepth);
                    if (upperDepth > 0.05f && lowerDepth > 0.05f && math.isfinite(upper) && math.isfinite(lower))
                        maximum = math.max(maximum, upper - lower);
                }
            }
            return maximum;
        }
    }
}
