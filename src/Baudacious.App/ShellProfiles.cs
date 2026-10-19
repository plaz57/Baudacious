using System.Diagnostics;
using System.Text;
using Baudacious.App.Config;
using Baudacious.Pty;

namespace Baudacious.App;

/// <summary>
/// The shells Baudacious can host: the three it ships with, and any the user has described
/// in the configuration file.
/// </summary>
/// <remarks>
/// An instance because the set is not fixed. The measured constants and the executable
/// resolvers stay static — they do not depend on what anyone configured — but which names
/// exist, how each is launched, and which of them have a line editor worth taming all do.
/// </remarks>
public sealed class ShellProfiles
{
    /// <summary>
    /// Below this rate a rich line editor stops being an annoyance and starts being
    /// unusable, so the cheaper line discipline becomes the default. See
    /// phase-0-findings.md section 4.
    /// </summary>
    public const int FastEchoThresholdBaud = 4800;

    /// <summary>The value of <c>like</c> that borrows PowerShell's handling.</summary>
    public const string LikePwsh = "pwsh";

    /// <summary>The three that need no configuration.</summary>
    public static readonly string[] BuiltinNames = ["pwsh", "cmd", "wsl"];

    private readonly IReadOnlyList<ShellProfileConfig> _configured;
    private readonly bool _powerShellProfile;

    /// <param name="powerShellProfile">
    /// Whether PowerShell loads the user's own profile script. On by default, because the
    /// alternative is a shell that is not theirs.
    /// </param>
    public ShellProfiles(
        IReadOnlyList<ShellProfileConfig>? configured = null, bool powerShellProfile = true)
    {
        _configured = configured ?? [];
        _powerShellProfile = powerShellProfile;
    }

    /// <summary>The three built-ins and nothing else. For paths that read no settings.</summary>
    public static ShellProfiles Builtin { get; } = new();

