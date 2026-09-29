using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>Flattened sheet record, ready for the binary writer.</summary>
    public class PSXSpriteSheetData
    {
        public string Name;
        public byte TexpageX, TexpageY;
        public ushort ClutX, ClutY;
        public byte U0, V0;
        public byte CellW, CellH;
        public byte Cols, Rows;
        public byte BitDepthIndex;   // 0 = 4bpp, 1 = 8bpp, 2 = 16bpp
    }

    /// <summary>Flattened animation record, ready for the binary writer.</summary>
    public class PSXSpriteAnimData
    {
        public string Name;
        public byte Sheet;
        public byte FirstFrame;
        public byte FrameCount;
        public byte FrameDuration;
        public bool Loop;
    }

    /// <summary>
    /// Collects the scene's sprite sheets and flattens them for export.
    /// </summary>
    public static class PSXSpriteExporter
    {
        // Must match SPRITE_MAX_SHEETS / SPRITE_MAX_ANIMS in the engine's
        // spritesystem.hh, which asserts on overflow at load. Failing here, with
        // the sheet's name in hand, beats an assert on the console.
        public const int MaxSheets = 16;
        public const int MaxAnims = 64;

        /// <summary>
        /// Find every distinct sheet the scene references. Called BEFORE VRAM
        /// packing so the textures can join the atlas.
        /// </summary>
        public static List<PSXSpriteSheet> CollectSheets()
        {
            var refs = Object.FindObjectsByType<PSXSprite>(FindObjectsSortMode.None);

            // A PSXUISprite references a sheet exactly as a PSXSprite does, and a
            // canvas built out of them may be the ONLY thing in the scene that
            // uses that sheet - a task panel with no world sprites at all. Left
            // out here the sheet never joins the atlas, and every element drawn
            // from it exports with a null PackedTexture: a screen of garbage
            // texels, with nothing in the log to say why.
            //
            // Inactive ones count. PSXUIExporter walks canvases with
            // GetComponentsInChildren(true), so a deactivated element is still
            // exported; if the search here skipped it the element would ship with
            // UVs into an unpacked sheet.
            var uiRefs = Object.FindObjectsByType<PSXUISprite>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            // Distinct: two objects referencing one sheet must not pack it twice.
            var sheets = refs.Select(r => r.Sheet)
                             .Concat(uiRefs.Select(r => r.Sheet))
                             .Where(s => s != null)
                             .Distinct()
                             .ToList();

            var problems = new List<string>();
            var names = new HashSet<string>();
            foreach (var sheet in sheets)
            {
                foreach (var p in sheet.Validate())
                    problems.Add($"[SpriteSheet '{sheet.name}'] {p}");

                if (!names.Add(sheet.SheetName))
                    problems.Add($"[SpriteSheet] Two sheets are both named '{sheet.SheetName}'; " +
                                 "Sprite.SheetIndex would only ever find the first.");
            }

            if (sheets.Count > MaxSheets)
                problems.Add($"[SpriteSheet] Scene uses {sheets.Count} sheets; the runtime pool holds {MaxSheets}.");

            int animTotal = sheets.Sum(s => s.Animations.Count);
            if (animTotal > MaxAnims)
                problems.Add($"[SpriteSheet] Scene defines {animTotal} animations; the runtime pool holds {MaxAnims}.");

            if (problems.Count > 0)
                throw new System.InvalidOperationException(
                    "Sprite sheet export failed:\n  " + string.Join("\n  ", problems));

            return sheets;
        }

        /// <summary>
        /// Flatten packed sheets into writer records. Called AFTER VRAM packing,
        /// once PackedTexture has real coordinates.
        /// </summary>
        public static void Flatten(List<PSXSpriteSheet> sheets,
                                   out List<PSXSpriteSheetData> sheetData,
                                   out List<PSXSpriteAnimData> animData)
        {
            sheetData = new List<PSXSpriteSheetData>();
            animData = new List<PSXSpriteAnimData>();

            for (int i = 0; i < sheets.Count; i++)
            {
                PSXSpriteSheet sheet = sheets[i];
                PSXTexture2D tex = sheet.PackedTexture;
                // Anims and the tilemap refer to sheets by their index in this
                // list, so skipping one would shift every sheet after it.
                if (tex == null)
                    throw new System.InvalidOperationException(
                        $"[SpriteSheet] '{sheet.SheetName}' was not packed into VRAM.");

                // A 4bpp texture stores four texels per VRAM hword, so the
                // packer's X is in hwords and the U axis needs expanding - the
                // same conversion PSXUIExporter does for UI images.
                int expander = 16 / (int)tex.BitDepth;

                sheetData.Add(new PSXSpriteSheetData
                {
                    Name = sheet.SheetName,
                    TexpageX = tex.TexpageX,
                    TexpageY = tex.TexpageY,
                    ClutX = (ushort)tex.ClutPackingX,
                    ClutY = (ushort)tex.ClutPackingY,
                    U0 = (byte)(tex.PackingX * expander),
                    V0 = (byte)tex.PackingY,
                    CellW = (byte)sheet.CellWidth,
                    CellH = (byte)sheet.CellHeight,
                    Cols = (byte)sheet.Columns,
                    Rows = (byte)sheet.Rows,
                    BitDepthIndex = tex.BitDepth switch
                    {
                        PSXBPP.TEX_4BIT => (byte)0,
                        PSXBPP.TEX_8BIT => (byte)1,
                        PSXBPP.TEX_16BIT => (byte)2,
                        _ => (byte)2
                    },
                });

                foreach (var anim in sheet.Animations)
                {
                    animData.Add(new PSXSpriteAnimData
                    {
                        Name = anim.animationName,
                        Sheet = (byte)i,
                        FirstFrame = (byte)Mathf.Clamp(anim.firstFrame, 0, 255),
                        FrameCount = (byte)Mathf.Clamp(anim.frameCount, 0, 255),
                        FrameDuration = (byte)Mathf.Clamp(anim.frameDuration, 1, 255),
                        Loop = anim.loop,
                    });
                }
            }
        }

        /// <summary>
        /// FNV-1a 32, the authored scene id's hash. Matches the runtime's
        /// expectation that the id is opaque and stable - the same string always
        /// produces the same number, on any machine, in any Unity version.
        /// </summary>
        public static uint HashSceneId(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            uint hash = offsetBasis;
            foreach (char c in id)
            {
                hash ^= (byte)c;
                hash *= prime;
            }
            // 0 is the sentinel for "not authored", so never hand it back as a
            // real hash; a scene id that happens to collide with it would
            // silently fall back to the derived hash.
            return hash == 0 ? 1u : hash;
        }
    }
}
