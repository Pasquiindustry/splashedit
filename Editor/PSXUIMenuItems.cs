using SplashEdit.RuntimeCode;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// GameObject-menu builders for PSX UI, plus the tilemap preview toggle.
    /// </summary>
    /// <remarks>
    /// Every one of these sets the pivot to the top-left corner and snaps the
    /// rect to whole pixels, because that is what makes an authored position mean
    /// what it says: PSXUILayout bakes <c>x = anchoredPosition.x - pivot.x*w</c>,
    /// so any other pivot turns "put it at 40,44" into arithmetic the author has
    /// to do in their head.
    /// </remarks>
    public static class PSXUIMenuItems
    {
        private const string k_GO = "GameObject/PlayStation 1/UI/";
        private const string k_Menu = "PlayStation 1/";

        // ----- preview toggle -----

        [MenuItem(k_Menu + "Show Tilemap in Scene View", false, 40)]
        private static void ToggleTilemapPreview() =>
            PSXTilemapPreview.ShowInSceneView = !PSXTilemapPreview.ShowInSceneView;

        [MenuItem(k_Menu + "Show Tilemap in Scene View", true)]
        private static bool ToggleTilemapPreviewValidate()
        {
            Menu.SetChecked(k_Menu + "Show Tilemap in Scene View", PSXTilemapPreview.ShowInSceneView);
            return true;
        }

        // ----- builders -----

        [MenuItem(k_GO + "Canvas", false, 10)]
        public static void CreateCanvas(MenuCommand cmd)
        {
            GameObject go = NewCanvas("PSX Canvas", cmd.context as GameObject);
            Selection.activeGameObject = go;
        }

        [MenuItem(k_GO + "Text", false, 20)]
        public static void CreateText(MenuCommand cmd) =>
            Selection.activeGameObject = NewElement<PSXUIText>("Text", cmd, new Vector2(120, 19));

        [MenuItem(k_GO + "Sprite (sheet cell)", false, 21)]
        public static void CreateSprite(MenuCommand cmd) =>
            Selection.activeGameObject = NewElement<PSXUISprite>("Sprite", cmd, new Vector2(16, 16));

        [MenuItem(k_GO + "Box", false, 22)]
        public static void CreateBox(MenuCommand cmd) =>
            Selection.activeGameObject = NewElement<PSXUIBox>("Box", cmd, new Vector2(64, 24));

        [MenuItem(k_GO + "Image", false, 23)]
        public static void CreateImage(MenuCommand cmd) =>
            Selection.activeGameObject = NewElement<PSXUIImage>("Image", cmd, new Vector2(64, 64));

        [MenuItem(k_GO + "Progress Bar", false, 24)]
        public static void CreateProgressBar(MenuCommand cmd) =>
            Selection.activeGameObject = NewElement<PSXUIProgressBar>("ProgressBar", cmd, new Vector2(120, 12));

        /// <summary>
        /// A nine-slice panel: eight edge/corner sprites round a middle, all from
        /// one sheet, built in one click.
        /// </summary>
        /// <remarks>
        /// This is the shape every authored panel in a PS1 game has, and building
        /// it by hand is nine components whose rects have to agree to the pixel -
        /// exactly the arithmetic that used to live in Lua as
        /// <c>drawFrame()</c>. Cell indices follow the conventional 3x3 block
        /// starting at the top-left corner; retarget them in the inspector if the
        /// sheet is laid out differently.
        /// </remarks>
        [MenuItem(k_GO + "Nine-Slice Panel", false, 40)]
        public static void CreateNineSlice(MenuCommand cmd)
        {
            GameObject parent = ResolveParent(cmd);
            if (parent == null) return;

            var root = new GameObject("Panel", typeof(RectTransform));
            GameObjectUtility.SetParentAndAlign(root, parent);
            RectTransform rrt = root.GetComponent<RectTransform>();
            Configure(rrt, new Vector2(0, 0), new Vector2(240, 152));
            Undo.RegisterCreatedObjectUndo(root, "Create PSX Nine-Slice Panel");

            // 16px is the cell size of every sheet this was written for, and the
            // builder cannot know better until a sheet is assigned - the inspector
            // "Snap size" button fixes each piece up afterwards.
            const int c = 16;
            float w = rrt.sizeDelta.x, h = rrt.sizeDelta.y;

            // name, x, y, w, h, cell
            var pieces = new (string name, float x, float y, float w, float h, int cell)[]
            {
                ("middle",      c,     c,     w - 2 * c, h - 2 * c, 4),
                ("edge_top",    c,     0,     w - 2 * c, c,         1),
                ("edge_bottom", c,     h - c, w - 2 * c, c,         7),
                ("edge_left",   0,     c,     c,         h - 2 * c, 3),
                ("edge_right",  w - c, c,     c,         h - 2 * c, 5),
                ("corner_tl",   0,     0,     c,         c,         0),
                ("corner_tr",   w - c, 0,     c,         c,         2),
                ("corner_bl",   0,     h - c, c,         c,         6),
                ("corner_br",   w - c, h - c, c,         c,         8),
            };

            // Created in this order deliberately: the middle is FIRST, so it is
            // the first sibling and therefore behind everything, and the corners
            // are last so they cover the seams where the edges meet. Draw order
            // is sibling order (see UISystem::renderOT).
            foreach (var p in pieces)
            {
                var go = new GameObject(p.name, typeof(RectTransform), typeof(PSXUISprite));
                GameObjectUtility.SetParentAndAlign(go, root);
                Configure(go.GetComponent<RectTransform>(), new Vector2(p.x, -p.y), new Vector2(p.w, p.h));

                var so = new SerializedObject(go.GetComponent<PSXUISprite>());
                so.FindProperty("elementName").stringValue = p.name;
                so.FindProperty("cell").intValue = p.cell;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            Selection.activeGameObject = root;
        }

        // ----- helpers -----

        private static GameObject NewCanvas(string name, GameObject context)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(PSXCanvas));
            GameObjectUtility.SetParentAndAlign(go, context);
            go.GetComponent<PSXCanvas>().ConfigureCanvas();
            Undo.RegisterCreatedObjectUndo(go, "Create PSX Canvas");
            return go;
        }

        /// <summary>
        /// The canvas the new element belongs under, creating one if the selection
        /// is not already inside a PSX canvas. An element outside a canvas is not
        /// exported at all, and nothing says so.
        /// </summary>
        private static GameObject ResolveParent(MenuCommand cmd)
        {
            var ctx = cmd.context as GameObject ?? Selection.activeGameObject;
            if (ctx != null && ctx.GetComponentInParent<PSXCanvas>(true) != null)
                return ctx;

            var existing = Object.FindFirstObjectByType<PSXCanvas>();
            if (existing != null) return existing.gameObject;

            return NewCanvas("PSX Canvas", null);
        }

        private static GameObject NewElement<T>(string name, MenuCommand cmd, Vector2 size)
            where T : Component
        {
            GameObject parent = ResolveParent(cmd);
            var go = new GameObject(name, typeof(RectTransform), typeof(T));
            GameObjectUtility.SetParentAndAlign(go, parent);
            Configure(go.GetComponent<RectTransform>(), Vector2.zero, size);
            Undo.RegisterCreatedObjectUndo(go, "Create PSX UI " + name);
            return go;
        }

        /// <summary>
        /// Top-left pivot, top-left anchor, whole-pixel rect. Anchoring at the
        /// canvas's top-left rather than its centre is what makes the inspector's
        /// Pos X / Pos Y read as PS1 screen pixels straight off.
        /// </summary>
        private static void Configure(RectTransform rt, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.sizeDelta = new Vector2(Mathf.Round(size.x), Mathf.Round(size.y));
            rt.anchoredPosition = new Vector2(Mathf.Round(anchoredPos.x), Mathf.Round(anchoredPos.y));
        }
    }
}
