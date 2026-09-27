using UnityEngine;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>Plain C# helpers used by the editor tools and the standalone runtime (not needed in Udon).</summary>
    public static class DigGridUtil
    {
        /// <summary>Trilinear SDF sample in voxels at a grid-space position. Outside the grid counts as air.</summary>
        public static float Sample(byte[] grid, int nx, int ny, int nz, Vector3 p)
        {
            if (p.x < 0 || p.y < 0 || p.z < 0 || p.x > nx || p.y > ny || p.z > nz) return DigFormat.SdfBand;
            int x0 = Mathf.Min((int)p.x, nx - 1), y0 = Mathf.Min((int)p.y, ny - 1), z0 = Mathf.Min((int)p.z, nz - 1);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            int sx = nx + 1, sxy = sx * (ny + 1);
            int i = x0 + sx * y0 + sxy * z0;
            float c00 = Mathf.Lerp(grid[i], grid[i + 1], fx);
            float c10 = Mathf.Lerp(grid[i + sx], grid[i + sx + 1], fx);
            float c01 = Mathf.Lerp(grid[i + sxy], grid[i + sxy + 1], fx);
            float c11 = Mathf.Lerp(grid[i + sxy + sx], grid[i + sxy + sx + 1], fx);
            float v = Mathf.Lerp(Mathf.Lerp(c00, c10, fy), Mathf.Lerp(c01, c11, fy), fz);
            return (v - 128f) / DigFormat.SdfScale;
        }

        /// <summary>Marches a grid-space ray until it enters solid. Returns the refined hit in grid space.</summary>
        public static bool Raycast(byte[] grid, int nx, int ny, int nz, Vector3 origin, Vector3 dir, float maxDist, out Vector3 hit)
        {
            hit = default;
            dir = dir.normalized;

            // Clip the ray to the grid box so the march starts where it matters.
            float tMin = 0f, tMax = maxDist;
            Vector3 size = new Vector3(nx, ny, nz);
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(dir[a]) < 1e-6f)
                {
                    if (origin[a] < 0 || origin[a] > size[a]) return false;
                    continue;
                }
                float t0 = (0 - origin[a]) / dir[a];
                float t1 = (size[a] - origin[a]) / dir[a];
                if (t0 > t1) { float tmp = t0; t0 = t1; t1 = tmp; }
                tMin = Mathf.Max(tMin, t0);
                tMax = Mathf.Min(tMax, t1);
                if (tMin > tMax) return false;
            }

            const float step = 0.5f;
            float prevT = tMin;
            float prev = Sample(grid, nx, ny, nz, origin + dir * tMin);
            if (prev < 0) { hit = origin + dir * tMin; return true; }

            for (float t = tMin + step; t <= tMax + step; t += step)
            {
                float tt = Mathf.Min(t, tMax);
                float s = Sample(grid, nx, ny, nz, origin + dir * tt);
                if (s < 0)
                {
                    float lo = prevT, hi = tt;
                    for (int k = 0; k < 8; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Sample(grid, nx, ny, nz, origin + dir * mid) < 0) hi = mid; else lo = mid;
                    }
                    hit = origin + dir * hi;
                    return true;
                }
                prevT = tt;
                if (tt >= tMax) break;
            }
            return false;
        }

        /// <summary>Editor smoothing brush. Not idempotent, so it is never sent over the network.</summary>
        public static bool Smooth(byte[] grid, int nx, int ny, int nz, int[] edit, Vector3 c, float r, float strength, int[] changed)
        {
            int sx = nx + 1;
            int sxy = sx * (ny + 1);
            int x0 = Mathf.Max(Mathf.Max(edit[0], 1), Mathf.FloorToInt(c.x - r));
            int y0 = Mathf.Max(Mathf.Max(edit[1], 1), Mathf.FloorToInt(c.y - r));
            int z0 = Mathf.Max(Mathf.Max(edit[2], 1), Mathf.FloorToInt(c.z - r));
            int x1 = Mathf.Min(Mathf.Min(edit[3], nx - 1), Mathf.CeilToInt(c.x + r));
            int y1 = Mathf.Min(Mathf.Min(edit[4], ny - 1), Mathf.CeilToInt(c.y + r));
            int z1 = Mathf.Min(Mathf.Min(edit[5], nz - 1), Mathf.CeilToInt(c.z + r));
            if (x1 < x0 || y1 < y0 || z1 < z0) return false;

            int w = x1 - x0 + 1, h = y1 - y0 + 1, dpt = z1 - z0 + 1;
            var result = new byte[w * h * dpt];
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = x + sx * y + sxy * z;
                float dist = Vector3.Distance(new Vector3(x, y, z), c);
                float k = Mathf.Clamp01(1f - dist / r) * strength;
                float avg = (grid[i - 1] + grid[i + 1] + grid[i - sx] + grid[i + sx] + grid[i - sxy] + grid[i + sxy]) / 6f;
                result[(x - x0) + w * ((y - y0) + h * (z - z0))] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(grid[i], avg, k)), 0, 255);
            }

            bool any = false;
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = x + sx * y + sxy * z;
                byte b = result[(x - x0) + w * ((y - y0) + h * (z - z0))];
                if (b == grid[i]) continue;
                grid[i] = b;
                if (!any)
                {
                    changed[0] = changed[3] = x;
                    changed[1] = changed[4] = y;
                    changed[2] = changed[5] = z;
                    any = true;
                }
                else
                {
                    changed[0] = Mathf.Min(changed[0], x); changed[3] = Mathf.Max(changed[3], x);
                    changed[1] = Mathf.Min(changed[1], y); changed[4] = Mathf.Max(changed[4], y);
                    changed[2] = Mathf.Min(changed[2], z); changed[5] = Mathf.Max(changed[5], z);
                }
            }
            return any;
        }
    }
}
