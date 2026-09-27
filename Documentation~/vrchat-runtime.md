# VRChat runtime

The VRChat runtime is written in UdonSharp and compiled with LCGUdonSharp. Bake the zone, then click **Add VRChat Runtime** in its inspector. That adds:

- `DigZoneRuntime` on the zone GameObject, which applies edits and remeshes chunks;
- `DigSync` on a child GameObject, which networks the edits.

From then on, every bake and sculpt stroke keeps them up to date. **Remove** next to the runtime label takes both off again.

The `DigZone` authoring component is `IEditorOnly` and is stripped at upload. Don't edit the baked fields on `DigZoneRuntime` by hand; bake or sculpt the zone instead.

The grid is stored on `DigZoneRuntime` per chunk, run-length compressed. Nothing is decoded when the world loads: a chunk is decoded the first time an edit reaches it (about 0.3–1 ms of Udon time for a 24³ chunk, within the frame budget), and until then it shows its baked mesh. So a player's memory grows with the area that gets dug, not with the zone size. Chunks without a surface have no GameObject; when digging or adding soil gives one a surface, the runtime copies the inactive **Chunk Template** under the zone's chunk root.

## DigTool

A pickup that digs, adds or paints where it points.

| Field | Meaning |
|---|---|
| `zones` | The `DigZoneRuntime`s this tool can edit. |
| `tip` | Ray origin and direction (forward). Defaults to the tool's own transform. |
| `layers` | Layers the ray can hit. Include the zone's Chunk Layer. |
| `reach` | Ray length in metres. |
| `radius` | Brush radius in metres (capped by the zone's Max Brush Radius). |
| `mode` | 0 dig, 1 add, 2 paint. |
| `paintLayer` | Layer painted in paint mode: 0 auto (erase paint), 1–4 terrain layers 0–3, 5 dug soil, 6–17 terrain layers 4–15 (`DigFormat.PaintValue(terrainLayer)`). |
| `addLayer` | Layer given to added soil, same values. 0 leaves it to Auto shading. |
| `interval` | Seconds between edits while Use is held. |
| `digIndicator`, `addIndicator`, `paintIndicator` | Optional objects shown for the current mode. |

Call `_ToggleMode()` to switch between dig and add, or `_NextMode()` to cycle dig, add, paint (for example from a UI button with `SendCustomEvent`).

## DigZoneRuntime

Methods other behaviours can call:

| Method | What it does |
|---|---|
| `_LocalEdit(Vector3 world, float radius, int op)` | Digs (op 0) or adds (op 1) at a world position. Goes through `DigSync` when there is one; otherwise the edit stays local. |
| `_LocalEditLayer(Vector3 world, float radius, int op, int layer)` | The same with a layer: the layer to paint for op 3 (paint), or the layer given to added soil for op 1. |
| `_ContainsWorld(Vector3 world)` | Whether a point lies inside the zone's box. |
| `_IsSolidAt(Vector3 world)` | Whether the grid is solid at a point. |
| `_IsReady()` / `_IsBusy()` | Whether the runtime has valid baked data, and whether edits or meshing are still pending. |
| `_DecodedChunkCount()` | Chunks decoded since the last reset (the ones edits have reached). |
| `_ResetToOriginal()` | Restores the baked grid locally: drops the decoded chunks and puts the baked meshes back, at once. To reset for everyone, use `DigSync._RequestReset()`. |

| Field | Meaning |
|---|---|
| `sync` | The zone's `DigSync`. Without it, edits stay local to each player. |
| `budgetMsDesktop` / `budgetMsMobile` | Milliseconds per frame for applying edits and meshing (default 2.5 / 1.2). The chunk nearest the player is meshed first. |
| `logTimings` | Logs meshing times to the console. |

Edit ops (`DigFormat`): `OpDig = 0`, `OpAdd = 1`, `OpPaint = 3`. `OpSmooth = 2` is editor only and is rejected at runtime.

## DigSync

`DigSync` keeps an ordered, append-only log of edits (see [How it works](how-it-works.md#networking)).

| Member | Meaning |
|---|---|
| `capacity` | Maximum edits per instance (default 4096, 8 bytes each). The full log must stay under the ~280 KB Manual sync limit. |
| `resetMasterOnly` | If set, only the instance master can reset the zone. |
| `_RequestReset()` | Resets the zone to its baked state for everyone. |
| `_GetCount()` / `_IsFull()` | Edits in the log, and whether the log is full. When it is full, further edits are dropped until the zone is reset. |

`DigSync` must stay on its own GameObject: it uses Manual sync, and the zone runtime uses no variable sync.

## Creating the Udon program assets

If a `.asset` program file for one of the scripts goes missing, run **Tools > DigHoleIt > Create Missing U# Program Assets**.
