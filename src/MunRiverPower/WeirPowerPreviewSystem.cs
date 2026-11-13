using System.Collections.Generic;
using Game;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using WaterPowered = Game.Buildings.WaterPowered;

namespace MunRiverPower
{
    /// <summary>
    /// Adapt only the tooltip's input cache. Prefabs, tool placement and permanent
    /// building data retain their native values; a paired system restores the cache.
    /// </summary>
    public partial class WeirPowerPreviewSystem : GameSystemBase
    {
        private struct Snapshot { internal WaterPowered Original, Display; }
        private Dictionary<Entity, Snapshot> snapshots;
        private EntityQuery query;
        private PrefabSystem prefabs;
        private WeirNativePlacementSystem placement;
        private WeirEconomicProfilesSystem profiles;
        private WaterSystem water;
        private TerrainSystem terrain;
        private WeirNetworkEconomy previewNetwork;
        private bool logged;
        protected override void OnCreate()
        {
            base.OnCreate();
            snapshots = new Dictionary<Entity, Snapshot>();
            query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<WaterPowered>(), ComponentType.ReadOnly<Game.Tools.Temp>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Game.Common.Deleted>() }
            });
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            placement = World.GetOrCreateSystemManaged<WeirNativePlacementSystem>();
            profiles = World.GetOrCreateSystemManaged<WeirEconomicProfilesSystem>();
            water = World.GetOrCreateSystemManaged<WaterSystem>();
            terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            previewNetwork = new WeirNetworkEconomy(prefabs);
        }
        protected override void OnUpdate()
        {
            Restore();
            previewNetwork.BeginRefresh();
            if (WeirFactory.OriginalProducer == null || !prefabs.TryGetEntity(WeirFactory.OriginalProducer, out var nativeProducer)) return;
            var nativeData = EntityManager.GetComponentData<WaterPoweredData>(nativeProducer);
            using (var entities = query.ToEntityArray(Allocator.Temp))
            {
                if (entities.Length == 0) { previewNetwork.EndRefresh(EntityManager); return; }
                var surface = water.GetSurfaceData(out var dependency); dependency.Complete();
                var height = terrain.GetHeightData();
                foreach (var entity in entities)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                    var temp = EntityManager.GetComponentData<Game.Tools.Temp>(entity);
                    // Match the native tooltip's create/modify/upgrade flags.
                    if (((int)temp.m_Flags & 577) == 0) continue;
                    var isMod = profiles.Owns(prefab) || (placement.IsPlacementActive && prefab == nativeProducer);
                    if (!isMod && EntityManager.Exists(temp.m_Original) && EntityManager.HasComponent<PrefabRef>(temp.m_Original))
                        isMod = profiles.Owns(EntityManager.GetComponentData<PrefabRef>(temp.m_Original).m_Prefab);
                    if (!isMod || !EntityManager.HasComponent<WaterPoweredData>(prefab)) continue;
                    var data = EntityManager.GetComponentData<WaterPoweredData>(prefab);
                    if (data.m_CapacityFactor <= 0) continue;
                    var measured = WeirGeometry.Measure(EntityManager, entity, ref surface, ref height, nativeData.m_CapacityFactor);
                    if (temp.m_Original == Entity.Null)
                        foreach (var edge in measured.Edges) previewNetwork.Apply(EntityManager, edge, 1);
                    DamSceneVisuals.Show(EntityManager, entity, measured.Edges, prefabs);
                    var original = EntityManager.GetComponentData<WaterPowered>(entity);
                    // Native initialization is authoritative when the temporary SubNet
                    // has not arrived yet. Never use foundation depth to count turbines.
                    var length = measured.Length > 0 ? measured.Length : original.m_Length;
                    var installedMW = HydroSpecification.InstalledMW(length);
                    var production = HydroSpecification.PreviewProductionGameUnits(length, measured.Head, original.m_Estimate);
                    var display = original;
                    display.m_Length = (float)HydroSpecification.ReferenceLengthMetres;
                    // The denominator shows installed rating even when head is too low;
                    // the numerator then shows zero. Staffing is unknown before building.
                    display.m_Height = (float)HydroSpecification.CapacityHeight(1, HydroSpecification.MinimumHeadMetres, data.m_CapacityFactor, installedMW);
                    display.m_Estimate = data.m_ProductionFactor > 0 ? (float)(production / data.m_ProductionFactor) : 0;
                    snapshots[entity] = new Snapshot { Original = original, Display = display };
                    EntityManager.SetComponentData(entity, display);
                    if (!logged)
                    {
                        logged = true;
                        Mod.Log.Info($"MODULAR PREVIEW APPLIED: length={length:F2}m, turbines={HydroSpecification.InstalledTurbines(length)}, estimated={production / 10000:F2}MW, installed={installedMW}MW, head={measured.Head:F2}m; temporary tooltip cache only.");
                    }
                }
            }
            previewNetwork.EndRefresh(EntityManager);
        }
        internal void Restore()
        {
            foreach (var item in snapshots)
            {
                if (!EntityManager.Exists(item.Key) || !EntityManager.HasComponent<Game.Tools.Temp>(item.Key) || !EntityManager.HasComponent<WaterPowered>(item.Key)) continue;
                var current = EntityManager.GetComponentData<WaterPowered>(item.Key);
                var display = item.Value.Display;
                // Do not overwrite a newer native initialization if the pipeline changed.
                if (current.m_Length.Equals(display.m_Length) && current.m_Height.Equals(display.m_Height) && current.m_Estimate.Equals(display.m_Estimate))
                    EntityManager.SetComponentData(item.Key, item.Value.Original);
            }
            snapshots.Clear();
        }
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        { Restore(); logged = false; base.OnGamePreload(purpose, mode); }
        protected override void OnDestroy() { Restore(); base.OnDestroy(); }
    }
    public partial class WeirPowerPreviewRestoreSystem : GameSystemBase
    {
        private WeirPowerPreviewSystem preview;
        protected override void OnCreate() { base.OnCreate(); preview = World.GetOrCreateSystemManaged<WeirPowerPreviewSystem>(); }
        protected override void OnUpdate() => preview.Restore();
    }
}
