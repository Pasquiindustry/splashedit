using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Scene-level navigation bake settings.
    /// Use this when a scene needs nav regions and AI support without requiring a PSXPlayer.
    /// </summary>
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXPlayer.png")]
    public class PSXNavigationSettings : MonoBehaviour
    {
        [Header("Agent")]
        [Tooltip("Reference agent height used when baking navigation.")]
        [SerializeField] private float agentHeight = 1.8f;

        [Tooltip("Reference agent radius used when baking navigation.")]
        [SerializeField] private float agentRadius = 0.5f;

        [Tooltip("Maximum height the baked agent can step up.")]
        [SerializeField] private float maxStepHeight = 0.35f;

        [Tooltip("Maximum walkable slope angle in degrees.")]
        [SerializeField] private float walkableSlopeAngle = 46.0f;

        [Header("Voxelization")]
        [SerializeField] private float navCellSize = 0.05f;
        [SerializeField] private float navCellHeight = 0.025f;

        [Header("Navigation - Advanced")]
        [SerializeField] private int navMinRegionArea = 8;
        [SerializeField] private int navMergeRegionArea = 20;
        [SerializeField] private float navMaxSimplifyError = 1.3f;
        [SerializeField] private float navMaxEdgeLength = 12.0f;
        [SerializeField] private NavPartitionMethod navPartitionMethod = NavPartitionMethod.Watershed;
        [SerializeField] private float navDetailSampleDist = 6.0f;
        [SerializeField] private float navDetailMaxError = 0.025f;
        [SerializeField] private float navMaxPlaneError = 0.15f;

        [Header("Start Region")]
        [Tooltip("Fallback point used to choose the start nav region when no PSXPlayer exists.")]
        [SerializeField] private Transform spawnAnchor;

        public float AgentHeight => agentHeight;
        public float AgentRadius => agentRadius;
        public float MaxStepHeight => maxStepHeight;
        public float WalkableSlopeAngle => walkableSlopeAngle;
        public float NavCellSize => navCellSize;
        public float NavCellHeight => navCellHeight;
        public int NavMinRegionArea => navMinRegionArea;
        public int NavMergeRegionArea => navMergeRegionArea;
        public float NavMaxSimplifyError => navMaxSimplifyError;
        public float NavMaxEdgeLength => navMaxEdgeLength;
        public NavPartitionMethod NavPartitionMethod => navPartitionMethod;
        public float NavDetailSampleDist => navDetailSampleDist;
        public float NavDetailMaxError => navDetailMaxError;
        public float NavMaxPlaneError => navMaxPlaneError;
        public Vector3 SpawnPoint => spawnAnchor != null ? spawnAnchor.position : transform.position;

        public void ApplyIndoorPreset()
        {
            navCellSize = 0.05f;
            navCellHeight = 0.025f;
            navMinRegionArea = 8;
            navMergeRegionArea = 20;
            navMaxSimplifyError = 1.3f;
            navMaxEdgeLength = 12.0f;
            navPartitionMethod = NavPartitionMethod.Watershed;
            navDetailSampleDist = 6.0f;
            navDetailMaxError = 0.025f;
            navMaxPlaneError = 0.15f;
        }

        public void ApplyTerrainPreset()
        {
            navCellSize = 0.08f;
            navCellHeight = 0.04f;
            navMinRegionArea = 4;
            navMergeRegionArea = 6;
            navMaxSimplifyError = 0.4f;
            navMaxEdgeLength = 5.0f;
            navPartitionMethod = NavPartitionMethod.Monotone;
            navDetailSampleDist = 4.0f;
            navDetailMaxError = 0.02f;
            navMaxPlaneError = 0.1f;
        }

        public void ApplyMultiLevelPreset()
        {
            navCellSize = 0.05f;
            navCellHeight = 0.02f;
            navMinRegionArea = 6;
            navMergeRegionArea = 12;
            navMaxSimplifyError = 0.8f;
            navMaxEdgeLength = 8.0f;
            navPartitionMethod = NavPartitionMethod.Layer;
            navDetailSampleDist = 5.0f;
            navDetailMaxError = 0.02f;
            navMaxPlaneError = 0.12f;
        }
    }
}
