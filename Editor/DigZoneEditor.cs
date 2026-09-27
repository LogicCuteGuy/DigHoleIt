using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    [CustomEditor(typeof(DigZone))]
    public class DigZoneEditor : UnityEditor.Editor
    {
        /// <summary>Set while the Terrain tools draw this inspector inside the Terrain inspector.</summary>
        internal static bool Embedded;

        /// <summary>
        /// Runtime backends (the VRChat bridge, the standalone runtime) draw their Add/Remove buttons here. Creating
        /// or baking a zone adds no runtime by itself.
        /// </summary>
        public static event Action<DigZone> RuntimeGUI;

        public override void OnInspectorGUI()
        {
            var zone = (DigZone)target;
            bool bakedLighting = zone.bakedLighting;
            float lightmapScale = zone.lightmapScale;
            DrawDefaultInspector();
            if (zone.bakedLighting != bakedLighting || !Mathf.Approximately(zone.lightmapScale, lightmapScale))
                DigZoneBaker.ApplyLighting(zone);

            EditorGUILayout.Space();
            DrawInfo(zone);

            EditorGUILayout.Space();
            DigZoneHandles.AutoRebake = EditorGUILayout.ToggleLeft(new GUIContent("Re-bake after dragging the zone handles",
                "Drag the coloured cubes on the zone's faces in the Scene view to move or resize it. On release the zone re-bakes and keeps its sculpting and paint."),
                DigZoneHandles.AutoRebake);
            DigTerrainSync.Enabled = EditorGUILayout.ToggleLeft(new GUIContent("Follow terrain edits",
                "When you raise, lower or paint the terrain under a baked zone, the zone updates when the stroke ends. Sculpting and paint are kept. Applies to all zones."),
                DigTerrainSync.Enabled);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fit To Terrain")) DigZoneBaker.FitToTerrain(zone);
                if (GUILayout.Button(new GUIContent("Bake", "Bake from the terrain, keeping sculpting and paint."), GUILayout.Height(24)))
                    DigZoneBaker.Bake(zone);
            }

            RuntimeGUI?.Invoke(zone);

            bool hasProbes = zone.GetComponentInChildren<LightProbeGroup>(true) != null;
            if (GUILayout.Button(new GUIContent(hasProbes ? "Update Light Probes" : "Add Light Probes",
                    "Light probes above the terrain over the zone. Chunks changed at runtime lose their lightmap and are lit by " +
                    "light probes, so bake lighting with probes in place.")))
                DigZoneBaker.AddLightProbes(zone);

            using (new EditorGUI.DisabledScope(zone.data == null || !zone.data.HasGrid))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (Embedded)
                {
                    if (GUILayout.Button("Select Zone")) Selection.activeGameObject = zone.gameObject;
                }
                else if (GUILayout.Button("Sculpt Tool")) ToolManager.SetActiveTool<DigSculptTool>();
                if (GUILayout.Button("Remesh All"))
                {
                    DigZoneBaker.RemeshAll(zone);
                    DigZoneBaker.NotifyGridChanged(zone);
                }
                if (GUILayout.Button("Apply Material"))
                    DigZoneBaker.ApplyMaterial(zone, zone.data, zone.material);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(zone.data == null))
                {
                    if (GUILayout.Button(new GUIContent("Reset To Terrain", "Bake again and discard all sculpting and paint.")) &&
                        EditorUtility.DisplayDialog("DigHoleIt", "Discard all sculpting and paint of this zone and bake it from the terrain again?", "Reset", "Cancel"))
                    {
                        DigZoneBaker.Bake(zone, false);
                    }
                    if (GUILayout.Button(new GUIContent("Clear", "Remove the chunk objects and fill the terrain hole back in.")) &&
                        EditorUtility.DisplayDialog("DigHoleIt", "Remove the chunk objects and fill the terrain hole back in? Sculpted data stays in the data asset until the next Bake.", "Clear", "Cancel"))
                    {
                        DigZoneBaker.Clear(zone);
                    }
                }
                if (GUILayout.Button(new GUIContent("Delete Zone", "Fill the terrain hole back in and delete this zone (undoable).")) &&
                    EditorUtility.DisplayDialog("DigHoleIt", $"Delete '{zone.name}' and fill its terrain hole back in?", "Delete", "Cancel"))
                {
                    DigTerrainHoles.DeleteZone(zone);
                    GUIUtility.ExitGUI();
                }
            }

            // Inside the Terrain inspector the tool draws the credit once, under all its zones.
            if (!Embedded) DigCredit.Draw();
        }

        private static void DrawInfo(DigZone zone)
        {
            Vector3Int c = zone.cells;
            long samples = (long)(c.x + 1) * (c.y + 1) * (c.z + 1);
            int chunks = Mathf.CeilToInt(c.x / (float)zone.chunkCells) * Mathf.CeilToInt(c.y / (float)zone.chunkCells) * Mathf.CeilToInt(c.z / (float)zone.chunkCells);
            Vector3 size = (Vector3)c * zone.voxelSize;

            string msg = $"Size {size.x:0.#} x {size.y:0.#} x {size.z:0.#} m   Grid {Megabytes(samples)}   Chunks {chunks}";
            DigZoneData d = zone.data;
            if (d == null || !d.HasGrid) msg += "\nNot baked yet.";
            else
            {
                msg += $"\nStored compressed: {Megabytes(d.StoredBytes)}";
                bool everyChunk = (zone.chunkIds == null || zone.chunkIds.Length == 0) && zone.chunkFilters != null && zone.chunkFilters.Length > 0;
                if (everyChunk)
                    msg += $"\nEvery chunk has an object ({zone.chunkFilters.Length}), as older versions baked it. Bake again to keep only chunks with a surface.";
                else if (zone.chunkFilters != null)
                    msg += $"\nChunk objects: {zone.chunkFilters.Length} (only chunks with a surface; digging adds more as needed)";
                if (!DigZoneBaker.IsUpToDate(zone))
                {
                    msg += DigZoneBaker.CanKeepSculpt(zone, out string reason)
                        ? "\nSettings changed since the last bake. Bake again (sculpting and paint are kept)."
                        : $"\nSettings changed since the last bake. Baking now discards the sculpting: {reason}.";
                }
            }
            msg += "\nOrange box: the zone. Drag the coloured cubes on its faces in the Scene view to resize it." +
                   "\nGreen box: the diggable area (the zone minus Border Voxels). Digging and sculpting only happen inside it.";
            if (d != null && d.HasStash)
                msg += $"\n{d.StashCount} sculpted samples are stored outside the zone; they come back when the zone covers them again.";
            EditorGUILayout.HelpBox(msg, MessageType.Info);

            int border = Mathf.Max(2, zone.borderVoxels);
            if (c.y - 1 - border < 4)
                EditorGUILayout.HelpBox($"Border Voxels ({zone.borderVoxels}) leaves only {Mathf.Max(0, c.y - 1 - border)} voxels of diggable depth out of {c.y}. " +
                    "Border Voxels is the untouched margin at the hole edge and above the floor; 2-3 is typical.", MessageType.Warning);

            if (samples > DigZone.MaxSamples)
                EditorGUILayout.HelpBox($"Too large to bake: {samples / 1_000_000} million samples, the limit is {DigZone.MaxSamples / 1_000_000} million. " +
                    "Make the zone smaller, split it into several zones, or use bigger voxels.", MessageType.Error);
            else if (samples > 16_000_000)
                EditorGUILayout.HelpBox("Large grid: baking and sculpting it in the editor take a while. Players decode only the chunks they dig, " +
                    "but every chunk with a surface is a draw call and a collider. Put zones only where players dig, especially for Quest.", MessageType.Warning);
            if (zone.transform.rotation != Quaternion.identity || zone.transform.lossyScale != Vector3.one)
                EditorGUILayout.HelpBox("Rotation and scale are ignored; Bake resets them.", MessageType.Warning);
        }

        private static string Megabytes(long bytes) =>
            bytes >= 1024 * 1024 ? $"{bytes / (1024f * 1024f):0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";

        private void OnSceneGUI()
        {
            if (ToolManager.activeToolType == typeof(DigSculptTool)) return;
            DigZoneHandles.OnSceneGUI((DigZone)target);
        }
    }
}
