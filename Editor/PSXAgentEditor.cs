using UnityEditor;
using UnityEngine;
using SplashEdit.RuntimeCode;

namespace SplashEdit.EditorCode
{
    [CustomEditor(typeof(PSXAgent))]
    [CanEditMultipleObjects]
    public class PSXAgentEditor : UnityEditor.Editor
    {
        private SerializedProperty _startEnabledProp;
        private SerializedProperty _moveSpeedProp;
        private SerializedProperty _stopDistanceProp;
        private SerializedProperty _hasVisionProp;
        private SerializedProperty _visionRangeProp;
        private SerializedProperty _visionFovDegreesProp;
        private SerializedProperty _visionRegionDepthProp;
        private SerializedProperty _hasHearingProp;
        private SerializedProperty _hearingRangeProp;
        private SerializedProperty _alertTimeoutFramesProp;
        private SerializedProperty _patrolWaypointsProp;
        private SerializedProperty _stateAnimsProp;

        private bool _moveFoldout    = true;
        private bool _visionFoldout  = true;
        private bool _hearFoldout    = true;
        private bool _alertFoldout   = false;
        private bool _patrolFoldout  = true;
        private bool _animFoldout    = false;
        private bool _luaFoldout     = false;
        private bool _runtimeFoldout = false;

        private static readonly string[] s_stateLabels =
            { "Idle","Patrol","Seek","Flee","Attack","Wander","Investigate","Custom" };

        private void OnEnable()
        {
            _startEnabledProp       = serializedObject.FindProperty("startEnabled");
            _moveSpeedProp          = serializedObject.FindProperty("moveSpeed");
            _stopDistanceProp       = serializedObject.FindProperty("stopDistance");
            _hasVisionProp          = serializedObject.FindProperty("hasVision");
            _visionRangeProp        = serializedObject.FindProperty("visionRange");
            _visionFovDegreesProp   = serializedObject.FindProperty("visionFovDegrees");
            _visionRegionDepthProp  = serializedObject.FindProperty("visionRegionDepth");
            _hasHearingProp         = serializedObject.FindProperty("hasHearing");
            _hearingRangeProp       = serializedObject.FindProperty("hearingRange");
            _alertTimeoutFramesProp = serializedObject.FindProperty("alertTimeoutFrames");
            _patrolWaypointsProp    = serializedObject.FindProperty("patrolWaypoints");
            _stateAnimsProp         = serializedObject.FindProperty("stateAnims");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var agent       = (PSXAgent)target;
            var exporter    = agent.GetComponent<PSXObjectExporter>();
            var sceneExp    = FindFirstObjectByType<PSXSceneExporter>();
            var navSettings = FindFirstObjectByType<PSXNavigationSettings>();
            var player      = FindFirstObjectByType<PSXPlayer>();

            DrawHeader(agent, exporter);
            EditorGUILayout.Space(4);

            _moveFoldout = PSXEditorStyles.DrawFoldoutCard("Movement", _moveFoldout, () =>
            {
                EditorGUILayout.PropertyField(_startEnabledProp, new GUIContent("Start Enabled"));
                EditorGUILayout.Slider(_moveSpeedProp, 0.01f, 12f, new GUIContent("Move Speed (u/s)"));
                EditorGUILayout.Slider(_stopDistanceProp, 0f, 2f, new GUIContent("Stop Distance (u)"));
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(
                    "Speed converts to fp12 per-frame at 30 fps. Same scale as PSXPlayer walk speed.",
                    PSXEditorStyles.RichLabel);
            });
            EditorGUILayout.Space(2);

            _visionFoldout = PSXEditorStyles.DrawFoldoutCard("Vision", _visionFoldout, () =>
            {
                EditorGUILayout.PropertyField(_hasVisionProp, new GUIContent("Enable Vision"));
                using (new EditorGUI.DisabledScope(!_hasVisionProp.boolValue))
                {
                    EditorGUILayout.Slider(_visionRangeProp, 0f, 30f, new GUIContent("Range (u)"));
                    EditorGUILayout.Slider(_visionFovDegreesProp, 1f, 360f,
                        new GUIContent("FOV (degrees, full cone)",
                            "Total angular width of the vision cone. 90 = 45 each side of forward."));
                    EditorGUILayout.IntSlider(_visionRegionDepthProp, 0, 8,
                        new GUIContent("Region Hop Depth",
                            "Max nav-region portal hops for portal-graph LOS. 0=same region only. 2-3 covers most interiors."));
                    float fov = _visionFovDegreesProp != null ? _visionFovDegreesProp.floatValue : 90f;
                    int fp12  = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Cos(fov * 0.5f * UnityEngine.Mathf.Deg2Rad) * 4096f), -4096, 4096);
                    EditorGUILayout.LabelField("visionCosAngle (fp12)", fp12.ToString(), EditorStyles.miniLabel);
                }
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Yellow cone arc shown in Scene view when selected.", PSXEditorStyles.RichLabel);
            });
            EditorGUILayout.Space(2);

