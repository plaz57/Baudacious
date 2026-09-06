using Baudacious.Pty;

namespace Baudacious.ConPtySpike;

internal static class ShellProfiles
{
    /// <summary>
    /// pwsh with everything left at its defaults — the worst case, and the one that
    /// decides whether design.md D2 survives contact with reality.
    /// </summary>
    public static PtyStartInfo PwshDefault(short cols = 80, short rows = 24) => new()
    {
        Executable = ResolvePwsh(),
        Arguments = "-NoLogo",
        Columns = cols,
        Rows = rows,
    };

    /// <summary>
    /// pwsh with the readline taming from specs/shell-hosting applied: no predictive
    /// suggestions, no syntax coloring, plain prompt.
    /// </summary>
    public static PtyStartInfo PwshTamed(short cols = 80, short rows = 24) => new()
    {
        Executable = ResolvePwsh(),
        Arguments = "-NoLogo -NoProfile",
        Columns = cols,
        Rows = rows,
    };

    public static PtyStartInfo Cmd(short cols = 80, short rows = 24) => new()
    {
        Executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe"),
        Columns = cols,
        Rows = rows,
    };

    /// <summary>
    /// Startup commands for the "fast echo" pwsh profile: drop PSReadLine so PowerShell
    /// falls back to the console host's cooked line input — the same line discipline
    /// cmd.exe uses. Measured at 1.0 bytes per keystroke, exact parity with cmd, against
    /// 96.9 for stock pwsh. See phase-0-findings.md §4.
    /// </summary>
    public static readonly string[] FastEchoCommands =
    [
        "Remove-Module PSReadLine -ErrorAction SilentlyContinue",
        "function prompt { 'PS> ' }",
    ];

    /// <summary>
    /// Startup commands for the tamed-but-full-featured pwsh profile: keeps PSReadLine
    /// and its completion and history, but turns off prediction and syntax color.
    /// Measured at 28.5 bytes per keystroke against 96.9 stock — a 71% reduction, but
    /// still 28x cmd.exe.
    /// </summary>
    public static readonly string[] TamingCommands =
    [
        "Set-PSReadLineOption -PredictionSource None",

        // Built with [char]27 and concatenation on purpose. A single-quoted PowerShell
        // string does not interpolate, so an earlier version of this passed the literal
        // text "$([char]27)[0m" as a color and silently did nothing at all.
        "$e=[char]27; $c=@{}; foreach($k in 'Command','Comment','Keyword','String'," +
        "'Operator','Variable','Parameter','Type','Number','Member','Default','Error'," +
        "'Selection','InlinePrediction','ListPrediction','ListPredictionSelected')" +
        "{$c[$k]=($e+'[0m')}; Set-PSReadLineOption -Colors $c",

        "function prompt { 'PS> ' }",
    ];

    /// <summary>
    /// Finds pwsh. specs/shell-hosting requires that a missing executable is reported
    /// with the path we looked for rather than crashing the app.
    /// </summary>
    public static string ResolvePwsh()
    {
        var candidates = new List<string>();

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(dir.Trim(), "pwsh.exe"));
        }

        candidates.Add(@"C:\Program Files\PowerShell\7\pwsh.exe");
        candidates.Add(@"C:\Program Files (x86)\PowerShell\7\pwsh.exe");

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry. Skip it.
            }
        }

        throw new FileNotFoundException(
            $"Could not find pwsh.exe. Looked in {candidates.Count} locations including " +
            @"'C:\Program Files\PowerShell\7\pwsh.exe'.");
    }
}
