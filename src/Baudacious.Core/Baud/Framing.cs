using System.Globalization;

namespace Baudacious.Core.Baud;

public enum Parity
{
    None,
    Even,
    Odd,
}

/// <summary>
/// Character framing on the wire: how many bits it costs to send one byte.
/// </summary>
/// <remarks>
/// This is not decoration. Baud is a rate of <em>bits</em>, so framing is what converts
/// it into a rate of characters, and two framings at the same baud produce visibly
/// different speeds: 1200 baud 8N1 is 120 characters/second, 1200 baud 8N2 is 109.1.
/// </remarks>
public readonly record struct Framing(int DataBits, Parity Parity, int StopBits)
{
    /// <summary>1 start bit + data bits + parity bit if any + stop bits.</summary>
    public int BitsPerCharacter => 1 + DataBits + (Parity == Parity.None ? 0 : 1) + StopBits;

    /// <summary>8 data bits, no parity, 1 stop bit. Ten bits per character; the modern default.</summary>
    public static Framing N81 => new(8, Parity.None, 1);

    /// <summary>7 data bits, even parity, 1 stop bit. Ten bits per character; classic BBS and mainframe.</summary>
    public static Framing E71 => new(7, Parity.Even, 1);

    /// <summary>8 data bits, no parity, 2 stop bits. Eleven bits per character.</summary>
    public static Framing N82 => new(8, Parity.None, 2);

    /// <summary>8 data bits, even parity, 1 stop bit. Eleven bits per character.</summary>
    public static Framing E81 => new(8, Parity.Even, 1);

    /// <summary>
    /// The framing a given rate conventionally used. 110 baud means an ASR-33, which ran
    /// two stop bits — and that is exactly why: 110 ÷ 11 is 10 characters per second on
    /// the nose. Everything else defaults to 8N1.
    /// </summary>
    public static Framing DefaultFor(int baud) => baud == 110 ? N82 : N81;

    public static bool TryParse(string? text, out Framing framing)
    {
        framing = default;
        if (text is null || text.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(0, 1), CultureInfo.InvariantCulture, out var dataBits)
            || dataBits is < 5 or > 8)
        {
            return false;
        }

        var parity = char.ToUpperInvariant(text[1]) switch
        {
            'N' => Parity.None,
            'E' => Parity.Even,
            'O' => Parity.Odd,
            _ => (Parity?)null,
        };

        if (parity is null)
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(2, 1), CultureInfo.InvariantCulture, out var stopBits)
            || stopBits is < 1 or > 2)
        {
            return false;
        }

        framing = new Framing(dataBits, parity.Value, stopBits);
        return true;
    }

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{DataBits}{Parity switch { Parity.None => 'N', Parity.Even => 'E', _ => 'O' }}{StopBits}");
}
