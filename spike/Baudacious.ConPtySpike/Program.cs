using System.Diagnostics;
using System.Text;
using Baudacious.ConPtySpike;
using Baudacious.Pty;

// BAUDACIOUS_LOG lets the spike run as a windowed (console-less) process and still be
// observed — the configuration the real application will run in.
var allocatedConsole = Environment.GetEnvironmentVariable("BAUDACIOUS_ALLOC_CONSOLE") is not null
    && ConsoleAttachment.EnsureHidden();

if (Environment.GetEnvironmentVariable("BAUDACIOUS_LOG") is { } logPath)
{
    var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
    Console.SetOut(log);
    Console.SetError(log);
}
else
{
    Console.OutputEncoding = Encoding.UTF8;
}

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

return command switch
{
    "smoke" => Smoke(args.Length > 1 ? args[1] : "pwsh"),
    "record" => Recording.Run(args.AsSpan(1)),
    "dump" => Dump.Run(args.AsSpan(1)),
    "probe" => Probe.Run(args.AsSpan(1)),
    "tabtest" => Probe.TabTest(args.AsSpan(1)),
    _ => Help(),
};

static int Help()
{
    Console.WriteLine("""
        baudacious conpty spike (phase 0 — throwaway; see openspec/changes/baudacious-v1/tasks.md)

          smoke                     launch pwsh, echo a command, verify clean shutdown
          record [name]             run the measurement scenarios, write captures
          dump <capture.bin> [-n N] [--hex]   annotated view of a capture
          probe [dir]               compare per-keystroke echo cost across pwsh configs
        """);
    return 0;
}

static int Smoke(string shell)
{
    Console.WriteLine($"=== ConPTY smoke test (task 1.3) — {shell} ===");
    Console.WriteLine();

    PtyStartInfo startInfo;
    try
    {
        startInfo = shell.ToLowerInvariant() switch
        {
            "cmd" => ShellProfiles.Cmd(),

            // A process that writes steadily and never reads stdin. If this survives,
            // ConPTY output and process hosting are sound and the problem is specific
            // to shells reading their input.
            "ping" => new PtyStartInfo
            {
                Executable = Path.Combine(Environment.SystemDirectory, "ping.exe"),
                Arguments = "-n 8 127.0.0.1",
            },

            _ => ShellProfiles.PwshTamed(),
        };
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    Console.WriteLine($"exe:   {startInfo.Executable}");
    Console.WriteLine($"console: {ConsoleAttachment.Describe()}");

    var sink = new ConsoleSink(escapeVisible: true);
    var responder = new AutoResponder(sink);
    var session = PtySession.Start(startInfo, responder);
    responder.Attach(session);
    Console.WriteLine($"pid:   {session.ProcessId}");
    Console.WriteLine($"job:   {(session.JobAssignmentError == 0 ? "assigned" : $"FAILED err={session.JobAssignmentError}")}");

    Console.WriteLine($"grid:  {session.Columns}x{session.Rows}");
    Console.WriteLine();
    Console.WriteLine("--- shell output ---");

    // Poll for the moment the shell dies, so a premature exit is dated rather than guessed at.
    var poll = Stopwatch.StartNew();
    var diedEarly = false;
    for (var i = 0; i < 12; i++)
    {
        Thread.Sleep(100);
        if (session.TryGetExitCode() is { } early)
        {
            Console.WriteLine($"  !! shell exited with {early} at {poll.ElapsedMilliseconds} ms (before any input was sent)");
            diedEarly = true;
            break;
        }
    }

    Console.WriteLine();
    Console.WriteLine("--- sending 'echo hello-from-baudacious' ---");
    session.Write("echo hello-from-baudacious\r"u8);
    Console.WriteLine($"  (WriteFile accepted {session.BytesWritten} bytes, err={session.LastWriteError})");
    Thread.Sleep(1200);

    Console.WriteLine();
    Console.WriteLine("--- resize to 100x30 ---");
    session.Resize(100, 30);
    session.Write("$Host.UI.RawUI.WindowSize\r"u8);
    Thread.Sleep(1200);

    Console.WriteLine();
    Console.WriteLine("--- shutting down ---");

    var shutdown = Stopwatch.StartNew();
    session.Dispose();
    shutdown.Stop();

    // The point of the exercise: prove the ordering in PtySession.Dispose leaves
    // nothing hung and nothing orphaned.
    var orphaned = false;
    try
    {
        using var probe = Process.GetProcessById(session.ProcessId);
        orphaned = !probe.HasExited;
    }
    catch (ArgumentException)
    {
        // Process is gone, which is what we want.
    }

    var readerThreads = Process.GetCurrentProcess().Threads.Count;

    Console.WriteLine();
    Console.WriteLine("=== results ===");
    Console.WriteLine($"bytes received:     {sink.Bytes}");
    Console.WriteLine($"chunks:             {sink.Chunks}");
    Console.WriteLine($"reader saw EOF:     {sink.Closed}");
    Console.WriteLine($"shutdown took:      {shutdown.ElapsedMilliseconds} ms");
    Console.WriteLine($"shell orphaned:     {orphaned}");
    Console.WriteLine($"threads still up:   {readerThreads}");
    Console.WriteLine();

    var ok = sink.Closed && !orphaned && sink.Bytes > 0 && !diedEarly;
    Console.WriteLine(ok ? "PASS — shell stayed alive, clean shutdown, no hung reader, no orphan." : "FAIL");
    return ok ? 0 : 1;
}
