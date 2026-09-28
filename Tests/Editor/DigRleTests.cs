using NUnit.Framework;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Tests
{
    public class DigRleTests
    {
        /// <summary>A terrain-like grid: solid below a wavy surface, air above, a thin quantized band between.</summary>
        private static byte[] TerrainGrid(int n)
        {
            var g = new byte[n * n * n];
            for (int z = 0; z < n; z++)
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float h = n * 0.4f + Mathf.Sin(x * 0.3f) * 3f + Mathf.Cos(z * 0.2f) * 2f;
                g[x + n * (y + n * z)] = DigFormat.Quantize(y - h);
            }
            return g;
        }

        private static byte[] Decode(byte[] rle, int length, bool zeroed, int tokensPerCall)
        {
            var dst = new byte[length];
            if (!zeroed) for (int i = 0; i < dst.Length; i++) dst[i] = 77;
            var state = new int[DigRle.StateSize];
            Assert.AreEqual(length, DigRle.Begin(rle, state));
            int r = 0, calls = 0;
            while (r == 0 && calls++ < 10_000_000) r = DigRle.Decode(rle, state, dst, tokensPerCall, zeroed);
            Assert.AreEqual(1, r);
            return dst;
        }

        [Test]
        public void TerrainGridRoundTripsAndShrinks()
        {
            byte[] g = TerrainGrid(48);
            byte[] rle = DigRleEncoder.Encode(g);
            CollectionAssert.AreEqual(g, Decode(rle, g.Length, true, 64));
            CollectionAssert.AreEqual(g, Decode(rle, g.Length, false, 1));
            Assert.Less(rle.Length, g.Length / 5, "a terrain grid should compress well");
        }

        [Test]
        public void RandomAndEdgeCasesRoundTrip()
        {
            var rng = new System.Random(7);
            foreach (int len in new[] { 1, 2, 7, 8, 9, 300, 5000 })
            {
                var g = new byte[len];
                // Mix of noise and long runs of 0, 255 and other values.
                for (int i = 0; i < len; i++) g[i] = (byte)((i / 37) % 4 == 0 ? rng.Next(256) : ((i / 37) % 4) * 60);
                byte[] rle = DigRleEncoder.Encode(g);
                CollectionAssert.AreEqual(g, Decode(rle, len, false, 3), $"length {len}");
                CollectionAssert.AreEqual(g, Decode(rle, len, true, 1000), $"length {len}, zeroed");
            }

            var solid = new byte[100_000];
            byte[] tiny = DigRleEncoder.Encode(solid);
            Assert.Less(tiny.Length, 16);
            CollectionAssert.AreEqual(solid, Decode(tiny, solid.Length, false, 1));
        }

        [Test]
        public void CorruptStreamsAreRejected()
        {
            byte[] g = TerrainGrid(16);
            byte[] rle = DigRleEncoder.Encode(g);
            var state = new int[DigRle.StateSize];

            Assert.AreEqual(-1, DigRle.Begin(null, state));
            Assert.AreEqual(-1, DigRle.Begin(new byte[0], state));

            var truncated = new byte[rle.Length / 2];
            System.Array.Copy(rle, truncated, truncated.Length);
            Assert.IsFalse(DigRle.DecodeAll(truncated, new byte[g.Length], true));

            Assert.IsFalse(DigRle.DecodeAll(rle, new byte[g.Length - 1], true), "wrong size");
        }

        [Test]
        public void ZoneDataSavesGridsCompressed()
        {
            var data = ScriptableObject.CreateInstance<DigZoneData>();
            try
            {
                data.nx = data.ny = data.nz = 23;
                byte[] g = TerrainGrid(24);
                data.baseGrid = g;
                data.grid = (byte[])g.Clone();
                data.grid[500] = 3;
                data.EnsurePaint()[600] = 2;

                string json = JsonUtility.ToJson(data);
                Assert.IsFalse(json.Contains("\"legacyGrid\":[1"), "raw grids must not be saved");

                var copy = ScriptableObject.CreateInstance<DigZoneData>();
                JsonUtility.FromJsonOverwrite(json, copy);
                CollectionAssert.AreEqual(data.grid, copy.grid);
                CollectionAssert.AreEqual(data.baseGrid, copy.baseGrid);
                CollectionAssert.AreEqual(data.paint, copy.paint);
                Object.DestroyImmediate(copy);

                // An in-place change is saved once the version is bumped.
                data.grid[501] = 4;
                data.gridVersion++;
                var copy2 = ScriptableObject.CreateInstance<DigZoneData>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(data), copy2);
                Assert.AreEqual(4, copy2.grid[501]);
                Object.DestroyImmediate(copy2);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        /// <summary>Undo and redo deserialize the whole asset; grids they didn't change keep their decoded arrays.</summary>
        [Test]
        public void RestoringTheAssetKeepsGridsItDidNotChange()
        {
            var data = ScriptableObject.CreateInstance<DigZoneData>();
            try
            {
                data.nx = data.ny = data.nz = 23;
                byte[] g = TerrainGrid(24);
                data.baseGrid = g;
                data.grid = (byte[])g.Clone();
                data.EnsurePaint()[600] = 2;
                data.gridVersion++;
                string saved = JsonUtility.ToJson(data);
                byte[] grid = data.grid, paint = data.paint, baseGrid = data.baseGrid;

                // Undo of something else in the asset (its trees).
                data.terrainTrees = new DigTreeInstance[2];
                JsonUtility.FromJsonOverwrite(saved, data);
                Assert.AreEqual(0, data.terrainTrees?.Length ?? 0);
                Assert.AreSame(grid, data.grid);
                Assert.AreSame(paint, data.paint);
                Assert.AreSame(baseGrid, data.baseGrid);

                // Undo of a sculpt stroke: the grid changed in place since it was saved, so it is decoded again.
                byte old = data.grid[700];
                data.grid[700] = (byte)(old ^ 0x55);
                data.gridVersion++;
                JsonUtility.FromJsonOverwrite(saved, data);
                Assert.AreNotSame(grid, data.grid);
                Assert.AreEqual(old, data.grid[700]);
                CollectionAssert.AreEqual(g, data.baseGrid);
                Assert.AreEqual(2, data.paint[600]);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }
    }
}
