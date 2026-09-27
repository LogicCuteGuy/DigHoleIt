using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// Scene-view brush input shared by the Dig Zone sculpt tool and the Terrain tools: raycasts the zones' grids,
    /// draws the brush, applies strokes with undo and remeshing, and handles the size/strength drag
    /// (hold A or S and drag left/right, like the Terrain Tools package) and [ ] for size.
    /// Dig, Add and Smooth work like the terrain Raise/Lower brush: they keep working while the mouse is held, at a
    /// speed set by Strength. Paint is applied once per brush spacing.
    /// </summary>
    public sealed class DigBrushStroke
    {
        private enum Adjust { None, Size, Strength }

        private readonly int[] _changed = new int[6];
        private readonly List<DigZone> _touched = new List<DigZone>();
        private bool _stroking;
        private Vector3 _lastStamp;
        private double _lastTime;
        private float _pending;
        private int _undoGroup;
        private SceneView _strokeView;

        private bool _sizeKey;
        private bool _strengthKey;
        private Adjust _adjust;
        private float _adjustStartX;
        private float _adjustStartValue;
        private bool _hasAnchor;
        private Vector3 _anchor;
        private Vector3 _anchorNormal;

        public static Color ModeColor(DigBrushMode mode)
        {
            switch (mode)
            {
                case DigBrushMode.Add: return new Color(0.3f, 1f, 0.4f);
                case DigBrushMode.Paint: return new Color(1f, 0.85f, 0.2f);
                case DigBrushMode.Smooth: return new Color(0.4f, 0.7f, 1f);
                case DigBrushMode.Reset: return new Color(0.85f, 0.85f, 0.85f);
                default: return new Color(1f, 0.4f, 0.2f);
            }
        }

        /// <summary>Call from OnSceneGUI / OnToolGUI for every event.</summary>
        /// <param name="layer">Paint layer (Paint) or added soil layer (Add).</param>
        public void OnSceneGUI(SceneView view, IReadOnlyList<DigZone> zones, DigBrushMode mode, int layer, DigBrushMask mask, int controlId)
        {
            Event e = Event.current;
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);

            HandleKeys(e);

            bool hit = Raycast(zones, HandleUtility.GUIPointToWorldRay(e.mousePosition), out DigZone hitZone, out Vector3 world, out Vector3 surfaceNormal);
            Vector3 axis = hit ? BrushAxis(view, surfaceNormal) : Vector3.up;

            if (_adjust != Adjust.None)
            {
                HandleAdjust(e, controlId, view);
            }
            else
            {
                if (hit)
                {
                    _hasAnchor = true;
                    _anchor = world;
                    _anchorNormal = axis;
                }

                switch (e.type)
                {
                    case EventType.MouseDown when e.button == 0 && !e.alt && (_sizeKey || _strengthKey):
                        _adjust = _sizeKey ? Adjust.Size : Adjust.Strength;
                        _adjustStartX = e.mousePosition.x;
                        _adjustStartValue = _adjust == Adjust.Size ? DigBrushSettings.Radius : DigBrushSettings.Strength;
                        GUIUtility.hotControl = controlId;
                        e.Use();
                        break;
                    case EventType.MouseDown when e.button == 0 && !e.alt && hit:
                        BeginStroke(view);
                        GUIUtility.hotControl = controlId;
                        Work(zones, world, axis, mode, layer, mask);
                        e.Use();
                        break;
                    case EventType.MouseDrag when _stroking && GUIUtility.hotControl == controlId:
                        if (hit) Work(zones, world, axis, mode, layer, mask);
                        e.Use();
                        break;
                    case EventType.Layout when _stroking && hit && mode != DigBrushMode.Paint:
                        // Keeps digging while the mouse is held still (the stroke repaints the view every frame).
                        Work(zones, world, axis, mode, layer, mask);
                        break;
                    case EventType.MouseUp when _stroking && e.button == 0:
                        EndStroke();
                        if (GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
                        e.Use();
                        break;
                    case EventType.MouseMove:
                        view.Repaint();
                        break;
                }
            }

            if (e.type == EventType.Repaint && _hasAnchor && (hit || _adjust != Adjust.None))
                DrawBrush(view, _anchor, _anchorNormal, mode);
        }

        /// <summary>Ends a stroke that is still open (tool switched mid-drag).</summary>
        public void Cancel()
        {
            if (_stroking) EndStroke();
            _adjust = Adjust.None;
            _sizeKey = _strengthKey = false;
        }

        // ---------------------------------------------------------------- Input

        private void HandleKeys(Event e)
        {
            if (e.type == EventType.KeyDown && GUIUtility.hotControl == 0 && !e.control && !e.command && !e.alt)
            {
                switch (e.keyCode)
                {
                    case KeyCode.A: _sizeKey = true; e.Use(); break;
                    case KeyCode.S: _strengthKey = true; e.Use(); break;
                    case KeyCode.LeftBracket: DigBrushSettings.Radius *= 0.9f; e.Use(); break;
                    case KeyCode.RightBracket: DigBrushSettings.Radius *= 1.1f; e.Use(); break;
                }
            }
            else if (e.type == EventType.KeyUp)
            {
                if (e.keyCode == KeyCode.A) _sizeKey = false;
                if (e.keyCode == KeyCode.S) _strengthKey = false;
            }
        }

        private void HandleAdjust(Event e, int controlId, SceneView view)
        {
            switch (e.type)
            {
                case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                    float dx = e.mousePosition.x - _adjustStartX;
                    if (_adjust == Adjust.Size) DigBrushSettings.Radius = _adjustStartValue * Mathf.Pow(1.01f, dx);
                    else DigBrushSettings.Strength = _adjustStartValue + dx * 0.004f;
                    e.Use();
                    view.Repaint();
                    InternalEditorUtilityRepaint();
                    break;
                case EventType.MouseUp when e.button == 0:
                    _adjust = Adjust.None;
                    if (GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        private static void InternalEditorUtilityRepaint()
        {
            // Keeps inspector sliders in step with the drag.
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static Vector3 BrushAxis(SceneView view, Vector3 surfaceNormal)
        {
            switch (DigBrushSettings.Align)
            {
                case DigBrushAlign.WorldUp: return Vector3.up;
                case DigBrushAlign.View: return -view.camera.transform.forward;
                default: return surfaceNormal;
            }
        }

        // ---------------------------------------------------------------- Raycast

        public static bool Raycast(IReadOnlyList<DigZone> zones, Ray ray, out DigZone zone, out Vector3 world, out Vector3 normal)
        {
            zone = null;
            world = default;
            normal = Vector3.up;
            float best = float.MaxValue;
            foreach (DigZone z in zones)
            {
                if (z == null || z.data == null || !z.data.HasGrid) continue;
                DigZoneData d = z.data;
                float v = d.voxelSize;
                if (!DigGridUtil.Raycast(d.grid, d.nx, d.ny, d.nz, (ray.origin - z.transform.position) / v, ray.direction, 100000f, out Vector3 g))
                    continue;
                Vector3 w = z.transform.position + g * v;
                float dist = (w - ray.origin).sqrMagnitude;
                if (dist >= best) continue;
                best = dist;
                zone = z;
                world = w;
                normal = DigEditorBrush.SurfaceNormal(d, g);
            }
            return zone != null;
        }

        // ---------------------------------------------------------------- Strokes

        private void BeginStroke(SceneView view)
        {
            _stroking = true;
            _lastTime = 0;
            _pending = 0f;
            _touched.Clear();
            _strokeView = view;
            EditorApplication.update -= RepaintDuringStroke;
            EditorApplication.update += RepaintDuringStroke;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Dig Sculpt");
            _undoGroup = Undo.GetCurrentGroup();
        }

        private void RepaintDuringStroke()
        {
            if (_strokeView != null) _strokeView.Repaint();
        }

        /// <summary>Dig/Add/Smooth/Reset: time-based work since the last call. Paint: one stamp per brush spacing.</summary>
        private void Work(IReadOnlyList<DigZone> zones, Vector3 world, Vector3 axis, DigBrushMode mode, int layer, DigBrushMask mask)
        {
            float radius = DigBrushSettings.Radius;
            float strength = DigBrushSettings.Strength;
            double now = EditorApplication.timeSinceStartup;

            if (mode == DigBrushMode.Paint)
            {
                if (_lastTime > 0 && Vector3.Distance(world, _lastStamp) < radius * 0.25f) return;
                _lastStamp = world;
                _lastTime = now;
                ApplyToZones(zones, world, axis, mode, layer, mask, strength);
                return;
            }

            // The first call of a stroke counts as one 30 Hz frame, so a click does a small bite.
            float dt = _lastTime > 0 ? Mathf.Min((float)(now - _lastTime), 0.1f) : 1f / 30f;
            _lastTime = now;
            _pending += dt;

            if (mode == DigBrushMode.Smooth || mode == DigBrushMode.Reset)
            {
                if (_pending < 1f / 30f) return;
                float k = Mathf.Clamp01(strength * _pending * 8f);
                _pending = 0f;
                ApplyToZones(zones, world, axis, mode, layer, mask, k);
                return;
            }

            // Surface speed at the brush centre, in metres per second. Work is batched into steps of at least
            // 0.15 voxel: the grid stores 1/64 voxel, so tiny per-frame steps would round away at the soft edge.
            float speed = strength * (1f + radius);
            float metres = speed * _pending;
            if (metres < 0.15f * MinVoxel(zones)) return;
            _pending = 0f;
            ApplyToZones(zones, world, axis, mode, layer, mask, metres);
        }

        private static float MinVoxel(IReadOnlyList<DigZone> zones)
        {
            float v = float.MaxValue;
            foreach (DigZone z in zones)
                if (z != null && z.data != null && z.data.HasGrid) v = Mathf.Min(v, z.data.voxelSize);
            return v == float.MaxValue ? 0.5f : v;
        }

        /// <param name="amount">Metres to move the surface (Dig/Add), blend amount (Smooth, Reset) or coverage (Paint).</param>
        private void ApplyToZones(IReadOnlyList<DigZone> zones, Vector3 world, Vector3 axis, DigBrushMode mode, int layer, DigBrushMask mask, float amount)
        {
            float radius = DigBrushSettings.Radius;
            foreach (DigZone zone in zones)
            {
                if (zone == null || zone.data == null || !zone.data.HasGrid) continue;
                Bounds b = zone.WorldBounds;
                b.Expand(radius * 3f);
                if (!b.Contains(world)) continue;

                DigZoneData data = zone.data;
                if (!_touched.Contains(zone))
                {
                    Undo.RegisterCompleteObjectUndo(data, "Dig Sculpt");
                    _touched.Add(zone);
                }

                Vector3 g = zone.WorldToGrid(world);
                float r = radius / data.voxelSize;
                bool changed = mode == DigBrushMode.Dig || mode == DigBrushMode.Add
                    ? DigEditorBrush.Offset(data, g, axis, r, amount / data.voxelSize, mask, mode == DigBrushMode.Dig, layer, _changed)
                    : DigEditorBrush.Apply(data, g, axis, r, amount, mask, mode, layer, _changed);
                if (!changed) continue;
                data.gridVersion++;
                DigZoneBaker.RemeshRange(zone, _changed);
            }
        }

        private void EndStroke()
        {
            _stroking = false;
            EditorApplication.update -= RepaintDuringStroke;
            _strokeView = null;
            foreach (DigZone zone in _touched)
            {
                if (zone == null || zone.data == null) continue;
                EditorUtility.SetDirty(zone.data);
                DigSculptUndo.MarkSeen(zone.data);
                DigZoneBaker.NotifyGridChanged(zone);
            }
            _touched.Clear();
            Undo.CollapseUndoOperations(_undoGroup);
        }

        // ---------------------------------------------------------------- Drawing

        private void DrawBrush(SceneView view, Vector3 world, Vector3 axis, DigBrushMode mode)
        {
            float radius = DigBrushSettings.Radius;
            float strength = DigBrushSettings.Strength;
            Color col = ModeColor(mode);

            using (new Handles.DrawingScope(col))
            {
                Handles.DrawWireDisc(world, axis, radius, 2f);
                Handles.color = new Color(col.r, col.g, col.b, 0.5f);
                if (mode == DigBrushMode.Dig || mode == DigBrushMode.Add)
                {
                    // Direction of the push; its length shows the strength.
                    Vector3 tip = world + axis * (mode == DigBrushMode.Dig ? -1f : 1f) * radius * 0.6f * strength;
                    Handles.DrawLine(world, tip, 2f);
                    Handles.DrawWireDisc(world, axis, radius * 0.5f);
                }
                else
                {
                    Handles.DrawWireDisc(world, axis, radius * strength);
                }
            }

            string label = _adjust == Adjust.Size ? $"Size {radius:0.##} m"
                         : _adjust == Adjust.Strength ? $"Strength {strength * 100f:0}%"
                         : $"{mode}  {radius:0.##} m  {strength * 100f:0}%";
            var style = new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.MiddleCenter };
            Handles.Label(world + view.camera.transform.up * radius * 1.15f, label, style);
        }
    }
}
