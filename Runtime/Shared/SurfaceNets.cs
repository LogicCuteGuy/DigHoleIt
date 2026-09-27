using UnityEngine;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Naive Surface Nets over the byte grid, one chunk at a time, split into resumable steps so the
    /// Udon runtime can spread a chunk over several frames. Shared by both runtimes (LCGUdonSharp subset).
    ///
    /// A chunk of n cells per axis owns cells [0, n). Vertices are also computed for the cells at -1 so the
    /// owned edges on the low faces can form quads; those border vertices duplicate the neighbour chunk's
    /// vertices exactly, so chunks meet without cracks. Local cell index li = (lx+1) + L*((ly+1) + L*(lz+1)),
    /// L = n + 1.
    ///
    /// Buffers (allocate once per runtime):
    ///   cellVert, vMask, vCell : CellBufferSize(n) ints
    ///   vPos, vNrm             : CellBufferSize(n) Vector3
    ///   vCol, vUv              : CellBufferSize(n) Color / Vector2 (paint weights, see BuildPaint)
    ///   tris                   : TriBufferSize(n) ints
    /// </summary>
    public static class SurfaceNets
    {
        public static int CellBufferSize(int n) { int l = n + 1; return l * l * l; }
        public static int TriBufferSize(int n) { return n * n * n * 18; }
        public static int RowCount(int n) { int l = n + 1; return l * l; }

        /// <summary>
        /// Step 1: computes vertices for cell rows [rowStart, rowEnd) of RowCount(n) rows.
        /// Positions are in metres relative to the chunk origin (cell ox, oy, oz).
        /// </summary>
        /// <returns>The new vertex count.</returns>
        public static int BuildRows(byte[] grid, int nx, int ny, int nz, int ox, int oy, int oz, int n, float voxel,
            int rowStart, int rowEnd, int[] cellVert, Vector3[] vPos, Vector3[] vNrm, int[] vMask, int[] vCell, int vertCount)
        {
            int l = n + 1;
            int sx = nx + 1;
            int sxy = sx * (ny + 1);

            for (int r = rowStart; r < rowEnd; r++)
            {
                int ly = r % l - 1;
                int lz = r / l - 1;
                int gy = oy + ly;
                int gz = oz + lz;
                int baseLi = l * r;

                if (gy < 0 || gz < 0 || gy >= ny || gz >= nz)
                {
                    for (int k = 0; k < l; k++) cellVert[baseLi + k] = -1;
                    continue;
                }

                int rowIdx = sx * gy + sxy * gz;
                bool carry = false;
                int s0 = 0, s2 = 0, s4 = 0, s6 = 0;

                for (int k = 0; k < l; k++)
                {
                    int gx = ox + k - 1;
                    int li = baseLi + k;
                    if (gx < 0 || gx >= nx)
                    {
                        cellVert[li] = -1;
                        carry = false;
                        continue;
                    }

                    int i0 = rowIdx + gx;
                    if (!carry)
                    {
                        s0 = grid[i0] - 128;
                        s2 = grid[i0 + sx] - 128;
                        s4 = grid[i0 + sxy] - 128;
                        s6 = grid[i0 + sx + sxy] - 128;
                        carry = true;
                    }
                    int s1 = grid[i0 + 1] - 128;
                    int s3 = grid[i0 + 1 + sx] - 128;
                    int s5 = grid[i0 + 1 + sxy] - 128;
                    int s7 = grid[i0 + 1 + sx + sxy] - 128;

                    int mask = (s0 < 0 ? 1 : 0) | (s1 < 0 ? 2 : 0) | (s2 < 0 ? 4 : 0) | (s3 < 0 ? 8 : 0)
                             | (s4 < 0 ? 16 : 0) | (s5 < 0 ? 32 : 0) | (s6 < 0 ? 64 : 0) | (s7 < 0 ? 128 : 0);

                    if (mask == 0 || mask == 255)
                    {
                        cellVert[li] = -1;
                    }
                    else
                    {
                        float px = 0f, py = 0f, pz = 0f, t;
                        int cnt = 0;

                        // x edges: 0-1 (y0 z0), 2-3 (y1 z0), 4-5 (y0 z1), 6-7 (y1 z1)
                        if ((s0 < 0) != (s1 < 0)) { t = s0 / (float)(s0 - s1); px += t; cnt++; }
                        if ((s2 < 0) != (s3 < 0)) { t = s2 / (float)(s2 - s3); px += t; py += 1f; cnt++; }
                        if ((s4 < 0) != (s5 < 0)) { t = s4 / (float)(s4 - s5); px += t; pz += 1f; cnt++; }
                        if ((s6 < 0) != (s7 < 0)) { t = s6 / (float)(s6 - s7); px += t; py += 1f; pz += 1f; cnt++; }
                        // y edges: 0-2 (x0 z0), 1-3 (x1 z0), 4-6 (x0 z1), 5-7 (x1 z1)
                        if ((s0 < 0) != (s2 < 0)) { t = s0 / (float)(s0 - s2); py += t; cnt++; }
                        if ((s1 < 0) != (s3 < 0)) { t = s1 / (float)(s1 - s3); px += 1f; py += t; cnt++; }
                        if ((s4 < 0) != (s6 < 0)) { t = s4 / (float)(s4 - s6); py += t; pz += 1f; cnt++; }
                        if ((s5 < 0) != (s7 < 0)) { t = s5 / (float)(s5 - s7); px += 1f; py += t; pz += 1f; cnt++; }
                        // z edges: 0-4 (x0 y0), 1-5 (x1 y0), 2-6 (x0 y1), 3-7 (x1 y1)
                        if ((s0 < 0) != (s4 < 0)) { t = s0 / (float)(s0 - s4); pz += t; cnt++; }
                        if ((s1 < 0) != (s5 < 0)) { t = s1 / (float)(s1 - s5); px += 1f; pz += t; cnt++; }
                        if ((s2 < 0) != (s6 < 0)) { t = s2 / (float)(s2 - s6); py += 1f; pz += t; cnt++; }
                        if ((s3 < 0) != (s7 < 0)) { t = s3 / (float)(s3 - s7); px += 1f; py += 1f; pz += t; cnt++; }

                        float inv = 1f / cnt;
                        vPos[vertCount] = new Vector3(
                            ((k - 1) + px * inv) * voxel,
                            (ly + py * inv) * voxel,
                            (lz + pz * inv) * voxel);

                        // SDF gradient inside the cell points from solid to air: the outward normal.
                        Vector3 g = new Vector3(
                            (s1 + s3 + s5 + s7) - (s0 + s2 + s4 + s6),
                            (s2 + s3 + s6 + s7) - (s0 + s1 + s4 + s5),
                            (s4 + s5 + s6 + s7) - (s0 + s1 + s2 + s3));
                        float len = g.magnitude;
                        vNrm[vertCount] = len > 0.0001f ? g / len : Vector3.up;

                        vMask[vertCount] = mask;
                        vCell[vertCount] = li;
                        cellVert[li] = vertCount;
                        vertCount++;
                    }

                    s0 = s1; s2 = s3; s4 = s5; s6 = s7;
                }
            }
            return vertCount;
        }

        /// <summary>
        /// Optional step between 1 and 2: paint weights for vertices [vStart, vEnd), a trilinear blend of the layers of
        /// the cell's 8 corner samples at the vertex position. <paramref name="vCol"/> receives the weights of terrain
        /// layers 0-3 (paint values 1-4), <paramref name="vUv"/>.x the dug soil weight (5). What is left up to 1 is
        /// "auto" shading. Unpainted vertices get zeros, so a zone that was never painted can skip this step and upload
        /// zeros instead.
        /// </summary>
        public static void BuildPaint(byte[] paint, int nx, int ny, int ox, int oy, int oz, int n, float voxel,
            int[] vCell, Vector3[] vPos, int vStart, int vEnd, Color[] vCol, Vector2[] vUv)
        {
            int l = n + 1;
            int ll = l * l;
            int sx = nx + 1;
            int sxy = sx * (ny + 1);
            float inv = 1f / voxel;

            for (int v = vStart; v < vEnd; v++)
            {
                int li = vCell[v];
                int lx = li % l - 1;
                int ly = (li / l) % l - 1;
                int lz = li / ll - 1;
                int i0 = (ox + lx) + sx * (oy + ly) + sxy * (oz + lz);

                if ((paint[i0] | paint[i0 + 1] | paint[i0 + sx] | paint[i0 + 1 + sx]
                   | paint[i0 + sxy] | paint[i0 + 1 + sxy] | paint[i0 + sx + sxy] | paint[i0 + 1 + sx + sxy]) == 0)
                {
                    vCol[v] = new Color(0f, 0f, 0f, 0f);
                    vUv[v] = new Vector2(0f, 0f);
                    continue;
                }

                Vector3 p = vPos[v];
                float fx = Mathf.Clamp01(p.x * inv - lx);
                float fy = Mathf.Clamp01(p.y * inv - ly);
                float fz = Mathf.Clamp01(p.z * inv - lz);
                float w1 = 0f, w2 = 0f, w3 = 0f, w4 = 0f, w5 = 0f;

                for (int k = 0; k < 8; k++)
                {
                    int bx = k & 1;
                    int by = (k >> 1) & 1;
                    int bz = (k >> 2) & 1;
                    int layer = paint[i0 + bx + sx * by + sxy * bz];
                    if (layer == 0) continue;
                    float w = (bx == 1 ? fx : 1f - fx) * (by == 1 ? fy : 1f - fy) * (bz == 1 ? fz : 1f - fz);
                    if (layer == 1) w1 += w;
                    else if (layer == 2) w2 += w;
                    else if (layer == 3) w3 += w;
                    else if (layer == 4) w4 += w;
                    else if (layer == 5) w5 += w;
                }

                vCol[v] = new Color(w1, w2, w3, w4);
                vUv[v] = new Vector2(w5, 0f);
            }
        }

        /// <summary>
        /// Step 2: emits quads for vertices [vStart, vEnd). Each owned cell emits the quads of the three sign-changing
        /// edges leaving its minimum corner. Triangles are clockwise when seen from the air side (Unity front face).
        /// Each quad is split along its shorter diagonal, which avoids saw-tooth folds along sharp rims.
        /// </summary>
        /// <returns>The new index count.</returns>
        public static int BuildQuads(int n, int[] cellVert, int[] vMask, int[] vCell, Vector3[] vPos, int vStart, int vEnd, int[] tris, int triCount)
        {
            int l = n + 1;
            int ll = l * l;

            for (int v = vStart; v < vEnd; v++)
            {
                int li = vCell[v];
                if (li % l == 0 || (li / l) % l == 0 || li / ll == 0) continue; // border cell at -1: not owned

                int m = vMask[v];
                bool solid0 = (m & 1) != 0;
                int a, b, d;

                if (solid0 != ((m & 2) != 0)) // edge along +x, quad in the y/z plane
                {
                    a = cellVert[li - l - ll]; b = cellVert[li - ll]; d = cellVert[li - l];
                    if (a >= 0 && b >= 0 && d >= 0) triCount = Quad(tris, vPos, triCount, a, b, v, d, solid0);
                }
                if (solid0 != ((m & 4) != 0)) // edge along +y, quad in the z/x plane
                {
                    a = cellVert[li - ll - 1]; b = cellVert[li - 1]; d = cellVert[li - ll];
                    if (a >= 0 && b >= 0 && d >= 0) triCount = Quad(tris, vPos, triCount, a, b, v, d, solid0);
                }
                if (solid0 != ((m & 16) != 0)) // edge along +z, quad in the x/y plane
                {
                    a = cellVert[li - 1 - l]; b = cellVert[li - l]; d = cellVert[li - 1];
                    if (a >= 0 && b >= 0 && d >= 0) triCount = Quad(tris, vPos, triCount, a, b, v, d, solid0);
                }
            }
            return triCount;
        }

        private static int Quad(int[] tris, Vector3[] vPos, int i, int c00, int c10, int c11, int c01, bool frontPositive)
        {
            bool mainDiagonal = (vPos[c00] - vPos[c11]).sqrMagnitude <= (vPos[c10] - vPos[c01]).sqrMagnitude;
            if (mainDiagonal)
            {
                if (frontPositive)
                {
                    tris[i] = c00; tris[i + 1] = c10; tris[i + 2] = c11;
                    tris[i + 3] = c00; tris[i + 4] = c11; tris[i + 5] = c01;
                }
                else
                {
                    tris[i] = c00; tris[i + 1] = c11; tris[i + 2] = c10;
                    tris[i + 3] = c00; tris[i + 4] = c01; tris[i + 5] = c11;
                }
            }
            else if (frontPositive)
            {
                tris[i] = c00; tris[i + 1] = c10; tris[i + 2] = c01;
                tris[i + 3] = c10; tris[i + 4] = c11; tris[i + 5] = c01;
            }
            else
            {
                tris[i] = c00; tris[i + 1] = c01; tris[i + 2] = c10;
                tris[i + 3] = c10; tris[i + 4] = c01; tris[i + 5] = c11;
            }
            return i + 6;
        }
    }
}
