using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Collects all PSXCanvas hierarchies in the scene, bakes RectTransform
    /// coordinates into PS1 pixel space, and produces <see cref="PSXCanvasData"/>
    /// arrays ready for binary serialization.
    /// </summary>
    public static class PSXUIExporter
    {
        /// <summary> 
        /// Collect all PSXCanvas components and their child UI elements,
        /// converting RectTransform coordinates to PS1 pixel space.
        /// Also collects and deduplicates custom fonts.
        /// </summary>
        /// <param name="resolution">Target PS1 resolution (e.g. 320x240).</param>
        /// <param name="fonts">Output: collected custom font data (max 3).</param>
        /// <returns>Array of canvas data ready for binary writing.</returns>
        public static PSXCanvasData[] CollectCanvases(Vector2 resolution, out PSXFontData[] fonts)
        {
            // Collect and deduplicate all custom fonts used by text elements
            List<PSXFontAsset> uniqueFonts = new List<PSXFontAsset>();

#if UNITY_EDITOR
            PSXCanvas[] canvases = Object.FindObjectsByType<PSXCanvas>(FindObjectsSortMode.None);
#else
            PSXCanvas[] canvases = Object.FindObjectsOfType<PSXCanvas>();
#endif
            if (canvases == null || canvases.Length == 0)
            {
                fonts = new PSXFontData[0];
                return new PSXCanvasData[0];
            }

            return CollectCanvasesInternal(canvases, resolution, out fonts);
        }

        /// <summary>
        /// Collect a single canvas from a prefab instance for loading screen export.
        /// The prefab must have a PSXCanvas on its root.
        /// Note: Image elements that reference VRAM textures will NOT work in loading screens
        /// since the VRAM hasn't been populated yet. Use Box, Text, and ProgressBar only.
        /// </summary>
        public static PSXCanvasData[] CollectCanvasFromPrefab(GameObject prefab, Vector2 resolution, out PSXFontData[] fonts)
        {
            if (prefab == null)
            {
                fonts = new PSXFontData[0];
                return new PSXCanvasData[0];
            }

            PSXCanvas canvas = prefab.GetComponentInChildren<PSXCanvas>(true);
            if (canvas == null)
            {
                Debug.LogWarning($"PSXUIExporter: Prefab '{prefab.name}' has no PSXCanvas component.");
                fonts = new PSXFontData[0];
                return new PSXCanvasData[0];
            }

            return CollectCanvasesInternal(new[] { canvas }, resolution, out fonts);
        }

        /// <summary>
        /// Internal shared implementation for canvas collection.
        /// Works on an explicit array of PSXCanvas components.
        /// </summary>
        private static PSXCanvasData[] CollectCanvasesInternal(PSXCanvas[] canvases, Vector2 resolution, out PSXFontData[] fonts)
        {
            List<PSXFontAsset> uniqueFonts = new List<PSXFontAsset>();

            // First pass: collect unique fonts
            foreach (PSXCanvas canvas in canvases)
            {
                PSXUIText[] texts = canvas.GetComponentsInChildren<PSXUIText>(true);
                foreach (PSXUIText txt in texts)
                {
                    PSXFontAsset font = txt.GetEffectiveFont();
                    if (font != null && !uniqueFonts.Contains(font) && uniqueFonts.Count < 3)
                        uniqueFonts.Add(font);
                }
            }

            // Build font data with VRAM positions.
            // Each font gets its own texture page to avoid V-coordinate overflow.
            // Font textures go at x=960:
            //   Font 1: y=0   (page 15,0) - 256px available
            //   Font 2: y=256 (page 15,1) - 208px available (system font at y=464)
            //   Font 3: not supported (would need different VRAM column)
            // System font: (960, 464) in page (15,1), occupies y=464-511.
            List<PSXFontData> fontDataList = new List<PSXFontData>();
            ushort[] fontPageStarts = { 0, 256 }; // one per texture page
            int fontPageIndex = 0;

            foreach (PSXFontAsset fa in uniqueFonts)
            {
                byte[] pixelData = fa.ConvertTo4BPP();
                if (pixelData == null) continue;

                // Read advance widths directly from the font asset.
                // These were computed during bitmap generation from the exact same
                // CharacterInfo used to render the glyphs - guaranteed to match.
                byte[] advances = fa.AdvanceWidths;
                if (advances == null || advances.Length < 96)
                {
                    Debug.LogWarning($"PSXUIExporter: Font '{fa.name}' has no stored advance widths. Using cell width as fallback.");
                    advances = new byte[96];
                    for (int i = 0; i < 96; i++) advances[i] = (byte)fa.GlyphWidth;
                }

                ushort texH = (ushort)fa.TextureHeight;

                if (fontPageIndex >= fontPageStarts.Length)
                {
                    Debug.LogError($"PSXUIExporter: Max 2 custom fonts supported (need separate texture pages). Skipping '{fa.name}'.");
                    continue;
                }

                ushort vramY = fontPageStarts[fontPageIndex];
                int maxHeight = (fontPageIndex == 1) ? 208 : 256; // page 1 shares with system font
                if (texH > maxHeight)
                {
                    Debug.LogWarning($"PSXUIExporter: Font '{fa.name}' texture ({texH}px) exceeds page limit ({maxHeight}px). May be clipped.");
                }

                fontDataList.Add(new PSXFontData
                {
                    Source = fa,
                    GlyphWidth = (byte)fa.GlyphWidth,
                    GlyphHeight = (byte)fa.GlyphHeight,
                    VramX = 960,
                    VramY = vramY,
                    TextureHeight = texH,
                    PixelData = pixelData,
                    AdvanceWidths = advances
                });
                fontPageIndex++;
            }
            fonts = fontDataList.ToArray();

            // Second pass: collect canvases with font index assignment
            List<PSXCanvasData> result = new List<PSXCanvasData>();

            foreach (PSXCanvas canvas in canvases)
            {
                Canvas unityCanvas = canvas.GetComponent<Canvas>();
                if (unityCanvas == null) continue;

                RectTransform canvasRect = canvas.GetComponent<RectTransform>();
                float canvasW = canvasRect.rect.width;
                float canvasH = canvasRect.rect.height;
                if (canvasW <= 0) canvasW = resolution.x;
                if (canvasH <= 0) canvasH = resolution.y;

                float scaleX = resolution.x / canvasW;
                float scaleY = resolution.y / canvasH;

                List<PSXUIElementData> elements = new List<PSXUIElementData>();

                Debug.Log($"[UIExporter] Canvas '{canvas.CanvasName}' on '{canvas.gameObject.name}' " +
                          $"canvasW={canvasW} canvasH={canvasH} childCount={canvas.transform.childCount}");

                // Collect all UI elements in hierarchy order (depth-first, sibling index).

                CollectAllElementsInHierarchyOrder(canvas.transform, canvasRect, scaleX, scaleY, resolution, elements, uniqueFonts);
                Debug.Log($"[UIExporter]   TOTAL elements: {elements.Count}");

                string name = canvas.CanvasName ?? "canvas";
                if (name.Length > 24) name = name.Substring(0, 24);

                result.Add(new PSXCanvasData
                {
                    Name = name,
                    StartVisible = canvas.StartVisible,
                    SortOrder = canvas.SortOrder,
                    Elements = elements.ToArray()
                });
            }

            WarnOnBudget(result);
            return result.ToArray();
        }

        /// <summary>
        /// Say plainly when a scene has authored more UI than the runtime pool
        /// holds. The loader clamps and carries on silently, so without this the
        /// symptom is "the last canvas I added does not exist" with nothing in any
        /// log to connect it to a limit.
        /// </summary>
        private static void WarnOnBudget(List<PSXCanvasData> canvases)
        {
            int total = 0;
            foreach (var cv in canvases) total += cv.Elements?.Length ?? 0;

            if (total > PSXUIBudget.MaxElements)
                Debug.LogError($"[UIExporter] {total} UI elements across {canvases.Count} canvases, but the " +
                               $"runtime pool holds {PSXUIBudget.MaxElements}. The last " +
                               $"{total - PSXUIBudget.MaxElements} will be DROPPED at load with no error.");

            if (canvases.Count > PSXUIBudget.MaxCanvases)
                Debug.LogError($"[UIExporter] {canvases.Count} canvases, but the runtime pool holds " +
                               $"{PSXUIBudget.MaxCanvases}. The rest will be dropped at load.");

            foreach (var cv in canvases)
            {
                if ((cv.Elements?.Length ?? 0) <= 255) continue;
                Debug.LogError($"[UIExporter] Canvas '{cv.Name}' has {cv.Elements.Length} elements; the " +
                               "splashpack stores that count in one byte, so it must be 255 or fewer.");
            }
        }

        // --- Coordinate baking helpers ---

        /// <summary>
        /// Convert a RectTransform into PS1 pixel-space layout values.
        /// </summary>
        /// <remarks>
        /// The arithmetic lives in <see cref="PSXUILayout"/> so the scene-view
        /// preview computes the identical rect. This wrapper only unpacks it into
        /// the out-parameters the collectors below were written against.
        /// </remarks>
        private static void BakeLayout(
            RectTransform rt, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            out short x, out short y, out short w, out short h,
            out byte anchorMinX, out byte anchorMinY,
            out byte anchorMaxX, out byte anchorMaxY)
        {
            PSXUILayout.Baked b = PSXUILayout.Bake(rt, scaleX, scaleY);
            x = b.X; y = b.Y; w = b.W; h = b.H;
            anchorMinX = b.AnchorMinX; anchorMinY = b.AnchorMinY;
            anchorMaxX = b.AnchorMaxX; anchorMaxY = b.AnchorMaxY;
        }

        private static string TruncateName(string name, int maxLen = 24)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length > maxLen ? name.Substring(0, maxLen) : name;
        }

        // --- Collectors ---

        /// <summary>
        /// Walk the hierarchy depth-first in sibling order, collecting every
        /// PSX UI component into <paramref name="elements"/> so that draw order
        /// matches the Unity scene tree (top-to-bottom = back-to-front).
        /// </summary>
        private static void CollectAllElementsInHierarchyOrder(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements,
            List<PSXFontAsset> uniqueFonts)
        {
            // GetComponentsInChildren iterates depth-first in sibling order -
            // exactly the hierarchy ordering we want.
            Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in allTransforms)
            {
                if (t == root) continue; // skip the canvas root itself

                // Check each supported component type on this transform.
                // A single GameObject should only have one PSX UI component,
                // but we check all to be safe.
                PSXUISprite spr = t.GetComponent<PSXUISprite>();
                if (spr != null)
                {
                    CollectSingleSprite(spr, canvasRect, scaleX, scaleY, resolution, elements);
                    continue;
                }

                PSXUIImage img = t.GetComponent<PSXUIImage>();
                if (img != null)
                {
                    CollectSingleImage(img, canvasRect, scaleX, scaleY, resolution, elements);
                    continue;
                }

                PSXUIBox box = t.GetComponent<PSXUIBox>();
                if (box != null)
                {
                    CollectSingleBox(box, canvasRect, scaleX, scaleY, resolution, elements);
                    continue;
                }

                PSXUILine line = t.GetComponent<PSXUILine>();
                if (line != null)
                {
                    CollectSingleLine(line, canvasRect, scaleX, scaleY, resolution, elements);
                    continue;
                }

                PSXUIText txt = t.GetComponent<PSXUIText>();
                if (txt != null)
                {
                    CollectSingleText(txt, canvasRect, scaleX, scaleY, resolution, elements, uniqueFonts);
                    continue;
                }

                PSXUIProgressBar bar = t.GetComponent<PSXUIProgressBar>();
                if (bar != null)
                {
                    CollectSingleProgressBar(bar, canvasRect, scaleX, scaleY, resolution, elements);
                    continue;
                }
            }
        }

        private static void CollectSingleImage(
            PSXUIImage img, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            RectTransform rt = img.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            var data = new PSXUIElementData
            {
                Type = PSXUIElementType.Image,
                StartVisible = img.StartVisible,
                Name = TruncateName(img.ElementName),
                X = x, Y = y, W = w, H = h,
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.b * 255f), 0, 255),
            };

            if (img.PackedTexture != null)
            {
                PSXTexture2D tex = img.PackedTexture;
                int expander = 16 / (int)tex.BitDepth;
                data.TexpageX = tex.TexpageX;
                data.TexpageY = tex.TexpageY;
                data.ClutX = (ushort)tex.ClutPackingX;
                data.ClutY = (ushort)tex.ClutPackingY;
                data.U0 = (byte)(tex.PackingX * expander);
                data.V0 = (byte)tex.PackingY;
                data.U1 = (byte)(tex.PackingX * expander + tex.Width - 1);
                data.V1 = (byte)(tex.PackingY + tex.Height - 1);
                data.BitDepthIndex = tex.BitDepth switch
                {
                    PSXBPP.TEX_4BIT => 0,
                    PSXBPP.TEX_8BIT => 1,
                    PSXBPP.TEX_16BIT => 2,
                    _ => 2
                };

                Debug.Log($"[UIImage] '{img.ElementName}' src='{(tex.OriginalTexture ? tex.OriginalTexture.name : "null")}' " +
                          $"bpp={(int)tex.BitDepth} W={tex.Width} H={tex.Height} QW={tex.QuantizedWidth} " +
                          $"packXY=({tex.PackingX},{tex.PackingY}) tpage=({tex.TexpageX},{tex.TexpageY}) " +
                          $"clutXY=({tex.ClutPackingX},{tex.ClutPackingY}) " +
                          $"UV=({data.U0},{data.V0})->({data.U1},{data.V1}) expander={expander} bitIdx={data.BitDepthIndex}");
            }
            else
            {
                Debug.LogWarning($"[UIImage] '{img.ElementName}' has NULL PackedTexture!");
            }

            elements.Add(data);
        }

        /// <summary>
        /// One cell of a sprite sheet, exported as an Image element plus the
        /// sheet's cell grid so the runtime can re-point it with UI.SetFrame.
        /// </summary>
        /// <remarks>
        /// The sheet's pixels are already in the atlas - PSXSpriteExporter packed
        /// them - so this costs no VRAM beyond the sheet itself, however many
        /// elements draw from it. That is the whole reason a panel can be built
        /// out of forty of these when forty PSXUIImages would be forty textures.
        /// </remarks>
        private static void CollectSingleSprite(
            PSXUISprite spr, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            RectTransform rt = spr.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            var data = new PSXUIElementData
            {
                Type = PSXUIElementType.Image,
                StartVisible = spr.StartVisible,
                Name = TruncateName(spr.ElementName),
                X = x, Y = y, W = w, H = h,
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(spr.Tint.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(spr.Tint.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(spr.Tint.b * 255f), 0, 255),
            };

            PSXSpriteSheet sheet = spr.Sheet;
            PSXTexture2D tex = sheet != null ? sheet.PackedTexture : null;
            if (sheet == null || tex == null)
            {
                // Not fatal: the element still exists, still has its name, and
                // Lua's UI.FindElement still resolves it. It just draws nothing
                // recognisable - which is a far better handover than an export
                // that refuses to run because one icon is unassigned.
                Debug.LogWarning($"[UISprite] '{spr.ElementName}' has " +
                                 (sheet == null ? "no sheet assigned." : "a sheet that was not packed - " +
                                  "is it referenced by a PSXSprite or another PSXUISprite in this scene?"));
                elements.Add(data);
                return;
            }

            // A 4bpp texture stores four texels per VRAM halfword, so the packer's
            // X is in halfwords and the U axis needs expanding - the same
            // conversion PSXSpriteExporter.Flatten does for the sheet table.
            int expander = 16 / (int)tex.BitDepth;
            int baseU = tex.PackingX * expander;
            int baseV = tex.PackingY;

            int cols = Mathf.Max(1, sheet.Columns);
            int cell = spr.ResolvedCell;
            int u0 = baseU + (cell % cols) * sheet.CellWidth;
            int v0 = baseV + (cell / cols) * sheet.CellHeight;

            data.TexpageX = tex.TexpageX;
            data.TexpageY = tex.TexpageY;
            data.ClutX = (ushort)tex.ClutPackingX;
            data.ClutY = (ushort)tex.ClutPackingY;
            data.U0 = (byte)u0;
            data.V0 = (byte)v0;
            // U1/V1 are the LAST texel, inclusive - without the -1 a 256-wide
            // sheet overflows the byte to 0 and the element samples a sliver.
            data.U1 = (byte)(u0 + sheet.CellWidth - 1);
            data.V1 = (byte)(v0 + sheet.CellHeight - 1);
            data.BitDepthIndex = tex.BitDepth switch
            {
                PSXBPP.TEX_4BIT => 0,
                PSXBPP.TEX_8BIT => 1,
                PSXBPP.TEX_16BIT => 2,
                _ => 2
            };
            data.CellW = (byte)sheet.CellWidth;
            data.CellH = (byte)sheet.CellHeight;
            data.SheetCols = (byte)Mathf.Clamp(cols, 1, 255);
            data.BaseU = (byte)baseU;
            data.BaseV = (byte)baseV;

            elements.Add(data);
        }

        private static void CollectSingleBox(
            PSXUIBox box, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            RectTransform rt = box.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            elements.Add(new PSXUIElementData
            {
                Type = PSXUIElementType.Box,
                StartVisible = box.StartVisible,
                Name = TruncateName(box.ElementName),
                X = x, Y = y, W = w, H = h,
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.b * 255f), 0, 255),
            });
        }

        private static void CollectSingleLine(
            PSXUILine line, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            RectTransform rt = line.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            elements.Add(new PSXUIElementData
            {
                Type = PSXUIElementType.Line,
                StartVisible = line.StartVisible,
                Name = TruncateName(line.ElementName),
                // Width and height are used for the second endpoint of the line
                X = (byte)Mathf.RoundToInt(line.Point1.x), Y = (byte)Mathf.RoundToInt(line.Point1.y),
                W = (byte)Mathf.RoundToInt(line.Point2.x), H = (byte)Mathf.RoundToInt(line.Point2.y),
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.b * 255f), 0, 255),
            });
        }

        private static void CollectSingleText(
            PSXUIText txt, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements,
            List<PSXFontAsset> uniqueFonts)
        {
            RectTransform rt = txt.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            string defaultText = txt.DefaultText ?? "";
            if (defaultText.Length > 63) defaultText = defaultText.Substring(0, 63);

            byte fontIndex = 0;
            PSXFontAsset effectiveFont = txt.GetEffectiveFont();
            if (effectiveFont != null && uniqueFonts != null)
            {
                int idx = uniqueFonts.IndexOf(effectiveFont);
                if (idx >= 0) fontIndex = (byte)(idx + 1);
            }

            elements.Add(new PSXUIElementData
            {
                Type = PSXUIElementType.Text,
                StartVisible = txt.StartVisible,
                Name = TruncateName(txt.ElementName),
                X = x, Y = y, W = w, H = h,
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.b * 255f), 0, 255),
                DefaultText = defaultText,
                FontIndex = fontIndex,
            });
        }

        private static void CollectSingleProgressBar(
            PSXUIProgressBar bar, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            RectTransform rt = bar.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            elements.Add(new PSXUIElementData
            {
                Type = PSXUIElementType.Progress,
                StartVisible = bar.StartVisible,
                Name = TruncateName(bar.ElementName),
                X = x, Y = y, W = w, H = h,
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.b * 255f), 0, 255),
                BgR = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.r * 255f), 0, 255),
                BgG = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.g * 255f), 0, 255),
                BgB = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.b * 255f), 0, 255),
                ProgressValue = (byte)bar.InitialValue,
            });
        }

        // --- Legacy per-type collectors (kept for reference, no longer called) ---

        private static void CollectImages(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            PSXUIImage[] images = root.GetComponentsInChildren<PSXUIImage>(true);
            foreach (PSXUIImage img in images)
            {
                RectTransform rt = img.GetComponent<RectTransform>();
                if (rt == null) continue;

                BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                    out short x, out short y, out short w, out short h,
                    out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

                var data = new PSXUIElementData
                {
                    Type = PSXUIElementType.Image,
                    StartVisible = img.StartVisible,
                    Name = TruncateName(img.ElementName),
                    X = x, Y = y, W = w, H = h,
                    AnchorMinX = amin_x, AnchorMinY = amin_y,
                    AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                    ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.r * 255f), 0, 255),
                    ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.g * 255f), 0, 255),
                    ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.b * 255f), 0, 255),
                };

                // Image texture data is filled in after VRAM packing by
                // FillImageTextureData() - see PSXSceneExporter integration
                if (img.PackedTexture != null)
                {
                    PSXTexture2D tex = img.PackedTexture;
                    // Convert PackingX from VRAM halfwords to texture-pixel U coords.
                    // 4bpp: 4 pixels per halfword, 8bpp: 2, 16bpp: 1
                    int expander = 16 / (int)tex.BitDepth;
                    data.TexpageX = tex.TexpageX;
                    data.TexpageY = tex.TexpageY;
                    data.ClutX = (ushort)tex.ClutPackingX;
                    data.ClutY = (ushort)tex.ClutPackingY;
                    data.U0 = (byte)(tex.PackingX * expander);
                    data.V0 = (byte)tex.PackingY;
                    // U1/V1 are the LAST texel (inclusive), not one-past-end.
                    // Without -1, values >= 256 overflow byte to 0.
                    data.U1 = (byte)(tex.PackingX * expander + tex.Width - 1);
                    data.V1 = (byte)(tex.PackingY + tex.Height - 1);
                    data.BitDepthIndex = tex.BitDepth switch
                    {
                        PSXBPP.TEX_4BIT => 0,
                        PSXBPP.TEX_8BIT => 1,
                        PSXBPP.TEX_16BIT => 2,
                        _ => 2
                    };

                    Debug.Log($"[UIImage] '{img.ElementName}' src='{(tex.OriginalTexture ? tex.OriginalTexture.name : "null")}' " +
                              $"bpp={(int)tex.BitDepth} W={tex.Width} H={tex.Height} QW={tex.QuantizedWidth} " +
                              $"packXY=({tex.PackingX},{tex.PackingY}) tpage=({tex.TexpageX},{tex.TexpageY}) " +
                              $"clutXY=({tex.ClutPackingX},{tex.ClutPackingY}) " +
                              $"UV=({data.U0},{data.V0})->({data.U1},{data.V1}) expander={expander} bitIdx={data.BitDepthIndex}");
                }
                else
                {
                    Debug.LogWarning($"[UIImage] '{img.ElementName}' has NULL PackedTexture!");
                }

                elements.Add(data);
            }
        }

        private static void CollectBoxes(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            PSXUIBox[] boxes = root.GetComponentsInChildren<PSXUIBox>(true);
            foreach (PSXUIBox box in boxes)
            {
                RectTransform rt = box.GetComponent<RectTransform>();
                if (rt == null) continue;

                BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                    out short x, out short y, out short w, out short h,
                    out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

                elements.Add(new PSXUIElementData
                {
                    Type = PSXUIElementType.Box,
                    StartVisible = box.StartVisible,
                    Name = TruncateName(box.ElementName),
                    X = x, Y = y, W = w, H = h,
                    AnchorMinX = amin_x, AnchorMinY = amin_y,
                    AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                    ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.r * 255f), 0, 255),
                    ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.g * 255f), 0, 255),
                    ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.b * 255f), 0, 255),
                });
            }
        }

        private static void CollectLines(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            PSXUILine[] lines = root.GetComponentsInChildren<PSXUILine>(true);
            foreach (PSXUILine line in lines)
            {
                RectTransform rt = line.GetComponent<RectTransform>();
                if (rt == null) continue;

                BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                    out short x, out short y, out short w, out short h,
                    out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

                elements.Add(new PSXUIElementData
                {
                    Type = PSXUIElementType.Line,
                    StartVisible = line.StartVisible,
                    Name = TruncateName(line.ElementName),
                    // Width and height are used for the second endpoint of the line
                    X = (byte)Mathf.RoundToInt(line.Point1.x), Y = (byte)Mathf.RoundToInt(line.Point1.y),
                    W = (byte)Mathf.RoundToInt(line.Point2.x), H = (byte)Mathf.RoundToInt(line.Point2.y),
                    AnchorMinX = amin_x, AnchorMinY = amin_y,
                    AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                    ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.r * 255f), 0, 255),
                    ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.g * 255f), 0, 255),
                    ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.b * 255f), 0, 255),
                });
            }
        }

        private static void CollectTexts(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements,
            List<PSXFontAsset> uniqueFonts = null)
        {
            PSXUIText[] texts = root.GetComponentsInChildren<PSXUIText>(true);
            foreach (PSXUIText txt in texts)
            {
                RectTransform rt = txt.GetComponent<RectTransform>();
                if (rt == null) continue;

                BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                    out short x, out short y, out short w, out short h,
                    out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

                string defaultText = txt.DefaultText ?? "";
                if (defaultText.Length > 63) defaultText = defaultText.Substring(0, 63);

                // Resolve font index: 0 = system font, 1+ = custom font
                byte fontIndex = 0;
                PSXFontAsset effectiveFont = txt.GetEffectiveFont();
                if (effectiveFont != null && uniqueFonts != null)
                {
                    int idx = uniqueFonts.IndexOf(effectiveFont);
                    if (idx >= 0) fontIndex = (byte)(idx + 1); // 1-based for custom fonts
                }

                elements.Add(new PSXUIElementData
                {
                    Type = PSXUIElementType.Text,
                    StartVisible = txt.StartVisible,
                    Name = TruncateName(txt.ElementName),
                    X = x, Y = y, W = w, H = h,
                    AnchorMinX = amin_x, AnchorMinY = amin_y,
                    AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                    ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.r * 255f), 0, 255),
                    ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.g * 255f), 0, 255),
                    ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.b * 255f), 0, 255),
                    DefaultText = defaultText,
                    FontIndex = fontIndex,
                });
            }
        }

        private static void CollectProgressBars(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            PSXUIProgressBar[] bars = root.GetComponentsInChildren<PSXUIProgressBar>(true);
            foreach (PSXUIProgressBar bar in bars)
            {
                RectTransform rt = bar.GetComponent<RectTransform>();
                if (rt == null) continue;

                BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                    out short x, out short y, out short w, out short h,
                    out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

                elements.Add(new PSXUIElementData
                {
                    Type = PSXUIElementType.Progress,
                    StartVisible = bar.StartVisible,
                    Name = TruncateName(bar.ElementName),
                    X = x, Y = y, W = w, H = h,
                    AnchorMinX = amin_x, AnchorMinY = amin_y,
                    AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                    // Fill color goes into primary color (used for the fill bar)
                    ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.r * 255f), 0, 255),
                    ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.g * 255f), 0, 255),
                    ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.b * 255f), 0, 255),
                    // Background color goes into progress-specific fields
                    BgR = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.r * 255f), 0, 255),
                    BgG = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.g * 255f), 0, 255),
                    BgB = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.b * 255f), 0, 255),
                    ProgressValue = (byte)bar.InitialValue,
                });
            }
        }
    }
}
