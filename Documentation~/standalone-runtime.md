# Standalone runtime

The standalone runtime is plain C# (`LogicCuteGuy.DigHoleIt.Standalone`). It compiles in any project without the VRChat SDK, or in a VRChat project with the `DIGHOLEIT_STANDALONE` scripting define.

Bake the zone as usual, then click **Add Standalone Runtime** in the zone inspector (or add **Dig Zone Runtime (Standalone)** to the zone GameObject).

## DigZoneRuntimeStandalone

| Member | What it does |
|---|---|
| `Dig(Vector3 world, float radius)` | Digs a sphere at a world position. |
| `Add(Vector3 world, float radius)` | Adds a sphere of soil. |
| `Paint(Vector3 world, float radius, int layer)` | Paints voxels: 0 auto (erase paint), 1–4 terrain layers 0–3, 5 dug soil, 6–17 terrain layers 4–15 (`DigFormat.PaintValue(terrainLayer)`). |
| `Tree(Vector3 world, float radius, int index)` / `Detail(...)` | Plants `treePrefabs[index]` / `detailPrefabs[index]` at a surface point, upright with a yaw and scale from the edit; index -1 erases planted ones within the radius. Digging or adding soil over a planted object removes it. |
| `Smooth(Vector3 world, float radius, float strength)` | Smooths the surface in the sphere (strength 0–1). Not idempotent: apply each smooth edit once, and don't feed your own back into `ApplyEdit`. |
| `SpawnedNear(Vector3 world, float meters, bool tree)` | Whether something was planted near a point. |
| `LocalEdit(Vector3 world, float radius, int op, int layer = 0)` | The general form. Raises `LocalEditRequested`, then applies the edit. |
| `ApplyEdit(long edit)` | Applies a packed edit, for example one received from another player. Returns true if the grid changed. |
| `LoadEdits(IEnumerable<long> edits)` | Restores the baked grid and replays a list of edits (loading a save, or a late joiner). |
| `ResetToBaked()` | Restores the baked grid and clears the edit log. |
| `Raycast(Ray ray, float maxDistance, out Vector3 hit)` | Marches a ray through the grid, without physics. |
| `Contains(Vector3 world)` | Whether a point lies inside the zone's box. |
| `EditLog` | Every edit applied since the last reset, in order. |
| `Grid`, `PaintGrid`, `Zone` | The live grids and the authoring zone. |
| `event LocalEditRequested(long)` | Raised when this client makes an edit, before it is applied. Send it to other clients. |
| `event EditApplied(long)` | Raised after any edit changed the grid (local or remote). |
| `maxChunksPerFrame` | Chunks remeshed per frame. 0 remeshes every dirty chunk at once. |

Radii are in metres and are capped by the zone's Max Brush Radius.

Terrain trees and details in the zone go away where the ground under them is dug away or buried, and come back on `ResetToBaked`. The detail renderers get a live foliage mask through a MaterialPropertyBlock, so the materials (assets) are never changed.

## DigToolStandalone

Mouse pen from a camera, using the legacy Input Manager: the left button uses the current mode (dig, add, paint, tree, detail, smooth), the right adds and the middle paints. With **Show Settings** it draws a panel (IMGUI, Tab hides it) for the mode, the option of the mode (soil or paint layer, tree or detail prefab, or erase), size, rate and a zone reset.

**Dig Pen (Standalone)** (`Example/Pen`) is the tool with a brush cursor; the standalone demo scene has one.

| Field | Meaning |
|---|---|
| `cam` | The camera to cast rays from. |
| `zones` | The zones this tool can edit. |
| `reach`, `radius`, `interval` | Ray length, brush radius and seconds between edits while a button is held. |
| `mode` | What the left button does. |
| `smoothStrength` | How far one smooth edit blends (0–1). |
| `addLayer`, `paintLayer` | Layers for added soil and for painting. |
| `treeIndex`, `detailIndex`, `treeSpacing`, `detailSpacing`, `detailsPerEdit` | What to plant (-1 erases) and how close together. |
| `cursor`, `showSettings`, `layerNames` | Brush cursor, settings panel and the terrain layer names it shows. |

`EditAtScreen(Vector2 screen, int op)` applies one edit at a screen position and `UseAtScreen(Vector2 screen, Mode mode)` uses a mode there, for your own input code.

## Saves

An edit is a single `long`. Save `EditLog`, and on load call `LoadEdits(savedEdits)`:

```csharp
long[] save = zoneRuntime.EditLog.ToArray();
// ... later
zoneRuntime.LoadEdits(save);
```

Dig, add and paint are idempotent, so replaying an edit twice is harmless.

## Multiplayer

1. Subscribe to `LocalEditRequested` and send the `long` over your netcode.
2. Call `ApplyEdit(edit)` for every edit received from others.
3. For late joiners, send the full `EditLog` and call `LoadEdits` on the joining client.

Apply edits in the same order on every client. Dig and Add do not commute, so different orders can give different results.
