using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// The one place a RectTransform becomes PS1 screen pixels.
    /// </summary>
    /// <remarks>
    /// Both the exporter and the scene-view preview go through here, and that is
    /// the point: a preview that computes positions its own way is a preview that
    /// can agree with itself and disagree with the console. The layout bugs this
    /// project has actually shipped - rows overlapping by three pixels, a panel
    /// starting six pixels outside the plate it belonged in - were all "the
    /// numbers were worked out twice".
    ///
    /// <see cref="Bake"/> mirrors what the exporter writes into the splashpack.
    /// <see cref="Resolve"/> then mirrors uisystem.cpp's <c>resolveLayout</c>,
    /// which is what the hardware actually draws - anchors applied, clamped to
    /// the framebuffer. Preview with Resolve; export with Bake.
    /// </remarks>
    public static class PSXUILayout
    {
        /// <summary>An element's baked record, in the units the splashpack stores.</summary>
        public struct Baked
        {
            /// <summary>Offset from the anchor, in PS1 pixels, Y already inverted.</summary>
            public short X, Y;
            /// <summary>Size in PS1 pixels, or the anchor-span inset when stretched.</summary>
            public short W, H;
            /// <summary>Anchors in 8.8 fixed point (0 = 0.0, 128 = 0.5, 255 ~ 1.0).</summary>
            public byte AnchorMinX, AnchorMinY, AnchorMaxX, AnchorMaxY;
        }

        /// <summary>
        /// Convert a RectTransform into the values the exporter writes.
        /// Handles anchor-based positioning and the Y flip (Unity counts up from
        /// the bottom, the PS1 counts down from the top).
        /// </summary>
        public static Baked Bake(RectTransform rt, float scaleX, float scaleY)
        {
            var b = new Baked();

            b.AnchorMinX = (byte)Mathf.Clamp(Mathf.RoundToInt(rt.anchorMin.x * 255f), 0, 255);
            b.AnchorMinY = (byte)Mathf.Clamp(Mathf.RoundToInt((1f - rt.anchorMax.y) * 255f), 0, 255); // Y invert
            b.AnchorMaxX = (byte)Mathf.Clamp(Mathf.RoundToInt(rt.anchorMax.x * 255f), 0, 255);
            b.AnchorMaxY = (byte)Mathf.Clamp(Mathf.RoundToInt((1f - rt.anchorMin.y) * 255f), 0, 255); // Y invert

            if (Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x) &&
                Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y))
            {
                // Fixed-size element with a single anchor point. anchoredPosition
                // is the offset from that anchor, measured at the pivot.
                float px = rt.anchoredPosition.x * scaleX;
                float py = -rt.anchoredPosition.y * scaleY; // Y invert
                float pw = rt.rect.width * scaleX;
                float ph = rt.rect.height * scaleY;

                px -= rt.pivot.x * pw;
                py -= (1f - rt.pivot.y) * ph; // pivot Y inverted

                b.X = (short)Mathf.RoundToInt(px);
                b.Y = (short)Mathf.RoundToInt(py);
                b.W = (short)Mathf.Max(1, Mathf.RoundToInt(pw));
                b.H = (short)Mathf.Max(1, Mathf.RoundToInt(ph));
            }
            else
            {
                // Stretched: x/y are the offset from the anchor start and w/h the
                // combined inset (negative shrinks).
                float leftOff = rt.offsetMin.x * scaleX;
                float rightOff = rt.offsetMax.x * scaleX;
                float topOff = -rt.offsetMax.y * scaleY;  // Y invert
                float bottomOff = -rt.offsetMin.y * scaleY; // Y invert

                b.X = (short)Mathf.RoundToInt(leftOff);
                b.Y = (short)Mathf.RoundToInt(topOff);
                b.W = (short)Mathf.RoundToInt(rightOff - leftOff);
                b.H = (short)Mathf.RoundToInt(bottomOff - topOff);
            }

            return b;
        }

        /// <summary>
        /// The rect the hardware draws, in PS1 pixels from the top-left.
        /// A line-for-line mirror of <c>UISystem::resolveLayout</c>, clamp
        /// included: an element pushed off the edge is drawn CLIPPED on a PS1,
        /// not moved, and a preview that quietly moves it hides the bug.
        /// </summary>
        public static RectInt Resolve(Baked b, Vector2 resolution)
        {
            int resW = Mathf.RoundToInt(resolution.x);
            int resH = Mathf.RoundToInt(resolution.y);

            int ax = (b.AnchorMinX * resW) >> 8;
            int ay = (b.AnchorMinY * resH) >> 8;
            int x = ax + b.X;
            int y = ay + b.Y;

            int w = b.W;
            int h = b.H;
            if (b.AnchorMaxX != b.AnchorMinX) w = ((b.AnchorMaxX * resW) >> 8) - ax + b.W;
            if (b.AnchorMaxY != b.AnchorMinY) h = ((b.AnchorMaxY * resH) >> 8) - ay + b.H;

            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (w <= 0) w = 1;
            if (h <= 0) h = 1;
            if (x + w > resW) w = resW - x;
            if (y + h > resH) h = resH - y;

            return new RectInt(x, y, w, h);
        }

        /// <summary>
        /// Bake and resolve in one step, for a RectTransform under a canvas of
        /// the given size. This is what a preview wants.
        /// </summary>
        public static RectInt ScreenRect(RectTransform rt, RectTransform canvasRect, Vector2 resolution)
        {
            float canvasW = canvasRect != null ? canvasRect.rect.width : resolution.x;
            float canvasH = canvasRect != null ? canvasRect.rect.height : resolution.y;
            if (canvasW <= 0) canvasW = resolution.x;
            if (canvasH <= 0) canvasH = resolution.y;
            return Resolve(Bake(rt, resolution.x / canvasW, resolution.y / canvasH), resolution);
        }
    }
}
