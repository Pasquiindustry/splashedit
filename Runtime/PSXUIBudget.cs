namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// The runtime's UI pool sizes, mirrored here so authoring can be checked
    /// before a disc is built.
    /// </summary>
    /// <remarks>
    /// These MUST match <c>UI_MAX_ELEMENTS</c> / <c>UI_MAX_CANVASES</c> /
    /// <c>UI_TEXT_BUF</c> in psxsplash/src/uisystem.hh.
    ///
    /// The overflow behaviour is what makes a mirrored constant worth keeping:
    /// <c>loadFromSplashpack</c> CLAMPS a canvas's element count against the
    /// remaining pool and carries on. Nothing is logged, nothing asserts - the
    /// elements past the cap simply do not exist, and from the Lua side
    /// <c>UI.FindElement</c> returns -1 for them. Half a panel goes missing and
    /// the only clue is which half.
    /// </remarks>
    public static class PSXUIBudget
    {
        /// <summary>Total elements across every canvas in one scene.</summary>
        public const int MaxElements = 256;

        /// <summary>Canvases in one scene.</summary>
        public const int MaxCanvases = 24;

        /// <summary>Characters of an element's text buffer, null terminator included.</summary>
        public const int TextBuffer = 64;

        /// <summary>Longest string <c>UI.SetText</c> can hold.</summary>
        public const int MaxTextLength = TextBuffer - 1;

        /// <summary>Longest canvas or element name the splashpack stores.</summary>
        public const int MaxNameLength = 24;
    }
}
