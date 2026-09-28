using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEditorInternal;
using UnityEngine;

namespace LogicCuteGuy.DigHoleIt.Editor
{
    /// <summary>
    /// Unity's own terrain tool, working inside Dig Zones too. The terrain can't be hit through its hole, so Unity's
    /// tools can't reach a zone. These wrap them: the settings and inspector are Unity's, and where the mouse is over
    /// a zone's voxel surface <see cref="PaintOnZone"/> paints there (by default through Unity's tool, at that spot of
    /// the terrain). Zones then show what was painted (see <see cref="DigFoliageBaker"/>).
    /// </summary>
    internal abstract class DigTerrainFoliageTool<T> : TerrainPaintTool<T> where T : DigTerrainFoliageTool<T>
    {
        private WrappedTool _inner;
        private bool _overZone;
        private bool _stroking;
        private DigZone _zone;
        private Vector3 _hit;
        private Vector3 _normal;

        /// <summary>Full name of the Unity tool to wrap.</summary>
        protected abstract string InnerTool { get; }

        protected abstract string ZoneHint { get; }

        private WrappedTool Inner => _inner ??= WrappedTool.Find(InnerTool);

        /// <summary>Unity's tool object, for its settings (null if this Unity version has no such tool).</summary>
        protected object InnerObject => Inner?.Tool;

        /// <summary>
        /// Unity's tool singleton, called through its public TerrainPaintTool methods (the tool classes themselves are
        /// internal).
        /// </summary>
        private sealed class WrappedTool
        {
            public object Tool;
            public Action OnEnterToolMode;
            public Action OnExitToolMode;
            public Action<Terrain, IOnInspectorGUI> OnInspectorGUI;
            public Action<Terrain, IOnSceneGUI> OnSceneGUI;
            public Action<Terrain, IOnSceneGUI> OnRenderBrushPreview;
            public Func<Terrain, IOnPaint, bool> OnPaint;

            public static WrappedTool Find(string typeName)
            {
                Type type = typeof(TerrainPaintTool<>).Assembly.GetType(typeName);
                if (type == null) return null;
                Type baseType = typeof(TerrainPaintTool<>).MakeGenericType(type);
                object tool = baseType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)?.GetValue(null);
                if (tool == null) return null;
                try
                {
                    return new WrappedTool
                    {
                        Tool = tool,
                        OnEnterToolMode = Bind<Action>(tool, "OnEnterToolMode"),
                        OnExitToolMode = Bind<Action>(tool, "OnExitToolMode"),
                        OnInspectorGUI = Bind<Action<Terrain, IOnInspectorGUI>>(tool, "OnInspectorGUI"),
                        OnSceneGUI = Bind<Action<Terrain, IOnSceneGUI>>(tool, "OnSceneGUI"),
                        OnRenderBrushPreview = Bind<Action<Terrain, IOnSceneGUI>>(tool, "OnRenderBrushPreview"),
                        OnPaint = Bind<Func<Terrain, IOnPaint, bool>>(tool, "OnPaint"),
                    };
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DigHoleIt] Couldn't use Unity's {typeName}: {e.Message}");
                    return null;
                }
            }

