using Baudacious.Core.Baud;
using Baudacious.Core.Terminal;
using Baudacious.Core.Timing;

namespace Baudacious.Core.Tests;

/// <summary>
/// Replays the real ConPTY byte streams recorded in Phase 0 through the parser.
/// </summary>
public class CaptureReplayTests
{
    public static TheoryData<string> Captures
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var path in Directory.GetFiles(CaptureDirectory, "*.bin").OrderBy(p => p, StringComparer.Ordinal))
            {
                data.Add(Path.GetFileName(path));
            }

            return data;
        }
    }

    private static string CaptureDirectory
    {
        get
        {
            // Walk up from the test binary to the repository root.
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "conpty-captures");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("could not locate tests/fixtures/conpty-captures");
        }
    }

    private static (Screen Screen, VtParser Parser) Build(int columns = 100, int rows = 30)
    {
        var screen = new Screen(columns, rows);
        return (screen, new VtParser(new TerminalEmulator(screen, new SilentHost())));
    }

    /// <summary>Answers queries, since ConPTY blocks on some of them, but records nothing.</summary>
    private sealed class SilentHost : ITerminalHost
    {
        public void Reply(ReadOnlySpan<byte> bytes)
        {
        }

        public void SetTitle(string title)
        {
        }

        public void Bell()
        {
        }
    }

    [Fact]
    public void TheFixturesArePresent() => Assert.NotEmpty(Directory.GetFiles(CaptureDirectory, "*.bin"));

    /// <summary>
    /// <b>The invariant that matters most.</b> The governor hands the parser whatever a
    /// frame's worth of wire time allows, so at 300 baud a capture arrives one byte at a
    /// time across thousands of frames. If that produced a different screen from feeding
    /// the whole stream at once, the baud rate would be changing what you see rather than
    /// only when you see it — and every emulation bug would become rate-dependent.
    /// </summary>
    [Theory]
    [MemberData(nameof(Captures))]
    public void ByteAtATime_producesTheSameScreenAsAllAtOnce(string capture)
    {
        var bytes = File.ReadAllBytes(Path.Combine(CaptureDirectory, capture));

        var (bulkScreen, bulkParser) = Build();
        bulkParser.Consume(bytes);

        var (dribbleScreen, dribbleParser) = Build();
        foreach (var b in bytes)
        {
            dribbleParser.Consume(new ReadOnlySpan<byte>(in b));
        }

        Assert.Equal(bulkScreen.ToText(), dribbleScreen.ToText());
        Assert.Equal(bulkScreen.ScrollbackText(), dribbleScreen.ScrollbackText());
        Assert.Equal(bulkScreen.CursorRow, dribbleScreen.CursorRow);
        Assert.Equal(bulkScreen.CursorColumn, dribbleScreen.CursorColumn);
    }

    /// <summary>The same invariant, but with the real governor doing the splitting.</summary>
    [Theory]
    [MemberData(nameof(Captures))]
    public void MeteredDelivery_producesTheSameScreenAsUnmetered(string capture)
    {
        var bytes = File.ReadAllBytes(Path.Combine(CaptureDirectory, capture));

        var (bulkScreen, bulkParser) = Build();
        bulkParser.Consume(bytes);

        var (meteredScreen, meteredParser) = Build();
        var ring = new ByteRing();
        ring.Write(bytes);
        var clock = new FakeTimeSource();
        var governor = new BaudGovernor(ring, clock, LineRate.Of(300));

        var guard = 0;
        while (governor.PendingBytes > 0 && guard++ < 5_000_000)
        {
            clock.AdvanceFrame();
            governor.Tick(meteredParser);
        }

        Assert.Equal(0, governor.PendingBytes);
        Assert.Equal(bulkScreen.ToText(), meteredScreen.ToText());
        Assert.Equal(bulkScreen.ScrollbackText(), meteredScreen.ScrollbackText());
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void EveryCaptureParsesWithoutLeavingStrayEscapesOnScreen(string capture)
    {
        var bytes = File.ReadAllBytes(Path.Combine(CaptureDirectory, capture));
        var (screen, parser) = Build();
        parser.Consume(bytes);

        var text = screen.ToText() + screen.ScrollbackText();

        // An escape byte reaching a cell means the parser printed something it should have
        // consumed.
        Assert.DoesNotContain('\u001b', text);
    }

    [Fact]
    public void TheAlternateBufferCaptures_enterAndLeaveIt()
    {
        // vim -u NONE, recorded in Phase 0. Verifies 1049 handling against a real program
        // rather than a synthetic sequence.
        foreach (var capture in Directory.GetFiles(CaptureDirectory, "*altbuffer.bin"))
        {
            var (screen, parser) = Build();
            parser.Consume(File.ReadAllBytes(capture));

            Assert.False(screen.IsAlternate);
            Assert.Contains(
                "vim",
                (screen.ToText() + screen.ScrollbackText()).ToLowerInvariant(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ADeepListing_fillsScrollback()
    {
        var (screen, parser) = Build();
        parser.Consume(File.ReadAllBytes(Path.Combine(CaptureDirectory, "cmd-deep-listing.bin")));

        Assert.NotEmpty(screen.Scrollback);
        Assert.Contains("Directory of", screen.ScrollbackText(), StringComparison.Ordinal);
    }
}
