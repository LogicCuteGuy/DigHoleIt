using UnityEngine;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// CSG sphere brushes on the byte grid. Dig, Add and Paint are idempotent, which network replay relies on.
    /// Shared by the Udon and standalone runtimes: keep to the LCGUdonSharp subset
    /// (static methods, primitives, arrays, Unity value types; no custom structs or classes).
    /// </summary>
    public static class DigBrush
    {
        /// <summary>
        /// Stamps a sphere at <paramref name="cx"/>,<paramref name="cy"/>,<paramref name="cz"/> (grid units) with radius
        /// <paramref name="r"/> voxels, limited to the inclusive editable box <paramref name="edit"/>
        /// ({minX, minY, minZ, maxX, maxY, maxZ}).
        /// Dig and Add change <paramref name="grid"/>; Paint sets <paramref name="paint"/> to <paramref name="layer"/>
        /// inside the sphere; Add with a layer above 0 also paints the soil it adds.
        /// </summary>
        /// <param name="paint">Paint grid (same layout as the SDF grid). May be null when no layer is used.</param>
        /// <param name="changed">Receives the inclusive changed sample box; untouched when nothing changed.</param>
        /// <returns>True if any sample changed.</returns>
        public static bool Stamp(byte[] grid, byte[] paint, int nx, int ny, int[] edit, float cx, float cy, float cz, float r,
            int op, int layer, int[] changed)
        {
            int sx = nx + 1;
            int sxy = sx * (ny + 1);
            float reach = r + DigFormat.SdfBand;

            int x0 = Mathf.Max(edit[0], Mathf.FloorToInt(cx - reach));
            int y0 = Mathf.Max(edit[1], Mathf.FloorToInt(cy - reach));
            int z0 = Mathf.Max(edit[2], Mathf.FloorToInt(cz - reach));
            int x1 = Mathf.Min(edit[3], Mathf.CeilToInt(cx + reach));
            int y1 = Mathf.Min(edit[4], Mathf.CeilToInt(cy + reach));
            int z1 = Mathf.Min(edit[5], Mathf.CeilToInt(cz + reach));

            bool dig = op == DigFormat.OpDig;
            bool doPaint = op == DigFormat.OpPaint;
            bool shape = dig || op == DigFormat.OpAdd;
            bool addPaint = op == DigFormat.OpAdd && layer > 0;
            if (paint == null && (doPaint || addPaint)) return false;
            if (!shape && !doPaint) return false;
            byte paintValue = (byte)layer;

            bool any = false;
            int cMinX = int.MaxValue, cMinY = int.MaxValue, cMinZ = int.MaxValue;
            int cMaxX = int.MinValue, cMaxY = int.MinValue, cMaxZ = int.MinValue;

            for (int z = z0; z <= z1; z++)
            {
                float dz = z - cz;
                for (int y = y0; y <= y1; y++)
                {
                    float dy = y - cy;
                    float dyz = dy * dy + dz * dz;
                    int row = sx * y + sxy * z;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x - cx;
                        float d = Mathf.Sqrt(dx * dx + dyz) - r;
                        if (d >= DigFormat.SdfBand) continue;

                        int i = row + x;
                        bool hit = false;

                        if (shape)
                        {
                            int q = Mathf.RoundToInt(d * DigFormat.SdfScale);
                            if (q < -128) q = -128;
                            if (q > 127) q = 127;

                            int s = grid[i] - 128;
                            int ns;
                            if (dig)
                            {
                                ns = -q > s ? -q : s;
                                if (ns > 127) ns = 127;
                            }
                            else
                            {
                                ns = q < s ? q : s;
                            }

                            if (ns != s)
                            {
                                grid[i] = (byte)(ns + 128);
                                hit = true;
                            }
                        }

                        if ((doPaint || addPaint) && d < 0f && paint[i] != paintValue)
                        {
                            paint[i] = paintValue;
                            hit = true;
                        }

                        if (hit)
                        {
                            any = true;
                            if (x < cMinX) cMinX = x;
                            if (x > cMaxX) cMaxX = x;
                            if (y < cMinY) cMinY = y;
                            if (y > cMaxY) cMaxY = y;
                            if (z < cMinZ) cMinZ = z;
                            if (z > cMaxZ) cMaxZ = z;
                        }
                    }
                }
            }

            if (any)
            {
                changed[0] = cMinX; changed[1] = cMinY; changed[2] = cMinZ;
                changed[3] = cMaxX; changed[4] = cMaxY; changed[5] = cMaxZ;
            }
            return any;
        }
    }
}
