using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace SplashEdit.RuntimeCode
{

    /// <summary>
    /// The TextureQuantizer class provides methods to quantize a texture into a limited number of colors using K-Means clustering and Floyd-Steinberg dithering.
    /// </summary>
    public class TextureQuantizer
    {

        /// <summary>
        /// Represents the result of the quantization process, containing the indices of the quantized colors and the palette of unique colors.
        /// </summary>
        public struct QuantizedResult
        {
            /// <summary>
            /// The indices of the quantized colors in the texture.
            /// </summary>
            public int[,] Indices;

            /// <summary>
            /// The palette of unique colors used in the quantized texture.
            /// </summary>
            public List<Vector3> Palette;
        }

        /// <summary>
        /// A source pixel is transparent below this alpha. The PS1 has no alpha
        /// channel - a texel is either drawn or it is not - so a cutout has to
        /// pick a threshold somewhere, and halfway is the least surprising place.
        /// </summary>
        public const float CutoutAlphaThreshold = 0.5f;

        /// <summary>
        /// Quantizes the given texture into a limited number of colors and dithers it.
        /// </summary>
        /// <param name="texture">The texture to be quantized.</param>
        /// <param name="maxColors">The maximum number of colors allowed in the quantized texture.</param>
        /// <param name="cutout">
        /// Reserve palette index 0 for transparency and map every pixel with
        /// alpha below <see cref="CutoutAlphaThreshold"/> to it.
        ///
        /// Needed by anything with a hole in it - a sprite, an icon, a decal.
        /// Without it alpha is simply discarded (the colour channels of a fully
        /// transparent pixel are quantized like any other), and the result is a
        /// character in an opaque rectangle. Costs one palette entry: a 4-bit
        /// cutout gets 15 colours, not 16.
        /// </param>
        /// <returns>A QuantizedResult containing the indices of the quantized colors and the palette of unique colors.</returns>
        public static QuantizedResult Quantize(Texture2D texture, int maxColors, bool cutout = false)
        {
            int width = texture.width, height = texture.height;
            Color[] pixels = texture.GetPixels();
            int[,] indices = new int[width, height];

            if (cutout)
            {
                // Index 0 is the transparent one, so the opaque pixels share what
                // is left. Quantizing the transparent pixels' colours alongside
                // them would waste entries on pixels nobody will ever see, and
                // would drag the palette toward whatever colour the artist left
                // in the invisible background.
                return QuantizeCutout(pixels, width, height, maxColors);
            }

            List<Vector3> uniqueColors = pixels.Select(c => new Vector3(c.r, c.g, c.b)).Distinct().ToList();
            if (uniqueColors.Count <= maxColors) return ConvertToOutput(pixels, width, height);

            List<Vector3> palette = KMeans(uniqueColors, maxColors);
            KDTree kdTree = new KDTree(palette);


            // Floyd-Steinberg Dithering
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3 oldColor = new Vector3(pixels[y * width + x].r, pixels[y * width + x].g, pixels[y * width + x].b);
                    int nearestIndex = kdTree.FindNearestIndex(oldColor);
                    indices[x, y] = nearestIndex;

                    Vector3 error = oldColor - palette[nearestIndex];
                    PropagateError(pixels, width, height, x, y, error);
                }
            }


            return new QuantizedResult { Indices = indices, Palette = palette };
        }

        /// <summary>
        /// Quantize only the opaque pixels, into palette entries 1..maxColors-1.
        /// Entry 0 is left for the caller to write as the PS1's transparent
        /// colour (0x0000).
        /// </summary>
        private static QuantizedResult QuantizeCutout(Color[] pixels, int width, int height, int maxColors)
        {
            int[,] indices = new int[width, height];
            List<Vector3> palette = new List<Vector3> { Vector3.zero };  // [0] = transparent

            int opaqueBudget = maxColors - 1;
            List<Vector3> opaqueColors = pixels
                .Where(c => c.a >= CutoutAlphaThreshold)
                .Select(c => new Vector3(c.r, c.g, c.b))
                .Distinct()
                .ToList();

            if (opaqueColors.Count == 0)
            {
                // Entirely transparent. Legal, if pointless; do not divide by it.
                return new QuantizedResult { Indices = indices, Palette = palette };
            }

            bool exact = opaqueColors.Count <= opaqueBudget;
            palette.AddRange(exact ? opaqueColors : KMeans(opaqueColors, opaqueBudget));

            // Match against the opaque colours only. Including entry 0 would let
            // a dark pixel snap to "transparent" and punch a hole in the sprite.
            List<Vector3> opaquePalette = palette.GetRange(1, palette.Count - 1);
            KDTree kdTree = exact ? null : new KDTree(opaquePalette);
            Dictionary<Vector3, int> exactLookup = null;
            if (exact)
            {
                exactLookup = new Dictionary<Vector3, int>();
                for (int i = 0; i < opaquePalette.Count; i++) exactLookup[opaquePalette[i]] = i;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color src = pixels[y * width + x];
                    if (src.a < CutoutAlphaThreshold)
                    {
                        indices[x, y] = 0;
                        continue;
                    }

                    Vector3 oldColor = new Vector3(src.r, src.g, src.b);
                    int opaqueIndex = exact
                        ? exactLookup[oldColor]
                        : kdTree.FindNearestIndex(oldColor);
                    indices[x, y] = opaqueIndex + 1;  // shift past the transparent entry

                    if (!exact)
                    {
                        // Diffuse the error, but never into a transparent pixel:
                        // its colour is never drawn, so pushing error there just
                        // loses it.
                        Vector3 error = oldColor - opaquePalette[opaqueIndex];
                        PropagateError(pixels, width, height, x, y, error);
                    }
                }
            }

            return new QuantizedResult { Indices = indices, Palette = palette };
        }

        private static List<Vector3> KMeans(List<Vector3> colors, int k)
        {
            List<Vector3> centroids = Enumerable.Range(0, k).Select(i => colors[i * colors.Count / k]).ToList();

            List<List<Vector3>> clusters;
            for (int i = 0; i < 10; i++) // Fixed iteration count
            {
                clusters = Enumerable.Range(0, k).Select(_ => new List<Vector3>()).ToList();
                foreach (Vector3 color in colors)
                {
                    int closest = centroids.Select((c, index) => (index, Vector3.SqrMagnitude(c - color)))
                                           .OrderBy(t => t.Item2).First().index;
                    clusters[closest].Add(color);
                }

                for (int j = 0; j < k; j++)
                {
                    if (clusters[j].Count > 0)
                        centroids[j] = clusters[j].Aggregate(Vector3.zero, (acc, c) => acc + c) / clusters[j].Count;
                }
            }
            return centroids;
        }

        private static void PropagateError(Color[] pixels, int width, int height, int x, int y, Vector3 error)
        {
            void AddError(int dx, int dy, float factor)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    int index = ny * width + nx;
                    pixels[index].r += error.x * factor;
                    pixels[index].g += error.y * factor;
                    pixels[index].b += error.z * factor;
                }
            }
            AddError(1, 0, 7f / 16f);
            AddError(-1, 1, 3f / 16f);
            AddError(0, 1, 5f / 16f);
            AddError(1, 1, 1f / 16f);
        }

        private static QuantizedResult ConvertToOutput(Color[] pixels, int width, int height)
        {
            int[,] indices = new int[width, height];
            List<Vector3> palette = new List<Vector3>();
            Dictionary<Vector3, int> colorToIndex = new Dictionary<Vector3, int>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3 color = new Vector3(pixels[y * width + x].r, pixels[y * width + x].g, pixels[y * width + x].b);
                    if (!colorToIndex.ContainsKey(color))
                    {
                        colorToIndex[color] = palette.Count;
                        palette.Add(color);
                    }
                    indices[x, y] = colorToIndex[color];
                }
            }

            return new QuantizedResult { Indices = indices, Palette = palette };
        }
    }

    public class KDTree
    {
        private class Node
        {
            public Vector3 Point;
            public int Index;
            public Node Left, Right;
        }

        private Node root;

        public KDTree(List<Vector3> points)
        {
            var indexed = new List<(Vector3 point, int index)>();
            for (int i = 0; i < points.Count; i++)
                indexed.Add((points[i], i));
            root = Build(indexed, 0);
        }

        private Node Build(List<(Vector3 point, int index)> items, int depth)
        {
            if (items.Count == 0) return null;

            int axis = depth % 3;
            items.Sort((a, b) => a.point[axis].CompareTo(b.point[axis]));
            int median = items.Count / 2;

            return new Node
            {
                Point = items[median].point,
                Index = items[median].index,
                Left = Build(items.Take(median).ToList(), depth + 1),
                Right = Build(items.Skip(median + 1).ToList(), depth + 1)
            };
        }

        public int FindNearestIndex(Vector3 target)
        {
            return FindNearest(root, target, 0, root).Index;
        }

        private Node FindNearest(Node node, Vector3 target, int depth, Node best)
        {
            if (node == null) return best;

            if (Vector3.SqrMagnitude(target - node.Point) < Vector3.SqrMagnitude(target - best.Point))
                best = node;

            int axis = depth % 3;
            Node first = target[axis] < node.Point[axis] ? node.Left : node.Right;
            Node second = first == node.Left ? node.Right : node.Left;

            best = FindNearest(first, target, depth + 1, best);
            if (Mathf.Pow(target[axis] - node.Point[axis], 2) < Vector3.SqrMagnitude(target - best.Point))
                best = FindNearest(second, target, depth + 1, best);

            return best;
        }
    }
}
