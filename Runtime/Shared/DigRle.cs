namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Run-length decoder for byte grids. A terrain grid is mostly solid (0) or air (255) with a thin surface band,
    /// so it shrinks to a few percent. Every token is decoded with <see cref="System.Array.Copy(System.Array, int, System.Array, int, int)"/>,
    /// so Udon pays per run, not per sample. The encoder is <c>DigRleEncoder</c> (Runtime/Core).
    ///
    /// Stream: varint sample count, then tokens. A token is varint <c>(length &lt;&lt; 1) | isRun</c>, followed by one
    /// value byte for a run or <c>length</c> literal bytes. Varints are 7 bits per byte, low bits first.
    ///
    /// Shared by the Udon and standalone runtimes: keep to the LCGUdonSharp subset.
    /// </summary>
    public static class DigRle
    {
        /// <summary>Length of the int[] decoder state: read position, samples written, sample count.</summary>
        public const int StateSize = 3;

        /// <summary>
        /// Starts decoding <paramref name="src"/>: reads the header into <paramref name="state"/> and returns the sample
        /// count, or -1 if <paramref name="src"/> is not a valid stream.
        /// </summary>
        public static int Begin(byte[] src, int[] state)
        {
            return BeginAt(src, 0, state);
        }

        /// <summary>
        /// Decodes up to <paramref name="maxTokens"/> tokens into <paramref name="dst"/>. Call <see cref="Begin"/> first.
        /// </summary>
        /// <param name="dstZeroed">True if <paramref name="dst"/> is all zero, so runs of 0 can be skipped.</param>
        /// <returns>1 when the grid is complete, 0 if there is more to decode, -1 if the stream is corrupt.</returns>
        public static int Decode(byte[] src, int[] state, byte[] dst, int maxTokens, bool dstZeroed)
        {
            int pos = state[0];
            int written = state[1];
            int total = state[2];
            if (total < 0 || dst == null || dst.Length < total) return -1;
            int n = src.Length;

            for (int t = 0; t < maxTokens && written < total; t++)
            {
                int h = 0;
                int shift = 0;
                bool more = true;
                while (more)
                {
                    if (pos >= n || shift > 28) return -1;
                    int b = src[pos];
                    pos++;
                    h |= (b & 0x7F) << shift;
                    shift += 7;
                    more = (b & 0x80) != 0;
                }

                if (h < 0) return -1;
                int len = h >> 1;
                if (len <= 0 || len > total - written) return -1;
                if ((h & 1) != 0)
                {
                    if (pos >= n) return -1;
                    byte v = src[pos];
                    pos++;
                    if (v != 0 || !dstZeroed) Fill(dst, written, len, v);
                }
                else
                {
                    if (len > n - pos) return -1;
                    System.Array.Copy(src, pos, dst, written, len);
                    pos += len;
                }
                written += len;
            }

            state[0] = pos;
            state[1] = written;
            return written >= total ? 1 : 0;
        }

        /// <summary>Decodes a whole stream into <paramref name="dst"/>. Returns false if it is corrupt or the wrong size.</summary>
        public static bool DecodeAll(byte[] src, byte[] dst, bool dstZeroed)
        {
            int[] state = new int[StateSize];
            int total = Begin(src, state);
            if (total < 0 || dst == null || total != dst.Length) return false;
            int r = 0;
            while (r == 0) r = Decode(src, state, dst, 1 << 20, dstZeroed);
            return r == 1;
        }

        /// <summary>
        /// Decodes one chunk of a chunked stream (see <c>DigChunkPacker</c>): <paramref name="offsets"/>[ci] is where the
        /// chunk's stream starts in <paramref name="blob"/>, or <c>-1 - value</c> for a chunk whose samples all equal
        /// value. Returns false if the stream is corrupt or does not fill <paramref name="dst"/> exactly.
        /// </summary>
        public static bool DecodeChunk(byte[] blob, int[] offsets, int ci, byte[] dst, int[] state, bool dstZeroed)
        {
            if (offsets == null || ci < 0 || ci >= offsets.Length || dst == null) return false;
            int off = offsets[ci];
            if (off < 0)
            {
                int v = -1 - off;
                if (v > 255) return false;
                if (dst.Length > 0 && (v != 0 || !dstZeroed)) Fill(dst, 0, dst.Length, (byte)v);
                return true;
            }
            if (blob == null || off >= blob.Length) return false;
            if (BeginAt(blob, off, state) != dst.Length) return false;
            int r = 0;
            while (r == 0) r = Decode(blob, state, dst, 1 << 20, dstZeroed);
            return r == 1;
        }

        /// <summary>Like <see cref="Begin"/>, for a stream that starts at <paramref name="offset"/>.</summary>
        public static int BeginAt(byte[] src, int offset, int[] state)
        {
            state[0] = 0;
            state[1] = 0;
            state[2] = -1;
            if (src == null || offset < 0 || offset >= src.Length) return -1;
            int value = 0;
            int shift = 0;
            int pos = offset;
            for (int k = 0; k < 5; k++)
            {
                if (pos >= src.Length) return -1;
                int b = src[pos];
                pos++;
                value |= (b & 0x7F) << shift;
                shift += 7;
                if ((b & 0x80) == 0)
                {
                    if (value < 0) return -1;
                    state[0] = pos;
                    state[2] = value;
                    return value;
                }
            }
            return -1;
        }

        /// <summary>Fills <paramref name="len"/> bytes with <paramref name="v"/> in log2(len) copies.</summary>
        private static void Fill(byte[] dst, int at, int len, byte v)
        {
            dst[at] = v;
            int done = 1;
            while (done < len)
            {
                int c = len - done;
                if (c > done) c = done;
                System.Array.Copy(dst, at, dst, at + done, c);
                done += c;
            }
        }
    }
}
