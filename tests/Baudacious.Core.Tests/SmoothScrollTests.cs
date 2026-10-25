using System.Text;
using Baudacious.Core.Commands;
using Baudacious.Core.Terminal;
using Baudacious.Render;

namespace Baudacious.Core.Tests;

/// <summary>
/// The arithmetic behind the VT220 smooth scroll, and the mode that asks for it.
/// </summary>
/// <remarks>
/// There is no GL here and there does not need to be. What can go wrong is the debt: a
/// residue that never quite reaches zero would leave an idle screen permanently a fraction
/// of a pixel out of place, and a debt allowed past one cell would ask the renderer to draw
/// a row it does not have.
/// </remarks>
public class SmoothScrollTests
{
    private const int CellHeight = 16;

    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

    [Fact]
    public void AStillScreenHasNoOffset()
    {
        var scroll = new SmoothScroll();

        Assert.Equal(0, scroll.Offset);
        Assert.False(scroll.IsAnimating);

        scroll.Advance(Frame, 20, CellHeight);

        Assert.Equal(0, scroll.Offset);
        Assert.False(scroll.IsAnimating);
    }

    [Fact]
    public void OneLinePutsTheDisplayOneCellBehind()
    {
        var scroll = new SmoothScroll();
        scroll.Scrolled(1, CellHeight);

        Assert.Equal(CellHeight, scroll.Offset);
        Assert.True(scroll.IsAnimating);
    }

    [Fact]
    public void TheDebtDrainsAtTheConfiguredRate()
    {
        var scroll = new SmoothScroll();
        scroll.Scrolled(1, CellHeight);

        // 20 lines a second at 60 Hz is a third of a cell per frame, so three frames.
        scroll.Advance(Frame, 20, CellHeight);
        Assert.True(scroll.Offset > 0);
        scroll.Advance(Frame, 20, CellHeight);
        Assert.True(scroll.Offset > 0);
        scroll.Advance(Frame, 20, CellHeight);

        Assert.Equal(0, scroll.Offset);
        Assert.False(scroll.IsAnimating);
    }

    [Fact]
    public void TheOriginalRateTakesTenFramesALine()
    {
        var scroll = new SmoothScroll();
        scroll.Scrolled(1, CellHeight);

        var frames = 0;
        while (scroll.IsAnimating && frames < 100)
        {
            scroll.Advance(Frame, SmoothScrollRate.Period, CellHeight);
            frames++;
        }

        // Two scan lines per refresh on a 16-pixel cell. This is the number that made
        // everybody turn the feature off.
        Assert.Equal(10, frames);
    }

    [Fact]
    public void TheDebtNeverExceedsOneCell()
    {
        var scroll = new SmoothScroll();

        for (var i = 0; i < 10; i++)
        {
            scroll.Scrolled(1, CellHeight);
            Assert.True(scroll.Offset <= CellHeight);
        }

        scroll.Scrolled(500, CellHeight);

        // One overscan row is one row: anything more has nothing to draw in the gap, so
        // output that outruns the animation makes it snap rather than fall behind.
        Assert.Equal(CellHeight, scroll.Offset);
    }

    [Fact]
    public void TheOffsetReachesExactlyZero()
    {
        var scroll = new SmoothScroll();
        scroll.Scrolled(1, CellHeight);

        // A rate that does not divide the cell evenly is the case where a residue would
        // survive, and a screen permanently a fraction of a pixel out of place is not the
        // same picture as one rendered with the feature off.
        for (var i = 0; i < 1000 && scroll.IsAnimating; i++)
        {
            scroll.Advance(Frame, 7.3, CellHeight);
        }

        Assert.Equal(0, scroll.Offset);
    }

    [Fact]
    public void RateZeroIsTheJumpScroll()
    {
        var scroll = new SmoothScroll();
        scroll.Scrolled(1, CellHeight);
        scroll.Advance(Frame, SmoothScrollRate.Off, CellHeight);

        Assert.Equal(0, scroll.Offset);
        Assert.False(scroll.IsAnimating);
    }

    [Fact]
    public void ResetAbandonsWhateverWasInProgress()
    {
        var scroll = new SmoothScroll();
        scroll.Scrolled(1, CellHeight);
        scroll.Reset();

        Assert.Equal(0, scroll.Offset);
    }

    [Fact]
    public void AFullCellBehindShowsThePreScrollScreen()
    {
        // The grid is emitted one row lower so the departing line has somewhere to be.
        // Lifting it by nothing leaves it exactly one row down, which is where it was
        // before the line arrived.
        Assert.Equal(0, SmoothScroll.LiftFor(CellHeight, CellHeight));
    }

    [Fact]
    public void SettledLiftsTheWholeRowBack()
    {
        Assert.Equal(CellHeight, SmoothScroll.LiftFor(0, CellHeight));
    }

    [Fact]
    public void TheImageRisesAsTheDebtDrains()
    {
        // The direction. With the sign reversed the scroll runs downwards and ends with a
        // jump, and a still taken halfway through looks the same either way.
        var scroll = new SmoothScroll();
        scroll.Scrolled(1, CellHeight);

        var previous = SmoothScroll.LiftFor(scroll.Offset, CellHeight);

        while (scroll.IsAnimating)
        {
            scroll.Advance(Frame, 20, CellHeight);

            var lift = SmoothScroll.LiftFor(scroll.Offset, CellHeight);
            Assert.True(lift > previous, $"lift went {previous} -> {lift}");
            previous = lift;
        }

        Assert.Equal(CellHeight, previous);
    }

    // ---- DECSCLM -------------------------------------------------------------

    private static (TerminalEmulator Emulator, VtParser Parser) Vt()
    {
        var emulator = new TerminalEmulator(new Screen(20, 6), new TerminalTests.RecordingHost());
        return (emulator, new VtParser(emulator));
    }

    private static void Feed(VtParser parser, string text) =>
        parser.Consume(Encoding.UTF8.GetBytes(text.Replace("ESC", "\u001b", StringComparison.Ordinal)));

    [Fact]
    public void SmoothScrollIsTheDefaultMode()
    {
        var (emulator, _) = Vt();

        // The opposite of the hardware, because here the rate is the opt-in and the mode
        // is only a program's veto over it.
        Assert.True(emulator.SmoothScrollMode);
    }

    [Fact]
    public void JumpScrollCanBeDemandedAndWithdrawn()
    {
        var (emulator, parser) = Vt();

        Feed(parser, "ESC[?4l");
        Assert.False(emulator.SmoothScrollMode);

        Feed(parser, "ESC[?4h");
        Assert.True(emulator.SmoothScrollMode);
    }

    [Fact]
    public void TheSgrFourIsStillUnderline()
    {
        var (emulator, parser) = Vt();

        Feed(parser, "ESC[?4l");
        Feed(parser, "ESC[4m");

        Assert.True(emulator.Screen.Pen.Flags.HasFlag(CellFlags.Underline));

        // Without the private marker this is an attribute, not a mode. Getting these two
        // confused would make underlined text change how the screen scrolls.
        Assert.False(emulator.SmoothScrollMode);
    }

    [Theory]
    [InlineData("ESC[!p")]
    [InlineData("ESCc")]
    public void AResetWithdrawsTheDemand(string sequence)
    {
        var (emulator, parser) = Vt();

        Feed(parser, "ESC[?4l");
        Feed(parser, sequence);

        Assert.True(emulator.SmoothScrollMode);
    }
}
