using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    // -- Agent state indices must match AgentState enum in scenemanager.hh ----------
    public enum PSXAgentState
    {
        Idle        = 0,
        Patrol      = 1,
        Seek        = 2,
        Flee        = 3,
        Attack      = 4,
        Wander      = 5,
        Investigate = 6,
        Custom      = 7,
    }

    /// <summary>
    /// Per-state animation clip binding for a PSX skinned-mesh agent.
    /// Set Clip to null to leave that state's animation slot empty (0xFF in binary).
    /// </summary>
    [System.Serializable]
    public struct PSXAgentStateAnim
    {
        [HideInInspector] public PSXAgentState state;
        [Tooltip("Animation clip to play when entering this state. Leave empty for no animation.")]
        public PSXAnimationClip clip;
    }

    /// <summary>
    /// Marks a PSXObject as a native nav agent and exports its AI defaults into the splashpack.
    /// Vision, hearing, patrol waypoints, and per-state animation clips are all configured here
    /// and baked to the 28-byte SPLASHPACKAgentV2 binary format at export time.
    /// </summary>
    [AddComponentMenu("PSX Splash/PSX Agent")]
    [RequireComponent(typeof(PSXObjectExporter))]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXObjectExporter.png")]
    public class PSXAgent : MonoBehaviour
    {
        // -- Movement -------------------------------------------------------------
        [Header("Movement")]
        [Tooltip("Whether this agent starts active when the scene loads.")]
        [SerializeField] private bool startEnabled = true;

        [Tooltip("Default native move speed in Unity units/second (same scale as PSXPlayer speed).")]
        [Min(0.01f)]
        [SerializeField] private float moveSpeed = 3.0f;

        [Tooltip("Distance in Unity units at which a move-to target is considered reached.")]
        [Min(0f)]
        [SerializeField] private float stopDistance = 0.2f;

        // -- Vision ----------------------------------------------------------------
        [Header("Vision")]
        [Tooltip("Enable vision sensing. When enabled the agent can see targets within range and FOV.")]
        [SerializeField] private bool hasVision = false;

        [Tooltip("Maximum line-of-sight range in Unity units. 0 disables vision even if hasVision is true.")]
        [Min(0f)]
        [SerializeField] private float visionRange = 8f;

        [Tooltip("Full cone FOV in degrees (e.g. 90 = 45 deg each side). " +
                 "360 = omnidirectional vision inside the range sphere.")]
        [Range(1f, 360f)]
        [SerializeField] private float visionFovDegrees = 90f;

        [Tooltip("Maximum number of nav-region portal hops used for portal-graph line-of-sight. " +
                 "0 = same region only. Higher values cost more CPU; 2-3 covers most interior scenes.")]
        [Range(0, 8)]
        [SerializeField] private int visionRegionDepth = 2;

        // -- Hearing ----------------------------------------------------------------
        [Header("Hearing")]
        [Tooltip("Enable hearing sensing. Targets within hearingRange broadcast noise events to the agent.")]
        [SerializeField] private bool hasHearing = false;

        [Tooltip("Maximum hearing radius in Unity units. No geometry check is applied; pure range only.")]
        [Min(0f)]
        [SerializeField] private float hearingRange = 6f;

        // -- Alert ------------------------------------------------------------------
        [Header("Alert")]
        [Tooltip("Number of frames (at 30 fps) the agent stays 'alert' after losing sight/sound of a target " +
                 "before the onTargetLost Lua event fires and the state machine reverts.")]
        [Range(0, 300)]
        [SerializeField] private int alertTimeoutFrames = 60;

        // -- Patrol waypoints --------------------------------------------------------
        [Header("Patrol Waypoints")]
        [Tooltip("World-space patrol waypoints visited in order. Leave empty for no patrol. " +
                 "When populated the AGENT_FLAG_HAS_PATROL bit is set and the native runtime " +
                 "cycles through these points in the PATROL state. Maximum 8 waypoints.")]
        [SerializeField] private List<Vector3> patrolWaypoints = new List<Vector3>();

        // -- Per-state animation -----------------------------------------------------
        [Header("State Animations")]
        [Tooltip("Animation clip to play for each agent state. Leave a slot empty to keep the " +
                 "previous animation running when that state is entered.")]
        [SerializeField]
        private PSXAgentStateAnim[] stateAnims = new PSXAgentStateAnim[8]
        {
            new PSXAgentStateAnim { state = PSXAgentState.Idle },
            new PSXAgentStateAnim { state = PSXAgentState.Patrol },
            new PSXAgentStateAnim { state = PSXAgentState.Seek },
            new PSXAgentStateAnim { state = PSXAgentState.Flee },
            new PSXAgentStateAnim { state = PSXAgentState.Attack },
            new PSXAgentStateAnim { state = PSXAgentState.Wander },
            new PSXAgentStateAnim { state = PSXAgentState.Investigate },
            new PSXAgentStateAnim { state = PSXAgentState.Custom },
        };

        // -- Public accessors --------------------------------------------------------
        public bool StartEnabled        => startEnabled;
        public float MoveSpeed          => moveSpeed;
        public float StopDistance       => stopDistance;
        public bool HasVision           => hasVision;
        public float VisionRange        => visionRange;
        public float VisionFovDegrees   => visionFovDegrees;
        public int VisionRegionDepth    => visionRegionDepth;
        public bool HasHearing          => hasHearing;
        public float HearingRange       => hearingRange;
        public int AlertTimeoutFrames   => alertTimeoutFrames;
        public IReadOnlyList<Vector3> PatrolWaypoints => patrolWaypoints;
        public PSXAgentStateAnim[] StateAnims => stateAnims;

        /// <summary>
        /// Half-FOV cosine in fp12 format, matching the visionCosAngle field in
        /// SPLASHPACKAgentV2. cos(halfFovRad) * 4096, clamped to int16 range.
        /// </summary>
        public short VisionCosAngleFp12
        {
            get
            {
                float halfRad = visionFovDegrees * 0.5f * Mathf.Deg2Rad;
                float cosVal  = Mathf.Cos(halfRad);
                return (short)Mathf.Clamp(Mathf.RoundToInt(cosVal * 4096f), -4096, 4096);
            }
        }

        private void OnValidate()
        {
            moveSpeed       = Mathf.Max(0.01f, moveSpeed);
            stopDistance    = Mathf.Max(0f, stopDistance);
            visionRange     = Mathf.Max(0f, visionRange);
            hearingRange    = Mathf.Max(0f, hearingRange);

            // Clamp waypoint list
            if (patrolWaypoints != null && patrolWaypoints.Count > 8)
                patrolWaypoints.RemoveRange(8, patrolWaypoints.Count - 8);

            // Keep state labels in sync (defensive - editor shouldn't drift these)
            if (stateAnims != null && stateAnims.Length == 8)
                for (int i = 0; i < 8; i++)
                    stateAnims[i].state = (PSXAgentState)i;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            DrawGizmos(true);
        }
        private void OnDrawGizmos()
        {
            DrawGizmos(false);
        }

        private void DrawGizmos(bool selected)
        {
            if (!selected && !hasVision && !hasHearing && (patrolWaypoints == null || patrolWaypoints.Count == 0))
                return;

            var t = transform;
            var center = t.position + Vector3.up * 0.9f;
            float alpha = selected ? 0.85f : 0.35f;

            // Vision cone (yellow)
            if (hasVision && visionRange > 0.001f)
            {
                UnityEditor.Handles.color = new Color(1f, 0.95f, 0.2f, alpha * 0.18f);
                float halfRad = visionFovDegrees * 0.5f * Mathf.Deg2Rad;
                Vector3 left  = Quaternion.AngleAxis(-visionFovDegrees * 0.5f, Vector3.up) * t.forward;
                Vector3 right = Quaternion.AngleAxis( visionFovDegrees * 0.5f, Vector3.up) * t.forward;
                UnityEditor.Handles.DrawSolidArc(center, Vector3.up, left, visionFovDegrees, visionRange);
                UnityEditor.Handles.color = new Color(1f, 0.95f, 0.2f, alpha);
                UnityEditor.Handles.DrawLine(center, center + left  * visionRange);
                UnityEditor.Handles.DrawLine(center, center + right * visionRange);
                UnityEditor.Handles.DrawWireArc(center, Vector3.up, left, visionFovDegrees, visionRange);
            }

            // Hearing radius (cyan)
            if (hasHearing && hearingRange > 0.001f)
            {
                UnityEditor.Handles.color = new Color(0.2f, 0.9f, 1f, alpha * 0.12f);
                UnityEditor.Handles.DrawSolidDisc(center, Vector3.up, hearingRange);
                UnityEditor.Handles.color = new Color(0.2f, 0.9f, 1f, alpha);
                UnityEditor.Handles.DrawWireDisc(center, Vector3.up, hearingRange);
            }

            // Patrol waypoints (green dots + lines)
            if (patrolWaypoints != null && patrolWaypoints.Count > 0)
            {
                UnityEditor.Handles.color = new Color(0.3f, 1f, 0.4f, alpha);
                Vector3 prev = center;
                for (int i = 0; i < patrolWaypoints.Count; i++)
                {
                    Vector3 wp = patrolWaypoints[i];
                    UnityEditor.Handles.DrawLine(prev, wp + Vector3.up * 0.1f);
                    UnityEditor.Handles.DrawSolidDisc(wp + Vector3.up * 0.1f, Vector3.up, 0.12f);
                    UnityEditor.Handles.Label(wp + Vector3.up * 0.45f, i.ToString(),
                        UnityEditor.EditorStyles.miniLabel);
                    prev = wp + Vector3.up * 0.1f;
                }
                // Close loop line back to start
                if (patrolWaypoints.Count > 1)
                    UnityEditor.Handles.DrawLine(prev, patrolWaypoints[0] + Vector3.up * 0.1f);
            }
        }
#endif
    }
}

