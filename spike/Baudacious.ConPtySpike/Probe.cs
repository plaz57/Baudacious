using System.Text;
using Baudacious.Pty;

namespace Baudacious.ConPtySpike;

/// <summary>
/// Measures per-keystroke echo cost across candidate PowerShell configurations, to find
/// out how close pwsh can be brought to cmd.exe's one-byte-per-key line discipline.
/// </summary>
internal static class Probe
{
    private const string Typed = "Get-ChildItem -Path .";
    private const int QuietMs = 350;
    private const int QuietTimeoutMs = 15_000;

    /// <summary>
    /// Neutralises every PSReadLine syntax color. Built with [char]27 and string
    /// concatenation because a single-quoted PowerShell string does not interpolate —
    /// the first attempt at this passed the literal text "$([char]27)[0m" as a color
    /// and silently did nothing.
    /// </summary>
    private const string NeutralColors =
        "$e=[char]27; $c=@{}; foreach($k in 'Command','Comment','Keyword','String'," +
        "'Operator','Variable','Parameter','Type','Number','Member','Default','Error'," +
        "'Selection','InlinePrediction','ListPrediction','ListPredictionSelected')" +
        "{$c[$k]=($e+'[0m')}; Set-PSReadLineOption -Colors $c";

    public static int Run(ReadOnlySpan<string> args)
    {
        var outputDir = args.Length > 0 ? args[0] : "probe";
        Directory.CreateDirectory(outputDir);

        string pwsh;
        try
        {
            pwsh = ShellProfiles.ResolvePwsh();
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var candidates = new (string Name, PtyStartInfo Start, string[] Setup)[]
        {
            ("pwsh-default", Pwsh(pwsh, "-NoLogo"), []),

            ("pwsh-noprofile", Pwsh(pwsh, "-NoLogo -NoProfile"), []),

            ("pwsh-nopredict", Pwsh(pwsh, "-NoLogo -NoProfile"),
                ["Set-PSReadLineOption -PredictionSource None"]),

            ("pwsh-nocolor", Pwsh(pwsh, "-NoLogo -NoProfile"),
                ["Set-PSReadLineOption -PredictionSource None", NeutralColors]),

            ("pwsh-plainprompt", Pwsh(pwsh, "-NoLogo -NoProfile"),
                ["Set-PSReadLineOption -PredictionSource None", NeutralColors,
                 "function prompt { 'PS> ' }"]),

            // The interesting one: drop PSReadLine altogether so PowerShell falls back
            // to the host's cooked line input — the same console line discipline cmd.exe
            // uses.
            ("pwsh-no-psreadline", Pwsh(pwsh, "-NoLogo -NoProfile"),
                ["Remove-Module PSReadLine -ErrorAction SilentlyContinue",
                 "function prompt { 'PS> ' }"]),

            ("cmd (reference)", ShellProfiles.Cmd(), []),
        };

        Console.WriteLine($"{"configuration",-22}{"bytes/key",10}{"min",6}{"max",6}   vs cmd");
        Console.WriteLine(new string('-', 62));

        var results = new List<(string Name, double PerKey, long Min, long Max)>();
        foreach (var (name, start, setup) in candidates)
        {
            try
            {
                var r = Measure(name, start with { Columns = 100, Rows = 30 }, setup, outputDir);
                results.Add(r);
                Console.WriteLine(
                    $"{name,-22}{r.PerKey,10:F1}{r.Min,6}{r.Max,6}   {r.PerKey / 1.0,5:F0}x");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{name,-22}  failed: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Typing lag generated per keystroke (8N1):");
        Console.WriteLine($"{"configuration",-22}{"300",9}{"1200",9}{"2400",9}{"9600",9}");
        Console.WriteLine(new string('-', 62));
        foreach (var (name, perKey, _, _) in results)
        {
            Console.WriteLine(
                $"{name,-22}{Ms(perKey, 300),9}{Ms(perKey, 1200),9}{Ms(perKey, 2400),9}{Ms(perKey, 9600),9}");
        }

        return 0;
    }

    /// <summary>
    /// What still works once PSReadLine is gone? Types a partial cmdlet name, presses
    /// Tab, and reports what came back — the decisive usability question for making
    /// "no PSReadLine" a default profile.
    /// </summary>
    public static int TabTest(ReadOnlySpan<string> args)
    {
        var pwsh = ShellProfiles.ResolvePwsh();

        foreach (var (label, setup) in new (string, string[])[]
        {
            ("with PSReadLine", []),
            ("without PSReadLine", ["Remove-Module PSReadLine -ErrorAction SilentlyContinue"]),
        })
        {
            var sink = new MemorySink();
            var responder = new AutoResponder(sink);
            using var session = PtySession.Start(
                Pwsh(pwsh, "-NoLogo -NoProfile") with { Columns = 100, Rows = 30 }, responder);
            responder.Attach(session);
            sink.WaitQuiet();

            foreach (var c in setup)
            {
                session.Write(Encoding.UTF8.GetBytes(c + "\r"));
                sink.WaitQuiet();
            }

            session.Write("Get-ChildIt"u8);
            sink.WaitQuiet();
            sink.Reset();

            session.Write("\t"u8);
            sink.WaitQuiet();

            var text = sink.Text;
            var completed = text.Contains("Get-ChildItem", StringComparison.Ordinal)
                            || text.Contains("em", StringComparison.Ordinal);

            Console.WriteLine($"{label,-22} tab -> {sink.Bytes,4} bytes; completed: {completed,-5}  {Escape(text)}");
        }

        return 0;
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '\u001b' => "<ESC>",
                '\r' => "<CR>",
                '\n' => "<LF>",
                '\a' => "<BEL>",
                < ' ' => $"<{(int)c:X2}>",
                _ => c.ToString(),
            });
        }

        var s = sb.ToString();
        return s.Length > 140 ? s[..140] + "..." : s;
    }

