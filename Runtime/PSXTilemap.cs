using System;
using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// One paintable tile "type": the graphic to draw, whether you can stand on
    /// it, and an optional opaque gameplay tag. The whole point of a palette is
    /// that you decide what a "wall" or a "vent" is ONCE, then paint it everywhere
    /// - no per-cell property editing, which is the thing that makes tile editors
    /// miserable.
    /// </summary>
    [Serializable]
    public class PSXTileBrush
    {
        [Tooltip("Shown in the palette. Purely for your own sanity while painting.")]
        public string name = "floor";

        [Tooltip("Which cell of the tileset to draw, counting left-to-right then " +
                 "top-to-bottom from 0. -1 draws nothing (a hole in the floor).")]
        public int tilesetCell = 0;

        [Tooltip("Can a player stand here? Off = solid wall.")]
        public bool walkable = true;

        [Tooltip("Opaque gameplay tag. 0 = a plain floor/wall (not an object). Any " +
                 "other value makes this an addressable object the game can find by " +
                 "kind+id. The ENGINE attaches no meaning to it - your game does. " +
                 "Name the values in the Tilemap's Object Kind Names list.")]
        public int objectKind = 0;

        [Tooltip("Object id for kinds that need a specific one. Leave -1 and the " +
                 "exporter numbers each kind for you in reading order (1, 2, 3...).")]
        public int objectId = -1;

        [Tooltip("Editor tint so this brush is easy to tell apart on the palette " +
                 "and (faintly) on the map. Not exported.")]
        public Color editorColor = Color.white;
    }

    /// <summary>
    /// A hand-painted tile map: one tileset, a grid of cells, and the palette of
    /// brushes the cells refer to. Each cell stores a brush INDEX (or -1 for
    /// empty), so changing a brush's walkability or graphic updates every cell
    /// that uses it at once.
    /// </summary>
    /// <remarks>
    /// Exported as splashpack v23. The tileset is a <see cref="PSXSpriteSheet"/>,
    /// so its pixels ride the very same VRAM atlas as UI, 3D textures and sprites
    /// - a tilemap costs exactly its cells (2 bytes each) plus its objects.
    /// </remarks>
    [CreateAssetMenu(fileName = "Tilemap", menuName = "PSXSplash/Tilemap")]
    public class PSXTilemap : ScriptableObject
    {
        [Tooltip("The tileset. Its cell grid IS the tile grid - tile size comes " +
                 "from the sheet's Cell Width/Height.")]
        [SerializeField] private PSXSpriteSheet tileset;

        [Tooltip("Map width in tiles.")]
        [SerializeField] private int width = 32;

        [Tooltip("Map height in tiles.")]
        [SerializeField] private int height = 32;

        [Tooltip("The paintable tile types.")]
        [SerializeField] private List<PSXTileBrush> brushes = new List<PSXTileBrush>();

        [Tooltip("Friendly names for object-kind values, purely for authoring. " +
                 "Index = the kind value: element 0 is 'none', element 1 is what " +
                 "kind 1 means in YOUR game, and so on. The engine never sees these " +
                 "- they only label the dropdown in the painter.")]
        [SerializeField] private List<string> objectKindNames = new List<string> { "(none)" };

        /// <summary>Flat row-major grid; each entry is a brush index or -1 (empty).</summary>
        [SerializeField] private int[] cells = new int[0];

        public PSXSpriteSheet Tileset => tileset;
        public int Width => Mathf.Max(1, width);
        public int Height => Mathf.Max(1, height);
        public List<PSXTileBrush> Brushes => brushes;

        public int TileWidth => tileset != null ? tileset.CellWidth : 16;
        public int TileHeight => tileset != null ? tileset.CellHeight : 16;

        public List<string> ObjectKindNames => objectKindNames;

        /// <summary>Friendly label for a kind value, or the number if unnamed.</summary>
        public string KindLabel(int kind)
        {
            if (objectKindNames != null && kind >= 0 && kind < objectKindNames.Count)
                return objectKindNames[kind];
            return kind == 0 ? "(none)" : "kind " + kind;
        }

        /// <summary>Brush index at (x, y), or -1 for an empty or out-of-range cell.</summary>
        public int GetCell(int x, int y)
        {
            EnsureCells();
            if (x < 0 || y < 0 || x >= Width || y >= Height) return -1;
            return cells[y * Width + x];
        }

        /// <summary>Paint (x, y) with a brush index (or -1 to erase).</summary>
        public void SetCell(int x, int y, int brushIndex)
        {
            EnsureCells();
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            cells[y * Width + x] = brushIndex;
        }

        public PSXTileBrush BrushAt(int x, int y)
        {
            int b = GetCell(x, y);
            return (b >= 0 && b < brushes.Count) ? brushes[b] : null;
        }

        /// <summary>Resize the grid, preserving the overlapping region.</summary>
        public void Resize(int newWidth, int newHeight)
        {
            newWidth = Mathf.Clamp(newWidth, 1, 256);
            newHeight = Mathf.Clamp(newHeight, 1, 256);
            EnsureCells();

            var next = new int[newWidth * newHeight];
            for (int i = 0; i < next.Length; i++) next[i] = -1;
            int copyW = Mathf.Min(Width, newWidth);
            int copyH = Mathf.Min(Height, newHeight);
            for (int y = 0; y < copyH; y++)
                for (int x = 0; x < copyW; x++)
                    next[y * newWidth + x] = cells[y * Width + x];

            cells = next;
            width = newWidth;
            height = newHeight;
        }

        /// <summary>Make sure <see cref="cells"/> matches width*height; fill new cells empty.</summary>
        public void EnsureCells()
        {
            int need = Width * Height;
            if (cells != null && cells.Length == need) return;

            var next = new int[need];
            for (int i = 0; i < need; i++) next[i] = -1;
            if (cells != null)
            {
                int n = Mathf.Min(cells.Length, need);
                Array.Copy(cells, next, n);
            }
            cells = next;
        }

        /// <summary>
        /// Everything wrong with this map, in words a person can act on. The
        /// inspector shows it and the exporter refuses to ship it.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();

            if (tileset == null)
            {
                problems.Add("No tileset assigned - there is nothing to draw the tiles from.");
            }
            else
            {
                int cellCount = tileset.CellCount;
                for (int i = 0; i < brushes.Count; i++)
                {
                    var b = brushes[i];
                    if (b.tilesetCell >= cellCount)
                        problems.Add($"Brush '{b.name}' points at tileset cell {b.tilesetCell}, " +
                                     $"but the tileset only has {cellCount} (0..{cellCount - 1}).");
                    if (b.tilesetCell < -1)
                        problems.Add($"Brush '{b.name}' has a negative tileset cell that isn't -1.");
                }
            }

            if (Width > 256 || Height > 256)
                problems.Add($"Map is {Width}x{Height}; each side must be 256 or less (the runtime uses 16-bit coords, but this cap keeps a map's cells under 128 KB).");

            // Every painted cell must reference a real brush.
            EnsureCells();
            int badRefs = 0;
            foreach (int c in cells)
                if (c >= brushes.Count) badRefs++;
            if (badRefs > 0)
                problems.Add($"{badRefs} cell(s) reference a brush that no longer exists - repaint or remove them.");

            return problems;
        }
    }
}
