using System;
using System.Collections.Generic;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Plain C# runtime for non-VRChat games. Uses the same grid, edit format and mesher as the Udon runtime.
    ///
    /// Networking is left to your game: subscribe to <see cref="LocalEditRequested"/> to send edits, and call
    /// <see cref="ApplyEdit"/> for edits you receive. <see cref="EditLog"/> / <see cref="LoadEdits"/> cover
    /// save games and late joiners.
    /// </summary>
    [RequireComponent(typeof(DigZone))]
    [AddComponentMenu("DigHoleIt/Dig Zone Runtime (Standalone)")]
    public class DigZoneRuntimeStandalone : MonoBehaviour
    {
        [Tooltip("Chunks remeshed per frame. 0 = all dirty chunks immediately.")]
        [Min(0)] public int maxChunksPerFrame = 4;

        /// <summary>Raised when this client makes an edit (before it is applied locally). Send it to other clients.</summary>
        public event Action<long> LocalEditRequested;

        /// <summary>Raised after any edit changed the grid (local or remote).</summary>
        public event Action<long> EditApplied;

        private DigZone _zone;
        private DigZoneData _data;
        private byte[] _grid;
        private byte[] _paint;
        private ChunkMesher _mesher;
        private Mesh[] _meshes;
        private bool[] _dirty;
        private readonly List<int> _dirtyList = new List<int>();
        private readonly List<long> _log = new List<long>();
        private readonly int[] _changed = new int[6];

        public IReadOnlyList<long> EditLog => _log;
        public byte[] Grid => _grid;
        public byte[] PaintGrid => _paint;
        public DigZone Zone => _zone;

        private void Awake()
        {
            _zone = GetComponent<DigZone>();
            _data = _zone.data;
            if (_data == null || !_data.HasGrid)
            {
                Debug.LogError("[DigHoleIt] Dig Zone is not baked.", this);
                enabled = false;
                return;
            }

            // Work on a copy so play-mode digging never writes into the asset.
            _grid = (byte[])_data.grid.Clone();
            _paint = _data.HasPaintGrid ? (byte[])_data.paint.Clone() : new byte[_grid.Length];
            _mesher = new ChunkMesher(_data.chunkCells);
            _meshes = new Mesh[_data.ChunkCount];
            _dirty = new bool[_data.ChunkCount];
        }

        public bool Contains(Vector3 world)
        {
            Vector3 g = (world - transform.position) / _data.voxelSize;
            return g.x >= 0 && g.y >= 0 && g.z >= 0 && g.x <= _data.nx && g.y <= _data.ny && g.z <= _data.nz;
        }

        public void Dig(Vector3 world, float radiusMeters) => LocalEdit(world, radiusMeters, DigFormat.OpDig);

        public void Add(Vector3 world, float radiusMeters) => LocalEdit(world, radiusMeters, DigFormat.OpAdd);

        /// <summary>Paints <paramref name="layer"/> (DigFormat: 0 auto, 1-4 terrain layers, 5 dug soil) onto the voxels in the sphere.</summary>
        public void Paint(Vector3 world, float radiusMeters, int layer) => LocalEdit(world, radiusMeters, DigFormat.OpPaint, layer);

        /// <param name="layer">Paint layer for OpPaint, or the layer given to added soil for OpAdd (0 = leave as is).</param>
        public void LocalEdit(Vector3 world, float radiusMeters, int op, int layer = 0)
        {
            if (!enabled || !Contains(world)) return;
            float r = Mathf.Min(radiusMeters, _zone.maxBrushRadius) / _data.voxelSize;
            long e = DigFormat.PackLayer((world - transform.position) / _data.voxelSize, r, op, layer);
            LocalEditRequested?.Invoke(e);
            ApplyEdit(e);
        }

        /// <summary>Applies a packed edit (dig, add and paint are idempotent). Returns true if the grid changed.</summary>
        public bool ApplyEdit(long e)
        {
            if (!enabled) return false;
            DigFormat.Unpack(e, out Vector3 p, out float r, out int op);
            int layer = DigFormat.UnpackLayer(e);
            if (op != DigFormat.OpDig && op != DigFormat.OpAdd && op != DigFormat.OpPaint) return false;
            if (layer > DigFormat.LayerMax) return false;
            if (!DigBrush.Stamp(_grid, _paint, _data.nx, _data.ny, _data.editBox, p.x, p.y, p.z, r, op, layer, _changed)) return false;

            _log.Add(e);
            DigFormat.AffectedChunks(_changed[0], _changed[3], _data.chunkCells, _data.ChunksX, out int cx0, out int cx1);
            DigFormat.AffectedChunks(_changed[1], _changed[4], _data.chunkCells, _data.ChunksY, out int cy0, out int cy1);
            DigFormat.AffectedChunks(_changed[2], _changed[5], _data.chunkCells, _data.ChunksZ, out int cz0, out int cz1);
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
                MarkDirty(_data.ChunkIndex(cx, cy, cz));

            EditApplied?.Invoke(e);
            return true;
        }

        /// <summary>Restores the baked grid, then replays <paramref name="edits"/> (for loading or late join).</summary>
        public void LoadEdits(IEnumerable<long> edits)
        {
            ResetToBaked();
            foreach (long e in edits) ApplyEdit(e);
        }

        public void ResetToBaked()
        {
            Array.Copy(_data.grid, _grid, _grid.Length);
            if (_data.HasPaintGrid) Array.Copy(_data.paint, _paint, _paint.Length);
            else Array.Clear(_paint, 0, _paint.Length);
            _log.Clear();
            for (int i = 0; i < _dirty.Length; i++) MarkDirty(i);
        }

        /// <summary>Marching-ray hit against the grid, independent of physics. <paramref name="hit"/> is world space.</summary>
        public bool Raycast(Ray ray, float maxDistance, out Vector3 hit)
        {
            float v = _data.voxelSize;
            bool ok = DigGridUtil.Raycast(_grid, _data.nx, _data.ny, _data.nz, (ray.origin - transform.position) / v, ray.direction, maxDistance / v, out Vector3 g);
            hit = transform.position + g * v;
            return ok;
        }

        private void MarkDirty(int ci)
        {
            if (_dirty[ci]) return;
            _dirty[ci] = true;
            _dirtyList.Add(ci);
        }

        private void LateUpdate()
        {
            if (_dirtyList.Count == 0) return;

            if (maxChunksPerFrame > 0 && _dirtyList.Count > maxChunksPerFrame)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 eye = (cam.transform.position - transform.position) / (_data.voxelSize * _data.chunkCells);
                    _dirtyList.Sort((a, b) => ChunkDistance(a, eye).CompareTo(ChunkDistance(b, eye)));
                }
            }

            int n = maxChunksPerFrame == 0 ? _dirtyList.Count : Mathf.Min(maxChunksPerFrame, _dirtyList.Count);
            for (int k = 0; k < n; k++)
            {
                int ci = _dirtyList[k];
                _dirty[ci] = false;
                Rebuild(ci);
            }
            _dirtyList.RemoveRange(0, n);
        }

        private float ChunkDistance(int ci, Vector3 eye)
        {
            int cx = ci % _data.ChunksX;
            int cy = (ci / _data.ChunksX) % _data.ChunksY;
            int cz = ci / (_data.ChunksX * _data.ChunksY);
            return (new Vector3(cx + 0.5f, cy + 0.5f, cz + 0.5f) - eye).sqrMagnitude;
        }

        private void Rebuild(int ci)
        {
            if (_zone.chunkFilters == null || ci >= _zone.chunkFilters.Length || _zone.chunkFilters[ci] == null) return;

            int cx = ci % _data.ChunksX;
            int cy = (ci / _data.ChunksX) % _data.ChunksY;
            int cz = ci / (_data.ChunksX * _data.ChunksY);
            _mesher.Build(_data, _grid, _paint, cx, cy, cz);

            Mesh m = _meshes[ci];
            if (m == null)
            {
                m = new Mesh { name = "DigChunk_" + ci };
                m.MarkDynamic();
                _meshes[ci] = m;
                _zone.chunkFilters[ci].sharedMesh = m;
            }
            bool has = _mesher.WriteTo(m);
            if (has) m.RecalculateBounds();

            MeshRenderer rend = _zone.chunkRenderers[ci];
            if (rend != null) rend.enabled = has;
            MeshCollider col = _zone.chunkColliders[ci];
            if (col != null)
            {
                col.sharedMesh = null;
                if (has) col.sharedMesh = m;
            }
        }

        private void OnDestroy()
        {
            if (_meshes == null) return;
            foreach (Mesh m in _meshes) if (m != null) Destroy(m);
        }
    }
}
