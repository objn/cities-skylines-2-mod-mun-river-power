"""Original modern low-head dam geometry. Run with Blender 4.2 --background --python.
No game meshes/materials are copied. Source assets only; no runtime prefab changes.
"""
import bpy
import math
import os
import json
import random
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'assets' / 'ModernDam'
OUT.mkdir(parents=True, exist_ok=True)
TEX = OUT / 'Textures'
TEX.mkdir(exist_ok=True)
random.seed(134)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for col in list(bpy.data.collections):
    if col.name != 'Collection':
        bpy.data.collections.remove(col)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1

def collection(name):
    col = bpy.data.collections.new(name)
    scene.collection.children.link(col)
    return col

def move(obj, col):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    col.objects.link(obj)

def material(name, rgb, roughness=0.7, metallic=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*rgb, 1)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*rgb, 1)
    shader.inputs['Roughness'].default_value = roughness
    shader.inputs['Metallic'].default_value = metallic
    return mat

CONCRETE = material('MRP_LightPrecastConcrete', (0.57, .61, .60), .86)
DARK_CONCRETE = material('MRP_WetConcrete', (.26, .32, .32), .82)
STEEL = material('MRP_GraphiteSteel', (.055, .085, .10), .38, .68)
TEAL = material('MRP_PowderCoatTeal', (.035, .23, .255), .40, .35)
SILVER = material('MRP_GalvanizedMetal', (.38, .46, .48), .42, .78)
VOID = material('MRP_IntakeRecess', (.012, .025, .027), .91)
DECK = material('MRP_ServiceDeck', (.18, .22, .23), .91)
SAFETY = material('MRP_SafetyOchre', (.80, .46, .065), .63)
GLASS = material('MRP_TintedPanel', (.045, .13, .17), .24, .36)

# Portable, original, tileable concrete texture; geometry UVs repeat every four metres.
import numpy as np
rng = np.random.default_rng(134)
size = 512
noise = rng.normal(0, .013, (size, size))
noise += .006 * np.sin(np.arange(size)[None, :] * 2 * np.pi / size * 8)
pixels = np.empty((size, size, 4), dtype=np.float32)
for channel, base in enumerate((.57, .61, .60)):
    pixels[:, :, channel] = np.clip(base + noise, 0, 1)
pixels[:, :, 3] = 1
img = bpy.data.images.new('MRP_Concrete_BaseColor', width=size, height=size, alpha=False)
img.pixels.foreach_set(pixels.ravel())
img.filepath_raw = str(TEX / 'Concrete_BaseColor.png')
img.file_format = 'PNG'
img.save()
img = bpy.data.images.load(str(TEX / 'Concrete_BaseColor.png'))
node = CONCRETE.node_tree.nodes.new('ShaderNodeTexImage')
node.image = img
CONCRETE.node_tree.links.new(node.outputs['Color'], CONCRETE.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])

def mesh_obj(name, verts, faces, mat, col):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    col.objects.link(obj)
    obj.data.materials.append(mat)
    return obj

def bevel(obj, width=.06):
    mod = obj.modifiers.new('Small manufactured edges', 'BEVEL')
    mod.width = width
    mod.segments = 1
    mod.affect = 'EDGES'
    mod = obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
    mod.keep_sharp = True

def box(name, x, y, bottom, w, d, h, mat, col, edge=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, bottom + h / 2))
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = (w, d, h)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    move(obj, col)
    obj.data.materials.append(mat)
    if edge:
        bevel(obj, min(edge, min(w, d, h) * .19))
    return obj

def beam(name, a, b, width, depth, mat, col):
    a, b = Vector(a), Vector(b)
    obj = box(name, 0, 0, 0, width, depth, (b-a).length, mat, col)
    obj.location = (a+b)/2
    obj.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    return obj

def cylinder(name, center, radius, length, mat, col, axis='Z', sides=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=length, location=center)
    obj = bpy.context.object
    obj.name = name
    if axis == 'X':
        obj.rotation_euler[1] = math.pi/2
    if axis == 'Y':
        obj.rotation_euler[0] = math.pi/2
    move(obj, col)
    obj.data.materials.append(mat)
    return obj

