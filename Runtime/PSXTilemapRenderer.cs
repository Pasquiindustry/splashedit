using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Drop one of these on a GameObject in a scene to say "this scene has a
    /// tilemap." The exporter finds it, packs the tileset into the VRAM atlas
    /// alongside the sprites, and writes the map into the splashpack (v23).
    /// </summary>
    /// <remarks>
    /// A scene may have at most one. Position is ignored: the map's origin is
    /// tile (0,0) at world (0,0), which is the same space the sprite system and
    /// the actors move in, so a bound avatar and the floor it stands on line up
    /// for free.
    /// </remarks>
    [AddComponentMenu("PSXSplash/PSX Tilemap Renderer")]
    public class PSXTilemapRenderer : MonoBehaviour
    {
        [Tooltip("The tile map asset this scene draws. Author it with " +
                 "PlayStation 1 > Tile Painter.")]
        public PSXTilemap tilemap;
    }
}
