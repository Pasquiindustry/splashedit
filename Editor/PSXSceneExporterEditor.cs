using UnityEngine;
using UnityEditor;
using SplashEdit.RuntimeCode;
using System.Linq;

namespace SplashEdit.EditorCode
{
    [CustomEditor(typeof(PSXSceneExporter))]
    public class PSXSceneExporterEditor : UnityEditor.Editor
    {
        private SerializedProperty gteScalingProp;
        private SerializedProperty sceneLuaProp;
        private SerializedProperty fogEnabledProp;
        private SerializedProperty fogColorProp;
        private SerializedProperty fogDensityProp;
        private SerializedProperty sceneTypeProp;
        private SerializedProperty sceneNetworkIdProp; // Added: Networking ID prop
        private SerializedProperty cutscenesProp;
        private SerializedProperty animationsProp;
        private SerializedProperty loadingScreenProp;
        private SerializedProperty streamWorldProp;
        private SerializedProperty streamRegionSizeProp;
        private SerializedProperty streamLoadDistanceProp;
        private SerializedProperty previewStreamingProp;
        private SerializedProperty previewBVHProp;
        private SerializedProperty previewRoomsPortalsProp;
        private SerializedProperty previewPointLightsProp;
        private SerializedProperty bvhDepthProp;

        private bool showFog = true;
        private bool showNetworking = true; // Added: Foldout toggle for Networking
        private bool showCutscenes = true;
        private bool showDebug = false;
        private bool showStreamingAdvanced = false;

        private void OnEnable()
        {
            gteScalingProp = serializedObject.FindProperty("GTEScaling");
            sceneLuaProp = serializedObject.FindProperty("SceneLuaFile");
            fogEnabledProp = serializedObject.FindProperty("FogEnabled");
            fogColorProp = serializedObject.FindProperty("FogColor");
            fogDensityProp = serializedObject.FindProperty("FogDensity");
            sceneTypeProp = serializedObject.FindProperty("SceneType");
            sceneNetworkIdProp = serializedObject.FindProperty("SceneNetworkId"); // Added: Find networking prop
            cutscenesProp = serializedObject.FindProperty("Cutscenes");
            animationsProp = serializedObject.FindProperty("Animations");
            loadingScreenProp = serializedObject.FindProperty("LoadingScreenPrefab");
            streamWorldProp = serializedObject.FindProperty("StreamWorldGeometry");
            streamRegionSizeProp = serializedObject.FindProperty("StreamRegionSize");
            streamLoadDistanceProp = serializedObject.FindProperty("StreamLoadDistance");
            previewStreamingProp = serializedObject.FindProperty("PreviewStreaming");
            previewBVHProp = serializedObject.FindProperty("PreviewBVH");
            previewRoomsPortalsProp = serializedObject.FindProperty("PreviewRoomsPortals");
            previewPointLightsProp = serializedObject.FindProperty("PreviewPointLights");
            bvhDepthProp = serializedObject.FindProperty("BVHPreviewDepth");
        }

