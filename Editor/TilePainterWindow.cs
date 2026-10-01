using System.Collections.Generic;
using System.IO;
using SplashEdit.RuntimeCode;
using UnityEditor;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// A grid tile painter for <see cref="PSXTilemap"/>. The design goal above all
    /// others is ease of use: pick a brush on the left, paint on the right, and
    /// the walls, floors and gameplay objects fall out of what you painted. There
    /// is no per-cell property editing and no separate "collision layer" - a tile's
    /// walkability and role come from its brush, so you decide once and paint many.
    /// </summary>
    public class TilePainterWindow : EditorWindow
    {
        private enum Tool { Paint, Erase, Fill, Pick }

        private PSXTilemap _map;
        private int _brush = 0;
        private Tool _tool = Tool.Paint;

        private float _zoom = 24f;          // editor pixels per tile
        private Vector2 _mapScroll;
        private Vector2 _paletteScroll;
        private int _hoverX = -1, _hoverY = -1;

        private bool _showGrid = true;
        private bool _showWalkable = true;
        private bool _showObjects = true;

        // A distinct overlay tint per object-kind value, derived from the value so
        // the painter needs no table of game-specific kinds.
        private static Color KindColor(int kind)
        {
            // Golden-angle hue spread: adjacent kinds get clearly different colours.
            float h = (kind * 0.61803398875f) % 1f;
            return Color.HSVToRGB(h, 0.7f, 1f);
        }

        [MenuItem("PlayStation 1/Tile Painter")]
        public static void ShowWindow()
        {
            var win = GetWindow<TilePainterWindow>("Tile Painter");
            win.minSize = new Vector2(760, 520);
            win.wantsMouseMove = true;
        }

        /// <summary>Open the painter already pointed at a specific map.</summary>
        public static void Open(PSXTilemap map)
        {
            ShowWindow();
            var win = GetWindow<TilePainterWindow>();
            win._map = map;
            win._brush = 0;
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_map == null)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.HelpBox(
                    "Assign a Tilemap above, or click New Tilemap.\n\n" +
                    "A Tilemap needs a tileset (a PSXSplash Sprite Sheet). The sheet's " +
                    "Cell Width/Height becomes the tile size. Paint brushes onto the grid; " +
                    "walls, floors and objects come from each brush's settings.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawPalette();     // left column
            DrawCanvas();      // main grid
            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------
        // Toolbar
        // ------------------------------------------------------------------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            _map = (PSXTilemap)EditorGUILayout.ObjectField(_map, typeof(PSXTilemap), false, GUILayout.Width(200));
            if (EditorGUI.EndChangeCheck()) _brush = 0;

            if (GUILayout.Button("New Tilemap", EditorStyles.toolbarButton, GUILayout.Width(90)))
                CreateNewTilemap();

            GUILayout.Space(12);

            if (_map != null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.LabelField($"tile {_map.TileWidth}x{_map.TileHeight}px", GUILayout.Width(90));

                GUILayout.Label("Tool:", GUILayout.Width(34));
                _tool = (Tool)GUILayout.Toolbar((int)_tool, new[] { "Paint", "Erase", "Fill", "Pick" },
                                                EditorStyles.toolbarButton, GUILayout.Width(220));

                GUILayout.Space(8);
                _showGrid = GUILayout.Toggle(_showGrid, "Grid", EditorStyles.toolbarButton, GUILayout.Width(44));
                _showWalkable = GUILayout.Toggle(_showWalkable, "Walls", EditorStyles.toolbarButton, GUILayout.Width(50));
                _showObjects = GUILayout.Toggle(_showObjects, "Objects", EditorStyles.toolbarButton, GUILayout.Width(60));
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Zoom", GUILayout.Width(38));
            _zoom = GUILayout.HorizontalSlider(_zoom, 8f, 48f, GUILayout.Width(120));

            EditorGUILayout.EndHorizontal();

            if (_map != null)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                int w = _map.Width, h = _map.Height;
                GUILayout.Label("Size", GUILayout.Width(34));
                int nw = EditorGUILayout.IntField(w, GUILayout.Width(50));
                GUILayout.Label("x", GUILayout.Width(12));
                int nh = EditorGUILayout.IntField(h, GUILayout.Width(50));
                if (GUILayout.Button("Resize", EditorStyles.toolbarButton, GUILayout.Width(60)) && (nw != w || nh != h))
                {
                    Undo.RecordObject(_map, "Resize Tilemap");
                    _map.Resize(nw, nh);
                    EditorUtility.SetDirty(_map);
                }

                GUILayout.Space(12);
                if (GUILayout.Button("Fill All", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    FillAll(_brush);
                if (GUILayout.Button("Clear All", EditorStyles.toolbarButton, GUILayout.Width(64)))
                    FillAll(-1);

                GUILayout.FlexibleSpace();
                if (_hoverX >= 0)
                {
                    var b = _map.BrushAt(_hoverX, _hoverY);
                    GUILayout.Label($"({_hoverX},{_hoverY}) {(b != null ? b.name : "empty")}", GUILayout.Width(200));
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // ------------------------------------------------------------------
        // Palette (left)
        // ------------------------------------------------------------------

        private void DrawPalette()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(240));
            EditorGUILayout.LabelField("Brushes", EditorStyles.boldLabel);

            var problems = _map.Validate();
            if (problems.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", problems), MessageType.Warning);

            _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll, GUILayout.Height(200));
            for (int i = 0; i < _map.Brushes.Count; i++)
            {
                var brush = _map.Brushes[i];
                var row = EditorGUILayout.BeginHorizontal();
                bool selected = (i == _brush);
                if (selected) EditorGUI.DrawRect(row, new Color(0.24f, 0.44f, 0.9f, 0.35f));

                Rect thumb = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
                DrawTileCell(thumb, brush.tilesetCell, Color.white);

                if (GUILayout.Button($"{brush.name}", EditorStyles.label))
                    _brush = i;

                string badge = brush.walkable ? "" : "WALL ";
                if (brush.objectKind != 0) badge += _map.KindLabel(brush.objectKind).ToUpper();
                GUILayout.Label(badge, EditorStyles.miniLabel, GUILayout.Width(80));

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Brush"))
            {
                Undo.RecordObject(_map, "Add Brush");
                _map.Brushes.Add(new PSXTileBrush { name = "brush " + _map.Brushes.Count });
                _brush = _map.Brushes.Count - 1;
                EditorUtility.SetDirty(_map);
            }
            using (new EditorGUI.DisabledScope(_map.Brushes.Count == 0))
                if (GUILayout.Button("Remove"))
                {
                    Undo.RecordObject(_map, "Remove Brush");
                    _map.Brushes.RemoveAt(_brush);
                    _brush = Mathf.Clamp(_brush, 0, _map.Brushes.Count - 1);
                    EditorUtility.SetDirty(_map);
                }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            DrawBrushEditor();

            EditorGUILayout.EndVertical();
        }

        private void DrawBrushEditor()
        {
            if (_brush < 0 || _brush >= _map.Brushes.Count) return;
            var b = _map.Brushes[_brush];

            EditorGUILayout.LabelField("Selected Brush", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            string name = EditorGUILayout.TextField("Name", b.name);
            bool walkable = EditorGUILayout.Toggle("Walkable", b.walkable);

            // Kind is an author-named byte: a popup of the tilemap's Object Kind
            // Names when they exist, a plain number field otherwise. The engine
            // never sees the names - they only make the dropdown readable.
            int objectKind = b.objectKind;
            var kindNames = _map.ObjectKindNames;
            if (kindNames != null && kindNames.Count > 1)
            {
                int clamped = Mathf.Clamp(objectKind, 0, kindNames.Count - 1);
                objectKind = EditorGUILayout.Popup("Kind", clamped, kindNames.ToArray());
            }
            else
            {
                objectKind = EditorGUILayout.IntField(
                    new GUIContent("Kind", "0 = plain tile. Any other value tags this as an object. " +
                                   "Add friendly names in the Tilemap asset's Object Kind Names list."),
                    objectKind);
            }

            int objectId = b.objectId;
            if (objectKind != 0)
            {
                objectId = EditorGUILayout.IntField(
                    new GUIContent("Object Id", "-1 auto-numbers this kind in reading order. " +
                                   "Set it when the id matters (your game decides what it means)."),
                    b.objectId);
            }

            Color col = EditorGUILayout.ColorField("Editor Tint", b.editorColor);

            EditorGUILayout.LabelField("Graphic (click a tile):");
            int tilesetCell = DrawTilesetPicker(b.tilesetCell);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_map, "Edit Brush");
                b.name = name;
                b.walkable = walkable;
                b.objectKind = objectKind;
                b.objectId = objectId;
                b.editorColor = col;
                b.tilesetCell = tilesetCell;
                EditorUtility.SetDirty(_map);
            }
        }

        /// <summary>A clickable grid of the tileset; returns the chosen cell index.</summary>
        private int DrawTilesetPicker(int current)
        {
            var sheet = _map.Tileset;
            if (sheet == null || sheet.SourceTexture == null)
            {
                EditorGUILayout.HelpBox("Assign a tileset on the Tilemap asset to pick graphics.", MessageType.None);
                return current;
            }

            int cols = sheet.Columns, rows = sheet.Rows;
            const float cellPx = 22f;
            Rect area = GUILayoutUtility.GetRect(cols * cellPx, rows * cellPx);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    Rect cell = new Rect(area.x + c * cellPx, area.y + r * cellPx, cellPx - 1, cellPx - 1);
                    DrawTileCell(cell, idx, Color.white);
                    if (idx == current)
                        DrawRectOutline(cell, new Color(0.24f, 0.6f, 1f), 2f);
                    if (Event.current.type == EventType.MouseDown && cell.Contains(Event.current.mousePosition))
                    {
                        current = idx;
                        // The caller wraps this in BeginChangeCheck/EndChangeCheck,
                        // which only fires for controls that raise GUI.changed. A
                        // hand-rolled click does not, so raise it ourselves or the
                        // new cell is computed and then thrown away.
                        GUI.changed = true;
                        Event.current.Use();
                        Repaint();
                    }
                }
            }
            return current;
        }

        // ------------------------------------------------------------------
        // Canvas (main grid)
        // ------------------------------------------------------------------

        private void DrawCanvas()
        {
            EditorGUILayout.BeginVertical();
            _mapScroll = EditorGUILayout.BeginScrollView(_mapScroll);

            int w = _map.Width, h = _map.Height;
            Rect grid = GUILayoutUtility.GetRect(w * _zoom, h * _zoom, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            // Background so empty cells read as "nothing", not as the window.
            EditorGUI.DrawRect(grid, new Color(0.12f, 0.12f, 0.14f));

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Rect cell = new Rect(grid.x + x * _zoom, grid.y + y * _zoom, _zoom, _zoom);
                    var brush = _map.BrushAt(x, y);
                    if (brush != null && brush.tilesetCell >= 0)
                        DrawTileCell(cell, brush.tilesetCell, brush.editorColor);

                    if (_showWalkable && brush != null && !brush.walkable)
                        EditorGUI.DrawRect(cell, new Color(1f, 0f, 0f, 0.28f));

                    if (_showObjects && brush != null && brush.objectKind != 0)
                    {
                        Rect dot = new Rect(cell.x + cell.width * 0.25f, cell.y + cell.height * 0.25f,
                                            cell.width * 0.5f, cell.height * 0.5f);
                        EditorGUI.DrawRect(dot, KindColor(brush.objectKind));
                    }
                }
            }

            if (_showGrid && _zoom >= 10f) DrawGridLines(grid, w, h);

            HandleMouse(grid, w, h);

            if (_hoverX >= 0 && _hoverX < w && _hoverY >= 0 && _hoverY < h)
            {
                Rect hov = new Rect(grid.x + _hoverX * _zoom, grid.y + _hoverY * _zoom, _zoom, _zoom);
                DrawRectOutline(hov, Color.white, 1.5f);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void HandleMouse(Rect grid, int w, int h)
        {
            Event e = Event.current;
            if (!grid.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseMove && (_hoverX != -1 || _hoverY != -1))
                {
                    _hoverX = _hoverY = -1;
                    Repaint();
                }
                return;
            }

            int x = Mathf.FloorToInt((e.mousePosition.x - grid.x) / _zoom);
            int y = Mathf.FloorToInt((e.mousePosition.y - grid.y) / _zoom);

            if (e.type == EventType.MouseMove)
            {
                _hoverX = x; _hoverY = y;
                Repaint();
            }

            bool paintButton = e.button == 0;
            bool eraseButton = e.button == 1;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && (paintButton || eraseButton))
            {
                ApplyTool(x, y, eraseButton);
                e.Use();
            }
        }

        private void ApplyTool(int x, int y, bool forceErase)
        {
            if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return;

            if (forceErase || _tool == Tool.Erase)
            {
                Paint(x, y, -1);
            }
            else if (_tool == Tool.Pick)
            {
                int b = _map.GetCell(x, y);
                if (b >= 0) { _brush = b; Repaint(); }
            }
            else if (_tool == Tool.Fill)
            {
                FloodFill(x, y, _brush);
            }
            else // Paint
            {
                Paint(x, y, _brush);
            }
        }

        private void Paint(int x, int y, int brushIndex)
        {
            if (_map.GetCell(x, y) == brushIndex) return;
            Undo.RecordObject(_map, "Paint Tile");
            _map.SetCell(x, y, brushIndex);
            Commit();
        }

        private void FloodFill(int x, int y, int brushIndex)
        {
            int target = _map.GetCell(x, y);
            if (target == brushIndex) return;

            Undo.RecordObject(_map, "Fill Tiles");
            var stack = new Stack<Vector2Int>();
            stack.Push(new Vector2Int(x, y));
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p.x < 0 || p.y < 0 || p.x >= _map.Width || p.y >= _map.Height) continue;
                if (_map.GetCell(p.x, p.y) != target) continue;
                _map.SetCell(p.x, p.y, brushIndex);
                stack.Push(new Vector2Int(p.x + 1, p.y));
                stack.Push(new Vector2Int(p.x - 1, p.y));
                stack.Push(new Vector2Int(p.x, p.y + 1));
                stack.Push(new Vector2Int(p.x, p.y - 1));
            }
            Commit();
        }

        private void FillAll(int brushIndex)
        {
            Undo.RecordObject(_map, "Fill All");
            for (int y = 0; y < _map.Height; y++)
                for (int x = 0; x < _map.Width; x++)
                    _map.SetCell(x, y, brushIndex);
            Commit();
        }

        /// <summary>
        /// Mark the map changed and refresh everything looking at it.
        /// </summary>
        /// <remarks>
        /// The scene view is included on purpose: PSXTilemapPreview draws the map
        /// where the player will walk, and a preview that only updates when you
        /// happen to nudge the camera is worse than none - you stop trusting it.
        /// </remarks>
        private void Commit()
        {
            EditorUtility.SetDirty(_map);
            Repaint();
            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------
        // Drawing helpers
        // ------------------------------------------------------------------

        /// <summary>Blit tileset cell <paramref name="cell"/> into <paramref name="rect"/>.</summary>
        private void DrawTileCell(Rect rect, int cell, Color tint)
        {
            var sheet = _map != null ? _map.Tileset : null;
            if (sheet == null || sheet.SourceTexture == null || cell < 0)
            {
                EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
                return;
            }
            int cols = sheet.Columns, rows = sheet.Rows;
            if (cols <= 0 || rows <= 0) return;
            int cx = cell % cols;
            int cy = cell / cols;
            // Unity UV origin is bottom-left; the sheet is addressed top-to-bottom,
            // so flip the row.
            float uw = 1f / cols, uh = 1f / rows;
            var uv = new Rect(cx * uw, 1f - (cy + 1) * uh, uw, uh);
            Color prev = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, sheet.SourceTexture, uv, true);
            GUI.color = prev;
        }

        private static void DrawGridLines(Rect grid, int w, int h)
        {
            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.12f);
            for (int x = 0; x <= w; x++)
            {
                float px = grid.x + x * (grid.width / w);
                Handles.DrawLine(new Vector3(px, grid.y), new Vector3(px, grid.y + grid.height));
            }
            for (int y = 0; y <= h; y++)
            {
                float py = grid.y + y * (grid.height / h);
                Handles.DrawLine(new Vector3(grid.x, py), new Vector3(grid.x + grid.width, py));
            }
            Handles.EndGUI();
        }

        private static void DrawRectOutline(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        // ------------------------------------------------------------------
        // New asset
        // ------------------------------------------------------------------

        private void CreateNewTilemap()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "New Tilemap", "Tilemap", "asset", "Where should the tilemap asset live?");
            if (string.IsNullOrEmpty(path)) return;

            var map = ScriptableObject.CreateInstance<PSXTilemap>();
            // Seed a sensible default palette so the window is usable immediately.
            map.Brushes.Add(new PSXTileBrush { name = "floor", tilesetCell = 0, walkable = true, editorColor = Color.white });
            map.Brushes.Add(new PSXTileBrush { name = "wall", tilesetCell = 1, walkable = false, editorColor = new Color(0.7f, 0.7f, 0.7f) });
            map.EnsureCells();

            AssetDatabase.CreateAsset(map, path);
            AssetDatabase.SaveAssets();
            _map = map;
            _brush = 0;
            Selection.activeObject = map;
        }
    }
}