def extrude_x(name, x0, x1, profile, mat, col):
    n = len(profile)
    verts = [(x, y, z) for x in (x0, x1) for y, z in profile]
    faces = [tuple(range(n-1, -1, -1)), tuple(range(n, n*2))]
    faces += [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)]
    return mesh_obj(name, verts, faces, mat, col)

def pier(name, cx, width, col, detail):
    footprint = [(cx-width/2,-5), (cx,-9), (cx+width/2,-5), (cx+width/2,10), (cx-width/2,10)]
    verts = [(x,y,z) for z in (1,16.8) for x,y in footprint]
    faces = [(4,3,2,1,0), (5,6,7,8,9)] + [(i,(i+1)%5,(i+1)%5+5,i+5) for i in range(5)]
    obj = mesh_obj(name, verts, faces, CONCRETE, col)
    if detail == 0:
        bevel(obj, .10)
    left, right = max(0, cx-(width+.15)/2), min(75, cx+(width+.15)/2)
    box(name+'_cap', (left+right)/2, 1, 16.7, right-left, 18, .30, CONCRETE, col, .06 if detail == 0 else 0)

def gate(cx, number, col, detail):
    sections = (12, 6, 3)[detail]
    # Curved radial gate, closed position. A solid bent plate with returned side edges.
    profile = []
    for i in range(sections+1):
        angle = math.radians(-45 + 75*i/sections)
        profile.append((-10+10*math.cos(angle), 11+10*math.sin(angle)))
    profile += [(y+.22,z) for y,z in reversed(profile)]
    extrude_x(f'RadialGate_{number}', cx-13.8, cx+13.8, profile, TEAL, col)
    extrude_x(f'OgeeChute_{number}', cx-14, cx+14,
              [(-5,1), (21,1), (21,1.25), (13,1.4), (7,2.0), (2,3.6), (-1,4.8), (-5,4.8)], DARK_CONCRETE, col)
    if detail < 2:
        for j, angle in enumerate((-38,-22,-6,10,25)):
            a = math.radians(angle)
            y,z = -10+10*math.cos(a), 11+10*math.sin(a)
            box(f'Gate_{number}_horizontal_rib_{j}', cx, y+.4, z-.12, 27.6, .30, .26, STEEL, col)
        for dx in (-11,11):
            pivot = (cx+dx, 7.4, 11.7)
            beam('Radial_arm_lower', pivot, (cx+dx,-2.35,5.1), .36,.44, STEEL,col)
            beam('Radial_arm_upper', pivot, (cx+dx,-.40,14.1), .30,.36, STEEL,col)
            cylinder('Gate_trunnion', pivot, .60,.65,SILVER,col,'X',12 if detail == 0 else 8)
        for dx in (-15,15):
            box('Hoist_portal_column', cx+dx,-11,17, .48,.65,4.4, STEEL,col)
        box('Hoist_portal_beam',cx,-11,21.15,31,.95,.75,STEEL,col,.04 if detail == 0 else 0)
        box('Hoist_weather_cover',cx,-11,21.90,31.4,1.25,.18,TEAL,col,.03 if detail == 0 else 0)
        for dx in (-9,9):
            box('Lift_gearbox',cx+dx,-11,20.45,2.1,1.6,1.25,STEEL,col,.08 if detail == 0 else 0)
            beam('Lift_rod',(cx+dx,-9.5,16),(cx+dx,-11,20.5),.12,.12,SILVER,col)
    else:
        for dx in (-15,15):
            box('Hoist_silhouette_column',cx+dx,-11,17,.48,.65,4.4,STEEL,col)
        box('Hoist_silhouette_beam',cx,-11,21.15,31,.95,.75,STEEL,col)
        box('Hoist_silhouette_cover',cx,-11,21.90,31.4,1.25,.18,TEAL,col)

