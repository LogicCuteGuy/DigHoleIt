using System.Collections.Generic;
using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>Finds the baked Dig Zones that sit on a terrain (cached until the hierarchy changes).</summary>
    [InitializeOnLoad]
    internal static class DigTerrainZones
    {
        private static readonly Dictionary<Terrain, List<DigZone>> Cache = new Dictionary<Terrain, List<DigZone>>();

        static DigTerrainZones()
        {
            EditorApplication.hierarchyChanged += Cache.Clear;
            Undo.undoRedoPerformed += Cache.Clear;
        }

        public static List<DigZone> For(Terrain terrain)
        {
            if (Cache.TryGetValue(terrain, out List<DigZone> list) && !list.Exists(z => z == null)) return list;
            list = new List<DigZone>();
            foreach (DigZone z in Object.FindObjectsByType<DigZone>(FindObjectsSortMode.None))
                if (z.terrain == terrain) list.Add(z);
            Cache[terrain] = list;
            return list;
        }

        public static void Invalidate() => Cache.Clear();

        private static readonly Dictionary<DigZone, UnityEditor.Editor> Editors = new Dictionary<DigZone, UnityEditor.Editor>();

        /// <summary>
        /// Every Dig Zone on this terrain with its full settings (the zone inspector, embedded), plus Create Dig Zone
        /// and Fix Leftover Holes. Returns the first baked zone (for layer names).
        /// </summary>
        public static DigZone StatusGUI(Terrain terrain)
        {
            List<DigZone> zones = For(terrain);
            DigZone firstBaked = zones.Find(z => z.data != null && z.data.HasGrid);

            EditorGUILayout.LabelField("Dig Zones", EditorStyles.boldLabel);
            if (zones.Count == 0)
                EditorGUILayout.HelpBox("No Dig Zone uses this terrain yet. A Dig Zone is the box of terrain that becomes diggable voxels.", MessageType.Info);

            foreach (DigZone zone in zones)
            {
                if (zone == null) continue;
                string key = "DigHoleIt.TerrainZoneFoldout." + zone.GetInstanceID();
                bool baked = zone.data != null && zone.data.HasGrid;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    bool open = EditorGUILayout.Foldout(SessionState.GetBool(key, zones.Count == 1), $"{zone.name}{(baked ? "" : "  (not baked)")}", true);
                    SessionState.SetBool(key, open);
                    if (!open) continue;

                    Editors.TryGetValue(zone, out UnityEditor.Editor editor);
                    UnityEditor.Editor.CreateCachedEditor(zone, typeof(DigZoneEditor), ref editor);
                    Editors[zone] = editor;
                    DigZoneEditor.Embedded = true;
                    try { editor.OnInspectorGUI(); }
                    finally { DigZoneEditor.Embedded = false; }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Dig Zone")) CreateZone(terrain);
                List<DigZoneData> leftovers = DigTerrainHoles.FindLeftovers(terrain.terrainData);
                using (new EditorGUI.DisabledScope(leftovers.Count == 0))
                {
                    var label = new GUIContent(leftovers.Count > 0 ? $"Fix Leftover Holes ({leftovers.Count})" : "No Leftover Holes",
                        "Fill the terrain holes of Dig Zones that were deleted. Holes of zones in scenes that are not open count as leftovers too.");
                    if (GUILayout.Button(label) &&
                        EditorUtility.DisplayDialog("DigHoleIt", $"Fill the terrain holes left by {leftovers.Count} deleted Dig Zone(s)?\n\nIf another scene that is not open has a Dig Zone on this same terrain, its hole would be filled too.", "Fill", "Cancel"))
                    {
                        int n = DigTerrainHoles.FixLeftoverHoles(terrain);
                        Debug.Log($"[DigHoleIt] Filled the holes of {n} deleted Dig Zone(s).", terrain);
                    }
                }
            }
            return firstBaked;
        }

        /// <summary>Zone boxes, diggable areas and resize handles for every zone on the terrain.</summary>
        public static void SceneGUI(Terrain terrain)
        {
            foreach (DigZone zone in For(terrain))
                if (zone != null) DigZoneHandles.OnSceneGUI(zone);
        }

        /// <summary>Creates a 32 m zone at the terrain centre (or scene view pivot), fits and bakes it.</summary>
        private static void CreateZone(Terrain terrain)
        {
            TerrainData td = terrain.terrainData;
            Vector3 tp = terrain.transform.position;
            var go = new GameObject("Dig Zone");
            Undo.RegisterCreatedObjectUndo(go, "Create Dig Zone");
            var zone = go.AddComponent<DigZone>();
            zone.terrain = terrain;

            float size = Mathf.Min(32f, td.size.x * 0.5f, td.size.z * 0.5f);
            int cells = Mathf.Max(16, Mathf.RoundToInt(size / zone.voxelSize));
            zone.cells = new Vector3Int(cells, zone.cells.y, cells);

            Vector3 centre = tp + td.size * 0.5f;
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                Vector3 pivot = sv.pivot;
                if (pivot.x > tp.x && pivot.z > tp.z && pivot.x < tp.x + td.size.x && pivot.z < tp.z + td.size.z) centre = pivot;
            }
            float half = cells * zone.voxelSize * 0.5f;
            float x = Mathf.Clamp(centre.x - half, tp.x, tp.x + td.size.x - 2f * half);
            float z = Mathf.Clamp(centre.z - half, tp.z, tp.z + td.size.z - 2f * half);
            go.transform.position = new Vector3(Mathf.Round(x), tp.y, Mathf.Round(z));

            DigZoneBaker.FitToTerrain(zone);
            Invalidate();
            DigZoneBaker.Bake(zone);
            Invalidate();
        }
    }

    /// <summary>Terrain > Paint Terrain > "DigHoleIt: Dig Voxels". Dig, add and smooth with Unity's terrain brushes.</summary>
    internal sealed class DigTerrainSculptTool : TerrainPaintTool<DigTerrainSculptTool>
    {
        private static readonly DigBrushMode[] Modes = { DigBrushMode.Dig, DigBrushMode.Add, DigBrushMode.Smooth, DigBrushMode.Reset };
        private readonly DigBrushStroke _stroke = new DigBrushStroke();

        private static DigBrushMode Mode
        {
            get => (DigBrushMode)EditorPrefs.GetInt("DigHoleIt.Brush.TerrainMode", (int)DigBrushMode.Dig);
            set => EditorPrefs.SetInt("DigHoleIt.Brush.TerrainMode", (int)value);
        }

        public override string GetName() => "DigHoleIt: Dig Voxels";

        public override string GetDescription() =>
            "Dig holes, tunnels and caves or add soil inside Dig Zones on this terrain.\n" +
            "Click and drag to dig. Shift: add. Ctrl: smooth.\n" + DigBrushGUI.Hints;

        public override void OnExitToolMode() => _stroke.Cancel();

        public override void OnInspectorGUI(Terrain terrain, IOnInspectorGUI editContext)
        {
            DigZone zone = DigTerrainZones.StatusGUI(terrain);
            EditorGUILayout.Space();
            Mode = DigBrushGUI.ModeToolbar(Mode, Modes);
            if (Mode == DigBrushMode.Add)
            {
                GUILayout.Label("Added soil texture", EditorStyles.miniBoldLabel);
                DigBrushSettings.AddLayer = DigBrushGUI.LayerGrid(DigBrushSettings.AddLayer, zone, true);
            }
            DigBrushGUI.BrushFields(false);
            editContext.ShowBrushesGUI(5, BrushGUIEditFlags.Select, 0);
        }

        public override void OnSceneGUI(Terrain terrain, IOnSceneGUI editContext)
        {
            Event e = Event.current;
            DigBrushMode mode = e.shift ? DigBrushMode.Add : e.control ? DigBrushMode.Smooth : Mode;
            DigTerrainZones.SceneGUI(terrain);
            _stroke.OnSceneGUI(editContext.sceneView, DigTerrainZones.For(terrain), mode, DigBrushSettings.AddLayer,
                DigBrushMask.ForTexture(editContext.brushTexture), editContext.controlId);
        }

        public override void OnRenderBrushPreview(Terrain terrain, IOnSceneGUI editContext) { }

        public override bool OnPaint(Terrain terrain, IOnPaint editContext) => false;
    }

    /// <summary>Terrain > Paint Terrain > "DigHoleIt: Paint Voxels". Paints terrain layers onto the voxel surface.</summary>
    internal sealed class DigTerrainPaintTool : TerrainPaintTool<DigTerrainPaintTool>
    {
        private readonly DigBrushStroke _stroke = new DigBrushStroke();

        public override string GetName() => "DigHoleIt: Paint Voxels";

        public override string GetDescription() =>
            "Paint terrain layers or dug soil onto tunnel walls, cave floors and mounds inside Dig Zones.\n" +
            "Strength thins the paint out so layers blend.\n" + DigBrushGUI.Hints;

        public override void OnExitToolMode() => _stroke.Cancel();

        public override void OnInspectorGUI(Terrain terrain, IOnInspectorGUI editContext)
        {
            DigZone zone = DigTerrainZones.StatusGUI(terrain);
            EditorGUILayout.Space();
            DigBrushSettings.PaintLayer = DigBrushGUI.LayerGrid(DigBrushSettings.PaintLayer, zone, false);
            DigBrushGUI.BrushFields(false);
            editContext.ShowBrushesGUI(5, BrushGUIEditFlags.Select, 0);
        }

        public override void OnSceneGUI(Terrain terrain, IOnSceneGUI editContext)
        {
            DigTerrainZones.SceneGUI(terrain);
            _stroke.OnSceneGUI(editContext.sceneView, DigTerrainZones.For(terrain), DigBrushMode.Paint, DigBrushSettings.PaintLayer,
                DigBrushMask.ForTexture(editContext.brushTexture), editContext.controlId);
        }

        public override void OnRenderBrushPreview(Terrain terrain, IOnSceneGUI editContext) { }

        public override bool OnPaint(Terrain terrain, IOnPaint editContext) => false;
    }
}
