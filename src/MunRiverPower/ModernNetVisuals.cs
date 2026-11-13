using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Unity.Entities;
using UnityEngine;

namespace MunRiverPower
{
    // Private render pieces for the mod's generated network compositions. The game's
    // shared road pieces and the native placement tool remain untouched.
    internal static class ModernNetVisuals
    {
        private static readonly Dictionary<NetPiecePrefab, NetPiecePrefab> replacements = new Dictionary<NetPiecePrefab, NetPiecePrefab>();

        internal static void Prepare(PrefabBase root, List<PrefabBase> registration)
        {
            replacements.Clear();
            if (!(root is NetGeometryPrefab network) || network.m_Sections == null) return;
            foreach (var sectionInfo in network.m_Sections)
            {
                if (!(sectionInfo?.m_Section is NetSectionPrefab section) || section.m_Pieces == null) continue;
                foreach (var pieceInfo in section.m_Pieces)
                {
                    if (!(pieceInfo?.m_Piece is NetPiecePrefab source) || replacements.ContainsKey(source) || !IsModuleAnchor(source.name)) continue;
                    try
                    {
                        var mesh = CreateModuleTile(source);
                        if (mesh == null) continue;
                        // GeometryAsset registration requires a .Geometry suffix. Use a
                        // unique name because distinct native pieces can share a label.
                        var id = replacements.Count;
                        var clone = (NetPiecePrefab)source.Clone("MunRiverPower.ModernTile." + id);
                        var geometry = AssetDatabase.user.AddAsset(AssetDataPath.Create("MunRiverPower/ModernDam", "MunRiverPower.ModernTile." + id + ".Geometry", EscapeStrategy.None), new[] { mesh });
                        if (geometry == null) throw new InvalidOperationException("Could not create geometry asset for " + source.name);
                        clone.geometryAsset = geometry;
                        clone.surfaceAssets = new[] { source.GetSurfaceAsset(0) };
                        clone.bounds = new Colossal.Mathematics.Bounds3(mesh.bounds);
                        clone.meshCount = 1;
                        clone.vertexCount = mesh.vertexCount;
                        clone.indexCount = (int)mesh.GetIndexCount(0);
                        replacements.Add(source, clone);
                        registration.Add(clone);
                        Mod.Log.Info($"AUTHORED DAM MODULE PREPARED: {source.name}, bounds={mesh.bounds.min}..{mesh.bounds.max}, vertices={mesh.vertexCount}.");
                    }
                    catch (Exception error) { Mod.Log.Error(error, "Authored dam module failed for " + source.name + "; native piece retained."); }
                }
            }
        }

        internal static bool TryGetReplacement(PrefabSystem prefabs, Entity native, out Entity replacement)
        {
            replacement = Entity.Null;
            return prefabs.TryGetPrefab<NetPiecePrefab>(native, out var source)
                && replacements.TryGetValue(source, out var custom)
                && prefabs.TryGetEntity(custom, out replacement);
        }

        internal static bool TryGetModule(PrefabSystem prefabs, out Entity replacement)
        {
            replacement = Entity.Null;
            foreach (var pair in replacements)
                if (pair.Key.name == "Elevated Bottom Piece 2" && prefabs.TryGetEntity(pair.Value, out replacement)) return true;
            foreach (var pair in replacements)
                if (prefabs.TryGetEntity(pair.Value, out replacement)) return true;
            return false;
        }

        private static bool IsModuleAnchor(string name) =>
            (name == "Elevated Bottom Piece 2" ||
             name == "Elevated Bottom Piece 3" ||
             name == "Elevated Bottom Piece 3.5 - Edge");

        private static Mesh CreateModuleTile(NetPiecePrefab source)
        {
            if (source.materialCount < 1 || source.GetSurfaceAsset(0) == null) return null;
            var b = source.bounds;
            var min = (Vector3)b.min; var max = (Vector3)b.max;
            if (max.z - min.z < 1f || max.z - min.z > 200f) return null;
            // The authored GLB uses X along its 75 m span and Y up. Network
            // pieces use Z along the edge. Keep the authored gates, piers and
            // turbine geometry; fit only the span length to the native tile.
            var mesh = UpgradeMeshFactory.ReadGlb("Dam_Module75_LOD0.glb");
            var vertices = mesh.vertices;
            var lengthScale = (max.z - min.z) / 75f;
            var centreZ = (min.z + max.z) * .5f;
            var deckLevel = max.y;
            for (var i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                vertices[i] = new Vector3(vertex.z + 5f, vertex.y - 17f + deckLevel,
                    centreZ + (vertex.x - 37.5f) * lengthScale);
            }
            mesh.vertices = vertices;
            mesh.name = "MunRiverPower.AuthoredDamModule." + source.name;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
