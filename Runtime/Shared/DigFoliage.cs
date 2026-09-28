namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Terrain trees and details in a Dig Zone stand on the voxel surface where they were placed: details on the top
    /// surface of their grid column, trees at their own point (a pit floor, a slope, a wall, a tunnel floor). Each one keeps
    /// an anchor, the point of that surface; it stands while the surface still passes through its anchor, and goes when the
    /// ground is dug away under it or it gets buried. Compiled into both runtimes (Runtime/Shared is a U# assembly).
    ///
    /// Foliage mask: one RGBA32 texel per grid column (x + (nx + 1) * z). r is <see cref="Standing"/> or
    /// <see cref="Removed"/> (the DigHoleIt/DigDetail shader reads it), g and b the column's anchor height in
    /// 1/<see cref="AnchorScale"/> voxel (g high byte), a 255 when the column has a surface.
    /// </summary>
    public static class DigFoliage
    {
        public const byte Standing = 255;
        public const byte Removed = 0;

        /// <summary>How far the surface may move off an anchor before what stands there goes, in 1/64 voxel.</summary>
        public const int Tolerance = 24;

        /// <summary>Anchor heights are stored in 1/16 voxel (16 bits: up to 4095 voxels).</summary>
        public const float AnchorScale = 16f;

        /// <summary>
        /// True if the surface still passes near a point <paramref name="t"/> (0..1) of the way from the grid sample
        /// <paramref name="below"/> to the one above it (raw grid bytes).
        /// </summary>
        public static bool StandsBetween(int below, int above, float t)
        {
            float s = below + (above - below) * t - 128f;
            return s <= Tolerance && s >= -Tolerance;
        }

        /// <summary><see cref="StandsBetween"/> at height <paramref name="y"/> (grid units) of column (x, z) of a whole grid.</summary>
        public static bool GridStands(byte[] grid, int nx, int ny, int x, int z, float y)
        {
            int y0 = (int)y;
            if (y0 < 0) y0 = 0;
            if (y0 > ny - 1) y0 = ny - 1;
            int sx = nx + 1;
            int i = x + sx * (y0 + (ny + 1) * z);
            return StandsBetween(grid[i], grid[i + sx], y - y0);
        }

        /// <summary>
        /// True if the surface still passes near the point (<paramref name="fx"/>, <paramref name="fy"/>,
        /// <paramref name="fz"/>) (0..1) of the grid cell whose lowest sample is at index <paramref name="i"/>, trilinear over
        /// its 8 samples (<paramref name="sx"/>, <paramref name="sxy"/>: row and layer strides). Trees use this, so they can
        /// stand on any surface: a floor, a slope, a pit wall.
        /// </summary>
        public static bool StandsInCell(byte[] g, int i, int sx, int sxy, float fx, float fy, float fz)
        {
            int j = i + sxy;
            int s000 = g[i], s100 = g[i + 1], s010 = g[i + sx], s110 = g[i + sx + 1];
            int s001 = g[j], s101 = g[j + 1], s011 = g[j + sx], s111 = g[j + sx + 1];
            float a = s000 + (s100 - s000) * fx;
            float b = s010 + (s110 - s010) * fx;
            float c = s001 + (s101 - s001) * fx;
            float d = s011 + (s111 - s011) * fx;
            float lo = a + (b - a) * fy;
            float hi = c + (d - c) * fy;
            float s = lo + (hi - lo) * fz - 128f;
            return s <= Tolerance && s >= -Tolerance;
        }

        /// <summary><see cref="StandsInCell"/> at grid position (x, y, z) of a whole grid.</summary>
        public static bool PointStands(byte[] grid, int nx, int ny, int nz, float x, float y, float z)
        {
            int x0 = Cell(x, nx), y0 = Cell(y, ny), z0 = Cell(z, nz);
            int sx = nx + 1, sxy = sx * (ny + 1);
            return StandsInCell(grid, x0 + sx * y0 + sxy * z0, sx, sxy, Frac(x, x0), Frac(y, y0), Frac(z, z0));
        }

        /// <summary>The cell (0..cells-1) holding grid coordinate <paramref name="p"/>.</summary>
        public static int Cell(float p, int cells)
        {
            int c = p < 0f ? 0 : (int)p;
            return c > cells - 1 ? cells - 1 : c;
        }

        /// <summary>Where <paramref name="p"/> lies in cell <paramref name="c"/> (0..1).</summary>
        public static float Frac(float p, int c)
        {
            float f = p - c;
            return f < 0f ? 0f : f > 1f ? 1f : f;
        }

        /// <summary>Height (grid units) of the topmost surface of column (x, z) of a whole grid, or -1 if it has none.</summary>
        public static float TopSurface(byte[] grid, int nx, int ny, int x, int z)
        {
            int sx = nx + 1;
            int col = x + sx * (ny + 1) * z;
            for (int y = ny - 1; y >= 0; y--)
            {
                int b = grid[col + sx * y];
                if (b >= 128) continue;
                // Sample y is solid and y + 1 is air (the scan came down through it): the surface crosses between them.
                int a = grid[col + sx * (y + 1)];
                return y + (128f - b) / (a - b);
            }
            return -1f;
        }
    }
}
