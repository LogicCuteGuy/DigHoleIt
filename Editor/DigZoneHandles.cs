using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// Scene-view gizmos and handles for a Dig Zone: the zone box with a cube handle per face (moves that face in whole
    /// voxels), the green diggable area, and labels. Used by the zone inspector and by the Terrain tools, so zones can
    /// be edited without selecting them. A resized zone re-bakes when the drag ends.
    /// </summary>
    public static class DigZoneHandles
    {
        public static readonly Color ZoneColor = new Color(1f, 0.6f, 0.1f, 1f);
        public static readonly Color DiggableColor = new Color(0.3f, 1f, 0.45f, 0.9f);

        private static readonly HashSet<DigZone> Pending = new HashSet<DigZone>();

        public static bool AutoRebake
        {
            get => EditorPrefs.GetBool("DigHoleIt.AutoRebake", true);
            set => EditorPrefs.SetBool("DigHoleIt.AutoRebake", value);
        }

        /// <summary>Draws and handles one zone. Call from OnSceneGUI for every event.</summary>
        /// <param name="editable">False draws the boxes only (no resize handles).</param>
        public static void OnSceneGUI(DigZone zone, bool editable = true)
        {
            if (zone == null) return;
            if (editable) SnapToLattice(zone);

            Bounds b = zone.WorldBounds;
            using (new Handles.DrawingScope(ZoneColor))
                Handles.DrawWireCube(b.center, b.size);
            DrawDiggable(zone);

            if (editable && DrawFaceHandles(b, out Vector3 min, out Vector3 max)) ApplyBox(zone, min, max, b);
            DrawSizeLabel(zone);

            // Re-bake once the drag ends (outside the GUI event, since baking recreates the chunk objects).
            if (Pending.Contains(zone) && GUIUtility.hotControl == 0)
            {
                Pending.Remove(zone);
                if (AutoRebake && zone.data != null && zone.data.HasGrid)
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (zone == null) return;
                        DigZoneBaker.Bake(zone);
                        DigTerrainZones.Invalidate();
                    };
                }
            }
        }

        private static void DrawDiggable(DigZone zone)
        {
            DigZoneData d = zone.data;
            if (d == null || d.editBox == null || d.editBox.Length != 6 || d.editBox[3] < d.editBox[0]) return;
            int[] e = d.editBox;
            Vector3 min = zone.GridToWorld(new Vector3(e[0], e[1], e[2]));
            Vector3 max = zone.GridToWorld(new Vector3(e[3], e[4], e[5]));
            using (new Handles.DrawingScope(DiggableColor))
                Handles.DrawWireCube((min + max) * 0.5f, max - min);
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = DiggableColor } };
            Handles.Label(new Vector3(min.x, max.y, min.z), "Diggable area", style);
        }

        /// <summary>
        /// One cube handle per face, in the axis colours, sized to stay grabbable at any distance.
        /// Returns true when a face was dragged, with the new (unsnapped) box.
        /// </summary>
        private static bool DrawFaceHandles(Bounds b, out Vector3 min, out Vector3 max)
        {
            min = b.min;
            max = b.max;
            bool changed = false;

            for (int a = 0; a < 3; a++)
            for (int side = 0; side < 2; side++)
            {
                Vector3 pos = b.center;
                pos[a] = side == 0 ? b.min[a] : b.max[a];
                Vector3 dir = Vector3.zero;
                dir[a] = side == 0 ? -1f : 1f;
                float size = HandleUtility.GetHandleSize(pos) * 0.16f;

                Color axis = a == 0 ? Handles.xAxisColor : a == 1 ? Handles.yAxisColor : Handles.zAxisColor;
                using (new Handles.DrawingScope(axis))
                {
                    Handles.DrawLine(pos, pos + dir * size * 2.5f, 2f);
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.Slider(pos + dir * size * 2.5f, dir, size, Handles.CubeHandleCap, 0f) - dir * size * 2.5f;
                    if (!EditorGUI.EndChangeCheck()) continue;
                    if (side == 0) min[a] = moved[a];
                    else max[a] = moved[a];
                    changed = true;
                }
            }
            return changed;
        }

        private static void DrawSizeLabel(DigZone zone)
        {
            Bounds b = zone.WorldBounds;
            Vector3Int c = zone.cells;
            var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, alignment = TextAnchor.MiddleCenter, richText = true };
            Handles.Label(new Vector3(b.center.x, b.max.y, b.center.z) + Vector3.up * HandleUtility.GetHandleSize(b.max) * 0.5f,
                $"<b>{zone.name}</b>\nCells {c.x} × {c.y} × {c.z}\n{b.size.x:0.#} × {b.size.y:0.#} × {b.size.z:0.#} m", style);
        }

        /// <summary>Keeps a baked zone moved with the Move tool on its voxel lattice, then re-bakes on release.</summary>
        private static void SnapToLattice(DigZone zone)
        {
            DigZoneData d = zone.data;
            if (d == null || !d.HasGrid || !d.hasOrigin || !Mathf.Approximately(d.voxelSize, zone.voxelSize)) return;
            Vector3 p = zone.transform.position;
            Vector3 snapped = d.origin + (Vector3)Vector3Int.RoundToInt((p - d.origin) / d.voxelSize) * d.voxelSize;
            if ((snapped - p).sqrMagnitude < 1e-8f) return;
            Undo.RecordObject(zone.transform, "Move Dig Zone");
            zone.transform.position = snapped;
            if ((snapped - d.origin).sqrMagnitude > 1e-8f) Pending.Add(zone);
        }

        private static void ApplyBox(DigZone zone, Vector3 min, Vector3 max, Bounds old)
        {
            float v = zone.voxelSize;
            Vector3 p = zone.transform.position;
            Vector3Int minStep = Vector3Int.RoundToInt((min - p) / v);
            Vector3Int maxStep = Vector3Int.RoundToInt((max - p) / v);
            Vector3Int minCells = new Vector3Int(8, 4, 8);
            Vector3Int cells = zone.cells;

            for (int a = 0; a < 3; a++)
            {
                bool minMoved = Mathf.Abs(min[a] - old.min[a]) > Mathf.Abs(max[a] - old.max[a]);
                int size = Mathf.Max(minCells[a], maxStep[a] - minStep[a]);
                if (minMoved) minStep[a] = cells[a] - size; // keep the max face, move the min face
                else minStep[a] = 0;
                cells[a] = size;
            }

            // Keep the footprint on the terrain (Bake requires it).
            if (zone.terrain != null && zone.terrain.terrainData != null)
            {
                Vector3 tp = zone.terrain.transform.position, ts = zone.terrain.terrainData.size;
                for (int a = 0; a < 3; a += 2)
                {
                    int lo = Mathf.CeilToInt((tp[a] - p[a]) / v - 1e-4f);
                    int hi = Mathf.FloorToInt((tp[a] + ts[a] - p[a]) / v + 1e-4f);
                    if (minStep[a] < lo) { cells[a] -= lo - minStep[a]; minStep[a] = lo; }
                    if (minStep[a] + cells[a] > hi) cells[a] = hi - minStep[a];
                    cells[a] = Mathf.Max(cells[a], minCells[a]);
                }
            }

            if (minStep == Vector3Int.zero && cells == zone.cells) return;
            Undo.RecordObject(zone.transform, "Resize Dig Zone");
            Undo.RecordObject(zone, "Resize Dig Zone");
            zone.transform.position = p + (Vector3)minStep * v;
            zone.cells = cells;
            Pending.Add(zone);
        }
    }
}
