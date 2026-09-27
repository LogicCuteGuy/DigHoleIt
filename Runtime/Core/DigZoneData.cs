using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Baked state of one Dig Zone: the SDF grid, chunk meshes (sub-assets), shader textures, and the terrain
    /// hole bookkeeping needed to undo the cut. Written by the editor baker and sculpt tool.
    /// The grids are stored run-length encoded (<see cref="DigRle"/>) and decoded when the asset loads.
    /// </summary>
    public class DigZoneData : ScriptableObject, ISerializationCallbackReceiver
    {
        [Header("Grid")]
        public int nx;
        public int ny;
        public int nz;
        public float voxelSize = 0.5f;
        public int chunkCells = 16;
        /// <summary>The SDF grid (see DigFormat). Bump <see cref="gridVersion"/> after changing it in place.</summary>
        [NonSerialized] public byte[] grid;
        /// <summary>Paint layer per sample (see DigFormat). Null or all zero when the zone was never painted.</summary>
        [NonSerialized] public byte[] paint;
        /// <summary>The grid as sampled from the terrain, before sculpting. A re-bake keeps samples that differ from it.</summary>
        [NonSerialized] public byte[] baseGrid;

        // What is saved: the three grids, run-length encoded.
        [SerializeField, HideInInspector] private byte[] gridRle;
        [SerializeField, HideInInspector] private byte[] paintRle;
        [SerializeField, HideInInspector] private byte[] baseGridRle;
        // Raw grids saved by 0.3 and earlier: read once, then saved encoded.
        [SerializeField, HideInInspector, FormerlySerializedAs("grid")] private byte[] legacyGrid;
        [SerializeField, HideInInspector, FormerlySerializedAs("paint")] private byte[] legacyPaint;
        [SerializeField, HideInInspector, FormerlySerializedAs("baseGrid")] private byte[] legacyBaseGrid;

        // The arrays and version the encoded copies were made from.
        [NonSerialized] private byte[] _packedGrid;
        [NonSerialized] private byte[] _packedPaint;
        [NonSerialized] private byte[] _packedBase;
        [NonSerialized] private int _packedVersion;
        [NonSerialized] private bool _packed;
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
        /// <summary>Splat weights of terrain layers 0-3 over the zone.</summary>
        public Texture2D controlTex;
        /// <summary>Splat weights of terrain layers 4-7, 8-11 and 12-15, for terrains with more than 4 layers.</summary>
        public Texture2D[] extraControlTex;
        /// <summary>Terrain layers the zone was baked with (at most <see cref="DigFormat.MaxTerrainLayers"/>).</summary>
        public int layerCount;
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

        /// <summary>The grid, run-length encoded (<see cref="DigRle"/>). Null when not baked.</summary>
        public byte[] PackedGrid
        {
            get
            {
                Pack();
                return gridRle;
            }
        }

        /// <summary>The paint grid, run-length encoded. Null when the zone has no paint grid.</summary>
        public byte[] PackedPaint
        {
            get
            {
                Pack();
                return paintRle;
            }
        }

        /// <summary>Bytes the grids take on disk (encoded).</summary>
        public long StoredBytes
        {
            get
            {
                Pack();
                return (long)(gridRle?.Length ?? 0) + (paintRle?.Length ?? 0) + (baseGridRle?.Length ?? 0);
            }
        }

        // Per-chunk streams for the VRChat runtime, and the sample boxes changed since (see MarkChanged).
        [NonSerialized] private byte[] _chunkSrcGrid;
        [NonSerialized] private byte[] _chunkSrcPaint;
        [NonSerialized] private int _chunkSrcVersion;
        [NonSerialized] private int _chunkSrcCells;
        [NonSerialized] private byte[][] _gridStreams;
        [NonSerialized] private int[] _gridUniform;
        [NonSerialized] private byte[][] _paintStreams;
        [NonSerialized] private int[] _paintUniform;
        [NonSerialized] private byte[] _chunkGrid;
        [NonSerialized] private int[] _chunkGridOffsets;
        [NonSerialized] private byte[] _chunkPaint;
        [NonSerialized] private int[] _chunkPaintOffsets;
        [NonSerialized] private readonly List<int> _changedVersions = new List<int>();
        [NonSerialized] private readonly List<int[]> _changedBoxes = new List<int[]>();
        private const int MaxChangedBoxes = 4096;

        /// <summary>
        /// Bumps <see cref="gridVersion"/> after the samples in <paramref name="box"/> ({minX, minY, minZ, maxX, maxY,
        /// maxZ}, inclusive) changed in place, so <see cref="GetChunkPacks"/> re-encodes only the chunks that read them.
        /// A plain <c>gridVersion++</c> is also correct, but re-encodes every chunk.
        /// </summary>
        public void MarkChanged(int[] box)
        {
            gridVersion++;
            if (_changedBoxes.Count >= MaxChangedBoxes)
            {
                _changedVersions.Clear();
                _changedBoxes.Clear();
                return;
            }
            _changedVersions.Add(gridVersion);
            _changedBoxes.Add((int[])box.Clone());
        }

        /// <summary>
        /// The grid and paint split into per-chunk streams (<see cref="DigChunkPacker"/>), for the VRChat runtime.
        /// Paint is null when nothing is painted. Cached until a grid array is replaced or <see cref="gridVersion"/>
        /// changes; after <see cref="MarkChanged"/> only the changed chunks are encoded again.
        /// Returns false when the zone is not baked.
        /// </summary>
        public bool GetChunkPacks(out byte[] gridBlob, out int[] gridOffsets, out byte[] paintBlob, out int[] paintOffsets)
        {
            gridBlob = paintBlob = null;
            gridOffsets = paintOffsets = null;
            if (!HasGrid) return false;

            bool sameArrays = _gridStreams != null && _chunkSrcCells == chunkCells && _gridStreams.Length == ChunkCount &&
                              ReferenceEquals(_chunkSrcGrid, grid) && ReferenceEquals(_chunkSrcPaint, paint);
            if (!sameArrays || _chunkSrcVersion != gridVersion)
            {
                bool[] dirty = sameArrays ? ChangedChunks() : null;
                EncodeChunks(ref _gridStreams, ref _gridUniform, grid, dirty);
                if (paint != null) EncodeChunks(ref _paintStreams, ref _paintUniform, paint, _paintStreams != null ? dirty : null);
                else _paintStreams = null;

                DigChunkPacker.Join(_gridStreams, _gridUniform, out _chunkGrid, out _chunkGridOffsets);
                _chunkPaint = null;
                _chunkPaintOffsets = null;
                if (_paintStreams != null && !AllZero(_paintStreams, _paintUniform))
                    DigChunkPacker.Join(_paintStreams, _paintUniform, out _chunkPaint, out _chunkPaintOffsets);

                _chunkSrcGrid = grid;
                _chunkSrcPaint = paint;
                _chunkSrcVersion = gridVersion;
                _chunkSrcCells = chunkCells;
                _changedVersions.Clear();
                _changedBoxes.Clear();
            }
            gridBlob = _chunkGrid;
            gridOffsets = _chunkGridOffsets;
            paintBlob = _chunkPaint;
            paintOffsets = _chunkPaintOffsets;
            return true;
        }

        /// <summary>Chunks the boxes marked since the last pack reach, or null if some change went unmarked.</summary>
        private bool[] ChangedChunks()
        {
            int since = gridVersion - _chunkSrcVersion;
            int first = _changedVersions.Count - since;
            if (since <= 0 || first < 0) return null;
            for (int k = first; k < _changedVersions.Count; k++)
                if (_changedVersions[k] != _chunkSrcVersion + 1 + (k - first)) return null;

            var dirty = new bool[ChunkCount];
            for (int k = first; k < _changedBoxes.Count; k++)
            {
                int[] b = _changedBoxes[k];
                DigFormat.AffectedChunks(b[0], b[3], chunkCells, ChunksX, out int x0, out int x1);
                DigFormat.AffectedChunks(b[1], b[4], chunkCells, ChunksY, out int y0, out int y1);
                DigFormat.AffectedChunks(b[2], b[5], chunkCells, ChunksZ, out int z0, out int z1);
                for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    dirty[ChunkIndex(x, y, z)] = true;
            }
            return dirty;
        }

        /// <summary>Encodes every chunk, or only the ones marked in <paramref name="dirty"/>.</summary>
        private void EncodeChunks(ref byte[][] streams, ref int[] uniform, byte[] src, bool[] dirty)
        {
            int count = ChunkCount;
            if (dirty == null || streams == null || streams.Length != count)
            {
                streams = new byte[count][];
                uniform = new int[count];
                dirty = null;
            }
            var buffer = new byte[DigChunkPacker.BufferSize(chunkCells)];
            for (int cz = 0; cz < ChunksZ; cz++)
            for (int cy = 0; cy < ChunksY; cy++)
            for (int cx = 0; cx < ChunksX; cx++)
            {
                int ci = ChunkIndex(cx, cy, cz);
                if (dirty != null && !dirty[ci]) continue;
                streams[ci] = DigChunkPacker.EncodeChunk(src, nx, ny, nz, chunkCells, cx, cy, cz, buffer, out uniform[ci]);
            }
        }

        private static bool AllZero(byte[][] streams, int[] uniform)
        {
            for (int i = 0; i < streams.Length; i++)
                if (streams[i] != null || uniform[i] != 0) return false;
            return true;
        }

        /// <summary>Re-encodes the grids if they changed (new arrays, or <see cref="gridVersion"/> bumped).</summary>
        private void Pack()
        {
            if (_packed && _packedVersion == gridVersion && ReferenceEquals(_packedGrid, grid) &&
                ReferenceEquals(_packedPaint, paint) && ReferenceEquals(_packedBase, baseGrid))
                return;
            gridRle = DigRleEncoder.Encode(grid);
            paintRle = DigRleEncoder.Encode(paint);
            baseGridRle = DigRleEncoder.Encode(baseGrid);
            Remember();
        }

        private void Remember()
        {
            _packedGrid = grid;
            _packedPaint = paint;
            _packedBase = baseGrid;
            _packedVersion = gridVersion;
            _packed = true;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            Pack();
            legacyGrid = null;
            legacyPaint = null;
            legacyBaseGrid = null;
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (legacyGrid != null && legacyGrid.Length > 0)
            {
                grid = legacyGrid;
                paint = legacyPaint != null && legacyPaint.Length > 0 ? legacyPaint : null;
                baseGrid = legacyBaseGrid != null && legacyBaseGrid.Length > 0 ? legacyBaseGrid : null;
                _packed = false;
                return;
            }
            grid = Unpack(gridRle);
            paint = Unpack(paintRle);
            baseGrid = Unpack(baseGridRle);
            Remember();
        }

        private static byte[] Unpack(byte[] rle)
        {
            if (rle == null || rle.Length == 0) return null;
            var state = new int[DigRle.StateSize];
            int count = DigRle.Begin(rle, state);
            if (count <= 0) return null;
            var a = new byte[count];
            if (!DigRle.DecodeAll(rle, a, true))
            {
                Debug.LogError("[DigHoleIt] A Dig Zone data asset has a corrupt grid. Bake the zone again.");
                return null;
            }
            return a;
        }
    }
}
