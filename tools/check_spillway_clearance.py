"""Check the authored spillway footprint against the rendered traffic deck."""

import json
import re
import struct
from pathlib import Path


root = Path(__file__).resolve().parents[1]
glb = (root / "assets/ModernDam/AutomaticSpillway_Upgrade_LOD0.glb").read_bytes()
magic, version, total = struct.unpack_from("<III", glb)
assert magic == 0x46546C67 and version == 2 and total == len(glb)
json_length, json_type = struct.unpack_from("<I4s", glb, 12)
assert json_type == b"JSON"
document = json.loads(glb[20 : 20 + json_length])

source = (root / "src/MunRiverPower/DamSceneVisuals.cs").read_text()
assert re.search(r"if\s*\(file == Gate\)\s*lateral = p\.z;", source)
road = re.search(r'DrawRoad\((-?[\d.]+)f,\s*(-?[\d.]+)f,\s*\.16f,\s*"road-asphalt"', source)
assert road, "Traffic deck bounds were not found"
road_min, road_max = map(float, road.groups())

# Blender exports glTF in right-handed coordinates. ReadGlb flips the Z axis
# when creating the Unity mesh, so Unity lateral = -glTF Z for this upgrade.
z_min = min(
    document["accessors"][primitive["attributes"]["POSITION"]]["min"][2]
    for mesh in document["meshes"]
    for primitive in mesh["primitives"]
)
z_max = max(
    document["accessors"][primitive["attributes"]["POSITION"]]["max"][2]
    for mesh in document["meshes"]
    for primitive in mesh["primitives"]
)
spillway_min, spillway_max = -z_max, -z_min
clearance = road_min - spillway_max
print(f"SPILLWAY_CLEARANCE road=[{road_min:.2f},{road_max:.2f}] spillway=[{spillway_min:.2f},{spillway_max:.2f}] margin={clearance:.2f}m")
assert clearance >= 0.5, "Spillway mesh obstructs the road"
