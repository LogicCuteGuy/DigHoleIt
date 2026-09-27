# Troubleshooting

**A gray strip or gap shows through the terrain next to a zone.**
The terrain has a hole that no zone covers, usually left by an older bake. Holes from deleted zones can be filled with **Fix Leftover Holes** in the terrain tools. For a hole no zone knows about, open the terrain's **Paint Holes** tool and paint the area back in, then bake the zone again.

**The bake fails with "too small" or "Border Voxels is too large".**
The terrain hole is cut one voxel inside the zone edge, and Border Voxels is kept untouched inside it. Make the zone wider than about 7 voxels in X and Z, or lower Border Voxels to 2–3.

**"Settings changed since the last bake" in the inspector.**
The zone's position, cells, voxel size or chunk cells changed. Click **Bake**. Sculpting and paint are kept unless the voxel size changed or the zone moved off its lattice; the message says which.

**The zone didn't follow a terrain edit.**
Check that **Follow terrain edits** is on in the zone inspector, and that the zone is baked and up to date. Zones only update after the brush stroke ends.

**Standalone scripts show as "Missing Script" in a VRChat project.**
That's expected: the standalone runtime only compiles outside VRChat projects. Add the `DIGHOLEIT_STANDALONE` scripting define if you need it.

**Edits don't sync in VRChat.**
Make sure the zone has a VRChat runtime (**Add VRChat Runtime** in the zone inspector), that `DigZoneRuntime.sync` points at its `DigSync`, and that `DigSync` is on its own GameObject. Remove the runtime and add it again to recreate both.

**"The grid is too large".**
A zone can have at most 128 Mi samples. Fit To Terrain makes the box as tall as the terrain under it, so a zone across a hill or cliff gets very tall. Make the zone smaller and put it only where players dig, split it into several zones, or use bigger voxels (twice the voxel size is an eighth of the samples).

**The scene has thousands of Chunk objects, or saving is slow.**
Zones baked by older versions have an object and a mesh for every chunk, even empty ones. Click **Bake** (sculpting and paint are kept): only chunks with a surface keep an object.

**The dig tool doesn't hit the zone.**
Include the zone's Chunk Layer in the tool's `layers` mask, and check that `reach` is long enough.

**The zone is dark or unlit with baked lights.**
Lights set to Baked only reach lightmapped objects and light probes. Keep **Baked Lighting** on for the zone, click **Add Light Probes**, and bake lighting again (after the zone's last Bake or sculpt). Dug chunks use the probes.

**Dark lines on the terrain along the zone edge, or the zone takes a lot of lightmap space.**
Zones baked before 0.4.0 have chunk meshes that reach past the terrain hole and a Scale In Lightmap of 1. Bake the zone again (the meshes are cut to the hole), then bake lighting (the chunks take the terrain's Scale In Lightmap, at least 16 texels across a chunk, times the zone's **Lightmap Scale**).

**A square grid shows in the baked lighting of a zone.**
Each chunk has its own lightmap, so chunks with few texels show their edges. Raise the zone's **Lightmap Scale** and bake lighting again.

**A dug area looks too bright or too dark.**
Dug chunks are lit by light probes above the ground and darkened with depth. Tune **Darkening Below Surface** and **Darkening Depth** on the zone material.

**The voxel surface looks gray or untextured.**
Click **Apply Material** to refill the material from the terrain layers. After adding terrain layers, Bake the zone so it gets their splat maps. DigTerrain Lite shades up to 8 layers.
