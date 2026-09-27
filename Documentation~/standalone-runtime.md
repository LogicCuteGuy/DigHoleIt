# Standalone runtime

The standalone runtime is plain C# (`LogicCuteGuy.DigHoleIt.Standalone`). It compiles in any project without the VRChat SDK, or in a VRChat project with the `DIGHOLEIT_STANDALONE` scripting define.

Bake the zone as usual, then click **Add Standalone Runtime** in the zone inspector (or add **Dig Zone Runtime (Standalone)** to the zone GameObject).

## DigZoneRuntimeStandalone

| Member | What it does |
|---|---|
| `Dig(Vector3 world, float radius)` | Digs a sphere at a world position. |
| `Add(Vector3 world, float radius)` | Adds a sphere of soil. |
| `Paint(Vector3 world, float radius, int layer)` | Paints voxels: 0 auto (erase paint), 1–4 terrain layers 0–3, 5 dug soil, 6–17 terrain layers 4–15 (`DigFormat.PaintValue(terrainLayer)`). |
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

## DigToolStandalone

Mouse digging from a camera, using the legacy Input Manager: the left button digs, the right adds and the middle paints.

| Field | Meaning |
|---|---|
| `cam` | The camera to cast rays from. |
| `zones` | The zones this tool can edit. |
| `reach`, `radius`, `interval` | Ray length, brush radius and seconds between edits while a button is held. |
| `addLayer`, `paintLayer` | Layers for added soil and for painting. |

`EditAtScreen(Vector2 screen, int op)` applies one edit at a screen position, for your own input code.

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
