using System.Runtime.InteropServices;

namespace Baudacious.Core.Terminal;

[Flags]
public enum CellFlags : ushort
{
    None = 0,
    Bold = 1 << 0,
    Faint = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    Blink = 1 << 4,
    Reverse = 1 << 5,
    Invisible = 1 << 6,
    Strikethrough = 1 << 7,
}

/// <summary>
/// One character cell. Sixteen bytes; a 200x60 grid is 12,000 of them, about 188 KB.
/// </summary>
/// <remarks>
/// Colors are resolved to RGBA when the cell is written rather than stored as palette
/// indices, so changing the palette later does not retroactively recolor history. The
/// phosphor transform happens at render time from these resolved values, which is why
/// switching phosphor <em>does</em> affect history — correct, because that is a property
/// of the display rather than of the text.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Cell : IEquatable<Cell>
{
    public uint Rune;
    public uint Foreground;
    public uint Background;
    public CellFlags Flags;
    private readonly ushort _padding;

    public Cell(uint rune, uint foreground, uint background, CellFlags flags)
    {
        Rune = rune;
        Foreground = foreground;
        Background = background;
        Flags = flags;
        _padding = 0;
    }

    public static Cell Blank(uint foreground, uint background) =>
        new(' ', foreground, background, CellFlags.None);

    public readonly bool IsBlank => Rune is ' ' or 0;

    public readonly char Char => Rune <= char.MaxValue ? (char)Rune : '�';

    public readonly bool Equals(Cell other) =>
        Rune == other.Rune
        && Foreground == other.Foreground
        && Background == other.Background
        && Flags == other.Flags;

    public override readonly bool Equals(object? obj) => obj is Cell cell && Equals(cell);

    public override readonly int GetHashCode() => HashCode.Combine(Rune, Foreground, Background, Flags);

    public static bool operator ==(Cell left, Cell right) => left.Equals(right);

    public static bool operator !=(Cell left, Cell right) => !left.Equals(right);
}
