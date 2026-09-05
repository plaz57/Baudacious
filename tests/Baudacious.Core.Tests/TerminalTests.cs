using System.Text;
using Baudacious.Core.Terminal;

namespace Baudacious.Core.Tests;

/// <summary>Drives the parser and emulator with escape sequences and asserts on the grid.</summary>
public class TerminalTests
{
    private static (Screen Screen, VtParser Parser, RecordingHost Host) Build(int columns = 20, int rows = 6)
    {
        var screen = new Screen(columns, rows);
        var host = new RecordingHost();
        var parser = new VtParser(new TerminalEmulator(screen, host));
        return (screen, parser, host);
    }

    private static void Feed(VtParser parser, string text) =>
        parser.Consume(Encoding.UTF8.GetBytes(text.Replace("ESC", "\u001b", StringComparison.Ordinal)));

    internal sealed class RecordingHost : ITerminalHost
    {
        private readonly List<byte> _replies = [];

        public string? Title { get; private set; }

        public int Bells { get; private set; }

        public string Replies => Encoding.ASCII.GetString([.. _replies]);

        public void Reply(ReadOnlySpan<byte> bytes) => _replies.AddRange(bytes);

        public void SetTitle(string title) => Title = title;

        public void Bell() => Bells++;
    }

    // ---- Printing and the cursor -------------------------------------------

    [Fact]
    public void PrintableText_landsAtTheCursorAndAdvancesIt()
    {
        var (screen, parser, _) = Build();
        Feed(parser, "Hi");

        Assert.Equal('H', screen.GetCell(0, 0).Char);
        Assert.Equal('i', screen.GetCell(0, 1).Char);
        Assert.Equal(2, screen.CursorColumn);
    }

    [Fact]
    public void AbsoluteCursorAddressing_isOneBased()
    {
        var (screen, parser, _) = Build(columns: 40, rows: 20);
        Feed(parser, "ESC[10;20H*");

        Assert.Equal(9, screen.CursorRow);
        Assert.Equal('*', screen.GetCell(9, 19).Char);
    }

    [Fact]
    public void WritingExactlyOneLine_doesNotScrollEarly()
    {
        // Deferred wrap: the cursor stays on the last column until another character
        // arrives. Without it, an exactly-full line scrolls a row too soon.
        var (screen, parser, _) = Build(columns: 5, rows: 3);
        Feed(parser, "ABCDE");

        Assert.Equal(0, screen.CursorRow);
        Assert.Equal("ABCDE", screen.RowText(0));

        Feed(parser, "F");
        Assert.Equal(1, screen.CursorRow);
        Assert.Equal('F', screen.GetCell(1, 0).Char);
    }

    [Fact]
    public void AutoWrapOff_overwritesTheLastColumn()
    {
        var (screen, parser, _) = Build(columns: 5, rows: 3);
        Feed(parser, "ESC[?7lABCDEFG");

        Assert.Equal(0, screen.CursorRow);
        Assert.Equal("ABCDG", screen.RowText(0));
    }

    // ---- Colour -------------------------------------------------------------

    [Fact]
    public void Truecolor_isStoredExactly()
    {
        var (screen, parser, _) = Build();
        Feed(parser, "ESC[38;2;255;128;0mX");

        Assert.Equal(Palette.Rgb(255, 128, 0), screen.GetCell(0, 0).Foreground);
    }

    [Fact]
    public void TruecolorInColonForm_withAndWithoutAColourSpaceId()
    {
        var (screen, parser, _) = Build();
        Feed(parser, "ESC[38:2::10:20:30mA");
        Assert.Equal(Palette.Rgb(10, 20, 30), screen.GetCell(0, 0).Foreground);

        Feed(parser, "ESC[38:2:40:50:60mB");
        Assert.Equal(Palette.Rgb(40, 50, 60), screen.GetCell(0, 1).Foreground);
    }

    [Fact]
    public void IndexedColour_usesTheXtermCube()
    {
        var (screen, parser, _) = Build();
        Feed(parser, "ESC[38;5;196mX");

        Assert.Equal(Palette.Indexed(196), screen.GetCell(0, 0).Foreground);
        Assert.Equal(Palette.Rgb(255, 0, 0), screen.GetCell(0, 0).Foreground);
    }

