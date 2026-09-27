using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    public enum DigBrushMode { Dig = 0, Add = 1, Paint = 2, Smooth = 3, Reset = 4 }

    public enum DigBrushShape { Sphere = 0, Soft = 1, Flat = 2, Custom = 3 }

    public enum DigBrushAlign { SurfaceNormal = 0, WorldUp = 1, View = 2 }

    /// <summary>Brush settings shared by the Dig Zone sculpt tool and the Terrain tools (stored in EditorPrefs).</summary>
    public static class DigBrushSettings
    {
        private const string Prefix = "DigHoleIt.Brush.";

        public const float MinRadius = 0.1f;
        public const float MaxRadius = 20f;

        public static DigBrushMode SculptMode
        {
            get => (DigBrushMode)EditorPrefs.GetInt(Prefix + "Mode", (int)DigBrushMode.Dig);
            set => EditorPrefs.SetInt(Prefix + "Mode", (int)value);
        }

        /// <summary>Brush radius in metres.</summary>
        public static float Radius
        {
            get => EditorPrefs.GetFloat(Prefix + "Radius", 1.5f);
            set => EditorPrefs.SetFloat(Prefix + "Radius", Mathf.Clamp(value, MinRadius, MaxRadius));
        }

        public static float Strength
        {
            get => EditorPrefs.GetFloat(Prefix + "Strength", 1f);
            set => EditorPrefs.SetFloat(Prefix + "Strength", Mathf.Clamp(value, 0.01f, 1f));
        }

        /// <summary>Layer painted by the Paint mode (DigFormat: 0 auto, 1-4 terrain layers 0-3, 5 dug soil, 6-17 terrain layers 4-15).</summary>
        public static int PaintLayer
        {
            get => EditorPrefs.GetInt(Prefix + "PaintLayer", 1);
            set => EditorPrefs.SetInt(Prefix + "PaintLayer", Mathf.Clamp(value, 0, DigFormat.LayerMax));
        }

        /// <summary>Layer given to added soil (0 = leave the paint as is).</summary>
        public static int AddLayer
        {
            get => EditorPrefs.GetInt(Prefix + "AddLayer", 0);
            set => EditorPrefs.SetInt(Prefix + "AddLayer", Mathf.Clamp(value, 0, DigFormat.LayerMax));
        }

        public static DigBrushShape Shape
        {
            get => (DigBrushShape)EditorPrefs.GetInt(Prefix + "Shape", (int)DigBrushShape.Sphere);
            set => EditorPrefs.SetInt(Prefix + "Shape", (int)value);
        }

        public static DigBrushAlign Align
        {
            get => (DigBrushAlign)EditorPrefs.GetInt(Prefix + "Align", (int)DigBrushAlign.SurfaceNormal);
            set => EditorPrefs.SetInt(Prefix + "Align", (int)value);
        }

        public static Texture2D CustomMask
        {
            get
            {
                string guid = EditorPrefs.GetString(Prefix + "CustomMask", "");
                return string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            }
            set
            {
                string path = value != null ? AssetDatabase.GetAssetPath(value) : null;
                EditorPrefs.SetString(Prefix + "CustomMask", string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path));
            }
        }
    }

    /// <summary>
    /// A brush footprint: height 0..1 over the square u, v in [-1, 1] (brush space). Built from a shape preset or any
    /// texture (Unity terrain brushes included), read back once and cached.
    /// </summary>
    public sealed class DigBrushMask
    {
        private const int Res = 64;
        private static readonly Dictionary<int, DigBrushMask> TextureCache = new Dictionary<int, DigBrushMask>();
        private static readonly DigBrushMask[] ShapeCache = new DigBrushMask[3];

        private readonly float[] _v = new float[Res * Res];

        public static DigBrushMask ForShape(DigBrushShape shape)
        {
            if (shape == DigBrushShape.Custom)
            {
                Texture2D tex = DigBrushSettings.CustomMask;
                return tex != null ? ForTexture(tex) : ForShape(DigBrushShape.Sphere);
            }

            int s = (int)shape;
            if (ShapeCache[s] != null) return ShapeCache[s];
            var mask = new DigBrushMask();
            for (int j = 0; j < Res; j++)
            for (int i = 0; i < Res; i++)
            {
                float u = i / (Res - 1f) * 2f - 1f, v = j / (Res - 1f) * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float h;
                switch (shape)
                {
                    case DigBrushShape.Soft:
                        float t = Mathf.Clamp01(1f - r);
                        h = t * t * (3f - 2f * t);
                        break;
                    case DigBrushShape.Flat:
                        h = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - r) / 0.3f));
                        break;
                    default: // a sphere: |a| < R * sqrt(1 - r^2)
                        h = Mathf.Sqrt(Mathf.Max(0f, 1f - r * r));
                        break;
                }
                mask._v[i + j * Res] = h;
            }
            return ShapeCache[s] = mask;
        }

        /// <summary>Reads any texture (including non-readable and R16 terrain brushes) into a mask.</summary>
        public static DigBrushMask ForTexture(Texture tex)
        {
            if (tex == null) return ForShape(DigBrushShape.Sphere);
            int key = tex.GetInstanceID();
            if (TextureCache.TryGetValue(key, out DigBrushMask cached)) return cached;

            RenderTexture rt = RenderTexture.GetTemporary(Res, Res, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture prev = RenderTexture.active;
            var read = new Texture2D(Res, Res, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, Res, Res), 0, 0, false);
                read.Apply(false);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }

            Color[] px = read.GetPixels();
            Object.DestroyImmediate(read);

            // Terrain brushes keep the height in red; alpha-only textures keep it in alpha.
            float maxR = 0f;
            foreach (Color c in px) maxR = Mathf.Max(maxR, c.r);
            bool useAlpha = maxR < 0.01f;

            var mask = new DigBrushMask();
            for (int k = 0; k < px.Length; k++) mask._v[k] = Mathf.Clamp01(useAlpha ? px[k].a : px[k].r);
            TextureCache[key] = mask;
            return mask;
        }

        /// <summary>Bilinear height at brush space u, v (0 outside the square).</summary>
        public float Sample(float u, float v)
        {
            if (u <= -1f || u >= 1f || v <= -1f || v >= 1f) return 0f;
            float x = (u * 0.5f + 0.5f) * (Res - 1), y = (v * 0.5f + 0.5f) * (Res - 1);
            int x0 = Mathf.Min((int)x, Res - 2), y0 = Mathf.Min((int)y, Res - 2);
            float fx = x - x0, fy = y - y0;
            int i = x0 + y0 * Res;
            float a = Mathf.Lerp(_v[i], _v[i + 1], fx);
            float b = Mathf.Lerp(_v[i + Res], _v[i + Res + 1], fx);
            return Mathf.Lerp(a, b, fy);
        }
    }

    /// <summary>
    /// Editor-only brush operations on a zone's grids, shaped by a <see cref="DigBrushMask"/> and oriented by a normal.
    /// Dig and Add push the surface in or out by an amount with a smooth falloff (like the terrain Raise/Lower brush),
    /// so repeated or held strokes build up gradually and blend. These are never networked, so they need not match the
    /// runtime sphere brush.
    /// </summary>
    public static class DigEditorBrush
    {
        /// <summary>Largest surface move per sub-step, in voxels; keeps the clamped distance field consistent.</summary>
        private const float MaxStep = 0.5f;

        /// <summary>
        /// One-shot brush. Dig and Add move the surface by up to <paramref name="r"/> * <paramref name="strength"/>
        /// voxels at the centre; Paint and Smooth use strength as coverage and blend amount.
        /// </summary>
        /// <param name="c">Brush centre in grid units.</param>
        /// <param name="normal">Brush axis (unit length).</param>
        /// <param name="r">Brush radius in voxels.</param>
        /// <param name="changed">Receives the inclusive changed sample box.</param>
        public static bool Apply(DigZoneData data, Vector3 c, Vector3 normal, float r, float strength, DigBrushMask mask,
            DigBrushMode mode, int layer, int[] changed)
        {
            switch (mode)
            {
                case DigBrushMode.Smooth: return Smooth(data, c, normal, r, strength, mask, changed);
                case DigBrushMode.Reset: return Reset(data, c, normal, r, strength, mask, changed);
                case DigBrushMode.Paint: return Paint(data, c, normal, r, strength, mask, layer, changed);
                default: return Offset(data, c, normal, r, r * strength, mask, mode == DigBrushMode.Dig, layer, changed);
            }
        }

        /// <summary>
        /// Moves the surface inward (dig) or outward (add) by <paramref name="amount"/> voxels at the brush centre,
        /// fading to nothing at the brush edge. Only samples near the surface are shifted; the deep samples, which the
        /// grid clamps at ±2 voxels, are then pulled along so the field stays a valid distance (|step| &lt;= 1 voxel).
        /// </summary>
        /// <param name="layer">For Add: layer given to the added soil (0 = leave the paint as is).</param>
        public static bool Offset(DigZoneData data, Vector3 c, Vector3 normal, float r, float amount, DigBrushMask mask,
            bool dig, int layer, int[] changed)
        {
            var box = new ChangeBox();
            int steps = Mathf.Max(1, Mathf.CeilToInt(amount / MaxStep));
            float step = amount / steps;
            for (int k = 0; k < steps; k++)
                OffsetStep(data, c, normal, r, step, mask, dig, layer, ref box);
            return box.CopyTo(changed);
        }

        private static void OffsetStep(DigZoneData data, Vector3 c, Vector3 normal, float r, float amount, DigBrushMask mask,
            bool dig, int layer, ref ChangeBox box)
        {
            int nx = data.nx, ny = data.ny, nz = data.nz;
            int sx = nx + 1, sxy = sx * (ny + 1);
            byte[] grid = data.grid;
            byte[] paint = !dig && layer > 0 ? data.EnsurePaint() : null;
            BuildFrame(normal, out Vector3 t1, out Vector3 t2);

            // Samples with neighbours on every side, inside the editable box.
            int[] e = data.editBox;
            int ex0 = Mathf.Max(e[0], 1), ey0 = Mathf.Max(e[1], 1), ez0 = Mathf.Max(e[2], 1);
            int ex1 = Mathf.Min(e[3], nx - 1), ey1 = Mathf.Min(e[4], ny - 1), ez1 = Mathf.Min(e[5], nz - 1);

            float reach = r * 1.42f + 1f;
            int x0 = Mathf.Max(ex0, Mathf.FloorToInt(c.x - reach)), x1 = Mathf.Min(ex1, Mathf.CeilToInt(c.x + reach));
            int y0 = Mathf.Max(ey0, Mathf.FloorToInt(c.y - reach)), y1 = Mathf.Min(ey1, Mathf.CeilToInt(c.y + reach));
            int z0 = Mathf.Max(ez0, Mathf.FloorToInt(c.z - reach)), z1 = Mathf.Min(ez1, Mathf.CeilToInt(c.z + reach));
            if (x1 < x0 || y1 < y0 || z1 < z0) return;

            float invR = 1f / r;
            float units = amount * DigFormat.SdfScale;
            int sign = dig ? 1 : -1;
            byte paintValue = (byte)layer;

            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var d = new Vector3(x - c.x, y - c.y, z - c.z);
                float t = Vector3.Dot(d, normal) * invR;
                float along = 1f - t * t;
                if (along <= 0f) continue;
                float m = mask.Sample(Vector3.Dot(d, t1) * invR, Vector3.Dot(d, t2) * invR);
                if (m <= 0f) continue;
                float w = m * along;

                int i = x + sx * y + sxy * z;
                int s = grid[i] - 128;
                // Clamped samples (deep solid for dig, far air for add) have no real distance to shift; the repair
                // pass below pulls them along.
                if (dig ? s <= -128 : s >= 127) continue;
                int q = Mathf.RoundToInt(units * w);
                if (q == 0) continue;
                int ns = Mathf.Clamp(s + sign * q, -128, 127);
                if (ns != s)
                {
                    grid[i] = (byte)(ns + 128);
                    box.Add(x, y, z);
                }
                if (paint != null && w > 0.3f && ns < 0 && ns > -128 && paint[i] != paintValue)
                {
                    paint[i] = paintValue;
                    box.Add(x, y, z);
                }
            }

            // Repair: a sample is never more than one voxel (64 units) further from the surface than a neighbour.
            // Dig raises deep samples toward their raised neighbours; Add lowers far air samples likewise.
            int band = DigFormat.SdfBand + 1;
            int rx0 = Mathf.Max(ex0, x0 - band), rx1 = Mathf.Min(ex1, x1 + band);
            // The top sample row is forced to air, not a real distance: keep the repair one row below it.
            int ry0 = Mathf.Max(ey0, y0 - band), ry1 = Mathf.Min(Mathf.Min(ey1, ny - 2), y1 + band);
            int rz0 = Mathf.Max(ez0, z0 - band), rz1 = Mathf.Min(ez1, z1 + band);
            for (int pass = 0; pass < band; pass++)
            for (int z = rz0; z <= rz1; z++)
            for (int y = ry0; y <= ry1; y++)
            for (int x = rx0; x <= rx1; x++)
            {
                int i = x + sx * y + sxy * z;
                int s = grid[i] - 128;
                int a0 = grid[i - 1], a1 = grid[i + 1], a2 = grid[i - sx], a3 = grid[i + sx], a4 = grid[i - sxy], a5 = grid[i + sxy];
                int ns;
                if (dig)
                {
                    int hi = Mathf.Max(Mathf.Max(Mathf.Max(a0, a1), Mathf.Max(a2, a3)), Mathf.Max(a4, a5)) - 128;
                    ns = Mathf.Max(s, hi - DigFormat.SdfScale);
                }
                else
                {
                    int lo = Mathf.Min(Mathf.Min(Mathf.Min(a0, a1), Mathf.Min(a2, a3)), Mathf.Min(a4, a5)) - 128;
                    ns = Mathf.Min(s, lo + DigFormat.SdfScale);
                }
                ns = Mathf.Clamp(ns, -128, 127);
                if (ns == s) continue;
                grid[i] = (byte)(ns + 128);
                box.Add(x, y, z);
            }
        }

        /// <summary>Sets the layer of the voxels inside the brush, thinned out by mask * strength (dithered, so it blends).</summary>
        private static bool Paint(DigZoneData data, Vector3 c, Vector3 normal, float r, float strength, DigBrushMask mask, int layer, int[] changed)
        {
            int nx = data.nx, ny = data.ny;
            int sx = nx + 1, sxy = sx * (ny + 1);
            byte[] paint = data.EnsurePaint();
            BuildFrame(normal, out Vector3 t1, out Vector3 t2);

            int[] e = data.editBox;
            int x0 = Mathf.Max(e[0], Mathf.FloorToInt(c.x - r)), x1 = Mathf.Min(e[3], Mathf.CeilToInt(c.x + r));
            int y0 = Mathf.Max(e[1], Mathf.FloorToInt(c.y - r)), y1 = Mathf.Min(e[4], Mathf.CeilToInt(c.y + r));
            int z0 = Mathf.Max(e[2], Mathf.FloorToInt(c.z - r)), z1 = Mathf.Min(e[5], Mathf.CeilToInt(c.z + r));

            float invR = 1f / r;
            byte paintValue = (byte)layer;
            var box = new ChangeBox();
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var d = new Vector3(x - c.x, y - c.y, z - c.z);
                if (d.sqrMagnitude >= r * r) continue;
                float m = mask.Sample(Vector3.Dot(d, t1) * invR, Vector3.Dot(d, t2) * invR);
                int i = x + sx * y + sxy * z;
                if (m * strength <= Hash01(i) || paint[i] == paintValue) continue;
                paint[i] = paintValue;
                box.Add(x, y, z);
            }
            return box.CopyTo(changed);
        }

        private static bool Smooth(DigZoneData data, Vector3 c, Vector3 normal, float r, float strength, DigBrushMask mask, int[] changed)
        {
            int nx = data.nx, ny = data.ny, nz = data.nz;
            int sx = nx + 1, sxy = sx * (ny + 1);
            byte[] grid = data.grid;
            BuildFrame(normal, out Vector3 t1, out Vector3 t2);

            int[] e = data.editBox;
            float reach = r * 1.42f;
            int x0 = Mathf.Max(Mathf.Max(e[0], 1), Mathf.FloorToInt(c.x - reach)), x1 = Mathf.Min(Mathf.Min(e[3], nx - 1), Mathf.CeilToInt(c.x + reach));
            int y0 = Mathf.Max(Mathf.Max(e[1], 1), Mathf.FloorToInt(c.y - reach)), y1 = Mathf.Min(Mathf.Min(e[4], ny - 1), Mathf.CeilToInt(c.y + reach));
            int z0 = Mathf.Max(Mathf.Max(e[2], 1), Mathf.FloorToInt(c.z - reach)), z1 = Mathf.Min(Mathf.Min(e[5], nz - 1), Mathf.CeilToInt(c.z + reach));
            if (x1 < x0 || y1 < y0 || z1 < z0) return false;

            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            var result = new byte[w * h * (z1 - z0 + 1)];
            float invR = 1f / r;
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = x + sx * y + sxy * z;
                var d = new Vector3(x - c.x, y - c.y, z - c.z);
                float m = mask.Sample(Vector3.Dot(d, t1) * invR, Vector3.Dot(d, t2) * invR);
                float k = strength * m * Mathf.Clamp01(1f - Mathf.Abs(Vector3.Dot(d, normal)) * invR);
                float avg = (grid[i - 1] + grid[i + 1] + grid[i - sx] + grid[i + sx] + grid[i - sxy] + grid[i + sxy]) / 6f;
                result[(x - x0) + w * ((y - y0) + h * (z - z0))] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(grid[i], avg, k)), 0, 255);
            }

            var box = new ChangeBox();
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = x + sx * y + sxy * z;
                byte b = result[(x - x0) + w * ((y - y0) + h * (z - z0))];
                if (b == grid[i]) continue;
                grid[i] = b;
                box.Add(x, y, z);
            }
            return box.CopyTo(changed);
        }

        /// <summary>
        /// Blends the voxels inside the brush back toward the terrain they were baked from and wipes their paint, by
        /// mask * <paramref name="strength"/> (1 = straight back to the terrain).
        /// </summary>
        private static bool Reset(DigZoneData data, Vector3 c, Vector3 normal, float r, float strength, DigBrushMask mask, int[] changed)
        {
            byte[] grid = data.grid, baseGrid = data.baseGrid;
            if (baseGrid == null || baseGrid.Length != grid.Length) return false;
            byte[] paint = data.HasPaintGrid ? data.paint : null;
            int nx = data.nx, ny = data.ny;
            int sx = nx + 1, sxy = sx * (ny + 1);
            BuildFrame(normal, out Vector3 t1, out Vector3 t2);

            int[] e = data.editBox;
            float reach = r * 1.42f;
            int x0 = Mathf.Max(e[0], Mathf.FloorToInt(c.x - reach)), x1 = Mathf.Min(e[3], Mathf.CeilToInt(c.x + reach));
            int y0 = Mathf.Max(e[1], Mathf.FloorToInt(c.y - reach)), y1 = Mathf.Min(e[4], Mathf.CeilToInt(c.y + reach));
            int z0 = Mathf.Max(e[2], Mathf.FloorToInt(c.z - reach)), z1 = Mathf.Min(e[5], Mathf.CeilToInt(c.z + reach));

            float invR = 1f / r;
            var box = new ChangeBox();
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var d = new Vector3(x - c.x, y - c.y, z - c.z);
                float t = Vector3.Dot(d, normal) * invR;
                float along = 1f - t * t;
                if (along <= 0f) continue;
                float k = strength * mask.Sample(Vector3.Dot(d, t1) * invR, Vector3.Dot(d, t2) * invR) * along;
                if (k <= 0f) continue;

                int i = x + sx * y + sxy * z;
                int s = grid[i], b = baseGrid[i];
                if (s != b)
                {
                    int ns = k >= 0.999f ? b : Mathf.RoundToInt(Mathf.Lerp(s, b, k));
                    // Always make progress, or low strength would stall a unit away from the terrain.
                    if (ns == s) ns += b > s ? 1 : -1;
                    grid[i] = (byte)ns;
                    box.Add(x, y, z);
                }
                if (paint != null && paint[i] != 0 && k > Hash01(i))
                {
                    paint[i] = 0;
                    box.Add(x, y, z);
                }
            }
            return box.CopyTo(changed);
        }

        /// <summary>Outward surface normal at a grid-space point, from the SDF gradient.</summary>
        public static Vector3 SurfaceNormal(DigZoneData data, Vector3 g)
        {
            byte[] grid = data.grid;
            int nx = data.nx, ny = data.ny, nz = data.nz;
            var n = new Vector3(
                DigGridUtil.Sample(grid, nx, ny, nz, g + Vector3.right) - DigGridUtil.Sample(grid, nx, ny, nz, g - Vector3.right),
                DigGridUtil.Sample(grid, nx, ny, nz, g + Vector3.up) - DigGridUtil.Sample(grid, nx, ny, nz, g - Vector3.up),
                DigGridUtil.Sample(grid, nx, ny, nz, g + Vector3.forward) - DigGridUtil.Sample(grid, nx, ny, nz, g - Vector3.forward));
            return n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
        }

        public static void BuildFrame(Vector3 n, out Vector3 t1, out Vector3 t2)
        {
            Vector3 helper = Mathf.Abs(n.y) < 0.99f ? Vector3.up : Vector3.right;
            t1 = Vector3.Cross(n, helper).normalized;
            t2 = Vector3.Cross(n, t1);
        }

        private static float Hash01(int i)
        {
            unchecked
            {
                uint h = (uint)i * 2654435761u;
                h ^= h >> 16;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        private struct ChangeBox
        {
            private bool _any;
            private int _x0, _y0, _z0, _x1, _y1, _z1;

            public void Add(int x, int y, int z)
            {
                if (!_any)
                {
                    _any = true;
                    _x0 = _x1 = x; _y0 = _y1 = y; _z0 = _z1 = z;
                    return;
                }
                if (x < _x0) _x0 = x; else if (x > _x1) _x1 = x;
                if (y < _y0) _y0 = y; else if (y > _y1) _y1 = y;
                if (z < _z0) _z0 = z; else if (z > _z1) _z1 = z;
            }

            public bool CopyTo(int[] changed)
            {
                if (!_any) return false;
                changed[0] = _x0; changed[1] = _y0; changed[2] = _z0;
                changed[3] = _x1; changed[4] = _y1; changed[5] = _z1;
                return true;
            }
        }
    }
}
