namespace Baudacious.Core.Terminal;

/// <summary>Which mouse events a hosted program has asked to be told about.</summary>
public enum MouseTracking
{
    /// <summary>None. The mouse belongs to the terminal, for selection.</summary>
    Off,

    /// <summary>Mode 1000: presses and releases only.</summary>
    Click,

    /// <summary>Mode 1002: presses, releases, and motion while a button is down.</summary>
    Drag,

    /// <summary>Mode 1003: all of the above plus motion with no button down.</summary>
    Motion,
}

/// <summary>How a mouse report is framed on the wire.</summary>
public enum MouseEncoding
{
    /// <summary>
    /// The original form, <c>ESC [ M</c> followed by three bytes biased by 32.
    /// </summary>
    /// <remarks>
    /// A byte holds the coordinate plus 32, so it cannot express a position past column or
    /// row 223. Programs that care ask for <see cref="Sgr"/> instead.
    /// </remarks>
    Legacy,

    /// <summary>
    /// Mode 1006: <c>ESC [ &lt; button ; column ; row M</c>, or <c>m</c> for a release.
    /// Decimal, so coordinates have no ceiling, and press and release are distinguishable.
    /// </summary>
    Sgr,
}
