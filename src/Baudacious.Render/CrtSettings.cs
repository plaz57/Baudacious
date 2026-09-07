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
    public double Scanline { get; init; } = 0.50;

    /// <summary>Darkening towards the corners.</summary>
    public double Vignette { get; init; } = 0.35;

    /// <summary>
    /// How much the screen bulges toward the viewer. 0 is flat glass.
    /// </summary>
    /// <remarks>
    /// Applied to the text area only. The status bar is chrome rather than screen content,
    /// and bending the one line that has to stay legible would be a poor trade.
    /// <para>
    /// The default was chosen by looking at 0.18, 0.40 and 1.00 over the glyph-check screen
    /// at 80x25 and 100x30. At 0.18 the bow has to be hunted for, and an effect nobody
    /// notices is not worth a shader branch. At 0.40 it is obvious at a glance and costs
    /// nothing in legibility — no glyph is deformed and nothing falls off the glass. At 1.00
    /// it reads as a fisheye and the corners go dark.
    /// </para>
    /// </remarks>
    public double Curvature { get; init; } = 0.40;

    /// <summary>A tube in a lit room is never quite black.</summary>
    public double AmbientGlow { get; init; } = 0.02;
}
