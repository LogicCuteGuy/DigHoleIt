using System.Diagnostics;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using Debug = UnityEngine.Debug;

namespace LogicCuteGuy.DigHoleIt.Udon
{
    /// <summary>
    /// VRChat runtime for one baked Dig Zone. Owns the SDF grid, applies queued edits, and remeshes dirty chunks
    /// within a per-frame time budget. Fields under "Baked" are written by the editor baker; do not edit by hand.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class DigZoneRuntime : UdonSharpBehaviour
    {
        private const int QueueCapacity = 16384;
        private const int RowsPerStep = 4;
        private const int QuadsPerStep = 256;
        private const int PaintPerStep = 128;

        [Header("Baked")]
        // Persisted by the baker; hidden because the inspector cannot draw a ~1 MB array usefully.
        [HideInInspector] public byte[] grid;
        [Tooltip("Paint layer per sample. Null when the zone was baked without paint.")]
        [HideInInspector] public byte[] paint;
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
        public MeshFilter[] chunkFilters;
        public MeshRenderer[] chunkRenderers;
        public MeshCollider[] chunkColliders;

        [Header("Runtime")]
        [Tooltip("Optional. Without it, edits stay local to this player.")]
        public DigSync sync;
        [Tooltip("Milliseconds per frame spent on edits and meshing (PC).")]
        public float budgetMsDesktop = 2.5f;
        [Tooltip("Milliseconds per frame spent on edits and meshing (Android/iOS).")]
        public float budgetMsMobile = 1.2f;
        public bool logTimings;

        private bool _ready;
        private byte[] _original;
        private byte[] _originalPaint;
        private bool _paintUsed;
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

        private bool _meshing;
        private int _meshChunk;
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

        private void Start()
        {
            int chunkCount = chunksX * chunksY * chunksZ;
            if (grid == null || grid.Length != (nx + 1) * (ny + 1) * (nz + 1) || chunkCount == 0 || editBox == null || editBox.Length != 6)
            {
                Debug.LogError("[DigHoleIt] DigZoneRuntime has no baked data. Bake the Dig Zone in the editor.", this);
                return;
            }

            _original = new byte[grid.Length];
            System.Array.Copy(grid, _original, grid.Length);
            if (paint == null || paint.Length != grid.Length)
            {
                paint = new byte[grid.Length];
                hasPaint = false;
            }
            _originalPaint = new byte[grid.Length];
            System.Array.Copy(paint, _originalPaint, grid.Length);
            _paintUsed = hasPaint;

            _queue = new long[QueueCapacity];
            _dirty = new bool[chunkCount];
            _dirtyList = new int[chunkCount];
            _meshes = new Mesh[chunkCount];
            _changed = new int[6];

            int cells = SurfaceNets.CellBufferSize(chunkCells);
            _cellVert = new int[cells];
            _vMask = new int[cells];
            _vCell = new int[cells];
            _vPos = new Vector3[cells];
            _vNrm = new Vector3[cells];
            _vCol = new Color[cells];
            _vUv = new Vector2[cells];
            _tris = new int[SurfaceNets.TriBufferSize(chunkCells)];
            _rowCount = SurfaceNets.RowCount(chunkCells);

            _sw = new Stopwatch();
#if UNITY_ANDROID || UNITY_IOS
            _budget = budgetMsMobile;
#else
            _budget = budgetMsDesktop;
#endif
            _ready = true;
        }

        // ---- Public API (local calls only: names start with '_' so they are not network-callable) ----

        public bool _IsReady() { return _ready; }

        public bool _IsBusy() { return _qCount > 0 || _dirtyCount > 0 || _meshing; }

        public bool _ContainsWorld(Vector3 world)
        {
            Vector3 g = (world - transform.position) / voxelSize;
            return g.x >= 0f && g.y >= 0f && g.z >= 0f && g.x <= nx && g.y <= ny && g.z <= nz;
        }

        /// <summary>True if the nearest grid sample to <paramref name="world"/> is solid.</summary>
        public bool _IsSolidAt(Vector3 world)
        {
            if (!_ready || !_ContainsWorld(world)) return false;
            Vector3 g = (world - transform.position) / voxelSize;
            int x = Mathf.RoundToInt(g.x);
            int y = Mathf.RoundToInt(g.y);
            int z = Mathf.RoundToInt(g.z);
            return grid[x + (nx + 1) * (y + (ny + 1) * z)] < 128;
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
        /// op 1 (add, 0 = leave as is). Layers: 0 auto, 1-4 terrain layers 0-3, 5 dug soil.
        /// </summary>
        public void _LocalEditLayer(Vector3 world, float radiusMeters, int op, int layer)
        {
            if (!_ready || !_ContainsWorld(world)) return;
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

        public void _EnqueueEdit(long e)
        {
            if (!_ready) return;
            if (_qCount >= QueueCapacity)
            {
                _ApplyNow(e);
                return;
            }
            _queue[(_qHead + _qCount) % QueueCapacity] = e;
            _qCount++;
        }

        /// <summary>Restores the baked grid and remeshes every chunk.</summary>
        public void _ResetToOriginal()
        {
            if (!_ready) return;
            System.Array.Copy(_original, grid, grid.Length);
            System.Array.Copy(_originalPaint, paint, paint.Length);
            _paintUsed = hasPaint;
            _qCount = 0;
            int count = chunksX * chunksY * chunksZ;
            for (int i = 0; i < count; i++) _MarkDirty(i);
        }

        // ---- Frame loop ----

        private void Update()
        {
            if (!_ready || (_qCount == 0 && _dirtyCount == 0 && !_meshing)) return;

            _sw.Reset();
            _sw.Start();

            while (_qCount > 0)
            {
                long e = _queue[_qHead];
                _qHead = (_qHead + 1) % QueueCapacity;
                _qCount--;
                _ApplyNow(e);
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

        private float _Elapsed()
        {
            return (float)_sw.Elapsed.TotalMilliseconds;
        }

        private void _ApplyNow(long e)
        {
            Vector3 p;
            float r;
            int op;
            DigFormat.Unpack(e, out p, out r, out op);
            int layer = DigFormat.UnpackLayer(e);
            if (op != DigFormat.OpDig && op != DigFormat.OpAdd && op != DigFormat.OpPaint) return;
            if (!DigBrush.Stamp(grid, paint, nx, ny, editBox, p.x, p.y, p.z, r, op, layer, _changed)) return;
            if (op == DigFormat.OpPaint || (op == DigFormat.OpAdd && layer > 0)) _paintUsed = true;

            int cx0, cx1, cy0, cy1, cz0, cz1;
            DigFormat.AffectedChunks(_changed[0], _changed[3], chunkCells, chunksX, out cx0, out cx1);
            DigFormat.AffectedChunks(_changed[1], _changed[4], chunkCells, chunksY, out cy0, out cy1);
            DigFormat.AffectedChunks(_changed[2], _changed[5], chunkCells, chunksZ, out cz0, out cz1);
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++)
                        _MarkDirty(cx + chunksX * (cy + chunksY * cz));
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
            _ox = (ci % chunksX) * chunkCells;
            _oy = ((ci / chunksX) % chunksY) * chunkCells;
            _oz = (ci / (chunksX * chunksY)) * chunkCells;
            _phase = 0;
            _row = 0;
            _vertCount = 0;
            _quadV = 0;
            _paintV = 0;
            _triCount = 0;
            _chunkMs = 0f;
            _meshing = true;
        }

        private void _StepMesh()
        {
            if (_phase == 0)
            {
                int end = Mathf.Min(_row + RowsPerStep, _rowCount);
                _vertCount = SurfaceNets.BuildRows(grid, nx, ny, nz, _ox, _oy, _oz, chunkCells, voxelSize,
                    _row, end, _cellVert, _vPos, _vNrm, _vMask, _vCell, _vertCount);
                _row = end;
                if (_row >= _rowCount)
                {
                    _phase = 1;
                    if (!_paintUsed)
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
                SurfaceNets.BuildPaint(paint, nx, ny, _ox, _oy, _oz, chunkCells, voxelSize,
                    _vCell, _vPos, _paintV, end, _vCol, _vUv);
                _paintV = end;
                if (_paintV >= _vertCount) _phase = 2;
                return;
            }

            if (_phase == 2)
            {
                int end = Mathf.Min(_quadV + QuadsPerStep, _vertCount);
                _triCount = SurfaceNets.BuildQuads(chunkCells, _cellVert, _vMask, _vCell, _vPos, _quadV, end, _tris, _triCount);
                _quadV = end;
                if (_quadV >= _vertCount) _phase = 3;
                return;
            }

            _Upload();
            _meshing = false;
        }

        private void _Upload()
        {
            int ci = _meshChunk;
            MeshFilter filter = chunkFilters[ci];
            if (filter == null) return;
            MeshRenderer rend = chunkRenderers[ci];
            MeshCollider col = chunkColliders[ci];

            Mesh m = _meshes[ci];
            if (m == null)
            {
                // Never write into the baked mesh asset: in the editor that would persist play-mode digs.
                m = new Mesh();
                m.name = "DigChunk_" + ci;
                _meshes[ci] = m;
                filter.sharedMesh = m;
            }
            m.Clear();

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
            if (rend != null) rend.enabled = true;

            if (logTimings)
                Debug.Log("[DigHoleIt] chunk " + ci + ": " + _vertCount + " verts, " + (_triCount / 3) + " tris, " + _chunkMs.ToString("F2") + " ms");
        }
    }
}
