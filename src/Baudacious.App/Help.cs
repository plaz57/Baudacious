namespace Baudacious.App;

/// <summary>The Alt+Z overlay text, in one place so the window and the renderer agree.</summary>
/// <remarks>
/// Twenty-one lines and about fifty columns, which is what fits inside a default 80x25
/// window once the overlay's border is allowed for. The overlay clips rather than scrolls,
/// so a twenty-second line is read by nobody — every addition here has to displace
/// something.
/// </remarks>
public static class Help
{
    /// <summary>
    /// The overlay, naming the settings directory as the configuration spec requires.
    /// </summary>
    /// <param name="directory">
    /// Where <c>baudacious.ini</c> lives. Given as a directory rather than a full path: the
    /// file name is the same for everyone and only the directory carries information.
    /// </param>
    public static string[] For(string directory) =>
    [
        "  BAUDACIOUS",
        "",
        "  alt+B  line rate         alt+G  framing",
        "  alt+F  fast-forward      alt+shift+F  10x while held",
        "  alt+P  phosphor          alt+C  CRT pass",
        "  alt+N  new session       alt+S  next session",
        "  alt+- / alt+=  scale     alt+enter  fullscreen",
        "  alt+X  quit (press twice within three seconds)",
        "  ctrl+]  local command prompt (tab completes)",
        "",
        "  ctrl+C  interrupt, or copy when text is selected",
        "  ctrl+V or shift+insert  paste",
        "",
        "  drag select    dbl-click word   triple-click line",
        "  shift+drag extend, or select in a mouse-aware program",
        "  wheel scrolls history, or the program if it tracks",
        "  shift+pgup/pgdn page   shift+home/end oldest/live",
        "  B, G and P open a menu; the chord again steps it on.",
        "  Any other alt chord goes to the shell as ESC + key.",
        "",
        "  settings: " + directory,
        "  alt+Z or escape to close",
    ];
}
