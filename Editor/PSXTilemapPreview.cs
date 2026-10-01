using System.Collections.Generic;
using SplashEdit.RuntimeCode;
using UnityEditor;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Draws a painted <see cref="PSXTilemap"/> the way the console draws it -
    /// in the scene view, in the world, and in the 2D screen preview.
    /// </summary>
    /// <remarks>
    /// Before this existed a tilemap was invisible everywhere except the Tile
    /// Painter window, so laying anything out against the map - a spawn point, a
    /// task station, a HUD element that must not cover the corridor the player
    /// walks down - meant painting, exporting, building a disc and looking at a
    /// television. The whole point of authoring in Unity is that the answer is on
    /// screen while you are still authoring.
    ///
    /// World mapping matches the engine exactly: tile (0,0) is world (0,0), one
    /// world unit is one pixel (Tile.MoveActor works in <c>pos.x.integer()</c>
    /// pixels), the grid lies in the XZ plane and +Z is DOWN the screen.
    /// </remarks>
    [InitializeOnLoad]
    public static class PSXTilemapPreview
    {
        private const string k_ShowWorldKey = "PSXSplash.Tilemap.ShowInScene";

        /// <summary>Draw the tilemap in the 3D scene view (menu-toggleable).</summary>
        public static bool ShowInSceneView
        {
            get => EditorPrefs.GetBool(k_ShowWorldKey, true);
            set { EditorPrefs.SetBool(k_ShowWorldKey, value); SceneView.RepaintAll(); }
        }

        static PSXTilemapPreview()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        // --- mesh cache -----------------------------------------------------
        //
        // A 64x64 map is 4096 quads. Rebuilding that every repaint would make the
        // scene view crawl, so the mesh is cached and rebuilt only when the map's
        // contents actually change. The change check walks the cells, which is
        // cheap next to allocating 16k vertices.

        private class Entry
        {
            public Mesh Mesh;
            public int Hash;
            public Texture2D Texture;

#if UNITY_6000_4_OR_NEWER
        public EntityId TilesetEntityId;
#endif

        }

#if UNITY_6000_4_OR_NEWER
        private static readonly Dictionary<EntityId, Entry> s_cache = new Dictionary<EntityId, Entry>();
#else
        private static readonly Dictionary<int, Entry> s_cache = new Dictionary<int, Entry>();
#endif
        private static Material s_material;

        /// <summary>Throw away every cached mesh (the painter calls this).</summary>
        public static void Invalidate()
        {
            foreach (var e in s_cache.Values)
                if (e.Mesh != null) Object.DestroyImmediate(e.Mesh);
            s_cache.Clear();
            SceneView.RepaintAll();
        }

        private static Material PreviewMaterial()
        {
            if (s_material != null) return s_material;

            // Unlit/Transparent is the closest built-in match for how the console
            // draws a tile: no lighting, alpha cut straight through, no fog. The
            // fallbacks matter because which built-in shaders are present depends
            // on the render pipeline the project happens to use.
            Shader shader = Shader.Find("Unlit/Transparent")
                            ?? Shader.Find("Sprites/Default")
                            ?? Shader.Find("Unlit/Texture");
            if (shader == null) return null;

            s_material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return s_material;
        }
#if UNITY_6000_4_OR_NEWER
        private static int ContentHash(PSXTilemap map)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + map.Width;
                h = h * 31 + map.Height;
                h = h * 31 + (map.Tileset != null ? map.Tileset.CellWidth * 397 + map.Tileset.CellHeight : 0);
                foreach (var b in map.Brushes) h = h * 31 + b.tilesetCell * 7 + (b.walkable ? 1 : 0);
                for (int y = 0; y < map.Height; y++)
                    for (int x = 0; x < map.Width; x++)
                        h = h * 31 + map.GetCell(x, y);
                return h;
            }
        }

        /// <summary>The cached world-space mesh for a map, rebuilt when it changes.</summary>
        public static Mesh MeshFor(PSXTilemap map)
        {
            if (map == null || map.Tileset == null || map.Tileset.SourceTexture == null) return null;

            EntityId key = map.GetEntityId();
            EntityId tilesetEntityId = map.Tileset.GetEntityId();
            int hash = ContentHash(map);
            if (s_cache.TryGetValue(key, out Entry e) && e.Mesh != null && e.Hash == hash && e.TilesetEntityId == tilesetEntityId)
                return e.Mesh;

            if (e != null && e.Mesh != null) Object.DestroyImmediate(e.Mesh);
            e = new Entry { Hash = hash, Texture = map.Tileset.SourceTexture, Mesh = BuildMesh(map), TilesetEntityId = tilesetEntityId };
            s_cache[key] = e;
            return e.Mesh;
        }
