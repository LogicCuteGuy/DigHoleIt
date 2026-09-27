using UnityEngine;

namespace LogicCuteGuy.DigHoleIt
{
    /// <summary>
    /// Authoring component for a diggable box over a Terrain. The transform position is the grid's minimum corner;
    /// rotation and scale must stay identity. In VRChat builds this component is stripped (IEditorOnly) and the
    /// UdonSharp DigZoneRuntime carries the baked data instead.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("DigHoleIt/Dig Zone")]
    public class DigZone : MonoBehaviour
#if VRC_SDK_VRCSDK3
        , VRC.SDKBase.IEditorOnly
#endif
    {
        [Tooltip("Terrain this zone cuts into. The whole zone footprint must lie on it.")]
        public Terrain terrain;

        [Min(0.1f)] public float voxelSize = 0.5f;
        [Tooltip("Grid size in cells. Samples are cells + 1 per axis.")]
        public Vector3Int cells = new Vector3Int(64, 32, 64);
        [Tooltip("Cells per chunk axis. Smaller chunks remesh faster in Udon but cost more draw calls. PC 16, Quest 8-12.")]
        [Range(4, 32)] public int chunkCells = 16;

        [Header("Fit To Terrain")]
        [Min(0f)] public float depthBelowTerrain = 8f;
        [Min(0f)] public float headroomAboveTerrain = 4f;

        [Header("Limits")]
        [Tooltip("Untouched margin, in voxels, inside the terrain hole edge and above the zone floor, so the dug mesh always meets the terrain cleanly. The green box in the Scene view is what is left to dig in. 2-3 is typical.")]
        [Range(MinBorder, MaxBorder)] public int borderVoxels = 2;
        [Tooltip("Largest brush radius accepted from players, in metres.")]
        [Min(0.1f)] public float maxBrushRadius = 3f;

        [Header("Output")]
        public Material material;
        [Tooltip("Layer for chunk renderers and colliders. Dig tools raycast against it.")]
        public int chunkLayer;
        public DigZoneData data;

        [HideInInspector] public Transform chunkRoot;
        [HideInInspector] public MeshFilter[] chunkFilters;
        [HideInInspector] public MeshRenderer[] chunkRenderers;
        [HideInInspector] public MeshCollider[] chunkColliders;

        public const int MinBorder = 2;
        public const int MaxBorder = 16;

        private void OnValidate()
        {
            borderVoxels = Mathf.Clamp(borderVoxels, MinBorder, MaxBorder);
        }

        public Vector3 WorldToGrid(Vector3 world) => (world - transform.position) / voxelSize;
        public Vector3 GridToWorld(Vector3 grid) => transform.position + grid * voxelSize;

        public Bounds WorldBounds
        {
            get
            {
                Vector3 size = (Vector3)cells * voxelSize;
                return new Bounds(transform.position + size * 0.5f, size);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Bounds b = WorldBounds;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(b.center, b.size);

            if (data != null && data.editBox != null && data.editBox.Length == 6 && data.editBox[3] >= data.editBox[0])
            {
                int[] e = data.editBox;
                Vector3 min = GridToWorld(new Vector3(e[0], e[1], e[2]));
                Vector3 max = GridToWorld(new Vector3(e[3], e[4], e[5]));
                Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
                Gizmos.DrawWireCube((min + max) * 0.5f, max - min);
            }
        }
    }
}