    /// <summary>
    /// Every name that can be launched, built-ins first.
    /// </summary>
    /// <remarks>
    /// A configured profile may take a built-in's name, which is how someone points
    /// <c>pwsh</c> at a different PowerShell; it is listed once either way.
    /// </remarks>
    public IReadOnlyList<string> Names
    {
        get
        {
            var names = new List<string>(BuiltinNames);

            foreach (var profile in _configured)
            {
                if (!names.Contains(profile.Name, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(profile.Name);
                }
            }

            return names;
        }
    }

    /// <summary>Echo traffic a tamed pwsh generates per keystroke. Measured, not guessed.</summary>
    public const double RichEchoBytesPerKeystroke = 28.5;

    /// <summary>
    /// Characters a second an unhurried typist produces. Only used to turn the echo cost
    /// into the shape of the problem — seconds of wire per second of typing.
    /// </summary>
    public const double TypingCharsPerSecond = 4.0;

    /// <summary>
    /// The rate below which a profile's own line editor stops keeping up, or 0 where there
    /// is nothing to warn about.
    /// </summary>
    /// <remarks>
    /// Measured, not guessed; see phase-0-findings.md section 4. cmd and wsl echo 1.0 bytes
    /// per keystroke and are comfortable at 300 baud, so they have no floor. A tamed pwsh
    /// costs 28.5 bytes per keystroke, which is comfortable from 9600 baud and progressively
    /// worse below it.
    /// </remarks>
    public int UsableFloorBaud(string profile, bool fastEcho) =>
        !fastEcho && SupportsFastEcho(profile) ? 9600 : 0;

    /// <summary>
    /// Whether swapping the line editor for the console's cooked input means anything here.
    /// </summary>
    /// <remarks>
    /// Only PowerShell has an editor expensive enough to be worth removing. cmd and bash
    /// already echo one byte per keystroke, so there is nothing to trade away and nothing to
    /// gain. A configured profile opts in with <c>like = pwsh</c>, which is what that setting
    /// is for: pointing at a different PowerShell and getting the same treatment.
    /// </remarks>
    public bool SupportsFastEcho(string profile)
    {
        if (Find(profile) is { } configured)
        {
            return LikePwsh.Equals(configured.Like, StringComparison.OrdinalIgnoreCase);
        }

        return Split(profile).Name.StartsWith("pwsh", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The configured profile of that exact name, or null.</summary>
    private ShellProfileConfig? Find(string profile)
    {
        foreach (var candidate in _configured)
        {
            if (candidate.Name.Equals(profile, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// What to send a running shell to change its line discipline, without restarting it
    /// and so without losing its history or working directory.
    /// </summary>
    /// <remarks>
    /// PSReadLine works by supplying a <c>PSConsoleHostReadLine</c> function that the host
    /// calls instead of its own line reader. Removing the module removes that function and
    /// the host falls back to the console's cooked input; importing it puts the function
    /// back. Neither touches the session itself.
    /// </remarks>
    public static string[] FastEchoCommands(bool on) =>
        on
            ? ["Remove-Module PSReadLine -ErrorAction SilentlyContinue"]
            : ["Import-Module PSReadLine", "Set-PSReadLineOption -PredictionSource None"];

    /// <summary>
    /// Builds a launch for a profile name. <c>wsl</c> may name a distribution after a
    /// colon, as in <c>wsl:Ubuntu</c>; without one, WSL picks its default.
    /// </summary>
    /// <remarks>
    /// A configured profile is matched on its exact name and wins over a built-in of the
    /// same name. An unrecognised name <b>throws</b>, listing what does exist. It used to
    /// fall through to pwsh, which meant a typo — or a <c>[profile.gitbash]</c> section back
    /// when nothing consumed those — silently started the wrong shell. A shell that is not
    /// the one asked for is worse than a message saying so.
    /// </remarks>
    public (PtyStartInfo Start, string[] Setup) Build(string profile, bool fastEcho)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (Find(profile) is { } configured)
        {
            return BuildConfigured(configured, fastEcho);
        }

        var (name, argument) = Split(profile);

        return name switch
        {
            "pwsh" => BuildPwsh(fastEcho),
            "cmd" => BuildCmd(),
            "wsl" => BuildWsl(argument),
            _ => throw new InvalidOperationException(
                $"no shell profile named '{profile}'. Known: {string.Join(", ", Names)}. "
                + "Add one with a [profile.<name>] section in baudacious.ini."),
        };
    }

    /// <summary>
    /// A shell described in the configuration file.
    /// </summary>
    /// <remarks>
    /// The command is passed through as written, so a bare name is found on PATH the way
    /// any other program is, and a full path with spaces works without quoting rules of its
    /// own. <c>like = pwsh</c> appends the taming after the user's own arguments rather
    /// than replacing them, since they may have switches of their own that matter.
    /// </remarks>
    private static (PtyStartInfo, string[]) BuildConfigured(
        ShellProfileConfig profile, bool fastEcho)
    {
        var arguments = profile.Arguments;

        if (LikePwsh.Equals(profile.Like, StringComparison.OrdinalIgnoreCase))
        {
            var tame = fastEcho
                ? "Remove-Module PSReadLine -ErrorAction SilentlyContinue"
                : "Set-PSReadLineOption -PredictionSource None";

            arguments = $"{arguments} -NoExit -Command \"{tame}\"".TrimStart();
        }

        return (new PtyStartInfo { Executable = profile.Command, Arguments = arguments }, []);
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

    /// <summary>
    /// PowerShell, with its line editor tamed and its own prompt left alone.
    /// </summary>
    /// <remarks>
    /// Fast echo: removing PSReadLine drops pwsh from 96.9 bytes per keystroke to 1.0,
    /// matching cmd, because PowerShell then falls back to the console's cooked line input.
    /// The cost is syntax color, prediction and history search — not tab completion, which
    /// the console host provides itself and which was measured still working with the module
    /// gone. A mode, then, rather than a fate.
    /// <para>
    /// The setup runs through <c>-Command</c> with <c>-NoExit</c> rather than being typed at
    /// the prompt. Typed setup is echoed by the shell like anything else typed, so the first
    /// thing a new window showed was two lines of Baudacious's own configuration. Passing it
    /// as an argument runs it before the first prompt is drawn, which also removes the
    /// wait-for-first-output race that typing it needed.
    /// </para>
    /// <para>
    /// <b>What is deliberately no longer done:</b> replacing the prompt with <c>PS&gt;</c>.
    /// That was here from Phase 3 and it bought 0.9 bytes per keystroke — 29.4 down to 28.5,
    /// about 3% — in exchange for silently throwing away the working directory, which is the
    /// most useful thing a shell prompt tells you. A 3% saving is not worth a visible change
    /// to somebody else's shell. Anyone who wants a terse prompt at 300 baud can set one in
    /// their own profile.
    /// </para>
    /// </remarks>
    private (PtyStartInfo, string[]) BuildPwsh(bool fastEcho)
    {
        var tame = fastEcho
            ? "Remove-Module PSReadLine -ErrorAction SilentlyContinue"
            : "Set-PSReadLineOption -PredictionSource None";

        // The taming runs after the profile, which is the order that makes it taming: the
        // user sets up their shell, then Baudacious quiets the one part of it that costs
        // wire time.
        var profile = _powerShellProfile ? string.Empty : " -NoProfile";

        return (
            new PtyStartInfo
            {
                Executable = ResolvePwsh(),
                Arguments = $"-NoLogo{profile} -NoExit -Command \"{tame}\"",
            },
            []);
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
