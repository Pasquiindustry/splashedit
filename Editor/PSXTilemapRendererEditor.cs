using SplashEdit.RuntimeCode;
using UnityEditor;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    [CustomEditor(typeof(PSXTilemapRenderer))]
    public class PSXTilemapRendererEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var renderer = (PSXTilemapRenderer)target;

            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(renderer.tilemap == null))
            {
                if (GUILayout.Button("Open in Tile Painter", GUILayout.Height(28)))
                    TilePainterWindow.Open(renderer.tilemap);
            }

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool show = GUILayout.Toggle(PSXTilemapPreview.ShowInSceneView,
                    new GUIContent("Draw in Scene View",
                        "Render the painted tiles on the XZ plane, where the engine puts them. " +
                        "Select this object to also see the grid, the solid cells and the object tiles."),
                    EditorStyles.miniButtonLeft, GUILayout.Height(20));
                if (show != PSXTilemapPreview.ShowInSceneView)
                    PSXTilemapPreview.ShowInSceneView = show;

                if (GUILayout.Button(new GUIContent("Refresh",
                        "Rebuild the cached preview mesh. Only needed if the map was changed " +
                        "from outside the Tile Painter."),
                        EditorStyles.miniButtonRight, GUILayout.Height(20), GUILayout.Width(70)))
                    PSXTilemapPreview.Invalidate();
            }

            if (renderer.tilemap == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a Tilemap asset (create one with PlayStation 1 > Tile Painter > New Tilemap), " +
                    "or this scene exports with no floor.", MessageType.Info);
                return;
            }

            var problems = renderer.tilemap.Validate();
            if (problems.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", problems), MessageType.Warning);
                return;
            }

            PSXTilemap map = renderer.tilemap;
            EditorGUILayout.HelpBox(
                $"{map.Width}x{map.Height} tiles, {map.TileWidth}x{map.TileHeight}px each.\n" +
                // The world extent, spelled out, because everything else in the
                // scene is placed against it: one world unit is one pixel, tile
                // (0,0) is the origin, and the map runs along +X and +Z.
                $"World extent: 0,0 to {map.Width * map.TileWidth},{map.Height * map.TileHeight} " +
                "(one unit = one pixel, on the XZ plane).\n" +
                $"Cells: {map.Width * map.Height * 2} bytes in the splashpack.",
                MessageType.None);

            EditorGUILayout.HelpBox(
                "The GameObject's transform is ignored on export - tile (0,0) is always world (0,0). " +
                "The scene-view preview draws it there too, so what you see is where the player walks.",
                MessageType.Info);
        }
    }
}
