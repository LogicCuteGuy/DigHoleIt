using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>Remeshes zones whose grid was changed by undo/redo.</summary>
    [InitializeOnLoad]
    internal static class DigSculptUndo
    {
        private static readonly Dictionary<DigZoneData, int> Seen = new Dictionary<DigZoneData, int>();

        static DigSculptUndo()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public static void MarkSeen(DigZoneData data) => Seen[data] = data.gridVersion;

        private static void OnUndoRedo()
        {
            foreach (DigZone zone in Object.FindObjectsByType<DigZone>(FindObjectsSortMode.None))
            {
                DigZoneData data = zone.data;
                if (data == null || !data.HasGrid) continue;
                if (Seen.TryGetValue(data, out int v) && v == data.gridVersion) continue;
                Seen[data] = data.gridVersion;
                DigZoneBaker.RemeshAll(zone);
                DigZoneBaker.NotifyGridChanged(zone);
            }
        }
    }
}
