using LogicCuteGuy.DigHoleIt.Editor;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Udon.Editor
{
    /// <summary>UdonSharp's inspector with the DigHoleIt credit line under it.</summary>
    public abstract class DigUdonInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(targets)) return;
            DrawDefaultInspector();
            DigCredit.Draw();
        }
    }

    [CustomEditor(typeof(DigTool)), CanEditMultipleObjects]
    internal sealed class DigToolEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(targets)) return;
            if (GUILayout.Button(new GUIContent("Refresh Zones",
                    "Sets Zones to every Dig Zone in the scene and Layer Names to the first zone terrain's layers. Use after adding or removing zones.")))
                foreach (Object t in targets) DigUdonBridge.RefreshTool((DigTool)t);
            DrawDefaultInspector();
            DigCredit.Draw();
        }
    }

    [CustomEditor(typeof(DigZoneRuntime)), CanEditMultipleObjects]
    internal sealed class DigZoneRuntimeEditor : DigUdonInspector { }

    [CustomEditor(typeof(DigSync)), CanEditMultipleObjects]
    internal sealed class DigSyncEditor : DigUdonInspector { }
}
