namespace Baudacious.Core.Commands;

/// <summary>
/// How fast a smooth scroll slides, in lines per second, and the bounds everything agrees on.
/// </summary>
/// <remarks>
/// One place, for the same reason <see cref="CrtEffect"/> is one place: the command line,
/// the configuration file and the local prompt all take this value, and three copies of a
/// range is three chances for them to disagree about what is valid.
/// </remarks>
public static class SmoothScrollRate
{
    /// <summary>Zero, meaning the jump scroll: the image advances a whole cell at once.</summary>
    public const double Off = 0;

    /// <summary>
    /// The default. Chosen against the default 4800 baud, where the wire itself delivers
    /// about six lines a second, so the animation is never what you are waiting for. That
    /// is roughly three times what DEC shipped, which was authentic and which everybody
    /// turned off.
    /// </summary>
    public const double Default = 20;

    /// <summary>What a VT100 or VT220 managed: two scan lines per refresh, near enough.</summary>
    public const double Period = 6;

    /// <summary>
    /// Past two cells a frame there is nothing left to see, so this is the edge of useful
    /// rather than the edge of possible.
    /// </summary>
    public const double Ceiling = 120;

    /// <summary>Holds a requested rate inside the range, treating anything negative as off.</summary>
    public static double Clamp(double rate) => Math.Clamp(rate, Off, Ceiling);
}
