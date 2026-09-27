using LogicCuteGuy.DigHoleIt.Editor;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Udon.Editor
{
    /// <summary>
    /// Copies baked DigZone data into the UdonSharp runtime (DigZoneRuntime + DigSync) whenever the zone is baked
    /// or sculpted. The DigZone component itself is IEditorOnly and gets stripped from VRChat builds.
    /// </summary>
    [InitializeOnLoad]
    internal static class DigUdonBridge
    {
        static DigUdonBridge()
        {
            DigZoneBaker.Baked += OnBaked;
            DigZoneBaker.GridChanged += OnGridChanged;
        }

        private static void OnBaked(DigZone zone)
        {
            DigZoneData data = zone.data;
            if (data == null || !data.HasGrid) return;

            DigZoneRuntime rt = zone.GetComponent<DigZoneRuntime>();
            if (rt == null) rt = zone.gameObject.AddUdonSharpComponent<DigZoneRuntime>();
            Undo.RecordObject(rt, "Bake Dig Zone");

            rt.grid = (byte[])data.grid.Clone();
            CopyPaint(rt, data);
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
            rt.chunkFilters = zone.chunkFilters;
            rt.chunkRenderers = zone.chunkRenderers;
            rt.chunkColliders = zone.chunkColliders;

            DigSync sync = zone.GetComponentInChildren<DigSync>(true);
            if (sync == null)
            {
                var go = new GameObject("DigSync");
                Undo.RegisterCreatedObjectUndo(go, "Bake Dig Zone");
                go.transform.SetParent(zone.transform, false);
                sync = go.AddUdonSharpComponent<DigSync>();
            }
            Undo.RecordObject(sync, "Bake Dig Zone");
            sync.zone = rt;
            rt.sync = sync;

            Commit(rt);
            Commit(sync);
        }

        private static void OnGridChanged(DigZone zone)
        {
            DigZoneRuntime rt = zone.GetComponent<DigZoneRuntime>();
            if (rt == null || zone.data == null || !zone.data.HasGrid) return;
            // A terrain sync is redone after undo, so it records none.
            if (!DigZoneBaker.Busy) Undo.RecordObject(rt, "Dig Sculpt");
            rt.grid = (byte[])zone.data.grid.Clone();
            CopyPaint(rt, zone.data);
            Commit(rt);
        }

        private static void CopyPaint(DigZoneRuntime rt, DigZoneData data)
        {
            // Unpainted zones store no paint grid in the scene; the runtime allocates an empty one.
            rt.hasPaint = data.HasPaint;
            rt.paint = rt.hasPaint ? (byte[])data.paint.Clone() : new byte[0];
        }

        private static void Commit(UdonSharpBehaviour behaviour)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            EditorUtility.SetDirty(behaviour);
            PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
        }
    }
}
