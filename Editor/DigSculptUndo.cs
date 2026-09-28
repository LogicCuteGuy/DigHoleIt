using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// Remeshes zones whose grid was changed by undo/redo: only the chunks around the samples that differ from the grid
    /// the meshes were made from.
    /// </summary>
    [InitializeOnLoad]
    internal static class DigSculptUndo
    {
        /// <summary>The grid and paint arrays a zone's meshes were last made from.</summary>
        private struct State
        {
            public int Version;
            public byte[] Grid;
            public byte[] Paint;
        }

        private static readonly Dictionary<DigZoneData, State> Seen = new Dictionary<DigZoneData, State>();

        static DigSculptUndo()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.delayCall += SeeAll;
            EditorSceneManager.sceneOpened += (scene, mode) => SeeAll();
        }

        public static void MarkSeen(DigZoneData data) =>
            Seen[data] = new State { Version = data.gridVersion, Grid = data.grid, Paint = data.paint };

        /// <summary>Zones not seen yet: their meshes match the grid they were loaded with.</summary>
        private static void SeeAll()
        {
            foreach (DigZone zone in Object.FindObjectsByType<DigZone>(FindObjectsSortMode.None))
                if (zone.data != null && zone.data.HasGrid && !Seen.ContainsKey(zone.data))
                    MarkSeen(zone.data);
        }

        private static void OnUndoRedo()
        {
            foreach (DigZone zone in Object.FindObjectsByType<DigZone>(FindObjectsSortMode.None))
            {
                DigZoneData data = zone.data;
                if (data == null || !data.HasGrid) continue;
                bool known = Seen.TryGetValue(data, out State s);
                // Same arrays: edits in place remesh as they go.
                if (known && ReferenceEquals(s.Grid, data.grid) && ReferenceEquals(s.Paint, data.paint))
                {
                    MarkSeen(data);
                    continue;
                }

                int[] box = known ? Difference(s, data) : null;
                if (box != null && box[0] > box[3])
                {
                    // Same samples in new arrays (undo of something else in the asset).
                    MarkSeen(data);
                    continue;
                }
                // What follows an undo is derived data: record no undo of its own.
                DigZoneBaker.RunBusy(() =>
                {
                    if (box == null)
                    {
                        DigZoneBaker.RemeshAll(zone);
                    }
                    else
                    {
                        data.AdoptUndo(s.Grid, s.Paint, s.Version, box);
                        DigZoneBaker.RemeshRange(zone, box);
                    }
                    MarkSeen(data);
                    DigZoneBaker.NotifyGridChanged(zone);
                });
            }
        }

        /// <summary>
        /// The sample box where the zone's grid and paint differ from <paramref name="s"/> (empty: min &gt; max), or null
        /// if they can't be compared.
        /// </summary>
        private static int[] Difference(State s, DigZoneData data)
        {
            int n = data.SampleCount;
            if (s.Grid == null || s.Grid.Length != n || s.Paint != null && s.Paint.Length != n) return null;
            if (data.paint != null && data.paint.Length != n) return null;
            var box = new[] { int.MaxValue, int.MaxValue, int.MaxValue, int.MinValue, int.MinValue, int.MinValue };
            Compare(s.Grid, data.grid, data, box);
            if (!ReferenceEquals(s.Paint, data.paint)) Compare(s.Paint, data.paint, data, box);
            return box;
        }

        /// <summary>Grows <paramref name="box"/> over the samples where a and b differ (null: all zero).</summary>
        private static void Compare(byte[] a, byte[] b, DigZoneData data, int[] box)
        {
            int sx = data.nx + 1, sy = data.ny + 1, sz = data.nz + 1;
            var zero = new byte[sx];
            for (int z = 0; z < sz; z++)
            for (int y = 0; y < sy; y++)
            {
                int row = sx * (y + sy * z);
                ReadOnlySpan<byte> ra = a != null ? new ReadOnlySpan<byte>(a, row, sx) : zero;
                ReadOnlySpan<byte> rb = b != null ? new ReadOnlySpan<byte>(b, row, sx) : zero;
                if (ra.SequenceEqual(rb)) continue;
                int x0 = 0, x1 = sx - 1;
                while (ra[x0] == rb[x0]) x0++;
                while (ra[x1] == rb[x1]) x1--;
                box[0] = Mathf.Min(box[0], x0);
                box[1] = Mathf.Min(box[1], y);
                box[2] = Mathf.Min(box[2], z);
                box[3] = Mathf.Max(box[3], x1);
                box[4] = Mathf.Max(box[4], y);
                box[5] = Mathf.Max(box[5], z);
            }
        }
    }
}
