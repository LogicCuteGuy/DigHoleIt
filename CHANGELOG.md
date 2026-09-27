# Changelog

## [0.4.0] - 2026-09-28

### Added
- Grid compression: `DigZoneData` and `DigZoneRuntime` store the grids run-length encoded (`DigRle` decoder in Runtime/Shared, also compiled into Udon; `DigRleEncoder` in Runtime/Core). A terrain grid shrinks to about 2–5 %. The zone inspector shows memory and stored sizes.
- Per-chunk grids in the VRChat runtime (`DigChunkPacker`, `DigRle.DecodeChunk`, `DigFormat.ChunkSamples`): nothing is decoded at load, a chunk is decoded the first time an edit reaches it, and `_ResetToOriginal` just drops the decoded chunks and restores the baked meshes. A player's memory grows with the area that gets dug, not with the zone size. `_DecodedChunkCount()`.
- Only chunks with a surface get a GameObject, mesh sub-asset and collider (`DigZone.chunkIds`); runtimes and editor sculpting copy the inactive Chunk Template when a chunk gains a surface.
- `DigZoneData.MarkChanged(box)`: sculpt strokes re-encode only the chunks they touched when the VRChat runtime is updated.
- Terrain layers follow the terrain: up to 16 (was 4), with one splat texture per four layers and shader variants for 4, 8, 12 or 16 layers (`_DIGLAYERS_*`). DigTerrain Lite shades up to 8. Paint values 6–17 are terrain layers 4–15 (`DigFormat.PaintValue`, `DigFormat.TerrainLayerOf`, `DigFormat.MaxTerrainLayers`); the layer picker lists every terrain layer.
- Paint slots: each chunk mesh holds up to 4 painted terrain layers plus dug soil (`SurfaceNets.SlotCount`, `ResetSlots`, `FinishPaint`); uv0.y carries the slots' layers.
- Baked lighting: **Baked Lighting** on the zone (on by default) gives chunk meshes lightmap UVs and marks them Contribute GI. Runtimes switch a chunk they remesh to light probes and restore its lightmap on reset. **Add Light Probes** places a Light Probe Group above the terrain over the zone. The shaders darken dug areas with depth (**Darkening Below Surface**, **Darkening Depth**). Chunks take the terrain's Scale In Lightmap (at least 16 texels across a chunk) times the zone's **Lightmap Scale**, and bake re-applies the lighting settings of every zone when a lighting bake starts.
- Baked chunk meshes are cut to the terrain hole (`ChunkMesher.ClipXZ`). The part outside was hidden by the shader but still shadowed the terrain in the lightmapper (dark lines along the hole edge) and took lightmap space.
- Holding `[` or `]` keeps shrinking or growing the brush; a tap still changes it by 10%.
- A "DigHoleIt · by LogicCuteGuy" credit line (with the package version, linking to the project page) at the bottom of every DigHoleIt inspector, the Terrain tool panels and the brush overlay (`DigCredit`).
- **Add VRChat Runtime** / **Add Standalone Runtime** buttons (with **Remove**) in the zone inspector, and `DigZoneEditor.RuntimeGUI` for runtime backends. `DigUdonBridge.AddRuntime` / `RemoveRuntime`.

### Changed
- Creating or baking a zone no longer adds `DigZoneRuntime` and `DigSync`. Add them with the button; after that, bakes and sculpting keep them up to date. The VRChat demo scene still adds them.
- An edit's layer field is 5 bits (bits 59-63), so the largest paint values set the sign bit. Layer values 0-5 keep their meaning.
- `SurfaceNets.BuildPaint` takes a paint slot array. `DigZoneData.extraControlTex` and `layerCount`.
- DigTerrain Lite targets shader model 3.5 (GLES3 / Vulkan), for the paint slot interpolator.
- `DigZoneRuntimeStandalone.ResetToBaked` puts the baked meshes back instead of remeshing every chunk.
- The grid limit is now 128 Mi samples (`DigZone.MaxSamples`, was 16 million), and the error says how to fix it. The inspector warns above 16 million.
- `DigZoneRuntime.grid` / `paint` are replaced by `chunkRle` / `chunkOffsets` / `paintRle` / `paintOffsets`, plus `chunkIds` and `chunkTemplate`; the chunk arrays list only chunks with an object. `DigZoneData.grid`, `paint` and `baseGrid` are no longer serialized directly. Code that changes a grid in place must call `MarkChanged` or bump `gridVersion`.
- `DigZone.chunkFilters` / `chunkRenderers` / `chunkColliders` are indexed through `DigZone.ChunkSlot(ci)`, and `DigZoneData.chunkMeshes` has null entries for chunks without a surface.
- The repository moved to https://github.com/LogicCuteGuy/DigHoleIt (the package name stays `com.logiccuteguy.digholeit`). Git URLs with the old name still redirect.
- A bake records only the zones whose terrain holes overlap its own for undo. It used to record every zone on the terrain, which took minutes when one of them was large.

### Upgrading from 0.3
- Data assets are read in the old format and saved compressed the next time they change, and `DigZoneRuntime`s are converted when their scene opens (save the scene to keep it).
- Bake each zone once to drop the objects and meshes of empty chunks, pick up terrain layers beyond 4 and get lightmap UVs (sculpting and paint are kept). Until then everything still works. Then bake the scene's lighting if it uses baked lights.

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
