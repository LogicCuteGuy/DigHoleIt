# Performance and limits

## Recommended settings

| Setting | PC | Quest |
|---|---|---|
| Voxel size | 0.5 m | 0.5–0.75 m |
| Chunk cells | 16 | 8–12 |
| Zone size | 64 × 32 × 64 cells or smaller | about 32 × 16 × 32 m |
| Material | `DigHoleIt/DigTerrain` (Standard, normal maps) | `DigHoleIt/DigTerrain Lite` (Lambert, about 9 samples) |

Prefer several small zones to one large one, especially for Quest.

## Measurements

Measured in the editor (ClientSim, PC): remeshing one 16³ chunk costs about **14–17 ms of Udon time**. At the default budget that is roughly 6 frames per chunk. This has not been measured on a Quest device yet. Expect it to be several times slower there, which is why smaller chunks are recommended.

## Memory

- **In a player's memory (VRChat):** the compressed grid, a few small arrays per chunk, and each chunk that edits have reached, decoded: `(chunkCells+2)³` bytes, plus the same for paint where something is painted. A zone nobody digs costs little more than its compressed size.
- **On disk:** the data asset (current and as-baked grid, paint) and the Udon fields in the scene are stored run-length compressed. A terrain grid shrinks to about 2–5 % of its raw size; the inspector shows both sizes.
- **Chunk objects:** only chunks with a surface get a GameObject, mesh and collider. For example, a 490 × 406 × 401 zone with 13-cell chunks has 37,696 chunks, of which 2,651 have a surface. Each of those is a draw call when it is in view.
- **Editor and standalone:** the editor and `DigZoneRuntimeStandalone` hold the whole grid, one byte per sample (plus paint).
- A zone can have at most 128 Mi samples (`DigZone.MaxSamples`). The inspector warns above 16 million, because baking and sculpting get slow in the editor.

## Limits

- One Terrain per zone. Zones are axis-aligned; rotation and scale are reset on bake.
- `DigSync.capacity` caps edits per instance (default 4096, 32 KB of synced log). Late joiners replay every edit in Udon, time-sliced, so large logs take a while to catch up.
- An edit sent to an owner who leaves before relaying it is lost. The sender keeps its local prediction.
- If the owner leaves while the next owner is still missing some edits, the two can assign different edits to the same sequence number and drift apart. Players who join later all get the new owner's log.
- Unpainted added soil is shaded with the terrain layers above the original surface and with dug soil below it. Give it a layer (`addLayer`) to control this.
- Paint has voxel resolution: edges blend over about one voxel.
- Zone shading uses up to 16 terrain layers (8 with DigTerrain Lite, 4 with the Standard shader on mobile). Each extra layer costs where it is visible: pixels only sample layers with weight. Each chunk mesh holds up to 4 painted layers.
- Runtime-dug chunks are lit by light probes, not lightmaps.
- The terrain material must support holes. Unity's default built-in terrain material does.
