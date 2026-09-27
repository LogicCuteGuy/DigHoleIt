using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// Keeps baked Dig Zones in step with their terrain: raising, lowering or painting the terrain (with Unity's
    /// terrain tools, scripts or undo) re-syncs the zones it touches once the stroke ends. Sculpting and voxel paint
    /// are kept. See <see cref="DigZoneBaker.SyncWithTerrain"/>.
    /// </summary>
    [InitializeOnLoad]
    public static class DigTerrainSync
    {
        private const string PrefKey = "DigHoleIt.SyncWithTerrain";
        private const double Delay = 0.3;

        // Changed regions per terrain, in heightmap samples and alphamap texels.
        private static readonly Dictionary<TerrainData, RectInt> Heights = new Dictionary<TerrainData, RectInt>();
        private static readonly Dictionary<TerrainData, RectInt> Textures = new Dictionary<TerrainData, RectInt>();
        private static readonly HashSet<TerrainData> UnsyncedHeights = new HashSet<TerrainData>();
        private static readonly HashSet<TerrainData> UnsyncedTextures = new HashSet<TerrainData>();
        private static double _due;
        private static bool _scheduled;

        /// <summary>Re-sync zones when their terrain is edited (EditorPrefs, on by default).</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        static DigTerrainSync()
        {
            TerrainCallbacks.heightmapChanged += OnHeightmapChanged;
            TerrainCallbacks.textureChanged += OnTextureChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private static bool Ignore => !Enabled || DigZoneBaker.Busy || EditorApplication.isPlayingOrWillChangePlaymode;

        private static void OnHeightmapChanged(Terrain terrain, RectInt region, bool synched)
        {
            if (Ignore || terrain == null || terrain.terrainData == null) return;
            TerrainData td = terrain.terrainData;
            Add(Heights, td, region);
            if (synched) UnsyncedHeights.Remove(td);
            else UnsyncedHeights.Add(td);
            Schedule();
        }

        private static void OnTextureChanged(Terrain terrain, string textureName, RectInt region, bool synched)
        {
            // Holes are ours (zone bakes cut them); only the layer blend matters here.
            if (Ignore || terrain == null || terrain.terrainData == null || textureName != TerrainData.AlphamapTextureName) return;
            TerrainData td = terrain.terrainData;
            Add(Textures, td, region);
            if (synched) UnsyncedTextures.Remove(td);
            else UnsyncedTextures.Add(td);
            Schedule();
        }

        private static void OnUndoRedo()
        {
            // An undone terrain stroke raises no reliable callback; check every zone (unchanged ones are skipped).
            if (Ignore) return;
            foreach (DigZone zone in Object.FindObjectsByType<DigZone>(FindObjectsSortMode.None))
            {
                TerrainData td = zone.terrain != null ? zone.terrain.terrainData : null;
                if (td == null) continue;
                var all = new RectInt(0, 0, int.MaxValue / 2, int.MaxValue / 2);
                Add(Heights, td, all);
                Add(Textures, td, all);
            }
            if (Heights.Count > 0) Schedule();
        }

        private static void Add(Dictionary<TerrainData, RectInt> map, TerrainData td, RectInt region)
        {
            if (map.TryGetValue(td, out RectInt r))
            {
                int x0 = Mathf.Min(r.xMin, region.xMin), y0 = Mathf.Min(r.yMin, region.yMin);
                int x1 = Mathf.Max(r.xMax, region.xMax), y1 = Mathf.Max(r.yMax, region.yMax);
                region = new RectInt(x0, y0, x1 - x0, y1 - y0);
            }
            map[td] = region;
        }

        private static void Schedule()
        {
            _due = EditorApplication.timeSinceStartup + Delay;
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            // Wait until the brush stroke (or slider drag) is over.
            if (EditorApplication.timeSinceStartup < _due || GUIUtility.hotControl != 0) return;
            EditorApplication.update -= Tick;
            _scheduled = false;
            if (Ignore)
            {
                Clear();
                return;
            }

            foreach (TerrainData td in UnsyncedHeights) if (td != null) td.SyncHeightmap();
            foreach (TerrainData td in UnsyncedTextures) if (td != null) td.SyncTexture(TerrainData.AlphamapTextureName);

            var heights = new Dictionary<TerrainData, RectInt>(Heights);
            var textures = new Dictionary<TerrainData, RectInt>(Textures);
            Clear();

            int synced = 0;
            foreach (DigZone zone in Object.FindObjectsByType<DigZone>(FindObjectsSortMode.None))
            {
                Terrain terrain = zone.terrain;
                if (terrain == null || terrain.terrainData == null) continue;
                TerrainData td = terrain.terrainData;
                bool h = heights.TryGetValue(td, out RectInt hr) && Overlaps(zone, td, hr, td.heightmapResolution);
                bool t = textures.TryGetValue(td, out RectInt tr) && Overlaps(zone, td, tr, td.alphamapResolution);
                if (!h && !t) continue;
                if (!DigZoneBaker.IsUpToDate(zone)) continue;
                if (DigZoneBaker.SyncWithTerrain(zone, h, t)) synced++;
            }
            if (synced > 0)
            {
                DigTerrainZones.Invalidate();
                SceneView.RepaintAll();
            }
        }

        private static void Clear()
        {
            Heights.Clear();
            Textures.Clear();
            UnsyncedHeights.Clear();
            UnsyncedTextures.Clear();
        }

        /// <summary>Whether a changed region (in a terrain map of <paramref name="res"/> texels per side) touches the zone.</summary>
        private static bool Overlaps(DigZone zone, TerrainData td, RectInt region, int res)
        {
            Vector3 tp = zone.terrain.transform.position;
            Bounds b = zone.WorldBounds;
            // Heightmap samples and alphamap texels both span the terrain as 0..res-1; pad for interpolation.
            float sx = (res - 1) / td.size.x, sz = (res - 1) / td.size.z;
            int x0 = Mathf.FloorToInt((b.min.x - tp.x) * sx) - 2, x1 = Mathf.CeilToInt((b.max.x - tp.x) * sx) + 2;
            int z0 = Mathf.FloorToInt((b.min.z - tp.z) * sz) - 2, z1 = Mathf.CeilToInt((b.max.z - tp.z) * sz) + 2;
            return region.xMin <= x1 && region.xMax >= x0 && region.yMin <= z1 && region.yMax >= z0;
        }
    }
}
