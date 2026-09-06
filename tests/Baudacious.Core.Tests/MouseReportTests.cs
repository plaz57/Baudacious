using System.Text;
using Baudacious.Core.Terminal;

namespace Baudacious.Core.Tests;

public class MouseModeTests
{
    private static (TerminalEmulator Emulator, VtParser Parser) Build()
    {
        var screen = new Screen(80, 24);
        var emulator = new TerminalEmulator(screen, NullTerminalHost.Instance);
        return (emulator, new VtParser(emulator));
    }

    private static void Feed(VtParser parser, string text) =>
        parser.Consume(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void TheMouseBelongsToTheTerminalUntilAskedFor()
    {
        var (emulator, _) = Build();
        Assert.Equal(MouseTracking.Off, emulator.MouseTracking);
        Assert.False(emulator.MouseReporting);
    }

    [Theory]
    [InlineData(1000, MouseTracking.Click)]
    [InlineData(1002, MouseTracking.Drag)]
    [InlineData(1003, MouseTracking.Motion)]
    public void EachTrackingModeIsRecognized(int mode, MouseTracking expected)
    {
        var (emulator, parser) = Build();
        Feed(parser, $"\u001b[?{mode}h");

        Assert.Equal(expected, emulator.MouseTracking);

        Feed(parser, $"\u001b[?{mode}l");
        Assert.Equal(MouseTracking.Off, emulator.MouseTracking);
    }

    [Fact]
    public void EncodingIsIndependentOfTrackingLevel()
    {
        // A program turns on 1002 to say which events it wants and 1006 to ask for the form
        // that is not limited to 223 columns. Conflating them loses one or the other.
        var (emulator, parser) = Build();
        Feed(parser, "\u001b[?1002h[?1006h");

        Assert.Equal(MouseTracking.Drag, emulator.MouseTracking);
        Assert.Equal(MouseEncoding.Sgr, emulator.MouseEncoding);

        Feed(parser, "\u001b[?1002l");
        Assert.Equal(MouseTracking.Off, emulator.MouseTracking);
        Assert.Equal(MouseEncoding.Sgr, emulator.MouseEncoding);
    }

    [Fact]
    public void ASoftResetGivesTheMouseBack()
    {
        var (emulator, parser) = Build();
        Feed(parser, "\u001b[?1003h[?1006h");
        Feed(parser, "\u001b[!p");

        Assert.Equal(MouseTracking.Off, emulator.MouseTracking);
        Assert.Equal(MouseEncoding.Legacy, emulator.MouseEncoding);
    }
}

public class MouseReportTests
{
    [Fact]
    public void SgrReportsCarryPositionAndButton()
    {
        var report = MouseReport.Encode(
            MouseTracking.Click, MouseEncoding.Sgr, MouseButton.Left,
            pressed: true, motion: false, column: 9, row: 4);

        // One-based on the wire, zero-based in the call.
        Assert.Equal("\u001b[<0;10;5M", report);
    }

    [Fact]
    public void SgrTellsPressAndReleaseApart()
    {
        var press = MouseReport.Encode(
            MouseTracking.Click, MouseEncoding.Sgr, MouseButton.Right,
            pressed: true, motion: false, column: 0, row: 0);
        var release = MouseReport.Encode(
            MouseTracking.Click, MouseEncoding.Sgr, MouseButton.Right,
            pressed: false, motion: false, column: 0, row: 0);

        Assert.EndsWith("M", press, StringComparison.Ordinal);
        Assert.EndsWith("m", release, StringComparison.Ordinal);
        Assert.StartsWith("\u001b[<2;", press, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyReportsBiasEveryFieldByThirtyTwo()
    {
        var report = MouseReport.Encode(
            MouseTracking.Click, MouseEncoding.Legacy, MouseButton.Left,
            pressed: true, motion: false, column: 0, row: 0);

        // Button 0 with one-based coordinates 1,1, each biased by 32: space, then two "!".
        Assert.Equal("\u001b[M !!", report);
        Assert.Equal('\u001b', report![0]);
    }

    [Fact]
    public void LegacyCannotExpressPositionsPastItsCeiling()
    {
        // Every field is a single byte plus 32, so column 224 has nowhere to go. Returning
        // nothing is better than sending a wrapped coordinate that points somewhere else.
        var report = MouseReport.Encode(
            MouseTracking.Click, MouseEncoding.Legacy, MouseButton.Left,
            pressed: true, motion: false, column: 300, row: 0);

        Assert.Null(report);
    }

    [Fact]
    public void LegacyReleaseIsAlwaysButtonThree()
    {
        // The old form has no room to say which button came up.
        var report = MouseReport.Encode(
            MouseTracking.Click, MouseEncoding.Legacy, MouseButton.Right,
            pressed: false, motion: false, column: 0, row: 0);

        Assert.Equal((char)(3 + 32), report![3]);
    }

    [Fact]
    public void ClickTrackingDoesNotReportMotion()
    {
        var report = MouseReport.Encode(
            MouseTracking.Click, MouseEncoding.Sgr, MouseButton.Left,
            pressed: true, motion: true, column: 5, row: 5);

        Assert.Null(report);
    }

    [Fact]
    public void DragTrackingDoesReportMotion()
    {
        var report = MouseReport.Encode(
            MouseTracking.Drag, MouseEncoding.Sgr, MouseButton.Left,
            pressed: true, motion: true, column: 5, row: 5);

        // Motion adds 32 to the button code.
        Assert.Equal("\u001b[<32;6;6M", report);
    }

    [Fact]
    public void NothingIsReportedWhenTrackingIsOff()
    {
        Assert.Null(MouseReport.Encode(
            MouseTracking.Off, MouseEncoding.Sgr, MouseButton.Left,
            pressed: true, motion: false, column: 1, row: 1));
    }

    [Theory]
    [InlineData(MouseButton.WheelUp, 64)]
    [InlineData(MouseButton.WheelDown, 65)]
    public void TheWheelHasItsOwnButtonCodes(MouseButton button, int expected)
    {
        var report = MouseReport.Encode(
            MouseTracking.Click, MouseEncoding.Sgr, button,
            pressed: true, motion: false, column: 0, row: 0);

        Assert.StartsWith($"\u001b[<{expected};", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ModifiersAreFoldedIntoTheButtonCode()
    {
        var report = MouseReport.Encode(
            MouseTracking.Click, MouseEncoding.Sgr, MouseButton.Left,
            pressed: true, motion: false, column: 0, row: 0,
            shift: true, alt: false, control: true);

        // Left(0) + shift(4) + control(16).
        Assert.StartsWith("\u001b[<20;", report, StringComparison.Ordinal);
    }
}
