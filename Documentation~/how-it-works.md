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

## Compression

Grids are saved run-length encoded (`DigRle`, `DigRleEncoder`): a varint sample count, then tokens that are either a run (length and one value) or literal bytes. Only runs of 8 or more are stored as runs, which keeps the token count low. A terrain grid is mostly solid (0) or air (255) with a thin surface band, so it shrinks to a few percent.

- `DigZoneData` saves the encoded grids and decodes them when the asset loads. It re-encodes only when a grid array is replaced or `gridVersion` is bumped, so code that edits a grid in place must bump `gridVersion`.
- `DigZoneRuntime` stores the grid per chunk (`DigChunkPacker`): each chunk's samples plus one sample on every side, which is all its mesh reads, encoded separately and joined into one blob with an offset per chunk. A chunk whose samples are all equal (all solid or all air) stores no stream, just its value. Udon decodes a chunk with `Array.Copy` per token (and log2(n) copies per run) the first time an edit reaches it.
- Neighbouring chunks share their border samples. Every edit is stamped into each chunk copy it reaches, so the copies stay identical and chunks still meet without cracks. Reset drops the decoded chunks and puts the baked meshes back.
- In the editor, sculpt strokes mark the sample box they changed (`DigZoneData.MarkChanged`), so only those chunks are encoded again when the runtime is updated.

## Paint grid

One byte per sample, same layout: 0 auto, 1–4 terrain layers 0–3, 5 dug soil, 6–17 terrain layers 4–15 (layers 0–3 and dug soil kept the values of earlier versions). A zone that was never painted stores no paint grid in the scene.

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
- Paint becomes per-vertex weights, interpolated over the cell's corners. Each chunk mesh has 4 paint slots: the first 4 terrain layers painted in the chunk. Vertex colour holds the slot weights, uv0.x the dug soil weight, and uv0.y the slots' terrain layers (4 bits each, the same on every vertex of the chunk). Meshes from earlier versions have 0 there, which reads as slots = layers 0–3.
- `Runtime/Shared` is registered as an UdonSharp assembly. LCGUdonSharp compiles the same static methods into Udon that the C# runtime calls directly.

## Shading

`DigHoleIt/DigTerrain` blends the terrain's layers (up to 16) from the baked splat maps (`_Control` for layers 0–3, `_Control1`–`_Control3` for the rest), with triplanar mapping and normal maps. Bake enables the `_DIGLAYERS_8`, `_DIGLAYERS_12` or `_DIGLAYERS_16` keyword for the terrain's layer count. A layer is only sampled where its weight is above zero, with explicit gradients so the branches don't disturb mipmapping, and all layer textures share one sampler. Below the original surface (from the baked height map) it uses the dug soil material, and darkens ambient light with depth. The shader clips the mesh outside the terrain hole, so the voxel surface meets the terrain edge exactly.

`DigHoleIt/DigTerrain Lite` is a Lambert version for Quest: up to 8 layers, each sampled once on the dominant axis plane, no normal maps. Mobile GPUs have 16 texture units, so on mobile the Standard shader shades only the first 4 layers; use Lite there.

## Lighting

Chunk objects are marked Contribute GI, Reflection Probe Static and Occludee Static when the zone's Baked Lighting is on (never Batching Static, since their meshes change, or Occluder Static, since digging opens views through the ground), and their meshes get lightmap UVs. Runtimes switch a chunk to light probes (`lightmapIndex = -1`) when they replace its mesh, and restore the baked lightmap index and scale/offset on reset. Chunks created at runtime are copies of the Chunk Template, which is never lightmapped.

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

- `DigZoneBaker` bakes zones and raises `Baked` and `GridChanged`. It creates chunk objects only for chunks with a surface, plus an inactive Chunk Template that runtimes copy when a chunk gains one; sculpting in the editor adds objects the same way. In VRChat projects, `DigUdonBridge` listens and copies the per-chunk data into the zone's `DigZoneRuntime`, if it has one. `DigZoneEditor.RuntimeGUI` is where the runtimes draw their Add/Remove buttons.
- `DigTerrainSync` listens to `TerrainCallbacks.heightmapChanged` and `textureChanged` and calls `DigZoneBaker.SyncWithTerrain` once a stroke ends.
- `DigTerrainHoles` records which terrain cells each zone cut, and fills them back in when zones are deleted.
