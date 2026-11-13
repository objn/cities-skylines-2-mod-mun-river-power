"""Verify the editable source and round-trip real exported meshes using Blender."""
import bpy, bmesh, math, json, struct
from pathlib import Path
from mathutils import Vector

out = Path(__file__).resolve().parents[1] / 'assets' / 'ModernDam'
bpy.ops.wm.open_mainfile(filepath=str(out/'ModernDam.blend'))
report = {'status':'PASS','runtime_game_integration':'NOT INSTALLED / NOT TESTED','meshes':{},'roundtrips':{}}
for level in range(3):
    obj = bpy.data.objects[f'Dam_Module75_LOD{level}']
    mesh = obj.data
    assert len(mesh.uv_layers) == 2, 'Need exactly tile UV and unique lightmap UV.'
    assert mesh.uv_layers[0].name=='UV0_Tileable' and mesh.uv_layers[1].name=='UV1_Lightmap'
    assert all(len(face.vertices)==3 for face in mesh.polygons), 'Export mesh must be triangles.'
    assert all(math.isfinite(c) for v in mesh.vertices for c in v.co), 'Nonfinite coordinates.'
    bm = bmesh.new(); bm.from_mesh(mesh)
    bad_edges = sum(not edge.is_manifold for edge in bm.edges)
    zero_faces = sum(face.calc_area()<1e-10 for face in bm.faces)
    assert bad_edges == 0 and zero_faces == 0, f'Invalid solid mesh: {bad_edges}, {zero_faces}'
    bm.free()
    assert abs(obj.dimensions.x-75)<.001 and abs(obj.dimensions.y-34)<.001
    assert abs(obj.dimensions.z-22.08)<.001
    report['meshes'][f'LOD{level}']={'triangles':len(mesh.polygons),'vertices':len(mesh.vertices),'nonmanifold_edges':bad_edges,'degenerate_faces':zero_faces,'uv_layers':len(mesh.uv_layers)}
assert report['meshes']['LOD2']['triangles'] < report['meshes']['LOD1']['triangles'] < report['meshes']['LOD0']['triangles']

def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def bounds(objects):
    vertices=[o.matrix_world @ v.co for o in objects for v in o.data.vertices]
    return [max(v[i] for v in vertices)-min(v[i] for v in vertices) for i in range(3)]

for name, width in [('Dam_Module75_LOD0',75),('Dam_Assembly300_LOD0',300),('Dam_Module75_Collision',75)]:
    for kind in ('glb','fbx'):
        clear()
        path=out/f'{name}.{kind}'
        if kind=='glb': bpy.ops.import_scene.gltf(filepath=str(path))
        else: bpy.ops.import_scene.fbx(filepath=str(path))
        bpy.context.view_layer.update()
        objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
        extent=bounds(objects)
        assert abs(extent[0]-width)<.01, f'{path.name}: wrong metric span {extent}'
        assert abs(extent[1]-34)<.01, f'{path.name}: wrong metric width {extent}'
        expected_height=17 if 'Collision' in name else 22.08
        assert abs(extent[2]-expected_height)<.01, f'{path.name}: wrong up-axis/height {extent}'
        assert all(len(o.data.uv_layers)>0 for o in objects)
        report['roundtrips'][path.name]={'mesh_objects':len(objects),'dimensions_metres_blender_xyz':extent,'material_count':len({m.name for o in objects for m in o.data.materials})}

# Check GLB data is embedded, including image bytes, so it opens without file/network dependencies.
for path in out.glob('*.glb'):
    raw=path.read_bytes()
    magic,version,length=struct.unpack_from('<III',raw)
    assert magic==0x46546c67 and version==2 and length==len(raw)
    chunk_length,chunk_type=struct.unpack_from('<II',raw,12)
    assert chunk_type==0x4e4f534a
    doc=json.loads(raw[20:20+chunk_length])
    assert all('uri' not in buffer for buffer in doc['buffers'])
    assert all('bufferView' in image for image in doc.get('images',[]))
(out/'model-validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('MODEL_VALIDATION_PASS',json.dumps(report),flush=True)
