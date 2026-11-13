# 3D models for dam upgrades

These are source models for integration with Mun River Power. Edit `ModernDam.blend` and export FBX and GLB files. GLB files contain their materials and textures.

## Fish Passage

- `FishPassage_Upgrade_LOD0.fbx` / `.glb`
- A concrete channel beside the pier, with alternating pools and water slots, an inspection rail, and inlet and outlet basins.
- Designed as one piece alongside the dam. Its 90 m channel illustrates a gradual ascent.
- Approximately 996 triangles.

## Automatic Spillway

- `AutomaticSpillway_Upgrade_LOD0.fbx` / `.glb`
- Four crest gate assemblies per 75 m module, with hydraulic equipment, a water level sensor pole, and a control cabinet.
- Approximately 504 triangles.

## Scope of these source files

These files contain art meshes and materials. This source set alone does not attach them to upgrade prefabs or positions in the game, so opening the files or merely updating the mod will not display them. The installed-upgrade preview illustrates intended placement; it is not an export of a combined dam and upgrade model. The current mod embeds smaller upgrade meshes; see [SPECIFICATION.md](../../SPECIFICATION.md) for integration details.

Units are meters. FBX and GLB exports use Y as the up axis. The fish passage geometry is a visual concept, not a construction drawing or a survey of the real dam.
