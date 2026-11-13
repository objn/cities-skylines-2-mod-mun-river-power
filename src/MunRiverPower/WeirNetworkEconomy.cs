using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;
using Unity.Entities;

namespace MunRiverPower
{
    // Keep native road/terrain composition data, but give this dam its own mesh
    // reference. The source mesh was baked with the stock concrete wall included.
    internal sealed class WeirNetworkEconomy
    {
        private readonly PrefabSystem prefabs;
        internal WeirNetworkEconomy(PrefabSystem prefabs) { this.prefabs = prefabs; }
        private struct Entry { internal Entity Native, Private; internal double Ratio; }
        private readonly Dictionary<Entity, Entry> entries = new Dictionary<Entity, Entry>();
        private readonly HashSet<Entity> seen = new HashSet<Entity>();
        private bool loggedRenderSource;

        internal void BeginRefresh() => seen.Clear();
        internal void Apply(EntityManager manager, Entity edge, double ratio)
        {
            seen.Add(edge);
            var composition = manager.GetComponentData<Game.Net.Composition>(edge);
            if (entries.TryGetValue(edge, out var previous) && manager.Exists(previous.Private) &&
                (composition.m_Edge == previous.Private || composition.m_Edge == previous.Native))
            {
                // The tool may restore the native composition on the same temporary
                // edge each frame. Reattach the already generated private mesh instead
                // of destroying it before the game's mesh builder can process it.
                if (composition.m_Edge != previous.Private)
                {
                    composition.m_Edge = previous.Private;
                    manager.SetComponentData(edge, composition);
                }
                if (System.Math.Abs(previous.Ratio - ratio) >= 0.0000001)
                {
                    if (manager.HasComponent<PlaceableNetComposition>(previous.Private) &&
                        manager.HasComponent<PlaceableNetComposition>(previous.Native))
                    {
                        var cost = manager.GetComponentData<PlaceableNetComposition>(previous.Native);
                        cost.m_UpkeepCost *= (float)ratio;
                        manager.SetComponentData(previous.Private, cost);
                    }
                    previous.Ratio = ratio;
                    entries[edge] = previous;
                }
                return;
            }
            else if (entries.TryGetValue(edge, out previous))
            {
                if (manager.Exists(previous.Private)) manager.DestroyEntity(previous.Private);
                entries.Remove(edge);
            }
            if (!manager.Exists(composition.m_Edge) || !manager.HasBuffer<NetCompositionPiece>(composition.m_Edge))
            {
                if (!loggedRenderSource) { loggedRenderSource = true; Mod.Log.Info("DAM RENDER DIAGNOSTIC: composition has no NetCompositionPiece buffer."); }
                return;
            }
            var source = composition.m_Edge;
            var privateEntity = manager.Instantiate(source);
            if (manager.HasComponent<PrefabData>(privateEntity)) manager.RemoveComponent<PrefabData>(privateEntity);
            if (manager.HasComponent<PlaceableNetComposition>(privateEntity))
            {
                var cost = manager.GetComponentData<PlaceableNetComposition>(privateEntity);
                cost.m_UpkeepCost *= (float)ratio;
                manager.SetComponentData(privateEntity, cost);
            }
            var pieces = manager.GetBuffer<NetCompositionPiece>(privateEntity);
            if (!loggedRenderSource)
            {
                loggedRenderSource = true;
                var names = new List<string>();
                foreach (var item in pieces)
                    names.Add(prefabs.TryGetPrefab<NetPiecePrefab>(item.m_Piece, out var part) ? part.name : "<unresolved>");
                Mod.Log.Info($"DAM COMPOSITION SOURCE: edge={edge}, pieces={pieces.Length}, meshData={manager.HasComponent<NetCompositionMeshData>(privateEntity)}, names={string.Join(", ", names)}.");
            }
            var removed = 0;
            for (var i = pieces.Length - 1; i >= 0; i--)
            {
                if (!prefabs.TryGetPrefab<NetPiecePrefab>(pieces[i].m_Piece, out var piece)) continue;
                var name = piece.name;
                if (!name.StartsWith("Hydroelectric_Power_Plant_01")) continue;
                pieces.RemoveAt(i);
                removed++;
            }
            if (removed > 0)
            {
                // Instantiation copies NetCompositionMeshRef, so simply removing a
                // piece still draws the stock baked wall. A Created composition is
                // picked up by NetCompositionMeshRefSystem, which generates a mesh
                // from the now filtered NetCompositionPiece buffer.
                if (manager.HasComponent<NetCompositionMeshRef>(privateEntity))
                    manager.SetComponentData(privateEntity, new NetCompositionMeshRef());
                else
                    manager.AddComponentData(privateEntity, new NetCompositionMeshRef());
                if (!manager.HasComponent<Game.Common.Created>(privateEntity))
                    manager.AddComponent<Game.Common.Created>(privateEntity);
                if (!loggedMeshRebuild)
                {
                    loggedMeshRebuild = true;
                    Mod.Log.Info($"NATIVE DAM MESH REBUILD QUEUED: edge={edge}, removed={removed}, sourceMeshRef={manager.HasComponent<NetCompositionMeshRef>(source)}, retainedPieces={pieces.Length}.");
                }
            }
            if (ratio >= 1 && removed == 0) { manager.DestroyEntity(privateEntity); return; }
            composition.m_Edge = privateEntity;
            manager.SetComponentData(edge, composition);
            entries[edge] = new Entry { Native = source, Private = privateEntity, Ratio = ratio };
        }
        internal void EndRefresh(EntityManager manager)
        {
            foreach (var edge in entries.Keys.Where(edge => !seen.Contains(edge)).ToArray())
            {
                var entry = entries[edge];
                if (manager.Exists(edge) && manager.HasComponent<Game.Net.Composition>(edge))
                {
                    var composition = manager.GetComponentData<Game.Net.Composition>(edge);
                    if (composition.m_Edge == entry.Private && manager.Exists(entry.Native))
                    { composition.m_Edge = entry.Native; manager.SetComponentData(edge, composition); }
                }
                if (manager.Exists(entry.Private)) manager.DestroyEntity(entry.Private);
                entries.Remove(edge);
            }
        }
        private bool loggedMeshRebuild;
        internal void Reset() { entries.Clear(); seen.Clear(); loggedRenderSource = false; loggedMeshRebuild = false; }
    }
}
