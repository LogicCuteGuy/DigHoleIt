# How it works

```
Terrain ──Bake──► DigZoneData (byte SDF grid, chunk meshes, splat/height textures)
                     │
      ┌──────────────┴──────────────┐
DigZoneRuntime (U#)          DigZoneRuntimeStandalone (C#)
      └───────► DigBrush.Stamp + SurfaceNets (Runtime/Shared) ◄───────┘
```

## Grid

- One byte per sample, `(cells.x+1)·(cells.y+1)·(cells.z+1)` samples.
- Each byte is a signed distance to the surface in 1/64 voxel, clamped to ±2 voxels. Values below 128 are solid.
- The data asset stores two copies: the current grid and the grid as baked from the terrain. Re-bakes and terrain follow compare the two to tell sculpted samples from untouched ones.

## Paint grid

One byte per sample, same layout: 0 auto, 1–4 terrain layers 0–3, 5 dug soil. A zone that was never painted stores no paint grid in the scene.

## Edits

An edit is packed into one `long`:

- position in 1/16 voxel;
- radius in 1/8 voxel;
- op (dig, add, paint);
- paint layer.

Dig is `max(d, -sphere)` and Add is `min(d, sphere)`. Paint sets the layer inside the sphere. All three are idempotent, so applying an edit twice changes nothing. Edits only touch samples inside the zone's edit box, at least Border Voxels inside the terrain hole.

## Meshing

- Naive Surface Nets, one mesh per chunk.
- Normals come from the SDF gradient, so chunk borders have no lighting seams.
- Each quad is split along its shorter diagonal, which avoids saw-tooth folds on sharp rims.
- Paint becomes per-vertex weights, interpolated over the cell's corners: vertex colour holds terrain layers 0–3 and uv0.x holds dug soil.
- `Runtime/Shared` is registered as an UdonSharp assembly. LCGUdonSharp compiles the same static methods into Udon that the C# runtime calls directly.

## Shading

`DigHoleIt/DigTerrain` blends the terrain's four layers from the baked splat map, with triplanar mapping and normal maps. Below the original surface (from the baked height map) it uses the dug soil material. The shader clips the mesh outside the terrain hole, so the voxel surface meets the terrain edge exactly.

`DigHoleIt/DigTerrain Lite` is a Lambert version with about nine texture samples, for Quest.

## Udon frame budget

Queued edits and meshing share `budgetMsDesktop` (2.5 ms) or `budgetMsMobile` (1.2 ms) per frame. The chunk nearest the player is meshed first.

## Networking

`DigSync` keeps an ordered, append-only edit log:

1. The digging player applies the edit locally right away (prediction) and asks the owner to append it.
2. The owner assigns the next sequence number and broadcasts the edit (`[NetworkCallable]`).
3. Every client applies edits strictly in sequence order and buffers any that arrive early. Re-applying the predicted edit changes nothing, because edits are idempotent.
4. Shortly after someone joins, the owner serializes the full log (Manual sync), and the late joiner replays it, time-sliced.
5. Every client keeps the full log, so an ownership handoff keeps working.

`_RequestReset()` starts a new epoch: everyone restores the baked grid and the log is cleared.

## Editor side

- `DigZoneBaker` bakes zones and raises `Baked` and `GridChanged`. In VRChat projects, `DigUdonBridge` listens and copies the data into `DigZoneRuntime`.
- `DigTerrainSync` listens to `TerrainCallbacks.heightmapChanged` and `textureChanged` and calls `DigZoneBaker.SyncWithTerrain` once a stroke ends.
- `DigTerrainHoles` records which terrain cells each zone cut, and fills them back in when zones are deleted.
