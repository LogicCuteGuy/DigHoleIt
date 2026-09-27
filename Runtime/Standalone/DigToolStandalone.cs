using UnityEngine;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Simple mouse digging for standalone games: hold left button to dig, right button to add soil, middle button to
    /// paint <see cref="paintLayer"/>.
    /// Uses the legacy Input Manager; with the Input System only, call <see cref="EditAtScreen"/> yourself.
    /// </summary>
    [AddComponentMenu("DigHoleIt/Dig Tool (Standalone)")]
    public class DigToolStandalone : MonoBehaviour
    {
        public Camera cam;
        public DigZoneRuntimeStandalone[] zones;
        public float reach = 50f;
        public float radius = 1f;
        [Tooltip("Seconds between edits while a button is held.")]
        public float interval = 0.1f;
        [Tooltip("Layer given to added soil: 0 = auto, 1-4 = terrain layers 0-3, 5 = dug soil.")]
        [Range(0, DigFormat.LayerMax)] public int addLayer;
        [Tooltip("Layer painted with the middle button: 0 = auto (erase paint), 1-4 = terrain layers 0-3, 5 = dug soil.")]
        [Range(0, DigFormat.LayerMax)] public int paintLayer = 1;

        private float _next;

        private void Reset()
        {
            cam = Camera.main;
            zones = FindObjectsByType<DigZoneRuntimeStandalone>(FindObjectsSortMode.None);
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Time.time < _next) return;
            int op = Input.GetMouseButton(0) ? DigFormat.OpDig
                   : Input.GetMouseButton(1) ? DigFormat.OpAdd
                   : Input.GetMouseButton(2) ? DigFormat.OpPaint : -1;
            if (op < 0) return;
            _next = Time.time + interval;
            EditAtScreen(Input.mousePosition, op);
#endif
        }

        /// <summary>Digs, adds or paints (DigFormat.Op*) where the screen point hits a zone.</summary>
        public bool EditAtScreen(Vector2 screen, int op)
        {
            Camera c = cam != null ? cam : Camera.main;
            if (c == null || zones == null) return false;
            Ray ray = c.ScreenPointToRay(screen);

            foreach (DigZoneRuntimeStandalone z in zones)
            {
                if (z == null || !z.Raycast(ray, reach, out Vector3 hit)) continue;
                float offset = op == DigFormat.OpDig ? radius * 0.4f : op == DigFormat.OpAdd ? -radius * 0.4f : 0f;
                z.LocalEdit(hit + ray.direction * offset, radius, op, op == DigFormat.OpPaint ? paintLayer : addLayer);
                return true;
            }
            return false;
        }
    }
}