#else
        private static int ContentHash(PSXTilemap map)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + map.Width;
                h = h * 31 + map.Height;
                h = h * 31 + (map.Tileset != null ? map.Tileset.GetInstanceID() : 0);
                h = h * 31 + (map.Tileset != null ? map.Tileset.CellWidth * 397 + map.Tileset.CellHeight : 0);
                foreach (var b in map.Brushes) h = h * 31 + b.tilesetCell * 7 + (b.walkable ? 1 : 0);
                for (int y = 0; y < map.Height; y++)
                    for (int x = 0; x < map.Width; x++)
                        h = h * 31 + map.GetCell(x, y);
                return h;
            }
        }

        /// <summary>The cached world-space mesh for a map, rebuilt when it changes.</summary>
        public static Mesh MeshFor(PSXTilemap map)
        {
            if (map == null || map.Tileset == null || map.Tileset.SourceTexture == null) return null;

            int key = map.GetInstanceID();
            int hash = ContentHash(map);
            if (s_cache.TryGetValue(key, out Entry e) && e.Mesh != null && e.Hash == hash)
                return e.Mesh;

            if (e != null && e.Mesh != null) Object.DestroyImmediate(e.Mesh);
            e = new Entry { Hash = hash, Texture = map.Tileset.SourceTexture, Mesh = BuildMesh(map) };
            s_cache[key] = e;
            return e.Mesh;
        }
