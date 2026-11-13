using System;
using System.Collections.Generic;
using Game;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace MunRiverPower
{
    internal struct SpillwayReport { internal SpillwayReading Reading; internal SpillwayStatus Status; }
    public partial class SpillwaySimulationSystem : GameSystemBase, IGPUSystem
    {
        private WaterSystem water;
        private TerrainSystem terrain;
        private SimulationSystem simulation;
        private WeirEconomicProfilesSystem profiles;
        private SpillwayUpgradeSystem upgradeSystem;
        private PrefabSystem prefabs;
        private EntityQuery query;
        private SpillwayWaterAdapter adapter;
        private Dictionary<Entity, SpillwayReport> reports;
        private List<KeyValuePair<Entity, SpillwayTransfer>> pending;
        private FreshWaterReadback readbacks;
        private bool loggedFailure, loggedTransfer, loggedReading, waterFaulted;
        bool IGPUSystem.Enabled => Mod.IsLoaded && Enabled && water != null && water.Loaded && simulation.selectedSpeed > 0 && pending.Count > 0;
        public bool IsAsync { get => water != null && water.IsAsync; set { } }
        protected override void OnCreate()
        {
            base.OnCreate();
            water = World.GetOrCreateSystemManaged<WaterSystem>(); terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            profiles = World.GetOrCreateSystemManaged<WeirEconomicProfilesSystem>(); upgradeSystem = World.GetOrCreateSystemManaged<SpillwayUpgradeSystem>();
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            adapter = new SpillwayWaterAdapter(); readbacks = new FreshWaterReadback(); reports = new Dictionary<Entity, SpillwayReport>(); pending = new List<KeyValuePair<Entity, SpillwayTransfer>>();
            query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.WaterPowered>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>(), ComponentType.ReadOnly<Game.Common.Deleted>(), ComponentType.ReadOnly<Game.Common.Destroyed>(), ComponentType.ReadOnly<Game.Common.Created>() }
            });
        }
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        { pending.Clear(); reports.Clear(); readbacks.Reset(); loggedFailure = loggedTransfer = loggedReading = waterFaulted = false; base.OnGamePreload(purpose, mode); }
        protected override void OnDestroy()
        { pending?.Clear(); waterFaulted = true; base.OnDestroy(); }
        private bool Supported()
        {
            if (waterFaulted) return false;
            try { return water.Loaded && adapter.Bind(water); }
            catch (Exception error)
            {
                waterFaulted = true;
                if (!loggedFailure) { loggedFailure = true; Mod.Log.Error(error, "Spillway water adapter unavailable. Normal hydro generation is unchanged; extra release disabled."); }
                return false;
            }
        }
        private Entity FindUpgrade(Entity building, out bool enabled)
        {
            enabled = true;
            if (EntityManager.HasComponent<Game.Buildings.Building>(building) && Game.Buildings.BuildingUtils.CheckOption(EntityManager.GetComponentData<Game.Buildings.Building>(building), Game.Buildings.BuildingOption.Inactive)) enabled = false;
            if (upgradeSystem.Upgrade == null || !prefabs.TryGetEntity(upgradeSystem.Upgrade, out var prefab) || !EntityManager.HasBuffer<Game.Buildings.InstalledUpgrade>(building)) return Entity.Null;
            foreach (var installed in EntityManager.GetBuffer<Game.Buildings.InstalledUpgrade>(building, true))
            {
                var entity = installed.m_Upgrade;
                if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Game.Common.Deleted>(entity) || EntityManager.HasComponent<Game.Common.Created>(entity) || !EntityManager.HasComponent<PrefabRef>(entity) || EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab != prefab) continue;
                if (Game.Buildings.BuildingUtils.CheckOption(installed, Game.Buildings.BuildingOption.Inactive)) enabled = false;
                if (EntityManager.HasComponent<Game.Buildings.Extension>(entity) && (EntityManager.GetComponentData<Game.Buildings.Extension>(entity).m_Flags & Game.Buildings.ExtensionFlags.Disabled) != 0) enabled = false;
                return entity;
            }
            return Entity.Null;
        }
        internal bool TryReport(Entity building, out SpillwayReport report)
        {
            report = default;
            if (!Mod.IsLoaded) return false;
            if (!EntityManager.Exists(building) || !EntityManager.HasComponent<PrefabRef>(building)) return false;
            if (!profiles.Owns(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab))
            {
                if (!EntityManager.HasComponent<Game.Common.Owner>(building)) return false;
                building = EntityManager.GetComponentData<Game.Common.Owner>(building).m_Owner;
                if (!EntityManager.Exists(building) || !EntityManager.HasComponent<PrefabRef>(building) || !profiles.Owns(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab)) return false;
            }
            // Also refresh the selected building while paused; never transfer water here.
            var surface = water.GetSurfaceData(out var dependency); dependency.Complete();
            var height = terrain.GetHeightData();
            var reading = SpillwayProbe.Measure(EntityManager, building, ref surface, ref height, water.CellSize, terrain.positionOffset);
            if (reading.Valid && !loggedReading)
            {
                loggedReading = true;
                Mod.Log.Info($"SPILLWAY SENSOR: freeboard={reading.Freeboard:F3}m, crest={reading.Crest:F3}m, upstream={reading.Upstream:F3}m, downstream={reading.Downstream:F3}m, engine={water.WaterSimulation?.GetType().Name}. Structural crest estimate; compare with visible overtopping in-game.");
            }
            var upgrade = FindUpgrade(building, out var enabled);
            var status = upgrade == Entity.Null ? SpillwayStatus.Monitoring : !enabled ? SpillwayStatus.Disabled : !reading.Valid ? SpillwayStatus.NoWater : !Supported() ? SpillwayStatus.Unsupported
                : reports.TryGetValue(building, out var previous) ? previous.Status : SpillwayStatus.Waiting;
            report = new SpillwayReport { Reading = reading, Status = status };
            return true;
        }
        protected override void OnUpdate()
        {
            if (!Mod.IsLoaded || !water.Loaded) { pending.Clear(); return; }
            var stamp = adapter.ReadbackFrame(water);
            if (!readbacks.TryConsume(stamp)) return;
            pending.Clear();
            var supported = Supported();
            var surface = water.GetSurfaceData(out var dependency); dependency.Complete();
            var height = terrain.GetHeightData();
            var used = new HashSet<int2>();
            var live = new HashSet<Entity>();
            using (var entities = query.ToEntityArray(Allocator.Temp))
                foreach (var building in entities)
                {
                    if (!profiles.Owns(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab)) continue;
                    live.Add(building);
                    var reading = SpillwayProbe.Measure(EntityManager, building, ref surface, ref height, water.CellSize, terrain.positionOffset);
                    var upgrade = FindUpgrade(building, out var enabled);
                    var report = new SpillwayReport { Reading = reading, Status = SpillwayStatus.Monitoring };
                    if (upgrade != Entity.Null)
                    {
                        var state = EntityManager.HasComponent<SpillwayGateState>(upgrade) ? EntityManager.GetComponentData<SpillwayGateState>(upgrade) : default;
                        var next = SpillwayControl.NextOpen(state.Open, reading.Freeboard, enabled, reading.Valid && supported);
                        if (state.Open != next)
                        {
                            state.Open = next;
                            if (EntityManager.HasComponent<SpillwayGateState>(upgrade)) EntityManager.SetComponentData(upgrade, state);
                            else EntityManager.AddComponentData(upgrade, state);
                            Mod.Log.Info($"SPILLWAY GATE {(next ? "OPEN" : "CLOSED")}: {building}, freeboard={reading.Freeboard:F3}m, upstream={reading.Upstream:F2}m, crest={reading.Crest:F2}m.");
                        }
                        report.Status = !enabled ? SpillwayStatus.Disabled : !reading.Valid ? SpillwayStatus.NoWater : !supported ? SpillwayStatus.Unsupported : next ? SpillwayStatus.Waiting : SpillwayStatus.Closed;
                        if (next && reading.Upstream <= reading.Downstream + 0.1f) report.Status = SpillwayStatus.Blocked;
                        if (next && reading.Upstream > reading.Downstream + 0.1f && SpillwayProbe.WetFootprints(ref surface, ref height, reading, water.CellSize, out var minimum))
                        {
                            var depth = (float)SpillwayControl.RemovalDepth(true, reading.Freeboard, minimum);
                            // Reserve footprints globally so adjacent structures cannot debit
                            // the same water pixels twice against one CPU readback.
                            var cells = FootprintCells(reading, water.CellSize, terrain.positionOffset);
                            var available = true;
                            foreach (var cell in cells) if (used.Contains(cell)) { available = false; break; }
                            if (available && depth > 0)
                            {
                                foreach (var cell in cells) used.Add(cell);
                                pending.Add(new KeyValuePair<Entity, SpillwayTransfer>(building, new SpillwayTransfer { Reading = reading, Depth = depth }));
                                report.Status = SpillwayStatus.Releasing;
                            }
                        }
                    }
                    reports[building] = report;
                }
            var stale = new List<Entity>(); foreach (var item in reports) if (!live.Contains(item.Key)) stale.Add(item.Key);
            foreach (var entity in stale) reports.Remove(entity);
        }
        private static List<int2> FootprintCells(SpillwayReading reading, float cell, float3 origin)
        {
            var result = new List<int2>(); var steps = (int)math.ceil(reading.Radius / cell) + 1;
            foreach (var center in new[] { reading.Intake, reading.Outlet })
            {
                var grid = (int2)math.floor((center.xz - origin.xz) / cell);
                for (var x = -steps; x <= steps; x++) for (var z = -steps; z <= steps; z++) result.Add(grid + new int2(x, z));
            }
            return result;
        }
        public void OnSimulateGPU(CommandBuffer commands)
        {
            if (!Mod.IsLoaded || simulation.selectedSpeed <= 0) { pending.Clear(); return; }
            try
            {
                foreach (var item in pending)
                {
                    // A purchase can be toggled or removed between preparing and submission.
                    if (!EntityManager.Exists(item.Key) || FindUpgrade(item.Key, out var enabled) == Entity.Null || !enabled) continue;
                    if (!adapter.Append(commands, water, terrain.positionOffset, item.Value))
                    {
                        if (reports.TryGetValue(item.Key, out var failed)) { failed.Status = SpillwayStatus.Waiting; reports[item.Key] = failed; }
                        continue;
                    }
                    if (reports.TryGetValue(item.Key, out var report)) { report.Status = SpillwayStatus.Releasing; reports[item.Key] = report; }
                    if (!loggedTransfer)
                    { loggedTransfer = true; Mod.Log.Info($"SPILLWAY TRANSFER SUBMITTED: equal/opposite native additive kernel pulses, depth={item.Value.Depth:F4}m, radius={item.Value.Reading.Radius:F1}m. Physical water levels must be verified in-game."); }
                }
            }
            catch (Exception error)
            {
                waterFaulted = true;
                if (!loggedFailure) { loggedFailure = true; Mod.Log.Error(error, "Automatic Spillway transfer stopped; water binding must be checked."); }
            }
            finally { pending.Clear(); }
        }
    }
}
