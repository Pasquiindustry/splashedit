using System.Collections.Generic;
using System.Linq;
using SplashEdit.RuntimeCode;
using UnityEditor;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Composites the whole PS1 screen - the tilemap floor and every UI canvas
    /// over it - into a rect, at exact PS1 pixels.
    /// </summary>
    /// <remarks>
    /// This is the answer to authoring UI blind. A PSXCanvas is a world-space
    /// Canvas in the XY plane and a tilemap lies in the XZ plane, so the 3D scene
    /// view can never show them together no matter how good the gizmos are - and
    /// "together" is exactly the question you have while placing a HUD: does this
    /// label sit on top of the corridor, does the task panel cover the prompt bar.
    ///
    /// Everything here goes through the same code the exporter uses
    /// (<see cref="PSXUILayout"/>) and mirrors what uisystem.cpp draws, including
    /// the two things that are easiest to get wrong by eye:
    ///
    ///   * <b>draw order</b> - lower sortOrder behind, earlier sibling behind;
    ///   * <b>tint</b> - the PS1 computes <c>texel * colour / 128</c>, so a 128
    ///     grey is "no tint" and Unity's white is DOUBLE brightness.
    /// </remarks>
    public static class PSXScreenPreview
    {
        /// <summary>One drawn element, kept so the preview can be clicked.</summary>
        public struct Hit
        {
            public GameObject Target;
            public Rect ScreenRect;   // in GUI space
            public RectInt PixelRect; // in PS1 pixels
        }

        // --- persisted view options -----------------------------------------

        private const string k_Prefix = "PSXSplash.ScreenPreview.";

        public static bool ShowTilemap
        {
            get => EditorPrefs.GetBool(k_Prefix + "Tilemap", true);
            set => EditorPrefs.SetBool(k_Prefix + "Tilemap", value);
        }

        public static bool ShowHiddenCanvases
        {
            get => EditorPrefs.GetBool(k_Prefix + "Hidden", true);
            set => EditorPrefs.SetBool(k_Prefix + "Hidden", value);
        }

        public static bool ShowOutlines
        {
            get => EditorPrefs.GetBool(k_Prefix + "Outlines", false);
            set => EditorPrefs.SetBool(k_Prefix + "Outlines", value);
        }

        public static int Zoom
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(k_Prefix + "Zoom", 1), 1, 4);
            set => EditorPrefs.SetInt(k_Prefix + "Zoom", Mathf.Clamp(value, 1, 4));
        }

        public static Vector2Int TileView
        {
            get => new Vector2Int(EditorPrefs.GetInt(k_Prefix + "ViewX", 0),
                                  EditorPrefs.GetInt(k_Prefix + "ViewY", 0));
            set
            {
                EditorPrefs.SetInt(k_Prefix + "ViewX", value.x);
                EditorPrefs.SetInt(k_Prefix + "ViewY", value.y);
            }
        }

        /// <summary>The PS1 clear colour behind everything. Matches a black TV.</summary>
        private static readonly Color k_Clear = new Color(0.02f, 0.02f, 0.03f, 1f);

        /// <summary>
        /// <c>texel * colour / 128</c>: 128 grey leaves art alone, 255 doubles it.
        /// Applied to every TEXTURED draw so the preview is not systematically
        /// half as bright as the console.
        /// </summary>
        public static Color TexTint(Color c) =>
            new Color(Mathf.Clamp01(c.r * 2f), Mathf.Clamp01(c.g * 2f), Mathf.Clamp01(c.b * 2f), 1f);

        /// <summary>
        /// Draw the composite. Returns what was drawn, in back-to-front order, so
        /// a caller can hit-test a click (walk it backwards to pick the topmost).
        /// </summary>
        public static List<Hit> Draw(Rect area, int zoom)
        {
            var hits = new List<Hit>();
            Vector2 res = PSXCanvas.PSXResolution;
            float scale = Mathf.Max(1, zoom);
            var screen = new Rect(area.x, area.y, res.x * scale, res.y * scale);

            EditorGUI.DrawRect(screen, k_Clear);

            if (ShowTilemap)
            {
                var renderer = Object.FindFirstObjectByType<PSXTilemapRenderer>();
                if (renderer != null && renderer.tilemap != null)
                {
                    Vector2Int v = TileView;
                    GUI.BeginClip(screen);
                    PSXTilemapPreview.DrawScreen(renderer.tilemap, new Rect(0, 0, screen.width, screen.height),
                                                 scale, v.x, v.y, res);
                    GUI.EndClip();
                }
            }

            // Ascending sortOrder: the engine draws the lowest first, so the
            // highest ends up in front. See UISystem::renderOT.
            var canvases = Object.FindObjectsByType<PSXCanvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                 .OrderBy(c => c.SortOrder)
                                 .ToList();

            GUI.BeginClip(screen);
            var inner = new Rect(0, 0, screen.width, screen.height);
            foreach (PSXCanvas canvas in canvases)
            {
                if (!canvas.StartVisible && !ShowHiddenCanvases) continue;
                DrawCanvas(canvas, inner, scale, res, hits, screen.position);
            }
            GUI.EndClip();

            // Border last, so it is never painted over.
            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.4f);
            Handles.DrawSolidRectangleWithOutline(
                new Vector3[]
                {
                    new Vector3(screen.xMin, screen.yMax), new Vector3(screen.xMin, screen.yMin),
                    new Vector3(screen.xMax, screen.yMin), new Vector3(screen.xMax, screen.yMax),
                }, Color.clear, new Color(1f, 1f, 1f, 0.4f));
            Handles.EndGUI();

            return hits;
        }

        /// <summary>The size <see cref="Draw"/> needs, for laying out the caller.</summary>
        public static Vector2 ScreenSize(int zoom)
        {
            Vector2 res = PSXCanvas.PSXResolution;
            float scale = Mathf.Max(1, zoom);
            return new Vector2(res.x * scale, res.y * scale);
        }

        private static void DrawCanvas(PSXCanvas canvas, Rect clip, float scale, Vector2 res,
                                       List<Hit> hits, Vector2 clipOrigin)
        {
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            if (canvasRect == null) return;

            bool dim = !canvas.StartVisible;

            // Hierarchy order: depth-first in sibling order, which is what
            // PSXUIExporter collects and therefore what the element array is.
            // Earlier = behind, so painting in order is correct.
            foreach (Transform t in canvas.GetComponentsInChildren<Transform>(true))
            {
                if (t == canvas.transform) continue;
                var rt = t as RectTransform;
                if (rt == null) continue;

                RectInt px = PSXUILayout.ScreenRect(rt, canvasRect, res);
                var r = new Rect(px.x * scale, px.y * scale, px.width * scale, px.height * scale);

                bool drew = DrawElement(t, rt, canvasRect, r, scale, res, dim);
                if (drew)
                    hits.Add(new Hit { Target = t.gameObject, ScreenRect = new Rect(r.position + clipOrigin, r.size), PixelRect = px });
            }
        }

        private static bool DrawElement(Transform t, RectTransform rt, RectTransform canvasRect,
                                        Rect r, float scale, Vector2 res, bool dim)
        {
            float alpha = dim ? 0.35f : 1f;

            var spr = t.GetComponent<PSXUISprite>();
            if (spr != null) { DrawSprite(spr, r, alpha); Outline(r, Color.cyan); return true; }

            var img = t.GetComponent<PSXUIImage>();
            if (img != null)
            {
                if (img.SourceTexture != null)
                {
                    Color c = TexTint(img.TintColor); c.a = alpha;
                    GUI.color = c;
                    GUI.DrawTexture(r, img.SourceTexture, ScaleMode.StretchToFill);
                    GUI.color = Color.white;
                }
                else EditorGUI.DrawRect(r, new Color(0.3f, 0.3f, 0.6f, 0.6f * alpha));
                Outline(r, Color.cyan);
                return true;
            }

            var box = t.GetComponent<PSXUIBox>();
            if (box != null)
            {
                // A Box is an untextured primitive: the PS1 uses its colour
                // directly, no /128 halving. Do NOT run it through TexTint.
                Color c = box.BoxColor; c.a = alpha;
                EditorGUI.DrawRect(r, c);
                Outline(r, Color.green);
                return true;
            }

            var bar = t.GetComponent<PSXUIProgressBar>();
            if (bar != null)
            {
                Color bg = bar.BackgroundColor; bg.a = alpha;
                EditorGUI.DrawRect(r, bg);
                float f = Mathf.Clamp01(bar.InitialValue / 100f);
                if (f > 0f)
                {
                    Color fc = bar.FillColor; fc.a = alpha;
                    EditorGUI.DrawRect(new Rect(r.x, r.y, r.width * f, r.height), fc);
                }
                Outline(r, Color.yellow);
                return true;
            }

            var line = t.GetComponent<PSXUILine>();
            if (line != null)
            {
                // A Line's endpoints are absolute PS1 pixels, not its rect - the
                // exporter writes Point1/Point2 straight into the record.
                Handles.BeginGUI();
                Color c = line.LineColor; c.a = alpha;
                Handles.color = c;
                Handles.DrawLine(new Vector3(line.Point1.x * scale, line.Point1.y * scale),
                                 new Vector3(line.Point2.x * scale, line.Point2.y * scale));
                Handles.EndGUI();
                return true;
            }

            var txt = t.GetComponent<PSXUIText>();
            if (txt != null)
            {
                DrawText(txt, r, scale, alpha);
                Outline(r, new Color(1f, 1f, 1f, 0.5f));
                return true;
            }

            return false;
        }

        private static void Outline(Rect r, Color c)
        {
            if (!ShowOutlines) return;
            Handles.BeginGUI();
            Handles.color = new Color(c.r, c.g, c.b, 0.5f);
            Handles.DrawAAPolyLine(1f,
                new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin),
                new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax), new Vector3(r.xMin, r.yMin));
            Handles.EndGUI();
        }

        /// <summary>One cell of a sheet, with the cell's tex coords.</summary>
        public static void DrawSprite(PSXUISprite spr, Rect r, float alpha = 1f)
        {
            PSXSpriteSheet sheet = spr.Sheet;
            if (sheet == null || sheet.SourceTexture == null)
            {
                EditorGUI.DrawRect(r, new Color(0.8f, 0.2f, 0.8f, 0.5f * alpha));
                return;
            }

            Texture2D tex = sheet.SourceTexture;
            RectInt cell = spr.CellRect;
            var uv = new Rect((float)cell.x / tex.width,
                              // CellRect counts down from the top; UVs count up.
                              1f - (float)(cell.y + cell.height) / tex.height,
                              (float)cell.width / tex.width,
                              (float)cell.height / tex.height);

            Color c = TexTint(spr.Tint); c.a = alpha;
            GUI.color = c;
            GUI.DrawTextureWithTexCoords(r, tex, uv, true);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Text, glyph by glyph with the authored font's advance widths - the same
        /// walk <c>UISystem::renderProportionalText</c> does. Drawing it with a
        /// Unity label instead is what let a "32 characters wide" row ship running
        /// off the right of the screen.
        /// </summary>
        public static void DrawText(PSXUIText txt, Rect r, float scale, float alpha = 1f)
        {
            string label = txt.DefaultText ?? "";
            PSXFontAsset font = txt.GetEffectiveFont();
            Color tint = TexTint(txt.TextColor); tint.a = alpha;

            if (font == null || font.FontTexture == null)
            {
                // System font (8x16 fixed). No bitmap to sample, so approximate
                // with a label - and say so, because the sizes differ.
                var style = new GUIStyle(EditorStyles.label)
                {
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(11 * scale), 7, 40),
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                };
                style.normal.textColor = tint;
                GUI.Label(r, label, style);
                return;
            }

            Texture2D fontTex = font.FontTexture;
            int glyphW = font.GlyphWidth, glyphH = font.GlyphHeight;
            int perRow = font.GlyphsPerRow;

            GUI.color = tint;
            float cursor = r.x;
            foreach (char ch in label)
            {
                char c = (ch < 32 || ch > 126) ? '?' : ch;
                int idx = c - 32;
                float advance = (font.AdvanceWidths != null && idx < font.AdvanceWidths.Length)
                                ? font.AdvanceWidths[idx] : glyphW;

                if (c != ' ')
                {
                    int col = idx % perRow, row = idx / perRow;
                    // The runtime draws `advance` pixels of a left-aligned glyph,
                    // so sample exactly that slice - a full cell would overlap the
                    // next character and read as bold.
                    float drawW = (advance > 0 && advance < glyphW) ? advance : glyphW;
                    var uv = new Rect((float)(col * glyphW) / fontTex.width,
                                      1f - (float)((row + 1) * glyphH) / fontTex.height,
                                      drawW / fontTex.width,
                                      (float)glyphH / fontTex.height);
                    GUI.DrawTextureWithTexCoords(
                        new Rect(cursor, r.y, drawW * scale, glyphH * scale), fontTex, uv, true);
                }
                cursor += advance * scale;
            }
            GUI.color = Color.white;
        }
    }
}
