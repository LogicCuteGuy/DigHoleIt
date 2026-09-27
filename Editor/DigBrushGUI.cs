using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>Brush settings GUI shared by the sculpt tool overlay and the Terrain tool inspectors.</summary>
    public static class DigBrushGUI
    {
        private static readonly GUIContent[] SculptModes =
        {
            new GUIContent("Dig"), new GUIContent("Add"), new GUIContent("Paint"), new GUIContent("Smooth"),
            new GUIContent("Reset", "Brush the voxels back to the terrain they were baked from and wipe their paint."),
        };

        public const string Hints = "A + drag: size   S + drag: strength   [ ]: size (hold to keep going)";

        /// <summary>Mode toolbar over the given modes. Returns the selected mode.</summary>
        public static DigBrushMode ModeToolbar(DigBrushMode mode, DigBrushMode[] modes)
        {
            var contents = new GUIContent[modes.Length];
            int sel = 0;
            for (int i = 0; i < modes.Length; i++)
            {
                contents[i] = SculptModes[(int)modes[i]];
                if (modes[i] == mode) sel = i;
            }
            sel = GUILayout.Toolbar(sel, contents);
            return modes[sel];
        }

        /// <summary>Size, strength and, for the sculpt tool, the shape preset (the Terrain tools use Unity brushes).</summary>
        public static void BrushFields(bool showShape)
        {
            DigBrushSettings.Radius = EditorGUILayout.Slider("Size (radius m)", DigBrushSettings.Radius, DigBrushSettings.MinRadius, DigBrushSettings.MaxRadius);
            DigBrushSettings.Strength = EditorGUILayout.Slider("Strength", DigBrushSettings.Strength, 0.01f, 1f);
            if (showShape)
            {
                DigBrushSettings.Shape = (DigBrushShape)EditorGUILayout.EnumPopup("Shape", DigBrushSettings.Shape);
                if (DigBrushSettings.Shape == DigBrushShape.Custom)
                    DigBrushSettings.CustomMask = (Texture2D)EditorGUILayout.ObjectField("Mask", DigBrushSettings.CustomMask, typeof(Texture2D), false);
            }
            DigBrushSettings.Align = (DigBrushAlign)EditorGUILayout.EnumPopup("Brush Axis", DigBrushSettings.Align);
        }

        /// <summary>
        /// Layer picker with terrain layer thumbnails: Auto, every terrain layer of the zone's terrain (up to
        /// <see cref="DigFormat.MaxTerrainLayers"/>), Dug Soil. Takes and returns a DigFormat paint value.
        /// </summary>
        public static int LayerGrid(int layer, DigZone zone, bool addMode)
        {
            TerrainLayer[] layers = zone != null && zone.terrain != null && zone.terrain.terrainData != null
                ? zone.terrain.terrainData.terrainLayers : new TerrainLayer[0];
            Texture dugTex = zone != null && zone.material != null && zone.material.HasProperty("_DugTex") ? zone.material.GetTexture("_DugTex") : null;
            int count = Mathf.Clamp(layers.Length, 1, DigFormat.MaxTerrainLayers);

            // Button k: 0 Auto, 1..count terrain layers 0..count-1, count+1 Dug Soil.
            var values = new List<int> { DigFormat.LayerAuto };
            var contents = new List<GUIContent>
            {
                new GUIContent(addMode ? "Keep" : "Auto", addMode ? "Added soil keeps the automatic shading." : "Erase paint: back to automatic terrain / dug soil shading."),
            };
            for (int i = 0; i < count; i++)
            {
                TerrainLayer l = i < layers.Length ? layers[i] : null;
                string name = l != null ? l.name : $"Layer {i}";
                values.Add(DigFormat.PaintValue(i));
                contents.Add(new GUIContent(Shorten(name), l != null ? l.diffuseTexture : null, $"Terrain layer {i}: {name}"));
            }
            values.Add(DigFormat.LayerDugSoil);
            contents.Add(new GUIContent("Dug Soil", dugTex, "The zone material's dug soil texture."));

            var style = new GUIStyle(GUI.skin.button)
            {
                imagePosition = ImagePosition.ImageAbove,
                fixedHeight = 52f,
                fontSize = 9,
                padding = new RectOffset(2, 2, 2, 2),
            };
            int current = values.IndexOf(layer);
            int selected = GUILayout.SelectionGrid(Mathf.Max(0, current), contents.ToArray(), 3, style);
            // A layer this terrain lacks stays chosen until another button is clicked.
            return current < 0 && selected == 0 ? layer : values[selected];
        }

        private static string Shorten(string s) => s.Length > 12 ? s.Substring(0, 11) + "…" : s;
    }
}
