# DigHoleIt documentation

DigHoleIt turns part of a Unity Terrain into diggable voxel terrain. You place a **Dig Zone** over the terrain and bake it. Players can then dig holes, tunnels and caves, add soil back, and paint terrain layers onto the voxel surface. In VRChat, every edit syncs to all players, including late joiners.

| Page | What it covers |
|---|---|
| [Getting started](getting-started.md) | Installing the package, the demo scenes, and setting up a zone on your own terrain |
| [Dig Zones](dig-zones.md) | Zone settings, baking, zone height, trees and details, moving and resizing, terrain holes, following terrain edits, terrain layers and baked lighting |
| [Editor brushes](editor-brushes.md) | Sculpting and painting zones in the editor |
| [VRChat runtime](vrchat-runtime.md) | `DigZoneRuntime`, `DigTool` and `DigSync` (UdonSharp) |
| [Standalone runtime](standalone-runtime.md) | `DigZoneRuntimeStandalone` and `DigToolStandalone` (plain C#), with saves and multiplayer |
| [How it works](how-it-works.md) | Grid format and compression, edit packing, meshing, shading, lighting and networking |
| [Performance and limits](performance.md) | Recommended settings for PC and Quest, memory use, and known limits |
| [Troubleshooting](troubleshooting.md) | Common problems and how to fix them |

## Which runtime do I get?

The package detects the project type:

- **VRChat project** (the VRChat SDK defines `VRC_SDK_VRCSDK3`): only the UdonSharp runtime (`Runtime/Udon`, `Editor/Udon`) compiles. The standalone runtime and its demo menu are hidden.
- **Any other project**: only the standalone runtime (`Runtime/Standalone`) compiles.
- To keep the standalone runtime in a VRChat project, add `DIGHOLEIT_STANDALONE` under *Project Settings > Player > Scripting Define Symbols*.

The authoring side (`DigZone`, the baker, the brushes) and the shared mesher and brush code (`Runtime/Core`, `Runtime/Shared`) are always compiled.

## Package layout

```
Runtime/Core        DigZone (authoring, editor only in builds), DigZoneData, ChunkMesher, DigChunkPacker, DigRleEncoder
Runtime/Shared      DigBrush, DigFormat, DigFoliage, DigRle, SurfaceNets: compiled into both Udon and C#
Runtime/Udon        DigZoneRuntime, DigTool, DigSync (VRChat only)
Runtime/Standalone  DigZoneRuntimeStandalone, DigToolStandalone (non-VRChat only)
Editor              Baker, trees and details (DigFoliageBaker, DigFoliageSync), inspectors, handles, brushes, terrain tools, terrain sync
Editor/Udon         Bridge that copies baked data into the Udon runtime, Udon inspectors, VRChat demo builder
Shaders             DigHoleIt/DigTerrain (Standard), DigHoleIt/DigTerrain Lite (Quest), DigHoleIt/DigDetail (terrain details in zones)
Tests/Editor        EditMode tests
```

## Source and support

DigHoleIt is made by [LogicCuteGuy](https://github.com/LogicCuteGuy). Source, releases and issues: [github.com/LogicCuteGuy/DigHoleIt](https://github.com/LogicCuteGuy/DigHoleIt).
