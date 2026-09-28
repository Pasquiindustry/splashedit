using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// One cell of a sprite sheet, drawn as a UI element.
    /// </summary>
    /// <remarks>
    /// This is the piece that lets a game's SCREEN FURNITURE be authored instead
    /// of assembled by script. A <see cref="PSXUIImage"/> owns a texture, so a
    /// panel built from forty of them is forty textures in the atlas; a
    /// PSXUISprite points at a cell of a sheet that is already resident, so a
    /// whole panel costs the sheet it was drawn from and nothing else.
    ///
    /// It exports as a plain Image element - the engine draws it with the same
    /// two triangles - plus the sheet's cell grid, which is what makes
    /// <c>UI.SetFrame(handle, cell)</c> work: a digit, a chevron or a lamp
    /// changes cell at run time without the Lua ever creating anything.
    ///
    /// Put one on a child of a <see cref="PSXCanvas"/>. The RectTransform is the
    /// rect it draws into, in PS1 pixels.
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [AddComponentMenu("PSX/UI/PSX UI Sprite")]
    public class PSXUISprite : MonoBehaviour
    {
        /// <summary>
        /// The PS1's tint identity. Hardware computes <c>texel * colour / 128</c>,
        /// so 128 (0.5 in Unity's 0-1 colour) leaves the art exactly as drawn and
        /// 255 doubles it. Unity's white is therefore NOT "no tint", which is the
        /// single easiest thing to get wrong here.
        /// </summary>
        public static readonly Color TintIdentity = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Tooltip("Name used to reference this element from Lua: " +
                 "UI.FindElement(canvas, \"name\"). Max 24 chars.")]
        [SerializeField] private string elementName = "sprite";

        [Tooltip("The sheet this cell comes from. Its texture is packed into the " +
                 "VRAM atlas once, however many elements draw from it.")]
        [SerializeField] private PSXSpriteSheet sheet;

        [Tooltip("Which cell to draw, counting left-to-right then top-to-bottom " +
                 "from 0. Change it at run time with UI.SetFrame().")]
        [SerializeField] private int cell = 0;

        [Tooltip("Tint. 128 grey is NO tint - the PS1 computes texel * colour / 128, " +
                 "so white is double brightness. Use the Reset button in the inspector.")]
        [SerializeField] private Color tint = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Tooltip("Whether this element is visible when the scene first loads.")]
        [SerializeField] private bool startVisible = true;

        /// <summary>Element name for Lua access.</summary>
        public string ElementName => elementName;

        /// <summary>The sheet this element draws a cell of.</summary>
        public PSXSpriteSheet Sheet => sheet;

        /// <summary>Cell index, clamped to the sheet on export.</summary>
        public int Cell => cell;

        /// <summary>Tint colour (RGB; 128 grey = the art's own colours).</summary>
        public Color Tint => tint;

        /// <summary>Initial visibility flag.</summary>
        public bool StartVisible => startVisible;

        /// <summary>
        /// Cell index actually exported: clamped into the sheet so a stale index
        /// draws the sheet's last cell rather than sampling whatever is packed
        /// beside it in VRAM.
        /// </summary>
        public int ResolvedCell
        {
            get
            {
                if (sheet == null) return 0;
                return Mathf.Clamp(cell, 0, Mathf.Max(0, sheet.CellCount - 1));
            }
        }

        /// <summary>
        /// The cell's rect in the sheet's source texture, in pixels from the
        /// TOP-LEFT. Used by the scene-view preview and by the exporter.
        /// </summary>
        public RectInt CellRect
        {
            get
            {
                if (sheet == null) return new RectInt(0, 0, 16, 16);
                int c = ResolvedCell;
                int cols = Mathf.Max(1, sheet.Columns);
                return new RectInt((c % cols) * sheet.CellWidth,
                                   (c / cols) * sheet.CellHeight,
                                   sheet.CellWidth, sheet.CellHeight);
            }
        }

        /// <summary>Resize the RectTransform to the sheet's natural cell size.</summary>
        public void SnapToCellSize()
        {
            if (sheet == null) return;
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            rt.sizeDelta = new Vector2(sheet.CellWidth, sheet.CellHeight);
        }

        private void Reset()
        {
            // A UI element is authored in PS1 pixels from the top-left of the
            // screen; that only works if the pivot is the top-left corner, which
            // is also what PSXUIExporter's coordinate bake assumes.
            var rt = GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(16, 16);
            }
        }
    }
}
