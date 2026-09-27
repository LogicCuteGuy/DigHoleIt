using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// Turns a DigZone into baked data: SDF grid from the terrain heights, a terrain hole over the zone footprint,
    /// shader textures, and chunk meshes. Runtime backends hook <see cref="Baked"/> / <see cref="GridChanged"/>
    /// to copy what they need (see Editor/Udon for the VRChat bridge).
    /// </summary>
    public static class DigZoneBaker
    {
        public const string DefaultShader = "DigHoleIt/DigTerrain";
        public const string LiteShader = "DigHoleIt/DigTerrain Lite";
        private const string ZoneFolder = "Assets/DigHoleIt/Zones";

        /// <summary>Raised after a full bake (chunk objects may have been recreated).</summary>
        public static event Action<DigZone> Baked;

        /// <summary>Raised after the grid changed without a rebuild (sculpt strokes, undo).</summary>
        public static event Action<DigZone> GridChanged;

        public static void NotifyGridChanged(DigZone zone) => GridChanged?.Invoke(zone);

        /// <summary>
        /// True while the baker itself changes terrains or re-syncs a zone to a terrain edit. Terrain callbacks are
        /// ignored meanwhile, and listeners should not record undo (terrain sync is derived data, redone after undo).
        /// </summary>
        public static bool Busy { get; private set; }

        // ---------------------------------------------------------------- Fit

        public static void FitToTerrain(DigZone zone)
        {
            Terrain t = zone.terrain;
            if (t == null) { Debug.LogError("[DigHoleIt] Assign a Terrain first.", zone); return; }

            Undo.RecordObject(zone.transform, "Fit Dig Zone");
            Undo.RecordObject(zone, "Fit Dig Zone");
            zone.transform.rotation = Quaternion.identity;
            zone.transform.localScale = Vector3.one;

            Vector3 p = zone.transform.position;
            float sizeX = zone.cells.x * zone.voxelSize;
            float sizeZ = zone.cells.z * zone.voxelSize;
            float minH = float.MaxValue, maxH = float.MinValue;
            const int probes = 24;
            for (int j = 0; j <= probes; j++)
            for (int i = 0; i <= probes; i++)
            {
                var w = new Vector3(p.x + sizeX * i / probes, 0f, p.z + sizeZ * j / probes);
                float h = t.SampleHeight(w) + t.transform.position.y;
                minH = Mathf.Min(minH, h);
                maxH = Mathf.Max(maxH, h);
            }

            // Stay on the current voxel lattice so a re-bake can keep the sculpting.
            float v = zone.voxelSize;
            float bottom = p.y + Mathf.Floor((minH - zone.depthBelowTerrain - p.y) / v) * v;
            float top = maxH + zone.headroomAboveTerrain;
            zone.transform.position = new Vector3(p.x, bottom, p.z);
            zone.cells.y = Mathf.Max(4, Mathf.CeilToInt((top - bottom) / v));
        }

        // ---------------------------------------------------------------- Sculpt bookkeeping

        /// <summary>True if the zone's grid differs from the terrain it was baked from, or it has paint.</summary>
        public static bool HasSculpt(DigZoneData data)
        {
            if (data == null || !data.HasGrid) return false;
            if (data.HasPaint || data.HasStash) return true;
            if (data.baseGrid == null || data.baseGrid.Length != data.grid.Length) return false;
            for (int i = 0; i < data.grid.Length; i++) if (data.grid[i] != data.baseGrid[i]) return true;
            return false;
        }

        /// <summary>Whether a bake with the zone's current settings can carry the sculpting over.</summary>
        public static bool CanKeepSculpt(DigZone zone, out string reason)
        {
            reason = null;
            DigZoneData data = zone.data;
            if (data == null || !data.HasGrid) return true;
            if (!Mathf.Approximately(data.voxelSize, zone.voxelSize))
            {
                reason = "the voxel size changed";
                return false;
            }
            Vector3 old = data.hasOrigin ? data.origin : zone.transform.position;
            Vector3 off = (zone.transform.position - old) / zone.voxelSize;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(off[a] - Mathf.Round(off[a])) > 0.01f)
                {
                    reason = "the zone moved off its voxel lattice (move it with the box handles to stay on it)";
                    return false;
                }
            }
            return true;
        }

        // ---------------------------------------------------------------- Bake

        /// <summary>Bakes the zone, keeping sculpting and paint where the new zone still covers them.</summary>
        public static bool Bake(DigZone zone) => Bake(zone, true);

        /// <param name="keepSculpt">
        /// Carry sculpted samples and paint over to the new grid (they must line up with it). False resets the zone to
        /// the plain terrain.
        /// </param>
        public static bool Bake(DigZone zone, bool keepSculpt)
        {
            if (!Validate(zone, out string error))
            {
                Debug.LogError("[DigHoleIt] " + error, zone);
                EditorUtility.DisplayDialog("DigHoleIt", error, "OK");
                return false;
            }

            if (keepSculpt && !CanKeepSculpt(zone, out string reason) && HasSculpt(zone.data) &&
                !EditorUtility.DisplayDialog("DigHoleIt", $"The sculpting and paint of '{zone.name}' can't be kept because {reason}. Bake anyway and discard it?", "Discard and Bake", "Cancel"))
            {
                return false;
            }

            bool wasBusy = Busy;
            try
            {
                Busy = true;
                EditorUtility.DisplayProgressBar("DigHoleIt", "Preparing", 0f);
                Terrain terrain = zone.terrain;
                TerrainData td = terrain.terrainData;

                Undo.RecordObject(zone.transform, "Bake Dig Zone");
                zone.transform.rotation = Quaternion.identity;
                zone.transform.localScale = Vector3.one;

                // Check before touching anything, so a failed bake leaves the previous one intact.
                if (!ComputeHoleRect(zone, td, out RectInt holeCells, out Vector4 holeWorld, out string holeError))
                {
                    Debug.LogError("[DigHoleIt] " + holeError, zone);
                    EditorUtility.DisplayDialog("DigHoleIt", holeError, "OK");
                    return false;
                }

                MakeDataUnique(zone);
                DigZoneData data = EnsureData(zone);
                Undo.RegisterCompleteObjectUndo(data, "Bake Dig Zone");

                RestoreTerrainCut(data);
                OldGrid old = keepSculpt && data.HasGrid && CanKeepSculpt(zone, out _) ? new OldGrid(data, zone) : null;

                data.nx = zone.cells.x;
                data.ny = zone.cells.y;
                data.nz = zone.cells.z;
                data.voxelSize = zone.voxelSize;
                data.chunkCells = zone.chunkCells;
                data.holeRect = holeWorld;
                data.editBox = ComputeEditBox(zone, holeWorld);

                EditorUtility.DisplayProgressBar("DigHoleIt", "Sampling terrain", 0.15f);
                BakeGrid(zone, data, out float minH, out float maxH);
                data.origin = zone.transform.position;
                data.hasOrigin = true;
                data.paint = null;
                if (old != null) old.CopyInto(data);
                else data.ClearStash();
                data.gridVersion++;
                DigSculptUndo.MarkSeen(data);

                EditorUtility.DisplayProgressBar("DigHoleIt", "Cutting terrain hole", 0.35f);
                CutTerrain(data, td, holeCells);

                EditorUtility.DisplayProgressBar("DigHoleIt", "Baking textures", 0.45f);
                BakeControl(zone, data, td);
                BakeHeight(zone, data, minH, maxH);

                EnsureMaterial(zone, data);

                EditorUtility.DisplayProgressBar("DigHoleIt", "Meshing chunks", 0.6f);
                BuildChunks(zone, data);

                EditorUtility.SetDirty(data);
                EditorUtility.SetDirty(zone);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
            }
            finally
            {
                Busy = wasBusy;
                EditorUtility.ClearProgressBar();
            }

            Baked?.Invoke(zone);
            Debug.Log($"[DigHoleIt] Baked '{zone.name}': {zone.cells.x}x{zone.cells.y}x{zone.cells.z} cells, " +
                      $"{zone.data.ChunkCount} chunks, grid {zone.data.grid.Length / 1024} KB.", zone);
            return true;
        }

        private static bool Validate(DigZone zone, out string error)
        {
            error = null;
            if (zone.terrain == null || zone.terrain.terrainData == null) { error = "Assign a Terrain with TerrainData."; return false; }
            if (zone.cells.x < 8 || zone.cells.y < 4 || zone.cells.z < 8) { error = "Cells must be at least 8 x 4 x 8."; return false; }
            if ((long)(zone.cells.x + 1) * (zone.cells.y + 1) * (zone.cells.z + 1) > 16_000_000) { error = "Grid is too large (over 16M samples)."; return false; }
            if (zone.cells.x * 16 > 65535 || zone.cells.y * 16 > 65535 || zone.cells.z * 16 > 65535) { error = "Cells per axis must be at most 4095."; return false; }

            Terrain t = zone.terrain;
            Vector3 tp = t.transform.position;
            Vector3 ts = t.terrainData.size;
            Bounds b = zone.WorldBounds;
            if (b.min.x < tp.x || b.min.z < tp.z || b.max.x > tp.x + ts.x || b.max.z > tp.z + ts.z)
            {
                error = "The zone footprint must lie completely on the terrain.";
                return false;
            }
            return true;
        }

        private static DigZoneData EnsureData(DigZone zone)
        {
            if (zone.data != null) return zone.data;

            if (!AssetDatabase.IsValidFolder("Assets/DigHoleIt")) AssetDatabase.CreateFolder("Assets", "DigHoleIt");
            if (!AssetDatabase.IsValidFolder(ZoneFolder)) AssetDatabase.CreateFolder("Assets/DigHoleIt", "Zones");

            string scene = string.IsNullOrEmpty(zone.gameObject.scene.name) ? "Scene" : zone.gameObject.scene.name;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ZoneFolder}/{scene}_{zone.name}.asset");
            var data = ScriptableObject.CreateInstance<DigZoneData>();
            AssetDatabase.CreateAsset(data, path);
            Undo.RecordObject(zone, "Create Dig Zone Data");
            zone.data = data;
            return data;
        }

        /// <summary>Hole cells fully inside the zone footprint shrunk by one voxel, so the mesh runs under the terrain edge.</summary>
        private static bool ComputeHoleRect(DigZone zone, TerrainData td, out RectInt cells, out Vector4 world, out string error)
        {
            Vector3 tp = zone.terrain.transform.position;
            int res = td.holesResolution;
            float hx = td.size.x / res;
            float hz = td.size.z / res;
            Bounds b = zone.WorldBounds;
            float v = zone.voxelSize;

            int i0 = Mathf.CeilToInt((b.min.x + v - tp.x) / hx);
            int j0 = Mathf.CeilToInt((b.min.z + v - tp.z) / hz);
            int i1 = Mathf.FloorToInt((b.max.x - v - tp.x) / hx);
            int j1 = Mathf.FloorToInt((b.max.z - v - tp.z) / hz);
            i0 = Mathf.Clamp(i0, 0, res); i1 = Mathf.Clamp(i1, 0, res);
            j0 = Mathf.Clamp(j0, 0, res); j1 = Mathf.Clamp(j1, 0, res);

            cells = new RectInt(i0, j0, i1 - i0, j1 - j0);
            world = new Vector4(tp.x + i0 * hx, tp.z + j0 * hz, tp.x + i1 * hx, tp.z + j1 * hz);
            int border = Mathf.Max(2, zone.borderVoxels);
            error = null;
            if (cells.width <= 0 || cells.height <= 0)
            {
                error = "The zone is too small to cut a terrain hole. Make it larger than a few terrain hole cells.";
                return false;
            }
            float narrow = Mathf.Min(world.z - world.x, world.w - world.y);
            if (narrow <= v * (2 * border + 2))
            {
                int fits = Mathf.Max(0, Mathf.FloorToInt(narrow / v * 0.5f) - 2);
                error = fits >= 2
                    ? $"Border Voxels ({zone.borderVoxels}) is too large for this zone: at most {fits} fits. Set it to 2-3."
                    : "The zone is too narrow: make it wider than about 7 voxels in X and Z.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Editable samples: at least <c>borderVoxels</c> inside the hole so every changed cell stays visible
        /// through it, above the bedrock floor, and below the top sample so the surface can always close.
        /// </summary>
        private static int[] ComputeEditBox(DigZone zone, Vector4 holeWorld)
        {
            Vector3 o = zone.transform.position;
            float v = zone.voxelSize;
            int border = Mathf.Max(2, zone.borderVoxels);
            return new[]
            {
                Mathf.CeilToInt((holeWorld.x - o.x) / v) + border,
                border,
                Mathf.CeilToInt((holeWorld.y - o.z) / v) + border,
                Mathf.FloorToInt((holeWorld.z - o.x) / v) - border,
                zone.cells.y - 1,
                Mathf.FloorToInt((holeWorld.w - o.z) / v) - border,
            };
        }

        private static void BakeGrid(DigZone zone, DigZoneData data, out float minH, out float maxH)
        {
            byte[] grid = SampleGrid(zone, data.nx, data.ny, data.nz, out minH, out maxH, out int clipped);
            if (clipped > 0)
                Debug.LogWarning($"[DigHoleIt] Terrain leaves the zone's vertical range at {clipped} columns. Use Fit To Terrain.", zone);

            data.baseGrid = grid;
            data.grid = (byte[])grid.Clone();
        }

        /// <summary>
        /// The zone's grid as the plain terrain (no sculpting). <paramref name="clipped"/> counts the columns whose
        /// terrain height leaves the zone's vertical range.
        /// </summary>
        private static byte[] SampleGrid(DigZone zone, int nx, int ny, int nz, out float minH, out float maxH, out int clipped)
        {
            Terrain t = zone.terrain;
            TerrainData td = t.terrainData;
            Vector3 tp = t.transform.position;
            Vector3 o = zone.transform.position;
            float v = zone.voxelSize;
            int sx = nx + 1, sxy = sx * (ny + 1);
            var grid = new byte[sxy * (nz + 1)];

            minH = float.MaxValue;
            maxH = float.MinValue;
            clipped = 0;

            for (int z = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++)
            {
                float wx = o.x + x * v, wz = o.z + z * v;
                float u = Mathf.Clamp01((wx - tp.x) / td.size.x);
                float w = Mathf.Clamp01((wz - tp.z) / td.size.z);
                float h = td.GetInterpolatedHeight(u, w) + tp.y;
                float ny01 = Mathf.Max(0.2f, td.GetInterpolatedNormal(u, w).y);
                minH = Mathf.Min(minH, h);
                maxH = Mathf.Max(maxH, h);
                if (h >= o.y + (ny - 1) * v || h <= o.y + v) clipped++;

                for (int y = 0; y <= ny; y++)
                {
                    byte b;
                    if (y == 0) b = DigFormat.SolidByte;
                    else if (y == ny) b = DigFormat.AirByte;
                    else b = DigFormat.Quantize((o.y + y * v - h) * ny01 / v);
                    grid[x + sx * y + sxy * z] = b;
                }
            }

            return grid;
        }

        /// <summary>
        /// The previous bake, used to carry sculpted samples and paint into a re-bake on the same voxel lattice.
        /// Edits that end up outside the new zone's diggable area go to the data's stash (keyed by lattice position)
        /// instead of being dropped, so shrinking a zone and growing it back restores them.
        /// </summary>
        private sealed class OldGrid
        {
            private const int FlagShape = 1, FlagPaint = 2;

            private readonly byte[] _grid, _base, _paint;
            private readonly int _nx, _ny;
            private readonly int[] _edit;
            private readonly Vector3 _origin;
            private readonly Vector3 _anchor;
            private readonly long[] _stashKeys;
            private readonly byte[] _stashGrid, _stashPaint, _stashFlags;

            public OldGrid(DigZoneData data, DigZone zone)
            {
                _grid = data.grid;
                _base = data.baseGrid != null && data.baseGrid.Length == data.grid.Length ? data.baseGrid : null;
                _paint = data.HasPaintGrid ? data.paint : null;
                _nx = data.nx;
                _ny = data.ny;
                _edit = (int[])data.editBox.Clone();
                // Bakes from before 0.2 did not record the origin; the zone has not moved since (no handles back then).
                _origin = data.hasOrigin ? data.origin : zone.transform.position;
                _anchor = data.hasLatticeAnchor ? data.latticeAnchor : _origin;
                if (data.HasStash)
                {
                    _stashKeys = data.stashKeys;
                    _stashGrid = data.stashGrid;
                    _stashPaint = data.stashPaint;
                    _stashFlags = data.stashFlags;
                }
            }

            private static long Key(int x, int y, int z) =>
                ((long)(x + (1 << 20)) & 0x1FFFFF) | (((long)(y + (1 << 20)) & 0x1FFFFF) << 21) | (((long)(z + (1 << 20)) & 0x1FFFFF) << 42);

            private Vector3Int LatticeOffset(Vector3 origin, float v) => Vector3Int.RoundToInt((origin - _anchor) / v);

            public void CopyInto(DigZoneData data)
            {
                float v = data.voxelSize;
                var edits = new Dictionary<long, (byte grid, byte paint, byte flags)>();

                // 1. Everything stashed by earlier bakes.
                if (_stashKeys != null)
                    for (int k = 0; k < _stashKeys.Length; k++)
                        edits[_stashKeys[k]] = (_stashGrid[k], _stashPaint[k], _stashFlags[k]);

                // 2. Every edited sample of the previous bake (newer, so it wins over the stash).
                Vector3Int o = LatticeOffset(_origin, v);
                Vector3Int n = LatticeOffset(data.origin, v);
                int sx = data.nx + 1, sxy = sx * (data.ny + 1);
                int osx = _nx + 1, osxy = osx * (_ny + 1);
                for (int z = _edit[2]; z <= _edit[5]; z++)
                for (int y = _edit[1]; y <= _edit[4]; y++)
                for (int x = _edit[0]; x <= _edit[3]; x++)
                {
                    int oi = x + osx * y + osxy * z;
                    byte baseValue;
                    if (_base != null) baseValue = _base[oi];
                    else
                    {
                        // Without a stored base, compare with the fresh terrain sample where the new grid covers it.
                        int nx = x + o.x - n.x, ny = y + o.y - n.y, nz = z + o.z - n.z;
                        if (nx < 0 || ny < 0 || nz < 0 || nx > data.nx || ny > data.ny || nz > data.nz) continue;
                        baseValue = data.baseGrid[nx + sx * ny + sxy * nz];
                    }
                    byte flags = (byte)((_grid[oi] != baseValue ? FlagShape : 0) | (_paint != null && _paint[oi] != 0 ? FlagPaint : 0));
                    long key = Key(x + o.x, y + o.y, z + o.z);
                    if (flags != 0) edits[key] = (_grid[oi], _paint != null ? _paint[oi] : (byte)0, flags);
                    else edits.Remove(key); // reset back to the terrain since it was stashed
                }

                // 3. Put back what the new diggable area covers; the rest stays stashed.
                int[] e = data.editBox;
                byte[] paint = null;
                int kept = 0;
                for (int z = e[2]; z <= e[5]; z++)
                for (int y = e[1]; y <= e[4]; y++)
                for (int x = e[0]; x <= e[3]; x++)
                {
                    long key = Key(x + n.x, y + n.y, z + n.z);
                    if (!edits.TryGetValue(key, out var ed)) continue;
                    edits.Remove(key);
                    int i = x + sx * y + sxy * z;
                    if ((ed.flags & FlagShape) != 0) data.grid[i] = ed.grid;
                    if ((ed.flags & FlagPaint) != 0)
                    {
                        paint ??= new byte[data.SampleCount];
                        paint[i] = ed.paint;
                    }
                    kept++;
                }
                data.paint = paint;

                data.latticeAnchor = _anchor;
                data.hasLatticeAnchor = true;
                data.stashKeys = new long[edits.Count];
                data.stashGrid = new byte[edits.Count];
                data.stashPaint = new byte[edits.Count];
                data.stashFlags = new byte[edits.Count];
                int s = 0;
                foreach (KeyValuePair<long, (byte grid, byte paint, byte flags)> kv in edits)
                {
                    data.stashKeys[s] = kv.Key;
                    data.stashGrid[s] = kv.Value.grid;
                    data.stashPaint[s] = kv.Value.paint;
                    data.stashFlags[s] = kv.Value.flags;
                    s++;
                }

                if (kept > 0 || edits.Count > 0)
                    Debug.Log($"[DigHoleIt] Kept {kept} edited samples in the zone; {edits.Count} outside its diggable area are stored and come back if the zone covers them again.");
            }
        }

        // ---------------------------------------------------------------- Terrain sync

        /// <summary>Whether the zone's settings and position still match its last bake.</summary>
        public static bool IsUpToDate(DigZone zone)
        {
            DigZoneData d = zone.data;
            if (d == null || !d.HasGrid) return false;
            Vector3Int c = zone.cells;
            bool moved = d.hasOrigin && (zone.transform.position - d.origin).sqrMagnitude > 1e-6f;
            return !moved && d.nx == c.x && d.ny == c.y && d.nz == c.z &&
                   Mathf.Approximately(d.voxelSize, zone.voxelSize) && d.chunkCells == zone.chunkCells;
        }

        /// <summary>
        /// Follows an edit of the zone's terrain. Heights: samples that were never sculpted take the new terrain,
        /// sculpted ones stay as they are, and the chunks are remeshed. If the terrain now leaves the zone's vertical
        /// range, the zone is fitted and re-baked (keeping the sculpting). Textures: the terrain layer blend and the
        /// layer settings are copied again. Zones whose settings changed since their last bake are skipped.
        /// </summary>
        /// <returns>True if the zone changed.</returns>
        public static bool SyncWithTerrain(DigZone zone, bool heights, bool textures)
        {
            DigZoneData data = zone.data;
            if (data == null || !data.HasGrid || zone.terrain == null || zone.terrain.terrainData == null) return false;
            if (!IsUpToDate(zone)) return false;

            bool wasBusy = Busy;
            Busy = true;
            try
            {
                bool changed = false;
                if (heights)
                {
                    byte[] fresh = SampleGrid(zone, data.nx, data.ny, data.nz, out float minH, out float maxH, out int clipped);
                    byte[] old = data.baseGrid;
                    bool same = old != null && old.Length == fresh.Length;
                    for (int i = 0; same && i < fresh.Length; i++) same = old[i] == fresh[i];
                    if (!same)
                    {
                        bool noBase = old == null || old.Length != fresh.Length;
                        if (clipped > 0 || noBase)
                        {
                            // Needs a taller zone (or a bake from before 0.2 without a stored base). The full re-bake
                            // keeps the sculpting, since fitting stays on the voxel lattice.
                            Vector3 position = zone.transform.position;
                            int cellsY = zone.cells.y;
                            FitToTerrain(zone);
                            if (noBase || zone.cells.y != cellsY || zone.transform.position != position)
                            {
                                Debug.Log($"[DigHoleIt] The terrain under '{zone.name}' left its box; fitted and re-baked it.", zone);
                                return Bake(zone, true);
                            }
                        }

                        byte[] grid = data.grid;
                        for (int i = 0; i < grid.Length; i++)
                            if (grid[i] == old[i]) grid[i] = fresh[i];
                        data.baseGrid = fresh;
                        data.gridVersion++;
                        DigSculptUndo.MarkSeen(data);
                        BakeHeight(zone, data, minH, maxH);
                        RemeshAll(zone);
                        changed = true;
                    }
                }

                if (textures)
                {
                    Color32[] before = ReadPixels(data.controlTex);
                    BakeControl(zone, data, zone.terrain.terrainData);
                    Color32[] after = ReadPixels(data.controlTex);
                    bool same = before != null && after != null && before.Length == after.Length;
                    for (int i = 0; same && i < before.Length; i++)
                        same = before[i].r == after[i].r && before[i].g == after[i].g && before[i].b == after[i].b && before[i].a == after[i].a;
                    changed |= !same;
                }

                if (!changed) return false;
                EditorUtility.SetDirty(data);
                ApplyMaterial(zone, data, zone.material, false);
                NotifyGridChanged(zone);
                return true;
            }
            finally
            {
                Busy = wasBusy;
            }
        }

        private static Color32[] ReadPixels(Texture2D tex)
        {
            if (tex == null || !tex.isReadable) return null;
            try { return tex.GetPixels32(); }
            catch (UnityException) { return null; }
        }

        // ---------------------------------------------------------------- Terrain holes

        private static void CutTerrain(DigZoneData data, TerrainData td, RectInt cells)
        {
            Undo.RegisterCompleteObjectUndo(td, "Cut Terrain Hole");
            bool[,] holes = td.GetHoles(cells.x, cells.y, cells.width, cells.height);
            var previous = new bool[cells.width * cells.height];
            for (int j = 0; j < cells.height; j++)
            for (int i = 0; i < cells.width; i++)
            {
                previous[i + j * cells.width] = holes[j, i];
                holes[j, i] = false; // false = hole
            }
            td.SetHoles(cells.x, cells.y, holes);

            data.cutTerrain = td;
            data.cutRect = cells;
            data.cutPrevious = previous;
            EditorUtility.SetDirty(td);
        }

        /// <summary>Puts back the terrain surface this zone removed.</summary>
        public static void RestoreTerrainCut(DigZoneData data) => DigTerrainHoles.RestoreCut(data);

        /// <summary>
        /// A duplicated zone (Ctrl+D) shares its data and material with the original. Give it its own copies before
        /// baking, or the two would overwrite each other's grid, textures and terrain hole records.
        /// </summary>
        private static void MakeDataUnique(DigZone zone)
        {
            if (zone.data == null) return;
            foreach (DigZone other in UnityEngine.Object.FindObjectsByType<DigZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (other == zone || other.data != zone.data) continue;

                string src = AssetDatabase.GetAssetPath(zone.data);
                string dir = Path.GetDirectoryName(src)?.Replace('\\', '/');
                string scene = string.IsNullOrEmpty(zone.gameObject.scene.name) ? "Scene" : zone.gameObject.scene.name;
                string dst = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{scene}_{zone.name}.asset");
                if (!AssetDatabase.CopyAsset(src, dst)) return;
                var copy = AssetDatabase.LoadAssetAtPath<DigZoneData>(dst);
                // The hole belongs to the original zone.
                copy.cutTerrain = null;
                copy.cutPrevious = null;
                copy.cutRect = default;
                EditorUtility.SetDirty(copy);

                Undo.RecordObject(zone, "Bake Dig Zone");
                zone.data = copy;
                if (zone.material != null && other.material == zone.material)
                {
                    string matSrc = AssetDatabase.GetAssetPath(zone.material);
                    string matDst = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{copy.name}_Material.mat");
                    if (!string.IsNullOrEmpty(matSrc) && AssetDatabase.CopyAsset(matSrc, matDst))
                        zone.material = AssetDatabase.LoadAssetAtPath<Material>(matDst);
                }
                Debug.Log($"[DigHoleIt] '{zone.name}' shared its data with '{other.name}'; it now has its own copy ({dst}).", zone);
                return;
            }
        }

        // ---------------------------------------------------------------- Shader textures

        private static void BakeControl(DigZone zone, DigZoneData data, TerrainData td)
        {
            Vector3 tp = zone.terrain.transform.position;
            Bounds b = zone.WorldBounds;
            int res = td.alphamapResolution;
            int layers = td.alphamapLayers;

            // The terrain shader maps uv 0..1 onto texel centres 0..res-1, so a texel i sits at u = i / (res - 1).
            float scaleX = (res - 1) / td.size.x;
            float scaleZ = (res - 1) / td.size.z;
            int ax0 = Mathf.Clamp(Mathf.FloorToInt((b.min.x - tp.x) * scaleX) - 1, 0, res - 1);
            int az0 = Mathf.Clamp(Mathf.FloorToInt((b.min.z - tp.z) * scaleZ) - 1, 0, res - 1);
            int ax1 = Mathf.Clamp(Mathf.CeilToInt((b.max.x - tp.x) * scaleX) + 1, 0, res - 1);
            int az1 = Mathf.Clamp(Mathf.CeilToInt((b.max.z - tp.z) * scaleZ) + 1, 0, res - 1);
            int w = ax1 - ax0 + 1, h = az1 - az0 + 1;

            var colors = new Color[w * h];
            if (layers > 0)
            {
                float[,,] maps = td.GetAlphamaps(ax0, az0, w, h);
                for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    colors[i + j * w] = new Color(
                        maps[j, i, 0],
                        layers > 1 ? maps[j, i, 1] : 0f,
                        layers > 2 ? maps[j, i, 2] : 0f,
                        layers > 3 ? maps[j, i, 3] : 0f);
                }
            }
            else
            {
                for (int k = 0; k < colors.Length; k++) colors[k] = Color.red;
            }

            Texture2D tex = ReplaceSubTexture(data, data.controlTex, "Control", w, h, TextureFormat.RGBA32);
            tex.SetPixels(colors);
            tex.Apply(false, false);
            data.controlTex = tex;

            data.controlST = new Vector4(
                scaleX / w,
                scaleZ / h,
                (-tp.x * scaleX - ax0 + 0.5f) / w,
                (-tp.z * scaleZ - az0 + 0.5f) / h);
        }

        private static void BakeHeight(DigZone zone, DigZoneData data, float minH, float maxH)
        {
            Terrain t = zone.terrain;
            TerrainData td = t.terrainData;
            Vector3 tp = t.transform.position;
            Vector3 o = zone.transform.position;
            float v = zone.voxelSize;
            int w = data.nx + 1, h = data.nz + 1;
            float range = Mathf.Max(0.001f, maxH - minH);

            var colors = new Color[w * h];
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float u = Mathf.Clamp01((o.x + x * v - tp.x) / td.size.x);
                float q = Mathf.Clamp01((o.z + z * v - tp.z) / td.size.z);
                float height = td.GetInterpolatedHeight(u, q) + tp.y;
                colors[x + z * w] = new Color((height - minH) / range, 0f, 0f, 1f);
            }

            Texture2D tex = ReplaceSubTexture(data, data.heightTex, "Height", w, h, TextureFormat.RHalf);
            tex.SetPixels(colors);
            tex.Apply(false, false);
            data.heightTex = tex;
            data.heightRange = new Vector2(minH, maxH);
            data.heightST = new Vector4(1f / (v * w), 1f / (v * h), (-o.x / v + 0.5f) / w, (-o.z / v + 0.5f) / h);
        }

        private static Texture2D ReplaceSubTexture(DigZoneData data, Texture2D old, string label, int w, int h, TextureFormat format)
        {
            if (old != null && old.width == w && old.height == h && old.format == format) return old;
            if (old != null) UnityEngine.Object.DestroyImmediate(old, true);

            var tex = new Texture2D(w, h, format, false, true)
            {
                name = data.name + "_" + label,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            AssetDatabase.AddObjectToAsset(tex, data);
            return tex;
        }

        // ---------------------------------------------------------------- Material

        private static void EnsureMaterial(DigZone zone, DigZoneData data)
        {
            if (zone.material == null)
            {
                Shader shader = Shader.Find(DefaultShader);
                if (shader == null) { Debug.LogError("[DigHoleIt] Shader '" + DefaultShader + "' not found.", zone); return; }
                var mat = new Material(shader) { name = data.name + "_Material" };
                string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(data))?.Replace('\\', '/');
                string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{data.name}_Material.mat");
                AssetDatabase.CreateAsset(mat, path);
                Undo.RecordObject(zone, "Create Dig Zone Material");
                zone.material = mat;
            }
            ApplyMaterial(zone, data, zone.material);
        }

        /// <summary>Copies the terrain layers and the zone's baked shading data onto <paramref name="mat"/>.</summary>
        public static void ApplyMaterial(DigZone zone, DigZoneData data, Material mat) => ApplyMaterial(zone, data, mat, true);

        private static void ApplyMaterial(DigZone zone, DigZoneData data, Material mat, bool recordUndo)
        {
            if (mat == null || data == null) return;
            if (recordUndo) Undo.RecordObject(mat, "Apply Dig Zone Material");

            Terrain t = zone.terrain;
            TerrainLayer[] layers = t != null && t.terrainData != null ? t.terrainData.terrainLayers : Array.Empty<TerrainLayer>();
            for (int i = 0; i < 4; i++)
            {
                TerrainLayer l = i < layers.Length ? layers[i] : null;
                string n = i.ToString();
                if (mat.HasProperty("_Splat" + n)) mat.SetTexture("_Splat" + n, l != null ? l.diffuseTexture : null);
                if (mat.HasProperty("_Normal" + n)) mat.SetTexture("_Normal" + n, l != null ? l.normalMapTexture : null);
                if (l != null)
                {
                    Vector2 size = new Vector2(Mathf.Max(0.001f, l.tileSize.x), Mathf.Max(0.001f, l.tileSize.y));
                    mat.SetVector("_Splat" + n + "_ST", new Vector4(1f / size.x, 1f / size.y, l.tileOffset.x / size.x, l.tileOffset.y / size.y));
                    mat.SetFloat("_Metallic" + n, l.metallic);
                    // Unity terrain takes smoothness from the albedo alpha when the texture has one, else from the slider.
                    bool alphaSmooth = l.diffuseTexture != null && GraphicsFormatUtility.HasAlphaChannel(l.diffuseTexture.graphicsFormat);
                    mat.SetFloat("_Smoothness" + n, alphaSmooth ? 1f : l.smoothness);
                    mat.SetFloat("_NormalScale" + n, l.normalScale);
                    mat.SetColor("_Tint" + n, (Color)l.diffuseRemapMax);
                }
                else
                {
                    mat.SetVector("_Splat" + n + "_ST", new Vector4(0.1f, 0.1f, 0f, 0f));
                    mat.SetColor("_Tint" + n, Color.white);
                }
            }

            Vector3 tp = t != null ? t.transform.position : Vector3.zero;
            mat.SetTexture("_Control", data.controlTex);
            mat.SetVector("_ControlST", data.controlST);
            mat.SetTexture("_HeightTex", data.heightTex);
            mat.SetVector("_HeightST", data.heightST);
            mat.SetVector("_HeightRange", new Vector4(data.heightRange.x, data.heightRange.y, 0f, 0f));
            mat.SetVector("_HoleRect", data.holeRect);
            mat.SetVector("_TerrainPos", new Vector4(tp.x, tp.y, tp.z, 0f));
            EditorUtility.SetDirty(mat);
        }

        // ---------------------------------------------------------------- Chunks

        private static void BuildChunks(DigZone zone, DigZoneData data)
        {
            int count = data.ChunkCount;

            if (zone.chunkRoot == null)
            {
                var root = new GameObject("Chunks");
                Undo.RegisterCreatedObjectUndo(root, "Bake Dig Zone");
                root.transform.SetParent(zone.transform, false);
                zone.chunkRoot = root.transform;
            }
            zone.chunkRoot.localPosition = Vector3.zero;
            zone.chunkRoot.localRotation = Quaternion.identity;
            zone.chunkRoot.localScale = Vector3.one;

            for (int i = zone.chunkRoot.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(zone.chunkRoot.GetChild(i).gameObject);

            // Reuse mesh sub-assets where possible so references elsewhere stay valid.
            var oldMeshes = new List<Mesh>();
            if (data.chunkMeshes != null) foreach (Mesh m in data.chunkMeshes) if (m != null) oldMeshes.Add(m);
            var meshes = new Mesh[count];
            for (int i = 0; i < count; i++)
            {
                if (i < oldMeshes.Count) { meshes[i] = oldMeshes[i]; continue; }
                meshes[i] = new Mesh();
                AssetDatabase.AddObjectToAsset(meshes[i], data);
            }
            for (int i = count; i < oldMeshes.Count; i++) UnityEngine.Object.DestroyImmediate(oldMeshes[i], true);
            data.chunkMeshes = meshes;

            zone.chunkFilters = new MeshFilter[count];
            zone.chunkRenderers = new MeshRenderer[count];
            zone.chunkColliders = new MeshCollider[count];

            var mesher = new ChunkMesher(data.chunkCells);
            float chunkSize = data.chunkCells * data.voxelSize;

            for (int cz = 0; cz < data.ChunksZ; cz++)
            for (int cy = 0; cy < data.ChunksY; cy++)
            for (int cx = 0; cx < data.ChunksX; cx++)
            {
                int ci = data.ChunkIndex(cx, cy, cz);
                var go = new GameObject($"Chunk_{cx}_{cy}_{cz}") { layer = zone.chunkLayer };
                Undo.RegisterCreatedObjectUndo(go, "Bake Dig Zone");
                go.transform.SetParent(zone.chunkRoot, false);
                go.transform.localPosition = new Vector3(cx, cy, cz) * chunkSize;

                Mesh mesh = meshes[ci];
                mesh.name = $"{data.name}_Chunk_{cx}_{cy}_{cz}";

                var filter = go.AddComponent<MeshFilter>();
                var rend = go.AddComponent<MeshRenderer>();
                var col = go.AddComponent<MeshCollider>();
                filter.sharedMesh = mesh;
                rend.sharedMaterial = zone.material;
                rend.shadowCastingMode = ShadowCastingMode.On;
                rend.lightProbeUsage = LightProbeUsage.BlendProbes;
                GameObjectUtility.SetStaticEditorFlags(go, 0); // meshes change at runtime

                zone.chunkFilters[ci] = filter;
                zone.chunkRenderers[ci] = rend;
                zone.chunkColliders[ci] = col;

                RebuildChunk(zone, data, mesher, cx, cy, cz);

                if (ci % 16 == 0)
                    EditorUtility.DisplayProgressBar("DigHoleIt", "Meshing chunks", 0.6f + 0.4f * ci / count);
            }
        }

        private static void RebuildChunk(DigZone zone, DigZoneData data, ChunkMesher mesher, int cx, int cy, int cz)
        {
            int ci = data.ChunkIndex(cx, cy, cz);
            Mesh mesh = data.chunkMeshes[ci];
            mesher.Build(data, data.grid, data.HasPaintGrid ? data.paint : null, cx, cy, cz);
            bool has = mesher.WriteTo(mesh);
            if (has) mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            if (zone.chunkRenderers != null && ci < zone.chunkRenderers.Length && zone.chunkRenderers[ci] != null)
                zone.chunkRenderers[ci].enabled = has;
            if (zone.chunkColliders != null && ci < zone.chunkColliders.Length && zone.chunkColliders[ci] != null)
            {
                zone.chunkColliders[ci].sharedMesh = null;
                if (has) zone.chunkColliders[ci].sharedMesh = mesh;
            }
        }

        /// <summary>Remeshes the chunks touched by a changed sample box {minX, minY, minZ, maxX, maxY, maxZ}.</summary>
        public static void RemeshRange(DigZone zone, int[] changed)
        {
            DigZoneData data = zone.data;
            if (data == null || !data.HasGrid || data.chunkMeshes == null || data.chunkMeshes.Length != data.ChunkCount) return;

            DigFormat.AffectedChunks(changed[0], changed[3], data.chunkCells, data.ChunksX, out int cx0, out int cx1);
            DigFormat.AffectedChunks(changed[1], changed[4], data.chunkCells, data.ChunksY, out int cy0, out int cy1);
            DigFormat.AffectedChunks(changed[2], changed[5], data.chunkCells, data.ChunksZ, out int cz0, out int cz1);

            var mesher = new ChunkMesher(data.chunkCells);
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
                RebuildChunk(zone, data, mesher, cx, cy, cz);
        }

        public static void RemeshAll(DigZone zone)
        {
            DigZoneData data = zone.data;
            if (data == null) return;
            RemeshRange(zone, new[] { 0, 0, 0, data.nx, data.ny, data.nz });
        }

        /// <summary>Removes chunk objects and restores the terrain. The data asset is kept.</summary>
        public static void Clear(DigZone zone)
        {
            if (zone.data != null) RestoreTerrainCut(zone.data);
            if (zone.chunkRoot != null) Undo.DestroyObjectImmediate(zone.chunkRoot.gameObject);
            Undo.RecordObject(zone, "Clear Dig Zone");
            zone.chunkRoot = null;
            zone.chunkFilters = null;
            zone.chunkRenderers = null;
            zone.chunkColliders = null;
            EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
        }
    }
}