            _hearFoldout = PSXEditorStyles.DrawFoldoutCard("Hearing", _hearFoldout, () =>
            {
                EditorGUILayout.PropertyField(_hasHearingProp, new GUIContent("Enable Hearing"));
                using (new EditorGUI.DisabledScope(!_hasHearingProp.boolValue))
                {
                    EditorGUILayout.Slider(_hearingRangeProp, 0f, 30f, new GUIContent("Range (u)"));
                    EditorGUILayout.LabelField("Pure range check, no geometry occlusion. Cyan disc in Scene view.", PSXEditorStyles.RichLabel);
                }
            });
            EditorGUILayout.Space(2);

            _alertFoldout = PSXEditorStyles.DrawFoldoutCard("Alert", _alertFoldout, () =>
            {
                if (_alertTimeoutFramesProp != null)
                {
                    EditorGUILayout.PropertyField(_alertTimeoutFramesProp,
                        new GUIContent("Alert Timeout (frames)",
                            "Frames the agent stays alert after losing sight/sound before onTargetLost fires. 60=2s@30fps."));
                    float secs = _alertTimeoutFramesProp.intValue / 30f;
                    EditorGUILayout.LabelField($"approx {secs:F1} s at 30 fps", EditorStyles.miniLabel);
                }
            });
            EditorGUILayout.Space(2);

            _patrolFoldout = PSXEditorStyles.DrawFoldoutCard("Patrol Waypoints", _patrolFoldout, () =>
            {
                if (_patrolWaypointsProp == null) return;
                int count = _patrolWaypointsProp.arraySize;
                EditorGUILayout.LabelField($"{count} / 8 waypoints", EditorStyles.miniLabel);
                for (int i = 0; i < count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    var elem = _patrolWaypointsProp.GetArrayElementAtIndex(i);
                    EditorGUILayout.PropertyField(elem, new GUIContent($"[{i}]"));
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button("^", GUILayout.Width(22)))
                            _patrolWaypointsProp.MoveArrayElement(i, i - 1);
                    using (new EditorGUI.DisabledScope(i >= count - 1))
                        if (GUILayout.Button("v", GUILayout.Width(22)))
                            _patrolWaypointsProp.MoveArrayElement(i, i + 1);
                    if (GUILayout.Button("X", GUILayout.Width(22)))
                    { _patrolWaypointsProp.DeleteArrayElementAtIndex(i); break; }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Space(2);
                using (new EditorGUI.DisabledScope(count >= 8))
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("+ Agent Position"))
                    {
                        _patrolWaypointsProp.arraySize++;
                        _patrolWaypointsProp.GetArrayElementAtIndex(_patrolWaypointsProp.arraySize - 1).vector3Value = agent.transform.position;
                    }
                    if (GUILayout.Button("+ World Origin"))
                    {
                        _patrolWaypointsProp.arraySize++;
                        _patrolWaypointsProp.GetArrayElementAtIndex(_patrolWaypointsProp.arraySize - 1).vector3Value = Vector3.zero;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(
                    "Exported as absolute world-space fp12 positions (12 bytes each). Sets AGENT_FLAG_HAS_PATROL. Draggable handles in Scene view.",
                    PSXEditorStyles.RichLabel);
            });
            EditorGUILayout.Space(2);

            _animFoldout = PSXEditorStyles.DrawFoldoutCard("State Animations", _animFoldout, () =>
            {
                EditorGUILayout.LabelField(
                    "Clip played when the agent enters each state. Leave empty to keep the previous clip. " +
                    "Resolved to index in PSXSceneExporter Animations list at export time.",
                    PSXEditorStyles.RichLabel);
                EditorGUILayout.Space(4);
                if (_stateAnimsProp != null)
                {
                    int len = UnityEngine.Mathf.Min(_stateAnimsProp.arraySize, 8);
                    for (int i = 0; i < len; i++)
                    {
                        var elem     = _stateAnimsProp.GetArrayElementAtIndex(i);
                        var clipProp = elem.FindPropertyRelative("clip");
                        if (clipProp != null)
                            EditorGUILayout.PropertyField(clipProp,
                                new GUIContent(s_stateLabels[i], $"Clip for {s_stateLabels[i]} state (stateAnimClip[{i}])"));
                    }
                }
            });
            EditorGUILayout.Space(2);

