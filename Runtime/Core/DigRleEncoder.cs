using System;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>Encoder for <see cref="DigRle"/> streams (plain C#; the decoder also runs in Udon).</summary>
    public static class DigRleEncoder
    {
        /// <summary>
        /// Shortest run stored as a run. Shorter repeats stay inside literals: that costs a few bytes but keeps the
        /// token count, which is what Udon pays for, low.
        /// </summary>
        public const int MinRun = 8;

        public static byte[] Encode(byte[] src)
        {
            if (src == null) return null;
            var w = new Writer(Math.Max(64, src.Length / 16));
            w.Varint((uint)src.Length);

            int n = src.Length;
            int i = 0, lit = 0;
            while (i < n)
            {
                byte v = src[i];
                int j = i + 1;
                while (j < n && src[j] == v) j++;
                if (j - i >= MinRun)
                {
                    if (i > lit) w.Literal(src, lit, i - lit);
                    w.Varint(((uint)(j - i) << 1) | 1u);
                    w.Byte(v);
                    lit = j;
                }
                i = j;
            }
            if (n > lit) w.Literal(src, lit, n - lit);
            return w.ToArray();
        }

        private sealed class Writer
        {
            private byte[] _buf;
            private int _len;

            public Writer(int capacity) => _buf = new byte[capacity];

            private void Reserve(int extra)
            {
                if (_len + extra <= _buf.Length) return;
                int size = Math.Max(_buf.Length * 2, _len + extra);
                Array.Resize(ref _buf, size);
            }

            public void Byte(byte b)
            {
                Reserve(1);
                _buf[_len++] = b;
            }

            public void Varint(uint v)
            {
                Reserve(5);
                while (v >= 0x80)
                {
                    _buf[_len++] = (byte)(v | 0x80);
                    v >>= 7;
                }
                _buf[_len++] = (byte)v;
            }

            public void Literal(byte[] src, int start, int count)
            {
                Varint((uint)count << 1);
                Reserve(count);
                Buffer.BlockCopy(src, start, _buf, _len, count);
                _len += count;
            }

            public byte[] ToArray()
            {
                var a = new byte[_len];
                Buffer.BlockCopy(_buf, 0, a, 0, _len);
                return a;
            }
        }
    }
}
