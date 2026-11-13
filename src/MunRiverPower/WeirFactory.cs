using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;

namespace MunRiverPower
{
    /// <summary>Clone only the menu network and producer; retain the game's initialized render graph.</summary>
    internal sealed class WeirFactory
    {
        internal const string RootName = "MunRiverPower.LowHeadWeir";
        internal static PrefabBase RegisteredRoot;
        internal static PrefabBase OriginalRoot;
        internal static PrefabBase RegisteredProducer;
        internal static PrefabBase OriginalProducer;
        private readonly PrefabSystem prefabs;
        private readonly Dictionary<PrefabBase, PrefabBase> clones = new Dictionary<PrefabBase, PrefabBase>();
        private readonly List<PrefabBase> registrationOrder = new List<PrefabBase>();
        private readonly Dictionary<string, PrefabBase> names = new Dictionary<string, PrefabBase>(StringComparer.Ordinal);
        private int producers;

        internal WeirFactory(PrefabSystem prefabs) { this.prefabs = prefabs; }

        internal static bool IsHydroMenuPrefab(PrefabBase prefab)
        {
            if (prefab == null || prefab.name.StartsWith("MunRiverPower.", StringComparison.Ordinal)) return false;
            if (!prefab.TryGet<UIObject>(out var ui) || ui.m_Group == null || ui.m_IsDebugObject) return false;
            if (prefab.Has<WaterPowered>() && prefab is BuildingPrefab) return true;
            if (!(prefab is NetPrefab)) return false;
            if (prefab.Has<WaterPowered>()) return true;
            return ContainsProducer(prefab, new HashSet<PrefabBase>());
        }

        internal static int SourceScore(PrefabBase prefab)
        {
            var score = prefab is NetPrefab ? 100 : 0;
            if (prefab.asset != null && ReferenceEquals(prefab.asset.database, AssetDatabase.game)) score += 10000;
            if (prefab.name.StartsWith("HydroelectricPowerPlant", StringComparison.OrdinalIgnoreCase)) score += 1000;
            if (prefab.name.IndexOf("Hydroelectric", StringComparison.OrdinalIgnoreCase) >= 0) score += 100;
            return score;
        }

        internal void Register(PrefabBase source)
        {
            Mod.Log.Info("Cloning hydro menu source " + source.GetType().Name + ":" + source.name);
            var root = Clone(source, true);
            if (producers == 0) throw new InvalidOperationException("The cloned dam contains no WaterPowered producer.");
            if (!root.Has<UIObject>()) throw new InvalidOperationException("The cloned dam has no UI menu entry.");
            ValidateNativeRenderGraph(source, root);
            RemoveInheritedDamPieces(root);
            Mod.RegisterText(registrationOrder);
            var visualPieces = new List<PrefabBase>();
            // Scene meshes are created after the owned span is measured.
            // Build and validate the whole graph before making anything available to the game.
            var added = new List<PrefabBase>();
            try
            {
                foreach (var prefab in registrationOrder)
                {
                    if (!prefabs.AddPrefab(prefab)) throw new InvalidOperationException("Game rejected prefab " + prefab.name);
                    added.Add(prefab);
                }
            }
            catch
            {
                for (var i = added.Count - 1; i >= 0; i--) prefabs.RemovePrefab(added[i]);
                throw;
            }
            Mod.Log.Info($"Added {registrationOrder.Count} private prefabs, including {producers} producers. Native sections, pieces, LODs and network costs retained.");
            foreach (var piece in visualPieces)
                try
                {
                    if (!prefabs.AddPrefab(piece)) Mod.Log.Warn("Modern dam tile rejected; native tile retained: " + piece.name);
                }
                catch (Exception error) { Mod.Log.Error(error, "Modern dam tile registration failed; native tile retained: " + piece.name); }
            if (source is NetGeometryPrefab network && network.m_Sections != null)
                foreach (var sectionInfo in network.m_Sections)
                    if (sectionInfo?.m_Section is NetSectionPrefab section)
                    {
                        Mod.Log.Info($"DAM RENDER SECTION: {section.name}; pieces={section.m_Pieces?.Length ?? 0}; subsections={section.m_SubSections?.Length ?? 0}.");
                        if (section.m_Pieces != null)
                            foreach (var piece in section.m_Pieces)
                                if (piece?.m_Piece != null) Mod.Log.Info("DAM RENDER PIECE: " + piece.m_Piece.name);
                    }
            RegisteredRoot = root;
            OriginalRoot = source;
            RegisteredProducer = registrationOrder.Find(p => p.Has<WaterPowered>());
            foreach (var pair in clones)
                if (pair.Value == RegisteredProducer) OriginalProducer = pair.Key;
        }