    [Fact]
    public void AttributesApplyOnlyToCellsWrittenAfterwards()
    {
        var (screen, parser, _) = Build();
        Feed(parser, "aESC[1;31mb");

        Assert.Equal(CellFlags.None, screen.GetCell(0, 0).Flags);
        Assert.Equal(Palette.DefaultForeground, screen.GetCell(0, 0).Foreground);
        Assert.True(screen.GetCell(0, 1).Flags.HasFlag(CellFlags.Bold));
        Assert.Equal(Palette.Ansi16[1], screen.GetCell(0, 1).Foreground);
    }

    [Fact]
    public void SgrZero_resetsEverything()
    {
        var (screen, parser, _) = Build();
        Feed(parser, "ESC[1;4;31;42maESC[0mb");

        Assert.Equal(CellFlags.None, screen.GetCell(0, 1).Flags);
        Assert.Equal(Palette.DefaultForeground, screen.GetCell(0, 1).Foreground);
        Assert.Equal(Palette.DefaultBackground, screen.GetCell(0, 1).Background);
    }

    // ---- Erase and edit -----------------------------------------------------

    [Fact]
    public void EraseInLine_clearsFromTheCursor()
    {
        var (screen, parser, _) = Build(columns: 10, rows: 2);
        Feed(parser, "ABCDEFGESC[1;4HESC[K");

        Assert.Equal("ABC", screen.RowText(0).TrimEnd());
    }

    [Fact]
    public void DeleteCharacters_shiftsTheRestOfTheLineLeft()
    {
        var (screen, parser, _) = Build(columns: 10, rows: 2);
        Feed(parser, "ABCDEFESC[1;2HESC[2P");

        Assert.Equal("ADEF", screen.RowText(0).TrimEnd());
    }

    [Fact]
    public void InsertCharacters_shiftsRight()
    {
        var (screen, parser, _) = Build(columns: 10, rows: 2);
        Feed(parser, "ABCDESC[1;2HESC[2@");

        Assert.Equal("A  BCD", screen.RowText(0).TrimEnd());
    }

    [Fact]
    public void InsertAndDeleteLines_moveWholeRows()
    {
        var (screen, parser, _) = Build(columns: 6, rows: 4);
        Feed(parser, "one\r\ntwo\r\nthree");

        Feed(parser, "ESC[1;1HESC[1L");
        Assert.Equal("", screen.RowText(0).TrimEnd());
        Assert.Equal("one", screen.RowText(1).TrimEnd());

        Feed(parser, "ESC[1;1HESC[1M");
        Assert.Equal("one", screen.RowText(0).TrimEnd());
    }

    // ---- Scroll regions ------------------------------------------------------

    [Fact]
    public void ScrollRegion_leavesRowsOutsideItAlone()
    {
        var (screen, parser, _) = Build(columns: 6, rows: 6);
        Feed(parser, "r0\r\nr1\r\nr2\r\nr3\r\nr4\r\nr5");

        // Region rows 3-5 (1-based), i.e. indices 2-4.
        Feed(parser, "ESC[3;5rESC[5;1H\n");

        Assert.Equal("r0", screen.RowText(0).TrimEnd());
        Assert.Equal("r1", screen.RowText(1).TrimEnd());
        Assert.Equal("r5", screen.RowText(5).TrimEnd());
        Assert.Equal("r3", screen.RowText(2).TrimEnd());
    }

    // ---- Alternate buffer ----------------------------------------------------

    [Fact]
    public void AlternateBuffer_restoresThePrimaryAndDoesNotTouchScrollback()
    {
        var (screen, parser, _) = Build(columns: 10, rows: 3);
        Feed(parser, "primary");

        var scrollbackBefore = screen.Scrollback.Count;

        Feed(parser, "ESC[?1049h");
        Feed(parser, "ESC[2JESC[1;1Hfullscreen\r\nmore");
        Assert.Contains("fullscreen", screen.ToText(), StringComparison.Ordinal);

        // Scrolling inside the alternate buffer must not push anything into history.
        Feed(parser, "\r\nlines\r\noverflow");
        Assert.DoesNotContain("fullscreen", screen.ToText(), StringComparison.Ordinal);
        Assert.Equal(scrollbackBefore, screen.Scrollback.Count);

        Feed(parser, "ESC[?1049l");

        Assert.Contains("primary", screen.ToText(), StringComparison.Ordinal);
        Assert.DoesNotContain("fullscreen", screen.ToText(), StringComparison.Ordinal);
        Assert.Equal(scrollbackBefore, screen.Scrollback.Count);
    }

