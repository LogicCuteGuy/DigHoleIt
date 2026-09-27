using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>The "DigHoleIt by LogicCuteGuy" line at the bottom of every DigHoleIt inspector and panel.</summary>
    public static class DigCredit
    {
        public const string Author = "LogicCuteGuy";
        public const string Url = "https://github.com/LogicCuteGuy/DigHoleIt";

        private static GUIContent _content;
        private static GUIStyle _style;

        private static GUIContent Content
        {
            get
            {
                if (_content != null) return _content;
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DigCredit).Assembly);
                string version = package != null ? " " + package.version : "";
                _content = new GUIContent($"DigHoleIt{version}  ·  by {Author}", Url);
                return _content;
            }
        }

        private static GUIStyle Style =>
            _style ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel) { richText = false };

        /// <summary>Draws the credit line; clicking it opens the project page.</summary>
        public static void Draw()
        {
            EditorGUILayout.Space(4);
            Rect line = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.25f));
            Rect r = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            if (GUI.Button(r, Content, Style)) Application.OpenURL(Url);
        }

        /// <summary>A default inspector with the credit line under it, for components without their own inspector.</summary>
        public abstract class Inspector : UnityEditor.Editor
        {
            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();
                Draw();
            }
        }
    }

    [CustomEditor(typeof(DigZoneData))]
    internal sealed class DigZoneDataEditor : DigCredit.Inspector { }

#if !VRC_SDK_VRCSDK3 || DIGHOLEIT_STANDALONE
    [CustomEditor(typeof(DigZoneRuntimeStandalone))]
    internal sealed class DigZoneRuntimeStandaloneEditor : DigCredit.Inspector { }

    [CustomEditor(typeof(DigToolStandalone))]
    internal sealed class DigToolStandaloneEditor : DigCredit.Inspector { }
#endif
}
