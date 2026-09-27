using UnityEngine;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Baked state of one Dig Zone: the SDF grid, chunk meshes (sub-assets), shader textures, and the terrain
    /// hole bookkeeping needed to undo the cut. Written by the editor baker and sculpt tool.
    /// </summary>
    public class DigZoneData : ScriptableObject
    {
        [Header("Grid")]
        public int nx;
        public int ny;
        public int nz;
        public float voxelSize = 0.5f;
        public int chunkCells = 16;
        [HideInInspector] public byte[] grid;
        /// <summary>Paint layer per sample (see DigFormat). Null or all zero when the zone was never painted.</summary>
        [HideInInspector] public byte[] paint;
        /// <summary>The grid as sampled from the terrain, before sculpting. A re-bake keeps samples that differ from it.</summary>
        [HideInInspector] public byte[] baseGrid;
        /// <summary>World position of sample (0, 0, 0) at the last bake (valid when <see cref="hasOrigin"/>).</summary>
        public Vector3 origin;
        [HideInInspector] public bool hasOrigin;

        /// <summary>
        /// Sculpted/painted samples that lie outside the zone after a resize, keyed by position on the voxel lattice
        /// (relative to <see cref="latticeAnchor"/>). A later bake puts them back when the zone covers them again.
        /// </summary>
        [HideInInspector] public Vector3 latticeAnchor;
        [HideInInspector] public bool hasLatticeAnchor;
        [HideInInspector] public long[] stashKeys;
        [HideInInspector] public byte[] stashGrid;
        [HideInInspector] public byte[] stashPaint;
        [HideInInspector] public byte[] stashFlags;

        public bool HasStash => stashKeys != null && stashKeys.Length > 0 && stashGrid != null && stashGrid.Length == stashKeys.Length
                                && stashPaint != null && stashPaint.Length == stashKeys.Length && stashFlags != null && stashFlags.Length == stashKeys.Length;

        public int StashCount => HasStash ? stashKeys.Length : 0;

        public void ClearStash()
        {
            stashKeys = null;
            stashGrid = null;
            stashPaint = null;
            stashFlags = null;
            hasLatticeAnchor = false;
        }
        /// <summary>Inclusive editable sample box {minX, minY, minZ, maxX, maxY, maxZ}.</summary>
        public int[] editBox = new int[6];
        /// <summary>Bumped on every grid change so editor tools can tell when undo/redo changed the grid.</summary>
        [HideInInspector] public int gridVersion;

        [Header("Chunks")]
        public Mesh[] chunkMeshes;

        [Header("Shading")]
        public Texture2D controlTex;
        public Texture2D heightTex;
        /// <summary>World xz → control UV: uv = xz * (x, y) + (z, w).</summary>
        public Vector4 controlST;
        /// <summary>World xz → height UV: uv = xz * (x, y) + (z, w).</summary>
        public Vector4 heightST;
        /// <summary>Original terrain height range encoded in <see cref="heightTex"/> (min, max).</summary>
        public Vector2 heightRange;
        /// <summary>World-space terrain hole rectangle (minX, minZ, maxX, maxZ).</summary>
        public Vector4 holeRect;

        [Header("Terrain cut")]
        public TerrainData cutTerrain;
        public RectInt cutRect;
        [HideInInspector] public bool[] cutPrevious;

        public int ChunksX => (nx + chunkCells - 1) / chunkCells;
        public int ChunksY => (ny + chunkCells - 1) / chunkCells;
        public int ChunksZ => (nz + chunkCells - 1) / chunkCells;
        public int ChunkCount => ChunksX * ChunksY * ChunksZ;
        public int SampleCount => (nx + 1) * (ny + 1) * (nz + 1);

        public int ChunkIndex(int cx, int cy, int cz) => cx + ChunksX * (cy + ChunksY * cz);

        public bool HasGrid => grid != null && grid.Length == SampleCount && SampleCount > 0;

        public bool HasPaintGrid => paint != null && paint.Length == SampleCount;

        /// <summary>True if any sample is painted.</summary>
        public bool HasPaint
        {
            get
            {
                if (!HasPaintGrid) return false;
                foreach (byte b in paint) if (b != 0) return true;
                return false;
            }
        }

        /// <summary>Returns the paint grid, creating an empty one if needed.</summary>
        public byte[] EnsurePaint()
        {
            if (!HasPaintGrid) paint = new byte[SampleCount];
            return paint;
        }
    }
}
