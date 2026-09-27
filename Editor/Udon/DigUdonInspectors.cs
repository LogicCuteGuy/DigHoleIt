using LogicCuteGuy.DigHoleIt.Editor;
using UdonSharpEditor;
using UnityEditor;

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
    internal sealed class DigToolEditor : DigUdonInspector { }

    [CustomEditor(typeof(DigZoneRuntime)), CanEditMultipleObjects]
    internal sealed class DigZoneRuntimeEditor : DigUdonInspector { }

    [CustomEditor(typeof(DigSync)), CanEditMultipleObjects]
    internal sealed class DigSyncEditor : DigUdonInspector { }
}
