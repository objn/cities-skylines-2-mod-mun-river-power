using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MunRiverPower
{
    internal static class FishPassageSpecification
    {
        internal const string Name = "MunRiverPower.FishPassage";
        internal const float Wellbeing = 2;
        internal const float RadiusMetres = 4000;
        // Game balance values, not a quotation for a real fish passage.
        internal const uint ConstructionCost = 25000;
        internal const int MonthlyUpkeep = 500;
    }

    /// <summary>A native internal building extension, purchased through the upgrade menu.</summary>
    public partial class FishPassageUpgradeSystem : GameSystemBase
    {
        internal BuildingExtensionPrefab Upgrade { get; private set; }
        private PrefabSystem prefabs;
        private EntityQuery producerPrefabs;
        protected override void OnCreate()
        {
            base.OnCreate();
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            producerPrefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<WaterPoweredData>());
        }
        protected override void OnUpdate()
        {
            if (Upgrade != null || !(WeirFactory.RegisteredProducer is BuildingPrefab main)) return;
            if (prefabs.TryGetPrefab(new PrefabID(nameof(BuildingExtensionPrefab), FishPassageSpecification.Name), out var existing))
            { Upgrade = (BuildingExtensionPrefab)existing; return; }
            var targets = new List<BuildingPrefab> { main };
            using (var entities = producerPrefabs.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                    if (prefabs.TryGetPrefab<BuildingPrefab>(entity, out var asset) && asset.name.StartsWith(WeirEconomicProfilesSystem.Prefix, StringComparison.Ordinal)) targets.Add(asset);

            var upgrade = PrefabBase.Create<BuildingExtensionPrefab>(FishPassageSpecification.Name);
            // Mesh loading is guarded so an optional visual can never stop PrefabUpdate.
            upgrade.m_Meshes = Array.Empty<ObjectMeshInfo>();
            upgrade.m_ExternalLot = false;
            var service = upgrade.AddComponent<ServiceUpgrade>();
            service.m_Buildings = targets.Distinct().ToArray();
            service.m_UpgradeCost = FishPassageSpecification.ConstructionCost;
            service.m_ForbidMultiple = true;
            service.m_XPReward = 0;
            var effects = upgrade.AddComponent<LocalEffects>();
            effects.m_Effects = new[]
            {
                new LocalEffectInfo
                {
                    m_Type = Game.Buildings.LocalModifierType.Wellbeing,
                    m_Mode = ModifierValueMode.Absolute,
                    m_Delta = FishPassageSpecification.Wellbeing,
                    m_Radius = FishPassageSpecification.RadiusMetres,
                    m_RadiusCombineMode = Game.Buildings.ModifierRadiusCombineMode.Maximal
                }
            };
            var consumption = upgrade.AddComponent<ServiceConsumption>();
            consumption.m_Upkeep = FishPassageSpecification.MonthlyUpkeep;
            consumption.m_ElectricityConsumption = 0;
            consumption.m_WaterConsumption = 0;
            consumption.m_GarbageAccumulation = 0;
            consumption.m_TelecomNeed = 0;
            var ui = upgrade.AddComponent<UIObject>();
            ui.m_Icon = "Media/Game/Icons/FishingLine.svg";
            ui.m_Priority = 10;
            ui.m_Group = null; // Listed under the owner's upgrades, not as a standalone building.
            Mod.RegisterText(new[] { upgrade });
            if (!prefabs.AddPrefab(upgrade)) throw new InvalidOperationException("Fish Passage upgrade registration failed.");
            Upgrade = upgrade;
            Mod.Log.Info("FISH PASSAGE REGISTERED: native internal extension; Wellbeing +2 absolute, radius 4000m at full efficiency; native distance falloff; one per powerhouse; price 25000, upkeep 500/month.");
        }
    }

    /// <summary>Keep both native upgrade indexes valid when a new price profile appears.</summary>
    public partial class FishPassageUpgradeLinkSystem : GameSystemBase
    {
        private FishPassageUpgradeSystem fish;
        private WeirEconomicProfilesSystem profiles;
        private PrefabSystem prefabs;
        private EntityQuery producerPrefabs;
        protected override void OnCreate()
        {
            base.OnCreate();
            fish = World.GetOrCreateSystemManaged<FishPassageUpgradeSystem>();
            profiles = World.GetOrCreateSystemManaged<WeirEconomicProfilesSystem>();
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            producerPrefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<WaterPoweredData>(), ComponentType.ReadOnly<ObjectData>());
        }
        protected override void OnUpdate()
        {
            if (fish.Upgrade == null || !prefabs.TryGetEntity(fish.Upgrade, out var upgrade) || !EntityManager.HasBuffer<ServiceUpgradeBuilding>(upgrade)) return;
            var service = fish.Upgrade.GetComponent<ServiceUpgrade>();
            var targets = new List<BuildingPrefab>(service.m_Buildings ?? Array.Empty<BuildingPrefab>());
            using (var entities = producerPrefabs.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    if (!profiles.Owns(entity) || !prefabs.TryGetPrefab<BuildingPrefab>(entity, out var asset)) continue;
                    if (!EntityManager.GetComponentData<ObjectData>(entity).m_Archetype.Valid) continue;
                    // AddUpgrade creates the InstalledUpgrade-compatible native archetype
                    // when this profile did not exist during the upgrade's initialization.
                    if (!EntityManager.HasBuffer<BuildingUpgradeElement>(entity)) asset.AddUpgrade(EntityManager, service);
                    var choices = EntityManager.GetBuffer<BuildingUpgradeElement>(entity);
                    var found = false;
                    foreach (var item in choices) if (item.m_Upgrade == upgrade) { found = true; break; }
                    if (!found) choices.Add(new BuildingUpgradeElement(upgrade));
                    var parents = EntityManager.GetBuffer<ServiceUpgradeBuilding>(upgrade);
                    found = false;
                    foreach (var item in parents) if (item.m_Building == entity) { found = true; break; }
                    if (!found) parents.Add(new ServiceUpgradeBuilding(entity));
                    if (!targets.Contains(asset)) targets.Add(asset);
                }
            service.m_Buildings = targets.ToArray();
        }
    }
}
