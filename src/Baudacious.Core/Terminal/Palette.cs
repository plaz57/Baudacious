namespace Baudacious.Core.Terminal;

/// <summary>
/// Resolves terminal colour attributes to RGBA. Values are 0xRRGGBBAA.
/// </summary>
public static class Palette
{
    /// <summary>The 16 ANSI colours, in SGR order: black..white, then the bright set.</summary>
    public static readonly uint[] Ansi16 =
    [
        0x000000FF, 0xCD0000FF, 0x00CD00FF, 0xCDCD00FF,
        0x0000EEFF, 0xCD00CDFF, 0x00CDCDFF, 0xE5E5E5FF,
        0x7F7F7FFF, 0xFF0000FF, 0x00FF00FF, 0xFFFF00FF,
        0x5C5CFFFF, 0xFF00FFFF, 0x00FFFFFF, 0xFFFFFFFF,
    ];

    public const uint DefaultForeground = 0xE5E5E5FF;
    public const uint DefaultBackground = 0x000000FF;

    private static readonly uint[] Xterm256 = BuildXterm256();

    /// <summary>Looks up one of the 256 indexed colours.</summary>
    public static uint Indexed(int index) =>
        (uint)index < (uint)Xterm256.Length ? Xterm256[index] : DefaultForeground;

    public static uint Rgb(int r, int g, int b) =>
        ((uint)(r & 0xFF) << 24) | ((uint)(g & 0xFF) << 16) | ((uint)(b & 0xFF) << 8) | 0xFF;

    /// <summary>
    /// Rec. 709 relative luminance, 0..1. The basis of the monochrome phosphor mapping:
    /// a per-colour lookup table cannot answer for 24-bit colour, whereas luminance
    /// degrades sensibly for schemes nobody anticipated.
    /// </summary>
    public static double Luminance(uint rgba)
    {
        var r = ((rgba >> 24) & 0xFF) / 255.0;
        var g = ((rgba >> 16) & 0xFF) / 255.0;
        var b = ((rgba >> 8) & 0xFF) / 255.0;
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static uint[] BuildXterm256()
    {
        var table = new uint[256];

        Array.Copy(Ansi16, table, 16);

        // 16-231: a 6x6x6 colour cube on the levels xterm uses.
        int[] levels = [0, 95, 135, 175, 215, 255];
        var i = 16;
        foreach (var r in levels)
        {
            foreach (var g in levels)
            {
                foreach (var b in levels)
                {
                    table[i++] = Rgb(r, g, b);
                }
            }
        }

        // 232-255: a 24-step grey ramp, neither end reaching pure black or white.
        for (var step = 0; step < 24; step++)
        {
            var value = 8 + (step * 10);
            table[232 + step] = Rgb(value, value, value);
        }

        return table;
    }
}
