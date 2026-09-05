using Baudacious.Core.Terminal;

namespace Baudacious.Render;

public enum Phosphor
{
    /// <summary>No transform: colour attributes render as their actual colours.</summary>
    Colour,

    /// <summary>P1 green.</summary>
    Green,

    /// <summary>P3 amber.</summary>
    Amber,

    /// <summary>P4 white.</summary>
    White,
}

public enum CursorShape
{
    Block,
    Underline,
    Bar,
}

public sealed record DisplaySettings
{
    public Phosphor Phosphor { get; init; } = Phosphor.Green;

    /// <summary>Whole-number magnification of the 8x16 cell. Never fractional — see the renderer.</summary>
    public int FontScale { get; init; } = 2;

    public CursorShape CursorShape { get; init; } = CursorShape.Block;

    public bool CursorBlink { get; init; } = true;

    public TimeSpan CursorBlinkInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan TextBlinkInterval { get; init; } = TimeSpan.FromMilliseconds(750);
}

/// <summary>
/// Maps a cell's colour attributes to the colours actually drawn.
/// </summary>
/// <remarks>
/// Reverse, invisible and the phosphor transform are resolved here rather than in the
/// shader, so the GPU renderer and the console renderer cannot disagree about what a cell
/// looks like. Luminance, not a per-colour lookup table: a table cannot answer for 24-bit
/// colour, whereas luminance degrades sensibly for schemes nobody anticipated.
/// </remarks>
public static class PhosphorMap
{
    public static (uint Foreground, uint Background) Resolve(Cell cell, Phosphor phosphor)
    {
        var foreground = cell.Foreground;
        var background = cell.Background;

        if (cell.Flags.HasFlag(CellFlags.Reverse))
        {
            (foreground, background) = (background, foreground);
        }

        if (cell.Flags.HasFlag(CellFlags.Invisible))
        {
            foreground = background;
        }

        if (phosphor == Phosphor.Colour)
        {
            return (foreground, background);
        }

        var tint = Tint(phosphor);

        var intensity = Palette.Luminance(foreground);
        if (cell.Flags.HasFlag(CellFlags.Bold))
        {
            intensity = Math.Min(1.0, intensity * 1.4);
        }

        if (cell.Flags.HasFlag(CellFlags.Faint))
        {
            intensity *= 0.6;
        }

        // The background keeps a fraction of its luminance so a reversed or coloured
        // background still reads as one, without washing the screen out.
        return (Apply(tint, intensity), Apply(tint, Palette.Luminance(background) * 0.35));
    }

    public static (double R, double G, double B) Tint(Phosphor phosphor) => phosphor switch
    {
        Phosphor.Green => (0.20, 1.00, 0.40),
        Phosphor.Amber => (1.00, 0.69, 0.00),
        Phosphor.White => (0.94, 0.94, 1.00),
        _ => (1.0, 1.0, 1.0),
    };

    private static uint Apply((double R, double G, double B) tint, double intensity)
    {
        intensity = Math.Clamp(intensity, 0.0, 1.0);
        return Palette.Rgb(
            (int)(tint.R * intensity * 255),
            (int)(tint.G * intensity * 255),
            (int)(tint.B * intensity * 255));
    }
}
