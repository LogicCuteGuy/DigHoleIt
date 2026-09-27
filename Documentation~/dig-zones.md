# Dig Zones

A Dig Zone is an axis-aligned box over one Unity Terrain. Inside it the terrain is replaced by a voxel grid that can be dug, filled and painted.

## Settings

| Setting | Meaning |
|---|---|
| **Terrain** | The terrain this zone cuts into. The whole footprint must lie on it. |
| **Voxel Size** | Edge length of one voxel in metres. |
| **Cells** | Grid size in voxels. The grid stores `cells + 1` samples per axis. |
| **Chunk Cells** | Voxels per chunk edge. Smaller chunks remesh faster in Udon but cost more draw calls. PC 16, Quest 8–12. |
| **Depth Below Terrain** / **Headroom Above Terrain** | How far Fit To Terrain extends the box below the lowest and above the highest terrain point. |
| **Border Voxels** | An untouched margin inside the terrain hole edge and above the zone floor, so the dug mesh always meets the terrain cleanly. 2–3 is typical. |
| **Max Brush Radius** | The largest brush radius accepted from players, in metres. |
| **Material** | The zone material. Baking creates one from the terrain layers if it is empty. |
| **Chunk Layer** | Layer of the chunk renderers and colliders. Dig tools raycast against it. |
| **Data** | The `DigZoneData` asset holding the grid. Created on the first bake under `Assets/DigHoleIt/Zones`. |

In the Scene view:

- the **orange box** is the zone;
- the **green box** is the diggable area (the zone minus Border Voxels). Digging and sculpting only happen inside it.

## Inspector buttons

| Button | What it does |
|---|---|
| **Fit To Terrain** | Sets the box height to cover the terrain under it, staying on the voxel lattice. |
| **Bake** | Bakes from the terrain and keeps sculpting and paint wherever the zone still covers them. |
| **Sculpt Tool** | Opens the [editor brushes](editor-brushes.md). |
| **Remesh All** | Rebuilds every chunk mesh from the grid. |
| **Apply Material** | Refills the material from the terrain layers and baked textures. |
| **Reset To Terrain** | Bakes from scratch, discarding all sculpting and paint. |
| **Clear** | Removes the chunk objects and fills the terrain hole back in. The grid stays in the data asset until the next Bake. |
| **Delete Zone** | Fills the terrain hole back in and deletes the zone. Undoable. |

## Moving and resizing

Select the zone and drag the coloured cubes on the faces of its box. Faces snap to whole voxels, and a label shows the size. When you release, the zone re-bakes (turn this off with **Re-bake after dragging the zone handles**). The Move tool also snaps a baked zone to its voxel lattice.

A re-bake keeps sculpting and paint as long as the voxel size is unchanged and the zone stayed on its lattice. Sculpted voxels that end up outside the zone are stored in the data asset and come back when the zone covers them again. **Reset To Terrain** discards them.

## Following terrain edits

Raise, lower, smooth or paint the Unity terrain as usual. When the stroke ends, every baked zone it touched updates:

- voxels you never sculpted take the new terrain shape;
- sculpted voxels and voxel paint stay as they are;
- the terrain texture blend is copied again;
- if the terrain rises or sinks out of the zone's box, the zone is fitted and re-baked, keeping the sculpting.

Undoing a terrain edit updates the zones too. Turn it off with **Follow terrain edits** in the zone inspector. The setting is per user and applies to all zones. Zones whose settings changed since their last bake are skipped until you bake them.

## Terrain holes

Each zone cuts a hole in the terrain over its footprint, one voxel inside its edge, so the voxel mesh runs under the terrain border. The zone records which cells it cut and what they were before.

- **Delete Zone**, deleting the zone GameObject, and **Clear** fill the hole back in. Cells another zone still needs are left open.
- Holes left by zones deleted before 0.2 can be filled with **Fix Leftover Holes** in the terrain tools.
- A duplicated zone (Ctrl+D) gets its own data asset and material the first time it is baked.

## Working from the Terrain

Select the terrain, open **Paint Terrain** and pick **DigHoleIt: Dig Voxels** or **DigHoleIt: Paint Voxels**. The Terrain inspector then lists every Dig Zone on the terrain with its full settings and buttons. The Scene view shows each zone's box, diggable area and resize handles.
