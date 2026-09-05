using System.Globalization;

namespace Baudacious.Core.Baud;

/// <summary>
/// A signalling rate and its character framing — together, the speed at which bytes
/// reach the screen.
/// </summary>
public readonly record struct LineRate
{
    /// <summary>The fastest rate that can be entered. Well past anything a UART ever did.</summary>
    public const int MaxBaud = 10_000_000;

    /// <summary>The rates offered as presets, in the order a menu should show them.</summary>
    public static readonly int[] StandardBauds =
        [110, 300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];

    private LineRate(int baud, Framing framing)
    {
        Baud = baud;
        Framing = framing;
    }

    /// <summary>Bits per second, or 0 when <see cref="IsUnlimited"/>.</summary>
    public int Baud { get; }

    public Framing Framing { get; }

    /// <summary>No metering at all: every byte is released in the frame it arrives.</summary>
    public bool IsUnlimited => Baud == 0;

    /// <summary>Characters per second. Meaningless — and never consulted — when unlimited.</summary>
    public double BytesPerSecond => Baud / (double)Framing.BitsPerCharacter;

    public static LineRate Unlimited { get; } = new(0, Framing.N81);

    /// <summary>Uses the framing conventionally paired with this rate; see <see cref="Framing.DefaultFor"/>.</summary>
    public static LineRate Of(int baud) => Of(baud, Framing.DefaultFor(baud));

    public static LineRate Of(int baud, Framing framing)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(baud, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(baud, MaxBaud);
        return new LineRate(baud, framing);
    }

    public static bool TryParse(string? text, out LineRate rate)
    {
        rate = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Equals("unlimited", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            rate = Unlimited;
            return true;
        }

        // "1200" or "1200 8N1" / "1200,8N1".
        var parts = trimmed.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!int.TryParse(parts[0], CultureInfo.InvariantCulture, out var baud)
            || baud < 1 || baud > MaxBaud)
        {
            return false;
        }

        var framing = Framing.DefaultFor(baud);
        if (parts.Length > 1 && !Framing.TryParse(parts[1], out framing))
        {
            return false;
        }

        rate = new LineRate(baud, framing);
        return true;
    }

    public override string ToString() =>
        IsUnlimited
            ? "unlimited"
            : string.Create(CultureInfo.InvariantCulture, $"{Baud} {Framing}");
}
