namespace Baudacious.Render;

/// <summary>
/// How much the display pretends to be a cathode-ray tube.
/// </summary>
/// <remarks>
/// Each effect has its own intensity so they can be judged separately, and the whole pass
/// can be switched off. An aesthetic that makes the terminal harder to read has failed, so
/// the defaults are deliberately restrained — the point of reference is a monitor in a lit
/// room, not a film prop.
/// </remarks>
public sealed record CrtSettings
{
    /// <summary>Everything off: text is blitted straight to the screen.</summary>
    public static readonly CrtSettings Off = new() { Enabled = false };

    public bool Enabled { get; init; } = true;

    /// <summary>Phosphor bleed around lit pixels. 0 is none.</summary>
    public double Bloom { get; init; } = 0.55;

    /// <summary>
    /// Depth of the dark gap between raster lines.
    /// </summary>
    /// <remarks>
    /// The scanline period is one source pixel row, so this needs at least 2x magnification
    /// to be visible at all: at 1x every output row samples the same phase and the effect
    /// degrades to a slight uniform dimming rather than banding.
    /// </remarks>
    public double Scanline { get; init; } = 0.30;

    /// <summary>Darkening towards the corners.</summary>
    public double Vignette { get; init; } = 0.35;

    /// <summary>A tube in a lit room is never quite black.</summary>
    public double AmbientGlow { get; init; } = 0.02;
}
