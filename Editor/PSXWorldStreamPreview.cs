using System.Collections.Generic;
using SplashEdit.RuntimeCode;
using UnityEditor;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Scene view preview of world streaming: region outlines with their size,
    /// objects that stay loaded, and the load and unload distance around the
    /// selection (or the scene camera when nothing is selected).
    /// </summary>
    /// <remarks>
    /// The plan comes from the same PSXWorldStreamPlanner the export runs, so the
    /// regions drawn are the regions that ship.
    /// </remarks>
    [InitializeOnLoad]
    public static class PSXWorldStreamPreview
    {
        private static PSXWorldStreamPlan s_plan;
        private static PSXWorldStreamInputs s_inputs;
        private static PSXSceneExporter s_planFor;
        private static double s_planTime = -1;
        private const double k_RefreshSeconds = 1.0;

        private static readonly Color k_Resident = new Color(1f, 0.55f, 0.1f, 1f);
        private static readonly Color k_InLoad = new Color(0.3f, 1f, 0.4f, 1f);
        private static readonly Color k_InUnload = new Color(1f, 0.9f, 0.3f, 1f);
        private static readonly Color k_Far = new Color(0.55f, 0.65f, 0.8f, 0.6f);

        static PSXWorldStreamPreview()
        {
            PSXWorldStreamPlanner.OrderingTableSize = () => SplashSettings.OtSize;
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.hierarchyChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
        }

        public static void Invalidate()
        {
            s_planTime = -1;
            SceneView.RepaintAll();
        }

        /// <summary>The live plan for this exporter, rebuilt at most once a second.</summary>
        public static PSXWorldStreamPlan GetPlan(PSXSceneExporter exporter)
        {
            double now = EditorApplication.timeSinceStartup;
            if (s_plan == null || s_planFor != exporter || s_planTime < 0 || now - s_planTime > k_RefreshSeconds)
            {
                s_inputs = PSXWorldStreamPlanner.FromScene(exporter);
                s_plan = PSXWorldStreamPlanner.Build(s_inputs);
                s_planFor = exporter;
                s_planTime = now;
            }
            return s_plan;
        }

        private static void OnSceneGui(SceneView view)
        {
            if (Event.current.type != EventType.Repaint) return;
            var exporter = Object.FindFirstObjectByType<PSXSceneExporter>();
            if (exporter == null || !exporter.StreamWorldGeometry || !exporter.PreviewStreaming) return;

            var plan = GetPlan(exporter);
            if (plan.Regions.Count == 0) return;
            float gte = exporter.GTEScaling;
            float k = gte / 4096f;

            Vector3 focus = Selection.activeTransform != null
                ? Selection.activeTransform.position
                : view.camera.transform.position;
            long fx = Mathf.RoundToInt(focus.x / gte * 4096f), fz = Mathf.RoundToInt(focus.z / gte * 4096f);
            long loadSq = (long)plan.LoadRadius * plan.LoadRadius;
            long unloadSq = (long)plan.UnloadRadius * plan.UnloadRadius;

            var label = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };

            for (int i = 0; i < plan.Regions.Count; i++)
            {
                var r = plan.Regions[i];
                // PS1 Y points down: Unity y = -psY.
                var min = new Vector3(r.MinX * k, -r.MaxY * k, r.MinZ * k);
                var max = new Vector3(r.MaxX * k, -r.MinY * k, r.MaxZ * k);
                long d = PSXWorldStreamPlanner.DistSq(r, fx, fz);
                Color c = d <= loadSq ? k_InLoad : d <= unloadSq ? k_InUnload : k_Far;

                Handles.color = c;
                Handles.DrawWireCube((min + max) * 0.5f, max - min);
                var floor = new[]
                {
                    new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                    new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                };
                Handles.DrawSolidRectangleWithOutline(floor, new Color(c.r, c.g, c.b, 0.06f), c);

                label.normal.textColor = c;
                Handles.Label(new Vector3((min.x + max.x) * 0.5f, max.y, (min.z + max.z) * 0.5f),
                              $"{r.ByteSize / 1024} KB", label);
            }

            // Objects that stay loaded whatever the camera does.
            Handles.color = k_Resident;
            for (int i = 0; i < s_inputs.Exporters.Length; i++)
            {
                int[] a = s_inputs.Aabbs[i];
                if (a == null || plan.Streamed.Contains(i)) continue;
                var min = new Vector3(a[0] * k, -a[4] * k, a[2] * k);
                var max = new Vector3(a[3] * k, -a[1] * k, a[5] * k);
                Handles.DrawWireCube((min + max) * 0.5f, max - min);
            }

            Handles.color = k_InLoad;
            Handles.DrawWireDisc(focus, Vector3.up, plan.LoadRadius * k, 2f);
            Handles.color = k_InUnload;
            Handles.DrawWireDisc(focus, Vector3.up, plan.UnloadRadius * k);
            label.normal.textColor = k_InLoad;
            Handles.Label(focus + new Vector3(0f, 0f, plan.LoadRadius * k), "loads here", label);
        }
    }
}