def make_module(detail):
    col = collection(f'MODULE_75m_LOD{detail}')
    box('Foundation_75m',37.5,5,0,75,34,1,DARK_CONCRETE,col,.10 if detail == 0 else 0)
    for i,(x,w) in enumerate(((1.5,3),(33,4),(64,2))):
        pier(f'Pier_{i}',x,w,col,detail)
    gate(17,'A',col,detail)
    gate(49,'B',col,detail)
    box('Integrated_turbine_body',70,0,1,10,18,15.8,CONCRETE,col,.10 if detail == 0 else 0)
    box('Intake_recess',70,-9.10,3,8.5,.18,11,VOID,col)
    if detail < 2:
        count = 24 if detail == 0 else 10
        for i in range(count+1):
            box('Trash_rack_bar',65.9+i*(8.2/count),-9.25,3,.075,.16,11,SILVER,col)
        for h in (3.2,6.6,10,13.6):
            box('Trash_rack_brace',70,-9.32,h,8.4,.15,.16,STEEL,col)
    box('Service_bridge_soffit',37.5,4,16.10,75,6.4,.9,CONCRETE,col,.06 if detail == 0 else 0)
    box('Service_deck_surface',37.5,4,17.01,75,5.9,.14,DECK,col)
    for y in (1.25,6.75):
        box('Deck_edge_curb',37.5,y,17.1,75,.30,.30,CONCRETE,col)
        if y == 1.25:
            continue  # The road-facing side is open; guardrail stays on the water edge.
        # A gap beside the control room gives staff access from the service walk.
        rail_length = 65
        box('Continuous_guardrail',rail_length/2,y,18.20,rail_length,.075,.10,SILVER,col)
        if detail < 2:
            box('Mid_guardrail',rail_length/2,y,17.76,rail_length,.06,.07,SILVER,col)
            spacing = 3 if detail == 0 else 7.5
            for i in range(int(rail_length/spacing)+1):
                box('Guardrail_post', min(74.95,max(.05,i*spacing)),y,17.3,.08,.08,1.02,STEEL,col)
    box('Service_safety_line',37.5,2.2,17.155,74.7,.10,.012,SAFETY,col)
    # Move the room entirely outside the traffic and sidewalk clearance.
    # A supported plinth meets the service walk at its outer edge.
    box('Control_room_platform',70,7.2,16.45,10,10.5,.9,CONCRETE,col)
    box('Control_room_support',70,9.3,0.0,4.8,4.8,16.45,DARK_CONCRETE,col)
    box('Control_enclosure',70,7,17,9.2,7.8,3.4,CONCRETE,col,.11 if detail == 0 else 0)
    box('Control_roof',70,7,20.4,10,8.5,.32,STEEL,col,.06 if detail == 0 else 0)
    box('Control_roof_teal_trim',70,11.20,20.40,9.8,.12,.16,TEAL,col)
    box('Control_tinted_facade',71,10.94,18.1,6.2,.10,1.25,GLASS,col)
    if detail == 0:
        for x in (68.9,71,73.1):
            box('Window_mullion',x,11.02,18.05,.07,.10,1.35,SILVER,col)
        for x in (67.3,71.7):
            box('Roof_vent',x,6.2,20.75,1.9,2.4,.45,STEEL,col,.05)
            for j in range(5):
                box('Vent_louvre',x,5.2+j*.45,21.2,1.75,.07,.045,SILVER,col)
        box('Maintenance_door',66.4,10.95,17.1,1.3,.14,2.5,STEEL,col)
        # A restrained identification plaque, part of the asset rather than a billboard.
        box('MRP_plaque',70,11.035,17.35,2.7,.08,.55,TEAL,col)
    return col

