using System;
using System.Collections.Generic;
using System.Linq;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using UnityEngine;
using Mesh = UnityEngine.Mesh;
using Object = UnityEngine.Object;

namespace MunRiverPower
{
    internal static class DamSceneVisuals
    {
        private sealed class Display { internal GameObject Root; internal Mesh[] Meshes; internal int Signature; }
        private static readonly Dictionary<Entity, Display> displays = new Dictionary<Entity, Display>();
        private static readonly HashSet<Entity> seen = new HashSet<Entity>();
        private const string Dam = "Dam_Module75_LOD0.glb";
        private const string Fish = "FishPassage_Upgrade_LOD0.glb";
        private const string Gate = "AutomaticSpillway_Upgrade_LOD0.glb";
        private static readonly Dictionary<string, Mesh> sources = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material[]> palettes = new Dictionary<string, Material[]>();

        private static Mesh Load(string file)
        {
            if (sources.TryGetValue(file, out var mesh)) return mesh;
            var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No supported scene shader");
            mesh = UpgradeMeshFactory.ReadGlb(file, true, out var colors);
            palettes[file] = colors.Select(c => {
                var m = new Material(shader);
                m.SetColor(m.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", c);
                return m;
            }).ToArray();
            sources.Add(file, mesh);
            return mesh;
        }
        private static bool failed;
        internal static void Begin() => seen.Clear();
        internal static void Show(EntityManager manager, Entity owner, List<Entity> edges, PrefabSystem prefabs)
        {
            if (failed || edges.Count == 0) return;
            seen.Add(owner);
            try
            {
                var curves = edges.Select(e => manager.GetComponentData<Curve>(e)).ToList();
                bool fish = false, gate = false;
                if (manager.HasBuffer<Game.Buildings.InstalledUpgrade>(owner))
                    foreach (var upgrade in manager.GetBuffer<Game.Buildings.InstalledUpgrade>(owner, true))
                    {
                        var entity = upgrade.m_Upgrade;
                        if (!manager.HasComponent<PrefabRef>(entity) || manager.HasComponent<Game.Common.Deleted>(entity)) continue;
                        if (!prefabs.TryGetPrefab<BuildingExtensionPrefab>(manager.GetComponentData<PrefabRef>(entity).m_Prefab, out var asset)) continue;
                        fish |= asset.name == FishPassageSpecification.Name;
                        gate |= asset.name == SpillwayControl.UpgradeName;
                    }
                int signature = 17 + (fish ? 1 : 0) + (gate ? 2 : 0);
                foreach (var c in curves) signature = unchecked(signature * 31 + c.m_Bezier.GetHashCode());
                if (displays.TryGetValue(owner, out var prior) && prior.Signature == signature) return;
                // Join curve samples by nearest endpoints, including reversed edges.
                var strips = curves.Select(c => Enumerable.Range(0,17).Select(i =>
                    (Vector3)Colossal.Mathematics.MathUtils.Position(c.m_Bezier,i/16f)).ToList()).ToList();
                var path = strips[0]; strips.RemoveAt(0);
                while (strips.Count > 0)
                {
                    var best = float.MaxValue; int choice=0; bool prepend=false, reverse=false;
                    for (int i=0;i<strips.Count;i++)
                    for (int k=0;k<4;k++)
                    {
                        var s=strips[i]; bool front=k>=2, flip=(k%2)==1;
                        var gap=(path[front?0:path.Count-1]-s[front!=flip?s.Count-1:0]).sqrMagnitude;
                        if(gap<best){best=gap;choice=i;prepend=front;reverse=flip;}
                    }
                    var next=strips[choice]; strips.RemoveAt(choice);
                    if(reverse) next.Reverse();
                    if(prepend) path.InsertRange(0,next); else path.AddRange(next);
                }
                var lengths=new float[path.Count];
                for(int i=1;i<path.Count;i++) lengths[i]=lengths[i-1]+Vector3.Distance(path[i-1],path[i]);
                float length=lengths[lengths.Length-1];
                if(length<1) return;
                int count=Math.Max(1,(int)Math.Floor((length+.01f)/75));
                var root=new GameObject("Mun River dam "+owner);
                var display=new Display {Root=root, Meshes=new Mesh[count*(gate?2:1)+(fish?1:0)+11], Signature=signature};
                if(prior!=null) Remove(prior);
                displays[owner]=display;
                root.transform.position=path[0];
                int partIndex = 0;
                for (int n=0; n<count; n++)
                {
                    Draw(Dam, n, false);
                    if (gate) Draw(Gate, n, false);
                }
                // The hydro-specific native piece also contained the road surface.
                // Its removal hides the stock concrete and its asphalt together, so
                // draw only the visible deck here. The game's road network remains
                // underneath for routing, connections and bulldozing.
                DrawRoad(-4.8f, 4.8f, .16f, "road-asphalt", new Color(.30f,.32f,.30f));
                DrawRoad(-5.15f, -4.8f, .19f, "road-sidewalk", new Color(.42f,.43f,.41f));
                DrawRoad(4.8f, 5.15f, .19f, "road-sidewalk", new Color(.42f,.43f,.41f));
                DrawRoad(-7.5f, -5.15f, .12f, "road-sidewalk", new Color(.42f,.43f,.41f));
                DrawRoad(5.15f, 7.5f, .12f, "road-sidewalk", new Color(.42f,.43f,.41f));
                DrawRoad(-7.72f, -7.5f, .19f, "road-sidewalk", new Color(.42f,.43f,.41f));
                DrawRoad(7.5f, 7.72f, .19f, "road-sidewalk", new Color(.42f,.43f,.41f));
                DrawRoad(-.19f, -.11f, .205f, "road-centre", new Color(.93f,.68f,.12f));
                DrawRoad(.11f, .19f, .205f, "road-centre", new Color(.93f,.68f,.12f));
                DrawRoad(-4.37f, -4.28f, .205f, "road-edge", new Color(.78f,.8f,.77f));
                DrawRoad(4.28f, 4.37f, .205f, "road-edge", new Color(.78f,.8f,.77f));
                if (fish) Draw(Fish, 0, false);
                Mod.Log.Info($"DAM SCENE CREATED: {owner}, {count} spans, singleBody=true, fish={fish}, spillway={gate}");
                void Draw(string file, int n, bool intake)
                {
                    var source = Load(file);
                    var original = source.vertices;
                    var mesh = Object.Instantiate(source);
                    display.Meshes[partIndex++] = mesh;
                    var v=new Vector3[original.Length];
                    for(int j=0;j<v.Length;j++)
                    {
                        var p=original[j]; float d = file == Fish ? Mathf.Clamp(p.x + 12f, 0, length) : Mathf.Clamp((n+p.x/75f)*length/count,0,length);
                        int idx=Array.BinarySearch(lengths,d);
                        idx=Mathf.Clamp(idx<0?~idx:idx,1,path.Count-1);
                        while(idx<path.Count-1 && lengths[idx]<=lengths[idx-1]) idx++;
                        var tangent=(path[idx]-path[idx-1]).normalized;
                        var side=Vector3.Cross(tangent,Vector3.up).normalized;
                        var centre=Vector3.Lerp(path[idx-1],path[idx],Mathf.InverseLerp(lengths[idx-1],lengths[idx],d));
                        // Keep the full spillway assembly outside the 9.6 m traffic deck.
                        // Its authored transverse extent is -10.35..-5.45 m, leaving
                        // at least 0.65 m from the road edge at -4.8 m.
                        float lateral = intake ? -p.z-11f : p.z+5f;
                        if (file == Fish) lateral = (p.z + 5f) * .35f;
                        if (file == Gate) lateral = p.z;
                        v[j]=centre-path[0]+side*lateral+Vector3.up*(p.y-(file == Fish ? 18f : 16.8f));
                    }
                    mesh.vertices=v; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    var part=new GameObject("Dam span"); part.transform.SetParent(root.transform,false);
                    part.AddComponent<MeshFilter>().sharedMesh=mesh;
                    part.AddComponent<MeshRenderer>().sharedMaterials=palettes[file];
                }
                void DrawRoad(float minOffset, float maxOffset, float elevation, string materialName, Color color)
                {
                    if (!palettes.TryGetValue(materialName, out var materials))
                    {
                        // Native road shaders deform vertices using game-specific mesh
                        // attributes. Applying one to this procedural ribbon stretched
                        // the road into the sky. Use a plain Lit material instead.
                        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
                        if (shader == null) throw new InvalidOperationException("No supported road shader");
                        var material = new Material(shader);
                        material.SetColor(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", color);
                        palettes[materialName] = materials = new[] { material };
                    }
                    var mesh = new Mesh { name = "Mun River road " + materialName };
                    display.Meshes[partIndex++] = mesh;
                    var vertices = new Vector3[path.Count * 2];
                    var uv = new Vector2[path.Count * 2];
                    var triangles = new int[(path.Count - 1) * 6];
                    for (var i = 0; i < path.Count; i++)
                    {
                        var tangent = (path[Math.Min(i + 1, path.Count - 1)] - path[Math.Max(i - 1, 0)]).normalized;
                        var side = Vector3.Cross(tangent, Vector3.up).normalized;
                        var origin = path[i] - path[0] + Vector3.up * elevation;
                        vertices[2 * i] = origin + side * minOffset;
                        vertices[2 * i + 1] = origin + side * maxOffset;
                        uv[2 * i] = new Vector2(lengths[i] / 4f, 0);
                        uv[2 * i + 1] = new Vector2(lengths[i] / 4f, (maxOffset - minOffset) / 4f);
                        if (i == path.Count - 1) continue;
                        var t = i * 6;
                        var v = i * 2;
                        triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                        triangles[t + 3] = v + 1; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
                    }
                    mesh.vertices = vertices;
                    mesh.uv = uv;
                    mesh.triangles = triangles;
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    var part = new GameObject("Mun River visible road deck");
                    part.transform.SetParent(root.transform, false);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    part.AddComponent<MeshRenderer>().sharedMaterials = materials;
                }
            }
            catch(Exception ex){failed=true;Mod.Log.Error(ex,"Dam scene rendering failed.");}
        }
        private static void Remove(Display d){Object.Destroy(d.Root);foreach(var m in d.Meshes) if(m!=null) Object.Destroy(m);}
        internal static void End(EntityManager manager){foreach(var e in displays.Keys.Where(e=>!seen.Contains(e) && (!manager.Exists(e) || !manager.HasComponent<Game.Tools.Temp>(e))).ToArray()){Remove(displays[e]);displays.Remove(e);}}
        internal static void Reset(){seen.Clear();foreach(var display in displays.Values) Remove(display);displays.Clear();foreach(var m in sources.Values) Object.Destroy(m);sources.Clear();
            foreach(var palette in palettes.Values) foreach(var material in palette) Object.Destroy(material);
            palettes.Clear();failed=false;}
    }
}
