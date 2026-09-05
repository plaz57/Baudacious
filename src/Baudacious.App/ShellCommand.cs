using System.Globalization;
using System.Text;
using Baudacious.Core.Baud;
using Baudacious.Core.Terminal;
using Baudacious.Core.Timing;
using Baudacious.Pty;
using Baudacious.Render;

namespace Baudacious.App;

/// <summary>
/// A live shell, hosted through ConPTY, metered at a chosen baud rate, parsed, and painted
/// onto the host console.
/// </summary>
/// <remarks>
/// Everything Baudacious does except owning its own window and the CRT shader. The pipeline
/// below is the one from design.md D2, unchanged; Phase 3 swaps the console renderer for
/// the GPU one and nothing else moves.
/// </remarks>
internal sealed class ShellCommand : IPtyOutputSink, ITerminalHost
{
    private readonly ByteRing _ring = new();
    private readonly ConsoleRenderer _renderer = new();

    private PtySession? _session;
    private Screen _screen = null!;
    private VtParser _parser = null!;
    private BaudGovernor _governor = null!;
    private string _title = string.Empty;
    private volatile bool _running = true;
    private volatile bool _dirty = true;
    private string _notice = string.Empty;
    private long _noticeUntil;

    public static int Run(string[] args) => new ShellCommand().Execute(args);

