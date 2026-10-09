using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// What a zone's runtime plants when its Tree Prefabs / Detail Prefabs are left empty: the terrain's tree prototypes
    /// and detail mesh prototypes, else the package's example trees and clumps. Also the terrain layer names tools show.
    /// </summary>
    public static class DigSpawnDefaults
    {
        private const string Example = "Packages/com.logiccuteguy.digholeit/Example";

        public static GameObject[] Trees(DigZone zone)
        {
            var list = new List<GameObject>();
            TerrainData td = zone != null && zone.terrain != null ? zone.terrain.terrainData : null;
            if (td != null)
                foreach (TreePrototype p in td.treePrototypes)
                    if (p.prefab != null && !list.Contains(p.prefab)) list.Add(p.prefab);
            if (list.Count == 0) AddExample(list, "Showcase/Oak.prefab", "Showcase/Pine.prefab");
            return list.ToArray();
        }

        public static GameObject[] Details(DigZone zone)
        {
            var list = new List<GameObject>();
            TerrainData td = zone != null && zone.terrain != null ? zone.terrain.terrainData : null;
            if (td != null)
                foreach (DetailPrototype p in td.detailPrototypes)
                    if (p.usePrototypeMesh && p.prototype != null && !list.Contains(p.prototype)) list.Add(p.prototype);
            // Grass-texture details have no prefab to plant: use the example clumps.
            if (list.Count == 0) AddExample(list, "Pen/Grass Clump.prefab", "Pen/Flower Clump.prefab");
            return list.ToArray();
        }

        /// <summary>Names of the zone terrain's layers, in order.</summary>
        public static string[] LayerNames(DigZone zone)
        {
            TerrainData td = zone != null && zone.terrain != null ? zone.terrain.terrainData : null;
            if (td == null) return new string[0];
            TerrainLayer[] layers = td.terrainLayers;
            var names = new string[layers.Length];
            for (int i = 0; i < layers.Length; i++) names[i] = layers[i] != null ? layers[i].name : "Layer " + i;
            return names;
        }

        private static void AddExample(List<GameObject> list, params string[] paths)
        {
            foreach (string p in paths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{Example}/{p}");
                if (go != null) list.Add(go);
            }
        }
    }
}
