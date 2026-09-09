using System.Runtime.Versioning;
using System.Text;
using Baudacious.Core.Baud;
using Baudacious.Core.Terminal;
using Baudacious.Core.Timing;
using Baudacious.Pty;

namespace Baudacious.App;

/// <summary>
/// One hosted shell and everything downstream of it: the wire it arrives on, the rate it is
/// metered at, the parser, and the grid it lands on.
/// </summary>
/// <remarks>
/// A window can hold several of these. Each has its own line rate, because the rate is a
/// property of the line rather than of the screen, and its own scrollback and selection for
/// the same reason. What they share is the window's look — palette, magnification, the CRT
/// pass — which is a property of the glass, not of any one connection.
/// <para>
/// Every session is metered on every frame, foreground or not. A wire does not stop
/// carrying because nobody is watching, and a background session that had been paused would
/// otherwise dump its whole backlog the instant it was switched to.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class TerminalSession : IPtyOutputSink, ITerminalHost, IDisposable
{
    private readonly ByteRing _ring = new();
    private readonly Action _invalidate;

    private PtySession? _pty;

    private TerminalSession(
        int number,
        string profile,
        LineRate rate,
        int columns,
        int rows,
        int scrollback,
        Action invalidate)
    {
        Number = number;
        Profile = profile;
        _invalidate = invalidate;

        Screen = new Screen(columns, rows, scrollback);
        Emulator = new TerminalEmulator(Screen, this);
        Parser = new VtParser(Emulator);
        Governor = new BaudGovernor(_ring, SystemTimeSource.Instance, rate);
    }

    /// <summary>Its number in the window, from one. Stable for the session's life.</summary>
    public int Number { get; }

    /// <summary>The profile name it was started from, as typed.</summary>
    public string Profile { get; }

    /// <summary>
    /// Whether the shell is using the console's cooked line input rather than its own
    /// editor. Meaningless, and always false, where the profile has no editor to replace.
    /// </summary>
    public bool FastEcho { get; private set; }

    public Screen Screen { get; }

    public TerminalEmulator Emulator { get; }

    public VtParser Parser { get; }

    public BaudGovernor Governor { get; }

    /// <summary>Whatever the shell last set by OSC, or empty.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// The shells that exist, kept because changing the line discipline later needs to know
    /// whether this profile has an editor worth replacing.
    /// </summary>
    /// <remarks>
    /// Defaults to the built-ins so a detached session — one with a grid and no shell — is
    /// still safe to ask.
    /// </remarks>
    private ShellProfiles _profiles = ShellProfiles.Builtin;

    /// <summary>Local, and therefore the window's to set but the session's to keep.</summary>
    public Selection? Selection { get; set; }

    /// <summary>Set once the shell has exited and its backlog has drained.</summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// Why there is no shell here, when there is no shell here.
    /// </summary>
    /// <remarks>
    /// Kept on the session rather than announced once, because a window with no shell in it
    /// needs to keep saying so. A notice that lapses after a few seconds leaves a blank
    /// screen and no explanation, which is what happened when an unknown profile stopped
    /// silently launching PowerShell and started correctly launching nothing.
    /// </remarks>
    public string? Failure { get; private set; }

    /// <summary>
    /// Starts a shell for a profile. Throws if the profile names something that is not
    /// installed, or nothing at all, which the caller is expected to report rather than
    /// swallow.
    /// </summary>
    public static TerminalSession Start(
        int number,
        string profile,
        ShellProfiles profiles,
        bool fastEcho,
        LineRate rate,
        int columns,
        int rows,
        int scrollback,
        Action invalidate)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var (start, setup) = profiles.Build(profile, fastEcho);
        var session = new TerminalSession(number, profile, rate, columns, rows, scrollback, invalidate)
        {
            _profiles = profiles,
            FastEcho = fastEcho && profiles.SupportsFastEcho(profile),
        };

        session._pty = PtySession.Start(
            start with { Columns = (short)columns, Rows = (short)rows }, session);

        session.SendSetup(setup);
        return session;
    }

    /// <summary>
    /// A session with a grid but no shell, for when the profile named something that is not
    /// installed. It exists so the window has somewhere to say so.
    /// </summary>
    public static TerminalSession Detached(
        int number,
        string profile,
        LineRate rate,
        int columns,
        int rows,
        int scrollback,
        Action invalidate,
        string? failure = null) =>
        new(number, profile, rate, columns, rows, scrollback, invalidate) { Failure = failure };

    public void Write(ReadOnlySpan<byte> bytes) => _pty?.Write(bytes);

    /// <summary>
    /// Changes the line discipline on the running shell. False when the profile has no
    /// editor worth replacing, in which case nothing is sent.
    /// </summary>
    public bool SetFastEcho(bool on)
    {
        if (!_profiles.SupportsFastEcho(Profile))
        {
            return false;
        }

        foreach (var command in ShellProfiles.FastEchoCommands(on))
        {
            Write(Encoding.UTF8.GetBytes(command + "\r"));
        }

        FastEcho = on;
        return true;
    }

    public void Resize(int columns, int rows)
    {
        // Reflow first, then tell ConPTY: the queued bytes are neither discarded nor
        // reordered by either step.
        Screen.Resize(columns, rows);
        _pty?.Resize((short)columns, (short)rows);
    }

    /// <summary>Releases whatever the rate allows and parses it. Returns the bytes released.</summary>
    public int Tick() => Governor.Tick(Parser);

    /// <summary>The shell's exit code once it has one, whether or not the backlog has drained.</summary>
    public int? PtyExitCode => _pty?.TryGetExitCode();

    /// <summary>True once the shell has gone and everything it sent has been shown.</summary>
    public bool IsFinished => ExitCode is not null;

    public void Dispose()
    {
        _pty?.Dispose();
        _pty = null;
    }

    // ---- Plumbing ----------------------------------------------------------------

    public void OnBytes(ReadOnlySpan<byte> bytes, long timestamp) => _ring.Write(bytes);

    public void OnClosed() => _invalidate();

    public void Reply(ReadOnlySpan<byte> bytes) => _pty?.Write(bytes);

    public void SetTitle(string title)
    {
        Title = title;
        _invalidate();
    }

    public void Bell() => _invalidate();

    /// <summary>
    /// Sends a profile's startup commands once the shell is listening.
    /// </summary>
    /// <remarks>
    /// Off the render thread, because the shell has to come up before it will read: a
    /// startup command delivered too early is simply lost, which showed up as the prompt
    /// override silently not applying.
    /// </remarks>
    private void SendSetup(string[] setup)
    {
        if (setup.Length == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline && _ring.IsEmpty)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            await Task.Delay(250).ConfigureAwait(false);

            foreach (var command in setup)
            {
                Write(Encoding.UTF8.GetBytes(command + "\r"));
                await Task.Delay(120).ConfigureAwait(false);
            }
        });
    }
}