def make_fish_passage():
    """Side-mounted, pool-and-slot fishway upgrade; standalone 75 m game asset."""
    col = collection('UPGRADE_FISH_PASSAGE_LOD0')
    # The bypass sits beside the left abutment (negative X). Its long, shallow
    # channel connects the two river faces and drops about 8 m over 90 m.
    cx = -10.0
    y0, y1 = -32.0, 58.0
    z0, z1 = 14.0, 6.0
    count = 15
    # Trough base and retaining walls follow the grade.
    beam('Fishway_sloping_base', (cx,y0,z0-.3), (cx,y1,z1-.3), 6.0,.60, DARK_CONCRETE,col)
    for x in (cx-3.15,cx+3.15):
        beam('Fishway_sidewall', (x,y0,z0), (x,y1,z1), .48,.48, CONCRETE,col)
        beam('Fishway_rail_postline', (x,y0,z0+1.1), (x,y1,z1+1.1), .12,.12, STEEL,col)
        # Modular handrail sections keep the 90 m run visually light.
        for i in range(19):
            t=i/18
            yy=y0+(y1-y0)*t; zz=z0+(z1-z0)*t+0.25
            box('Fishway_guardrail_post',x,yy,zz,.12,.12,.9,STEEL,col)
    for i in range(count):
        t=(i+.5)/count
        yy=y0+(y1-y0)*t; zz=z0+(z1-z0)*t
        # Alternating side-slot baffles make a visible pool-and-slot path.
        gap_side = -1 if i%2==0 else 1
        width=5.05
        center=cx + (gap_side * .95)
        box('Fishway_pool_baffle',center,yy,zz-.12,width,.38,.95,TEAL,col,.035)
        # Open slot rim and a visible downstream sill.
        box('Fishway_slot_lip',cx+gap_side*2.30,yy,zz+.76,.60,.42,.12,SILVER,col)
    for yy,zz,name in ((y0,z0,'Upstream_intake'),(y1,z1,'Downstream_outlet')):
        box('Fishway_'+name+'_basin',cx,yy,zz-.45,9.0,9.0,.55,DARK_CONCRETE,col,.10)
        for x in (cx-4.6,cx+4.6):
            box('Fishway_'+name+'_wingwall',x,yy,zz-.1,.5,9.0,1.8,CONCRETE,col,.06)
    # Inspection walk and a small modern gate/flow-control cabinet.
    box('Fishway_inspection_walk',cx+4.5,13.0,9.1,1.2,18,.22,DECK,col,.04)
    for yy in (4.0,22.0):
        box('Fishway_access_step',cx+4.5,yy,9.2,2.2,1.6,.25,CONCRETE,col,.04)
    box('Fishway_flow_control_cabinet',cx+5.2,-25,13.0,1.4,2.0,2.0,TEAL,col,.06)
    return col

def make_automatic_spillway():
    """Crest-mounted automatic flap-gate and sensor upgrade for one 75 m span."""
    col = collection('UPGRADE_AUTOMATIC_SPILLWAY_LOD0')
    # Four independent crest leaves, actuator arms, and serviceable hinge seats.
    for i,cx in enumerate((9.0,27.5,46.0,64.5),1):
        box('AutoSpillway_hinge_seat',cx,-9.65,16.9,15.0,1.4,.55,DARK_CONCRETE,col,.06)
        # Raised plate on the upstream crest; understated teal finish matches gates.
        box('AutoSpillway_flap_leaf',cx,-9.20,17.15,14.2,.34,1.55,TEAL,col,.045)
        box('AutoSpillway_top_edge',cx,-9.20,18.72,14.35,.42,.16,SILVER,col,.025)
        for side in (-1,1):
            px=cx+side*5.7
            box('AutoSpillway_actuator_pedestal',px,-7.35,17.0,.9,1.0,1.2,STEEL,col,.05)
            beam('AutoSpillway_hydraulic_cylinder',(px,-7.35,17.6),(px,-9.0,18.55),.42,.42,SILVER,col)
            beam('AutoSpillway_piston_rod',(px,-8.5,18.18),(px,-9.1,18.53),.16,.16,TEAL,col)
    # One water-level sensor mast and compact control cabinet identify the upgrade.
    box('AutoSpillway_sensor_base',2.2,-7.0,17.0,1.2,1.2,.6,DARK_CONCRETE,col,.05)
    box('AutoSpillway_sensor_mast',2.2,-7.0,17.6,.16,.16,4.8,STEEL,col)
    box('AutoSpillway_sensor_head',2.2,-7.0,22.35,.9,.9,.55,SAFETY,col,.06)
    box('AutoSpillway_control_cabinet',72,-6.8,17.0,2.2,2.4,2.6,CONCRETE,col,.08)
    box('AutoSpillway_control_roof',72,-6.8,19.62,2.5,2.7,.24,TEAL,col,.04)
    box('AutoSpillway_status_lamp',72,-8.2,19.0,.32,.32,.32,SAFETY,col,.04)
    return col

