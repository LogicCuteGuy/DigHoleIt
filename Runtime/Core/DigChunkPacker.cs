using System;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Splits a grid into per-chunk streams for the VRChat runtime, so a player decodes only the chunks that get dug.
    /// Each chunk holds the samples <see cref="DigFormat.ChunkSamples"/> gives (its cells plus one sample on each side),
    /// run-length encoded (<see cref="DigRleEncoder"/>) and appended to one blob. A chunk whose samples are all equal
    /// stores no stream: its offset is <c>-1 - value</c>. Decode with <see cref="DigRle.DecodeChunk"/>.
    /// </summary>
    public static class DigChunkPacker
    {
        /// <summary>Scratch buffer size for <see cref="EncodeChunk"/>.</summary>
        public static int BufferSize(int chunkCells) => (chunkCells + 2) * (chunkCells + 2) * (chunkCells + 2);

        /// <summary>Packs <paramref name="grid"/>. Returns false (and nulls) if the grid is missing or the wrong size.</summary>
        public static bool Pack(byte[] grid, int nx, int ny, int nz, int chunkCells, out byte[] blob, out int[] offsets)
        {
            blob = null;
            offsets = null;
            if (grid == null || chunkCells <= 0 || grid.Length != (nx + 1) * (ny + 1) * (nz + 1)) return false;

            int ncx = (nx + chunkCells - 1) / chunkCells;
            int ncy = (ny + chunkCells - 1) / chunkCells;
            int ncz = (nz + chunkCells - 1) / chunkCells;
            var streams = new byte[ncx * ncy * ncz][];
            var uniform = new int[streams.Length];
            var buffer = new byte[BufferSize(chunkCells)];
            for (int cz = 0; cz < ncz; cz++)
            for (int cy = 0; cy < ncy; cy++)
            for (int cx = 0; cx < ncx; cx++)
            {
                int ci = cx + ncx * (cy + ncy * cz);
                streams[ci] = EncodeChunk(grid, nx, ny, nz, chunkCells, cx, cy, cz, buffer, out uniform[ci]);
            }
            Join(streams, uniform, out blob, out offsets);
            return true;
        }

        /// <summary>
        /// Encodes one chunk. Returns null, with the value in <paramref name="uniform"/>, when all its samples are equal.
        /// </summary>
        /// <param name="buffer">Scratch space of at least <see cref="BufferSize"/> bytes.</param>
        public static byte[] EncodeChunk(byte[] grid, int nx, int ny, int nz, int chunkCells, int cx, int cy, int cz,
            byte[] buffer, out int uniform)
        {
            int sx = nx + 1, sy = ny + 1;
            DigFormat.ChunkSamples(cx, chunkCells, nx, out int x0, out int x1);
            DigFormat.ChunkSamples(cy, chunkCells, ny, out int y0, out int y1);
            DigFormat.ChunkSamples(cz, chunkCells, nz, out int z0, out int z1);
            int w = x1 - x0 + 1;
            int len = w * (y1 - y0 + 1) * (z1 - z0 + 1);

            byte first = grid[x0 + sx * (y0 + sy * z0)];
            bool same = true;
            int at = 0;
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            {
                Buffer.BlockCopy(grid, x0 + sx * (y + sy * z), buffer, at, w);
                if (same)
                    for (int k = 0; k < w; k++)
                        if (buffer[at + k] != first) { same = false; break; }
                at += w;
            }

            uniform = first;
            if (same) return null;
            var chunk = new byte[len];
            Buffer.BlockCopy(buffer, 0, chunk, 0, len);
            return DigRleEncoder.Encode(chunk);
        }

        /// <summary>Appends the chunk streams into one blob; null streams become uniform offsets.</summary>
        public static void Join(byte[][] streams, int[] uniform, out byte[] blob, out int[] offsets)
        {
            offsets = new int[streams.Length];
            long total = 0;
            foreach (byte[] s in streams) if (s != null) total += s.Length;
            blob = new byte[total];
            int at = 0;
            for (int ci = 0; ci < streams.Length; ci++)
            {
                byte[] s = streams[ci];
                if (s == null)
                {
                    offsets[ci] = -1 - uniform[ci];
                    continue;
                }
                offsets[ci] = at;
                Buffer.BlockCopy(s, 0, blob, at, s.Length);
                at += s.Length;
            }
        }

        /// <summary>
        /// Copies the samples chunk (<paramref name="cx"/>, <paramref name="cy"/>, <paramref name="cz"/>) holds out of
        /// <paramref name="grid"/>, in the order <see cref="DigRle.DecodeChunk"/> writes them.
        /// </summary>
        public static byte[] Extract(byte[] grid, int nx, int ny, int nz, int chunkCells, int cx, int cy, int cz)
        {
            int sx = nx + 1, sy = ny + 1;
            DigFormat.ChunkSamples(cx, chunkCells, nx, out int x0, out int x1);
            DigFormat.ChunkSamples(cy, chunkCells, ny, out int y0, out int y1);
            DigFormat.ChunkSamples(cz, chunkCells, nz, out int z0, out int z1);
            int w = x1 - x0 + 1;
            var dst = new byte[w * (y1 - y0 + 1) * (z1 - z0 + 1)];
            int at = 0;
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            {
                Buffer.BlockCopy(grid, x0 + sx * (y + sy * z), dst, at, w);
                at += w;
            }
            return dst;
        }
    }
}