    /// <summary>Keeps arriving bytes in memory so nothing has to be read back off disk.</summary>
    private sealed class MemorySink : IPtyOutputSink
    {
        private readonly List<byte> _buffer = [];
        private readonly Lock _gate = new();
        private long _lastTick = Environment.TickCount64;

        public int Bytes { get { lock (_gate) { return _buffer.Count; } } }

        public string Text { get { lock (_gate) { return Encoding.UTF8.GetString(_buffer.ToArray()); } } }

        public void OnBytes(ReadOnlySpan<byte> bytes, long timestamp)
        {
            lock (_gate)
            {
                _buffer.AddRange(bytes);
                _lastTick = Environment.TickCount64;
            }
        }

        public void OnClosed()
        {
        }

        public void Reset()
        {
            lock (_gate)
            {
                _buffer.Clear();
            }
        }

        public void WaitQuiet()
        {
            var deadline = Environment.TickCount64 + QuietTimeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(40);
                lock (_gate)
                {
                    if (Environment.TickCount64 - _lastTick >= QuietMs)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static string Ms(double bytesPerKey, int baud)
    {
        var ms = bytesPerKey / (baud / 10.0) * 1000.0;
        return ms >= 1000 ? $"{ms / 1000:F2}s" : $"{ms:F0}ms";
    }

    private static PtyStartInfo Pwsh(string exe, string arguments) =>
        new() { Executable = exe, Arguments = arguments };

    private static (string Name, double PerKey, long Min, long Max) Measure(
        string name, PtyStartInfo start, string[] setup, string outputDir)
    {
        using var recorder = new CaptureRecorder(Path.Combine(outputDir, name.Replace(' ', '-')), name);
        var responder = new AutoResponder(recorder);
        using var session = PtySession.Start(start, responder);
        responder.Attach(session);

        Quiesce(recorder);
        foreach (var command in setup)
        {
            session.Write(Encoding.UTF8.GetBytes(command + "\r"));
            Quiesce(recorder);
        }

        recorder.Annotate("keystroke measurement begins");
        recorder.MarkAndCount();

        long total = 0;
        long min = long.MaxValue;
        long max = 0;
        foreach (var c in Typed)
        {
            session.Write(Encoding.UTF8.GetBytes(c.ToString()));
            Quiesce(recorder);
            var cost = recorder.MarkAndCount();
            total += cost;
            min = Math.Min(min, cost);
            max = Math.Max(max, cost);
        }

        session.Write([0x03]);
        Quiesce(recorder);

        return (name, (double)total / Typed.Length, min == long.MaxValue ? 0 : min, max);
    }

    private static void Quiesce(CaptureRecorder recorder)
    {
        var deadline = Environment.TickCount64 + QuietTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(40);
            if (Environment.TickCount64 - recorder.LastArrivalTick >= QuietMs)
            {
                return;
            }
        }
    }
}
