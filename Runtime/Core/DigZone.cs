using System;
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
        [Tooltip("Chunk meshes get lightmap UVs and are marked Contribute GI, so baked lighting covers them like the terrain. " +
                 "Chunks changed at runtime can't keep their lightmap and switch to light probes: add a Light Probe Group over the zone.")]
        public bool bakedLighting = true;
        [Tooltip("Multiplies the chunks' lightmap resolution. At 1 chunks get the terrain's Scale In Lightmap (the same texel " +
                 "size as the terrain), but at least 16 texels across a chunk so chunk edges don't show. Raise it for sharper " +
                 "baked shadows in dug areas, lower it to save lightmap space.")]
        [Min(0.01f)] public float lightmapScale = 1f;
        public DigZoneData data;

        [HideInInspector] public Transform chunkRoot;
        // Only chunks with a surface get a GameObject. chunkIds[k] is the chunk index of chunkFilters[k] etc.; when
        // chunkIds is empty the arrays hold one entry per chunk (zones baked by 0.4 and earlier).
        [HideInInspector] public int[] chunkIds;
        [HideInInspector] public MeshFilter[] chunkFilters;
        [HideInInspector] public MeshRenderer[] chunkRenderers;
        [HideInInspector] public MeshCollider[] chunkColliders;
        [Tooltip("Inactive chunk object that runtimes copy when digging or adding soil gives an empty chunk a surface.")]
        [HideInInspector] public GameObject chunkTemplate;

        [NonSerialized] private int[] _slots;
        [NonSerialized] private int[] _slotsIds;
        [NonSerialized] private int _slotsCount;

        public const int MinBorder = 2;
        public const int MaxBorder = 16;
        /// <summary>Largest grid (samples) a zone may bake: 128 Mi, one byte each in memory at runtime.</summary>
        public const int MaxSamples = 128 * 1024 * 1024;

        private void OnValidate()
        {
            borderVoxels = Mathf.Clamp(borderVoxels, MinBorder, MaxBorder);
        }

        /// <summary>
        /// Index into <see cref="chunkFilters"/> / <see cref="chunkRenderers"/> / <see cref="chunkColliders"/> of chunk
        /// <paramref name="ci"/>, or -1 if the chunk has no GameObject.
        /// </summary>
        public int ChunkSlot(int ci)
        {
            if (chunkFilters == null || ci < 0) return -1;
            if (chunkIds == null || chunkIds.Length == 0) return ci < chunkFilters.Length ? ci : -1;
            int count = data != null ? data.ChunkCount : 0;
            if (_slots == null || !ReferenceEquals(_slotsIds, chunkIds) || _slotsCount != count)
            {
                _slots = new int[count];
                for (int i = 0; i < count; i++) _slots[i] = -1;
                for (int k = 0; k < chunkIds.Length; k++)
                    if (chunkIds[k] >= 0 && chunkIds[k] < count) _slots[chunkIds[k]] = k;
                _slotsIds = chunkIds;
                _slotsCount = count;
            }
            return ci < _slots.Length && _slots[ci] < chunkFilters.Length ? _slots[ci] : -1;
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
