# Getting started

## Requirements

| | VRChat world | Standalone game |
|---|---|---|
| Unity | 2022.3 | 2022.3 |
| Render pipeline | Built-in | Built-in |
| Other | VRChat Worlds SDK 3.10.5 and **LCGUdonSharp** 0.3.4 or later | none |

## Installing

Pick one:

- **VRChat Creator Companion (VCC):** add the LogicCuteGuy repository from [docs.logiccuteguy.com](https://docs.logiccuteguy.com/) (**Install via VCC**), or add
  `https://vpm.logiccuteguy.com/index.json` in *Settings > Packages > Add Repository*. Then add **DigHoleIt** to your project; VCC installs LCGUdonSharp with it. The listing may lag behind the newest GitHub release.
- **Git URL:** *Window > Package Manager > + > Add package from git URL* and enter
  `https://github.com/LogicCuteGuy/DigHoleIt.git`.
  Add `#v0.5.0` (or another tag) to the end to pin a version.
- **Embedded:** clone or copy the repository into your project's `Packages/com.logiccuteguy.digholeit` folder.

With a Git URL or embedded install in a VRChat project, install LCGUdonSharp first. The Udon runtime does not compile without it.

## Demo scenes

- **Tools > DigHoleIt > Create VRChat Demo Scene** builds a terrain, a baked Dig Zone, a VRCWorld spawn and three shovels (dig, add, paint). Press Play with ClientSim, pick up a shovel and hold Use.
- **Tools > DigHoleIt > Create Standalone Demo Scene** (non-VRChat projects only) builds a terrain, a baked zone and a camera with `DigToolStandalone`. Press Play: the left mouse button digs, the right adds and the middle paints.

## A zone on your own terrain

1. Create an empty GameObject and add **DigHoleIt > Dig Zone**. Its position is the zone's minimum corner.
2. Assign the **Terrain**. Set **Voxel Size**, **Cells** and **Chunk Cells** (see [Performance and limits](performance.md)).
3. Click **Fit To Terrain**, then **Bake**. Baking:
   - samples the terrain into the voxel grid;
   - cuts a terrain hole over the zone footprint;
   - bakes the terrain's splat and height maps and fills the zone material from the terrain layers;
   - creates chunk mesh objects for the chunks that have a surface (digging adds more as needed).
4. Add a runtime and a way to dig. Creating or baking a zone adds no runtime by itself:
   - **VRChat:** click **Add VRChat Runtime** in the zone inspector (adds `DigZoneRuntime` and a child `DigSync`). Then add a pickup with a **DigTool** and put the zone in its `zones` array. See [VRChat runtime](vrchat-runtime.md).
   - **Standalone:** click **Add Standalone Runtime** in the zone inspector, and add **Dig Tool Standalone** to a camera. See [Standalone runtime](standalone-runtime.md).
5. Optional: sculpt and paint the zone in the editor. See [Editor brushes](editor-brushes.md).
6. If the scene uses baked lighting: click **Add Light Probes** in the zone inspector, then bake lighting (*Window > Rendering > Lighting*). Bake lighting again after sculpting. See [Baked lighting](dig-zones.md#baked-lighting).

You can also start from the terrain: select it, open **Paint Terrain**, pick **DigHoleIt: Dig Voxels** and click **Create Dig Zone**.

The terrain material must support holes. Unity's default built-in terrain material does.
