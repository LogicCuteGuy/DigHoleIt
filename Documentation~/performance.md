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

- Grid: `(cells.x+1)·(cells.y+1)·(cells.z+1)` bytes.
- The data asset stores it twice (current and as baked), and the scene stores it once more in the Udon field.
- A painted zone adds a paint grid of the same size. An unpainted zone stores none in the scene.
- The inspector warns above 2 million samples.

## Limits

- One Terrain per zone. Zones are axis-aligned; rotation and scale are reset on bake.
- `DigSync.capacity` caps edits per instance (default 4096, 32 KB of synced log). Late joiners replay every edit in Udon, time-sliced, so large logs take a while to catch up.
- An edit sent to an owner who leaves before relaying it is lost. The sender keeps its local prediction.
- If the owner leaves while the next owner is still missing some edits, the two can assign different edits to the same sequence number and drift apart. Players who join later all get the new owner's log.
- Unpainted added soil is shaded with the terrain layers above the original surface and with dug soil below it. Give it a layer (`addLayer`) to control this.
- Paint has voxel resolution: edges blend over about one voxel.
- Zone shading uses the first four terrain layers.
- The terrain material must support holes. Unity's default built-in terrain material does.
