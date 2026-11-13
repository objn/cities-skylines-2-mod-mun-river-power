import bpy
from mathutils import Vector
from pathlib import Path

source = Path(__file__).resolve().parents[1] / 'assets' / 'ModernDam' / 'ModernDam.blend'
bpy.ops.wm.open_mainfile(filepath=str(source))
collection = bpy.data.collections['MODULE_75m_LOD0']
road_min_y, road_max_y = -9.8, -0.2
road_z = 16.8
obstacles = []
for obj in collection.objects:
    if obj.type != 'MESH':
        continue
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    lower = [min(c[i] for c in corners) for i in range(3)]
    upper = [max(c[i] for c in corners) for i in range(3)]
    if lower[1] < road_max_y and upper[1] > road_min_y and upper[2] > road_z:
        obstacles.append((obj.name, tuple(round(v, 2) for v in lower), tuple(round(v, 2) for v in upper)))
print('ROAD_CLEARANCE_OBSTACLES', obstacles, flush=True)
assert not obstacles, 'Dam geometry intrudes into the 9.6 m road clearance above the deck.'
