using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>A terrain tree a Dig Zone keeps while its hole is cut: the fields of Unity's TreeInstance.</summary>
    [Serializable]
    public struct DigTreeInstance
    {
        /// <summary>Position on the terrain, 0..1 on each axis like TreeInstance.position (y = height / terrain height).</summary>
        public Vector3 position;
        public float widthScale;
        public float heightScale;
        /// <summary>Radians around the up axis.</summary>
        public float rotation;
        public Color32 color;
        public Color32 lightmapColor;
        public int prototypeIndex;
        /// <summary>
        /// Which way the tree grows inside a zone (a unit vector; zero means straight up, as on the terrain). Trees
        /// painted under a cave ceiling hang down. The terrain keeps no such thing, so trees given back to it stand up.
        /// </summary>
        public Vector3 up;

        /// <summary>The direction the tree grows (straight up when <see cref="up"/> is not set).</summary>
        public Vector3 Up => up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;

        public DigTreeInstance(TreeInstance t)
        {
            position = t.position;
            widthScale = t.widthScale;
            heightScale = t.heightScale;
            rotation = t.rotation;
            color = t.color;
            lightmapColor = t.lightmapColor;
            prototypeIndex = t.prototypeIndex;
            up = Vector3.zero;
        }

        public TreeInstance ToTreeInstance() => new TreeInstance
        {
            position = position,
            widthScale = widthScale,
            heightScale = heightScale,
            rotation = rotation,
            color = color,
            lightmapColor = lightmapColor,
            prototypeIndex = prototypeIndex,
        };
    }

    /// <summary>
    /// A detail (grass, flower, detail mesh) a zone keeps on its own voxel surface where the terrain's detail map can't
    /// put one: walls, cave ceilings, tunnel floors. Painted with DigHoleIt: Paint Details.
    /// </summary>
    [Serializable]
    public struct DigDetailInstance
    {
        /// <summary>Root position in world units from the terrain's position (stays put when the zone is re-baked).</summary>
        public Vector3 position;
        /// <summary>Which way it grows: the surface normal where it was painted.</summary>
        public Vector3 normal;
        /// <summary>Radians around <see cref="normal"/>.</summary>
        public float rotation;
        public float width;
        public float height;
        public int prototypeIndex;
    }

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
        /// <summary>
        /// The grid as sampled from the terrain, before sculpting. A re-bake keeps samples that differ from it. Assign a new
        /// array to change it, never change it in place.
        /// </summary>
        [NonSerialized] public byte[] baseGrid;

        // What is saved: the three grids, run-length encoded.
        [SerializeField, HideInInspector] private byte[] gridRle;
        [SerializeField, HideInInspector] private byte[] paintRle;
        [SerializeField, HideInInspector] private byte[] baseGridRle;
        // Raw grids saved by 0.3 and earlier: read once, then saved encoded.
        [SerializeField, HideInInspector, FormerlySerializedAs("grid")] private byte[] legacyGrid;
        [SerializeField, HideInInspector, FormerlySerializedAs("paint")] private byte[] legacyPaint;
        [SerializeField, HideInInspector, FormerlySerializedAs("baseGrid")] private byte[] legacyBaseGrid;

        // The arrays and version the encoded copies were made from, and those encoded copies.
        [NonSerialized] private byte[] _packedGrid;
        [NonSerialized] private byte[] _packedPaint;
        [NonSerialized] private byte[] _packedBase;
        [NonSerialized] private byte[] _packedGridRle;
        [NonSerialized] private byte[] _packedPaintRle;
        [NonSerialized] private byte[] _packedBaseRle;
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
        public int gridVersion
        {
            get => _gridVersion;
            set
            {
                _gridVersion = value;
                _liveVersion = value;
            }
        }

        [SerializeField, HideInInspector, FormerlySerializedAs("gridVersion")] private int _gridVersion;
        // The version of the grids in memory. Deserializing (undo, redo) overwrites _gridVersion but not this.
        [NonSerialized] private int _liveVersion;

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

        [Header("Trees and details")]
        /// <summary>
        /// The terrain's trees inside the zone's hole. A terrain deletes the trees in its holes (and refuses new ones
        /// there), so the zone takes them over when it cuts its hole and gives them back when the hole is filled.
        /// </summary>
        public DigTreeInstance[] terrainTrees;
        /// <summary>
        /// Details on the zone's own surface (walls, cave ceilings, tunnel floors), painted with DigHoleIt: Paint Details.
        /// The terrain's detail map only covers the top surface, so these stay with the zone.
        /// </summary>
        public DigDetailInstance[] surfaceDetails;
        /// <summary>
        /// Foliage mask (RGBA32, see <see cref="DigFoliage"/>): where details still stand. Rows 0..nz hold the grid
        /// columns, the rows above one texel per surface detail. Null when the zone shows none.
        /// </summary>
        public Texture2D foliageMask;
        /// <summary>Merged detail mesh of each chunk column (cx + ChunksX * cz), null where the column has no details.</summary>
        public Mesh[] detailMeshes;
        /// <summary>DigHoleIt/DigDetail material of each terrain detail prototype, null for prototypes the zone doesn't show.</summary>
        public Material[] detailMaterials;
        /// <summary>Hash of the terrain trees, details and settings the foliage was built from (editor change tracking).</summary>
        [HideInInspector] public int foliageSignature;
        /// <summary>The same without the trees (changing only the trees rebuilds only the trees).</summary>
        [HideInInspector] public int foliageDetailSignature;
        /// <summary>Trees plus detail meshes the zone built (0: it shows no foliage).</summary>
        [HideInInspector] public int foliageCount;

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
            bool sameVersion = _packed && _packedVersion == gridVersion;
            if (sameVersion && ReferenceEquals(_packedGrid, grid) && ReferenceEquals(_packedPaint, paint) && ReferenceEquals(_packedBase, baseGrid))
                return;
            if (!sameVersion || !ReferenceEquals(_packedGrid, grid)) gridRle = DigRleEncoder.Encode(grid);
            if (!sameVersion || !ReferenceEquals(_packedPaint, paint)) paintRle = DigRleEncoder.Encode(paint);
            // The base grid is replaced, never changed in place (so sculpting doesn't encode it again).
            if (!_packed || !ReferenceEquals(_packedBase, baseGrid)) baseGridRle = DigRleEncoder.Encode(baseGrid);
            Remember();
        }

        private void Remember()
        {
            _packedGrid = grid;
            _packedPaint = paint;
            _packedBase = baseGrid;
            _packedGridRle = gridRle;
            _packedPaintRle = paintRle;
            _packedBaseRle = baseGridRle;
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
            // Undo and redo deserialize the whole asset, also when they only touched something else (trees, settings).
            // A grid whose encoding is unchanged keeps its decoded array, unless that was changed since it was encoded:
            // decoding a large zone takes long and a lot of memory.
            bool current = _packed && _packedVersion == _liveVersion;
            bool sameGrid = current && SameBytes(gridRle, _packedGridRle);
            bool samePaint = current && SameBytes(paintRle, _packedPaintRle);
            bool sameBase = current && SameBytes(baseGridRle, _packedBaseRle);
            grid = sameGrid ? _packedGrid : Unpack(gridRle);
            paint = samePaint ? _packedPaint : Unpack(paintRle);
            baseGrid = sameBase ? _packedBase : Unpack(baseGridRle);
            // Nothing changed: keep the version too, so what was made from these grids stays valid.
            if (sameGrid && samePaint && sameBase) _gridVersion = _liveVersion;
            _liveVersion = _gridVersion;
            Remember();
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            return new ReadOnlySpan<byte>(a).SequenceEqual(b);
        }

        /// <summary>
        /// After undo or redo replaced the grid arrays: <paramref name="box"/> ({minX, minY, minZ, maxX, maxY, maxZ},
        /// inclusive) holds every sample where the new grid and paint differ from the arrays they replaced
        /// (<paramref name="oldGrid"/>, <paramref name="oldPaint"/>, at version <paramref name="oldVersion"/>). The
        /// per-chunk streams stay, so <see cref="GetChunkPacks"/> encodes only the chunks that read the box again.
        /// </summary>
        public void AdoptUndo(byte[] oldGrid, byte[] oldPaint, int oldVersion, int[] box)
        {
            bool packedNow = _packed && _packedVersion == _gridVersion && ReferenceEquals(_packedGrid, grid) &&
                             ReferenceEquals(_packedPaint, paint) && ReferenceEquals(_packedBase, baseGrid);
            int v = Math.Max(Math.Max(_gridVersion, oldVersion), _chunkSrcVersion) + 1;

            // The streams were made from the old arrays, and every change to those since is marked.
            int since = oldVersion - _chunkSrcVersion;
            int first = _changedVersions.Count - since;
            bool keep = _gridStreams != null && ReferenceEquals(_chunkSrcGrid, oldGrid) && ReferenceEquals(_chunkSrcPaint, oldPaint) &&
                        (paint == null) == (oldPaint == null) && since >= 0 && first >= 0;
            if (keep)
            {
                var boxes = _changedBoxes.GetRange(first, since);
                boxes.Add((int[])box.Clone());
                _changedVersions.Clear();
                _changedBoxes.Clear();
                _chunkSrcGrid = grid;
                _chunkSrcPaint = paint;
                _chunkSrcVersion = v;
                gridVersion = v;
                foreach (int[] b in boxes) MarkChanged(b);
            }
            else
            {
                gridVersion = v;
            }
            // The encoded copies were just read, so they still match.
            if (packedNow) _packedVersion = _gridVersion;
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
