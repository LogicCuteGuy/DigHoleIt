using UnityEngine;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Shared data format, compiled into both the UdonSharp and the C# runtimes (Runtime/Shared is a U# assembly).
    ///
    /// Grid: one byte per sample, index = x + sx * (y + sy * z), sx = nx + 1, sy = ny + 1.
    /// Signed value s = byte - 128 is a signed distance in 1/64 voxel units, clamped to about ±2 voxels.
    /// s &lt; 0 is solid, s &gt;= 0 is air.
    ///
    /// Paint grid: same layout, one layer per sample: 0 auto (terrain splat above the original surface, dug soil
    /// below), 1-4 terrain layers 0-3, 5 dug soil, 6-17 terrain layers 4-15. Use <see cref="PaintValue"/> and
    /// <see cref="TerrainLayerOf"/> to convert.
    ///
    /// Edit (long, all 64 bits used):
    ///   bits  0-15 x, 16-31 y, 32-47 z : zone grid position in 1/16 voxel
    ///   bits 48-55 radius              : 1/8 voxel
    ///   bits 56-58 op                  : 0 dig, 1 add, 2 smooth, 3 paint, 4 tree, 5 detail
    ///   bits 59-63 layer               : paint value (paint), value given to the added soil (add, 0 = leave as is),
    ///                                    prefab index + 1 (tree, detail; 0 = erase the spawned ones in the sphere),
    ///                                    strength in 1/31 (smooth)
    /// </summary>
    public static class DigFormat
    {
        public const int SdfScale = 64;
        public const int SdfBand = 2;
        public const byte SolidByte = 0;
        public const byte AirByte = 255;

        public const int OpDig = 0;
        public const int OpAdd = 1;
        /// <summary>
        /// Blends each sample towards its neighbours. Not idempotent: runtimes apply it once, in log order, and DigSync
        /// does not predict it.
        /// </summary>
        public const int OpSmooth = 2;
        /// <summary>Largest smoothing strength in an edit's layer bits (strength = layer / SmoothSteps).</summary>
        public const int SmoothSteps = 31;
        public const int OpPaint = 3;
        /// <summary>Spawns tree prefab layer - 1 of the zone at the edit point, or erases spawned trees (layer 0).</summary>
        public const int OpTree = 4;
        /// <summary>Spawns detail prefab layer - 1 of the zone at the edit point, or erases spawned details (layer 0).</summary>
        public const int OpDetail = 5;

        public const int LayerAuto = 0;
        public const int LayerDugSoil = 5;
        /// <summary>Terrain layers a zone can shade and paint.</summary>
        public const int MaxTerrainLayers = 16;
        /// <summary>Largest paint value: terrain layer 15.</summary>
        public const int LayerMax = 17;

        public const float PosScale = 16f;
        public const float RadiusScale = 8f;

        public static byte Quantize(float sdfVoxels)
        {
            int q = Mathf.RoundToInt(sdfVoxels * SdfScale);
            if (q < -128) q = -128;
            if (q > 127) q = 127;
            return (byte)(q + 128);
        }

        public static long Pack(Vector3 gridPos, float radiusVoxels, int op)
        {
            long x = Mathf.Clamp(Mathf.RoundToInt(gridPos.x * PosScale), 0, 65535);
            long y = Mathf.Clamp(Mathf.RoundToInt(gridPos.y * PosScale), 0, 65535);
            long z = Mathf.Clamp(Mathf.RoundToInt(gridPos.z * PosScale), 0, 65535);
            long r = Mathf.Clamp(Mathf.RoundToInt(radiusVoxels * RadiusScale), 1, 255);
            long o = op & 7;
            return x | (y << 16) | (z << 32) | (r << 48) | (o << 56);
        }

        /// <summary><see cref="Pack"/> plus a paint layer (see the format above).</summary>
        public static long PackLayer(Vector3 gridPos, float radiusVoxels, int op, int layer)
        {
            return Pack(gridPos, radiusVoxels, op) | ((long)(layer & 31) << 59);
        }

        public static int UnpackLayer(long e)
        {
            return (int)((e >> 59) & 31);
        }

        /// <summary>Paint value of terrain layer <paramref name="terrainLayer"/> (0-15), or 0 (auto) if out of range.</summary>
        public static int PaintValue(int terrainLayer)
        {
            if (terrainLayer < 0 || terrainLayer >= MaxTerrainLayers) return LayerAuto;
            return terrainLayer < 4 ? terrainLayer + 1 : terrainLayer + 2;
        }

        /// <summary>Terrain layer a paint value stands for, or -1 for auto, dug soil and invalid values.</summary>
        public static int TerrainLayerOf(int paintValue)
        {
            if (paintValue >= 1 && paintValue <= 4) return paintValue - 1;
            if (paintValue >= 6 && paintValue <= LayerMax) return paintValue - 2;
            return -1;
        }

        public static void Unpack(long e, out Vector3 gridPos, out float radiusVoxels, out int op)
        {
            gridPos = new Vector3(
                (e & 0xFFFF) / PosScale,
                ((e >> 16) & 0xFFFF) / PosScale,
                ((e >> 32) & 0xFFFF) / PosScale);
            radiusVoxels = ((e >> 48) & 0xFF) / RadiusScale;
            op = (int)((e >> 56) & 7);
        }

        /// <summary>Chunk range whose meshes read any sample in [min, max] along one axis.</summary>
        public static void AffectedChunks(int min, int max, int chunkCells, int chunkCount, out int cMin, out int cMax)
        {
            cMin = min - 1 < 0 ? 0 : (min - 1) / chunkCells;
            cMax = (max + 1) / chunkCells;
            if (cMax > chunkCount - 1) cMax = chunkCount - 1;
        }

        /// <summary>
        /// Samples chunk <paramref name="c"/> reads along one axis, inclusive: its own cells plus one sample on each side,
        /// clamped to the grid (<paramref name="cells"/> cells, so samples 0..cells). A chunk meshed from just these
        /// samples matches the chunk meshed from the whole grid.
        /// </summary>
        public static void ChunkSamples(int c, int chunkCells, int cells, out int s0, out int s1)
        {
            s0 = c * chunkCells - 1;
            if (s0 < 0) s0 = 0;
            s1 = c * chunkCells + chunkCells;
            if (s1 > cells) s1 = cells;
        }
    }
}
