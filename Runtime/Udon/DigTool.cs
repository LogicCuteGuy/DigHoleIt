using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace LogicCuteGuy.DigHoleIt.Udon
{
    /// <summary>
    /// Shovel for a VRC_Pickup. Hold Use to dig, add soil or paint where the tip points, at a fixed rate.
    /// Works for desktop (click) and VR (trigger).
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class DigTool : UdonSharpBehaviour
    {
        public const int ModeDig = 0;
        public const int ModeAdd = 1;
        public const int ModePaint = 2;

        public DigZoneRuntime[] zones;
        [Tooltip("Ray origin and direction (forward). Defaults to this transform.")]
        public Transform tip;
        [Tooltip("Layers the ray can hit. Include the Dig Zone chunk layer.")]
        public LayerMask layers = -1;
        public float reach = 2.5f;
        public float radius = 0.75f;
        [Tooltip("0 = dig, 1 = add soil, 2 = paint")]
        public int mode = ModeDig;
        [Tooltip("Layer painted in paint mode: 0 = auto (erase paint), 1-4 = terrain layers 0-3, 5 = dug soil.")]
        public int paintLayer = 1;
        [Tooltip("Layer given to added soil: 0 = auto, 1-4 = terrain layers 0-3, 5 = dug soil.")]
        public int addLayer;
        [Tooltip("Seconds between edits while Use is held.")]
        public float interval = 0.25f;
        [Tooltip("Optional objects shown for the current mode.")]
        public GameObject digIndicator;
        public GameObject addIndicator;
        public GameObject paintIndicator;

        private bool _held;
        private float _next;
        private VRC_Pickup _pickup;

        private void Start()
        {
            _pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            _RefreshIndicators();
        }

        public override void OnPickupUseDown()
        {
            _held = true;
            _next = 0f;
        }

        public override void OnPickupUseUp() { _held = false; }

        public override void OnDrop() { _held = false; }

        /// <summary>Switches between dig and add. Wire to a UI button or a second interaction.</summary>
        public void _ToggleMode()
        {
            mode = mode == ModeDig ? ModeAdd : ModeDig;
            _RefreshIndicators();
        }

        /// <summary>Cycles dig, add, paint.</summary>
        public void _NextMode()
        {
            mode = (mode + 1) % 3;
            _RefreshIndicators();
        }

        private void Update()
        {
            if (!_held || Time.time < _next) return;
            _next = Time.time + interval;
            _DigOnce();
        }

        private void _DigOnce()
        {
            Transform t = tip != null ? tip : transform;
            Vector3 origin = t.position;
            Vector3 dir = t.forward;

            Vector3 point;
            RaycastHit hit;
            if (Physics.Raycast(origin, dir, out hit, reach, layers, QueryTriggerInteraction.Ignore))
            {
                // Dig centred slightly into the ground, add centred slightly above it, paint on it.
                float offset = mode == ModeDig ? radius * 0.4f : mode == ModeAdd ? -radius * 0.4f : 0f;
                point = hit.point + dir * offset;
            }
            else
            {
                // The tip may already be buried (rays that start inside a collider do not hit it).
                if (mode != ModeDig) return;
                point = origin;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                DigZoneRuntime z = zones[i];
                if (z == null || !z._ContainsWorld(point)) continue;
                if (hit.collider == null && !z._IsSolidAt(point)) return;
                int op = mode == ModePaint ? DigFormat.OpPaint : mode == ModeAdd ? DigFormat.OpAdd : DigFormat.OpDig;
                z._LocalEditLayer(point, radius, op, mode == ModePaint ? paintLayer : addLayer);
                _Haptic();
                return;
            }
        }

        private void _Haptic()
        {
            if (_pickup == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null || !lp.IsUserInVR()) return;
            lp.PlayHapticEventInHand(_pickup.currentHand, 0.08f, 0.35f, 120f);
        }

        private void _RefreshIndicators()
        {
            if (digIndicator != null) digIndicator.SetActive(mode == ModeDig);
            if (addIndicator != null) addIndicator.SetActive(mode == ModeAdd);
            if (paintIndicator != null) paintIndicator.SetActive(mode == ModePaint);
        }
    }
}
