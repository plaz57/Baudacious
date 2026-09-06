using Baudacious.Pty;

namespace Baudacious.App;

/// <summary>
/// The v1 shell profiles and their startup commands.
/// </summary>
internal static class ShellProfiles
{
    /// <summary>
    /// Below this rate the rich line editor is swapped for the console's cooked input by
    /// default, because its per-keystroke cost stops being an annoyance and starts being
    /// unusable. See phase-0-findings.md §4.
    /// </summary>
    public const int FastEchoThresholdBaud = 4800;

    public static (PtyStartInfo Start, string[] Setup) Build(string profile, bool fastEcho)
    {
        if (profile.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            // cmd already echoes one byte per keystroke; there is nothing to tame.
            return (
                new PtyStartInfo { Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe") },
                []);
        }

        var start = new PtyStartInfo { Executable = ResolvePwsh(), Arguments = "-NoLogo -NoProfile" };

        // Fast echo: removing PSReadLine drops pwsh from 96.9 bytes per keystroke to 1.0,
        // matching cmd, because PowerShell then falls back to the console's cooked line
        // input. The cost is syntax color, prediction, history search and probably tab
        // completion — which is why it is a mode rather than a fate.
        return fastEcho
            ? (start, ["Remove-Module PSReadLine -ErrorAction SilentlyContinue", "function prompt { 'PS> ' }"])
            : (start, ["Set-PSReadLineOption -PredictionSource None", "function prompt { 'PS> ' }"]);
    }

    public static string ResolvePwsh()
    {
        var candidates = new List<string>();
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(directory.Trim(), "pwsh.exe"));
        }

        candidates.Add(@"C:\Program Files\PowerShell\7\pwsh.exe");

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
                // A malformed PATH entry; skip it.
            }
        }

        throw new FileNotFoundException(
            $"Could not find pwsh.exe (looked in {candidates.Count} places). Try --shell cmd.");
    }
}
