using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MunRiverPower
{
    public struct SpillwayGateState : IComponentData, ISerializable
    {
        public bool Open;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(Open);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out Open);
    }
    public sealed class SpillwayStateAuthoring : ComponentBase
    {
        public override void GetPrefabComponents(HashSet<ComponentType> components) { }
        public override void GetArchetypeComponents(HashSet<ComponentType> components) => components.Add(ComponentType.ReadWrite<SpillwayGateState>());
    }
    public partial class SpillwayUpgradeSystem : GameSystemBase
    {
        internal BuildingExtensionPrefab Upgrade { get; private set; }
        private PrefabSystem prefabs;
        protected override void OnCreate() { base.OnCreate(); prefabs = World.GetOrCreateSystemManaged<PrefabSystem>(); }
        protected override void OnUpdate()
        {
            if (Upgrade != null || !(WeirFactory.RegisteredProducer is BuildingPrefab main)) return;
            if (prefabs.TryGetPrefab(new PrefabID(nameof(BuildingExtensionPrefab), SpillwayControl.UpgradeName), out var existing))
            { Upgrade = (BuildingExtensionPrefab)existing; return; }
            var upgrade = PrefabBase.Create<BuildingExtensionPrefab>(SpillwayControl.UpgradeName);
            upgrade.m_Meshes = Array.Empty<ObjectMeshInfo>();
            upgrade.m_ExternalLot = false;
            upgrade.AddComponent<SpillwayStateAuthoring>();
            var service = upgrade.AddComponent<ServiceUpgrade>();
            service.m_Buildings = new[] { main };
            service.m_ForbidMultiple = true;
            service.m_UpgradeCost = SpillwayControl.UpgradeCost;
            var consumption = upgrade.AddComponent<ServiceConsumption>();
            consumption.m_Upkeep = SpillwayControl.MonthlyUpkeep;
            consumption.m_ElectricityConsumption = consumption.m_WaterConsumption = consumption.m_GarbageAccumulation = 0;
            consumption.m_TelecomNeed = 0;
            var ui = upgrade.AddComponent<UIObject>();
            ui.m_Icon = "Media/Game/Icons/Dam.svg";
            ui.m_Priority = 20;
            Mod.RegisterText(new[] { upgrade });
            if (!prefabs.AddPrefab(upgrade)) throw new InvalidOperationException("Automatic Spillway registration failed.");
            Upgrade = upgrade;
            Mod.Log.Info("AUTOMATIC SPILLWAY REGISTERED: open freeboard <=0.5m; close >=1m; native paired additive GPU transfer; price 100000, upkeep 1000/month. Crest gates and level sensor mesh attached.");
        }
    }
    public partial class SpillwayUpgradeLinkSystem : GameSystemBase
    {
        private SpillwayUpgradeSystem spillway;
        private WeirEconomicProfilesSystem profiles;
        private PrefabSystem prefabs;
        private EntityQuery query;
        protected override void OnCreate()
        {
            base.OnCreate();
            spillway = World.GetOrCreateSystemManaged<SpillwayUpgradeSystem>();
            profiles = World.GetOrCreateSystemManaged<WeirEconomicProfilesSystem>();
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            query = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<WaterPoweredData>(), ComponentType.ReadOnly<ObjectData>());
        }
        protected override void OnUpdate()
        {
            if (spillway.Upgrade == null || !prefabs.TryGetEntity(spillway.Upgrade, out var upgrade) || !EntityManager.HasBuffer<ServiceUpgradeBuilding>(upgrade)) return;
            var service = spillway.Upgrade.GetComponent<ServiceUpgrade>();
            var targets = new List<BuildingPrefab>(service.m_Buildings ?? Array.Empty<BuildingPrefab>());
            using (var entities = query.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    if (!profiles.Owns(entity) || !prefabs.TryGetPrefab<BuildingPrefab>(entity, out var asset) || !EntityManager.GetComponentData<ObjectData>(entity).m_Archetype.Valid) continue;
                    if (!EntityManager.HasBuffer<BuildingUpgradeElement>(entity)) asset.AddUpgrade(EntityManager, service);
                    var choices = EntityManager.GetBuffer<BuildingUpgradeElement>(entity);
                    var choiceFound = false;
                    foreach (var item in choices) if (item.m_Upgrade == upgrade) { choiceFound = true; break; }
                    if (!choiceFound) choices.Add(new BuildingUpgradeElement(upgrade));
                    var parents = EntityManager.GetBuffer<ServiceUpgradeBuilding>(upgrade);
                    var found = false;
                    foreach (var item in parents) if (item.m_Building == entity) { found = true; break; }
                    if (!found) parents.Add(new ServiceUpgradeBuilding(entity));
                    if (!targets.Contains(asset)) targets.Add(asset);
                }
            service.m_Buildings = targets.ToArray();
        }
    }
}
