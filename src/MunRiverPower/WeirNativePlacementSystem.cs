using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace MunRiverPower
{
    /// <summary>Use the unmodified native dam for placement, then specialize only new producers.</summary>
    public partial class WeirNativePlacementSystem : GameSystemBase
    {
        private PrefabSystem prefabs;
        private ToolSystem tools;
        private DefaultToolSystem defaultTool;
        private EntityQuery producers;
        private PlacementLedger<Entity> ledger;
        private bool redirecting;
        private HashSet<Entity> knownMod;
        private WeirEconomicProfilesSystem economicProfiles;
        internal bool IsPlacementActive => ledger != null && ledger.Active;
        protected override void OnCreate()
        {
            base.OnCreate();
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            tools = World.GetOrCreateSystemManaged<ToolSystem>();
            defaultTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            ledger = new PlacementLedger<Entity>();
            knownMod = new HashSet<Entity>();
            economicProfiles = World.GetOrCreateSystemManaged<WeirEconomicProfilesSystem>();
            producers = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.WaterPowered>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Game.Common.Deleted>() }
            });
            tools.EventPrefabChanged += PrefabChanged;
        }
        protected override void OnDestroy()
        {
            if (tools != null) tools.EventPrefabChanged -= PrefabChanged;
            base.OnDestroy();
        }
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            // Never carry a placement snapshot or unfinished conversion into another save.
            ledger?.Reset();
            knownMod?.Clear();
            redirecting = false;
            base.OnGamePreload(purpose, mode);
        }
        protected override void OnUpdate()
        {
            RememberModInstances();
            if (ledger.Active)
            {
                var current = Snapshot();
                if (tools.activePrefab != WeirFactory.RegisteredRoot) ledger.End(current);
                else ledger.Observe(current);
            }
            ConvertPending();
        }
        private void RememberModInstances()
        {
            if (WeirFactory.RegisteredProducer == null || !prefabs.TryGetEntity(WeirFactory.RegisteredProducer, out var replacement) || !prefabs.TryGetEntity(WeirFactory.OriginalProducer, out var original)) return;
            using (var entities = producers.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    var reference = EntityManager.GetComponentData<PrefabRef>(entity);
                    if (economicProfiles.Owns(reference.m_Prefab)) knownMod.Add(entity);
                    // Native editing can reuse an existing entity and reset its prefab.
                    // Preserve previously identified mod instances, never stock dams.
                    else if (knownMod.Contains(entity) && reference.m_Prefab == original && !EntityManager.HasComponent<Game.Common.Created>(entity))
                    { reference.m_Prefab = replacement; EntityManager.SetComponentData(entity, reference); }
                }
            knownMod.RemoveWhere(entity => !EntityManager.Exists(entity));
        }
        private void PrefabChanged(PrefabBase selected)
        {
            if (redirecting) return;
            if (ledger.Active) ledger.End(Snapshot());
            if (selected?.name != WeirFactory.RootName) return;
            try
            {
                ValidateNativePlacement();
                ledger.Begin(Snapshot());
                Mod.Log.Info("PLACEMENT USES MOD DAM PREFAB: " + WeirFactory.RegisteredRoot.GetPrefabID());
            }
            catch (Exception error)
            {
                ledger.End(Snapshot());
                tools.activeTool = defaultTool;
                Mod.Log.Error(error, "PLACEMENT BLOCKED before native tool update.");
            }
            finally { redirecting = false; }
        }
        private void ValidateNativePlacement()
        {
            if (WeirFactory.OriginalRoot == null || !prefabs.TryGetEntity(WeirFactory.RegisteredRoot, out var root))
                throw new InvalidOperationException("Native dam entity missing.");
            var data = EntityManager.GetComponentData<NetData>(root);
            var geometry = EntityManager.GetComponentData<NetGeometryData>(root);
            if (!data.m_NodeArchetype.Valid || !data.m_EdgeArchetype.Valid || !geometry.m_NodeCompositionArchetype.Valid || !geometry.m_EdgeCompositionArchetype.Valid)
                throw new InvalidOperationException("Native dam placement data is not initialized.");
            if (!prefabs.TryGetEntity(WeirFactory.RegisteredProducer, out var producer) || !EntityManager.HasComponent<WaterPoweredData>(producer) || !EntityManager.GetComponentData<ObjectData>(producer).m_Archetype.Valid)
                throw new InvalidOperationException("Mod producer data is not initialized.");
            if (WeirFactory.OriginalProducer == null || !prefabs.TryGetEntity(WeirFactory.OriginalProducer, out var originalProducer))
                throw new InvalidOperationException("Native producer entity missing.");
            // We only swap data references after construction; component layouts must match.
            var originalArchetype = EntityManager.GetComponentData<ObjectData>(originalProducer).m_Archetype;
            var modArchetype = EntityManager.GetComponentData<ObjectData>(producer).m_Archetype;
            using (var originalTypes = originalArchetype.GetComponentTypes())
            using (var modTypes = modArchetype.GetComponentTypes())
            {
                var nativeLayout = originalTypes.ToArray();
                var modLayout = modTypes.ToArray();
                if (!new HashSet<ComponentType>(nativeLayout).SetEquals(modLayout))
                {
                    Mod.Log.Info("PRODUCER LAYOUT native-only=[" + Describe(nativeLayout.Except(modLayout)) + "] mod-only=[" + Describe(modLayout.Except(nativeLayout)) + "]");
                    var nativeDeclared = DeclaredLayout(WeirFactory.OriginalProducer);
                    var modDeclared = DeclaredLayout(WeirFactory.RegisteredProducer);
                    if (!LayoutCompatibility<ComponentType>.CanAdoptNative(nativeDeclared, modDeclared, nativeLayout, modLayout))
                        throw new InvalidOperationException("Producer layouts differ and cannot be reconciled safely. See PRODUCER LAYOUT in this log.");
                    // The stock builder created the real instance. Preserve that builder's
                    // complete runtime contract, including components added during late
                    // initialization (e.g. upgrades). Only the mod prefab's cached ObjectData
                    // changes; the native prefab and existing buildings remain untouched.
                    EntityManager.SetComponentData(producer, EntityManager.GetComponentData<ObjectData>(originalProducer));
                    Mod.Log.Info("PRODUCER LAYOUT SYNCHRONIZED: identical authored component contracts; copied the native baked ObjectData to the mod prefab.");
                    using (var verified = EntityManager.GetComponentData<ObjectData>(producer).m_Archetype.GetComponentTypes())
                        if (!new HashSet<ComponentType>(nativeLayout).SetEquals(verified.ToArray()))
                            throw new InvalidOperationException("Producer layout verification failed after synchronization.");
                }
            }
        }
        private static HashSet<ComponentType> DeclaredLayout(PrefabBase prefab)
        {
            var components = new List<ComponentBase>();
            prefab.GetComponents(components);
            var result = new HashSet<ComponentType>();
            foreach (var component in components) component.GetArchetypeComponents(result);
            return result;
        }
        private static string Describe(IEnumerable<ComponentType> types) => string.Join(",", types.Select(type => type.GetManagedType()?.FullName ?? ("TypeIndex:" + type.TypeIndex)));
        private List<Entity> Snapshot()
        {
            var result = new List<Entity>();
            using (var entities = producers.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities) result.Add(entity);
            return result;
        }
        private void ConvertPending()
        {
            if (WeirFactory.OriginalProducer == null || WeirFactory.RegisteredProducer == null) return;
            if (!prefabs.TryGetEntity(WeirFactory.OriginalProducer, out var original) || !prefabs.TryGetEntity(WeirFactory.RegisteredProducer, out var replacement)) return;
            foreach (var entity in ledger.Pending)
            {
                if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Game.Common.Deleted>(entity) || !EntityManager.HasComponent<PrefabRef>(entity))
                { ledger.Complete(entity); continue; }
                if (EntityManager.HasComponent<Game.Common.Created>(entity)) continue;
                var reference = EntityManager.GetComponentData<PrefabRef>(entity);
                if (reference.m_Prefab != original) { ledger.Complete(entity); continue; }
                if (!EntityManager.HasComponent<Game.Buildings.ElectricityProducer>(entity)) continue;
                // No network prefab, geometry, cached batches or construction components change.
                // Identical component layouts and visual references were checked at selection.
                reference.m_Prefab = replacement;
                EntityManager.SetComponentData(entity, reference);
                ledger.Complete(entity);
                Mod.Log.Info("NEW PRODUCER SPECIALIZED: " + entity + "; native dam network retained.");
            }
        }
    }
}
