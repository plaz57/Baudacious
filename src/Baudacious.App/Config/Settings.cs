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

    public LineRate Rate { get; init; } = LineRate.Of(1200);

    /// <summary>The shell profile a new window starts with.</summary>
    public string Profile { get; init; } = "pwsh";

    /// <summary>Null lets the rate decide; see the line-discipline notes on the window.</summary>
    public bool? FastEcho { get; init; }

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
