using System.Diagnostics;
using System.Text;
using Baudacious.Pty;

namespace Baudacious.App;

/// <summary>
/// The shell profiles Baudacious ships with, and how each is launched.
/// </summary>
internal static class ShellProfiles
{
    /// <summary>
    /// Below this rate a rich line editor stops being an annoyance and starts being
    /// unusable, so the cheaper line discipline becomes the default. See
    /// phase-0-findings.md section 4.
    /// </summary>
    public const int FastEchoThresholdBaud = 4800;

    /// <summary>Profile names accepted on the command line.</summary>
    public static readonly string[] Names = ["pwsh", "cmd", "wsl"];

    /// <summary>
    /// Builds a launch for a profile name. <c>wsl</c> may name a distribution after a
    /// colon, as in <c>wsl:Ubuntu</c>; without one, WSL picks its default.
    /// </summary>
    public static (PtyStartInfo Start, string[] Setup) Build(string profile, bool fastEcho)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var (name, argument) = Split(profile);

        return name switch
        {
            "cmd" => BuildCmd(),
            "wsl" => BuildWsl(argument),
            _ => BuildPwsh(fastEcho),
        };
    }

    private static (string Name, string? Argument) Split(string profile)
    {
        var colon = profile.IndexOf(':', StringComparison.Ordinal);
        return colon < 0
            ? (profile.ToLowerInvariant(), null)
            : (profile[..colon].ToLowerInvariant(), profile[(colon + 1)..]);
    }

    private static (PtyStartInfo, string[]) BuildCmd() =>
        // cmd already echoes one byte per keystroke; there is nothing to tame.
        (new PtyStartInfo { Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe") }, []);

    private static (PtyStartInfo, string[]) BuildPwsh(bool fastEcho)
    {
        var start = new PtyStartInfo { Executable = ResolvePwsh(), Arguments = "-NoLogo -NoProfile" };

        // Fast echo: removing PSReadLine drops pwsh from 96.9 bytes per keystroke to 1.0,
        // matching cmd, because PowerShell then falls back to the console's cooked line
        // input. The cost is syntax color, prediction, history search and probably tab
        // completion — which is why it is a mode rather than a fate.
        return fastEcho
            ? (start, ["Remove-Module PSReadLine -ErrorAction SilentlyContinue", "function prompt { 'PS> ' }"])
            : (start, ["Set-PSReadLineOption -PredictionSource None", "function prompt { 'PS> ' }"]);
    }

    /// <summary>
    /// A Linux shell under the Windows Subsystem for Linux.
    /// </summary>
    /// <remarks>
    /// <c>wsl.exe</c> is an ordinary Windows console program, so it needs no special
    /// handling: ConPTY hosts it exactly as it hosts cmd, and the Linux side sees a proper
    /// terminal. Its output is UTF-8, which the parser already decodes, and box-drawing and
    /// accented characters therefore arrive intact.
    /// <para>
    /// No taming is needed, and that is measured rather than assumed: bash echoes
    /// <b>1.0 bytes per keystroke</b>, min 1 and max 1 — identical to cmd.exe and a
    /// hundredth of stock pwsh. readline only repaints the line for operations that need it
    /// (history recall, completion), not for ordinary insertion, which is precisely where
    /// PSReadLine differs. WSL is therefore comfortable to type into at every supported
    /// rate, 300 baud included.
    /// </para>
    /// </remarks>
    private static (PtyStartInfo, string[]) BuildWsl(string? distribution)
    {
        var executable = ResolveWsl();

        // -- separates WSL's own switches from the command, so a distribution named like a
        // switch cannot be misread as one.
        var arguments = string.IsNullOrWhiteSpace(distribution)
            ? string.Empty
            : $"-d \"{distribution}\"";

        return (new PtyStartInfo { Executable = executable, Arguments = arguments }, []);
    }

    /// <summary>Locates wsl.exe, preferring the real one over the app-execution alias.</summary>
    public static string ResolveWsl()
    {
        var system32 = Path.Combine(Environment.SystemDirectory, "wsl.exe");
        if (File.Exists(system32))
        {
            return system32;
        }

        foreach (var directory in SearchPath())
        {
            var candidate = Path.Combine(directory, "wsl.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "Could not find wsl.exe. The Windows Subsystem for Linux does not appear to be " +
            "installed; `wsl --install` sets it up.");
    }

    /// <summary>
    /// The installed distributions, newest listing first, or empty when none are.
    /// </summary>
    /// <remarks>
    /// Only used to make failures legible. <c>wsl --list</c> writes UTF-16 rather than the
    /// UTF-8 everything else on this path uses, which is why the encoding is stated
    /// explicitly instead of inherited.
    /// </remarks>
    public static IReadOnlyList<string> ListDistributions()
    {
        try
        {
            var info = new ProcessStartInfo(ResolveWsl(), "--list --quiet")
            {
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.Unicode,
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(info);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return [.. output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim('\0', ' '))
                .Where(line => line.Length > 0)];
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    public static string ResolvePwsh()
    {
        var candidates = new List<string>();
        foreach (var directory in SearchPath())
        {
            candidates.Add(Path.Combine(directory, "pwsh.exe"));
        }

        candidates.Add(@"C:\Program Files\PowerShell\7\pwsh.exe");

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not find pwsh.exe (looked in {candidates.Count} places). Try --shell cmd.");
    }

    private static IEnumerable<string> SearchPath()
    {
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = entry.Trim();
            if (directory.Length == 0 || directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                continue;
            }

            yield return directory;
        }
    }
}