    private int Execute(string[] args)
    {
        var rate = LineRate.Of(1200);
        var profile = "pwsh";
        var fastEcho = true;
        var script = new List<string>();
        var runFor = 0.0;
        string? reportPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--baud" or "-b" when i + 1 < args.Length:
                    if (!LineRate.TryParse(args[++i], out rate))
                    {
                        Console.Error.WriteLine($"not a rate: {args[i]}");
                        return 1;
                    }

                    break;

                case "--shell" or "-s" when i + 1 < args.Length:
                    profile = args[++i].ToLowerInvariant();
                    break;

                case "--phosphor" or "-p" when i + 1 < args.Length:
                    if (!Enum.TryParse<Phosphor>(args[++i], ignoreCase: true, out var phosphor))
                    {
                        Console.Error.WriteLine($"not a phosphor: {args[i]} (colour, green, amber, white)");
                        return 1;
                    }

                    _renderer.Phosphor = phosphor;
                    break;

                case "--rich-editor":
                    fastEcho = false;
                    break;

                // Scripted mode: no keyboard, no repaint. Sends commands, waits for the
                // line to drain, then dumps the resulting grid as plain text. This is the
                // end-to-end check for ConPTY -> ring -> governor -> parser -> screen, and
                // the only way to exercise it without a human at a keyboard.
                case "--script" when i + 1 < args.Length:
                    script.Add(args[++i]);
                    break;

                // The report goes to a file rather than stdout on purpose. Redirecting our
                // own stdout with a shell '>' confuses the hosted child's console
                // attachment — its output ends up on the inherited handle instead of the
                // pseudoconsole, so the pipeline sees only the input echo. Same trap as
                // Phase 0, different disguise.
                case "--out" when i + 1 < args.Length:
                    reportPath = args[++i];
                    break;

                case "--run-for" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out runFor))
                    {
                        Console.Error.WriteLine($"not a duration: {args[i]}");
                        return 1;
                    }

                    break;

                case "-h" or "--help":
                    Usage();
                    return 0;

                default:
                    Console.Error.WriteLine($"unrecognised option: {args[i]}");
                    Usage();
                    return 1;
            }
        }

        var scripted = script.Count > 0 || runFor > 0;

        if (!scripted && (Console.IsOutputRedirected || Console.IsInputRedirected))
        {
            Console.Error.WriteLine(
                "This needs a real console: run it from a terminal window, not through a pipe. " +
                "A hosted shell whose console attachment is confused exits moments after its prompt.");
            return 1;
        }

        PtyStartInfo start;
        string[] setup;
        try
        {
            (start, setup) = BuildProfile(profile, fastEcho);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (!scripted)
        {
            VirtualTerminal.EnableOnStdout();
        }

        var columns = scripted ? 100 : Math.Max(Console.WindowWidth, 20);
        var rows = scripted ? 30 : Math.Max(Console.WindowHeight - 1, 5);

        _screen = new Screen(columns, rows);
        var emulator = new TerminalEmulator(_screen, this);
        _parser = new VtParser(emulator);
        _governor = new BaudGovernor(_ring, SystemTimeSource.Instance, rate);

        if (!scripted)
        {
            Console.Write("\u001b[2J\u001b[H");
        }

        _session = PtySession.Start(start with { Columns = (short)columns, Rows = (short)rows }, this);

        // Let the shell come up before sending its startup commands. Writing them the
        // instant the process exists is a race: the shell is not reading yet, and a command
        // delivered too early is simply lost — which showed up as the prompt override
        // silently not applying.
        WaitForFirstOutput(TimeSpan.FromSeconds(3));

        foreach (var command in setup)
        {
            _session.Write(Encoding.UTF8.GetBytes(command + "\r"));
            Thread.Sleep(120);
        }

        if (scripted)
        {
            return RunScript(script, runFor <= 0 ? 10.0 : runFor, reportPath);
        }

        var input = new Thread(() => InputLoop(emulator))
        {
            IsBackground = true,
            Name = "baudacious-console-input",
        };

        input.Start();

        try
        {
            RenderLoop();
        }
        finally
        {
            _running = false;
            _session.Dispose();
            Console.Write("\u001b[0m\u001b[2J\u001b[H\u001b[?25h");
            Console.Out.Flush();
        }

        return 0;
    }

    /// <summary>Waits until the shell has produced anything at all, or the timeout expires.</summary>
    private void WaitForFirstOutput(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline && _ring.IsEmpty)
        {
            Thread.Sleep(10);
        }

        // A little longer, so the shell has finished its start-up chatter and is sitting at
        // a prompt rather than mid-banner.
        Thread.Sleep(250);
    }

    private int RunScript(List<string> commands, double seconds, string? reportPath)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // Let the shell settle, metering as we go.
        Pump(clock, 1.5);

        foreach (var command in commands)
        {
            _session!.Write(Encoding.UTF8.GetBytes(command + "\r"));
            Pump(clock, seconds / commands.Count);
        }

        // Drain whatever is still on the wire, so the grid reflects the whole session.
        var drainDeadline = clock.Elapsed.TotalSeconds + 120;
        while (_governor.PendingBytes > 0 && clock.Elapsed.TotalSeconds < drainDeadline)
        {
            _governor.Tick(_parser);
            Thread.Sleep(2);
        }

        _governor.Tick(_parser);

        var report = new StringBuilder();
        report.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"# rate {_governor.Rate}, released {_governor.TotalReleased} bytes in {clock.Elapsed.TotalSeconds:F2}s"));
        report.AppendLine($"# title: {_title}");
        report.AppendLine("# ---- screen ----");
        report.AppendLine(_screen.ToText());
        report.AppendLine("# ---- scrollback ----");
        report.Append(_screen.ScrollbackText());

        if (reportPath is null)
        {
            Console.Write(report);
        }
        else
        {
            File.WriteAllText(reportPath, report.ToString());
        }

        _running = false;
        _session!.Dispose();
        return 0;
    }

    private void Pump(System.Diagnostics.Stopwatch clock, double seconds)
    {
        var until = clock.Elapsed.TotalSeconds + seconds;
        while (clock.Elapsed.TotalSeconds < until)
        {
            _governor.Tick(_parser);
            Thread.Sleep(2);
        }
    }

    private void RenderLoop()
    {
        while (_running)
        {
            var released = _governor.Tick(_parser);
            if (released > 0)
            {
                _dirty = true;
            }

            if (_session?.TryGetExitCode() is { } code && _governor.PendingBytes == 0)
            {
                _renderer.Render(_screen, Status($"shell exited ({code}) — any key to quit"));
                Console.ReadKey(intercept: true);
                return;
            }

            if (_dirty)
            {
                _dirty = false;
                _renderer.Render(_screen, Status(null));
            }

            Thread.Sleep(8);
        }
    }

    private string Status(string? message)
    {
        if (message is null && Environment.TickCount64 < _noticeUntil)
        {
            message = _notice;
        }

        var pending = _governor.PendingBytes;
        var backlog = pending == 0
            ? "idle"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{pending} pending, {_governor.EstimatedDrain.TotalSeconds:F1}s to drain");

        var head = string.Create(
            CultureInfo.InvariantCulture,
            $" {_governor.Rate}  [{backlog}]  Alt+B baud  Alt+F flush-ahead  Alt+P phosphor  Alt+X quit ");

        return message is null ? head + _title : head + "  " + message;
    }

    private void Notify(string message)
    {
        _notice = message;
        _noticeUntil = Environment.TickCount64 + 2500;
        _dirty = true;
    }

    // ---- Input ---------------------------------------------------------------

    private void InputLoop(TerminalEmulator emulator)
    {
        while (_running)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Modifiers.HasFlag(ConsoleModifiers.Alt) && HandleLocalCommand(key))
            {
                continue;
            }

            // Ctrl+C interrupts the shell and discards the backlog: the user is never held
            // hostage by output they have already abandoned.
            if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.C)
            {
                var dropped = _governor.Flush();
                _parser.Reset();
                _session?.Write([0x03]);
                Notify(dropped > 0 ? $"interrupted, {dropped} queued bytes dropped" : "interrupted");
                continue;
            }

            var bytes = KeyEncoder.Encode(key, emulator.ApplicationCursorKeys);
            if (!bytes.IsEmpty)
            {
                _session?.Write(bytes.Span);
            }
        }
    }

    private bool HandleLocalCommand(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.B:
                CycleBaud();
                return true;

            case ConsoleKey.F:
                var pending = _governor.PendingBytes;
                _governor.FastForward();
                Notify(pending > 0 ? $"fast-forwarding {pending} bytes" : "nothing queued");
                return true;

            case ConsoleKey.P:
                _renderer.Phosphor = (Phosphor)(((int)_renderer.Phosphor + 1) % 4);
                _renderer.Invalidate();
                Notify($"phosphor: {_renderer.Phosphor}");
                return true;

            case ConsoleKey.X:
                _running = false;
                return true;

            default:
                return false;
        }
    }

    private void CycleBaud()
    {
        var rates = LineRate.StandardBauds;
        var current = Array.IndexOf(rates, _governor.Rate.Baud);
        var next = rates[(current + 1) % rates.Length];
        _governor.Rate = LineRate.Of(next);
        Notify($"line rate now {_governor.Rate}");
    }

    // ---- IPtyOutputSink ------------------------------------------------------

    public void OnBytes(ReadOnlySpan<byte> bytes, long timestamp) => _ring.Write(bytes);

    public void OnClosed() => _dirty = true;

    // ---- ITerminalHost -------------------------------------------------------

    public void Reply(ReadOnlySpan<byte> bytes) => _session?.Write(bytes);

    public void SetTitle(string title)
    {
        _title = title;
        _dirty = true;
    }

    public void Bell() => _dirty = true;

    // ---- Profiles -------------------------------------------------------------

    private static (PtyStartInfo Start, string[] Setup) BuildProfile(string profile, bool fastEcho) =>
        ShellProfiles.Build(profile, fastEcho);

    private static void Usage() =>
        Console.Error.WriteLine("""
            usage: Baudacious.App shell [options]

              -b, --baud <rate>      line rate, or 'unlimited' (default 1200)
              -s, --shell <name>     pwsh (default) or cmd
              -p, --phosphor <name>  green (default), amber, white, colour
                  --rich-editor      keep PSReadLine; costs ~29 bytes per keystroke
                                     instead of 1, so typing lags below 9600 baud

            While running:
              Alt+B  step through the standard line rates
              Alt+F  fast-forward the queued backlog
              Alt+P  cycle the phosphor
              Alt+X  quit
              Ctrl+C interrupt the shell and drop the backlog
            """);
}
