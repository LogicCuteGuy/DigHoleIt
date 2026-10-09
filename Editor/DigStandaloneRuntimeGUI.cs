#if !VRC_SDK_VRCSDK3 || DIGHOLEIT_STANDALONE
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>Add/Remove button for the plain C# runtime in the Dig Zone inspector.</summary>
    [InitializeOnLoad]
    internal static class DigStandaloneRuntimeGUI
    {
        static DigStandaloneRuntimeGUI()
        {
            DigZoneEditor.RuntimeGUI += Draw;
        }

        private static void Draw(DigZone zone)
        {
            var rt = zone.GetComponent<DigZoneRuntimeStandalone>();
            if (rt == null)
            {
                using (new EditorGUI.DisabledScope(zone.data == null || !zone.data.HasGrid))
                {
                    if (GUILayout.Button(new GUIContent("Add Standalone Runtime",
                            "Adds DigZoneRuntimeStandalone so this zone can be dug at runtime from C# (Dig, Add, Paint).")))
                    {
                        var added = Undo.AddComponent<DigZoneRuntimeStandalone>(zone.gameObject);
                        added.treePrefabs = DigSpawnDefaults.Trees(zone);
                        added.detailPrefabs = DigSpawnDefaults.Details(zone);
                        GUIUtility.ExitGUI();
                    }
                }
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Standalone runtime: DigZoneRuntimeStandalone", EditorStyles.miniLabel);
                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    Undo.DestroyObjectImmediate(rt);
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
#endif
