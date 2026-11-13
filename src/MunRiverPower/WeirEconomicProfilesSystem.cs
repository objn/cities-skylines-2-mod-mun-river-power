using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using Game.Prefabs;
using Unity.Entities;
using UnityEngine;

namespace MunRiverPower
{
    [Serializable] internal sealed class EconomicProfileCatalog { public int[] ratios = Array.Empty<int>(); }
    public partial class WeirEconomicProfilesSystem : GameSystemBase
    {
        internal const string Prefix = "MunRiverPower.Economic.";
        private PrefabSystem prefabs;
        private HashSet<int> requested;
        private Dictionary<int, PrefabBase> profiles;
        private string catalogPath;
        protected override void OnCreate()
        {
            base.OnCreate();
            prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            requested = new HashSet<int>(); profiles = new Dictionary<int, PrefabBase>();
            catalogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Colossal Order", "Cities Skylines II", "ModsData", "MunRiverPower", "economic-profiles.json");
            try
            {
                if (File.Exists(catalogPath))
                    foreach (var key in JsonUtility.FromJson<EconomicProfileCatalog>(File.ReadAllText(catalogPath)).ratios)
                        if (key > 0 && key < 1000000) requested.Add(key);
            }
            catch (Exception error) { Mod.Log.Error(error, "Economic profile catalog could not be read; retain this file when moving saves."); }
        }
        protected override void OnUpdate()
        {
            if (WeirFactory.RegisteredProducer == null) return;
            foreach (var key in requested.ToArray())
            {
                if (profiles.ContainsKey(key)) continue;
                var profile = WeirFactory.RegisteredProducer.Clone(Prefix + key);
                profile.Remove<ObsoleteIdentifiers>();
                var ratio = key / 1000000.0;
                if (profile.TryGet<ServiceConsumption>(out var consumption)) consumption.m_Upkeep = HydroSpecification.ScalePrice(consumption.m_Upkeep, ratio);
                if (!prefabs.AddPrefab(profile)) throw new InvalidOperationException("Could not register upkeep profile " + key);
                profiles.Add(key, profile);
                Mod.RegisterText(new[] { profile });
            }
        }
        internal bool Owns(Entity prefab)
        {
            if (prefabs.TryGetPrefab<PrefabBase>(prefab, out var asset))
                return asset == WeirFactory.RegisteredProducer || asset.name.StartsWith(Prefix, StringComparison.Ordinal);
            return false;
        }
        internal double Ratio(Entity prefab)
        {
            if (!prefabs.TryGetPrefab<PrefabBase>(prefab, out var asset) ||
                !asset.name.StartsWith(Prefix, StringComparison.Ordinal)) return 1.0;
            return int.TryParse(asset.name.Substring(Prefix.Length), out var key) && key > 0 && key < 1000000
                ? key / 1000000.0 : 1.0;
        }
        internal bool TryProfile(double ratio, out Entity entity)
        {
            entity = Entity.Null;
            var key = (int)Math.Round(Math.Max(0.000001, Math.Min(1, ratio)) * 1000000);
            if (key == 1000000) return prefabs.TryGetEntity(WeirFactory.RegisteredProducer, out entity);
            if (!requested.Contains(key))
            {
                // Persist the identity before using it in a save. Profiles are recreated
                // inside PrefabUpdate on startup, before saved prefab references load.
                var saved = requested.Concat(new[] { key }).OrderBy(x => x).ToArray();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(catalogPath));
                    var temporary = catalogPath + ".tmp";
                    File.WriteAllText(temporary, JsonUtility.ToJson(new EconomicProfileCatalog { ratios = saved }, true));
                    if (File.Exists(catalogPath)) File.Replace(temporary, catalogPath, catalogPath + ".bak");
                    else File.Move(temporary, catalogPath);
                    requested.Add(key);
                }
                catch (Exception error) { Mod.Log.Error(error, "Could not persist upkeep profile; profile was not applied."); }
            }
            if (!profiles.TryGetValue(key, out var asset) || !prefabs.TryGetEntity(asset, out entity) || EntityManager.HasComponent<Game.Common.Created>(entity) || !EntityManager.HasComponent<ObjectData>(entity)) return false;
            var source = prefabs.GetEntity(WeirFactory.RegisteredProducer);
            EntityManager.SetComponentData(entity, EntityManager.GetComponentData<ObjectData>(source));
            // ServiceUpkeepData contains money/material maintenance independently of
            // ConsumptionData. Scale the private profile buffer, never the native prefab.
            if (EntityManager.HasBuffer<ServiceUpkeepData>(source) && EntityManager.HasBuffer<ServiceUpkeepData>(entity))
            {
                var original = EntityManager.GetBuffer<ServiceUpkeepData>(source, true).ToNativeArray(Unity.Collections.Allocator.Temp);
                try
                {
                    var target = EntityManager.GetBuffer<ServiceUpkeepData>(entity);
                    target.Clear();
                    foreach (var item in original)
                    {
                        var adjusted = item;
                        adjusted.m_Upkeep.m_Amount = HydroSpecification.ScalePrice(item.m_Upkeep.m_Amount, key / 1000000.0);
                        target.Add(adjusted);
                    }
                }
                finally { original.Dispose(); }
            }
            return true;
        }
    }
}