        private void OnDisable()
        {
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var exporter = (PSXSceneExporter)target;

            DrawExporterHeader();
            EditorGUILayout.Space(4);

            DrawSceneSettings();
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawNetworkingSection(); // Added: Draw Networking section
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawFogSection(exporter);
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawStreamingSection(exporter);
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawCutscenesSection();
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawAnimationsSection();
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawLoadingSection();
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawDebugSection();
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawSceneStats(exporter); // Updated: Pass exporter to access post-export stats

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawExporterHeader()
        {
            EditorGUILayout.BeginVertical(PSXEditorStyles.CardStyle);
            EditorGUILayout.LabelField("Scene Exporter", PSXEditorStyles.CardHeaderStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawSceneSettings()
        {
            EditorGUILayout.PropertyField(sceneTypeProp, new GUIContent("Scene Type"));

            bool isInterior = (PSXSceneType)sceneTypeProp.enumValueIndex == PSXSceneType.Interior;
            EditorGUILayout.LabelField(
                isInterior
                    ? "<color=#88aaff>Room/portal occlusion culling.</color>"
                    : "<color=#88cc88>BVH frustum culling.</color>",
                PSXEditorStyles.RichLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(gteScalingProp, new GUIContent("GTE Scaling"));
            EditorGUILayout.PropertyField(sceneLuaProp, new GUIContent("Scene Lua"));

            if (sceneLuaProp.objectReferenceValue != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15);
                if (GUILayout.Button("Edit", EditorStyles.miniButtonLeft, GUILayout.Width(50)))
                    AssetDatabase.OpenAsset(sceneLuaProp.objectReferenceValue);
                if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(50)))
                    sceneLuaProp.objectReferenceValue = null;
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        // Added: Dedicated Networking Section
        private void DrawNetworkingSection()
        {
            showNetworking = EditorGUILayout.Foldout(showNetworking, "Networking", true, PSXEditorStyles.FoldoutHeader);
            if (!showNetworking) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(sceneNetworkIdProp, new GUIContent("Scene Network Id", 
                "Stable id for this scene on the network. Leave empty to fall back to the derived hash."));
            
            if (string.IsNullOrEmpty(sceneNetworkIdProp.stringValue))
            {
                EditorGUILayout.LabelField(
                    "<color=#aaaaaa>Using derived hash (changes when scene objects change).</color>",
                    PSXEditorStyles.RichLabel);
            }
            else
            {
                EditorGUILayout.LabelField(
                    $"<color=#88cc88>Locked ID: {sceneNetworkIdProp.stringValue}</color>",
                    PSXEditorStyles.RichLabel);
            }
            EditorGUI.indentLevel--;
        }

        private void DrawFogSection(PSXSceneExporter exporter)
        {
            showFog = EditorGUILayout.Foldout(showFog, "Fog & Background", true, PSXEditorStyles.FoldoutHeader);
            if (!showFog) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(fogColorProp, new GUIContent("Background Color",
                "Background clear color. Also used as the fog blend target when fog is enabled."));

            EditorGUILayout.PropertyField(fogEnabledProp, new GUIContent("Distance Fog"));

            if (fogEnabledProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(fogDensityProp, new GUIContent("Density"));

                float gteScale = exporter.GTEScaling;
                int density = Mathf.Clamp(exporter.FogDensity, 1, 10);
                float fogFarUnity = (8000f / density) * gteScale / 4096f;
                float fogNearUnity = fogFarUnity / 3f;

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(
                    $"<color=#aaaaaa>GTE range: {fogNearUnity:F1} - {fogFarUnity:F1} units  |  " +
                    $"{8000f / (density * 3f):F0} - {8000f / density:F0} SZ</color>",
                    PSXEditorStyles.RichLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
        }

        private void DrawStreamingSection(PSXSceneExporter exporter)
        {
            EditorGUILayout.PropertyField(streamWorldProp, new GUIContent("Stream World Geometry",
                "For big open worlds. Scenery loads from disc piece by piece as the player gets close and is " +
                "dropped again behind them, so the scene needs much less RAM. Objects with a script, an " +
                "animation or cutscene track, an interaction or skinning always stay loaded."));
            if (!streamWorldProp.boolValue) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(previewStreamingProp, new GUIContent("Show in Scene View",
                "Draw each region's outline and size, objects that always stay loaded (orange), and the load " +
                "distance around the selected object, or around the Scene camera when nothing is selected."));

            showStreamingAdvanced = EditorGUILayout.Foldout(showStreamingAdvanced, "Advanced", true);
            if (showStreamingAdvanced)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(streamRegionSizeProp, new GUIContent("Region Size",
                    "How wide each loadable piece of the world is, in Unity units. Smaller pieces use less memory " +
                    "but are read from disc more often. 0 = SplashEdit picks the size that uses the least memory."));
                EditorGUILayout.PropertyField(streamLoadDistanceProp, new GUIContent("Load Distance",
                    "How close the player has to get before a piece loads, in Unity units. 0 = the distance the " +
                    "console can draw (the fog distance when fog is on), plus a margin so nothing pops in."));
                if (streamRegionSizeProp.floatValue < 0f) streamRegionSizeProp.floatValue = 0f;
                if (streamLoadDistanceProp.floatValue < 0f) streamLoadDistanceProp.floatValue = 0f;
                EditorGUI.indentLevel--;
            }

            // Live, from the same planner the export runs.
            serializedObject.ApplyModifiedProperties();
            var plan = PSXWorldStreamPreview.GetPlan(exporter);
            var st = plan.Stats;
            if (plan.Regions.Count > 0)
            {
                EditorGUILayout.LabelField(
                    $"<b>{st.RegionCount}</b> regions, <b>{st.StreamedObjects}</b> objects stream, " +
                    $"<b>{st.ResidentObjects}</b> always loaded",
                    PSXEditorStyles.RichLabel);
                EditorGUILayout.LabelField(
                    $"Streaming memory <b>{st.PoolBytes / 1024} KB</b> ({st.SlotCount} x {st.SlotBytes / 1024} KB), " +
                    (st.RamSaved > 0
                        ? $"saves <b>{st.RamSaved / 1024} KB</b> of RAM"
                        : $"<color=#ffaa44>uses {-st.RamSaved / 1024} KB more RAM than not streaming</color>"),
                    PSXEditorStyles.RichLabel);
                EditorGUILayout.LabelField(
                    $"<color=#aaaaaa>Regions {st.RegionSize:F0} units wide, load within {st.LoadDistance:F0} units</color>",
                    PSXEditorStyles.RichLabel);
            }
            foreach (string w in plan.Warnings)
                EditorGUILayout.HelpBox(w, MessageType.Warning);
            EditorGUI.indentLevel--;
        }

        private void DrawCutscenesSection()
        {
            showCutscenes = EditorGUILayout.Foldout(showCutscenes, "Cutscenes", true, PSXEditorStyles.FoldoutHeader);
            if (!showCutscenes) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(cutscenesProp, new GUIContent("Clips"), true);
            EditorGUI.indentLevel--;
        }

        private bool showAnimations = true;
        private void DrawAnimationsSection()
        {
            showAnimations = EditorGUILayout.Foldout(showAnimations, "Animations", true, PSXEditorStyles.FoldoutHeader);
            if (!showAnimations) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(animationsProp, new GUIContent("Clips"), true);
            EditorGUI.indentLevel--;
        }

        private void DrawLoadingSection()
        {
            EditorGUILayout.PropertyField(loadingScreenProp, new GUIContent("Loading Screen Prefab"));
            if (loadingScreenProp.objectReferenceValue != null)
            {
                var go = loadingScreenProp.objectReferenceValue as GameObject;
                if (go != null && go.GetComponentInChildren<PSXCanvas>() == null)
                {
                    EditorGUILayout.LabelField(
                        "<color=#ffaa44>Prefab has no PSXCanvas component.</color>",
                        PSXEditorStyles.RichLabel);
                }
            }
        }

        private void DrawDebugSection()
        {
            showDebug = EditorGUILayout.Foldout(showDebug, "Debug", true, PSXEditorStyles.FoldoutHeader);
            if (!showDebug) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(previewBVHProp, new GUIContent("Preview BVH"));
            if (previewBVHProp.boolValue)
                EditorGUILayout.PropertyField(bvhDepthProp, new GUIContent("BVH Depth"));
            EditorGUILayout.PropertyField(previewRoomsPortalsProp, new GUIContent("Preview Rooms/Portals"));
            EditorGUILayout.PropertyField(previewPointLightsProp, new GUIContent("Preview Point Lights",
                "Scene view: mark meshes reached by more Realtime/Mixed Point Lights than the PS1 applies (4), " +
                "and draw lines to the lights of the selected mesh."));
            EditorGUI.indentLevel--;
        }

        // Updated: Added post-export triangle count display
        private void DrawSceneStats(PSXSceneExporter exporter)
        {
            var exporters = FindObjectsByType<PSXObjectExporter>(FindObjectsSortMode.None);
            int total = exporters.Length;
            int active = exporters.Count(e => e.IsActive);
            int staticCol = exporters.Count(e => e.CollisionType == PSXCollisionType.Static);
            int dynamicCol = exporters.Count(e => e.CollisionType == PSXCollisionType.Dynamic);
            int triggerBoxes = FindObjectsByType<PSXTriggerBox>(FindObjectsSortMode.None).Length;

            EditorGUILayout.BeginVertical(PSXEditorStyles.CardStyle);
            EditorGUILayout.LabelField(
                $"<b>{active}</b>/{total} objects  |  <b>{staticCol}</b> static  <b>{dynamicCol}</b> dynamic  <b>{triggerBoxes}</b> triggers",
                PSXEditorStyles.RichLabel);

            // Display Last Export stats if an export has been run
            if (exporter.LastExportTriangleCount > 0)
            {
                EditorGUILayout.LabelField(
                    $"Last Export: <b>{exporter.LastExportTriangleCount}</b> triangles",
                    PSXEditorStyles.RichLabel);
            }
            EditorGUILayout.EndVertical();
        }

    }
}