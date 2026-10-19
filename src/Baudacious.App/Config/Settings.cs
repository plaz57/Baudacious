using Baudacious.Core.Baud;
using Baudacious.Render;

namespace Baudacious.App.Config;

/// <summary>A shell the user has described in the configuration file.</summary>
/// <param name="Name">What to pass to <c>--shell</c> or <c>session new</c>.</param>
/// <param name="Command">The executable. Looked up on PATH if it is not a full path.</param>
/// <param name="Arguments">Passed as written.</param>
/// <param name="Like">
/// A built-in profile whose line-discipline handling to borrow, or null for none. Only
/// <c>pwsh</c> has any, so this exists to let a profile pointing at another PowerShell get
/// the fast-echo treatment.
/// </param>
public sealed record ShellProfileConfig(string Name, string Command, string Arguments, string? Like);

/// <summary>
/// How big the window opens, in cells rather than pixels.
/// </summary>
/// <remarks>
/// Cells, because the magnification is a setting too: 100x30 means the same amount of
/// terminal at 1x and at 4x, where a pixel size would mean a quarter of it.
/// </remarks>
public sealed record WindowGeometry
{
    public int Columns { get; init; } = 100;

    public int Rows { get; init; } = 30;

    public bool Fullscreen { get; init; }
}

/// <summary>
/// Everything configurable, resolved.
/// </summary>
/// <remarks>
/// One record for the whole application, built by starting from the defaults below and
/// layering the configuration file, then the persisted state, then the command line over
/// it. Each layer only replaces what it actually says, which is what makes the precedence
/// rule a consequence of the order rather than a thing to implement.
/// </remarks>
public sealed record Settings
{
    /// <summary>What Baudacious does with no configuration at all.</summary>
    public static Settings Defaults { get; } = new();

    /// <summary>
    /// The rate a new installation starts at.
    /// </summary>
    /// <remarks>
    /// 4800 is fast enough to work in and slow enough to watch — text arrives at 480
    /// characters a second, which reads as quick typing rather than as an instant paint.
    /// It also sits exactly at <c>ShellProfiles.FastEchoThresholdBaud</c>, so a fresh pwsh
    /// keeps its own line editor: the threshold is "below 4800", and this is not below it.
    /// Anyone who wants the crawl has <c>--baud 300</c> and Alt+B.
    /// </remarks>
    public LineRate Rate { get; init; } = LineRate.Of(4800);

    /// <summary>The shell profile a new window starts with.</summary>
    public string Profile { get; init; } = "pwsh";

    /// <summary>Null lets the rate decide; see the line-discipline notes on the window.</summary>
    public bool? FastEcho { get; init; }

    /// <summary>
    /// Whether PowerShell loads the user's own profile script on startup.
    /// </summary>
    /// <remarks>
    /// On, because a terminal that does not run your profile is not your shell: no aliases,
    /// no functions, no prompt, none of the things you put there. It was off from Phase 3,
    /// which is why an alias defined in <c>$PROFILE</c> did not exist in a Baudacious
    /// window.
    /// <para>
    /// Worth being able to turn off here, though, in a way it would not be in an ordinary
    /// terminal: a profile that prints a banner or sets up a themed prompt can be hundreds
    /// of bytes, and at 300 baud those are seconds of crawl before every new session.
    /// </para>
    /// </remarks>
    public bool PowerShellProfile { get; init; } = true;

    /// <summary>Palette, magnification, cursor and the CRT pass.</summary>
    public DisplaySettings Display { get; init; } = new();

    /// <summary>The bitmap typeface, by name.</summary>
    public string Font { get; init; } = "unscii";

    public int Scrollback { get; init; } = 10_000;

    public WindowGeometry Window { get; init; } = new();

    public KeyBindings Keys { get; init; } = KeyBindings.Default;

    /// <summary>Profiles from the configuration file, added to the built-in three.</summary>
    public IReadOnlyList<ShellProfileConfig> Profiles { get; init; } = [];
}
