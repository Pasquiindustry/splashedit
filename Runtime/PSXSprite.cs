using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Marks a sheet as used by this scene, so the exporter packs it into VRAM
    /// and lists it in the splashpack.
    /// </summary>
    /// <remarks>
    /// Sprite INSTANCES are created at runtime from Lua (Sprite.Create), not
    /// authored here. That is the opposite of the UI, and deliberately so: a game
    /// spawns and destroys sprites as players join, bullets fire and props die,
    /// and none of that is known at export time. What must be authored is the
    /// data - the sheets and their animations - because that is what has to be
    /// resident in VRAM.
    ///
    /// Put one of these anywhere in the scene for each sheet the scene uses.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("PSX/PSX Sprite Sheet Ref")]
    public class PSXSprite : MonoBehaviour
    {
        [Tooltip("The sheet this scene needs in VRAM. Referenced from Lua by its sheet name.")]
        [SerializeField] private PSXSpriteSheet sheet;

        public PSXSpriteSheet Sheet => sheet;
    }
}
