using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;

namespace LogicCuteGuy.DigHoleIt.Udon
{
    /// <summary>
    /// Shovel or pen for a VRC_Pickup. Hold Use to dig, add soil, smooth, paint a terrain layer, or plant trees and details
    /// where the tip points, at a fixed rate. Works for desktop (click) and VR (trigger).
    ///
    /// The optional settings UI (a world space canvas with VRC Ui Shape) calls the public methods without a leading
    /// underscore: SelectDig, SelectAdd, SelectPaint, SelectTree, SelectDetail, SelectSmooth, NextOption, PrevOption, OnSliderChanged,
    /// ResetZones. Settings are local to each player; the edits they make are networked.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class DigTool : UdonSharpBehaviour
    {
        public const int ModeDig = 0;
        public const int ModeAdd = 1;
        public const int ModePaint = 2;
        public const int ModeTree = 3;
        public const int ModeDetail = 4;
        public const int ModeSmooth = 5;
        public const int ModeCount = 6;

        [Tooltip("Optional: zones the tip may be buried in. Zones it points at are found from the hit collider.")]
        public DigZoneRuntime[] zones;
        [Tooltip("Ray origin and direction (forward). Defaults to this transform.")]
        public Transform tip;
        [Tooltip("Layers the ray can hit. Include the Dig Zone chunk layer.")]
        public LayerMask layers = -1;
        public float reach = 2.5f;
        public float radius = 0.75f;
        [Tooltip("0 = dig, 1 = add soil, 2 = paint, 3 = tree, 4 = detail, 5 = smooth")]
        public int mode = ModeDig;
        [Tooltip("Layer painted in paint mode: 0 = auto (erase paint), 1-4 = terrain layers 0-3, 5 = dug soil, 6-17 = terrain layers 4-15.")]
        public int paintLayer = 1;
        [Tooltip("Layer given to added soil: 0 = auto, 1-4 = terrain layers 0-3, 5 = dug soil, 6-17 = terrain layers 4-15.")]
        public int addLayer;
        [Tooltip("Seconds between edits while Use is held.")]
        public float interval = 0.25f;
        [Tooltip("How far smooth mode blends each edit (0..1).")]
        [Range(0f, 1f)] public float smoothStrength = 0.5f;
        [Tooltip("Optional objects shown for the current mode.")]
        public GameObject digIndicator;
        public GameObject addIndicator;
        public GameObject paintIndicator;

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

        [Header("Pen Look (optional)")]
        [Tooltip("Coloured with the mode colour.")]
        public Renderer tipRenderer;
        [Tooltip("One colour per mode: dig, add, paint, tree, detail, smooth.")]
        public Color[] modeColors;
        [Tooltip("Shown at the brush while the pen is held, scaled to the brush size.")]
        public Transform cursor;

        [Header("Settings UI (optional)")]
        [Tooltip("Names of the terrain layers, for the paint option label.")]
        public string[] layerNames;
        public Text modeLabel;
        public Text optionLabel;
        public Text sizeLabel;
        [Tooltip("Brush radius in meters.")]
        public Slider sizeSlider;
        [Tooltip("Edits per second.")]
        public Slider rateSlider;

        private DigZoneRuntime _lastZone;
        private bool _warnedEmpty;
        private bool _held;
        private bool _holding;
        private float _next;
        private VRC_Pickup _pickup;

        private void Start()
        {
            _pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            float r = radius, iv = interval;
            if (sizeSlider != null) sizeSlider.value = r;
            if (rateSlider != null && iv > 0f) rateSlider.value = 1f / iv;
            radius = r;
            interval = iv;
            if (cursor != null) cursor.gameObject.SetActive(false);
            _Refresh();
        }

        public override void OnPickup() { _holding = true; }

        public override void OnPickupUseDown()
        {
            _held = true;
            _next = 0f;
        }

        public override void OnPickupUseUp() { _held = false; }

        public override void OnDrop()
        {
            _held = false;
            _holding = false;
            if (cursor != null) cursor.gameObject.SetActive(false);
        }

        /// <summary>Switches between dig and add. Wire to a UI button or a second interaction.</summary>
        public void _ToggleMode()
        {
            mode = mode == ModeDig ? ModeAdd : ModeDig;
            _Refresh();
        }

        /// <summary>Cycles dig, add, paint, tree, detail, smooth.</summary>
        public void _NextMode()
        {
            mode = (mode + 1) % ModeCount;
            _Refresh();
        }

        // ---- Settings UI (local only: a network event is ignored) ----

        public void SelectDig() { _SetMode(ModeDig); }
        public void SelectAdd() { _SetMode(ModeAdd); }
        public void SelectPaint() { _SetMode(ModePaint); }
        public void SelectTree() { _SetMode(ModeTree); }
        public void SelectDetail() { _SetMode(ModeDetail); }
        public void SelectSmooth() { _SetMode(ModeSmooth); }
        public void NextOption() { _StepOption(1); }
        public void PrevOption() { _StepOption(-1); }

        /// <summary>Reads the size and rate sliders.</summary>
        public void OnSliderChanged()
        {
            if (NetworkCalling.InNetworkCall) return;
            if (sizeSlider != null) radius = sizeSlider.value;
            if (rateSlider != null && rateSlider.value > 0f) interval = 1f / rateSlider.value;
            _Refresh();
        }

        /// <summary>Asks every zone's DigSync for a reset (the instance master only, unless the sync allows everyone).</summary>
        public void ResetZones()
        {
            if (NetworkCalling.InNetworkCall) return;
            if (zones == null) return;
            for (int i = 0; i < zones.Length; i++)
                if (zones[i] != null && zones[i].sync != null) zones[i].sync._RequestReset();
        }

        private void _SetMode(int m)
        {
            if (NetworkCalling.InNetworkCall) return;
            mode = m;
            _Refresh();
        }

        /// <summary>Steps the option of the current mode: soil or paint layer, tree or detail prefab (or erase).</summary>
        private void _StepOption(int step)
        {
            if (NetworkCalling.InNetworkCall) return;
            if (mode == ModeSmooth) smoothStrength = Mathf.Clamp(Mathf.Round(smoothStrength * 10f + step) / 10f, 0.1f, 1f);
            else if (mode == ModePaint) paintLayer = _StepLayer(paintLayer, step);
            else if (mode == ModeAdd) addLayer = _StepLayer(addLayer, step);
            else if (mode == ModeTree || mode == ModeDetail)
            {
                DigZoneRuntime z = _FirstZone();
                GameObject[] prefabs = z == null ? null : mode == ModeTree ? z.treePrefabs : z.detailPrefabs;
                int count = prefabs == null ? 0 : prefabs.Length;
                // -1 is erase.
                int index = mode == ModeTree ? treeIndex : detailIndex;
                index = (index + 1 + step + count + 1) % (count + 1) - 1;
                if (mode == ModeTree) treeIndex = index;
                else detailIndex = index;
            }
            _Refresh();
        }

        /// <summary>Next layer value in the order auto, dug soil, then the terrain layers (as many as <see cref="layerNames"/>).</summary>
        private int _StepLayer(int value, int step)
        {
            int count = layerNames != null && layerNames.Length > 0 ? Mathf.Min(layerNames.Length, DigFormat.MaxTerrainLayers) : DigFormat.MaxTerrainLayers;
            // Auto and invalid values count as auto (-2).
            int choice = value == DigFormat.LayerDugSoil ? -1 : DigFormat.TerrainLayerOf(value);
            if (choice == -1 && value != DigFormat.LayerDugSoil) choice = -2;
            choice = (choice + 2 + step + count + 2) % (count + 2) - 2;
            return choice == -2 ? DigFormat.LayerAuto : choice == -1 ? DigFormat.LayerDugSoil : DigFormat.PaintValue(choice);
        }

        /// <summary>The zone whose prefab lists the UI shows: the first of <see cref="zones"/>, else the last one edited.</summary>
        private DigZoneRuntime _FirstZone()
        {
            if (zones != null)
                for (int i = 0; i < zones.Length; i++)
                    if (zones[i] != null) return zones[i];
            return _lastZone;
        }

        // ---- Editing ----

        private void Update()
        {
            if (!_holding) return;
            Transform t = tip != null ? tip : transform;
            RaycastHit hit;
            bool hits = Physics.Raycast(t.position, t.forward, out hit, reach, layers, QueryTriggerInteraction.Ignore);
            if (cursor != null)
            {
                cursor.gameObject.SetActive(hits);
                if (hits)
                {
                    cursor.position = hit.point;
                    cursor.localScale = Vector3.one * (radius * 2f);
                }
            }

            if (!_held || Time.time < _next) return;
            _next = Time.time + interval;
            if (mode == ModeTree || mode == ModeDetail) _PlantOnce(t);
            else _DigOnce(t, hits, hit);
        }

        private void _DigOnce(Transform t, bool hits, RaycastHit hit)
        {
            Vector3 dir = t.forward;
            Vector3 point;
            DigZoneRuntime z = null;
            if (hits)
            {
                // Dig centred slightly into the ground, add centred slightly above it, paint on it.
                float offset = mode == ModeDig ? radius * 0.4f : mode == ModeAdd ? -radius * 0.4f : 0f;
            if (mode == ModeSmooth) offset = 0f;
                point = hit.point + dir * offset;
                z = hit.collider.GetComponentInParent<DigZoneRuntime>();
            }
            else
            {
                // The tip may already be buried (rays that start inside a collider do not hit it).
                if (mode != ModeDig) return;
                point = t.position;
            }
            if (z == null) z = _ZoneContaining(point);
            if (z == null || !z._ContainsWorld(point)) return;
            if (!hits && !z._IsSolidAt(point)) return;
            int op = mode == ModePaint ? DigFormat.OpPaint : mode == ModeAdd ? DigFormat.OpAdd : mode == ModeSmooth ? DigFormat.OpSmooth : DigFormat.OpDig;
            int layer = mode == ModePaint ? paintLayer : mode == ModeSmooth
                ? Mathf.Clamp(Mathf.RoundToInt(smoothStrength * DigFormat.SmoothSteps), 1, DigFormat.SmoothSteps) : addLayer;
            _lastZone = z;
            z._LocalEditLayer(point, radius, op, layer);
            _Haptic();
        }

        /// <summary>Plants trees or details on the zone surface, scattered over the brush, or erases them (index -1).</summary>
        private void _PlantOnce(Transform t)
        {
            bool tree = mode == ModeTree;
            int index = tree ? treeIndex : detailIndex;
            int op = tree ? DigFormat.OpTree : DigFormat.OpDetail;
            Vector3 dir = t.forward;
            RaycastHit hit;

            if (index < 0)
            {
                if (!Physics.Raycast(t.position, dir, out hit, reach, layers, QueryTriggerInteraction.Ignore)) return;
                DigZoneRuntime ez = hit.collider.GetComponentInParent<DigZoneRuntime>();
                if (ez == null) return;
                ez._LocalEditLayer(hit.point, radius, op, 0);
                _Haptic();
                return;
            }

            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(dir, Vector3.forward);
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir);
            int tries = tree ? 1 : Mathf.Max(1, detailsPerEdit);
            float spacing = tree ? treeSpacing : detailSpacing;
            bool any = false;
            for (int k = 0; k < tries; k++)
            {
                Vector2 o = Random.insideUnitCircle * radius;
                Vector3 origin = t.position + side * o.x + up * o.y;
                if (!Physics.Raycast(origin, dir, out hit, reach, layers, QueryTriggerInteraction.Ignore)) continue;
                // Only the zone's own surface: spawned trees sit beside the zone, so they are not found here.
                DigZoneRuntime z = hit.collider.GetComponentInParent<DigZoneRuntime>();
                if (z == null) continue;
                _lastZone = z;
                GameObject[] prefabs = tree ? z.treePrefabs : z.detailPrefabs;
                if (prefabs == null || index >= prefabs.Length || prefabs[index] == null)
                {
                    if (!_warnedEmpty)
                    {
                        _warnedEmpty = true;
                        Debug.LogWarning("[DigHoleIt] '" + z.name + "' has no " + (tree ? "tree" : "detail") + " prefab " + index +
                            ". Fill Tree Prefabs / Detail Prefabs on its DigZoneRuntime.", z);
                    }
                    return;
                }
                if (z._SpawnedNear(hit.point, spacing, tree)) continue;
                z._LocalEditLayer(hit.point, radius, op, index + 1);
                any = true;
            }
            if (any) _Haptic();
        }