        private PrefabBase Clone(PrefabBase source, bool root = false)
        {
            if (clones.TryGetValue(source, out var cached)) return cached;
            if (!root && (!(source is BuildingPrefab) || !source.Has<WaterPowered>()))
                throw new InvalidOperationException("Only the menu network and water-powered building may be copied.");
            var name = root ? RootName : "MunRiverPower." + source.GetType().Name + "." + source.name;
            if (names.TryGetValue(name, out var other) && other != source)
                throw new InvalidOperationException("Ambiguous source prefab identity: " + name);
            names[name] = source;
            var clone = source.Clone(name);
            clones.Add(source, clone);
            ValidateNativeRenderGraph(source, clone);
            // A copied obsolete ID would hijack the original prefab's save-game identity.
            clone.Remove<ObsoleteIdentifiers>();

            if (clone.TryGet<WaterPowered>(out var water))
            {
                producers++;
                water.m_ProductionFactor = (float)HydroSpecification.ProductionFactor;
                water.m_CapacityFactor = (float)HydroSpecification.CapacityFactor;
                // Native WaterPowered requires this to stay zero: production comes from water.
                if (clone.TryGet<PowerPlant>(out var plant)) plant.m_ElectricityProduction = 0;
                Mod.Log.Info($"Producer {name}: production factor={water.m_ProductionFactor}, capacity factor={water.m_CapacityFactor}");
            }
            // Placement charges the stock network/building prices. Do not advertise a
            // construction discount that is not charged by the native placement path.
            // No verified real-world O&M/currency conversion: retain the game's baseline
            // rather than presenting an arbitrary 50% discount as an engineering value.
            if (clone.TryGet<UIObject>(out var ui) && root) ui.m_Priority += 1;

            // Never duplicate render pieces or their LOD/material registrations. The game's
            // existing section graph stays shared and read-only in this compatibility release.
            if (clone.TryGet<NetSubObjects>(out var netObjects) && netObjects.m_SubObjects != null)
            {
                if (source.TryGet<NetSubObjects>(out var originalObjects))
                {
                    if (ReferenceEquals(originalObjects.m_SubObjects, netObjects.m_SubObjects))
                        throw new InvalidOperationException("Network object array must be private before rewiring.");
                    for (var i = 0; i < netObjects.m_SubObjects.Length; i++)
                        if (netObjects.m_SubObjects[i] != null && ReferenceEquals(originalObjects.m_SubObjects[i], netObjects.m_SubObjects[i]))
                            throw new InvalidOperationException("Network object entries must be private before rewiring.");
                }
                foreach (var item in netObjects.m_SubObjects)
                    if (item?.m_Object != null && item.m_Object.Has<WaterPowered>())
                        item.m_Object = (ObjectPrefab)Clone(item.m_Object);
            }

            registrationOrder.Add(clone);
            return clone;
        }

        private static void ValidateNativeRenderGraph(PrefabBase source, PrefabBase clone)
        {
            if (source is ObjectGeometryPrefab objectSource && clone is ObjectGeometryPrefab objectCopy)
            {
                if ((objectSource.m_Meshes?.Length ?? 0) != (objectCopy.m_Meshes?.Length ?? 0))
                    throw new InvalidOperationException("Powerhouse mesh list was not preserved.");
                for (var i = 0; i < (objectSource.m_Meshes?.Length ?? 0); i++)
                    if (objectSource.m_Meshes[i]?.m_Mesh != objectCopy.m_Meshes[i]?.m_Mesh)
                        throw new InvalidOperationException("Powerhouse must retain the game's native mesh references.");
            }
            if (!(source is NetGeometryPrefab original) || !(clone is NetGeometryPrefab copy)) return;
            if (original.m_Sections == null || copy.m_Sections == null || original.m_Sections.Length != copy.m_Sections.Length)
                throw new InvalidOperationException("Network section data was not preserved.");
            for (var i = 0; i < original.m_Sections.Length; i++)
                if (original.m_Sections[i]?.m_Section != copy.m_Sections[i]?.m_Section)
                    throw new InvalidOperationException("The copied network must retain every native section reference.");
            Mod.Log.Info("Validated native section identity: " + original.m_Sections.Length + " sections; no render pieces cloned.");
        }

