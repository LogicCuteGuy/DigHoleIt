using System.Collections.Generic;
using LogicCuteGuy.DigHoleIt.Editor;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LogicCuteGuy.DigHoleIt.Udon.Editor
{
    /// <summary>
    /// Adds the UdonSharp runtime (DigZoneRuntime + DigSync) to a Dig Zone on request, and keeps it in step with the
    /// zone's baked data whenever the zone is baked or sculpted. The grids are copied as per-chunk run-length encoded
    /// streams (DigChunkPacker), so players decode only the chunks that get dug.
    /// The DigZone component itself is IEditorOnly and gets stripped from VRChat builds.
    /// </summary>
    [InitializeOnLoad]
    public static class DigUdonBridge
    {
        static DigUdonBridge()
        {
            DigZoneBaker.Baked += OnBaked;
            DigZoneBaker.GridChanged += OnGridChanged;
            DigFoliageBaker.Built += OnFoliageBuilt;
            DigZoneEditor.RuntimeGUI += DrawRuntimeGUI;
            EditorSceneManager.sceneOpened += (scene, mode) => UpgradeRuntimes();
            EditorApplication.delayCall += UpgradeRuntimes;
            // Pens and zones dropped in since: give them their defaults before they run or get uploaded.
            EditorSceneManager.sceneSaving += (scene, path) => FillDefaults(scene);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode) return;
                for (int i = 0; i < EditorSceneManager.sceneCount; i++) FillDefaults(EditorSceneManager.GetSceneAt(i));
            };
        }

        public static bool HasRuntime(DigZone zone) => zone.GetComponent<DigZoneRuntime>() != null;

        /// <summary>Adds DigZoneRuntime and a child DigSync to a baked zone and fills them in (undoable).</summary>
        public static DigZoneRuntime AddRuntime(DigZone zone)
        {
            DigZoneRuntime rt = zone.GetComponent<DigZoneRuntime>();
            if (rt == null) rt = UdonSharpUndo.AddComponent<DigZoneRuntime>(zone.gameObject);

            DigSync sync = zone.GetComponentInChildren<DigSync>(true);
            if (sync == null)
            {
                var go = new GameObject("DigSync");
                Undo.RegisterCreatedObjectUndo(go, "Add VRChat Runtime");
                go.transform.SetParent(zone.transform, false);
                sync = UdonSharpUndo.AddComponent<DigSync>(go);
            }
            Undo.RecordObject(sync, "Add VRChat Runtime");
            sync.zone = rt;
            Commit(sync);

            Fill(zone, rt, true);
            Undo.RecordObject(rt, "Add VRChat Runtime");
            rt.sync = sync;
            Commit(rt);
            return rt;
        }

        /// <summary>Removes DigZoneRuntime and the zone's DigSync (undoable).</summary>
        public static void RemoveRuntime(DigZone zone)
        {
            DigSync sync = zone.GetComponentInChildren<DigSync>(true);
            if (sync != null)
            {
                GameObject go = sync.gameObject;
                bool own = go != zone.gameObject && go.GetComponents<Component>().Length <= 3; // Transform, proxy, UdonBehaviour
                UdonSharpUndo.DestroyImmediate(sync);
                if (own) Undo.DestroyObjectImmediate(go);
            }
            DigZoneRuntime rt = zone.GetComponent<DigZoneRuntime>();
            if (rt != null) UdonSharpUndo.DestroyImmediate(rt);
        }

        private static void DrawRuntimeGUI(DigZone zone)
        {
            bool baked = zone.data != null && zone.data.HasGrid;
            if (!HasRuntime(zone))
            {
                using (new EditorGUI.DisabledScope(!baked))
                {
                    if (GUILayout.Button(new GUIContent("Add VRChat Runtime",
                            "Adds DigZoneRuntime and DigSync so players can dig this zone in VRChat. Bakes and sculpting keep them up to date.")))
                    {
                        AddRuntime(zone);
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.HelpBox(baked
                    ? "No VRChat runtime: players can't dig this zone yet. Click Add VRChat Runtime."
                    : "Bake the zone, then click Add VRChat Runtime so players can dig it.", MessageType.None);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("VRChat runtime: DigZoneRuntime + DigSync", EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("Remove", "Remove DigZoneRuntime and DigSync from this zone."), EditorStyles.miniButton, GUILayout.Width(70)) &&
                    EditorUtility.DisplayDialog("DigHoleIt", $"Remove the VRChat runtime from '{zone.name}'? Players won't be able to dig it.", "Remove", "Cancel"))
                {
                    RemoveRuntime(zone);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void OnBaked(DigZone zone)
        {
            DigZoneRuntime rt = zone.GetComponent<DigZoneRuntime>();
            if (rt == null || zone.data == null || !zone.data.HasGrid) return;
            Fill(zone, rt, true);
            Commit(rt);
        }

        private static void OnGridChanged(DigZone zone)
        {
            DigZoneRuntime rt = zone.GetComponent<DigZoneRuntime>();
            if (rt == null || zone.data == null || !zone.data.HasGrid) return;
            // A terrain sync is redone after undo, so it records none.
            if (!DigZoneBaker.Busy) Undo.RecordObject(rt, "Dig Sculpt");
            CopyGrids(rt, zone.data);
            CopyChunkObjects(zone, rt); // sculpting may have given empty chunks an object
            CopyFoliage(zone, rt);
            Commit(rt);
        }

        private static void OnFoliageBuilt(DigZone zone)
        {
            DigZoneRuntime rt = zone.GetComponent<DigZoneRuntime>();
            if (rt == null) return;
            // Rebuilt from the terrain (derived data, redone after undo), so no undo of its own.
            CopyFoliage(zone, rt);
            Commit(rt);
        }

        /// <summary>Copies everything the runtime needs from the zone's baked data.</summary>
        private static void Fill(DigZone zone, DigZoneRuntime rt, bool recordUndo)
        {
            DigZoneData data = zone.data;
            if (data == null || !data.HasGrid) return;
            if (recordUndo) Undo.RecordObject(rt, "Bake Dig Zone");
            CopyGrids(rt, data);
            rt.nx = data.nx;
            rt.ny = data.ny;
            rt.nz = data.nz;
            rt.voxelSize = data.voxelSize;
            rt.chunkCells = data.chunkCells;
            rt.chunksX = data.ChunksX;
            rt.chunksY = data.ChunksY;
            rt.chunksZ = data.ChunksZ;
            rt.editBox = (int[])data.editBox.Clone();
            rt.maxBrushRadius = zone.maxBrushRadius;
            CopyChunkObjects(zone, rt);
            CopyFoliage(zone, rt);
            FillPrefabs(zone, rt);
        }

        /// <summary>Gives a runtime with empty Tree Prefabs / Detail Prefabs the defaults (DigSpawnDefaults). True if it changed.</summary>
        private static bool FillPrefabs(DigZone zone, DigZoneRuntime rt)
        {
            bool changed = false;
            if (rt.treePrefabs == null || rt.treePrefabs.Length == 0)
            {
                rt.treePrefabs = DigSpawnDefaults.Trees(zone);
                changed = rt.treePrefabs.Length > 0;
            }
            if (rt.detailPrefabs == null || rt.detailPrefabs.Length == 0)
            {
                rt.detailPrefabs = DigSpawnDefaults.Details(zone);
                changed |= rt.detailPrefabs.Length > 0;
            }
            return changed;
        }

        /// <summary>
        /// Fills empty prefab lists on the scene's zone runtimes, and empty Zones and Layer Names on its DigTools (the
        /// zones of the scene; the names of the first zone's terrain layers). Zones that were deleted drop out of Zones.
        /// </summary>
        private static void FillDefaults(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            var runtimes = new List<DigZoneRuntime>();
            var tools = new List<DigTool>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                runtimes.AddRange(root.GetComponentsInChildren<DigZoneRuntime>(true));
                tools.AddRange(root.GetComponentsInChildren<DigTool>(true));
            }

            foreach (DigZoneRuntime rt in runtimes)
            {
                DigZone zone = rt.GetComponent<DigZone>();
                if (zone == null) continue;
                Undo.RecordObject(rt, "DigHoleIt Defaults");
                if (FillPrefabs(zone, rt)) Commit(rt);
            }

            if (runtimes.Count == 0) return;
            DigZone first = runtimes[0].GetComponent<DigZone>();
            foreach (DigTool tool in tools)
            {
                bool changed = false;
                Undo.RecordObject(tool, "DigHoleIt Defaults");
                DigZoneRuntime[] kept = tool.zones == null ? new DigZoneRuntime[0] : System.Array.FindAll(tool.zones, z => z != null);
                if (kept.Length == 0) kept = runtimes.ToArray();
                if (tool.zones == null || kept.Length != tool.zones.Length)
                {
                    tool.zones = kept;
                    changed = true;
                }
                if ((tool.layerNames == null || tool.layerNames.Length == 0) && first != null)
                {
                    tool.layerNames = DigSpawnDefaults.LayerNames(first);
                    changed |= tool.layerNames.Length > 0;
                }
                if (changed) Commit(tool);
            }
        }

        /// <summary>
        /// Points <paramref name="tool"/> at every zone in its scene and the first zone terrain's layer names, for after
        /// zones were added or removed (undoable).
        /// </summary>
        public static void RefreshTool(DigTool tool)
        {
            var runtimes = new List<DigZoneRuntime>();
            foreach (GameObject root in tool.gameObject.scene.GetRootGameObjects())
                runtimes.AddRange(root.GetComponentsInChildren<DigZoneRuntime>(true));
            Undo.RecordObject(tool, "Refresh Dig Tool Zones");
            tool.zones = runtimes.ToArray();
            DigZone first = runtimes.Count > 0 ? runtimes[0].GetComponent<DigZone>() : null;
            if (first != null) tool.layerNames = DigSpawnDefaults.LayerNames(first);
            Commit(tool);
        }

        /// <summary>Copies the zone's terrain trees and details (see DigFoliageBaker).</summary>
        private static void CopyFoliage(DigZone zone, DigZoneRuntime rt)
        {
            DigZoneData data = zone.data;
            rt.foliageMask = data != null ? data.foliageMask : null;
            rt.detailRenderers = zone.detailRenderers ?? new MeshRenderer[0];
            rt.treeObjects = zone.treeObjects ?? new GameObject[0];
            rt.treeAnchors = zone.treeAnchors ?? new Vector3[0];
            rt.treeBuckets = zone.treeBuckets ?? new int[0];
            rt.surfaceDetailAnchors = zone.surfaceDetailAnchors ?? new Vector3[0];
            rt.surfaceDetailBuckets = zone.surfaceDetailBuckets ?? new int[0];
        }

        private static void CopyGrids(DigZoneRuntime rt, DigZoneData data)
        {
            data.GetChunkPacks(out byte[] grid, out int[] gridOffsets, out byte[] paint, out int[] paintOffsets);
            rt.chunkRle = grid;
            rt.chunkOffsets = gridOffsets;
            // Unpainted zones store no paint in the scene; the runtime allocates it where something is painted.
            rt.hasPaint = paint != null;
            rt.paintRle = paint ?? new byte[0];
            rt.paintOffsets = paintOffsets ?? new int[0];
        }

        /// <summary>Copies the chunk objects that exist, as a compact list (zones baked by 0.4 have one per chunk).</summary>
        private static void CopyChunkObjects(DigZone zone, DigZoneRuntime rt)
        {
            int count = zone.data != null ? zone.data.ChunkCount : 0;
            var ids = new List<int>();
            var filters = new List<MeshFilter>();
            var renderers = new List<MeshRenderer>();
            var colliders = new List<MeshCollider>();
            for (int ci = 0; ci < count; ci++)
            {
                int slot = zone.ChunkSlot(ci);
                if (slot < 0 || zone.chunkFilters[slot] == null) continue;
                ids.Add(ci);
                filters.Add(zone.chunkFilters[slot]);
                renderers.Add(slot < zone.chunkRenderers?.Length ? zone.chunkRenderers[slot] : null);
                colliders.Add(slot < zone.chunkColliders?.Length ? zone.chunkColliders[slot] : null);
            }
            rt.chunkIds = ids.ToArray();
            rt.chunkFilters = filters.ToArray();
            rt.chunkRenderers = renderers.ToArray();
            rt.chunkColliders = colliders.ToArray();
            rt.chunkTemplate = zone.chunkTemplate;
        }

        /// <summary>Runtimes saved by 0.4 or earlier hold whole-grid streams; give them the per-chunk ones.</summary>
        private static void UpgradeRuntimes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (DigZoneRuntime rt in Object.FindObjectsByType<DigZoneRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rt.chunkOffsets != null && rt.chunkOffsets.Length > 0) continue;
                DigZone zone = rt.GetComponent<DigZone>();
                if (zone == null || zone.data == null || !zone.data.HasGrid) continue;
                Fill(zone, rt, false);
                Commit(rt);
                EditorSceneManager.MarkSceneDirty(rt.gameObject.scene);
                Debug.Log($"[DigHoleIt] Updated the VRChat runtime of '{zone.name}' to the per-chunk grid format.", zone);
            }
            for (int i = 0; i < EditorSceneManager.sceneCount; i++) FillDefaults(EditorSceneManager.GetSceneAt(i));
        }

        private static void Commit(UdonSharpBehaviour behaviour)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            EditorUtility.SetDirty(behaviour);
            PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
        }
    }
}
