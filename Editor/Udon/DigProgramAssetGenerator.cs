using System.IO;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Udon.Editor
{
    /// <summary>
    /// Creates the UdonSharpProgramAsset next to each U# script in this package when the .asset file is missing
    /// (for example after copying the scripts without their .asset files). Never touches an existing file: while the
    /// compiler is being reinstalled the program asset type can fail to load, and overwriting then would save a
    /// program asset without its script reference.
    /// </summary>
    [InitializeOnLoad]
    internal static class DigProgramAssetGenerator
    {
        private const string UdonFolder = "Packages/com.logiccuteguy.digholeit/Runtime/Udon";

        static DigProgramAssetGenerator()
        {
            EditorApplication.delayCall += Generate;
        }

        [MenuItem("Tools/DigHoleIt/Create Missing U# Program Assets")]
        private static void Generate()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Generate;
                return;
            }
            if (!AssetDatabase.IsValidFolder(UdonFolder)) return;

            bool created = false;
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { UdonFolder }))
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                string assetPath = Path.ChangeExtension(scriptPath, ".asset").Replace('\\', '/');
                if (File.Exists(Path.GetFullPath(assetPath))) continue;

                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                System.Type type = script != null ? script.GetClass() : null;
                if (type == null || type.IsAbstract || !typeof(UdonSharpBehaviour).IsAssignableFrom(type)) continue;

                var program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                if (MonoScript.FromScriptableObject(program) == null)
                {
                    // The program asset type has no script asset right now (compiler mid-install); try again later.
                    Object.DestroyImmediate(program);
                    return;
                }
                program.sourceCsScript = script;
                AssetDatabase.CreateAsset(program, assetPath);
                Debug.Log("[DigHoleIt] Created U# program asset " + assetPath);
                created = true;
            }

            if (!created) return;
            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset.CompileAllCsPrograms();
        }
    }
}
