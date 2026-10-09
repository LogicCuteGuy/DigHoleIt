using NUnit.Framework;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Tests
{
    /// <summary>Where terrain trees and details in a zone stand, and when they go (<see cref="DigFoliage"/>).</summary>
    public class DigFoliageTests
    {
        private const int Nx = 30, Ny = 26, Nz = 28, N = 8;
        private const float Surface = 14.3f;
        private static readonly int[] Edit = { 1, 1, 1, Nx - 1, Ny - 1, Nz - 1 };

        private static float Height(int x) => Surface + Mathf.Sin(x * 0.4f) * 1.5f;

        private static byte[] Ground()
        {
            var g = new byte[(Nx + 1) * (Ny + 1) * (Nz + 1)];
            for (int z = 0; z <= Nz; z++)
            for (int y = 0; y <= Ny; y++)
            for (int x = 0; x <= Nx; x++)
                g[x + (Nx + 1) * (y + (Ny + 1) * z)] = y == 0 ? DigFormat.SolidByte : y == Ny ? DigFormat.AirByte : DigFormat.Quantize(y - Height(x));
            return g;
        }

        private static bool Stamp(byte[] grid, float x, float y, float z, float r, int op) =>
            DigBrush.Stamp(grid, new byte[grid.Length], Nx, Ny, Edit, x, y, z, r, op, 0, new int[6]);

        private static float Top(byte[] g, int x, int z) => DigFoliage.TopSurface(g, Nx, Ny, x, z);

        private static bool Stands(byte[] g, int x, int z, float y) => DigFoliage.GridStands(g, Nx, Ny, x, z, y);

        [Test]
        public void TopSurfaceFindsTheGround()
        {
            byte[] g = Ground();
            for (int x = 0; x <= Nx; x++)
                Assert.AreEqual(Height(x), Top(g, x, 5), 0.05f, $"column {x}");

            // A pit: the top surface is its floor.
            Assert.IsTrue(Stamp(g, 15, Surface, 14, 4f, DigFormat.OpDig));
            Assert.Less(Top(g, 15, 14), Height(15) - 3f);

            var air = new byte[g.Length];
            for (int i = 0; i < air.Length; i++) air[i] = DigFormat.AirByte;
            Assert.AreEqual(-1f, Top(air, 3, 3));
        }

        [Test]
        public void UntouchedGroundKeepsEverything()
        {
            byte[] g = Ground();
            for (int z = 0; z <= Nz; z++)
            for (int x = 0; x <= Nx; x++)
                Assert.IsTrue(Stands(g, x, z, Top(g, x, z)), $"column {x}, {z}");
        }

        [Test]
        public void DiggingTheGroundAwayRemovesWhatStoodThere()
        {
            byte[] g = Ground();
            float anchor = Top(g, 15, 14);
            Assert.IsTrue(Stamp(g, 15, Surface, 14, 3f, DigFormat.OpDig));
            Assert.IsFalse(Stands(g, 15, 14, anchor), "the pit");
            Assert.IsTrue(Stands(g, 3, 3, Top(Ground(), 3, 3)), "far away");
        }

        [Test]
        public void BuryingRemovesWhatStoodThere()
        {
            byte[] g = Ground();
            float anchor = Top(g, 15, 14);
            Assert.IsTrue(Stamp(g, 15, Surface + 1.5f, 14, 2.5f, DigFormat.OpAdd));
            Assert.IsFalse(Stands(g, 15, 14, anchor));
        }

        [Test]
        public void WhatStandsInAPitStaysUntilThePitChanges()
        {
            byte[] g = Ground();
            Stamp(g, 15, Surface, 14, 4f, DigFormat.OpDig);
            float floor = Top(g, 15, 14);
            Assert.IsTrue(Stands(g, 15, 14, floor), "planted on the pit floor");
            Stamp(g, 15, floor, 14, 1.5f, DigFormat.OpDig);
            Assert.IsFalse(Stands(g, 15, 14, floor), "dug deeper");
        }

        [Test]
        public void TunnelsBelowKeepIt()
        {
            byte[] g = Ground();
            float anchor = Top(g, 15, 14);
            // The tunnel roof is 1.5 voxels thick: the surface stays where it was.
            Assert.IsTrue(Stamp(g, 15, anchor - 3.5f, 14, 2f, DigFormat.OpDig));
            Assert.IsTrue(Stands(g, 15, 14, anchor));
        }

        /// <summary>A point of the surface the editor tree brush would hit from <paramref name="from"/> along <paramref name="dir"/>.</summary>
        private static Vector3 Hit(byte[] g, Vector3 from, Vector3 dir)
        {
            Assert.IsTrue(DigGridUtil.Raycast(g, Nx, Ny, Nz, from, dir, 100f, out Vector3 hit), $"ray from {from}");
            return hit;
        }

        private static bool TreeStands(byte[] g, Vector3 a) => DigFoliage.PointStands(g, Nx, Ny, Nz, a.x, a.y, a.z);

        [Test]
        public void TreesStandOnAnySurfaceUntilItChanges()
        {
            byte[] g = Ground();
            Stamp(g, 15, Surface, 14, 5f, DigFormat.OpDig);
            Vector3 floor = Hit(g, new Vector3(15.3f, Ny - 1, 14.2f), Vector3.down);
            Vector3 wall = Hit(g, new Vector3(15.1f, Surface - 2.2f, 14.3f), Vector3.right);
            Vector3 slope = Hit(g, new Vector3(4.6f, Ny - 1, 22.7f), Vector3.down);
            Assert.IsTrue(TreeStands(g, floor), "pit floor");
            Assert.IsTrue(TreeStands(g, wall), "pit wall");
            Assert.IsTrue(TreeStands(g, slope), "untouched ground");

            // Widening the pit takes the wall away, and the tree on it; the floor stays where it was.
            Stamp(g, wall.x + 1.5f, wall.y, wall.z, 2.5f, DigFormat.OpDig);
            Assert.IsFalse(TreeStands(g, wall), "wall dug away");
            Assert.IsTrue(TreeStands(g, slope), "far away");

            Stamp(g, floor.x, floor.y + 1f, floor.z, 1.8f, DigFormat.OpAdd);
            Assert.IsFalse(TreeStands(g, floor), "buried");
        }

        [Test]
        public void WhatHangsFromACaveCeilingStaysUntilTheCeilingChanges()
        {
            byte[] g = Ground();
            // A cave under the surface, its roof about 3 voxels thick.
            Assert.IsTrue(Stamp(g, 15, Surface - 7f, 14, 3.5f, DigFormat.OpDig));
            Vector3 ceiling = Hit(g, new Vector3(15.2f, Surface - 7f, 14.1f), Vector3.up);
            Vector3 floor = Hit(g, new Vector3(15.2f, Surface - 7f, 14.1f), Vector3.down);
            Assert.Less(ceiling.y, Top(g, 15, 14) - 1f, "the ceiling is under the top surface");
            Assert.IsTrue(TreeStands(g, ceiling), "on the ceiling");
            Assert.IsTrue(TreeStands(g, floor), "on the cave floor");

            // Digging the cave higher takes the ceiling (and what hangs from it) away; the floor stays.
            Stamp(g, ceiling.x, ceiling.y + 0.5f, ceiling.z, 1.5f, DigFormat.OpDig);
            Assert.IsFalse(TreeStands(g, ceiling), "ceiling dug away");
            Assert.IsTrue(TreeStands(g, floor), "floor untouched");
        }

        [Test]
        public void SmallChangesWithinToleranceKeepIt()
        {
            Assert.IsTrue(DigFoliage.StandsBetween(128 - 40, 128 + 40, 0.5f));
            Assert.IsTrue(DigFoliage.StandsBetween(128 + DigFoliage.Tolerance, 200, 0f));
            Assert.IsFalse(DigFoliage.StandsBetween(128 + DigFoliage.Tolerance + 1, 200, 0f));
            Assert.IsFalse(DigFoliage.StandsBetween(0, 10, 0.5f), "buried");
            Assert.IsFalse(DigFoliage.StandsBetween(250, 255, 0.5f), "in the air");
        }

        /// <summary>
        /// The VRChat runtime checks each anchor in the one chunk that holds it (the column's own chunk, and the sample
        /// below the anchor among its own samples), from that chunk's copy of the samples. That must agree with the whole grid.
        /// </summary>
        [Test]
        public void PerChunkChecksAgreeWithTheWholeGrid()
        {
            byte[] baked = Ground();
            var anchors = new float[(Nx + 1) * (Nz + 1)];
            for (int z = 0; z <= Nz; z++)
            for (int x = 0; x <= Nx; x++)
                anchors[x + (Nx + 1) * z] = Top(baked, x, z);

            byte[] g = Ground();
            Stamp(g, 9, Surface, 9, 3f, DigFormat.OpDig);
            Stamp(g, 22, Surface + 1f, 20, 2.5f, DigFormat.OpAdd);
            Stamp(g, 16, Surface - 5.5f, 5, 2f, DigFormat.OpDig);

            int cX = (Nx + N - 1) / N, cY = (Ny + N - 1) / N, cZ = (Nz + N - 1) / N;
            var checks = new int[anchors.Length];
            var standing = new bool[anchors.Length];
            for (int cz = 0; cz < cZ; cz++)
            for (int cy = 0; cy < cY; cy++)
            for (int cx = 0; cx < cX; cx++)
            {
                byte[] cur = DigChunkPacker.Extract(g, Nx, Ny, Nz, N, cx, cy, cz);
                DigFormat.ChunkSamples(cx, N, Nx, out int rx0, out int rx1);
                DigFormat.ChunkSamples(cy, N, Ny, out int ry0, out int ry1);
                DigFormat.ChunkSamples(cz, N, Nz, out int rz0, out _);
                int w = rx1 - rx0 + 1, h = ry1 - ry0 + 1;

                // As DigZoneRuntime._OwnColumns picks them.
                int x0 = cx * N, x1 = cx == cX - 1 ? Nx : x0 + N - 1;
                int z0 = cz * N, z1 = cz == cZ - 1 ? Nz : z0 + N - 1;
                int y0 = cy * N, y1 = cy == cY - 1 ? Ny - 1 : y0 + N - 1;
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float a = anchors[x + (Nx + 1) * z];
                    int ay = Mathf.Min((int)a, Ny - 1);
                    if (a < 0f || ay < y0 || ay > y1) continue;
                    int i = (x - rx0) + w * ((ay - ry0) + h * (z - rz0));
                    checks[x + (Nx + 1) * z]++;
                    standing[x + (Nx + 1) * z] = DigFoliage.StandsBetween(cur[i], cur[i + w], a - ay);
                }
            }

            int removed = 0;
            for (int z = 0; z <= Nz; z++)
            for (int x = 0; x <= Nx; x++)
            {
                int col = x + (Nx + 1) * z;
                Assert.AreEqual(1, checks[col], $"column {x}, {z} is checked by exactly one chunk");
                bool whole = Stands(g, x, z, anchors[col]);
                Assert.AreEqual(whole, standing[col], $"column {x}, {z}");
                if (!whole) removed++;
            }
            Assert.Greater(removed, 20, "the edits removed something");
        }

        /// <summary>
        /// Trees are checked by the chunk holding the grid cell around their anchor (DigFoliageBaker.AnchorChunk), from
        /// that chunk's samples (DigZoneRuntime._ShowTrees). That must agree with the whole grid, on walls too.
        /// </summary>
        [Test]
        public void PerChunkTreeChecksAgreeWithTheWholeGrid()
        {
            byte[] baked = Ground();
            Stamp(baked, 8, Surface, 8, 4f, DigFormat.OpDig);
            var anchors = new System.Collections.Generic.List<Vector3>();
            for (int k = 0; k < 400; k++)
            {
                // Rays down and sideways, like the brush dropping trees on floors, slopes and walls.
                float x = 0.5f + (k * 7.31f) % (Nx - 1), z = 0.5f + (k * 3.17f) % (Nz - 1);
                Vector3 dir = k % 3 == 0 ? Vector3.down : k % 3 == 1 ? new Vector3(1f, -1f, 0.3f) : new Vector3(-0.5f, -1f, -1f);
                if (DigGridUtil.Raycast(baked, Nx, Ny, Nz, new Vector3(x, Surface + 2.5f, z), dir, 100f, out Vector3 a)) anchors.Add(a);
            }
            Assert.Greater(anchors.Count, 200);

            byte[] g = (byte[])baked.Clone();
            Stamp(g, 9, Surface - 3f, 9, 3f, DigFormat.OpDig);
            Stamp(g, 22, Surface + 1f, 20, 2.5f, DigFormat.OpAdd);
            Stamp(g, 16, Surface - 1f, 5, 2f, DigFormat.OpDig);

            int cX = (Nx + N - 1) / N, cY = (Ny + N - 1) / N, cZ = (Nz + N - 1) / N;
            int removed = 0;
            foreach (Vector3 a in anchors)
            {
                int x0 = DigFoliage.Cell(a.x, Nx), y0 = DigFoliage.Cell(a.y, Ny), z0 = DigFoliage.Cell(a.z, Nz);
                int cx = Mathf.Min(x0 / N, cX - 1), cy = Mathf.Min(y0 / N, cY - 1), cz = Mathf.Min(z0 / N, cZ - 1);
                byte[] cur = DigChunkPacker.Extract(g, Nx, Ny, Nz, N, cx, cy, cz);
                DigFormat.ChunkSamples(cx, N, Nx, out int rx0, out int rx1);
                DigFormat.ChunkSamples(cy, N, Ny, out int ry0, out int ry1);
                DigFormat.ChunkSamples(cz, N, Nz, out int rz0, out int rz1);
                int w = rx1 - rx0 + 1, h = ry1 - ry0 + 1;
                int x = Mathf.Clamp(Mathf.FloorToInt(a.x), rx0, rx1 - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(a.y), ry0, ry1 - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt(a.z), rz0, rz1 - 1);
                Assert.AreEqual(new Vector3Int(x0, y0, z0), new Vector3Int(x, y, z), $"the chunk holds the cell of {a}");
                int i = (x - rx0) + w * ((y - ry0) + h * (z - rz0));
                bool chunk = DigFoliage.StandsInCell(cur, i, w, w * h, DigFoliage.Frac(a.x, x), DigFoliage.Frac(a.y, y), DigFoliage.Frac(a.z, z));
                bool whole = TreeStands(g, a);
                Assert.AreEqual(whole, chunk, $"tree at {a}");
                Assert.IsTrue(TreeStands(baked, a), $"tree at {a} stood when placed");
                if (!whole) removed++;
            }
            Assert.Greater(removed, 5, "the edits removed some trees");
        }

        [Test]
        public void SpawnEditsRoundTripAndLookTheSameEverywhere()
        {
            long e = DigFormat.PackLayer(new Vector3(12.25f, 7.5f, 30f), 1.5f, DigFormat.OpTree, 2);
            DigFormat.Unpack(e, out Vector3 p, out float r, out int op);
            Assert.AreEqual(DigFormat.OpTree, op);
            Assert.AreEqual(2, DigFormat.UnpackLayer(e));
            Assert.AreEqual(12.25f, p.x, 1e-4f);
            Assert.AreEqual(1.5f, r, 1e-4f);

            long d = DigFormat.PackLayer(new Vector3(3f, 4f, 5f), 1f, DigFormat.OpDetail, 0);
            DigFormat.Unpack(d, out _, out _, out op);
            Assert.AreEqual(DigFormat.OpDetail, op);
            Assert.AreEqual(0, DigFormat.UnpackLayer(d));

            for (long k = 0; k < 2000; k++)
            {
                long s = DigFormat.PackLayer(new Vector3(k * 0.37f % 60f, k % 30, k * 0.11f % 60f), 1f, DigFormat.OpDetail, 1);
                float yaw = DigFoliage.SpawnYaw(s), scale = DigFoliage.SpawnScale(s);
                Assert.That(yaw, Is.InRange(0f, 360f));
                Assert.That(scale, Is.InRange(0.8f, 1.2f));
                Assert.AreEqual(yaw, DigFoliage.SpawnYaw(s));
            }
        }
    }
}
