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
Make sure the zone's `DigZoneRuntime.sync` points at its `DigSync`, and that `DigSync` is on its own GameObject. Bake again to recreate both.

**The dig tool doesn't hit the zone.**
Include the zone's Chunk Layer in the tool's `layers` mask, and check that `reach` is long enough.

**The voxel surface looks gray or untextured.**
Click **Apply Material** to refill the material from the terrain layers. The zone uses the terrain's first four layers.
