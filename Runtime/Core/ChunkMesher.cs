using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>Plain C# driver for <see cref="SurfaceNets"/>: builds a whole chunk in one call and writes it to a Mesh.</summary>
    public sealed class ChunkMesher
    {
        private readonly int _n;
        private readonly int[] _cellVert;
        private readonly int[] _vMask;
        private readonly int[] _vCell;
        private int[] _tris;
        private Vector3[] _vPos;
        private Vector3[] _vNrm;
        private Color[] _vCol;
        private Vector2[] _vUv;
        private readonly int[] _slots = new int[SurfaceNets.SlotCount];

        // ClipXZ scratch.
        private Vector2 _clipMin, _clipMax;
        private readonly List<Vector3> _pos = new List<Vector3>();
        private readonly List<Vector3> _nrm = new List<Vector3>();
        private readonly List<Color> _col = new List<Color>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _idx = new List<int>();
        private readonly List<int> _poly = new List<int>();
        private readonly List<int> _next = new List<int>();
        private readonly Dictionary<long, int> _cut = new Dictionary<long, int>();

        public int VertexCount { get; private set; }
        public int IndexCount { get; private set; }

        public ChunkMesher(int chunkCells)
        {
            _n = chunkCells;
            int cells = SurfaceNets.CellBufferSize(chunkCells);
            _cellVert = new int[cells];
            _vMask = new int[cells];
            _vCell = new int[cells];
            _vPos = new Vector3[cells];
            _vNrm = new Vector3[cells];
            _vCol = new Color[cells];
            _vUv = new Vector2[cells];
            _tris = new int[SurfaceNets.TriBufferSize(chunkCells)];
        }

        /// <param name="paint">Paint grid, or null for an unpainted zone.</param>
        public void Build(byte[] grid, byte[] paint, int nx, int ny, int nz, int cx, int cy, int cz, float voxel)
        {
            int ox = cx * _n, oy = cy * _n, oz = cz * _n;
            int vc = SurfaceNets.BuildRows(grid, nx, ny, nz, ox, oy, oz, _n, voxel,
                0, SurfaceNets.RowCount(_n), _cellVert, _vPos, _vNrm, _vMask, _vCell, 0);
            if (paint != null)
            {
                SurfaceNets.ResetSlots(_slots);
                SurfaceNets.BuildPaint(paint, nx, ny, ox, oy, oz, _n, voxel, _vCell, _vPos, 0, vc, _vCol, _vUv, _slots);
                SurfaceNets.FinishPaint(_slots, _vUv, 0, vc);
            }
            else
            {
                System.Array.Clear(_vCol, 0, vc);
                System.Array.Clear(_vUv, 0, vc);
            }
            VertexCount = vc;
            IndexCount = SurfaceNets.BuildQuads(_n, _cellVert, _vMask, _vCell, _vPos, 0, vc, _tris, 0);
        }

        public void Build(DigZoneData data, byte[] grid, byte[] paint, int cx, int cy, int cz)
        {
            Build(grid, paint, data.nx, data.ny, data.nz, cx, cy, cz, data.voxelSize);
        }

        /// <summary>
        /// Cuts the last build to the box minX..maxX, minZ..maxZ (chunk-local XZ), splitting the triangles that cross
        /// it. Used for the terrain hole: outside it the surface lies on the terrain, hidden by the shader but still
        /// seen by the lightmapper, where it shadows the terrain and takes lightmap space.
        /// </summary>
        public void ClipXZ(float minX, float minZ, float maxX, float maxZ)
        {
            if (IndexCount == 0) return;
            _clipMin = new Vector2(minX, minZ);
            _clipMax = new Vector2(maxX, maxZ);

            // Nothing outside: keep the build as it is.
            bool inside = true;
            for (int v = 0; v < VertexCount && inside; v++)
                inside = Outside(_vPos[v], 0) <= 0f && Outside(_vPos[v], 1) <= 0f && Outside(_vPos[v], 2) <= 0f && Outside(_vPos[v], 3) <= 0f;
            if (inside) return;

            _pos.Clear(); _nrm.Clear(); _col.Clear(); _uv.Clear(); _idx.Clear(); _cut.Clear();
            for (int v = 0; v < VertexCount; v++)
            {
                _pos.Add(_vPos[v]); _nrm.Add(_vNrm[v]); _col.Add(_vCol[v]); _uv.Add(_vUv[v]);
            }

            for (int t = 0; t < IndexCount; t += 3)
            {
                _poly.Clear();
                _poly.Add(_tris[t]); _poly.Add(_tris[t + 1]); _poly.Add(_tris[t + 2]);
                for (int plane = 0; plane < 4 && _poly.Count >= 3; plane++) ClipPolygon(plane);
                for (int k = 2; k < _poly.Count; k++)
                {
                    // A triangle that only touches the clip box collapses onto its side: no area, and a chunk of
                    // nothing else is an invalid collision mesh.
                    Vector3 a = _pos[_poly[0]];
                    if (Vector3.Cross(_pos[_poly[k - 1]] - a, _pos[_poly[k]] - a).sqrMagnitude < 1e-12f) continue;
                    _idx.Add(_poly[0]); _idx.Add(_poly[k - 1]); _idx.Add(_poly[k]);
                }
            }

            // Compact: drop the vertices no triangle uses any more.
            int count = _pos.Count;
            var remap = new int[count];
            for (int v = 0; v < count; v++) remap[v] = -1;
            int next = 0;
            for (int i = 0; i < _idx.Count; i++)
            {
                int v = _idx[i];
                if (remap[v] < 0) remap[v] = next++;
            }
            EnsureCapacity(next, _idx.Count);
            for (int v = 0; v < count; v++)
            {
                int r = remap[v];
                if (r < 0) continue;
                _vPos[r] = _pos[v]; _vNrm[r] = _nrm[v]; _vCol[r] = _col[v]; _vUv[r] = _uv[v];
            }
            for (int i = 0; i < _idx.Count; i++) _tris[i] = remap[_idx[i]];
            VertexCount = next;
            IndexCount = _idx.Count;
        }

        // Distance of p beyond clip plane 0..3 (min X, max X, min Z, max Z); > 0 is outside.
        private float Outside(Vector3 p, int plane)
        {
            switch (plane)
            {
                case 0: return _clipMin.x - p.x;
                case 1: return p.x - _clipMax.x;
                case 2: return _clipMin.y - p.z;
                default: return p.z - _clipMax.y;
            }
        }

        // Sutherland-Hodgman against one plane. Crossing points are shared between the triangles on both sides of an
        // edge, so the mesh stays connected.
        private void ClipPolygon(int plane)
        {
            _next.Clear();
            int n = _poly.Count;
            for (int i = 0; i < n; i++)
            {
                int a = _poly[i], b = _poly[(i + 1) % n];
                float da = Outside(_pos[a], plane), db = Outside(_pos[b], plane);
                if (da <= 0f) _next.Add(a);
                if ((da <= 0f) != (db <= 0f)) _next.Add(CutVertex(a, b, plane));
            }
            _poly.Clear();
            _poly.AddRange(_next);
        }

        private int CutVertex(int a, int b, int plane)
        {
            if (a > b) { int s = a; a = b; b = s; }
            long key = ((long)a << 34) | ((long)b << 2) | (long)plane;
            if (_cut.TryGetValue(key, out int v)) return v;
            float da = Outside(_pos[a], plane), db = Outside(_pos[b], plane);
            float t = da / (da - db);
            v = _pos.Count;
            _pos.Add(Vector3.Lerp(_pos[a], _pos[b], t));
            _nrm.Add(Vector3.Lerp(_nrm[a], _nrm[b], t).normalized);
            _col.Add(Color.Lerp(_col[a], _col[b], t));
            // uv0.y (the chunk's paint slots) is the same on every vertex, so interpolating keeps it.
            _uv.Add(Vector2.Lerp(_uv[a], _uv[b], t));
            _cut[key] = v;
            return v;
        }

        private void EnsureCapacity(int vertices, int indices)
        {
            if (vertices > _vPos.Length)
            {
                System.Array.Resize(ref _vPos, vertices);
                System.Array.Resize(ref _vNrm, vertices);
                System.Array.Resize(ref _vCol, vertices);
                System.Array.Resize(ref _vUv, vertices);
            }
            if (indices > _tris.Length) System.Array.Resize(ref _tris, indices);
        }

        /// <summary>Writes the last build into <paramref name="mesh"/>. Returns false if the chunk has no triangles.</summary>
        public bool WriteTo(Mesh mesh)
        {
            mesh.Clear();
            if (IndexCount == 0) return false;
            mesh.indexFormat = VertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vPos, 0, VertexCount);
            mesh.SetNormals(_vNrm, 0, VertexCount);
            // Always written: the shaders read paint weights from the vertex colour and uv0.x.
            mesh.SetColors(_vCol, 0, VertexCount);
            mesh.SetUVs(0, _vUv, 0, VertexCount);
            mesh.SetTriangles(_tris, 0, IndexCount, 0, true);
            return true;
        }
    }
}
