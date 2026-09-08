using Baudacious.App;

namespace Baudacious.Core.Tests;

/// <summary>
/// The help overlay has to fit the smallest window Baudacious claims to support.
/// </summary>
/// <remarks>
/// The overlay clips rather than scrolls, and it clips from the bottom — so an overlay one
/// line too tall loses the line that says how to close it, which is the worst line to lose
/// and the least likely to be noticed. It had grown to twenty-two lines and was doing
/// exactly that before this test existed.
/// </remarks>
public class HelpFitsTests
{
    /// <summary>The smallest grid the renderer and the README both promise.</summary>
    private const int MinimumRows = 25;

    private const int MinimumColumns = 80;

    /// <summary>
    /// What the overlay reserves for its own border, top and bottom.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>TextRenderer.AppendOverlay</c>, where the panel is
    /// <c>Math.Min(rows - 2, lines + 2)</c> tall: two rows for the border, and the panel
    /// itself inset by one row top and bottom.
    /// </remarks>
    private const int Chrome = 4;

    [Fact]
    public void EveryLineIsDrawnAtTheSmallestSupportedSize()
    {
        var lines = Help.For(@"C:\Users\someone\AppData\Roaming\Baudacious");

        Assert.True(
            lines.Length <= MinimumRows - Chrome,
            $"the help overlay is {lines.Length} lines and only {MinimumRows - Chrome} are drawn "
            + $"in an {MinimumColumns}x{MinimumRows} window, so the last "
            + $"{lines.Length - (MinimumRows - Chrome)} would be clipped — including how to close it");
    }

    [Fact]
    public void NoLineIsWiderThanTheSmallestSupportedSize()
    {
        var lines = Help.For(@"C:\Users\someone\AppData\Roaming\Baudacious");
        var widest = lines.Max(l => l.Length);

        // The panel is inset two columns and clips horizontally as well.
        Assert.True(
            widest <= MinimumColumns - 4,
            $"the widest help line is {widest} columns, past the {MinimumColumns - 4} that fit");
    }

    [Fact]
    public void TheClosingInstructionIsTheLastLine()
    {
        // Which is why the height matters: this is what a too-tall overlay loses.
        var lines = Help.For("anywhere");

        Assert.Contains("escape", lines[^1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALongPathIsShortenedRatherThanAllowedToOverflow()
    {
        var lines = Help.For(new string('x', 300));

        Assert.True(lines.Max(l => l.Length) <= MinimumColumns - 4);
    }
}
