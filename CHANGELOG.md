# Changelog

## [0.3.0] - 2026-09-27

### Added
- Zones follow terrain edits: raising, lowering or painting the terrain under a baked zone (Unity terrain tools, scripts or undo) updates it when the stroke ends. Unsculpted voxels take the new terrain, sculpted ones and paint are kept; a zone the terrain leaves is fitted and re-baked. **Follow terrain edits** toggle in the zone inspector. `DigZoneBaker.SyncWithTerrain`, `DigZoneBaker.IsUpToDate`.
- **Reset** brush mode (sculpt tool and DigHoleIt: Dig Voxels): brushes voxels back to the baked terrain and wipes their paint.

### Changed
- `Runtime/Standalone` compiles only outside VRChat projects (`!VRC_SDK_VRCSDK3 || DIGHOLEIT_STANDALONE`), and the standalone demo menu is hidden there. The Udon runtime still compiles only with the VRChat SDK.
- Documentation moved to `Documentation~`; the README is now an overview with install steps. MIT license. `package.json` links the repository, docs, changelog and license, and no longer lists an email.

## [0.2.0] - 2026-09-27

### Added
- Voxel texture painting: a per-sample paint grid (auto, terrain layers 0-3, dug soil), blended into vertex colours by the mesher and into both shaders. Paint is a networked edit op (`DigFormat.OpPaint`), and Add can give the soil it adds a layer.
- `DigTool` paint mode (`mode = 2`, `paintLayer`, `addLayer`) and `_LocalEditLayer`; the VRChat demo has a paint shovel. `DigZoneRuntimeStandalone.Paint` and middle-button painting in `DigToolStandalone`.
- Terrain tools in the Terrain component's Paint Terrain dropdown: **DigHoleIt: Dig Voxels** (dig / add / smooth) and **DigHoleIt: Paint Voxels**, using Unity's terrain brushes, plus a Create Dig Zone button.
- Shaped brushes with strength for the sculpt tool (Sphere, Soft, Flat, custom mask texture), brush axis option, and a Scene view overlay. Hold A or S and drag to change size or strength.
- Terrain tools list every Dig Zone on the terrain with its full inspector, and show zone boxes, diggable areas and resize handles in the Scene view.
- Deleting a Dig Zone fills its terrain hole back in (never cells another zone still needs); **Delete Zone** button; **Fix Leftover Holes** for zones deleted earlier. Duplicated zones get their own data and material on first bake.
- Border Voxels is limited to 2-16, and a bake that fails says whether the zone is too small or Border Voxels too large.
- Box handles to move and resize a Dig Zone in whole voxels (coloured cube per face, size label); the zone re-bakes on release and stays on the terrain.
- Re-baking keeps sculpting and paint. Edits that fall outside the zone after a resize are stashed and come back when the zone covers them again. **Reset To Terrain** discards them.
- Editor Dig/Add is a smooth push/pull brush (like terrain Raise/Lower) that keeps working while the mouse is held.

### Changed
- `DigBrush.Stamp` takes the paint grid and a layer. `ChunkMesher.Build` takes the paint grid. Chunk meshes always carry vertex colours and uv0 (paint weights).
- Fit To Terrain stays on the zone's voxel lattice.
- Surface Nets splits each quad along its shorter diagonal (fewer saw-tooth folds on sharp rims). `SurfaceNets.BuildQuads` takes the vertex positions.
- A bake that fails the terrain-hole check no longer touches the existing data.
- Moving a baked zone with the Move tool snaps it to its voxel lattice.

### Upgrading from 0.1
- Bake each zone once (sculpting is kept). Chunk meshes baked by 0.1 have no paint weights.

## [0.1.0] - 2026-09-27

### Added
- Dig Zone authoring component, baker (terrain → SDF grid, terrain hole cut/restore, splat and height textures, chunk meshes) and inspector.
- Shared Surface Nets mesher and CSG sphere brush (`Runtime/Shared`), compiled into both Udon (LCGUdonSharp) and plain C#.
- VRChat runtime: `DigZoneRuntime` (time-sliced edits and meshing), `DigSync` (ordered edit log, late-joiner replay, reset), `DigTool` pickup shovel.
- Standalone runtime: `DigZoneRuntimeStandalone` with netcode hooks, edit log save/load, grid raycast; `DigToolStandalone`.
- `DigHoleIt/DigTerrain` (Standard) and `DigHoleIt/DigTerrain Lite` (Quest) shaders matching terrain layers.
- Scene-view sculpt tool (dig, add, smooth) with undo.
- Demo scene builders for VRChat and standalone.
- EditMode tests.
