using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Tests
{
    public class DigCoreTests
    {
        private const int N = 16;

        private static byte[] MakeGrid(int n, System.Func<int, int, int, float> sdf)
        {
            int s = n + 1;
            var grid = new byte[s * s * s];
            for (int z = 0; z < s; z++)
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                grid[x + s * (y + s * z)] = DigFormat.Quantize(sdf(x, y, z));
            return grid;
        }

        private static byte[] Sphere(int n, Vector3 c, float r) =>
            MakeGrid(n, (x, y, z) => Vector3.Distance(new Vector3(x, y, z), c) - r);

        private struct Built
        {
            public List<Vector3> pos;
            public List<Vector3> nrm;
            public List<int> tris;
        }

        /// <summary>Meshes every chunk and concatenates the results in world (grid) space.</summary>
        private static Built BuildAll(byte[] grid, int n, int chunk)
        {
            var b = new Built { pos = new List<Vector3>(), nrm = new List<Vector3>(), tris = new List<int>() };
            int cells = SurfaceNets.CellBufferSize(chunk);
            var cellVert = new int[cells];
            var vMask = new int[cells];
            var vCell = new int[cells];
            var vPos = new Vector3[cells];
            var vNrm = new Vector3[cells];
            var tris = new int[SurfaceNets.TriBufferSize(chunk)];
            int chunks = (n + chunk - 1) / chunk;

            for (int cz = 0; cz < chunks; cz++)
            for (int cy = 0; cy < chunks; cy++)
            for (int cx = 0; cx < chunks; cx++)
            {
                int vc = SurfaceNets.BuildRows(grid, n, n, n, cx * chunk, cy * chunk, cz * chunk, chunk, 1f,
                    0, SurfaceNets.RowCount(chunk), cellVert, vPos, vNrm, vMask, vCell, 0);
                int tc = SurfaceNets.BuildQuads(chunk, cellVert, vMask, vCell, vPos, 0, vc, tris, 0);
                int baseIndex = b.pos.Count;
                var offset = new Vector3(cx, cy, cz) * chunk;
                for (int i = 0; i < vc; i++)
                {
                    b.pos.Add(vPos[i] + offset);
                    b.nrm.Add(vNrm[i]);
                }
                for (int i = 0; i < tc; i++) b.tris.Add(tris[i] + baseIndex);
            }
            return b;
        }

        [Test]
        public void PackUnpack_RoundTrips()
        {
            long e = DigFormat.Pack(new Vector3(12.25f, 3.5f, 100.0625f), 2.5f, DigFormat.OpAdd);
            DigFormat.Unpack(e, out Vector3 p, out float r, out int op);
            Assert.AreEqual(12.25f, p.x, 1e-4f);
            Assert.AreEqual(3.5f, p.y, 1e-4f);
            Assert.AreEqual(100.0625f, p.z, 1e-4f);
            Assert.AreEqual(2.5f, r, 1e-4f);
            Assert.AreEqual(DigFormat.OpAdd, op);
            Assert.GreaterOrEqual(e, 0L, "sign bit must stay clear");
        }

        [Test]
        public void Stamp_IsIdempotent_AndRespectsEditBox()
        {
            byte[] grid = MakeGrid(N, (x, y, z) => y - 8f); // flat ground at y = 8
            var edit = new[] { 2, 2, 2, 13, 15, 13 };
            var changed = new int[6];

            Assert.IsTrue(DigBrush.Stamp(grid, null, N, N, edit, 8f, 8f, 8f, 3f, DigFormat.OpDig, 0, changed));
            for (int k = 0; k < 3; k++) Assert.GreaterOrEqual(changed[k], edit[k]);
            for (int k = 3; k < 6; k++) Assert.LessOrEqual(changed[k], edit[k]);

            var copy = (byte[])grid.Clone();
            Assert.IsFalse(DigBrush.Stamp(grid, null, N, N, edit, 8f, 8f, 8f, 3f, DigFormat.OpDig, 0, changed), "second identical dig must not change anything");
            CollectionAssert.AreEqual(copy, grid);

            // A sample outside the edit box never changes, however big the brush.
            int s = N + 1;
            byte corner = grid[1 + s * (8 + s * 1)];
            DigBrush.Stamp(grid, null, N, N, edit, 8f, 8f, 8f, 20f, DigFormat.OpDig, 0, changed);
            Assert.AreEqual(corner, grid[1 + s * (8 + s * 1)]);
        }

        [Test]
        public void SurfaceNets_Sphere_IsClosed_AndFacesOutward()
        {
            var c = new Vector3(8f, 8f, 8f);
            // Negative inside: solid ball.
            Built b = BuildAll(Sphere(N, c, 5f), N, N);
            Assert.Greater(b.tris.Count, 0);

            var edges = new Dictionary<long, int>();
            for (int t = 0; t < b.tris.Count; t += 3)
            {
                int i0 = b.tris[t], i1 = b.tris[t + 1], i2 = b.tris[t + 2];
                Vector3 a = b.pos[i0], bb = b.pos[i1], cc = b.pos[i2];
                // Unity front faces are clockwise; Cross(b - a, c - a) then points out of the front face.
                Vector3 faceN = Vector3.Cross(bb - a, cc - a);
                Vector3 centre = (a + bb + cc) / 3f;
                Assert.Greater(Vector3.Dot(faceN, centre - c), 0f, "triangle faces into the solid");
                Assert.Greater(Vector3.Dot(b.nrm[i0], b.pos[i0] - c), 0f, "vertex normal points inward");

                AddEdge(edges, i0, i1);
                AddEdge(edges, i1, i2);
                AddEdge(edges, i2, i0);
            }
            foreach (KeyValuePair<long, int> kv in edges)
                Assert.AreEqual(2, kv.Value, "open or non-manifold edge");
        }

        [Test]
        public void SurfaceNets_Chunks_MatchSingleChunk()
        {
            byte[] grid = Sphere(N, new Vector3(7.3f, 8.6f, 8.1f), 5.5f);
            Built whole = BuildAll(grid, N, N);
            Built split = BuildAll(grid, N, 8);

            Assert.AreEqual(whole.tris.Count, split.tris.Count);
            float area = TotalArea(whole);
            Assert.AreEqual(area, TotalArea(split), area * 1e-4f);
        }

        [Test]
        public void SurfaceNets_DugPlane_FacesUpOnGround()
        {
            byte[] grid = MakeGrid(N, (x, y, z) => y - 8.3f);
            var changed = new int[6];
            DigBrush.Stamp(grid, null, N, N, new[] { 1, 1, 1, 15, 15, 15 }, 8f, 8.3f, 8f, 3f, DigFormat.OpDig, 0, changed);
            Built b = BuildAll(grid, N, 8);

            int up = 0;
            for (int i = 0; i < b.pos.Count; i++)
                if (Vector3.Distance(new Vector3(b.pos[i].x, 0, b.pos[i].z), new Vector3(8, 0, 8)) > 5f && b.nrm[i].y > 0.9f) up++;
            Assert.Greater(up, 0, "untouched ground should face up");
        }

        [Test]
        public void AffectedChunks_CoversNeighbourReads()
        {
            DigFormat.AffectedChunks(16, 16, 16, 4, out int a, out int b);
            Assert.AreEqual(0, a);
            Assert.AreEqual(1, b);
            DigFormat.AffectedChunks(14, 14, 16, 4, out a, out b);
            Assert.AreEqual(0, a);
            Assert.AreEqual(0, b);
            DigFormat.AffectedChunks(15, 15, 16, 4, out a, out b);
            Assert.AreEqual(0, a);
            Assert.AreEqual(1, b);
            DigFormat.AffectedChunks(0, 63, 16, 4, out a, out b);
            Assert.AreEqual(0, a);
            Assert.AreEqual(3, b);
        }

        [Test]
        public void PackLayer_RoundTrips_AndKeepsOtherFields()
        {
            long plain = DigFormat.Pack(new Vector3(3f, 4f, 5f), 1.5f, DigFormat.OpPaint);
            long e = DigFormat.PackLayer(new Vector3(3f, 4f, 5f), 1.5f, DigFormat.OpPaint, DigFormat.LayerDugSoil);
            Assert.AreEqual(DigFormat.LayerDugSoil, DigFormat.UnpackLayer(e));
            Assert.AreEqual(0, DigFormat.UnpackLayer(plain));
            DigFormat.Unpack(e, out Vector3 p, out float r, out int op);
            Assert.AreEqual(new Vector3(3f, 4f, 5f), p);
            Assert.AreEqual(1.5f, r, 1e-4f);
            Assert.AreEqual(DigFormat.OpPaint, op);

            // The largest paint value uses the top bit; everything else must survive it.
            long top = DigFormat.PackLayer(new Vector3(3f, 4f, 5f), 1.5f, DigFormat.OpAdd, DigFormat.LayerMax);
            Assert.AreEqual(DigFormat.LayerMax, DigFormat.UnpackLayer(top));
            DigFormat.Unpack(top, out p, out r, out op);
            Assert.AreEqual(new Vector3(3f, 4f, 5f), p);
            Assert.AreEqual(1.5f, r, 1e-4f);
            Assert.AreEqual(DigFormat.OpAdd, op);
        }

        [Test]
        public void PaintValues_MapToTerrainLayers()
        {
            for (int l = 0; l < DigFormat.MaxTerrainLayers; l++)
            {
                int v = DigFormat.PaintValue(l);
                Assert.AreNotEqual(DigFormat.LayerAuto, v);
                Assert.AreNotEqual(DigFormat.LayerDugSoil, v);
                Assert.LessOrEqual(v, DigFormat.LayerMax);
                Assert.AreEqual(l, DigFormat.TerrainLayerOf(v));
            }
            Assert.AreEqual(1, DigFormat.PaintValue(0), "layers 0-3 keep the values of earlier versions");
            Assert.AreEqual(4, DigFormat.PaintValue(3));
            Assert.AreEqual(-1, DigFormat.TerrainLayerOf(DigFormat.LayerAuto));
            Assert.AreEqual(-1, DigFormat.TerrainLayerOf(DigFormat.LayerDugSoil));
            Assert.AreEqual(-1, DigFormat.TerrainLayerOf(DigFormat.LayerMax + 1));
        }

        [Test]
        public void Paint_SetsLayerInsideSphere_OnlyInEditBox_AndIsIdempotent()
        {
            byte[] grid = MakeGrid(N, (x, y, z) => y - 8f);
            var gridCopy = (byte[])grid.Clone();
            var paint = new byte[grid.Length];
            var edit = new[] { 2, 2, 2, 13, 15, 13 };
            var changed = new int[6];
            int s = N + 1;

            Assert.IsTrue(DigBrush.Stamp(grid, paint, N, N, edit, 8f, 8f, 8f, 3f, DigFormat.OpPaint, 3, changed));
            CollectionAssert.AreEqual(gridCopy, grid, "paint must not change the shape");
            Assert.AreEqual(3, paint[8 + s * (8 + s * 8)]);
            Assert.AreEqual(0, paint[8 + s * (8 + s * 12)], "outside the sphere");

            var paintCopy = (byte[])paint.Clone();
            Assert.IsFalse(DigBrush.Stamp(grid, paint, N, N, edit, 8f, 8f, 8f, 3f, DigFormat.OpPaint, 3, changed));
            CollectionAssert.AreEqual(paintCopy, paint);

            DigBrush.Stamp(grid, paint, N, N, edit, 8f, 8f, 8f, 20f, DigFormat.OpPaint, 2, changed);
            Assert.AreEqual(0, paint[1 + s * (8 + s * 1)], "outside the edit box");
            Assert.IsFalse(DigBrush.Stamp(grid, null, N, N, edit, 8f, 8f, 8f, 3f, DigFormat.OpPaint, 2, changed), "no paint grid: no-op");
        }

        [Test]
        public void AddWithLayer_PaintsAddedSoil()
        {
            byte[] grid = MakeGrid(N, (x, y, z) => y - 6f);
            var paint = new byte[grid.Length];
            var changed = new int[6];
            int s = N + 1;
            DigBrush.Stamp(grid, paint, N, N, new[] { 1, 1, 1, 15, 15, 15 }, 8f, 7f, 8f, 3f, DigFormat.OpAdd, 2, changed);
            int i = 8 + s * (9 + s * 8);
            Assert.Less(grid[i], 128, "added soil is solid");
            Assert.AreEqual(2, paint[i]);
        }

        [Test]
        public void BuildPaint_GivesAChunkUpToFourLayerSlots()
        {
            byte[] grid = MakeGrid(N, (x, y, z) => y - 8.4f);
            var paint = new byte[grid.Length];
            int s = N + 1;
            // Stripes along x: terrain layers 9, 2, 12, 5 and 7; the fifth has no slot left.
            int[] stripes = { 9, 2, 12, 5, 7 };
            for (int z = 0; z <= N; z++)
            for (int y = 0; y <= N; y++)
            for (int x = 0; x <= N; x++)
                paint[x + s * (y + s * z)] = (byte)DigFormat.PaintValue(stripes[Mathf.Min(x / 3, 4)]);

            int cells = SurfaceNets.CellBufferSize(N);
            var cellVert = new int[cells];
            var vMask = new int[cells];
            var vCell = new int[cells];
            var vPos = new Vector3[cells];
            var vNrm = new Vector3[cells];
            var vCol = new Color[cells];
            var vUv = new Vector2[cells];
            var slots = new int[SurfaceNets.SlotCount];
            int vc = SurfaceNets.BuildRows(grid, N, N, N, 0, 0, 0, N, 0.5f, 0, SurfaceNets.RowCount(N), cellVert, vPos, vNrm, vMask, vCell, 0);
            SurfaceNets.ResetSlots(slots);
            SurfaceNets.BuildPaint(paint, N, N, 0, 0, 0, N, 0.5f, vCell, vPos, 0, vc, vCol, vUv, slots);
            SurfaceNets.FinishPaint(slots, vUv, 0, vc);

            CollectionAssert.AreEquivalent(new[] { 9, 2, 12, 5 }, slots);
            float packed = vUv[0].y;
            int p = Mathf.RoundToInt(packed) - 1;
            for (int k = 0; k < SurfaceNets.SlotCount; k++, p /= 16) Assert.AreEqual(slots[k], p % 16, $"slot {k} layer in uv0.y");
            for (int v = 0; v < vc; v++)
            {
                Assert.AreEqual(packed, vUv[v].y, "every vertex carries the same slot layers");
                float gx = vPos[v].x / 0.5f;
                float sum = vCol[v].r + vCol[v].g + vCol[v].b + vCol[v].a;
                if (gx < 11f) Assert.AreEqual(1f, sum, 1e-4f, "painted with slotted layers");
                if (gx > 13f) Assert.AreEqual(0f, sum, 1e-4f, "the fifth layer falls back to auto");
            }
        }

        [Test]
        public void BuildPaint_WeightsBlendAndSumToOne()
        {
            byte[] grid = MakeGrid(N, (x, y, z) => y - 8.4f);
            var paint = new byte[grid.Length];
            int s = N + 1;
            // Left half layer 1 (terrain layer 0), right half dug soil (5).
            for (int z = 0; z <= N; z++)
            for (int y = 0; y <= N; y++)
            for (int x = 0; x <= N; x++)
                paint[x + s * (y + s * z)] = (byte)(x < 8 ? 1 : 5);

            int cells = SurfaceNets.CellBufferSize(N);
            var cellVert = new int[cells];
            var vMask = new int[cells];
            var vCell = new int[cells];
            var vPos = new Vector3[cells];
            var vNrm = new Vector3[cells];
            var vCol = new Color[cells];
            var vUv = new Vector2[cells];
            var slots = new int[SurfaceNets.SlotCount];
            int vc = SurfaceNets.BuildRows(grid, N, N, N, 0, 0, 0, N, 0.5f, 0, SurfaceNets.RowCount(N), cellVert, vPos, vNrm, vMask, vCell, 0);
            SurfaceNets.ResetSlots(slots);
            SurfaceNets.BuildPaint(paint, N, N, 0, 0, 0, N, 0.5f, vCell, vPos, 0, vc, vCol, vUv, slots);
            Assert.AreEqual(0, slots[0], "terrain layer 0 takes the first slot");

            Assert.Greater(vc, 0);
            bool sawBlend = false;
            for (int v = 0; v < vc; v++)
            {
                float sum = vCol[v].r + vCol[v].g + vCol[v].b + vCol[v].a + vUv[v].x;
                Assert.AreEqual(1f, sum, 1e-4f, "fully painted: no auto weight left");
                float gx = vPos[v].x / 0.5f;
                if (gx < 6.5f) Assert.AreEqual(1f, vCol[v].r, 1e-4f);
                if (gx > 8.5f) Assert.AreEqual(1f, vUv[v].x, 1e-4f);
                if (vCol[v].r > 0.05f && vUv[v].x > 0.05f) sawBlend = true;
            }
            Assert.IsTrue(sawBlend, "the boundary between layers should blend");

            System.Array.Clear(paint, 0, paint.Length);
            SurfaceNets.ResetSlots(slots);
            SurfaceNets.BuildPaint(paint, N, N, 0, 0, 0, N, 0.5f, vCell, vPos, 0, vc, vCol, vUv, slots);
            for (int v = 0; v < vc; v++) Assert.AreEqual(new Color(0, 0, 0, 0), vCol[v]);
        }

        private static void AddEdge(Dictionary<long, int> edges, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            edges.TryGetValue(key, out int n);
            edges[key] = n + 1;
        }

        private static float TotalArea(Built b)
        {
            float area = 0f;
            for (int t = 0; t < b.tris.Count; t += 3)
                area += Vector3.Cross(b.pos[b.tris[t + 1]] - b.pos[b.tris[t]], b.pos[b.tris[t + 2]] - b.pos[b.tris[t]]).magnitude * 0.5f;
            return area;
        }
    }
}
