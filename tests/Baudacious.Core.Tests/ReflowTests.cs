using System.Text;
using Baudacious.Core.Terminal;

namespace Baudacious.Core.Tests;

/// <summary>
/// Resize behaviour. Historically the buggiest part of any terminal, which is why the
/// wrap flags exist in the row model from the first commit rather than being inferred.
/// </summary>
public class ReflowTests
{
    private static (Screen Screen, VtParser Parser) Build(int columns, int rows)
    {
        var screen = new Screen(columns, rows);
        return (screen, new VtParser(new TerminalEmulator(screen, NullTerminalHost.Instance)));
    }

    private static void Feed(VtParser parser, string text) =>
        parser.Consume(Encoding.UTF8.GetBytes(text));

    private static string Visible(Screen screen) =>
        string.Join('\n', screen.ScrollbackText().Split('\n').Concat(screen.ToText().Split('\n')))
            .Replace("\n\n", "\n", StringComparison.Ordinal)
            .Trim();

    [Fact]
    public void Narrowing_rewrapsASoftWrappedLineWithoutLosingCharacters()
    {
        var (screen, parser) = Build(columns: 12, rows: 6);
        Feed(parser, "abcdefghijklmnopqrstuvwx");   // exactly two rows at 12 wide

        Assert.True(screen[0].SoftWrapped);

        screen.Resize(8, 6);

        var text = Visible(screen).Replace("\n", string.Empty, StringComparison.Ordinal);
        Assert.Equal("abcdefghijklmnopqrstuvwx", text);
    }

    [Fact]
    public void Widening_rejoinsWhatFits()
    {
        var (screen, parser) = Build(columns: 8, rows: 6);
        Feed(parser, "abcdefghijklmnop");           // two rows at 8 wide

        screen.Resize(20, 6);

        Assert.Equal("abcdefghijklmnop", screen.RowText(0).TrimEnd());
        Assert.False(screen[0].SoftWrapped);
    }

    [Fact]
    public void HardLineBreaks_surviveAWiden()
    {
        var (screen, parser) = Build(columns: 10, rows: 6);
        Feed(parser, "one\r\ntwo\r\nthree");

        screen.Resize(40, 6);

        Assert.Equal("one", screen.RowText(0).TrimEnd());
        Assert.Equal("two", screen.RowText(1).TrimEnd());
        Assert.Equal("three", screen.RowText(2).TrimEnd());
    }

    [Fact]
    public void NarrowThenWideThenNarrow_roundTripsTheContent()
    {
        var (screen, parser) = Build(columns: 30, rows: 10);
        Feed(parser, "The quick brown fox jumps over the lazy dog and keeps going for a while\r\n");
        Feed(parser, "second line\r\n");
        Feed(parser, "a third, rather longer line that will certainly need to wrap somewhere");

        var before = Flatten(screen);

        screen.Resize(17, 10);
        screen.Resize(64, 10);
        screen.Resize(30, 10);

        Assert.Equal(before, Flatten(screen));
    }

    [Fact]
    public void Attributes_surviveReflow()
    {
        var (screen, parser) = Build(columns: 10, rows: 4);
        Feed(parser, "\u001b[31mredredredredred");

        screen.Resize(6, 4);

        var found = false;
        for (var row = 0; row < screen.RowCount && !found; row++)
        {
            for (var column = 0; column < screen.Columns; column++)
            {
                var cell = screen.GetCell(row, column);
                if (cell.Rune == 'r')
                {
                    Assert.Equal(Palette.Ansi16[1], cell.Foreground);
                    found = true;
                    break;
                }
            }
        }

        Assert.True(found, "no red text survived the resize");
    }

    [Fact]
    public void TheCursorStaysOnItsLogicalLine()
    {
        var (screen, parser) = Build(columns: 20, rows: 6);
        Feed(parser, "hello\r\nworld");

        screen.Resize(10, 6);

        // Still just after "world", wherever that landed.
        Assert.Equal(5, screen.CursorColumn);
        Assert.Equal("world", screen.RowText(screen.CursorRow).TrimEnd());
    }

    [Fact]
    public void ResizingTheAlternateBuffer_doesNotDisturbThePrimary()
    {
        var (screen, parser) = Build(columns: 20, rows: 6);
        Feed(parser, "primary content");

        Feed(parser, "\u001b[?1049h");
        Feed(parser, "\u001b[2J[1;1Hfullscreen");

        screen.Resize(40, 10);
        Feed(parser, "\u001b[?1049l");

        Assert.Contains("primary content", screen.ToText(), StringComparison.Ordinal);
    }

    /// <summary>All visible and historic text, with layout removed, for round-trip comparison.</summary>
    private static string Flatten(Screen screen)
    {
        var builder = new StringBuilder();
        foreach (var row in screen.Scrollback)
        {
            Append(builder, row);
        }

        for (var i = 0; i < screen.RowCount; i++)
        {
            Append(builder, screen[i]);
        }

        return builder.ToString().TrimEnd();
    }

    private static void Append(StringBuilder builder, Row row)
    {
        var blank = Cell.Blank(Palette.DefaultForeground, Palette.DefaultBackground);
        var end = row.SoftWrapped ? row.Length : row.TrimmedLength(blank);
        for (var i = 0; i < end; i++)
        {
            builder.Append(row.Cells[i].Rune == 0 ? ' ' : row.Cells[i].Char);
        }

        if (!row.SoftWrapped)
        {
            builder.Append('\n');
        }
    }
}