            private static TDelegate Bind<TDelegate>(object target, string name) where TDelegate : Delegate
            {
                Type[] args = typeof(TDelegate).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray();
                MethodInfo m = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, args, null);
                if (m == null) throw new MissingMethodException(target.GetType().Name, name);
                return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), target, m);
            }
        }

        private static readonly int ControlHash = "DigHoleIt.TerrainFoliageTool".GetHashCode();
        private static MethodInfo _getActiveTool;
        private BrushState _brush = new BrushState { Size = 10f, Strength = 0.5f };

        /// <summary>The terrain brush as Unity last passed it (it only does while the mouse is over a terrain).</summary>
        protected struct BrushState
        {
            public Texture Texture;
            public float Size;
            public float Strength;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            // Over a zone the mouse hits no terrain, and Unity then calls the tool with whichever terrain it hit last (or
            // not at all). So zone strokes are handled here, before the Terrain inspector sees the events.
            SceneView.beforeSceneGui -= BeforeSceneGUI;
            SceneView.beforeSceneGui += BeforeSceneGUI;
            SceneView.duringSceneGui -= DuringSceneGUI;
            SceneView.duringSceneGui += DuringSceneGUI;
        }

        public override void OnDisable()
        {
            SceneView.beforeSceneGui -= BeforeSceneGUI;
            SceneView.duringSceneGui -= DuringSceneGUI;
            base.OnDisable();
        }

        public override void OnEnterToolMode() => Inner?.OnEnterToolMode();

        public override void OnExitToolMode()
        {
            _stroking = false;
            _overZone = false;
            Inner?.OnExitToolMode();
        }

        public override void OnInspectorGUI(Terrain terrain, IOnInspectorGUI editContext)
        {
            if (Inner == null)
            {
                EditorGUILayout.HelpBox($"Unity's {InnerTool} was not found in this Unity version.", MessageType.Error);
                return;
            }
            Inner.OnInspectorGUI(terrain, editContext);
            EditorGUILayout.Space();
            AngleGUI();
            ZoneInspectorGUI();
            int zones = DigTerrainZones.For(terrain).Count;
            EditorGUILayout.HelpBox(zones > 0
                ? $"{ZoneHint}\nDig Zones on this terrain: {zones}."
                : "No Dig Zone on this terrain: this works like Unity's own tool.", MessageType.Info);
            DigCredit.Draw();
        }

        // ---------------------------------------------------------------- Surface angle

        private static readonly GUIContent AngleLabel = new GUIContent("Surface Angle",
            "Paint only surfaces whose angle from facing straight up is in this range: 0° floors and flat ground, 90° " +
            "walls, 180° cave ceilings (terrain slopes count too). Erasing ignores it.");

        private static string AngleKey => "DigHoleIt." + typeof(T).Name + ".Angle";

        /// <summary>
        /// The surfaces the brush adds to: their angle from facing straight up, min and max in degrees (EditorPrefs).
        /// </summary>
        protected static Vector2 AngleRange
        {
            get => new Vector2(EditorPrefs.GetFloat(AngleKey + "Min", 0f), EditorPrefs.GetFloat(AngleKey + "Max", 180f));
            set
            {
                EditorPrefs.SetFloat(AngleKey + "Min", value.x);
                EditorPrefs.SetFloat(AngleKey + "Max", value.y);
            }
        }

        /// <summary>True if <see cref="AngleRange"/> leaves some surfaces out.</summary>
        protected static bool AngleLimited
        {
            get
            {
                Vector2 r = AngleRange;
                return r.x > 0.01f || r.y < 179.99f;
            }
        }

        /// <summary>True if the brush may add to a surface facing <paramref name="normal"/>.</summary>
        protected static bool AngleAllows(Vector3 normal)
        {
            Vector2 r = AngleRange;
            if (r.x <= 0.01f && r.y >= 179.99f) return true;
            float a = Vector3.Angle(Vector3.up, normal);
            return a >= r.x - 0.5f && a <= r.y + 0.5f;
        }

        private static void AngleGUI()
        {
            Vector2 r = AngleRange;
            float min = r.x, max = r.y;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(AngleLabel);
                min = EditorGUILayout.FloatField(min, GUILayout.Width(36f));
                EditorGUILayout.MinMaxSlider(ref min, ref max, 0f, 180f);
                max = EditorGUILayout.FloatField(max, GUILayout.Width(36f));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(" ");
                if (GUILayout.Button("All", EditorStyles.miniButtonLeft)) { min = 0f; max = 180f; }
                if (GUILayout.Button("Floors", EditorStyles.miniButtonMid)) { min = 0f; max = 45f; }
                if (GUILayout.Button("Walls", EditorStyles.miniButtonMid)) { min = 45f; max = 135f; }
                if (GUILayout.Button("Ceilings", EditorStyles.miniButtonRight)) { min = 135f; max = 180f; }
            }
            min = Mathf.Clamp(Mathf.Round(min), 0f, 180f);
            max = Mathf.Clamp(Mathf.Round(max), 0f, 180f);
            if (max < min) max = min;
            if (min != r.x || max != r.y) AngleRange = new Vector2(min, max);
        }

        public override void OnSceneGUI(Terrain terrain, IOnSceneGUI editContext)
        {
            _brush = new BrushState { Texture = editContext.brushTexture, Size = editContext.brushSize, Strength = editContext.brushStrength };
            Inner?.OnSceneGUI(terrain, editContext);
        }

        /// <summary>True while the Terrain inspector shows this tool.</summary>
        private bool IsActive()
        {
            if (Selection.activeGameObject == null || !Selection.activeGameObject.TryGetComponent(out Terrain _)) return false;
            foreach (UnityEditor.Editor editor in ActiveEditorTracker.sharedTracker.activeEditors)
            {
                if (editor == null || editor.GetType().Name != "TerrainInspector") continue;
                _getActiveTool ??= editor.GetType().GetMethod("GetActiveTool", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return _getActiveTool != null && ReferenceEquals(_getActiveTool.Invoke(editor, null), this);
            }
            return false;
        }

        private void BeforeSceneGUI(SceneView view)
        {
            Event e = Event.current;
            if (!IsActive())
            {
                _overZone = false;
                return;
            }
            if (e.type != EventType.Used) _overZone = ZoneHit(HandleUtility.GUIPointToWorldRay(e.mousePosition));

            int id = GUIUtility.GetControlID(ControlHash, FocusType.Passive);
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && _overZone:
                    _stroking = true;
                    PaintOnZone(_zone, _hit, _normal, _brush, true);
                    e.Use();
                    break;
                case EventType.MouseDrag when e.button == 0 && _overZone:
                    // A stroke started on the terrain continues onto the zone.
                    PaintOnZone(_zone, _hit, _normal, _brush, !_stroking);
                    _stroking = true;
                    e.Use();
                    break;
                case EventType.MouseUp when _stroking && e.button == 0:
                    _stroking = false;
                    if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.MouseMove:
                    view.Repaint();
                    break;
            }
        }

        private void DuringSceneGUI(SceneView view)
        {
            if (Event.current.type != EventType.Repaint || !IsActive()) return;
            foreach (Terrain t in Terrain.activeTerrains)
            foreach (DigZone zone in DigTerrainZones.For(t))
                DigZoneHandles.OnSceneGUI(zone, false);
            if (!_overZone) return;
            using (new Handles.DrawingScope(new Color(0.4f, 0.8f, 1f, 0.9f)))
                Handles.DrawWireDisc(_hit, ZoneBrushAxis(_normal), Mathf.Max(0.05f, ZoneBrushRadius(_brush)), 2f);
        }

        /// <summary>Settings of the tool inside zones, drawn under Unity's settings.</summary>
        protected virtual void ZoneInspectorGUI() { }

        /// <summary>The axis the brush disc faces on a zone, where the surface there faces <paramref name="surfaceNormal"/>.</summary>
        protected virtual Vector3 ZoneBrushAxis(Vector3 surfaceNormal) => surfaceNormal;

        public override void OnRenderBrushPreview(Terrain terrain, IOnSceneGUI editContext)
        {
            if (!_overZone) Inner?.OnRenderBrushPreview(terrain, editContext);
        }

        public override bool OnPaint(Terrain terrain, IOnPaint editContext)
        {
            if (Inner == null) return false;
            bool result = Paint(terrain, editContext, editContext.raycastHit.point, editContext.brushSize * 0.5f, false);
            DigFoliageSync.MarkDirty(terrain.terrainData);
            return result;
        }

        /// <summary>
        /// Unity's tool painting at <paramref name="hit"/>: a terrain hit, or a point of a zone's voxel surface
        /// (<paramref name="onZone"/>).
        /// </summary>
        protected virtual bool Paint(Terrain terrain, IOnPaint context, Vector3 hit, float radius, bool onZone) =>
            Inner != null && Inner.OnPaint(terrain, context);

        /// <summary>
        /// True if the mouse ray hits the voxel surface of a zone (on any terrain) inside its terrain hole, before it hits
        /// a terrain.
        /// </summary>
        private bool ZoneHit(Ray ray)
        {
            float best = float.MaxValue;
            bool any = false;
            foreach (Terrain t in Terrain.activeTerrains)
            {
                List<DigZone> zones = DigTerrainZones.For(t);
                if (zones.Count == 0 || !DigBrushStroke.Raycast(zones, ray, out DigZone zone, out Vector3 world, out Vector3 normal)) continue;
                if (zone.data.cutTerrain != t.terrainData) continue;
                Vector4 hole = zone.data.holeRect;
                if (world.x < hole.x || world.x > hole.z || world.z < hole.y || world.z > hole.w) continue;
                float distance = Vector3.Distance(ray.origin, world);
                if (distance >= best) continue;
                best = distance;
                _zone = zone;
                _hit = world;
                _normal = normal;
                any = true;
            }
            if (!any) return false;

            // Around the hole the terrain is there to be hit, and it may be in front of the zone.
            foreach (Terrain t in Terrain.activeTerrains)
                if (t.TryGetComponent(out TerrainCollider c) && c.Raycast(ray, out RaycastHit h, best) && h.distance < best)
                    return false;
            return true;
        }

        /// <summary>Radius of the brush circle drawn on a zone.</summary>
        protected virtual float ZoneBrushRadius(BrushState brush) => brush.Size * 0.5f;

        /// <summary>
        /// Paints at <paramref name="hit"/> on <paramref name="zone"/>'s voxel surface. By default Unity's tool paints
        /// the zone's terrain under that point, which works for data the terrain keeps in its holes.
        /// </summary>
        /// <param name="strokeStart">True for the first dab of a stroke on the zone (record undo once per stroke).</param>
        protected virtual void PaintOnZone(DigZone zone, Vector3 hit, Vector3 normal, BrushState brush, bool strokeStart)
        {
            Terrain terrain = zone.terrain;
            if (Inner == null || terrain == null || terrain.terrainData == null) return;
            Vector3 tp = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            var raycastHit = new RaycastHit { point = hit, normal = normal };
            var context = new PaintContext(brush, new Vector2((hit.x - tp.x) / size.x, (hit.z - tp.z) / size.z), raycastHit);
            Paint(terrain, context, hit, ZoneBrushRadius(brush), true);
            DigFoliageSync.MarkDirty(terrain.terrainData);
        }

        protected static TValue GetSetting<TValue>(object tool, string name, TValue fallback)
        {
            PropertyInfo p = tool?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p != null && p.GetValue(tool) is TValue v ? v : fallback;
        }

        protected static TValue CallSetting<TValue>(object tool, string name, TValue fallback, params object[] args)
        {
            MethodInfo m = tool?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
            return m != null && m.Invoke(tool, args) is TValue v ? v : fallback;
        }

        /// <summary>The paint call a terrain hit would have made, at a point of the zone's surface.</summary>
        private sealed class PaintContext : IOnPaint
        {
            private readonly BrushState _brush;

            public PaintContext(BrushState brush, Vector2 uv, RaycastHit hit)
            {
                _brush = brush;
                this.uv = uv;
                raycastHit = hit;
            }

            public Texture brushTexture => _brush.Texture != null ? _brush.Texture : Texture2D.whiteTexture;
            public Vector2 uv { get; }
            public float brushStrength => _brush.Strength;
            public float brushSize => _brush.Size;
            public bool hitValidTerrain => true;
            public RaycastHit raycastHit { get; }
            public void RepaintAllInspectors() => InternalEditorUtility.RepaintAllViews();
            public void Repaint(RepaintFlags flags) => SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Terrain > Paint Terrain > "DigHoleIt: Paint Trees": Unity's Paint Trees, also inside Dig Zones. The terrain deletes
    /// trees in its holes, so inside a zone the trees go into the zone's own list (DigZoneData.terrainTrees), placed the
    /// way Unity's tool places them, with its settings. They return to the terrain when the zone's hole is filled.
    /// </summary>
    internal sealed class DigTerrainTreeTool : DigTerrainFoliageTool<DigTerrainTreeTool>
    {
        protected override string InnerTool => "UnityEditor.TerrainTools.PaintTreesTool";

        private bool _undoRecorded;

        private const string AxisPref = "DigHoleIt.TreeBrush.Axis";

        private static readonly GUIContent AxisLabel = new GUIContent("Brush Axis In Zones",
            "Which way the brush faces inside Dig Zones.\n" +
            "Surface Normal: lies on the surface under the mouse, at any angle (floors, slopes, pit walls).\n" +
            "World Up: flat, like Unity's brush; trees drop straight down.\n" +
            "View: faces the camera; trees drop away from it.");

        private static readonly GUIContent[] AxisNames =
        {
            new GUIContent("Surface Normal (any angle)"), new GUIContent("World Up"), new GUIContent("View"),
        };

        /// <summary>Which way the brush faces inside zones (EditorPrefs).</summary>
        private static DigBrushAlign Axis
        {
            get => (DigBrushAlign)EditorPrefs.GetInt(AxisPref, (int)DigBrushAlign.SurfaceNormal);
            set => EditorPrefs.SetInt(AxisPref, (int)value);
        }

        private const string GrowPref = "DigHoleIt.TreeBrush.AlongSurface";

        private static readonly GUIContent GrowLabel = new GUIContent("Tree Direction In Zones",
            "Which way trees painted inside Dig Zones grow.\n" +
            "Upright: straight up like on the terrain, and hanging straight down from cave ceilings.\n" +
            "Along Surface: out of the surface, tilted with slopes and sideways out of walls.");

        private static readonly GUIContent[] GrowNames =
        {
            new GUIContent("Upright (hang from ceilings)"), new GUIContent("Along Surface"),
        };

        /// <summary>True: trees grow along the surface normal; false: upright, hanging from ceilings (EditorPrefs).</summary>
        private static bool AlongSurface
        {
            get => EditorPrefs.GetBool(GrowPref, false);
            set => EditorPrefs.SetBool(GrowPref, value);
        }

        protected override void ZoneInspectorGUI()
        {
            Axis = (DigBrushAlign)EditorGUILayout.Popup(AxisLabel, (int)Axis, AxisNames);
            AlongSurface = EditorGUILayout.Popup(GrowLabel, AlongSurface ? 1 : 0, GrowNames) == 1;
        }

        protected override Vector3 ZoneBrushAxis(Vector3 surfaceNormal)
        {
            switch (Axis)
            {
                case DigBrushAlign.WorldUp:
                    return Vector3.up;
                case DigBrushAlign.View:
                    Camera cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                    return cam != null ? -cam.transform.forward : surfaceNormal;
                default:
                    return surfaceNormal;
            }
        }

        /// <summary>On the terrain Unity's tool places the trees; those on slopes outside Surface Angle are taken out again.</summary>
        protected override bool Paint(Terrain terrain, IOnPaint context, Vector3 hit, float radius, bool onZone)
        {
            TerrainData td = terrain.terrainData;
            if (onZone || !AngleLimited || td == null) return base.Paint(terrain, context, hit, radius, onZone);
            int before = td.treeInstanceCount;
            bool result = base.Paint(terrain, context, hit, radius, onZone);
            if (td.treeInstanceCount <= before) return result;
            // Unity's tool adds its trees at the end.
            TreeInstance[] all = td.treeInstances;
            var kept = new List<TreeInstance>(all.Length);
            for (int i = 0; i < all.Length; i++)
                if (i < before || AngleAllows(td.GetInterpolatedNormal(all[i].position.x, all[i].position.z)))
                    kept.Add(all[i]);
            if (kept.Count != all.Length) td.SetTreeInstances(kept.ToArray(), true);
            return result;
        }

        // Unity's tree tool has its own brush size.
        protected override float ZoneBrushRadius(BrushState brush) => GetSetting(InnerObject, "brushSize", brush.Size) * 0.5f;

        protected override void PaintOnZone(DigZone zone, Vector3 hit, Vector3 normal, BrushState brush, bool strokeStart)
        {
            object tool = InnerObject;
            DigZoneData data = zone.data;
            Terrain terrain = zone.terrain;
            TerrainData td = terrain != null ? terrain.terrainData : null;
            if (tool == null || data == null || td == null || data.cutTerrain != td) return;
            if (strokeStart) _undoRecorded = false;

            Event e = Event.current;
            bool remove = e.shift || e.control;
            int selected = GetSetting(tool, "selectedTree", -1);
            if (!remove && (selected < 0 || selected >= td.treePrototypes.Length)) return;
            // Unity's tree tool has its own brush size.
            float size = Mathf.Max(0.1f, GetSetting(tool, "brushSize", 20f));

            var trees = new List<DigTreeInstance>(data.terrainTrees ?? Array.Empty<DigTreeInstance>());
            bool changed = remove
                ? RemoveTrees(trees, terrain, hit, size * 0.5f, e.control ? selected : -1)
                : PlaceTrees(trees, tool, terrain, zone, hit, normal, ZoneBrushAxis(normal), size, selected, AlongSurface, AngleAllows);
            if (!changed) return;

            // Once per stroke: the data asset holds the whole grid, so recording it is not free.
            if (!_undoRecorded) Undo.RegisterCompleteObjectUndo(data, remove ? "Remove Trees" : "Place Trees");
            _undoRecorded = true;
            data.terrainTrees = trees.ToArray();
            EditorUtility.SetDirty(data);
            DigFoliageBaker.RebuildTrees(zone);
        }

        /// <summary>Surfaces facing further down than this are ceilings: upright trees hang from them.</summary>
        private const float MinNormalY = -0.2f;

        /// <summary>Which way a tree on a surface facing <paramref name="n"/> grows (zero: straight up).</summary>
        private static Vector3 GrowDirection(Vector3 n, bool alongSurface)
        {
            if (alongSurface) return n.normalized;
            return n.y < MinNormalY ? Vector3.down : Vector3.zero;
        }

        private static Vector3 TreeWorld(DigTreeInstance t, Vector3 terrainPos, Vector3 terrainSize) =>
            terrainPos + Vector3.Scale(t.position, terrainSize);

        /// <summary>Removes the trees within the brush sphere (a stroke in a pit leaves the rim above alone).</summary>
        private static bool RemoveTrees(List<DigTreeInstance> trees, Terrain terrain, Vector3 hit, float radius, int prototype)
        {
            Vector3 tp = terrain.transform.position, size = terrain.terrainData.size;
            int before = trees.Count;
            trees.RemoveAll(t => (prototype < 0 || t.prototypeIndex == prototype) && (TreeWorld(t, tp, size) - hit).sqrMagnitude <= radius * radius);
            return trees.Count != before;
        }

        /// <summary>
        /// Like Unity's tool: one tree at the brush centre, then a scatter over the brush, kept Tree Density apart. The
        /// brush is a disc facing <paramref name="axis"/> (by default the surface normal under the mouse, so it lies on a
        /// floor, a slope or a pit wall alike), and each scattered tree drops from it onto the voxel surface along the
        /// axis, within the brush sphere: a stroke in a pit stays in the pit.
        /// </summary>
        private static bool PlaceTrees(List<DigTreeInstance> trees, object tool, Terrain terrain, DigZone zone, Vector3 hit, Vector3 normal, Vector3 axis, float brush, int prototype, bool alongSurface,
            Func<Vector3, bool> angleAllows)
        {
            TerrainData td = terrain.terrainData;
            DigZoneData d = zone.data;
            Vector3 tp = terrain.transform.position, size = td.size;
            float spacing = Mathf.Max(0.05f, GetSetting(tool, "spacing", 0.8f));
            float extent = TreeExtent(td.treePrototypes[prototype].prefab);
            float radius = brush * 0.5f;
            Vector4 hole = d.holeRect;

            bool placed = false;
            void TryPlace(Vector3 world, Vector3 surfaceNormal, float minDistance)
            {
                if (!angleAllows(surfaceNormal)) return;
                if (world.x < hole.x || world.x >= hole.z || world.z < hole.y || world.z >= hole.w) return;
                foreach (DigTreeInstance t in trees)
                    if (t.prototypeIndex == prototype && (TreeWorld(t, tp, size) - world).sqrMagnitude < minDistance * minDistance)
                        return;
                float height = CallSetting(tool, "GetTreeHeight", 1f);
                trees.Add(new DigTreeInstance
                {
                    // On the voxel surface, not the terrain: a pit floor, a slope, a wall, a cave ceiling.
                    position = new Vector3((world.x - tp.x) / size.x, (world.y - tp.y) / size.y, (world.z - tp.z) / size.z),
                    up = GrowDirection(surfaceNormal, alongSurface),
                    heightScale = height,
                    widthScale = CallSetting(tool, "GetTreeWidth", height, height),
                    rotation = CallSetting(tool, "GetTreeRotation", 0f),
                    color = CallSetting(tool, "GetTreeColor", Color.white),
                    lightmapColor = Color.white,
                    prototypeIndex = prototype,
                });
                placed = true;
            }

            TryPlace(hit, normal, extent * spacing);

            Vector3 n = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
            DigEditorBrush.BuildFrame(n, out Vector3 t1, out Vector3 t2);
            float perAxis = brush / (extent * spacing * 0.5f);
            int count = Mathf.Clamp((int)(perAxis * perAxis * 0.5f), 0, 100);
            float reach = 2f * radius / d.voxelSize;
            for (int i = 1; i < count; i++)
            {
                Vector2 o = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 from = zone.WorldToGrid(hit + t1 * o.x + t2 * o.y + n * radius);
                // Starting inside the ground (a corner of the pit): no surface to drop onto from here.
                if (DigGridUtil.Sample(d.grid, d.nx, d.ny, d.nz, from) < 0f) continue;
                if (!DigGridUtil.Raycast(d.grid, d.nx, d.ny, d.nz, from, -n, reach, out Vector3 g)) continue;
                Vector3 at = zone.GridToWorld(g);
                if ((at - hit).sqrMagnitude > radius * radius) continue;
                TryPlace(at, DigEditorBrush.SurfaceNormal(d, g), extent * spacing * 0.5f);
            }
            return placed;
        }

        /// <summary>Width of a tree prefab across the ground (its meshes, scaled), at least 0.5 m.</summary>
        private static float TreeExtent(GameObject prefab)
        {
            if (prefab == null) return 1f;
            float width = 0f;
            foreach (MeshFilter f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                Vector3 s = Vector3.Scale(f.sharedMesh.bounds.size, f.transform.lossyScale);
                width = Mathf.Max(width, new Vector2(s.x, s.z).magnitude);
            }
            return width > 0f ? Mathf.Max(0.5f, width) : 1f;
        }

        protected override string ZoneHint =>
            "Trees inside a Dig Zone stand on its voxel surface as copies of their prefabs: on floors, slopes and walls, " +
            "and hanging from cave ceilings. " +
            "The brush paints the surface under the mouse, not the rim above a pit. Digging the ground away " +
            "under a tree or burying it removes the tree. Uncheck Trees on a zone to hide them there.";

        public override string GetName() => "DigHoleIt: Paint Trees";

        public override string GetDescription() =>
            "Unity's Paint Trees brush, also inside Dig Zones (the terrain hole keeps Unity's own brush out).\n" +
            "Click to place trees, Shift+click to remove them, Ctrl+click to remove only the selected tree.";
    }

    /// <summary>Terrain > Paint Terrain > "DigHoleIt: Paint Details": Unity's Paint Details, also inside Dig Zones.</summary>
    internal sealed class DigTerrainDetailTool : DigTerrainFoliageTool<DigTerrainDetailTool>
    {
        protected override string InnerTool => "UnityEditor.TerrainTools.PaintDetailsTool";

        private bool _undoRecorded;

        public override void OnInspectorGUI(Terrain terrain, IOnInspectorGUI editContext)
        {
            TerrainData td = terrain != null ? terrain.terrainData : null;
            if (td != null && td.detailResolution <= 0)
            {
                // Terrains made by script (like the demo terrain of DigHoleIt 0.4.0 and earlier) start without one.
                EditorGUILayout.HelpBox("This terrain has no detail map (Detail Resolution 0), so neither this tool nor " +
                                        "Unity's Paint Details can paint on it.", MessageType.Warning);
                if (GUILayout.Button("Set Detail Resolution To 512"))
                {
                    Undo.RegisterCompleteObjectUndo(td, "Set Detail Resolution");
                    td.SetDetailResolution(512, Mathf.Clamp(td.detailResolutionPerPatch, 8, 128));
                    EditorUtility.SetDirty(td);
                }
            }
            base.OnInspectorGUI(terrain, editContext);
        }

        // On the top surface the terrain's (flat) detail map is painted; elsewhere the brush lies on the surface.
        protected override Vector3 ZoneBrushAxis(Vector3 surfaceNormal) => surfaceNormal.y > 0.7f ? Vector3.up : surfaceNormal;

        /// <summary>
        /// On a zone's top surface (the ground, a pit floor) this paints the terrain's detail map like Unity's tool.
        /// Anywhere else (walls, cave ceilings, tunnel floors) the detail map can't hold details, so they go into the
        /// zone's own list (<see cref="DigZoneData.surfaceDetails"/>), growing out of the surface.
        /// </summary>
        protected override void PaintOnZone(DigZone zone, Vector3 hit, Vector3 normal, BrushState brush, bool strokeStart)
        {
            if (strokeStart) _undoRecorded = false;
            Event e = Event.current;
            bool erase = e.shift || e.control;
            bool top = OnTopSurface(zone, hit, normal);
            if (top) base.PaintOnZone(zone, hit, normal, brush, strokeStart);
            if (top && !erase) return;
            PaintSurfaceDetails(zone, hit, normal, brush, erase, e.control);
        }

        /// <summary>True if <paramref name="hit"/> lies on the top surface of its grid column, facing up.</summary>
        private static bool OnTopSurface(DigZone zone, Vector3 hit, Vector3 normal)
        {
            DigZoneData d = zone.data;
            if (normal.y < 0.3f) return false;
            Vector3 g = zone.WorldToGrid(hit);
            int x = Mathf.Clamp(Mathf.RoundToInt(g.x), 0, d.nx), z = Mathf.Clamp(Mathf.RoundToInt(g.z), 0, d.nz);
            float top = DigFoliage.TopSurface(d.grid, d.nx, d.ny, x, z);
            return top >= 0f && Mathf.Abs(top - g.y) <= 1.5f;
        }

        /// <summary>
        /// Paints (or erases) details of the selected prototype on the zone's surface around <paramref name="hit"/>,
        /// scattered over a disc lying on the surface and dropped onto it, as dense as the tool's Target Strength asks.
        /// </summary>
        private void PaintSurfaceDetails(DigZone zone, Vector3 hit, Vector3 normal, BrushState brush, bool erase, bool onlySelected)
        {
            object tool = InnerObject;
            DigZoneData d = zone.data;
            Terrain terrain = zone.terrain;
            TerrainData td = terrain != null ? terrain.terrainData : null;
            if (tool == null || d == null || !d.HasGrid || td == null || d.cutTerrain != td) return;
            DetailPrototype[] protos = td.detailPrototypes;
            int selected = GetSetting(tool, "selectedDetail", -1);
            bool valid = selected >= 0 && selected < protos.Length && protos[selected] != null;
            if (!erase && !valid) return;

            float radius = Mathf.Max(0.05f, ZoneBrushRadius(brush));
            Vector3 tp = terrain.transform.position;
            var list = new List<DigDetailInstance>(d.surfaceDetails ?? Array.Empty<DigDetailInstance>());
            bool changed;
            if (erase)
            {
                int before = list.Count;
                list.RemoveAll(x => (!onlySelected || x.prototypeIndex == selected) && (tp + x.position - hit).sqrMagnitude <= radius * radius);
                changed = list.Count != before;
            }
            else
            {
                changed = ScatterDetails(list, zone, td, protos[selected], selected, hit, normal, radius, tool, AngleAllows);
            }
            if (!changed) return;

            // Once per stroke: the data asset holds the whole grid, so recording it is not free.
            if (!_undoRecorded) Undo.RegisterCompleteObjectUndo(d, erase ? "Erase Details" : "Paint Details");
            _undoRecorded = true;
            d.surfaceDetails = list.ToArray();
            EditorUtility.SetDirty(d);
            // Rebuilt once the stroke pauses or ends.
            DigFoliageSync.MarkDirty(td);
        }

        private static bool ScatterDetails(List<DigDetailInstance> list, DigZone zone, TerrainData td, DetailPrototype proto, int prototype,
            Vector3 hit, Vector3 normal, float radius, object tool, Func<Vector3, bool> angleAllows)
        {
            DigZoneData d = zone.data;
            Terrain terrain = zone.terrain;
            Vector3 tp = terrain.transform.position;
            // Like the terrain's instance count: Target Strength 1 is 16 per detail cell, times the terrain's density.
            float cell = td.size.x / Mathf.Max(1, td.detailResolution);
            float strength = Mathf.Clamp01(GetSetting(tool, "detailStrength", 0.5f));
            float opacity = Mathf.Clamp01(GetSetting(tool, "detailOpacity", 1f));
            float perM2 = Mathf.Min(64f, Mathf.Max(0.05f, strength * 16f) / (cell * cell) * Mathf.Max(0.05f, terrain.detailObjectDensity));
            int target = Mathf.Min(4000, Mathf.RoundToInt(perM2 * Mathf.PI * radius * radius));
            float spacing = 0.5f / Mathf.Sqrt(perM2);

            int have = 0;
            foreach (DigDetailInstance x in list)
                if (x.prototypeIndex == prototype && (tp + x.position - hit).sqrMagnitude <= radius * radius) have++;
            int want = Mathf.CeilToInt((target - have) * Mathf.Max(0.1f, opacity));
            if (want <= 0) return false;

            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            DigEditorBrush.BuildFrame(n, out Vector3 t1, out Vector3 t2);
            float reach = 2f * radius / d.voxelSize;
            Vector4 hole = d.holeRect;
            var placed = new List<Vector3>();
            bool any = false;
            for (int i = 0; i < want * 3 && placed.Count < want; i++)
            {
                Vector2 o = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 from = zone.WorldToGrid(hit + t1 * o.x + t2 * o.y + n * radius);
                if (DigGridUtil.Sample(d.grid, d.nx, d.ny, d.nz, from) < 0f) continue;
                if (!DigGridUtil.Raycast(d.grid, d.nx, d.ny, d.nz, from, -n, reach, out Vector3 g)) continue;
                Vector3 at = zone.GridToWorld(g);
                if ((at - hit).sqrMagnitude > radius * radius) continue;
                if (at.x < hole.x || at.x >= hole.z || at.z < hole.y || at.z >= hole.w) continue;
                bool crowded = false;
                foreach (Vector3 q in placed)
                    if ((q - at).sqrMagnitude < spacing * spacing) { crowded = true; break; }
                if (crowded) continue;
                Vector3 surfaceNormal = DigEditorBrush.SurfaceNormal(d, g);
                if (!angleAllows(surfaceNormal)) continue;
                placed.Add(at);
                float w = UnityEngine.Random.Range(proto.minWidth, proto.maxWidth);
                float h = UnityEngine.Random.Range(proto.minHeight, proto.maxHeight);
                list.Add(new DigDetailInstance
                {
                    position = at - tp,
                    normal = surfaceNormal,
                    rotation = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                    width = w,
                    height = h,
                    prototypeIndex = prototype,
                });
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Unity's detail brush paints a flat disc of the terrain's detail map. Where that reaches into a zone, only cells
        /// whose surface is within the brush's reach of the mouse keep what it painted: a stroke on a pit floor leaves the
        /// rim (and the terrain around the hole) alone, and a stroke on the terrain beside a pit leaves the pit floor alone.
        /// </summary>
        protected override bool Paint(Terrain terrain, IOnPaint context, Vector3 hit, float radius, bool onZone)
        {
            bool limited = AngleLimited;
            List<DetailSnapshot> before = Snapshot(hit, radius, onZone, limited);
            bool result = base.Paint(terrain, context, hit, radius, onZone);
            foreach (DetailSnapshot snap in before) snap.Restore(hit, radius, onZone, limited ? (Func<Vector3, bool>)AngleAllows : null);
            return result;
        }

        private static List<DetailSnapshot> Snapshot(Vector3 hit, float radius, bool onZone, bool angleLimited)
        {
            var list = new List<DetailSnapshot>();
            foreach (Terrain t in Terrain.activeTerrains)
            {
                TerrainData td = t.terrainData;
                if (td == null || td.detailPrototypes.Length == 0) continue;
                List<DigZone> zones = DigTerrainZones.For(t).FindAll(z => z != null && z.data != null && z.data.HasGrid && z.data.cutTerrain == td);
                // A stroke on a terrain without zones stays Unity's (unless Surface Angle leaves slopes out).
                if (!onZone && zones.Count == 0 && !angleLimited) continue;
                int res = td.detailResolution;
                Vector3 tp = t.transform.position, size = td.size;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((hit.x - radius - tp.x) / size.x * res) - 1);
                int x1 = Mathf.Min(res - 1, Mathf.CeilToInt((hit.x + radius - tp.x) / size.x * res) + 1);
                int y0 = Mathf.Max(0, Mathf.FloorToInt((hit.z - radius - tp.z) / size.z * res) - 1);
                int y1 = Mathf.Min(res - 1, Mathf.CeilToInt((hit.z + radius - tp.z) / size.z * res) + 1);
                if (x1 < x0 || y1 < y0) continue;
                var snap = new DetailSnapshot { Terrain = t, Zones = zones, X = x0, Y = y0, W = x1 - x0 + 1, H = y1 - y0 + 1 };
                snap.Layers = new int[td.detailPrototypes.Length][,];
                for (int l = 0; l < snap.Layers.Length; l++) snap.Layers[l] = td.GetDetailLayer(x0, y0, snap.W, snap.H, l);
                list.Add(snap);
            }
            return list;
        }

        /// <summary>A terrain's detail layers under the brush before a dab.</summary>
        private sealed class DetailSnapshot
        {
            public Terrain Terrain;
            public List<DigZone> Zones;
            public int X, Y, W, H;
            public int[][,] Layers;

            /// <summary>
            /// Puts back what the dab changed on cells whose surface is out of the brush's reach, and what it added where
            /// the surface's angle is outside <paramref name="angleAllows"/> (null: any angle).
            /// </summary>
            public void Restore(Vector3 hit, float radius, bool onZone, Func<Vector3, bool> angleAllows)
            {
                TerrainData td = Terrain.terrainData;
                if (td == null || td.detailPrototypes.Length != Layers.Length) return;
                var reach = new sbyte[H, W]; // 0 unknown, 1 keep, 2 keep only erasing, -1 put back
                for (int l = 0; l < Layers.Length; l++)
                {
                    int[,] old = Layers[l];
                    int[,] now = td.GetDetailLayer(X, Y, W, H, l);
                    bool dirty = false;
                    for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        if (now[y, x] == old[y, x]) continue;
                        if (reach[y, x] == 0)
                        {
                            bool reaches = Reaches(X + x, Y + y, hit, radius, onZone, out Vector3 normal);
                            reach[y, x] = (sbyte)(!reaches ? -1 : angleAllows != null && !angleAllows(normal) ? 2 : 1);
                        }
                        if (reach[y, x] == 1 || reach[y, x] == 2 && now[y, x] < old[y, x]) continue;
                        now[y, x] = old[y, x];
                        dirty = true;
                    }
                    if (dirty) td.SetDetailLayer(X, Y, l, now);
                }
            }

            /// <summary>
            /// True if the surface details grow on at detail cell (x, y) is within the brush radius of the hit's height;
            /// <paramref name="normal"/> is that surface's normal.
            /// </summary>
            private bool Reaches(int x, int y, Vector3 hit, float radius, bool onZone, out Vector3 normal)
            {
                TerrainData td = Terrain.terrainData;
                Vector3 tp = Terrain.transform.position, size = td.size;
                int res = td.detailResolution;
                var world = new Vector3(tp.x + (x + 0.5f) / res * size.x, 0f, tp.z + (y + 0.5f) / res * size.z);
                foreach (DigZone zone in Zones)
                {
                    Vector4 hole = zone.data.holeRect;
                    if (world.x < hole.x || world.x >= hole.z || world.z < hole.y || world.z >= hole.w) continue;
                    // Inside a zone details grow on its top surface (DigFoliageBaker).
                    DigZoneData d = zone.data;
                    Vector3 g = zone.WorldToGrid(world);
                    int gx = Mathf.Clamp(Mathf.RoundToInt(g.x), 0, d.nx), gz = Mathf.Clamp(Mathf.RoundToInt(g.z), 0, d.nz);
                    float top = DigFoliage.TopSurface(d.grid, d.nx, d.ny, gx, gz);
                    normal = top >= 0f ? DigEditorBrush.SurfaceNormal(d, new Vector3(gx, top, gz)) : Vector3.up;
                    return top >= 0f && Mathf.Abs(zone.GridToWorld(new Vector3(0f, top, 0f)).y - hit.y) <= radius;
                }
                normal = td.GetInterpolatedNormal((world.x - tp.x) / size.x, (world.z - tp.z) / size.z);
                return !onZone || Mathf.Abs(tp.y + Terrain.SampleHeight(world) - hit.y) <= radius;
            }
        }

        protected override string ZoneHint =>
            "Details inside a Dig Zone (grass, flowers, detail meshes) grow on its voxel surface: on the top surface from " +
            "the terrain's detail map, and on walls, cave ceilings and tunnel floors from the zone's own list (shown when " +
            "the stroke pauses). The brush paints the surface under the mouse, not the rim above a pit. Digging the ground " +
            "away under them or burying them removes them. Uncheck Details on a zone to hide them there.";

        public override string GetName() => "DigHoleIt: Paint Details";

        public override string GetDescription() =>
            "Unity's Paint Details brush, also inside Dig Zones (the terrain hole keeps Unity's own brush out).\n" +
            "Click to paint the selected detail, Shift+click to erase, Ctrl+click to erase only the selected detail.";
    }
}
