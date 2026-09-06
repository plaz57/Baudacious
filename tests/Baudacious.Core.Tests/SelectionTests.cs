using System.Text;
using Baudacious.Core.Terminal;

namespace Baudacious.Core.Tests;

public class ScrollbackViewTests
{
    private static (Screen Screen, VtParser Parser) Build(int columns = 20, int rows = 4)
    {
        var screen = new Screen(columns, rows);
        return (screen, new VtParser(new TerminalEmulator(screen, NullTerminalHost.Instance)));
    }

    private static void Feed(VtParser parser, string text) =>
        parser.Consume(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void TheLiveViewShowsTheGrid()
    {
        var (screen, parser) = Build();
        Feed(parser, "one\r\ntwo\r\nthree");

        Assert.False(screen.IsScrolledBack);
        Assert.Equal('o', screen.GetViewCell(0, 0).Char);
    }

    [Fact]
    public void ScrollingBackShowsHistory()
    {
        var (screen, parser) = Build();
        for (var i = 0; i < 20; i++)
        {
            Feed(parser, $"line{i}\r\n");
        }

        // Twenty lines each ending in a newline leaves line17..line19 plus the cursor's
        // blank row on a four-row screen, so history holds line0..line16 — seventeen lines.
        Assert.Equal(17, screen.Scrollback.Count);

        screen.ScrollView(10);

        Assert.True(screen.IsScrolledBack);
        Assert.Equal(10, screen.ViewOffset);

        // Ten back from the end of seventeen is scrollback[7].
        var top = new string(Enumerable.Range(0, 8).Select(c => screen.GetViewCell(0, c).Char).ToArray());
        Assert.StartsWith("line7", top, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollingIsClampedToWhatExists()
    {
        var (screen, parser) = Build();
        Feed(parser, "only\r\n");

        screen.ScrollView(1000);
        Assert.Equal(screen.Scrollback.Count, screen.ViewOffset);

        screen.ScrollView(-1000);
        Assert.Equal(0, screen.ViewOffset);
        Assert.False(screen.IsScrolledBack);
    }

    [Fact]
    public void NewOutputDoesNotDragTheViewAlong()
    {
        // specs/terminal-emulation: the view stays where the user put it, and the new
        // output is still recorded.
        var (screen, parser) = Build();
        for (var i = 0; i < 20; i++)
        {
            Feed(parser, $"line{i}\r\n");
        }

        screen.ScrollView(5);
        var before = ReadTopLine(screen);

        for (var i = 20; i < 30; i++)
        {
            Feed(parser, $"line{i}\r\n");
        }

        Assert.Equal(before, ReadTopLine(screen));
        Assert.True(screen.IsScrolledBack);

        screen.ScrollToLive();
        Assert.Contains("line29", screen.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AbsoluteLineNumbersSurviveEviction()
    {
        // The property a selection depends on: a line keeps its number as history is
        // dropped from the front of the buffer.
        var screen = new Screen(20, 4, scrollbackCapacity: 5);
        var parser = new VtParser(new TerminalEmulator(screen, NullTerminalHost.Instance));

        for (var i = 0; i < 50; i++)
        {
            Feed(parser, $"L{i}\r\n");
        }

        Assert.True(screen.ScrolledLines >= 40);
        Assert.True(screen.Scrollback.Count <= 5);

        // The newest scrollback line is still findable by its absolute number.
        var newest = screen.ScrolledLines - 1;
        Assert.True(screen.TryGetAbsoluteRow(newest, out _));

        // An evicted one is honestly reported as gone rather than returning the wrong row.
        Assert.False(screen.TryGetAbsoluteRow(0, out _));
    }

    private static string ReadTopLine(Screen screen) =>
        new(Enumerable.Range(0, screen.Columns).Select(c => screen.GetViewCell(0, c).Char).ToArray());
}

public class SelectionTests
{
    private static (Screen Screen, VtParser Parser) Build(int columns = 20, int rows = 6)
    {
        var screen = new Screen(columns, rows);
        return (screen, new VtParser(new TerminalEmulator(screen, NullTerminalHost.Instance)));
    }

    private static void Feed(VtParser parser, string text) =>
        parser.Consume(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void SelectingPartOfALineReturnsThatText()
    {
        var (screen, parser) = Build();
        Feed(parser, "hello world");

        var selection = new Selection(new GridPoint(0, 6), new GridPoint(0, 11));
        Assert.Equal("world", selection.GetText(screen));
    }

    [Fact]
    public void TrailingBlanksAreDropped()
    {
        var (screen, parser) = Build();
        Feed(parser, "hi");

        var selection = new Selection(new GridPoint(0, 0), new GridPoint(0, 20));
        Assert.Equal("hi", selection.GetText(screen));
    }

    [Fact]
    public void ASoftWrappedLineComesBackAsOneLine()
    {
        // The break was the right margin's doing, not the text's — so copying it must not
        // introduce a newline the text never had.
        var (screen, parser) = Build(columns: 10, rows: 6);
        Feed(parser, "abcdefghijklmnop");

        Assert.True(screen[0].SoftWrapped);

        var selection = new Selection(new GridPoint(0, 0), new GridPoint(1, 6));
        Assert.Equal("abcdefghijklmnop", selection.GetText(screen));
    }

    [Fact]
    public void HardBreaksAreKept()
    {
        var (screen, parser) = Build();
        Feed(parser, "one\r\ntwo");

        var selection = new Selection(new GridPoint(0, 0), new GridPoint(1, 3));
        Assert.Equal("one\ntwo", selection.GetText(screen));
    }

    [Fact]
    public void SelectionIsDirectionless()
    {
        var (screen, parser) = Build();
        Feed(parser, "hello world");

        var forward = new Selection(new GridPoint(0, 0), new GridPoint(0, 5));
        var backward = new Selection(new GridPoint(0, 5), new GridPoint(0, 0));

        Assert.Equal(forward.GetText(screen), backward.GetText(screen));
        Assert.Equal("hello", backward.GetText(screen));
    }

    [Fact]
    public void DoubleClickTakesTheWordUnderIt()
    {
        var (screen, parser) = Build();
        Feed(parser, "alpha beta gamma");

        var selection = Selection.Expand(screen, new GridPoint(0, 7), SelectionMode.Word);
        Assert.Equal("beta", selection.GetText(screen));
    }

    [Fact]
    public void TripleClickTakesTheWholeLine()
    {
        var (screen, parser) = Build();
        Feed(parser, "alpha beta gamma");

        var selection = Selection.Expand(screen, new GridPoint(0, 7), SelectionMode.Line);
        Assert.Equal("alpha beta gamma", selection.GetText(screen));
    }

    [Fact]
    public void AnEmptySelectionYieldsNothing()
    {
        var (screen, parser) = Build();
        Feed(parser, "text");

        Assert.True(new Selection(new GridPoint(0, 3), new GridPoint(0, 3)).IsEmpty);
        Assert.Equal(string.Empty, new Selection(new GridPoint(0, 3), new GridPoint(0, 3)).GetText(screen));
    }

    [Fact]
    public void SelectionSpansScrollbackAndScreen()
    {
        var (screen, parser) = Build(columns: 20, rows: 3);
        for (var i = 0; i < 10; i++)
        {
            Feed(parser, $"line{i}\r\n");
        }

        // Two lines that are now on opposite sides of the scrollback boundary.
        var from = new GridPoint(screen.ScrolledLines - 2, 0);
        var to = new GridPoint(screen.ScrolledLines, 5);

        var text = new Selection(from, to).GetText(screen);
        Assert.Contains("line", text, StringComparison.Ordinal);
        Assert.Contains('\n', text);
    }
}
