using SplashEdit.RuntimeCode;
using UnityEditor;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Inspector for <see cref="PSXUISprite"/>: a cell picker rather than an int
    /// field.
    /// </summary>
    /// <remarks>
    /// Typing "17" into a Cell field and rebuilding a disc to find out whether 17
    /// is the chevron or the lamp is the authoring loop this component exists to
    /// end. So the sheet is drawn, the current cell is highlighted, and clicking a
    /// cell picks it.
    /// </remarks>
    [CustomEditor(typeof(PSXUISprite))]
    [CanEditMultipleObjects]
    public class PSXUISpriteEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX UI Sprite", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            PSXEditorStyles.BeginCard();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("elementName"), new GUIContent(
                "Element Name", "Name used from Lua: UI.FindElement(canvas, \"name\"). Max 24 chars."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sheet"), new GUIContent(
                "Sprite Sheet", "The sheet this cell comes from. Packed into VRAM once, however " +
                "many elements draw from it."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("cell"), new GUIContent(
                "Cell", "Cell index, left-to-right then top-to-bottom from 0. " +
                "Change at run time with UI.SetFrame()."));

            SerializedProperty tint = serializedObject.FindProperty("tint");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(tint, new GUIContent(
                    "Tint", "The PS1 computes texel * colour / 128, so 128 grey is NO tint " +
                    "and white is double brightness."));
                if (GUILayout.Button(new GUIContent("128", "Reset to the tint identity (no colour change)."),
                                     EditorStyles.miniButton, GUILayout.Width(34)))
                    tint.colorValue = PSXUISprite.TintIdentity;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("startVisible"));
            PSXEditorStyles.EndCard();

            var spr = (PSXUISprite)target;

            if (!serializedObject.isEditingMultipleObjects && spr.Sheet != null)
            {
                EditorGUILayout.Space(4);
                PSXEditorStyles.BeginCard();
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Pick a cell", PSXEditorStyles.CardHeaderStyle);
                    if (GUILayout.Button(new GUIContent("Snap size",
                            "Resize the RectTransform to the sheet's cell size."),
                            EditorStyles.miniButton, GUILayout.Width(74)))
                    {
                        Undo.RecordObject(spr.GetComponent<RectTransform>(), "Snap to cell size");
                        spr.SnapToCellSize();
                    }
                }
                PSXEditorStyles.DrawSeparator(2, 4);
                DrawCellPicker(spr);
                PSXEditorStyles.EndCard();
            }

            if (spr.Sheet == null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    "No sheet assigned. This element still exports and Lua can still find it, " +
                    "but it draws whatever happens to be at UV (0,0) in the atlas.",
                    MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>The sheet drawn as a clickable grid, current cell highlighted.</summary>
        private void DrawCellPicker(PSXUISprite spr)
        {
            PSXSpriteSheet sheet = spr.Sheet;
            Texture2D tex = sheet.SourceTexture;
            if (tex == null)
            {
                EditorGUILayout.HelpBox("The sheet has no source texture.", MessageType.Warning);
                return;
            }

            int cols = Mathf.Max(1, sheet.Columns);
            int rows = Mathf.Max(1, sheet.Rows);

            float avail = EditorGUIUtility.currentViewWidth - 60f;
            // Whole-number zoom only: a tile grid at 1.37x has seams in it, and
            // the seam looks exactly like a one-pixel gap in the art.
            int zoom = Mathf.Max(1, Mathf.FloorToInt(avail / Mathf.Max(1, tex.width)));
            float w = tex.width * zoom, h = tex.height * zoom;

            Rect area = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(area, new Color(0.1f, 0.1f, 0.12f, 1f));
            GUI.DrawTexture(area, tex, ScaleMode.StretchToFill, true);

            float cw = area.width / cols, ch = area.height / rows;

            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            for (int c = 1; c < cols; c++)
                Handles.DrawLine(new Vector3(area.x + c * cw, area.y), new Vector3(area.x + c * cw, area.yMax));
            for (int r = 1; r < rows; r++)
                Handles.DrawLine(new Vector3(area.x, area.y + r * ch), new Vector3(area.xMax, area.y + r * ch));

            int cur = spr.ResolvedCell;
            var sel = new Rect(area.x + (cur % cols) * cw, area.y + (cur / cols) * ch, cw, ch);
            Handles.color = Color.cyan;
            Handles.DrawAAPolyLine(2f,
                new Vector3(sel.xMin, sel.yMin), new Vector3(sel.xMax, sel.yMin),
                new Vector3(sel.xMax, sel.yMax), new Vector3(sel.xMin, sel.yMax), new Vector3(sel.xMin, sel.yMin));
            Handles.EndGUI();

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(e.mousePosition))
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.x - area.x) / cw), 0, cols - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.y - area.y) / ch), 0, rows - 1);
                serializedObject.FindProperty("cell").intValue = cy * cols + cx;
                e.Use();
            }

            EditorGUILayout.LabelField(
                $"Cell {cur} of {sheet.CellCount}  ({sheet.CellWidth}x{sheet.CellHeight}px, {cols}x{rows} grid)",
                PSXEditorStyles.InfoBox);
        }
    }
}
