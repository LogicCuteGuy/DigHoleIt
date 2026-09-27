using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// Terrain hole bookkeeping across zones. Filling a zone's hole back in never touches cells another live zone still
    /// needs, and hands that zone the true "before" state of the shared cells. Deleting a zone GameObject fills its hole
    /// automatically; <see cref="FixLeftoverHoles"/> repairs holes left by zones deleted before this existed.
    /// </summary>
    [InitializeOnLoad]
    public static class DigTerrainHoles
    {
        // Zones seen in loaded scenes, so a deleted one can still be matched with its data.
        private static readonly Dictionary<DigZone, DigZoneData> Known = new Dictionary<DigZone, DigZoneData>();

        static DigTerrainHoles()
        {
            // A zone that vanished from a loaded scene (deleted, or its creation undone) is caught on the next
            // hierarchy change; scene closing and play mode are excluded below.
            EditorApplication.hierarchyChanged += Track;
            EditorApplication.delayCall += Track;
            EditorSceneManager.sceneClosing += (scene, removing) =>
            {
                var gone = new List<DigZone>();
                foreach (DigZone z in Known.Keys) if (z == null || z.gameObject.scene == scene) gone.Add(z);
                foreach (DigZone z in gone) Known.Remove(z);
            };
            EditorApplication.playModeStateChanged += state =>
            {
                // Play mode reloads the scene objects; that is not a deletion.
                if (state == PlayModeStateChange.ExitingEditMode) Known.Clear();
                if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Track;
            };
        }

        private static void Track()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            FillHolesOfDeletedZones();
            foreach (DigZone z in Object.FindObjectsByType<DigZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (z.data != null) Known[z] = z.data;
        }

        private static void FillHolesOfDeletedZones()
        {
            var deleted = new List<KeyValuePair<DigZone, DigZoneData>>();
            foreach (KeyValuePair<DigZone, DigZoneData> kv in Known) if (kv.Key == null) deleted.Add(kv);
            foreach (KeyValuePair<DigZone, DigZoneData> kv in deleted)
            {
                Known.Remove(kv.Key);
                DigZoneData data = kv.Value;
                if (data == null || data.cutTerrain == null || IsUsed(data)) continue;
                RestoreCut(data);
                Debug.Log($"[DigHoleIt] Filled the terrain hole of the deleted Dig Zone ({data.name}).");
            }
            if (deleted.Count > 0) DigTerrainZones.Invalidate();
        }

        /// <summary>
        /// Live zones (loaded scenes) other than <paramref name="data"/>'s whose hole in the same terrain overlaps
        /// <paramref name="rect"/>. Only those are recorded for undo: a big zone's data is slow to record.
        /// </summary>
        private static List<DigZoneData> OtherCuts(DigZoneData data, TerrainData td, RectInt rect)
        {
            var list = new List<DigZoneData>();
            foreach (DigZone z in Object.FindObjectsByType<DigZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                DigZoneData d = z.data;
                if (d == null || d == data || d.cutTerrain != td || d.cutPrevious == null || list.Contains(d)) continue;
                if (d.cutPrevious.Length != d.cutRect.width * d.cutRect.height || !d.cutRect.Overlaps(rect)) continue;
                list.Add(d);
            }
            return list;
        }

        public static bool IsUsed(DigZoneData data)
        {
            foreach (DigZone z in Object.FindObjectsByType<DigZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (z.data == data) return true;
            return false;
        }

        /// <summary>
        /// Puts back the terrain surface <paramref name="data"/> cut away, except where another live zone's hole
        /// covers it; those zones inherit the true previous state of the shared cells.
        /// </summary>
        public static void RestoreCut(DigZoneData data)
        {
            if (data == null || data.cutTerrain == null || data.cutPrevious == null) return;
            TerrainData td = data.cutTerrain;
            RectInt r = data.cutRect;
            if (r.width <= 0 || r.height <= 0 || data.cutPrevious.Length != r.width * r.height) return;

            List<DigZoneData> others = OtherCuts(data, td, r);
            Undo.RegisterCompleteObjectUndo(td, "Fill Terrain Hole");
            Undo.RegisterCompleteObjectUndo(data, "Fill Terrain Hole");
            foreach (DigZoneData o in others) Undo.RegisterCompleteObjectUndo(o, "Fill Terrain Hole");

            bool[,] holes = td.GetHoles(r.x, r.y, r.width, r.height);
            for (int j = 0; j < r.height; j++)
            for (int i = 0; i < r.width; i++)
            {
                int tx = r.x + i, tz = r.y + j;
                bool previous = data.cutPrevious[i + j * r.width];
                bool keep = false;
                foreach (DigZoneData o in others)
                {
                    RectInt q = o.cutRect;
                    if (tx < q.xMin || tz < q.yMin || tx >= q.xMax || tz >= q.yMax) continue;
                    // The other zone recorded this cell as "before" while our hole was there; give it the real one.
                    o.cutPrevious[(tx - q.x) + (tz - q.y) * q.width] = previous;
                    EditorUtility.SetDirty(o);
                    keep = true;
                }
                if (!keep) holes[j, i] = previous;
            }
            td.SetHoles(r.x, r.y, holes);
            EditorUtility.SetDirty(td);

            data.cutTerrain = null;
            data.cutPrevious = null;
            data.cutRect = default;
            EditorUtility.SetDirty(data);
        }

        /// <summary>Data assets that still hold a hole in <paramref name="td"/> but no zone in the open scenes uses.</summary>
        public static List<DigZoneData> FindLeftovers(TerrainData td)
        {
            var list = new List<DigZoneData>();
            foreach (string guid in AssetDatabase.FindAssets("t:DigZoneData"))
            {
                var d = AssetDatabase.LoadAssetAtPath<DigZoneData>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null && d.cutTerrain == td && d.cutPrevious != null && !IsUsed(d)) list.Add(d);
            }
            return list;
        }

        /// <summary>Fills the holes of zones that were deleted earlier. Returns how many were filled.</summary>
        public static int FixLeftoverHoles(Terrain terrain)
        {
            List<DigZoneData> leftovers = FindLeftovers(terrain.terrainData);
            foreach (DigZoneData d in leftovers) RestoreCut(d);
            if (leftovers.Count > 0) AssetDatabase.SaveAssets();
            return leftovers.Count;
        }

        /// <summary>Fills the zone's terrain hole and deletes the zone (one undo step).</summary>
        public static void DeleteZone(DigZone zone)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Delete Dig Zone");
            int group = Undo.GetCurrentGroup();
            Known.Remove(zone);
            if (zone.data != null) RestoreCut(zone.data);
            Undo.DestroyObjectImmediate(zone.gameObject);
            Undo.CollapseUndoOperations(group);
            DigTerrainZones.Invalidate();
        }
    }
}
