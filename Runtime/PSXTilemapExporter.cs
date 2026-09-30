using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>One addressable object lifted out of the map, ready for the writer.</summary>
    public class PSXTileObjectData
    {
        public byte Kind;
        public byte Id;
        public ushort TileX;
        public ushort TileY;
    }

    /// <summary>Flattened tilemap, ready for the binary writer (splashpack v23).</summary>
    public class PSXTilemapData
    {
        public ushort Width;
        public ushort Height;
        public byte TileW;
        public byte TileH;
        public byte TilesetSheet;             // index into the sprite sheet table
        public byte[] Cells;                  // 2 bytes per cell: tile, flags (row-major)
        public List<PSXTileObjectData> Objects = new List<PSXTileObjectData>();
    }

    /// <summary>
    /// Collects the scene's tilemap and flattens it for export. Kept separate from
    /// the sprite exporter, but its tileset joins the sprite sheet table so the
    /// engine can resolve it by index - see <see cref="CollectTileset"/>.
    /// </summary>
    public static class PSXTilemapExporter
    {
        public const int MaxObjects = 128;  // must match TILE_MAX_OBJECTS in tilesystem.hh
        private const byte TileEmpty = 0xFF;
        private const byte FlagWalkable = 0x01;

        /// <summary>The scene's tilemap renderer, or null. Errors if there are two.</summary>
        public static PSXTilemapRenderer FindRenderer()
        {
            var renderers = Object.FindObjectsByType<PSXTilemapRenderer>(FindObjectsSortMode.None);
            if (renderers.Length == 0) return null;
            if (renderers.Length > 1)
                throw new System.InvalidOperationException(
                    "Tilemap export failed: a scene may have at most one PSXTilemapRenderer, " +
                    $"but this scene has {renderers.Length}.");
            return renderers[0];
        }

        /// <summary>
        /// The tileset the scene's tilemap needs packed into the VRAM atlas, or
        /// null if the scene has no tilemap. The sprite collector calls this so
        /// the tileset is packed and indexed like any other sheet.
        /// </summary>
        public static PSXSpriteSheet CollectTileset()
        {
            var r = FindRenderer();
            return (r != null && r.tilemap != null) ? r.tilemap.Tileset : null;
        }

        /// <summary>
        /// Flatten the scene's tilemap. Returns null when there is no tilemap.
        /// <paramref name="sheetList"/> is the already-collected sheet list, in the
        /// exact order they are written, so the tileset's index is stable.
        /// </summary>
        public static PSXTilemapData Flatten(List<PSXSpriteSheet> sheetList)
        {
            var r = FindRenderer();
            if (r == null || r.tilemap == null) return null;

            PSXTilemap map = r.tilemap;

            var problems = map.Validate();
            if (problems.Count > 0)
                throw new System.InvalidOperationException(
                    "Tilemap export failed:\n  " + string.Join("\n  ", problems));

            int sheetIndex = sheetList != null ? sheetList.IndexOf(map.Tileset) : -1;
            if (sheetIndex < 0)
                throw new System.InvalidOperationException(
                    $"Tilemap export failed: tileset '{(map.Tileset != null ? map.Tileset.SheetName : "<none>")}' " +
                    "was not packed into the sprite sheet table. This is an exporter wiring bug - " +
                    "PSXTilemapExporter.CollectTileset() must run before sheet packing.");

            map.EnsureCells();
            int w = map.Width, h = map.Height;

            var data = new PSXTilemapData
            {
                Width = (ushort)w,
                Height = (ushort)h,
                TileW = (byte)Mathf.Clamp(map.TileWidth, 1, 255),
                TileH = (byte)Mathf.Clamp(map.TileHeight, 1, 255),
                TilesetSheet = (byte)sheetIndex,
                Cells = new byte[w * h * 2],
            };

            // Auto-numbering per kind, in reading order, so the first tile of a
            // kind is id 1 - predictable and needing no manual ids. Keyed by the
            // opaque kind byte; the exporter never interprets what it means.
            var nextId = new Dictionary<int, int>();

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    PSXTileBrush b = map.BrushAt(x, y);
                    int ci = (y * w + x) * 2;

                    if (b == null || b.tilesetCell < 0)
                    {
                        data.Cells[ci] = TileEmpty;
                        data.Cells[ci + 1] = 0;
                        continue;
                    }

                    data.Cells[ci] = (byte)Mathf.Clamp(b.tilesetCell, 0, 254);
                    data.Cells[ci + 1] = (byte)(b.walkable ? FlagWalkable : 0);

                    // Kind 0 is a plain tile; anything else is an addressable object.
                    if (b.objectKind != 0)
                    {
                        int kind = Mathf.Clamp(b.objectKind, 0, 255);
                        int id;
                        if (b.objectId >= 0)
                        {
                            id = b.objectId;
                        }
                        else
                        {
                            if (!nextId.TryGetValue(kind, out int n)) n = 1;
                            id = n;
                            nextId[kind] = n + 1;
                        }

                        data.Objects.Add(new PSXTileObjectData
                        {
                            Kind = (byte)kind,
                            Id = (byte)Mathf.Clamp(id, 0, 255),
                            TileX = (ushort)x,
                            TileY = (ushort)y,
                        });
                    }
                }
            }

            if (data.Objects.Count > MaxObjects)
                throw new System.InvalidOperationException(
                    $"Tilemap export failed: {data.Objects.Count} addressable object tiles, " +
                    $"but the runtime pool holds {MaxObjects}. Reduce task/vent/etc. tiles.");

            return data;
        }
    }
}