#endif
        private static Mesh BuildMesh(PSXTilemap map)
        {
            PSXSpriteSheet set = map.Tileset;
            Texture2D tex = set.SourceTexture;
            int tw = map.TileWidth, th = map.TileHeight;
            int cols = Mathf.Max(1, set.Columns);

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float uStep = (float)set.CellWidth / tex.width;
            float vStep = (float)set.CellHeight / tex.height;

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    PSXTileBrush brush = map.BrushAt(x, y);
                    if (brush == null || brush.tilesetCell < 0) continue;

                    int cell = brush.tilesetCell;
                    float u0 = (cell % cols) * uStep;
                    // Cells count from the TOP-left; Unity's V axis runs from the
                    // BOTTOM. Flip once here rather than in three draw sites.
                    float v1 = 1f - (cell / cols) * vStep;
                    float v0 = v1 - vStep;

                    float x0 = x * tw, x1 = x0 + tw;
                    float z0 = y * th, z1 = z0 + th;

                    int b = verts.Count;
                    verts.Add(new Vector3(x0, 0, z0));
                    verts.Add(new Vector3(x1, 0, z0));
                    verts.Add(new Vector3(x1, 0, z1));
                    verts.Add(new Vector3(x0, 0, z1));

                    uvs.Add(new Vector2(u0, v1));
                    uvs.Add(new Vector2(u0 + uStep, v1));
                    uvs.Add(new Vector2(u0 + uStep, v0));
                    uvs.Add(new Vector2(u0, v0));

                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                }
            }

            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "PSXTilemapPreview" };
            // A 256x256 map is 65536 quads: past the 16-bit index limit, so say so
            // rather than silently drawing the first 16384 tiles.
            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // --- 3D scene view ---------------------------------------------------

        private static void OnSceneGui(SceneView view)
        {
            if (!ShowInSceneView) return;
            if (Event.current.type != EventType.Repaint) return;

            var renderers = Object.FindObjectsByType<PSXTilemapRenderer>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var r in renderers)
                DrawWorld(r);
        }

        private static void DrawWorld(PSXTilemapRenderer renderer)
        {
            if (renderer == null || renderer.tilemap == null) return;
            PSXTilemap map = renderer.tilemap;

            Mesh mesh = MeshFor(map);
            Material mat = PreviewMaterial();
            if (mesh != null && mat != null)
            {
                mat.mainTexture = map.Tileset.SourceTexture;
                mat.SetPass(0);
                // Identity, not the renderer's transform: the engine ignores the
                // GameObject's position entirely (tile 0,0 IS world 0,0), so a
                // preview that followed the transform would lie about where the
                // floor is the moment somebody nudged the object.
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
            }

            bool selected = Selection.activeGameObject == renderer.gameObject;
            if (selected) DrawGridOverlay(map);
        }

        /// <summary>
        /// Grid, map border and the solid cells, drawn only for the selected
        /// tilemap so it does not fight the art the rest of the time.
        /// </summary>
        private static void DrawGridOverlay(PSXTilemap map)
        {
            int tw = map.TileWidth, th = map.TileHeight;
            float w = map.Width * tw, h = map.Height * th;

            Handles.color = new Color(1f, 1f, 1f, 0.25f);
            Handles.DrawAAPolyLine(2f,
                new Vector3(0, 0, 0), new Vector3(w, 0, 0),
                new Vector3(w, 0, h), new Vector3(0, 0, h), new Vector3(0, 0, 0));

            // Solid cells get a red wash. "Where can I walk" is the question a
            // level layout is actually answering, and it is invisible in the art.
            Handles.color = new Color(1f, 0.25f, 0.25f, 0.18f);
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    PSXTileBrush b = map.BrushAt(x, y);
                    if (b != null && b.walkable) continue;
                    if (b == null && map.GetCell(x, y) < 0) continue; // unpainted: leave it clear
                    Handles.DrawAAConvexPolygon(
                        new Vector3(x * tw, 0, y * th),
                        new Vector3(x * tw + tw, 0, y * th),
                        new Vector3(x * tw + tw, 0, y * th + th),
                        new Vector3(x * tw, 0, y * th + th));
                }
            }

            // Object tiles: the addressable ones a game looks up by kind and id.
            Handles.color = new Color(0.3f, 1f, 0.9f, 0.9f);
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    PSXTileBrush b = map.BrushAt(x, y);
                    if (b == null || b.objectKind == 0) continue;
                    var c = new Vector3(x * tw + tw * 0.5f, 0, y * th + th * 0.5f);
                    Handles.DrawWireDisc(c, Vector3.up, Mathf.Min(tw, th) * 0.35f);
                }
            }
        }

        // --- 2D screen preview ----------------------------------------------

        /// <summary>
        /// Draw the tiles visible through a 320x240 window at <paramref name="viewX"/>,
        /// <paramref name="viewY"/> world pixels, into a GUI rect. This is the
        /// same culling window <c>TileSystem::renderOT</c> computes.
        /// </summary>
        public static void DrawScreen(PSXTilemap map, Rect screen, float scale,
                                      int viewX, int viewY, Vector2 resolution)
        {
            if (map == null || map.Tileset == null || map.Tileset.SourceTexture == null) return;
            Texture2D tex = map.Tileset.SourceTexture;
            PSXSpriteSheet set = map.Tileset;

            int tw = map.TileWidth, th = map.TileHeight;
            if (tw <= 0 || th <= 0) return;
            int cols = Mathf.Max(1, set.Columns);

            int x0 = Mathf.FloorToInt(viewX / (float)tw);
            int y0 = Mathf.FloorToInt(viewY / (float)th);
            int x1 = Mathf.FloorToInt((viewX + resolution.x - 1) / (float)tw);
            int y1 = Mathf.FloorToInt((viewY + resolution.y - 1) / (float)th);
            x0 = Mathf.Max(0, x0); y0 = Mathf.Max(0, y0);
            x1 = Mathf.Min(map.Width - 1, x1); y1 = Mathf.Min(map.Height - 1, y1);

            float uStep = (float)set.CellWidth / tex.width;
            float vStep = (float)set.CellHeight / tex.height;

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    PSXTileBrush b = map.BrushAt(x, y);
                    if (b == null || b.tilesetCell < 0) continue;

                    int cell = b.tilesetCell;
                    float u0 = (cell % cols) * uStep;
                    float v0 = 1f - (cell / cols + 1) * vStep;

                    var r = new Rect(screen.x + (x * tw - viewX) * scale,
                                     screen.y + (y * th - viewY) * scale,
                                     tw * scale, th * scale);
                    GUI.DrawTextureWithTexCoords(r, tex, new Rect(u0, v0, uStep, vStep));
                }
            }
        }
    }
}
