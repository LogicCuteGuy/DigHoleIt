using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// Scene-view sculpting for a baked DigZone: dig, add, paint and smooth with shaped brushes.
    /// Shift adds, Ctrl smooths while held. Edits the data asset directly, so the runtime starts from the sculpted state.
    /// The same brush is available from the Terrain component (Paint Terrain > DigHoleIt tools).
    /// </summary>
    [EditorTool("Dig Sculpt", typeof(DigZone))]
    public class DigSculptTool : EditorTool
    {
        private GUIContent _icon;
        private readonly DigBrushStroke _stroke = new DigBrushStroke();
        private readonly List<DigZone> _zones = new List<DigZone>(1);

        public override GUIContent toolbarIcon =>
            _icon ??= new GUIContent(EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSculpt").image, "Dig Sculpt");

        public override void OnWillBeDeactivated() => _stroke.Cancel();

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView)) return;
            var zone = target as DigZone;
            if (zone == null || zone.data == null || !zone.data.HasGrid) return;
            Event e = Event.current;

            DigBrushMode mode = e.shift ? DigBrushMode.Add : e.control ? DigBrushMode.Smooth : DigBrushSettings.SculptMode;
            int layer = mode == DigBrushMode.Paint ? DigBrushSettings.PaintLayer : DigBrushSettings.AddLayer;

            _zones.Clear();
            _zones.Add(zone);
            int id = GUIUtility.GetControlID(FocusType.Passive);
            _stroke.OnSceneGUI(sceneView, _zones, mode, layer, DigBrushMask.ForShape(DigBrushSettings.Shape), id);
        }
    }

    /// <summary>Brush settings panel shown in the Scene view while the Dig Sculpt tool is active.</summary>
    [Overlay(typeof(SceneView), "DigHoleIt Brush", true)]
    internal sealed class DigSculptOverlay : IMGUIOverlay, ITransientOverlay
    {
        private static readonly DigBrushMode[] Modes = { DigBrushMode.Dig, DigBrushMode.Add, DigBrushMode.Paint, DigBrushMode.Smooth, DigBrushMode.Reset };

        public bool visible => ToolManager.activeToolType == typeof(DigSculptTool);

        public override void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(320)))
            {
                DigBrushMode mode = DigBrushGUI.ModeToolbar(DigBrushSettings.SculptMode, Modes);
                DigBrushSettings.SculptMode = mode;

                DigZone zone = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<DigZone>() : null;
                if (mode == DigBrushMode.Paint)
                    DigBrushSettings.PaintLayer = DigBrushGUI.LayerGrid(DigBrushSettings.PaintLayer, zone, false);
                else if (mode == DigBrushMode.Add)
                {
                    GUILayout.Label("Added soil texture", EditorStyles.miniBoldLabel);
                    DigBrushSettings.AddLayer = DigBrushGUI.LayerGrid(DigBrushSettings.AddLayer, zone, true);
                }

                DigBrushGUI.BrushFields(true);
                GUILayout.Label(DigBrushGUI.Hints + "\nShift: add   Ctrl: smooth", EditorStyles.miniLabel);
                DigCredit.Draw();
            }
        }
    }
}