        private DigZoneRuntime _ZoneContaining(Vector3 point)
        {
            if (zones == null) return null;
            for (int i = 0; i < zones.Length; i++)
                if (zones[i] != null && zones[i]._ContainsWorld(point)) return zones[i];
            return null;
        }

        private void _Haptic()
        {
            if (_pickup == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null || !lp.IsUserInVR()) return;
            lp.PlayHapticEventInHand(_pickup.currentHand, 0.08f, 0.35f, 120f);
        }

        // ---- Display ----

        private void _Refresh()
        {
            if (digIndicator != null) digIndicator.SetActive(mode == ModeDig);
            if (addIndicator != null) addIndicator.SetActive(mode == ModeAdd);
            if (paintIndicator != null) paintIndicator.SetActive(mode == ModePaint);
            if (tipRenderer != null && modeColors != null && mode < modeColors.Length) tipRenderer.material.color = modeColors[mode];

            if (modeLabel != null)
                modeLabel.text = mode == ModeDig ? "Dig" : mode == ModeAdd ? "Add soil" : mode == ModePaint ? "Paint terrain"
                    : mode == ModeTree ? "Plant trees" : mode == ModeDetail ? "Plant details" : "Smooth";
            if (sizeLabel != null)
                sizeLabel.text = "Size " + radius.ToString("F2") + " m   Rate " + (interval > 0f ? (1f / interval).ToString("F1") : "-") + " /s";
            if (optionLabel != null) optionLabel.text = _OptionText();
        }

        private string _OptionText()
        {
            if (mode == ModeDig) return "-";
            if (mode == ModeSmooth) return "Strength: " + Mathf.RoundToInt(smoothStrength * 100f) + "%";
            if (mode == ModeAdd) return "Soil: " + _LayerName(addLayer);
            if (mode == ModePaint) return "Layer: " + _LayerName(paintLayer);
            bool tree = mode == ModeTree;
            int index = tree ? treeIndex : detailIndex;
            if (index < 0) return "Erase";
            DigZoneRuntime z = _FirstZone();
            GameObject[] prefabs = z == null ? null : tree ? z.treePrefabs : z.detailPrefabs;
            if (prefabs != null && index < prefabs.Length && prefabs[index] != null) return prefabs[index].name;
            return (tree ? "Tree " : "Detail ") + (index + 1);
        }

        private string _LayerName(int value)
        {
            if (value == DigFormat.LayerAuto) return "Auto";
            if (value == DigFormat.LayerDugSoil) return "Dug soil";
            int t = DigFormat.TerrainLayerOf(value);
            if (layerNames != null && t >= 0 && t < layerNames.Length && !string.IsNullOrEmpty(layerNames[t])) return layerNames[t];
            return "Terrain layer " + t;
        }
    }
}
