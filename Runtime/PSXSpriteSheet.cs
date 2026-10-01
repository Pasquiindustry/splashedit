using System;
using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A named run of consecutive cells on a sheet.
    /// </summary>
    [Serializable]
    public class PSXSpriteAnimation
    {
        [Tooltip("Name used to reference this animation from Lua (max 24 chars).")]
        public string animationName = "idle";

        [Tooltip("First cell of the run, counting left-to-right then top-to-bottom from 0.")]
        public int firstFrame = 0;

        [Tooltip("How many consecutive cells the animation covers.")]
        public int frameCount = 1;

        [Tooltip("How many vsync frames each cell is held. At 60Hz, 6 gives 10 fps.")]
        public int frameDuration = 6;

        [Tooltip("Restart at the first frame when the run ends; otherwise hold the last.")]
        public bool loop = true;
    }

    /// <summary>
    /// A sprite sheet: one texture cut into a uniform grid of cells, plus the
    /// animations defined over that grid.
    /// </summary>
    /// <remarks>
    /// The texture is packed into the same VRAM atlas as UI images and 3D
    /// textures, so a sheet costs exactly what its pixels cost and nothing more.
    ///
    /// Cells are addressed by index, counting left-to-right then top-to-bottom,
    /// which is the order every sprite tool on earth exports in.
    /// </remarks>
    [CreateAssetMenu(fileName = "SpriteSheet", menuName = "PSXSplash/Sprite Sheet")]
    public class PSXSpriteSheet : ScriptableObject
    {
        [Tooltip("Name used to reference this sheet from Lua (max 24 chars).")]
        [SerializeField] private string sheetName = "sheet";

        [Tooltip("Source texture. Quantized and packed into VRAM alongside UI and 3D textures.")]
        [SerializeField] private Texture2D sourceTexture;

        [Tooltip("Bit depth for VRAM storage. 4-bit is the cheapest and suits flat 2D art.")]
        [SerializeField] private PSXBPP bitDepth = PSXBPP.TEX_4BIT;

        [Tooltip("Cell width in pixels. The texture width should be a whole multiple of this.")]
        [SerializeField] private int cellWidth = 16;

        [Tooltip("Cell height in pixels. The texture height should be a whole multiple of this.")]
        [SerializeField] private int cellHeight = 16;

        [Tooltip("Animations defined over this sheet's cells.")]
        [SerializeField] private List<PSXSpriteAnimation> animations = new List<PSXSpriteAnimation>();

        public string SheetName => sheetName;
        public Texture2D SourceTexture => sourceTexture;
        public PSXBPP BitDepth => bitDepth;
        public int CellWidth => Mathf.Max(1, cellWidth);
        public int CellHeight => Mathf.Max(1, cellHeight);
        public List<PSXSpriteAnimation> Animations => animations;

        /// <summary>Cells per row, derived from the source texture.</summary>
        public int Columns => sourceTexture != null ? Mathf.Max(1, sourceTexture.width / CellWidth) : 1;

        /// <summary>Rows of cells, derived from the source texture.</summary>
        public int Rows => sourceTexture != null ? Mathf.Max(1, sourceTexture.height / CellHeight) : 1;

        /// <summary>Total addressable cells.</summary>
        public int CellCount => Columns * Rows;

        /// <summary>
        /// Filled in by the exporter after VRAM packing, so the writer can emit
        /// tpage/clut/UV coordinates.
        /// </summary>
        [NonSerialized] public PSXTexture2D PackedTexture;

        /// <summary>
        /// Everything wrong with this sheet, in the words a person can act on.
        /// The inspector shows it and the exporter refuses to ship it.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(sheetName))
                problems.Add("Sheet name is empty - Lua has no way to reference this sheet.");
            else if (sheetName.Length > 24)
                problems.Add($"Sheet name '{sheetName}' is {sheetName.Length} chars; the limit is 24.");

            if (sourceTexture == null)
            {
                problems.Add("No source texture.");
                return problems;  // the rest is unknowable without it
            }

            if (cellWidth <= 0 || cellHeight <= 0)
            {
                problems.Add("Cell size must be positive.");
                return problems;
            }

            // A cell grid that does not divide the texture silently shifts every
            // frame after the first, which reads as "my animation is subtly
            // wrong" rather than as an error. Say it plainly instead.
            if (sourceTexture.width % CellWidth != 0)
                problems.Add($"Texture width {sourceTexture.width} is not a multiple of cell width {CellWidth}; " +
                             $"the rightmost {sourceTexture.width % CellWidth}px would be unreachable.");
            if (sourceTexture.height % CellHeight != 0)
                problems.Add($"Texture height {sourceTexture.height} is not a multiple of cell height {CellHeight}; " +
                             $"the bottom {sourceTexture.height % CellHeight}px would be unreachable.");

            // The runtime addresses cells and UVs with bytes.
            if (CellCount > 255)
                problems.Add($"{CellCount} cells exceeds the 255 the runtime can address.");
            if (CellWidth > 255 || CellHeight > 255)
                problems.Add("Cell size must fit in a byte (max 255).");

            var seen = new HashSet<string>();
            foreach (var anim in animations)
            {
                if (string.IsNullOrWhiteSpace(anim.animationName))
                {
                    problems.Add("An animation has no name.");
                    continue;
                }
                if (anim.animationName.Length > 24)
                    problems.Add($"Animation name '{anim.animationName}' is {anim.animationName.Length} chars; the limit is 24.");
                if (!seen.Add(anim.animationName))
                    problems.Add($"Duplicate animation name '{anim.animationName}' - lookups return the first only.");
                if (anim.frameCount < 1)
                    problems.Add($"Animation '{anim.animationName}' has no frames.");
                if (anim.firstFrame < 0 || anim.firstFrame + anim.frameCount > CellCount)
                    problems.Add($"Animation '{anim.animationName}' covers cells " +
                                 $"{anim.firstFrame}..{anim.firstFrame + anim.frameCount - 1}, " +
                                 $"but the sheet only has {CellCount} (0..{CellCount - 1}).");
                if (anim.frameDuration < 1)
                    problems.Add($"Animation '{anim.animationName}' has a frame duration below 1; " +
                                 "the runtime clamps this to 1 (advance every frame).");
            }

            return problems;
        }
    }
}