lod_cols = [make_module(i) for i in range(3)]
fish_passage_col = make_fish_passage()
automatic_spillway_col = make_automatic_spillway()

def finish(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    for modifier in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    # Recalculate closed-solid winding, including the custom prisms.
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    for old_layer in list(obj.data.uv_layers):
        obj.data.uv_layers.remove(old_layer)
    uv = obj.data.uv_layers.new(name='UV0_Tileable')
    for face in obj.data.polygons:
        n = face.normal
        for loop in face.loop_indices:
            v = obj.data.vertices[obj.data.loops[loop].vertex_index].co
            if abs(n.z) > .7: coord = (v.x/4, v.y/4)
            elif abs(n.x) > .7: coord = (v.y/4, v.z/4)
            else: coord = (v.x/4, v.z/4)
            uv.data[loop].uv = coord

for col in lod_cols:
    for obj in list(col.objects): finish(obj)

def merged_copy(col, name):
    copies = []
    export_col = collection(name)
    for source in col.objects:
        obj = source.copy(); obj.data = source.data.copy()
        export_col.objects.link(obj); copies.append(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in copies: obj.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    bpy.ops.object.join()
    merged = bpy.context.object
    merged.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # A separate unique UV set is useful when importing/baking; UV0 remains tileable.
    merged.data.uv_layers.new(name='UV1_Lightmap')
    merged.data.uv_layers.active_index = 1
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.01)
    bpy.ops.object.mode_set(mode='OBJECT')
    merged.data.uv_layers.active_index = 0
    triangulate = merged.modifiers.new('Export triangles with tangent-space normals', 'TRIANGULATE')
    if hasattr(triangulate, 'keep_custom_normals'):
        triangulate.keep_custom_normals = True
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    return merged, export_col

modules = [merged_copy(c, f'Dam_Module75_LOD{i}') for i,c in enumerate(lod_cols)]
full_col = collection('DAM_ASSEMBLY_300m')
for i in range(4):
    obj = modules[0][0].copy()
    full_col.objects.link(obj)
    obj.name = f'Dam_Unit_{i+1:02d}_75m'
    obj.location.x = i*75

def select_objects(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]

def export(name, objects):
    select_objects(objects)
    bpy.ops.export_scene.gltf(filepath=str(OUT/(name+'.glb')), export_format='GLB', use_selection=True,
                              export_apply=True, export_yup=True, export_texcoords=True,
                              export_normals=True, export_tangents=True, export_materials='EXPORT')
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')), use_selection=True, object_types={'MESH'},
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                             mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
                             path_mode='RELATIVE', embed_textures=False)

for i,(obj,col) in enumerate(modules):
    export(f'Dam_Module75_LOD{i}', [obj])
export('Dam_Assembly300_LOD0', list(full_col.objects))
fish_passage_mesh, fish_passage_export_col = merged_copy(fish_passage_col, 'FishPassage_Upgrade_LOD0')
spillway_mesh, spillway_export_col = merged_copy(automatic_spillway_col, 'AutomaticSpillway_Upgrade_LOD0')
export('FishPassage_Upgrade_LOD0', [fish_passage_mesh])
export('AutomaticSpillway_Upgrade_LOD0', [spillway_mesh])

# Render-only assembly with upgrade meshes attached; game exports above remain
# independent so each future upgrade can be imported/placed separately.
upgrade_preview_col = collection('RENDER_UPGRADES_INSTALLED_NOT_EXPORTED')
def preview_copy(source, name, x_offset=0):
    obj=source.copy(); obj.data=source.data.copy(); obj.name=name
    upgrade_preview_col.objects.link(obj); obj.location.x += x_offset
    return obj
preview_copy(fish_passage_mesh,'Preview_FishPassage_Installed')
for i in range(4): preview_copy(spillway_mesh,f'Preview_AutoSpillway_Unit_{i+1:02d}',i*75)

