# DigHoleIt

Diggable voxel terrain for Unity, in the spirit of Digger Pro. Built for **VRChat worlds (LCGUdonSharp)** and usable in **standalone games**.

- Dig and add soil at runtime: holes, tunnels and caves under a normal Unity Terrain.
- Paint terrain layers (or dug soil) onto the voxel surface, at runtime and in the editor.
- In VRChat, edits sync to every player, including late joiners.
- Shading matches the terrain's own layers, with a "dug soil" material underground.
- Terrain-style editor brushes: Dig, Add, Paint, Smooth and Reset, with shapes, size and strength.
- Zones follow the terrain: raise, lower or paint the terrain under a zone and it updates, keeping your sculpting.
- Move and resize zones with box handles; re-baking keeps your sculpting.
- One brush and mesher implementation, shared by the Udon runtime and plain C#.

## Requirements

| | VRChat world | Standalone game |
|---|---|---|
| Unity | 2022.3 | 2022.3 |
| Render pipeline | Built-in | Built-in |
| Other | VRChat Worlds SDK 3.10.5 and **LCGUdonSharp** 0.3.4 or later | none |

## Install

*Window > Package Manager > + > Add package from git URL*:

```
https://github.com/LogicCuteGuy/com.logiccuteguy.digholeit.git
```

Or clone this repository into your project's `Packages/com.logiccuteguy.digholeit` folder.

The package picks its runtime from the project. With the VRChat SDK only the Udon runtime compiles; without it only the standalone runtime does. To keep the standalone runtime in a VRChat project, add the scripting define `DIGHOLEIT_STANDALONE`.

## Quick start (VRChat)

1. **Tools > DigHoleIt > Create VRChat Demo Scene** builds a terrain, a baked Dig Zone, a spawn and three shovels (dig, add, paint). Press Play with ClientSim, pick up a shovel and hold Use.
2. On your own terrain: add **DigHoleIt > Dig Zone** to an empty GameObject, assign the Terrain, click **Fit To Terrain**, then **Bake**.
3. Add a pickup with a **DigTool** and put the zone in its `zones` array.

## Quick start (standalone)

1. **Tools > DigHoleIt > Create Standalone Demo Scene**, or bake your own zone as above.
2. Add **Dig Zone Runtime (Standalone)** to the zone.
3. Call `Dig(worldPos, radius)`, `Add(worldPos, radius)` or `Paint(worldPos, radius, layer)`. `DigToolStandalone` gives mouse digging. For saves and multiplayer, store or send the `long` edits from `EditLog` / `LocalEditRequested`.

## Documentation

- [Getting started](Documentation~/getting-started.md)
- [Dig Zones](Documentation~/dig-zones.md): settings, baking, resizing, terrain holes, following terrain edits
- [Editor brushes](Documentation~/editor-brushes.md)
- [VRChat runtime](Documentation~/vrchat-runtime.md)
- [Standalone runtime](Documentation~/standalone-runtime.md)
- [How it works](Documentation~/how-it-works.md)
- [Performance and limits](Documentation~/performance.md)
- [Troubleshooting](Documentation~/troubleshooting.md)

See the [changelog](CHANGELOG.md) for what changed in each version.

## Tests

EditMode tests in `Tests/Editor` cover edit packing, brush idempotence and edit-box limits, painting, Surface Nets winding, watertight output, and chunked output matching single-chunk output. Run them from the Test Runner; the package is listed in `testables`.

## License

[MIT](LICENSE.md)
