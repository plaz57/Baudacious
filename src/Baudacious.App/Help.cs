namespace Baudacious.App;

/// <summary>The Alt+Z overlay text, in one place so the window and the renderer agree.</summary>
/// <remarks>
/// Kept to about twenty lines and fifty columns so it fits inside a default 80x25 window
/// with its border. The overlay clips rather than scrolls, so anything longer is simply not
/// read.
/// </remarks>
public static class Help
{
    public static readonly string[] Lines =
    [
        "  BAUDACIOUS",
        "",
        "  alt+B  line rate         alt+G  framing",
        "  alt+F  fast-forward      alt+shift+F  10x while held",
        "  alt+P  phosphor          alt+C  CRT pass",
        "  alt+N  new session       alt+S  next session",
        "  alt+- / alt+=  scale     alt+Z  this help",
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
        "",
        "  Any other alt chord goes to the shell as ESC + key.",
        "",
        "  alt+Z or escape to close",
    ];
}
