using System.Globalization;
using System.Reflection;

namespace Baudacious.App;

/// <summary>
/// What Baudacious is, and whose work is in it.
/// </summary>
/// <remarks>
/// unscii-16 is public domain and so carries no attribution requirement. The credit is here
/// because it is deserved, not because it is owed — see <c>assets/README.md</c>, which also
/// records the trap: the <c>-full</c> variant incorporates GNU Unifont and is GPL, so it
/// would reach the whole application.
/// </remarks>
public static class About
{
    /// <summary>The about panel, sized like the help overlay.</summary>
    public static string[] For(string settingsDirectory) =>
    [
        "  BAUDACIOUS " + Version(),
        "  a terminal with a baud rate",
        "",
        "  MIT licensed. Copyright (c) 2026 Patrick Plaisted.",
        "",
        "  Typeface: unscii-16 by Viznut, public domain.",
        "    viznut.fi/unscii",
        "    250 of the 256 CP437 cells come from it as drawn;",
        "    two more are derived from glyphs it does have, and",
        "    two fall back to a thresholded system face.",
        "",
        "  Phosphor curves after P1 green, P3 amber and P4 white.",
        "  Framing arithmetic after the UARTs that enforced it.",
        "",
        "  settings: " + Paths.Shorten(settingsDirectory, 42),
        "",
        "  escape to close",
    ];

    /// <summary>
    /// The assembly version, without the build metadata nobody reads aloud.
    /// </summary>
    private static string Version()
    {
        var version = typeof(About).Assembly.GetName().Version;

        return version is null
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}");
    }
}
