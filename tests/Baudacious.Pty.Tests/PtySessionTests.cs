using System.ComponentModel;
using Baudacious.Pty;

namespace Baudacious.Pty.Tests;

/// <summary>
/// What can be checked without a live shell.
/// </summary>
/// <remarks>
/// The substantive tests for this assembly — a session that starts, echoes, resizes and
/// shuts down cleanly — need a real process and console context and belong in Phase 3
/// (tasks 4.8-4.12). They cannot run under a harness that pipes the host's stdio: as
/// Phase 0 found, the hosted shell then exits with status 0 shortly after printing its
/// prompt, so such a test would fail for reasons unrelated to the code under test.
/// </remarks>
public class PtyStartInfoTests
{
    [Fact]
    public void Defaults_areAnEightyByTwentyFourGrid()
    {
        var info = new PtyStartInfo { Executable = "cmd.exe" };

        Assert.Equal(80, info.Columns);
        Assert.Equal(24, info.Rows);
        Assert.Equal(string.Empty, info.Arguments);
        Assert.Null(info.WorkingDirectory);
        Assert.Null(info.Environment);
    }

    [Fact]
    public void WithExpression_changesOnlyWhatIsNamed()
    {
        var info = new PtyStartInfo { Executable = "pwsh", Arguments = "-NoLogo" };
        var resized = info with { Columns = 120, Rows = 40 };

        Assert.Equal("pwsh", resized.Executable);
        Assert.Equal("-NoLogo", resized.Arguments);
        Assert.Equal(120, resized.Columns);
        Assert.Equal(40, resized.Rows);

        // The original is untouched, which is what lets one profile be reused per session.
        Assert.Equal(80, info.Columns);
    }
}

public class PtySessionArgumentTests
{
    private sealed class NullSink : IPtyOutputSink
    {
        public void OnBytes(ReadOnlySpan<byte> bytes, long timestamp)
        {
        }

        public void OnClosed()
        {
        }
    }

    [Fact]
    public void Start_rejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PtySession.Start(null!, new NullSink()));

        Assert.Throws<ArgumentNullException>(() =>
            PtySession.Start(new PtyStartInfo { Executable = "cmd.exe" }, null!));
    }

    [Fact]
    public void Start_withAMissingExecutable_reportsWhatItLookedFor()
    {
        // specs/shell-hosting: a missing executable is reported, naming the path, and does
        // not take the application down with it.
        var missing = Path.Combine(Path.GetTempPath(), "baudacious-no-such-shell-8f2a.exe");
        var info = new PtyStartInfo { Executable = missing };

        var ex = Assert.Throws<Win32Exception>(() => PtySession.Start(info, new NullSink()));
        Assert.Contains(missing, ex.Message, StringComparison.Ordinal);
    }
}
