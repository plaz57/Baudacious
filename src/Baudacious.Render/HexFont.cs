using System.Globalization;
using System.Reflection;

namespace Baudacious.Render;

/// <summary>
/// A bitmap font in GNU Unifont's <c>.hex</c> format: one line per glyph,
/// <c>CODEPOINT:HEXBYTES</c>, row-major, most significant bit leftmost.
/// </summary>
/// <remarks>
/// <para>
/// Thirty-two hex characters is an 8x16 glyph (one byte per row); sixty-four is a
/// double-width 16x16 one, which Baudacious does not render and skips.
/// </para>
/// <para>
/// The bundled face is <b>unscii-16</b> by Viznut, a bitmap font built to preserve the
/// look of classic system fonts. It is in the <b>public domain</b> — note that this is the
/// plain variant, not <c>unscii-16-full</c>, which pulls in GNU Unifont glyphs and is
/// therefore GPL. See <c>assets/README.md</c>.
/// </para>
/// </remarks>
public sealed class HexFont
{
    private const string ResourceName = "Baudacious.Render.unscii-16.hex";

    private readonly Dictionary<int, byte[]> _glyphs;

    private HexFont(Dictionary<int, byte[]> glyphs) => _glyphs = glyphs;

    public int Count => _glyphs.Count;

    /// <summary>Rows of the glyph, one byte each, or null if the font has no such character.</summary>
    public byte[]? Rows(uint codepoint) => _glyphs.GetValueOrDefault((int)codepoint);

    public bool Contains(uint codepoint) => _glyphs.ContainsKey((int)codepoint);

    /// <summary>Loads the font compiled into this assembly, or null if it is absent.</summary>
    public static HexFont? LoadEmbedded()
    {
        using var stream = typeof(HexFont).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return Parse(reader);
    }

    public static HexFont Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var glyphs = new Dictionary<int, byte[]>(4096);

        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var data = line.AsSpan(separator + 1).Trim();

            // Only single-width 8x16 glyphs. A 16x16 entry would need a two-cell layout,
            // which the grid does not model.
            if (data.Length != 32
                || !int.TryParse(
                    line.AsSpan(0, separator),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var codepoint))
            {
                continue;
            }

            var rows = new byte[16];
            var valid = true;
            for (var row = 0; row < 16; row++)
            {
                if (!byte.TryParse(
                        data.Slice(row * 2, 2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out rows[row]))
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
            {
                glyphs[codepoint] = rows;
            }
        }

        return new HexFont(glyphs);
    }
}