collision_col = collection('COLLISION_PROXY')
box('Collision_foundation',37.5,5,0,75,34,1,DARK_CONCRETE,collision_col)
box('Collision_closed_dam',37.5,0,1,75,11,16,DARK_CONCRETE,collision_col)
box('Collision_deck',37.5,4,16.1,75,6.4,.9,DARK_CONCRETE,collision_col)
export('Dam_Module75_Collision', list(collision_col.objects))

def stats(objects):
    triangles = vertices = 0
    lower = [float('inf')]*3; upper = [-float('inf')]*3
    for obj in objects:
        obj.data.calc_loop_triangles()
        triangles += len(obj.data.loop_triangles); vertices += len(obj.data.vertices)
        for v in obj.data.vertices:
            p = obj.matrix_world @ v.co
            for j in range(3): lower[j] = min(lower[j],p[j]); upper[j] = max(upper[j],p[j])
    return {'vertices':vertices,'triangles':triangles,'bounds_blender_z_up':{'min':lower,'max':upper}}

manifest = {'asset':'Mun River Modern Dam','version':'1.1','units':'metres','export_up_axis':'Y',
    'nominal_module_length':75,'nominal_assembly_length':300,'structural_crest_height':17,
    'design':'Original modern low-head dam inspired by the user reference, not a survey replica.',
    'status':'Standalone editable model. NOT imported into the game or installed mod.',
    'upgrades':{
        'fish_passage':{'asset':'FishPassage_Upgrade_LOD0','triangles':stats([fish_passage_mesh])['triangles'],
            'bounds_blender_z_up':stats([fish_passage_mesh])['bounds_blender_z_up'],
            'description':'Side-mounted pool-and-slot bypass with alternating baffles, two basins, access walk and handrails.'},
        'automatic_spillway':{'asset':'AutomaticSpillway_Upgrade_LOD0','triangles':stats([spillway_mesh])['triangles'],
            'bounds_blender_z_up':stats([spillway_mesh])['bounds_blender_z_up'],
            'description':'Four crest flap leaves with hydraulic actuators, level sensor mast and control cabinet.'}},
    'excluded':['external powerhouse building','roads','powerlines'],
    'lods':{f'LOD{i}':stats([o]) for i,(o,c) in enumerate(modules)},
    'assembly':stats(list(full_col.objects)), 'collision':stats(list(collision_col.objects))}
(OUT/'mesh-info.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print('MODEL_EXPORTS_COMPLETE',json.dumps(manifest['lods']),flush=True)

# Studio/render context lives in its own collection and is excluded from every export.
context = collection('RENDER_CONTEXT_NOT_EXPORTED')
floor = material('Studio_ground',(.24,.30,.31),.95)
box('Presentation_ground',150,10,-1,650,500,1,floor,context)
water_mat = material('Context_water',(.025,.16,.19),.24,.2)
for name,y,depth,height in [('Upstream',-55,109,14.0),('Downstream',68.5,123,5.0)]:
    obj=box(name+'_water_context',150,y,0,312,depth,height,water_mat,context)
    obj.hide_render=True
    obj.hide_viewport=True
nodes = water_mat.node_tree.nodes
noise = nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value = 2.5
bump = nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.12; bump.inputs['Distance'].default_value=.12
water_mat.node_tree.links.new(noise.outputs['Fac'],bump.inputs['Height'])
water_mat.node_tree.links.new(bump.outputs['Normal'],nodes.get('Principled BSDF').inputs['Normal'])
lights = collection('STUDIO_LIGHTS_CAMERA')
world = bpy.data.worlds.new('Soft studio environment')
scene.world = world; world.use_nodes=True
world.node_tree.nodes.get('Background').inputs[0].default_value=(.55,.66,.70,1)
world.node_tree.nodes.get('Background').inputs[1].default_value=.45
def area(name,position,power,size):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);lights.objects.link(obj);obj.location=position
    obj.rotation_euler=(Vector((150,0,8))-obj.location).to_track_quat('-Z','Y').to_euler()
