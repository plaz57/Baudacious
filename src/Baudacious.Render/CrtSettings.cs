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

    /// <summary>
    /// Aperture-grille stripes: how strongly output pixels are weighted into repeating
    /// red, green and blue columns. 0 is a continuous phosphor coating.
    /// </summary>
    /// <remarks>
    /// The phase is taken from the output pixel, not the character cell, so the triad stays
    /// fixed to the physical screen and does not swim when the grid is resized.
    /// <para>
    /// Masking removes light by construction — two of every three stripes are dimmed — so
    /// the weights are normalised to keep the mean level, which means the lit stripe is
    /// driven correspondingly harder. That is what a real grille did too. Measured over the
    /// glyph-check screen, mean luminance holds to within 1.2% at 0.25 and 4.2% at 0.50;
    /// past that the compensation runs into the top of the range and cannot recover what
    /// clipping takes, reaching 25% down at 1.00.
    /// </para>
    /// <para>
    /// <b>Off by default, and deliberately.</b> An aperture grille is a colour-tube
    /// artifact: a P1 green monochrome tube had a continuous phosphor coating and no grille
    /// at all, so switching it on under the monochrome palettes is a stylistic choice rather
    /// than a more faithful one. It pairs naturally with <see cref="Phosphor.Color"/>, where
    /// 0.5 and up reads convincingly like a Trinitron under a loupe.
    /// </para>
    /// <para>
    /// Wants a magnification of 2 or more. At 1x a character is eight pixels wide and the
    /// triad is three, so the mask period is close enough to the stroke width to interfere
    /// with the glyphs rather than texture them — measurably less crisp, for the same reason
    /// scanlines have their own magnification floor.
    /// </para>
    /// </remarks>
    public double Mask { get; init; }

    /// <summary>
    /// How long light lingers after the pixel that made it has gone. 0 is no persistence.
    /// </summary>
    /// <remarks>
    /// The effect this product was asking for. In an instant terminal decay is only visible
    /// when the screen scrolls; at 300 baud characters arrive one at a time, so the trail
    /// follows the cursor as the text is written. Metering onto the screen a character at a
    /// time is exactly what makes a decaying phosphor worth having.
    /// <para>
    /// Scaled to 0.9 survival a frame at the top of the dial — about a third of a second of
    /// tail — rather than mapped straight through, because 1.0 meaning "never fades" is a
    /// bug however it is spelled.
    /// </para>
    /// <para>
    /// The default gives a tail of about four frames — 65 ms at 60 Hz. Measured by
    /// presenting blank frames through the pass and reading the mean luminance back: the
    /// tail is gone by frame 4 at this setting, by frame 8 at 0.6, and by frame 32 at 1.0.
    /// At 300 baud a character arrives every other frame, so four frames of tail is three or
    /// four characters of trail behind the cursor — which is the whole point of it.
    /// </para>
    /// </remarks>
    public double Persistence { get; init; } = 0.45;

    /// <summary>A tube in a lit room is never quite black.</summary>
    public double AmbientGlow { get; init; } = 0.02;
}
