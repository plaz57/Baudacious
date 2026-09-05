using System.Text;

namespace Baudacious.ConPtySpike;

/// <summary>
/// Renders a byte stream so escape sequences can be read by eye. Deliberately dumb —
/// the real parser is Phase 2; this only needs to make a capture legible.
/// </summary>
internal static class Vt
{
    public static string Visible(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(Visible(b));
        }

        return sb.ToString();
    }

    public static string Visible(byte b) => b switch
    {
        0x1B => "<ESC>",
        0x07 => "<BEL>",
        0x08 => "<BS>",
        0x09 => "<TAB>",
        0x0A => "<LF>\n",
        0x0D => "<CR>",
        0x00 => "<NUL>",
        < 0x20 => $"<{b:X2}>",
        0x7F => "<DEL>",
        < 0x7F => ((char)b).ToString(),
        _ => $"<{b:X2}>",
    };

    /// <summary>
    /// Splits a byte stream into VT tokens so a capture can be counted and described.
    /// Recognises CSI, OSC, and simple ESC sequences; everything else is text or a
    /// lone control byte.
    /// </summary>
    public static List<VtToken> Tokenize(ReadOnlySpan<byte> bytes)
    {
        var tokens = new List<VtToken>();
        var i = 0;

        while (i < bytes.Length)
        {
            var start = i;
            var b = bytes[i];

            if (b == 0x1B && i + 1 < bytes.Length)
            {
                var next = bytes[i + 1];
                if (next == '[')
                {
                    // CSI: parameters and intermediates, then a final byte 0x40-0x7E.
                    i += 2;
                    while (i < bytes.Length && bytes[i] is >= 0x20 and <= 0x3F)
                    {
                        i++;
                    }

                    if (i < bytes.Length)
                    {
                        i++;
                    }

                    tokens.Add(new VtToken(VtTokenKind.Csi, start, i - start, Describe(bytes[start..i])));
                    continue;
                }

                if (next is (byte)']' or (byte)'P' or (byte)'X' or (byte)'^' or (byte)'_')
                {
                    // String sequences: run to ST (ESC \) or BEL.
                    i += 2;
                    while (i < bytes.Length)
                    {
                        if (bytes[i] == 0x07)
                        {
                            i++;
                            break;
                        }

                        if (bytes[i] == 0x1B && i + 1 < bytes.Length && bytes[i + 1] == '\\')
                        {
                            i += 2;
                            break;
                        }

                        i++;
                    }

                    tokens.Add(new VtToken(VtTokenKind.String, start, i - start, Describe(bytes[start..i])));
                    continue;
                }

                i += 2;
                tokens.Add(new VtToken(VtTokenKind.Escape, start, i - start, Describe(bytes[start..i])));
                continue;
            }

            if (b < 0x20 || b == 0x7F)
            {
                i++;
                tokens.Add(new VtToken(VtTokenKind.Control, start, 1, Visible(b)));
                continue;
            }

            while (i < bytes.Length && bytes[i] >= 0x20 && bytes[i] != 0x7F && bytes[i] != 0x1B)
            {
                i++;
            }

            tokens.Add(new VtToken(VtTokenKind.Text, start, i - start, Encoding.UTF8.GetString(bytes[start..i])));
        }

        return tokens;
    }

    private static string Describe(ReadOnlySpan<byte> seq)
    {
        var sb = new StringBuilder();
        foreach (var b in seq)
        {
            sb.Append(b == 0x1B ? "ESC" : b == 0x07 ? "BEL" : ((char)b).ToString());
        }

        return sb.ToString();
    }
}

internal enum VtTokenKind
{
    Text,
    Csi,
    String,
    Escape,
    Control,
}

internal readonly record struct VtToken(VtTokenKind Kind, int Offset, int Length, string Text);
