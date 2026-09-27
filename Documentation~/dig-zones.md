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
| **Baked Lighting** | On by default. Chunk meshes get lightmap UVs and are marked Contribute GI, so baked lighting covers them like the terrain. See [Baked lighting](#baked-lighting). |
| **Lightmap Scale** | Multiplies the chunks' lightmap resolution. At 1 the chunks get the terrain's **Scale In Lightmap** (the same texel size as the terrain), but at least 16 texels across a chunk, since each chunk is its own lightmap island and a coarser one shows its edges. Raise it for sharper baked shadows in dug areas, lower it to save lightmap space. |
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
| **Add Light Probes** / **Update Light Probes** | Places a Light Probe Group over the zone, 0.5 m and 3 m above the terrain. See [Baked lighting](#baked-lighting). |
| **Add VRChat Runtime** / **Add Standalone Runtime** | Adds the runtime for the project type ([VRChat](vrchat-runtime.md) or [standalone](standalone-runtime.md)). Creating or baking a zone adds none. **Remove** takes it off again. |

The info box shows the grid's size, its compressed size on disk, and how many chunk objects the zone has. Only chunks with a surface get an object; a zone baked by an older version has one per chunk until it is baked again.

## Terrain layers

The zone shades with as many terrain layers as its terrain has, up to 16. Bake copies the layers into the zone material and bakes one splat texture per four layers; the material switches to the matching shader variant (4, 8, 12 or 16 layers). **DigTerrain Lite** (Quest) shades up to 8. Adding or removing terrain layers needs a Bake (or **Apply Material** after a Bake).

Paint can use any of the terrain's layers. Each chunk mesh holds up to 4 painted layers plus dug soil; in a chunk painted with more, the extra layers show automatic shading.

## Baked lighting

Lights set to **Baked** don't light moving or changed objects directly, only through light probes. With **Baked Lighting** on:

1. Bake the zone, then bake the scene's lighting (Window > Rendering > Lighting). The chunks get lightmaps like the terrain.
2. Click **Add Light Probes** before baking lighting. A chunk that is dug at runtime can't keep its lightmap (its mesh changed), so it switches to light probes; **Reset** gives it its lightmap back.
3. Dug areas get darker with depth below the original surface (**Darkening Below Surface** and **Darkening Depth** on the material), because the probes sit above the ground and would light a cave like the surface.

Chunk meshes end exactly at the terrain hole edge, so they don't overlap the terrain in the lightmapper. Starting a lighting bake re-applies each zone's lighting settings (static flags, lightmap UVs, Scale In Lightmap), so changing the terrain's Scale In Lightmap only needs a new lighting bake.

Sculpting in the editor changes the chunk meshes, so bake lighting again after sculpting. Turn Baked Lighting off for zones that should only use light probes; that also skips the lightmap UVs, which make a big zone's bake slower (about 6 ms per chunk).

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
