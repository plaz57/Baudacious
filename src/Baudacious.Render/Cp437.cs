namespace Baudacious.Render;

/// <summary>
/// Code page 437 — the IBM PC character set, and the repertoire the glyph atlas covers.
/// </summary>
/// <remarks>
/// Index 0 is blank rather than a glyph: a cell holding NUL should show nothing. Every
/// other slot maps to the Unicode code point that means the same thing, so text arriving
/// as UTF-8 can be looked up by <see cref="IndexOf"/>.
/// </remarks>
public static class Cp437
{
    /// <summary>Unicode code point for each of the 256 slots.</summary>
    public static readonly char[] ToUnicode =
    [
        // 0x00-0x0F — the "graphics" half of the control range, as the PC ROM drew them.
        ' ', '☺', '☻', '♥', '♦', '♣', '♠', '•', '◘', '○', '◙', '♂', '♀', '♪', '♫', '☼',
        // 0x10-0x1F
        '►', '◄', '↕', '‼', '¶', '§', '▬', '↨', '↑', '↓', '→', '←', '∟', '↔', '▲', '▼',
        // 0x20-0x7E — plain ASCII.
        ' ', '!', '"', '#', '$', '%', '&', '\'', '(', ')', '*', '+', ',', '-', '.', '/',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ':', ';', '<', '=', '>', '?',
        '@', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O',
        'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '[', '\\', ']', '^', '_',
        '`', 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o',
        'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z', '{', '|', '}', '~', '⌂',
        // 0x80-0x8F
        'Ç', 'ü', 'é', 'â', 'ä', 'à', 'å', 'ç', 'ê', 'ë', 'è', 'ï', 'î', 'ì', 'Ä', 'Å',
        // 0x90-0x9F
        'É', 'æ', 'Æ', 'ô', 'ö', 'ò', 'û', 'ù', 'ÿ', 'Ö', 'Ü', '¢', '£', '¥', '₧', 'ƒ',
        // 0xA0-0xAF
        'á', 'í', 'ó', 'ú', 'ñ', 'Ñ', 'ª', 'º', '¿', '⌐', '¬', '½', '¼', '¡', '«', '»',
        // 0xB0-0xBF — shades and box drawing.
        '░', '▒', '▓', '│', '┤', '╡', '╢', '╖', '╕', '╣', '║', '╗', '╝', '╜', '╛', '┐',
        // 0xC0-0xCF
        '└', '┴', '┬', '├', '─', '┼', '╞', '╟', '╚', '╔', '╩', '╦', '╠', '═', '╬', '╧',
        // 0xD0-0xDF
        '╨', '╤', '╥', '╙', '╘', '╒', '╓', '╫', '╪', '┘', '┌', '█', '▄', '▌', '▐', '▀',
        // 0xE0-0xEF
        'α', 'ß', 'Γ', 'π', 'Σ', 'σ', 'µ', 'τ', 'Φ', 'Θ', 'Ω', 'δ', '∞', 'φ', 'ε', '∩',
        // 0xF0-0xFF
        '≡', '±', '≥', '≤', '⌠', '⌡', '÷', '≈', '°', '∙', '·', '√', 'ⁿ', '²', '■', ' ',
    ];

    /// <summary>The slot shown for anything the atlas cannot draw.</summary>
    public const int Substitute = 0xFE;   // a solid square

    private static readonly Dictionary<char, byte> Reverse = BuildReverse();

    private static Dictionary<char, byte> BuildReverse()
    {
        var map = new Dictionary<char, byte>(256);
        for (var i = 0; i < ToUnicode.Length; i++)
        {
            // First slot wins: 0x20 is the real space, and 0x00 and 0xFF should not
            // shadow it.
            map.TryAdd(ToUnicode[i], (byte)i);
        }

        map[' '] = 0x20;
        return map;
    }

    /// <summary>
    /// Atlas slot for a code point, or <see cref="Substitute"/> when it is outside the
    /// repertoire. specs/terminal-emulation requires a visible substitution rather than a
    /// blank, so an unrenderable character is obvious rather than silently missing.
    /// </summary>
    public static int IndexOf(uint codepoint)
    {
        if (codepoint == 0)
        {
            return 0;
        }

        return codepoint <= char.MaxValue && Reverse.TryGetValue((char)codepoint, out var index)
            ? index
            : Substitute;
    }
}
