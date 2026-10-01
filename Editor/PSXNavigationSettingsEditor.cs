using UnityEditor;
using UnityEngine;
using SplashEdit.RuntimeCode;

namespace SplashEdit.EditorCode
{
    [CustomEditor(typeof(PSXNavigationSettings))]
    public class PSXNavigationSettingsEditor : UnityEditor.Editor
    {
        private bool _agentFoldout = true;
        private bool _voxelFoldout = true;
        private bool _advancedFoldout = true;
        private bool _presetsFoldout = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var settings = (PSXNavigationSettings)target;

            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("Navigation Settings", PSXEditorStyles.CardHeaderStyle);
            EditorGUILayout.LabelField("Scene-level nav bake settings for player and AI agents.", PSXEditorStyles.RichLabel);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            _agentFoldout = PSXEditorStyles.DrawFoldoutCard("Agent", _agentFoldout, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("agentHeight"), new GUIContent("Agent Height"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("agentRadius"), new GUIContent("Agent Radius"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxStepHeight"), new GUIContent("Max Step Height"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("walkableSlopeAngle"), new GUIContent("Walkable Slope"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnAnchor"), new GUIContent("Spawn Anchor"));
            });

            EditorGUILayout.Space(2);

            _voxelFoldout = PSXEditorStyles.DrawFoldoutCard("Voxelization", _voxelFoldout, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navCellSize"), new GUIContent("Cell Size (XZ)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navCellHeight"), new GUIContent("Cell Height (Y)"));
            });

            EditorGUILayout.Space(2);

            _advancedFoldout = PSXEditorStyles.DrawFoldoutCard("Advanced", _advancedFoldout, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navMinRegionArea"), new GUIContent("Min Region Area"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navMergeRegionArea"), new GUIContent("Merge Region Area"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navMaxSimplifyError"), new GUIContent("Max Simplify Error"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navMaxEdgeLength"), new GUIContent("Max Edge Length"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navPartitionMethod"), new GUIContent("Partition Method"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navDetailSampleDist"), new GUIContent("Detail Sample Dist"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navDetailMaxError"), new GUIContent("Detail Max Error"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navMaxPlaneError"), new GUIContent("Max Plane Error"));
            });

            EditorGUILayout.Space(2);

            _presetsFoldout = PSXEditorStyles.DrawFoldoutCard("Quick Presets", _presetsFoldout, () =>
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Indoor", GUILayout.Height(24)))
                {
                    Undo.RecordObject(settings, "Apply Indoor Nav Preset");
                    settings.ApplyIndoorPreset();
                    EditorUtility.SetDirty(settings);
                }
                if (GUILayout.Button("Terrain", GUILayout.Height(24)))
                {
                    Undo.RecordObject(settings, "Apply Terrain Nav Preset");
                    settings.ApplyTerrainPreset();
                    EditorUtility.SetDirty(settings);
                }
                if (GUILayout.Button("Multi-Level", GUILayout.Height(24)))
                {
                    Undo.RecordObject(settings, "Apply Multi-Level Nav Preset");
                    settings.ApplyMultiLevelPreset();
                    EditorUtility.SetDirty(settings);
                }
                EditorGUILayout.EndHorizontal();
            });

            serializedObject.ApplyModifiedProperties();
        }
    }
}
