using NUnit.Framework;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Tests
{
    /// <summary>
    /// Per-chunk storage, as the VRChat runtime uses it: every chunk holds its own copy of the samples it reads,
    /// edits are stamped into each copy, and chunks are meshed from their copy alone.
    /// </summary>
    public class DigChunkTests
    {
        private const int Nx = 45, Ny = 30, Nz = 38, N = 13;

        private static byte[] TerrainGrid()
        {
            var g = new byte[(Nx + 1) * (Ny + 1) * (Nz + 1)];
            for (int z = 0; z <= Nz; z++)
            for (int y = 0; y <= Ny; y++)
            for (int x = 0; x <= Nx; x++)
            {
                float h = Ny * 0.5f + Mathf.Sin(x * 0.3f) * 4f + Mathf.Cos(z * 0.2f) * 3f;
                g[x + (Nx + 1) * (y + (Ny + 1) * z)] = DigFormat.Quantize(y - h);
            }
            return g;
        }

        private static int ChunksX => (Nx + N - 1) / N;
        private static int ChunksY => (Ny + N - 1) / N;
        private static int ChunksZ => (Nz + N - 1) / N;

        private static int[] Range(int ci)
        {
            DigFormat.ChunkSamples(ci % ChunksX, N, Nx, out int x0, out int x1);
            DigFormat.ChunkSamples(ci / ChunksX % ChunksY, N, Ny, out int y0, out int y1);
            DigFormat.ChunkSamples(ci / (ChunksX * ChunksY), N, Nz, out int z0, out int z1);
            return new[] { x0, y0, z0, x1, y1, z1 };
        }

        private static byte[][] DecodeAll(byte[] blob, int[] offsets)
        {
            var chunks = new byte[offsets.Length][];
            var state = new int[DigRle.StateSize];
            for (int ci = 0; ci < offsets.Length; ci++)
            {
                int[] r = Range(ci);
                chunks[ci] = new byte[(r[3] - r[0] + 1) * (r[4] - r[1] + 1) * (r[5] - r[2] + 1)];
                Assert.IsTrue(DigRle.DecodeChunk(blob, offsets, ci, chunks[ci], state, true), $"chunk {ci}");
            }
            return chunks;
        }

        private static void AssertChunksMatch(byte[] grid, byte[][] chunks, string what)
        {
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                int cx = ci % ChunksX, cy = ci / ChunksX % ChunksY, cz = ci / (ChunksX * ChunksY);
                CollectionAssert.AreEqual(DigChunkPacker.Extract(grid, Nx, Ny, Nz, N, cx, cy, cz), chunks[ci], $"{what}, chunk {ci}");
            }
        }

        [Test]
        public void ChunkPackRoundTrips()
        {
            byte[] g = TerrainGrid();
            Assert.IsTrue(DigChunkPacker.Pack(g, Nx, Ny, Nz, N, out byte[] blob, out int[] offsets));
            Assert.AreEqual(ChunksX * ChunksY * ChunksZ, offsets.Length);
            AssertChunksMatch(g, DecodeAll(blob, offsets), "decoded");

            int uniform = 0;
            foreach (int o in offsets) if (o < 0) uniform++;
            Assert.Greater(uniform, 0, "all-solid and all-air chunks store no stream");

            var state = new int[DigRle.StateSize];
            var wrongSize = new byte[3];
            int streamed = System.Array.FindIndex(offsets, o => o >= 0);
            Assert.IsFalse(DigRle.DecodeChunk(blob, offsets, streamed, wrongSize, state, true));
            Assert.IsFalse(DigRle.DecodeChunk(blob, offsets, offsets.Length, wrongSize, state, true));
        }

        [Test]
        public void ChunkedEditsMatchWholeGridEdits()
        {
            byte[] g = TerrainGrid();
            var paint = new byte[g.Length];
            DigChunkPacker.Pack(g, Nx, Ny, Nz, N, out byte[] blob, out int[] offsets);
            byte[][] chunks = DecodeAll(blob, offsets);
            var chunkPaint = new byte[chunks.Length][];
            for (int ci = 0; ci < chunks.Length; ci++) chunkPaint[ci] = new byte[chunks[ci].Length];

            int[] edit = { 2, 2, 2, Nx - 2, Ny - 2, Nz - 2 };
            var changed = new int[6];
            var box = new int[6];
            var rng = new System.Random(3);
            for (int k = 0; k < 60; k++)
            {
                var p = new Vector3(rng.Next(0, Nx * 16) / 16f, rng.Next(Ny * 4, Ny * 12) / 16f, rng.Next(0, Nz * 16) / 16f);
                float r = rng.Next(8, 40) / 8f;
                int op = k % 3 == 0 ? DigFormat.OpAdd : k % 5 == 0 ? DigFormat.OpPaint : DigFormat.OpDig;
                int layer = op == DigFormat.OpDig ? 0 : 1 + k % 5;

                DigBrush.Stamp(g, paint, Nx, Ny, edit, p.x, p.y, p.z, r, op, layer, changed);
                for (int ci = 0; ci < chunks.Length; ci++)
                {
                    int[] rg = Range(ci);
                    box[0] = Mathf.Max(edit[0], rg[0]) - rg[0];
                    box[1] = Mathf.Max(edit[1], rg[1]) - rg[1];
                    box[2] = Mathf.Max(edit[2], rg[2]) - rg[2];
                    box[3] = Mathf.Min(edit[3], rg[3]) - rg[0];
                    box[4] = Mathf.Min(edit[4], rg[4]) - rg[1];
                    box[5] = Mathf.Min(edit[5], rg[5]) - rg[2];
                    if (box[0] > box[3] || box[1] > box[4] || box[2] > box[5]) continue;
                    DigBrush.Stamp(chunks[ci], chunkPaint[ci], rg[3] - rg[0], rg[4] - rg[1], box,
                        p.x - rg[0], p.y - rg[1], p.z - rg[2], r, op, layer, changed);
                }
            }
            AssertChunksMatch(g, chunks, "grid");
            AssertChunksMatch(paint, chunkPaint, "paint");

            // Each chunk meshed from its own samples matches the chunk meshed from the whole grid.
            int cells = SurfaceNets.CellBufferSize(N);
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                int cx = ci % ChunksX, cy = ci / ChunksX % ChunksY, cz = ci / (ChunksX * ChunksY);
                int[] rg = Range(ci);
                Vector3[] whole = Mesh(g, paint, Nx, Ny, Nz, cx * N, cy * N, cz * N, cells, out Color[] wholeCol, out int wholeTris);
                Vector3[] local = Mesh(chunks[ci], chunkPaint[ci], rg[3] - rg[0], rg[4] - rg[1], rg[5] - rg[2],
                    cx * N - rg[0], cy * N - rg[1], cz * N - rg[2], cells, out Color[] localCol, out int localTris);
                CollectionAssert.AreEqual(whole, local, $"vertices, chunk {ci}");
                CollectionAssert.AreEqual(wholeCol, localCol, $"paint, chunk {ci}");
                Assert.AreEqual(wholeTris, localTris, $"triangles, chunk {ci}");
            }
        }

        [Test]
        public void ZoneDataRepacksOnlyWhatChanged()
        {
            var data = ScriptableObject.CreateInstance<DigZoneData>();
            try
            {
                data.nx = Nx; data.ny = Ny; data.nz = Nz; data.chunkCells = N;
                data.grid = TerrainGrid();
                int[] edit = { 0, 0, 0, Nx, Ny, Nz };
                var changed = new int[6];

                Assert.IsTrue(data.GetChunkPacks(out byte[] g0, out int[] o0, out byte[] p0, out _));
                Assert.IsNull(p0, "no paint grid, no paint streams");
                data.GetChunkPacks(out byte[] again, out _, out _, out _);
                Assert.AreSame(g0, again, "cached while nothing changed");

                // Marked edits: only the touched chunks are encoded again, with the same result as a full pack.
                DigBrush.Stamp(data.grid, null, Nx, Ny, edit, 20f, 15f, 20f, 4f, DigFormat.OpDig, 0, changed);
                data.MarkChanged(changed);
                DigBrush.Stamp(data.grid, null, Nx, Ny, edit, 40f, 12f, 5f, 3f, DigFormat.OpDig, 0, changed);
                data.MarkChanged(changed);
                data.GetChunkPacks(out byte[] g1, out int[] o1, out _, out _);
                DigChunkPacker.Pack(data.grid, Nx, Ny, Nz, N, out byte[] full, out int[] fullOffsets);
                CollectionAssert.AreEqual(full, g1);
                CollectionAssert.AreEqual(fullOffsets, o1);

                // An unmarked change is still picked up, by encoding everything.
                DigBrush.Stamp(data.grid, null, Nx, Ny, edit, 5f, 15f, 30f, 4f, DigFormat.OpAdd, 0, changed);
                data.gridVersion++;
                data.GetChunkPacks(out byte[] g2, out _, out _, out _);
                DigChunkPacker.Pack(data.grid, Nx, Ny, Nz, N, out full, out _);
                CollectionAssert.AreEqual(full, g2);

                // Paint appears only once something is painted.
                data.EnsurePaint();
                data.GetChunkPacks(out _, out _, out byte[] noPaint, out _);
                Assert.IsNull(noPaint, "an all-zero paint grid stores no paint");
                DigBrush.Stamp(data.grid, data.paint, Nx, Ny, edit, 20f, 15f, 20f, 5f, DigFormat.OpPaint, 2, changed);
                data.MarkChanged(changed);
                data.GetChunkPacks(out _, out _, out byte[] paint, out int[] paintOffsets);
                DigChunkPacker.Pack(data.paint, Nx, Ny, Nz, N, out full, out fullOffsets);
                CollectionAssert.AreEqual(full, paint);
                CollectionAssert.AreEqual(fullOffsets, paintOffsets);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void ClipXZ_CutsTheMeshToTheBox()
        {
            byte[] g = TerrainGrid();
            var mesher = new ChunkMesher(N);
            var before = new UnityEngine.Mesh();
            var after = new UnityEngine.Mesh();
            try
            {
                // Chunk 0,1,0 holds the terrain surface; the box cuts through it on all four sides except min Z.
                mesher.Build(g, null, Nx, Ny, Nz, 0, 1, 0, 0.5f);
                Assert.Greater(mesher.IndexCount, 0);
                mesher.WriteTo(before);
                const float x0 = 1.3f, z0 = -1f, x1 = 4.7f, z1 = 3.1f;
                mesher.ClipXZ(x0, z0, x1, z1);
                mesher.WriteTo(after);

                Vector3[] v = after.vertices;
                int[] t = after.triangles;
                foreach (Vector3 p in v)
                    Assert.IsTrue(p.x >= x0 - 1e-4f && p.x <= x1 + 1e-4f && p.z >= z0 - 1e-4f && p.z <= z1 + 1e-4f, $"{p} outside the box");
                Assert.AreEqual(v.Length, new System.Collections.Generic.HashSet<Vector3>(v).Count, "cut points are shared, not duplicated");

                // The surface is a height field here, so it covers the same XZ points inside the box before and after.
                for (float x = x0 + 0.013f; x < x1; x += 0.11f)
                for (float z = 0.007f; z < z1; z += 0.11f)
                {
                    float hb = HeightAt(before, x, z), ha = HeightAt(after, x, z);
                    Assert.AreEqual(float.IsNaN(hb), float.IsNaN(ha), $"coverage at {x}, {z}");
                    if (!float.IsNaN(hb)) Assert.AreEqual(hb, ha, 1e-3f, $"height at {x}, {z}");
                }
                Assert.Greater(t.Length, 0);

                // A box around everything keeps the build; a box beside it leaves nothing.
                mesher.Build(g, null, Nx, Ny, Nz, 0, 1, 0, 0.5f);
                int count = mesher.IndexCount;
                mesher.ClipXZ(-1f, -1f, 100f, 100f);
                Assert.AreEqual(count, mesher.IndexCount);
                mesher.ClipXZ(50f, 50f, 60f, 60f);
                Assert.AreEqual(0, mesher.IndexCount);
            }
            finally
            {
                Object.DestroyImmediate(before);
                Object.DestroyImmediate(after);
            }
        }

        // Height of the mesh surface above x, z (NaN if no triangle covers it).
        private static float HeightAt(UnityEngine.Mesh mesh, float x, float z)
        {
            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float wa = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                float wb = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                float wc = 1f - wa - wb;
                if (wa >= -1e-5f && wb >= -1e-5f && wc >= -1e-5f) return a.y * wa + b.y * wb + c.y * wc;
            }
            return float.NaN;
        }

        private static Vector3[] Mesh(byte[] grid, byte[] paint, int nx, int ny, int nz, int ox, int oy, int oz, int cells,
            out Color[] colors, out int tris)
        {
            var cellVert = new int[cells];
            var vMask = new int[cells];
            var vCell = new int[cells];
            var vPos = new Vector3[cells];
            var vNrm = new Vector3[cells];
            var vCol = new Color[cells];
            var vUv = new Vector2[cells];
            var tri = new int[SurfaceNets.TriBufferSize(N)];
            int vc = SurfaceNets.BuildRows(grid, nx, ny, nz, ox, oy, oz, N, 0.5f, 0, SurfaceNets.RowCount(N),
                cellVert, vPos, vNrm, vMask, vCell, 0);
            var slots = new int[SurfaceNets.SlotCount];
            SurfaceNets.ResetSlots(slots);
            SurfaceNets.BuildPaint(paint, nx, ny, ox, oy, oz, N, 0.5f, vCell, vPos, 0, vc, vCol, vUv, slots);
            tris = SurfaceNets.BuildQuads(N, cellVert, vMask, vCell, vPos, 0, vc, tri, 0);
            var pos = new Vector3[vc];
            colors = new Color[vc];
            System.Array.Copy(vPos, pos, vc);
            System.Array.Copy(vCol, colors, vc);
            return pos;
        }
    }
}
