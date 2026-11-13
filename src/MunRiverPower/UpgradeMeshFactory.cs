using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MunRiverPower
{
    /// <summary>Loads the authored GLB upgrade meshes into the game's geometry asset database.</summary>
    internal static class UpgradeMeshFactory
    {
        private static RenderPrefab fishPassageMesh;
        private static RenderPrefab automaticSpillwayMesh;

        internal static ObjectMeshInfo[] FishPassage(PrefabSystem prefabs, BuildingPrefab template)
        {
            if (fishPassageMesh == null) fishPassageMesh = Create(prefabs, template,
                "MunRiverPower.FishPassage.Geometry", "MunRiverPower.FishPassage.Render", "FishPassage_Upgrade_LOD0.glb", new Vector3(.55f,.2f,.17f));
            return new[] { new ObjectMeshInfo { m_Mesh = fishPassageMesh, m_Position = LocalAnchor(template, true), m_Rotation = quaternion.identity } };
        }

        internal static ObjectMeshInfo[] AutomaticSpillway(PrefabSystem prefabs, BuildingPrefab template)
        {
            if (automaticSpillwayMesh == null) automaticSpillwayMesh = Create(prefabs, template,
                "MunRiverPower.AutomaticSpillway.Geometry", "MunRiverPower.AutomaticSpillway.Render", "AutomaticSpillway_Upgrade_LOD0.glb", new Vector3(.18f,.3f,.7f));
            return new[] { new ObjectMeshInfo { m_Mesh = automaticSpillwayMesh, m_Position = LocalAnchor(template, false), m_Rotation = quaternion.identity } };
        }

        private static float3 LocalAnchor(BuildingPrefab template, bool fish)
        {
            var min = new float3(float.MaxValue); var max = new float3(float.MinValue);
            var found = false;
            foreach (var item in template.m_Meshes ?? Array.Empty<ObjectMeshInfo>())
                if (item?.m_Mesh is RenderPrefab render)
                {
                    var bounds = render.bounds;
                    min = math.min(min, bounds.min + item.m_Position);
                    max = math.max(max, bounds.max + item.m_Position);
                    found = true;
                }
            if (!found) return float3.zero;
            var position = fish
                ? new float3(min.x + 2f, max.y - 20f, (min.z + max.z) * .5f)
                : new float3((min.x + max.x) * .5f, max.y - 6f, min.z - 7f);
            Mod.Log.Info($"UPGRADE MODEL ANCHOR: {(fish ? "fish" : "spillway")} position={position}, nativeBounds={min}..{max} (producer-local metres).");
            return position;
        }

        private static RenderPrefab Create(PrefabSystem prefabs, BuildingPrefab template, string geometryName, string renderName, string resourceFile, Vector3 scale)
        {
            var surface = FindSurface(template);
            var mesh = ReadGlb(resourceFile);
            // An internal extension is anchored to the powerhouse at one end of the
            // drawn network. Keep its visual footprint local to that anchor.
            var center = mesh.bounds.center; var bottom = mesh.bounds.min.y;
            var vertices = mesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = Vector3.Scale(vertices[i] - new Vector3(center.x,bottom,center.z), scale);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var geometry = AssetDatabase.user.AddAsset(
                AssetDataPath.Create("MunRiverPower", geometryName, EscapeStrategy.None), new[] { mesh });
            if (geometry == null) throw new InvalidOperationException("The game asset database rejected upgrade geometry " + geometryName);

            var render = PrefabBase.Create<RenderPrefab>(renderName);
            render.geometryAsset = geometry;
            render.surfaceAssets = new[] { surface };
            render.bounds = new Colossal.Mathematics.Bounds3(mesh.bounds);
            render.meshCount = 1;
            render.vertexCount = mesh.vertexCount;
            render.indexCount = (int)mesh.GetIndexCount(0);
            if (!prefabs.AddPrefab(render)) throw new InvalidOperationException("Game rejected upgrade render prefab " + renderName);
            return render;
        }

        private static SurfaceAsset FindSurface(BuildingPrefab template)
        {
            if (template is ObjectGeometryPrefab geometry && geometry.m_Meshes != null)
                foreach (var item in geometry.m_Meshes)
                    if (item.m_Mesh is RenderPrefab render && render.materialCount > 0)
                    {
                        var surface = render.GetSurfaceAsset(0);
                        if (surface != null) return surface;
                    }
            throw new InvalidOperationException("No native surface material was available for the upgrade model.");
        }

        internal static Mesh ReadGlb(string fileName) => ReadGlb(fileName, false, out _);

        internal static Mesh ReadGlb(string fileName, bool splitMaterials, out Color[] colors)
        {
            var assembly = typeof(UpgradeMeshFactory).Assembly;
            var resource = assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("." + fileName, StringComparison.Ordinal));
            if (resource == null) throw new FileNotFoundException("Embedded upgrade mesh is missing: " + fileName);
            byte[] bytes;
            using (var stream = assembly.GetManifestResourceStream(resource))
            using (var data = new MemoryStream()) { stream.CopyTo(data); bytes = data.ToArray(); }
            if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67 || BitConverter.ToUInt32(bytes, 4) != 2)
                throw new InvalidDataException("Invalid GLB header for " + fileName);
            var jsonLength = checked((int)BitConverter.ToUInt32(bytes, 12));
            var json = System.Text.Encoding.UTF8.GetString(bytes, 20, jsonLength).TrimEnd('\0', ' ', '\r', '\n', '\t');
            var binHeader = 20 + jsonLength;
            if (binHeader + 8 > bytes.Length || BitConverter.ToUInt32(bytes, binHeader + 4) != 0x004E4942)
                throw new InvalidDataException("GLB has no binary geometry chunk: " + fileName);
            var binaryLength = checked((int)BitConverter.ToUInt32(bytes, binHeader));
            var binaryOffset = binHeader + 8;
            if (binaryOffset + binaryLength > bytes.Length) throw new InvalidDataException("Truncated GLB binary chunk: " + fileName);
            var document = JsonConvert.DeserializeObject<GltfDocument>(json);
            if (document == null || document.meshes == null || document.accessors == null || document.bufferViews == null)
                throw new InvalidDataException("GLB geometry metadata is incomplete: " + fileName);

            var groups = new List<int[]>();
            var palette = new List<Color>();
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var indices = new List<int>();
            foreach (var meshData in document.meshes)
                foreach (var primitive in meshData.primitives ?? Array.Empty<GltfPrimitive>())
                {
                    if (primitive.attributes == null || primitive.attributes.POSITION < 0) continue;
                    var indexStart = indices.Count;
                    var oldCount = positions.Count;
                    ReadVector3(document, bytes, binaryOffset, primitive.attributes.POSITION, positions);
                    if (primitive.attributes.NORMAL >= 0) ReadVector3(document, bytes, binaryOffset, primitive.attributes.NORMAL, normals);
                    else normals.AddRange(Enumerable.Repeat(Vector3.up, positions.Count - oldCount));
                    if (primitive.attributes.TEXCOORD_0 >= 0) ReadVector2(document, bytes, binaryOffset, primitive.attributes.TEXCOORD_0, uvs);
                    else uvs.AddRange(Enumerable.Repeat(Vector2.zero, positions.Count - oldCount));
                    if (primitive.indices >= 0) ReadIndices(document, bytes, binaryOffset, primitive.indices, oldCount, indices);
                    else for (var i = 0; i < positions.Count - oldCount; i++) indices.Add(oldCount + i);
                    if (splitMaterials)
                    {
                        var group = indices.GetRange(indexStart, indices.Count - indexStart).ToArray();
                        for (int j=0; j<group.Length; j+=3) { var t=group[j+1]; group[j+1]=group[j+2]; group[j+2]=t; }
                        groups.Add(group);
                        var c = document.materials[primitive.material].pbrMetallicRoughness?.baseColorFactor;
                        palette.Add(c == null ? Color.white : new Color(c[0], c[1], c[2], 1));
                    }
                }
            if (positions.Count == 0 || indices.Count < 3 || indices.Count % 3 != 0)
                throw new InvalidDataException("GLB contains no triangle mesh: " + fileName);
            // glTF is right-handed; Unity/game mesh space is left-handed. The asset
            // importer normally flips Z and winding, so do the same for this direct loader.
            for (var i = 0; i < positions.Count; i++) positions[i] = new Vector3(positions[i].x, positions[i].y, -positions[i].z);
            for (var i = 0; i < normals.Count; i++) normals[i] = new Vector3(normals[i].x, normals[i].y, -normals[i].z);
            for (var i = 0; i < indices.Count; i += 3) { var swap = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = swap; }
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(fileName) };
            if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); if (splitMaterials) {
                mesh.subMeshCount = groups.Count;
                for (int i=0; i<groups.Count; i++) mesh.SetTriangles(groups[i], i);
            } else mesh.SetTriangles(indices, 0, true);
            colors = palette.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ReadVector3(GltfDocument doc, byte[] bytes, int binaryOffset, int accessorIndex, List<Vector3> output)
        {
            var accessor = doc.accessors[accessorIndex]; var view = doc.bufferViews[accessor.bufferView];
            if (accessor.componentType != 5126 || accessor.type != "VEC3") throw new InvalidDataException("Expected float VEC3 in upgrade mesh.");
            var stride = view.byteStride > 0 ? view.byteStride : 12; var start = binaryOffset + view.byteOffset + accessor.byteOffset;
            for (var i = 0; i < accessor.count; i++)
            { var p = start + i * stride; output.Add(new Vector3(F32(bytes,p),F32(bytes,p+4),F32(bytes,p+8))); }
        }
        private static void ReadVector2(GltfDocument doc, byte[] bytes, int binaryOffset, int accessorIndex, List<Vector2> output)
        {
            var accessor = doc.accessors[accessorIndex]; var view = doc.bufferViews[accessor.bufferView];
            if (accessor.componentType != 5126 || accessor.type != "VEC2") throw new InvalidDataException("Expected float VEC2 in upgrade mesh.");
            var stride = view.byteStride > 0 ? view.byteStride : 8; var start = binaryOffset + view.byteOffset + accessor.byteOffset;
            for (var i = 0; i < accessor.count; i++)
            { var p = start + i * stride; output.Add(new Vector2(F32(bytes,p),F32(bytes,p+4))); }
        }
        private static void ReadIndices(GltfDocument doc, byte[] bytes, int binaryOffset, int accessorIndex, int vertexOffset, List<int> output)
        {
            var accessor = doc.accessors[accessorIndex]; var view = doc.bufferViews[accessor.bufferView];
            var p = binaryOffset + view.byteOffset + accessor.byteOffset;
            for (var i = 0; i < accessor.count; i++)
            {
                uint index;
                if (accessor.componentType == 5121) index = bytes[p+i];
                else if (accessor.componentType == 5123) index = BitConverter.ToUInt16(bytes,p+i*2);
                else if (accessor.componentType == 5125) index = BitConverter.ToUInt32(bytes,p+i*4);
                else throw new InvalidDataException("Unsupported index type in upgrade mesh.");
                output.Add(checked(vertexOffset + (int)index));
            }
        }
        private static float F32(byte[] bytes, int offset) => BitConverter.ToSingle(bytes, offset);

#pragma warning disable 0649 // Populated by Newtonsoft.Json from the embedded glTF document.
        [Serializable] private sealed class GltfMaterial { public GltfPbr pbrMetallicRoughness; }
        private sealed class GltfPbr { public float[] baseColorFactor; }
        private sealed class GltfDocument { public GltfMaterial[] materials; public GltfMesh[] meshes; public GltfAccessor[] accessors; public GltfBufferView[] bufferViews; }
        [Serializable] private sealed class GltfMesh { public GltfPrimitive[] primitives; }
        [Serializable] private sealed class GltfPrimitive { public int material; public GltfAttributes attributes; public int indices = -1; }
        [Serializable] private sealed class GltfAttributes { public int POSITION = -1; public int NORMAL = -1; public int TEXCOORD_0 = -1; }
        [Serializable] private sealed class GltfAccessor { public int bufferView; public int byteOffset; public int componentType; public int count; public string type; }
        [Serializable] private sealed class GltfBufferView { public int byteOffset; public int byteLength; public int byteStride; }
#pragma warning restore 0649
    }
}