            _runtimeFoldout = PSXEditorStyles.DrawFoldoutCard("Runtime Checks", _runtimeFoldout, () =>
            {
                if (sceneExp == null)
                    EditorGUILayout.HelpBox("No PSXSceneExporter in this scene. Agent will not export.", MessageType.Error);
                if (navSettings == null && player == null)
                    EditorGUILayout.HelpBox("No PSXNavigationSettings or PSXPlayer found. Agents need nav regions to pathfind.", MessageType.Error);
                else if (navSettings != null && player == null)
                    EditorGUILayout.HelpBox("Nav regions baked from PSXNavigationSettings. Agents run without a player.", MessageType.Info);
                else if (player != null)
                    EditorGUILayout.HelpBox("PSXPlayer present - agents share the scene nav data.", MessageType.Info);
                if (exporter != null && exporter.LuaFile == null)
                    EditorGUILayout.HelpBox("No Lua script assigned. Agent can be driven by scene scripts or cutscenes.", MessageType.None);
                int wpCount = agent.PatrolWaypoints != null ? agent.PatrolWaypoints.Count : 0;
                EditorGUILayout.LabelField("Export Cost", $"{28 + wpCount * 12} bytes (28 struct + {wpCount}x12 waypoints)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Movement Model", "Native nav-region path following", EditorStyles.miniLabel);
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                if (sceneExp != null && GUILayout.Button("Scene Exporter", PSXEditorStyles.SecondaryButton))
                    Selection.activeGameObject = sceneExp.gameObject;
                if (navSettings != null && GUILayout.Button("Nav Settings", PSXEditorStyles.SecondaryButton))
                    Selection.activeGameObject = navSettings.gameObject;
                else if (player != null && GUILayout.Button("PSX Player", PSXEditorStyles.SecondaryButton))
                    Selection.activeGameObject = player.gameObject;
                EditorGUILayout.EndHorizontal();
            });
            EditorGUILayout.Space(2);

            _luaFoldout = PSXEditorStyles.DrawFoldoutCard("Lua Quick Start", _luaFoldout, () =>
            {
                EditorGUILayout.LabelField(
                    "Events: onStateEnter(newState,oldState)  onStateExit(state)  onTargetSeen(target)  " +
                    "onTargetLost(target)  onTargetReached()  onPatrolPoint(idx)  onPathBlocked(). Fire on transitions only.",
                    PSXEditorStyles.RichLabel);
                EditorGUILayout.Space(4);
                EditorGUILayout.SelectableLabel(
                    "-- AGENT_IDLE AGENT_PATROL AGENT_SEEK AGENT_FLEE\n" +
                    "-- AGENT_ATTACK AGENT_WANDER AGENT_INVESTIGATE AGENT_CUSTOM\n\n" +
                    "function self:onTargetSeen(target)\n" +
                    "  Agent.SetState(self, AGENT_SEEK)\n" +
                    "end\n\n" +
                    "function self:onTargetLost(target)\n" +
                    "  Agent.SetState(self, AGENT_PATROL)\n" +
                    "end\n\n" +
                    "function self:onStateEnter(newState, oldState)\n" +
                    "  -- react to state change\n" +
                    "end",
                    EditorStyles.textArea, GUILayout.MinHeight(140));
                if (exporter != null && exporter.LuaFile != null &&
                    GUILayout.Button("Open Lua Script", PSXEditorStyles.SecondaryButton))
                    AssetDatabase.OpenAsset(exporter.LuaFile);
            });

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            var agent  = (PSXAgent)target;
            var wpProp = serializedObject.FindProperty("patrolWaypoints");
            if (wpProp == null || wpProp.arraySize == 0) return;
            serializedObject.Update();
            bool changed = false;
            Handles.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            for (int i = 0; i < wpProp.arraySize; i++)
            {
                var elem    = wpProp.GetArrayElementAtIndex(i);
                Vector3 pos = elem.vector3Value;
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.PositionHandle(pos, Quaternion.identity);
                if (EditorGUI.EndChangeCheck()) { elem.vector3Value = newPos; changed = true; }
                Handles.Label(pos + Vector3.up * 0.55f, $"WP {i}", EditorStyles.miniLabel);
            }
            if (changed) serializedObject.ApplyModifiedProperties();
        }

        private static void DrawHeader(PSXAgent agent, PSXObjectExporter exporter)
        {
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX Agent", PSXEditorStyles.CardHeaderStyle);
            EditorGUILayout.LabelField(
                "Native nav agent with vision, hearing, patrol waypoints and per-state animations. " +
                "Serialized as SPLASHPACKAgentV2 (28 bytes + waypoints).",
                PSXEditorStyles.RichLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("GameObject", agent.gameObject.name, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Exporter", exporter != null ? "Attached" : "Missing", EditorStyles.miniLabel);
            PSXEditorStyles.EndCard();
        }
    }
}
