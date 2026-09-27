# DigHoleIt documentation

DigHoleIt turns part of a Unity Terrain into diggable voxel terrain. You place a **Dig Zone** over the terrain and bake it. Players can then dig holes, tunnels and caves, add soil back, and paint terrain layers onto the voxel surface. In VRChat, every edit syncs to all players, including late joiners.

| Page | What it covers |
|---|---|
| [Getting started](getting-started.md) | Installing the package, the demo scenes, and setting up a zone on your own terrain |
| [Dig Zones](dig-zones.md) | Zone settings, baking, moving and resizing, terrain holes, and following terrain edits |
| [Editor brushes](editor-brushes.md) | Sculpting and painting zones in the editor |
| [VRChat runtime](vrchat-runtime.md) | `DigZoneRuntime`, `DigTool` and `DigSync` (UdonSharp) |
| [Standalone runtime](standalone-runtime.md) | `DigZoneRuntimeStandalone` and `DigToolStandalone` (plain C#), with saves and multiplayer |
| [How it works](how-it-works.md) | Grid format, edit packing, meshing, shading and networking |
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
Runtime/Core        DigZone (authoring, editor only in builds), DigZoneData, ChunkMesher
Runtime/Shared      DigBrush, DigFormat, SurfaceNets: compiled into both Udon and C#
Runtime/Udon        DigZoneRuntime, DigTool, DigSync (VRChat only)
Runtime/Standalone  DigZoneRuntimeStandalone, DigToolStandalone (non-VRChat only)
Editor              Baker, inspector, handles, brushes, terrain tools, terrain sync
Editor/Udon         Bridge that copies baked data into the Udon runtime, VRChat demo builder
Shaders             DigHoleIt/DigTerrain (Standard) and DigHoleIt/DigTerrain Lite (Quest)
Tests/Editor        EditMode tests
```
