using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace MunRiverPower
{
    // Net.CostSystem produces the actual invoice; ToolApplySystem charges Temp.m_Cost.
    // Adjust that invoice, rather than displaying a discount the player never receives.
    public partial class WeirConstructionEconomySystem : GameSystemBase
    {
        private struct Invoice { internal int Native, Scaled; }
        private Dictionary<Entity, Invoice> invoices;
        private Dictionary<Entity, double> owned;
        private EntityQuery query;
        private PrefabSystem prefabs;
        private WeirNativePlacementSystem placement;
        private WaterSystem water;
        private TerrainSystem terrain;
        protected override void OnCreate()
        {
            base.OnCreate();
            invoices = new Dictionary<Entity, Invoice>(); owned = new Dictionary<Entity, double>();
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            placement = World.GetOrCreateSystemManaged<WeirNativePlacementSystem>();
            water = World.GetOrCreateSystemManaged<WaterSystem>(); terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            query = GetEntityQuery(ComponentType.ReadWrite<Temp>(), ComponentType.ReadOnly<PrefabRef>());
        }
        internal void BeginOwnedRefresh() => owned.Clear();
        internal void SetOwnedRatio(Entity entity, double ratio) => owned[entity] = ratio;
        internal void RestoreInvoices()
        {
            foreach (var item in invoices)
            {
                if (!EntityManager.Exists(item.Key) || !EntityManager.HasComponent<Temp>(item.Key)) continue;
                var temp = EntityManager.GetComponentData<Temp>(item.Key);
                if (temp.m_Cost != item.Value.Scaled) continue;
                temp.m_Cost = item.Value.Native;
                EntityManager.SetComponentData(item.Key, temp);
            }
            invoices.Clear();
        }
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            invoices.Clear(); owned.Clear();
            base.OnGamePreload(purpose, mode);
        }
        protected override void OnUpdate()
        {
            if (WeirFactory.OriginalProducer == null || !prefabs.TryGetEntity(WeirFactory.OriginalProducer, out var producer)) return;
            using (var entities = query.ToEntityArray(Allocator.Temp))
            {
                var active = placement.IsPlacementActive;
                var ratio = 1.0;
                if (active)
                {
                    var length = 0.0; var capacity = 0.0;
                    var surface = water.GetSurfaceData(out var dependency); dependency.Complete();
                    var height = terrain.GetHeightData();
                    var factor = EntityManager.GetComponentData<WaterPoweredData>(producer).m_CapacityFactor;
                    foreach (var entity in entities)
                    {
                        if (EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab != producer || !EntityManager.HasComponent<Game.Buildings.WaterPowered>(entity)) continue;
                        var measured = WeirGeometry.Measure(EntityManager, entity, ref surface, ref height, factor);
                        if (measured.Length > 0) { length += measured.Length; capacity += measured.NativeCapacity; }
                        else
                        {
                            var state = EntityManager.GetComponentData<Game.Buildings.WaterPowered>(entity);
                            length += state.m_Length;
                            capacity += System.Math.Min(state.m_Length * state.m_Height, 1000000) * factor;
                        }
                    }
                    ratio = HydroSpecification.PriceRatio(length, capacity);
                }
                var live = new HashSet<Entity>(entities.ToArray());
                foreach (var key in invoices.Keys.Where(key => !live.Contains(key)).ToArray()) invoices.Remove(key);
                foreach (var entity in entities)
                {
                    var temp = EntityManager.GetComponentData<Temp>(entity);
                    var asset = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                    var apply = owned.TryGetValue(temp.m_Original, out var priceRatio);
                    if (!apply && active && temp.m_Original == Entity.Null)
                    {
                        apply = asset == producer || (EntityManager.HasComponent<PlaceableNetData>(asset) && ((int)EntityManager.GetComponentData<PlaceableNetData>(asset).m_PlacementFlags & 192) != 0);
                        priceRatio = ratio;
                    }
                    if (!apply)
                    {
                        if (invoices.TryGetValue(entity, out var old) && temp.m_Cost == old.Scaled)
                        { temp.m_Cost = old.Native; EntityManager.SetComponentData(entity, temp); }
                        invoices.Remove(entity);
                        continue;
                    }
                    var baseline = invoices.TryGetValue(entity, out var cached) && temp.m_Cost == cached.Scaled ? cached.Native : temp.m_Cost;
                    var scaled = HydroSpecification.ScalePrice(baseline, priceRatio);
                    invoices[entity] = new Invoice { Native = baseline, Scaled = scaled };
                    if (temp.m_Cost != scaled) { temp.m_Cost = scaled; EntityManager.SetComponentData(entity, temp); }
                }
            }
        }
    }
    public partial class WeirConstructionRestoreSystem : GameSystemBase
    {
        private WeirConstructionEconomySystem economy;
        protected override void OnCreate()
        { base.OnCreate(); economy = World.GetOrCreateSystemManaged<WeirConstructionEconomySystem>(); }
        protected override void OnUpdate() => economy.RestoreInvoices();
    }
}
