using UnityEngine;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Mouse pen for standalone games: hold the left button to use the current <see cref="mode"/> (dig, add soil, paint a
    /// terrain layer, plant trees or details), the right button to add soil and the middle button to paint
    /// <see cref="paintLayer"/>. With <see cref="showSettings"/> a panel picks the mode, option, size and rate
    /// (Tab hides it).
    /// Uses the legacy Input Manager; with the Input System only, call <see cref="EditAtScreen"/> yourself.
    /// </summary>
    [AddComponentMenu("DigHoleIt/Dig Tool (Standalone)")]
    public class DigToolStandalone : MonoBehaviour
    {
        public enum Mode { Dig, Add, Paint, Tree, Detail, Smooth }

        public Camera cam;
        public DigZoneRuntimeStandalone[] zones;
        public float reach = 50f;
        public float radius = 1f;
        [Tooltip("Seconds between edits while a button is held.")]
        public float interval = 0.1f;
        [Tooltip("What the left button does.")]
        public Mode mode = Mode.Dig;
        [Tooltip("Layer given to added soil: 0 = auto, 1-4 = terrain layers 0-3, 5 = dug soil, 6-17 = terrain layers 4-15.")]
        [Range(0, DigFormat.LayerMax)] public int addLayer;
        [Tooltip("Layer painted: 0 = auto (erase paint), 1-4 = terrain layers 0-3, 5 = dug soil, 6-17 = terrain layers 4-15.")]
        [Range(0, DigFormat.LayerMax)] public int paintLayer = 1;
        [Tooltip("How far smooth mode blends each edit (0..1).")]
        [Range(0f, 1f)] public float smoothStrength = 0.5f;

        [Header("Trees And Details")]
        [Tooltip("Tree mode places the zone's tree prefab with this index; -1 erases spawned trees in the brush.")]
        public int treeIndex;
        [Tooltip("Detail mode places the zone's detail prefab with this index; -1 erases spawned details in the brush.")]
        public int detailIndex;
        [Tooltip("No tree is planted closer than this to another spawned tree (meters).")]
        public float treeSpacing = 2f;
        [Tooltip("No detail is planted closer than this to another spawned detail (meters).")]
        public float detailSpacing = 0.35f;
        [Tooltip("Details tried per edit, scattered over the brush.")]
        public int detailsPerEdit = 3;

        [Header("Look And Settings")]
        [Tooltip("Optional: shown at the brush, scaled to the brush size.")]
        public Transform cursor;
        [Tooltip("Draws the settings panel.")]
        public bool showSettings = true;
        [Tooltip("Names of the terrain layers, for the panel.")]
        public string[] layerNames;

        private float _next;
        private DigZoneRuntimeStandalone _lastZone;
        private Rect _panel = new Rect(10, 10, 240, 250);

        private static readonly string[] ModeNames = { "Dig", "Add", "Paint", "Tree", "Detail", "Smooth" };

        private void Reset()
        {
            cam = Camera.main;
            zones = FindObjectsByType<DigZoneRuntimeStandalone>(FindObjectsSortMode.None);
        }

        private void Start()
        {
            // A tool dropped in from a prefab has no zones yet: use every zone in the scene.
            if (zones == null || zones.Length == 0) zones = FindObjectsByType<DigZoneRuntimeStandalone>(FindObjectsSortMode.None);
            // And the names of the first zone terrain's layers for the panel.
            DigZoneRuntimeStandalone z = FirstZone();
            if ((layerNames == null || layerNames.Length == 0) && z != null && z.Zone != null && z.Zone.terrain != null)
            {
                TerrainLayer[] layers = z.Zone.terrain.terrainData.terrainLayers;
                layerNames = new string[layers.Length];
                for (int i = 0; i < layers.Length; i++) layerNames[i] = layers[i] != null ? layers[i].name : "Layer " + i;
            }
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Tab)) showSettings = !showSettings;
            Vector2 mouse = Input.mousePosition;
            UpdateCursor(mouse);
            // GUI space has y down.
            if (showSettings && _panel.Contains(new Vector2(mouse.x, Screen.height - mouse.y))) return;
            if (Time.time < _next) return;
            Mode m = Input.GetMouseButton(0) ? mode
                   : Input.GetMouseButton(1) ? Mode.Add
                   : Input.GetMouseButton(2) ? Mode.Paint : (Mode)(-1);
            if (m < 0) return;
            _next = Time.time + interval;
            UseAtScreen(mouse, m);
#endif
        }

        /// <summary>Digs, adds or paints (DigFormat.Op*) where the screen point hits a zone.</summary>
        public bool EditAtScreen(Vector2 screen, int op)
        {
            if (!HitAtScreen(screen, out Ray ray, out DigZoneRuntimeStandalone z, out Vector3 hit)) return false;
            if (op == DigFormat.OpTree || op == DigFormat.OpDetail)
            {
                z.LocalEdit(hit, radius, op, op == DigFormat.OpTree ? treeIndex + 1 : detailIndex + 1);
                return true;
            }
            float offset = op == DigFormat.OpDig ? radius * 0.4f : op == DigFormat.OpAdd ? -radius * 0.4f : 0f;
            int layer = op == DigFormat.OpPaint ? paintLayer : op == DigFormat.OpSmooth
                ? Mathf.Clamp(Mathf.RoundToInt(smoothStrength * DigFormat.SmoothSteps), 1, DigFormat.SmoothSteps) : addLayer;
            z.LocalEdit(hit + ray.direction * offset, radius, op, layer);
            return true;
        }

        /// <summary>Uses <paramref name="m"/> where the screen point hits a zone; trees and details scatter over the brush.</summary>
        public bool UseAtScreen(Vector2 screen, Mode m)
        {
            if (m == Mode.Dig) return EditAtScreen(screen, DigFormat.OpDig);
            if (m == Mode.Add) return EditAtScreen(screen, DigFormat.OpAdd);
            if (m == Mode.Paint) return EditAtScreen(screen, DigFormat.OpPaint);
            if (m == Mode.Smooth) return EditAtScreen(screen, DigFormat.OpSmooth);

            bool tree = m == Mode.Tree;
            int index = tree ? treeIndex : detailIndex;
            int op = tree ? DigFormat.OpTree : DigFormat.OpDetail;
            if (index < 0) return EditAtScreen(screen, op);

            Camera c = cam != null ? cam : Camera.main;
            if (c == null) return false;
            // Brush radius in pixels at the hit, for the scatter.
            if (!HitAtScreen(screen, out _, out _, out Vector3 centre)) return false;
            float px = Vector2.Distance(c.WorldToScreenPoint(centre), c.WorldToScreenPoint(centre + c.transform.right * radius));
            float spacing = tree ? treeSpacing : detailSpacing;
            bool any = false;
            for (int k = 0, n = tree ? 1 : Mathf.Max(1, detailsPerEdit); k < n; k++)
            {
                Vector2 s = screen + Random.insideUnitCircle * px;
                if (!HitAtScreen(s, out _, out DigZoneRuntimeStandalone z, out Vector3 hit)) continue;
                GameObject[] prefabs = tree ? z.treePrefabs : z.detailPrefabs;
                if (prefabs == null || index >= prefabs.Length || prefabs[index] == null)
                {
                    Debug.LogWarning($"[DigHoleIt] '{z.name}' has no {(tree ? "tree" : "detail")} prefab {index}. Fill Tree Prefabs / Detail Prefabs on its runtime.", z);
                    return false;
                }
                if (z.SpawnedNear(hit, spacing, tree)) continue;
                z.LocalEdit(hit, radius, op, index + 1);
                any = true;
            }
            return any;
        }

        private bool HitAtScreen(Vector2 screen, out Ray ray, out DigZoneRuntimeStandalone zone, out Vector3 hit)
        {
            Camera c = cam != null ? cam : Camera.main;
            ray = default;
            zone = null;
            hit = default;
            if (c == null || zones == null) return false;
            ray = c.ScreenPointToRay(screen);
            foreach (DigZoneRuntimeStandalone z in zones)
            {
                if (z == null || !z.Raycast(ray, reach, out hit)) continue;
                zone = _lastZone = z;
                return true;
            }
            return false;
        }

        private void UpdateCursor(Vector2 screen)
        {
            if (cursor == null) return;
            bool on = HitAtScreen(screen, out _, out _, out Vector3 hit);
            cursor.gameObject.SetActive(on);
            if (!on) return;
            cursor.position = hit;
            cursor.localScale = Vector3.one * (radius * 2f);
        }

        // ---- Settings panel ----

        private void OnGUI()
        {
            if (!showSettings) return;
            GUILayout.BeginArea(_panel, GUI.skin.box);
            GUILayout.Label("Dig Pen  (Tab hides)");
            mode = (Mode)GUILayout.SelectionGrid((int)mode, ModeNames, 3);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(28))) StepOption(-1);
            GUILayout.Label(OptionText(), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(28))) StepOption(1);
            GUILayout.EndHorizontal();

            float max = MaxRadius();
            GUILayout.Label($"Size {radius:F2} m");
            radius = GUILayout.HorizontalSlider(radius, 0.25f, max);
            float rate = interval > 0f ? 1f / interval : 10f;
            GUILayout.Label($"Rate {rate:F1} /s");
            interval = 1f / GUILayout.HorizontalSlider(rate, 1f, 20f);

            GUILayout.Space(6);
            if (GUILayout.Button("Reset zones"))
                foreach (DigZoneRuntimeStandalone z in zones)
                    if (z != null) z.ResetToBaked();
            GUILayout.Label("Left: use   Right: add   Middle: paint", GUI.skin.label);
            GUILayout.EndArea();
        }

        private float MaxRadius()
        {
            DigZoneRuntimeStandalone z = FirstZone();
            return z != null && z.Zone != null ? Mathf.Max(0.5f, z.Zone.maxBrushRadius) : 3f;
        }

        private DigZoneRuntimeStandalone FirstZone()
        {
            if (zones != null)
                foreach (DigZoneRuntimeStandalone z in zones)
                    if (z != null) return z;
            return _lastZone;
        }

        /// <summary>Steps the option of the current mode: soil or paint layer, tree or detail prefab (or erase).</summary>
        public void StepOption(int step)
        {
            switch (mode)
            {
                case Mode.Paint: paintLayer = StepLayer(paintLayer, step); break;
                case Mode.Smooth: smoothStrength = Mathf.Clamp(Mathf.Round(smoothStrength * 10f + step) / 10f, 0.1f, 1f); break;
                case Mode.Add: addLayer = StepLayer(addLayer, step); break;
                case Mode.Tree: treeIndex = StepIndex(treeIndex, Prefabs(true), step); break;
                case Mode.Detail: detailIndex = StepIndex(detailIndex, Prefabs(false), step); break;
            }
        }

        /// <summary>Next layer value in the order auto, dug soil, then the terrain layers (as many as <see cref="layerNames"/>).</summary>
        private int StepLayer(int value, int step)
        {
            int count = layerNames != null && layerNames.Length > 0 ? Mathf.Min(layerNames.Length, DigFormat.MaxTerrainLayers) : DigFormat.MaxTerrainLayers;
            // Auto and invalid values count as auto (-2).
            int choice = value == DigFormat.LayerDugSoil ? -1 : DigFormat.TerrainLayerOf(value);
            if (choice == -1 && value != DigFormat.LayerDugSoil) choice = -2;
            choice = (choice + 2 + step + count + 2) % (count + 2) - 2;
            return choice == -2 ? DigFormat.LayerAuto : choice == -1 ? DigFormat.LayerDugSoil : DigFormat.PaintValue(choice);
        }

        /// <summary>Next prefab index; -1 is erase.</summary>
        private static int StepIndex(int index, GameObject[] prefabs, int step)
        {
            int count = prefabs != null ? prefabs.Length : 0;
            return (index + 1 + step + count + 1) % (count + 1) - 1;
        }

        private GameObject[] Prefabs(bool tree)
        {
            DigZoneRuntimeStandalone z = FirstZone();
            return z == null ? null : tree ? z.treePrefabs : z.detailPrefabs;
        }

        private string OptionText()
        {
            switch (mode)
            {
                case Mode.Add: return "Soil: " + LayerName(addLayer);
                case Mode.Paint: return "Layer: " + LayerName(paintLayer);
                case Mode.Smooth: return $"Strength: {smoothStrength * 100f:F0}%";
                case Mode.Tree:
                case Mode.Detail:
                    bool tree = mode == Mode.Tree;
                    int index = tree ? treeIndex : detailIndex;
                    if (index < 0) return "Erase";
                    GameObject[] prefabs = Prefabs(tree);
                    return prefabs != null && index < prefabs.Length && prefabs[index] != null ? prefabs[index].name : (tree ? "Tree " : "Detail ") + (index + 1);
                default: return "-";
            }
        }

        private string LayerName(int value)
        {
            if (value == DigFormat.LayerAuto) return "Auto";
            if (value == DigFormat.LayerDugSoil) return "Dug soil";
            int t = DigFormat.TerrainLayerOf(value);
            if (layerNames != null && t >= 0 && t < layerNames.Length && !string.IsNullOrEmpty(layerNames[t])) return layerNames[t];
            return "Terrain layer " + t;
        }
    }
}
