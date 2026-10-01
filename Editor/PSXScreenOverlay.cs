using System.Collections.Generic;
using SplashEdit.RuntimeCode;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// A live 320x240 view of the scene, docked inside the scene view.
    /// </summary>
    /// <remarks>
    /// Toggle it from the scene view's overlay menu (the three dots, top right)
    /// under "PSX Screen".
    ///
    /// It is the scene view's answer to a question the 3D viewport structurally
    /// cannot answer: a canvas lives in the XY plane and the tilemap in XZ, so
    /// "what will the television show" is not a camera angle. Click an element in
    /// here to select it in the hierarchy - which is how you find the one row out
    /// of nine that is three pixels too low.
    /// </remarks>
    [Overlay(typeof(SceneView), k_Id, "PSX Screen", false)]
    public class PSXScreenOverlay : Overlay
    {
        private const string k_Id = "psxsplash-screen-preview";

        private IMGUIContainer m_Container;

        public override VisualElement CreatePanelContent()
        {
            m_Container = new IMGUIContainer(OnGUI);
            Resize();
            // The preview must follow the scene, not the repaint clock: a colour
            // typed into an inspector should show up here immediately.
            m_Container.onGUIHandler = OnGUI;
            return m_Container;
        }

        private void Resize()
        {
            if (m_Container == null) return;
            Vector2 size = PSXScreenPreview.ScreenSize(PSXScreenPreview.Zoom);
            m_Container.style.width = size.x + 8;
            m_Container.style.height = size.y + 30;
        }

        private void OnGUI()
        {
            int zoom = PSXScreenPreview.Zoom;

            // --- toolbar ---
            using (new EditorGUILayout.HorizontalScope())
            {
                int newZoom = EditorGUILayout.IntPopup(zoom,
                    new[] { "1x", "2x", "3x", "4x" }, new[] { 1, 2, 3, 4 },
                    GUILayout.Width(46));
                if (newZoom != zoom)
                {
                    PSXScreenPreview.Zoom = newZoom;
                    Resize();
                }

                PSXScreenPreview.ShowTilemap = GUILayout.Toggle(
                    PSXScreenPreview.ShowTilemap, new GUIContent("Map", "Draw the scene's tilemap behind the UI."),
                    EditorStyles.miniButton, GUILayout.Width(38));

                PSXScreenPreview.ShowHiddenCanvases = GUILayout.Toggle(
                    PSXScreenPreview.ShowHiddenCanvases,
                    new GUIContent("Hidden", "Also draw canvases whose Start Visible is off, dimmed."),
                    EditorStyles.miniButton, GUILayout.Width(50));

                PSXScreenPreview.ShowOutlines = GUILayout.Toggle(
                    PSXScreenPreview.ShowOutlines,
                    new GUIContent("Bounds", "Outline every element's rect."),
                    EditorStyles.miniButton, GUILayout.Width(50));
            }

            Vector2 size = PSXScreenPreview.ScreenSize(zoom);
            Rect area = GUILayoutUtility.GetRect(size.x, size.y, GUILayout.ExpandWidth(false));

            List<PSXScreenPreview.Hit> hits = PSXScreenPreview.Draw(area, zoom);

            HandleInput(area, size, hits);
        }

        private void HandleInput(Rect area, Vector2 size, List<PSXScreenPreview.Hit> hits)
        {
            Event e = Event.current;
            var screen = new Rect(area.x, area.y, size.x, size.y);
            if (!screen.Contains(e.mousePosition)) return;

            // Middle-drag (or alt-drag) scrolls the tilemap window, which is the
            // engine's Sprite.SetViewOffset - the same thing the camera does at
            // run time. Without it the preview only ever shows the top-left
            // corner of a map, which is rarely where the UI has to work.
            if (e.type == EventType.MouseDrag && (e.button == 2 || e.alt))
            {
                float scale = Mathf.Max(1, PSXScreenPreview.Zoom);
                Vector2Int v = PSXScreenPreview.TileView;
                PSXScreenPreview.TileView = new Vector2Int(
                    Mathf.Max(0, v.x - Mathf.RoundToInt(e.delta.x / scale)),
                    Mathf.Max(0, v.y - Mathf.RoundToInt(e.delta.y / scale)));
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // Backwards: the list is back-to-front, so the last match is the
                // one actually on top - which is the one the click meant.
                for (int i = hits.Count - 1; i >= 0; i--)
                {
                    if (!hits[i].ScreenRect.Contains(e.mousePosition)) continue;
                    Selection.activeGameObject = hits[i].Target;
                    EditorGUIUtility.PingObject(hits[i].Target);
                    e.Use();
                    return;
                }
            }
        }
    }
}
