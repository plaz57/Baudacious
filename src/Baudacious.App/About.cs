using System.Globalization;
using Baudacious.Render;

namespace Baudacious.App;

/// <summary>
/// What Baudacious is, and whose work is in it.
/// </summary>
/// <remarks>
/// Of the three bundled faces only Spleen's licence actually requires its notice to be
/// reproduced. The other two are here because the credit is deserved, not because it is
/// owed — see <c>assets/README.md</c>, which also records the trap: unscii's <c>-full</c>
/// variant incorporates GNU Unifont and is GPL, so it would reach the whole application.
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
        "  Typefaces, all 8x16, chosen with 'font <name>':",
        .. FontCatalog.Faces.Select(Describe),
        "",
        "  Phosphor curves after P1 green, P3 amber and P4 white.",
        "  Framing arithmetic after the UARTs that enforced it.",
        "",
        "  settings: " + Paths.Shorten(settingsDirectory, 42),
        "",
        "  escape to close",
    ];

    /// <summary>
    /// One face, credited, with the fact that decides between them.
    /// </summary>
    /// <remarks>
    /// Stem width, because that is the whole reason there is a choice: two pixels doubles
    /// again at 2x magnification, and four pixels of stem is what "too bold" means at this
    /// size.
    /// </remarks>
    private static string Describe(BitmapFace face) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"    {face.Name,-9} {face.Stem}px stems, {face.Credit}");

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