area('Large_softbox',(120,90,150),1300000,180)
area('Cool_fill',(50,-80,90),450000,110)
sun_data=bpy.data.lights.new('Soft_sun','SUN');sun_data.energy=2.2;sun_data.angle=math.radians(18)
sun=bpy.data.objects.new('Soft_sun',sun_data);lights.objects.link(sun);sun.rotation_euler=(math.radians(24),math.radians(-25),math.radians(-30))
camera_data=bpy.data.cameras.new('Presentation_camera');camera=bpy.data.objects.new('Presentation_camera',camera_data)
lights.objects.link(camera);scene.camera=camera;camera_data.type='ORTHO';camera_data.lens=50;camera_data.clip_end=2000
def view(position,target,scale):
    camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.ortho_scale=scale
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=32
scene.cycles.use_denoising=True
scene.render.threads_mode='FIXED';scene.render.threads=12
scene.render.resolution_x=2200;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='AgX'
scene.view_settings.look='AgX - Medium High Contrast'
scene.render.film_transparent=False
for col in lod_cols+[c for o,c in modules]+[collision_col,fish_passage_col,automatic_spillway_col,
        fish_passage_export_col,spillway_export_col]: col.hide_render=True;col.hide_viewport=True
view((340,340,225),(150,5,8),345)
# Keep a useful full-assembly view as the default editable file.
bpy.ops.object.select_all(action='DESELECT')
for obj in full_col.objects: obj.select_set(True)
bpy.context.view_layer.objects.active=list(full_col.objects)[0]
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.clip_end=3000
            area.spaces.active.region_3d.view_location=(150,5,8)
            area.spaces.active.region_3d.view_rotation=camera.rotation_euler.to_quaternion()
            area.spaces.active.region_3d.view_distance=250
            area.spaces.active.region_3d.view_perspective='ORTHO'
bpy.ops.file.make_paths_relative()
for image in bpy.data.images:
    if image.source=='FILE': image.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ModernDam.blend'))
if '--skip-render' in sys.argv:
    print('MODEL_EXPORTS_COMPLETE_SKIP_RENDER',flush=True)
    sys.exit(0)
scene.render.filepath=str(OUT/'Preview_300m.png')
bpy.ops.render.render(write_still=True)
print('HERO_RENDER_COMPLETE',flush=True)
upgrade_preview_col.hide_render=False
scene.render.filepath=str(OUT/'Preview_Upgrades_Installed_300m.png')
bpy.ops.render.render(write_still=True)
print('UPGRADE_HERO_RENDER_COMPLETE',flush=True)
upgrade_preview_col.hide_render=True
full_col.hide_render=True
modules[0][1].hide_render=False
for obj in context.objects:
    if 'water_context' in obj.name: obj.hide_render=True
view((109,100,67),(37.5,2,10),91)
scene.render.resolution_x=1800;scene.render.resolution_y=1200
scene.render.filepath=str(OUT/'Preview_Module75.png')
bpy.ops.render.render(write_still=True)
print('MODULE_RENDER_COMPLETE',flush=True)
view((37.5,118,13),(37.5,0,12),84)
scene.render.resolution_x=1900;scene.render.resolution_y=700
scene.render.filepath=str(OUT/'Preview_Front75.png')
bpy.ops.render.render(write_still=True)
modules[0][1].hide_render=True
def setup_upgrade_preview(feature, filename, target, scale):
    showcase=collection('RENDER_'+filename)
    base=modules[0][0].copy(); base.data=modules[0][0].data.copy(); showcase.objects.link(base)
    add=feature.copy(); add.data=feature.data.copy(); showcase.objects.link(add)
    view((target[0]+110,target[1]+90,target[2]+60),target,scale)
    scene.render.resolution_x=1900;scene.render.resolution_y=1200
    scene.render.filepath=str(OUT/filename)
    bpy.ops.render.render(write_still=True)
    showcase.hide_render=True
setup_upgrade_preview(fish_passage_mesh,'Preview_FishPassage_Upgrade.png',(5,10,7),135)
setup_upgrade_preview(spillway_mesh,'Preview_AutomaticSpillway_Upgrade.png',(37.5,0,12),105)
print('ALL_MODEL_OUTPUTS_COMPLETE',flush=True)
