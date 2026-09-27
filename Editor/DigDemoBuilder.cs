using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>Builds a small test scene: a hilly Terrain with two layers and a baked Dig Zone.</summary>
    public static class DigDemoBuilder
    {
        public const string Folder = "Assets/DigHoleIt/Demo";

#if !VRC_SDK_VRCSDK3 || DIGHOLEIT_STANDALONE
        // The standalone runtime only compiles outside VRChat projects (see the Standalone asmdef).
        [MenuItem("Tools/DigHoleIt/Create Standalone Demo Scene")]
        private static void CreateStandalone()
        {
            DigZone zone = Build("DigHoleItStandaloneDemo");
            if (zone == null) return;

            zone.gameObject.AddComponent<DigZoneRuntimeStandalone>();
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = zone.transform.position + new Vector3(-6f, zone.cells.y * zone.voxelSize + 6f, -6f);
                cam.transform.LookAt(zone.WorldBounds.center);
                var tool = cam.gameObject.AddComponent<DigToolStandalone>();
                tool.cam = cam;
                tool.zones = new[] { zone.GetComponent<DigZoneRuntimeStandalone>() };
            }
            Save(zone);
        }
#endif

        /// <summary>Creates the scene, terrain and baked zone. Returns null if the user cancelled.</summary>
        public static DigZone Build(string sceneName)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EnsureFolder();
            // Save first so the baked data asset is named after the scene.
            EditorSceneManager.SaveScene(scene, AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{sceneName}.unity"));

            TerrainLayer grass = MakeLayer("Grass", new Color(0.30f, 0.47f, 0.18f), new Color(0.22f, 0.36f, 0.12f));
            TerrainLayer rock = MakeLayer("Rock", new Color(0.50f, 0.48f, 0.45f), new Color(0.36f, 0.34f, 0.32f));

            var td = new TerrainData { heightmapResolution = 257, alphamapResolution = 128 };
            td.size = new Vector3(96f, 24f, 96f);
            int res = td.heightmapResolution;
            var heights = new float[res, res];
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float u = x / (float)(res - 1), v = y / (float)(res - 1);
                heights[y, x] = 0.25f + 0.12f * Mathf.PerlinNoise(u * 3f + 7.1f, v * 3f + 2.3f) + 0.04f * Mathf.PerlinNoise(u * 11f, v * 11f);
            }
            td.SetHeights(0, 0, heights);
            td.terrainLayers = new[] { grass, rock };

            int ar = td.alphamapResolution;
            var alpha = new float[ar, ar, 2];
            for (int y = 0; y < ar; y++)
            for (int x = 0; x < ar; x++)
            {
                float steep = td.GetSteepness(x / (float)(ar - 1), y / (float)(ar - 1));
                float r = Mathf.Clamp01((steep - 12f) / 10f);
                alpha[y, x, 0] = 1f - r;
                alpha[y, x, 1] = r;
            }
            td.SetAlphamaps(0, 0, alpha);
            AssetDatabase.CreateAsset(td, AssetDatabase.GenerateUniqueAssetPath(Folder + "/DemoTerrain.asset"));

            GameObject tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "Terrain";

            var zgo = new GameObject("DigZone");
            zgo.transform.position = new Vector3(32f, 0f, 32f);
            var zone = zgo.AddComponent<DigZone>();
            zone.terrain = tgo.GetComponent<Terrain>();
            zone.voxelSize = 0.5f;
            zone.cells = new Vector3Int(64, 32, 64);
            zone.chunkCells = 16;
            DigZoneBaker.FitToTerrain(zone);
            if (!DigZoneBaker.Bake(zone)) return null;

            Texture2D dirt = MakeNoiseTexture("Dirt", new Color(0.62f, 0.50f, 0.38f), new Color(0.40f, 0.30f, 0.22f));
            zone.material.SetTexture("_DugTex", dirt);
            zone.material.SetTextureScale("_DugTex", new Vector2(0.5f, 0.5f));
            zone.material.SetColor("_DugColor", Color.white);
            EditorUtility.SetDirty(zone.material);
            return zone;
        }

        public static void Save(DigZone zone)
        {
            EditorSceneManager.SaveScene(zone.gameObject.scene);
            Selection.activeGameObject = zone.gameObject;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/DigHoleIt")) AssetDatabase.CreateFolder("Assets", "DigHoleIt");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/DigHoleIt", "Demo");
        }

        private static TerrainLayer MakeLayer(string name, Color a, Color b)
        {
            string path = $"{Folder}/{name}.terrainlayer";
            var existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (existing != null) return existing;

            var layer = new TerrainLayer { diffuseTexture = MakeNoiseTexture(name, a, b), tileSize = new Vector2(4f, 4f) };
            AssetDatabase.CreateAsset(layer, path);
            return layer;
        }

        private static Texture2D MakeNoiseTexture(string name, Color a, Color b)
        {
            string texPath = $"{Folder}/{name}_Albedo.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (existing != null) return existing;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { name = name + "_Albedo", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Tileable noise from wrapped sine/perlin mix.
                float n = Mathf.PerlinNoise(Mathf.Sin(x * Mathf.PI * 2f / size) * 2f + 5f, Mathf.Sin(y * Mathf.PI * 2f / size) * 2f + 9f) * 0.6f
                        + Mathf.PerlinNoise(Mathf.Cos(x * Mathf.PI * 2f / size) * 4f + 1f, Mathf.Cos(y * Mathf.PI * 2f / size) * 4f + 3f) * 0.4f;
                px[x + y * size] = Color.Lerp(a, b, n);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, texPath);
            return tex;
        }
    }
}