    // ---- Queries -------------------------------------------------------------

    [Fact]
    public void CursorPositionReport_answersWithOneBasedCoordinates()
    {
        var (_, parser, host) = Build(columns: 40, rows: 20);
        Feed(parser, "ESC[7;13HESC[6n");

        Assert.Equal("\u001b[7;13R", host.Replies);
    }

    [Fact]
    public void DeviceAttributes_claimAVt100()
    {
        var (_, parser, host) = Build();
        Feed(parser, "ESC[c");

        Assert.Equal("\u001b[?1;2c", host.Replies);
    }

    [Fact]
    public void WindowTitle_isReadFromOsc()
    {
        var (_, parser, host) = Build();
        Feed(parser, "ESC]0;C:\\dev\\Baudacious\u0007");

        Assert.Equal(@"C:\dev\Baudacious", host.Title);
    }

    // ---- Robustness ----------------------------------------------------------

    [Fact]
    public void UnsupportedSequences_vanishRatherThanPrint()
    {
        var (screen, parser, _) = Build();
        Feed(parser, "aESC[99;99;99~b");

        Assert.Equal("ab", screen.RowText(0).TrimEnd());
    }

    [Fact]
    public void ATruncatedSequence_leavesTheParserWaitingAndPrintsNothing()
    {
        var (screen, parser, _) = Build();
        Feed(parser, "abcESC[1;3");

        Assert.Equal("abc", screen.RowText(0).TrimEnd());
        Assert.True(parser.IsMidSequence);
    }

    [Fact]
    public void CanAbortsASequence()
    {
        var (screen, parser, _) = Build();
        Feed(parser, "aESC[12\u0018b");

        Assert.Equal("ab", screen.RowText(0).TrimEnd());
    }

    // ---- Encoding -------------------------------------------------------------

    [Fact]
    public void Utf8_isDecoded()
    {
        var (screen, parser, _) = Build();
        parser.Consume(Encoding.UTF8.GetBytes("┌─┐"));

        Assert.Equal('┌', screen.GetCell(0, 0).Char);
        Assert.Equal('─', screen.GetCell(0, 1).Char);
        Assert.Equal('┐', screen.GetCell(0, 2).Char);
    }

    [Fact]
    public void InvalidUtf8_becomesOneSubstitutionAndParsingContinues()
    {
        var (screen, parser, _) = Build();
        parser.Consume([(byte)'a', 0xFF, (byte)'b']);

        Assert.Equal('a', screen.GetCell(0, 0).Char);
        Assert.Equal('\uFFFD', screen.GetCell(0, 1).Char);
        Assert.Equal('b', screen.GetCell(0, 2).Char);
    }

    [Fact]
    public void ATruncatedUtf8Character_doesNotSwallowTheNextOne()
    {
        var (screen, parser, _) = Build();

        // Leading byte of a three-byte character, then an unrelated ASCII byte.
        parser.Consume([0xE2, (byte)'X']);

        Assert.Equal('\uFFFD', screen.GetCell(0, 0).Char);
        Assert.Equal('X', screen.GetCell(0, 1).Char);
    }

    // ---- Scrollback ------------------------------------------------------------

    [Fact]
    public void LinesScrollingOffTheTop_areKept()
    {
        var (screen, parser, _) = Build(columns: 10, rows: 3);
        for (var i = 0; i < 10; i++)
        {
            Feed(parser, $"line{i}\r\n");
        }

        // Three rows, so after ten lines the screen holds line8 and line9 and the cursor
        // sits on the blank row below them; everything earlier is history.
        Assert.Contains("line0", screen.ScrollbackText(), StringComparison.Ordinal);
        Assert.Contains("line7", screen.ScrollbackText(), StringComparison.Ordinal);
        Assert.Contains("line9", screen.ToText(), StringComparison.Ordinal);
        Assert.DoesNotContain("line7", screen.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollbackCapacity_isEnforced()
    {
        var screen = new Screen(10, 3, scrollbackCapacity: 5);
        var parser = new VtParser(new TerminalEmulator(screen, NullTerminalHost.Instance));

        for (var i = 0; i < 100; i++)
        {
            Feed(parser, $"L{i}\r\n");
        }

        Assert.True(screen.Scrollback.Count <= 5);
    }
}