        private void RemoveInheritedDamPieces(PrefabBase root)
        {
            if (!(root is NetGeometryPrefab network) || network.m_Sections == null) return;
            var copies = new Dictionary<NetSectionPrefab, NetSectionPrefab>();
            var sections = new NetSectionInfo[network.m_Sections.Length];
            for (var i = 0; i < sections.Length; i++)
            {
                var old = network.m_Sections[i];
                if (old == null) continue;
                sections[i] = new NetSectionInfo
                {
                    m_Section = CopySection(old.m_Section, copies),
                    m_RequireAll = old.m_RequireAll,
                    m_RequireAny = old.m_RequireAny,
                    m_RequireNone = old.m_RequireNone,
                    m_HiddenLayers = old.m_HiddenLayers,
                    m_Invert = old.m_Invert,
                    m_Flip = old.m_Flip,
                    m_Median = old.m_Median,
                    m_HalfLength = old.m_HalfLength,
                    m_Offset = old.m_Offset
                };
            }
            network.m_Sections = sections;
            Mod.Log.Info($"MOD DAM RENDER GRAPH: cloned {copies.Count} nested sections; native source unchanged.");
        }

        private NetSectionPrefab CopySection(NetSectionPrefab source, Dictionary<NetSectionPrefab, NetSectionPrefab> copies)
        {
            if (source == null) return null;
            if (!ContainsDamPiece(source, new HashSet<NetSectionPrefab>())) return source;
            if (copies.TryGetValue(source, out var found)) return found;
            var clone = (NetSectionPrefab)source.Clone("MunRiverPower.Section." + copies.Count + "." + source.name);
            copies.Add(source, clone);
            if (source.m_Pieces != null)
            {
                clone.m_Pieces = source.m_Pieces.Where(info => info?.m_Piece == null ||
                    !info.m_Piece.name.StartsWith("Hydroelectric_Power_Plant_01", StringComparison.Ordinal)).ToArray();
                var removed = source.m_Pieces.Length - clone.m_Pieces.Length;
                if (removed > 0) Mod.Log.Info($"MOD DAM PREFAB PIECES REMOVED: section={source.name}, count={removed}.");
            }
            if (source.m_SubSections != null)
            {
                clone.m_SubSections = source.m_SubSections.Select(old => old == null ? null : new NetSubSectionInfo
                {
                    m_Section = CopySection(old.m_Section, copies),
                    m_RequireAll = old.m_RequireAll,
                    m_RequireAny = old.m_RequireAny,
                    m_RequireNone = old.m_RequireNone
                }).ToArray();
            }
            registrationOrder.Add(clone);
            return clone;
        }

        private static bool ContainsDamPiece(NetSectionPrefab section, HashSet<NetSectionPrefab> seen)
        {
            if (section == null || !seen.Add(section)) return false;
            if (section.m_Pieces != null && section.m_Pieces.Any(info => info?.m_Piece != null &&
                info.m_Piece.name.StartsWith("Hydroelectric_Power_Plant_01", StringComparison.Ordinal))) return true;
            return section.m_SubSections != null && section.m_SubSections.Any(info =>
                info != null && ContainsDamPiece(info.m_Section, seen));
        }

        private static bool ContainsProducer(PrefabBase prefab, HashSet<PrefabBase> visited)
        {
            if (prefab == null || !visited.Add(prefab)) return false;
            if (prefab.Has<WaterPowered>()) return true;
            if (prefab.TryGet<NetSubObjects>(out var objects) && objects.m_SubObjects != null)
                foreach (var item in objects.m_SubObjects)
                    if (item?.m_Object != null && item.m_Object.Has<WaterPowered>()) return true;
            if (prefab.TryGet<NetPieceObjects>(out var props) && props.m_PieceObjects != null)
                foreach (var item in props.m_PieceObjects)
                    if (item?.m_Object != null && item.m_Object.Has<WaterPowered>()) return true;
            if (prefab is NetGeometryPrefab net && net.m_Sections != null)
                foreach (var section in net.m_Sections)
                    if (section != null && ContainsProducer(section.m_Section, visited)) return true;
            if (prefab is NetSectionPrefab group)
            {
                if (group.m_SubSections != null)
                    foreach (var section in group.m_SubSections)
                        if (section != null && ContainsProducer(section.m_Section, visited)) return true;
                if (group.m_Pieces != null)
                    foreach (var piece in group.m_Pieces)
                        if (piece != null && ContainsProducer(piece.m_Piece, visited)) return true;
            }
            return false;
        }
    }
}
