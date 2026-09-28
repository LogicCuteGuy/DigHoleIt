using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// Keeps the trees and details of baked Dig Zones in step with their terrain. Painting trees or details (with the
    /// DigHoleIt tools, Unity's own tools, Mass Place Trees or a script that records undo), editing the prototypes,
    /// the terrain's detail and wind settings, a zone's Trees / Details toggles, and undo rebuild the zones they
    /// change once the stroke ends. See <see cref="DigFoliageBaker"/>.
    /// </summary>
    [InitializeOnLoad]
    public static class DigFoliageSync
    {
        private const double Delay = 0.3;

        private static readonly HashSet<TerrainData> Pending = new HashSet<TerrainData>();
        private static bool _all;
        private static double _due;
        private static bool _scheduled;

        static DigFoliageSync()
        {
            ObjectChangeEvents.changesPublished += OnChanges;
            Undo.undoRedoPerformed += () =>
            {
                _all = true;
                Schedule();
            };
        }

        /// <summary>Checks the zones on this terrain once the current stroke ends.</summary>
        public static void MarkDirty(TerrainData td)
        {
            if (td == null) return;
            Pending.Add(td);
            Schedule();
        }

        private static void OnChanges(ref ObjectChangeEventStream stream)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        stream.GetChangeAssetObjectPropertiesEvent(i, out ChangeAssetObjectPropertiesEventArgs asset);
                        if (EditorUtility.InstanceIDToObject(asset.instanceId) is TerrainData td) MarkDirty(td);
                        break;
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out ChangeGameObjectOrComponentPropertiesEventArgs change);
                        switch (EditorUtility.InstanceIDToObject(change.instanceId))
                        {
                            case Terrain t:
                                MarkDirty(t.terrainData);
                                break;
                            case DigZone z when z.terrain != null:
                                MarkDirty(z.terrain.terrainData);
                                break;
                            case GameObject go when go.TryGetComponent(out Terrain t2):
                                MarkDirty(t2.terrainData);
                                break;
                        }
                        break;
                }
            }
        }

        private static void Schedule()
        {
            _due = EditorApplication.timeSinceStartup + Delay;
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            // Wait until the brush stroke (or slider drag) is over.
            if (EditorApplication.timeSinceStartup < _due || GUIUtility.hotControl != 0) return;
            EditorApplication.update -= Tick;
            _scheduled = false;
            bool all = _all;
            var pending = new HashSet<TerrainData>(Pending);
            _all = false;
            Pending.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode || DigZoneBaker.Busy) return;

            foreach (DigZone zone in Object.FindObjectsByType<DigZone>(FindObjectsSortMode.None))
            {
                if (!IsLive(zone)) continue;
                if (!all && !pending.Contains(zone.terrain.terrainData)) continue;
                DigFoliageBaker.Refresh(zone);
            }
        }

        /// <summary>
        /// A zone baked with trees and details (a zone baked by an older version gets them on its next bake, not behind
        /// the user's back) whose hole is cut in its terrain (a cleared zone shows no foliage of its own).
        /// </summary>
        private static bool IsLive(DigZone zone) =>
            zone.terrain != null && zone.terrain.terrainData != null && zone.data != null && zone.data.HasGrid &&
            zone.data.foliageSignature != 0 && zone.chunkRoot != null && zone.data.cutTerrain == zone.terrain.terrainData;
    }
}
