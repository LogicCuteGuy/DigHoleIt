using System.Diagnostics;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using Debug = UnityEngine.Debug;

namespace LogicCuteGuy.DigHoleIt.Udon
{
    /// <summary>
    /// VRChat runtime for one baked Dig Zone. Applies queued edits and remeshes dirty chunks within a per-frame time
    /// budget. Fields under "Baked" are written by the editor bridge; do not edit by hand.
    ///
    /// The grid is stored per chunk, run-length encoded, and a chunk is decoded only when an edit reaches it; until
    /// then it keeps its baked mesh. So memory grows with the area players dig, not with the zone size, and a reset
    /// just drops the decoded chunks. Chunks without a surface have no GameObject until an edit gives them one
    /// (copied from <see cref="chunkTemplate"/>).
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class DigZoneRuntime : UdonSharpBehaviour
    {
        private const int QueueCapacity = 16384;
        private const int RowsPerStep = 4;
        private const int QuadsPerStep = 256;
        private const int PaintPerStep = 128;
        private const int ColumnsPerStep = 64;

        [Header("Baked")]
        // Per-chunk run-length encoded grids (DigChunkPacker); hidden because the inspector cannot draw them usefully.
        [HideInInspector] public byte[] chunkRle;
        [HideInInspector] public int[] chunkOffsets;
        [HideInInspector] public byte[] paintRle;
        [HideInInspector] public int[] paintOffsets;
        [Tooltip("False when the zone was baked without paint.")]
        public bool hasPaint;
        public int nx;
        public int ny;
        public int nz;
        public float voxelSize = 0.5f;
        public int chunkCells = 16;
        public int chunksX;
        public int chunksY;
        public int chunksZ;
        public int[] editBox;
        public float maxBrushRadius = 3f;
        // Chunk index of each entry in the chunk arrays below. Only chunks with a baked surface have an object.
        [HideInInspector] public int[] chunkIds;
        public MeshFilter[] chunkFilters;
        public MeshRenderer[] chunkRenderers;
        public MeshCollider[] chunkColliders;
        [Tooltip("Inactive chunk object copied when an edit gives an empty chunk a surface.")]
        public GameObject chunkTemplate;

        [Header("Baked Trees And Details")]
        [Tooltip("Foliage mask the editor baked (see DigFoliage). None when the zone shows no terrain trees or details.")]
        public Texture2D foliageMask;
        public MeshRenderer[] detailRenderers;
        public GameObject[] treeObjects;
        // Grid position each tree stands on, and where each chunk's trees start (DigZone.treeAnchors / treeBuckets).
        [HideInInspector] public Vector3[] treeAnchors;
        [HideInInspector] public int[] treeBuckets;
        // Details on walls and cave ceilings: grid position and chunk buckets like the trees; detail k has mask texel
        // (nx + 1) * (nz + 1) + k (DigZone.surfaceDetailAnchors / surfaceDetailBuckets).
        [HideInInspector] public Vector3[] surfaceDetailAnchors;
        [HideInInspector] public int[] surfaceDetailBuckets;

        [Header("Runtime")]
        [Tooltip("Optional. Without it, edits stay local to this player.")]
        public DigSync sync;
        [Tooltip("Milliseconds per frame spent on edits and meshing (PC).")]
        public float budgetMsDesktop = 2.5f;
        [Tooltip("Milliseconds per frame spent on edits and meshing (Android/iOS).")]
        public float budgetMsMobile = 1.2f;
        public bool logTimings;

        private bool _started;
        private bool _failed;
        private int _count;

        // Decoded chunks: the samples DigFormat.ChunkSamples gives, x fastest. Null until an edit reaches the chunk.
        private byte[][] _cg;
        private byte[][] _cp;
        private int[] _decoded;
        private int _decodedCount;
        private int[] _dec;
        private int[] _r;
        private int[] _er;
        private int[] _box;
        private byte[] _noPaint;

        private MeshFilter[] _filters;
        private MeshRenderer[] _renderers;
        private MeshCollider[] _colliders;
        private Mesh[] _bakedMesh;
        private Mesh[] _bakedCol;
        private bool[] _bakedOn;
        private int[] _bakedLightmap;
        private Vector4[] _bakedLightmapST;

        private long[] _queue;
        private int _qHead;
        private int _qCount;

        private bool[] _dirty;
        private int[] _dirtyList;
        private int _dirtyCount;
        private Mesh[] _meshes;
        private int[] _changed;

        private int[] _cellVert;
        private int[] _vMask;
        private int[] _vCell;
        private int[] _tris;
        private Vector3[] _vPos;
        private Vector3[] _vNrm;
        private Color[] _vCol;
        private Vector2[] _vUv;
        private int[] _slots;

        private bool _meshing;
        private int _meshChunk;
        private byte[] _mGrid;
        private byte[] _mPaint;
        private int _lnx;
        private int _lny;
        private int _lnz;
        private int _phase;
        private int _row;
        private int _rowCount;
        private int _vertCount;
        private int _quadV;
        private int _paintV;
        private int _triCount;
        private int _ox;
        private int _oy;
        private int _oz;
        private float _chunkMs;

        private Stopwatch _sw;
        private float _budget;

        // Terrain trees and details (DigFoliage). After meshing a chunk, the details whose column anchor it holds and the
        // trees anchored in it are checked against its samples. The live mask starts as a copy of the baked one.
        private bool _foliage;
        private Color32[] _mask;
        private Texture2D _maskTex;
        private bool _maskDirty;
        private int _fCol;
        private int _fCount;
        private bool _fChanged;
        private int _fx0;
        private int _fz0;
        private int _fw;
        private int _fd;
        private int _fy0;
        private int _fy1;
        private int _fr0;
        private int _fr1;
        private int _fr2;
        private int _fr3;
        private int _fr4;
        private int _fr5;
        // Mask texels of surface details changed by the last check (none when _sHi < _sLo).
        private int _sLo;
        private int _sHi;
        private bool[] _treeBaked;

        private void Start()
        {
            _Init();
        }

        /// <summary>Allocates the runtime state. Safe to call more than once (DigSync may call first).</summary>
        private void _Init()
        {
            if (_started) return;
            _started = true;

            _count = chunksX * chunksY * chunksZ;
            if (_count <= 0 || nx <= 0 || ny <= 0 || nz <= 0 || chunkOffsets == null || chunkOffsets.Length != _count ||
                editBox == null || editBox.Length != 6)
            {
                Debug.LogError("[DigHoleIt] DigZoneRuntime has no baked data. Bake the Dig Zone in the editor.", this);
                _failed = true;
                return;
            }
            if (hasPaint && (paintOffsets == null || paintOffsets.Length != _count)) hasPaint = false;

            _cg = new byte[_count][];
            _cp = new byte[_count][];
            _decoded = new int[_count];
            _dec = new int[DigRle.StateSize];
            _r = new int[6];
            _er = new int[6];
            _box = new int[6];
            _noPaint = new byte[0];

            _filters = new MeshFilter[_count];
            _renderers = new MeshRenderer[_count];
            _colliders = new MeshCollider[_count];
            _bakedMesh = new Mesh[_count];
            _bakedCol = new Mesh[_count];
            _bakedOn = new bool[_count];
            _bakedLightmap = new int[_count];
            _bakedLightmapST = new Vector4[_count];
            if (chunkIds != null && chunkFilters != null)
            {
                for (int k = 0; k < chunkIds.Length && k < chunkFilters.Length; k++)
                {
                    int ci = chunkIds[k];
                    if (ci < 0 || ci >= _count || chunkFilters[k] == null) continue;
                    _filters[ci] = chunkFilters[k];
                    _bakedMesh[ci] = chunkFilters[k].sharedMesh;
                    if (chunkRenderers != null && k < chunkRenderers.Length && chunkRenderers[k] != null)
                    {
                        _renderers[ci] = chunkRenderers[k];
                        _bakedOn[ci] = chunkRenderers[k].enabled;
                        _bakedLightmap[ci] = chunkRenderers[k].lightmapIndex;
                        _bakedLightmapST[ci] = chunkRenderers[k].lightmapScaleOffset;
                    }
                    if (chunkColliders != null && k < chunkColliders.Length && chunkColliders[k] != null)
                    {
                        _colliders[ci] = chunkColliders[k];
                        _bakedCol[ci] = chunkColliders[k].sharedMesh;
                    }
                }
            }

            _queue = new long[QueueCapacity];
            _dirty = new bool[_count];
            _dirtyList = new int[_count];
            _meshes = new Mesh[_count];
            _changed = new int[6];

            int cells = SurfaceNets.CellBufferSize(chunkCells);
            _cellVert = new int[cells];
            _vMask = new int[cells];
            _vCell = new int[cells];
            _vPos = new Vector3[cells];
            _vNrm = new Vector3[cells];
            _vCol = new Color[cells];
            _vUv = new Vector2[cells];
            _slots = new int[SurfaceNets.SlotCount];
            _tris = new int[SurfaceNets.TriBufferSize(chunkCells)];
            _rowCount = SurfaceNets.RowCount(chunkCells);

            _foliage = foliageMask != null && (treeObjects != null && treeObjects.Length > 0 || detailRenderers != null && detailRenderers.Length > 0);
            if (_foliage && treeObjects != null)
            {
                // Which trees the bake left standing, for resets.
                _treeBaked = new bool[treeObjects.Length];
                for (int t = 0; t < treeObjects.Length; t++) _treeBaked[t] = treeObjects[t] != null && treeObjects[t].activeSelf;
            }

            _sw = new Stopwatch();
#if UNITY_ANDROID || UNITY_IOS
            _budget = budgetMsMobile;
#else
            _budget = budgetMsDesktop;
#endif
        }

        /// <summary>Puts the samples chunk <paramref name="ci"/> holds into _r: {x0, y0, z0, x1, y1, z1}, inclusive.</summary>
        private void _SetRange(int ci)
        {
            int x0, x1, y0, y1, z0, z1;
            DigFormat.ChunkSamples(ci % chunksX, chunkCells, nx, out x0, out x1);
            DigFormat.ChunkSamples((ci / chunksX) % chunksY, chunkCells, ny, out y0, out y1);
            DigFormat.ChunkSamples(ci / (chunksX * chunksY), chunkCells, nz, out z0, out z1);
            _r[0] = x0; _r[1] = y0; _r[2] = z0;
            _r[3] = x1; _r[4] = y1; _r[5] = z1;
        }

        /// <summary>Decodes chunk <paramref name="ci"/> if it is not decoded yet. False if the data is corrupt.</summary>
        private bool _EnsureChunk(int ci)
        {
            if (_cg[ci] != null) return true;
            _SetRange(ci);
            int len = (_r[3] - _r[0] + 1) * (_r[4] - _r[1] + 1) * (_r[5] - _r[2] + 1);
            byte[] g = new byte[len];
            if (!DigRle.DecodeChunk(chunkRle, chunkOffsets, ci, g, _dec, true))
            {
                Debug.LogError("[DigHoleIt] DigZoneRuntime has a corrupt grid. Bake the Dig Zone again.", this);
                _failed = true;
                return false;
            }
            // Offset -1 is a chunk whose paint is all zero: it gets a paint array only once something is painted.
            if (hasPaint && paintOffsets[ci] != -1)
            {
                byte[] p = new byte[len];
                if (!DigRle.DecodeChunk(paintRle, paintOffsets, ci, p, _dec, true))
                {
                    Debug.LogError("[DigHoleIt] DigZoneRuntime has corrupt paint. Bake the Dig Zone again.", this);
                    _failed = true;
                    return false;
                }
                _cp[ci] = p;
            }
            _cg[ci] = g;
            _decoded[_decodedCount] = ci;
            _decodedCount++;
            return true;
        }

        /// <summary>
        /// Puts the grid columns chunk <paramref name="ci"/> owns into _fx0, _fz0, _fw, _fd (its own cells; the last chunk
        /// along an axis also owns the last sample), and the lowest and highest sample it owns as the one below an anchor
        /// into _fy0, _fy1 (DigFoliageBaker.AnchorChunk).
        /// </summary>
        private void _OwnColumns(int ci)
        {
            int cx = ci % chunksX;
            int cy = (ci / chunksX) % chunksY;
            int cz = ci / (chunksX * chunksY);
            _fx0 = cx * chunkCells;
            _fz0 = cz * chunkCells;
            _fy0 = cy * chunkCells;
            _fw = (cx == chunksX - 1 ? nx : _fx0 + chunkCells - 1) - _fx0 + 1;
            _fd = (cz == chunksZ - 1 ? nz : _fz0 + chunkCells - 1) - _fz0 + 1;
            _fy1 = cy == chunksY - 1 ? ny - 1 : _fy0 + chunkCells - 1;
        }

        /// <summary>
        /// Puts the chunks an edit at <paramref name="p"/> (grid units) may change into _er: {cx0, cy0, cz0, cx1, cy1, cz1}.
        /// False if the edit lies outside the editable box.
        /// </summary>
        private bool _EditChunks(Vector3 p, float r)
        {
            float reach = r + DigFormat.SdfBand;
            int x0 = Mathf.Max(editBox[0], Mathf.FloorToInt(p.x - reach));
            int y0 = Mathf.Max(editBox[1], Mathf.FloorToInt(p.y - reach));
            int z0 = Mathf.Max(editBox[2], Mathf.FloorToInt(p.z - reach));
            int x1 = Mathf.Min(editBox[3], Mathf.CeilToInt(p.x + reach));
            int y1 = Mathf.Min(editBox[4], Mathf.CeilToInt(p.y + reach));
            int z1 = Mathf.Min(editBox[5], Mathf.CeilToInt(p.z + reach));
            if (x0 > x1 || y0 > y1 || z0 > z1) return false;
            int a, b;
            DigFormat.AffectedChunks(x0, x1, chunkCells, chunksX, out a, out b);
            _er[0] = a; _er[3] = b;
            DigFormat.AffectedChunks(y0, y1, chunkCells, chunksY, out a, out b);
            _er[1] = a; _er[4] = b;
            DigFormat.AffectedChunks(z0, z1, chunkCells, chunksZ, out a, out b);
            _er[2] = a; _er[5] = b;
            return true;
        }

        /// <summary>Decodes the chunks an edit needs. False if the frame budget ran out first (call again next frame).</summary>
        private bool _PrepareEdit(long e)
        {
            Vector3 p;
            float r;
            int op;
            DigFormat.Unpack(e, out p, out r, out op);
            if (!_EditChunks(p, r)) return true;
            for (int cz = _er[2]; cz <= _er[5]; cz++)
                for (int cy = _er[1]; cy <= _er[4]; cy++)
                    for (int cx = _er[0]; cx <= _er[3]; cx++)
                    {
                        int ci = cx + chunksX * (cy + chunksY * cz);
                        if (_cg[ci] != null) continue;
                        if (!_EnsureChunk(ci)) return true;
                        if (_Elapsed() >= _budget) return false;
                    }
            return true;
        }

        // ---- Public API (local calls only: names start with '_' so they are not network-callable) ----

        /// <summary>True once the runtime has valid baked data. Chunks are decoded later, when edits reach them.</summary>
        public bool _IsReady()
        {
            _Init();
            return !_failed;
        }

        public bool _IsBusy() { return _qCount > 0 || _dirtyCount > 0 || _meshing; }

        /// <summary>Chunks decoded so far: the ones edits have reached since the last reset.</summary>
        public int _DecodedChunkCount() { return _decodedCount; }

        public bool _ContainsWorld(Vector3 world)
        {
            Vector3 g = (world - transform.position) / voxelSize;
            return g.x >= 0f && g.y >= 0f && g.z >= 0f && g.x <= nx && g.y <= ny && g.z <= nz;
        }

        /// <summary>True if the nearest grid sample to <paramref name="world"/> is solid.</summary>
        public bool _IsSolidAt(Vector3 world)
        {
            if (!_IsReady() || !_ContainsWorld(world)) return false;
            Vector3 g = (world - transform.position) / voxelSize;
            int x = Mathf.RoundToInt(g.x);
            int y = Mathf.RoundToInt(g.y);
            int z = Mathf.RoundToInt(g.z);
            int ci = Mathf.Min(x / chunkCells, chunksX - 1)
                   + chunksX * (Mathf.Min(y / chunkCells, chunksY - 1) + chunksY * Mathf.Min(z / chunkCells, chunksZ - 1));
            if (_cg[ci] == null)
            {
                int off = chunkOffsets[ci];
                if (off < 0) return -1 - off < 128; // uniform chunk: no need to decode it
                if (!_EnsureChunk(ci)) return false;
            }
            _SetRange(ci);
            int w = _r[3] - _r[0] + 1;
            int h = _r[4] - _r[1] + 1;
            return _cg[ci][(x - _r[0]) + w * ((y - _r[1]) + h * (z - _r[2]))] < 128;
        }

        public long _PackWorld(Vector3 world, float radiusMeters, int op)
        {
            float r = Mathf.Min(radiusMeters, maxBrushRadius) / voxelSize;
            return DigFormat.Pack((world - transform.position) / voxelSize, r, op);
        }

        /// <summary>Digs (op 0) or adds (op 1) at a world position. Routed through DigSync when present.</summary>
        public void _LocalEdit(Vector3 world, float radiusMeters, int op)
        {
            _LocalEditLayer(world, radiusMeters, op, 0);
        }

        /// <summary>
        /// Like _LocalEdit with a paint layer: the layer to paint for op 3 (paint), or the layer given to added soil for
        /// op 1 (add, 0 = leave as is). Layers: 0 auto, 1-4 terrain layers 0-3, 5 dug soil, 6-17 terrain layers 4-15 (DigFormat.PaintValue).
        /// </summary>
        public void _LocalEditLayer(Vector3 world, float radiusMeters, int op, int layer)
        {
            if (!_IsReady() || !_ContainsWorld(world)) return;
            float r = Mathf.Min(radiusMeters, maxBrushRadius) / voxelSize;
            long e = DigFormat.PackLayer((world - transform.position) / voxelSize, r, op, layer);
            if (sync != null) sync._Submit(e);
            else _EnqueueEdit(e);
        }

        /// <summary>Validates a packed edit received from another player.</summary>
        public bool _IsValidEdit(long e)
        {
            Vector3 p;
            float r;
            int op;
            DigFormat.Unpack(e, out p, out r, out op);
            if (op != DigFormat.OpDig && op != DigFormat.OpAdd && op != DigFormat.OpPaint) return false;
            if (DigFormat.UnpackLayer(e) > DigFormat.LayerMax) return false;
            if (r * voxelSize > maxBrushRadius + voxelSize * 0.25f) return false;
            return p.x <= nx && p.y <= ny && p.z <= nz;
        }

        /// <summary>Queues an edit. The chunks it reaches are decoded first, within the frame budget.</summary>
        public void _EnqueueEdit(long e)
        {
            _Init();
            if (_failed) return;
            if (_qCount >= QueueCapacity)
            {
                _ApplyNow(e);
                return;
            }
            _queue[(_qHead + _qCount) % QueueCapacity] = e;
            _qCount++;
        }

        /// <summary>
        /// Restores the baked grid: drops every decoded chunk and puts the baked meshes back, so it costs nothing per
        /// sample. Edits queued after this call are applied on top.
        /// </summary>
        public void _ResetToOriginal()
        {
            _Init();
            if (_failed) return;
            _qCount = 0;
            _meshing = false;
            while (_dirtyCount > 0)
            {
                _dirtyCount--;
                _dirty[_dirtyList[_dirtyCount]] = false;
            }
            for (int k = 0; k < _decodedCount; k++)
            {
                int ci = _decoded[k];
                _cg[ci] = null;
                _cp[ci] = null;
                Mesh m = _meshes[ci];
                if (m == null) continue;
                m.Clear();
                if (_filters[ci] != null) _filters[ci].sharedMesh = _bakedMesh[ci];
                if (_renderers[ci] != null)
                {
                    _renderers[ci].enabled = _bakedOn[ci];
                    _renderers[ci].lightmapIndex = _bakedLightmap[ci];
                    _renderers[ci].lightmapScaleOffset = _bakedLightmapST[ci];
                }
                if (_colliders[ci] != null)
                {
                    _colliders[ci].sharedMesh = null;
                    _colliders[ci].sharedMesh = _bakedCol[ci];
                }
            }
            _decodedCount = 0;

            if (_foliage && _mask != null)
            {
                // Back to the baked grid: everything the bake left standing stands again.
                _mask = foliageMask.GetPixels32();
                if (_maskTex != null)
                {
                    _maskTex.SetPixels32(_mask);
                    _maskDirty = true;
                }
                if (treeObjects != null && _treeBaked != null)
                    for (int t = 0; t < treeObjects.Length && t < _treeBaked.Length; t++)
                        if (treeObjects[t] != null) treeObjects[t].SetActive(_treeBaked[t]);
            }
        }

        // ---- Frame loop ----

        private void Update()
        {
            if (_failed || (_qCount == 0 && _dirtyCount == 0 && !_meshing)) return;

            _sw.Reset();
            _sw.Start();

            while (_qCount > 0)
            {
                long e = _queue[_qHead];
                if (!_PrepareEdit(e)) return;
                _qHead = (_qHead + 1) % QueueCapacity;
                _qCount--;
                _ApplyNow(e);
                if (_failed) return;
                if (_Elapsed() >= _budget) return;
            }

            // Mesh only once the queue is drained, so a late-join replay remeshes each chunk once.
            while (_Elapsed() < _budget)
            {
                if (!_meshing)
                {
                    if (_dirtyCount == 0) break;
                    _BeginChunk(_PopNearestDirty());
                }
                float before = _Elapsed();
                _StepMesh();
                _chunkMs += _Elapsed() - before;
            }
        }

        private void LateUpdate()
        {
            if (!_maskDirty) return;
            _maskDirty = false;
            _maskTex.Apply(false);
        }

        private float _Elapsed()
        {
            return (float)_sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Stamps an edit into every chunk it reaches (decoding them if needed) and marks the changed ones dirty.</summary>
        private void _ApplyNow(long e)
        {
            Vector3 p;
            float r;
            int op;
            DigFormat.Unpack(e, out p, out r, out op);
            int layer = DigFormat.UnpackLayer(e);
            if (op != DigFormat.OpDig && op != DigFormat.OpAdd && op != DigFormat.OpPaint) return;
            if (!_EditChunks(p, r)) return;
            bool paints = op == DigFormat.OpPaint || (op == DigFormat.OpAdd && layer > 0);

            // Neighbouring chunks share their border samples, and every copy gets the same edit, so they stay equal.
            for (int cz = _er[2]; cz <= _er[5]; cz++)
                for (int cy = _er[1]; cy <= _er[4]; cy++)
                    for (int cx = _er[0]; cx <= _er[3]; cx++)
                    {
                        int ci = cx + chunksX * (cy + chunksY * cz);
                        if (!_EnsureChunk(ci)) return;
                        _SetRange(ci);
                        _box[0] = Mathf.Max(editBox[0], _r[0]) - _r[0];
                        _box[1] = Mathf.Max(editBox[1], _r[1]) - _r[1];
                        _box[2] = Mathf.Max(editBox[2], _r[2]) - _r[2];
                        _box[3] = Mathf.Min(editBox[3], _r[3]) - _r[0];
                        _box[4] = Mathf.Min(editBox[4], _r[4]) - _r[1];
                        _box[5] = Mathf.Min(editBox[5], _r[5]) - _r[2];
                        if (_box[0] > _box[3] || _box[1] > _box[4] || _box[2] > _box[5]) continue;

                        // Paint lands only inside the sphere; a chunk the sphere misses gets an empty stand-in,
                        // which the brush never indexes.
                        byte[] pg = _cp[ci];
                        if (paints && pg == null)
                        {
                            bool inside = p.x + r >= _r[0] - 1 && p.x - r <= _r[3] + 1 && p.y + r >= _r[1] - 1 &&
                                          p.y - r <= _r[4] + 1 && p.z + r >= _r[2] - 1 && p.z - r <= _r[5] + 1;
                            if (inside)
                            {
                                pg = new byte[_cg[ci].Length];
                                _cp[ci] = pg;
                            }
                            else pg = _noPaint;
                        }

                        if (DigBrush.Stamp(_cg[ci], pg, _r[3] - _r[0], _r[4] - _r[1], _box,
                                p.x - _r[0], p.y - _r[1], p.z - _r[2], r, op, layer, _changed))
                            _MarkDirty(ci);
                    }
        }

        private void _MarkDirty(int ci)
        {
            if (_dirty[ci]) return;
            _dirty[ci] = true;
            _dirtyList[_dirtyCount] = ci;
            _dirtyCount++;
        }

        private int _PopNearestDirty()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            Vector3 eye = lp != null ? lp.GetPosition() : transform.position;
            Vector3 local = (eye - transform.position) / (voxelSize * chunkCells);

            int best = 0;
            float bestD = float.MaxValue;
            for (int k = 0; k < _dirtyCount; k++)
            {
                int ci = _dirtyList[k];
                int cx = ci % chunksX;
                int cy = (ci / chunksX) % chunksY;
                int cz = ci / (chunksX * chunksY);
                float dx = cx + 0.5f - local.x;
                float dy = cy + 0.5f - local.y;
                float dz = cz + 0.5f - local.z;
                float d = dx * dx + dy * dy + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }

            int chosen = _dirtyList[best];
            _dirtyCount--;
            _dirtyList[best] = _dirtyList[_dirtyCount];
            _dirty[chosen] = false;
            return chosen;
        }

        private void _BeginChunk(int ci)
        {
            _meshChunk = ci;
            _SetRange(ci);
            _mGrid = _cg[ci];
            _mPaint = _cp[ci];
            _lnx = _r[3] - _r[0];
            _lny = _r[4] - _r[1];
            _lnz = _r[5] - _r[2];
            // Chunk origin relative to the chunk's own samples.
            _ox = (ci % chunksX) * chunkCells - _r[0];
            _oy = ((ci / chunksX) % chunksY) * chunkCells - _r[1];
            _oz = (ci / (chunksX * chunksY)) * chunkCells - _r[2];
            _phase = 0;
            _row = 0;
            _vertCount = 0;
            _quadV = 0;
            _paintV = 0;
            SurfaceNets.ResetSlots(_slots);
            _triCount = 0;
            _chunkMs = 0f;
            _meshing = true;

            _fCol = 0;
            _fCount = 0;
            _fChanged = false;
            if (_foliage && _LoadMask())
            {
                _OwnColumns(ci);
                _fCount = _fw * _fd;
                // _r changes when queued edits are applied between mesh steps.
                _fr0 = _r[0];
                _fr1 = _r[1];
                _fr2 = _r[2];
                _fr3 = _r[3];
                _fr4 = _r[4];
                _fr5 = _r[5];
            }
        }

        private void _StepMesh()
        {
            if (_phase == 0)
            {
                int end = Mathf.Min(_row + RowsPerStep, _rowCount);
                _vertCount = SurfaceNets.BuildRows(_mGrid, _lnx, _lny, _lnz, _ox, _oy, _oz, chunkCells, voxelSize,
                    _row, end, _cellVert, _vPos, _vNrm, _vMask, _vCell, _vertCount);
                _row = end;
                if (_row >= _rowCount)
                {
                    _phase = 1;
                    if (_mPaint == null)
                    {
                        // Never painted: upload zero weights (auto shading everywhere).
                        System.Array.Clear(_vCol, 0, _vertCount);
                        System.Array.Clear(_vUv, 0, _vertCount);
                        _phase = 2;
                    }
                }
                return;
            }

            if (_phase == 1)
            {
                int end = Mathf.Min(_paintV + PaintPerStep, _vertCount);
                SurfaceNets.BuildPaint(_mPaint, _lnx, _lny, _ox, _oy, _oz, chunkCells, voxelSize,
                    _vCell, _vPos, _paintV, end, _vCol, _vUv, _slots);
                _paintV = end;
                if (_paintV >= _vertCount)
                {
                    _phase = 4;
                    _paintV = 0;
                }
                return;
            }

            if (_phase == 4)
            {
                // Every vertex carries the chunk's slot layers, known only once all paint is read.
                int end = Mathf.Min(_paintV + PaintPerStep * 8, _vertCount);
                SurfaceNets.FinishPaint(_slots, _vUv, _paintV, end);
                _paintV = end;
                if (_paintV >= _vertCount) _phase = 2;
                return;
            }

            if (_phase == 2)
            {
                int end = Mathf.Min(_quadV + QuadsPerStep, _vertCount);
                _triCount = SurfaceNets.BuildQuads(chunkCells, _cellVert, _vMask, _vCell, _vPos, _quadV, end, _tris, _triCount);
                _quadV = end;
                if (_quadV >= _vertCount) _phase = _fCount > 0 ? 5 : 3;
                return;
            }

            if (_phase == 5)
            {
                // Details: the chunk's own columns whose anchor lies in its own samples.
                int end = Mathf.Min(_fCol + ColumnsPerStep, _fCount);
                int w = _fr3 - _fr0 + 1;
                int h = _fr4 - _fr1 + 1;
                int stride = nx + 1;
                for (int k = _fCol; k < end; k++)
                {
                    int gx = _fx0 + k % _fw;
                    int gz = _fz0 + k / _fw;
                    int col = gx + stride * gz;
                    Color32 c = _mask[col];
                    if (c.a == 0) continue;
                    float anchor = ((c.g << 8) | c.b) / DigFoliage.AnchorScale;
                    int ay = Mathf.Min((int)anchor, ny - 1);
                    if (ay < _fy0 || ay > _fy1) continue;
                    int i = (gx - _fr0) + w * ((ay - _fr1) + h * (gz - _fr2));
                    byte r = DigFoliage.StandsBetween(_mGrid[i], _mGrid[i + w], anchor - ay) ? DigFoliage.Standing : DigFoliage.Removed;
                    if (c.r == r) continue;
                    c.r = r;
                    _mask[col] = c;
                    _fChanged = true;
                }
                _fCol = end;
                if (_fCol >= _fCount)
                {
                    _CheckSurfaceDetails();
                    if (_fChanged || _sHi >= _sLo) _WriteMask();
                    _ShowTrees();
                    _phase = 3;
                }
                return;
            }

            _Upload();
            _meshing = false;
        }

        /// <summary>Loads the baked foliage mask (anchors and what the bake left standing) once. False if it can't be read.</summary>
        private bool _LoadMask()
        {
            if (_mask != null) return true;
            if (!foliageMask.isReadable)
            {
                Debug.LogError("[DigHoleIt] The foliage mask of this Dig Zone is not readable. Bake the Dig Zone again.", this);
                _foliage = false;
                return false;
            }
            _mask = foliageMask.GetPixels32();
            // Rows above the grid columns hold the surface details.
            if (_mask == null || _mask.Length < (nx + 1) * (nz + 1) || foliageMask.width != nx + 1)
            {
                Debug.LogError("[DigHoleIt] The foliage mask of this Dig Zone does not fit its grid. Bake the Dig Zone again.", this);
                _mask = null;
                _foliage = false;
                return false;
            }
            return true;
        }

        /// <summary>Writes the meshed chunk's own columns (_fx0.. from _OwnColumns) into the live mask texture.</summary>
        private void _WriteMask()
        {
            if (_maskTex == null)
            {
                _maskTex = new Texture2D(foliageMask.width, foliageMask.height, TextureFormat.RGBA32, false, true);
                _maskTex.filterMode = FilterMode.Point;
                _maskTex.wrapMode = TextureWrapMode.Clamp;
                _maskTex.SetPixels32(_mask);
                // The detail materials are shared assets: give the renderers the live mask instead.
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                if (detailRenderers != null)
                {
                    for (int i = 0; i < detailRenderers.Length; i++)
                    {
                        MeshRenderer r = detailRenderers[i];
                        if (r == null) continue;
                        r.GetPropertyBlock(block);
                        block.SetTexture("_DigFoliageMask", _maskTex);
                        r.SetPropertyBlock(block);
                    }
                }
            }
            else
            {
                int stride = nx + 1;
                if (_fChanged)
                {
                    Color32[] px = new Color32[_fw * _fd];
                    for (int lz = 0; lz < _fd; lz++)
                        for (int lx = 0; lx < _fw; lx++)
                            px[lx + _fw * lz] = _mask[(_fx0 + lx) + stride * (_fz0 + lz)];
                    _maskTex.SetPixels32(_fx0, _fz0, _fw, _fd, px);
                }
                if (_sHi >= _sLo)
                {
                    // The rows holding the changed surface details.
                    int row0 = _sLo / stride;
                    int rows = _sHi / stride - row0 + 1;
                    Color32[] px = new Color32[stride * rows];
                    System.Array.Copy(_mask, row0 * stride, px, 0, Mathf.Min(px.Length, _mask.Length - row0 * stride));
                    _maskTex.SetPixels32(0, row0, stride, rows, px);
                }
            }
            _maskDirty = true;
        }

        /// <summary>
        /// Checks the surface details (walls, cave ceilings) anchored in the meshed chunk against its samples, like the
        /// trees, and puts the texels that changed into _sLo.._sHi.
        /// </summary>
        private void _CheckSurfaceDetails()
        {
            _sLo = 1;
            _sHi = 0;
            if (surfaceDetailAnchors == null || surfaceDetailBuckets == null) return;
            int ci = _meshChunk;
            if (ci + 1 >= surfaceDetailBuckets.Length) return;
            int w = _fr3 - _fr0 + 1;
            int h = _fr4 - _fr1 + 1;
            int texel0 = (nx + 1) * (nz + 1);
            int end = Mathf.Min(surfaceDetailBuckets[ci + 1], Mathf.Min(surfaceDetailAnchors.Length, _mask.Length - texel0));
            for (int k = surfaceDetailBuckets[ci]; k < end; k++)
            {
                Vector3 a = surfaceDetailAnchors[k];
                int x = Mathf.Clamp(Mathf.FloorToInt(a.x), _fr0, _fr3 - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(a.y), _fr1, _fr4 - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt(a.z), _fr2, _fr5 - 1);
                int i = (x - _fr0) + w * ((y - _fr1) + h * (z - _fr2));
                byte r = DigFoliage.StandsInCell(_mGrid, i, w, w * h, DigFoliage.Frac(a.x, x), DigFoliage.Frac(a.y, y), DigFoliage.Frac(a.z, z))
                    ? DigFoliage.Standing : DigFoliage.Removed;
                Color32 c = _mask[texel0 + k];
                if (c.r == r) continue;
                c.r = r;
                _mask[texel0 + k] = c;
                if (_sHi < _sLo)
                {
                    _sLo = texel0 + k;
                    _sHi = texel0 + k;
                }
                else _sHi = texel0 + k;
            }
        }

        /// <summary>Shows the trees anchored in the meshed chunk that still stand and hides the others.</summary>
        private void _ShowTrees()
        {
            if (treeObjects == null || treeAnchors == null || treeBuckets == null) return;
            int ci = _meshChunk;
            if (ci + 1 >= treeBuckets.Length) return;
            int w = _fr3 - _fr0 + 1;
            int h = _fr4 - _fr1 + 1;
            int end = Mathf.Min(treeBuckets[ci + 1], Mathf.Min(treeObjects.Length, treeAnchors.Length));
            for (int t = treeBuckets[ci]; t < end; t++)
            {
                GameObject go = treeObjects[t];
                if (go == null) continue;
                // The chunk holds the grid cell around the anchor (DigFoliageBaker.AnchorChunk).
                Vector3 a = treeAnchors[t];
                int x = Mathf.Clamp(Mathf.FloorToInt(a.x), _fr0, _fr3 - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(a.y), _fr1, _fr4 - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt(a.z), _fr2, _fr5 - 1);
                int i = (x - _fr0) + w * ((y - _fr1) + h * (z - _fr2));
                go.SetActive(DigFoliage.StandsInCell(_mGrid, i, w, w * h, DigFoliage.Frac(a.x, x), DigFoliage.Frac(a.y, y), DigFoliage.Frac(a.z, z)));
            }
        }

        /// <summary>Gives chunk <paramref name="ci"/> a GameObject copied from <see cref="chunkTemplate"/>.</summary>
        private bool _CreateChunkObject(int ci)
        {
            if (chunkTemplate == null) return false;
            int cx = ci % chunksX;
            int cy = (ci / chunksX) % chunksY;
            int cz = ci / (chunksX * chunksY);
            GameObject go = Instantiate(chunkTemplate, chunkTemplate.transform.parent);
            go.name = "Chunk_" + cx + "_" + cy + "_" + cz;
            go.transform.localPosition = new Vector3(cx, cy, cz) * (chunkCells * voxelSize);
            go.SetActive(true);
            _filters[ci] = go.GetComponent<MeshFilter>();
            _renderers[ci] = go.GetComponent<MeshRenderer>();
            _colliders[ci] = go.GetComponent<MeshCollider>();
            return _filters[ci] != null;
        }

        private void _Upload()
        {
            int ci = _meshChunk;
            MeshFilter filter = _filters[ci];
            if (filter == null)
            {
                if (_triCount == 0 || !_CreateChunkObject(ci)) return;
                filter = _filters[ci];
            }
            MeshRenderer rend = _renderers[ci];
            MeshCollider col = _colliders[ci];

            Mesh m = _meshes[ci];
            if (m == null)
            {
                // Never write into the baked mesh asset: in the editor that would persist play-mode digs.
                m = new Mesh();
                m.name = "DigChunk_" + ci;
                _meshes[ci] = m;
            }
            m.Clear();
            filter.sharedMesh = m;

            if (_triCount == 0)
            {
                if (col != null) col.sharedMesh = null;
                if (rend != null) rend.enabled = false;
                return;
            }

            Vector3[] verts = new Vector3[_vertCount];
            Vector3[] norms = new Vector3[_vertCount];
            int[] tris = new int[_triCount];
            System.Array.Copy(_vPos, verts, _vertCount);
            System.Array.Copy(_vNrm, norms, _vertCount);
            System.Array.Copy(_tris, tris, _triCount);
            m.vertices = verts;
            m.normals = norms;
            m.SetColors(_vCol, 0, _vertCount);
            m.SetUVs(0, _vUv, 0, _vertCount);
            m.triangles = tris;
            m.RecalculateBounds();

            if (col != null)
            {
                col.sharedMesh = null;
                col.sharedMesh = m;
            }
            if (rend != null)
            {
                rend.enabled = true;
                // The baked lightmap no longer fits the new mesh: light it with light probes instead.
                if (rend.lightmapIndex >= 0 && rend.lightmapIndex < 0xFFFE) rend.lightmapIndex = -1;
            }

            if (logTimings)
                Debug.Log("[DigHoleIt] chunk " + ci + ": " + _vertCount + " verts, " + (_triCount / 3) + " tris, " + _chunkMs.ToString("F2") + " ms");
        }
    }
}
