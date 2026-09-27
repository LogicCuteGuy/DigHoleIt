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
        private readonly int[] _tris;
        private readonly Vector3[] _vPos;
        private readonly Vector3[] _vNrm;
        private readonly Color[] _vCol;
        private readonly Vector2[] _vUv;

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
            if (paint != null) SurfaceNets.BuildPaint(paint, nx, ny, ox, oy, oz, _n, voxel, _vCell, _vPos, 0, vc, _vCol, _vUv);
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
