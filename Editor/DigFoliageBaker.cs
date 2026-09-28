using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// Terrain trees and details inside a Dig Zone. The terrain hides both in its hole, so the zone shows them itself:
    /// trees as copies of their prefabs, details as one merged mesh per chunk column (DigHoleIt/DigDetail shader).
    /// The foliage mask (<see cref="DigFoliage"/>) takes them away where the ground under them was dug away or built
    /// over. All of it is derived from the terrain and rebuilt when the terrain changes, so none of it records undo.
    /// </summary>
    public static class DigFoliageBaker
    {
        public const string DetailShader = "DigHoleIt/DigDetail";
        private const string RootName = "Trees And Details";

        /// <summary>Raised after a zone's trees and details were rebuilt (runtimes copy the new objects).</summary>
        public static event Action<DigZone> Built;

        private struct TreeSpot
        {
            public int Prototype;
            public Vector3 Position;
            public float Rotation;
            public float Width;
            public float Height;
            public Vector3 Up;
            public Vector3 Anchor;
            public int Bucket;
        }

        private struct DetailSpot
        {
            public int Prototype;
            public Vector3 Root; // zone local
            public float Rotation;
            public float Width;
            public float Height;
            public int Column;
            /// <summary>Which way it grows (zero: straight up, the terrain's details).</summary>
            public Vector3 Normal;
            /// <summary>Its own foliage mask texel (a surface detail), or -1 for its column's.</summary>
            public int Texel;
        }

        /// <summary>A surface detail (DigZoneData.surfaceDetails) with its anchor and the chunk that checks it.</summary>
        private struct SurfaceSpot
        {
            public DetailSpot Spot;
            public Vector3 Anchor;
            public int Bucket;
        }

        public static bool HasFoliage(DigZone zone) =>
            zone.foliageRoot != null || zone.data != null && zone.data.foliageMask != null;

        // ---------------------------------------------------------------- Build

        /// <summary>
        /// Rebuilds the zone's trees, details and foliage mask. Details grow on the top surface of the grid as it is now
        /// (so also on the floor of a pit dug in the editor), trees stand where they were placed.
        /// </summary>
        /// <param name="recordUndo">
        /// Record the replaced and new objects, as part of an undoable operation (a bake). Rebuilds that follow the
        /// terrain are derived data and are redone after undo instead.
        /// </param>
        public static void Build(DigZone zone, bool recordUndo = false)
        {
            DigZoneData data = zone.data;
            Terrain terrain = zone.terrain;
            ClearObjects(zone, recordUndo);
            if (data == null) return;
            if (!data.HasGrid || terrain == null || terrain.terrainData == null || !ValidHole(data))
            {
                ClearData(data);
                Built?.Invoke(zone);
                return;
            }

            TerrainData td = terrain.terrainData;
            float[] anchors = TopSurfaces(data);
            List<TreeSpot> trees = zone.trees ? CollectTrees(zone, data, terrain) : new List<TreeSpot>();
            List<DetailSpot>[] details = zone.details ? CollectDetails(zone, data, terrain, anchors) : null;
            List<SurfaceSpot> surface = details != null ? CollectSurfaceDetails(zone, data, terrain) : new List<SurfaceSpot>();
            int texel0 = (data.nx + 1) * (data.nz + 1);
            for (int k = 0; k < surface.Count; k++)
            {
                DetailSpot spot = surface[k].Spot;
                spot.Texel = texel0 + k;
                details[spot.Prototype].Add(spot);
            }
            bool anyDetails = false;
            if (details != null) foreach (List<DetailSpot> l in details) anyDetails |= l.Count > 0;

            data.foliageSignature = Signature(zone);
            data.foliageDetailSignature = Signature(zone, false);
            data.foliageCount = 0;
            if (trees.Count == 0 && !anyDetails)
            {
                ClearData(data);
                EditorUtility.SetDirty(data);
                Built?.Invoke(zone);
                return;
            }

            WriteMask(EnsureMask(data, surface.Count), anchors, data, surface);

            var root = new GameObject(RootName);
            root.transform.SetParent(zone.transform, false);
            if (recordUndo) Undo.RegisterCreatedObjectUndo(root, "Bake Dig Zone");
            zone.foliageRoot = root.transform;
            BuildTrees(zone, data, td, trees, root.transform);
            BuildDetails(zone, data, terrain, anyDetails ? details : null, root.transform);
            SetSurfaceAnchors(zone, data, surface);
            data.foliageCount = zone.treeObjects.Length + zone.detailRenderers.Length;

            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(zone);
            EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
            Built?.Invoke(zone);
        }

        private static bool ValidHole(DigZoneData d) => d.holeRect.z > d.holeRect.x && d.holeRect.w > d.holeRect.y;

        private static bool InHole(DigZoneData d, Vector3 world) =>
            world.x >= d.holeRect.x && world.x < d.holeRect.z && world.z >= d.holeRect.y && world.z < d.holeRect.w;

        private static Vector3 Origin(DigZone zone) => zone.data.hasOrigin ? zone.data.origin : zone.transform.position;

        private static void ClearObjects(DigZone zone, bool recordUndo)
        {
            if (recordUndo) Undo.RecordObject(zone, "Bake Dig Zone");
            for (int guard = 0; guard < 8; guard++)
            {
                Transform old = zone.foliageRoot != null ? zone.foliageRoot : zone.transform.Find(RootName);
                if (old == null) break;
                if (recordUndo) Undo.DestroyObjectImmediate(old.gameObject);
                else Object.DestroyImmediate(old.gameObject);
                zone.foliageRoot = null;
            }
            zone.foliageRoot = null;
            zone.treeObjects = null;
            zone.treeAnchors = null;
            zone.treeBuckets = null;
            zone.detailRenderers = null;
            zone.surfaceDetailAnchors = null;
            zone.surfaceDetailBuckets = null;
        }

        private static void ClearData(DigZoneData data)
        {
            if (data == null) return;
            if (data.foliageMask != null) Object.DestroyImmediate(data.foliageMask, true);
            if (data.detailMeshes != null)
                foreach (Mesh m in data.detailMeshes)
                    if (m != null) Object.DestroyImmediate(m, true);
            if (data.detailMaterials != null)
                foreach (Material m in data.detailMaterials)
                    if (m != null) Object.DestroyImmediate(m, true);
            data.foliageMask = null;
            data.detailMeshes = null;
            data.detailMaterials = null;
            data.foliageCount = 0;
        }

        // ---------------------------------------------------------------- Anchors and foliage mask

        /// <summary>Top surface height (grid units, -1 for none) of every grid column, x fastest.</summary>
        private static float[] TopSurfaces(DigZoneData d)
        {
            int w = d.nx + 1;
            var top = new float[w * (d.nz + 1)];
            for (int z = 0; z <= d.nz; z++)
            for (int x = 0; x <= d.nx; x++)
                top[x + w * z] = DigFoliage.TopSurface(d.grid, d.nx, d.ny, x, z);
            return top;
        }

        /// <summary>
        /// Height of the top surface at grid-space (x, z), blended from the four columns around it (grid units), or -1
        /// where none of them has a surface.
        /// </summary>
        private static float SurfaceAt(float[] top, DigZoneData d, float gx, float gz)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, d.nx), z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, d.nz);
            int x1 = Mathf.Min(x0 + 1, d.nx), z1 = Mathf.Min(z0 + 1, d.nz);
            float fx = Mathf.Clamp01(gx - x0), fz = Mathf.Clamp01(gz - z0);
            int w = d.nx + 1;
            float sum = 0f, weight = 0f;
            void Add(int x, int z, float k)
            {
                float h = top[x + w * z];
                if (h < 0f || k <= 0f) return;
                sum += h * k;
                weight += k;
            }
            Add(x0, z0, (1f - fx) * (1f - fz));
            Add(x1, z0, fx * (1f - fz));
            Add(x0, z1, (1f - fx) * fz);
            Add(x1, z1, fx * fz);
            return weight > 0f ? sum / weight : -1f;
        }

        /// <summary>Rows of the foliage mask: the grid columns, then one texel per surface detail.</summary>
        public static int MaskHeight(DigZoneData d, int surfaceDetails) =>
            d.nz + 1 + (surfaceDetails + d.nx) / (d.nx + 1);

        private static Texture2D EnsureMask(DigZoneData d, int surfaceDetails)
        {
            int w = d.nx + 1, h = MaskHeight(d, surfaceDetails);
            Texture2D t = d.foliageMask;
            if (t != null && t.width == w && t.height == h && t.format == TextureFormat.RGBA32) return t;
            if (t != null) Object.DestroyImmediate(t, true);
            t = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
            {
                name = d.name + "_FoliageMask",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            AssetDatabase.AddObjectToAsset(t, d);
            d.foliageMask = t;
            return t;
        }

        /// <summary>Mask texel of a column: standing where it has a surface, which is its anchor.</summary>
        private static Color32 MaskTexel(float anchor)
        {
            if (anchor < 0f) return new Color32(DigFoliage.Removed, 0, 0, 0);
            int a = Mathf.Clamp(Mathf.RoundToInt(anchor * DigFoliage.AnchorScale), 0, 65535);
            return new Color32(DigFoliage.Standing, (byte)(a >> 8), (byte)(a & 255), 255);
        }

        /// <summary>Mask texel of a surface detail (its anchor is kept in DigZone.surfaceDetailAnchors).</summary>
        private static Color32 SurfaceTexel(bool stands) => new Color32(stands ? DigFoliage.Standing : DigFoliage.Removed, 0, 0, 255);

        private static float Anchor(Color32 texel) =>
            texel.a == 0 ? -1f : ((texel.g << 8) | texel.b) / DigFoliage.AnchorScale;

        private static void WriteMask(Texture2D t, float[] anchors, DigZoneData d, List<SurfaceSpot> surface)
        {
            var px = new Color32[t.width * t.height];
            for (int i = 0; i < anchors.Length; i++) px[i] = MaskTexel(anchors[i]);
            for (int k = 0; k < surface.Count; k++)
                px[anchors.Length + k] = SurfaceTexel(TreeStands(d, surface[k].Anchor));
            t.SetPixels32(px);
            t.Apply(false, false);
            EditorUtility.SetDirty(t);
        }

        /// <summary>
        /// Updates the foliage mask and the trees after the samples in <paramref name="changed"/> ({minX, minY, minZ,
        /// maxX, maxY, maxZ}) were sculpted: what stood where the ground was dug away or built over goes, and comes back
        /// if the surface returns to it (undo).
        /// </summary>
        public static void UpdateMask(DigZone zone, int[] changed)
        {
            DigZoneData d = zone.data;
            if (d == null || !d.HasGrid || d.foliageMask == null) return;
            Texture2D t = d.foliageMask;
            int surfaceCount = zone.surfaceDetailAnchors?.Length ?? 0;
            if (t.width != d.nx + 1 || t.height != MaskHeight(d, surfaceCount) || t.format != TextureFormat.RGBA32)
            {
                Build(zone);
                return;
            }

            int x0 = Mathf.Clamp(changed[0] - 1, 0, d.nx), x1 = Mathf.Clamp(changed[3] + 1, 0, d.nx);
            int z0 = Mathf.Clamp(changed[2] - 1, 0, d.nz), z1 = Mathf.Clamp(changed[5] + 1, 0, d.nz);
            if (x1 < x0 || z1 < z0) return;
            NativeArray<Color32> px = t.GetPixelData<Color32>(0);
            int w = d.nx + 1;
            bool any = ShowSurfaceDetails(zone, changed, px);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = x + w * z;
                Color32 c = px[i];
                float anchor = Anchor(c);
                if (anchor < 0f) continue;
                byte r = DigFoliage.GridStands(d.grid, d.nx, d.ny, x, z, anchor) ? DigFoliage.Standing : DigFoliage.Removed;
                if (c.r == r) continue;
                c.r = r;
                px[i] = c;
                any = true;
            }
            if (any)
            {
                t.Apply(false, false);
                EditorUtility.SetDirty(t);
            }
            if (ShowTrees(zone, changed) || any) EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
        }

        /// <summary>Shows the trees whose anchor chunk the changed samples reach, if they still stand. True if any changed.</summary>
        private static bool ShowTrees(DigZone zone, int[] changed)
        {
            GameObject[] trees = zone.treeObjects;
            Vector3[] anchors = zone.treeAnchors;
            int[] buckets = zone.treeBuckets;
            DigZoneData d = zone.data;
            if (trees == null || anchors == null || buckets == null || buckets.Length != d.ChunkCount + 1) return false;

            DigFormat.AffectedChunks(changed[0], changed[3], d.chunkCells, d.ChunksX, out int cx0, out int cx1);
            DigFormat.AffectedChunks(changed[1], changed[4], d.chunkCells, d.ChunksY, out int cy0, out int cy1);
            DigFormat.AffectedChunks(changed[2], changed[5], d.chunkCells, d.ChunksZ, out int cz0, out int cz1);
            bool any = false;
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                int ci = d.ChunkIndex(cx, cy, cz);
                for (int t = buckets[ci]; t < buckets[ci + 1] && t < trees.Length; t++)
                {
                    if (trees[t] == null) continue;
                    bool on = TreeStands(d, anchors[t]);
                    if (trees[t].activeSelf == on) continue;
                    trees[t].SetActive(on);
                    any = true;
                }
            }
            return any;
        }

        /// <summary>
        /// Updates the mask texels of the surface details whose anchor chunk the changed samples reach. True if any changed.
        /// </summary>
        private static bool ShowSurfaceDetails(DigZone zone, int[] changed, NativeArray<Color32> px)
        {
            Vector3[] anchors = zone.surfaceDetailAnchors;
            int[] buckets = zone.surfaceDetailBuckets;
            DigZoneData d = zone.data;
            if (anchors == null || anchors.Length == 0 || buckets == null || buckets.Length != d.ChunkCount + 1) return false;
            int texel0 = (d.nx + 1) * (d.nz + 1);

            DigFormat.AffectedChunks(changed[0], changed[3], d.chunkCells, d.ChunksX, out int cx0, out int cx1);
            DigFormat.AffectedChunks(changed[1], changed[4], d.chunkCells, d.ChunksY, out int cy0, out int cy1);
            DigFormat.AffectedChunks(changed[2], changed[5], d.chunkCells, d.ChunksZ, out int cz0, out int cz1);
            bool any = false;
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                int ci = d.ChunkIndex(cx, cy, cz);
                for (int k = buckets[ci]; k < buckets[ci + 1] && k < anchors.Length && texel0 + k < px.Length; k++)
                {
                    Color32 c = px[texel0 + k];
                    byte r = TreeStands(d, anchors[k]) ? DigFoliage.Standing : DigFoliage.Removed;
                    if (c.r == r) continue;
                    c.r = r;
                    px[texel0 + k] = c;
                    any = true;
                }
            }
            return any;
        }

        private static bool TreeStands(DigZoneData d, Vector3 anchor) =>
            DigFoliage.PointStands(d.grid, d.nx, d.ny, d.nz, anchor.x, anchor.y, anchor.z);

        /// <summary>
        /// The chunk whose samples decide whether a tree anchored at grid position <paramref name="a"/> stands (the
        /// runtimes check it after remeshing that chunk): the one holding the grid cell around it.
        /// </summary>
        public static int AnchorChunk(DigZoneData d, Vector3 a)
        {
            int x = DigFoliage.Cell(a.x, d.nx), y = DigFoliage.Cell(a.y, d.ny), z = DigFoliage.Cell(a.z, d.nz);
            int cc = d.chunkCells;
            return d.ChunkIndex(Mathf.Min(x / cc, d.ChunksX - 1), Mathf.Min(y / cc, d.ChunksY - 1), Mathf.Min(z / cc, d.ChunksZ - 1));
        }

        // ---------------------------------------------------------------- Trees

        /// <summary>
        /// Rebuilds only the zone's tree objects, after its kept trees changed (painting trees into the zone). Much
        /// cheaper than <see cref="Build"/> on zones full of details.
        /// </summary>
        public static void RebuildTrees(DigZone zone)
        {
            DigZoneData d = zone.data;
            if (d == null || !d.HasGrid || zone.foliageRoot == null || d.foliageMask == null || zone.terrain == null || zone.terrain.terrainData == null)
            {
                Build(zone);
                return;
            }

            Transform old = zone.foliageRoot.Find("Trees");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            List<TreeSpot> trees = zone.trees ? CollectTrees(zone, d, zone.terrain) : new List<TreeSpot>();
            if (trees.Count == 0 && (zone.detailRenderers == null || zone.detailRenderers.Length == 0))
            {
                Build(zone);
                return;
            }
            BuildTrees(zone, d, zone.terrain.terrainData, trees, zone.foliageRoot);
            d.foliageCount = zone.treeObjects.Length + (zone.detailRenderers?.Length ?? 0);
            d.foliageSignature = Signature(zone);
            d.foliageDetailSignature = Signature(zone, false);
            EditorUtility.SetDirty(d);
            EditorUtility.SetDirty(zone);
            EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
            Built?.Invoke(zone);
        }

        /// <summary>
        /// The trees the zone keeps (<see cref="DigZoneData.terrainTrees"/>) at their own height, sorted by anchor chunk.
        /// </summary>
        private static List<TreeSpot> CollectTrees(DigZone zone, DigZoneData d, Terrain terrain)
        {
            var list = new List<TreeSpot>();
            TerrainData td = terrain.terrainData;
            TreePrototype[] protos = td.treePrototypes;
            if (protos.Length == 0 || d.terrainTrees == null || d.terrainTrees.Length == 0) return list;

            Vector3 tp = terrain.transform.position;
            Vector3 origin = Origin(zone);
            foreach (DigTreeInstance ti in d.terrainTrees)
            {
                if (ti.prototypeIndex < 0 || ti.prototypeIndex >= protos.Length || protos[ti.prototypeIndex].prefab == null) continue;
                Vector3 w = tp + Vector3.Scale(ti.position, td.size);
                Vector3 anchor = (w - origin) / d.voxelSize;
                list.Add(new TreeSpot
                {
                    Prototype = ti.prototypeIndex,
                    Position = w,
                    Rotation = ti.rotation * Mathf.Rad2Deg,
                    Width = ti.widthScale,
                    Height = ti.heightScale,
                    Up = ti.Up,
                    Anchor = anchor,
                    Bucket = AnchorChunk(d, anchor),
                });
            }
            // Stable, so a rebuild keeps the order within a chunk.
            var sorted = new List<TreeSpot>(list.Count);
            var byBucket = new List<TreeSpot>[d.ChunkCount];
            foreach (TreeSpot s in list) (byBucket[s.Bucket] ??= new List<TreeSpot>()).Add(s);
            foreach (List<TreeSpot> b in byBucket) if (b != null) sorted.AddRange(b);
            return sorted;
        }

        private static void BuildTrees(DigZone zone, DigZoneData d, TerrainData td, List<TreeSpot> trees, Transform root)
        {
            int buckets = d.ChunkCount;
            zone.treeBuckets = new int[buckets + 1];
            zone.treeObjects = new GameObject[trees.Count];
            zone.treeAnchors = new Vector3[trees.Count];
            if (trees.Count == 0) return;

            var parent = new GameObject("Trees").transform;
            parent.SetParent(root, false);
            TreePrototype[] protos = td.treePrototypes;
            for (int i = 0; i < trees.Count; i++)
            {
                TreeSpot s = trees[i];
                GameObject prefab = protos[s.Prototype].prefab;
                GameObject go = PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab
                    ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)
                    : Object.Instantiate(prefab, parent);
                go.name = prefab.name;
                // Upright like on the terrain, or the way it was painted to grow (hanging from a cave ceiling).
                go.transform.SetPositionAndRotation(s.Position, Quaternion.FromToRotation(Vector3.up, s.Up) * Quaternion.Euler(0f, s.Rotation, 0f));
                // The terrain draws a tree at the instance's scale, not the prefab root's.
                go.transform.localScale = new Vector3(s.Width, s.Height, s.Width);
                go.SetActive(TreeStands(d, s.Anchor));
                zone.treeObjects[i] = go;
                zone.treeAnchors[i] = s.Anchor;
                zone.treeBuckets[s.Bucket + 1]++;
            }
            for (int k = 0; k < buckets; k++) zone.treeBuckets[k + 1] += zone.treeBuckets[k];
        }

        /// <summary>
        /// After the terrain under the zone changed height: trees that stood on the old top surface move onto the new one
        /// (the terrain moves its own trees with its heights, too). Trees on pit or tunnel floors stay where they are.
        /// </summary>
        public static void FollowHeights(DigZone zone, byte[] oldGrid)
        {
            DigZoneData d = zone.data;
            Terrain terrain = zone.terrain;
            if (d == null || !d.HasGrid || oldGrid == null || oldGrid.Length != d.grid.Length || d.terrainTrees == null || terrain == null) return;
            TerrainData td = terrain.terrainData;
            Vector3 tp = terrain.transform.position, origin = Origin(zone);
            bool any = false;
            for (int i = 0; i < d.terrainTrees.Length; i++)
            {
                DigTreeInstance t = d.terrainTrees[i];
                if (t.Up.y < 0.9f) continue; // hanging or growing sideways: not on the top surface
                Vector3 g = (tp + Vector3.Scale(t.position, td.size) - origin) / d.voxelSize;
                int x = Mathf.Clamp(Mathf.RoundToInt(g.x), 0, d.nx), z = Mathf.Clamp(Mathf.RoundToInt(g.z), 0, d.nz);
                float oldTop = DigFoliage.TopSurface(oldGrid, d.nx, d.ny, x, z);
                if (oldTop < 0f || Mathf.Abs(oldTop - g.y) > 0.5f) continue;
                float newTop = DigFoliage.TopSurface(d.grid, d.nx, d.ny, x, z);
                if (newTop < 0f) continue;
                t.position.y = (origin.y + newTop * d.voxelSize - tp.y) / td.size.y;
                d.terrainTrees[i] = t;
                any = true;
            }
            if (any) EditorUtility.SetDirty(d);
        }

        // ---------------------------------------------------------------- Details

        /// <summary>
        /// The terrain's detail instances inside the zone's hole, per prototype, on the zone's top surface. The terrain
        /// scatters no details in its holes, so they come from a copy of its detail layers without holes: the same patches
        /// give the same instances.
        /// </summary>
        private static List<DetailSpot>[] CollectDetails(DigZone zone, DigZoneData d, Terrain terrain, float[] top)
        {
            TerrainData td = terrain.terrainData;
            DetailPrototype[] protos = td.detailPrototypes;
            int res = td.detailResolution, perPatch = td.detailResolutionPerPatch;
            if (protos.Length == 0 || res <= 0 || perPatch <= 0) return null;

            Vector3 tp = terrain.transform.position;
            Vector3 origin = Origin(zone);
            Vector4 hole = d.holeRect;
            float sx = res / td.size.x, sz = res / td.size.z;
            int patches = Mathf.CeilToInt(res / (float)perPatch);
            int px0 = Mathf.Clamp(Mathf.FloorToInt((hole.x - tp.x) * sx) / perPatch, 0, patches - 1);
            int pz0 = Mathf.Clamp(Mathf.FloorToInt((hole.y - tp.z) * sz) / perPatch, 0, patches - 1);
            int px1 = Mathf.Clamp(Mathf.CeilToInt((hole.z - tp.x) * sx) / perPatch, 0, patches - 1);
            int pz1 = Mathf.Clamp(Mathf.CeilToInt((hole.w - tp.z) * sz) / perPatch, 0, patches - 1);
            int cx0 = px0 * perPatch, cz0 = pz0 * perPatch;
            int cw = Mathf.Min((px1 + 1) * perPatch, res) - cx0, ch = Mathf.Min((pz1 + 1) * perPatch, res) - cz0;

            var result = new List<DetailSpot>[protos.Length];
            for (int p = 0; p < protos.Length; p++) result[p] = new List<DetailSpot>();

            var copy = new TerrainData { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                copy.size = td.size;
                copy.SetDetailResolution(res, perPatch);
                copy.SetDetailScatterMode(td.detailScatterMode);
                copy.detailPrototypes = protos;
                var used = new bool[protos.Length];
                for (int p = 0; p < protos.Length; p++)
                {
                    if (!Usable(protos[p])) continue;
                    int[,] layer = td.GetDetailLayer(cx0, cz0, cw, ch, p);
                    foreach (int v in layer)
                        if (v > 0) { used[p] = true; break; }
                    if (used[p]) copy.SetDetailLayer(cx0, cz0, p, layer);
                }

                float density = terrain.detailObjectDensity;
                for (int p = 0; p < protos.Length; p++)
                {
                    if (!used[p]) continue;
                    for (int pz = pz0; pz <= pz1; pz++)
                    for (int px = px0; px <= px1; px++)
                    {
                        DetailInstanceTransform[] items = copy.ComputeDetailInstanceTransforms(px, pz, p, density, out Bounds _);
                        foreach (DetailInstanceTransform it in items)
                        {
                            var w = new Vector3(tp.x + it.posX, 0f, tp.z + it.posZ);
                            if (!InHole(d, w)) continue;
                            float gx = (w.x - origin.x) / d.voxelSize, gz = (w.z - origin.z) / d.voxelSize;
                            int x = Mathf.Clamp(Mathf.RoundToInt(gx), 0, d.nx), z = Mathf.Clamp(Mathf.RoundToInt(gz), 0, d.nz);
                            // Only where its column has a surface (the mask anchors the column there).
                            if (top[x + (d.nx + 1) * z] < 0f) continue;
                            float h = SurfaceAt(top, d, gx, gz);
                            if (h < 0f) continue;
                            w.y = origin.y + h * d.voxelSize;
                            result[p].Add(new DetailSpot
                            {
                                Prototype = p,
                                Root = w - origin,
                                Rotation = it.rotationY,
                                Width = it.scaleXZ,
                                Height = it.scaleY,
                                Column = x + (d.nx + 1) * z,
                                Texel = -1,
                            });
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
            return result;
        }

        /// <summary>The zone's surface details (DigZoneData.surfaceDetails) inside its grid, sorted by anchor chunk.</summary>
        private static List<SurfaceSpot> CollectSurfaceDetails(DigZone zone, DigZoneData d, Terrain terrain)
        {
            var list = new List<SurfaceSpot>();
            DigDetailInstance[] all = d.surfaceDetails;
            DetailPrototype[] protos = terrain.terrainData.detailPrototypes;
            if (all == null || all.Length == 0) return list;
            Vector3 tp = terrain.transform.position, origin = Origin(zone);
            foreach (DigDetailInstance di in all)
            {
                if (di.prototypeIndex < 0 || di.prototypeIndex >= protos.Length || !Usable(protos[di.prototypeIndex])) continue;
                Vector3 w = tp + di.position;
                Vector3 a = (w - origin) / d.voxelSize;
                if (a.x < 0f || a.y < 0f || a.z < 0f || a.x > d.nx || a.y > d.ny || a.z > d.nz) continue;
                int x = Mathf.Clamp(Mathf.RoundToInt(a.x), 0, d.nx), z = Mathf.Clamp(Mathf.RoundToInt(a.z), 0, d.nz);
                list.Add(new SurfaceSpot
                {
                    Spot = new DetailSpot
                    {
                        Prototype = di.prototypeIndex,
                        Root = w - origin,
                        Rotation = di.rotation,
                        Width = di.width,
                        Height = di.height,
                        Column = x + (d.nx + 1) * z,
                        Normal = di.normal,
                        Texel = -1,
                    },
                    Anchor = a,
                    Bucket = AnchorChunk(d, a),
                });
            }
            var sorted = new List<SurfaceSpot>(list.Count);
            var byBucket = new Dictionary<int, List<SurfaceSpot>>();
            foreach (SurfaceSpot sp in list)
            {
                if (!byBucket.TryGetValue(sp.Bucket, out List<SurfaceSpot> b)) byBucket[sp.Bucket] = b = new List<SurfaceSpot>();
                b.Add(sp);
            }
            var keys = new List<int>(byBucket.Keys);
            keys.Sort();
            foreach (int k in keys) sorted.AddRange(byBucket[k]);
            return sorted;
        }

        private static void SetSurfaceAnchors(DigZone zone, DigZoneData d, List<SurfaceSpot> surface)
        {
            zone.surfaceDetailAnchors = new Vector3[surface.Count];
            zone.surfaceDetailBuckets = new int[d.ChunkCount + 1];
            for (int k = 0; k < surface.Count; k++)
            {
                zone.surfaceDetailAnchors[k] = surface[k].Anchor;
                zone.surfaceDetailBuckets[surface[k].Bucket + 1]++;
            }
            for (int c = 0; c < d.ChunkCount; c++) zone.surfaceDetailBuckets[c + 1] += zone.surfaceDetailBuckets[c];
        }

        private static bool Usable(DetailPrototype p) =>
            p != null && (p.usePrototypeMesh ? PrototypeMesh(p) != null : p.prototypeTexture != null);

        private static Mesh PrototypeMesh(DetailPrototype p)
        {
            if (p.prototype == null) return null;
            MeshFilter f = p.prototype.GetComponent<MeshFilter>();
            if (f == null) f = p.prototype.GetComponentInChildren<MeshFilter>(true);
            return f != null ? f.sharedMesh : null;
        }

        private static Material PrototypeMaterial(DetailPrototype p)
        {
            if (p.prototype == null) return null;
            Renderer r = p.prototype.GetComponent<Renderer>();
            if (r == null) r = p.prototype.GetComponentInChildren<Renderer>(true);
            return r != null ? r.sharedMaterial : null;
        }

        /// <summary>Vertex lists of one chunk column's details, with an index list per prototype (submesh).</summary>
        private sealed class DetailMeshBuilder
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Color32> Colors = new List<Color32>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Vector4> Mask = new List<Vector4>();
            public readonly List<Vector3> Roots = new List<Vector3>();
            public readonly SortedDictionary<int, List<int>> Indices = new SortedDictionary<int, List<int>>();

            public List<int> IndicesOf(int prototype)
            {
                if (!Indices.TryGetValue(prototype, out List<int> l)) Indices[prototype] = l = new List<int>();
                return l;
            }

            public void Vertex(Vector3 p, Vector3 n, Color32 c, Vector2 uv, Vector2 maskUv, float sway, Vector3 root)
            {
                Positions.Add(p);
                Normals.Add(n);
                Colors.Add(c);
                Uv.Add(uv);
                Mask.Add(new Vector4(maskUv.x, maskUv.y, sway, 0f));
                Roots.Add(root);
            }
        }

        private static void BuildDetails(DigZone zone, DigZoneData d, Terrain terrain, List<DetailSpot>[] details, Transform root)
        {
            TerrainData td = terrain.terrainData;
            DetailPrototype[] protos = td.detailPrototypes;
            var oldMeshes = new List<Mesh>();
            if (d.detailMeshes != null) foreach (Mesh m in d.detailMeshes) if (m != null) oldMeshes.Add(m);
            Material[] oldMaterials = d.detailMaterials ?? Array.Empty<Material>();

            int buckets = d.ChunksX * d.ChunksZ;
            d.detailMeshes = new Mesh[buckets];
            var materials = new Material[protos.Length];
            var renderers = new List<MeshRenderer>();

            if (details != null)
            {
                var builders = new DetailMeshBuilder[buckets];
                float maskRows = d.foliageMask != null ? d.foliageMask.height : d.nz + 1;
                var meshCache = new Dictionary<int, (Vector3[] v, Vector3[] n, Vector2[] uv, int[] tri, float top)>();
                Vector3 origin = Origin(zone);
                for (int p = 0; p < protos.Length; p++)
                {
                    if (details[p].Count == 0) continue;
                    materials[p] = DetailMaterial(d, terrain, protos[p], p, p < oldMaterials.Length ? oldMaterials[p] : null);
                    foreach (DetailSpot s in details[p])
                    {
                        int gx = s.Column % (d.nx + 1), gz = s.Column / (d.nx + 1);
                        int k = Mathf.Min(gx / d.chunkCells, d.ChunksX - 1) + d.ChunksX * Mathf.Min(gz / d.chunkCells, d.ChunksZ - 1);
                        DetailMeshBuilder b = builders[k] ??= new DetailMeshBuilder();
                        int tx = s.Texel >= 0 ? s.Texel % (d.nx + 1) : gx, ty = s.Texel >= 0 ? s.Texel / (d.nx + 1) : gz;
                        var maskUv = new Vector2((tx + 0.5f) / (d.nx + 1), (ty + 0.5f) / maskRows);
                        Color32 tint = Tint(protos[p], origin + s.Root);
                        if (protos[p].usePrototypeMesh) AddMeshInstance(b, protos[p], p, s, tint, maskUv, meshCache);
                        else AddGrassInstance(b, p, s, tint, maskUv);
                    }
                }

                Transform parent = null;
                for (int k = 0; k < buckets; k++)
                {
                    DetailMeshBuilder b = builders[k];
                    if (b == null || b.Positions.Count == 0) continue;
                    int cx = k % d.ChunksX, cz = k / d.ChunksX;
                    Mesh mesh;
                    if (oldMeshes.Count > 0)
                    {
                        mesh = oldMeshes[oldMeshes.Count - 1];
                        oldMeshes.RemoveAt(oldMeshes.Count - 1);
                        mesh.Clear();
                    }
                    else
                    {
                        mesh = new Mesh();
                        AssetDatabase.AddObjectToAsset(mesh, d);
                    }
                    mesh.name = $"{d.name}_Details_{cx}_{cz}";
                    mesh.indexFormat = b.Positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    mesh.SetVertices(b.Positions);
                    mesh.SetNormals(b.Normals);
                    mesh.SetColors(b.Colors);
                    mesh.SetUVs(0, b.Uv);
                    mesh.SetUVs(2, b.Mask);
                    mesh.SetUVs(3, b.Roots);
                    mesh.subMeshCount = b.Indices.Count;
                    var mats = new Material[b.Indices.Count];
                    int sub = 0;
                    foreach (KeyValuePair<int, List<int>> kv in b.Indices)
                    {
                        mesh.SetTriangles(kv.Value, sub, false);
                        mats[sub++] = materials[kv.Key];
                    }
                    mesh.RecalculateBounds();
                    // Wind moves the tops a little past the bounds.
                    Bounds bounds = mesh.bounds;
                    bounds.Expand(new Vector3(1f, 0.5f, 1f));
                    mesh.bounds = bounds;
                    EditorUtility.SetDirty(mesh);
                    d.detailMeshes[k] = mesh;

                    if (parent == null)
                    {
                        parent = new GameObject("Details").transform;
                        parent.SetParent(root, false);
                    }
                    var go = new GameObject($"Details_{cx}_{cz}") { layer = zone.gameObject.layer };
                    go.transform.SetParent(parent, false);
                    go.transform.position = origin;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var rend = go.AddComponent<MeshRenderer>();
                    rend.sharedMaterials = mats;
                    rend.shadowCastingMode = ShadowCastingMode.Off;
                    rend.receiveShadows = true;
                    rend.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderers.Add(rend);
                }
            }

            foreach (Mesh m in oldMeshes) Object.DestroyImmediate(m, true);
            for (int p = 0; p < oldMaterials.Length; p++)
                if (oldMaterials[p] != null && (p >= materials.Length || materials[p] != oldMaterials[p]))
                    Object.DestroyImmediate(oldMaterials[p], true);
            d.detailMaterials = materials;
            zone.detailRenderers = renderers.ToArray();
        }

        /// <summary>Two crossed quads, like the terrain's grass (billboard grass too: it stays readable from every side).</summary>
        private static void AddGrassInstance(DetailMeshBuilder b, int p, DetailSpot s, Color32 tint, Vector2 maskUv)
        {
            List<int> idx = b.IndicesOf(p);
            float half = s.Width * 0.5f;
            Quaternion tilt = Tilt(s);
            Vector3 n = tilt * Vector3.up;
            for (int q = 0; q < 2; q++)
            {
                float a = s.Rotation + q * Mathf.PI * 0.5f;
                Vector3 right = tilt * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * half;
                Vector3 up = n * s.Height;
                int v0 = b.Positions.Count;
                b.Vertex(s.Root - right, n, tint, new Vector2(0f, 0f), maskUv, 0f, s.Root);
                b.Vertex(s.Root + right, n, tint, new Vector2(1f, 0f), maskUv, 0f, s.Root);
                b.Vertex(s.Root + right + up, n, tint, new Vector2(1f, 1f), maskUv, 1f, s.Root);
                b.Vertex(s.Root - right + up, n, tint, new Vector2(0f, 1f), maskUv, 1f, s.Root);
                idx.Add(v0); idx.Add(v0 + 2); idx.Add(v0 + 1);
                idx.Add(v0); idx.Add(v0 + 3); idx.Add(v0 + 2);
            }
        }

        /// <summary>Turns straight up into the way the detail grows.</summary>
        private static Quaternion Tilt(DetailSpot s) =>
            s.Normal.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(Vector3.up, s.Normal.normalized) : Quaternion.identity;

        private static void AddMeshInstance(DetailMeshBuilder b, DetailPrototype proto, int p, DetailSpot s, Color32 tint, Vector2 maskUv,
            Dictionary<int, (Vector3[] v, Vector3[] n, Vector2[] uv, int[] tri, float top)> cache)
        {
            if (!cache.TryGetValue(p, out var m))
            {
                Mesh src = PrototypeMesh(proto);
                Vector3[] v = src.vertices;
                Vector3[] n = src.normals;
                Vector2[] uv = src.uv;
                float top = 0f;
                foreach (Vector3 x in v) top = Mathf.Max(top, x.y);
                m = (v, n.Length == v.Length ? n : null, uv.Length == v.Length ? uv : null, src.triangles, Mathf.Max(top, 1e-3f));
                cache[p] = m;
            }

            // Grass render mode sways with the wind like the terrain's grass; vertex lit meshes stand still.
            bool sways = proto.renderMode == DetailRenderMode.Grass;
            Quaternion rot = Tilt(s) * Quaternion.Euler(0f, s.Rotation * Mathf.Rad2Deg, 0f);
            var scale = new Vector3(s.Width, s.Height, s.Width);
            List<int> idx = b.IndicesOf(p);
            int v0 = b.Positions.Count;
            for (int i = 0; i < m.v.Length; i++)
            {
                Vector3 pos = s.Root + rot * Vector3.Scale(m.v[i], scale);
                Vector3 nrm = m.n != null ? (rot * new Vector3(m.n[i].x / scale.x, m.n[i].y / scale.y, m.n[i].z / scale.z)).normalized : Vector3.up;
                float sway = sways ? Mathf.Clamp01(m.v[i].y / m.top) : 0f;
                b.Vertex(pos, nrm, tint, m.uv != null ? m.uv[i] : Vector2.zero, maskUv, sway, s.Root);
            }
            foreach (int t in m.tri) idx.Add(v0 + t);
        }

        /// <summary>Healthy / dry colour from the prototype's noise, like the terrain's details.</summary>
        private static Color32 Tint(DetailPrototype p, Vector3 world)
        {
            float spread = Mathf.Max(1e-4f, p.noiseSpread);
            float seed = p.noiseSeed * 0.137f;
            float t = Mathf.Clamp01(Mathf.PerlinNoise(world.x * spread + seed, world.z * spread + seed));
            return Color.Lerp(p.dryColor, p.healthyColor, t);
        }

        private static Material DetailMaterial(DigZoneData d, Terrain terrain, DetailPrototype proto, int p, Material old)
        {
            Shader shader = Shader.Find(DetailShader);
            Material mat = old;
            if (mat == null || mat.shader != shader)
            {
                if (mat != null) Object.DestroyImmediate(mat, true);
                mat = new Material(shader);
                AssetDatabase.AddObjectToAsset(mat, d);
            }
            mat.name = $"{d.name}_Detail{p}";

            Texture tex = null;
            Color color = Color.white;
            if (proto.usePrototypeMesh)
            {
                Material src = PrototypeMaterial(proto);
                if (src != null)
                {
                    if (src.HasProperty("_MainTex")) tex = src.mainTexture;
                    else if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
                    // Like the terrain: instanced detail meshes draw with their material, the others with just its
                    // texture and the healthy / dry colours.
                    if (proto.useInstancing)
                    {
                        if (src.HasProperty("_Color")) color = src.color;
                        else if (src.HasProperty("_BaseColor")) color = src.GetColor("_BaseColor");
                    }
                }
            }
            else tex = proto.prototypeTexture;

            TerrainData td = terrain.terrainData;
            mat.SetTexture("_MainTex", tex);
            mat.SetColor("_Color", color);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetVector("_WaveAndDistance", new Vector4(td.wavingGrassSpeed, td.wavingGrassStrength, td.wavingGrassAmount, terrain.detailObjectDistance));
            mat.SetColor("_WavingTint", td.wavingGrassTint);
            mat.SetTexture("_DigFoliageMask", d.foliageMask);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- Change tracking

        /// <summary>
        /// Brings the zone's trees and details in step with its terrain: only the trees when nothing else changed
        /// (painting trees, or undoing that), otherwise everything.
        /// </summary>
        public static void Refresh(DigZone zone)
        {
            if (IsUpToDate(zone)) return;
            DigZoneData d = zone.data;
            if (d.foliageDetailSignature != 0 && d.foliageDetailSignature == Signature(zone, false))
            {
                RebuildTrees(zone);
                if (IsUpToDate(zone)) return;
            }
            Build(zone);
        }

        /// <summary>
        /// Hash of everything the zone's trees and details are built from except the terrain heights (height changes
        /// re-sync the zone, which rebuilds them anyway). Without <paramref name="withTrees"/>: all but the trees.
        /// </summary>
        public static int Signature(DigZone zone, bool withTrees = true)
        {
            DigZoneData d = zone.data;
            Terrain terrain = zone.terrain;
            if (d == null || terrain == null || terrain.terrainData == null) return 0;
            TerrainData td = terrain.terrainData;
            unchecked
            {
                int h = 17;
                void Add(int v) => h = h * 31 + v;
                void AddF(float v) => Add(v.GetHashCode());

                Add(zone.trees ? 1 : 2);
                Add(zone.details ? 1 : 2);
                Add(d.nx); Add(d.ny); Add(d.nz); Add(d.chunkCells);
                AddF(d.voxelSize);
                Add(d.origin.GetHashCode());
                Add(d.holeRect.GetHashCode());
                Add(terrain.transform.position.GetHashCode());
                Add(td.size.GetHashCode());

                if (withTrees && zone.trees && d.terrainTrees != null)
                {
                    foreach (TreePrototype p in td.treePrototypes) Add(p.prefab != null ? p.prefab.GetInstanceID() : 0);
                    foreach (DigTreeInstance ti in d.terrainTrees)
                    {
                        Add(ti.position.GetHashCode());
                        Add(ti.up.GetHashCode());
                        Add(ti.prototypeIndex);
                        AddF(ti.widthScale);
                        AddF(ti.heightScale);
                        AddF(ti.rotation);
                    }
                }

                if (zone.details && td.detailResolution > 0)
                {
                    AddF(terrain.detailObjectDensity);
                    AddF(terrain.detailObjectDistance);
                    AddF(td.wavingGrassSpeed);
                    AddF(td.wavingGrassStrength);
                    AddF(td.wavingGrassAmount);
                    Add(td.wavingGrassTint.GetHashCode());
                    Add((int)td.detailScatterMode);
                    Add(td.detailResolution);
                    Add(td.detailResolutionPerPatch);
                    DetailPrototype[] protos = td.detailPrototypes;
                    foreach (DetailPrototype p in protos)
                    {
                        Add(p.usePrototypeMesh ? 1 : 2);
                        Add(p.prototype != null ? p.prototype.GetInstanceID() : 0);
                        Add(p.prototypeTexture != null ? p.prototypeTexture.GetInstanceID() : 0);
                        Material m = PrototypeMaterial(p);
                        Add(m != null ? m.GetInstanceID() : 0);
                        AddF(p.minWidth); AddF(p.maxWidth); AddF(p.minHeight); AddF(p.maxHeight);
                        AddF(p.noiseSpread); Add(p.noiseSeed);
                        Add(p.healthyColor.GetHashCode()); Add(p.dryColor.GetHashCode());
                        Add((int)p.renderMode);
                        AddF(p.density);
                    }

                    Vector3 tp = terrain.transform.position;
                    int res = td.detailResolution;
                    float sx = res / td.size.x, sz = res / td.size.z;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt((d.holeRect.x - tp.x) * sx) - 1, 0, res - 1);
                    int z0 = Mathf.Clamp(Mathf.FloorToInt((d.holeRect.y - tp.z) * sz) - 1, 0, res - 1);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt((d.holeRect.z - tp.x) * sx) + 1, 0, res - 1);
                    int z1 = Mathf.Clamp(Mathf.CeilToInt((d.holeRect.w - tp.z) * sz) + 1, 0, res - 1);
                    for (int p = 0; p < protos.Length; p++)
                        foreach (int v in td.GetDetailLayer(x0, z0, x1 - x0 + 1, z1 - z0 + 1, p))
                            Add(v);
                    if (d.surfaceDetails != null)
                        foreach (DigDetailInstance di in d.surfaceDetails)
                        {
                            Add(di.position.GetHashCode());
                            Add(di.normal.GetHashCode());
                            Add(di.prototypeIndex);
                            AddF(di.rotation); AddF(di.width); AddF(di.height);
                        }
                }
                return h == 0 ? 1 : h;
            }
        }

        /// <summary>
        /// True if the zone's trees and details were built from what its terrain holds now and all still exist (undo
        /// can bring back references to objects and sub-assets that were rebuilt since).
        /// </summary>
        public static bool IsUpToDate(DigZone zone)
        {
            DigZoneData d = zone.data;
            if (d == null || !d.HasGrid) return true;
            if (d.foliageSignature != Signature(zone)) return false;
            if (d.foliageCount == 0) return zone.foliageRoot == null;

            int surface = zone.surfaceDetailAnchors?.Length ?? 0;
            if (zone.foliageRoot == null || d.foliageMask == null || d.foliageMask.width != d.nx + 1 ||
                d.foliageMask.height != MaskHeight(d, surface) || d.foliageMask.format != TextureFormat.RGBA32)
                return false;
            if (surface > 0 && (zone.surfaceDetailBuckets == null || zone.surfaceDetailBuckets.Length != d.ChunkCount + 1)) return false;
            int count = 0;
            if (zone.treeObjects != null)
            {
                if (zone.treeAnchors == null || zone.treeAnchors.Length != zone.treeObjects.Length) return false;
                foreach (GameObject t in zone.treeObjects)
                    if (t == null) return false;
                count += zone.treeObjects.Length;
            }
            if (zone.detailRenderers != null)
            {
                foreach (MeshRenderer r in zone.detailRenderers)
                {
                    if (r == null || !r.TryGetComponent(out MeshFilter f) || f.sharedMesh == null) return false;
                    foreach (Material m in r.sharedMaterials)
                        if (m == null) return false;
                }
                count += zone.detailRenderers.Length;
            }
            return count == d.foliageCount;
        }
    }
}
