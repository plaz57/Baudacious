using System.Text;
using Baudacious.Pty;

namespace Baudacious.ConPtySpike;

/// <summary>
/// Runs the Phase 0 measurement scenarios and writes a capture per (profile, scenario).
/// </summary>
/// <remarks>
/// Must be launched with its own process/console context (Start-Process or a shortcut),
/// not from a harness that pipes stdio: a hosted shell whose console attachment is
/// confused by an inherited redirected console exits with status 0 shortly after
/// printing its prompt, and every measurement then reads as zero.
/// </remarks>
internal static class Recording
{
    private const int QuietMs = 400;
    private const int QuietTimeoutMs = 20_000;

    public static int Run(ReadOnlySpan<string> args)
    {
        var outputDir = args.Length > 0 ? args[0] : "captures";
        Directory.CreateDirectory(outputDir);

        var results = new List<Measurement>();

        foreach (var profile in Profiles())
        {
            Console.WriteLine();
            Console.WriteLine($"### profile: {profile.Name}");

            try
            {
                results.Add(MeasureIdle(profile, outputDir));
                results.Add(MeasureKeystrokes(profile, outputDir));
                results.Add(MeasurePromptRedraw(profile, outputDir));
                results.Add(MeasureCommand(profile, outputDir, "listing", profile.ListingCommand));
                results.Add(MeasureCommand(profile, outputDir, "color", profile.ColorCommand));
                results.Add(MeasureCommand(profile, outputDir, "deep-listing", profile.DeepListingCommand));

                if (GitFixture() is { } repo)
                {
                    results.Add(MeasureCommand(
                        profile, outputDir, "git-status",
                        $"git -C \"{repo}\" -c color.status=always -c color.ui=always status"));
                }

                results.Add(MeasureAltBuffer(profile, outputDir));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  !! {profile.Name} failed: {ex.Message}");
            }
        }

        var reportPath = Path.Combine(outputDir, "measurements.tsv");
        using (var report = new StreamWriter(reportPath))
        {
            report.WriteLine("profile\tscenario\tevents\tbytes\tbytes_per_event\tchunks\twall_ms\tnotes");
            foreach (var m in results)
            {
                report.WriteLine(
                    $"{m.Profile}\t{m.Scenario}\t{m.Events}\t{m.Bytes}\t{m.BytesPerEvent:F1}\t{m.Chunks}\t{m.WallMs}\t{m.Notes}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"=== wrote {results.Count} measurements to {reportPath} ===");
        foreach (var m in results)
        {
            Console.WriteLine($"  {m.Profile,-14} {m.Scenario,-14} {m.Bytes,9} bytes  {m.BytesPerEvent,8:F1}/event  {m.Notes}");
        }

        return 0;
    }

    private static IEnumerable<Profile> Profiles()
    {
        string? pwsh = null;
        try
        {
            pwsh = ShellProfiles.ResolvePwsh();
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine($"  (skipping pwsh profiles: {ex.Message})");
        }

        if (pwsh is not null)
        {
            // Defaults: profile loaded, PSReadLine predictions and syntax coloring on.
            // This is the worst case and the one design.md D2 has to survive.
            yield return new Profile(
                "pwsh-default",
                new PtyStartInfo { Executable = pwsh, Arguments = "-NoLogo" },
                Startup: [],
                ListingCommand: "Get-ChildItem",
                ColorCommand: "Get-ChildItem | Format-Table",
                DeepListingCommand: "Get-ChildItem -Recurse -Force -ErrorAction SilentlyContinue");

            // Tamed, per specs/shell-hosting: no predictions, no syntax color, plain prompt.
            yield return new Profile(
                "pwsh-tamed",
                new PtyStartInfo { Executable = pwsh, Arguments = "-NoLogo -NoProfile" },
                Startup: ShellProfiles.TamingCommands,
                ListingCommand: "Get-ChildItem",
                ColorCommand: "Get-ChildItem | Format-Table",
                DeepListingCommand: "Get-ChildItem -Recurse -Force -ErrorAction SilentlyContinue");
        }

        yield return new Profile(
            "cmd",
            ShellProfiles.Cmd(),
            Startup: [],
            ListingCommand: "dir",
            ColorCommand: "dir /a",
            DeepListingCommand: "dir /s");
    }

    private sealed record Profile(
        string Name,
        PtyStartInfo StartInfo,
        string[] Startup,
        string ListingCommand,
        string ColorCommand,
        string DeepListingCommand);

    private sealed record Measurement(
        string Profile,
        string Scenario,
        int Events,
        long Bytes,
        int Chunks,
        long WallMs,
        string Notes)
    {
        public double BytesPerEvent => Events == 0 ? Bytes : (double)Bytes / Events;
    }

    /// <summary>Opens a session, runs the startup commands, and waits for the prompt to settle.</summary>
    private static (PtySession Session, CaptureRecorder Recorder, AutoResponder Responder) Open(
        Profile profile, string outputDir, string scenario)
    {
        var recorder = new CaptureRecorder(
            Path.Combine(outputDir, $"{profile.Name}-{scenario}"),
            $"{profile.Name} / {scenario}");

        var responder = new AutoResponder(recorder);
        var session = PtySession.Start(profile.StartInfo with { Columns = 100, Rows = 30 }, responder);
        responder.Attach(session);

        Quiesce(recorder);

        foreach (var command in profile.Startup)
        {
            Send(session, command);
            Quiesce(recorder);
        }

        recorder.Annotate($"scenario={scenario} start");
        recorder.MarkAndCount();
        return (session, recorder, responder);
    }

    private static void Send(PtySession session, string text) =>
        session.Write(Encoding.UTF8.GetBytes(text + "\r"));

    /// <summary>Waits until no bytes have arrived for <see cref="QuietMs"/>.</summary>
    private static void Quiesce(CaptureRecorder recorder)
    {
        var deadline = Environment.TickCount64 + QuietTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(50);
            if (Environment.TickCount64 - recorder.LastArrivalTick >= QuietMs)
            {
                return;
            }
        }
    }

    /// <summary>Unsolicited traffic while sitting at a prompt with nobody typing.</summary>
    private static Measurement MeasureIdle(Profile profile, string outputDir)
    {
        var (session, recorder, _) = Open(profile, outputDir, "idle");
        using var _s = session;
        using var _r = recorder;

        var start = Environment.TickCount64;
        Thread.Sleep(5000);
        var bytes = recorder.MarkAndCount();
        var wall = Environment.TickCount64 - start;

        var note = bytes == 0
            ? "silent at rest"
            : $"NOT silent: {bytes} bytes over {wall} ms";
        Report(profile.Name, "idle", bytes, note);
        return new Measurement(profile.Name, "idle", 1, bytes, recorder.ChunkCount, wall, note);
    }

    /// <summary>
    /// Bytes of echo traffic caused by one keystroke. The number design.md D2 hinges on:
    /// a line editor that repaints the whole input line makes typing expensive at low baud.
    /// </summary>
    private static Measurement MeasureKeystrokes(Profile profile, string outputDir)
    {
        const string Typed = "Get-ChildItem -Path .";
        var (session, recorder, _) = Open(profile, outputDir, "keystrokes");
        using var _s = session;
        using var _r = recorder;

        var start = Environment.TickCount64;
        long total = 0;
        var perKey = new List<long>(Typed.Length);

        foreach (var c in Typed)
        {
            session.Write(Encoding.UTF8.GetBytes(c.ToString()));
            Quiesce(recorder);
            var cost = recorder.MarkAndCount();
            perKey.Add(cost);
            total += cost;
        }

        // Abandon the line rather than run it.
        session.Write([0x03]);
        Quiesce(recorder);

        var wall = Environment.TickCount64 - start;
        var max = perKey.Count == 0 ? 0 : perKey.Max();
        var note = $"min={(perKey.Count == 0 ? 0 : perKey.Min())} max={max} over {Typed.Length} keys";
        Report(profile.Name, "keystrokes", total, $"{(double)total / Typed.Length:F1} bytes/key, {note}");
        return new Measurement(profile.Name, "keystrokes", Typed.Length, total, recorder.ChunkCount, wall, note);
    }

    /// <summary>Cost of one prompt redraw: press Enter on an empty line, ten times.</summary>
    private static Measurement MeasurePromptRedraw(Profile profile, string outputDir)
    {
        const int Presses = 10;
        var (session, recorder, _) = Open(profile, outputDir, "prompt");
        using var _s = session;
        using var _r = recorder;

        var start = Environment.TickCount64;
        long total = 0;
        for (var i = 0; i < Presses; i++)
        {
            session.Write("\r"u8);
            Quiesce(recorder);
            total += recorder.MarkAndCount();
        }

        var wall = Environment.TickCount64 - start;
        Report(profile.Name, "prompt", total, $"{(double)total / Presses:F1} bytes per prompt redraw");
        return new Measurement(profile.Name, "prompt", Presses, total, recorder.ChunkCount, wall,
            $"{(double)total / Presses:F1} bytes/redraw");
    }

    private static Measurement MeasureCommand(Profile profile, string outputDir, string scenario, string command)
    {
        var (session, recorder, _) = Open(profile, outputDir, scenario);
        using var _s = session;
        using var _r = recorder;

        var start = Environment.TickCount64;
        Send(session, command);
        Quiesce(recorder);
        var bytes = recorder.MarkAndCount();
        var wall = Environment.TickCount64 - start;

        Report(profile.Name, scenario, bytes, $"`{command}`");
        return new Measurement(profile.Name, scenario, 1, bytes, recorder.ChunkCount, wall, command);
    }

    /// <summary>The scratch repository used for the colorised git capture, if it exists.</summary>
    private static string? GitFixture()
    {
        var path = Environment.GetEnvironmentVariable("BAUDACIOUS_GIT_FIXTURE");
        return path is not null && Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// A full-screen program entering and leaving the alternate screen buffer. Uses vim
    /// when Git for Windows supplied one; otherwise drives the sequences directly, which
    /// is a less realistic but still useful parser fixture.
    /// </summary>
    private static Measurement MeasureAltBuffer(Profile profile, string outputDir)
    {
        var vim = Environment.GetEnvironmentVariable("BAUDACIOUS_VIM");
        var haveVim = vim is not null && File.Exists(vim);

        var (session, recorder, _) = Open(profile, outputDir, "altbuffer");
        using var _s = session;
        using var _r = recorder;

        var start = Environment.TickCount64;

        if (haveVim)
        {
            // pwsh needs the call operator to execute a quoted path; cmd must not have it.
            var invoke = profile.Name.StartsWith("pwsh", StringComparison.Ordinal) ? "& " : string.Empty;
            Send(session, $"{invoke}\"{vim}\" -u NONE .");
            Thread.Sleep(2500);
            session.Write(":q!\r"u8);
            Quiesce(recorder);
        }
        else
        {
            // 1049 = save cursor, switch to alternate buffer, clear.
            var draw = profile.Name == "cmd"
                ? "echo [?1049h[2J[5;10HALT BUFFER[?1049l"
                : "[Console]::Write(\"`e[?1049h`e[2J`e[5;10HALT BUFFER\"); Start-Sleep -Milliseconds 800; [Console]::Write(\"`e[?1049l\")";
            Send(session, draw);
            Quiesce(recorder);
        }

        var bytes = recorder.MarkAndCount();
        var wall = Environment.TickCount64 - start;
        var note = haveVim ? "vim -u NONE" : "synthetic 1049 sequences";
        Report(profile.Name, "altbuffer", bytes, note);
        return new Measurement(profile.Name, "altbuffer", 1, bytes, recorder.ChunkCount, wall, note);
    }

    private static void Report(string profile, string scenario, long bytes, string note) =>
        Console.WriteLine($"  {profile,-14} {scenario,-14} {bytes,9} bytes   {note}");
}
