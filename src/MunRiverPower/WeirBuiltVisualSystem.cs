using System.Collections.Generic;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MunRiverPower
{
    /// <summary>
    /// Keep the placed dam's render composition separate from hydraulic simulation.
    /// UIUpdate also runs while simulation is paused, immediately after placement.
    /// </summary>
    public partial class WeirBuiltVisualSystem : GameSystemBase
    {
        private PrefabSystem prefabs;
        private WeirEconomicProfilesSystem profiles;
        private WeirNetworkEconomy network;
        private EntityQuery buildings;
        private HashSet<Entity> loggedMeshes;

        protected override void OnCreate()
        {
            base.OnCreate();
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            profiles = World.GetOrCreateSystemManaged<WeirEconomicProfilesSystem>();
            network = new WeirNetworkEconomy(prefabs);
            loggedMeshes = new HashSet<Entity>();
            buildings = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.WaterPowered>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>(), ComponentType.ReadOnly<Game.Common.Deleted>() }
            });
        }

        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            network.Reset();
            loggedMeshes.Clear();
            base.OnGamePreload(purpose, mode);
        }

        protected override void OnUpdate()
        {
            network.BeginRefresh();
            DamSceneVisuals.Begin();
            using (var entities = buildings.ToEntityArray(Allocator.Temp))
            {
                foreach (var building in entities)
                {
                    var buildingPrefab = EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
                    if (!profiles.Owns(buildingPrefab) || !EntityManager.HasBuffer<Game.Net.SubNet>(building)) continue;
                    var ratio = profiles.Ratio(buildingPrefab);
                    var edges = new List<Entity>();
                    foreach (var item in EntityManager.GetBuffer<Game.Net.SubNet>(building, true))
                    {
                        var edge = item.m_SubNet;
                        if (!EntityManager.Exists(edge) || !EntityManager.HasComponent<Game.Net.Curve>(edge) ||
                            !EntityManager.HasComponent<Game.Net.Composition>(edge) || !EntityManager.HasComponent<PrefabRef>(edge) ||
                            EntityManager.HasComponent<Game.Common.Deleted>(edge)) continue;
                        var edgePrefab = EntityManager.GetComponentData<PrefabRef>(edge).m_Prefab;
                        if (!EntityManager.HasComponent<PlaceableNetData>(edgePrefab) ||
                            ((int)EntityManager.GetComponentData<PlaceableNetData>(edgePrefab).m_PlacementFlags & 192) == 0) continue;
                        edges.Add(edge);
                        network.Apply(EntityManager, edge, ratio);
                        if (!loggedMeshes.Contains(edge))
                        {
                            var composition = EntityManager.GetComponentData<Game.Net.Composition>(edge).m_Edge;
                            if (EntityManager.Exists(composition) && EntityManager.HasComponent<NetCompositionMeshRef>(composition))
                            {
                                var mesh = EntityManager.GetComponentData<NetCompositionMeshRef>(composition).m_Mesh;
                                if (mesh != Entity.Null && loggedMeshes.Add(edge))
                                    Mod.Log.Info($"BUILT DAM MESH READY: building={building}, edge={edge}, composition={composition}, mesh={mesh}.");
                            }
                        }
                    }
                    if (edges.Count != 0) DamSceneVisuals.Show(EntityManager, building, edges, prefabs);
                }
            }
            network.EndRefresh(EntityManager);
            DamSceneVisuals.End(EntityManager);
        }
    }
}
