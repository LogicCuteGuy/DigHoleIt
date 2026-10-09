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
            GiveBackTrees(data, td, others);
            EditorUtility.SetDirty(td);

            data.cutTerrain = null;
            data.cutPrevious = null;
            data.cutRect = default;
            EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// Trees a re-bake gave back to the terrain and took again (<paramref name="taken"/>): those the zone had
        /// before (<paramref name="before"/>) get their height and direction back, so trees on pit or cave floors and
        /// under ceilings stay there instead of moving to the terrain surface.
        /// </summary>
        public static DigTreeInstance[] KeepPlacement(DigTreeInstance[] taken, DigTreeInstance[] before)
        {
            if (taken == null || taken.Length == 0 || before == null || before.Length == 0) return taken;
            var byKey = new Dictionary<(float, float, int, float), Queue<DigTreeInstance>>();
            foreach (DigTreeInstance t in before)
            {
                var key = (t.position.x, t.position.z, t.prototypeIndex, t.rotation);
                if (!byKey.TryGetValue(key, out Queue<DigTreeInstance> q)) byKey[key] = q = new Queue<DigTreeInstance>();
                q.Enqueue(t);
            }
            for (int i = 0; i < taken.Length; i++)
            {
                DigTreeInstance t = taken[i];
                if (byKey.TryGetValue((t.position.x, t.position.z, t.prototypeIndex, t.rotation), out Queue<DigTreeInstance> q) && q.Count > 0)
                    taken[i] = q.Dequeue();
            }
            return taken;
        }

        /// <summary>The terrain hole cell a tree position (0..1, like TreeInstance.position) lies in.</summary>
        public static bool HoleCell(TerrainData td, Vector3 position, out Vector2Int cell)
        {
            int res = td.holesResolution;
            cell = new Vector2Int(Mathf.FloorToInt(position.x * res), Mathf.FloorToInt(position.z * res));
            return cell.x >= 0 && cell.y >= 0 && cell.x < res && cell.y < res;
        }

        /// <summary>
        /// Puts the trees a zone kept while its hole was cut back on the terrain. Trees in cells another zone's hole still
        /// covers go to that zone instead (the terrain would delete them).
        /// </summary>
        private static void GiveBackTrees(DigZoneData data, TerrainData td, List<DigZoneData> others)
        {
            DigTreeInstance[] kept = data.terrainTrees;
            data.terrainTrees = null;
            if (kept == null || kept.Length == 0) return;

            int prototypes = td.treePrototypes.Length;
            var trees = new List<TreeInstance>(td.treeInstances);
            int lost = 0;
            foreach (DigTreeInstance t in kept)
            {
                if (t.prototypeIndex < 0 || t.prototypeIndex >= prototypes)
                {
                    lost++;
                    continue;
                }
                DigZoneData owner = null;
                if (HoleCell(td, t.position, out Vector2Int c))
                    foreach (DigZoneData o in others)
                        if (o.cutRect.Contains(c)) { owner = o; break; }
                if (owner == null)
                {
                    // Trees from pit or mound floors land on the terrain again.
                    TreeInstance back = t.ToTreeInstance();
                    back.position.y = td.GetInterpolatedHeight(back.position.x, back.position.z) / td.size.y;
                    trees.Add(back);
                    continue;
                }
                var list = new List<DigTreeInstance>(owner.terrainTrees ?? System.Array.Empty<DigTreeInstance>()) { t };
                owner.terrainTrees = list.ToArray();
                EditorUtility.SetDirty(owner);
                DigFoliageSync.MarkDirty(td);
            }
            td.SetTreeInstances(trees.ToArray(), false);
            if (lost > 0)
                Debug.LogWarning($"[DigHoleIt] {lost} trees of '{data.name}' use tree prototypes the terrain no longer has; they were dropped.");
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

        /// <summary>
        /// Terrain hole cells of <paramref name="terrain"/> that no Dig Zone in the open scenes cut: holes of zones that
        /// were deleted without a record of their cut (their data asset gone or copied), or painted by hand.
        /// With <paramref name="fill"/> they are filled (one undo step). Returns how many there are.
        /// </summary>
        public static int HolesOutsideZones(Terrain terrain, bool fill)
        {
            TerrainData td = terrain.terrainData;
            if (td == null) return 0;
            var rects = new List<RectInt>();
            foreach (DigZone z in Object.FindObjectsByType<DigZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                DigZoneData d = z.data;
                if (d != null && d.cutTerrain == td && d.cutRect.width > 0 && d.cutRect.height > 0) rects.Add(d.cutRect);
            }

            int res = td.holesResolution;
            bool[,] holes = td.GetHoles(0, 0, res, res); // true = surface, false = hole; [z, x]
            int count = 0;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                if (holes[z, x]) continue;
                bool inZone = false;
                foreach (RectInt r in rects)
                    if (x >= r.xMin && x < r.xMax && z >= r.yMin && z < r.yMax) { inZone = true; break; }
                if (inZone) continue;
                holes[z, x] = true;
                count++;
            }
            if (fill && count > 0)
            {
                Undo.RegisterCompleteObjectUndo(td, "Refresh Terrain Holes");
                td.SetHoles(0, 0, holes);
                EditorUtility.SetDirty(td);
                AssetDatabase.SaveAssets();
            }
            return count;
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
